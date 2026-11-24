// 패널 탭 합류(UI-14, 대안 A + 핀 유지, 옛 PocDockFactory.PanelTabs) — 탭 영역 찾기·문서 보내기·되돌리기·▾ 문서로 도킹·도구 드러내기·핀(PW2 몫 기억)
using System;
using System.Linq;
using Dock.Model;
using Dock.Model.Controls;
using Dock.Model.Core;
using xkxk.frameworks.ui.shell.ViewModels;

namespace xkxk.frameworks.ui.shell.Docking;

/// <summary>
/// override(DockAsDocument·PinDockableCore·OnDockableMoved)는 PocDockFactory 에 남고 본문만 여기로 위임한다(P6).
/// base.PinDockableCore 는 _f.BasePinDockableCore, 재정의를 타는 핀 토글은 _f.PinCore 로 부른다. 이벤트 PanelTabsChanged 는 _f.RaisePanelTabsChanged 로 발화한다.
/// </summary>
internal sealed class PanelTabsService
{
    private readonly PocDockFactory _f;
    private readonly DockLayoutState _st;

    /// <summary>0보다 크면 CollapsePanel·RestorePanel 내부 핀 토글이다(PW2 기억·복원을 건너뛴다). 재진입을 위해 계수로 둔다.</summary>
    private int _pinShareBypass;

    internal PanelTabsService(PocDockFactory f, DockLayoutState st)
    {
        _f = f;
        _st = st;
    }

    /// <summary>패널 Id(바깥 패널 또는 안쪽 탭 영역 Id)로 탭 영역을 찾는다. 없는 Id 는 null.</summary>
    internal PanelDocumentDock? TabsOf(string panelId)
    {
        foreach (var (area, tabs) in _st.HomeTabs)
        {
            if (panelId == tabs.Id || panelId == _st.HomePanes[area].Id)
            {
                return tabs;
            }
        }

        // 분할 패널(UI-20): 바깥 Id(예: LeftPane.2) 또는 탭 영역 Id(LeftPane.2.Tabs)
        return _st.SplitPanes.TryGetValue(panelId, out var sp) ? sp.Tabs
            : panelId.EndsWith(".Tabs", StringComparison.Ordinal) && _st.SplitPanes.TryGetValue(panelId[..^".Tabs".Length], out var sp2) ? sp2.Tabs : null;
    }

    /// <summary>핀·닫기 대상이 되는 안쪽 영역(패널의 탭 영역). 없는 Id 는 null.</summary>
    internal IDock? ContentTabsOf(string panelId) => TabsOf(panelId);

    /// <summary>문서를 지정한 측면 패널의 탭으로 옮긴다(끌어 놓기와 같은 결과의 명시 동작 — 원격·메뉴용). 성공하면 true.</summary>
    internal bool SendDocumentToPanel(PocDocument document, string panelId)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (ContentTabsOf(panelId) is not { } target || document.Owner is not IDock owner)
        {
            return false; // 없는 패널은 대상이 아니다
        }

        if (!ReferenceEquals(owner, target))
        {
            _f.MoveDockable(owner, target, document, null);
        }

