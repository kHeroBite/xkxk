// 원격 명령 placement.selftest 의 MDI 잔존 비율 표본과 placement.state 의 minVisiblePercent 값 — 진단 빌드 전용, 제품 동작 불변
#if PD_DIAGNOSTICS // 진단 빌드 전용(PdDiagnostics=true)
using System;
using Avalonia;
using Pd.Poc.Shell.Docking;

namespace Pd.Poc.Shell.Diagnostics.Remote;

internal static partial class RemotePlacementCommands
{
    /// <summary>placement.state 에 내는 지금 잔존 비율(정수 %). 시험 도구가 이 값으로 기대 잔존을 계산한다(정본 01 §4.3).</summary>
    private static int MinVisiblePercent() => (int)Math.Round(MdiDragLimit.CurrentMinVisibleRatio * 100);

    /// <summary>잔존 비율 p 를 고정한 구간 안에서 f 를 계산한다. 바깥에 이미 비율 고정이 있으면 바깥 값이 유지되므로 RatioCases 는 비율 고정 구간 밖에서 부른다.</summary>
    private static T WithRatio<T>(double p, Func<T> f)
    {
        using (MdiDragLimit.UseFixedMinVisibleRatio(p))
        {
            return f();
        }
    }

    /// <summary>
    /// 잔존 비율 표본(2026-11-10 사용자 요청, 정본 01 §4.3). 구간 MdiGrabZone(55, 305, 33, 196)·창 640×420·캔버스 800×600·MinGrab 50 은 DragLimitCases 와 같은 기준값이다.
    /// 기대값은 사람이 정한 값이다: 25% 가로 잔존 160(=ceil 640×0.25) → X 640, 아래 잔존 105(=ceil 420×0.25) → Y 495, 위쪽은 비율과 무관하게 0.
    /// </summary>
    private static void RatioCases(Action<string, bool, string> add, Size area)
    {
        var z = new MdiGrabZone(55, 305, 33, 196);
        var right = new Rect(5000, 0, 640, 420);
        var bottom = new Rect(0, 5000, 640, 420);
        double X(double p, Rect r) => WithRatio(p, () => MdiDragLimit.ClampMove(r, area, z).X);
        double Y(double p, Rect r) => WithRatio(p, () => MdiDragLimit.ClampMove(r, area, z).Y);

        var r25 = X(0.25, right);
        add("drag_ratio_right_keep", r25 == 640, $"x={r25}"); // 잔존 160 > 잡을 곳 규칙 105

        var b25 = Y(0.25, bottom);
        var top25 = Y(0.25, new Rect(0, -50, 640, 420));
        add("drag_ratio_bottom_keep", b25 == 495 && top25 == 0, $"y={b25} top={top25}"); // 잔존 105 > 제목 줄 33, 위쪽 0 정지 불변

        // 0 = 비율 끔(2026-11-08 규칙 그대로). 비율이 잡을 곳 규칙보다 작으면 규칙이 하한이다(10% 가로 64 < 105, 5% 아래 21 < 33)
        var r0 = X(0, right);
        var b0 = Y(0, bottom);
        var r10 = X(0.10, right);
        var b5 = Y(0.05, bottom);
        add("drag_ratio_zero_is_grab_rule", r0 == 695 && b0 == 567 && r10 == 695 && b5 == 567, $"r0={r0} b0={b0} r10={r10} b5={b5}");

        // 범위: 0.9 상한(5 → 0.9, 가로 잔존 576 → X 224), 음수·NaN → 0, 중첩 고정은 바깥 값 유지
        var hi = WithRatio(5.0, () => MdiDragLimit.CurrentMinVisibleRatio);
        var x90 = X(5.0, right);
        var neg = WithRatio(-0.3, () => MdiDragLimit.CurrentMinVisibleRatio);
        var nan = WithRatio(double.NaN, () => MdiDragLimit.CurrentMinVisibleRatio);
        var nested = WithRatio(0.25, () => WithRatio(0.5, () => MdiDragLimit.CurrentMinVisibleRatio));
        add("drag_ratio_clamp", hi == MdiDragLimit.MaxMinVisibleRatio && x90 == 224 && neg == 0 && nan == 0 && nested == 0.25, $"hi={hi} x90={x90} neg={neg} nan={nan} nested={nested}");
    }
}
#endif
