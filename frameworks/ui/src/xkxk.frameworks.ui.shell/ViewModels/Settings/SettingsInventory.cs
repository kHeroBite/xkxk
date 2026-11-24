// 설정 창 인벤토리(요건 7) — 설정 항목 → 페이지 매핑표. 화면 행(SettingsRow.SettingId)·검색·전수 대조(AC-7-01)의 단일 출처
using System;
using System.Collections.Generic;
using System.Linq;
using xkxk.frameworks.ui.shell.Localization;
using xkxk.frameworks.ui.shell.Theming;

namespace xkxk.frameworks.ui.shell.ViewModels.Settings;

/// <summary>설정 창 범주(왼쪽 목록 순서와 같음).</summary>
public enum SettingsCategoryId
{
    Appearance,
    Window,
    Language,
    Layout,
    System,
}

/// <summary>설정 항목 1개. Setting 은 UiSettings 속성 이름이며, UiSettings 에 없는 항목(명령·메인 뷰모델 값)은 null 이다.</summary>
public sealed record SettingsEntry(string Id, SettingsCategoryId Category, string LabelKey, string? Setting)
{
    /// <summary>같은 값을 이번 실행만 강제하는 시험 인자 속성(저장 안 됨, 별도 입력 없음). 이 행이 그 효과값을 함께 보여 준다.</summary>
    public string? OverrideSetting { get; init; }
}

/// <summary>모든 설정 항목. 페이지 axaml 의 행 SettingId 와 1:1 이며 시험이 창 트리와 대조한다.</summary>
public static class SettingsInventory
{
    /// <summary>범주별 제목 사전 키.</summary>
    public static string TitleKey(SettingsCategoryId id) => "settings.category." + id.ToString().ToLowerInvariant();

