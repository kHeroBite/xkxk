// 기본 도킹 트리 조립 — 패널 영역 네 칸이 문서 영역을 한 겹씩 감싸는 단을 만들고 트리 참조를 상태에 채운다. 어느 패널이든 같은 방식으로 만든다
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;

namespace Pd.Poc.Shell.Docking;

/// <summary>
/// 기본 레이아웃: Root → 바깥[가로 분할[왼쪽 패널 | 세로 분할[여분 단[문서 칸[문서 영역(MDI)] ] / 아래 패널]] | 오른쪽 패널].
/// 단은 고정이고 패널만 가장자리에 맞춰 앞뒤로 옮겨 다닌다(PanelCornerBuild). 여분 패널은 비어 있어 첫 정리 때 접힌다(PocDockFactory.InitLayout).
/// 문서 칸을 따로 두어 문서 분할이 패널 단에 섞이지 않는다. 숨김·분할 기록 비우기(앞), 언어 전환 구독(뒤)은 CreateLayout 이 한다. 상태는 갖지 않는다.
/// </summary>
internal static class DockLayoutBuilder
{
    internal static IRootDock Build(PocDockFactory f, DockLayoutState st, Tool explorer, Tool log, System.Action<IRootDock, IDocumentDock> setRootAndDocuments)
    {
        var documents = new DocumentDock
        {
            Id = PocDockFactory.DocumentDockId,
            Title = Localization.Loc.Tr("panel.documents"),
            IsCollapsable = false, // 빈 상태로 시작해도 접혀 트리에서 빠지지 않게(스모크 18:33 문서 영역 소실 대응)
            EmptyContent = Localization.Loc.Tr("panel.documents.empty"), // Dock.Model.Mvvm 기본값 대체(otest-m-1), 언어 전환은 RelocalizePanels
            CanCreateDocument = false,
            LayoutMode = DocumentLayoutMode.Mdi,
            VisibleDockables = f.CreateList<IDockable>(),
        };

        // 측면 패널(UI-14 대안 A + 핀 유지): 바깥은 ToolDock(도구 창 머리줄·핀(자동 숨김)·접기 유지 — Dock 핀은 소유자가 IToolDock 일 때만 동작),
        // 안쪽은 탭 모드 DocumentDock(도구·문서 모두 끌어 놓기로 탭 합류). 자세한 근거는 PanelDocumentDock.cs.
        foreach (var tool in new Tool[] { explorer, log, f.Notifications })
        {
            tool.CanDockAsDocument = true;
            // 도구 탭은 띄우기(Float) 금지: Dock 은 놓을 대상 없이 놓으면 Float 로 소유자에서 떼어 새 창에 넣는다(DockControlState Released 분기).
            // 도구 창 호스트가 없으면 탭이 '어디에도 없는' 상태가 된다(otest-1 i0339). 금지하면 무효 놓기는 아무것도 바꾸지 않아 탭이 원래 패널에 남는다.
            // 캡처 잃음(CaptureLost)은 Dock 이 이동 없이 끌기만 끝낸다(디컴파일 확인). 패널에 들어간 문서는 SDI 분리(Float)를 그대로 허용한다.
            tool.CanFloat = false;
        }

        var left = NewPanel(f, st, PanelArea.Explorer, "LeftPane", PocDockFactory.LeftTabsId, Localization.Loc.Tr("panel.explorer"), explorer, 0.18, PanelEdge.Left);
        var bottom = NewPanel(f, st, PanelArea.Log, "BottomPane", PocDockFactory.BottomTabsId, Localization.Loc.Tr("panel.log"), log, 0.2, PanelEdge.Bottom);
        var notify = NewPanel(f, st, PanelArea.Notification, PocDockFactory.NotificationPaneId, PocDockFactory.NotificationTabsId, Localization.Loc.Tr("notify.title"), f.Notifications, PocDockFactory.SidePanelProportion, PanelEdge.Right);
        var extra = NewPanel(f, st, PanelArea.Extra, PocDockFactory.ExtraPaneId, PocDockFactory.ExtraTabsId, "", null, PocDockFactory.EdgePanelProportion, PanelEdge.Top);

        var host = new ProportionalDock
        {
            Id = PocDockFactory.DocumentsHostId,
            IsCollapsable = false,
            Orientation = Orientation.Vertical,
            VisibleDockables = f.CreateList<IDockable>(documents),
            ActiveDockable = documents,
        };

        var extraLevel = Level(f, PocDockFactory.ExtraLevelId, Orientation.Vertical, host, extra, first: true);
        var center = Level(f, "CenterPane", Orientation.Vertical, extraLevel, bottom, first: false);
        var main = Level(f, "MainPane", Orientation.Horizontal, center, left, first: true);
        var outer = Level(f, PocDockFactory.OuterPaneId, Orientation.Horizontal, main, notify, first: false);

        var root = f.CreateRootDock();
        root.Id = "Root";
        root.Title = "Root";
        IDockable rootChild = outer;
        DockFactoryDiag.OverrideRootChild(outer, main, ref rootChild); // 진단 빌드 전용 교체 지점(제품 빌드에서는 호출 자체가 빠진다)
        root.VisibleDockables = f.CreateList<IDockable>(rootChild);
        root.ActiveDockable = rootChild;
        root.DefaultDockable = rootChild;

        setRootAndDocuments(root, documents); // 원본: Root = root; Documents = documents;
        st.Outer = outer;
        st.MainPane = main;
        st.DocumentsHost = host;
        f.Corners.OnTreeBuilt(new[] { outer, main, center, extraLevel }, PanelCornerOwnership.Default); // 새 트리는 기본 모서리 소유로 시작한다(요건 3). 저장본은 ApplyPanels 가 입힌다
        return root;
    }

    /// <summary>패널 한 벌(바깥 ToolDock + 안쪽 탭 영역)을 만들어 상태에 넣는다.</summary>
    private static ToolDock NewPanel(PocDockFactory f, DockLayoutState st, PanelArea area, string paneId, string tabsId, string title, Tool? tool, double share, PanelEdge edge)
    {
        var tabs = new PanelDocumentDock
        {
            Id = tabsId,
            Title = title,
            VisibleDockables = tool is null ? f.CreateList<IDockable>() : f.CreateList<IDockable>(tool),
            ActiveDockable = tool,
        };
        var pane = new ToolDock
        {
            Id = paneId,
            Proportion = share,
            Alignment = PanelMoveService.AlignmentOf(edge),
            VisibleDockables = f.CreateList<IDockable>(tabs),
            ActiveDockable = tabs,
        };
        st.HomePanes[area] = pane;
        st.HomeTabs[area] = tabs;
        st.Edges[area] = edge;
        return pane;
    }

    /// <summary>[패널, 분할선, 안쪽 칸] 또는 [안쪽 칸, 분할선, 패널] 단.</summary>
    private static ProportionalDock Level(PocDockFactory f, string id, Orientation orientation, IDockable inner, IDockable panel, bool first) => new()
    {
        Id = id,
        IsCollapsable = false,
        Orientation = orientation,
        VisibleDockables = first
            ? f.CreateList<IDockable>(panel, new ProportionalDockSplitter(), inner)
            : f.CreateList<IDockable>(inner, new ProportionalDockSplitter(), panel),
        ActiveDockable = inner,
    };
}
