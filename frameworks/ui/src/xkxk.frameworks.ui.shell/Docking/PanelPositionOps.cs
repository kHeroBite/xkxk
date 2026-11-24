// 패널·패널 탭 '위치' 메뉴의 항목과 실행 — 네 가장자리와 다른 패널을 같은 규칙으로 낸다. 실행은 PanelMoveService 가 한다. 화면을 모르고 상태를 갖지 않는다
using System;
using System.Collections.Generic;
using System.Linq;
using Dock.Model.Core;

namespace xkxk.frameworks.ui.shell.Docking;

/// <summary>메뉴가 옮기는 단위. 패널 제목 줄은 패널 전체, 패널 안 탭은 그 탭 하나다.</summary>
public enum PanelMoveScope
{
    Panel,
    Tab,
}

/// <summary>이동 대상 종류.</summary>
public enum PanelMoveKind
{
    Edge,
    Panel,
    Documents,
}

/// <summary>메뉴 항목 하나. IsCurrent 는 지금 위치(체크·비활성)다.</summary>
public sealed record PanelMoveOption(string TargetId, PanelMoveKind Kind, string Label, bool IsCurrent, bool IsEnabled);

/// <summary>
/// 어느 패널이든 항목이 같다: 위·아래·왼쪽·오른쪽, 다른 패널 목록(탭 합류), 문서 탭이면 문서 영역.
/// 탭이 하나뿐인 패널의 탭은 패널 전체와 같다. 도구 탭에 문서 영역을 내지 않는 이유: 문서 영역에 들어간 도구는 이 메뉴가 없는 자리(MDI 창)로 간다.
/// </summary>
public static class PanelPositionOps
{
    private const string EdgePrefix = "Edge.";
    private const string DocumentsTarget = "Documents";
    private static readonly PanelEdge[] Edges = { PanelEdge.Top, PanelEdge.Bottom, PanelEdge.Left, PanelEdge.Right };

    /// <summary>대상의 이동 항목. 패널 탭·패널이 아니면(중앙 문서 영역 탭, 독립 창 안 등) 빈 목록이다.</summary>
    public static IReadOnlyList<PanelMoveOption> OptionsFor(PocDockFactory factory, IDockable? subject, PanelMoveScope scope)
    {
        if (subject is null || (scope == PanelMoveScope.Tab && subject is IDock)
            || factory.Split.ResolvePane(scope == PanelMoveScope.Tab ? subject.Owner : subject) is not { } hit)
        {
            return Array.Empty<PanelMoveOption>();
        }

        var whole = IsWhole(factory, hit.Pane, scope);
        var options = EdgeOptions(factory, hit.Area, hit.Pane, whole);
        options.AddRange(PanelOptions(factory, hit.Pane));
        if (scope == PanelMoveScope.Tab && subject is PocDocument)
        {
            options.Add(new PanelMoveOption(DocumentsTarget, PanelMoveKind.Documents, Localization.Loc.Tr("panel.pos.documents"), false, true));
        }

        return options;
    }

    /// <summary>
    /// 패널 ▾ 메뉴의 탭별 '이 탭 옮기기'(F07) 대상. 탭이 둘 이상인 패널만 낸다(하나뿐이면 패널 전체 메뉴와 같다).
    /// 이동은 기존 Move(Tab 범위)를 그대로 쓴다 — 새 이동 경로 없음.
    /// </summary>
    public static IReadOnlyList<IDockable> MovableTabs(PocDockFactory factory, IDockable? panel)
    {
        if (panel is null || panel is not IDock || factory.Split.ResolvePane(panel) is not { } hit
            || factory.Tabs.TabsOf(hit.Pane.Id ?? "")?.VisibleDockables is not { Count: > 1 } tabs)
        {
            return Array.Empty<IDockable>();
        }

        return tabs.Where(t => t is not IDock).ToList();
    }

    /// <summary>탭 하나를 가장자리(왼·오른·위·아래)나 독립 창으로 옮긴다. 가장자리는 Move(Tab, Edge.*), 독립 창은 분리 창 공통 입구(FloatDockable)다.</summary>
    public static bool MoveTabTo(PocDockFactory factory, IDockable tab, string target)
    {
        if (target == FloatTarget)
        {
            if (!tab.CanFloat)
            {
                return false;
            }

            factory.FloatDockable(tab);
            factory.Hide.SyncSidePanels();
            return true;
        }

        return Move(factory, tab, PanelMoveScope.Tab, EdgePrefix + target);
    }

