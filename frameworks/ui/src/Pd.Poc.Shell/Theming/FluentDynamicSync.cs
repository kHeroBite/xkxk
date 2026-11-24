// Fluent 팔레트 동적 브러시 동기화(M4 a1) — 팔레트 교체(앱 전체 리소스 알림) 없이 같은 변형 테마 전환의 Fluent 색을 이전 방식과 같게 맞춘다
using System;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Pd.Poc.Shell.Diagnostics;

namespace Pd.Poc.Shell.Theming;

/// <summary>ThemeApplier.Apply 가 부르는 Fluent 동적 브러시 동기화와 그 표(변형별 키 → 팔레트 색 키).</summary>
internal static class FluentDynamicSync
{
    // 출처: Avalonia.Themes.Fluent 12.1.4(MIT, github.com/AvaloniaUI/Avalonia) 를 ilspycmd 로 디컴파일한 FluentTheme 테마 사전(2026-10-29, odev-7c).
    // SolidColorBrush.Color 를 팔레트 색에 DynamicResource 로 묶은 브러시 전부다(밝게 41·어둡게 44). 이 어셈블리의 동적 색 참조 85건과 개수가 같다.
    // 나머지 테마 브러시 125개는 첫 생성 때 StaticResource 로 색이 고정되므로 팔레트를 교체해도 바뀌지 않는다 — 그중 팔레트 색을 쓰는 것은 아래 StaticPalette 표로 따로 맞춘다(D-13).
    // ButtonBackground·CheckBoxCheckBackgroundFillChecked 같은 컨트롤 키 다수는 이 브러시를 StaticResource 로 가리키는 별칭이라 같은 인스턴스다.
    // 값 약어: A=SystemAccentColor, L1=…Light1, D1=…Dark1, BH=SystemBaseHighColor, AM=SystemAltMediumColor.
    private const string LightDynamic =
        "SystemControlBackgroundAccentBrush=A SystemControlDisabledAccentBrush=A SystemControlFocusVisualPrimaryBrush=BH " +
        "SystemControlFocusVisualSecondaryBrush=AM SystemControlRevealFocusVisualBrush=A SystemControlForegroundAccentBrush=A " +
        "SystemControlHighlightAccentBrush=A SystemControlHighlightAltAccentBrush=A SystemControlHighlightAltListAccentHighBrush=A " +
        "SystemControlHighlightAltListAccentLowBrush=A SystemControlHighlightAltListAccentMediumBrush=A SystemControlHighlightListAccentHighBrush=A " +
        "SystemControlHighlightListAccentLowBrush=A SystemControlHighlightListAccentMediumBrush=A SystemControlHyperlinkTextBrush=A " +
        "SystemControlHighlightAccentRevealBackgroundBrush=A SystemControlHighlightAccent3RevealBackgroundBrush=A " +
        "SystemControlHighlightAccent2RevealBackgroundBrush=A AccentButtonBackgroundPointerOver=L1 AccentButtonBackgroundPressed=D1 " +
        "ToggleButtonBackgroundCheckedPointerOver=L1 HyperlinkButtonForeground=A HyperlinkButtonForegroundPointerOver=A HyperlinkButtonForegroundPressed=D1 " +
        "CheckBoxCheckBackgroundStrokeCheckedPointerOver=L1 CheckBoxCheckBackgroundStrokeIndeterminatePointerOver=L1 " +
        "CheckBoxCheckBackgroundStrokeIndeterminatePressed=D1 CheckBoxCheckBackgroundFillCheckedPointerOver=L1 CheckBoxCheckBackgroundFillCheckedPressed=D1 " +
        "CheckBoxCheckBackgroundFillIndeterminatePointerOver=L1 CheckBoxCheckBackgroundFillIndeterminatePressed=D1 " +
        "RadioButtonOuterEllipseCheckedStrokePointerOver=L1 RadioButtonOuterEllipseCheckedStrokePressed=D1 RadioButtonOuterEllipseCheckedFillPointerOver=L1 " +
        "RadioButtonOuterEllipseCheckedFillPressed=D1 SliderThumbBackgroundPointerOver=L1 SliderThumbBackgroundPressed=D1 ToggleSwitchFillOnPointerOver=L1 " +
        "ToggleSwitchFillOnPressed=D1 SplitButtonBackgroundCheckedPointerOver=L1 SplitButtonBackgroundCheckedPressed=D1";

