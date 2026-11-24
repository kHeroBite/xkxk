// Dock 의 탭 끌어내기·탭 닫기·제거 뒤 문서 1개 이하 남은 MDI 탭 묶음을 Dispatcher 한 바퀴 뒤 자동으로 푸는 PocDockFactory 부분(정본 01 §4.4·03 §3.1)
using System.Collections.Generic;
using System.Linq;
using Avalonia.Threading;
using Dock.Model.Controls;
using Dock.Model.Core;

namespace Pd.Poc.Shell.Docking;

public sealed partial class PocDockFactory
{
    private bool _tabGroupTidyQueued; // 예약 1건만(같은 바퀴의 이동·제거·닫기를 한 번에 정리)
    private bool _tabGroupTidyRunning; // Tidy 자신의 MoveDockable·RemoveDockable 이 다시 예약하지 않게 막는다
    private PocDocument? _detachedFromGroup; // 묶음에서 MDI 칸으로 끌어낸 마지막 문서 — 정리 뒤 활성화·맨 위(01 §4.4 7항, 2026-11-11)
    private (IDock Carrier, PanelDocumentDock From)? _panelToCarrier; // 패널 탭 → 운반 dock 이동 기억(묶음 창 분할 놓기의 패널 뒷정리용)

    /// <summary>
    /// 이동이 '묶음 → MDI 칸(묶음 아님)' 이면 그 문서를 기억한다. 정리(Tidy)가 묶음을 풀며 남은 문서를 활성화해 분리한 창을 덮으므로(DissolveIfSingle)
    /// 정리가 끝난 뒤 분리한 창을 다시 활성화한다. Tidy 자신의 이동(남은 문서를 칸으로 되돌림)은 기억하지 않는다.
    /// </summary>
    private void NoteTabGroupDetach(IDock sourceDock, IDock targetDock, IDockable dockable)
    {
        // 이동 뒤 묶음이 비었으면 '묶음 풀기'(DissolveIfSingle 이 남은 문서를 되돌리는 이동)라 기억하지 않는다 — 이것까지 기억하면 남은 문서가 맨 위로 올라갔다(oi-fix-1 원격 실측)
        if (!_tabGroupTidyRunning && sourceDock is MdiTabGroupDock { VisibleDockables.Count: > 0 } && dockable is PocDocument doc
            && targetDock is IDocumentDock { LayoutMode: DocumentLayoutMode.Mdi } and not MdiTabGroupDock and not PanelDocumentDock)
        {
            _detachedFromGroup = doc;
            QueueTabGroupTidy(doc); // OnDockableMoved 가 이미 예약했으면 그대로(예약 1건)
        }
    }

    /// <summary>
    /// 분할 놓기의 대상이 탭 묶음 창이면 분할하지 않고, 운반 dock(Dock DockService.SplitDocumentDockable 이 끈 탭을 옮겨 둔 새 문서 dock)의 문서를
    /// 묶음이 있는 MDI 칸에 보통 MDI 창으로 꺼낸다(묶음 왼쪽 위에서 30 DIP 계단 — MdiTabGroup.Detach 와 같은 자리 규칙). 마지막 문서가 활성·맨 위.
    /// Dock 기본 분할은 묶음의 주인(MDI 문서 dock)이 ProportionalDock 이 아니라서 묶음을 새 ProportionalDock 으로 감싸 MDI 칸에 넣는다(디컴파일 FactoryBase.SplitToDock).
    /// 그 ProportionalDock 은 MDI 창(IMdiDocument)이 아니라 그려지지 않아, 묶음의 남은 문서와 분리한 문서가 모두 화면에서 사라졌다(otest-m-1 Fold5 재현 4/4, 2026-11-11).
    /// 처리했으면 true, 묶음 대상이 아니거나 옮길 문서가 없으면 false(호출자가 기존 분할 경로).
    /// </summary>
    private bool TrySplitOntoTabGroup(IDock dock, IDockable dockable)
    {
        if (dock is not MdiTabGroupDock group || group.Owner is not IDocumentDock { LayoutMode: DocumentLayoutMode.Mdi } mdi)
        {
            return false;
        }

        var docs = dockable is IDock carrier
            ? (carrier.VisibleDockables ?? Enumerable.Empty<IDockable>()).OfType<PocDocument>().ToList()
            : dockable is PocDocument single ? new List<PocDocument> { single } : new List<PocDocument>();
        if (docs.Count == 0)
        {
            return false;
        }

        var fromPanel = _panelToCarrier is { } memo && ReferenceEquals(memo.Carrier, dockable) ? memo.From : null; // 패널 탭에서 끌어 온 문서면 그 패널
        _panelToCarrier = null;
        Carry.ForgetCarried(); // 운반 dock 을 트리에 넣지 않는다 — 원래 자리 기억도 버린다(SplitCarryService)
        var b = group.MdiBounds;
        var top = (mdi.VisibleDockables ?? Enumerable.Empty<IDockable>()).OfType<IMdiDocument>().Select(d => d.MdiZIndex).DefaultIfEmpty(0).Max();
        PocDocument? last = null;
        foreach (var doc in docs)
        {
            MoveDockable(doc.Owner as IDock ?? mdi, mdi, doc, null);
            if (!ReferenceEquals(doc.Owner, mdi))
            {
                continue; // 옮기기가 거부됐다
            }

            var step = 30 * (docs.IndexOf(doc) + 1);
            doc.MdiBounds = new DockRect(b.X + step, b.Y + step, b.Width, b.Height);
            doc.MdiState = MdiWindowState.Normal;
            doc.MdiZIndex = ++top;
            last = doc;
        }

        if (last is not null)
        {
            SetActiveDockable(last);
            SetFocusedDockable(mdi, last);
            _detachedFromGroup = last; // 묶음이 1개로 줄어 풀리면(DissolveIfSingle 이 남은 문서를 활성화) 그 뒤에 다시 맨 위로
            QueueTabGroupTidy(last);
        }

        if (fromPanel is not null)
        {
            CenterDropToMdi.AfterLeave(this, fromPanel); // 가운데 드롭(TryDrop)과 같은 뒷정리 — 그 패널의 탭 변경 알림 + 빈 패널 접기
        }

        return true;
    }

