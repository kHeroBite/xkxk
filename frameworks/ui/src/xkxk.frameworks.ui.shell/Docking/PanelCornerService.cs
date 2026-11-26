// 패널 끝 확장 동작(요건 3) — 현재 패널 배치에서 낼 버튼을 계산하고, 모서리를 가져오거나 되돌릴 때 소유 상태와 트리를 함께 바꾼다
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using Avalonia;
using Avalonia.Layout;
using Avalonia.Threading;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;
using xkxk.frameworks.ui.shell.Theming;
using xkxk.frameworks.ui.Controls;
using xkxk.frameworks.ui.shell.Framework;

namespace xkxk.frameworks.ui.shell.Docking;

/// <summary>
/// 대상은 기본 패널(영역마다 하나)이다. 분할로 만든 패널은 소속 영역 째 움직이므로 따로 다루지 않는다.
/// 변경 뒤에는 PocDockFactory.SplitChanged 를 발화해 자동 저장·로그가 기존 경로를 탄다. 화면 쪽(PanelCornerChrome)은 패널 Bounds 변화와 <see cref="ButtonsInvalidated"/> 로 다시 그린다.
/// </summary>
public sealed class PanelCornerService
{
    private readonly PocDockFactory _f;
    private readonly DockLayoutState _st;
    private readonly PanelCornerBuild _build;
    private readonly List<PanelCornerBuild.Level> _levels = new();
    private bool _invalidatePosted;
    private ProportionalDock? _host;

    internal PanelCornerService(PocDockFactory f, DockLayoutState st)
    {
        _f = f;
        _st = st;
        _build = new PanelCornerBuild(f, st);
    }

    /// <summary>
    /// 버튼 구성이 바뀌었을 수 있다(단의 칸 변화·모서리 변경). 문서 영역 분할·정리는 Dock 기본 경로라 SplitChanged 가 오지 않고 패널 Bounds 도 그대로일 수 있어 단 목록 변화를 직접 본다.
    /// 같은 틱의 변화는 한 번으로 모아 다음 UI 틱에 알린다.
    /// </summary>
    public event Action? ButtonsInvalidated;

    /// <summary>현재 모서리 소유.</summary>
    internal PanelCornerOwnership Ownership { get; private set; } = PanelCornerOwnership.Default;

    /// <summary>이 기기에서 확장 버튼을 쓰는가. 스마트폰은 패널이 없는 모바일형만 쓴다.</summary>
    internal static bool IsSupported => xkxk.frameworks.ui.shell.ViewModels.UiStyleViewModel.ClassifyHost(xkxk.frameworks.ui.shell.Platform.InputFormFactor.HostShortSideDp ?? double.MaxValue) != DeviceClass.Phone;

    /// <summary>새 트리가 만들어진 뒤(DockLayoutBuilder). 단을 기억하고 소유를 정한다. 단 목록은 바깥부터다.</summary>
    internal void OnTreeBuilt(IReadOnlyList<ProportionalDock> outerFirst, PanelCornerOwnership ownership)
    {
        foreach (var level in _levels)
        {
            WatchLevel(level.Dock, false);
        }

        _levels.Clear();
        if (_host is not null)
        {
            WatchLevel(_host, false);
        }

        _host = _st.DocumentsHost; // 문서 칸 분할은 패널 단 밖에서 일어나므로 따로 본다(버튼 표시 조건)
        if (_host is not null)
        {
            WatchLevel(_host, true);
        }

        foreach (var dock in outerFirst)
        {
            if (dock.VisibleDockables?.FirstOrDefault(d => d is ISplitter) is { } splitter)
            {
                _levels.Add(new PanelCornerBuild.Level(dock, splitter));
                WatchLevel(dock, true);
            }
        }

        Ownership = ownership;
    }

    private void WatchLevel(ProportionalDock dock, bool on)
    {
        if (dock.VisibleDockables is INotifyCollectionChanged list)
        {
            list.CollectionChanged -= OnLevelChanged;
            if (on)
            {
                list.CollectionChanged += OnLevelChanged;
            }
        }
    }

    private void OnLevelChanged(object? sender, NotifyCollectionChangedEventArgs e) => InvalidateButtons();

    private void InvalidateButtons()
    {
        if (_invalidatePosted || ButtonsInvalidated is null)
        {
            return;
        }

        _invalidatePosted = true;
        Dispatcher.UIThread.Post(() =>
        {
            _invalidatePosted = false;
            ButtonsInvalidated?.Invoke();
        }, DispatcherPriority.Background);
    }

