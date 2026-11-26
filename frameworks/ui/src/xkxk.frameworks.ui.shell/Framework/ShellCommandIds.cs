// 셸 기본 명령 ID 상수 23개 — 값은 바꾸지 않고 추가만 한다(T-CMD, T-RESERVED, UADR-0009 원칙 5)
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
    });

    /// <summary>기본 명령 ID 전체(지금 23개). 순서는 기본 등록 순서와 같다. 읽기 전용이다.</summary>
    /// <remarks>뒤 판에서 끝에 항목이 더해질 수 있다. 개수를 고정값으로 가정하지 않는다.</remarks>
    public static IReadOnlyList<string> All => AllIds;
}
