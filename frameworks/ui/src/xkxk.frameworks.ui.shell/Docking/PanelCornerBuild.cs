// 패널 순서 → 도킹 트리 반영(요건 3) — OuterPane·MainPane·CenterPane·ExtraLevel 네 단에 바깥 패널부터 한 칸씩 배치한다. 단 자체와 문서 영역은 건드리지 않아 뷰가 유지된다
using System.Collections.Generic;
using System.Linq;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;

namespace xkxk.frameworks.ui.shell.Docking;

/// <summary>
/// 트리는 언제나 Outer ⊃ Main ⊃ Center ⊃ Extra ⊃ 문서 칸으로 겹쳐 있고 패널만 단 사이를 옮겨 다닌다.
/// 한 단은 [패널, 분할선, 안쪽 단]이며 패널이 왼쪽·위 가장자리면 앞에, 오른쪽·아래면 뒤에 놓는다. 안쪽 단은 목록에서 빼지 않는다
/// (목록에서 빠지면 그 아래 뷰가 전부 다시 만들어진다 — 문서 영역 보존의 핵심).
/// 접힌 패널(PanelHideService.Collapsed)은 목록에 넣지 않고 복원 기록만 새 단으로 고친다.
/// </summary>
internal sealed class PanelCornerBuild
{
    /// <summary>단 하나와 그 단의 전용 분할선.</summary>
    internal sealed record Level(ProportionalDock Dock, IDockable Splitter);

    /// <summary>패널 한 칸: 자리(분할된 영역이면 영역 dock), 가장자리, 접힘 여부.</summary>
    internal sealed record Slot(string Id, IDock Dock, PanelEdge Edge);

    private readonly PocDockFactory _f;
    private readonly DockLayoutState _st;

    internal PanelCornerBuild(PocDockFactory f, DockLayoutState st)
    {
        _f = f;
        _st = st;
    }

    /// <summary>순서대로 단에 배치한다. keepRaw 면 비율을 그대로 둔다(저장본 복원 — 저장 값이 이미 그 배치의 비율이다). 아니면 패널 두께가 그대로 유지되도록 비율을 다시 계산한다.</summary>
    internal void Apply(IReadOnlyList<Level> levels, IReadOnlyList<Slot> order, bool keepRaw, Avalonia.Size area)
    {
        var slots = order.Select(s => s.Dock).ToList();
        var oldOrder = levels.Select(l => order.FirstOrDefault(s => CurrentLevelOf(s, l))).OfType<Slot>().ToList();
        var shares = keepRaw ? null : TargetShares(oldOrder, order, area);

        // 1) 옮길 패널을 새 단으로 보낸다. 안쪽 단은 건드리지 않는다.
        for (var i = 0; i < order.Count; i++)
        {
            MoveTo(order[i], levels[i], slots);
        }

        // 2) 단 방향과 목록 순서·분할선을 맞춘다.
        for (var i = 0; i < order.Count; i++)
        {
            Arrange(levels[i], order[i], slots);
            if (shares is not null && shares.TryGetValue(order[i].Dock, out var share))
            {
                PocDockFactory.SetShare(order[i].Dock, share);
            }

            SetInnerShare(levels[i], order[i].Dock);
        }
    }

    /// <summary>패널이 지금 이 단에 있는가(접힌 패널은 기록의 단).</summary>
    private bool CurrentLevelOf(Slot slot, Level level) =>
        _st.Collapsed.TryGetValue(slot.Dock, out var r) ? ReferenceEquals(r.Owner, level.Dock) : ReferenceEquals(slot.Dock.Owner, level.Dock);

