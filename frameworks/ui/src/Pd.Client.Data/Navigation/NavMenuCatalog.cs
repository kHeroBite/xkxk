// 모바일형 하단 탭·삼선 서랍이 쓰는 셸 메뉴 전체 카탈로그 — UI 비의존 순수 데이터 레이어(데스크톱 메뉴 AutomationId 1:1 대응표 포함)
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Pd.Client.Data.Navigation;

/// <summary>메뉴 묶음 구분(제목줄 상위 메뉴 4개).</summary>
public enum NavGroup
{
    File,
    Window,
    View,
    System,
}

/// <summary>
/// 카탈로그 항목 1개. Applicable=false 이면 모바일형에서 실행하지 않고 비활성+사유(ReasonKey)로 서랍에 둔다.
/// DesktopIds 는 이 항목이 대신하는 데스크톱 메뉴 AutomationId 들이다(도달률 판정용).
/// </summary>
public sealed record NavEntry(string Id, string TextKey, NavGroup Group, int Depth, bool IsGroup, string? Command, string? Param,
    int TabOrder, string? TabKey, string? Icon, bool Applicable, string? ReasonKey, string[] DesktopIds);

/// <summary>셸 메뉴 전수 + 하단 탭 배분 데이터. 탭을 바꾸려면 TabOrder 만 고친다(결정 12).</summary>
public static class NavMenuCatalog
{
    public const string ReasonPcTablet = "nav.reason.pcTablet";
    public const string ReasonUnavailable = "nav.reason.unavailable";

    // 하단 탭 아이콘(벡터 PathData, 18x18 안)
    private const string IconExplorer = "M1,3 H6 L8,5 H17 V15 H1 Z";
    private const string IconDocs = "M2,2 H12 V12 H2 Z M5,5 H15 V15 H5 Z";
    private const string IconBell = "M8,0 C5,0 3,2.5 3,5.5 V9 L1.5,11.5 V12.5 H14.5 V11.5 L13,9 V5.5 C13,2.5 11,0 8,0 Z M6,13.5 A2,2 0 0 0 10,13.5 Z";
    private const string IconSettings = "M1,3 H17 V5 H1 Z M6,1 H8 V7 H6 Z M1,12 H17 V14 H1 Z M11,10 H13 V16 H11 Z";

    /// <summary>하단 '더보기' 탭(항목이 아니라 서랍 열기).</summary>
    public const string MoreIcon = "M2,7 H5 V10 H2 Z M7.5,7 H10.5 V10 H7.5 Z M13,7 H16 V10 H13 Z";
    public const string MoreTabKey = "nav.tab.more";

    private static NavEntry G(NavGroup g, int depth, string id, string key, params string[] extra) =>
        new(id, key, g, depth, true, null, null, -1, null, null, true, null, Ids(id, extra));

    private static NavEntry I(NavGroup g, int depth, string id, string key, string cmd, string? param = null, params string[] extra) =>
        new(id, key, g, depth, false, cmd, param, -1, null, null, true, null, Ids(id, extra));

    // 모바일형에서 의미 없는 항목(창 배열·독립 창·모니터 등) — 숨기지 않고 비활성+사유
    private static NavEntry X(NavGroup g, int depth, string id, string key, bool isGroup = false, params string[] extra) =>
        new(id, key, g, depth, isGroup, null, null, -1, null, null, false, ReasonPcTablet, Ids(id, extra));

    private static NavEntry Tab(NavEntry e, int order, string tabKey, string icon) => e with { TabOrder = order, TabKey = tabKey, Icon = icon };

    private static string[] Ids(string id, string[] extra) =>
        new[] { id }.Concat(extra).Select(s => s.StartsWith("shell.", StringComparison.Ordinal) ? s : "shell.mnu" + s).ToArray();

    /// <summary>카탈로그 전체(서랍 표시 순서). 하단 탭은 TabOrder >= 0 인 항목.</summary>
    public static IReadOnlyList<NavEntry> Entries { get; } = Build();

