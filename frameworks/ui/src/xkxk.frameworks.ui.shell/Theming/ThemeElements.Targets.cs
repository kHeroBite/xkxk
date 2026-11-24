// 테마 요소별 디자인 대상 표(REQ-101) — 요소·속성이 걸리는 셀렉터 문자열, 문서 창 안 되돌림 대상, 글자 크기 기준 토큰(제외 규칙 정본은 ThemeElements.cs)
using System;
using System.Collections.Generic;
using System.Linq;

namespace xkxk.frameworks.ui.shell.Theming;

/// <summary>
/// 요소 1종이 Setter 를 거는 대상 1개. Selector = 셀렉터 문자열(ThemeDesignStyles 가 공개 Selectors API 로 조립 — 문법은 그 파일 머리 주석),
/// Props = 이 대상이 받는 속성(쉼표 구분). 지정된 (요소, 속성)만 Setter 가 생기고 미지정이면 아무것도 걸지 않는다(D-4).
/// </summary>
public sealed record DesignTarget(string Selector, string Props)
{
    public bool Takes(string prop) => Props.Split(',', StringSplitOptions.TrimEntries).Contains(prop);
}

public static partial class ThemeElements
{
    /// <summary>Application.Styles 안 요소 디자인 전용 Styles 표식 리소스 키(PdApp.axaml 의 빈 슬롯과 적용기가 만드는 Styles 가 함께 가진다).</summary>
    public const string StylesSlotKey = "PdDesignStylesSlot";

    /// <summary>
    /// 요소 button 셀렉터 = Button + 제외 클래스 + 제목줄 루트 클래스 자손 제외(정본 상수 ChromeButtonClassList·ChromeTitleBarClass). 형식 반사 없음.
    /// :not 안 결합자는 공식 문서에 지원 명시가 없으나 격리 진단판 실측(2026-11-16)에서 제목줄 UI 형 단추가 빠졌다(테두리 3·글자 +4 지정 시 0·14 유지,
    /// 이 제외를 빼면 3·18 로 바뀜).
    /// </summary>
    private static readonly string ButtonSelector = "Button"
        + string.Concat(ChromeButtonClassList.Split(',').Select(c => ":not(." + c + ")"))
        + ":not(." + ChromeTitleBarClass + " Button)";

    /// <summary>문서 래퍼 표식 클래스. UiSettingsApplier.ApplyDocumentScope 가 래퍼에 단다(:is(인터페이스)는 실측에서 맞지 않았다 — 2026-11-16).</summary>
    public const string DocHostClass = "pdDocScope";

    /// <summary>재평가 표식 Style 의 클래스(어떤 컨트롤도 달지 않는다 — ThemeDesignStyles.Build 주석).</summary>
    public const string RestyleSentinelClass = "pdDesignRestyle";

    /// <summary>문서 창 안 되돌림 대상의 공통 머리(문서 래퍼). 래퍼는 PdFontFamily·PdFontBody 를 로컬로 덮는다(ApplyDocumentScope).</summary>
    private const string DocHost = "." + DocHostClass;

    private const string Font3 = "fontFamily,fontSize,fontWeight";

    /// <summary>
    /// 요소별 대상. 순서: 셸 요소 → (적용기가 넣는) 문서 안 되돌림 → 문서 범위 요소(표 머리·칸). 같은 속성은 뒤 스타일이 이긴다.
    /// docTitle 의 MDI 쪽은 제목줄 테두리 부품(@Header — 제목 글자·도구 제목 포함, 단추는 경로 그림이라 글자 속성 무관)에 상속 글자 속성을 건다.
    /// </summary>
    private static readonly Dictionary<string, DesignTarget[]> TargetTable = new(StringComparer.Ordinal)
    {
        ["app"] = new DesignTarget[]
        {
            new(":is(TopLevel), :is(TemplatedControl)", "fontFamily"),
            new(":is(TopLevel), MenuItem, TabItem, ListBoxItem, ComboBoxItem, Button, TextBox, ComboBox", "fontSize"),
            new(":is(TopLevel)", "fontWeight"), // 굵기는 상속만 — TemplatedControl 에 직접 주면 컨트롤 고유 굵기를 덮는다
        },
        ["menuBar"] = new DesignTarget[] { new("Menu > MenuItem", Font3 + ",padding") },
        ["docTitle"] = new DesignTarget[] { new("controls|MdiDocumentWindow /template/ Border#@Header", Font3), new(":is(UserControl).pdSdiTitleBar", Font3) }, // SDI 제목줄 루트 클래스(SdiTitleBar.axaml)
        ["windowTab"] = new DesignTarget[] { new("ListBox#TaskbarTabs ListBoxItem Button.tabTitle", "fontFamily,fontWeight") },
        ["button"] = new DesignTarget[] { new(ButtonSelector, Font3 + ",cornerRadius,borderThickness,padding") },
        ["input"] = new DesignTarget[]
        {
            new("TextBox, ComboBox, NumericUpDown", Font3 + ",cornerRadius,borderThickness"),
            new("TextBox, ComboBox", "padding"), // NumericUpDown 은 안쪽 TextBox 가 받으므로 겹쳐 주지 않는다
        },
        // 편집 화면 미리보기 표본(ThemeDesignPanel)의 표 머리는 클래스 pdPreviewGridHeader 를 단 ContentControl — 로컬 글자·여백 값 없이 이 대상을 받는다
        ["gridHeader"] = new DesignTarget[] { new("DataGridColumnHeader", Font3 + ",padding"), new("ContentControl.pdPreviewGridHeader", Font3 + ",padding") },
        ["gridCell"] = new DesignTarget[] { new("DataGridCell", Font3 + ",padding") },
        ["panel"] = new DesignTarget[] { new("controls|ToolChromeControl", Font3) },
        ["statusBar"] = new DesignTarget[] { new(":is(UserControl).pdStatusBar", Font3 + ",padding") }, // 상태바 루트 클래스(ShellStatusBar.axaml)
    };

