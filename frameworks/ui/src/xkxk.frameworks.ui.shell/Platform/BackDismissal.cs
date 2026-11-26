// 뒤로 가기 한 번에 맨 위 일시 화면 하나만 닫는다 — 투어·팝업·메뉴·시스템 Flyout·서랍·설정 시트·알림 오버레이 순서(모바일 화면설계 §4)
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using xkxk.frameworks.ui.shell.Tour;
using xkxk.frameworks.ui.shell.ViewModels.Settings;
using xkxk.frameworks.ui.shell.Framework;

namespace xkxk.frameworks.ui.shell.Platform;

/// <summary>
/// 호스트가 BACK 을 Esc 로 뷰에 넘기기 전에 부른다. Avalonia Android 는 BACK 을 Esc 로 바꾸는데, Esc 는 초점 위치에 따라 닿는 곳이 달라
/// 같은 화면이 터치 전후로 다르게 동작한다. 메뉴·시트는 BackRequested 도 받지 않는다(Avalonia 12.1.4 소스 확인). 그래서 Esc 로 흉내 내지 않고 직접 닫는다.
/// </summary>
public static class BackDismissal
{
    /// <summary>닫았으면 true, 열린 것이 없으면 false 다(호스트가 기존 뒤로 가기로 넘긴다).</summary>
    public static bool TryDismissTop(TopLevel top)
    {
        if (TourController.SessionOf(top) is { } tour)
        {
            tour.End();
            return true;
        }

        var visuals = xkxk.frameworks.ui.shell.Docking.TabSlotMarker.Descendants(top).Reverse().ToList(); // 숨은 탭 문서 하위는 건너뛴다(문서 전환 가속 이전과 같은 대상)

        // 메뉴 하위 팝업은 아래 메뉴 닫기가 함께 닫는다. 빛 닫기가 아닌 팝업은 사용자가 닫는 대상이 아니다
        if (visuals.OfType<Popup>().FirstOrDefault(p => p is { IsOpen: true, IsLightDismissEnabled: true } && p.TemplatedParent is not MenuItem) is { } popup)
        {
            popup.IsOpen = false;
            return true;
        }

        if (visuals.Any(TryCloseMenuOf) || FittedFlyout.TryHideTop())
        {
            return true;
        }

        if (visuals.OfType<SplitView>().FirstOrDefault(s => s is { IsPaneOpen: true, DisplayMode: SplitViewDisplayMode.Overlay or SplitViewDisplayMode.CompactOverlay }) is { } drawer)
        {
            drawer.SetCurrentValue(SplitView.IsPaneOpenProperty, false);
            return true;
        }

        // 설정 시트는 VM 상태로 그려지는 본문이라 팝업이 아니다. CloseAction 은 시트로 열 때만 들어 있다
        if (visuals.OfType<Control>().Select(c => c.DataContext).OfType<SettingsWindowViewModel>().FirstOrDefault(v => v.CloseAction is not null) is { } sheet)
        {
            sheet.CloseAction!();
            return true;
        }

        if (XkxkUi.Current.MainViewModel is { Compact.IsNotifyOpen: true } main)
        {
            main.Compact.IsNotifyOpen = false;
            return true;
        }

        return false;
    }

    private static bool TryCloseMenuOf(Visual visual)
    {
        switch (visual)
        {
            case Menu { IsOpen: true } menu:
                menu.Close();
                return true;
            case Button { Flyout.IsOpen: true } button:
                button.Flyout.Hide();
                return true;
            case Control { ContextMenu.IsOpen: true } control:
                control.ContextMenu!.Close();
                return true;
            case Control { ContextFlyout.IsOpen: true } control:
                control.ContextFlyout!.Hide();
                return true;
            case Control control when FlyoutBase.GetAttachedFlyout(control) is { IsOpen: true } attached:
                attached.Hide();
                return true;
            default:
                return false;
        }
    }
}
