// NAV 닫기가 '닫기 요청 때 보이던 문서'인지 판정(WindowTransitions 의 partial, 21 설계 §4.1) — 본문 화면 DataContext 에 기대지 않고 셸의 활성 문서로 본다
using System;
using Avalonia.Threading;
using Dock.Model.Core;
using xkxk.frameworks.ui.shell.Docking;

namespace xkxk.frameworks.ui.views.Depth;

public static partial class WindowTransitions
{
    private static WeakReference<PocDocument>? s_navShown; // 활성 변경이 화면에 반영된 뒤 잡은 문서
    private static bool s_navTaken; // 같은 처리 안에서 NAV 덮개는 1장(모두 닫기가 다음 문서를 잇달아 찍지 않게)

    private static void TrackNavShown(WindowCommands shell)
    {
        shell.ActiveChanged += _ => Dispatcher.UIThread.Post(() => RememberNavShown(shell), DispatcherPriority.Loaded);
        RememberNavShown(shell);
    }

    private static void RememberNavShown(WindowCommands shell) =>
        s_navShown = shell.ActiveDocument is { } doc ? new WeakReference<PocDocument>(doc) : null;

    private static PocDocument? NavShown() => s_navShown is not null && s_navShown.TryGetTarget(out var doc) ? doc : null;

    /// <summary>
    /// 본문 호스트(ContentPresenter)는 컨트롤 내용을 받으면 DataContext 를 지워 MobileNavViewModel 을 물려받으므로 쓰지 않는다.
    /// 지금 활성 문서와 활성 변경이 반영되기 전에 보이던 문서 둘 다 인정한다.
    /// </summary>
    private static bool TakeNavCurrent(IDockable dockable)
    {
        if (s_navTaken || (!ReferenceEquals(s_shell?.ActiveDocument, dockable) && !ReferenceEquals(NavShown(), dockable)))
        {
            return false;
        }

        s_navTaken = true;
        Dispatcher.UIThread.Post(() => s_navTaken = false, DispatcherPriority.Background);
        return true;
    }
}