    /// <summary>
    /// 문서 창 안 되돌림(01 §5.2 — 문서 글꼴·크기 우선). 셸 요소의 글자 속성을 지정했을 때만 그 속성에 한해 만든다.
    /// 값은 적용기가 문서 글꼴·문서 기준 크기·기본 굵기로 넣는다.
    /// </summary>
    private static readonly Dictionary<string, DesignTarget[]> DocRevertTable = new(StringComparer.Ordinal)
    {
        ["app"] = new DesignTarget[]
        {
            new(DocHost, "fontFamily,fontWeight"),
            new(DocHost + " :is(TemplatedControl)", "fontFamily"),
            new(string.Join(", ", new[] { "MenuItem", "TabItem", "ListBoxItem", "ComboBoxItem", "Button", "TextBox", "ComboBox", "NumericUpDown" }.Select(t => DocHost + " " + t)), "fontSize"),
        },
        ["button"] = new DesignTarget[] { new(DocHost + " Button", Font3) },
        ["input"] = new DesignTarget[] { new(string.Join(", ", new[] { "TextBox", "ComboBox", "NumericUpDown" }.Select(t => DocHost + " " + t)), Font3) },
    };

    /// <summary>
    /// 요소 기본 글자 크기의 기준 토큰(UiSettings.FontTokens 키). 실효 크기 = clamp(기준(시스템 또는 문서) + 토큰 오프셋 + 요소 오프셋, 최소, 최대)(D-3).
    /// 토큰은 그 요소 글자의 실제 무지정 렌더 크기와 같아야 한다(오프셋 0 = 지금 화면). 상태바 글자는 로컬 크기 없이 본문 크기를 상속한다(ShellStatusBar.axaml).
    /// </summary>
    private static readonly Dictionary<string, string> SizeTokenTable = new(StringComparer.Ordinal)
    {
        ["app"] = "PdFontBody", ["menuBar"] = "PdFontBody", ["docTitle"] = "PdFontBody", ["windowTab"] = "PdFontCaption", ["button"] = "PdFontBody",
        ["input"] = "PdFontBody", ["gridHeader"] = "PdFontBody", ["gridCell"] = "PdFontBody", ["panel"] = "PdFontBody", ["statusBar"] = "PdFontBody",
    };

    /// <summary>요소가 Setter 를 거는 대상(모르는 요소면 빈 목록).</summary>
    public static IReadOnlyList<DesignTarget> Targets(string el) => TargetTable.TryGetValue(el, out var t) ? t : Array.Empty<DesignTarget>();

    /// <summary>요소 글자 속성을 지정했을 때 문서 창 안을 되돌리는 대상(셸 요소만, 없으면 빈 목록).</summary>
    public static IReadOnlyList<DesignTarget> DocReverts(string el) => DocRevertTable.TryGetValue(el, out var t) ? t : Array.Empty<DesignTarget>();

    /// <summary>요소 기본 글자 크기 기준 토큰 키.</summary>
    public static string SizeTokenOf(string el) => SizeTokenTable.TryGetValue(el, out var k) ? k : UiSettings.FontTokens.First(t => t.Offset == 0).Key;

    /// <summary>요소 자기 기본 글자 크기(오프셋 0일 때 지금 화면 크기) = (문서 범위면 문서 글자 크기, 아니면 시스템 글자 크기) + 기준 토큰 오프셋.</summary>
    public static double BaseFontSize(string el, UiSettings settings)
    {
        var size = Element(el)?.DocScope == true ? settings.BaseFontSize : settings.ShellFontSize;
        var token = SizeTokenOf(el);
        return size + UiSettings.FontTokens.FirstOrDefault(t => t.Key == token).Offset;
    }
}
