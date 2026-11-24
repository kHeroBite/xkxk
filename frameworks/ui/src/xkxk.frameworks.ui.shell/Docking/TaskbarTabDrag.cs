// 작업표시줄 탭 끌기(Q15 후속) — 탭을 끌어 탐색기·로그 패널 위에 놓으면 문서가 그 패널 탭으로 합류한다(합류 판정·강조는 odev-1 PanelJoinDrag)
using xkxk.frameworks.ui.shell;
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using xkxk.frameworks.ui.shell.Docking;

namespace xkxk.frameworks.ui.shell.Docking;

/// <summary>
/// 규칙: 왼쪽 버튼(터치는 400ms 길게 누름)으로 누른 탭에서 PanelJoinDrag.MinDragDistance 이상 움직이면 끌기를 시작한다.
/// 끄는 동안 PanelJoinDrag.UpdateHoverAt 이 대상 패널에 pdJoinTarget 강조를 붙이고, 놓으면 TryJoinAt 이 합류를 판정한다
/// (같은 패널 위 250ms 이상 머물러야 함). 놓은 곳이 패널이 아니거나 Esc·캡처 잃음이면 아무것도 바뀌지 않는다.
/// 끌기가 시작되면 탭 클릭(활성화/최소화)은 일어나지 않고, 끌기 전의 클릭·우클릭 메뉴는 그대로다.
/// </summary>
public sealed class TaskbarTabDrag
{
    private static readonly TimeSpan TouchHold = TimeSpan.FromMilliseconds(400);

    private readonly Control _host;
    private PocDocument? _doc;
    private Point _start;
    private DateTime _pressedAt;
    private bool _touch;
    private bool _dragging;

    private TaskbarTabDrag(Control host)
    {
        _host = host;
        host.AddHandler(InputElement.PointerPressedEvent, OnPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        // 목록·항목·버튼이 이동·놓기를 처리됨(Handled)으로 표시해도 받는다(odev-1 지적 ①)
        host.AddHandler(InputElement.PointerMovedEvent, OnMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        host.AddHandler(InputElement.PointerReleasedEvent, OnReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        // 작업표시줄 자신이 캡처를 잃을 때만 취소한다. 끌기 시작 때 캡처를 하위 요소(탭 버튼·글자)에서 작업표시줄로 옮기면
        // 하위 요소의 캡처 잃음이 여기까지 올라오는데, 그것까지 취소하면 끌기가 곧바로 끊겨 합류가 일어나지 않았다(웹 Q15 i0136, odev-1 분석).
        host.AddHandler(InputElement.PointerCaptureLostEvent, (_, e) =>
        {
            if (ReferenceEquals(e.Source, _host))
            {
                Cancel();
            }
        }, RoutingStrategies.Bubble);
        host.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
    }

    /// <summary>작업표시줄(탭 목록)에 끌기를 붙인다.</summary>
    public static TaskbarTabDrag Attach(Control taskbar) => Current = new(taskbar);

    private void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(_host);
        _doc = point.Properties.IsLeftButtonPressed ? FindDocument(e.Source as Visual) : null;
        _start = point.Position;
        _pressedAt = DateTime.UtcNow;
        _touch = e.Pointer.Type == PointerType.Touch;
        _dragging = false;
        _last = "pressed";
    }

    /// <summary>
    /// 누른 요소에서 작업표시줄까지 올라가며 처음 만나는 문서. 제목 줄 구조(Grid shell.titleStrip 안 ListBox TaskbarTabs)에서 탭 항목 루트는 Grid,
    /// 제목 버튼 내용은 TextBlock 이라 DataContext 가 문서로 상속된다. 그래도 눌린 요소 자신만 보지 않고 위로 올라가 찾는다
    /// (옛 문자열 내용 버튼은 ContentPresenter 자식의 DataContext 가 문자열이라 끌기가 시작되지 않았다 — 웹 Q15 i0215, odev-1 지적 ②).
    /// 탭 폭이 가변(TitleTabPanel, 간격 2)이어도 좌표는 작업표시줄(ListBox) 기준이라 영향이 없다. 오른쪽 독립 창 아이콘(SdiIcons)은 이 목록 밖이라 끌기가 붙지 않는다.
    /// </summary>
    private PocDocument? FindDocument(Visual? source)
    {
        for (var v = source; v is not null && !ReferenceEquals(v, _host); v = v.GetVisualParent())
        {
            if (v is StyledElement { DataContext: PocDocument doc })
            {
                return doc;
            }
        }

        return null;
    }

    private string _last = "none";

    /// <summary>진단 상태(원격 joinProbe 용): 문서·끌기 중·터치·마지막 이벤트.</summary>
    public string State => $"doc={_doc?.Id ?? "null"} dragging={_dragging} touch={_touch} last={_last}";

    /// <summary>마지막으로 붙인 작업표시줄 끌기(진단 조회용 — 메인 창 1개).</summary>
    public static TaskbarTabDrag? Current { get; private set; }

    private void OnMoved(object? sender, PointerEventArgs e)
    {
        if (_doc is null || TopLevel.GetTopLevel(_host) is not { } top)
        {
            return;
        }

        if (!_dragging)
        {
            var d = e.GetPosition(_host) - _start;
            var far = Math.Sqrt((d.X * d.X) + (d.Y * d.Y)) >= PanelJoinDrag.MinDragDistance;
            if (!far)
            {
                return;
            }

            if (_touch && DateTime.UtcNow - _pressedAt < TouchHold)
            {
                _doc = null; // 터치는 길게 누르지 않고 움직이면 스크롤로 본다
                _last = "touch-scroll";
                return;
            }

            _dragging = true;
            e.Pointer.Capture(_host); // 탭 버튼의 클릭이 일어나지 않게 입력을 가져온다
        }

        _last = "hover:" + (PanelJoinDrag.UpdateHoverAt(top, e.GetPosition(top)) ?? "none");
        e.Handled = true;
    }

    private void OnReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_dragging || _doc is not { } doc || TopLevel.GetTopLevel(_host) is not { } top)
        {
            _doc = null;
            return;
        }

        var point = e.GetPosition(top);
        var joined = PanelJoinDrag.TryJoinAt(doc, top, point); // 합류하면 PanelJoinDrag.Joined 이벤트·실제 이동은 Dispatcher 로(odev-1)
        End(top);
        _last = joined ? "joined" : "dropped-no-join";
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (_dragging && e.Key == Key.Escape)
        {
            Cancel();
            e.Handled = true;
        }
    }

    private void Cancel()
    {
        if (_dragging && TopLevel.GetTopLevel(_host) is { } top)
        {
            End(top);
            _last = "cancelled";
        }

        _doc = null;
    }

    private void End(TopLevel top)
    {
        PanelJoinDrag.UpdateHoverAt(top, (Point?)null); // 강조 지우기
        _dragging = false;
        _doc = null;
    }
}