    /// <summary>
    /// 패널 배치. 순위는 단 순서와 같다(알림 → 탐색기 → 로그 → 여분)이고 가장자리와 무관하다. 같은 가장자리면 순위가 작은 쪽이 바깥 줄이다.
    /// 알림 영역이 트리에 없는 진단 모드(--notify-panel off)면 뺀다.
    /// </summary>
    internal IReadOnlyList<PanelPlacement> Placements()
    {
        var result = new List<PanelPlacement>();
        var ranked = new[] { PanelArea.Notification, PanelArea.Explorer, PanelArea.Log, PanelArea.Extra };
        for (var rank = 0; rank < ranked.Length; rank++)
        {
            var area = ranked[rank];
            if ((area == PanelArea.Notification && !HasOuterLevel) || _f.Split.SlotOf(area) is not { } slot || _f.Split.HomePane(area)?.Id is not { } id)
            {
                continue;
            }

            var tabs = _f.Tabs.ContentTabsOf(id);
            var visible = !_st.Collapsed.ContainsKey(slot) && (tabs is null || !_f.IsDockablePinned(tabs));
            result.Add(new PanelPlacement(id, _f.Moves.EdgeOf(area), rank, visible));
        }

        return result;
    }

    /// <summary>바깥 단이 실제 트리에 있는가(--notify-panel off 진단 빌드는 OuterPane 이 트리 밖이다).</summary>
    private bool HasOuterLevel => _st.MainPane is { Owner: { } owner } && ReferenceEquals(owner, _st.Outer);

    private IReadOnlyList<PanelCornerBuild.Level> ActiveLevels() => HasOuterLevel ? _levels : _levels.Skip(1).ToList();

    private IDock? SlotDock(string id) => _f.Split.ResolvePane(_f.Tabs.TabsOf(id)) is { } hit ? _f.Split.SlotOf(hit.Area) : null;

    /// <summary>패널 끝마다 낼 버튼(끝 모서리·방향). 없으면 빈 목록.</summary>
    internal IReadOnlyList<(PanelCorner Corner, PanelCornerDirection Direction)> ButtonsOf(string panelId)
    {
        var panels = Placements();
        if (!CanChange(panels) || panels.FirstOrDefault(p => p.Id == panelId) is not { } panel)
        {
            return System.Array.Empty<(PanelCorner, PanelCornerDirection)>();
        }

        return PanelCornerRules.ClaimableCorners(panels, Ownership)
            .Where(t => t.PanelId == panelId)
            .Select(t => (t.Corner, DirectionOf(panel.Edge, t.Corner)))
            .ToList();
    }

    /// <summary>
    /// 머리줄 하나에 둘 버튼(대상 기본 패널 Id·끝 모서리·방향). 영역이 분할돼 있으면 영역의 그 끝에 있는 칸에만 둔다 —
    /// 위·왼쪽 끝은 첫 칸, 아래·오른쪽 끝은 마지막 칸. 영역 밖 패널(독립 창 등)은 빈 목록이다.
    /// </summary>
    public IReadOnlyList<(string PanelId, PanelCorner Corner, PanelCornerDirection Direction)> ButtonsForChrome(IDockable pane)
    {
        if (_f.Split.ResolvePane(pane) is not { } hit || !ReferenceEquals(hit.Pane, pane))
        {
            return System.Array.Empty<(string, PanelCorner, PanelCornerDirection)>();
        }

        var panelId = _f.Split.HomePane(hit.Area)?.Id ?? "";
        var panes = _f.PanesOf(hit.Area);
        var isFirst = panes.Count > 0 && ReferenceEquals(panes[0].Pane, pane);
        var isLast = panes.Count > 0 && ReferenceEquals(panes[^1].Pane, pane);
        return ButtonsOf(panelId)
            .Where(b => b.Direction is PanelCornerDirection.Up or PanelCornerDirection.Left ? isFirst : isLast)
            .Select(b => (panelId, b.Corner, b.Direction))
            .ToList();
    }

    /// <summary>패널 끝에서 창 끝으로 향하는 방향. 좌우 패널은 위·아래, 상하 패널은 왼쪽·오른쪽이다.</summary>
    internal static PanelCornerDirection DirectionOf(PanelEdge edge, PanelCorner corner) => edge is PanelEdge.Left or PanelEdge.Right
        ? (corner is PanelCorner.TopLeft or PanelCorner.TopRight ? PanelCornerDirection.Up : PanelCornerDirection.Down)
        : (corner is PanelCorner.TopLeft or PanelCorner.BottomLeft ? PanelCornerDirection.Left : PanelCornerDirection.Right);

    /// <summary>모서리를 바꿀 수 있는 상태인가. 버튼 표시와 실행(버튼·원격 명령)이 같은 판정을 쓴다.</summary>
    private bool CanChange(IReadOnlyList<PanelPlacement> panels) => IsSupported && !HasDocumentSplit;

