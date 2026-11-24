// 캡션 3버튼(최소·최대·닫기)의 배치·크기·색·기호 리소스 별칭 — Windows(오른쪽 넓은 버튼)와 macOS(왼쪽 신호등)를 같은 템플릿으로 그리게 한다
using System;
using System.Globalization;
using Avalonia;
using Avalonia.Layout;
using Avalonia.Media;
using xkxk.frameworks.ui.shell.Theming;

namespace xkxk.frameworks.ui.shell.Docking;

/// <summary>
/// WindowCommands.ApplyShadow 가 크롬 리소스를 다시 걸 때 함께 호출한다. 리소스를 쓰는 곳: Themes/MdiDocumentWindowTheme.axaml(MDI 머리줄),
/// Views/SdiTitleBar.axaml, Themes/CaptionButtons.axaml(pdCap 공통 스타일). macOS 수치(원 12·간격 8·제목줄 28·왼쪽 8)와 색은
/// PD 설계값, 공식값 아님, Mac 실측 과제다(plan/04_기술스택/05_창크롬_OS디자인_기준.md §2).
/// </summary>
public static class CaptionLayout
{
    /// <summary>별칭을 다시 건 뒤 알린다(스타일·강제 지정·테마 변경 때). 구독자(메인·SDI 제목줄)는 ChromePlatform.UseNativeMacCaption 만 다시 읽으면 된다.</summary>
    public static event Action? Applied;

    public static void Apply(Application app, ChromeStyleKind style, double inactiveOpacity)
    {
        var mac = style == ChromeStyleKind.MacOS;
        ApplyPlacement(app, mac);
        ApplySize(app, mac, style == ChromeStyleKind.MarsClassic, inactiveOpacity);
        ApplyGlyphStates(app, style == ChromeStyleKind.MarsClassic || (WindowChromeStyle.AccentTitleBar && !mac)); // ApplyBrushes 의 PdCapGlyph* 별칭이 이 값을 가리키므로 먼저
        ApplyBrushes(app, mac);
        ApplyGlyphs(app, mac);
        ApplyTabColors(app);
        Applied?.Invoke();
    }

    /// <summary>
    /// U6 기호 상태 색: hover·누름·켜짐은 바탕 대신 기호 색으로 보인다. 강조 띠(남색) 위는 호박·하늘색, 표면색 제목줄 위는 파랑 계열(ChromeResources.axaml PD 설계값).
    /// 캡션 3버튼 별칭(PdCapGlyph*)은 macOS 면 신호등 기호색 그대로다.
    /// </summary>
    private static void ApplyGlyphStates(Application app, bool accent)
    {
        var on = accent ? "OnAccent" : "OnSurface";
        WindowCommands.Alias(app, "PdChromeGlyphHover", "PdBrushChromeGlyphHover" + on);
        WindowCommands.Alias(app, "PdChromeGlyphPressed", "PdBrushChromeGlyphPressed" + on);
        WindowCommands.Alias(app, "PdChromeGlyphChecked", "PdBrushChromeGlyphChecked" + on);
        WindowCommands.Alias(app, "PdChromeCloseGlyphHover", "PdBrushChromeCloseGlyphHover" + on);
        WindowCommands.Alias(app, "PdChromeCloseGlyphPressed", "PdBrushChromeCloseGlyphPressed" + on);
        // 버튼 상태 표현(사용자 지시 2026-11-05, 표는 공통원칙 §4.2): 호버 = 옅은 채움+테두리, 켜짐·누름 = 그림색 채움+반전 그림, 켜짐+호버 = 안쪽 테두리.
        // 그림색은 쉬는 기호색(클래식은 은색 토큰)이 아니라 제목줄 글자색이다 — 채움 위 대비를 지키기 위해서다. 터치 기기는 포인터가 떠 있지 않아 호버 표시가 고착되지 않게 끈다
        const string none = "PdBrushTransparent";
        var touch = xkxk.frameworks.ui.shell.Platform.InputFormFactor.IsTouchDevice;
        var glyph = accent ? "PdBrushMdiTitleText" : "PdBrushText";
        WindowCommands.Alias(app, "PdChromeStateGlyph", glyph);
        WindowCommands.Alias(app, "PdChromeStateHoverFill", touch ? none : "PdChromeHover");
        // hover·누름 테두리는 없다(사용자 지시 2026-11-11 — 메뉴 막대처럼 hover 는 바탕만). 별칭은 남겨 두고 투명으로 고정한다
        WindowCommands.Alias(app, "PdChromeStateHoverBorder", none);
        WindowCommands.Alias(app, "PdChromeStateOnFill", glyph);
        WindowCommands.Alias(app, "PdChromeStateOnGlyph", "PdChromeTitleActive");
        WindowCommands.Alias(app, "PdChromeStateOnHoverBorder", none);
        WindowCommands.Alias(app, "PdChromeStatePressedFill", glyph);
        // 새로고침 적중률 색: 강조 띠 위 / 표면색 제목줄 위 토큰(ChromeButtons.CacheBrush)
        WindowCommands.Alias(app, "PdChromeCacheZero", accent ? "PdBrushChromeGlyph" : "PdBrushCacheZeroOnSurface");
        WindowCommands.Alias(app, "PdChromeCacheLow", accent ? "PdBrushCacheLow" : "PdBrushCacheLowOnSurface");
        WindowCommands.Alias(app, "PdChromeCacheHigh", accent ? "PdBrushCacheHigh" : "PdBrushCacheHighOnSurface");
        WindowCommands.Alias(app, "PdChromeCacheFull", accent ? "PdBrushCacheFull" : "PdBrushCacheFullOnSurface");
    }