    private void MoveTo(Slot slot, Level level, IReadOnlyList<IDock> slots)
    {
        if (_st.Collapsed.ContainsKey(slot.Dock))
        {
            return; // 접힌 패널은 트리에 없다 — Arrange 가 복원 기록을 고친다
        }

        if (slot.Dock.Owner is IDock owner && !ReferenceEquals(owner, level.Dock))
        {
            var inner = InnerOf(level, slots);
            var first = slot.Edge is PanelEdge.Left or PanelEdge.Top;
            _f.MoveDockable(owner, level.Dock, slot.Dock, first ? inner : null); // 탭·핀·활성 탭은 패널 안쪽에 있어 그대로 따라온다
            if (ReferenceEquals(owner.ActiveDockable, slot.Dock) || owner.ActiveDockable is null)
            {
                owner.ActiveDockable = owner.VisibleDockables?.FirstOrDefault(d => d is not ISplitter && !slots.Contains(d));
            }
        }
    }

    /// <summary>[패널, 분할선, 안쪽 단] 모양으로 맞춘다. 안쪽 단은 그대로 두고 패널·분할선만 옮긴다.</summary>
    private void Arrange(Level level, Slot slot, IReadOnlyList<IDock> slots)
    {
        var list = level.Dock.VisibleDockables!;
        var first = slot.Edge is PanelEdge.Left or PanelEdge.Top;
        var horizontal = slot.Edge is PanelEdge.Left or PanelEdge.Right;
        level.Dock.Orientation = horizontal ? Orientation.Horizontal : Orientation.Vertical;
        var inner = InnerOf(level, slots);
        foreach (var stray in list.Where(d => d is ISplitter && !ReferenceEquals(d, level.Splitter)).ToList())
        {
            _f.RemoveVisible(level.Dock, stray); // 옮겨 온 패널과 함께 남은 다른 단의 분할선(있으면)
        }

        if (_st.Collapsed.TryGetValue(slot.Dock, out var record))
        {
            if (list.Contains(level.Splitter))
            {
                _f.RemoveVisible(level.Dock, level.Splitter);
            }

            var siblings = inner is null ? record.Siblings : new List<(IDockable Dockable, double Proportion)> { (inner, double.NaN) };
            _st.Collapsed[slot.Dock] = record with { Owner = level.Dock, Index = first ? 0 : 1, Splitter = level.Splitter, SplitterBefore = !first, Siblings = siblings };
            return;
        }

        if (inner is null)
        {
            return;
        }

        level.Dock.ActiveDockable = inner; // MoveDockable 은 옮긴 패널을 활성으로 둔다 — 단의 활성은 안쪽 칸으로 유지한다
        var panelAt = list.IndexOf(slot.Dock);
        var innerAt = list.IndexOf(inner);
        if (panelAt >= 0 && (panelAt < innerAt) != first)
        {
            list.RemoveAt(panelAt);
            panelAt = -1;
        }

        if (panelAt < 0)
        {
            _f.InsertVisible(level.Dock, first ? list.IndexOf(inner) : list.IndexOf(inner) + 1, slot.Dock);
            slot.Dock.Owner = level.Dock;
        }

        var wanted = list.IndexOf(slot.Dock) + (first ? 1 : 0);
        var splitterAt = list.IndexOf(level.Splitter);
        if (splitterAt != wanted)
        {
            if (splitterAt >= 0)
            {
                _f.RemoveVisible(level.Dock, level.Splitter);
            }

            _f.InsertVisible(level.Dock, list.IndexOf(first ? slot.Dock : inner) + 1, level.Splitter);
            level.Splitter.Owner = level.Dock;
        }
    }

    /// <summary>단의 안쪽 칸(패널·분할선이 아닌 첫 칸).</summary>
    private static IDockable? InnerOf(Level level, IReadOnlyList<IDock> slots) =>
        level.Dock.VisibleDockables?.FirstOrDefault(d => d is not ISplitter && !slots.Contains(d));

    /// <summary>안쪽 칸 몫을 남은 몫으로 맞춘다. 옛 몫이 남으면 Dock 정규화로 패널 몫이 달라진다.</summary>
    private static void SetInnerShare(Level level, IDock panel)
    {
        foreach (var inner in level.Dock.VisibleDockables?.Where(d => d is not ISplitter && !ReferenceEquals(d, panel)) ?? Enumerable.Empty<IDockable>())
        {
            PocDockFactory.SetShare(inner, panel.Proportion is > 0 and < 1 ? 1 - panel.Proportion : double.NaN);
        }
    }

