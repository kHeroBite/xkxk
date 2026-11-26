// 테마 요소별 사용자 지정 카탈로그(REQ-101) — 요소·속성 표(기본값·범위·매핑 색 토큰), 값 검증, 설정 계층 합성(사용자 ?? 회사 ?? 템플릿 ?? 제품 기본값), 대비 계산
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia.Media;
using xkxk.frameworks.ui.shell.Localization;
using xkxk.frameworks.ui.shell.Framework;

namespace xkxk.frameworks.ui.shell.Theming;

/// <summary>
/// 속성 값 종류. 적용기(ThemeDesignApplier)가 이 종류로 리소스 값 형식을 고른다.
/// Family = 글꼴 이름, SizeOffset = 기준 크기(시스템 또는 문서) + 정수 오프셋(D-352), Weight = Normal·Medium·SemiBold·Bold,
/// Corner = 모서리 반경 1값, Border = 테두리 두께 1값, Padding = 안쪽 여백 1값·좌우,상하 2값·좌,위,우,아래 4값.
/// </summary>
public enum DesignPropKind { Family, SizeOffset, Weight, Corner, Border, Padding }

/// <summary>값이 정해진 층. 편집 화면의 '덮어씀' 표시와 되돌리기 판단에 쓴다. Style(디자인 테마, REQ-104)은 Company 와 Template 사이 층이지만 서수 보존을 위해 끝에 둔다.</summary>
public enum DesignLayer { Default, Template, Company, User, Style }

/// <summary>속성 1종의 정의(값 형식·범위·제품 기본값). 범위는 이 표의 데이터이며 코드 분기에 박지 않는다(REQ-99).</summary>
public sealed record DesignPropSpec(string Key, DesignPropKind Kind, double Min, double Max, string Default);

/// <summary>요소 1종의 정의. Colors = (역할, 기존 PdBrush* 토큰) — 색은 새 토큰을 만들지 않는다. DocScope = 문서 글자 크기 기준 오프셋.</summary>
public sealed record DesignElementSpec(
    string Key, string[] Props, (string Role, string Token)[] Colors, string ForegroundToken, string BackgroundToken, bool DocScope);

/// <summary>회사 계층 접점(REQ-100, 설계만). 값 사전(키 &lt;요소&gt;.&lt;속성&gt;)과 잠금 목록(키·&lt;요소&gt;.*·*). PoC 는 빈 공급자.</summary>
public interface IThemeDesignPolicy
{
    IReadOnlyDictionary<string, string> Values { get; }

    IReadOnlyCollection<string> Locked { get; }
}

/// <summary>요소별 디자인 카탈로그. 요소·속성 추가는 아래 표에 행만 더한다(D-350). 셀렉터·글자 크기 기준·크롬 제외 규칙은 ThemeElements.Targets.cs.</summary>
public static partial class ThemeElements
{
    /// <summary>설정 키 접두. 설정 키 = theme.design.&lt;요소&gt;.&lt;속성&gt;, 표시명 키 = theme.design.&lt;요소&gt;·theme.design.prop.&lt;속성&gt;.</summary>
    public const string SettingPrefix = "theme.design.";

    /// <summary>요소 button 에서 빼는 단추 클래스(정본, 쉼표 구분) — 창 크롬(pdChrome)·창 탭 제목/닫기(tabTitle·tabClose, windowTab 소속)·SDI 아이콘. 셀렉터와 원격 측정이 같이 쓴다.</summary>
    public const string ChromeButtonClassList = "pdChrome,tabTitle,tabClose,sdiIcon";

    /// <summary>요소 button 에서 빼는 제목줄 루트 클래스(정본). 메인 제목줄·SDI 제목줄 루트가 이 클래스를 달고, 그 자손 Button(UI 형 스위치 등)은 IsChromeButton(원격 측정 대상)에서 제외한다(스타일 셀렉터 쪽 사정은 ThemeElements.Targets.cs ButtonSelector).</summary>
    public const string ChromeTitleBarClass = "pdTitleBar";

    /// <summary>ChromeButtonClassList 를 나눈 목록.</summary>
    public static IReadOnlyList<string> ChromeButtonClasses => ChromeButtonClassList.Split(',');

    /// <summary>요소 button 제외 판정(정본): 제외 클래스를 가졌거나 논리 조상에 제목줄 루트 클래스가 있으면 true. 원격 측정 대상 고르기도 이것을 쓴다.</summary>
    public static bool IsChromeButton(Avalonia.Controls.Button button) =>
        ChromeButtonClasses.Any(button.Classes.Contains)
        || Avalonia.LogicalTree.LogicalExtensions.GetLogicalAncestors(button).OfType<Avalonia.StyledElement>().Any(a => a.Classes.Contains(ChromeTitleBarClass));