    /// <summary>탭 상태 색(U7·U19 — odev-tabs 가 쓴다): 활성 창 탭·패널 선택 탭은 창 제목줄과 같은 반전, 최소화 창은 흐린 글자.</summary>
    private static void ApplyTabColors(Application app)
    {
        // 원본은 테마 편집 토큰(ThemeTokens — UI-07 사용자 편집·저장 대상, 옛 저장 테마는 ThemeCatalog.Normalize 가 내장값으로 채움)
        WindowCommands.Alias(app, "PdChromeTabActiveBackground", "PdBrushWindowTabActive");
        WindowCommands.Alias(app, "PdChromeTabActiveText", "PdBrushWindowTabActiveText");
        WindowCommands.Alias(app, "PdChromeTabInactiveBackground", "PdBrushWindowTabInactive");
        WindowCommands.Alias(app, "PdChromeTabInactiveText", "PdBrushWindowTabInactiveText");
        WindowCommands.Alias(app, "PdChromeTabMinimizedBackground", "PdBrushTransparent");
        WindowCommands.Alias(app, "PdChromeTabMinimizedText", "PdBrushWindowTabMinimizedText");
        WindowCommands.Alias(app, "PdChromeTabHoverBackground", "PdBrushWindowTabHover");
        WindowCommands.Alias(app, "PdChromePanelTabSelectedBackground", "PdBrushPanelTabSelected");
        WindowCommands.Alias(app, "PdChromePanelTabSelectedText", "PdBrushPanelTabSelectedText");
    }

    /// <summary>머리줄 열(0 = 맨 왼쪽 신호등 칸, 5 = 맨 오른쪽 Windows 캡션 칸)과 3버튼 순서(Windows 최소·최대·닫기 / macOS 닫기·최소·확대).</summary>
    private static void ApplyPlacement(Application app, bool mac)
    {
        WindowCommands.Set(app, "PdCaptionColumn", mac ? 0 : 5);
        WindowCommands.Set(app, "PdCapMinColumn", mac ? 1 : 0);
        WindowCommands.Set(app, "PdCapMaxColumn", mac ? 2 : 1);
        WindowCommands.Set(app, "PdCapCloseColumn", mac ? 0 : 2);
        WindowCommands.Set(app, "PdCaptionOpsMargin", mac ? new Thickness(4, 0, 4, 0) : default);
        WindowCommands.Set(app, "PdLogoVisible", !mac); // macOS 창 제목줄에는 앱 아이콘이 없다
        WindowCommands.Set(app, "PdTitleAlignment", mac ? HorizontalAlignment.Center : HorizontalAlignment.Stretch); // macOS 제목은 가운데(제목 열 기준)
    }

    /// <summary>크기: Windows 36×32(U11 — 46 은 기능 버튼 줄 옆에서 간격이 과했다, 05_창크롬 문서 §2.6)·Mars 18×22, macOS 는 원 12px + 좌우 4px(중심 간격 20px).</summary>
    private static void ApplySize(Application app, bool mac, bool mars, double inactiveOpacity)
    {
        WindowCommands.Set(app, "PdCapWidth", mac ? 12.0 : mars ? 18.0 : 36.0);
        WindowCommands.Set(app, "PdCapHeight", mac ? 12.0 : mars ? 22.0 : 32.0);
        WindowCommands.Set(app, "PdCapMargin", mac ? new Thickness(4, 0, 4, 0) : default);
        WindowCommands.Set(app, "PdCapCorner", new CornerRadius(mac ? 6 : 0));
        WindowCommands.Set(app, "PdCapGlyphSize", mac ? 6.0 : mars ? 9.0 : 10.0);
        WindowCommands.Set(app, "PdCapGlyphRestOpacity", mac ? 0.0 : 1.0); // macOS 기호는 3버튼 묶음에 포인터가 올 때만 보인다
        WindowCommands.Set(app, "PdCapOpsInactiveOpacity", mac ? 1.0 : inactiveOpacity); // macOS 비활성은 흐림 대신 회색 원
        // MDI 머리줄의 캡션 자리(MdiDocumentWindowTheme.axaml): 캡션 3버튼을 끝에 겹쳐 고정하고 나머지 줄은 이 폭만큼 비운다 — 좁은 창에서도 캡션이 창 밖으로 밀리지 않게(U9 ⑩)
        var reserve = mac ? 3 * (12.0 + 8) + 8 : 3 * (mars ? 18.0 : 36.0); // 3 × (버튼 폭 + 좌우 여백) + 묶음 여백(macOS 4+4)
        WindowCommands.Set(app, "PdCaptionReserve", mac ? new Thickness(reserve, 0, 0, 0) : new Thickness(0, 0, reserve, 0));
        WindowCommands.Set(app, "PdCaptionAlign", mac ? HorizontalAlignment.Left : HorizontalAlignment.Right);
        // 제목 최소 칸(리드 결정 #7): 기능 버튼 줄은 이 폭 오른쪽에만 놓인다 — WindowMinSize 의 제목 항과 같은 산식("W…" 측정 + 8)
        var size = UiSettings.Current.BaseFontSize > 0 ? UiSettings.Current.BaseFontSize : 12;
        var title = new FormattedText("W…", CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Typeface.Default, size, null).Width + 8;
        WindowCommands.Set(app, "PdTitleMinReserve", new Thickness(Math.Ceiling(title), 0, 0, 0));
    }

