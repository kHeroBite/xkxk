// 분할 운반 dock 되돌리기(UI-20 R2 후속, 옛 PocDockFactory.SplitCarry) — 놓기 전 원래 자리를 기억했다가, 분할을 못 하면 그 자리로 되돌린다
using System.Collections.Generic;
using System.Linq;
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;

namespace xkxk.frameworks.ui.shell.Docking;

/// <summary>
/// 근거(Dock 12.1.0.12 디컴파일 DockService.SplitToolDockable·SplitDocumentDockable):
/// 가장자리 놓기는 트리 밖에 새 운반 dock 을 만들고(CreateToolDock·CreateDocumentDock) MoveDockable(원래 소유자 → 운반 dock) 한 뒤
/// SplitToDock(대상, 운반 dock) 만 부르고 후처리가 없다. 원래 소유자는 SplitToDock 에 넘어오지 않는다.
/// 그래서 '트리 밖 dock 으로 옮기는' MoveDockable 을 가로채 원래 소유자·인덱스를 기억한다(운반 dock 하나분만).
/// MoveDockable override 는 PocDockFactory 에 남아 Remember 를 부른 뒤 base.MoveDockable 을 부른다. base.SplitToDock 은 _f.BaseSplitToDock 이다.
/// </summary>
internal sealed class SplitCarryService
{
    private readonly PocDockFactory _f;
    private readonly DockLayoutState _st;
    private IDock? _carrier;
    private readonly List<(IDockable Item, IDock From, int Index)> _carriedFrom = new();

    internal SplitCarryService(PocDockFactory f, DockLayoutState st)
    {
        _f = f;
        _st = st;
    }

    /// <summary>PocDockFactory.MoveDockable(override)가 base 호출 전에 부른다 — 트리 밖 dock 으로 옮기면 원래 소유자·인덱스를 기억한다.</summary>
    internal void Remember(IDock sourceDock, IDock targetDock, IDockable sourceDockable)
    {
        if (targetDock.Owner is null && !ReferenceEquals(targetDock, _f.Root) && sourceDock.VisibleDockables is { } from)
        {
            if (!ReferenceEquals(targetDock, _carrier))
            {
                _carrier = targetDock; // 새 운반 dock — 이전 기억은 버린다
                _carriedFrom.Clear();
            }

            _carriedFrom.Add((sourceDockable, sourceDock, from.IndexOf(sourceDockable)));
        }
    }

    internal void ForgetCarried()
    {
        _carrier = null;
        _carriedFrom.Clear();
    }

    /// <summary>
    /// 분할을 못 한 운반 dock 의 내용을 되돌린다. ① 기억한 원래 소유자가 아직 트리에 있으면 그 자리·순서로,
    /// ② 기억이 없으면 종류별 기본 자리(문서 → 중앙 문서 영역, Log → 하단, 그 밖 도구 → 좌측, 알림 → 알림 탭 영역)로 보낸다.
    /// 갈 곳이 없는 항목이 하나라도 있으면(레이아웃 생성 전이라 Documents 가 없는 등) Dock 기본 분할(base.SplitToDock)로 넘겨 운반 dock 을 트리에 남긴다 —
    /// 문서가 화면에서 사라져 되찾을 수 없는 것보다 패널 안 분할이 생기는 편이 낫다(lead 결정).
    /// </summary>
    internal void ReturnCarried(IDock dock, IDock carrier, DockOperation operation)
    {
        var memo = ReferenceEquals(carrier, _carrier) ? _carriedFrom.ToList() : new List<(IDockable Item, IDock From, int Index)>();
        ForgetCarried();
        var plan = (carrier.VisibleDockables?.ToList() ?? new List<IDockable>())
            .Select(item => (Item: item, Home: OriginOf(item, memo) ?? DefaultHome(item), Index: IndexOf(item, memo)))
            .ToList();
        if (plan.Any(p => p.Home is null))
        {
            System.Diagnostics.Debug.WriteLine("[경고][dock] 분할 취소 되돌리기 불가: 갈 곳 없음(레이아웃 생성 전) — Dock 기본 분할로 트리에 남긴다"); // i18n-ok 개발자 진단 출력
            _f.BaseSplitToDock(dock, carrier, operation);
            return;
        }

        foreach (var (item, home, index) in plan)
        {
            var list = home!.VisibleDockables;
            var before = list is { Count: > 0 } && index >= 0 && index < list.Count ? list[index] : list?.LastOrDefault();
            _f.MoveDockable(carrier, home, item, before);
            _f.RaisePanelTabsChanged(home.Id);
        }

        _f.Hide.SyncSidePanels();
    }

    /// <summary>기억한 원래 소유자 — 그 사이 정리돼 트리에서 빠졌으면 null(기본 자리로).</summary>
    private IDock? OriginOf(IDockable item, List<(IDockable Item, IDock From, int Index)> memo)
    {
        var from = memo.FirstOrDefault(m => ReferenceEquals(m.Item, item)).From;
        return from is not null && _f.Root is { } root && _f.FindDockable(root, d => ReferenceEquals(d, from)) is not null ? from : null;
    }

    /// <summary>기억한 원래 인덱스(없으면 -1 — 맨 뒤로).</summary>
    private static int IndexOf(IDockable item, List<(IDockable Item, IDock From, int Index)> memo)
    {
        foreach (var m in memo)
        {
            if (ReferenceEquals(m.Item, item))
            {
                return m.Index;
            }
        }

        return -1;
    }

    private IDock? DefaultHome(IDockable item) =>
        item is ViewModels.NotificationPanelViewModel ? _f.Split.HomeTabs(PanelArea.Notification)
            : item is Tool ? _f.Split.HomeTabs(item.Id == "Log" ? PanelArea.Log : PanelArea.Explorer) : _f.Documents;
}