        _f.Hide.SyncSidePanels(); // 숨긴 도구 때문에 접힌 패널이면 문서가 들어와 다시 보이게 한다
        _f.SetActiveDockable(document);
        _f.RaisePanelTabsChanged(target.Id);
        return true;
    }

    /// <summary>패널 탭에 있는 문서를 중앙 문서 영역으로 되돌린다. 성공하면 true.</summary>
    internal bool ReturnPanelDocument(PocDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (_f.Documents is null || document.Owner is not PanelDocumentDock owner)
        {
            return false;
        }

        _f.MoveDockable(owner, _f.Documents, document, null);
        _f.SetActiveDockable(document);
        _f.RaisePanelTabsChanged(owner.Id);
        return true;
    }

    /// <summary>
    /// ▾ '문서로 도킹'(D-DAD1). Dock 기본(FactoryBase.DockAsDocument)은 트리를 앞에서부터 훑어 처음 만난 IDocumentDock 으로 옮긴다 —
    /// 이 구성에서는 좌측 탭 영역(LeftPane.Tabs, 알림이 왼쪽이면 알림 탭 영역)이 먼저 걸려 중앙 문서 영역으로 가지 않는다(디컴파일 확인).
    /// 그래서 대상을 중앙 문서 영역(Documents)으로 정한다. 측면 패널 탭 영역 자체가 오면(머리줄 ▾) 그 활성 탭을 옮긴다.
    /// 측면 탭 영역이 문서를 받는 끌어 놓기·합류 경로(UI-14·UI-20)는 바꾸지 않는다.
    /// 바깥 패널(ToolDock, 예 LeftPane)이 오면 안쪽 탭 영역으로 바꿔 읽는다. 그래도 컨테이너(IDock)가 남으면 옮기지 않는다 —
    /// 컨테이너를 문서 영역에 넣으면 그려지지 않아 도구가 화면·트리에서 사라지고 되살릴 수 없다(D-DAD2, i1835rc 실측).
    /// </summary>
    internal void DockAsDocument(IDockable dockable)
    {
        var source = dockable is IToolDock outer && outer.VisibleDockables?.OfType<PanelDocumentDock>().FirstOrDefault() is { } inner
            ? inner : dockable; // 바깥 패널 → 안쪽 탭 영역
        var item = source is PanelDocumentDock tabs ? tabs.ActiveDockable : source;
        if (item is null || item is IDock
            || item.Owner is not IDock owner || _f.Documents is not IDock center || ReferenceEquals(owner, center)
            || !DockCapabilityResolver.IsEnabled(item, DockCapability.DockAsDocument, DockCapabilityResolver.ResolveOperationDock(item)))
        {
            return;
        }

        _f.MoveDockable(owner, center, item, center.VisibleDockables?.LastOrDefault());
        _f.SetActiveDockable(item);
        _f.RaisePanelTabsChanged(owner.Id);
        _f.Hide.SyncSidePanels(); // 옮긴 뒤 빈 측면 패널은 접는다(분할 패널이면 정리)
    }

    /// <summary>
    /// 보기 메뉴 '탐색기'·'로그'·'알림' 용(odev-2) — 도구를 화면에 드러낸다. 대상 도구가 아니면 false.
    /// ① SetToolHidden 으로 숨겨져 있으면 원래 탭 인덱스에 되넣는다.
    /// ② 홈 패널이 접혀 있으면(도구를 다른 곳으로 옮겨 빈 패널이 접힘) 도구를 홈 탭 영역으로 옮기고 원래 자리·분할선·비율로 되살린다.
    /// ③ 그 밖에는 옮기지 않고 지금 있는 곳에서 활성화한다.
    /// ④ 소유 패널이 핀(자동 숨김) 상태면 핀은 유지하고 플라이아웃만 연다(사용자가 고른 핀 설정을 메뉴가 바꾸지 않는다).
    /// 집중 템플릿(SidePanelsHidden)은 호출자가 먼저 푼다.
    /// </summary>
    internal bool RevealTool(string toolId)
    {
        var (tool, area) = toolId switch
        {
            "Explorer" => ((IDockable?)_f.ExplorerTool, PanelArea.Explorer),
            "Log" => (_f.LogTool, PanelArea.Log),
            NotificationPanelViewModel.ToolId => (_f.Notifications, PanelArea.Notification),
            _ => ((IDockable?)null, PanelArea.Explorer),
        };
        if (tool is null || !_st.HomePanes.TryGetValue(area, out var pane) || !_st.HomeTabs.TryGetValue(area, out var home))
        {
            return false;
        }

        _f.OpenStates.ReopenPanel(pane); // 머리줄 ✕ 로 닫혀 있으면 아래 SyncSidePanels 가 원래 자리로 되살린다

        if (_f.Hide.IsToolHidden(toolId))
        {
            _f.Hide.SetToolHidden(toolId, false);
        }

        // 도구가 어디에도 보이지 않거나 문서 영역에 있으면 홈으로 돌린다. 홈이 접혀 있으면 되살린다
        if (_st.Collapsed.ContainsKey(pane) && tool.Owner is IDock owner && !ReferenceEquals(owner, home))
        {
            _f.MoveDockable(owner, home, tool, null);
        }

        _f.Hide.SyncSidePanels();
        if (_f.IsDockablePinned(home))
        {
            home.ActiveDockable = tool;
            _f.PreviewPinnedDockable(home);
        }
        else
        {
            _f.SetActiveDockable(tool);
        }

        _f.RaisePanelTabsChanged(PocDockFactory.PanelOf(tool) ?? home.Id);
        return true;
    }

    /// <summary>패널 제목줄 끌기가 띄우려는 대상을 정한다. 패널이 아니면 그대로, 도구가 있는 패널이면 null(띄우지 않음), 문서만 있는 패널이면 활성 문서(패널은 트리에 남는다).</summary>
    internal IDockable? PanelFloatItem(IDockable dockable)
    {
        var tabs = dockable as PanelDocumentDock ?? (dockable as IToolDock)?.VisibleDockables?.OfType<PanelDocumentDock>().FirstOrDefault();
        if (tabs is null)
        {
            return dockable;
        }

        return tabs.VisibleDockables?.Any(d => d is ITool) == false ? tabs.ActiveDockable : null;
    }

    /// <summary>
    /// PocDockFactory.MoveDockable(override)가 base 호출 전에 부른다. false 면 호출자가 옮기지 않는다.
    /// 패널 탭 영역은 어떤 놓기로도 옮기지 않는다(D-PANELLOST). 문서만 있는 패널을 다른 곳의 가운데 안내에 놓으면 활성 문서만 분리한다. 자기 패널에 놓으면 그대로 둔다.
    /// </summary>
    internal bool BeforeMove(IDock sourceDock, IDock targetDock, IDockable dockable)
    {
        if (dockable is not PanelDocumentDock tabs)
        {
            _f.Carry.Remember(sourceDock, targetDock, dockable); // 트리 밖 운반 dock 으로 옮기면 원래 자리 기억(SplitCarryService)
            _f.Split.NotePanelCarry(sourceDock, targetDock); // 패널 탭에서 운반 dock 으로 — 분할 뒤 원래 패널을 정리한다(SplitAreaService.FinishPanelCarry)
            CenterDropToMdi.NoteMove(_f, sourceDock, targetDock, dockable); // 패널 → MDI 문서 칸 가운데 놓기는 커서 자리 MDI 창(U13)
            return true;
        }

        if (!ReferenceEquals(targetDock, tabs) && PanelFloatItem(sourceDock) is { } item)
        {
            if (_f.DockControls.FirstOrDefault(c => ReferenceEquals(c.Layout, _f.Root))?.DockManager.ScreenPosition is { } drop)
            {
                sourceDock.SetPointerScreenPosition(drop.X, drop.Y); // 안내 놓기는 Dock 이 포인터 위치를 넘기지 않는다
            }

            sourceDock.GetPointerScreenPosition(out var px, out var py);
            _f.FloatDockable(sourceDock);
            if (double.IsNaN(px) || double.IsNaN(py))
            {
                SdiScreenPlacement.PlaceOnMainMonitor(_f.Root, item); // 포인터 좌표가 없으면 Dock 후보 위치는 주 모니터다(메인 모니터로)
            }
        }

        return false;
    }

    /// <summary>분리 창 생성을 실행하고, 패널 탭에서 빠졌으면 빈 패널을 바로 접는다. 창 생성은 MoveDockable 을 타지 않아 OnDockableMoved 의 동기화가 오지 않는다(디컴파일 SplitToWindow).</summary>
    internal void RunFloat(IDockable item, Action floatCore)
    {
        var fromPanel = item.Owner is PanelDocumentDock;
        floatCore();
        if (fromPanel)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(_f.Hide.SyncSidePanels);
        }
    }

    /// <summary>패널(바깥 또는 안쪽 탭 영역 Id)의 핀(자동 숨김) 여부.</summary>
    internal bool IsPanelPinned(string panelId) => ContentTabsOf(panelId) is { } tabs && _f.IsDockablePinned(tabs);

    /// <summary>
    /// 측면 패널을 핀(자동 숨김) 상태로 만들거나 푼다(원격 시험·메뉴용). 핀 대상은 안쪽 탭 영역이다(PinDockableCore 는 Owner 가 IToolDock 일 때만 처리).
    /// 집중 숨김 등으로 패널이 접혀 있으면 false(복원 기록의 핀 상태와 어긋나지 않게 한다). 이미 같은 상태면 true.
    /// </summary>
    internal bool SetPanelPinned(string panelId, bool pinned)
    {
        if (ContentTabsOf(panelId) is not { } tabs || (pinned && !_f.PanelFeature.Enabled) || _f.Hide.SidePanelsHidden || _st.Collapsed.Keys.Any(p => ContentTabsOf(p.Id ?? "") == tabs))
        {
            return false;
        }

        if (_f.IsDockablePinned(tabs) != pinned)
        {
            _f.PinCore(tabs);
        }

        return _f.IsDockablePinned(tabs) == pinned;
    }

    /// <summary>
    /// 모든 핀 토글 경로가 여기를 지난다(12.1.0.12 디컴파일: 테마 📌 IFactory.PinDockable·Unpin·Move·Hide·RemoveDockable·원격 SetPanelPinned).
    /// 측면 패널 탭 영역이면 핀 켜기 직전 비율을 기억하고 핀이 풀리면 되돌린다(PW2, PanelPinShares). 그 밖·집중 숨김 내부 호출은 기본 동작이다.
    /// PocDockFactory.PinDockableCore(override)가 부른다.
    /// </summary>
    internal void PinDockableCore(IDockable dockable)
    {
        if (_pinShareBypass > 0 || dockable is not PanelDocumentDock tabs)
        {
            _f.BasePinDockableCore(dockable);
            return;
        }

        var wasPinned = _f.IsDockablePinned(tabs);
        if (!wasPinned)
        {
            _st.PinShares.Remember(tabs);
        }

        _f.BasePinDockableCore(dockable);
        var nowPinned = _f.IsDockablePinned(tabs);
        if (wasPinned && !nowPinned && _st.PinShares.Restore(tabs) is { } restored)
        {
            _f.Diag.DiagPinRestore(restored, _st.MainPane); // 진단 빌드 전용(제품은 호출·인자 평가가 빠진다)
        }
        else if (!wasPinned && !nowPinned)
        {
            _st.PinShares.Forget(tabs); // 핀이 걸리지 않았으면 기억을 버린다
        }
    }

    /// <summary>CollapsePanel·RestorePanel 전용: PW2 기억·복원 없이 핀만 토글한다. 표식은 예외가 나도 finally 로 푼다.</summary>
    internal void PinDockableCoreBypassShare(IDockable dockable)
    {
        _pinShareBypass++;
        try
        {
            _f.PinCore(dockable);
        }
        finally
        {
            _pinShareBypass--;
        }
    }

    /// <summary>끌어 놓기로 패널에 들어가거나 나온 경우에도 PanelTabsChanged 를 알린다. PocDockFactory.OnDockableMoved(override)가 base 호출 뒤 부른다.</summary>
    internal void OnDockableMoved(IDockable? dockable)
    {
        if (dockable is not null && PocDockFactory.PanelOf(dockable) is { } id)
        {
            _f.RaisePanelTabsChanged(id);
        }

        // 끌어 놓기로 패널 탭이 모두 빠지거나 다시 들어오면 패널을 접거나 되살린다.
        // Dock 의 놓기 처리(Drop) 도중 트리를 바꾸지 않도록 처리가 끝난 뒤로 미룬다.
        Avalonia.Threading.Dispatcher.UIThread.Post(_f.Hide.SyncSidePanels);
    }
}