    /// <summary>문서 칸이 나란히 나뉜 상태. 이때는 모서리 버튼을 내지 않는다(공통원칙 §4.4 4번).</summary>
    private bool HasDocumentSplit => _st.DocumentsHost?.VisibleDockables?.Count(d => d is not ISplitter) > 1;

    /// <summary>패널이 모서리를 가져온다: 그 방향 끝까지 넓어지고 원래 차지하던 패널이 줄어든다. 바꾸지 못했으면 false.</summary>
    public bool ClaimCorner(string panelId, PanelCorner corner)
    {
        var panels = Placements();
        if (!CanChange(panels) || !PanelCornerRules.TruncatedCorners(panels, Ownership).Contains(new TruncatedCorner(panelId, corner)))
        {
            return false;
        }

        var next = PanelCornerRules.Claim(panels, Ownership, panelId, corner);
        if (PanelCornerRules.IsPinwheel(panels, next))
        {
            return false;
        }

        if (next.IsDefault || next.OwnerOf(corner) is null)
        {
            return ReleaseCorner(corner);
        }

        Commit(panels, next, keepRaw: false);
        return true;
    }

    /// <summary>모서리 기록을 지워 기본 규칙으로 되돌린다. 기록이 없으면 false.</summary>
    internal bool ReleaseCorner(PanelCorner corner)
    {
        var panels = Placements();
        if (Ownership.OwnerOf(corner) is null || !CanChange(panels))
        {
            return false;
        }

        Commit(panels, Ownership.Without(corner), keepRaw: false);
        return true;
    }

    /// <summary>패널 가장자리를 옮기기 전 기본 배치로 돌린다. 옮긴 뒤에는 다투는 모서리가 달라져 옛 기록의 뜻이 없다.</summary>
    internal void ResetForEdgeMove()
    {
        if (!Ownership.IsDefault)
        {
            Commit(Placements(), PanelCornerOwnership.Default, keepRaw: false);
        }
    }

    /// <summary>패널 가장자리가 바뀐 뒤 단의 방향·순서를 맞춘다. 비율은 호출자가 정했다.</summary>
    internal void RearrangeForEdges() => Commit(Placements(), PanelCornerOwnership.Default, keepRaw: true);

    /// <summary>저장본 복원 시작: 기본 배치로 돌린다. 이어지는 복원이 저장된 비율을 그대로 넣으므로 비율은 계산하지 않는다.</summary>
    internal void ResetForRestore()
    {
        if (!Ownership.IsDefault)
        {
            Commit(Placements(), PanelCornerOwnership.Default, keepRaw: true);
        }
    }

    /// <summary>저장본 복원 끝: 저장된 모서리 소유를 트리에 반영한다. 없는 패널이거나 풍차면 기본 배치를 유지한다.</summary>
    internal void ApplyRestored(IReadOnlyDictionary<string, string>? stored)
    {
        var panels = Placements();
        var next = PanelCornerOwnership.FromStored(stored);
        if (!next.IsDefault && !PanelCornerRules.IsPinwheel(panels, next))
        {
            Commit(panels, next, keepRaw: true);
        }
    }

    /// <summary>저장 표현(PanelsSnapshot.CornerOwners).</summary>
    internal Dictionary<string, string>? Capture() => Ownership.ToStored();

    /// <summary>지금 바깥부터의 패널 Id 순서(진단·시험용).</summary>
    internal IReadOnlyList<string> CurrentOrder() => PanelCornerRules.PeelOrder(Placements(), Ownership)?.Select(p => p.Id).ToList() ?? new List<string>();

    private void Commit(IReadOnlyList<PanelPlacement> panels, PanelCornerOwnership next, bool keepRaw)
    {
        if (PanelCornerRules.PeelOrder(panels, next) is not { } order)
        {
            return;
        }

        var levels = ActiveLevels();
        var slots = order.Select(p => (p, Dock: SlotDock(p.Id))).Where(x => x.Dock is not null)
            .Select(x => new PanelCornerBuild.Slot(x.p.Id, x.Dock!, x.p.Edge)).ToList();
        if (slots.Count != levels.Count)
        {
            return; // 배치와 단 수가 어긋나면 트리를 건드리지 않는다
        }

        var roots = _f.DockControls.OfType<Visual>().ToList();
        var restoreView = XkxkUi.Current.Views.CapturePanelViewState(roots);
        _build.Apply(levels, slots, keepRaw, MainAreaSize());
        restoreView();
        Ownership = next;
        _f.RaiseSplitChanged();
        InvalidateButtons();
    }

    /// <summary>메인 도킹 영역 크기(최소 길이 계산용). 독립 창의 DockControl 은 뺀다. 배치 전이면 0.</summary>
    private Size MainAreaSize() => (_f.DockControls.FirstOrDefault(c => ReferenceEquals(c.Layout, _f.Root)) as Layoutable)?.Bounds.Size ?? default;
}
