// 패널 영역 분할 정리(UI-20 e, 옛 PocDockFactory.SplitTidy) — 빈 분할 패널 빼기·기본 패널 이어받기·영역 감싸기 풀기·제목 맞추기
using System.Collections.Generic;
using System.Linq;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;

namespace Pd.Poc.Shell.Docking;

/// <summary>
/// 설계서는 Split+SplitTidy 를 SplitAreaService 하나로 두었지만 합치면 .cs 300줄을 넘는다(274+158). 그래서 정리 부분만 따로 둔다.
/// 영역 기본 패널 조회는 _f.Split, 이벤트 SplitChanged 는 _f.RaiseSplitChanged 로 발화한다.
/// </summary>
internal sealed class SplitAreaCleanup
{
    private readonly PocDockFactory _f;
    private readonly DockLayoutState _st;

    internal SplitAreaCleanup(PocDockFactory f, DockLayoutState st)
    {
        _f = f;
        _st = st;
    }

    /// <summary>언어가 바뀌면 측면 패널 제목을 다시 만든다(UI-16). 탐색기·로그 도구 탭 제목은 그 도구 소유자(MainViewModel)가 바꾼다.</summary>
    internal void RelocalizePanels()
    {
        foreach (var (area, key) in new[] { (PanelArea.Explorer, "panel.explorer"), (PanelArea.Log, "panel.log"), (PanelArea.Notification, "notify.title") })
        {
            if (_st.HomeTabs.TryGetValue(area, out var tabs))
            {
                tabs.Title = Localization.Loc.Tr(key);
            }
        }

        if (_f.Documents is DocumentDock documents)
        {
            documents.Title = Localization.Loc.Tr("panel.documents");
            documents.EmptyContent = Localization.Loc.Tr("panel.documents.empty");
        }

        RetitleSplitPanes(); // 분할 패널 제목 = 활성 탭 제목(UI-20)
    }

    /// <summary>
    /// 분할 정리(UI-20 e). SyncSidePanels 가 먼저 부른다. 접힌(집중 숨김) 영역은 건드리지 않는다.
    /// ① 탭이 빈 분할 패널은 분할선째 뺀다 — 남은 패널이 그 몫을 나눠 가진다.
    /// ② 기본 패널(탐색기·로그) 탭이 비었고 다른 분할 패널이 남아 있으면, 첫 분할 패널의 탭을 기본 패널로 옮기고 그 분할 패널을 뺀다(기본 패널이 자리를 이어받는다).
    /// ③ 영역에 기본 패널만 남으면 영역 감싸기를 풀고 기본 패널을 영역 자리·비율로 되돌린다.
    /// </summary>
    internal void NormalizeSplitAreas()
    {
        var changed = false;
        foreach (var (area, areaDock) in _st.Areas.ToList())
        {
            if (_st.Collapsed.ContainsKey(areaDock) || areaDock.VisibleDockables is not { } list || _f.Split.HomePane(area) is not { } home)
            {
                continue;
            }

            foreach (var sp in _st.SplitPanes.Values.Where(p => p.Area == area && list.Contains(p.Pane) && IsTabsEmpty(p.Tabs)).ToList())
            {
                RemoveSplitPane(areaDock, sp);
                changed = true;
            }

            if (_f.Split.HomeTabs(area) is { } homeTabs && IsTabsEmpty(homeTabs)
                && _st.SplitPanes.Values.Where(p => p.Area == area && list.Contains(p.Pane)).OrderBy(p => list.IndexOf(p.Pane)).FirstOrDefault() is { } heir)
            {
                var active = heir.Tabs.ActiveDockable;
                foreach (var item in heir.Tabs.VisibleDockables?.ToList() ?? new List<IDockable>())
                {
                    _f.MoveDockable(heir.Tabs, homeTabs, item, homeTabs.VisibleDockables?.LastOrDefault()); // 맨 뒤에 붙여 탭 순서를 지킨다
                }

                if (active is not null && homeTabs.VisibleDockables?.Contains(active) == true)
                {
                    homeTabs.ActiveDockable = active;
                }

                // 기본 패널을 이어받는 분할 패널 자리로 옮긴 뒤 그 분할 패널을 뺀다(화면 배치는 그대로).
                var heirAt = list.IndexOf(heir.Pane);
                var homeAt = list.IndexOf(home);
                var heirShare = heir.Pane.Proportion;
                if (heirAt >= 0 && homeAt >= 0)
                {
                    list[homeAt] = heir.Pane;
                    list[heirAt] = home;
                    var homeShare = home.Proportion;
                    PocDockFactory.SetShare(home, heirShare);
                    PocDockFactory.SetShare(heir.Pane, homeShare);
                }

                RemoveSplitPane(areaDock, heir);
                changed = true;
            }

            if (list.Count(d => d is not ISplitter) <= 1 && list.Contains(home))
            {
                UnwrapArea(area, areaDock, home);
                changed = true;
            }
        }

        if (changed)
        {
            _f.RaiseSplitChanged();
        }
    }

