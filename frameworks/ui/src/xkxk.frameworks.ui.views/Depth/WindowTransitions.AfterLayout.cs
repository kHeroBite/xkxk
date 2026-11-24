// 배치 뒤·그리기 전 실행 시점(WindowTransitions 의 partial, 300줄 규칙으로 분리) — 열기·전환 효과의 시작 상태가 첫 그림부터 보이게 한다
using System;
using Avalonia.Layout;
using Avalonia.Threading;

namespace xkxk.frameworks.ui.views.Depth;

public static partial class WindowTransitions
{
    /// <summary>
    /// 배치 뒤·그리기 전에 한 번 실행한다. 배치는 렌더 작업 안(MediaContext.RenderCore → BeginInvokeOnRender 로 건 LayoutManager.ExecuteQueuedLayoutPass)에서 돌고,
    /// LayoutUpdated 는 그 끝·합성기 커밋(CommitCompositorsWithThrottling) 전에 난다(Avalonia 12.1.4 디컴파일). 그래서 거기서 건 시작 상태가 첫 그림부터 보인다.
    /// 예전 Post(Loaded)는 Render 보다 우선순위가 낮아 이미 걸린 렌더가 먼저 돌았다(새 화면 100% 한 장 — r7 콤보 깜박임). 배치가 없을 때를 위해 Post(Loaded)를 예비로 남긴다.
    /// tryRun(final) 이 false 면 다음 시점을 기다린다(final = 예비 시점, 마지막 기회).
    /// </summary>
    private static void AfterLayout(Func<bool, bool> tryRun)
    {
        var layoutRoot = Root() as Layoutable;
        var done = false;
        EventHandler? onLayout = null;
        void Run(bool final)
        {
            if (done || (!tryRun(final) && !final))
            {
                return;
            }

            done = true;
            if (layoutRoot is not null && onLayout is not null)
            {
                layoutRoot.LayoutUpdated -= onLayout;
            }
        }

        if (layoutRoot is not null)
        {
            onLayout = (_, _) => Run(false);
            layoutRoot.LayoutUpdated += onLayout;
        }

        Dispatcher.UIThread.Post(() => Run(true), DispatcherPriority.Loaded);
    }
}
