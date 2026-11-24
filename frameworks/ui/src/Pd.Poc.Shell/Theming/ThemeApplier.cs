// 테마 적용기(UI-07) — 토큰 브러시 교체 + 기반 ThemeVariant + Fluent 팔레트·Dock 색 키 덮어쓰기(알림 묶음 처리·계측), OS 라이트/다크 감지
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Pd.Poc.Shell.Diagnostics;

namespace Pd.Poc.Shell.Theming;

/// <summary>
/// 테마를 Application.Resources 에 반영한다. 화면은 {DynamicResource PdBrush*} 로 참조하므로 즉시 바뀐다.
/// 브러시는 키마다 SolidColorBrush 인스턴스를 1개 유지하고 Color 만 바꾼다(편집 미리보기 때 리소스 재탐색 비용 없음).
/// </summary>
public static class ThemeApplier
{
    // 진단 훅 3개(키 제외·팔레트 교체 생략·M4 이전 방식)는 ThemeApplierDiag 의 Conditional 메서드다 — 제품 빌드는 호출 자체가 지워진다.
    // Fluent 동적 브러시 동기화는 FluentDynamicSync 가 맡는다.

    private static bool s_paletteAttached; // 팔레트를 한 번이라도 붙였는가(첫 적용은 변형과 무관하게 붙인다)

    // Dock.Avalonia.Themes.Fluent 12.1.0.12 의 색 키(출처: 패키지 dll 문자열 실측 2026-10-28, 49개 중 매핑 가능한 것).
    // App.Resources 는 Styles 리소스보다 먼저 조회되므로 같은 키를 넣으면 덮인다. Dock 템플릿이 DynamicResource 로 참조하는 곳만 실시간 반영.
    private static readonly (string DockKey, string Token)[] DockMap =
    {
        ("DockThemeBackgroundBrush", "PdBrushBackground"),
        ("DockThemeControlBackgroundBrush", "PdBrushSurface"),
        ("DockThemeForegroundBrush", "PdBrushText"),
        ("DockThemeBorderLowBrush", "PdBrushBorder"),
        ("DockThemeAccentBrush", "PdBrushAccent"),
        ("DockApplicationAccentBrushHigh", "PdBrushAccent"),
        ("DockApplicationAccentBrushMed", "PdBrushAccent"),
        ("DockApplicationAccentBrushLow", "PdBrushAccent"),
        ("DockApplicationAccentBrushIndicator", "PdBrushAccent"),
        ("DockApplicationAccentForegroundBrush", "PdBrushAccentText"),
        ("DockBorderSubtleBrush", "PdBrushBorder"),
        ("DockBorderStrongBrush", "PdBrushBorder"),
        ("DockSeparatorBrush", "PdBrushBorder"),
        ("DockSplitterIdleBrush", "PdBrushBorder"),
        ("DockSplitterHoverBrush", "PdBrushAccent"),
        ("DockSplitterDragBrush", "PdBrushAccent"),
        ("DockTargetIndicatorBrush", "PdBrushAccent"),
        ("DockSurfaceWorkbenchBrush", "PdBrushBackground"),
        ("DockSurfaceEditorBrush", "PdBrushBackground"),
        ("DockSurfacePanelBrush", "PdBrushSurface"),
        ("DockSurfaceSidebarBrush", "PdBrushSurface"),
        ("DockSurfaceHeaderBrush", "PdBrushMdiTitleBarInactive"),
        ("DockSurfaceHeaderActiveBrush", "PdBrushMdiTitleBar"),
        ("DockTabBackgroundBrush", "PdBrushTabInactive"),
        ("DockTabActiveBackgroundBrush", "PdBrushTabActive"),
        ("DockTabHoverBackgroundBrush", "PdBrushSurfaceAlt"),
        ("DockTabForegroundBrush", "PdBrushTextMuted"),
        ("DockTabActiveForegroundBrush", "PdBrushTabActiveText"),
        ("DockTabSelectedForegroundBrush", "PdBrushTabActiveText"),
        ("DockTabActiveIndicatorBrush", "PdBrushAccent"),
        ("DockDocumentTabSelectedForegroundBrush", "PdBrushTabActiveText"),
        ("DockDocumentTabStripBackgroundBrush", "PdBrushSurface"),
        ("DockDocumentContentBorderBrush", "PdBrushBorder"),
        ("DockWindowChromeBackgroundBrush", "PdBrushSurface"),
        ("DockWindowChromeTitleBarBackgroundBrush", "PdBrushMdiTitleBar"),
        ("DockWindowChromeForegroundBrush", "PdBrushMdiTitleText"),
        ("DockWindowChromeBorderBrush", "PdBrushBorder"),
        ("DockChromeButtonForegroundBrush", "PdBrushText"),
        ("DockChromeButtonHoverBackgroundBrush", "PdBrushSurfaceAlt"),
        ("DockToolChromeIconBrush", "PdBrushTextMuted"),
        // Fluent 기본 컨트롤 키(같은 방식으로 App.Resources 가 테마 사전보다 먼저 조회됨) — Mars 팔레트 버튼 #FDFDFD/#D0D0D0·입력칸 #ABADB3(odev-1 실측)
        ("ButtonBackground", "PdBrushButton"),
        ("ButtonBorderBrush", "PdBrushButtonBorder"),
        ("TextControlBorderBrush", "PdBrushInputBorder"),
        // 창 배경(T9): Fluent Window·EmbeddableControlRoot 의 Background 는 SystemRegionBrush, Window 투명 대체 배경은 SystemControlBackgroundAltHighBrush 다
        // (출처: AvaloniaUI/Avalonia Fluent Controls/Window.xaml·EmbeddableControlRoot.xaml). 두 브러시는 BaseResources 에서 Color 를 StaticResource 로 받아
        // 첫 생성 때 색이 고정되므로, 팔레트를 바꾸지 않는 같은 변형 전환(pd-ocean→pd-light·pd-dark→pd-contrast)에서 앞 테마 배경이 남았다.
        // 팔레트도 RegionColor·AltHigh 를 PdBrushBackground 로 채우므로 같은 토큰으로 덮어 의미는 그대로 두고 매 적용마다 따라가게 한다.
        ("SystemRegionBrush", "PdBrushBackground"),
        ("SystemControlBackgroundAltHighBrush", "PdBrushBackground"),
    };

