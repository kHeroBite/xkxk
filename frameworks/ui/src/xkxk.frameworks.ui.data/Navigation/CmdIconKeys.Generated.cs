// 명령 아이콘 키 상수(상단 메뉴 실행 항목과 그 메뉴로 연 창이 같이 쓴다) — plan/_build/menu_icons.py 생성물(손으로 고치지 말고 menu_icon_data.py CMD 를 고친 뒤 다시 만든다)
using System.Collections.Generic;

namespace xkxk.frameworks.ui.data.Navigation;

/// <summary>명령 아이콘 리소스 키 = xkxk.frameworks.ui.views/Themes/MenuIcons.axaml 의 StreamGeometry 키 PdCmdIcon&lt;이름&gt;. UI 비의존 문자열만 둔다(22 설계 §2.1).</summary>
public static class CmdIconKeys
{
    public const string KeyPrefix = "PdCmdIcon";
    public const int Count = 58;

    public const string OpenSdi = "PdCmdIconOpenSdi";
    public const string OpenBench = "PdCmdIconOpenBench";
    public const string SaveLayout = "PdCmdIconSaveLayout";
    public const string LoadLayout = "PdCmdIconLoadLayout";
    public const string LayoutSave = "PdCmdIconLayoutSave";
    public const string LayoutManage = "PdCmdIconLayoutManage";
    public const string Exit = "PdCmdIconExit";
    public const string Cascade = "PdCmdIconCascade";
    public const string TileH = "PdCmdIconTileH";
    public const string TileV = "PdCmdIconTileV";
    public const string Tabbed = "PdCmdIconTabbed";
    public const string Snap = "PdCmdIconSnap";
    public const string Minimize = "PdCmdIconMinimize";
    public const string Maximize = "PdCmdIconMaximize";
    public const string Restore = "PdCmdIconRestore";
    public const string Pin = "PdCmdIconPin";
    public const string Rename = "PdCmdIconRename";
    public const string ResetLayout = "PdCmdIconResetLayout";
    public const string MinimizeAll = "PdCmdIconMinimizeAll";
    public const string RestoreAll = "PdCmdIconRestoreAll";
    public const string RestoreLastMin = "PdCmdIconRestoreLastMin";
    public const string Next = "PdCmdIconNext";
    public const string Prev = "PdCmdIconPrev";
    public const string SdiGroup = "PdCmdIconSdiGroup";
    public const string ToggleSdi = "PdCmdIconToggleSdi";
    public const string Float = "PdCmdIconFloat";
    public const string Redock = "PdCmdIconRedock";
    public const string SdiResetPos = "PdCmdIconSdiResetPos";
    public const string SdiMinimize = "PdCmdIconSdiMinimize";
    public const string SdiRestore = "PdCmdIconSdiRestore";
    public const string SdiCloseAll = "PdCmdIconSdiCloseAll";
    public const string Monitor = "PdCmdIconMonitor";
    public const string SingleMonitor = "PdCmdIconSingleMonitor";
    public const string MultiMonitor = "PdCmdIconMultiMonitor";
    public const string MonitorItem = "PdCmdIconMonitorItem";
    public const string MonitorNone = "PdCmdIconMonitorNone";
    public const string MonitorMaximize = "PdCmdIconMonitorMaximize";
    public const string ResetMain = "PdCmdIconResetMain";
    public const string Overview = "PdCmdIconOverview";
    public const string Close = "PdCmdIconClose";
    public const string CloseOthers = "PdCmdIconCloseOthers";
    public const string CloseAll = "PdCmdIconCloseAll";
    public const string Flash = "PdCmdIconFlash";
    public const string WinListMore = "PdCmdIconWinListMore";
    public const string FontReset = "PdCmdIconFontReset";
    public const string ToggleTheme = "PdCmdIconToggleTheme";
    public const string NextTheme = "PdCmdIconNextTheme";
    public const string GpuRestart = "PdCmdIconGpuRestart";
    public const string SysMonitor = "PdCmdIconSysMonitor";
    public const string AppMonitor = "PdCmdIconAppMonitor";
    public const string LogGroup = "PdCmdIconLogGroup";
    public const string LogFolder = "PdCmdIconLogFolder";
    public const string LogExport = "PdCmdIconLogExport";
    public const string Inquiry = "PdCmdIconInquiry";
    public const string InquiryList = "PdCmdIconInquiryList";
    public const string Update = "PdCmdIconUpdate";
    public const string Changelog = "PdCmdIconChangelog";
    public const string About = "PdCmdIconAbout";

    /// <summary>모든 명령 아이콘 키(데이터 순서).</summary>
    public static IReadOnlyList<string> All { get; } = new[]
    {
        OpenSdi,
        OpenBench,
        SaveLayout,
        LoadLayout,
        LayoutSave,
        LayoutManage,
        Exit,
        Cascade,
        TileH,
        TileV,
        Tabbed,
        Snap,
        Minimize,
        Maximize,
        Restore,
        Pin,
        Rename,
        ResetLayout,
        MinimizeAll,
        RestoreAll,
        RestoreLastMin,
        Next,
        Prev,
        SdiGroup,
        ToggleSdi,
        Float,
        Redock,
        SdiResetPos,
        SdiMinimize,
        SdiRestore,
        SdiCloseAll,
        Monitor,
        SingleMonitor,
        MultiMonitor,
        MonitorItem,
        MonitorNone,
        MonitorMaximize,
        ResetMain,
        Overview,
        Close,
        CloseOthers,
        CloseAll,
        Flash,
        WinListMore,
        FontReset,
        ToggleTheme,
        NextTheme,
        GpuRestart,
        SysMonitor,
        AppMonitor,
        LogGroup,
        LogFolder,
        LogExport,
        Inquiry,
        InquiryList,
        Update,
        Changelog,
        About,
    };
}