    private static bool IsTabsEmpty(IDock tabs) => (tabs.VisibleDockables?.Count ?? 0) == 0;

    /// <summary>분할 패널 하나를 인접 분할선과 함께 영역에서 빼고, 남은 칸 비율 합을 1 로 맞춘다.</summary>
    private void RemoveSplitPane(ProportionalDock areaDock, SplitAreaService.SplitPane sp)
    {
        var list = areaDock.VisibleDockables;
        var at = list?.IndexOf(sp.Pane) ?? -1;
        if (list is null || at < 0)
        {
            _st.SplitPanes.Remove(sp.Id);
            return;
        }

        IDockable? splitter = at > 0 && list[at - 1] is ISplitter b ? b : at + 1 < list.Count && list[at + 1] is ISplitter a ? a : null;
        _f.RemoveVisible(areaDock, sp.Pane);
        if (splitter is not null)
        {
            _f.RemoveVisible(areaDock, splitter);
        }

        if (ReferenceEquals(areaDock.ActiveDockable, sp.Pane))
        {
            areaDock.ActiveDockable = list.FirstOrDefault(d => d is not ISplitter);
        }

        _st.SplitPanes.Remove(sp.Id);
        var rest = list.Where(d => d is not ISplitter).ToList();
        var sum = rest.Where(d => !double.IsNaN(d.Proportion) && d.Proportion > 0).Sum(d => d.Proportion);
        foreach (var d in rest)
        {
            PocDockFactory.SetShare(d, sum > 0 && !double.IsNaN(d.Proportion) && d.Proportion > 0 ? d.Proportion / sum : 1.0 / rest.Count);
        }
    }

    /// <summary>영역 감싸기를 풀어 기본 패널을 영역 자리·영역 비율로 되돌린다.</summary>
    private void UnwrapArea(PanelArea area, ProportionalDock areaDock, IDock home)
    {
        _st.Areas.Remove(area);
        if (areaDock.Owner is not IDock { VisibleDockables: { } parentList } parent)
        {
            return;
        }

        var at = parentList.IndexOf(areaDock);
        if (at < 0)
        {
            return;
        }

        _f.RemoveVisible(areaDock, home);
        _f.RemoveVisibleAt(parent, at);
        _f.InsertVisible(parent, at, home);
        PocDockFactory.SetShare(home, areaDock.Proportion);
        _f.InitDockable(home, parent);
        if (ReferenceEquals(parent.ActiveDockable, areaDock))
        {
            parent.ActiveDockable = home;
        }

        if (ReferenceEquals(parent.DefaultDockable, areaDock))
        {
            parent.DefaultDockable = home;
        }
    }

    /// <summary>분할 패널 머리줄 제목 = 활성 탭 제목(언어 전환·탭 이동 뒤 다시 맞춘다).</summary>
    internal void RetitleSplitPanes()
    {
        foreach (var sp in _st.SplitPanes.Values)
        {
            sp.Tabs.Title = sp.Tabs.ActiveDockable?.Title ?? sp.Tabs.VisibleDockables?.FirstOrDefault()?.Title ?? "";
        }
    }

    /// <summary>CreateLayout 이 새 트리를 만들 때 옛 분할 기록을 비운다.</summary>
    internal void ResetSplitState()
    {
        _st.SplitPanes.Clear();
        _st.Areas.Clear();
    }
}
