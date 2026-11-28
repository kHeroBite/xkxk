// 셸 4메뉴(파일·창·보기·시스템)의 유일한 출처 표 — UI 비의존 순수 데이터(행 1줄 = 항목 1개, 리터럴 AutomationId, 모바일형 투영 힌트 포함)
using System;
using System.Collections.Generic;

namespace xkxk.frameworks.ui.data.Navigation;

/// <summary>셸 메뉴 행 종류. 값 순서는 프레임워크 메뉴 종류(Command·Submenu·Separator·Section)와 같다.</summary>
public enum ShellMenuRowKind
{
    Command,
    Submenu,
    Separator,
    Section,
}

/// <summary>
/// 모바일형(하단 탭·삼선 서랍) 투영 힌트. InDrawer=false 면 서랍에 넣지 않는다(PC·태블릿 하위 항목을 묶음이 대신할 때).
/// Available=false 면 서랍에 비활성+사유(ReasonKey)로 둔다. Covers 는 이 항목이 대신하는 다른 데스크톱 AutomationId(shell. 로 시작하지 않으면 shell.mnu 접두)다.
/// </summary>
public sealed record ShellMenuMobileRow(bool InDrawer, bool Available, string? ReasonKey, string? EntryId, string? TitleKey, string? CommandId, string? Param,
    int TabOrder, string? TabKey, string? TabIcon, string[] Covers);

/// <summary>
/// 셸 메뉴 행 1개. CommandId 는 레지스트리 명령 ID("xkxk.…" 리터럴), VisibleWhen·ToolTipWhen 은 조건 키 문자열이다
/// (표시: cmd(행의 CommandId 가 명령 레지스트리에 등록돼 있을 때만 보임 — Submenu 는 자식 Command 행 중 하나라도 등록돼 있으면 보임)·bench·gpuRestartPending·logTool·desktopWindow·monitors·noMonitors, 툴팁: snap·singleMonitor·monitorMaximize·resetMain·logFolder). 실제 대리자는 셸이 붙인다.
/// </summary>
public sealed record ShellMenuRow(string Id, string? Parent, ShellMenuRowKind Kind, string? CommandId, string? Param, string TitleKey, string? Icon,
    string? AutomationId, string? Section, string? VisibleWhen, string? ToolTipWhen, ShellMenuMobileRow Mobile);

/// <summary>셸 4메뉴 표. 행 순서 = 형제 순서(트리 깊이 우선). 탭을 바꾸려면 모바일 값의 TabOrder 만 고친다.</summary>
public static class ShellMenuTable
{
    // 하단 탭 아이콘(벡터 PathData, 18x18 안)
    private const string IconExplorer = "M1,3 H6 L8,5 H17 V15 H1 Z";
    private const string IconDocs = "M2,2 H12 V12 H2 Z M5,5 H15 V15 H5 Z";
    private const string IconBell = "M8,0 C5,0 3,2.5 3,5.5 V9 L1.5,11.5 V12.5 H14.5 V11.5 L13,9 V5.5 C13,2.5 11,0 8,0 Z M6,13.5 A2,2 0 0 0 10,13.5 Z";
    private const string IconSettings = "M1,3 H17 V5 H1 Z M6,1 H8 V7 H6 Z M1,12 H17 V14 H1 Z M11,10 H13 V16 H11 Z";

    // 서랍 항목(실행 가능) · PC·태블릿 전용(비활성+사유) · 서랍 밖(묶음이 대신하거나 모바일에서 뜻이 없음)
    private static readonly ShellMenuMobileRow Drawer = new(true, true, null, null, null, null, null, -1, null, null, Array.Empty<string>());
    private static readonly ShellMenuMobileRow PcOnly = new(true, false, NavMenuCatalog.ReasonPcTablet, null, null, null, null, -1, null, null, Array.Empty<string>());
    private static readonly ShellMenuMobileRow Hidden = new(false, true, null, null, null, null, null, -1, null, null, Array.Empty<string>());

