// 패널 영역 분할(UI-20) 저장·복원과 조회(옛 PocDockFactory.SplitLayout) — 분할 구조·순서·비율을 PanelTabsSnapshot(Area·Index·Proportion)에 실어 레이아웃과 함께 남긴다
using xkxk.frameworks.ui.data.Layout;
using System;
using System.Collections.Generic;
using System.Linq;
using Dock.Model.Controls;
using Dock.Model.Core;

namespace xkxk.frameworks.ui.shell.Docking;

/// <summary>레이아웃 패널 저장·복원(CapturePanels·ApplyPanels)과 영역 조회. 숨김은 _f.Hide, 분할은 _f.Split, 탭 구성은 _f.Query 로 부른다.</summary>
internal sealed class SplitLayoutCodec
{
    private readonly PocDockFactory _f;
    private readonly DockLayoutState _st;

    internal SplitLayoutCodec(PocDockFactory f, DockLayoutState st)
    {
        _f = f;
        _st = st;
    }

    /// <summary>패널 비율·가장자리와 탭 구성·분할 캡처(레이아웃 Version 4).</summary>
    internal PanelsSnapshot? CapturePanels()
    {
        if (_f.Split.SlotOf(PanelArea.Explorer) is not { } explorer || _f.Split.SlotOf(PanelArea.Log) is not { } log)
        {
            return null;
        }

        // 분할된 영역이면 영역 자리 비율이 문서 영역 대비 몫이다(UI-20). 영역 안 비율은 탭 스냅샷(Area·Proportion)에 담는다.
        var snapshot = new PanelsSnapshot
        {
            ExplorerProportion = explorer.Proportion,
            LogProportion = log.Proportion,
            CornerOwners = _f.Corners.Capture(),
        };
        _f.Query.CaptureEdgesAndTabs(snapshot);
        return snapshot;
    }

    /// <summary>도구 패널 비율 적용. null(Version 1 저장본)·잘못된 값(0 이하·1 이상·NaN)은 건너뛰고 현재 값을 유지한다.</summary>
    internal void ApplyPanels(PanelsSnapshot? panels)
    {
        if (panels is null)
        {
            return;
        }

        _f.Corners.ResetForRestore(); // 저장 비율은 저장 당시 배치의 값이다 — 기본 배치로 돌린 뒤 넣고 끝에서 모서리 소유를 입힌다(요건 3)
        _f.Query.ApplyEdges(panels); // 영역 방향이 가장자리를 따르므로 분할 복원보다 먼저
        PrepareSplitRestore(panels.Tabs); // 저장된 분할 패널을 빈 채로 먼저 만든다(UI-20) — 아래 비율·탭 이동이 그 자리를 쓴다
        static bool Valid(double p) => !double.IsNaN(p) && p > 0 && p < 1;
        if (_f.Split.SlotOf(PanelArea.Explorer) is { } explorerSlot && Valid(panels.ExplorerProportion))
        {
            _f.Hide.SetPaneProportion(explorerSlot, panels.ExplorerProportion);
        }

        if (_f.Split.SlotOf(PanelArea.Log) is { } logSlot && Valid(panels.LogProportion))
        {
            _f.Hide.SetPaneProportion(logSlot, panels.LogProportion);
        }

        _f.Query.ApplyTabs(panels); // Version 3 필드(null 이면 현재 유지)
        ApplySplitProportions(panels.Tabs); // 영역 안 비율(UI-20)
        _f.Hide.SyncSidePanels(); // 복원으로 탭이 다시 들어온 빈 패널은 되살리고, 빈 채로 남은 패널은 접는다
        _f.Corners.ApplyRestored(panels.CornerOwners);
    }

    /// <summary>분할로 만든 패널 Id 목록(기본 패널 제외).</summary>
    internal IReadOnlyList<string> SplitPaneIds => _st.SplitPanes.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();

