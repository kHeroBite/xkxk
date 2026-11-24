// 테마 모델(UI-07) — 테마 Id·이름·기반 Light/Dark·의미 단위 색 토큰 사전·내장 여부와 토큰 목록·JSON 직렬화 컨텍스트
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.Json.Serialization;
using Avalonia.Media;
using xkxk.frameworks.ui.shell.Localization;

namespace xkxk.frameworks.ui.shell.Theming;

/// <summary>테마의 기반 변형. Fluent·Dock 기본 컨트롤 색(ThemeVariant)을 결정한다.</summary>
public enum ThemeBase
{
    Light,
    Dark,
}

/// <summary>
/// 색 테마 1개. Colors 는 토큰 키(PdBrush*) → "#RRGGBB" 또는 "#AARRGGBB".
/// 누락 토큰은 같은 기반의 내장 테마 값으로 채운다(ThemeCatalog.Normalize).
/// </summary>
public sealed class ThemeDefinition : INotifyPropertyChanged
{
    /// <summary>표시명 변경 알림(언어 전환 시 ThemeCatalog 가 NotifyDisplayName 으로 올린다). 저장 JSON 과 무관.</summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    public void NotifyDisplayName()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LocalizedName)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayName)));
    }

    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    public ThemeBase Base { get; set; }

    public Dictionary<string, string> Colors { get; set; } = new(StringComparer.Ordinal);

    /// <summary>
    /// 요소별 디자인 덮어쓰기(REQ-101). 키 "&lt;요소&gt;.&lt;속성&gt;" → 정규화된 값 문자열. 바꾼 키만 둔다(희소 저장).
    /// 옛 저장본·옛 JSON 에는 없으며 그때는 빈 사전 = 화면 무변경. 검증은 ThemeCatalog.Normalize 가 ThemeElements.TryNormalize 로 한다.
    /// </summary>
    public Dictionary<string, string> Design { get; set; } = new(StringComparer.Ordinal);

    /// <summary>복제 원본 내장 템플릿 Id(되돌리기 기준). 옛 저장본은 null → 같은 기반 내장(pd-light·pd-dark)을 기준으로 삼는다.</summary>
    public string? BaseTemplateId { get; set; }

    /// <summary>내장 템플릿이면 true(편집·삭제 불가, 복제만 가능). 저장 JSON 에는 넣지 않는다.</summary>
    [JsonIgnore]
    public bool IsBuiltin { get; set; }

    /// <summary>내장 테마 이름의 다국어 키(theme.builtin.&lt;Id&gt;). 표시 때 현재 언어로 번역한다. 사용자 테마는 null(이름 = 사용자 데이터).</summary>
    [JsonIgnore]
    public string? NameKey { get; set; }

    /// <summary>현재 언어의 이름(접미 없음). 내장은 NameKey 번역, 사용자 테마는 Name 그대로. Name·Id(저장값)는 바꾸지 않는다(D-THEME-NAME-I18N).</summary>
    [JsonIgnore]
    public string LocalizedName => NameKey is null ? Name : Loc.Tr(NameKey);

    [JsonIgnore]
    public string DisplayName => IsBuiltin ? Loc.Tr("theme.displayBuiltin", LocalizedName) : Name;

    /// <summary>토큰 색. 없거나 형식 오류면 fallback.</summary>
    public Color GetColor(string key, Color fallback) =>
        Colors.TryGetValue(key, out var hex) && Color.TryParse(hex, out var c) ? c : fallback;

    public ThemeDefinition Clone(string id, string name, bool builtin = false) => new()
    {
        Id = id,
        Name = name,
        Base = Base,
        Colors = new Dictionary<string, string>(Colors, StringComparer.Ordinal),
        Design = new Dictionary<string, string>(Design, StringComparer.Ordinal),
        BaseTemplateId = IsBuiltin ? Id : BaseTemplateId, // 내장을 복제하면 그 내장이, 사용자 테마를 복제하면 원본의 기준 템플릿이 되돌리기 기준
        IsBuiltin = builtin,
    };

    public override string ToString() => DisplayName;
}