    private const string DarkDynamic =
        "SystemControlBackgroundAccentBrush=A SystemControlDisabledAccentBrush=A SystemControlFocusVisualPrimaryBrush=BH " +
        "SystemControlFocusVisualSecondaryBrush=AM SystemControlRevealFocusVisualBrush=A SystemControlForegroundAccentBrush=A " +
        "SystemControlHighlightAccentBrush=A SystemControlHighlightAltAccentBrush=A SystemControlHighlightAltListAccentHighBrush=A " +
        "SystemControlHighlightAltListAccentLowBrush=A SystemControlHighlightAltListAccentMediumBrush=A SystemControlHighlightListAccentHighBrush=A " +
        "SystemControlHighlightListAccentLowBrush=A SystemControlHighlightListAccentMediumBrush=A SystemControlHyperlinkTextBrush=A " +
        "SystemControlHighlightAccentRevealBackgroundBrush=A SystemControlHighlightAccent3RevealBackgroundBrush=A " +
        "SystemControlHighlightAccent2RevealBackgroundBrush=A AccentButtonBackgroundPointerOver=L1 AccentButtonBackgroundPressed=D1 " +
        "ToggleButtonBackgroundCheckedPointerOver=D1 HyperlinkButtonForeground=A HyperlinkButtonForegroundPointerOver=A HyperlinkButtonForegroundPressed=L1 " +
        "CheckBoxCheckBackgroundStrokeCheckedPointerOver=D1 CheckBoxCheckBackgroundStrokeCheckedPressed=L1 " +
        "CheckBoxCheckBackgroundStrokeIndeterminatePointerOver=L1 CheckBoxCheckBackgroundStrokeIndeterminatePressed=D1 " +
        "CheckBoxCheckBackgroundFillCheckedPointerOver=L1 CheckBoxCheckBackgroundFillCheckedPressed=D1 CheckBoxCheckBackgroundFillIndeterminatePointerOver=L1 " +
        "CheckBoxCheckBackgroundFillIndeterminatePressed=D1 RadioButtonOuterEllipseCheckedStrokePointerOver=L1 RadioButtonOuterEllipseCheckedStrokePressed=D1 " +
        "RadioButtonOuterEllipseCheckedFillPointerOver=L1 RadioButtonOuterEllipseCheckedFillPressed=D1 SliderThumbBackgroundPointerOver=L1 " +
        "SliderThumbBackgroundPressed=D1 ToggleSwitchFillOnPointerOver=L1 ToggleSwitchFillOnPressed=D1 ToggleSwitchStrokeOnPointerOver=L1 " +
        "ToggleSwitchStrokeOnPressed=D1 SplitButtonBackgroundCheckedPointerOver=D1 SplitButtonBackgroundCheckedPressed=L1";