    /// <summary>영역이 분할돼 감싸져 있는지와 영역 자리의 비율·방향.</summary>
    internal (bool Wrapped, double Proportion, bool Vertical, string? SlotId) AreaInfo(PanelArea area)
    {
        var slot = _f.Split.SlotOf(area);
        return (_st.Areas.ContainsKey(area), slot?.Proportion ?? double.NaN, _f.Split.IsAreaVertical(area), slot?.Id);
    }

    /// <summary>영역 안 패널을 화면 순서대로. 분할되지 않았으면 기본 패널 1개(접혀 있으면 빈 목록).</summary>
    internal IReadOnlyList<SplitPaneInfo> PanesOf(PanelArea area)
    {
        IList<IDockable> list;
        if (_st.Areas.TryGetValue(area, out var areaDock))
        {
            list = areaDock.VisibleDockables ?? new List<IDockable>();
        }
        else if (_f.Split.HomePane(area) is { Owner: IDock { VisibleDockables: { } parentList } } home && parentList.Contains(home))
        {
            list = new List<IDockable> { home };
        }
        else
        {
            return Array.Empty<SplitPaneInfo>();
        }

        return list.OfType<IDock>().Where(d => d is not ISplitter).Select((pane, i) =>
        {
            var tabs = _f.Tabs.TabsOf(pane.Id ?? "");
            IDock content = (IDock?)tabs ?? pane;
            return new SplitPaneInfo(pane.Id, tabs?.Id, area, i, pane.Proportion, ReferenceEquals(pane, _f.Split.HomePane(area)),
                content.VisibleDockables?.Select(d => d.Id).ToList() ?? new List<string>(), content.ActiveDockable?.Id, pane);
        }).ToList();
    }

    /// <summary>
    /// 명시 분할(원격·메뉴용): 패널 paneId 옆 op 방향에 빈 새 패널을 만든다. 성공하면 새 패널 Id.
    /// 끌어 놓기 분할과 달리 내용을 옮기지 않는다 — 빈 패널은 다음 정리(SyncSidePanels)에서 빠지므로 곧바로 탭을 넣어야 한다.
    /// </summary>
    internal string? SplitEmptyPane(string paneId, DockOperation op)
    {
        var dock = _f.Tabs.TabsOf(paneId);
        if (_f.Split.ResolvePane(dock) is not { } hit || _f.Split.SplitPaneCore(hit.Area, hit.Pane, op, null) is not { } tabs)
        {
            return null;
        }

        _f.RaiseSplitChanged();
        return tabs.Id[..^".Tabs".Length];
    }

    /// <summary>분할 정보를 탭 스냅샷에 채운다(CaptureNotificationAndTabs 뒤). 분할되지 않은 영역은 필드를 비워 옛 저장본과 같은 모양으로 둔다.</summary>
    internal void CaptureSplits(PanelsSnapshot snapshot)
    {
        snapshot.Tabs ??= new List<PanelTabsSnapshot>();
        foreach (var area in _st.Areas.Keys.ToList())
        {
            foreach (var info in PanesOf(area))
            {
                var tabsId = info.TabsId ?? info.PaneId;
                var entry = snapshot.Tabs.FirstOrDefault(t => t.PanelId == tabsId);
                if (entry is null)
                {
                    entry = new PanelTabsSnapshot { PanelId = tabsId, ToolIds = info.Tabs.ToList(), ActiveToolId = info.Active };
                    snapshot.Tabs.Add(entry);
                }

                entry.Area = PanelAreaNames.Stored(area);
                entry.Index = info.Index;
                entry.Proportion = double.IsNaN(info.Proportion) ? null : info.Proportion;
            }
        }
    }

