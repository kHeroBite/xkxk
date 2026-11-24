// 패널 잘림 판정·풍차 금지 순수 함수(요건 3) — 패널 배치와 모서리 소유만으로 앞뒤 순서를 계산한다. Dock·화면 형식을 쓰지 않는다
using System.Collections.Generic;
using System.Linq;

namespace xkxk.frameworks.ui.shell.Docking;

/// <summary>패널 하나의 가장자리. Rank 가 작을수록 기본 순서에서 바깥이다(같은 가장자리에서는 항상 작은 쪽이 바깥).</summary>
public sealed record PanelPlacement(string Id, PanelEdge Edge, int Rank, bool Visible = true);

/// <summary>패널의 잘린 끝 하나.</summary>
public sealed record TruncatedCorner(string PanelId, PanelCorner Corner);

/// <summary>
/// 배치를 '바깥부터 한 패널씩 창 한 변을 통째로 차지하는' 순서로 본다. 이 순서가 있으면 이분 분할 트리로 표현된다.
/// 직교하는 두 패널이 모서리를 다투면 앞선 쪽이 그 모서리까지 닿고 뒤쪽이 잘린다. 순서에 순환이 생기면 풍차다.
/// </summary>
public static class PanelCornerRules
{
    private static readonly PanelCorner[] AllCorners = { PanelCorner.TopLeft, PanelCorner.TopRight, PanelCorner.BottomLeft, PanelCorner.BottomRight };

    private static bool IsVertical(PanelEdge edge) => edge is PanelEdge.Left or PanelEdge.Right;

    /// <summary>모서리에 닿는 세로 변(왼쪽·오른쪽).</summary>
    private static PanelEdge VerticalSide(PanelCorner c) => c is PanelCorner.TopLeft or PanelCorner.BottomLeft ? PanelEdge.Left : PanelEdge.Right;

    /// <summary>모서리에 닿는 가로 변(위·아래).</summary>
    private static PanelEdge HorizontalSide(PanelCorner c) => c is PanelCorner.TopLeft or PanelCorner.TopRight ? PanelEdge.Top : PanelEdge.Bottom;

    /// <summary>패널의 두 끝 모서리.</summary>
    public static IReadOnlyList<PanelCorner> EndsOf(PanelEdge edge) => AllCorners.Where(c => VerticalSide(c) == edge || HorizontalSide(c) == edge).ToList();

    /// <summary>모서리 소유 기록이 없을 때의 앞뒤 순서. Rank 가 작은 쪽이 앞선다.</summary>
    public static bool DefaultPrecedes(PanelPlacement a, PanelPlacement b) => a.Rank < b.Rank;

    /// <summary>모서리 corner 에서 직교하는 a 가 b 보다 앞서는가. 소유 패널과 같은 변의 패널은 소유 패널까지 앞서고 그 안쪽은 뒤진다.</summary>
    private static bool Precedes(IReadOnlyList<PanelPlacement> panels, PanelCornerOwnership ownership, PanelCorner corner, PanelPlacement a, PanelPlacement b)
    {
        if (ownership.OwnerOf(corner) is { } ownerId && panels.FirstOrDefault(p => p.Id == ownerId) is { } owner)
        {
            if (a.Edge == owner.Edge)
            {
                return a.Rank <= owner.Rank;
            }

            if (b.Edge == owner.Edge)
            {
                return b.Rank > owner.Rank;
            }
        }

        return DefaultPrecedes(a, b);
    }

    /// <summary>모서리에서 다투는 직교 쌍(세로 변 패널, 가로 변 패널).</summary>
    private static IEnumerable<(PanelPlacement V, PanelPlacement H)> PairsAt(IReadOnlyList<PanelPlacement> panels, PanelCorner corner) =>
        from v in panels.Where(p => p.Edge == VerticalSide(corner))
        from h in panels.Where(p => p.Edge == HorizontalSide(corner))
        select (v, h);

    /// <summary>
    /// 잘린 끝 목록. 패널의 끝 모서리를 직교 패널이 앞서 차지하고 두 패널이 모두 보일 때 잘린 것이다.
    /// </summary>
    public static IReadOnlyList<TruncatedCorner> TruncatedCorners(IReadOnlyList<PanelPlacement> panels, PanelCornerOwnership ownership)
    {
        var result = new List<TruncatedCorner>();
        foreach (var corner in AllCorners)
        {
            foreach (var panel in panels.Where(p => p.Visible && EndsOf(p.Edge).Contains(corner)))
            {
                var rivals = panels.Where(p => p.Visible && p.Edge == (IsVertical(panel.Edge) ? HorizontalSide(corner) : VerticalSide(corner)));
                if (rivals.Any(r => Precedes(panels, ownership, corner, r, panel)))
                {
                    result.Add(new TruncatedCorner(panel.Id, corner));
                }
            }
        }

        return result;
    }

    /// <summary>바깥부터의 패널 순서. 순환(풍차)이면 null. 동률은 Rank 순이라 기본 배치는 기본 트리와 같은 순서다.</summary>
    public static IReadOnlyList<PanelPlacement>? PeelOrder(IReadOnlyList<PanelPlacement> panels, PanelCornerOwnership ownership)
    {
        var before = panels.ToDictionary(p => p.Id, _ => new HashSet<string>());
        foreach (var inner in panels)
        {
            foreach (var outer in panels.Where(p => p.Edge == inner.Edge && p.Rank < inner.Rank))
            {
                before[inner.Id].Add(outer.Id);
            }
        }

        foreach (var corner in AllCorners)
        {
            foreach (var (v, h) in PairsAt(panels, corner))
            {
                var (first, second) = Precedes(panels, ownership, corner, v, h) ? (v, h) : (h, v);
                before[second.Id].Add(first.Id);
            }
        }

        var order = new List<PanelPlacement>();
        var rest = panels.OrderBy(p => p.Rank).ToList();
        while (rest.Count > 0)
        {
            var next = rest.FirstOrDefault(p => before[p.Id].All(id => order.Any(o => o.Id == id)));
            if (next is null)
            {
                return null;
            }

            order.Add(next);
            rest.Remove(next);
        }

        return order;
    }

    /// <summary>이 소유 상태가 이분 분할 트리로 표현되지 않는 풍차인가.</summary>
    public static bool IsPinwheel(IReadOnlyList<PanelPlacement> panels, PanelCornerOwnership ownership) => PeelOrder(panels, ownership) is null;

    /// <summary>패널이 모서리를 가져간 뒤의 소유 상태. 결과가 기본 규칙과 같으면 기록을 지운다(왕복하면 처음과 같은 값이 된다).</summary>
    public static PanelCornerOwnership Claim(IReadOnlyList<PanelPlacement> panels, PanelCornerOwnership ownership, string panelId, PanelCorner corner)
    {
        var claimed = ownership.With(corner, panelId);
        var isDefault = PairsAt(panels, corner).All(pair => Precedes(panels, claimed, corner, pair.V, pair.H) == DefaultPrecedes(pair.V, pair.H));
        return isDefault ? ownership.Without(corner) : claimed;
    }

    /// <summary>버튼을 낼 끝 목록 — 잘린 끝 가운데 눌러도 풍차가 되지 않는 것만. 풍차가 되는 끝은 버튼이 아예 없다.</summary>
    public static IReadOnlyList<TruncatedCorner> ClaimableCorners(IReadOnlyList<PanelPlacement> panels, PanelCornerOwnership ownership) =>
        TruncatedCorners(panels, ownership).Where(t => !IsPinwheel(panels, Claim(panels, ownership, t.PanelId, t.Corner))).ToList();
}