    // D-13: 팔레트 색을 StaticResource 로 받는 브러시 중 우리 팔레트(FillPalette)가 채우는 색 키를 쓰는 것 59개(밝게·어둡게 표가 같다).
    // 출처: 위와 같은 디컴파일(Avalonia.Themes.Fluent 12.1.4 net10.0, 2026-11-17 odev-theme-1 재추출). 첫 생성 때 색이 고정되므로 같은 변형 전환에서는
    // 앞 테마 색이 남았다(예: pd-ocean→pd-light 에서 ButtonForeground·TextControlForeground = SystemControlForegroundBaseHighBrush 가 #10263B 유지).
    // 팔레트가 채우지 않는 색(AltMedium·ChromeWhite 등 Fluent 기본 색)을 쓰는 나머지 35개는 테마와 무관해 넣지 않는다.
    // 값 약어(추가): BMH=BaseMediumHigh, BM=BaseMedium, BL=BaseLow, LM=ListMedium, LL=ListLow, CM=ChromeMedium, CML=ChromeMediumLow,
    // CH=ChromeHigh, CL=ChromeLow, AH=AltHigh, R=Region, E=ErrorText.
    private const string StaticPalette =
        "SystemControlBackgroundAltHighBrush=AH SystemControlBackgroundBaseHighBrush=BH SystemControlBackgroundBaseLowBrush=BL " +
        "SystemControlBackgroundBaseMediumBrush=BM SystemControlBackgroundBaseMediumHighBrush=BMH SystemControlBackgroundChromeMediumBrush=CM " +
        "SystemControlBackgroundChromeMediumLowBrush=CML SystemControlBackgroundListLowBrush=LL SystemControlBackgroundListMediumBrush=LM " +
        "SystemControlDisabledBaseHighBrush=BH SystemControlDisabledBaseLowBrush=BL SystemControlDisabledChromeHighBrush=CH " +
        "SystemControlDisabledChromeMediumLowBrush=CML SystemControlDisabledListMediumBrush=LM SystemControlForegroundAltHighBrush=AH " +
        "SystemControlForegroundBaseHighBrush=BH SystemControlForegroundBaseLowBrush=BL SystemControlForegroundBaseMediumBrush=BM " +
        "SystemControlForegroundBaseMediumHighBrush=BMH SystemControlForegroundChromeHighBrush=CH SystemControlForegroundChromeMediumBrush=CM " +
        "SystemControlForegroundListLowBrush=LL SystemControlForegroundListMediumBrush=LM SystemControlHighlightAltAltHighBrush=AH " +
        "SystemControlHighlightAltBaseHighBrush=BH SystemControlHighlightAltBaseLowBrush=BL SystemControlHighlightAltBaseMediumBrush=BM " +
        "SystemControlHighlightAltBaseMediumHighBrush=BMH SystemControlHighlightBaseHighBrush=BH SystemControlHighlightBaseLowBrush=BL " +
        "SystemControlHighlightBaseMediumBrush=BM SystemControlHighlightBaseMediumHighBrush=BMH SystemControlHighlightChromeHighBrush=CH " +
        "SystemControlHighlightListMediumBrush=LM SystemControlHighlightListLowBrush=LL SystemControlHyperlinkBaseHighBrush=BH " +
        "SystemControlHyperlinkBaseMediumBrush=BM SystemControlHyperlinkBaseMediumHighBrush=BMH SystemControlPageBackgroundAltHighBrush=AH " +
        "SystemControlPageBackgroundBaseLowBrush=BL SystemControlPageBackgroundBaseMediumBrush=BM SystemControlPageBackgroundListLowBrush=LL " +
        "SystemControlPageBackgroundChromeLowBrush=CL SystemControlPageBackgroundChromeMediumLowBrush=CML SystemControlPageTextBaseHighBrush=BH " +
        "SystemControlPageTextBaseMediumBrush=BM SystemControlErrorTextForegroundBrush=E SystemControlHighlightListLowRevealBackgroundBrush=LM " +
        "SystemControlHighlightListMediumRevealBackgroundBrush=LM SystemControlTransientBackgroundBrush=CML SystemRegionBrush=R " +
        "ButtonBackgroundPointerOver=BH RepeatButtonBackgroundPointerOver=BH ToggleButtonBackgroundPointerOver=BH ExpanderChevronBackgroundPointerOver=BH " +
        "ScrollBarTrackFill=CM ScrollBarTrackFillPointerOver=CM SplitButtonBackgroundPointerOver=BH ScrollViewerScrollBarsSeparatorBackground=CM";

    private static (string Key, string ColorKey)[]? s_lightDynamic;
    private static (string Key, string ColorKey)[]? s_darkDynamic;
    private static (string Key, string ColorKey)[]? s_static;

    /// <summary>D-13: 팔레트 색을 정적으로 받은 브러시 표(두 변형 공통).</summary>
    internal static (string Key, string ColorKey)[] StaticKeys => s_static ??= Parse(StaticPalette);

    /// <summary>변형별 동적 브러시 표(밝게 = Fluent Default 사전, 어둡게 = Dark 사전).</summary>
    internal static (string Key, string ColorKey)[] DynamicKeys(ThemeVariant variant) => variant == ThemeVariant.Dark
        ? s_darkDynamic ??= Parse(DarkDynamic)
        : s_lightDynamic ??= Parse(LightDynamic);

