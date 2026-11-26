// 원격 명령(다국어 Q17 — 현재 언어 조회·실시간 전환·번역 누락 키·화면의 미번역 문자열 후보) — Localization.Loc 을 그대로 호출
#if PD_DIAGNOSTICS // 진단 빌드 전용(PdDiagnostics=true)
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.VisualTree;
using xkxk.frameworks.ui.shell.Localization;
using xkxk.frameworks.ui.shell.Framework;

namespace xkxk.frameworks.ui.shell.Diagnostics.Remote;

/// <summary>언어·미번역 점검 원격 명령 군(옛 RemoteCommands.Lang). RemoteCommandRouter 가 Register 를 부른다.</summary>
internal static class RemoteLangCommands
{
    // 공용 멤버 전달자(W2) — 본문은 옛 이름 그대로 쓴다.
    private static Avalonia.Visual? Scope(JsonObject a) => RemoteUiTargets.Scope(a);

    private static JsonObject LangJson() => new()
    {
        ["language"] = XkxkUi.Current.Localization.Language,
        ["culture"] = CultureInfo.CurrentUICulture.Name,
        // D-CULTURE 판정용: 숫자·날짜 형식 문화권과 실제 형식 표본(ICU 자료가 없으면 이름만 맞고 형식은 기본값이 된다)
        ["numberCulture"] = CultureInfo.CurrentCulture.Name,
        ["sampleN0"] = 10000.ToString("N0", CultureInfo.CurrentCulture),
        ["samplePercent"] = 1.0.ToString("P0", CultureInfo.CurrentCulture),
        ["sampleDate"] = new System.DateTime(2026, 1, 2).ToString("d", CultureInfo.CurrentCulture),
        ["languages"] = new JsonArray(XkxkUi.Current.Localization.Languages
            .Select(l => (JsonNode?)new JsonObject { ["code"] = l.Code, ["name"] = l.Name }).ToArray()),
        ["keys"] = UiAppContext.Instance.CoreLoc.AllKeys.Count,
        // 진단 스위치 --loc static: {loc:Tr} 를 바인딩 대신 고정 문자열로 해석 중인지·몇 개 해석했는지(스위치가 실제로 먹었는지 확인)
        ["staticMode"] = TrExtension.StaticMode,
        ["staticCount"] = TrExtension.StaticCount,
    };

    internal static void Register(RemoteCommandRouter r)
    {
        r.Register("lang.get", "현재 언어·문화권·사전 언어 목록·키 수", _ => LangJson());
        r.Register("lang.set", "언어 실시간 전환 code=ko|en (별칭 lang= — 계획 문서 표기) (사전에 없는 코드는 ko) → 실제 적용값", a =>
        {
            XkxkUi.Current.Localization.Language = a.Str("code") ?? a.Str("lang") ?? throw new RemoteCommandException("인자 'code'(또는 'lang')가 필요합니다.");
            return LangJson();
        });
        // i18n.missing·i18n.scan 은 리드 확정 이름(00:1x), lang.missingKeys·lang.scan 은 같은 처리기의 별칭
        foreach (var name in new[] { "i18n.missing", "lang.missingKeys" })
        {
            r.Register(name, "기본 언어(ko)에는 있는데 lang 에 없는 키 [lang=en(생략 시 현재 언어)]", a =>
            {
                var keys = UiAppContext.Instance.CoreLoc.MissingKeys(a.Str("lang") ?? XkxkUi.Current.Localization.Language);
                return new JsonObject { ["count"] = keys.Count, ["keys"] = new JsonArray(keys.Select(k => (JsonNode?)JsonValue.Create(k)).ToArray()) };
            });
        }

        foreach (var name in new[] { "i18n.scan", "lang.scan" })
        {
            r.Register(name, "보이는 화면 글자 중 한글이 들어간 것(현재 언어가 ko 가 아닐 때의 미번역 후보) [window=n|doc] — 텍스트·AutomationId·타입·위치", ScanHangul);
        }
    }

    private static JsonNode? ScanHangul(JsonObject a)
    {
        var root = Scope(a) ?? VisualTreeInspector.Main();
        var hits = new JsonArray();
        foreach (var c in root.GetSelfAndVisualDescendants().OfType<Control>().Where(c => c.IsEffectivelyVisible))
        {
            if (VisualTreeInspector.TextOf(c) is { Length: > 0 } t && t.Any(IsHangul))
            {
                var d = VisualTreeInspector.Describe(c);
                d.Remove("enabled");
                hits.Add(d);
            }
        }

        return new JsonObject { ["language"] = XkxkUi.Current.Localization.Language, ["count"] = hits.Count, ["hits"] = hits };
    }

    private static bool IsHangul(char ch) => ch is >= '가' and <= '힣' or >= 'ᄀ' and <= 'ᇿ' or >= '㄰' and <= '㆏';
}
#endif
