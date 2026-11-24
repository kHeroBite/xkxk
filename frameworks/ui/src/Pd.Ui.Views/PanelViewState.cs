// 패널 내용 화면 상태 보존(요건 3) — 패널 끝 확장으로 패널이 다른 단으로 옮겨질 때 목록 선택·스크롤 위치를 기억했다가 배치 뒤 되돌린다
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;

namespace Pd.Ui.Views;

/// <summary>
/// 패널을 옮기면 머리줄·탭 영역 컨트롤이 다시 만들어지고, 재사용되는 도구 내용도 트리에서 떼어지는 동안 ItemsSource 바인딩이 끊겨 선택이 지워진다(로그 목록 실측 1→-1).
/// 이 상태들은 뷰모델에 없는 순수 화면 값이고 내용 뷰가 늘어도 같은 규칙이 필요해, 뷰모델마다 속성을 두지 않고 화면 층에서 패널별 등장 순서로 기억한다.
/// 입력 글자는 뷰가 재사용되므로 대상이 아니다.
/// </summary>
internal sealed class PanelViewState
{
    private readonly Dictionary<object, (List<int> Selected, List<Vector> Offsets)> _panes = new();

    /// <summary>창 안의 도구 패널 머리줄 전부에서 지금 값을 기억한다.</summary>
    internal static PanelViewState Capture(IEnumerable<Visual> roots)
    {
        var state = new PanelViewState();
        foreach (var chrome in Chromes(roots))
        {
            if (chrome.DataContext is { } key)
            {
                state._panes[key] = (Lists(chrome).Select(l => l.SelectedIndex).ToList(), Scrollers(chrome).Select(s => s.Offset).ToList());
            }
        }

        return state;
    }

    /// <summary>
    /// 배치가 끝난 뒤 값을 되돌린다. 값이 같으면 건드리지 않는다. 옮겨진 내용 뷰는 트리에 다시 붙은 뒤에야 바인딩·스크롤 범위가 돌아오고 그 시점을 알리는 공개 이벤트가 없어,
    /// 첫 레이아웃 뒤와 그 뒤 두 번 더 맞춘다(모두 멱등). 클릭 직후 0.3초 안이라 사용자 조작과 겹치지 않는다.
    /// </summary>
    internal void RestoreAfterLayout(IEnumerable<Visual> roots)
    {
        Dispatcher.UIThread.Post(() => Restore(roots), DispatcherPriority.Background);
        DispatcherTimer.RunOnce(() => Restore(roots), System.TimeSpan.FromMilliseconds(100));
        DispatcherTimer.RunOnce(() => Restore(roots), System.TimeSpan.FromMilliseconds(300));
    }

    private void Restore(IEnumerable<Visual> roots)
    {
        foreach (var chrome in Chromes(roots))
        {
            if (chrome.DataContext is not { } key || !_panes.TryGetValue(key, out var saved))
            {
                continue;
            }

            var lists = Lists(chrome).ToList();
            for (var i = 0; i < lists.Count && i < saved.Selected.Count; i++)
            {
                if (lists[i].SelectedIndex != saved.Selected[i] && saved.Selected[i] < lists[i].ItemCount)
                {
                    lists[i].SelectedIndex = saved.Selected[i];
                }
            }

            var scrollers = Scrollers(chrome).ToList();
            for (var i = 0; i < scrollers.Count && i < saved.Offsets.Count; i++)
            {
                if (scrollers[i].Offset != saved.Offsets[i])
                {
                    scrollers[i].Offset = saved.Offsets[i];
                }
            }
        }
    }

    private static IEnumerable<ToolChromeControl> Chromes(IEnumerable<Visual> roots) =>
        roots.SelectMany(r => r.GetVisualDescendants()).OfType<ToolChromeControl>();

    private static IEnumerable<ListBox> Lists(Visual chrome) => chrome.GetVisualDescendants().OfType<ListBox>();

    private static IEnumerable<ScrollViewer> Scrollers(Visual chrome) => chrome.GetVisualDescendants().OfType<ScrollViewer>();
}
