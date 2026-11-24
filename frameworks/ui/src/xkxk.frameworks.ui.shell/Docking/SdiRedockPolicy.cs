// SDI→MDI 재결합 위치 정책(요건 4) — 창 면적 50% 이상이 MDI 영역에 보이고 제목줄에 닿으면 위치·크기를 그대로 두는 순수 판정(화면·창 조작 없음)
using System;
using Avalonia;

namespace xkxk.frameworks.ui.shell.Docking;

/// <summary>재결합 위치 판정 결과.</summary>
public enum SdiRedockOutcome
{
    /// <summary>보이는 비율과 제목줄 조건을 모두 충족 — 위치·크기를 보정하지 않는다.</summary>
    Keep,

    /// <summary>조건 미달 — 기존처럼 MDI 영역 안으로 당긴다.</summary>
    Clamp,

    /// <summary>프레임이나 영역을 잴 수 없음(웹 관리형 창·비전경 시험 등) — 기존 PreFloatBounds 경로.</summary>
    FrameUnavailable,
}

/// <summary>
/// 사용자 요구: SDI→MDI 전환 때 창의 50% 이상이 보이면 일부가 가려져도 원래 자리 그대로 둔다.
/// 비율의 분모는 SDI 자신의 보이는 프레임 면적이다(계획서 가정 A1). 입력은 같은 좌표계·같은 단위(DIP)여야 한다.
/// </summary>
public static class SdiRedockPolicy
{
    /// <summary>위치를 그대로 두는 최소 가시 비율(이 값 포함).</summary>
    public const double MinVisibleRatio = 0.5;

    /// <summary>SDI↔MDI 왕복 뒤 허용하는 위치 어긋남(px, AC-4-06).</summary>
    public const int MaxDriftPx = 2;

    /// <summary>frame 면적 중 area 안에 든 비율(0~1). frame 이 비어 있으면 0.</summary>
    public static double VisibleRatio(Rect frame, Rect area)
    {
        var whole = frame.Width * frame.Height;
        if (whole <= 0)
        {
            return 0;
        }

        var hit = frame.Intersect(area);
        return Math.Clamp(hit.Width * hit.Height / whole, 0, 1);
    }

    /// <summary>재결합 위치를 판정한다. 비율을 채워도 제목줄에 닿지 못하면 창을 끌 수 없으므로 Clamp 다.</summary>
    public static SdiRedockOutcome Decide(Rect? frame, Rect? area)
    {
        if (frame is not { Width: > 0, Height: > 0 } f || area is not { Width: > 0, Height: > 0 } a)
        {
            return SdiRedockOutcome.FrameUnavailable;
        }

        return VisibleRatio(f, a) >= MinVisibleRatio && PlacementRecovery.IsTitleReachable(f, [a])
            ? SdiRedockOutcome.Keep
            : SdiRedockOutcome.Clamp;
    }
}
