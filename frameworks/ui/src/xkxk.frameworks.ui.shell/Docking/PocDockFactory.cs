// Dock 레이아웃(탐색기·문서 영역·로그·알림)을 만드는 팩토리 파사드 — override·이벤트·공개 API 를 두고 본문은 패널·분할 서비스에 위임한다(W3b partial 해소)
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Dock.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm;
using Dock.Model.Mvvm.Controls;
using Dock.Settings;
using xkxk.frameworks.ui.shell.ViewModels;

namespace xkxk.frameworks.ui.shell.Docking;

/// <summary>
/// 레이아웃(DockLayoutBuilder)은 패널 단이 문서 칸을 감싼 트리다. 패널 화면은 DataTemplate 이 그린다. 상태는 DockLayoutState, 본문은 서비스에 있다.
/// override·IDockDropPolicy·IDockOperationObserver·이벤트·공개 API 는 여기에 남는다(P2·P6). 진단 전용 조회는 DockFactoryDiag 가 제공한다.
/// </summary>
public sealed partial class PocDockFactory : Factory, IDockDropPolicy, IDockOperationObserver
{
    public const string DocumentDockId = "Documents";
    public const string DocumentsHostId = "DocumentsHost";
    public const string OuterPaneId = "OuterPane";
    public const string ExtraLevelId = "ExtraLevel";
    public const string NotificationPaneId = "NotificationPane";
    public const string ExtraPaneId = "ExtraPane";
    public const string LeftTabsId = "LeftPane.Tabs";
    public const string NotificationTabsId = "NotificationPane.Tabs";
    public const string BottomTabsId = "BottomPane.Tabs";
    public const string ExtraTabsId = "ExtraPane.Tabs";
    internal const double SidePanelProportion = 0.2;
    internal const double EdgePanelProportion = 0.18;

    /// <summary>MDI 새 창 계단 간격(Mars Fn폼위치중복시변경반환 30px).</summary>
    public const double CascadeStep = 30;

    public const double DefaultMdiWidth = 640;
    public const double DefaultMdiHeight = 420;

    private readonly Tool _explorer;
    private readonly Tool _log;
    private readonly DockLayoutState _state = new();

    public PocDockFactory(Tool explorer, Tool log)
    {
        _explorer = explorer;
        _log = log;
        Query = new PanelQuery(this, _state); // 서비스는 상태·팩토리 참조만 저장한다(R3 — 구독·설치 없음)
        Tabs = new PanelTabsService(this, _state);
        Hide = new PanelHideService(this, _state);
        OpenStates = new PanelOpenStates(this, _state);
        Split = new SplitAreaService(this, _state);
        Tidy = new SplitAreaCleanup(this, _state);
        Carry = new SplitCarryService(this, _state);
        Codec = new SplitLayoutCodec(this, _state);
        Moves = new PanelMoveService(this, _state);
        Drops = new PanelDropService(this);
        Notifications = new NotificationPanelViewModel();
    }

    internal PanelQuery Query { get; }
    internal PanelTabsService Tabs { get; }
    internal PanelHideService Hide { get; }
    internal PanelOpenStates OpenStates { get; }
    internal SplitAreaService Split { get; }
    internal SplitAreaCleanup Tidy { get; }
    internal SplitCarryService Carry { get; }
    internal SplitLayoutCodec Codec { get; }
    internal PanelMoveService Moves { get; }
    internal PanelDropService Drops { get; }
    public PanelCornerService Corners => field ??= new PanelCornerService(this, _state); // 패널 끝 확장(요건 3)
    public PanelFeatureGate PanelFeature => field ??= new PanelFeatureGate(this); // 패널 기능 켬/끔(PanelQuery.CanDrop·CanPin 이 읽는다)
    internal DockFactoryDiag Diag { get; } = new(); // K2 필드 초기화자(진단 상태만, 제품판은 빈 객체)
    // 주의: 이 클래스 안에서 ExplorerTool·LogTool 은 아래 속성으로 해석된다 — 같은 이름의 형식(ViewModels.*)은 한정 이름으로 쓴다.
    internal Tool ExplorerTool => _explorer;
    internal Tool LogTool => _log;

    public IRootDock? Root { get; private set; }

    public IDocumentDock? Documents { get; private set; }

    /// <summary>알림 패널 모델(UI-15).</summary>
    public NotificationPanelViewModel Notifications { get; }

