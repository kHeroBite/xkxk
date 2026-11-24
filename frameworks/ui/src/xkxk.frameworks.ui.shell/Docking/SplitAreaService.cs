// 패널 영역 분할(UI-20, 옛 PocDockFactory.Split) — 패널 가장자리에 놓으면 같은 영역 안에 새 패널을 1/2 크기로 만든다
using System.Collections.Generic;
using System.Linq;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;

namespace xkxk.frameworks.ui.shell.Docking;

/// <summary>
/// 근거(Dock 12.1.0.12 디컴파일, logs/odev1r/ilspy_ui20):
/// ① DockManager 는 가장자리(Left·Right·Top·Bottom) 놓기를 DockService.SplitDockable 로 보내고,
///   SplitToolDockable·SplitDocumentDockable 이 새 ToolDock·DocumentDock 에 내용을 옮긴 뒤 factory.SplitToDock(대상, 새 dock, 방향)을 부른다.
/// ② 기본 SplitToDock 은 대상의 소유자가 같은 방향 ProportionalDock 이면 옆에 넣고(비율 = 대상 몫의 1/2), 아니면 대상 자리를 새 ProportionalDock 으로 감싼다.
///   측면 패널 탭 영역(PanelDocumentDock)의 소유자는 ToolDock 이라 기본 동작은 ToolDock 안에 분할을 만들어 패널 구조(핀·머리줄)를 깨뜨린다.
/// 그래서 SplitToDock(virtual)을 재정의해, 대상이 패널이면 '같은 영역 ProportionalDock 안에 ToolDock+탭 영역 한 벌'을 새로 넣고 운반 dock 의 내용을 그 탭으로 옮긴다.
/// 끌기 시작·안내 표시·드롭 판정은 Dock 그대로다(웹·Android·데스크톱 같은 코드 경로).
/// override 는 PocDockFactory.SplitToDock 에 남고 본문만 여기로 위임한다. base.SplitToDock 은 _f.BaseSplitToDock 이다.
/// 정리(빈 패널 빼기·감싸기 풀기·제목 맞추기)는 SplitAreaCleanup(옛 SplitTidy)이 한다.
/// </summary>
internal sealed class SplitAreaService
{
    /// <summary>분할로 만든 패널 한 벌(바깥 ToolDock + 안쪽 탭 영역).</summary>
    internal sealed record SplitPane(string Id, ToolDock Pane, PanelDocumentDock Tabs, PanelArea Area);

    private readonly PocDockFactory _f;
    private readonly DockLayoutState _st;

    internal SplitAreaService(PocDockFactory f, DockLayoutState st)
    {
        _f = f;
        _st = st;
    }

    /// <summary>영역의 기본 패널(분할 전부터 있던 것).</summary>
    internal IDock? HomePane(PanelArea area) => _st.HomePanes.TryGetValue(area, out var pane) ? pane : null;

    internal PanelDocumentDock? HomeTabs(PanelArea area) => _st.HomeTabs.TryGetValue(area, out var tabs) ? tabs : null;

    /// <summary>영역 안에서 패널이 쌓이는 방향이 세로(위/아래 분할)인지. 좌우 가장자리 영역은 세로, 상하 가장자리 영역은 가로다.</summary>
    internal bool IsAreaVertical(PanelArea area) => _f.Moves.EdgeOf(area) is PanelEdge.Left or PanelEdge.Right;

    /// <summary>영역 방향과 맞는 분할 방향인지. 가로질러 쪼개는 방향(예: 왼쪽 패널의 왼쪽·오른쪽 가장자리)은 받지 않는다.</summary>
    internal bool IsSplitAxis(PanelArea area, DockOperation op) => IsAreaVertical(area)
        ? op is DockOperation.Top or DockOperation.Bottom
        : op is DockOperation.Left or DockOperation.Right;

    /// <summary>dock(바깥 패널·안쪽 탭 영역)이 속한 패널과 영역. 패널이 아니면 null.</summary>
    internal (PanelArea Area, IDock Pane)? ResolvePane(IDockable? dock)
    {
        if (dock is null)
        {
            return null;
        }

        foreach (var area in PanelAreaNames.All)
        {
            if (HomePane(area) is { } home && (ReferenceEquals(dock, home) || ReferenceEquals(dock, HomeTabs(area))))
            {
                return (area, home);
            }
        }

        var hit = _st.SplitPanes.Values.FirstOrDefault(p => ReferenceEquals(p.Pane, dock) || ReferenceEquals(p.Tabs, dock));
        return hit is null ? null : (hit.Area, hit.Pane);
    }

