// 측면 패널·도구 숨김(옛 PocDockFactory.Hide — 집중 템플릿·제품 빌드 로그 숨김) — 원래 인덱스·분할선·비율·핀 상태까지 그대로 되돌린다
using System.Collections.Generic;
using System.Linq;
using Dock.Model.Controls;
using Dock.Model.Core;

namespace xkxk.frameworks.ui.shell.Docking;

/// <summary>
/// Dock 기본 HideDockable/RestoreDockable 을 쓰지 않는 이유(12.1.0.12 디컴파일):
/// ① HideDockable 은 CleanupOrphanedSplitters 로 인접 분할선을 지운다.
/// ② RestoreDockable 은 AddVisibleDockable 로 목록 끝에 붙인다(원래 인덱스·분할선 미복원).
/// 그래서 소유 dock·인덱스·분할선·비율·핀을 직접 기억했다가 InsertVisibleDockable 로 같은 자리에 되넣는다.
/// protected 인 Insert/RemoveVisibleDockable 은 팩토리 전달자(_f.InsertVisible·_f.RemoveVisible)로 부른다.
/// </summary>
internal sealed class PanelHideService
{
    /// <summary>
    /// 접힌 바깥 패널 하나의 복원 정보. Siblings 는 숨기기 직전 같은 분할의 다른 칸 비율이다.
    /// 패널을 빼면 남은 칸이 빈자리를 채우며 비율이 바뀌므로(집중 ③ 실측 −13%), 되살릴 때 함께 되돌린다.
    /// </summary>
    internal sealed record CollapsedPanel(IDock Owner, int Index, IDockable? Splitter, bool SplitterBefore, double Proportion, bool WasPinned,
        IReadOnlyList<(IDockable Dockable, double Proportion)> Siblings);

    /// <summary>숨긴 도구 하나의 복원 정보(안쪽 탭 영역과 탭 인덱스).</summary>
    internal sealed record HiddenTool(IDockable Tool, IDock Tabs, int Index);

    private readonly PocDockFactory _f;
    private readonly DockLayoutState _st;
    private readonly List<IDock> _collapseOrder = new();
    private readonly HashSet<IDock> _proportionSetWhileCollapsed = new();

    internal PanelHideService(PocDockFactory f, DockLayoutState st)
    {
        _f = f;
        _st = st;
    }

    /// <summary>바깥 패널 비율을 정한다(ApplyPanels 용). 접혀 있는 동안 정한 값은 되살릴 때 숨기기 전 값보다 우선한다.</summary>
    internal void SetPaneProportion(IDock pane, double proportion)
    {
        PocDockFactory.SetShare(pane, proportion); // 분할선을 끈 뒤 남은 CollapsedProportion 이 새 비율을 덮지 않게(UI-20 f)
        if (_st.Collapsed.ContainsKey(pane))
        {
            _proportionSetWhileCollapsed.Add(pane);
        }
    }

    /// <summary>집중 템플릿으로 패널이 숨겨져 있는지.</summary>
    internal bool SidePanelsHidden { get; private set; }

    /// <summary>도구가 SetToolHidden 으로 숨겨져 있는지.</summary>
    internal bool IsToolHidden(string toolId) => _st.HiddenTools.ContainsKey(toolId);

    /// <summary>패널을 분할선째 숨기거나 원래 자리·비율·핀 상태로 되돌린다. 같은 값이면 아무것도 하지 않는다.</summary>
    internal void SetSidePanelsHidden(bool hidden)
    {
        if (SidePanelsHidden == hidden)
        {
            return;
        }

        SidePanelsHidden = hidden;
        SyncSidePanels();
    }

