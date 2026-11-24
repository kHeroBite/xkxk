// 패널 위치 이동 — 영역을 가장자리로 옮기고, 패널·탭을 가장자리나 다른 패널로 옮긴다. 메뉴와 끌어 놓기가 같은 경로를 탄다. 모든 패널이 같은 규칙을 따른다
using System.Collections.Generic;
using System.Linq;
using Dock.Model.Core;

namespace xkxk.frameworks.ui.shell.Docking;

/// <summary>
/// 규칙.
/// ① 패널 혼자 있는 영역은 영역째 그 가장자리로 간다. 그 가장자리에 다른 영역이 있으면 단이 달라 옆 줄에 나란히 놓인다(겹치지 않는다).
/// ② 분할 영역의 한 칸이나 탭 하나를 가장자리로 보내면, 비어 있는 영역을 그 가장자리로 옮겨 담는다. 빈 영역이 없으면 그 가장자리 영역 안에 칸을 나눠 넣는다.
/// ③ 위치를 옮기면 모서리 소유를 기본으로 되돌린다. 옛 기록은 다투는 모서리가 달라져 뜻이 없다.
/// ④ 영역 몫은 방향(좌우·상하)별로 기억해 왕복해도 맞춘 몫이 유지된다(W-1).
/// </summary>
internal sealed class PanelMoveService
{
    private readonly PocDockFactory _f;
    private readonly DockLayoutState _st;
    private readonly Dictionary<PanelArea, (double Horizontal, double Vertical)> _shares = new();

    internal PanelMoveService(PocDockFactory f, DockLayoutState st)
    {
        _f = f;
        _st = st;
    }

    internal static Alignment AlignmentOf(PanelEdge edge) => edge switch
    {
        PanelEdge.Top => Alignment.Top,
        PanelEdge.Bottom => Alignment.Bottom,
        PanelEdge.Left => Alignment.Left,
        _ => Alignment.Right,
    };

    /// <summary>영역이 붙은 가장자리.</summary>
    internal PanelEdge EdgeOf(PanelArea area) => _st.Edges[area];

    /// <summary>패널 Id(바깥·탭 영역)의 가장자리. 패널이 아니면 null.</summary>
    internal PanelEdge? EdgeOfPanel(string panelId) => _f.Split.ResolvePane(_f.Tabs.TabsOf(panelId)) is { } hit ? EdgeOf(hit.Area) : null;

    /// <summary>새 트리에서 몫 기억을 비운다.</summary>
    internal void Reset() => _shares.Clear();

    /// <summary>영역째 가장자리로 옮긴다. 같은 위치면 아무것도 하지 않고 false.</summary>
    internal bool MoveAreaToEdge(PanelArea area, PanelEdge edge)
    {
        if (EdgeOf(area) == edge || _f.Split.SlotOf(area) is not { } slot)
        {
            return false;
        }

        _f.OpenStates.ReleaseScales(); // 알림 숨김 보정은 지금 가장자리 기준이다 — 풀고 옮긴 뒤 다시 계산한다
        var wasPinned = _f.OpenStates.BeginMove(area); // 닫힘·핀 상태에서도 아래 재구성이 패널을 중복시키지 않게 한다
        _f.Corners.ResetForEdgeMove();
        var horizontal = edge is PanelEdge.Left or PanelEdge.Right;
        var shares = _shares.TryGetValue(area, out var known) ? known : (Horizontal: PocDockFactory.SidePanelProportion, Vertical: PocDockFactory.EdgePanelProportion);
        if (slot.Proportion is var current && current > 0 && current < 1) // NaN 은 비교가 거짓이라 걸러진다
        {
            shares = EdgeOf(area) is PanelEdge.Left or PanelEdge.Right ? (current, shares.Vertical) : (shares.Horizontal, current); // 옮기기 전 방향의 몫(끌어 바꾼 값 포함)
        }

        _shares[area] = shares;
        PocDockFactory.SetShare(slot, horizontal ? shares.Horizontal : shares.Vertical);
        _st.Edges[area] = edge;
        foreach (var info in _f.Codec.PanesOf(area))
        {
            if (info.Pane is Dock.Model.Controls.IToolDock tool)
            {
                tool.Alignment = AlignmentOf(edge);
            }
        }

        if (_st.HomePanes[area] is Dock.Model.Controls.IToolDock home)
        {
            home.Alignment = AlignmentOf(edge); // 접힌 기본 패널도 맞춘다
        }

        if (_st.Areas.TryGetValue(area, out var areaDock))
        {
            areaDock.Orientation = horizontal ? Orientation.Vertical : Orientation.Horizontal;
        }

        _f.Corners.RearrangeForEdges();
        _f.OpenStates.EndMove(area, wasPinned);
        _f.RaisePanelEdgeChanged(edge);
        return true;
    }

