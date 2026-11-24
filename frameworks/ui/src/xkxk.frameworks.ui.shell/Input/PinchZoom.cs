// 두 손가락 핀치 제스처를 Ctrl+휠과 같은 확대 단계(±1)로 바꾸는 입력 처리(태블릿·폰·터치스크린) — 배율 적용은 StepHandler 를 연결하는 쪽(UiSettings, odev-2r)이 맡는다
using System;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using xkxk.frameworks.ui.shell.Diagnostics;

namespace xkxk.frameworks.ui.shell.Input;

/// <summary>
/// Avalonia 12.1.4 <see cref="PinchGestureRecognizer"/>(터치·펜 전용, 두 번째 접점부터 두 포인터 캡처·다른 인식기 차단)를 TopLevel 마다 1개 붙인다.
/// Pinch 의 누적 배율(시작 거리 대비)을 단계 비율 <see cref="StepRatio"/>(Ctrl+휠 1칸 = 10%)로 나눠 정수 단계로 바꾸고,
/// 이미 보낸 단계와의 차만큼 <see cref="StepHandler"/>(대상 visual, +1 확대 / −1 축소)를 부른다. 대상은 핀치 시작 중점의 hit 결과다.
/// 마우스·Ctrl+휠·키보드에는 관여하지 않는다(인식기가 터치·펜 포인터만 받음).
/// </summary>
public static class PinchZoom
{
    /// <summary>단계 1개에 해당하는 배율 비(1.10 = 손가락 거리 10% 변화당 1단계).</summary>
    public const double StepRatio = 1.10;

    /// <summary>단계 처리기(대상 visual, ±1) → 처리했으면 true. 보통 SettingsInput.Step(문서 창 밖이면 false). null 이면 핀치를 처리하지 않는다.</summary>
    public static Func<Visual?, int, bool>? StepHandler { get; set; }

    /// <summary>시작된 핀치 수(문서 창 밖 핀치 포함).</summary>
    public static int Gestures { get; private set; }

    /// <summary>StepHandler 가 처리한 단계 수.</summary>
    public static int Steps { get; private set; }

    private static bool s_installed;
    private static readonly ConditionalWeakTable<TopLevel, State> s_states = new();

    private sealed class State
    {
        public bool Attached;   // 이 TopLevel 에 인식기를 붙였는가
        public bool Started;    // 진행 중인 핀치가 있는가
        public bool Rejected;   // 대상이 문서 밖 등으로 처리기가 거절 → 이 핀치는 끝날 때까지 무시
        public int Sent;        // 이번 핀치에서 보낸 단계(부호 포함)
        public Visual? Target;  // 시작 중점의 문서 래퍼(ZoomDecorator) — 제스처 동안 고정
        public Point Origin;
    }