    /// <summary>셸 메뉴 전체 행(등록 순서). 읽기 전용이다.</summary>
    public static IReadOnlyList<ShellMenuRow> Rows { get; } = Array.AsReadOnly(new ShellMenuRow[]
    {
        // ── 파일 ──
        new("xkxk.file", null, ShellMenuRowKind.Submenu, null, null, "menu.file", null, "shell.mnuFile", null, null, null, Drawer),
        new("xkxk.file.sep1", "xkxk.file", ShellMenuRowKind.Separator, null, null, "", null, null, null, null, null, Hidden),
        new("xkxk.file.saveLayout", "xkxk.file", ShellMenuRowKind.Command, "xkxk.layout.save", null, "menu.file.saveLayout", CmdIconKeys.SaveLayout, "shell.mnuSaveLayout", null, null, null, Drawer),
        new("xkxk.file.loadLayout", "xkxk.file", ShellMenuRowKind.Command, "xkxk.layout.load", null, "menu.file.loadLayout", CmdIconKeys.LoadLayout, "shell.mnuLoadLayout", null, null, null, Drawer),
        new("xkxk.file.layoutSave", "xkxk.file", ShellMenuRowKind.Command, "xkxk.layout.saveCurrent", null, "menu.ws.layoutSave", CmdIconKeys.LayoutSave, "shell.mnuLayoutSave", null, null, null, Drawer),
        new("xkxk.file.layoutManage", "xkxk.file", ShellMenuRowKind.Command, "xkxk.layout.manage", null, "menu.ws.layoutManage", CmdIconKeys.LayoutManage, "shell.mnuLayoutManage", null, null, null, Drawer),
        new("xkxk.file.sep2", "xkxk.file", ShellMenuRowKind.Separator, null, null, "", null, null, null, "desktopWindow", null, Hidden),
        new("xkxk.file.exit", "xkxk.file", ShellMenuRowKind.Command, "xkxk.app.exit", null, "menu.file.exit", CmdIconKeys.Exit, "shell.mnuExit", null, "desktopWindow", null, PcOnly),

        // ── 창 ──
        new("xkxk.window", null, ShellMenuRowKind.Submenu, null, null, "menu.window", null, "shell.mnuWindow", null, null, null, Drawer),
        new("xkxk.window.cascade", "xkxk.window", ShellMenuRowKind.Command, "xkxk.window.cascade", null, "menu.window.cascade", CmdIconKeys.Cascade, "shell.mnuArrangeCascade", null, null, null, PcOnly),
        new("xkxk.window.tileHorizontal", "xkxk.window", ShellMenuRowKind.Command, "xkxk.window.tileHorizontal", null, "menu.window.tileH", CmdIconKeys.TileH, "shell.mnuArrangeTileH", null, null, null, PcOnly),
        new("xkxk.window.tileVertical", "xkxk.window", ShellMenuRowKind.Command, "xkxk.window.tileVertical", null, "menu.window.tileV", CmdIconKeys.TileV, "shell.mnuArrangeTile", null, null, null, PcOnly),
        new("xkxk.window.tabbed", "xkxk.window", ShellMenuRowKind.Command, "xkxk.window.tabbed", null, "menu.window.tabbed", CmdIconKeys.Tabbed, "shell.mnuArrangeTabbed", null, null, null, PcOnly),
        new("xkxk.window.snap", "xkxk.window", ShellMenuRowKind.Submenu, null, null, "menu.window.snap", CmdIconKeys.Snap, "shell.mnuWinSnap", null, null, "snap", Hidden),
        new("xkxk.window.snap.cells", "xkxk.window.snap", ShellMenuRowKind.Section, null, null, "", null, null, "xkxk.window.snap", null, null, Hidden),
        new("xkxk.window.sep1", "xkxk.window", ShellMenuRowKind.Separator, null, null, "", null, null, null, null, null, Hidden),
        new("xkxk.window.minimize", "xkxk.window", ShellMenuRowKind.Command, "xkxk.window.minimize", null, "menu.window.minimize", CmdIconKeys.Minimize, "shell.mnuMinimize", null, null, null, PcOnly),
        new("xkxk.window.maximize", "xkxk.window", ShellMenuRowKind.Command, "xkxk.window.maximizeToggle", null, "menu.window.maximizeToggle", CmdIconKeys.Maximize, "shell.mnuMaximize", null, null, null, PcOnly),
        new("xkxk.window.restore", "xkxk.window", ShellMenuRowKind.Command, "xkxk.window.restore", null, "menu.window.restore", CmdIconKeys.Restore, "shell.mnuRestore", null, null, null, PcOnly),
        new("xkxk.window.pin", "xkxk.window", ShellMenuRowKind.Command, "xkxk.window.pin", null, "menu.window.pin", CmdIconKeys.Pin, "shell.mnuPin", null, null, null, Drawer),
        new("xkxk.window.rename", "xkxk.window", ShellMenuRowKind.Command, "xkxk.window.rename", null, "menu.window.rename", CmdIconKeys.Rename, "shell.mnuRename", null, null, null, PcOnly),
        new("xkxk.window.favoriteToggle", "xkxk.window", ShellMenuRowKind.Command, "xkxk.favorite.toggle", null, "favorite.toggle", "PdShellIconFavorite", "shell.mnuFavoriteToggle", null, null, null, Hidden),
        new("xkxk.window.resetLayout", "xkxk.window", ShellMenuRowKind.Command, "xkxk.window.resetLayout", null, "menu.window.resetLayout", CmdIconKeys.ResetLayout, "shell.mnuResetLayout", null, null, null, Drawer),
        new("xkxk.window.sep2", "xkxk.window", ShellMenuRowKind.Separator, null, null, "", null, null, null, null, null, Hidden),
        new("xkxk.window.minimizeAll", "xkxk.window", ShellMenuRowKind.Command, "xkxk.window.minimizeAll", null, "menu.window.minimizeAll", CmdIconKeys.MinimizeAll, "shell.mnuMinimizeAll", null, null, null, PcOnly),
        new("xkxk.window.restoreAll", "xkxk.window", ShellMenuRowKind.Command, "xkxk.window.restoreAll", null, "menu.window.restoreAll", CmdIconKeys.RestoreAll, "shell.mnuRestoreAll", null, null, null, PcOnly),
        new("xkxk.window.restoreLastMin", "xkxk.window", ShellMenuRowKind.Command, "xkxk.window.restoreLastMinimized", null, "menu.window.restoreLastMin", CmdIconKeys.RestoreLastMin, "shell.mnuRestoreLastMin", null, null, null, PcOnly),
        new("xkxk.window.sep3", "xkxk.window", ShellMenuRowKind.Separator, null, null, "", null, null, null, null, null, Hidden),
        new("xkxk.window.next", "xkxk.window", ShellMenuRowKind.Command, "xkxk.window.next", null, "menu.window.next", CmdIconKeys.Next, "shell.mnuNext", null, null, null, Drawer),
        new("xkxk.window.prev", "xkxk.window", ShellMenuRowKind.Command, "xkxk.window.previous", null, "menu.window.prev", CmdIconKeys.Prev, "shell.mnuPrev", null, null, null, Drawer),
        new("xkxk.window.sep4", "xkxk.window", ShellMenuRowKind.Separator, null, null, "", null, null, null, null, null, Hidden),
        new("xkxk.window.sdiGroup", "xkxk.window", ShellMenuRowKind.Submenu, null, null, "menu.window.sdiGroup", CmdIconKeys.SdiGroup, "shell.mnuSdiGroup", null, null, null, new ShellMenuMobileRow(true, false, NavMenuCatalog.ReasonPcTablet, null, null, null, null, -1, null, null, new[] { "ToggleSdi", "Float", "Redock", "SdiResetPos", "SdiMinimize", "SdiRestore", "SdiCloseAll" })),
        new("xkxk.window.toggleSdi", "xkxk.window.sdiGroup", ShellMenuRowKind.Command, "xkxk.window.toggleSdi", null, "menu.window.toggleSdi", CmdIconKeys.ToggleSdi, "shell.mnuToggleSdi", null, null, null, Hidden),
        new("xkxk.window.float", "xkxk.window.sdiGroup", ShellMenuRowKind.Command, "xkxk.window.float", null, "menu.window.float", CmdIconKeys.Float, "shell.mnuFloat", null, null, null, Hidden),
        new("xkxk.window.redock", "xkxk.window.sdiGroup", ShellMenuRowKind.Command, "xkxk.window.redock", null, "menu.window.redock", CmdIconKeys.Redock, "shell.mnuRedock", null, null, null, Hidden),
        new("xkxk.window.sep5", "xkxk.window.sdiGroup", ShellMenuRowKind.Separator, null, null, "", null, null, null, null, null, Hidden),
        new("xkxk.window.sdiResetPos", "xkxk.window.sdiGroup", ShellMenuRowKind.Command, "xkxk.sdi.resetPositions", null, "menu.ws.sdiResetPos", CmdIconKeys.SdiResetPos, "shell.mnuSdiResetPos", null, null, null, Hidden),
        new("xkxk.window.sdiMinimize", "xkxk.window.sdiGroup", ShellMenuRowKind.Command, "xkxk.sdi.minimizeAll", null, "menu.ws.sdiMinimize", CmdIconKeys.SdiMinimize, "shell.mnuSdiMinimize", null, null, null, Hidden),
        new("xkxk.window.sdiRestore", "xkxk.window.sdiGroup", ShellMenuRowKind.Command, "xkxk.sdi.restoreAll", null, "menu.ws.sdiRestore", CmdIconKeys.SdiRestore, "shell.mnuSdiRestore", null, null, null, Hidden),
        new("xkxk.window.sdiCloseAll", "xkxk.window.sdiGroup", ShellMenuRowKind.Command, "xkxk.sdi.closeAll", null, "menu.ws.sdiCloseAll", CmdIconKeys.SdiCloseAll, "shell.mnuSdiCloseAll", null, null, null, Hidden),
        new("xkxk.window.monitor", "xkxk.window", ShellMenuRowKind.Submenu, null, null, "menu.window.monitor", CmdIconKeys.Monitor, "shell.mnuMonitor", null, null, null, new ShellMenuMobileRow(true, false, NavMenuCatalog.ReasonPcTablet, null, null, null, null, -1, null, null, new[] { "WinSingleMonitor", "WinMultiMonitor", "WinMonitorNone", "WinMonitorMaximize" })),
        new("xkxk.window.singleMonitor", "xkxk.window.monitor", ShellMenuRowKind.Command, "xkxk.monitor.single", null, "menu.ws.singleMonitor", CmdIconKeys.SingleMonitor, "shell.mnuWinSingleMonitor", null, null, "singleMonitor", Hidden),
        new("xkxk.window.monitor.list", "xkxk.window.monitor", ShellMenuRowKind.Section, null, null, "", null, null, "xkxk.window.monitor.list", null, null, Hidden),
        new("xkxk.window.sep6", "xkxk.window.monitor", ShellMenuRowKind.Separator, null, null, "", null, null, null, null, null, Hidden),
        new("xkxk.window.monitorMaximize", "xkxk.window.monitor", ShellMenuRowKind.Command, "xkxk.monitor.maximizeSelected", null, "menu.monitor.maximize", CmdIconKeys.MonitorMaximize, "shell.mnuWinMonitorMaximize", null, null, "monitorMaximize", Hidden),
        new("xkxk.window.resetMain", "xkxk.window", ShellMenuRowKind.Command, "xkxk.mainWindow.reset", null, "menu.ws.resetMain", CmdIconKeys.ResetMain, "shell.mnuWinResetMain", null, null, "resetMain", PcOnly),
        new("xkxk.window.sep7", "xkxk.window", ShellMenuRowKind.Separator, null, null, "", null, null, null, null, null, Hidden),
        new("xkxk.window.overview", "xkxk.window", ShellMenuRowKind.Command, "xkxk.window.overview", null, "menu.window.overview", CmdIconKeys.Overview, "shell.mnuOverview", null, null, null, Hidden),
        new("xkxk.window.close", "xkxk.window", ShellMenuRowKind.Command, "xkxk.window.close", null, "menu.window.close", CmdIconKeys.Close, "shell.mnuClose", null, null, null, Drawer),
        new("xkxk.window.closeOthers", "xkxk.window", ShellMenuRowKind.Command, "xkxk.window.closeOthers", null, "menu.window.closeOthers", CmdIconKeys.CloseOthers, "shell.mnuCloseOthers", null, null, null, Drawer),
        new("xkxk.window.closeAll", "xkxk.window", ShellMenuRowKind.Command, "xkxk.window.closeAll", null, "menu.window.closeAll", CmdIconKeys.CloseAll, "shell.mnuCloseAll", null, null, null, Drawer),
        new("xkxk.window.sep8", "xkxk.window", ShellMenuRowKind.Separator, null, null, "", null, null, null, null, null, Hidden),
        new("xkxk.window.flash", "xkxk.window", ShellMenuRowKind.Command, "xkxk.window.flash", null, "menu.window.flash", CmdIconKeys.Flash, "shell.mnuFlash", null, null, null, PcOnly),
        new("xkxk.window.openList", "xkxk.window", ShellMenuRowKind.Section, null, null, "", null, null, "xkxk.window.openList", null, null, new ShellMenuMobileRow(true, true, null, "WinList1", "nav.tab.docs", "xkxk.nav.openPanel", "docs", 1, "nav.tab.docs", IconDocs, new[] { "WinList2", "WinList3", "WinList4", "WinList5", "WinList6", "WinList7", "WinList8", "WinList9", "WinListMore" })),

        // ── 보기 ──
        new("xkxk.view", null, ShellMenuRowKind.Submenu, null, null, "menu.view", null, "shell.mnuView", null, null, null, Drawer),
        new("xkxk.view.presetCascade", "xkxk.view", ShellMenuRowKind.Command, "xkxk.layout.preset.mdiCascade", null, "menu.view.presetCascade", CmdIconKeys.Cascade, "shell.mnuPresetCascade", null, null, null, PcOnly),
        new("xkxk.view.presetTabbed", "xkxk.view", ShellMenuRowKind.Command, "xkxk.layout.preset.tabbed", null, "menu.view.presetTabbed", CmdIconKeys.Tabbed, "shell.mnuPresetTabbed", null, null, null, PcOnly),
        new("xkxk.view.presetTile", "xkxk.view", ShellMenuRowKind.Command, "xkxk.layout.preset.mdiTileVertical", null, "menu.view.presetTile", CmdIconKeys.TileV, "shell.mnuPresetTile", null, null, null, PcOnly),
        new("xkxk.view.sep1", "xkxk.view", ShellMenuRowKind.Separator, null, null, "", null, null, null, null, null, Hidden),
        new("xkxk.view.showExplorer", "xkxk.view", ShellMenuRowKind.Command, "xkxk.view.showExplorer", null, "menu.view.showExplorer", "PdShellIconExplorer", "shell.mnuShowExplorer", null, null, null, new ShellMenuMobileRow(true, true, null, null, null, "xkxk.nav.openPanel", "explorer", 0, "nav.tab.explorer", IconExplorer, Array.Empty<string>())),
        new("xkxk.view.showLog", "xkxk.view", ShellMenuRowKind.Command, "xkxk.view.showLog", null, "menu.view.showLog", "PdShellIconLog", "shell.mnuShowLog", null, "logTool", null, PcOnly),
        new("xkxk.view.showNotifications", "xkxk.view", ShellMenuRowKind.Command, "xkxk.view.showNotifications", null, "menu.view.showNotifications", "PdShellIconNotify", "shell.mnuShowNotifications", null, null, null, new ShellMenuMobileRow(true, true, null, null, null, "xkxk.nav.openPanel", "notify", 2, "nav.tab.notify", IconBell, Array.Empty<string>())),
        new("xkxk.view.sep2", "xkxk.view", ShellMenuRowKind.Separator, null, null, "", null, null, null, null, null, Hidden),
        new("xkxk.view.fontReset", "xkxk.view", ShellMenuRowKind.Command, "xkxk.view.fontReset", null, "menu.view.fontReset", CmdIconKeys.FontReset, "shell.mnuFontReset", null, null, null, Drawer),
        new("xkxk.view.toggleTheme", "xkxk.view", ShellMenuRowKind.Command, "xkxk.theme.toggle", null, "menu.view.toggleTheme", CmdIconKeys.ToggleTheme, "shell.mnuToggleTheme", null, null, null, Drawer),
        new("xkxk.view.settings", "xkxk.view", ShellMenuRowKind.Command, "xkxk.system.settings", null, "menu.view.settings", "PdShellIconSettings", "shell.mnuSettings", null, null, null, new ShellMenuMobileRow(true, true, null, null, null, "xkxk.nav.openPanel", "settings", 3, "nav.tab.settings", IconSettings, new[] { "SysSettings" })),
        new("xkxk.view.nextTheme", "xkxk.view", ShellMenuRowKind.Command, "xkxk.theme.next", null, "menu.view.nextTheme", CmdIconKeys.NextTheme, "shell.mnuNextTheme", null, null, null, PcOnly),

        // ── 시스템 ──
        new("xkxk.system", null, ShellMenuRowKind.Submenu, null, null, "menu.sys", null, "shell.mnuSys", null, null, null, Drawer),
        new("xkxk.system.settings", "xkxk.system", ShellMenuRowKind.Command, "xkxk.system.settings", null, "menu.sys.settings", "PdShellIconSettings", "shell.mnuSysSettings", null, null, null, Hidden),
        new("xkxk.system.gpuRestart", "xkxk.system", ShellMenuRowKind.Command, "xkxk.system.restartRequired", null, "sys.restartRequired.title", CmdIconKeys.GpuRestart, "shell.mnuSysGpuRestart", null, "gpuRestartPending", null, Drawer),
        new("xkxk.system.sep1", "xkxk.system", ShellMenuRowKind.Separator, null, null, "", null, null, null, null, null, Hidden),
        new("xkxk.system.monitor", "xkxk.system", ShellMenuRowKind.Submenu, null, null, "menu.sys.monitor", CmdIconKeys.SysMonitor, "shell.mnuSysMonitor", null, null, null, Drawer),
        new("xkxk.system.monitorApp", "xkxk.system.monitor", ShellMenuRowKind.Command, "xkxk.system.appMonitor", null, "menu.sys.monitor.app", CmdIconKeys.AppMonitor, "shell.mnuSysMonitorApp", null, null, null, Drawer),
        new("xkxk.system.log", "xkxk.system", ShellMenuRowKind.Submenu, null, null, "menu.sys.log", CmdIconKeys.LogGroup, "shell.mnuSysLog", null, null, null, Drawer),
        new("xkxk.system.logView", "xkxk.system.log", ShellMenuRowKind.Command, "xkxk.system.logView", null, "menu.sys.log.view", "PdShellIconLog", "shell.mnuSysLogView", null, null, null, Drawer),
        new("xkxk.system.logFolder", "xkxk.system.log", ShellMenuRowKind.Command, "xkxk.system.logFolder", null, "menu.sys.log.openFolder", CmdIconKeys.LogFolder, "shell.mnuSysLogFolder", null, null, "logFolder", Drawer),
        new("xkxk.system.logExport", "xkxk.system.log", ShellMenuRowKind.Command, "xkxk.system.logExport", null, "menu.sys.log.export", CmdIconKeys.LogExport, "shell.mnuSysLogExport", null, null, null, Drawer),
        new("xkxk.system.sep2", "xkxk.system", ShellMenuRowKind.Separator, null, null, "", null, null, null, null, null, Hidden),
        new("xkxk.system.inquiry", "xkxk.system", ShellMenuRowKind.Command, "xkxk.system.inquiry", null, "menu.sys.inquiry", CmdIconKeys.Inquiry, "shell.mnuSysInquiry", null, null, null, Drawer),
        new("xkxk.system.inquiryList", "xkxk.system", ShellMenuRowKind.Command, "xkxk.system.inquiryList", null, "menu.sys.inquiryList", CmdIconKeys.InquiryList, "shell.mnuSysInquiryList", null, null, null, Drawer),
        new("xkxk.system.sep3", "xkxk.system", ShellMenuRowKind.Separator, null, null, "", null, null, null, null, null, Hidden),
        new("xkxk.system.update", "xkxk.system", ShellMenuRowKind.Command, "xkxk.system.update", null, "menu.sys.update", CmdIconKeys.Update, "shell.mnuSysUpdate", null, null, null, Drawer),
        new("xkxk.system.changelog", "xkxk.system", ShellMenuRowKind.Command, "xkxk.system.changelog", null, "menu.sys.changelog", CmdIconKeys.Changelog, "shell.mnuSysChangelog", null, null, null, Drawer),
        new("xkxk.system.help", "xkxk.system", ShellMenuRowKind.Command, "xkxk.system.help", null, "menu.sys.help", "PdShellIconHelp", "shell.mnuSysHelp", null, null, null, Drawer),
        new("xkxk.system.about", "xkxk.system", ShellMenuRowKind.Command, "xkxk.system.about", null, "menu.sys.about", CmdIconKeys.About, "shell.mnuSysAbout", null, null, null, Drawer),
    });
}