    public override IRootDock CreateLayout()
    {
        Hide.ResetHiddenState(); // 새 트리에 옛 숨김 기록을 남기지 않는다(PanelHideService)
        Tidy.ResetSplitState(); // 옛 분할 패널·영역 기록도 비운다(UI-20, SplitAreaCleanup)
        Moves.Reset();
        var root = DockLayoutBuilder.Build(this, _state, _explorer, _log, (r, d) => { Root = r; Documents = d; });
        Localization.Loc.Instance.LanguageChanged -= Tidy.RelocalizePanels;
        Localization.Loc.Instance.LanguageChanged += Tidy.RelocalizePanels;
        PanelFeature.Reapply(); // 새 패널 탭 영역에 CanPin 반영
        return root;
    }

    public override void InitLayout(IDockable layout)
    {
        // 데스크톱은 OS 실 창(호스트가 App.CreateHostWindow 로 주입 가능). 브라우저는 호스트가 UseManagedWindows=true 로 관리형 창을 쓴다.
        if (!DockSettings.UseManagedWindows)
        {
            DefaultHostWindowLocator = () => App.CreateHostWindow?.Invoke() ?? new HostWindow();
        }

        ViewRegistry.Current.EnsureNotificationTemplate(); // 알림 패널 DataTemplate(Application 수준, 1회)
        base.InitLayout(layout);
        Hide.SyncSidePanels(); // 비어 있는 여분 패널을 접는다
        PanelJoinDrag.Install(this); // MDI 제목줄 끌기 → 측면 패널 탭 합류(UI-14)
        PanelTabStripDrop.Install(); // 패널 탭 줄 빈 곳 놓기 → 맨 끝으로 순서 변경(Q15 ③)
    }

    /// <summary>기존 창과 좌상단이 겹치지 않을 때까지 30px 씩 비킨 새 MDI 창 영역.</summary>
    public static DockRect NextMdiBounds(IEnumerable<DockRect> existing)
    {
        var taken = existing.Select(b => (b.X, b.Y)).ToHashSet();
        double x = 0, y = 0;
        for (var i = 0; i < 200 && taken.Contains((x, y)); i++)
        {
            x += CascadeStep;
            y += CascadeStep;
        }

        return new DockRect(x, y, DefaultMdiWidth, DefaultMdiHeight);
    }

    // ── override(P6): 선언은 여기, 본문은 서비스 ──

    /// <summary>▾ '문서로 도킹'(D-DAD1) — 대상은 중앙 문서 영역(PanelTabsService.DockAsDocument).</summary>
    public override void DockAsDocument(IDockable dockable) => Tabs.DockAsDocument(dockable);

    /// <summary>모든 핀 토글 경로가 지난다 — PW2 몫 기억·복원(PanelTabsService.PinDockableCore).</summary>
    protected override void PinDockableCore(IDockable dockable)
    {
        Tabs.PinDockableCore(dockable);
        OpenStates.SyncScales();
    }

    /// <summary>머리줄 ✕: 기본 패널 탭 영역은 접어 두어 다시 열 수 있게 한다(PanelOpenStates). 그 밖은 Dock 기본이다.</summary>
    public override void CloseDockable(IDockable? dockable)
    {
        if (!OpenStates.TryClosePanel(dockable))
        {
            base.CloseDockable(dockable);
        }
    }

    /// <summary>측면 패널 본문을 눌러도 바깥 패널 머리줄이 활성되게 초점 뒤 바깥 패널 IsActive 를 맞춘다(PanelFocusActivation, D-C2-22).</summary>
    public override void SetFocusedDockable(IDock dock, IDockable? dockable)
    {
        // 분리 창이 칸으로 나뉘면 창 루트의 활성 항목이 ProportionalDock 이고, 그 제목(형식 이름)이 관리형 창 제목으로 나온다(U15, 디컴파일 ManagedDockWindowDocument.UpdateTitleFromLayout). 그 안의 문서로 맞춘다.
        if (dockable is IProportionalDock split && !ReferenceEquals(FindRoot(dock, _ => true), Root) && MdiZOrder.FindDocument(split) is { Owner: IDock owner } document)
        {
            dock = owner;
            dockable = document;
        }

        PanelFocusActivation.SetFocused(this, dock, dockable, base.SetFocusedDockable);
    }

    // OnDockableMoved(패널 탭 알림 + 묶음 정리)·OnDockableRemoved·OnDockableClosed 는 PocDockFactory.TabGroups.cs 에 있다.

    /// <summary>Dock 의 가장자리 놓기가 부르는 분할(SplitAreaService.SplitToDock). 대상이 MDI 탭 묶음 창이면 분할하지 않고 MDI 창으로 꺼낸다(PocDockFactory.TabGroups.cs).</summary>
    public override void SplitToDock(IDock dock, IDockable dockable, DockOperation operation)
    {
        if (!TrySplitOntoTabGroup(dock, dockable))
        {
            Split.SplitToDock(dock, dockable, operation);
        }
    }

