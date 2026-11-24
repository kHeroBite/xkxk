// MDI 창 끌기 중 포인터 이동을 렌더 프레임당 1회로 합치는 옵션 처리기(01a K5, MdiMoveLight.CoalescePointer, 기본 끔 — 끄면 지금 경로 그대로)
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Dock.Avalonia.Controls;

namespace xkxk.frameworks.ui.shell.Docking;

/// <summary>
/// 근거(디컴파일 Dock.Avalonia 12.1.0.12 MdiDocumentWindow): 창은 PointerMoved 를 Bubble·handledEventsToo=false 로 받아 이동마다
/// DragMove(GetDragBounds·자석·MdiBounds·InvalidateMdiLayout)를 한다. 놓기 처리기는 DragMove 를 하지 않는다.
/// 켬이면: 창이 포인터를 캡처한 동안(이동·크기 조절) TopLevel 터널에서 이동을 Handled 로 표시해 Dock 에 가지 않게 하고 좌표만 기억한다.
/// 다음 렌더 직전(TopLevel.RequestAnimationFrame)에 마지막 좌표 1건을 같은 창에 다시 올려 Dock·자석·스냅이 그 좌표를 받게 한다.
/// 놓기·키 입력 직전에는 남은 좌표를 동기로 먼저 올린다(놓기 처리기가 마지막 좌표를 받은 창 위치로 끝나게). 캡처가 풀렸거나 다른 요소로 갔으면 버린다.
/// Pd 의 handledEventsToo 처리기(스냅·탭 합류·패널 합류 등)는 원본 이동을 지금처럼 모두 받고, 프레임당 재생 1건을 더 받는다(같은 좌표).
/// </summary>
internal static class MdiMovePointerCoalesce
{
    private static MdiDocumentWindow? s_window;
    private static TopLevel? s_top;
    private static IPointer? s_pointer;
    private static Point s_pos; // TopLevel 좌표
    private static PointerPointProperties s_props = PointerPointProperties.None;
    private static KeyModifiers s_mods;
    private static ulong s_stamp;
    private static bool s_pending;
    private static bool s_frameRequested;
    private static bool s_replaying;

#pragma warning disable CA2255 // 등록 파일(테마·App)이 배정 밖이라 어셈블리 적재 때 처리기를 붙인다(MdiTabJoinDrag·ManagedSdiTabJoin 과 같은 방식)
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Install()
    {
        InputElement.PointerMovedEvent.AddClassHandler<TopLevel>(OnMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        InputElement.PointerReleasedEvent.AddClassHandler<TopLevel>((_, _) => Flush(Kind.Release), RoutingStrategies.Tunnel, handledEventsToo: true);
        InputElement.KeyDownEvent.AddClassHandler<TopLevel>((_, _) => Flush(Kind.Release), RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    private enum Kind { Frame, Release }

    private static void OnMoved(TopLevel top, PointerEventArgs e)
    {
        if (s_replaying) { return; } // 자기 재생은 그대로 흘린다
        if (!MdiMoveLight.CoalescePointer || e.Pointer.Captured is not MdiDocumentWindow w || !ReferenceEquals(e.Source, w))
        {
            Flush(Kind.Release); // 끔으로 바뀌었거나 캡처 끌기가 아니면 남은 좌표부터 반영(대개 없음 — 즉시 끝)
            return;
        }

        if (s_pending && (!ReferenceEquals(s_window, w) || !ReferenceEquals(s_pointer, e.Pointer)))
        {
            Flush(Kind.Release);
        }

        (s_window, s_top, s_pointer) = (w, top, e.Pointer);
        s_pos = e.GetPosition(top);
        s_props = e.GetCurrentPoint(top).Properties;
        (s_mods, s_stamp, s_pending) = (e.KeyModifiers, e.Timestamp, true);
        e.Handled = true; // Dock(handledEventsToo=false)은 이번 이동을 건너뛴다 — 다음 프레임에 마지막 좌표로 1회
#if PD_DIAGNOSTICS
        MdiMoveLight.NoteCoalesce(held: 1);
#endif
        if (!s_frameRequested)
        {
            s_frameRequested = true;
            top.RequestAnimationFrame(_ =>
            {
                s_frameRequested = false;
                Flush(Kind.Frame);
            });
        }
    }

    private static void Flush(Kind kind)
    {
        if (!s_pending) { return; }
        s_pending = false;
        var (w, top, pointer) = (s_window, s_top, s_pointer);
        (s_window, s_top, s_pointer) = (null, null, null);
        if (w is null || top is null || pointer is null || !ReferenceEquals(pointer.Captured, w))
        {
#if PD_DIAGNOSTICS
            MdiMoveLight.NoteCoalesce(dropped: 1); // 캡처가 풀렸다(놓기·Esc 취소 뒤) — Dock 끌기도 끝났으므로 버린다
#endif
            return;
        }

        var args = new PointerEventArgs(InputElement.PointerMovedEvent, w, pointer, top, s_pos, s_stamp, s_props, s_mods);
        s_replaying = true;
        MdiMoveLight.CoalesceReplaying = true; // 측정 코드가 다시 보낸 이동을 입력으로 세지 않게 거를 수 있다(01a K5)
        try
        {
            w.RaiseEvent(args);
        }
        finally
        {
            s_replaying = false;
            MdiMoveLight.CoalesceReplaying = false;
        }
#if PD_DIAGNOSTICS
        if (kind == Kind.Frame) { MdiMoveLight.NoteCoalesce(replayed: 1); } else { MdiMoveLight.NoteCoalesce(flushed: 1); }
#else
        _ = kind; // 제품 빌드는 카운터가 없다
#endif
    }
}
