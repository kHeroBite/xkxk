// 셸 기본 명령 ID 상수 61개(B06 23 + B07 38) — 값은 바꾸지 않고 추가만 한다(T-CMD, T-RESERVED, UADR-0009 원칙 5)
using System;
using System.Collections.Generic;

namespace xkxk.frameworks.ui.shell.Framework;

/// <summary>
/// 프레임워크가 <see cref="IUiCommandRegistry"/> 에 기본으로 등록하는 셸 명령의 ID 다.
/// </summary>
/// <remarks>
/// 모든 값은 프레임워크 예약 접두어 <see cref="UiRegistryOwners.ReservedPrefix"/>("xkxk.")로 시작한다.
/// 소비자는 이 ID 로 명령을 실행하거나 <see cref="UiCommandOptions.Replace"/> 로 동작을 바꿀 수 있지만,
/// 예약 접두어로 시작하는 새 ID 를 만들 수는 없다(XKUI0308).
/// 확장 규칙: 새 기본 명령은 상수를 추가하고 <see cref="All"/> 의 끝에 덧붙인다. 기존 상수의 이름·값·순서는 바꾸지 않는다.
/// </remarks>
public static class ShellCommandIds
{
    /// <summary>창 배치를 저장한다.</summary>
    public const string LayoutSave = "xkxk.layout.save";

    /// <summary>저장한 창 배치를 불러온다.</summary>
    public const string LayoutLoad = "xkxk.layout.load";

    /// <summary>창을 계단식으로 배열한다.</summary>
    public const string WindowCascade = "xkxk.window.cascade";

    /// <summary>창을 가로로 나란히 배열한다.</summary>
    public const string WindowTileHorizontal = "xkxk.window.tileHorizontal";

    /// <summary>창을 세로로 나란히 배열한다.</summary>
    public const string WindowTileVertical = "xkxk.window.tileVertical";

    /// <summary>창을 탭으로 모은다.</summary>
    public const string WindowTabbed = "xkxk.window.tabbed";

    /// <summary>대상 창을 최소화한다.</summary>
    public const string WindowMinimize = "xkxk.window.minimize";

    /// <summary>대상 창의 고정 상태를 바꾼다.</summary>
    public const string WindowPin = "xkxk.window.pin";

    /// <summary>창 배치를 처음 상태로 되돌린다.</summary>
    public const string WindowResetLayout = "xkxk.window.resetLayout";

    /// <summary>모든 창을 최소화한다.</summary>
    public const string WindowMinimizeAll = "xkxk.window.minimizeAll";

    /// <summary>최소화한 창을 모두 복원한다.</summary>
    public const string WindowRestoreAll = "xkxk.window.restoreAll";

    /// <summary>마지막으로 최소화한 창을 복원한다.</summary>
    public const string WindowRestoreLastMinimized = "xkxk.window.restoreLastMinimized";

    /// <summary>다음 창을 활성화한다.</summary>
    public const string WindowNext = "xkxk.window.next";

    /// <summary>이전 창을 활성화한다.</summary>
    public const string WindowPrevious = "xkxk.window.previous";

    /// <summary>대상 창을 띄운다(플로팅).</summary>
    public const string WindowFloat = "xkxk.window.float";

    /// <summary>띄운 대상 창을 다시 도킹한다.</summary>
    public const string WindowRedock = "xkxk.window.redock";

    /// <summary>창 전체 보기를 연다.</summary>
    public const string WindowOverview = "xkxk.window.overview";

    /// <summary>대상 창을 닫는다.</summary>
    public const string WindowClose = "xkxk.window.close";

    /// <summary>대상 창을 뺀 나머지 창을 닫는다.</summary>
    public const string WindowCloseOthers = "xkxk.window.closeOthers";

    /// <summary>모든 창을 닫는다.</summary>
    public const string WindowCloseAll = "xkxk.window.closeAll";

    /// <summary>배치 프리셋 "MDI 계단식"을 적용한다.</summary>
    public const string LayoutPresetMdiCascade = "xkxk.layout.preset.mdiCascade";

    /// <summary>배치 프리셋 "탭"을 적용한다.</summary>
    public const string LayoutPresetTabbed = "xkxk.layout.preset.tabbed";

