// 문서 뷰를 다른 창(TopLevel)으로 옮기기 직전에 옛 창의 대기 중인 측정·배치를 비워 Avalonia LayoutManager 의 'wrong LayoutManager' 예외를 막는다
using System;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Recycling;
using Avalonia.Layout;
using Avalonia.VisualTree;

namespace xkxk.frameworks.ui.views;

/// <summary>
/// Avalonia 12.1.4 LayoutManager 는 떨어지거나 다른 창으로 간 컨트롤을 대기열에서 빼지 않는다(디컴파일 확인).
/// 옛 창 대기열에 남은 항목이 새 창 트리에서 조상 측정 무효를 만나면 InvalidateArrange 가 다른 창 소속이라며 던진다(A10 FATAL).
/// 꺼내 가기 전에 옛 창 레이아웃 패스를 돌려 대기열을 비운다. 그 창이 이미 패스 중이면 ExecuteLayoutPass 는 아무것도 하지 않는다(_running 검사, 디컴파일 확인).
/// 뷰가 이미 옛 창에서 떨어진 뒤일 수도 있다(A10 실측: 탭을 다른 창에 놓으면 메인 탭 영역의 KeepAlive 칸이 먼저 뷰를 뗀다).
/// 그래서 떨어질 때의 루트를 기억했다가 그 루트의 패스를 돌린다. 떨어진 항목은 Arrange·Measure 가 NotVisible 로 건너뛰고 대기열에서 빠진다(디컴파일 확인).
/// </summary>
internal static class CrossRootLayoutFlush
{
    /// <summary>문서 뷰마다 마지막으로 떨어진 창의 루트(약한 참조 — 닫힌 창을 붙잡지 않는다).</summary>
    private static readonly ConditionalWeakTable<Layoutable, WeakReference<Layoutable>> s_lastRoot = new();

    /// <summary>문서 뷰가 떨어질 때 그 창의 루트를 기억한다. 뷰를 처음 만들 때 한 번 건다.</summary>
    public static void Track(Control view) =>
        view.DetachedFromVisualTree += (_, e) =>
        {
            if (e.Root is Layoutable root)
            {
                s_lastRoot.AddOrUpdate(view, new WeakReference<Layoutable>(root));
            }
        };

    /// <summary>presenter 가 공용 캐시에서 받아 갈 data 의 뷰가 다른 창에 붙어 있거나 다른 창에서 막 떨어졌으면 그 창의 레이아웃을 먼저 끝낸다.</summary>
    public static void BeforeFetch(object? data, Control presenter)
    {
        if (data is null
            || ControlRecyclingDataTemplate.GetControlRecycling(presenter) is not ControlRecycling cache
            || !cache.TryGetValue(data, out var cached) || cached is not Layoutable view)
        {
            return;
        }

        Flush(view, TopLevel.GetTopLevel(presenter));
    }

    /// <summary>view 가 target 과 다른 창에 붙어 있거나 그런 창에서 떨어진 채라면 그 창의 레이아웃 패스를 지금 돌린다(target 이 null 이면 그 창을 그대로 비운다).</summary>
    public static void Flush(Layoutable view, TopLevel? target)
    {
        // IsAttachedToVisualTree 는 외부에서 볼 수 없다(빌드 CS1061). 떨어진 컨트롤은 GetTopLevel 이 null 이라 같은 판정이 된다
        if (TopLevel.GetTopLevel(view) is { } old)
        {
            if (!ReferenceEquals(old, target))
            {
                old.UpdateLayout();
            }

            return;
        }

        // 이미 떨어졌다: 떨어진 창의 대기열에 이 뷰 하위 항목이 남아 있을 수 있다. 다른 창에 붙기 전, 아직 떨어진 지금 비워야 한다
        if (s_lastRoot.TryGetValue(view, out var weak) && weak.TryGetTarget(out var root))
        {
            s_lastRoot.Remove(view);
            // 떨어질 때 받는 루트는 창(TopLevel)의 시각 부모(TopLevelHost)라 GetTopLevel 로 창을 얻지 못한다(실측·디컴파일). 루트에서 바로 패스를 돌린다
            if (!IsSelfOrAncestor(root, target))
            {
                root.UpdateLayout();
            }
        }
    }

    private static bool IsSelfOrAncestor(Visual root, Visual? v)
    {
        for (; v is not null; v = v.GetVisualParent())
        {
            if (ReferenceEquals(v, root))
            {
                return true;
            }
        }

        return false;
    }
}
