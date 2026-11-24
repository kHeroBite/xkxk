// 그리드 터치 스크롤 제스처를 프레임당 1번으로 합치는 우회(D-SC1) — DataGrid 12.1.2 UpdateScroll 이 대기 스크롤을 대입해 측정 사이 여러 제스처 중 마지막만 반영되던 손실을 막는다
using xkxk.frameworks.ui.shell;
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
#if PD_DIAGNOSTICS
using xkxk.frameworks.ui.shell.Diagnostics;
#endif

namespace xkxk.frameworks.ui.views.Documents;

/// <summary>
/// 원인(원본 확인): ScrollGestureRecognizer 는 포인터 이동마다 ScrollGesture 를 내고, DataGridRowsPresenter.OnScrollGesture → DataGrid.UpdateScroll 은
/// DisplayData.PendingVerticalScrollHeight 에 이번 delta 를 '대입'한다(DataGrid.cs:2349). 대기값은 다음 측정(OnRowsMeasure)에서 한 번 소비되므로,
/// 한 프레임에 제스처가 N번 오면 마지막 1번만 스크롤됐다(Fold5 adb swipe 7행 → 2행).
/// 우회: DataGridRowsPresenter 의 ScrollGesture 를 클래스 처리기(같은 요소의 인스턴스 처리기보다 먼저 — EventRoute.RaiseEventImpl 의 InvokeRaised)로 받아
/// delta 를 누적하고 Handled 로 둔다(OnScrollGesture 는 e.Handled || UpdateScroll 이라 단락된다). 그 프레임의 애니메이션 프레임 콜백
/// (MediaContext.RenderCore: Clock.Pulse → 그다음 레이아웃)에서 합친 delta 로 새 ScrollGestureEventArgs 를 1번 다시 보낸다.
/// 마우스 휠(OnPointerWheelChanged → UpdateScroll 직접)·스크롤바 경로는 ScrollGesture 를 타지 않으므로 영향이 없다.
/// 안전장치: ① 클래스 처리기가 인스턴스 처리기보다 늦게 불린 흔적(이미 Handled)이 보이면 즉시 끄고 원래 경로로 둔다
/// ② 누적이 FlushTimeoutMs 안에 소비되지 않으면(RAF 가정 깨짐) 즉시 내보내고 끈다 ③ 직전 내보내기가 스크롤하지 못했으면(끝 도달) 같은 제스처는 그대로 통과시켜
/// 관성 종료·바깥 스크롤 연쇄를 원래대로 둔다.
/// </summary>
internal static class GridScrollCoalescer
{
    private const double FlushTimeoutMs = 100;

    private static bool s_installed;
    private static bool s_disabled;
    private static bool s_reraising;

    private static DataGridRowsPresenter? s_target;
    private static int s_gestureId;
    private static Vector s_sum;
    private static int s_count;
    private static long s_firstTick;
    private static int s_edgeGestureId = -1;
    private static long s_seq; // 누적 묶음 번호(제한 시간 검사용)

#if PD_DIAGNOSTICS
    // 진단(perf.get metrics: grid_scroll_coalesce_*)
    private static long s_flushes;
    private static long s_events;
    private static int s_maxPerFlush;
#endif

    public static string? DisabledReason { get; private set; }

    /// <summary>앱 시작 때 1회(GridDocumentView 생성자에서 호출, 중복 무시).</summary>
    public static void Install()
    {
        if (s_installed)
        {
            return;
        }

        s_installed = true;
        InputElement.ScrollGestureEvent.AddClassHandler<DataGridRowsPresenter>(OnScrollGesture, handledEventsToo: true);
    }

    private static void OnScrollGesture(DataGridRowsPresenter presenter, ScrollGestureEventArgs e)
    {
        if (s_reraising || s_disabled)
        {
            return; // 우리가 다시 보낸 이벤트, 또는 꺼진 상태 — 원래 처리기로
        }

        if (e.Handled)
        {
            Disable("class-handler-after-instance"); // 순서 가정 깨짐(이미 처리됨) — 이중 스크롤 방지를 위해 끈다
            return;
        }

        if (e.Id == s_edgeGestureId)
        {
            return; // 이 제스처는 끝에 닿았다 — 원래대로 통과(관성 종료·연쇄)
        }

        var now = Environment.TickCount64;
        if (s_count > 0)
        {
            if (!ReferenceEquals(presenter, s_target) || e.Id != s_gestureId)
            {
                Flush(); // 다른 그리드·다른 제스처 — 앞 누적을 먼저 내보낸다
            }
            else if (now - s_firstTick > FlushTimeoutMs)
            {
                Flush();
                Disable("raf-timeout"); // 애니메이션 프레임 콜백이 오지 않았다 — 끄고 이번 이벤트는 원래 경로로
                return;
            }
        }

        if (s_count == 0)
        {
            s_target = presenter;
            s_gestureId = e.Id;
            s_firstTick = now;
            if (TopLevel.GetTopLevel(presenter) is { } top)
            {
                var seq = ++s_seq;
                top.RequestAnimationFrame(_ => Flush());
                // 안전장치 ②: 다음 이벤트가 없어도 누적이 남지 않게 — 제한 시간 뒤에도 같은 누적이 남아 있으면 내보내고 끈다
                DispatcherTimer.RunOnce(() =>
                {
                    if (s_count > 0 && seq == s_seq)
                    {
                        Flush();
                        Disable("raf-timeout");
                    }
                }, TimeSpan.FromMilliseconds(FlushTimeoutMs), DispatcherPriority.Background);
            }
            else
            {
                return; // 트리 밖 — 원래 경로
            }
        }

        s_sum += e.Delta;
        s_count++;
#if PD_DIAGNOSTICS
        s_events++;
#endif
        e.Handled = true; // 원래 처리기(OnScrollGesture)를 단락. 인식기는 Handled 를 보고 포인터를 캡처한다(원래 스크롤 시와 같음)
    }

    private static void Flush()
    {
        var target = s_target;
        var sum = s_sum;
        var count = s_count;
        var id = s_gestureId;
        s_target = null;
        s_sum = default;
        s_count = 0;
        if (target is null || count == 0)
        {
            return;
        }

        var args = new ScrollGestureEventArgs(id, sum);
        s_reraising = true;
        try
        {
            target.RaiseEvent(args);
        }
        finally
        {
            s_reraising = false;
        }

        if (!args.Handled)
        {
            s_edgeGestureId = id; // 스크롤하지 못함(끝) — 같은 제스처의 다음 이벤트는 통과
        }

#if PD_DIAGNOSTICS
        s_flushes++;
        if (count > s_maxPerFlush)
        {
            s_maxPerFlush = count;
        }
#endif

#if PD_DIAGNOSTICS // 진단 빌드 전용 계수기(제품 동작은 누적·병합만)
        var perf = PerfProbe.Current;
        perf.SetMetric("grid_scroll_coalesce_flushes", s_flushes);
        perf.SetMetric("grid_scroll_coalesce_events", s_events);
        perf.SetMetric("grid_scroll_coalesce_last_count", count);
        perf.SetMetric("grid_scroll_coalesce_last_dy", Math.Round(sum.Y, 2));
        perf.SetMetricMax("grid_scroll_coalesce_max_per_flush", count);
#endif
    }

    private static void Disable(string reason)
    {
        s_disabled = true;
        DisabledReason = reason;
#if PD_DIAGNOSTICS
        PerfProbe.Current.SetText("grid_scroll_coalesce_disabled", reason);
#endif
    }
}