    /// <summary>배치 프리셋 "MDI 세로 나란히"를 적용한다.</summary>
    public const string LayoutPresetMdiTileVertical = "xkxk.layout.preset.mdiTileVertical";

    /// <summary>데모 문서를 연다. 파라미터는 창 ID 또는 옛 이름(Grid·Gantt·Form)이다. 시험대 모듈이 등록한다(B10). 셸은 이 ID 의 명령을 등록하지 않는다.</summary>
    public const string DemoOpen = "xkxk.demo.open";

    /// <summary>데모 문서를 독립 창(SDI)으로 연다. 파라미터는 창 ID 또는 옛 이름(Grid·Gantt·Form)이다. 시험대 모듈이 등록한다(B10). 셸은 등록하지 않는다.</summary>
    public const string DemoOpenSdi = "xkxk.demo.openSdi";

    /// <summary>성능 벤치 화면을 연다. 파라미터는 벤치 종류 문자열이다. 벤치를 쓸 수 있을 때만 실행할 수 있다. 시험대 모듈이 등록한다(B10). 셸은 등록하지 않는다.</summary>
    public const string BenchOpen = "xkxk.bench.open";

    /// <summary>현재 배치를 이름 붙여 저장하는 창을 연다.</summary>
    public const string LayoutSaveCurrent = "xkxk.layout.saveCurrent";

    /// <summary>저장한 배치를 관리하는 창을 연다.</summary>
    public const string LayoutManage = "xkxk.layout.manage";

    /// <summary>앱을 끝낸다(메인 창 닫기와 같은 경로). 데스크톱 메인 창이 있을 때만 실행할 수 있다.</summary>
    public const string AppExit = "xkxk.app.exit";

    /// <summary>대상 창의 최대화와 보통 크기를 바꾼다.</summary>
    public const string WindowMaximizeToggle = "xkxk.window.maximizeToggle";

    /// <summary>대상 창을 이전 크기로 되돌린다.</summary>
    public const string WindowRestore = "xkxk.window.restore";

    /// <summary>대상 창의 이름 변경을 시작한다.</summary>
    public const string WindowRename = "xkxk.window.rename";

    /// <summary>대상 창의 즐겨찾기 등록 상태를 바꾼다.</summary>
    public const string FavoriteToggle = "xkxk.favorite.toggle";

    /// <summary>대상 창을 MDI 와 독립 창(SDI) 사이에서 바꾼다.</summary>
    public const string WindowToggleSdi = "xkxk.window.toggleSdi";

    /// <summary>독립 창 위치를 처음 상태로 되돌린다.</summary>
    public const string SdiResetPositions = "xkxk.sdi.resetPositions";

    /// <summary>독립 창을 모두 최소화한다.</summary>
    public const string SdiMinimizeAll = "xkxk.sdi.minimizeAll";

    /// <summary>독립 창을 모두 복원한다.</summary>
    public const string SdiRestoreAll = "xkxk.sdi.restoreAll";

    /// <summary>독립 창을 모두 닫는다.</summary>
    public const string SdiCloseAll = "xkxk.sdi.closeAll";

    /// <summary>창을 한 모니터 안으로 모은다.</summary>
    public const string MonitorSingle = "xkxk.monitor.single";

    /// <summary>메인 창을 선택한 모니터에서 최대화한다.</summary>
    public const string MonitorMaximizeSelected = "xkxk.monitor.maximizeSelected";

    /// <summary>메인 창 크기·위치를 처음 상태로 되돌린다.</summary>
    public const string MainWindowReset = "xkxk.mainWindow.reset";

    /// <summary>대상 창을 반짝여 위치를 알린다.</summary>
    public const string WindowFlash = "xkxk.window.flash";

    /// <summary>탐색기 도구 창을 보인다.</summary>
    public const string ViewShowExplorer = "xkxk.view.showExplorer";

    /// <summary>로그 도구 창을 보인다. 로그 도구를 쓸 수 있을 때만 실행할 수 있다.</summary>
    public const string ViewShowLog = "xkxk.view.showLog";

    /// <summary>알림 도구 창을 보인다.</summary>
    public const string ViewShowNotifications = "xkxk.view.showNotifications";

    /// <summary>글자 크기(또는 창 배율)를 기본값으로 되돌린다.</summary>
    public const string ViewFontReset = "xkxk.view.fontReset";