    /// <summary>앱 시작 때 1회. TopLevel 의 첫 터치·펜 누름 때 그 TopLevel 에 인식기를 붙이고 Pinch/PinchEnded 를 처리한다.</summary>
    public static void Install()
    {
        if (s_installed)
        {
            return;
        }

        s_installed = true;
        TouchToolTipGuard.Install(); // 터치 입력에서는 hover 툴팁을 열지 않는다(D-W3)
        // Tunnel 단계에서 붙이면 같은 누름이 Bubble 단계의 인식기 처리(InputElement 클래스 처리기)에 그대로 전달된다
        InputElement.PointerPressedEvent.AddClassHandler<TopLevel>(OnPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        InputElement.PinchEvent.AddClassHandler<TopLevel>(OnPinch);
        InputElement.PinchEndedEvent.AddClassHandler<TopLevel>((top, _) => End(top));
    }

    private static void OnPressed(TopLevel top, PointerPressedEventArgs e)
    {
        if (e.Pointer.Type is not (PointerType.Touch or PointerType.Pen))
        {
            return;
        }

        var state = s_states.GetOrCreateValue(top);
        if (!state.Attached)
        {
            state.Attached = true;
            top.GestureRecognizers.Add(new PinchGestureRecognizer());
        }
    }

    private static void OnPinch(TopLevel top, PinchEventArgs e)
    {
        if (Apply(top, e.ScaleOrigin, e.Scale))
        {
            e.Handled = true;
        }
    }

    /// <summary>
    /// 핀치 1회의 현재 누적 배율을 반영한다(실제 Pinch 이벤트와 진단 모의 공용). origin = 시작 중점(TopLevel 좌표, 첫 호출 때만 쓰임).
    /// 반환: 이번 핀치가 처리 대상(문서 창 위)이면 true.
    /// </summary>
    public static bool Apply(TopLevel top, Point origin, double scale)
    {
        if (StepHandler is not { } handler)
        {
            return false;
        }

        var state = s_states.GetOrCreateValue(top);
        if (!state.Started)
        {
            state.Started = true;
            state.Rejected = false;
            state.Sent = 0;
            state.Origin = origin;
            state.Target = StableTarget(top, origin);
            Gestures++;
        }

        if (state.Rejected)
        {
            return false;
        }

        LastScale = scale;
        var want = TargetSteps(scale);
        while (state.Sent != want)
        {
            var d = Math.Sign(want - state.Sent);
            bool ok;
            using (PerfProbe.Current.TimeOperation("ui.pinch.step"))
            {
                // 대상이 배율·글자 변경 뒤 재배치로 시각 트리에서 떨어졌으면(그리드 행 재활용 등) 같은 시작 중점에서 다시 찾는다
                if (state.Target is { } t && TopLevel.GetTopLevel(t) is null) // 12.1.4 에는 GetVisualRoot 확장이 없다(odev-4b 컴파일 확인) — 공개 API 로 트리 부착 여부 판정
                {
                    state.Target = StableTarget(top, state.Origin);
                }

                ok = handler(state.Target, d);
            }

            if (!ok)
            {
                state.Rejected = true; // 문서 창 밖 — Ctrl+휠과 같은 규칙으로 무시
                Rejections++;
                return false;
            }

            state.Sent += d;
            Steps++;
        }

        return true;
    }

    /// <summary>
    /// 시작 중점 아래 visual 을 문서 래퍼(ZoomDecorator)로 올려 잡는다. 손가락 아래 셀·글자 visual 은 창 배율·글자 크기 변경 때
    /// 그리드 행 재활용으로 트리에서 떨어져(i1113 웹 실측: 배율 1.0→1.9 에서 멈춤) 문서 판정이 거짓이 되므로, 제스처 동안 유지되는 래퍼를 쓴다.
    /// 래퍼가 없으면(문서 창 밖) 그 visual 그대로 — 처리기가 거절한다.
    /// </summary>
    private static Visual? StableTarget(TopLevel top, Point origin)
    {
        var hit = top.InputHitTest(origin) as Visual;
        return hit?.GetSelfAndVisualAncestors().FirstOrDefault(v => v is Docking.IDocumentZoomHost) ?? hit;
    }

    /// <summary>핀치 종료(손가락 하나라도 뗌). 다음 핀치는 단계 0 부터 다시 센다.</summary>
    public static void End(TopLevel top)
    {
        if (s_states.TryGetValue(top, out var state))
        {
            state.Started = false;
            state.Rejected = false;
            state.Sent = 0;
            state.Target = null;
        }
    }

    /// <summary>경계 허용치(단계 단위). 거의 다음 단계만큼 벌렸으면 그 단계로 인정한다(lead 결정 — 1.33 → +3, 체감 보정). 확대·축소 대칭.</summary>
    public const double StepTolerance = 0.05;

    /// <summary>누적 배율 → 목표 단계. n = floor(|ln s ÷ ln 1.1| + 0.05) × 부호. 예: 1.1→+1, 1.33→+3(2.99+0.05), 0.75→−3(3.02), 1.09→+0(0.90+0.05).</summary>
    public static int TargetSteps(double scale)
    {
        if (!(scale > 0) || !double.IsFinite(scale))
        {
            return 0;
        }

        var x = Math.Log(scale) / Math.Log(StepRatio);
        return Math.Sign(x) * (int)Math.Floor(Math.Abs(x) + StepTolerance);
    }

    /// <summary>진단용: 마지막 Pinch 누적 배율·처리기 거절 수.</summary>
    public static double LastScale { get; private set; } = 1;

    public static int Rejections { get; private set; }
}