    private static readonly Dictionary<string, SolidColorBrush> Brushes = new(StringComparer.Ordinal);

    /// <summary>브러시 인스턴스가 새로 생기거나 잊힐 때 오르는 세대(색만 바뀌면 그대로). 별칭 캐시(WindowChromeStyle)가 다시 걸지 판단한다(K13).</summary>
    internal static int BrushGeneration { get; private set; }

    /// <summary>마지막으로 적용을 마친 테마(자동화 상태 덤프·진단용). 적용은 프레임 뒤로 미뤄지므로 선택 표시·편집 판단에는 ThemeCatalog.Requested 를 쓴다.</summary>
    public static ThemeDefinition? Active { get; private set; }

    /// <summary>PERF-UI 비교용: 환경변수 PD_THEME_APPLY=legacy 면 이전 방식(키마다 대입 → 키마다 리소스 변경 알림).</summary>
    private static readonly bool Legacy = Environment.GetEnvironmentVariable("PD_THEME_APPLY") == "legacy";

    // Color 짝 사전(PdBrush…Color)은 Q4 에서 제거했다: 쓰는 곳 0건(grep 2026-10-29)인데 적용마다 병합 사전 교체로 트리 전체 알림 1회를 냈다.
    // 코드에서 색 값이 필요하면 ColorOf(알림 없음)를 쓴다.
    private static string? s_signature; // 마지막 적용 테마의 Id·기반·토큰 색 서명
    private static bool s_counting;
    private static int s_notifications;

