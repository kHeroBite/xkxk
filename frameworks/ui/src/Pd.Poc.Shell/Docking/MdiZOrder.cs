// MDI 창 Z순서(MRU 겹침·재기동 복원 공용 API·항상 위 그룹)와 위치 고정 옵션, 제목줄용 문서 찾기 헬퍼(옛 WindowCommands.ZOrder) — 항상 위·위치 고정은 서로 독립
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;
using Pd.Poc.Shell.Diagnostics;
using Pd.Ui.Controls;

namespace Pd.Poc.Shell.Docking;

/// <summary>MRU(s.Mru)는 읽기만 한다. 활성 변경 구독은 s.Owner.ActiveChanged 에 1회 건다.</summary>
[WindowCommandsPart]
internal sealed class MdiZOrder
{
    private readonly WindowSession _s;
    private bool _zHooked;
    private bool _zPending;

    internal MdiZOrder(WindowSession s) => _s = s;

    /// <summary>
    /// 위치 고정(IsLocked) 중 최소화·최대화도 막을지. 기본 false(허용) — IDE 조사(odev-1) 결론 '허용'.
    /// true 면 MDI 제목줄 최소·최대 버튼 누름과 SDI 제목줄 최소·최대·더블클릭을 무시한다. 닫기는 항상 허용.
    /// </summary>
    public static bool LockBlocksMinMax { get; set; }

    /// <summary>
    /// 제목줄이 붙은 곳의 DataContext 에서 문서를 찾는다(ChromeButtons·SdiTitleBar 공용).
    /// PocDocument 그대로, 웹 관리형 SDI 창(ManagedDockWindowDocument)은 Window.Layout 안, 도킹 트리는 활성 쪽 우선 → 첫 문서.
    /// </summary>
    public static PocDocument? FindDocument(object? context) => context switch
    {
        PocDocument doc => doc,
        ManagedDockWindowDocument managed => FindDocument(managed.Window?.Layout),
        IDock dock => FindDocument(dock.ActiveDockable)
            ?? (dock.VisibleDockables ?? (IEnumerable<IDockable>)Array.Empty<IDockable>()).Select(FindDocument).FirstOrDefault(d => d is not null),
        _ => null,
    };

    /// <summary>MDI 창(도킹 상태 문서)을 아래→위 순서로 반환. 같은 MdiZIndex 는 열린 순서. 탭 묶음 안 문서는 묶음 창의 Z 를 따른다.</summary>
    public IReadOnlyList<PocDocument> GetMdiZOrder() =>
        _s.MdiDocs().Select((d, i) => (d, i)).OrderBy(t => MdiTabGroup.WindowOf(t.d)!.MdiZIndex).ThenBy(t => t.i).Select(t => t.d).ToList();

    /// <summary>
    /// MDI 창 Z순서를 아래→위 목록대로 지정(목록에 없는 MDI 창은 맨 아래, 기존 상대 순서 유지).
    /// 지정 뒤 항상 위 창 그룹을 다시 맨 위로 올리되 그룹 안 상대 순서는 지정 순서를 따른다.
    /// </summary>
    public void SetMdiZOrder(IReadOnlyList<PocDocument> bottomToTop)
    {
        var rest = GetMdiZOrder().Where(d => !bottomToTop.Contains(d));
        ApplyZOrder(rest.Concat(bottomToTop.Where(d => _s.Owner.OpenDocuments.Contains(d))));
    }

    /// <summary>문서 1개의 Z 순위를 지정(0=맨 아래). 범위를 벗어나면 맨 위. 항상 위 그룹 규칙은 SetMdiZOrder 와 같다.</summary>
    public void SetMdiZOrder(PocDocument document, int rank)
    {
        var order = GetMdiZOrder().Where(d => !ReferenceEquals(d, document)).ToList();
        order.Insert(rank < 0 || rank > order.Count ? order.Count : rank, document);
        SetMdiZOrder(order);
    }

    /// <summary>
    /// 활성 변경마다 MDI Z순서를 MRU(최근 활성이 위)로 다시 맞추는 훅을 1회 건다(제목줄이 처음 붙을 때 호출).
    /// Dock 의 ClassicMdiLayoutManager.UpdateZOrder 는 열린 순서 + 활성 1개만 위로 다시 매겨 활성 이력을 잃으므로,
    /// 그 뒤(Background)에 덮어쓴다. 값이 바뀌는 창에만 대입한다(PERF-UI).
    /// </summary>
    public void EnsureZOrderHook()
    {
        EnsureRepaintOnZChange();
        if (_zHooked)
        {
            return;
        }

        _zHooked = true;
        _s.Owner.ActiveChanged += _ =>
        {
            if (!_zPending)
            {
                _zPending = true;
                Pd.Poc.Shell.Platform.UiDeferred.Post(ApplyMruZOrder);
            }
        };
    }

