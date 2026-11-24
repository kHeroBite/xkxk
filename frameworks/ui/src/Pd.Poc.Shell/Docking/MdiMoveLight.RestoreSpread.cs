// MDI 끌기 P2(놓은 뒤 정지 창 복원 프레임 분산) 값과 진단 카운터 — 01a P2 절, 기본 0(지금처럼 한 번에)
using System;

namespace Pd.Poc.Shell.Docking;

public static partial class MdiMoveLight
{
    /// <summary>P2 기본값 — 0(끄는 창이 QuietMs 에 원복될 때 정지 창 가벼운 크롬·캐시를 한 프레임에 한꺼번에 떼는 지금 동작).</summary>
    public const int DefaultRestorePerFrame = 0;

    /// <summary>P2 허용 범위(프레임당 창 수) — 옵션 B(SiblingsPerFrame)와 같은 범위·같은 보정 방식.</summary>
    public const int MinRestorePerFrame = 0, MaxRestorePerFrame = 16;

    private static int _restorePerFrame = DefaultRestorePerFrame;

    /// <summary>
    /// P2: 0 보다 크면 이동이 모두 끝나 정지 창을 원복할 때(MdiStaticLight) 한 번에 떼지 않고 다음 프레임부터 이 수만큼씩 나눠 뗀다.
    /// 끄는 창 자신의 원복은 그대로 바로 한다. 0 이면 지금 경로 그대로. MinRestorePerFrame~MaxRestorePerFrame 로 자른다. 다음 원복부터 반영(도중에 0 이 되면 남은 것을 한 번에).
    /// </summary>
    public static int RestorePerFrame
    {
        get => _restorePerFrame;
        set => _restorePerFrame = Math.Clamp(value, MinRestorePerFrame, MaxRestorePerFrame);
    }

#if PD_DIAGNOSTICS
    /// <summary>진단 카운터(P2): 나눠 뗀 프레임 수 누계.</summary>
    public static int RestoreFrames { get; set; }

    /// <summary>진단 카운터(P2): 다 떼기 전에 새 이동·옵션 변경이 와서 남은 해제를 한 번에 마친 수 누계.</summary>
    public static int RestoreFlushed { get; set; }

    /// <summary>진단 카운터(P2): 분산으로 넘긴 정지 창 수 누계(원복 시점에 걸려 있던 창).</summary>
    public static int RestoreQueued { get; set; }
#endif
}