    /// <summary>패널 Id 전체를 가장자리로 옮긴다(원격 명령). 옮겼으면 true.</summary>
    internal bool MovePanelToEdge(string panelId, PanelEdge edge) =>
        _f.Tabs.TabsOf(panelId) is { VisibleDockables: { } items } tabs && _f.Split.ResolvePane(tabs) is { } hit
        && MoveToEdge(hit.Pane, items.ToList(), tabs.ActiveDockable ?? tabs, edge, wholePanel: true);

    /// <summary>패널·탭을 그 가장자리로 보낼 수 있는가. 패널 혼자인 영역은 항상 된다. 집중 숨김 중에는 새 칸이 보이지 않으므로 칸을 옮기지 않는다.</summary>
    internal bool CanMoveToEdge(IDock pane, PanelEdge edge, bool wholePanel)
    {
        if (_f.Split.ResolvePane(pane) is not { } hit)
        {
            return false;
        }

        if (wholePanel && _f.Codec.PanesOf(hit.Area).Count <= 1)
        {
            return true;
        }

        return !_f.SidePanelsHidden && (FreeArea(hit.Area) is not null || AreaAt(edge) is not null);
    }

    /// <summary>패널 전체(wholePanel) 또는 탭들을 가장자리로 옮긴다. active 는 옮긴 뒤 활성 탭이다. 옮겼으면 true.</summary>
    internal bool MoveToEdge(IDock pane, IReadOnlyList<IDockable> tabs, IDockable active, PanelEdge edge, bool wholePanel)
    {
        if (!CanMoveToEdge(pane, edge, wholePanel) || _f.Split.ResolvePane(pane) is not { } hit)
        {
            return false;
        }

        if (wholePanel && _f.Codec.PanesOf(hit.Area).Count <= 1)
        {
            return MoveAreaToEdge(hit.Area, edge);
        }

        IDock? target = null;
        if (FreeArea(hit.Area) is { } free)
        {
            MoveAreaToEdge(free, edge);
            target = _st.HomeTabs[free];
        }
        else if (AreaAt(edge) is { } at && _f.Codec.PanesOf(at) is { Count: > 0 } panes)
        {
            target = _f.Split.SplitPaneCore(at, panes[^1].Pane, _f.Split.IsAreaVertical(at) ? DockOperation.Bottom : DockOperation.Right, null);
        }

        return target is not null && MoveTabs(tabs, active, target);
    }

    /// <summary>패널 전체나 탭들을 다른 패널의 탭으로 옮긴다. 닫힌 영역은 먼저 열고, 빈 패널 접기·되살리기는 SyncSidePanels 가 맡는다. 활성 탭 처리는 RevealTool 과 같다(핀 패널은 핀을 유지하고 미리 보기만 연다).</summary>
    internal bool MoveTabs(IReadOnlyList<IDockable> tabs, IDockable active, IDock target)
    {
        if (tabs.Count == 0)
        {
            return false;
        }

        if (_f.Split.ResolvePane(target) is { } hit && _f.Split.HomePane(hit.Area) is { } home)
        {
            _f.OpenStates.ReopenPanel(home);
        }

        foreach (var tab in tabs.Where(t => t.Owner is IDock))
        {
            _f.MoveDockable((IDock)tab.Owner!, target, tab, null);
        }

        _f.Hide.SyncSidePanels();
        if (_f.IsDockablePinned(target))
        {
            target.ActiveDockable = active;
            _f.PreviewPinnedDockable(target);
        }
        else
        {
            _f.SetActiveDockable(active);
        }

        _f.Tidy.RetitleSplitPanes();
        _f.RaisePanelTabsChanged(target.Id ?? "");
        _f.RaiseSplitChanged();
        return true;
    }

    /// <summary>비어 있어 접힌 영역(분할 없음, ✕ 로 닫지 않음). 여분 영역을 먼저 쓴다.</summary>
    private PanelArea? FreeArea(PanelArea except)
    {
        foreach (var area in PanelAreaNames.All.Reverse())
        {
            if (area != except && !_st.Areas.ContainsKey(area) && !_st.ClosedAreas.Contains(area) && _st.HomePanes.ContainsKey(area)
                && (_st.HomeTabs[area].VisibleDockables?.Count ?? 0) == 0 && _f.Split.SlotOf(area) is { Owner: not null })
            {
                return area;
            }
        }

        return null;
    }

    /// <summary>그 가장자리에 보이는 영역(칸을 나눌 수 있는 곳).</summary>
    private PanelArea? AreaAt(PanelEdge edge)
    {
        foreach (var area in PanelAreaNames.All)
        {
            if (_st.Edges.TryGetValue(area, out var at) && at == edge && _f.Split.CanSplitNow(area))
            {
                return area;
            }
        }

        return null;
    }
}
