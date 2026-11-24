// 원격 제어 요청·응답 JSON 형식과 인자 읽기 도우미(System.Text.Json.Nodes — 리플렉션 직렬화 미사용)
#if PD_DIAGNOSTICS // 진단 빌드 전용(PdDiagnostics=true)
using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace xkxk.frameworks.ui.shell.Diagnostics.Remote;

/// <summary>명령 실패(인자 오류·대상 없음). 응답 {ok:false,error} 로 바뀐다.</summary>
public sealed class RemoteCommandException(string message) : Exception(message);

/// <summary>
/// 앱 화면이 아직 준비되지 않음(TopLevel·뷰모델·셸 없음) — 일반 오류와 구분해 status=NotReady 로 응답한다(lead 02:5x).
/// Android 재기동 직후 원격 서버가 메인 화면보다 먼저 떠서 panel.list 등이 '표시된 창이 없습니다' 로 실패했고, 시험이 그 실패를 결과 null 로 읽어 거짓 FAIL 이 났다.
/// </summary>
public sealed class RemoteNotReadyException(string reason, string message) : Exception(message)
{
    public string Reason { get; } = reason;
}

/// <summary>
/// 응답 형식: 성공 {"ok":true,"cmd":"docs.list","result":...,"ms":n} / 실패 {"ok":false,"cmd":"...","error":"..."}.
/// 요청 형식: POST /api/cmd {"cmd":"docs.open","args":{"kind":"grid"}} 또는 경로형 /api/docs/open + 쿼리·본문 인자.
/// </summary>
public static class RemoteProtocol
{
    /// <summary>
    /// 응답 직렬화 옵션. MaxDepth 기본 64 는 tree.get 의 중첩(노드 1단 = 객체+children 배열 2단)을 못 견딘다
    /// (Android Fold5 펼침 실측 'CurrentDepth (64) … maximum allowed depth of 64'). 트리 덤프 깊이 상한(VisualTreeInspector.MaxDumpDepth)의 2배보다 크게 둔다.
    /// </summary>
    public static readonly JsonSerializerOptions Indented = new() { WriteIndented = true, MaxDepth = 1024 };

    /// <summary>ms=처리 시간, elapsed_ms=요청 수신→UI 반영, ui_frame_ms=처리 종료→렌더 반영(측정 안 했으면 생략·null).</summary>
    public static string Ok(string cmd, JsonNode? result, double ms, double? elapsedMs = null, double? frameMs = null)
    {
        var o = new JsonObject { ["ok"] = true, ["cmd"] = cmd, ["result"] = result, ["ms"] = Math.Round(ms, 1) };
        if (elapsedMs is { } e) { o["elapsed_ms"] = Math.Round(e, 1); o["ui_frame_ms"] = frameMs is { } f ? Math.Round(f, 1) : null; }
        return o.ToJsonString(Indented);
    }

    public static string Error(string cmd, string error) =>
        new JsonObject { ["ok"] = false, ["cmd"] = cmd, ["error"] = error }.ToJsonString(Indented);

    /// <summary>준비 안 됨: {ok:false, cmd, status:"NotReady", reason(no-toplevel|no-viewmodel|no-shell), error, retryAfterMs} — 시험은 재시도 대상으로 본다.</summary>
    public static string NotReady(string cmd, string reason, string error) =>
        new JsonObject { ["ok"] = false, ["cmd"] = cmd, ["status"] = "NotReady", ["reason"] = reason, ["error"] = error, ["retryAfterMs"] = 500 }
            .ToJsonString(Indented);

    /// <summary>본문 JSON 을 객체로 파싱. 빈 본문은 빈 객체, 객체가 아니면 예외.</summary>
    public static JsonObject ParseObject(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) { return new JsonObject(); }
        return JsonNode.Parse(json) as JsonObject ?? throw new RemoteCommandException("본문은 JSON 객체여야 합니다.");
    }

    public static string? Str(this JsonObject a, string key)
    {
        if (!a.TryGetPropertyValue(key, out var n) || n is null) { return null; }
        return n is JsonValue v && v.TryGetValue<string>(out var s) ? s : n.ToJsonString();
    }

    public static string ReqStr(this JsonObject a, string key) =>
        a.Str(key) is { Length: > 0 } s ? s : throw new RemoteCommandException($"인자 '{key}' 가 필요합니다.");

    public static double? Num(this JsonObject a, string key)
    {
        if (!a.TryGetPropertyValue(key, out var n) || n is not JsonValue v) { return null; }
        if (v.TryGetValue<double>(out var d)) { return d; }
        return v.TryGetValue<string>(out var s) && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out d) ? d : null;
    }

    public static int? Int(this JsonObject a, string key) => a.Num(key) is { } d ? (int)d : null;

    public static bool Bool(this JsonObject a, string key, bool fallback = false)
    {
        if (!a.TryGetPropertyValue(key, out var n) || n is not JsonValue v) { return fallback; }
        if (v.TryGetValue<bool>(out var b)) { return b; }
        var s = v.ToString();
        return s is "1" || s.Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>열거형 인자(대소문자·하이픈 무시: "tile-v" → TileV 비교).</summary>
    public static T? EnumArg<T>(this JsonObject a, string key) where T : struct, Enum =>
        a.Str(key) is { } s && Enum.TryParse<T>(s.Replace("-", ""), true, out var e) ? e : null;
}
#endif
