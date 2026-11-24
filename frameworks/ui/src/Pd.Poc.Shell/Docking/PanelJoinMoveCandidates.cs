// MDI 창 끌기 이동 중 패널 합류 대상 판정용 후보 캐시(W4 웹 끌기 프레임 간격) — PanelJoinDrag 가 쓴다
using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Dock.Avalonia.Controls;

namespace Pd.Poc.Shell.Docking;

/// <summary>
/// 끌기 이동마다 시각 트리를 훑지 않고 합류 후보(ToolDockControl) 목록을 RefreshMs 동안 재사용한다.
/// 보임·위치·탭 영역 여부는 매번 새로 본다(PanelJoinTargets.FindAt). 놓기 판정(PanelJoinDrag.OnReleased·Commit)은 이 캐시를 쓰지 않는다.
/// </summary>
internal static class PanelJoinMoveCandidates
{
    /// <summary>끄는 동안 후보를 다시 모으는 간격(ms). 체류 판정(PanelJoinDrag.DwellMs)보다 짧게 두어 끄는 중 새로 보인 패널도 곧 후보가 된다.</summary>
    private const long RefreshMs = 200;

    private static List<ToolDockControl>? s_candidates;
    private static TopLevel? s_top;
    private static long s_at;

    /// <summary>점 아래 합류 가능한 측면 패널(없으면 null). 판정 조건은 PanelJoinTargets.FindAt 과 같다.</summary>
    internal static Control? TargetAt(PocDockFactory? factory, TopLevel top, Point point)
    {
        var now = Environment.TickCount64;
        if (s_candidates is null || !ReferenceEquals(s_top, top) || now - s_at >= RefreshMs)
        {
            s_candidates = PanelJoinTargets.Candidates(top);
            s_top = top;
            s_at = now;
        }

        return PanelJoinTargets.FindAt(factory, top, point, s_candidates);
    }

    /// <summary>다음 끌기는 후보를 새로 모은다(닫힌 패널 참조를 붙들지 않게).</summary>
    internal static void Clear()
    {
        s_candidates = null;
        s_top = null;
    }
}
