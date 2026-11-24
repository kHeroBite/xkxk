// 패널에서 끌어 온 문서를 문서 영역 가운데에 놓으면 그 칸의 MDI 떠 있는 창으로 연다(U13) — 분할은 가장자리 안내 표적에서만 한다
using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;

namespace xkxk.frameworks.ui.shell.Docking;

/// <summary>
/// 근거(Dock 12.1.0.12 디컴파일 logs/odev1r/ilspy_ui20):
/// ① DockControlState Released — 안내 표적이 없거나 놓기가 거부되면 DockManagerState.Float → factory.FloatDockable → SplitToWindow 로 독립 창(SDI)을 만든다.
/// ② 안내 밖 놓기는 연산이 Window 라 DockManager.DockDockable → DockService.DockDockableIntoWindow → factory.SplitToWindow 로 같은 결과다.
///    그래서 문서 영역 가운데(가장자리 표적이 아닌 곳)에 놓으면 MDI 가 아니라 SDI 가 되었다(U13, 9.png).
/// ③ 두 경로 모두 놓는 동안 DockControl.IsDraggingDock 이 true 다(Released 분기 끝에서 false). 메뉴 '독립 창으로 분리'는 끌기가 아니라 false 다.
/// 대응: PocDockFactory.SplitToWindow 가 분리 직전에 TryDrop 을 부른다. 끌기 중이고 마지막 포인터가 메인 창의 MDI 문서 칸 안이면
/// 문서를 그 칸으로 옮겨 커서 자리에 기본 크기 MDI 창으로 두고 분리를 건너뛴다. 가운데 안내(Fill) 놓기는 PanelTabsService.BeforeMove 가 NoteMove 를 부른다.
/// 포인터는 TopLevel 클래스 처리기로 기억한다(DIP·TopLevel 기준 — 데스크톱 실 창과 브라우저 관리형 창이 같은 좌표계를 쓴다).
/// </summary>
internal static class CenterDropToMdi
{
    /// <summary>기존 위치 규칙(가운데 안내·칸 합류): 커서가 MDI 창 제목줄 위에 오도록 창 왼쪽 위를 커서에서 비키는 양(DIP).</summary>
    private const double GrabOffsetX = 40;
    private const double GrabOffsetY = 12;

    /// <summary>기존 규칙에서 창이 칸 밖으로 완전히 나가지 않게 남겨 두는 최소 폭·높이(DIP).</summary>
    private const double KeepVisible = 80;

    private static double Clamp(double value, double max) => Math.Max(0, Math.Min(value, Math.Max(0, max)));

    /// <summary>TryDrop 이 문서를 옮기는 동안 true — NoteMove(가운데 안내 경로)가 위치를 다시 정하지 않게 한다.</summary>
    private static bool s_inDrop;

    private static TopLevel? _top;
    private static Point _point;
    private static bool _installed;