    /// <summary>빈 칸 정리. 문서 칸이면 그 몫을 이웃 문서 칸에 돌려준다(DocumentSplitCollapse, D-SPLITGROW).</summary>
    public override void CollapseDock(IDock dock) => DocumentSplitCollapse.Collapse(dock, base.CollapseDock);

    public override void MoveDockable(IDock sourceDock, IDock targetDock, IDockable sourceDockable, IDockable? targetDockable)
    {
        if (Tabs.BeforeMove(sourceDock, targetDock, sourceDockable))
        {
            NotePanelToCarrier(sourceDock, targetDock); // 패널 탭 → 운반 dock(분할 첫 단계) 기억 — 묶음 창 분할 놓기의 패널 뒷정리용(PocDockFactory.TabGroups.cs)
            base.MoveDockable(sourceDock, targetDock, sourceDockable, targetDockable);
            NoteTabGroupDetach(sourceDock, targetDock, sourceDockable); // 묶음에서 끌어낸 탭은 정리 뒤 맨 위로(PocDockFactory.TabGroups.cs)
        }
    }

    /// <summary>Dock 의 띄우기(탭 끌어 놓기 실패 포함). 패널 탭 줄 빈 곳에 놓은 것이면 띄우지 않고 맨 끝으로 옮긴다(PanelTabStripDrop, Q15 ③).</summary>
    public override void FloatDockable(IDockable dockable, DockWindowOptions? options)
    {
        if (!PanelTabStripDrop.TryReorderToEnd(this, dockable) && Tabs.PanelFloatItem(dockable) is not null)
        {
            base.FloatDockable(dockable, options);
        }
    }

    /// <summary>탭 메뉴 '새 가로 문서 영역'. 패널 문서는 중앙으로 옮긴 뒤 기본 경로를 탄다(DocumentTabMenu.PrepareNewArea).</summary>
    public override void NewHorizontalDocumentDock(IDockable dockable)
    {
        if (DocumentTabMenu.PrepareNewArea(this, dockable))
        {
            base.NewHorizontalDocumentDock(dockable);
        }
    }

    /// <summary>탭 메뉴 '새 세로 문서 영역'. 가로와 같다.</summary>
    public override void NewVerticalDocumentDock(IDockable dockable)
    {
        if (DocumentTabMenu.PrepareNewArea(this, dockable))
        {
            base.NewVerticalDocumentDock(dockable);
        }
    }

    /// <summary>탭 메뉴 '모두 독립 창으로 분리'. 문서 영역 소속이면 문서마다 창 1개다(DocumentTabMenu.FloatAllDocuments).</summary>
    public override void FloatAllDockables(IDockable dockable, DockWindowOptions? options)
    {
        if (!DocumentTabMenu.FloatAllDocuments(this, dockable))
        {
            base.FloatAllDockables(dockable, options);
        }
    }

    /// <summary>분리 창 생성의 공통 입구(FloatDockable·안내 밖 놓기). 패널째 분리하지 않는다(PanelTabsService.PanelFloatItem).</summary>
    public override void SplitToWindow(IDock dock, IDockable dockable, double x, double y, double width, double height, DockWindowOptions? options)
    {
        if (Tabs.PanelFloatItem(dockable) is { } item && !CenterDropToMdi.TryDrop(this, item)) // 끌어 놓기로 문서 영역 가운데면 MDI 창(U13)
        {
            Tabs.RunFloat(item, () => base.SplitToWindow(dock, item, x, y, width, height, options));
        }
    }

    // ── 서비스용 protected·base 전달(Dock 12.1.0.12 FactoryBase: protected 4개·virtual PinDockableCore — 같은 이름이면 CS0108 숨김 경고라 이름을 바꿈) ──
    internal void AddVisible(IDock dock, IDockable dockable) => AddVisibleDockable(dock, dockable);
    internal void InsertVisible(IDock dock, int index, IDockable dockable) => InsertVisibleDockable(dock, index, dockable);
    internal void RemoveVisible(IDock dock, IDockable dockable) => RemoveVisibleDockable(dock, dockable);
    internal void RemoveVisibleAt(IDock dock, int index) => RemoveVisibleDockableAt(dock, index);
    internal void PinCore(IDockable dockable) => PinDockableCore(dockable); // 재정의(PW2)를 탄다
    internal void BasePinDockableCore(IDockable dockable) => base.PinDockableCore(dockable);
    internal void BaseSplitToDock(IDock dock, IDockable dockable, DockOperation operation) => base.SplitToDock(dock, dockable, operation);

