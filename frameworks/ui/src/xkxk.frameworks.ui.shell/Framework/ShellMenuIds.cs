// 셸 기본 메뉴 ID 상수(구분선·섹션 포함)와 최상위·전체 목록 — 값은 바꾸지 않고 추가만 한다(UADR-0009 원칙 5, 정본 표 ShellMenuTable)
using System;
using System.Collections.Generic;
using System.Linq;
using xkxk.frameworks.ui.data.Navigation;

namespace xkxk.frameworks.ui.shell.Framework;

/// <summary>
/// 프레임워크가 <see cref="IUiMenuRegistry"/> 에 기본으로 등록하는 셸 메뉴(파일·창·보기·시스템)의 ID 다.
/// </summary>
/// <remarks>
/// ID 규칙은 xkxk.&lt;루트&gt;.&lt;이름&gt; 이고 구분선은 xkxk.&lt;루트&gt;.sep&lt;n&gt; 이다. 모든 값은 예약 접두어 <see cref="UiRegistryOwners.ReservedPrefix"/> 로 시작한다.
/// 소비자는 이 ID 를 Parent·InsertAfter·InsertBefore 앵커로 쓰거나 Move·SetHidden·Replace 로 바꿀 수 있다.
/// 명령 ID(<see cref="ShellCommandIds"/>)와 같은 문자열이어도 레지스트리가 달라 충돌이 아니다.
/// 확장 규칙: 새 셸 메뉴는 상수를 추가하고 표 끝 쪽에 덧붙인다. 기존 상수의 이름·값은 바꾸지 않는다.
/// </remarks>
public static class ShellMenuIds
{
    /// <summary>섹션 키 — 스냅 배치 6종의 칸(자동화 이름 shell.mnuWinSnap…).</summary>
    public const string SectionSnap = "xkxk.window.snap";

    /// <summary>섹션 키 — 모니터 라디오 목록과 없음 안내(shell.mnuWinMultiMonitor·shell.mnuWinMonitorNone).</summary>
    public const string SectionMonitors = "xkxk.window.monitor.list";

    /// <summary>섹션 키 — 열린 창 칸 1~9 와 더 보기(shell.mnuWinList1~9·shell.mnuWinListMore).</summary>
    public const string SectionOpenWindows = "xkxk.window.openList";

