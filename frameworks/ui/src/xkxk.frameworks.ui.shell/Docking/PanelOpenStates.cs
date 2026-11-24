// 패널 닫기·다시 열기와 가장자리 이동 전후 처리, 패널을 숨긴 동안 같은 축 안쪽 패널 몫 보정 — 모든 패널 공통. 접기·되살리기 자체는 PanelHideService 가 한다. 상태는 세션 안에서만 유지한다
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Threading;
using Dock.Model.Controls;
using Dock.Model.Core;

namespace xkxk.frameworks.ui.shell.Docking;

/// <summary>
/// 머리줄 ✕ 는 Dock 기본(RemoveDockable, 디컴파일 FactoryBase.CloseDockableCore)으로 가면 접기 기록 없이 트리에서 빠져 보기 메뉴로 되살릴 수 없다.
/// 그래서 기본 패널의 탭 영역은 닫힘 표시(DockLayoutState.ClosedAreas)만 남기고 PanelHideService.SyncSidePanels 의 접기 경로를 태운다.
/// 분할 패널·문서는 대상이 아니다(Dock 기본 동작).
/// </summary>
internal sealed class PanelOpenStates
{
    private readonly PocDockFactory _f;
    private readonly DockLayoutState _st;
    private readonly Dictionary<PanelArea, (IDock Target, double Factor)> _scales = new();

    internal PanelOpenStates(PocDockFactory f, DockLayoutState st)
    {
        _f = f;
        _st = st;
    }

    /// <summary>머리줄 ✕ 본문. 기본 패널이면 접고 true, 아니면 false(호출자가 Dock 기본 동작을 탄다). 영역이 분할돼 있으면 영역째 접는다.</summary>
    internal bool TryClosePanel(IDockable? dockable)
    {
        if (dockable is PanelDocumentDock tabs && ReturnDocumentsOnly(tabs))
        {
            return true;
        }

        if (_f.Split.ResolvePane(dockable) is not { } hit || !ReferenceEquals(hit.Pane, _f.Split.HomePane(hit.Area)))
        {
            return false;
        }

        _st.ClosedAreas.Add(hit.Area);
        _f.Hide.SyncSidePanels();
        _f.RaisePanelTabsChanged(dockable!.Id);
        return true;
    }

    /// <summary>
    /// 문서만 있는 패널의 ✕: 문서를 중앙으로 돌려보내고 빈 패널은 접는다. 접어 두면 제품판은 로그를 다시 여는 경로가 없어 재기동까지 문서를 볼 수 없다(team-lead 10-30).
    /// 도구가 함께 있으면 false 다(패널째 접고 보기 메뉴로 되살린다).
    /// </summary>
    private bool ReturnDocumentsOnly(PanelDocumentDock tabs)
    {
        var docs = tabs.VisibleDockables?.OfType<PocDocument>().ToList();
        if (docs is not { Count: > 0 } || docs.Count != tabs.VisibleDockables!.Count)
        {
            return false;
        }

        foreach (var doc in docs)
        {
            _f.Tabs.ReturnPanelDocument(doc);
        }

        _f.Hide.SyncSidePanels();
        return true;
    }

    /// <summary>
    /// 패널을 닫거나 핀으로 숨긴 동안 문서 영역만 넓어지게 한다. 바깥 단의 패널 몫이 빠지면 안쪽 단이 넓어지고 같은 축 안쪽 패널이 비율대로 함께 넓어지므로,
    /// 그 패널 비율을 (1 − 숨긴 몫)배로 줄였다가 돌아오면 되돌린다. 멱등이라 접기·핀 토글 끝마다 부른다.
    /// </summary>
    internal void SyncScales()
    {
        foreach (var area in PanelAreaNames.All)
        {
            if (_st.HomeTabs.TryGetValue(area, out var tabs) && _f.Split.SlotOf(area) is { } slot)
            {
                SyncScale(area, tabs, slot);
            }
        }
    }

    private void SyncScale(PanelArea area, IDock tabs, IDock slot)
    {
        var collapsed = _st.Collapsed.TryGetValue(slot, out var record);
        var hidden = collapsed || _f.IsDockablePinned(tabs);
        if (_scales.TryGetValue(area, out var on) && !hidden)
        {
            _scales.Remove(area);
            SetCrossShare(on.Target, on.Target.Proportion / on.Factor);
        }
        else if (!_scales.ContainsKey(area) && hidden && (collapsed || ReferenceEquals(slot, _st.HomePanes[area])))
        {
            // 영역이 분할돼 있으면 핀으로 칸 하나만 빠져도 다른 칸이 남아 바깥 몫이 그대로다. 저장된 몫도 영역 안 비율이라 곱하면 안쪽 패널이 줄어든다(N9, 196→98).
            var share = _st.PinShares.SavedProportion(tabs) ?? record?.Proportion ?? 0;
            if (InnerSameAxis(area) is { Proportion: > 0 and < 1 } target && share is > 0 and < 1)
            {
                var horizontal = _f.Moves.EdgeOf(area) is PanelEdge.Left or PanelEdge.Right;
                var before = Extent(target, horizontal); // 아직 배치 전이라 숨기기 전 폭(높이)
                _scales[area] = (target, 1 - share);
                SetCrossShare(target, target.Proportion * (1 - share));
                if (before > 0)
                {
                    Dispatcher.UIThread.Post(() => CorrectScale(area, target, horizontal, before), DispatcherPriority.Background);
                }
            }
        }
    }

