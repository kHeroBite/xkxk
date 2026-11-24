// 창 탭 합류 순수 규칙 — 표적 기록·표적 고르기·머무름 판정·끌기 판정(UI 없음, 정본 01 §4.4·계획서 §3.3 규칙 1~3)
using System;
using System.Collections.Generic;
using Avalonia;

namespace Pd.Poc.Shell.Docking;

/// <summary>합류 표적 종류. Icon = 대상 창 아이콘 칸(SDI 로고, MDI 는 끌기 중에만 보이는 합치기 아이콘), TabStrip = 이미 있는 탭 줄.</summary>
public enum TabJoinKind
{
    Icon,
    TabStrip,
}

/// <summary>
/// 합류 표적 1건. Rect 는 표적을 모으는 쪽이 정한 한 좌표계(MDI 는 문서 칸 DIP, SDI 는 화면 DIP)로 통일한다.
/// Z 는 클수록 위. WindowKey 는 표적이 속한 창(MDI 는 문서·묶음 Id, SDI 는 창 식별자) — 끄는 창 자신을 빼는 데 쓴다.
/// </summary>
public sealed record TabJoinTarget(string Id, TabJoinKind Kind, Rect Rect, int Z, string WindowKey);

/// <summary>
/// 탭 합류 판정 규칙. 겹침만으로는 합치지 않는다 — 표적(아이콘 칸·탭 줄) 위에 DwellMs 이상 머문 뒤 놓을 때만 합류한다.
/// 값은 패널 합류(PanelJoinDrag)와 같다(사용자가 두 합류를 같은 손맛으로 쓰게).
/// </summary>
public static class TabJoinRules
{
    /// <summary>같은 표적 위에 머물러야 하는 시간(ms) — PanelJoinDrag.DwellMs 와 같은 값.</summary>
    public const long DwellMs = PanelJoinDrag.DwellMs;

    /// <summary>누른 곳에서 이만큼(DIP) 움직여야 끌기로 본다 — PanelJoinDrag.MinDragDistance 와 같은 값.</summary>
    public const double MinDragDistance = PanelJoinDrag.MinDragDistance;

    /// <summary>누른 점에서 지금 점까지 MinDragDistance 이상이면 끌기.</summary>
    public static bool IsDrag(Point start, Point now)
    {
        var dx = now.X - start.X;
        var dy = now.Y - start.Y;
        return Math.Sqrt((dx * dx) + (dy * dy)) >= MinDragDistance;
    }

    /// <summary>
    /// 포인터를 담은 표적 중 Z 가 가장 위인 것. 끄는 창(sourceKey)의 표적은 뺀다. 같은 Z 면 목록에서 뒤에 온 것(나중에 그린 것).
    /// 담은 표적이 없으면 null.
    /// </summary>
    public static TabJoinTarget? Pick(Point point, IReadOnlyList<TabJoinTarget> targets, string? sourceKey)
    {
        TabJoinTarget? best = null;
        foreach (var t in targets)
        {
            if (sourceKey is not null && string.Equals(t.WindowKey, sourceKey, StringComparison.Ordinal))
            {
                continue;
            }

            if (!t.Rect.Contains(point))
            {
                continue;
            }

            if (best is null || t.Z >= best.Z)
            {
                best = t;
            }
        }

        return best;
    }
}

/// <summary>
/// 머무름 판정. Update 로 지금 포인터 아래 표적(없으면 null)을 넘기면, 같은 표적(Id 기준)에 DwellMs 이상 머물렀을 때 IsArmed 가 true 다.
/// 표적이 바뀌거나 사라지면 시계를 다시 잰다. 시계는 시험용으로 바꿀 수 있다(기본 Environment.TickCount64).
/// </summary>
public sealed class TabJoinDwellGate
{
    private readonly Func<long> _clock;
    private long _since;

    public TabJoinDwellGate(Func<long>? clock = null) => _clock = clock ?? (() => Environment.TickCount64);

    /// <summary>지금 머무는 표적(강조 대상). 없으면 null.</summary>
    public TabJoinTarget? Current { get; private set; }

    /// <summary>Current 위에 DwellMs 이상 머물렀는가 — 놓을 때 이 값이 true 여야 합류한다.</summary>
    public bool IsArmed => Current is not null && _clock() - _since >= TabJoinRules.DwellMs;

    /// <summary>지금 표적을 넘긴다. 표적이 바뀌었으면 true(강조 갱신 신호).</summary>
    public bool Update(TabJoinTarget? target)
    {
        if (target?.Id == Current?.Id && (target is null) == (Current is null))
        {
            return false;
        }

        Current = target;
        _since = _clock();
        return true;
    }

    /// <summary>끌기 끝·취소 때 비운다.</summary>
    public void Reset()
    {
        Current = null;
        _since = 0;
    }
}