    private const string TextFont = "fontFamily,fontSize,fontWeight";

    private static readonly DesignPropSpec[] PropTable =
    {
        // 기본값 "" = 상속(스타일 미적용 = 지금 화면 그대로). fontSize 0 = 오프셋 없음. 범위는 초기 제시값(17 문서 §3)
        new("fontFamily", DesignPropKind.Family, 0, 128, ""),
        new("fontSize", DesignPropKind.SizeOffset, -4, 8, "0"),
        new("fontWeight", DesignPropKind.Weight, 0, 0, ""),
        new("cornerRadius", DesignPropKind.Corner, 0, 16, ""),
        new("borderThickness", DesignPropKind.Border, 0, 4, ""),
        new("padding", DesignPropKind.Padding, 0, 24, ""),
    };

    private static readonly DesignElementSpec[] ElementTable =
    {
        new("app", Split(TextFont), new[] { ("background", "PdBrushBackground"), ("foreground", "PdBrushText"), ("accent", "PdBrushAccent") }, "PdBrushText", "PdBrushBackground", false),
        new("menuBar", Split(TextFont + ",padding"), new[] { ("background", "PdBrushMainTitleBar"), ("foreground", "PdBrushMainTitleText") }, "PdBrushMainTitleText", "PdBrushMainTitleBar", false), // D-16: 메뉴는 제목줄 안(MainTitleBar.axaml MainMenu)
        new("docTitle", Split(TextFont), new[] { ("background", "PdBrushMdiTitleBar"), ("foreground", "PdBrushMdiTitleText") }, "PdBrushMdiTitleText", "PdBrushMdiTitleBar", false),
        // windowTab 은 글꼴·굵기만: 탭 글자 크기(MainView 탭 TextBlock 로컬 PdFontCaption)·제목 단추 여백(로컬 6,0)·모서리(미반영)는 스타일이 이기지 못해 편집 목록에서 뺐다. 옛 저장본 키는 TryNormalize 가 거부해 무시
        new("windowTab", Split("fontFamily,fontWeight"), new[] { ("background", "PdBrushWindowTabActive"), ("foreground", "PdBrushWindowTabActiveText") }, "PdBrushWindowTabActiveText", "PdBrushWindowTabActive", false),
        new("button", Split(TextFont + ",cornerRadius,borderThickness,padding"), new[] { ("background", "PdBrushButton"), ("border", "PdBrushButtonBorder"), ("foreground", "PdBrushText") }, "PdBrushText", "PdBrushButton", false),
        new("input", Split(TextFont + ",cornerRadius,borderThickness,padding"), new[] { ("background", "PdBrushSurface"), ("border", "PdBrushInputBorder"), ("foreground", "PdBrushText") }, "PdBrushText", "PdBrushSurface", false),
        new("gridHeader", Split(TextFont + ",padding"), new[] { ("background", "PdBrushGridHeader"), ("border", "PdBrushGridHeaderLine"), ("foreground", "PdBrushText") }, "PdBrushText", "PdBrushGridHeader", true),
        new("gridCell", Split(TextFont + ",padding"), new[] { ("background", "PdBrushSurface"), ("border", "PdBrushGridLine"), ("stripe", "PdBrushGridStripe"), ("foreground", "PdBrushText") }, "PdBrushText", "PdBrushSurface", true),
        // panel 의 cornerRadius 는 ToolChromeControl 템플릿이 쓰지 않아 미반영 — 편집 목록에서 뺐다
        new("panel", Split(TextFont), new[] { ("background", "PdBrushSurfaceAlt"), ("border", "PdBrushBorder") }, "PdBrushText", "PdBrushSurfaceAlt", false),
        new("statusBar", Split(TextFont + ",padding"), new[] { ("background", "PdBrushStatusBar"), ("foreground", "PdBrushStatusBarText") }, "PdBrushStatusBarText", "PdBrushStatusBar", false),
    };

