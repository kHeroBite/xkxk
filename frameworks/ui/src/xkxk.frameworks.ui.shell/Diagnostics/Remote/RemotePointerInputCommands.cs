// 원격 앱 내부 포인터 입력(진단) — grid.clickRow·ui.pointerClick·ui.pointerDrag·ui.pointerMove. 대상 점을 히트 테스트해 포인터 이벤트를 RaiseEvent 한다(OS 입력·전경 변화 없음)
#if PD_DIAGNOSTICS // 진단 빌드 전용(PdDiagnostics=true)
using System;
using System.Collections;
using System.Linq;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace xkxk.frameworks.ui.shell.Diagnostics.Remote;

internal static class RemotePointerInputCommands
{
    /// <summary>
    /// 배경(team-lead 2026-10-30): L2 PostMessage 클릭이 앱을 사용자 화면 맨 앞으로 올려 금지됐다. 그래서 포인터 경로가 필요한 시험이 막혔다.
    /// GV1-D 행 클릭, 크롬 W13 캡션 실클릭 계측(CaptionClickTiming 은 PointerReleased 만 본다), W14 잠금 시 제목줄 끌기(MdiLockDragGuard 는 PointerPressed 터널)가 그 예다.
    /// ui.click(UIA Invoke)·grid.select(SelectedIndex)·docs.move(MdiBounds 직접 계산)는 포인터 이벤트를 만들지 않아 이 경로를 대신하지 못한다.
    /// 여기서는 Avalonia 12.1.4 공개 생성자로 만든 이벤트를 히트 요소에 올린다(터널·버블 라우팅은 실입력과 같다). 다른 점은 :pointerover·OS 포커스 이동이 없다는 것이다.
    /// </summary>
    internal static void Register(RemoteCommandRouter r)
    {
        RemoteWheelCommands.Register(r); // 수식키+휠 input.wheel·dense.state(RemoteWheelCommands.cs)
        r.Register("grid.clickRow",
            "그리드 행 클릭(앱 내부 포인터) index=<행 번호(보이는 순서)> [col=<표시 순서 열, 기본 0>] [button=left|right] [count=1|2] 대상 id=|dock= [doc] " +
            "→ {target, handled{pressed,released}, before, after}", a =>
            {
                var grid = RemoteScrollCommands.GridOf(a);
                var index = a.Int("index") ?? throw new RemoteCommandException("index= 가 필요합니다.");
                if (grid.ItemsSource is not IList items || index < 0 || index >= items.Count)
                {
                    throw new RemoteCommandException($"index 범위 밖입니다(0~{(grid.ItemsSource as IList)?.Count - 1}).");
                }

                var before = RemoteScrollCommands.GridState(grid);
                var (top, point, hit) = HitCenter(RealizedCell(grid, items[index], a.Int("col") ?? 0));
                var o = RaiseClick(hit, top, point, IsRight(a), a.Int("count") ?? 1);
                grid.UpdateLayout();
                o["before"] = before;
                o["after"] = RemoteScrollCommands.GridState(grid);
                return o;
            });
        r.Register("ui.pointerClick",
            "컨트롤 가운데 클릭(앱 내부 포인터 — ui.click 과 달리 PointerPressed·Released 를 만든다) id=|dock= [doc] [button=left|right] [count=1|2] → {target, handled{pressed,released}}",
            a =>
            {
                var (top, point, hit) = HitCenter(RemoteUiTargets.Target(a));
                return RaiseClick(hit, top, point, IsRight(a), a.Int("count") ?? 1);
            });
        r.Register("ui.pointerDrag",
            "컨트롤 가운데에서 왼쪽 버튼 끌기(앱 내부 포인터) id=|dock= [doc] dx= dy=(DIP) [steps=10] → {target, handled{pressed,released}, captured}"
            + " — 이동 결과는 chrome.state 의 mdiBounds 등으로 전후 비교",
            a =>
            {
                var (top, point, hit) = HitCenter(RemoteUiTargets.Target(a));
                return RaiseDrag(hit, top, point, new Vector(a.Num("dx") ?? 0, a.Num("dy") ?? 0), Math.Clamp(a.Int("steps") ?? 10, 1, 100));
            });
        r.Register("ui.pointerMove",
            "포인터 올리기(앱 내부 — 버튼 없음 PointerEntered·PointerMoved, 툴팁·호버 캡처용) id=|dock= [doc] [x= y=(대상 컨트롤 좌표 DIP, 생략 시 가운데)] [leave=true 면 PointerExited 로 떠남]"
            + " → {target, point, entered[], handled}. 한계: :pointerover 의사 클래스·Avalonia ToolTip.Tip 는 입력 관리자 상태라 이 명령으로 켜지지 않을 수 있다[추정]."
            + " 차트가 PointerMoved 로 자체 툴팁을 그리는지는 라이브러리마다 다르다[추정 — 캡처로 판정]",
            a => RaiseHover(RemoteUiTargets.Target(a), a.Num("x"), a.Num("y"), a.Bool("leave")));
    }

