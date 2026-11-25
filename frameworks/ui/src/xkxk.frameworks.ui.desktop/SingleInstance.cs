// 데스크톱 단일 인스턴스(UI-17 프로그램 중복모드 끔) — 채널별 이름 있는 Mutex(현재 사용자·세션 한정)와 named pipe 로 기존 인스턴스를 앞으로 가져온다
using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json;

namespace xkxk.frameworks.ui.desktop;

/// <summary>
/// 설계 §3.4·§3.5(plan/05_아키텍처/05_제품구성_시스템메뉴.md).
/// - 이름 있는 Mutex 는 NamedWaitHandleOptions(CurrentUserOnly·CurrentSessionOnly)로 만든다 — .NET 10 참조 문서(System.Threading.xml)상
///   두 값의 기본이 true 이고, '현재 사용자'·'현재 세션' 으로 접근을 제한한다(기본 ACL 의 다른 사용자 열기 문제 대응).
/// - 이름에 채널(dev|staging|qa|prod)을 넣어 채널끼리는 공존한다.
/// - 두 번째 실행은 파이프(PipeOptions.CurrentUserOnly)로 기존 인스턴스에 알리고 끝난다.
/// - GPU [지금 다시 시작]: 새 프로세스는 --wait-pid 로 옛 프로세스 종료(레이아웃 자동 저장·Mutex 해제)를 최대 10초 기다린 뒤
///   Mutex 를 잡고 설정을 읽는다(odev-3 제안 순서 — 옛 프로세스의 종료 저장과 새 프로세스의 읽기가 겹치지 않는다).
/// </summary>
public sealed class SingleInstance : IDisposable
{
    /// <summary>settings.json 키(UiSettings 의 같은 이름 속성, bool, 기본 false = 하나만 실행).</summary>
    public const string SettingsKey = "AllowDuplicateInstance";

    /// <summary>호스트 전용 인자: 재시작 때 옛 프로세스 pid. 셸로 넘기지 않는다.</summary>
    public const string WaitPidArg = "--wait-pid";

    private static readonly TimeSpan WaitPidLimit = TimeSpan.FromSeconds(10);
    private const string ActivateMessage = "activate";

    private readonly Mutex _mutex;
    private readonly string _pipeName;
    private readonly CancellationTokenSource _cts = new();

    private SingleInstance(Mutex mutex, string pipeName)
    {
        _mutex = mutex;
        _pipeName = pipeName;
    }

    /// <summary>settings.json 텍스트에서 중복 실행 허용 값만 읽는다. 없거나 손상이면 null(= 기본 false).</summary>
    public static bool? AllowDuplicateFromSettings(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty(SettingsKey, out var p)
                && p.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? p.GetBoolean()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>--wait-pid 값의 프로세스가 끝날 때까지 최대 10초 기다린다(없거나 이미 끝났으면 즉시 반환).</summary>
    public static void WaitForPid(string? pidText, Action<string> log)
    {
        if (!int.TryParse(pidText, out var pid) || pid == Environment.ProcessId)
        {
            return;
        }

        try
        {
            using var old = Process.GetProcessById(pid);
            var sw = Stopwatch.StartNew();
            var exited = old.WaitForExit(WaitPidLimit);
            log($"single-instance wait-pid {pid} exited={exited} {sw.ElapsedMilliseconds}ms");
        }
        catch (ArgumentException)
        {
            log($"single-instance wait-pid {pid} already exited");
        }
        catch (Exception ex)
        {
            log($"single-instance wait-pid {pid} error {ex.GetType().Name}");
        }
    }

    /// <summary>
    /// 첫 인스턴스면 Mutex 를 쥔 객체를, 이미 실행 중이면 null 을 돌려준다(이때 기존 인스턴스에 '앞으로' 를 알린다).
    /// Mutex 를 만들 수 없는 환경이면 실행을 막지 않는다(로그만 남기고 빈 소유로 진행).
    /// </summary>
    public static SingleInstance? TryAcquire(string channel, Action<string> log)
    {
        var name = $"xkxk.frameworks.ui.{channel}.Instance";
        var pipe = $"xkxk.frameworks.ui.{channel}.{Process.GetCurrentProcess().SessionId}.Activate";
        Mutex mutex;
        bool createdNew;
        try
        {
            var options = new NamedWaitHandleOptions { CurrentUserOnly = true, CurrentSessionOnly = true };
            mutex = new Mutex(true, name, options, out createdNew);
        }
        catch (Exception ex)
        {
            log($"single-instance mutex error {ex.GetType().Name}: {ex.Message} — continue without duplicate check");
            return new SingleInstance(new Mutex(), pipe);
        }

        if (createdNew)
        {
            log($"single-instance acquired channel={channel}");
            return new SingleInstance(mutex, pipe);
        }

        mutex.Dispose();
        var signalled = Signal(pipe);
        log($"single-instance already running channel={channel} signalled={signalled}");
        return null;
    }

    /// <summary>두 번째 실행의 알림을 받으면 onActivate 를 부른다(호출 스레드는 백그라운드 — UI 작업은 호출자가 디스패치).</summary>
    public void Listen(Action onActivate)
    {
        _ = Task.Run(async () =>
        {
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    await using var server = new NamedPipeServerStream(_pipeName, PipeDirection.In, 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await server.WaitForConnectionAsync(_cts.Token);
                    using var reader = new StreamReader(server);
                    if (await reader.ReadLineAsync(_cts.Token) == ActivateMessage)
                    {
                        onActivate();
                    }
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception)
                {
                    await Task.Delay(500); // 파이프 오류는 잠시 뒤 다시 듣는다
                }
            }
        });
    }

    /// <summary>
    /// 재시작: 같은 실행 파일·원래 인자 + --wait-pid &lt;이 pid&gt; 로 새 프로세스를 띄운다(이전 재시작의 --wait-pid 는 뺀다).
    /// 새 프로세스는 이 프로세스가 끝나기(레이아웃 자동 저장 → Mutex 해제)를 기다린 뒤 Mutex 를 잡는다.
    /// </summary>
    public static bool StartSuccessor(IReadOnlyList<string> originalArgs, Action<string> log)
    {
        if (Environment.ProcessPath is not { } exe)
        {
            return false;
        }

        var psi = new ProcessStartInfo(exe) { UseShellExecute = false };
        for (var i = 0; i < originalArgs.Count; i++)
        {
            if (string.Equals(originalArgs[i], WaitPidArg, StringComparison.OrdinalIgnoreCase))
            {
                i++;
                continue;
            }

            psi.ArgumentList.Add(originalArgs[i]);
        }

        psi.ArgumentList.Add(WaitPidArg);
        psi.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        try
        {
            using var _ = Process.Start(psi);
            log("restart successor started");
            return true;
        }
        catch (Exception ex)
        {
            log($"restart successor failed {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        try
        {
            _mutex.ReleaseMutex();
        }
        catch (Exception)
        {
            // 소유하지 않은 경우(빈 소유) 무시
        }

        _mutex.Dispose();
    }

    private static bool Signal(string pipeName)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            client.Connect(1000);
            using var writer = new StreamWriter(client) { AutoFlush = true };
            writer.WriteLine(ActivateMessage);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
