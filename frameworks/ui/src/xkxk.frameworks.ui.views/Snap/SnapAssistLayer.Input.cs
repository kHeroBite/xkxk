// 남은 칸 창 고르기 덮개의 키·포인터·포커스 입력 처리 부분 — SnapAssistLayer partial
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using xkxk.frameworks.ui.data.Logging;
using xkxk.frameworks.ui.shell.Docking;
using xkxk.frameworks.ui.shell.Localization;

namespace xkxk.frameworks.ui.views.Snap;

/// <summary>키·포인터·포커스 입력 처리.</summary>
public sealed partial class SnapAssistLayer
{
    // ── 입력 ──

    private void OnCardPicked(int index)
    {
        if (_shown)
        {
            PdLog.Info(LogTag, $"overlay pick index={index}");
            SnapAssistSession.Pick(index);
        }
    }

    /// <summary>
    /// 최상위 터널 단계에서 Tab·Shift+Tab 을 받아 후보 포커스 이동에 쓴다(AC-015). 세션이 떠 있으면 덮개 포커스 여부와 무관하게 처리한다.
    /// TabFocusScope(Input/TabFocusScope.cs)가 TopLevel 터널 클래스 처리기로 메뉴 영역 밖 Tab 을 막고 포커스를 메뉴 첫 항목(FileMenu)으로 옮기므로,
    /// 이 처리기가 그 뒤에 불려 포커스가 이미 밖에 있으면 TakeFocus 로 덮개에 되돌린다(_refocusPending 이 켜져 있어 이탈 판정 CheckFocus 는 끝내지 않는다).
    /// e.Handled 로 아래 OnKeyDown 의 Tab 처리(중복 이동)를 막는다. Ctrl·Alt 가 붙은 Tab(창 전환 단축키)은 건드리지 않는다. 나머지 키는 OnKeyDown 이 그대로 처리한다.
    /// </summary>
    private void OnTopKeyDown(object? sender, KeyEventArgs e)
    {
        if (!_shown || e.Key != Key.Tab || _view is null || (e.KeyModifiers & ~KeyModifiers.Shift) != KeyModifiers.None)
        {
            return;
        }

        var within = IsKeyboardFocusWithin;
        var focused = _top?.FocusManager?.GetFocusedElement();
        var focusedName = focused is null ? "none" : focused is Control fc && !string.IsNullOrEmpty(fc.Name) ? $"{focused.GetType().Name}#{fc.Name}" : focused.GetType().Name;
        PdLog.Info(LogTag, $"overlay tab key handledBefore={e.Handled} focusWithin={within} focused={focusedName} shift={e.KeyModifiers.HasFlag(KeyModifiers.Shift)}");
        e.Handled = true;
        if (!within)
        {
            TakeFocus();
        }

        SnapAssistSession.MoveFocus(e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? -1 : 1);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!_shown || e.Handled || _view is not { } v)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Escape:
                e.Handled = true;
                SnapAssistSession.Cancel(SnapAssistEndReason.Esc);
                break;
            case Key.Tab:
                e.Handled = true;
                SnapAssistSession.MoveFocus(e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? -1 : 1);
                break;
            case Key.Right:
            case Key.Down:
                e.Handled = true;
                SnapAssistSession.MoveFocus(1);
                break;
            case Key.Left:
            case Key.Up:
                e.Handled = true;
                SnapAssistSession.MoveFocus(-1);
                break;
            case Key.Enter:
            case Key.Space:
                e.Handled = true;
                if (v.FocusIndex >= 0)
                {
                    SnapAssistSession.Pick(v.FocusIndex);
                }

                break;
        }
    }

    /// <summary>
    /// 최상위 터널 단계에서 모든 누름을 본다. 카드 위 → 카드가 처리(Tapped), 흐린 다른 칸 → SelectZone, 활성 칸 빈 곳 → 무시,
    /// 그 밖(채운 칸·칸 없는 곳·덮개 밖) → Cancel(outside). 덮개 밖 누름은 막지 않는다(그 누름으로 다른 창 끌기를 시작할 수 있다).
    /// </summary>
    private void OnTopPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!_shown || _view is not { } v)
        {
            return;
        }

        var src = e.Source as Visual;
        if (src is not null && _picker is { IsVisible: true } picker && (ReferenceEquals(src, picker) || picker.IsVisualAncestorOf(src)))
        {
            return;
        }

        if (src is not null && (ReferenceEquals(src, this) || this.IsVisualAncestorOf(src)))
        {
            var p = e.GetPosition(this) + new Vector(_origin.X, _origin.Y);
            var zone = v.Zones.FirstOrDefault(z => !z.Filled && z.Rect.Contains(p));
            if (zone is not null)
            {
                e.Handled = true;
                if (zone.Cell != v.ActiveCell)
                {
                    PdLog.Info(LogTag, $"overlay zone cell={zone.Cell}");
                    SnapAssistSession.SelectZone(zone.Cell);
                }

                return;
            }
        }

        PdLog.Info(LogTag, "overlay outside press");
        SnapAssistSession.Cancel(SnapAssistEndReason.Outside);
    }

    // ── 포커스 ──

    /// <summary>
    /// 포커스를 덮개로 가져온다(지금 1회 + 배치 뒤처리가 끝난 다음 1회). 두 번째가 끝날 때까지 포커스 이탈은 판정하지 않는다.
    /// SDI 호스트 창은 창 자체도 다시 활성화한다(Pick 이 다른 OS 창을 앞으로 가져온다).
    /// </summary>
    private void TakeFocus()
    {
        _refocusPending = true;
        Focus(NavigationMethod.Unspecified);
        Dispatcher.UIThread.Post(() =>
        {
            if (!_shown)
            {
                _refocusPending = false;
                return;
            }

            if (_view is { Scope: SnapAssistScope.Sdi } && _top is Window { IsActive: false } w)
            {
                w.Activate();
            }

            Focus(NavigationMethod.Unspecified);
            _refocusPending = false;
        }, DispatcherPriority.Background);
    }

    private void OnLostFocus(object? sender, RoutedEventArgs e)
    {
        if (!_shown || _refocusPending)
        {
            return;
        }

        Dispatcher.UIThread.Post(CheckFocus, DispatcherPriority.Background);
    }

    /// <summary>포커스가 정말 덮개 밖으로 갔으면 끝(focus). 덮개 자식으로 옮긴 것이면 그대로 둔다.</summary>
    private void CheckFocus()
    {
        if (!_shown || _refocusPending)
        {
            return;
        }

        var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Visual;
        if (focused is not null && (ReferenceEquals(focused, this) || this.IsVisualAncestorOf(focused)))
        {
            return;
        }

        PdLog.Info(LogTag, $"overlay focus lost to={focused?.GetType().Name ?? "none"}");
        SnapAssistSession.Cancel(SnapAssistEndReason.Focus);
    }

    private void OnWindowDeactivated(object? sender, EventArgs e)
    {
        if (!_shown || _refocusPending)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (_shown && !_refocusPending && _top is Window { IsActive: false })
            {
                PdLog.Info(LogTag, "overlay window deactivated");
                SnapAssistSession.Cancel(SnapAssistEndReason.Focus);
            }
        }, DispatcherPriority.Background);
    }
}
