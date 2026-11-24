// 탭형 문서 본문 유지 칸 표식 — 뷰 층(xkxk.frameworks.ui.views/DocumentTabKeepAlive)이 칸에 붙이고, 셸·뷰의 화면 트리 훑기가 숨은 칸 하위를 건너뛰는 데 쓴다
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace xkxk.frameworks.ui.shell.Docking;

/// <summary>
/// 문서 전환 가속은 가려진 탭 문서 뷰를 숨긴 채 화면 트리에 남긴다(xkxk.frameworks.ui.views/DocumentTabKeepAlive.cs). 예전에는 떼어져 트리에 없었다.
/// 그래서 화면 트리 전체를 훑는 곳은 숨은 문서 하위(그리드 행·셀 수천 개)까지 훑게 됐다. 그런 곳은 <see cref="Descendants"/> 로 바꿔 예전 비용·예전 결과로 돌린다.
/// 쓰는 곳: MdiActiveSync(활성 전환), WindowSystemMenu(Alt+Space 창 메뉴·MDI 문서 창 메뉴), BackDismissal(Android 뒤로 가기), SettingsInput(Ctrl+0 대상).
/// </summary>
public static class TabSlotMarker
{
    /// <summary>칸(Decorator)에 붙이는 스타일 클래스 이름.</summary>
    public const string SlotClass = "pdTabKeepAliveSlot";

    /// <summary>숨은 탭 본문 칸인가(표식이 있고 지금 IsVisible=false).</summary>
    public static bool IsHiddenSlot(Visual visual) => !visual.IsVisible && visual is StyledElement { Classes: var classes } && classes.Contains(SlotClass);

    /// <summary>
    /// root 의 하위 요소를 GetVisualDescendants 와 같은 순서(앞→뒤 전위 순회)로 낸다. 숨은 탭 본문 칸은 그 칸 자신만 내고 하위로 내려가지 않는다.
    /// 숨은 칸 하위는 문서 전환 가속 이전에는 트리에 없던 요소들이라, 찾는 결과는 그때와 같다.
    /// </summary>
    public static IEnumerable<Visual> Descendants(Visual root)
    {
        var stack = new Stack<Visual>();
        PushChildren(stack, root);
        while (stack.Count > 0)
        {
            var visual = stack.Pop();
            yield return visual;
            if (!IsHiddenSlot(visual))
            {
                PushChildren(stack, visual);
            }
        }
    }

    // 뒤 자식부터 넣어 앞 자식이 먼저 나오게 한다(재귀 전위 순회와 같은 순서)
    private static void PushChildren(Stack<Visual> stack, Visual parent)
    {
        foreach (var child in parent.GetVisualChildren().Reverse())
        {
            stack.Push(child);
        }
    }
}
