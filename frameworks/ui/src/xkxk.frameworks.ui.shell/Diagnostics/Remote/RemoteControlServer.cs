// 실행 중 앱을 외부(AI·스크립트)에서 조작하는 로컬 HTTP/JSON 제어 서버 — 기본 꺼짐, --remote-control[=port] 또는 PD_REMOTE_CONTROL 로 켬, 루프백(127.0.0.1·[::1])에서만 수신
#if PD_DIAGNOSTICS // 진단 빌드 전용(PdDiagnostics=true). 제품 빌드 대체(항상 꺼짐)는 RemoteProductStubs.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace xkxk.frameworks.ui.shell.Diagnostics.Remote;

/// <summary>
/// Mars RestApiServer 의 /api/... 경로 방식에 기동 토큰·루프백 전용 수신을 더했다.
/// 수신: TcpListener 를 IPAddress.Loopback·IPv6Loopback 에만 바인드(다른 NIC 주소로는 연결 자체가 안 됨) + 요청 원격 주소가 루프백이 아니면 403.
///   HttpListener(http.sys)는 관리자 권한 없이 루프백만 바인드할 수 없어(localhost 접두사 = 모든 주소 수신 후 Host 헤더로만 거름 — otest-1 실측) 쓰지 않는다.
/// 토큰: 기동마다 무작위 32자, 요청마다 X-Pd-Token / Authorization: Bearer / ?token= 로 확인. /api/health 만 토큰 없이 응답(루프백에서만).
/// 연결 정보 파일: %LOCALAPPDATA%\PdPoc\remote-control.json {url,port,token,pid,started}. Android 는 앱 전용 files/PdPoc/.
/// 경로: POST /api/cmd {"cmd","args"} 또는 /api/{영역}/{동작}(쿼리·JSON 본문이 인자). GET /api/screenshot/download 는 PNG 바이트.
/// 개발·시험 전용. 운영 배포 빌드에서는 켜지 않는다(README 보안 주의).
/// </summary>
public sealed partial class RemoteControlServer : IDisposable
{
    public const int DefaultPort = 5960; // Mars(5959) 와 동시 실행해도 겹치지 않게 +1
    public const string EnvName = "PD_REMOTE_CONTROL";
    private const int PortRetries = 10;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(120); // 요청 수신 대기 상한(처리 시간 상한 아님)
    private static readonly TimeSpan ResponseWriteTimeout = TimeSpan.FromSeconds(30); // 처리가 끝난 뒤 응답 쓰기 상한