    public const string FloatTarget = "Float";

    /// <summary>항목 실행. 없는 대상·지금 위치·비활성 항목은 거절한다(false).</summary>
    public static bool Move(PocDockFactory factory, IDockable subject, PanelMoveScope scope, string targetId)
    {
        if (OptionsFor(factory, subject, scope).FirstOrDefault(o => o.TargetId == targetId) is not { IsEnabled: true } option
            || factory.Split.ResolvePane(scope == PanelMoveScope.Tab ? subject.Owner : subject) is not { } hit)
        {
            return false;
        }

        var source = factory.Tabs.TabsOf(hit.Pane.Id ?? "");
        var tabs = scope == PanelMoveScope.Tab ? new List<IDockable> { subject } : source?.VisibleDockables?.ToList() ?? new List<IDockable>();
        var active = scope == PanelMoveScope.Tab ? subject : source?.ActiveDockable ?? tabs.FirstOrDefault();
        switch (option.Kind)
        {
            case PanelMoveKind.Edge:
                return active is not null && factory.Moves.MoveToEdge(hit.Pane, tabs, active, Enum.Parse<PanelEdge>(targetId[EdgePrefix.Length..]), IsWhole(factory, hit.Pane, scope));
            case PanelMoveKind.Documents:
                var returned = subject is PocDocument doc && factory.ReturnPanelDocument(doc);
                factory.Hide.SyncSidePanels(); // 옮긴 뒤 빈 패널을 접는다
                return returned;
            default:
                return active is not null && factory.Tabs.TabsOf(targetId) is { } target && factory.Moves.MoveTabs(tabs, active, target);
        }
    }

    private static bool IsWhole(PocDockFactory factory, IDock pane, PanelMoveScope scope) =>
        scope == PanelMoveScope.Panel || (factory.Tabs.TabsOf(pane.Id ?? "")?.VisibleDockables?.Count ?? 0) <= 1;

    /// <summary>가장자리 항목. 패널 전체가 지금 붙은 가장자리는 체크·비활성이다. 여러 탭 중 하나는 같은 가장자리에도 새 패널로 나갈 수 있다.</summary>
    private static List<PanelMoveOption> EdgeOptions(PocDockFactory factory, PanelArea area, IDock pane, bool whole)
    {
        var current = factory.Moves.EdgeOf(area);
        return Edges.Select(e =>
        {
            var isCurrent = whole && e == current;
            return new PanelMoveOption(EdgePrefix + e, PanelMoveKind.Edge, PanelEdgeNames.DisplayName(e), isCurrent, !isCurrent && factory.Moves.CanMoveToEdge(pane, e, whole));
        }).ToList();
    }

    /// <summary>패널 목록. 접힌 영역은 기본 패널 하나로 낸다 — 탭이 들어오면 되살아난다. 쓰지 않는 여분 영역은 뺀다. 집중 숨김으로 접힌 영역은 들어가도 보이지 않아 비활성이다.</summary>
    private static List<PanelMoveOption> PanelOptions(PocDockFactory factory, IDock currentPane)
    {
        var options = new List<PanelMoveOption>();
        foreach (var area in PanelAreaNames.All)
        {
            var panes = factory.PanesOf(area).Select(p => p.Pane).ToList();
            if (panes.Count == 0 && area != PanelArea.Extra && factory.Split.HomePane(area) is { } home)
            {
                panes.Add(home);
            }

            foreach (var pane in panes.Where(p => area != PanelArea.Extra || (factory.Tabs.TabsOf(p.Id ?? "")?.VisibleDockables?.Count ?? 0) > 0))
            {
                var isCurrent = ReferenceEquals(pane, currentPane);
                var enabled = !isCurrent && !factory.SidePanelsHidden;
                options.Add(new PanelMoveOption(pane.Id ?? "", PanelMoveKind.Panel, PanelLabel(factory, area, pane), isCurrent, enabled));
            }
        }

        return options;
    }

    private static string PanelLabel(PocDockFactory factory, PanelArea area, IDock pane)
    {
        var name = PanelEdgeNames.DisplayName(factory.Moves.EdgeOf(area));
        var title = factory.Tabs.TabsOf(pane.Id ?? "")?.ActiveDockable?.Title;
        return string.IsNullOrEmpty(title) ? Localization.Loc.Tr("panel.pos.dest", name) : Localization.Loc.Tr("panel.pos.destNamed", name, title);
    }
}