    /// <summary>안쪽 탭 영역에서 도구 하나를 빼거나 원래 탭 인덱스에 되넣는다. 탭 영역이 비면 바깥 패널을 접고, 다시 차면 되살린다. 성공하면 true.</summary>
    internal bool SetToolHidden(string toolId, bool hidden)
    {
        string? tabsId;
        if (hidden)
        {
            if (_st.HiddenTools.ContainsKey(toolId) || _f.Query.FindPanelTool(toolId) is not { } tool || tool.Owner is not PanelDocumentDock tabs || tabs.VisibleDockables is null)
            {
                return false;
            }

            var index = tabs.VisibleDockables.IndexOf(tool);
            _f.RemoveVisible(tabs, tool);
            if (ReferenceEquals(tabs.ActiveDockable, tool))
            {
                tabs.ActiveDockable = tabs.VisibleDockables.FirstOrDefault();
            }

            _st.HiddenTools[toolId] = new HiddenTool(tool, tabs, index);
            tabsId = tabs.Id;
        }
        else
        {
            if (!_st.HiddenTools.Remove(toolId, out var entry) || entry.Tabs.VisibleDockables is null)
            {
                return false;
            }

            // 숨긴 사이 그 탭 영역의 분할 패널이 정리돼 트리에 없으면(UI-20) 도구의 기본 탭 영역으로 되넣는다.
            var tabs = _f.Query.PanelDocks().Contains(entry.Tabs) ? entry.Tabs
                : _f.Split.HomeTabs(toolId == "Log" ? PanelArea.Log : PanelArea.Explorer) ?? entry.Tabs;
            var list = tabs.VisibleDockables!;
            _f.InsertVisible(tabs, System.Math.Clamp(entry.Index, 0, list.Count), entry.Tool);
            entry.Tool.Owner = tabs;
            tabsId = tabs.Id;
        }

        SyncSidePanels();
        if (tabsId is not null)
        {
            _f.RaisePanelTabsChanged(tabsId);
        }

        return true;
    }

    /// <summary>각 바깥 패널의 목표 상태(집중 숨김, 머리줄 ✕, 숨긴 도구 때문에 빈 탭 영역)를 계산해 접기·되살리기를 맞춘다.</summary>
    internal void SyncSidePanels()
    {
        _f.Tidy.NormalizeSplitAreas(); // 빈 분할 패널 정리·기본 패널 이어받기·영역 감싸기 풀기(UI-20)를 먼저 끝낸다
        _f.Tidy.RetitleSplitPanes();

        // 분할된 영역은 영역 ProportionalDock 째 접고 되살린다(집중 숨김). 정리 뒤 분할 영역의 기본 패널은 비어 있지 않다.
        foreach (var pane in PanelAreaNames.All.Select(_f.Split.SlotOf))
        {
            if (pane is null)
            {
                continue;
            }

            var isCollapsed = _st.Collapsed.ContainsKey(pane);
            if (ShouldCollapse(pane) && !isCollapsed)
            {
                CollapsePanel(pane);
            }
        }

        // 되살리기는 접은 역순(LIFO)으로 해야 기억한 인덱스가 맞는다.
        for (var i = _collapseOrder.Count - 1; i >= 0; i--)
        {
            var pane = _collapseOrder[i];
            if (!ShouldCollapse(pane))
            {
                RestorePanel(pane);
            }
        }

        _f.OpenStates.SyncScales();
    }

    /// <summary>패널을 접어 둬야 하는지. 집중 숨김은 모든 패널에 같게 적용한다.</summary>
    private bool ShouldCollapse(IDock pane)
    {
        return SidePanelsHidden || IsPanelEmpty(pane) || _st.ClosedAreas.Any(a => ReferenceEquals(_f.Split.SlotOf(a), pane));
    }

    /// <summary>
    /// 안쪽 탭 영역이 비었는지(숨긴 도구 때문이든 사용자가 탭을 모두 끌어내서든). 빈 도구 패널은 접는다 —
    /// 탭 없이 '탐색기' 머리글과 빈 몸체만 남는 화면을 만들지 않는다(otest-1 i0339, lead 결정).
    /// 탭이 다시 들어오면(SendDocumentToPanel·레이아웃 복원·도구 다시 보이기) 원래 자리·분할선·비율로 되살린다.
    /// </summary>
    private bool IsPanelEmpty(IDock pane)
    {
        var tabs = pane.Id is { } id ? _f.Tabs.TabsOf(id) : null;
        return tabs is not null && (tabs.VisibleDockables?.Count ?? 0) == 0;
    }

