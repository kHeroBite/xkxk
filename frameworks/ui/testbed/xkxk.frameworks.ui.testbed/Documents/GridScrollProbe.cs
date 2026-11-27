// 그리드 터치 스크롤 진단 계수기(D-SC2·D-TP1, 진단 빌드 전용 초안) — 레이아웃 사이 ScrollGesture 수·delta 합과 포인터 이동 1건의 .NET 처리 시간을 잰다. 동작은 바꾸지 않는다
#if PD_DIAGNOSTICS
using xkxk.frameworks.ui.shell;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using xkxk.frameworks.ui.shell.Diagnostics;

namespace xkxk.frameworks.ui.testbed.Documents;

/// <summary>
/// 확인할 것 두 가지다.
/// ① D-SC2 H1: DataGrid.UpdateScroll 은 PendingVerticalScrollHeight 에 delta 를 '대입'한다. 그래서 다음 측정 전에 ScrollGesture 가 2번 넘게 오면 앞의 것이 사라진다.
///    애니메이션 프레임(레이아웃 직전) 사이의 제스처 수와, 버려졌을 delta 합(마지막 것을 뺀 나머지)을 센다.
/// ② D-TP1: 포인터 이동 1건의 .NET 처리 시간을 잰다. TopLevel Tunnel 클래스 처리기에서 Bubble(handledEventsToo)까지다.
///    제스처의 첫 이동과 나머지 이동을 나눠 최대값을 낸다. JS 계수기의 handlerMs 와 대조하는 데 쓴다.
/// 관찰만 한다. Handled 를 바꾸지 않고 이벤트를 다시 보내지 않는다.
/// 결과는 perf.get metrics 의 grid_probe_* 로 본다. 합계는 누적값이라 swipe 앞뒤 차이로 읽고, *_max 는 회마다 앱을 다시 띄워 읽는다.
/// </summary>
internal static class GridScrollProbe
{
    private static bool s_installed;
    private static int s_inFrame;          // 이번 프레임(직전 애니메이션 프레임 이후)의 ScrollGesture 수
    private static double s_frameLostDy;   // 이번 프레임에서 덮어써졌을 delta(마지막 것을 뺀 합)
    private static double s_frameLastDy;
    private static bool s_rafQueued;
    private static long s_moveStart;
    private static int s_moveIndex;        // 누름 이후 이동 순번(0 = 첫 이동)
    private static long s_gestures;        // 누적값(perf.reset 은 PerfProbe 쪽만 비우므로 앱을 다시 띄우거나 앞뒤 차이로 읽는다)
    private static double s_sumDy;
    private static long s_multiFrames;
    private static double s_lostDy;

    public static void Install()
    {
        if (s_installed)
        {
            return;
        }

        s_installed = true;
        GridRouteProbe.Install(); // D-PF2 M2 라우팅 구간 계측(GridRouteProbe.cs, 중복 호출 무시)
        InputElement.ScrollGestureEvent.AddClassHandler<DataGridRowsPresenter>(OnGesture, handledEventsToo: true);
        InputElement.PointerPressedEvent.AddClassHandler<TopLevel>((_, e) => { if (e.Pointer.Type == PointerType.Touch) { s_moveIndex = 0; } },
            RoutingStrategies.Tunnel, handledEventsToo: true);
        InputElement.PointerMovedEvent.AddClassHandler<TopLevel>((_, e) => { if (e.Pointer.Type == PointerType.Touch) { s_moveStart = Stopwatch.GetTimestamp(); } },
            RoutingStrategies.Tunnel, handledEventsToo: true);
        InputElement.PointerMovedEvent.AddClassHandler<TopLevel>(OnMoveEnd, RoutingStrategies.Bubble, handledEventsToo: true);
    }

    private static void OnMoveEnd(TopLevel top, PointerEventArgs e)
    {
        if (e.Pointer.Type != PointerType.Touch || s_moveStart == 0)
        {
            return;
        }

        var ms = Stopwatch.GetElapsedTime(s_moveStart).TotalMilliseconds;
        s_moveStart = 0;
        var p = PerfProbe.Current;
        p.SetMetricMax(s_moveIndex == 0 ? "grid_probe_move_first_ms_max" : "grid_probe_move_rest_ms_max", ms);
        p.SetMetric("grid_probe_move_last_ms", ms);
        p.SetMetric("grid_probe_move_intermediate_last", e.GetIntermediatePoints(top).Count);
        s_moveIndex++;
    }

    private static void OnGesture(DataGridRowsPresenter presenter, ScrollGestureEventArgs e)
    {
        var p = PerfProbe.Current;
        p.SetMetric("grid_probe_gestures", ++s_gestures);
        p.SetMetric("grid_probe_sum_dy", s_sumDy += e.Delta.Y);
        if (s_inFrame > 0)
        {
            s_frameLostDy += s_frameLastDy; // 앞 제스처의 대기값은 이번 대입으로 덮어써진다
        }

        s_inFrame++;
        s_frameLastDy = e.Delta.Y;
        if (!s_rafQueued && TopLevel.GetTopLevel(presenter) is { } top)
        {
            s_rafQueued = true;
            top.RequestAnimationFrame(_ => EndFrame());
        }
    }

    private static void EndFrame()
    {
        var p = PerfProbe.Current;
        p.SetMetricMax("grid_probe_per_frame_max", s_inFrame);
        if (s_inFrame > 1)
        {
            p.SetMetric("grid_probe_multi_frames", ++s_multiFrames);
            p.SetMetric("grid_probe_lost_dy", s_lostDy += s_frameLostDy);
        }

        s_inFrame = 0;
        s_frameLostDy = 0;
        s_rafQueued = false;
    }
}
#endif