    /// <summary>
    /// 대상 컨트롤 좌표의 점에 버튼 없는 포인터를 올린다: 히트 요소부터 대상까지 들어간 요소마다 PointerEntered(직접 이벤트), 히트 요소에 PointerMoved(버블).
    /// leave=true 면 같은 요소들에 PointerExited 를 보낸다(실입력에서 포인터가 나갈 때와 같은 순서 — 안쪽부터).
    /// </summary>
    private static JsonObject RaiseHover(Control c, double? x, double? y, bool leave)
    {
        var top = TopLevel.GetTopLevel(c) ?? throw new RemoteCommandException("대상이 화면 트리에 없습니다.");
        var local = new Point(x ?? c.Bounds.Width / 2, y ?? c.Bounds.Height / 2);
        var point = c.TranslatePoint(local, top) ?? throw new RemoteCommandException("대상 좌표를 계산하지 못했습니다.");
        var hit = top.InputHitTest(point) as Interactive;
        if (hit is not Visual hv || !(ReferenceEquals(hv, c) || hv.GetVisualAncestors().Contains(c)))
        {
            throw new RemoteCommandException($"그 점을 다른 요소가 가립니다: {hit?.GetType().Name ?? "없음"}(x·y 가 대상 안인지 확인).");
        }

        using var pointer = new Pointer(RemotePointerId, PointerType.Mouse, true);
        var none = new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.Other);
        var chain = hv.GetSelfAndVisualAncestors().TakeWhile(v => !ReferenceEquals(v, c)).Append(c).OfType<Interactive>().ToList();
        var crossing = leave ? InputElement.PointerExitedEvent : InputElement.PointerEnteredEvent;
        foreach (var el in leave ? chain : Enumerable.Reverse(chain)) // 들어갈 때는 바깥부터, 나갈 때는 안쪽부터
        {
            el.RaiseEvent(new PointerEventArgs(crossing, el, pointer, top, point, Stamp(), none, KeyModifiers.None));
        }

        var handled = false;
        if (!leave)
        {
            var move = new PointerEventArgs(InputElement.PointerMovedEvent, hit, pointer, top, point, Stamp(), none, KeyModifiers.None);
            hit.RaiseEvent(move);
            handled = move.Handled;
        }