    /// <summary>바탕·기호: macOS 는 원 색(쉼·hover 같은 색, 누름만 진하게, 비활성 회색). Windows·Mars 는 쉬는 바탕이 투명이고 hover·누름은 채움+테두리, 닫기는 빨강 채움이다(사용자 지시 2026-11-05, U6 의 기호 색 표현을 대체).</summary>
    private static void ApplyBrushes(Application app, bool mac)
    {
        const string none = "PdBrushTransparent";
        var touch = xkxk.frameworks.ui.shell.Platform.InputFormFactor.IsTouchDevice;
        WindowCommands.Set(app, "PdCapBorderThickness", new Thickness(mac ? 0 : 1)); // 호버 테두리가 생겨도 크기가 흔들리지 않게 늘 같은 두께(평소 투명)
        WindowCommands.Alias(app, "PdCapHoverBorder", mac ? none : "PdChromeStateHoverBorder");
        WindowCommands.Alias(app, "PdCapGlyph", mac ? "PdBrushMacCapGlyph" : "PdChromeGlyph");
        WindowCommands.Alias(app, "PdCapGlyphHover", mac ? "PdBrushMacCapGlyph" : "PdChromeStateGlyph");
        WindowCommands.Alias(app, "PdCapGlyphPressed", mac ? "PdBrushMacCapGlyph" : "PdChromeStateOnGlyph");
        WindowCommands.Alias(app, "PdCapCloseGlyphHover", mac ? "PdBrushMacCapGlyph" : touch ? "PdChromeGlyph" : "PdBrushMainTitleText");
        WindowCommands.Alias(app, "PdCapCloseRest", mac ? "PdBrushMacCapClose" : none);
        WindowCommands.Alias(app, "PdCapMinRest", mac ? "PdBrushMacCapMin" : none);
        WindowCommands.Alias(app, "PdCapMaxRest", mac ? "PdBrushMacCapMax" : none);
        WindowCommands.Alias(app, "PdCapCloseHover", mac ? "PdBrushMacCapClose" : touch ? none : "PdBrushCaptionCloseHover");
        WindowCommands.Alias(app, "PdCapMinHover", mac ? "PdBrushMacCapMin" : "PdChromeStateHoverFill");
        WindowCommands.Alias(app, "PdCapMaxHover", mac ? "PdBrushMacCapMax" : "PdChromeStateHoverFill");
        WindowCommands.Alias(app, "PdCapClosePressed", mac ? "PdBrushMacCapClosePressed" : "PdBrushCaptionClosePressed");
        WindowCommands.Alias(app, "PdCapClosePressedMdi", mac ? "PdBrushMacCapClosePressed" : "PdBrushCaptionClosePressed");
        WindowCommands.Alias(app, "PdCapCloseGlyphPressed", mac ? "PdBrushMacCapGlyph" : "PdBrushCaptionClosePressedGlyph");
        WindowCommands.Alias(app, "PdCapMinPressed", mac ? "PdBrushMacCapMinPressed" : "PdChromeStatePressedFill");
        WindowCommands.Alias(app, "PdCapMaxPressed", mac ? "PdBrushMacCapMaxPressed" : "PdChromeStatePressedFill");
        WindowCommands.Alias(app, "PdCapInactiveRest", mac ? "PdBrushMacCapInactive" : "PdBrushTransparent"); // HIG: inactive·non-key main 창은 gray
    }

    /// <summary>기호: Windows·Mars 는 PD 벡터(ChromeIcons.axaml, U10), macOS 는 PD 가 직접 그린 신호등 기호(MacCaptionResources.axaml — Apple 자산 복제 아님).</summary>
    private static void ApplyGlyphs(Application app, bool mac)
    {
        WindowCommands.Alias(app, "PdCapMinGeometry", mac ? "PdMacGlyphMinimize" : "PdGlyphMinimize");
        WindowCommands.Alias(app, "PdCapMaxGeometry", mac ? "PdMacGlyphZoom" : "PdGlyphMaximize");
        WindowCommands.Alias(app, "PdCapRestoreGeometry", mac ? "PdMacGlyphZoom" : "PdGlyphRestore");
        WindowCommands.Alias(app, "PdCapCloseGeometry", mac ? "PdMacGlyphClose" : "PdGlyphClose");
    }
}