    /// <summary>이 영역 바로 안쪽의, 같은 축에 있고 보이는 패널 자리. 모서리 확장으로 순서가 바뀌면 그 순서를 따른다.</summary>
    private IDock? InnerSameAxis(PanelArea area)
    {
        var horizontal = _f.Moves.EdgeOf(area) is PanelEdge.Left or PanelEdge.Right;
        var order = _f.Corners.CurrentOrder();
        var at = _st.HomePanes.TryGetValue(area, out var home) && home.Id is { } id ? order.ToList().IndexOf(id) : -1;
        if (at < 0)
        {
            return null;
        }

        foreach (var innerId in order.Skip(at + 1))
        {
            foreach (var (other, pane) in _st.HomePanes)
            {
                if (pane.Id == innerId && (_f.Moves.EdgeOf(other) is PanelEdge.Left or PanelEdge.Right) == horizontal
                    && _f.Split.SlotOf(other) is { } slot && !_st.Collapsed.ContainsKey(slot))
                {
                    return slot;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// 배치 뒤 1회 보정(N12): 핀으로 숨기면 바깥에 핀 레일(약 29dip)이 생겨 바깥 폭이 줄어든다. 위 비율 계산은 레일을 모르므로
    /// 안쪽 패널이 약 4dip 좁아졌다(200→196). 실제 폭이 숨기기 전 폭과 같아지게 비율을 고치고, 같은 배수를 기록에 곱해 다시 열 때 원래 비율로 돌아가게 한다.
    /// </summary>
    private void CorrectScale(PanelArea area, IDock target, bool horizontal, double before)
    {
        if (!_scales.TryGetValue(area, out var on) || !ReferenceEquals(on.Target, target) || target.Proportion is not (> 0 and < 1))
        {
            return;
        }

        var after = Extent(target, horizontal);
        if (after <= 0 || Math.Abs(after - before) < 0.5)
        {
            return;
        }

        var k = before / after;
        _scales[area] = (target, on.Factor * k);
        SetCrossShare(target, target.Proportion * k);
    }

    /// <summary>Dock 모델이 기록한 보이는 폭(가로 가장자리) 또는 높이(세로 가장자리). 아직 없으면 0.</summary>
    private static double Extent(IDock dock, bool horizontal)
    {
        dock.GetVisibleBounds(out _, out _, out var w, out var h);
        var v = horizontal ? w : h;
        return double.IsFinite(v) ? v : 0;
    }

    /// <summary>새 레이아웃을 만들 때 옛 트리를 가리키는 기록을 버린다.</summary>
    internal void ResetScales() => _scales.Clear();

    /// <summary>보정을 모두 되돌린다. 가장자리를 옮기기 전에 부른다. 옮긴 뒤 SyncScales 가 새 배치로 다시 계산한다.</summary>
    internal void ReleaseScales()
    {
        foreach (var (target, factor) in _scales.Values)
        {
            SetCrossShare(target, target.Proportion / factor);
        }

        _scales.Clear();
    }

    private static void SetCrossShare(IDock target, double share)
    {
        var old = target.Proportion;
        var siblings = (target.Owner as IDock)?.VisibleDockables?.Where(d => d is not ISplitter && !ReferenceEquals(d, target) && d.Proportion is > 0 and < 1).ToList();
        foreach (var sibling in siblings ?? new List<IDockable>())
        {
            PocDockFactory.SetShare(sibling, sibling.Proportion * (1 - share) / (1 - old));
        }

        PocDockFactory.SetShare(target, share);
    }

    /// <summary>닫힌 기본 패널의 닫힘 표시를 지운다. 되살리기는 호출자의 SyncSidePanels 가 한다.</summary>
    internal void ReopenPanel(IDock homePane)
    {
        foreach (var area in PanelAreaNames.All.Where(a => ReferenceEquals(_f.Split.HomePane(a), homePane)))
        {
            _st.ClosedAreas.Remove(area);
        }
    }

    /// <summary>가장자리 이동 준비: 닫힘·핀을 잠시 풀어 트리 재구성이 열린 상태와 같은 경로를 타게 한다. 핀 상태였으면 true.</summary>
    internal bool BeginMove(PanelArea area)
    {
        if (_f.Split.SlotOf(area) is { } slot && _st.Collapsed.ContainsKey(slot))
        {
            _f.Hide.RestorePanel(slot);
        }

        var pinned = _st.HomeTabs.TryGetValue(area, out var tabs) && _f.IsDockablePinned(tabs);
        if (pinned)
        {
            _f.Tabs.PinDockableCoreBypassShare(tabs!);
        }

        return pinned;
    }

    /// <summary>가장자리 이동 마무리: 새 가장자리에서 핀·닫힘·접힘을 되돌린다.</summary>
    internal void EndMove(PanelArea area, bool wasPinned)
    {
        if (wasPinned)
        {
            _f.Tabs.PinDockableCoreBypassShare(_st.HomeTabs[area]);
        }

        _f.Hide.SyncSidePanels(); // 빈 영역·닫힌 영역·집중 숨김 영역은 다시 접히고 몫 보정도 새 배치로 맞춘다
    }
}
