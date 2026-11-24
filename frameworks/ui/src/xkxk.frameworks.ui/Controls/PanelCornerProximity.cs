// 패널 끝 확장 버튼 근접 표시 — 창 하나에 하나씩 포인터 위치를 보고, 버튼 경계에서 일정 거리 안일 때만 그 버튼을 보이게 한다
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace xkxk.frameworks.ui.Controls;

/// <summary>
/// 버튼이 하나 이상 있는 창만 구독한다. 이동마다 그 창의 버튼 수만큼 거리만 재고, 시각 트리를 순회하지 않는다. 적중 검사는 거리 안에 들어온 버튼에만 한다.
/// 숨기는 경우: 거리 밖, 포인터가 창을 떠남, 마우스 단추를 누른 끌기 중(눌린 버튼 자신의 포인터 잡기는 예외), 버튼 중심을 다른 층(메뉴·투어·겹침 화면)이 덮음.
/// 터치 기기 버튼은 호버가 없어 여기에 등록하지 않고 항상 보인다(PanelCornerButton).
/// 마우스 기기에서도 마지막 입력이 터치·펜(Avalonia PointerType 가 Mouse 아님)이면 근접 규칙 대신 버튼을 항상 보이고, 마우스 이동이 오면 근접 규칙으로 돌아간다(터치 노트북).
/// 숨은 버튼 자리를 터치로 누른 첫 탭은 버튼을 보이게 하면서 그 버튼을 누른 것으로 처리한다(아래 패널로 지나가지 않음).
/// </summary>
public sealed class PanelCornerProximity
{
    /// <summary>버튼 경계에서 이 거리(dip) 이내면 보인다. 배율 100%에서 px 와 같다. 경계값을 포함한다.</summary>
    public const double RevealDistance = 50;

    private static readonly ConditionalWeakTable<TopLevel, PanelCornerProximity> s_byWindow = new();

    private readonly TopLevel _top;
    private readonly List<PanelCornerButton> _buttons = new();
    private Point? _pointer;
    private bool _subscribed;
    private bool _reevaluatePosted;
    private bool _touchMode;

    private PanelCornerProximity(TopLevel top) => _top = top;

#if PD_DIAGNOSTICS
    /// <summary>시험 전용: 실제 커서가 창 밖이면 OS 가 창 메시지 이동 직후 떠남을 보낸다. 참이면 떠남을 무시해 창 메시지로 거리만 잴 수 있다.</summary>
    public static bool HoldExitForTest { get; set; }
#endif

    /// <summary>포인터 이동 처리 횟수(진단용).</summary>
    public long MoveCount { get; private set; }

    /// <summary>적중 검사 횟수(진단용). 거리 안에 들어온 동안만 는다.</summary>
    public long HitTestCount { get; private set; }

    /// <summary>이 창이 지금 포인터 이벤트를 구독하는가(진단용).</summary>
    public bool IsSubscribed => _subscribed;

    /// <summary>등록된 버튼 수(진단용).</summary>
    public int ButtonCount => _buttons.Count;

    /// <summary>근접 추적을 만든 적 있는 창 전체(진단용).</summary>
    public static IReadOnlyList<PanelCornerProximity> All => s_byWindow.Select(p => p.Value).ToList();

    /// <summary>호스트가 포인터 떠남을 알린다. 웹은 Avalonia 가 페이지 떠남을 받지 못해 호스트가 직접 부른다.</summary>
    public static void NotifyPointerLeftHost()
    {
        foreach (var tracker in All)
        {
            tracker._pointer = null;
            tracker.Evaluate();
        }
    }

