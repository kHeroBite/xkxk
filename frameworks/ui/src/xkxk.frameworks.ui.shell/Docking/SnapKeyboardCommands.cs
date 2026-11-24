// 창 배치(스냅) 키보드 대안 명령 — 방향 → 칸 적용·복원 변환(단축키·메뉴·원격 공용, 11_창배치_스냅레이아웃.md §6)
using xkxk.frameworks.ui.data.Logging;
using xkxk.frameworks.ui.shell.Theming;

namespace xkxk.frameworks.ui.shell.Docking;

/// <summary>스냅 키보드 명령의 방향.</summary>
public enum SnapKeyDirection
{
    /// <summary>왼쪽 반쪽.</summary>
    Left,

    /// <summary>오른쪽 반쪽.</summary>
    Right,

    /// <summary>최대화.</summary>
    Up,

    /// <summary>복원(스냅 전 크기·보통 상태).</summary>
    Down,
}

/// <summary>
/// 방향 → 칸 변환만 한다(← half#0, → half#1, ↑ max#0, ↓ 복원). 키 판정·대상 창 찾기는 화면 층이 하고, 실제 배치는 WindowMenuOps.ApplyCell·RestoreSnap 이 한다.
/// 옵션 SnapKeyboard(스냅 전체가 꺼져도 함께 꺼짐 — EffectiveSnapKeyboard)가 꺼져 있으면 아무 것도 하지 않고 false 를 돌려준다(키를 소비하지 않는다).
/// </summary>
public static class SnapKeyboardCommands
{
    /// <summary>단축키가 켜져 있는가.</summary>
    public static bool Enabled => UiSettings.Current.EffectiveSnapKeyboard;

    /// <summary>대상 창에 방향 명령을 적용한다. 옵션이 꺼졌거나 대상이 없거나 적용할 것이 없으면 false.</summary>
    public static bool Execute(WindowMenuTarget? target, SnapKeyDirection direction)
    {
        if (!Enabled || target is null)
        {
            return false;
        }

        var done = direction switch
        {
            SnapKeyDirection.Left => WindowMenuOps.ApplyCell(target, SnapLayoutCatalog.Half, 0),
            SnapKeyDirection.Right => WindowMenuOps.ApplyCell(target, SnapLayoutCatalog.Half, 1),
            SnapKeyDirection.Up => WindowMenuOps.ApplyCell(target, SnapLayoutCatalog.Max, 0),
            _ => WindowMenuOps.RestoreSnap(target),
        };
        PdLog.Info("window-menu", $"snap-key {direction} {target.Kind} doc={target.Document?.Id} done={done}");
        return done;
    }
}