    // ── 이벤트(선언·구독 대상은 여기 고정, 서비스는 Raise 로 발화) ──

    /// <summary>패널 탭 구성이 바뀐 뒤(로그·자동 저장·작업표시줄 갱신 연결용). 인자는 탭 영역 Id.</summary>
    public event Action<string>? PanelTabsChanged;

    /// <summary>패널 가장자리가 바뀐 뒤(로그·자동 저장 연결용).</summary>
    public event Action<PanelEdge>? PanelEdgeChanged;

    /// <summary>분할 구조가 바뀐 뒤(만들기·정리·되감기·복원). 로그·자동 저장 연결용.</summary>
    public event Action? SplitChanged;

    internal void RaisePanelTabsChanged(string tabsId) => PanelTabsChanged?.Invoke(tabsId);
    internal void RaisePanelEdgeChanged(PanelEdge edge) => PanelEdgeChanged?.Invoke(edge);
    internal void RaiseSplitChanged() => SplitChanged?.Invoke();

    // ── 공개 API 전달(설명은 각 서비스 메서드에 있다) ──
    public IReadOnlyList<string> PanelIds => Query.PanelIds;
    public bool CanDrop(IDockable source, IDockable target, DockOperation operation) => !WindowTabJoinService.BlocksWindowDock(source, target, operation) && Query.CanDrop(source, target, operation);
    public IDockOperationScope? BeginDockOperation(IDockable source, IDockable target, DockOperation operation) => Drops.Begin(source, target, operation);
    public PanelEdge? PanelEdgeOf(string panelId) => Moves.EdgeOfPanel(panelId);
    public IReadOnlyList<string> PanelTabs(string panelId) => Query.PanelTabs(panelId);
    public PanelDocumentDock? TabsOf(string panelId) => Tabs.TabsOf(panelId);
    public bool SendDocumentToPanel(PocDocument document, string panelId) => Tabs.SendDocumentToPanel(document, panelId);
    public bool ReturnPanelDocument(PocDocument document) => Tabs.ReturnPanelDocument(document);
    public bool RevealTool(string toolId) => Tabs.RevealTool(toolId);
    public bool IsPanelPinned(string panelId) => Tabs.IsPanelPinned(panelId);
    public bool SetPanelPinned(string panelId, bool pinned) => !PanelFeature.RefusesPin(pinned) && Tabs.SetPanelPinned(panelId, pinned); // 끔이면 핀 켜기 거부(판정은 PanelFeatureGate)

    /// <summary>문서·도구가 지금 측면 패널 탭에 있으면 그 탭 영역 Id, 아니면 null(레이아웃 저장·창 명령 분기용).</summary>
    public static string? PanelOf(IDockable dockable) => (dockable.Owner as PanelDocumentDock)?.Id;
    public bool SidePanelsHidden => Hide.SidePanelsHidden;
    public bool IsToolHidden(string toolId) => Hide.IsToolHidden(toolId);
    public void SetSidePanelsHidden(bool hidden) => Hide.SetSidePanelsHidden(hidden);
    public bool SetToolHidden(string toolId, bool hidden) => Hide.SetToolHidden(toolId, hidden);
    public static string AreaId(PanelArea area) => PanelAreaNames.Stored(area) + "Area";
    public bool IsAreaVertical(PanelArea area) => Split.IsAreaVertical(area);
    public bool IsSplitAxis(PanelArea area, DockOperation op) => Split.IsSplitAxis(area, op);

    /// <summary>
    /// 칸 비율을 정한다. 접히지 않은 칸도 CollapsedProportion 이 유효하면 그 값을 목표 비율로 쓰므로(디컴파일 ProportionManager.HandleCollapsedChildren) 함께 맞춘다.
    /// </summary>
    internal static void SetShare(IDockable dockable, double proportion)
    {
        dockable.Proportion = proportion;
        dockable.CollapsedProportion = proportion;
    }

    public PanelsSnapshot? CapturePanels() => Codec.CapturePanels();
    public void ApplyPanels(PanelsSnapshot? panels)
    {
        Codec.ApplyPanels(panels);
        PanelFeature.Reapply(); // 복원된 핀·새 탭 영역에도 끔 규칙이 이긴다
    }
    public IReadOnlyList<string> SplitPaneIds => Codec.SplitPaneIds;
    public (bool Wrapped, double Proportion, bool Vertical, string? SlotId) AreaInfo(PanelArea area) => Codec.AreaInfo(area);
    public IReadOnlyList<SplitPaneInfo> PanesOf(PanelArea area) => Codec.PanesOf(area);
    public string? SplitEmptyPane(string paneId, DockOperation op) => Codec.SplitEmptyPane(paneId, op);
}