    /// <summary>제품 기본값을 현재 렌더 값(Fluent 리소스)에서 읽어 올 속성. 키는 &lt;요소&gt;.&lt;속성&gt;. 못 찾으면 표 기본값.</summary>
    internal static readonly IReadOnlyDictionary<string, string> BaselineResources = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["button.cornerRadius"] = "ControlCornerRadius", ["button.borderThickness"] = "ButtonBorderThemeThickness", ["button.padding"] = "ButtonPadding",
        ["input.cornerRadius"] = "ControlCornerRadius", ["input.borderThickness"] = "TextControlBorderThemeThickness", ["input.padding"] = "TextControlThemePadding",
    };

    /// <summary>설정 키와 기본값(대비 기준·잠금·대비 테마 판정). 회사 값(Policy.Values)이 있으면 그 값이 우선한다.</summary>
    private static readonly IReadOnlyDictionary<string, string> SettingDefaults = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["theme.design.contrastMin"] = "4.5", ["theme.design.contrastMinLarge"] = "3", ["theme.design.contrastMinHigh"] = "7",
        ["theme.design.largeTextSize"] = "24", ["theme.design.contrastTemplate"] = "pd-contrast", ["theme.design.locked"] = "",
    };

    private static readonly Dictionary<string, string> Baseline = new(StringComparer.Ordinal);

    private sealed class EmptyPolicy : IThemeDesignPolicy
    {
        public IReadOnlyDictionary<string, string> Values { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

        public IReadOnlyCollection<string> Locked { get; } = Array.Empty<string>();
    }

    /// <summary>회사 계층 공급자(REQ-100 연결 전에는 빈 값 — TODO-172).</summary>
    public static IThemeDesignPolicy Policy { get; set; } = new EmptyPolicy();

    /// <summary>요소 키 목록(표시 순서).</summary>
    public static IReadOnlyList<string> All { get; } = ElementTable.Select(e => e.Key).ToArray();

    public static DesignElementSpec? Element(string el) => ElementTable.FirstOrDefault(e => e.Key == el);

    public static DesignPropSpec? Prop(string prop) => PropTable.FirstOrDefault(p => p.Key == prop);

    /// <summary>요소가 가진 디자인 속성(색 제외). 모르는 요소면 빈 목록.</summary>
    public static IReadOnlyList<string> Props(string el) => Element(el)?.Props ?? Array.Empty<string>();

    /// <summary>요소에 매핑된 색 토큰 키(PdBrush*) 목록. 색 편집은 기존 테마 색 사전(ThemeCatalog.SetColor)으로 한다.</summary>
    public static IReadOnlyList<string> ColorTokens(string el) => ColorRoles(el).Select(c => c.Token).ToArray();

    /// <summary>요소의 색 역할(background·foreground·border·accent·stripe)과 매핑 토큰. 표시명 키 theme.design.color.&lt;역할&gt;.</summary>
    public static IReadOnlyList<(string Role, string Token)> ColorRoles(string el) => Element(el)?.Colors ?? Array.Empty<(string, string)>();

    public static bool Has(string el, string prop) => Props(el).Contains(prop);

    /// <summary>Design 사전 키(&lt;요소&gt;.&lt;속성&gt;).</summary>
    public static string DesignKey(string el, string prop) => el + "." + prop;

    public static string SettingKey(string el, string prop) => SettingPrefix + DesignKey(el, prop);

    public static string LabelOf(string el) => Loc.Tr(SettingPrefix + el);

    public static string PropLabelOf(string prop) => Loc.Tr(SettingPrefix + "prop." + prop);

    /// <summary>제품 기본값. 현재 렌더 값을 읽었으면 그 값, 아니면 표 기본값("" = 상속).</summary>
    public static string Default(string el, string prop) =>
        Baseline.TryGetValue(DesignKey(el, prop), out var v) ? v : Prop(prop)?.Default ?? "";

    /// <summary>적용기가 처음 적용할 때 Fluent 리소스에서 읽은 현재 렌더 값을 기록한다(정규화된 문자열만).</summary>
    internal static void SetBaseline(string designKey, string value) => Baseline[designKey] = value;

    /// <summary>설정 값(회사 값 ?? 기본값). 모르는 키는 "".</summary>
    public static string Setting(string key) =>
        Policy.Values.TryGetValue(key, out var v) ? v : SettingDefaults.TryGetValue(key, out var d) ? d : "";

    public static double SettingNumber(string key) =>
        double.TryParse(Setting(key), NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : 0;

    /// <summary>값 검증·정규화. 모르는 요소·속성, 형식·범위 밖(음수·NaN·빈 글꼴)은 false.</summary>
    public static bool TryNormalize(string el, string prop, string? value, out string normalized)
    {
        normalized = "";
        if (!Has(el, prop) || Prop(prop) is not { } spec || value is null)
        {
            return false;
        }

        var text = value.Trim();
        switch (spec.Kind)
        {
            case DesignPropKind.Family:
                normalized = text;
                return text.Length > 0 && text.Length <= spec.Max;
            case DesignPropKind.Weight:
                var weight = new[] { "Normal", "Medium", "SemiBold", "Bold" }.FirstOrDefault(w => string.Equals(w, text, StringComparison.OrdinalIgnoreCase));
                normalized = weight ?? "";
                return weight is not null;
            case DesignPropKind.Padding:
                var parts = text.Split(',', StringSplitOptions.TrimEntries);
                if (parts.Length is not (1 or 2 or 4) || !parts.All(p => TryNumber(p, spec, out _)))
                {
                    return false;
                }

                normalized = string.Join(",", parts.Select(p => { TryNumber(p, spec, out var n); return Format(n); }));
                return true;
            default:
                if (!TryNumber(text, spec, out var number))
                {
                    return false;
                }

                normalized = Format(number);
                return true;
        }
    }

    /// <summary>실효값(§4 합성). 잠금이면 사용자 값을 건너뛴다(저장값은 보존).</summary>
    public static string Effective(ThemeDefinition t, string el, string prop)
    {
        Resolve(t, el, prop, out var value);
        return value;
    }

    /// <summary>실효값과 그 값을 정한 층. 모든 층이 비면 Default + 제품 기본값.</summary>
    public static DesignLayer Resolve(ThemeDefinition t, string el, string prop, out string value)
    {
        if (!IsLocked(el, prop) && !t.IsBuiltin && Valid(t.Design, el, prop, out value))
        {
            return DesignLayer.User;
        }

        if (Valid(Policy.Values, el, prop, out value))
        {
            return DesignLayer.Company;
        }

        if (DesignThemeResolver.ElementValue(t, el, prop, out var styled) && TryNormalize(el, prop, styled, out value))
        {
            return DesignLayer.Style; // 디자인 테마 층(REQ-104 §4.4): 회사 ?? 디자인 테마 ?? 템플릿
        }

        if (TemplateOf(t) is { } template && Valid(template.Design, el, prop, out value))
        {
            return DesignLayer.Template;
        }

        value = Default(el, prop);
        return DesignLayer.Default;
    }

    /// <summary>회사 잠금 여부. 잠금 목록 항목은 키·&lt;요소&gt;.*·* 형식.</summary>
    public static bool IsLocked(string el, string prop)
    {
        var key = DesignKey(el, prop);
        var setting = Setting(SettingPrefix + "locked").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return Policy.Locked.Concat(setting).Any(p => p == "*" || p == key || p == el + ".*");
    }

    /// <summary>되돌리기·템플릿 층의 기준 내장 템플릿. 내장이면 자신, 사용자 테마면 BaseTemplateId, 없으면 같은 기반 내장.</summary>
    public static ThemeDefinition? TemplateOf(ThemeDefinition t)
    {
        if (t.IsBuiltin)
        {
            return t;
        }

        var catalog = XkxkUi.Current.Themes;
        return catalog.Find(t.BaseTemplateId) is { IsBuiltin: true } b ? b
            : catalog.Find(t.Base == ThemeBase.Dark ? BuiltinThemes.DarkId : BuiltinThemes.LightId);
    }

    /// <summary>요소 글자색 대 바탕색 대비와 기준. 기준 템플릿이 대비 테마면 높은 기준, 큰 글자면 큰 글자 기준. 경고만 하고 차단하지 않는다(D-354).</summary>
    public static (double Ratio, double Min, bool Warn) Contrast(ThemeDefinition t, string el, double effectiveFontSize)
    {
        if (Element(el) is not { } spec)
        {
            return (0, 0, false);
        }

        var ratio = ContrastRatio(t.GetColor(spec.ForegroundToken, Colors.Black), t.GetColor(spec.BackgroundToken, Colors.White));
        var high = string.Equals(TemplateOf(t)?.Id, Setting(SettingPrefix + "contrastTemplate"), StringComparison.OrdinalIgnoreCase);
        var min = high ? SettingNumber(SettingPrefix + "contrastMinHigh")
            : effectiveFontSize >= SettingNumber(SettingPrefix + "largeTextSize") ? SettingNumber(SettingPrefix + "contrastMinLarge")
            : SettingNumber(SettingPrefix + "contrastMin");
        return (ratio, min, ratio < min);
    }

    /// <summary>WCAG 2.x 대비 비율((밝은 L + 0.05) / (어두운 L + 0.05)). 알파는 무시한다.</summary>
    public static double ContrastRatio(Color a, Color b)
    {
        var la = Luminance(a);
        var lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static double Luminance(Color c)
    {
        static double Channel(byte v)
        {
            var s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Channel(c.R)) + (0.7152 * Channel(c.G)) + (0.0722 * Channel(c.B));
    }

    private static bool Valid(IReadOnlyDictionary<string, string>? source, string el, string prop, out string value)
    {
        value = "";
        return source is not null && source.TryGetValue(DesignKey(el, prop), out var raw) && TryNormalize(el, prop, raw, out value);
    }

    private static bool TryNumber(string text, DesignPropSpec spec, out double number) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out number)
        && double.IsFinite(number) && number == Math.Round(number) && number >= spec.Min && number <= spec.Max;

    private static string Format(double n) => n.ToString("0", CultureInfo.InvariantCulture);

    private static string[] Split(string csv) => csv.Split(',');
}