    private void CollapsePanel(IDock pane)
    {
        if (pane.Owner is not IDock owner || owner.VisibleDockables is not { } list)
        {
            return;
        }

        // 비율은 핀 해제로 바뀌기 전에 기억한다.
        var proportion = pane.Proportion;

        // 핀(자동 숨김) 상태면 먼저 풀어 안쪽 탭 영역을 바깥 패널로 되돌린다(PinDockableCore 는 토글, 권한 정책 미평가).
        var tabs = pane.Id is { } id ? _f.Tabs.ContentTabsOf(id) : null;
        var wasPinned = tabs is not null && _f.IsDockablePinned(tabs);
        if (wasPinned)
        {
            _f.Tabs.PinDockableCoreBypassShare(tabs!); // PW2 핀 기억·복원은 건너뛴다(이 경로는 record 가 비율을 맡는다)
        }

        var index = list.IndexOf(pane);
        IDockable? splitter = null;
        var splitterBefore = false;
        if (index > 0 && list[index - 1] is ISplitter before)
        {
            splitter = before;
            splitterBefore = true;
        }
        else if (index + 1 < list.Count && list[index + 1] is ISplitter after)
        {
            splitter = after;
        }

        var siblings = list.Where(d => !ReferenceEquals(d, pane) && d is not ISplitter).Select(d => (d, d.Proportion)).ToList();
        var record = new CollapsedPanel(owner, splitterBefore ? index - 1 : index, splitter, splitterBefore, proportion, wasPinned, siblings);
        _f.RemoveVisible(owner, pane);
        if (splitter is not null)
        {
            _f.RemoveVisible(owner, splitter);
        }

        _st.Collapsed[pane] = record;
        _collapseOrder.Add(pane);
        _f.Diag.DiagSplitRecord("collapse", pane, record, overridden: false, _st.MainPane); // 진단 빌드 전용(제품은 호출·인자 평가가 빠진다)
    }

    internal void RestorePanel(IDock pane)
    {
        if (!_st.Collapsed.Remove(pane, out var r) || r.Owner.VisibleDockables is not { } list)
        {
            return;
        }

        _collapseOrder.Remove(pane);
        var at = System.Math.Clamp(r.Index, 0, list.Count);
        if (r.Splitter is not null && r.SplitterBefore)
        {
            _f.InsertVisible(r.Owner, at, r.Splitter);
            _f.InsertVisible(r.Owner, at + 1, pane);
        }
        else
        {
            _f.InsertVisible(r.Owner, at, pane);
            if (r.Splitter is not null)
            {
                _f.InsertVisible(r.Owner, at + 1, r.Splitter);
            }
        }

        pane.Owner = r.Owner;
        if (r.Splitter is not null)
        {
            r.Splitter.Owner = r.Owner;
        }

        // 숨긴 동안 ApplyPanels 등이 비율을 새로 정했으면(같은 트리 재사용 복원) 그 값을 존중하고, 아니면 숨기기 전 값으로 되돌린다.
        var overridden = _proportionSetWhileCollapsed.Remove(pane);
        if (!overridden)
        {
            PocDockFactory.SetShare(pane, r.Proportion); // CollapsedProportion 도 함께(분할선을 끈 뒤 남은 값이 덮지 않게)
        }

        // 다른 칸 비율도 숨기기 전 값으로 되돌린다. 패널 비율이 새로 정해졌으면 나머지 칸이 (1 − 새 비율)을 같은 비례로 나눠 갖게 한다.
        var savedSum = r.Siblings.Where(s => !double.IsNaN(s.Proportion)).Sum(s => s.Proportion);
        var scale = overridden && savedSum > 0 && !double.IsNaN(pane.Proportion) ? (1 - pane.Proportion) / savedSum : 1;
        foreach (var (sibling, saved) in r.Siblings)
        {
            if (list.Contains(sibling))
            {
                // 자동 몫(NaN)도 그대로 되돌린다. 숨긴 동안 혼자 남은 칸(CenterPane)에 채워진 값(1.0 추정)이 남으면
                // 되살린 패널이 0.18/(0.18+1)=0.1525 로 줄어든다(W-2, i1720rc LeftPane 200→169).
                PocDockFactory.SetShare(sibling, double.IsNaN(saved) ? double.NaN : saved * scale);
            }
        }

        if (r.WasPinned && pane.Id is { } id && _f.Tabs.ContentTabsOf(id) is { } tabs)
        {
            _f.Tabs.PinDockableCoreBypassShare(tabs); // PW2 핀 기억·복원은 건너뛴다(숨기기 전 핀 상태로만 되돌린다)
        }

        _f.Diag.DiagSplitRecord("restore", pane, r, overridden, _st.MainPane);
    }

    /// <summary>CreateLayout 이 새 레이아웃을 만들 때 옛 객체를 가리키는 숨김 기록을 비운다.</summary>
    internal void ResetHiddenState()
    {
        _st.Collapsed.Clear();
        _collapseOrder.Clear();
        _proportionSetWhileCollapsed.Clear();
        _st.ClosedAreas.Clear();
        _f.OpenStates.ResetScales();
        _st.HiddenTools.Clear();
        SidePanelsHidden = false;
    }
}