    /// <summary>영역 자리를 차지하는 dock: 분할돼 있으면 영역 ProportionalDock, 아니면 기본 패널.</summary>
    internal IDock? SlotOf(PanelArea area) => _st.Areas.TryGetValue(area, out var a) ? a : HomePane(area);

    /// <summary>분할이 지금 가능한지(영역 자리가 트리에 보이는지 — 집중 숨김·접힘이면 불가).</summary>
    internal bool CanSplitNow(PanelArea area) =>
        SlotOf(area) is { Owner: IDock owner } slot && owner.VisibleDockables?.Contains(slot) == true && !_st.Collapsed.ContainsKey(slot);

    /// <summary>패널 탭 영역에서 트리 밖 운반 dock 으로 옮겨진 운반 dock 과 원래 탭 영역(Q15 ④). 다음 운반이 생기면 덮어쓴다.</summary>
    private (IDock Carrier, PanelDocumentDock From)? _panelCarry;

    /// <summary>PocDockFactory.MoveDockable(override)가 base 호출 전에 부른다 — 패널 탭 → 트리 밖 운반 dock 이동을 기억한다.</summary>
    internal void NotePanelCarry(IDock sourceDock, IDock targetDock)
    {
        if (sourceDock is PanelDocumentDock from && targetDock.Owner is null && !ReferenceEquals(targetDock, _f.Root))
        {
            _panelCarry = (targetDock, from);
        }
    }

    /// <summary>패널 탭 문서가 문서 칸으로 분할된 뒤: 활성으로 두고 원래 패널의 탭 변경을 알리고 빈 패널을 접는다. OnDockableMoved 는 문서가 이미 패널 밖이라 탭 변경을 알리지 못한다.</summary>
    private void FinishPanelCarry((IDock Carrier, PanelDocumentDock From)? memo, IDockable carrier)
    {
        if (memo is not { } m || !ReferenceEquals(m.Carrier, carrier))
        {
            return;
        }

        if (m.Carrier.VisibleDockables?.FirstOrDefault() is { } item)
        {
            _f.SetActiveDockable(item);
        }

        _f.RaisePanelTabsChanged(m.From.Id);
        _f.Hide.SyncSidePanels(); // 마지막 탭이 빠져 빈 패널이면 접는다
    }

    /// <summary>Dock 의 가장자리 놓기가 부르는 분할(PocDockFactory.SplitToDock 본문). 대상이 패널이면 같은 영역에 새 패널을 만들고 운반 dock 의 내용을 그 탭으로 옮긴다.</summary>
    internal void SplitToDock(IDock dock, IDockable dockable, DockOperation operation)
    {
        var panelCarry = _panelCarry;
        _panelCarry = null;
        if (ResolvePane(dock) is not { } hit || dockable is not IDock carrier)
        {
            _f.Carry.ForgetCarried(); // 패널이 아닌 대상 — Dock 기본 분할이 운반 dock 을 트리에 넣는다
            DocumentSplitMode.Adopt(_f, dock, dockable); // 문서 칸 분할이면 새 칸을 MDI·고유 Id 로(Q12 zIndex, EXT-01)
            _f.BaseSplitToDock(dock, dockable, operation);
            FinishPanelCarry(panelCarry, dockable); // 패널 탭 문서도 안내대로 분할한다(w12 Z1, 옛 Q15 ④ C안 대체)
            return;
        }

        if (!IsSplitAxis(hit.Area, operation) || SplitPaneCore(hit.Area, hit.Pane, operation, null) is not { } tabs)
        {
            _f.Carry.ReturnCarried(dock, carrier, operation); // 패널 대상은 원래 자리로 되돌린다 — 갈 곳이 없을 때만 Dock 기본 분할(odev-8m R2, SplitCarryService)
            return;
        }

        foreach (var item in carrier.VisibleDockables?.ToList() ?? new List<IDockable>())
        {
            _f.MoveDockable(carrier, tabs, item, tabs.VisibleDockables?.LastOrDefault()); // 맨 뒤에 붙여 순서를 지킨다(대상 null 이면 Dock 은 마지막 앞에 끼운다)
        }

        if (tabs.VisibleDockables?.FirstOrDefault() is { } first)
        {
            tabs.ActiveDockable = first;
            _f.SetActiveDockable(first);
        }

        _f.Carry.ForgetCarried(); // 분할 성공 — 원래 자리 기억은 더 쓰지 않는다(SplitCarryService)
        _f.Tidy.RetitleSplitPanes();
        _f.RaisePanelTabsChanged(tabs.Id);
        _f.RaiseSplitChanged();
    }