    public static IReadOnlyList<SettingsEntry> All { get; } = new[]
    {
        new SettingsEntry("theme", SettingsCategoryId.Appearance, "settings.theme", nameof(UiSettings.Theme)),
        new SettingsEntry("colorTheme", SettingsCategoryId.Appearance, "settings.colorTheme", nameof(UiSettings.ThemeId)),
        new SettingsEntry("systemLight", SettingsCategoryId.Appearance, "settings.systemLight", nameof(UiSettings.SystemLightThemeId)),
        new SettingsEntry("systemDark", SettingsCategoryId.Appearance, "settings.systemDark", nameof(UiSettings.SystemDarkThemeId)),
        new SettingsEntry("themeEditor", SettingsCategoryId.Appearance, "settings.themeEditor", null),
        new SettingsEntry("font", SettingsCategoryId.Appearance, "settings.font", nameof(UiSettings.FontName)),
        new SettingsEntry("docFont", SettingsCategoryId.Appearance, "settings.docFont", nameof(UiSettings.DocumentDefaultFontSize)),
        new SettingsEntry("fontSize", SettingsCategoryId.Appearance, "settings.fontSize", nameof(UiSettings.BaseFontSize)),
        new SettingsEntry("wheelMode", SettingsCategoryId.Appearance, "settings.wheelMode", nameof(UiSettings.WheelMode)),
        new SettingsEntry("shellFont", SettingsCategoryId.Appearance, "settings.shellFont", nameof(UiSettings.ShellFontName)),
        new SettingsEntry("shellFontSize", SettingsCategoryId.Appearance, "settings.shellFontSize", nameof(UiSettings.ShellFontSize)),
        new SettingsEntry("tabStripHeight", SettingsCategoryId.Appearance, "settings.tabStripHeight", nameof(UiSettings.ShellTabStripHeight)),
        new SettingsEntry("tabCloseMode", SettingsCategoryId.Appearance, "settings.tabCloseMode", nameof(UiSettings.TitleTabCloseMode)),

        new SettingsEntry("windowStyle", SettingsCategoryId.Window, "settings.windowStyle", nameof(UiSettings.WindowStyle)),
        new SettingsEntry("shadow", SettingsCategoryId.Window, "settings.shadow", nameof(UiSettings.WindowShadow)),
        new SettingsEntry("moveShadow", SettingsCategoryId.Window, "settings.moveShadow", nameof(UiSettings.MdiMoveLightShadowUser)),
        new SettingsEntry("switchSources", SettingsCategoryId.Window, "settings.switchSources", nameof(UiSettings.MotionSwitchSourcesUser)),
        new SettingsEntry("titleTabDisplay", SettingsCategoryId.Window, "settings.titleTabDisplay", nameof(UiSettings.TitleTabDisplay)),
        new SettingsEntry("menuIcon", SettingsCategoryId.Window, "settings.menuIcon", nameof(UiSettings.MenuIconVisible)),
        new SettingsEntry("favoriteBar", SettingsCategoryId.Window, "settings.favoriteBar", nameof(UiSettings.FavoriteBarVisible)),
        new SettingsEntry("favoritePosition", SettingsCategoryId.Window, "settings.favoritePosition", nameof(UiSettings.FavoriteBarPosition)),
        new SettingsEntry("favoriteClick", SettingsCategoryId.Window, "settings.favoriteClick", nameof(UiSettings.FavoriteBarClick)),
        new SettingsEntry("busyIcon", SettingsCategoryId.Window, "settings.busyIcon", nameof(UiSettings.BusyIconEnabled)),
        new SettingsEntry("busyTitleOnly", SettingsCategoryId.Window, "settings.busyTitleOnly", nameof(UiSettings.BusyIconTitleOnly)),
        new SettingsEntry("sdiAboveMain", SettingsCategoryId.Window, "settings.sdiAboveMain", nameof(UiSettings.SdiAboveMain)),
        new SettingsEntry("magnet", SettingsCategoryId.Window, "menu.ws.magnet", nameof(UiSettings.MagnetEnabled)),
        new SettingsEntry("magnetRange", SettingsCategoryId.Window, "settings.magnetRange", nameof(UiSettings.MagnetRange)),
        new SettingsEntry("snapEnabled", SettingsCategoryId.Window, "settings.snapEnabled", nameof(UiSettings.SnapEnabled)),
        new SettingsEntry("snapLayouts", SettingsCategoryId.Window, "settings.snapLayouts", nameof(UiSettings.SnapLayouts)),
        new SettingsEntry("snapEdgeDistance", SettingsCategoryId.Window, "settings.snapEdgeDistance", nameof(UiSettings.SnapEdgeDistance)),
        new SettingsEntry("snapBarDelay", SettingsCategoryId.Window, "settings.snapBarDelay", nameof(UiSettings.SnapBarDelayMs)),
        new SettingsEntry("snapEdge", SettingsCategoryId.Window, "settings.snapEdge", nameof(UiSettings.SnapEdgeEnabled)),
        new SettingsEntry("snapTopEdgeMaximize", SettingsCategoryId.Window, "settings.snapTopEdgeMaximize", nameof(UiSettings.SnapTopEdgeMaximize)),
        new SettingsEntry("snapKeyboard", SettingsCategoryId.Window, "settings.snapKeyboard", nameof(UiSettings.SnapKeyboard)),
        new SettingsEntry("snapFlyout", SettingsCategoryId.Window, "settings.snapFlyout", nameof(UiSettings.SnapFlyoutEnabled)),
        new SettingsEntry("snapFlyoutDelay", SettingsCategoryId.Window, "settings.snapFlyoutDelay", nameof(UiSettings.SnapFlyoutDelayMs)),
        new SettingsEntry("snapSdiMode", SettingsCategoryId.Window, "settings.snapSdiMode", nameof(UiSettings.SnapSdiMode)),
        new SettingsEntry("snapAssist", SettingsCategoryId.Window, "settings.snapAssist", nameof(UiSettings.SnapAssistEnabled)),
        new SettingsEntry("snapAssistOnEdge", SettingsCategoryId.Window, "settings.snapAssistOnEdge", nameof(UiSettings.SnapAssistOnEdge)),
        new SettingsEntry("snapAssistThumbnails", SettingsCategoryId.Window, "settings.snapAssistThumbnails", nameof(UiSettings.SnapAssistThumbnails)),
        new SettingsEntry("snapAssistSdiGrace", SettingsCategoryId.Window, "settings.snapAssistSdiGrace", nameof(UiSettings.SnapAssistSdiGraceMs)),
        new SettingsEntry("escClose", SettingsCategoryId.Window, "menu.ws.escClose", nameof(UiSettings.EscClose)),
        new SettingsEntry("dblClose", SettingsCategoryId.Window, "menu.window.dblClose", null),
        new SettingsEntry("autosave", SettingsCategoryId.Window, "menu.ws.autosave", nameof(UiSettings.WindowAutoSave)),
        new SettingsEntry("capture", SettingsCategoryId.Window, "menu.ws.capture", nameof(UiSettings.CaptureProtection)),
        new SettingsEntry("tray", SettingsCategoryId.Window, "menu.window.tray", nameof(UiSettings.TrayEnabled)),
        new SettingsEntry("uiStyle", SettingsCategoryId.Window, "menu.view.uiStyle", nameof(UiSettings.UiStyle)) { OverrideSetting = nameof(UiSettings.UiStyleOverride) },
        new SettingsEntry("tabletMode", SettingsCategoryId.Window, "settings.tabletMode", nameof(UiSettings.TabletUiMode)) { OverrideSetting = nameof(UiSettings.TabletUiModeOverride) },
        new SettingsEntry("panelFeature", SettingsCategoryId.Window, "settings.panelFeature", nameof(UiSettings.PanelFeatureMode)),
        new SettingsEntry("compactWindowKeepsTablet", SettingsCategoryId.Window, "settings.compactWindowKeepsTablet", nameof(UiSettings.CompactWindowKeepsTablet)),
        new SettingsEntry("busyDelay", SettingsCategoryId.Window, "settings.busyDelay", nameof(UiSettings.BusyShowDelayMs)),
        new SettingsEntry("busyMinVisible", SettingsCategoryId.Window, "settings.busyMinVisible", nameof(UiSettings.BusyMinVisibleMs)),
        new SettingsEntry("prewarmViews", SettingsCategoryId.Window, "settings.prewarmViews", nameof(UiSettings.PrewarmViews)),
        new SettingsEntry("gridDeferCells", SettingsCategoryId.Window, "settings.gridDeferCells", nameof(UiSettings.GridDeferOffscreenCells)),
        new SettingsEntry("gridCompactCells", SettingsCategoryId.Window, "settings.gridCompactCells", nameof(UiSettings.GridCompactCells)),
        new SettingsEntry("gridDrawnCellText", SettingsCategoryId.Window, "settings.gridDrawnCellText", nameof(UiSettings.GridDrawnCellText)),
        new SettingsEntry("gridViewPool", SettingsCategoryId.Window, "settings.gridViewPool", nameof(UiSettings.GridViewPool)),
        new SettingsEntry("restoreViews", SettingsCategoryId.Window, "settings.restoreViews", nameof(UiSettings.RestoreViewCreation)),

        new SettingsEntry("language", SettingsCategoryId.Language, "lang.label", nameof(UiSettings.Language)),

        new SettingsEntry("layoutSave", SettingsCategoryId.Layout, "menu.ws.layoutSave", null),
        new SettingsEntry("layoutManage", SettingsCategoryId.Layout, "menu.ws.layoutManage", null),

        new SettingsEntry("gpu", SettingsCategoryId.System, "menu.sys.gpu", nameof(UiSettings.GpuMode)),
        new SettingsEntry("duplicate", SettingsCategoryId.System, "menu.sys.duplicate", nameof(UiSettings.AllowDuplicateInstance)),
        new SettingsEntry("monitor", SettingsCategoryId.System, "settings.monitor", nameof(UiSettings.SelectedMonitorId)),
        new SettingsEntry("fastDocSwitch", SettingsCategoryId.System, "settings.fastDocSwitch", nameof(UiSettings.FastDocumentSwitch)),
        new SettingsEntry("tabHeaderFirst", SettingsCategoryId.System, "settings.tabHeaderFirst", nameof(UiSettings.TabHeaderFirst)),
        new SettingsEntry("debug", SettingsCategoryId.System, "menu.sys.debug", nameof(UiSettings.DebugMode)),
        new SettingsEntry("logLevel", SettingsCategoryId.System, "menu.sys.log.level", nameof(UiSettings.LogMinLevel)),
    };

    /// <summary>검색어가 항목 이름(현재 언어)에 들어 있는가. 비교 규칙은 SettingsRow.Matches 와 같다.</summary>
    public static bool Matches(SettingsEntry entry, string? search) =>
        string.IsNullOrWhiteSpace(search)
        || Loc.Tr(entry.LabelKey).Contains(search.Trim(), StringComparison.CurrentCultureIgnoreCase);

    /// <summary>이 범주에 검색어와 맞는 항목이 있는가.</summary>
    public static bool CategoryMatches(SettingsCategoryId category, string? search) =>
        All.Any(e => e.Category == category && Matches(e, search));
}
