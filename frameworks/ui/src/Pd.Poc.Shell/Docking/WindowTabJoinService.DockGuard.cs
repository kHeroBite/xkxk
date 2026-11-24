// SDI 창 끌기 중 Dock 기본 창 도킹 차단과 탭 합류 실행 — 제목줄로 끄는 SDI 창을 다른 SDI 창 몸통에 겹쳐 놓아도 합쳐지지 않게 하고(정본 03 §3.2·04 W-23), 머무름 합류의 문서 옮기기(Join)를 둔다
using System.Collections.Generic;
using System.Linq;
using Avalonia.Threading;
using Dock.Model.Controls;
using Dock.Model.Core;

namespace Pd.Poc.Shell.Docking;

public static partial class WindowTabJoinService
{
    private static IRootDock? s_dragRoot;

    /// <summary>제목줄로 끄는 중인 SDI 창 루트(Begin 에 서고, 놓기 뒤 Dock 놓기 처리까지 끝난 다음 지운다). 진단·시험 관측용.</summary>
    public static IRootDock? DraggedWindowRoot => s_dragRoot;

    /// <summary>
    /// Dock 드롭 정책(IDockDropPolicy.CanDrop) 앞단 — 끄는 SDI 창(제목줄 끌기)의 내용을 다른 SDI 창 안에 도킹하는 것만 막는다.
    /// Dock 은 관리형·OS 떠 있는 창을 끌어 다른 창 몸통에 놓으면 탭 도킹을 실행한다(디컴파일 ManagedHostWindowState.Drop → DockManager.IsDockingAllowed → CanDrop).
    /// 탭 끌기(U15 본창 탭 → SDI 몸통)는 제목줄 누름이 아니라 표시가 서지 않고, 메인 창 도킹 영역·가운데 드롭·패널 놓기는 대상이 메인 루트라 그대로다. Window(분리)는 막지 않는다.
    /// false 는 Dock 이 그 위 도킹 안내를 감추는 데에도 쓴다.
    /// </summary>
    public static bool BlocksWindowDock(IDockable source, IDockable target, DockOperation operation)
    {
        if (operation == DockOperation.Window || s_dragRoot is not { } dragged || !ReferenceEquals(RootOf(source), dragged))
        {
            return false;
        }

        return RootOf(target) is { Window: not null } targetRoot && !ReferenceEquals(targetRoot, dragged);
    }

    /// <summary>끄는 창 표시를 지운다 — Avalonia 클래스 처리기(놓기)가 Dock 의 놓기 처리보다 먼저 돌므로 지금이 아니라 다음 차례에 지운다.</summary>
    private static void ClearDragRootLater()
    {
        if (s_dragRoot is not { } root)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (ReferenceEquals(s_dragRoot, root))
            {
                s_dragRoot = null;
            }
        }, DispatcherPriority.Background);
    }

    private static IRootDock? RootOf(IDockable? node)
    {
        for (; node is not null; node = node.Owner)
        {
            if (node is IRootDock root)
            {
                return root;
            }
        }

        return null;
    }

    /// <summary>
    /// 원천 창의 문서 전부를 대상 창 문서 dock 끝에 차례로 붙이고 dragged 를 활성으로 한다. 문서·도구가 남지 않은 원천 창은 닫는다.
    /// 그 사이 원천 창이 닫혔거나(Dock 놓기 처리 등) 대상에 문서가 없으면 하지 않는다. 합류하면 true.
    /// </summary>
    public static bool Join(IRootDock sourceRoot, IRootDock targetRoot, PocDocument? dragged)
    {
        if (ReferenceEquals(sourceRoot, targetRoot) || sourceRoot.Window is not { } window
            || WindowCommands.FindDocument(targetRoot)?.Owner is not IDock targetDock || targetDock.Factory is not { } factory)
        {
            return false;
        }

        var docs = Leaves(sourceRoot).OfType<PocDocument>().ToList();
        if (docs.Count == 0) { return false; }

        foreach (var doc in docs)
        {
            if (doc.Owner is IDock from)
            {
                // targetDockable = 끝 항목이어야 맨 끝에 붙는다(null 이면 Dock 이 끝에서 하나 앞에 넣는다 — 디컴파일 FactoryBase.MoveDockable)
                factory.MoveDockable(from, targetDock, doc, targetDock.VisibleDockables?.LastOrDefault());
            }
        }

        var active = dragged is not null && docs.Contains(dragged) ? dragged : docs[^1];
        if (ReferenceEquals(active.Owner, targetDock))
        {
            factory.SetActiveDockable(active);
            factory.SetFocusedDockable(targetDock, active);
        }

        if (window.Owner is IRootDock && !Leaves(sourceRoot).Any())
        {
            factory.RemoveWindow(window); // 빈 원천 창 닫기(호스트 Exit 포함 — 디컴파일 FactoryBase.RemoveWindow)
        }

        return docs.TrueForAll(d => ReferenceEquals(d.Owner, targetDock));
    }

    /// <summary>dock 이 아닌 잎(문서·도구) — 원천 창에 남은 내용 판정.</summary>
    private static IEnumerable<IDockable> Leaves(IDockable node)
    {
        if (node is not IDock dock)
        {
            yield return node;
            yield break;
        }

        foreach (var child in (dock.VisibleDockables ?? Enumerable.Empty<IDockable>()).Where(c => c is not IProportionalDockSplitter))
        {
            foreach (var leaf in Leaves(child)) { yield return leaf; }
        }
    }
}
