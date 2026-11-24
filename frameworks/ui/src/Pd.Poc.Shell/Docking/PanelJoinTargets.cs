// 패널 탭 합류 판정 도우미 — 점 아래 합류 가능한 측면 패널 찾기와 MDI 제목줄 누름 여부 판정(상태 없음)
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using Dock.Model.Core;
using Pd.Ui.Controls;

namespace Pd.Poc.Shell.Docking;

/// <summary>PanelJoinDrag 가 쓰는 순수 판정 함수. 끌기 상태는 PanelJoinDrag 가 가진다.</summary>
internal static class PanelJoinTargets
{
    /// <summary>점 아래의 보이는 측면 패널(바깥 ToolDockControl). 팩터리가 없거나 집중 숨김 중이거나 없으면 null.</summary>
    public static Control? FindAt(PocDockFactory? factoryOrNull, TopLevel top, Point point) =>
        factoryOrNull is { SidePanelsHidden: false } ? FindAt(factoryOrNull, top, point, Candidates(top)) : null;

    /// <summary>미리 모은 후보(<see cref="Candidates"/>) 중 점 아래의 보이는 측면 패널. 판정 조건은 위 FindAt 과 같다(끄는 동안 후보 재사용 — PanelJoinDrag).</summary>
    public static Control? FindAt(PocDockFactory? factoryOrNull, TopLevel top, Point point, IReadOnlyList<ToolDockControl> candidates)
    {
        if (factoryOrNull is not { SidePanelsHidden: false } factory)
        {
            return null;
        }

        return candidates
            .Where(c => c.IsEffectivelyVisible && c.DataContext is IDock { Id: { } id } && factory.Tabs.ContentTabsOf(id) is not null) // 알림 서랍 포함(U18)
            .FirstOrDefault(c => c.TranslatePoint(default, top) is { } origin && new Rect(origin, c.Bounds.Size).Contains(point));
    }

    /// <summary>
    /// 합류 후보(ToolDockControl)를 시각 트리 앞선 순서(GetVisualDescendants 와 같은 깊이 우선 전위 순서)로 모은다.
    /// W4(웹 끌기 프레임 간격): 예전에는 끌기 이동마다 TopLevel 아래 전체 트리(열린 문서 내용 — 표·차트 등 포함)를 훑어 문서 수에 비례해 느려졌다.
    /// 문서(DataContext 가 PocDocument 인 요소) 아래에는 측면 패널이 없으므로 그 안으로 내려가지 않는다. 문서 영역 밖 트리는 예전과 같이 모두 본다.
    /// </summary>
    public static List<ToolDockControl> Candidates(TopLevel top)
    {
        var found = new List<ToolDockControl>();
        var stack = new Stack<Visual>();
        PushChildren(stack, top);
        while (stack.Count > 0)
        {
            var v = stack.Pop();
            if (v is ToolDockControl tool)
            {
                found.Add(tool);
            }

            if (v is StyledElement { DataContext: PocDocument })
            {
                continue; // 문서 내용 안은 보지 않는다
            }

            PushChildren(stack, v);
        }

        return found;
    }

    /// <summary>자식을 거꾸로 넣어 꺼낼 때 앞 자식부터 나오게 한다(전위 순서 유지).</summary>
    private static void PushChildren(Stack<Visual> stack, Visual parent)
    {
        var children = parent.GetVisualChildren() as IList<Visual> ?? new List<Visual>(parent.GetVisualChildren());
        for (var i = children.Count - 1; i >= 0; i--)
        {
            stack.Push(children[i]);
        }
    }

    /// <summary>누른 곳이 MDI 창 제목줄(PART_Header)인가. 캡션 버튼·Dock 자체 끌기 손잡이(PART_DragHandle)는 제외한다.</summary>
    public static bool IsTitlePress(Visual? source, MdiDocumentWindow window)
    {
        for (var v = source; v is not null && !ReferenceEquals(v, window); v = v.GetVisualParent())
        {
            if (v is Button || v is Control { Name: TemplateParts.DragHandle })
            {
                return false;
            }

            if (v is Control { Name: TemplateParts.Header })
            {
                return true;
            }
        }

        return false;
    }
}