    /// <summary>
    /// 패널 pane 옆(op 방향)에 빈 새 패널을 넣고 그 탭 영역을 돌려준다. 새 패널과 pane 은 pane 이 차지하던 몫을 반씩(1/2) 나눈다.
    /// 영역이 아직 분할되지 않았으면 기본 패널 자리를 영역 ProportionalDock 으로 감싼다. 불가하면 null.
    /// </summary>
    internal PanelDocumentDock? SplitPaneCore(PanelArea area, IDock pane, DockOperation op, string? id)
    {
        if (!IsSplitAxis(area, op) || !CanSplitNow(area) || EnsureArea(area) is not { VisibleDockables: { } list } areaDock)
        {
            return null;
        }

        var index = list.IndexOf(pane);
        if (index < 0)
        {
            return null;
        }

        var parts = list.Count(d => d is not ISplitter);
        var share = double.IsNaN(pane.Proportion) || pane.Proportion <= 0 ? 1.0 / parts : pane.Proportion;
        var created = CreateSplitPane(area, id);
        var splitter = new ProportionalDockSplitter();
        if (op is DockOperation.Top or DockOperation.Left)
        {
            _f.InsertVisible(areaDock, index, created.Pane);
            _f.InsertVisible(areaDock, index + 1, splitter);
        }
        else
        {
            _f.InsertVisible(areaDock, index + 1, splitter);
            _f.InsertVisible(areaDock, index + 2, created.Pane);
        }

        _f.InitDockable(created.Pane, areaDock);
        _f.InitDockable(splitter, areaDock);
        PocDockFactory.SetShare(pane, share / 2);
        PocDockFactory.SetShare(created.Pane, share / 2);
        return created.Tabs;
    }

    /// <summary>영역 ProportionalDock(없으면 기본 패널 자리를 감싸 만든다). 기본 패널이 트리에 없으면 null.</summary>
    private ProportionalDock? EnsureArea(PanelArea area)
    {
        if (_st.Areas.TryGetValue(area, out var existing))
        {
            return existing;
        }

        if (HomePane(area) is not { Owner: IDock { VisibleDockables: { } parentList } parent } home)
        {
            return null;
        }

        var at = parentList.IndexOf(home);
        if (at < 0)
        {
            return null;
        }

        var areaDock = new ProportionalDock
        {
            Id = PocDockFactory.AreaId(area),
            Title = PocDockFactory.AreaId(area),
            IsCollapsable = false,
            Orientation = IsAreaVertical(area) ? Orientation.Vertical : Orientation.Horizontal,
            Proportion = home.Proportion,
            CollapsedProportion = home.Proportion,
            VisibleDockables = _f.CreateList<IDockable>(),
        };
        _f.RemoveVisibleAt(parent, at);
        _f.InsertVisible(parent, at, areaDock);
        PocDockFactory.SetShare(home, 1);
        _f.AddVisible(areaDock, home);
        areaDock.ActiveDockable = home;
        _f.InitDockable(areaDock, parent);
        if (ReferenceEquals(parent.ActiveDockable, home))
        {
            parent.ActiveDockable = areaDock;
        }

        if (ReferenceEquals(parent.DefaultDockable, home))
        {
            parent.DefaultDockable = areaDock;
        }

        _st.Areas[area] = areaDock;
        return areaDock;
    }

    private SplitPane CreateSplitPane(PanelArea area, string? id)
    {
        var prefix = PanelAreaNames.Stored(area) + "Pane";
        if (id is null || _st.SplitPanes.ContainsKey(id))
        {
            var n = 2;
            while (_st.SplitPanes.ContainsKey($"{prefix}.{n}"))
            {
                n++;
            }

            id = $"{prefix}.{n}";
        }

        // 제목은 활성 탭 제목을 따른다(RetitleSplitPanes) — 새 사전 키를 만들지 않는다.
        var tabs = new PanelDocumentDock { Id = id + ".Tabs", Title = "", VisibleDockables = _f.CreateList<IDockable>() };
        var pane = new ToolDock
        {
            Id = id,
            Alignment = PanelMoveService.AlignmentOf(_f.Moves.EdgeOf(area)),
            VisibleDockables = _f.CreateList<IDockable>(tabs),
            ActiveDockable = tabs,
        };
        var created = new SplitPane(id, pane, tabs, area);
        _st.SplitPanes[id] = created;
        return created;
    }
}