    /// <summary>최상위 메뉴(shell.mnuFile).</summary>
    public const string File = "xkxk.file";
    /// <summary>명령 항목(shell.mnuOpenGrid).</summary>
    public const string FileOpenGrid = "xkxk.file.openGrid";
    /// <summary>명령 항목(shell.mnuOpenGantt).</summary>
    public const string FileOpenGantt = "xkxk.file.openGantt";
    /// <summary>명령 항목(shell.mnuOpenForm).</summary>
    public const string FileOpenForm = "xkxk.file.openForm";
    /// <summary>하위 메뉴 항목(shell.mnuOpenSdiGroup).</summary>
    public const string FileOpenSdiGroup = "xkxk.file.openSdiGroup";
    /// <summary>명령 항목(shell.mnuOpenGridSdi).</summary>
    public const string FileOpenGridSdi = "xkxk.file.openGridSdi";
    /// <summary>명령 항목(shell.mnuOpenGanttSdi).</summary>
    public const string FileOpenGanttSdi = "xkxk.file.openGanttSdi";
    /// <summary>명령 항목(shell.mnuOpenFormSdi).</summary>
    public const string FileOpenFormSdi = "xkxk.file.openFormSdi";
    /// <summary>명령 항목(shell.mnuOpenBench).</summary>
    public const string FileOpenBench = "xkxk.file.openBench";
    /// <summary>구분선.</summary>
    public const string FileSep1 = "xkxk.file.sep1";
    /// <summary>명령 항목(shell.mnuSaveLayout).</summary>
    public const string FileSaveLayout = "xkxk.file.saveLayout";
    /// <summary>명령 항목(shell.mnuLoadLayout).</summary>
    public const string FileLoadLayout = "xkxk.file.loadLayout";
    /// <summary>명령 항목(shell.mnuLayoutSave).</summary>
    public const string FileLayoutSave = "xkxk.file.layoutSave";
    /// <summary>명령 항목(shell.mnuLayoutManage).</summary>
    public const string FileLayoutManage = "xkxk.file.layoutManage";
    /// <summary>구분선.</summary>
    public const string FileSep2 = "xkxk.file.sep2";
    /// <summary>명령 항목(shell.mnuExit).</summary>
    public const string FileExit = "xkxk.file.exit";
    /// <summary>최상위 메뉴(shell.mnuWindow).</summary>
    public const string Window = "xkxk.window";
    /// <summary>명령 항목(shell.mnuArrangeCascade).</summary>
    public const string WindowCascade = "xkxk.window.cascade";
    /// <summary>명령 항목(shell.mnuArrangeTileH).</summary>
    public const string WindowTileHorizontal = "xkxk.window.tileHorizontal";
    /// <summary>명령 항목(shell.mnuArrangeTile).</summary>
    public const string WindowTileVertical = "xkxk.window.tileVertical";
    /// <summary>명령 항목(shell.mnuArrangeTabbed).</summary>
    public const string WindowTabbed = "xkxk.window.tabbed";
    /// <summary>하위 메뉴 항목(shell.mnuWinSnap).</summary>
    public const string WindowSnap = "xkxk.window.snap";
    /// <summary>섹션 자리(키 xkxk.window.snap).</summary>
    public const string WindowSnapCells = "xkxk.window.snap.cells";
    /// <summary>구분선.</summary>
    public const string WindowSep1 = "xkxk.window.sep1";
    /// <summary>명령 항목(shell.mnuMinimize).</summary>
    public const string WindowMinimize = "xkxk.window.minimize";
    /// <summary>명령 항목(shell.mnuMaximize).</summary>
    public const string WindowMaximize = "xkxk.window.maximize";
    /// <summary>명령 항목(shell.mnuRestore).</summary>
    public const string WindowRestore = "xkxk.window.restore";
    /// <summary>명령 항목(shell.mnuPin).</summary>
    public const string WindowPin = "xkxk.window.pin";
    /// <summary>명령 항목(shell.mnuRename).</summary>
    public const string WindowRename = "xkxk.window.rename";
    /// <summary>명령 항목(shell.mnuFavoriteToggle).</summary>
    public const string WindowFavoriteToggle = "xkxk.window.favoriteToggle";
    /// <summary>명령 항목(shell.mnuResetLayout).</summary>
    public const string WindowResetLayout = "xkxk.window.resetLayout";
    /// <summary>구분선.</summary>
    public const string WindowSep2 = "xkxk.window.sep2";
    /// <summary>명령 항목(shell.mnuMinimizeAll).</summary>
    public const string WindowMinimizeAll = "xkxk.window.minimizeAll";
    /// <summary>명령 항목(shell.mnuRestoreAll).</summary>
    public const string WindowRestoreAll = "xkxk.window.restoreAll";
    /// <summary>명령 항목(shell.mnuRestoreLastMin).</summary>
    public const string WindowRestoreLastMin = "xkxk.window.restoreLastMin";
    /// <summary>구분선.</summary>
    public const string WindowSep3 = "xkxk.window.sep3";
    /// <summary>명령 항목(shell.mnuNext).</summary>
    public const string WindowNext = "xkxk.window.next";
    /// <summary>명령 항목(shell.mnuPrev).</summary>
    public const string WindowPrev = "xkxk.window.prev";
    /// <summary>구분선.</summary>
    public const string WindowSep4 = "xkxk.window.sep4";
    /// <summary>하위 메뉴 항목(shell.mnuSdiGroup).</summary>
    public const string WindowSdiGroup = "xkxk.window.sdiGroup";
    /// <summary>명령 항목(shell.mnuToggleSdi).</summary>
    public const string WindowToggleSdi = "xkxk.window.toggleSdi";
    /// <summary>명령 항목(shell.mnuFloat).</summary>
    public const string WindowFloat = "xkxk.window.float";
    /// <summary>명령 항목(shell.mnuRedock).</summary>
    public const string WindowRedock = "xkxk.window.redock";
    /// <summary>구분선.</summary>
    public const string WindowSep5 = "xkxk.window.sep5";
    /// <summary>명령 항목(shell.mnuSdiResetPos).</summary>
    public const string WindowSdiResetPos = "xkxk.window.sdiResetPos";
    /// <summary>명령 항목(shell.mnuSdiMinimize).</summary>
    public const string WindowSdiMinimize = "xkxk.window.sdiMinimize";
    /// <summary>명령 항목(shell.mnuSdiRestore).</summary>
    public const string WindowSdiRestore = "xkxk.window.sdiRestore";
    /// <summary>명령 항목(shell.mnuSdiCloseAll).</summary>
    public const string WindowSdiCloseAll = "xkxk.window.sdiCloseAll";
    /// <summary>하위 메뉴 항목(shell.mnuMonitor).</summary>
    public const string WindowMonitor = "xkxk.window.monitor";
    /// <summary>명령 항목(shell.mnuWinSingleMonitor).</summary>
    public const string WindowSingleMonitor = "xkxk.window.singleMonitor";
    /// <summary>섹션 자리(키 xkxk.window.monitor.list).</summary>
    public const string WindowMonitorList = "xkxk.window.monitor.list";
    /// <summary>구분선.</summary>
    public const string WindowSep6 = "xkxk.window.sep6";
    /// <summary>명령 항목(shell.mnuWinMonitorMaximize).</summary>
    public const string WindowMonitorMaximize = "xkxk.window.monitorMaximize";
    /// <summary>명령 항목(shell.mnuWinResetMain).</summary>
    public const string WindowResetMain = "xkxk.window.resetMain";
    /// <summary>구분선.</summary>
    public const string WindowSep7 = "xkxk.window.sep7";
    /// <summary>명령 항목(shell.mnuOverview).</summary>
    public const string WindowOverview = "xkxk.window.overview";
    /// <summary>명령 항목(shell.mnuClose).</summary>
    public const string WindowClose = "xkxk.window.close";
    /// <summary>명령 항목(shell.mnuCloseOthers).</summary>
    public const string WindowCloseOthers = "xkxk.window.closeOthers";
    /// <summary>명령 항목(shell.mnuCloseAll).</summary>
    public const string WindowCloseAll = "xkxk.window.closeAll";
    /// <summary>구분선.</summary>
    public const string WindowSep8 = "xkxk.window.sep8";
    /// <summary>명령 항목(shell.mnuFlash).</summary>
    public const string WindowFlash = "xkxk.window.flash";
    /// <summary>섹션 자리(키 xkxk.window.openList).</summary>
    public const string WindowOpenList = "xkxk.window.openList";
    /// <summary>최상위 메뉴(shell.mnuView).</summary>
    public const string View = "xkxk.view";
    /// <summary>명령 항목(shell.mnuPresetCascade).</summary>
    public const string ViewPresetCascade = "xkxk.view.presetCascade";
    /// <summary>명령 항목(shell.mnuPresetTabbed).</summary>
    public const string ViewPresetTabbed = "xkxk.view.presetTabbed";
    /// <summary>명령 항목(shell.mnuPresetTile).</summary>
    public const string ViewPresetTile = "xkxk.view.presetTile";
    /// <summary>구분선.</summary>
    public const string ViewSep1 = "xkxk.view.sep1";
    /// <summary>명령 항목(shell.mnuShowExplorer).</summary>
    public const string ViewShowExplorer = "xkxk.view.showExplorer";
    /// <summary>명령 항목(shell.mnuShowLog).</summary>
    public const string ViewShowLog = "xkxk.view.showLog";
    /// <summary>명령 항목(shell.mnuShowNotifications).</summary>
    public const string ViewShowNotifications = "xkxk.view.showNotifications";
    /// <summary>구분선.</summary>
    public const string ViewSep2 = "xkxk.view.sep2";
    /// <summary>명령 항목(shell.mnuFontReset).</summary>
    public const string ViewFontReset = "xkxk.view.fontReset";
    /// <summary>명령 항목(shell.mnuToggleTheme).</summary>
    public const string ViewToggleTheme = "xkxk.view.toggleTheme";
    /// <summary>명령 항목(shell.mnuSettings).</summary>
    public const string ViewSettings = "xkxk.view.settings";
    /// <summary>명령 항목(shell.mnuNextTheme).</summary>
    public const string ViewNextTheme = "xkxk.view.nextTheme";
    /// <summary>최상위 메뉴(shell.mnuSys).</summary>
    public const string System = "xkxk.system";
    /// <summary>명령 항목(shell.mnuSysSettings).</summary>
    public const string SystemSettings = "xkxk.system.settings";
    /// <summary>명령 항목(shell.mnuSysGpuRestart).</summary>
    public const string SystemGpuRestart = "xkxk.system.gpuRestart";
    /// <summary>구분선.</summary>
    public const string SystemSep1 = "xkxk.system.sep1";
    /// <summary>하위 메뉴 항목(shell.mnuSysMonitor).</summary>
    public const string SystemMonitor = "xkxk.system.monitor";
    /// <summary>명령 항목(shell.mnuSysMonitorApp).</summary>
    public const string SystemMonitorApp = "xkxk.system.monitorApp";
    /// <summary>하위 메뉴 항목(shell.mnuSysLog).</summary>
    public const string SystemLog = "xkxk.system.log";
    /// <summary>명령 항목(shell.mnuSysLogView).</summary>
    public const string SystemLogView = "xkxk.system.logView";
    /// <summary>명령 항목(shell.mnuSysLogFolder).</summary>
    public const string SystemLogFolder = "xkxk.system.logFolder";
    /// <summary>명령 항목(shell.mnuSysLogExport).</summary>
    public const string SystemLogExport = "xkxk.system.logExport";
    /// <summary>구분선.</summary>
    public const string SystemSep2 = "xkxk.system.sep2";
    /// <summary>명령 항목(shell.mnuSysInquiry).</summary>
    public const string SystemInquiry = "xkxk.system.inquiry";
    /// <summary>명령 항목(shell.mnuSysInquiryList).</summary>
    public const string SystemInquiryList = "xkxk.system.inquiryList";
    /// <summary>구분선.</summary>
    public const string SystemSep3 = "xkxk.system.sep3";
    /// <summary>명령 항목(shell.mnuSysUpdate).</summary>
    public const string SystemUpdate = "xkxk.system.update";
    /// <summary>명령 항목(shell.mnuSysChangelog).</summary>
    public const string SystemChangelog = "xkxk.system.changelog";
    /// <summary>명령 항목(shell.mnuSysHelp).</summary>
    public const string SystemHelp = "xkxk.system.help";
    /// <summary>명령 항목(shell.mnuSysAbout).</summary>
    public const string SystemAbout = "xkxk.system.about";

    private static readonly IReadOnlyList<string> RootIds = Array.AsReadOnly(new[] { File, Window, View, System });

    private static readonly IReadOnlyList<string> AllIds = Array.AsReadOnly(ShellMenuTable.Rows.Select(r => r.Id).ToArray());

    /// <summary>셸 최상위 메뉴 4개(파일·창·보기·시스템, 표시 순서). 읽기 전용이다.</summary>
    public static IReadOnlyList<string> Roots => RootIds;

    /// <summary>셸 메뉴 ID 전체(구분선·섹션 포함). 순서는 기본 등록 순서(표 행 순서)와 같다. 읽기 전용이다.</summary>
    /// <remarks>뒤 판에서 항목이 더해질 수 있다. 개수를 고정값으로 가정하지 않는다.</remarks>
    public static IReadOnlyList<string> All => AllIds;
}