    /// <summary>밝은 테마와 어두운 테마를 바꾼다.</summary>
    public const string ThemeToggle = "xkxk.theme.toggle";

    /// <summary>다음 색 테마로 바꾼다.</summary>
    public const string ThemeNext = "xkxk.theme.next";

    /// <summary>설정 화면을 연다.</summary>
    public const string SystemSettings = "xkxk.system.settings";

    /// <summary>재시작이 필요한 설정 안내를 연다.</summary>
    public const string SystemRestartRequired = "xkxk.system.restartRequired";

    /// <summary>앱 상태 모니터를 연다.</summary>
    public const string SystemAppMonitor = "xkxk.system.appMonitor";

    /// <summary>로그 보기를 연다.</summary>
    public const string SystemLogView = "xkxk.system.logView";

    /// <summary>로그 폴더를 연다.</summary>
    public const string SystemLogFolder = "xkxk.system.logFolder";

    /// <summary>로그를 내보낸다.</summary>
    public const string SystemLogExport = "xkxk.system.logExport";

    /// <summary>문의 작성 화면을 연다.</summary>
    public const string SystemInquiry = "xkxk.system.inquiry";

    /// <summary>문의 목록을 연다.</summary>
    public const string SystemInquiryList = "xkxk.system.inquiryList";

    /// <summary>업데이트 화면을 연다.</summary>
    public const string SystemUpdate = "xkxk.system.update";

    /// <summary>변경 내역을 연다.</summary>
    public const string SystemChangelog = "xkxk.system.changelog";

    /// <summary>도움말을 연다.</summary>
    public const string SystemHelp = "xkxk.system.help";

    /// <summary>앱 정보를 연다.</summary>
    public const string SystemAbout = "xkxk.system.about";

    /// <summary>모바일형 화면의 패널을 연다. 파라미터는 explorer·docs·notify·settings 중 하나다(없으면 메뉴 패널). 모바일형 화면이 있을 때만 실행할 수 있다.</summary>
    public const string NavOpenPanel = "xkxk.nav.openPanel";

    private static readonly IReadOnlyList<string> AllIds = Array.AsReadOnly(new[]
    {
        LayoutSave,
        LayoutLoad,
        WindowCascade,
        WindowTileHorizontal,
        WindowTileVertical,
        WindowTabbed,
        WindowMinimize,
        WindowPin,
        WindowResetLayout,
        WindowMinimizeAll,
        WindowRestoreAll,
        WindowRestoreLastMinimized,
        WindowNext,
        WindowPrevious,
        WindowFloat,
        WindowRedock,
        WindowOverview,
        WindowClose,
        WindowCloseOthers,
        WindowCloseAll,
        LayoutPresetMdiCascade,
        LayoutPresetTabbed,
        LayoutPresetMdiTileVertical,
        DemoOpen,
        DemoOpenSdi,
        BenchOpen,
        LayoutSaveCurrent,
        LayoutManage,
        AppExit,
        WindowMaximizeToggle,
        WindowRestore,
        WindowRename,
        FavoriteToggle,
        WindowToggleSdi,
        SdiResetPositions,
        SdiMinimizeAll,
        SdiRestoreAll,
        SdiCloseAll,
        MonitorSingle,
        MonitorMaximizeSelected,
        MainWindowReset,
        WindowFlash,
        ViewShowExplorer,
        ViewShowLog,
        ViewShowNotifications,
        ViewFontReset,
        ThemeToggle,
        ThemeNext,
        SystemSettings,
        SystemRestartRequired,
        SystemAppMonitor,
        SystemLogView,
        SystemLogFolder,
        SystemLogExport,
        SystemInquiry,
        SystemInquiryList,
        SystemUpdate,
        SystemChangelog,
        SystemHelp,
        SystemAbout,
        NavOpenPanel,
    });

    /// <summary>기본 명령 ID 전체(지금 61개). 순서는 기본 등록 순서와 같다. 읽기 전용이다.</summary>
    /// <remarks>뒤 판에서 끝에 항목이 더해질 수 있다. 개수를 고정값으로 가정하지 않는다.</remarks>
    public static IReadOnlyList<string> All => AllIds;
}
