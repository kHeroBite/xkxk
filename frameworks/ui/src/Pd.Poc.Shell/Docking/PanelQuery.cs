// 도구 패널 조회와 드롭 정책 본문, 패널 가장자리·탭 구성 저장·복원 — 모든 패널이 같은 경로를 탄다
using Pd.Client.Data.Layout;
using System;
using System.Collections.Generic;
using System.Linq;
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;

namespace Pd.Poc.Shell.Docking;

/// <summary>트리 참조는 DockLayoutState, Dock 기반 동작은 팩토리(_f — MoveDockable 은 재정의를 탄다), 다른 서비스는 _f.Split·_f.Codec·_f.Tabs·_f.Moves 로 부른다.</summary>
internal sealed class PanelQuery
{
    private readonly PocDockFactory _f;
    private readonly DockLayoutState _st;

    internal PanelQuery(PocDockFactory f, DockLayoutState st)
    {
        _f = f;
        _st = st;
    }

    /// <summary>탭 합류를 받는 패널의 Id 목록. 쓰지 않는 여분 패널은 빼고, 분할로 만든 패널(UI-20)이 뒤에 붙는다.</summary>
    internal IReadOnlyList<string> PanelIds => PanelAreaNames.All
        .Where(a => _st.HomePanes.ContainsKey(a) && (a != PanelArea.Extra || (_st.HomeTabs[a].VisibleDockables?.Count ?? 0) > 0))
        .Select(a => _st.HomePanes[a].Id ?? "")
        .Concat(_f.Codec.SplitPaneIds).ToList();

    /// <summary>
    /// 드롭 정책. 패널째 끌기는 PanelDropService 가 판정한다. 패널 가장자리 놓기는 영역 방향과 맞을 때만 받는다(UI-20 — 좌우 가장자리 영역은 위/아래, 상하 가장자리 영역은 왼/오른).
    /// false 는 DockManager 가 도킹 안내 표시(IsDockTargetVisible)를 감추는 데에도 쓴다. Window 는 분리 경로의 판정이라 그대로 둔다.
    /// </summary>
    internal bool CanDrop(IDockable source, IDockable target, DockOperation operation)
    {
        if (!_f.PanelFeature.Enabled && operation != DockOperation.Window
            && !(source.Owner is { } from && (ReferenceEquals(from, target) || ReferenceEquals(from, target.Owner)))
            && !Theming.PanelFeatureRules.AllowsPanelExit(source.Owner is PanelDocumentDock || IsSingleDocPanelDrag(source), operation == DockOperation.Fill, operation == DockOperation.Window))
        {
            return false; // 패널 기능 끔 — 같은 탭 영역 안 순서 바꾸기와 패널 탭 문서의 문서 영역 놓기(Fill)만 허용, 그 밖은 도킹 안내도 숨긴다
        }

        if (operation != DockOperation.Window && _f.Drops.IsPanelDrag(source))
        {
            return _f.Drops.CanDrop(source, target, operation);
        }

        if (operation is DockOperation.Left or DockOperation.Right or DockOperation.Top or DockOperation.Bottom && _f.Split.ResolvePane(target) is { } hit)
        {
            return _f.Split.IsSplitAxis(hit.Area, operation) && _f.Split.CanSplitNow(hit.Area);
        }

        return true;
    }

    /// <summary>문서가 정확히 1개(도구 0)인 패널의 머리줄 끌기인가. 탭 줄이 숨어 머리줄이 곧 그 문서를 끄는 손잡이라, 패널 기능 끔에서도 패널 탭 문서와 같이 밖으로 빼기(Fill·Window)를 받는다(B5). 여러 문서·도구 패널은 끔에서 Window 만이다.</summary>
    private bool IsSingleDocPanelDrag(IDockable source) => _f.Drops.IsPanelDrag(source) && _f.Tabs.PanelFloatItem(source) is not null
        && (source as PanelDocumentDock ?? (source as IDock)?.VisibleDockables?.OfType<PanelDocumentDock>().FirstOrDefault())?.VisibleDockables?.Count == 1;

    /// <summary>패널 가장자리·비율과 탭 구성을 레이아웃 스냅샷(Version 4)에 채운다. CapturePanels 가 부른다.</summary>
    internal void CaptureEdgesAndTabs(PanelsSnapshot snapshot)
    {
        snapshot.ExplorerEdge = _f.Moves.EdgeOf(PanelArea.Explorer).ToString();
        snapshot.LogEdge = _f.Moves.EdgeOf(PanelArea.Log).ToString();
        snapshot.NotificationEdge = _f.Moves.EdgeOf(PanelArea.Notification).ToString();
        snapshot.ExtraEdge = _f.Moves.EdgeOf(PanelArea.Extra).ToString();
        snapshot.NotificationProportion = _f.Split.SlotOf(PanelArea.Notification)?.Proportion; // 분할돼 있으면 영역 자리 비율(UI-20)
        snapshot.ExtraProportion = _f.Split.SlotOf(PanelArea.Extra)?.Proportion;
        snapshot.Tabs = PanelDocks()
            .Where(d => d.Id is not (PocDockFactory.ExtraTabsId) || (d.VisibleDockables?.Count ?? 0) > 0)
            .Select(d => new PanelTabsSnapshot
            {
                PanelId = d.Id,
                ToolIds = d.VisibleDockables?.Select(x => x.Id).ToList() ?? new List<string>(),
                ActiveToolId = d.ActiveDockable?.Id,
            })
            .ToList();
        _f.Codec.CaptureSplits(snapshot); // 분할 영역·순서·비율(UI-20, SplitLayoutCodec)
    }