    /// <summary>포인터 기억 처리기를 1회 등록한다(PanelJoinDrag.Install 이 부른다).</summary>
    internal static void Install()
    {
        if (_installed)
        {
            return;
        }

        _installed = true;
        InputElement.PointerMovedEvent.AddClassHandler<TopLevel>((top, e) =>
        {
            _top = top;
            _point = e.GetPosition(top);
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    /// <summary>
    /// PocDockFactory.SplitToWindow(분리 창 생성 공통 입구)가 부른다. 끌어 놓기로 문서 영역 가운데에 놓은 문서면 MDI 창으로 넣고 true(분리하지 않음).
    /// 끌기가 아니거나(메뉴) 문서가 아니거나 놓은 곳이 MDI 문서 칸이 아니면 false(호출자가 SDI 로 분리).
    /// </summary>
    internal static bool TryDrop(PocDockFactory factory, IDockable item)
    {
        if (item is not PocDocument doc || doc.Owner is not IDock owner || !IsDragging(factory) || AreaAtPointer(factory) is not { } hit)
        {
            return false;
        }

        if (!ReferenceEquals(owner, hit.Dock))
        {
            // D-C3-12: 이 MoveDockable 이 BeforeMove→NoteMove 로 기존 규칙 Place 를 예약해 아래 제목줄 중앙 Place 를 나중에 덮어썼다 — 이 호출 동안은 NoteMove 를 끈다
            s_inDrop = true;
            try
            {
                factory.MoveDockable(owner, hit.Dock, doc, null);
            }
            finally
            {
                s_inDrop = false;
            }
        }

        if (!ReferenceEquals(doc.Owner, hit.Dock))
        {
            return false; // 옮기기가 거부됐다 — 원래대로 분리 경로에 맡긴다
        }

        if (hit.Dock.LayoutMode == DocumentLayoutMode.Mdi)
        {
            Place(doc, hit.View, alignTitle: true); // 탭 모드 칸은 탭으로 합류한다(위치 없음 — c2dev_n 실측: 탭 줄 빈 곳 놓기가 SDI 로 빠지던 결함)
        }

        factory.SetActiveDockable(doc);
        AfterLeave(factory, owner);
        return true;
    }

    /// <summary>
    /// PanelTabsService.BeforeMove(가운데 안내 Fill 놓기 포함)가 부른다. 끌기 중 패널(측면·알림 서랍)에서 MDI 문서 칸으로 들어오는 문서면
    /// 옮김이 끝난 뒤 커서 자리에 기본 크기 MDI 창으로 둔다(옛 MdiBounds 자리로 튀지 않게).
    /// </summary>
    internal static void NoteMove(PocDockFactory factory, IDock sourceDock, IDock targetDock, IDockable dockable)
    {
        if (s_inDrop || dockable is not PocDocument doc || sourceDock is not PanelDocumentDock || !IsMdiArea(factory, targetDock)
            || !IsDragging(factory) || ViewOf(targetDock) is not { } view)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (ReferenceEquals(doc.Owner, targetDock))
            {
                Place(doc, view);
            }
        });
    }

    /// <summary>MDI 문서 칸(중앙 문서 영역 또는 문서 영역 분할 칸)인가. 측면 패널·알림 서랍 탭 영역, 탭 모드 칸은 아니다.</summary>
    internal static bool IsMdiArea(PocDockFactory factory, IDockable? dock) =>
        dock is IDocumentDock { LayoutMode: DocumentLayoutMode.Mdi } and not PanelDocumentDock
        && factory.Root is { } root && factory.FindDockable(root, d => ReferenceEquals(d, dock)) is not null;

    /// <summary>마지막 포인터 아래의 보이는 문서 칸(메인 창 한정 — MDI·탭 모드 모두, 측면 패널·알림 서랍 제외). 없으면 null.</summary>
    internal static (IDocumentDock Dock, Control View)? AreaAtPointer(PocDockFactory factory)
    {
        if (_top is not { } top || !IsMainTop(factory, top))
        {
            return null;
        }

        foreach (var view in top.GetVisualDescendants().OfType<DocumentDockControl>())
        {
            if (view.IsEffectivelyVisible && view.DataContext is IDocumentDock dock && SplitMergeService.IsDocumentArea(factory, dock)
                && view.TranslatePoint(default, top) is { } origin && new Rect(origin, view.Bounds.Size).Contains(_point))
            {
                return (dock, view);
            }
        }

        return null;
    }

    /// <summary>마지막 포인터 위치(TopLevel 기준 DIP)와 그 TopLevel. 기억이 없으면 null(SplitMergeService 가 합류 판정에 쓴다).</summary>
    internal static (TopLevel Top, Point Point)? LastPointer => _top is { } top ? (top, _point) : null;

    /// <summary>문서를 칸 안 커서 자리에 기본 크기·보통 상태로 두고 맨 위로 올린다. 제목줄 가로 중앙이 커서에 오게 하고(CenterDropTitleAlign 이 배치 뒤 실측 보정), 캔버스 밖이면 제목줄이 칸 안에 남게 옮긴다.</summary>
    internal static void Place(PocDocument doc, Control view, bool alignTitle = false)
    {
        var host = view.GetVisualDescendants().OfType<MdiLayoutPanel>().FirstOrDefault() ?? (Control)view;
        var local = _top is { } top && top.TranslatePoint(_point, host) is { } p ? p : new Point(0, 0);
        var size = host.Bounds.Size;
        const double w = PocDockFactory.DefaultMdiWidth, h = PocDockFactory.DefaultMdiHeight;
        doc.MdiState = MdiWindowState.Normal;
        if (alignTitle)
        {
            // C4(D-C3-13): 빈 곳 놓기(SplitToWindow→TryDrop)만 제목줄 중앙=커서. 가운데 안내(center)·칸 합류는 아래 기존 규칙을 유지한다(요구문의 5영역 예외).
            var titleH = CenterDropTitleAlign.TitleHeight();
            var target = CenterDropTitleAlign.Target(local, size); // 캔버스 안 유지 보정 없음(D-C3-12)
            var placed = new DockRect(target.X - w / 2, target.Y - titleH / 2, w, h); // 추정 — 그림자 여백은 배치 뒤 Refine 이 실측으로 바로잡는다
            CenterDropTitleAlign.Mark(doc, placed); // 배치(Arrange)가 영역 안 clamp 를 하지 않게 표시(MagnetMdiLayoutManager)
            doc.MdiBounds = placed;
            CenterDropTitleAlign.Refine(doc, host, local, target);
        }
        else
        {
            var x = Clamp(local.X - GrabOffsetX, size.Width - (size.Width >= w ? w : KeepVisible));
            var y = Clamp(local.Y - GrabOffsetY, size.Height - (size.Height >= h ? h : KeepVisible));
            doc.MdiBounds = new DockRect(x, y, w, h);
        }

        if (doc.Owner is IDock { VisibleDockables: { } siblings })
        {
            doc.MdiZIndex = siblings.OfType<IMdiDocument>().Where(d => !ReferenceEquals(d, doc)).Select(d => d.MdiZIndex).DefaultIfEmpty(0).Max() + 1;
        }
    }

    /// <summary>문서가 빠져나간 원래 칸 뒷정리: 패널이면 탭 변경을 알리고 빈 패널을 접는다. 문서 분할 칸이 비면 정리한다.</summary>
    internal static void AfterLeave(PocDockFactory factory, IDock owner)
    {
        if (owner is PanelDocumentDock)
        {
            factory.RaisePanelTabsChanged(owner.Id);
            Dispatcher.UIThread.Post(factory.Hide.SyncSidePanels);
        }
        else if (owner is MdiTabGroupDock group)
        {
            if (group.Owner is IDocumentDock mdi) { MdiTabGroup.DissolveIfSingle(factory, mdi, group); } // 묶음 탭을 끌어낸 원천 — 분할 칸이 아니므로 접지 않고 1개 이하면 푼다
        }
        else if (owner.VisibleDockables is { Count: 0 } && !ReferenceEquals(owner, factory.Documents) && SplitMergeService.IsDocumentArea(factory, owner))
        {
            factory.CollapseDock(owner); // 빈 문서 분할 칸 — 몫은 이웃 문서 칸에 돌려준다(DocumentSplitCollapse)
            factory.RaiseSplitChanged();
        }
    }

    /// <summary>Dock 끌어 놓기 진행 중인가(놓기 처리 도중 포함). 메뉴 명령에서는 false.</summary>
    private static bool IsDragging(PocDockFactory factory) => factory.DockControls.OfType<DockControl>().Any(c => c.IsDraggingDock);

    /// <summary>메인 창(루트 레이아웃을 그리는 DockControl 이 있는 TopLevel)인가. SDI·패널 분리 창은 제외한다.</summary>
    private static bool IsMainTop(PocDockFactory factory, TopLevel top) =>
        factory.DockControls.OfType<DockControl>().Any(c => ReferenceEquals(c.Layout, factory.Root) && ReferenceEquals(TopLevel.GetTopLevel(c), top));

    /// <summary>문서 칸을 그리는 보이는 DocumentDockControl(마지막 포인터의 TopLevel 안). 없으면 null.</summary>
    internal static Control? ViewOf(IDock dock) =>
        _top?.GetVisualDescendants().OfType<DocumentDockControl>().FirstOrDefault(v => v.IsEffectivelyVisible && ReferenceEquals(v.DataContext, dock));
}
