// 패널 탭 줄 빈 곳 놓기(Q15 ③) — Dock 이 드롭 대상 없음으로 띄우려는(Float) 패널 탭을, 놓은 점이 그 패널 탭 줄 안이면 맨 끝으로 순서만 바꾼다
using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using Dock.Model.Core;

namespace Pd.Poc.Shell.Docking;

/// <summary>
/// 근거(Dock 12.1.0.12 디컴파일 logs/odev1r/ilspy_ui20 DockControlState.Process Released):
/// 탭을 끌어 놓았을 때 놓은 점에 드롭 대상(IsDropArea)이 없거나 연산이 None 이면 Drop 이 false 가 되고,
/// 띄우기 가능(CanFloat)하면 DockManagerState.Float → factory.FloatDockable 을 부른다.
/// 마지막 탭 오른쪽 빈 머리줄이 바로 이 경우라 순서 변경 대신 SDI 로 분리됐다(otest-12 Q15 ③).
/// 대응: TopLevel 에서 누름·놓기 지점을 엿보아 기록하고(터널, handledEventsToo), PocDockFactory.FloatDockable 이
/// 띄우기 전에 TryReorderToEnd 를 부른다. 조건은 ① 문서가 측면 패널 탭 영역(PanelDocumentDock)에 있고
/// ② 직전 놓기가 끌기(MinDragDistance 이상 이동)였고 ③ 놓은 점이 그 탭 영역의 탭 줄(DocumentTabStrip) 안일 때다.
/// 더블클릭·메뉴 띄우기처럼 끌기가 아닌 경로와 탭 줄 밖에 놓은 경우는 Dock 기본(띄우기) 그대로다.
/// </summary>
public static class PanelTabStripDrop
{
    /// <summary>끌기로 보는 최소 이동 거리(DIP). 클릭·더블클릭으로 띄우는 경로를 거른다.</summary>
    public const double MinDragDistance = 4;

    /// <summary>놓기 기록을 믿는 시간(ms). Dock 의 놓기 처리는 같은 놓기 이벤트 안에서 FloatDockable 을 부른다.</summary>
    public const long ReleaseFreshMs = 1000;

    private static bool _installed;
    private static Point _pressPoint;
    private static TopLevel? _releaseTop;
    private static Point _releasePoint;
    private static bool _releaseWasDrag;
    private static long _releaseAt;

    /// <summary>순서를 바꾼 뒤(로그·시험 관측용). 인자는 문서 Id 와 탭 영역 Id.</summary>
    public static event Action<string, string>? Reordered;

    /// <summary>클래스 처리기를 1회 등록한다(PocDockFactory.InitLayout).</summary>
    public static void Install()
    {
        if (_installed)
        {
            return;
        }

        _installed = true;
        InputElement.PointerPressedEvent.AddClassHandler<TopLevel>((top, e) => _pressPoint = e.GetPosition(top), RoutingStrategies.Tunnel, handledEventsToo: true);
        InputElement.PointerReleasedEvent.AddClassHandler<TopLevel>(OnReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    private static void OnReleased(TopLevel top, PointerReleasedEventArgs e)
    {
        var point = e.GetPosition(top);
        var d = point - _pressPoint;
        _releaseTop = top;
        _releasePoint = point;
        _releaseWasDrag = Math.Sqrt((d.X * d.X) + (d.Y * d.Y)) >= MinDragDistance;
        _releaseAt = Environment.TickCount64;
    }

    /// <summary>
    /// 띄우기 대신 탭 줄 맨 끝으로 옮겼으면 true(호출자는 띄우지 않는다). 조건이 맞지 않으면 false(Dock 기본 띄우기).
    /// </summary>
    internal static bool TryReorderToEnd(PocDockFactory factory, IDockable dockable)
    {
        if (dockable.Owner is not PanelDocumentDock tabs || tabs.VisibleDockables is not { Count: > 0 } list
            || _releaseTop is not { } top || !_releaseWasDrag || Environment.TickCount64 - _releaseAt > ReleaseFreshMs
            || !IsOnTabStrip(top, _releasePoint, tabs))
        {
            return false;
        }

        _releaseTop = null; // 한 번의 놓기로 한 번만 쓴다
        if (list[^1] is var last && !ReferenceEquals(last, dockable))
        {
            factory.MoveDockable(tabs, dockable, last); // 같은 탭 영역 안 이동 — 마지막 자리로(FactoryBase.MoveDockable(dock, source, target))
        }

        factory.SetActiveDockable(dockable);
        factory.RaisePanelTabsChanged(tabs.Id);
        Reordered?.Invoke(dockable.Id, tabs.Id);
        return true;
    }

    /// <summary>
    /// 점이 그 탭 영역의 보이는 탭 줄 요소(DocumentTabStrip#PART_TabStrip, DataContext = 탭 영역) 경계 안인가(team-lead: 요소 경계로 판정).
    /// Dock Fluent DocumentControl 템플릿은 탭 줄을 PART_DockPanel 의 첫 자식으로 위쪽에 붙이므로(DockPanel.Dock, HorizontalAlignment 미지정)
    /// 탭 줄 요소가 패널 폭 전체를 차지해 마지막 탭 오른쪽 빈 머리줄도 이 경계 안이다(근거: GitHub master DocumentControl.axaml — 12.1.0.12 판과 같은지는 미확인).
    /// </summary>
    private static bool IsOnTabStrip(TopLevel top, Point point, PanelDocumentDock tabs) =>
        top.GetVisualDescendants().OfType<DocumentTabStrip>()
            .Where(s => ReferenceEquals(s.DataContext, tabs) && s.IsEffectivelyVisible)
            .Any(s => s.TranslatePoint(default, top) is { } origin && new Rect(origin, s.Bounds.Size).Contains(point));
}