    /// <summary>
    /// 패널 두께가 변하지 않게 하는 새 비율. 비율은 '자기보다 바깥에 있는 같은 축 패널이 남긴 길이'에 대한 몫이므로, 바깥 같은 축 패널 집합이 그대로면 값도 그대로 둔다
    /// (왕복하면 소수점까지 같아야 한다). 집합이 바뀐 패널만 다시 계산하고 소수 6자리로 맞춘다. 접힌·핀(비율 0) 패널은 공간을 쓰지 않으므로 계산에서 뺀다.
    /// 줄어드는 패널의 길이가 최소(PanelCornerMinSize)보다 작아지면 모서리를 가져간 패널의 두께를 그만큼 줄인다. 이때만 두께가 변하고 되돌려도 원래 두께로 돌아오지 않는다.
    /// </summary>
    private Dictionary<IDock, double> TargetShares(IReadOnlyList<Slot> oldOrder, IReadOnlyList<Slot> newOrder, Avalonia.Size area)
    {
        bool Occupies(Slot s) => !_st.Collapsed.ContainsKey(s.Dock) && s.Dock.Proportion is > 0 and < 1;
        static bool SameAxis(Slot a, Slot b) => (a.Edge is PanelEdge.Left or PanelEdge.Right) == (b.Edge is PanelEdge.Left or PanelEdge.Right);

        var occupied = newOrder.Where(Occupies).ToList();
        var current = occupied.Select(slot => slot.Dock.Proportion * Remaining(oldOrder.TakeWhile(s => s.Dock != slot.Dock).Where(s => SameAxis(s, slot) && Occupies(s)).ToList(), oldOrder, Occupies)).ToList();
        var limited = PanelCornerMinSize.Limit(occupied.Select((s, i) => (s.Edge is PanelEdge.Left or PanelEdge.Right, current[i])).ToList(), area.Width, area.Height);

        var result = new Dictionary<IDock, double>();
        for (var i = 0; i < occupied.Count; i++)
        {
            var slot = occupied[i];
            var outerAt = Enumerable.Range(0, i).Where(j => SameAxis(occupied[j], slot)).ToList();
            var oldOuter = oldOrder.TakeWhile(s => s.Dock != slot.Dock).Where(s => SameAxis(s, slot) && Occupies(s)).Select(s => s.Dock);
            var sameThickness = limited[i] == current[i] && outerAt.All(j => limited[j] == current[j]);
            if (sameThickness && oldOuter.ToHashSet().SetEquals(outerAt.Select(j => occupied[j].Dock)))
            {
                continue;
            }

            var remaining = 1 - outerAt.Sum(j => limited[j]);
            if (remaining > 0)
            {
                result[slot.Dock] = System.Math.Round(limited[i] / remaining, 6);
            }
        }

        return result;
    }

    /// <summary>바깥 패널들이 차지하고 남은 길이(창 = 1). 두께는 옛 순서 기준으로 잰다.</summary>
    private static double Remaining(IReadOnlyList<Slot> outers, IReadOnlyList<Slot> oldOrder, System.Func<Slot, bool> occupies)
    {
        var remaining = 1.0;
        foreach (var outer in outers)
        {
            var left = 1.0;
            foreach (var s in oldOrder)
            {
                if (ReferenceEquals(s.Dock, outer.Dock))
                {
                    break;
                }

                if ((s.Edge is PanelEdge.Left or PanelEdge.Right) == (outer.Edge is PanelEdge.Left or PanelEdge.Right) && occupies(s))
                {
                    left *= 1 - s.Dock.Proportion;
                }
            }

            remaining -= outer.Dock.Proportion * left;
        }

        return remaining;
    }
}