    // Mars 경로 호환 별칭(같은 시험 습관으로 호출 가능)
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["windows"] = "windows.list",
        ["logs.latest"] = "log.tail",
        ["window.open"] = "docs.open",
        ["window.close"] = "docs.close",
        ["window.activate"] = "docs.activate",
        ["window.sdi-toggle"] = "docs.sdi",
    };

    private readonly RemoteCommandRouter _router;
    private readonly List<TcpListener> _listeners = new();
    private readonly CancellationTokenSource _stop = new();

    private RemoteControlServer(RemoteCommandRouter router) => _router = router;

    /// <summary>실행 중인 서버(없으면 null).</summary>
    public static RemoteControlServer? Current { get; private set; }

    public int Port { get; private set; }

    public string Token { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

    public string Url => $"http://localhost:{Port}/";

    /// <summary>실제 바인드된 주소(예: 127.0.0.1:5960, [::1]:5960).</summary>
    public IReadOnlyList<string> Bindings => _listeners.ConvertAll(l => l.LocalEndpoint.ToString() ?? "");

    /// <summary>연결 정보 파일 경로(%LOCALAPPDATA%\PdPoc\remote-control.json).</summary>
    public static string InfoFilePath => RemoteInfoPath.Current; // --data-dir/PD_DATA_DIR 가 있으면 그 폴더

    /// <summary>
    /// 인자 --remote-control / --remote-control=포트 / --remote-control 포트, 또는 환경변수 PD_REMOTE_CONTROL(1|true|포트)이 있으면 켠다.
    /// 둘 다 없으면 null(기본 꺼짐). 브라우저는 소켓 수신이 없어 미지원(null) — window.pdAutomation 브리지를 쓴다.
    /// </summary>
    public static RemoteControlServer? StartFromArgs(IReadOnlyList<string> args)
    {
        RemoteInfoPath.Configure(args);
        if (Current is not null) { return Current; }
        var port = RequestedPort(args);
        if (port is null) { return null; }
        if (OperatingSystem.IsBrowser())
        {
            RemoteCommandRouter.Trace("브라우저는 HTTP 제어 서버 미지원 — window.pdAutomation.invoke 사용");
            return null;
        }

        var server = new RemoteControlServer(RemoteCommandRouter.Default);
        server.Start(port.Value);
        Current = server;
        return server;
    }

    /// <summary>요청 포트. 꺼져 있으면 null, 값 없이 켜면 기본 포트.</summary>
    public static int? RequestedPort(IReadOnlyList<string> args)
    {
        for (var i = 0; i < args.Count; i++)
        {
            var a = args[i];
            if (a.Equals("--remote-control", StringComparison.OrdinalIgnoreCase))
            {
                return i + 1 < args.Count && int.TryParse(args[i + 1], out var p) ? p : DefaultPort;
            }

            if (a.StartsWith("--remote-control=", StringComparison.OrdinalIgnoreCase))
            {
                return int.TryParse(a["--remote-control=".Length..], out var p) ? p : DefaultPort;
            }
        }

        var env = Environment.GetEnvironmentVariable(EnvName);
        if (string.IsNullOrWhiteSpace(env) || env is "0" || env.Equals("false", StringComparison.OrdinalIgnoreCase)) { return null; }
        return int.TryParse(env, out var ep) && ep > 1 ? ep : DefaultPort;
    }

    /// <summary>127.0.0.1 바인드가 되는 첫 포트를 쓰고, 같은 포트로 [::1] 도 연다(IPv6 미지원 환경은 생략).</summary>
    private void Start(int port)
    {
        Exception? last = null;
        for (var i = 0; i < PortRetries && _listeners.Count == 0; i++)
        {
            var v4 = new TcpListener(IPAddress.Loopback, port + i);
            try
            {
                v4.Start();
                _listeners.Add(v4);
                Port = port + i;
            }
            catch (SocketException ex)
            {
                last = ex;
            }
        }

        if (_listeners.Count == 0) { throw new InvalidOperationException($"원격 제어 서버 시작 실패(포트 {port}~{port + PortRetries - 1})", last); }
        if (Socket.OSSupportsIPv6)
        {
            var v6 = new TcpListener(IPAddress.IPv6Loopback, Port);
            try { v6.Start(); _listeners.Add(v6); }
            catch (SocketException ex) { RemoteCommandRouter.Trace("[::1] 바인드 생략: " + ex.SocketErrorCode); }
        }

        WriteInfoFile();
        RemoteCommandRouter.Trace($"원격 제어 서버 시작 {Url} bind={string.Join(",", Bindings)} pid={Environment.ProcessId} token={Token} info={InfoFilePath}");
        foreach (var l in _listeners) { _ = Task.Run(() => AcceptLoopAsync(l)); }
    }

    private void WriteInfoFile()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(InfoFilePath)!);
            var info = new JsonObject
            {
                ["url"] = Url, ["port"] = Port, ["token"] = Token, ["pid"] = Environment.ProcessId,
                ["bind"] = new JsonArray(Bindings.Select(b => (JsonNode?)JsonValue.Create(b)).ToArray()),
                ["started"] = DateTime.Now.ToString("o"),
            };
            File.WriteAllText(InfoFilePath, info.ToJsonString(RemoteProtocol.Indented), new UTF8Encoding(false));
        }
        catch (Exception ex)
        {
            RemoteCommandRouter.Trace("연결 정보 파일 쓰기 실패(토큰은 이 로그 줄 참조): " + ex.Message);
        }
    }

    private async Task AcceptLoopAsync(TcpListener listener)
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(_stop.Token);
            }
            catch (Exception) when (_stop.IsCancellationRequested)
            {
                return; // 종료
            }
            catch (Exception ex)
            {
                RemoteCommandRouter.Trace("수신 오류: " + ex.Message);
                continue;
            }

            _ = HandleClientAsync(client);
        }
    }

    private async Task HandleClientAsync(TcpClient client)
    {
        var received = System.Diagnostics.Stopwatch.GetTimestamp(); // elapsed_ms 기준점(요청 수신)
        using var _ = client;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
        cts.CancelAfter(RequestTimeout);
        var stream = client.GetStream();
        var remote = client.Client.RemoteEndPoint as IPEndPoint;
        try
        {
            // 방어 2단: 바인드가 루프백뿐이어도 원격 주소를 다시 확인(IPv4 매핑 IPv6 포함)
            if (remote is null || !IPAddress.IsLoopback(remote.Address.IsIPv4MappedToIPv6 ? remote.Address.MapToIPv4() : remote.Address))
            {
                RemoteCommandRouter.Trace($"루프백 아닌 요청 거부: {remote}");
                await SendAsync(stream, 403, RemoteProtocol.Error("", "루프백(127.0.0.1·::1)에서만 받습니다."), cts.Token);
                return;
            }

            var req = await RemoteHttp.ReadAsync(stream, remote, cts.Token);
            var (status, contentType, body) = await HandleAsync(req, received);
            // 응답 쓰기는 요청 수신용 120초 토큰과 분리한다(cmd.run perf-ui 등 120초를 넘는 처리 뒤에도 응답을 보낸다 — 이전엔 만료 토큰으로 쓰기가 취소돼 curl rc=52 빈 응답)
            using var writeCts = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
            writeCts.CancelAfter(ResponseWriteTimeout);
            await RemoteHttp.WriteAsync(stream, status, contentType, body, writeCts.Token);
        }
        catch (Exception ex)
        {
            try { await SendAsync(stream, ex is InvalidDataException ? 400 : 500, RemoteProtocol.Error("", ex.Message), cts.Token); }
            catch (Exception) { /* 연결 끊김 */ }
        }
    }

    // 경로 분기(HandleAsync)·응답 헬퍼(Json·SendAsync)·토큰 확인(Authorized)·인자 읽기(ReadArgs)는 RemoteControlServer.Routes.cs

    /// <summary>서버 중지 + 연결 정보 파일 삭제(앱 종료 시 호스트가 호출).</summary>
    public void Dispose()
    {
        _stop.Cancel();
        foreach (var l in _listeners)
        {
            try { l.Stop(); } catch (Exception) { /* 이미 종료 */ }
        }

        _listeners.Clear();
        try { if (File.Exists(InfoFilePath)) { File.Delete(InfoFilePath); } } catch (Exception) { /* 무시 */ }
        if (ReferenceEquals(Current, this)) { Current = null; }
    }
}
#endif