    /// <summary>
    /// 테마 적용. UiSettings.Apply 안에서 호출된다(그 뒤 UiSettings.Changed 로 코드 그리기 컨트롤 갱신).
    /// 계측: theme.apply.{batched|legacy}(sync·frame ms), 메트릭 theme.apply.notifications_last/max = 이번 적용 중 앱 리소스 변경 알림 수.
    /// </summary>
    public static void Apply(Application app, ThemeDefinition theme)
    {
        if (!s_counting)
        {
            s_counting = true;
            app.ResourcesChanged += (_, _) => s_notifications++; // 알림 1회 = 열린 모든 창의 DynamicResource 재조회(원인 가설)
        }

        // 같은 테마·같은 색이면 생략(셸은 Theme 부분 요청 때마다 호출). 색 편집 직후에는 서명이 달라 다시 적용된다.
        var signature = Signature(theme);
        if (!Legacy && signature == s_signature)
        {
            Active = theme;
            return;
        }

        s_signature = signature;
        var before = s_notifications;
        using (PerfProbe.Current.TimeOperation(Legacy ? "theme.apply.legacy" : "theme.apply.batched"))
        {
            Active = theme;
            var variant = theme.Base == ThemeBase.Dark ? ThemeVariant.Dark : ThemeVariant.Light;
            var variantChanged = app.RequestedThemeVariant != variant; // M4 b2: 같은 변형이면 변형 설정(전체 재해석)을 건너뛴다
            var always = Legacy;
            ThemeApplierDiag.PaletteAlways(ref always);
            var fallback = theme.Base == ThemeBase.Dark ? Colors.White : Colors.Black;
            var res = app.Resources;
            foreach (var key in ThemeTokens.Keys)
            {
                SetBrush(res, key, theme.GetColor(key, fallback));
            }

            foreach (var (dockKey, token) in DockMap)
            {
                SetBrush(res, dockKey, theme.GetColor(token, fallback));
            }

            // a1+b2: 팔레트(전체 알림)는 변형이 바뀔 때·첫 적용 때만 교체하고, 교체를 변형 설정보다 먼저 끝내 재해석을 1회로 묶는다.
            var skipPalette = false;
            ThemeApplierDiag.SkipPalette(ref skipPalette);
            if (!skipPalette && (always || variantChanged || !s_paletteAttached))
            {
                ApplyFluentPalette(app, theme, fallback);
                s_paletteAttached = true;
            }

            if (variantChanged)
            {
                app.RequestedThemeVariant = variant;
            }

            // a1: 팔레트에 동적으로 묶인 Fluent 브러시 85개와 첫 생성 때 색이 고정되는 59개(D-13 글자색)를 알림 없이 Color 만 맞춘다.
            // 팔레트를 매번 교체하는 진단·legacy 경로(always)도 StaticResource 로 고정된 59개는 팔레트 교체로 안 바뀌므로 똑같이 맞춘다(동적 85개는 같은 값을 다시 쓸 뿐 무해).
            FluentDynamicSync.Sync(app, theme, fallback, variant);
        }

        var count = s_notifications - before;
        PerfProbe.Current.SetMetric("theme.apply.notifications_last", count);
        PerfProbe.Current.SetMetricMax("theme.apply.notifications_max", count);
    }

    private static string Signature(ThemeDefinition t) =>
        t.Id + "|" + t.Base + "|" + string.Join(",", ThemeTokens.Keys.Select(k => t.Colors.GetValueOrDefault(k)));

    /// <summary>토큰(또는 Dock 키)의 현재 색. 리소스 조회·알림 없이 유지 중인 브러시에서 읽는다. 아직 적용 전이면 fallback.</summary>
    public static Color ColorOf(string key, Color fallback) => Brushes.TryGetValue(key, out var b) ? b.Color : fallback;

    /// <summary>OS 가 다크 모드인지(브라우저는 prefers-color-scheme). 알 수 없으면 false.</summary>
    public static bool IsOsDark(Application app) =>
        app.PlatformSettings?.GetColorValues().ThemeVariant == PlatformThemeVariant.Dark;

    /// <summary>OS 라이트/다크 변경 감시(시스템 모드에서 짝 테마 재적용용).</summary>
    public static void WatchOs(Application app, Action onChanged)
    {
        if (app.PlatformSettings is { } ps)
        {
            ps.ColorValuesChanged += (_, _) => onChanged();
        }
    }

    /// <summary>브러시는 인스턴스를 유지하고 Color 만 바꾼다(리소스 알림 없음, 다시 그리기만). 처음 보는 키만 리소스에 넣는다(알림 1회, 기동 때).</summary>
    private static void SetBrush(IResourceDictionary res, string key, Color color)
    {
        var skip = false;
        ThemeApplierDiag.Filter(key, ref skip);
        if (skip) { return; }

        if (Brushes.TryGetValue(key, out var brush))
        {
            brush.Color = color;
            return;
        }

        brush = new SolidColorBrush(color);
        Brushes[key] = brush;
        BrushGeneration++;
        res[key] = brush;
    }