/// <summary>의미 단위 색 토큰 정의. 화면은 {DynamicResource 키} 로만 참조한다(고정 색 금지).</summary>
public static class ThemeTokens
{
    /// <summary>리소스 키 목록. 순서 = 편집기 표시 순서 = BuiltinThemes 의 색 배열 순서. 표시명은 사전 키 theme.token.&lt;리소스 키&gt;(Q17).</summary>
    private static readonly string[] KeyList =
    {
        "PdBrushBackground", "PdBrushSurface", "PdBrushSurfaceAlt", "PdBrushText", "PdBrushTextMuted",
        "PdBrushAccent", "PdBrushAccentText", "PdBrushSelection", "PdBrushSelectionText", "PdBrushBorder",
        "PdBrushTabActive", "PdBrushTabInactive", "PdBrushTabActiveText",
        "PdBrushMdiTitleBar", "PdBrushMdiTitleBarInactive", "PdBrushMdiTitleText",
        "PdBrushStatusBar", "PdBrushStatusBarText",
        "PdBrushGridStripe", "PdBrushGridHeader", "PdBrushGridLine",
        "PdBrushGanttBar", "PdBrushGanttBarCritical", "PdBrushGanttProgress", "PdBrushToday",
        "PdBrushWarning", "PdBrushError", "PdBrushSuccess", "PdBrushAttention",
        // 창 외곽선 2개: 기본(Windows 스타일)에서는 쓰지 않고 Mars 클래식 스타일 1px 단색에서만 쓴다 — 저장 테마 호환을 위해 정의·불러오기는 유지.
        "PdBrushWindowBorderActive", "PdBrushWindowBorderInactive", "PdBrushWindowShadow",
        "PdBrushMdiTitleButtonHover", "PdBrushMdiTitleButtonChecked", "PdBrushMdiTitleBarPinned",
        "PdBrushChromeGlyph", "PdBrushChromePinOn",
        "PdBrushCacheLow", "PdBrushCacheHigh", "PdBrushCacheFull", "PdBrushChromeLockOn",
        "PdBrushWindowHairlineActive", "PdBrushWindowHairlineInactive",
        "PdBrushCaptionHover", "PdBrushCaptionPressed", "PdBrushCaptionCloseHover",
        "PdBrushWindowHighlight", "PdBrushMdiBackground", "PdBrushMenuBar", "PdBrushToolBar",
        "PdBrushGridHeaderLine", "PdBrushSelectionLine", "PdBrushButton", "PdBrushButtonBorder", "PdBrushInputBorder",
        // U6 문서 창 버튼 기호 상태 색(강조 띠 위 / 표면색 제목줄 위) — CaptionLayout 이 PdChromeGlyph* 별칭으로 고른다
        "PdBrushChromeGlyphHoverOnAccent", "PdBrushChromeGlyphPressedOnAccent", "PdBrushChromeGlyphCheckedOnAccent",
        "PdBrushChromeCloseGlyphHoverOnAccent", "PdBrushChromeCloseGlyphPressedOnAccent",
        "PdBrushChromeGlyphHoverOnSurface", "PdBrushChromeGlyphPressedOnSurface", "PdBrushChromeGlyphCheckedOnSurface",
        "PdBrushChromeCloseGlyphHoverOnSurface", "PdBrushChromeCloseGlyphPressedOnSurface",
        // U7·U19 창 제목 탭·패널 선택 탭 상태 색 — CaptionLayout 이 PdChromeTab*·PdChromePanelTabSelected* 별칭으로 건다
        "PdBrushWindowTabActive", "PdBrushWindowTabActiveText", "PdBrushWindowTabInactive", "PdBrushWindowTabInactiveText",
        "PdBrushWindowTabMinimizedText", "PdBrushWindowTabHover", "PdBrushPanelTabSelected", "PdBrushPanelTabSelectedText",
        // 메인 창 타이틀바(활성 바탕·비활성 바탕·글자) — 창 스타일과 무관하게 아래 제목 줄과 구분한다(공통원칙 §4.2)
        "PdBrushMainTitleBar", "PdBrushMainTitleBarInactive", "PdBrushMainTitleText",
        // 표면색 제목줄(강조 끔·macOS 스타일) 위 새로고침 적중률 색(0%·낮음·높음·100%) — CaptionLayout 이 PdChromeCache* 별칭으로 고른다
        "PdBrushCacheZeroOnSurface", "PdBrushCacheLowOnSurface", "PdBrushCacheHighOnSurface", "PdBrushCacheFullOnSurface",
        "PdBrushChromeHoverInactive",
    };

    /// <summary>
    /// 옛 사용자 테마에 없는 새 토큰을 그 테마 색에 맞게 채운다. 메인 타이틀바는 그 테마의 문서 창 제목줄 색에서, 표면용 적중률 색은 그 테마 면 색의 밝기에 맞는 내장 값에서 가져온다(대비는 보장하지 않는다).
    /// </summary>
    public static void FillMissing(ThemeDefinition item, ThemeDefinition theme, ThemeDefinition lightBuiltin, ThemeDefinition darkBuiltin)
    {
        foreach (var (key, source) in new[] { ("PdBrushMainTitleBar", "PdBrushMdiTitleBar"), ("PdBrushMainTitleBarInactive", "PdBrushMdiTitleBarInactive"), ("PdBrushMainTitleText", "PdBrushMdiTitleText") })
        {
            if (!item.Colors.ContainsKey(key))
            {
                theme.Colors[key] = theme.Colors[source];
            }
        }

        if (!item.Colors.ContainsKey("PdBrushChromeHoverInactive"))
        {
            theme.Colors["PdBrushChromeHoverInactive"] = theme.Colors["PdBrushMdiTitleButtonHover"]; // 옛 테마: 비활성 호버도 현행 호버 값(무회귀)
        }

        var surface = theme.GetColor("PdBrushSurface", Colors.White);
        var pick = (0.2126 * surface.R + 0.7152 * surface.G + 0.0722 * surface.B) / 255 < 0.5 ? darkBuiltin : lightBuiltin;
        foreach (var key in new[] { "PdBrushCacheZeroOnSurface", "PdBrushCacheLowOnSurface", "PdBrushCacheHighOnSurface", "PdBrushCacheFullOnSurface" })
        {
            if (!item.Colors.ContainsKey(key))
            {
                theme.Colors[key] = pick.Colors[key];
            }
        }
    }

    /// <summary>(리소스 키, 편집기 표시명). 표시명은 읽을 때마다 현재 언어로 만든다 — 언어 전환 후 다시 읽으면 새 언어.</summary>
    public static IReadOnlyList<(string Key, string Label)> All => KeyList.Select(k => (k, LabelOf(k))).ToArray();

    /// <summary>키만 필요한 경로(적용·내장 테마 생성)는 번역 비용 없이 이 목록을 쓴다.</summary>
    public static IEnumerable<string> Keys => KeyList;

    public static string LabelOf(string key) => Loc.Tr("theme.token." + key);
}

/// <summary>테마 JSON 직렬화(트리밍·WASM 안전 소스 생성).</summary>
[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(ThemeDefinition))]
[JsonSerializable(typeof(List<ThemeDefinition>))]
internal sealed partial class ThemeJsonContext : JsonSerializerContext
{
}
