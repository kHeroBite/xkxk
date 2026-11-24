// 원격 제어 서버의 요청 경로 처리·토큰 확인·인자 읽기 부분(RemoteControlServer.cs 의 partial)
#if PD_DIAGNOSTICS // 진단 빌드 전용(PdDiagnostics=true). 제품 빌드 대체(항상 꺼짐)는 RemoteProductStubs.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace xkxk.frameworks.ui.shell.Diagnostics.Remote;

/// <summary>RemoteControlServer 의 /api/... 경로 분기·인증·인자 처리(수신·바인드는 RemoteControlServer.cs).</summary>
public sealed partial class RemoteControlServer
{
    private async Task<(int Status, string ContentType, byte[] Body)> HandleAsync(RemoteHttpRequest req, long received)
    {
        var path = req.Path.Trim('/');
        if (!path.StartsWith("api", StringComparison.OrdinalIgnoreCase))
        {
            return Json(404, RemoteProtocol.Error(path, "경로는 /api/... 입니다(GET /api/help)."));
        }

        var cmd = path.Length > 3 ? path[4..].Replace('/', '.') : "help";
        if (cmd.Equals("health", StringComparison.OrdinalIgnoreCase))
        {
            var (ready, reason) = await VisualTreeInspector.ReadyAsync(); // ready=TopLevel·뷰모델·셸 모두 있음, 아니면 reason=첫 결핍(lead 02:5x)
            return Json(200, RemoteProtocol.Ok("health", new JsonObject { ["app"] = "Pd.Poc", ["pid"] = Environment.ProcessId, ["ready"] = ready, ["reason"] = reason, ["restore"] = RemoteCommands.StartupRestore }, 0)); // restore=기동 복원 결과(odev-2 RestoreResult)
        }

        if (!Authorized(req))
        {
            return Json(401, RemoteProtocol.Error(cmd, $"토큰이 필요합니다(X-Pd-Token). 연결 정보: {InfoFilePath}"));
        }

        var args = ReadArgs(req);
        if (cmd.Equals("cmd", StringComparison.OrdinalIgnoreCase))
        {
            cmd = args.ReqStr("cmd");
            args = args["args"] is JsonObject inner ? (JsonObject)inner.DeepClone() : new JsonObject();
        }

        if (Aliases.TryGetValue(cmd, out var alias)) { cmd = alias; }
        if (cmd.Equals("screenshot.download", StringComparison.OrdinalIgnoreCase))
        {
            // screenshot 명령으로 PNG 를 만든 뒤 바이트를 그대로 돌려준다(Mars 동명 경로 호환)
            var shot = await _router.InvokeAsync("screenshot", args, received);
            var file = (JsonNode.Parse(shot)?["result"] as JsonObject)?.Str("path");
            return file is not null && File.Exists(file) ? (200, "image/png", await File.ReadAllBytesAsync(file)) : Json(400, shot);
        }

        var json = await _router.InvokeAsync(cmd, args, received);
        return Json(json.Contains("\"ok\": true", StringComparison.Ordinal) ? 200 : 400, json);
    }

    private static (int, string, byte[]) Json(int status, string json) => (status, "application/json; charset=utf-8", Encoding.UTF8.GetBytes(json));

    private static Task SendAsync(Stream s, int status, string json, CancellationToken ct) =>
        RemoteHttp.WriteAsync(s, status, "application/json; charset=utf-8", Encoding.UTF8.GetBytes(json), ct);

    private bool Authorized(RemoteHttpRequest req)
    {
        var given = req.Headers.GetValueOrDefault("X-Pd-Token")
            ?? (req.Headers.GetValueOrDefault("Authorization") is { } auth && auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? auth[7..].Trim() : null)
            ?? req.Query.GetValueOrDefault("token");
        return given is not null && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(given), Encoding.UTF8.GetBytes(Token));
    }

    /// <summary>쿼리 문자열(token 제외) + JSON 본문(객체)을 합친 인자. 본문 값이 우선한다.</summary>
    private static JsonObject ReadArgs(RemoteHttpRequest req)
    {
        var args = new JsonObject();
        foreach (var (k, v) in req.Query)
        {
            if (k != "token") { args[k] = v; }
        }

        if (!string.IsNullOrWhiteSpace(req.Body))
        {
            foreach (var (k, v) in RemoteProtocol.ParseObject(req.Body).ToList())
            {
                args[k] = v?.DeepClone();
            }
        }

        return args;
    }
}
#endif