    /// <summary>
    /// 저장된 가장자리를 적용한다. 분할 복원보다 먼저 부른다(영역 방향이 가장자리를 따른다).
    /// 탐색기·로그 값이 없으면(Version 3 이하) 왼쪽·아래다. 알림 값이 없으면(Version 1·2) 현재를 유지한다. 알 수 없는 문자열은 건너뛴다.
    /// </summary>
    internal void ApplyEdges(PanelsSnapshot snapshot)
    {
        Apply(PanelArea.Explorer, snapshot.ExplorerEdge, PanelEdge.Left);
        Apply(PanelArea.Log, snapshot.LogEdge, PanelEdge.Bottom);
        Apply(PanelArea.Notification, snapshot.NotificationEdge, null);
        Apply(PanelArea.Extra, snapshot.ExtraEdge, null);

        void Apply(PanelArea area, string? stored, PanelEdge? missing)
        {
            if (Enum.TryParse<PanelEdge>(stored, ignoreCase: true, out var edge) && Enum.IsDefined(edge))
            {
                _f.Moves.MoveAreaToEdge(area, edge);
            }
            else if (stored is null && missing is { } fallback)
            {
                _f.Moves.MoveAreaToEdge(area, fallback);
            }
        }
    }

    /// <summary>패널 비율과 탭 구성을 적용한다. ApplyPanels 가 문서 복원 뒤에 부른다. 탭은 저장된 탭 영역·순서로 옮기고, 지금 없는 도구·문서 Id 와 탭 영역 Id 는 건너뛴다.</summary>
    internal void ApplyTabs(PanelsSnapshot snapshot)
    {
        ApplyShare(PanelArea.Notification, snapshot.NotificationProportion);
        ApplyShare(PanelArea.Extra, snapshot.ExtraProportion);
        if (snapshot.Tabs is null)
        {
            return;
        }

        var docks = PanelDocks().ToDictionary(d => d.Id);
        foreach (var tabs in snapshot.Tabs)
        {
            if (!docks.TryGetValue(tabs.PanelId, out var target))
            {
                continue;
            }

            for (var i = 0; i < tabs.ToolIds.Count; i++)
            {
                var tool = FindPanelTool(tabs.ToolIds[i]);
                if (tool is null)
                {
                    continue;
                }

                if (tool.Owner is IDock owner && !ReferenceEquals(owner, target))
                {
                    _f.MoveDockable(owner, target, tool, null);
                }

                var list = target.VisibleDockables;
                if (list is not null && list.IndexOf(tool) is var at and >= 0 && at != Math.Min(i, list.Count - 1))
                {
                    list.RemoveAt(at);
                    list.Insert(Math.Min(i, list.Count), tool);
                }
            }

            if (tabs.ActiveToolId is { } activeId && target.VisibleDockables?.FirstOrDefault(d => d.Id == activeId) is { } active)
            {
                target.ActiveDockable = active;
            }
        }
    }

    private void ApplyShare(PanelArea area, double? share)
    {
        if (_f.Split.SlotOf(area) is { } slot && share is double proportion and > 0 and < 1)
        {
            PocDockFactory.SetShare(slot, proportion);
        }
    }

    /// <summary>탭 목록(원격 명령 panel.tabs 용). 없는 패널이면 빈 목록.</summary>
    internal IReadOnlyList<string> PanelTabs(string panelId) =>
        ((IDock?)_f.Tabs.TabsOf(panelId) ?? PanelDocks().FirstOrDefault(d => d.Id == panelId))?.VisibleDockables?.Select(d => d.Id).ToList()
        ?? (IReadOnlyList<string>)Array.Empty<string>();

    /// <summary>탭을 가진 패널 영역: 기본 패널과 분할 패널의 안쪽 탭 영역.</summary>
    internal IEnumerable<IDock> PanelDocks()
    {
        foreach (var area in PanelAreaNames.All)
        {
            if (_st.HomeTabs.TryGetValue(area, out var tabs))
            {
                yield return tabs;
            }
        }

        foreach (var id in _f.Codec.SplitPaneIds)
        {
            yield return _st.SplitPanes[id].Tabs; // 분할 패널 탭 영역(UI-20)
        }
    }

    internal IDockable? FindPanelTool(string id)
    {
        foreach (var dock in PanelDocks())
        {
            if (dock.VisibleDockables?.FirstOrDefault(d => d.Id == id) is { } found)
            {
                return found;
            }
        }

        // 도구와 문서(패널 탭으로 들어간 PocDocument)만 옮긴다. 도크 자체는 대상이 아니다.
        return _f.Root is null ? null : _f.FindDockable(_f.Root, d => d.Id == id && (d is Tool || d is PocDocument));
    }
}