    /// <summary>버튼 경계와 점 사이 거리. 경계 안이면 0이다.</summary>
    public static double DistanceTo(Rect bounds, Point point)
    {
        var dx = Math.Max(Math.Max(bounds.Left - point.X, point.X - bounds.Right), 0);
        var dy = Math.Max(Math.Max(bounds.Top - point.Y, point.Y - bounds.Bottom), 0);
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    internal static PanelCornerProximity Register(TopLevel top, PanelCornerButton button)
    {
        var tracker = s_byWindow.GetValue(top, static t => new PanelCornerProximity(t));
        tracker._buttons.Add(button);
        tracker.UpdateSubscription();
        tracker.Evaluate(); // 포인터가 멈춘 채 새 버튼이 생긴 경우(옆 버튼을 눌러 끝이 바뀜)
        return tracker;
    }

    internal void Unregister(PanelCornerButton button)
    {
        _buttons.Remove(button);
        button.IsRevealed = false;
        UpdateSubscription();
    }

    /// <summary>포인터가 멈춘 채 버튼이 움직였다. 배치가 끝난 뒤 한 번만 다시 본다.</summary>
    internal void RequestReevaluate()
    {
        if (_reevaluatePosted)
        {
            return;
        }

        _reevaluatePosted = true;
        Dispatcher.UIThread.Post(() =>
        {
            _reevaluatePosted = false;
            Evaluate();
        }, DispatcherPriority.Background);
    }

    private void UpdateSubscription()
    {
        var wanted = _buttons.Count > 0;
        if (wanted == _subscribed)
        {
            return;
        }

        _subscribed = wanted;
        if (wanted)
        {
            // 다른 컨트롤이 처리한 이동도 봐야 하므로 터널 단계에서 처리된 이벤트까지 받는다(PointerFollower 와 같은 방식)
            _top.AddHandler(InputElement.PointerMovedEvent, OnPointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
            _top.AddHandler(InputElement.PointerExitedEvent, OnPointerExited, RoutingStrategies.Direct, handledEventsToo: true);
            _top.AddHandler(InputElement.PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        }
        else
        {
            _top.RemoveHandler(InputElement.PointerPressedEvent, OnPointerPressed);
            _top.RemoveHandler(InputElement.PointerMovedEvent, OnPointerMoved);
            _top.RemoveHandler(InputElement.PointerExitedEvent, OnPointerExited);
        }
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        MoveCount++;
        UpdateTouchMode(e);
        _pointer = IsForeignDrag(e) ? null : e.GetPosition(_top);
        Evaluate();
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // 숨은 버튼 자리를 손가락으로 누른 첫 탭은 적중 검사가 이미 아래 패널로 정해졌다. 모드를 바꾸기 전에 그 버튼을 찾아 둔다
        var tapped = e.Pointer.Type == PointerType.Touch ? HiddenButtonAt(e.GetPosition(_top)) : null;

        // 터치는 이동 없이 탭만 할 수 있어 누름에서도 입력 종류를 갱신한다
        if (UpdateTouchMode(e))
        {
            Evaluate();
        }

        // 터치에는 호버가 없어 '가까이 가서 보이게 한 뒤 누르기'가 안 된다. 버튼을 보이게 하는 탭이 곧 그 버튼을 누른 것이다
        if (tapped is { IsRevealed: true, IsEffectivelyEnabled: true })
        {
            e.Handled = true; // 아래 패널 머리줄로 탭이 지나가지 않게 한다
            tapped.ClickFromTouch();
        }
    }

    /// <summary>점이 경계 안에 있는 숨은 버튼(터치 첫 탭이 노린 버튼). 다른 층이 덮었으면 없다.</summary>
    private PanelCornerButton? HiddenButtonAt(Point point) =>
        _buttons.FirstOrDefault(b => !b.IsShown && b.Bounds.Size != default
            && b.TranslatePoint(default, _top) is { } origin
            && DistanceTo(new Rect(origin, b.Bounds.Size), point) == 0
            && !IsCovered(b));

    /// <summary>마지막 입력이 터치·펜이면 호버로 다가갈 수 없다고 보고 터치 모드로 둔다(펜은 기존 동작 유지). 바뀌었으면 참.</summary>
    private bool UpdateTouchMode(PointerEventArgs e)
    {
        var touch = e.Pointer.Type is PointerType.Touch or PointerType.Pen;
        if (_touchMode == touch)
        {
            return false;
        }

        _touchMode = touch;
        return true;
    }

    private void OnPointerExited(object? sender, PointerEventArgs e)
    {
#if PD_DIAGNOSTICS
        if (HoldExitForTest)
        {
            return;
        }
#endif
        _pointer = null;
        Evaluate();
    }

    /// <summary>마우스 단추를 누른 채 움직이면 다른 조작(분할선·탭 끌기)이다. 버튼 자신을 누른 경우만 뺀다.</summary>
    private bool IsForeignDrag(PointerEventArgs e)
    {
        if (e.Pointer.Type != PointerType.Mouse || e.Pointer.Captured is PanelCornerButton)
        {
            return false;
        }

        var p = e.GetCurrentPoint(_top).Properties;
        return p.IsLeftButtonPressed || p.IsRightButtonPressed || p.IsMiddleButtonPressed;
    }

    private void Evaluate()
    {
        foreach (var button in _buttons)
        {
            // 터치 입력이면 근접 규칙 대신 항상 표시, 마우스로 돌아오면 근접 규칙 복귀
            button.IsRevealed = _touchMode
                ? !IsCovered(button)
                : _pointer is { } point && IsInRange(button, point) && !IsCovered(button);
        }
    }

    private bool IsInRange(PanelCornerButton button, Point point) =>
        button.Bounds.Size != default
        && button.TranslatePoint(default, _top) is { } origin
        && DistanceTo(new Rect(origin, button.Bounds.Size), point) <= RevealDistance;

    /// <summary>버튼 중심의 맨 위가 버튼이 놓인 패널 머리줄 밖의 요소면 다른 층이 덮은 것이다.</summary>
    private bool IsCovered(PanelCornerButton button)
    {
        HitTestCount++;
        var owner = button.GetVisualParent()?.GetVisualParent();
        if (owner is null || button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), _top) is not { } center)
        {
            return false;
        }

        return _top.InputHitTest(center) is Visual hit && !ReferenceEquals(hit, owner) && !owner.IsVisualAncestorOf(hit);
    }
}