        return new JsonObject
        {
            ["target"] = hit.GetType().Name, ["point"] = new JsonObject { ["x"] = local.X, ["y"] = local.Y },
            ["entered"] = new JsonArray(chain.Select(e => (JsonNode?)JsonValue.Create(e.GetType().Name)).ToArray()), ["handled"] = handled, ["leave"] = leave,
        };
    }

    private static bool IsRight(JsonObject a) => string.Equals(a.Str("button"), "right", StringComparison.OrdinalIgnoreCase);

    /// <summary>컨트롤 가운데 점(TopLevel 좌표)과 그 점의 히트 요소. 히트 요소가 컨트롤 자신·자손이 아니면(가림) 오류.</summary>
    private static (TopLevel Top, Point Point, Interactive Hit) HitCenter(Control c)
    {
        var top = TopLevel.GetTopLevel(c) ?? throw new RemoteCommandException("대상이 화면 트리에 없습니다.");
        var point = c.TranslatePoint(new Point(c.Bounds.Width / 2, c.Bounds.Height / 2), top)
            ?? throw new RemoteCommandException("대상 좌표를 계산하지 못했습니다.");
        var hit = top.InputHitTest(point) as Interactive;
        if (hit is not Visual hv || !(ReferenceEquals(hv, c) || hv.GetVisualAncestors().Contains(c)))
        {
            throw new RemoteCommandException($"대상 가운데를 다른 요소가 가립니다: {hit?.GetType().Name ?? "없음"}(오버레이·팝업·IsHitTestVisible 확인).");
        }

        return (top, point, hit);
    }

    /// <summary>행을 보이게 한 뒤(ScrollIntoView) 실현된 행의 표시 순서 col 번째 셀. 열 → 셀은 GetCellContent 의 조상 셀로 찾는다.</summary>
    private static DataGridCell RealizedCell(DataGrid grid, object? item, int col)
    {
        var column = grid.Columns.Where(c => c.IsVisible).OrderBy(c => c.DisplayIndex).ElementAtOrDefault(col)
            ?? throw new RemoteCommandException($"col 범위 밖입니다(보이는 열 {grid.Columns.Count(c => c.IsVisible)}개).");
        grid.ScrollIntoView(item, column);
        grid.UpdateLayout();
        var row = grid.GetVisualDescendants().OfType<DataGridRow>().FirstOrDefault(r => ReferenceEquals(r.DataContext, item) && r.IsVisible)
            ?? throw new RemoteCommandException("행이 실현되지 않았습니다(가상화).");
        // 내용 없는 셀(그리는 글자 셀 K15·화면 밖 지연 셀)은 GetCellContent 가 null 이다 — 셀 표시자 자식 순서 = Columns 순서로 찾는다(VisibleColumns 와 같은 기준)
        return column.GetCellContent(row)?.GetVisualAncestors().OfType<DataGridCell>().FirstOrDefault()
            ?? row.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.DataGridCellsPresenter>().FirstOrDefault()?.Children.OfType<DataGridCell>().ElementAtOrDefault(grid.Columns.IndexOf(column))
            ?? throw new RemoteCommandException("셀을 찾지 못했습니다.");
    }

    /// <summary>누름 → 놓음 1쌍(count=2 면 두 번째 누름의 ClickCount=2). 포인터 캡처는 끝에 푼다.</summary>
    private static JsonObject RaiseClick(Interactive target, TopLevel top, Point point, bool right, int count)
    {
        using var pointer = new Pointer(RemotePointerId, PointerType.Mouse, true);
        bool pressed = false, released = false;
        for (var n = 1; n <= Math.Clamp(count, 1, 2); n++)
        {
            pressed |= Press(target, pointer, top, point, right, n);
            released |= Release(target, pointer, top, point, right);
        }

        pointer.Capture(null);
        return new JsonObject { ["target"] = target.GetType().Name, ["handled"] = new JsonObject { ["pressed"] = pressed, ["released"] = released } };
    }

    /// <summary>누름 → steps 번 이동(버튼 누른 채) → 놓음. 이동·놓음은 캡처한 요소가 있으면 그 요소, 없으면 그 점의 히트 요소로 보낸다(실입력 라우팅과 같다).</summary>
    private static JsonObject RaiseDrag(Interactive target, TopLevel top, Point start, Vector delta, int steps)
    {
        using var pointer = new Pointer(RemotePointerId, PointerType.Mouse, true);
        var pressed = Press(target, pointer, top, start, false, 1);
        if (pointer.Captured is null) { pointer.Capture(target as IInputElement); } // 처리기가 캡처하지 않았으면 실입력의 누름 시 암시적 캡처처럼 누른 요소에 잡는다[추정 — 실기 대조 필요]
        var held = new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.Other);
        var p = start;
        for (var i = 1; i <= steps; i++)
        {
            p = start + delta * ((double)i / steps);
            var to = pointer.Captured as Interactive ?? top.InputHitTest(p) as Interactive ?? target;
            to.RaiseEvent(new PointerEventArgs(InputElement.PointerMovedEvent, to, pointer, top, p, Stamp(), held, KeyModifiers.None));
        }

        var captured = pointer.Captured?.GetType().Name;
        var end = pointer.Captured as Interactive ?? top.InputHitTest(p) as Interactive ?? target;
        var released = Release(end, pointer, top, p, false);
        pointer.Capture(null);
        return new JsonObject
        {
            ["target"] = target.GetType().Name, ["captured"] = captured,
            ["handled"] = new JsonObject { ["pressed"] = pressed, ["released"] = released },
        };
    }

    private static bool Press(Interactive to, Pointer pointer, TopLevel top, Point p, bool right, int clickCount)
    {
        var props = new PointerPointProperties(right ? RawInputModifiers.RightMouseButton : RawInputModifiers.LeftMouseButton,
            right ? PointerUpdateKind.RightButtonPressed : PointerUpdateKind.LeftButtonPressed);
        var e = new PointerPressedEventArgs(to, pointer, top, p, Stamp(), props, KeyModifiers.None, clickCount);
        to.RaiseEvent(e);
        return e.Handled;
    }

    private static bool Release(Interactive to, Pointer pointer, TopLevel top, Point p, bool right)
    {
        var props = new PointerPointProperties(RawInputModifiers.None, right ? PointerUpdateKind.RightButtonReleased : PointerUpdateKind.LeftButtonReleased);
        var e = new PointerReleasedEventArgs(to, pointer, top, p, Stamp(), props, KeyModifiers.None, right ? MouseButton.Right : MouseButton.Left);
        to.RaiseEvent(e);
        return e.Handled;
    }

    private static ulong Stamp() => (ulong)Environment.TickCount64;

    /// <summary>원격 합성 포인터 Id(실제 장치 Id 와 겹치지 않게 큰 값).</summary>
    private const int RemotePointerId = 0x5D_0000;
}
#endif