    internal void EnsurePinHook() => EnsureZOrderHook();

#if PD_DIAGNOSTICS
    /// <summary>진단 스위치(U3 A/B, render.switch fixoff): true 면 아래 Z 변경 다시 그리기 보정을 끈다. 진단 빌드에만 있다.</summary>
    internal static bool DiagRepaintDisabled { get; set; }
#else
    private const bool DiagRepaintDisabled = false; // 제품판: 보정은 늘 켜짐(끄는 경로 없음)
#endif

    private static bool s_repaintHooked;

    /// <summary>
    /// U3 활성 전환 시 간헐 오렌더 완화 — 원인 미확정(추정).
    /// 관측(GPU 켬에서만): 활성 전환 직후 Dock 의 중간 순서('열린 순서+활성 맨 위')가 화면에 남고, 뒤이은 ApplyMruZOrder 의 MRU 순서가
    /// 호버 같은 부분 다시 그리기가 생긴 곳에만 반영됐다(직전 활성 창이 묻히고 아래 창 조각이 위에 그려짐). 그때 ZIndex·합성 순서·히트는 맞았다.
    /// 반증된 것: 'Z 재정렬만으로는 다시 그려지지 않는다'(H2 순수형) — 활성 전환 없이 Z 만 바꾸면 이 보정 없이도 정상(logs/odev_c2/render/u3_ab.md).
    /// 완화: Z 가 바뀐 MDI 창(ZIndex 가 걸린 ContentPresenter)의 바깥 테두리(창 전체·그림자를 덮어 그리는 층)를 다시 그리게 해 그 사각형을 새 순서로 다시 합성한다(창 1개당 요소 1개).
    /// </summary>
    private static void EnsureRepaintOnZChange()
    {
        if (s_repaintHooked)
        {
            return;
        }

        s_repaintHooked = true;
        Visual.ZIndexProperty.Changed.AddClassHandler<ContentPresenter>((presenter, _) =>
        {
            if (!DiagRepaintDisabled && presenter.GetVisualParent() is MdiLayoutPanel)
            {
                (presenter.GetVisualDescendants().OfType<Border>().FirstOrDefault(b => b.Name == TemplateParts.OuterBorder) ?? (Visual)presenter).InvalidateVisual();
            }
        });
    }

    /// <summary>MRU(s.Mru 앞=가장 최근, 셸 상태 — 읽기만)를 아래→위로 뒤집어 적용. MRU 에 없는 MDI 창은 현재 순서대로 맨 아래.</summary>
    internal void ApplyMruZOrder()
    {
        using var op = PerfProbe.Current.TimeOperation("ui.activate.zorder"); // 활성 전환 분해(odev-2 요청)
        _zPending = false;
        var mdi = _s.MdiDocs().ToHashSet();
        var recent = _s.Mru.Where(mdi.Contains).Reverse().ToList();
        ApplyZOrder(GetMdiZOrder().Where(d => !recent.Contains(d)).Concat(recent));
    }

    /// <summary>항상 위(IsPinned) MDI 창을 다른 MDI 창 위로 올린다(현재 상대 순서 유지). IsLocked 와 무관.</summary>
    internal void RaisePinned() => ApplyZOrder(GetMdiZOrder());

    /// <summary>
    /// 아래→위 목록을 '보통 창 → 항상 위 창' 순으로 0..n-1 에 매긴다. 값이 다른 창에만 대입.
    /// 탭 묶음은 창 1개로 센다(그 안 문서 중 가장 위 순서를 쓰고, 문서 하나라도 항상 위면 묶음이 항상 위).
    /// </summary>
    private void ApplyZOrder(IEnumerable<PocDocument> bottomToTop)
    {
        EnsureRepaintOnZChange();
        var list = new List<IMdiDocument>();
        foreach (var w in bottomToTop.Select(d => MdiTabGroup.WindowOf(d)!))
        {
            list.Remove(w);
            list.Add(w);
        }

        static bool Pinned(IMdiDocument w) => w is MdiTabGroupDock g ? g.Documents.Any(d => DocumentChromeService.ChromeOf(d).IsPinned) : DocumentChromeService.ChromeOf((PocDocument)w).IsPinned;
        var z = 0;
        foreach (var win in list.Where(w => !Pinned(w)).Concat(list.Where(Pinned)))
        {
            if (win.MdiZIndex != z)
            {
                win.MdiZIndex = z;
            }

            z++;
        }

        if (list.Any(Pinned))
        {
            EnsureZOrderHook(); // 레이아웃 복원으로 켜진 항상 위도 이후 활성 변경을 따라가게
        }
    }
}