    // Fluent 기본 컨트롤(버튼·메뉴·텍스트박스·DataGrid 등) 색은 FluentTheme.Palettes[기반 변형] 으로 바꾼다.
    // 출처: Avalonia.Themes.Fluent 12.1.4 XML 문서 — ColorPaletteResources(Accent·RegionColor·Base*·Chrome*·List*·ErrorText), FluentTheme.Palettes.
    private static void ApplyFluentPalette(Application app, ThemeDefinition t, Color fb)
    {
        if (app.Styles.OfType<FluentTheme>().FirstOrDefault() is not { } fluent)
        {
            return;
        }

        var variant = t.Base == ThemeBase.Dark ? ThemeVariant.Dark : ThemeVariant.Light;
        var attached = fluent.Palettes.TryGetValue(variant, out var p) ? p : null;

        // 배치: 붙지 않은 새 팔레트에 14색을 채운 뒤 1회 교체(붙은 팔레트의 속성을 하나씩 바꾸면 속성마다 알림).
        p = FillPalette(Legacy && attached is not null ? attached : new ColorPaletteResources(), t, fb);
        if (!ReferenceEquals(p, attached) && (attached is null || Key(attached) != Key(p)))
        {
            fluent.Palettes[variant] = p; // 14색이 같으면 교체 생략(알림 0회)
        }
    }

    /// <summary>팔레트 14색 채우기(붙인 팔레트·M4 동기화용 참조 팔레트 공용 — 같은 식이어야 색이 일치한다). FluentDynamicSync·ThemeApplierDiag 도 쓴다.</summary>
    internal static ColorPaletteResources FillPalette(ColorPaletteResources p, ThemeDefinition t, Color fb)
    {
        p.Accent = t.GetColor("PdBrushAccent", fb);
        p.RegionColor = t.GetColor("PdBrushBackground", fb);
        p.AltHigh = t.GetColor("PdBrushBackground", fb);
        p.BaseHigh = t.GetColor("PdBrushText", fb);
        p.BaseMediumHigh = t.GetColor("PdBrushText", fb);
        p.BaseMedium = t.GetColor("PdBrushTextMuted", fb);
        p.BaseLow = t.GetColor("PdBrushBorder", fb);
        p.ChromeLow = t.GetColor("PdBrushSurface", fb);
        p.ChromeMediumLow = t.GetColor("PdBrushSurface", fb);
        p.ChromeMedium = t.GetColor("PdBrushSurfaceAlt", fb);
        p.ChromeHigh = t.GetColor("PdBrushBorder", fb);
        p.ListLow = t.GetColor("PdBrushSurfaceAlt", fb);
        p.ListMedium = t.GetColor("PdBrushSelection", fb);
        p.ErrorText = t.GetColor("PdBrushError", fb);
        return p;
    }

    private static string Key(ColorPaletteResources p) => string.Join(",", p.Accent, p.RegionColor, p.AltHigh, p.BaseHigh,
        p.BaseMediumHigh, p.BaseMedium, p.BaseLow, p.ChromeLow, p.ChromeMediumLow, p.ChromeMedium, p.ChromeHigh, p.ListLow, p.ListMedium, p.ErrorText);

    /// <summary>다음 Apply 가 같은 테마라도 다시 칠하게 서명을 지운다(진단 스위치 전환용).</summary>
    internal static void ResetSignature() => s_signature = null;

    /// <summary>유지 중인 브러시를 잊는다(진단으로 키를 리소스에서 뺄 때 — 다시 넣을 때 새 인스턴스를 만든다).</summary>
    internal static void ForgetBrush(string key)
    {
        if (Brushes.Remove(key)) { BrushGeneration++; }
    }
#if PD_DIAGNOSTICS

    // 진단 공개 API 전달자(W1 — 호출부 AutomationBridge 는 W2 범위라 이름을 유지한다. W5 에서 정리).
    public static string DiagnosticOverrides => ThemeApplierDiag.DiagnosticOverrides;

    public static void SetFluentPalette(bool on) => ThemeApplierDiag.SetFluentPalette(on);

    public static bool SetFluentPaletteMode(string? mode) => ThemeApplierDiag.SetFluentPaletteMode(mode);

    public static string FluentSyncCensus() => ThemeApplierDiag.FluentSyncCensus();

    public static void SetDiagnosticOverrides(bool fluentOverride, bool newTokens) => ThemeApplierDiag.SetDiagnosticOverrides(fluentOverride, newTokens);
#endif
}