    /// <summary>
    /// 복원 준비(ApplyPanels 첫 단계). 분할 기록이 있으면 분할을 다시 만들 수 있게 영역 자리를 보이게 한다 —
    /// 집중 숨김은 풀고(이어서 ApplySidePanels 가 저장값으로 다시 맞춘다), 비어서 접힌 기본 패널은 되살린다(끝의 SyncSidePanels 가 여전히 비었으면 다시 접는다).
    /// </summary>
    private void PrepareSplitRestore(List<PanelTabsSnapshot>? tabs)
    {
        if (tabs?.Any(t => t.Area is not null) != true)
        {
            return;
        }

        if (_f.Hide.SidePanelsHidden)
        {
            _f.Hide.SetSidePanelsHidden(false);
        }

        foreach (var area in PanelAreaNames.All)
        {
            if (_f.Split.HomePane(area) is { } home && _st.Collapsed.ContainsKey(home))
            {
                _f.Hide.RestorePanel(home);
            }
        }

        RebuildSplits(tabs);
    }

    /// <summary>
    /// 저장된 분할 패널을 빈 채로 먼저 만든다(PrepareSplitRestore). 이미 있는 Id 는 다시 쓰고, 영역 안 순서는 Index 대로 맞춘다.
    /// 탭 이동(ApplyNotificationAndTabs)이 이 패널들로 도구·문서를 옮긴 뒤 ApplySplitProportions 가 비율을 맞춘다.
    /// </summary>
    private void RebuildSplits(List<PanelTabsSnapshot>? tabs)
    {
        if (tabs is null)
        {
            return;
        }

        foreach (var group in tabs.Where(t => t.Area is not null && t.Index is not null).GroupBy(t => t.Area!))
        {
            if (!PanelAreaNames.TryParse(group.Key, out var area) || _f.Split.HomePane(area) is null)
            {
                continue;
            }

            var ordered = group.OrderBy(t => t.Index).ToList();
            foreach (var t in ordered)
            {
                var paneId = t.PanelId.EndsWith(".Tabs", StringComparison.Ordinal) ? t.PanelId[..^".Tabs".Length] : t.PanelId;
                if (ReferenceEquals(ResolvePaneById(paneId), null) && PanesOf(area).LastOrDefault() is { } last)
                {
                    _f.Split.SplitPaneCore(area, last.Pane, _f.Split.IsAreaVertical(area) ? DockOperation.Bottom : DockOperation.Right, paneId);
                }
            }

            if (_st.Areas.TryGetValue(area, out var areaDock) && areaDock.VisibleDockables is { } list)
            {
                var want = ordered.Select(t => ResolvePaneById(t.PanelId.EndsWith(".Tabs", StringComparison.Ordinal) ? t.PanelId[..^".Tabs".Length] : t.PanelId))
                    .OfType<IDock>().Where(list.Contains).ToList();
                var others = list.Where(d => d is not ISplitter && !want.Contains(d)).ToList();
                var splitters = list.Where(d => d is ISplitter).ToList();
                var panes = want.Concat(others).ToList();
                if (panes.Count > 0 && splitters.Count >= panes.Count - 1)
                {
                    list.Clear();
                    for (var i = 0; i < panes.Count; i++)
                    {
                        if (i > 0)
                        {
                            list.Add(splitters[i - 1]);
                        }

                        list.Add(panes[i]);
                    }
                }
            }
        }
    }

    /// <summary>저장된 영역 안 비율(Proportion)을 적용한다. 없는 패널·잘못된 값은 건너뛴다.</summary>
    private void ApplySplitProportions(List<PanelTabsSnapshot>? tabs)
    {
        foreach (var t in tabs?.Where(t => t.Area is not null && t.Proportion is double p && p > 0 && p <= 1) ?? Enumerable.Empty<PanelTabsSnapshot>())
        {
            var paneId = t.PanelId.EndsWith(".Tabs", StringComparison.Ordinal) ? t.PanelId[..^".Tabs".Length] : t.PanelId;
            if (ResolvePaneById(paneId) is { } pane && _st.Areas.Values.Any(a => a.VisibleDockables?.Contains(pane) == true))
            {
                PocDockFactory.SetShare(pane, t.Proportion!.Value);
            }
        }
    }

    /// <summary>패널 Id(기본 패널 또는 분할 패널)로 바깥 패널을 찾는다.</summary>
    private IDock? ResolvePaneById(string paneId) => _f.Split.ResolvePane(_f.Tabs.TabsOf(paneId))?.Pane;
}
