// LiveCharts2 차트를 창 끌기 동안 얼리는 게이트 — 끌기 시작에 애니·갱신·호버를 멈추고 끝나면 되돌린다(변형 L2)
// 초안(odev-gm, 2026-10-30, 컴파일 확인 전, 빌드 안 함). 대상 경로: src/xkxk.frameworks.ui.shell/Documents/Bench/Dash.LiveCharts.DragGate.cs
// 호출 지점(이 파일 밖, 소유자 협의 필요): DashboardBench 끌기 시작 직전 DashDragGate.SetDragging(true), 끝난 직후 SetDragging(false).
//   변형 L2 는 DashDragGate.Enabled = true 일 때만 동작한다(기본 false → L0/L1 과 동일). 켜는 방법은 벤치 인자 1줄(소유자: DashboardBench 담당).
// 확인 필요(컴파일): CartesianChart/PieChart 의 AnimationsSpeed·UpdaterThrottler 속성 이름과 형(TimeSpan). 없으면 AnimationsSpeed 만 쓴다.
#if PD_CHART_LIVECHARTS
using xkxk.frameworks.ui.shell;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using LiveChartsCore.SkiaSharpView.Avalonia;

namespace xkxk.frameworks.ui.views.Documents.Bench;

public static class DashDragGate
{
    private sealed class Saved
    {
        public TimeSpan Speed;
        public TimeSpan Throttle;
        public bool Hit;
    }

    // 끌기 중 갱신 요청을 사실상 보류하는 간격(끌기는 수 초 이내라 이 값이면 그 사이에 갱신이 일어나지 않는다)
    private static readonly TimeSpan FreezeThrottle = TimeSpan.FromMinutes(10);

    private static readonly List<WeakReference<Control>> s_charts = new();
    private static readonly ConditionalWeakTable<Control, Saved> s_saved = new();

    public static bool Enabled { get; set; }

    public static void Register(Control chart)
    {
        if (!Enabled && !DashDragSnapshot.Enabled) { return; }
        s_charts.Add(new WeakReference<Control>(chart));
    }

    // 마지막 Begin 의 스냅샷 캡처 시간(ms). L3 가 꺼져 있으면 0. 벤치가 결과 JSON 에 남기려면 읽는다(이번 cand 는 읽는 코드를 넣지 않았다)
    public static double LastSnapshotMs { get; private set; }

    public static void SetDragging(bool dragging)
    {
        if (!Enabled && !DashDragSnapshot.Enabled) { return; }
        var live = new List<Control>();
        foreach (var w in s_charts.ToArray())
        {
            if (!w.TryGetTarget(out var c)) { s_charts.Remove(w); continue; }
            live.Add(c);
            if (Enabled) { if (dragging) { Freeze(c); } else { Thaw(c); } }
        }

        if (!DashDragSnapshot.Enabled) { return; }
        if (dragging) { LastSnapshotMs = DashDragSnapshot.Begin(live); } else { DashDragSnapshot.End(); }
    }

    private static void Freeze(Control c)
    {
        if (s_saved.TryGetValue(c, out _)) { return; } // 이미 얼림
        switch (c)
        {
            case CartesianChart cc:
                s_saved.Add(c, new Saved { Speed = cc.AnimationsSpeed, Throttle = cc.UpdaterThrottler, Hit = c.IsHitTestVisible });
                cc.AnimationsSpeed = TimeSpan.Zero;
                cc.UpdaterThrottler = FreezeThrottle;
                break;
            case PieChart pc:
                s_saved.Add(c, new Saved { Speed = pc.AnimationsSpeed, Throttle = pc.UpdaterThrottler, Hit = c.IsHitTestVisible });
                pc.AnimationsSpeed = TimeSpan.Zero;
                pc.UpdaterThrottler = FreezeThrottle;
                break;
            default:
                return;
        }

        c.IsHitTestVisible = false; // 호버 툴팁 갱신도 막는다
    }

    private static void Thaw(Control c)
    {
        if (!s_saved.TryGetValue(c, out var sv)) { return; }
        switch (c)
        {
            case CartesianChart cc: cc.AnimationsSpeed = sv.Speed; cc.UpdaterThrottler = sv.Throttle; break;
            case PieChart pc: pc.AnimationsSpeed = sv.Speed; pc.UpdaterThrottler = sv.Throttle; break;
        }

        c.IsHitTestVisible = sv.Hit;
        s_saved.Remove(c);
    }
}
#endif