    private static (string, string)[] Parse(string table) => table.Split(' ', StringSplitOptions.RemoveEmptyEntries)
        .Select(e => e.Split('='))
        .Select(p => (p[0], p[1] switch
        {
            "A" => "SystemAccentColor",
            "L1" => "SystemAccentColorLight1",
            "D1" => "SystemAccentColorDark1",
            "BH" => "SystemBaseHighColor",
            "BMH" => "SystemBaseMediumHighColor",
            "BM" => "SystemBaseMediumColor",
            "BL" => "SystemBaseLowColor",
            "LM" => "SystemListMediumColor",
            "LL" => "SystemListLowColor",
            "CM" => "SystemChromeMediumColor",
            "CML" => "SystemChromeMediumLowColor",
            "CH" => "SystemChromeHighColor",
            "CL" => "SystemChromeLowColor",
            "AH" => "SystemAltHighColor",
            "R" => "SystemRegionColor",
            "E" => "SystemErrorTextColor",
            _ => "SystemAltMediumColor",
        }))
        .ToArray();

    /// <summary>
    /// 현재 변형 사전의 동적 브러시 인스턴스에 이전 방식(팔레트 교체)과 같은 색을 직접 넣는다 — 리소스 알림 없이 다시 그리기만 난다.
    /// 목표 색은 붙이지 않은 참조 팔레트(FillPalette 같은 식)에서 읽는다 — 강조 음영(Light1·Dark1)도 Fluent 자체 계산을 그대로 쓴다.
    /// 참조 팔레트에 없는 색(AltMedium 등)은 Fluent 기본 색 사전 값이다(어느 방식에서도 테마와 무관하게 같다).
    /// </summary>
    internal static void Sync(Application app, ThemeDefinition t, Color fb, ThemeVariant variant)
    {
        if (app.Styles.OfType<FluentTheme>().FirstOrDefault() is not { } fluent)
        {
            return;
        }

        var reference = ThemeApplier.FillPalette(new ColorPaletteResources(), t, fb); // 주인 없는 팔레트 — 알림이 나지 않는다
        var (found, changed, missing) = Apply(fluent, reference, DynamicKeys(variant), variant);
        PerfProbe.Current.SetMetric("theme.fluent_sync.found", found);
        PerfProbe.Current.SetMetric("theme.fluent_sync.changed", changed);
        PerfProbe.Current.SetMetric("theme.fluent_sync.missing", missing); // 0 이 아니면 Fluent 버전이 바뀌어 표가 어긋난 것

        // D-13: 정적으로 고정된 브러시도 같은 참조 팔레트 색으로 맞춘다 — 같은 변형 전환에서 글자색(BaseHigh)·테두리 등이 앞 테마 값으로 남지 않게 한다.
        (found, changed, missing) = Apply(fluent, reference, StaticKeys, variant);
        PerfProbe.Current.SetMetric("theme.fluent_sync.static_found", found);
        PerfProbe.Current.SetMetric("theme.fluent_sync.static_changed", changed);
        PerfProbe.Current.SetMetric("theme.fluent_sync.static_missing", missing);
    }

    /// <summary>표의 브러시 인스턴스마다 참조 팔레트(없으면 Fluent 기본 색) 색을 넣는다. 인스턴스를 바꾸지 않으므로 리소스 알림이 없다.</summary>
    private static (int Found, int Changed, int Missing) Apply(FluentTheme fluent, ColorPaletteResources reference,
        (string Key, string ColorKey)[] table, ThemeVariant variant)
    {
        int found = 0, changed = 0, missing = 0;
        foreach (var (key, colorKey) in table)
        {
            if (!fluent.TryGetResource(key, variant, out var v) || v is not SolidColorBrush brush
                || !(reference.TryGetResource(colorKey, variant, out var c) || fluent.TryGetResource(colorKey, variant, out c)) || c is not Color color)
            {
                missing++;
                continue;
            }

            found++;
            if (brush.Color != color)
            {
                brush.Color = color;
                changed++;
            }
        }

        return (found, changed, missing);
    }
}