    /// <summary>하단 탭(순서대로, '더보기'는 포함하지 않음).</summary>
    public static IReadOnlyList<NavEntry> Tabs => Entries.Where(e => e.TabOrder >= 0).OrderBy(e => e.TabOrder).ToList();

    private static List<NavEntry> Build()
    {
        const NavGroup F = NavGroup.File, W = NavGroup.Window, V = NavGroup.View, S = NavGroup.System;
        return new List<NavEntry>
        {
            // ── 파일 ──
            G(F, 0, "File", "menu.file"),
            I(F, 1, "OpenGrid", "menu.file.openGrid", "Open", "Grid"),
            I(F, 1, "OpenGantt", "menu.file.openGantt", "Open", "Gantt"),
            I(F, 1, "OpenForm", "menu.file.openForm", "Open", "Form"),
            X(F, 1, "OpenSdiGroup", "menu.file.openSdi", true),
            X(F, 2, "OpenGridSdi", "menu.file.openGridSdi"),
            X(F, 2, "OpenGanttSdi", "menu.file.openGanttSdi"),
            X(F, 2, "OpenFormSdi", "menu.file.openFormSdi"),
            I(F, 1, "OpenBench", "menu.file.openBench", "Bench", "grid"),
            I(F, 1, "SaveLayout", "menu.file.saveLayout", "SaveLayout"),
            I(F, 1, "LoadLayout", "menu.file.loadLayout", "LoadLayout"),
            I(F, 1, "LayoutSave", "menu.ws.layoutSave", "LayoutSave"),
            I(F, 1, "LayoutManage", "menu.ws.layoutManage", "LayoutManage"),
            X(F, 1, "Exit", "menu.file.exit"),

            // ── 창 ──
            G(W, 0, "Window", "menu.window"),
            X(W, 1, "ArrangeCascade", "menu.window.cascade"),
            X(W, 1, "ArrangeTileH", "menu.window.tileH"),
            X(W, 1, "ArrangeTile", "menu.window.tileV"),
            X(W, 1, "ArrangeTabbed", "menu.window.tabbed"),
            X(W, 1, "Minimize", "menu.window.minimize"),
            X(W, 1, "Maximize", "menu.window.maximizeToggle"),
            X(W, 1, "Restore", "menu.window.restore"),
            I(W, 1, "Pin", "menu.window.pin", "Pin"),
            X(W, 1, "Rename", "menu.window.rename"),
            I(W, 1, "ResetLayout", "menu.window.resetLayout", "ResetLayout"),
            X(W, 1, "MinimizeAll", "menu.window.minimizeAll"),
            X(W, 1, "RestoreAll", "menu.window.restoreAll"),
            X(W, 1, "RestoreLastMin", "menu.window.restoreLastMin"),
            I(W, 1, "Next", "menu.window.next", "Next"),
            I(W, 1, "Prev", "menu.window.prev", "Prev"),
            X(W, 1, "SdiGroup", "menu.window.sdiGroup", true, "ToggleSdi", "Float", "Redock", "SdiResetPos", "SdiMinimize", "SdiRestore", "SdiCloseAll"),
            X(W, 1, "Monitor", "menu.window.monitor", true, "WinSingleMonitor", "WinMultiMonitor", "WinMonitorNone", "WinMonitorMaximize"),
            X(W, 1, "WinResetMain", "menu.ws.resetMain"),
            I(W, 1, "Close", "menu.window.close", "CloseDoc"),
            I(W, 1, "CloseOthers", "menu.window.closeOthers", "CloseOthers"),
            I(W, 1, "CloseAll", "menu.window.closeAll", "CloseAll"),
            X(W, 1, "Flash", "menu.window.flash"),
            // 열린 창 목록 1~9·더 보기 → 하단 '문서' 탭(열린 문서 목록)
            Tab(I(W, 1, "WinList1", "nav.tab.docs", "Panel", "docs", "WinList2", "WinList3", "WinList4", "WinList5", "WinList6", "WinList7", "WinList8", "WinList9", "WinListMore"), 1, "nav.tab.docs", IconDocs),

            // ── 보기 ──
            G(V, 0, "View", "menu.view"),
            X(V, 1, "PresetCascade", "menu.view.presetCascade"),
            X(V, 1, "PresetTabbed", "menu.view.presetTabbed"),
            X(V, 1, "PresetTile", "menu.view.presetTile"),
            Tab(I(V, 1, "ShowExplorer", "menu.view.showExplorer", "Panel", "explorer"), 0, "nav.tab.explorer", IconExplorer),
            X(V, 1, "ShowLog", "menu.view.showLog"),
            Tab(I(V, 1, "ShowNotifications", "menu.view.showNotifications", "Panel", "notify"), 2, "nav.tab.notify", IconBell),
            I(V, 1, "FontReset", "menu.view.fontReset", "FontReset"),
            I(V, 1, "ToggleTheme", "menu.view.toggleTheme", "ToggleTheme"),
            // 요건 7: 값 설정은 서랍에 두지 않고 설정 시트로 모은다(데스크톱 시스템 메뉴의 설정 항목도 여기로 도달)
            Tab(I(V, 1, "Settings", "menu.view.settings", "Panel", "settings", "SysSettings"), 3, "nav.tab.settings", IconSettings),
            X(V, 1, "NextTheme", "menu.view.nextTheme"),

            // ── 시스템 ──
            G(S, 0, "Sys", "menu.sys"),
            I(S, 1, "SysGpuRestart", "sys.restartRequired.title", "SystemView", "RestartRequired"),
            G(S, 1, "SysMonitor", "menu.sys.monitor"),
            I(S, 2, "SysMonitorApp", "menu.sys.monitor.app", "SystemView", "AppMonitor"),
            G(S, 1, "SysLog", "menu.sys.log"),
            I(S, 2, "SysLogView", "menu.sys.log.view", "SystemView", "LogView"),
            I(S, 2, "SysLogFolder", "menu.sys.log.openFolder", "SystemView", "LogFolder"),
            I(S, 2, "SysLogExport", "menu.sys.log.export", "SystemView", "LogExport"),
            I(S, 1, "SysInquiry", "menu.sys.inquiry", "SystemView", "Inquiry"),
            I(S, 1, "SysInquiryList", "menu.sys.inquiryList", "SystemView", "InquiryList"),
            I(S, 1, "SysUpdate", "menu.sys.update", "SystemView", "Update"),
            I(S, 1, "SysChangelog", "menu.sys.changelog", "SystemView", "Changelog"),
            I(S, 1, "SysHelp", "menu.sys.help", "SystemView", "Help"),
            I(S, 1, "SysAbout", "menu.sys.about", "SystemView", "About"),
        };
    }

    /// <summary>정적 덤프(JSON) — 판정기·원격 진단 nav.menu 의 기본값. enabled 는 Applicable 기준(런타임 상태는 MobileNavViewModel.ToJson).</summary>
    public static string ToJson(Func<NavEntry, (bool Enabled, string? Reason)>? state = null)
    {
        var sb = new StringBuilder("{\"tabs\":[");
        sb.Append(string.Join(",", Tabs.Select(t => "\"" + t.Id + "\"")));
        sb.Append("],\"drawer\":[");
        sb.Append(string.Join(",", Entries.Select(e =>
        {
            var (en, why) = state?.Invoke(e) ?? (e.Applicable, e.Applicable ? null : e.ReasonKey);
            return "{\"id\":\"" + e.Id + "\",\"group\":" + (e.IsGroup ? "true" : "false") + ",\"enabled\":" + (en ? "true" : "false")
                + ",\"reason\":" + (why is null ? "null" : "\"" + why + "\"")
                + ",\"desktopIds\":[" + string.Join(",", e.DesktopIds.Select(d => "\"" + d + "\"")) + "]}";
        })));
        return sb.Append("]}").ToString();
    }
}