    /// <summary>
    /// 패널 탭 → 트리 밖 운반 dock 이동(Dock DockService.SplitDocumentDockable 의 첫 단계)을 기억한다. PocDockFactory.MoveDockable 이 base 호출 전에 부른다.
    /// TrySplitOntoTabGroup 이 운반 dock 을 받으면 이 기억으로 원래 패널의 뒷정리를 한다(SplitAreaService.NotePanelCarry 와 같은 조건, 그 기억은 private 이라 따로 둔다).
    /// </summary>
    private void NotePanelToCarrier(IDock sourceDock, IDock targetDock)
    {
        if (sourceDock is PanelDocumentDock from && targetDock.Owner is null && !ReferenceEquals(targetDock, Root))
        {
            _panelToCarrier = (targetDock, from);
        }
    }

    /// <summary>분리한 문서가 아직 MDI 칸(묶음 밖)에 있으면 맨 위 Z 로 올리고 활성화한다(MRU Z 순서 훅이 활성 변경을 따라 다시 맞춘다).</summary>
    private void RaiseDetached()
    {
        var doc = _detachedFromGroup;
        _detachedFromGroup = null;
        if (doc?.Owner is not IDocumentDock { LayoutMode: DocumentLayoutMode.Mdi } mdi || mdi is MdiTabGroupDock)
        {
            return;
        }

        doc.MdiZIndex = (mdi.VisibleDockables ?? Enumerable.Empty<IDockable>()).OfType<IMdiDocument>().Select(d => d.MdiZIndex).DefaultIfEmpty(0).Max() + 1;
        SetActiveDockable(doc);
        SetFocusedDockable(mdi, doc);
    }

    /// <summary>탭 끌어내기·이동 뒤(Dock 기본 + 패널 탭 알림 + 묶음 정리 예약).</summary>
    public override void OnDockableMoved(IDockable? dockable)
    {
        base.OnDockableMoved(dockable);
        Tabs.OnDockableMoved(dockable);
        QueueTabGroupTidy(dockable);
    }

    /// <summary>탭 제거 뒤 묶음 정리 예약.</summary>
    public override void OnDockableRemoved(IDockable? dockable)
    {
        base.OnDockableRemoved(dockable);
        QueueTabGroupTidy(dockable);
    }

    /// <summary>탭 X 닫기 뒤 묶음 정리 예약.</summary>
    public override void OnDockableClosed(IDockable? dockable)
    {
        base.OnDockableClosed(dockable);
        QueueTabGroupTidy(dockable);
    }

    /// <summary>문서·묶음이 움직였을 때만 예약한다(도구 패널·분할 칸 변화는 무시). Dock 이 끌기 처리 중일 수 있어 바로 풀지 않는다.</summary>
    private void QueueTabGroupTidy(IDockable? dockable)
    {
        if (_tabGroupTidyRunning || _tabGroupTidyQueued || dockable is not (PocDocument or MdiTabGroupDock))
        {
            return;
        }

        _tabGroupTidyQueued = true;
        Dispatcher.UIThread.Post(RunTabGroupTidy, DispatcherPriority.Background);
    }

    private void RunTabGroupTidy()
    {
        _tabGroupTidyQueued = false;
        if (Root is null)
        {
            return;
        }

        _tabGroupTidyRunning = true;
        try
        {
            foreach (var mdi in MdiDocks(Root).ToList()) // 분할로 MDI 칸이 여럿일 수 있다. 패널·SDI(Tabbed) 칸은 건너뛴다. 푼 결과는 MdiTabGroup.Changed(Dissolved)로 알려진다
            {
                MdiTabGroup.Tidy(this, mdi);
            }
        }
        finally
        {
            _tabGroupTidyRunning = false;
        }

        RaiseDetached(); // 정리(남은 문서 활성화) 뒤에 해야 분리한 창이 맨 위에 남는다
    }

    /// <summary>트리 안 MDI 문서 칸(패널 탭 영역 제외). 묶음 안으로는 내려가지 않는다.</summary>
    private static IEnumerable<IDocumentDock> MdiDocks(IDockable node)
    {
        if (node is IDocumentDock { LayoutMode: DocumentLayoutMode.Mdi } mdi and not PanelDocumentDock and not MdiTabGroupDock)
        {
            yield return mdi;
        }

        if (node is MdiTabGroupDock || node is not IDock dock)
        {
            yield break;
        }

        foreach (var child in dock.VisibleDockables ?? Enumerable.Empty<IDockable>())
        {
            foreach (var found in MdiDocks(child))
            {
                yield return found;
            }
        }
    }
}
