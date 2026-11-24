// 측면 패널 본문을 눌러 안쪽 탭이 초점을 받아도 바깥 패널 머리줄(:active)이 활성으로 보이게 맞추는 초점 보정(D-C2-22)
using System;
using System.Collections.Generic;
using System.Linq;
using Dock.Model;
using Dock.Model.Controls;
using Dock.Model.Core;

namespace xkxk.frameworks.ui.shell.Docking;

/// <summary>
/// 원인(Dock 12.1.0.12 디컴파일): ToolChromeControl·ToolControl·DocumentControl 이 모두 PointerPressed 를 Tunnel 로 받아
/// factory.SetFocusedDockable(root, 자기 dock.ActiveDockable) 을 부른다. 측면 패널은 바깥 ToolDock(머리줄) 안에 탭 영역
/// (PanelDocumentDock)이 있는 2겹이다.
///  · 머리줄을 누르면 바깥 처리기만 돌아 초점 = 탭 영역 → 그 소유자인 ToolDock.IsActive=true → 머리줄 :active.
///  · 본문을 누르면 바깥 처리기 다음에 안쪽 DocumentControl 처리기가 마지막으로 돌아 초점 = 탭(탐색기 등) →
///    FactoryBase.SetFocusedDockable 은 '초점 dockable 의 바로 위 소유자'(탭 영역)만 IsActive 로 올리고 이전 소유자(ToolDock)는 내린다.
///    그래서 머리줄이 활성으로 바뀌지 않는다. 포인터 종류와 무관한 모델 동작이라 데스크톱 클릭도 같다(코드 근거, 실기 미확인).
/// 보정: 초점 변경 전후에 관련된 바깥 패널마다 'IsActive = 안쪽 탭 영역이 활성 || 어떤 루트의 초점이 그 탭 영역' 으로 다시 맞춘다.
/// 머리줄을 눌렀을 때(초점 = 탭 영역)는 Dock 기본 결과와 같고, 본문을 눌렀을 때만 바깥 패널이 함께 활성된다. 본문 안 컨트롤 동작은 건드리지 않는다.
/// </summary>
internal static class PanelFocusActivation
{
    internal static void SetFocused(FactoryBase factory, IDock dock, IDockable? dockable, Action<IDock, IDockable?> baseSet)
    {
        // 초점 변경은 루트를 만들거나 없애지 않는다. Find 는 DockControl 에 등록된 레이아웃만 훑으므로 이번 대상 루트도 넣는다
        var roots = factory.Find(x => x is IRootDock).OfType<IRootDock>().ToList();
        if (factory.FindRoot(dock, r => r.IsFocusableRoot) is { } own && !roots.Contains(own))
        {
            roots.Add(own);
        }

        var panels = new HashSet<IToolDock>(PanelsOfFocus(roots));
        baseSet(dock, dockable);
        panels.UnionWith(PanelsOfFocus(roots));
        var focused = FocusedOfRoots(roots).ToList();
        foreach (var panel in panels)
        {
            var inner = InnerTabs(panel);
            panel.IsActive = inner is not null && (inner.IsActive || focused.Contains(inner));
        }
    }

    private static IEnumerable<IDockable> FocusedOfRoots(List<IRootDock> roots) =>
        roots.Select(r => r.FocusedDockable).OfType<IDockable>();

    /// <summary>각 루트의 초점이 측면 패널 탭(또는 그 탭 영역)이면 바깥 패널(ToolDock).</summary>
    private static IEnumerable<IToolDock> PanelsOfFocus(List<IRootDock> roots)
    {
        foreach (var focused in FocusedOfRoots(roots))
        {
            var tabs = IsPanelTabs(focused) ? focused : focused.Owner is { } o && IsPanelTabs(o) ? o : null;
            if (tabs?.Owner is IToolDock panel)
            {
                yield return panel;
            }
        }
    }

    private static IDock? InnerTabs(IToolDock panel) =>
        panel.VisibleDockables?.FirstOrDefault(IsPanelTabs) as IDock;

    private static bool IsPanelTabs(IDockable d) => d is PanelDocumentDock;
}
