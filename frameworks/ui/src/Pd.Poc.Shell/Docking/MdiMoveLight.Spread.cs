// MDI 끌기 옵션 A(탭 합치기 아이콘 가까울 때만 준비)·옵션 B(정지 창 가벼운 크롬 프레임 분산) 값과 진단 카운터 — 01a 옵션 절, 기본은 A 켬·B 0(지금 동작)
using System;

namespace Pd.Poc.Shell.Docking;

public static partial class MdiMoveLight
{
    /// <summary>옵션 A 기본값 — 켬(아이콘 가까이 왔을 때만 준비, 2026-11-15 G3 판정으로 끔에서 변경, 리드 결정 D-46). 끄면 끌기 시작 때 모든 창을 준비하는 옛 동작.</summary>
    public const bool DefaultTabJoinArmNear = true;

    /// <summary>옵션 A 반경 기본값(px). 아이콘 표적 사각형을 이만큼 키운 영역 안에 포인터가 들어오면 준비한다.</summary>
    public const double DefaultTabJoinArmRadius = 96;

    /// <summary>옵션 A 반경 허용 범위(px).</summary>
    public const double MinTabJoinArmRadius = 24, MaxTabJoinArmRadius = 400;

    /// <summary>옵션 B 기본값 — 0(첫 이동 프레임에 정지 창 전부에 한꺼번에 거는 지금 동작).</summary>
    public const int DefaultSiblingsPerFrame = 0;

    /// <summary>옵션 B 허용 범위(프레임당 창 수).</summary>
    public const int MinSiblingsPerFrame = 0, MaxSiblingsPerFrame = 16;

    private static double _tabJoinArmRadius = DefaultTabJoinArmRadius;
    private static int _siblingsPerFrame = DefaultSiblingsPerFrame;

    /// <summary>옵션 A: 탭 합치기 표적을 아이콘 가까이 왔을 때만 준비할지(MdiTabJoinDrag). 다음 끌기부터 반영.</summary>
    public static bool TabJoinArmNear { get; set; } = DefaultTabJoinArmNear;

    /// <summary>옵션 A 반경(px). MinTabJoinArmRadius~MaxTabJoinArmRadius 로 자른다.</summary>
    public static double TabJoinArmRadius
    {
        get => _tabJoinArmRadius;
        set => _tabJoinArmRadius = double.IsFinite(value) ? Math.Clamp(value, MinTabJoinArmRadius, MaxTabJoinArmRadius) : DefaultTabJoinArmRadius;
    }

    /// <summary>
    /// 옵션 B: 0 보다 크면 정지 창 가벼운 크롬(.pdMovingSibling, MdiStaticLight)을 첫 이동 프레임에 한꺼번에 걸지 않고 다음 프레임부터 이 수만큼씩 나눠 건다.
    /// 0 이면 지금 경로 그대로. MinSiblingsPerFrame~MaxSiblingsPerFrame 로 자른다. 다음 이동 시작부터 반영.
    /// </summary>
    public static int SiblingsPerFrame
    {
        get => _siblingsPerFrame;
        set => _siblingsPerFrame = Math.Clamp(value, MinSiblingsPerFrame, MaxSiblingsPerFrame);
    }

#if PD_DIAGNOSTICS
    /// <summary>진단 카운터(옵션 B): 나눠 건 프레임 수 누계.</summary>
    public static int SpreadFrames { get; set; }

    /// <summary>진단 카운터(옵션 B): 다 걸기 전에 끌기가 끝나 남은 적용을 취소한 수 누계.</summary>
    public static int SpreadCanceled { get; set; }
#endif
}
