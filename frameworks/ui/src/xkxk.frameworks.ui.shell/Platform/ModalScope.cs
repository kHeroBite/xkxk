// 앱 공용 모달 범위 — 모달(설정 창 등)이 열린 동안 앱의 다른 창(메인·MDI·SDI·시스템 창)을 플랫폼 훅(OS)·입력 관문(앱) 두 겹으로 막고, 끝나면 이 범위가 막은 창만 되돌린다
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace xkxk.frameworks.ui.shell.Platform;

/// <summary>
/// 앱 모달 범위(03_2 §8.2). Window.ShowDialog 는 owner 하나만 막으므로(Avalonia Window.UpdateEnabled) 나머지 top-level 창은 여기서 막는다.
/// 해제 보장: Begin 이 돌려준 IDisposable 을 Dispose 하거나, 모달 창이 닫히거나(Closed), 모달 내용이 화면에서 떨어지면(DetachedFromVisualTree) 끝난다.
/// 끝은 한 번만 처리되고(멱등), 창마다 막은 횟수를 세어 마지막 범위가 끝날 때 플랫폼 차단을 푼다. 한 창 되돌리기가 실패해도 나머지는 계속 되돌린다.
/// UI 스레드 전용이다(다른 스레드의 Dispose 는 UI 스레드로 넘긴다).
/// </summary>
public static partial class ModalScope
{
    private static readonly List<Scope> Stack = new();
    private static readonly Dictionary<TopLevel, int> PlatformCounts = new();
    private static bool s_windowHookInstalled;

    /// <summary>OS 수준 창 차단 훅(데스크톱 호스트가 설치 — Windows 는 EnableWindow). (창, true=막기/false=풀기). null 이면 앱 관문만 쓴다.</summary>
    public static Action<TopLevel, bool>? PlatformBlock { get; set; }

    /// <summary>모달 상태가 바뀌었다(범위 시작·끝, 모달 중 새 창 차단). 진단 modal.state·트레이 라우팅이 쓴다.</summary>
    public static event EventHandler? Changed;

    /// <summary>모달 범위가 하나 이상 열려 있는가.</summary>
    public static bool IsActive => Stack.Count > 0;

    /// <summary>맨 위 모달 범위 이름(예: "settings"). 없으면 null.</summary>
    public static string? ActiveName => Stack.Count > 0 ? Stack[^1].Name : null;

    /// <summary>맨 위 모달(데스크톱 = 모달 Window, 단일 뷰 = Flyout 내용). 없으면 null.</summary>
    public static Control? ActiveModal => Stack.Count > 0 ? Stack[^1].Modal : null;

    /// <summary>
    /// 이 top-level 이 통째로 막혔는가(맨 위 범위 기준). 모달 창 자신·그 소유 사슬 아래 창·그 창의 팝업은 false 다.
    /// 단일 뷰(웹·Android)는 모달 내용이 같은 TopLevel 안 Flyout 이라 TopLevel 자체는 false 이고 내용 단위로 가른다(IsAllowed).
    /// </summary>
    public static bool IsBlocked(TopLevel top)
    {
        if (Stack.Count == 0)
        {
            return false;
        }

        var scope = Stack[^1];
        if (scope.Modal is not TopLevel && ReferenceEquals(TopLevel.GetTopLevel(scope.Modal), top))
        {
            return false; // 단일 뷰 — 같은 TopLevel 안에서 내용 단위로 막는다
        }

        return !IsTopLevelWithin(scope, top);
    }

    /// <summary>플랫폼 훅으로 지금 막혀 있는 창인가(이 범위들이 막은 것만 — Avalonia 가 owner 로 막은 창은 포함하지 않는다).</summary>
    public static bool IsPlatformBlocked(TopLevel top) => PlatformCounts.ContainsKey(top);

    /// <summary>모달 창(Window)을 범위로 연다. 창을 ShowDialog/Show 한 직후에 부른다.</summary>
    public static IDisposable Begin(TopLevel modal, string name) => BeginCore(modal, name);

    /// <summary>단일 뷰 모달 내용(Flyout 내용)을 범위로 연다. 같은 TopLevel 안에서 이 내용 밖의 입력을 막는다.</summary>
    public static IDisposable Begin(Control content, string name) => BeginCore(content, name);

    private static IDisposable BeginCore(Control modal, string name)
    {
        ArgumentNullException.ThrowIfNull(modal);
        EnsureHooks();
        var scope = new Scope(modal, name ?? string.Empty);
        foreach (var outer in Stack)
        {
            if (modal is TopLevel top)
            {
                Release(outer, top); // 바깥 범위가 새로 열린 모달 창을 막았으면 푼다(중첩 모달)
                if (HitRootOf(top) is { } root && outer.HitBlocked.Remove(root))
                {
                    HitRelease(root);
                }
            }
        }

        Stack.Add(scope);
        foreach (var window in DesktopWindows())
        {
            Block(scope, window);
        }

        HitBlock(scope, SingleViewRootFor(modal)); // 단일 뷰 — 메인 뷰 히트 테스트를 끈다(Flyout 은 OverlayLayer 라 그대로)

        if (modal is Window w)
        {
            w.Closed += scope.OnModalGone;
        }
        else
        {
            modal.DetachedFromVisualTree += scope.OnModalGone;
        }

        Log($"begin name={scope.Name} blocked={scope.Blocked.Count} depth={Stack.Count}");
        RaiseChanged();
        return scope;
    }

    private static void End(Scope scope)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => End(scope));
            return;
        }

        if (scope.Ended)
        {
            return;
        }

        scope.Ended = true;
        if (scope.Modal is Window w)
        {
            w.Closed -= scope.OnModalGone;
        }
        else
        {
            scope.Modal.DetachedFromVisualTree -= scope.OnModalGone;
        }

        // 범위를 스택에서 빼기 전에 푼다 — 푸는 동안 IsActive 가 true 로 남아 플랫폼 안전망(Changed→IsActive=false)이 해제 도중에 끼어들지 않는다
        foreach (var top in scope.Blocked.ToArray())
        {
            Release(scope, top); // 내부에서 예외를 삼키므로 한 창 실패가 나머지를 막지 않는다
        }

        scope.Blocked.Clear();
        foreach (var target in scope.HitBlocked)
        {
            HitRelease(target);
        }

        scope.HitBlocked.Clear();
        Stack.Remove(scope);
        if (Stack.Count == 0)
        {
            SweepLeaks(); // 기록이 어긋나 남은 차단(해제 누락)이 있으면 마지막 범위가 끝날 때 전부 푼다
            SweepHitBlocks();
        }

        Log($"end name={scope.Name} depth={Stack.Count}");
        RaiseChanged();
    }

    /// <summary>범위가 이 창을 막는다(모달 사슬 안이거나 이미 막았으면 건너뜀). ShowDialog owner 는 Avalonia 가 OS 차단을 맡으므로 관문만 쓴다.</summary>
    private static void Block(Scope scope, TopLevel top)
    {
        if (scope.Ended || IsTopLevelWithin(scope, top) || scope.Blocked.Contains(top))
        {
            return;
        }

        HitBlock(scope, HitRootOf(top)); // 1차 — 그 창 안 요소로 포인터가 가지 않는다(ShowDialog owner 도 포함, ModalScope.RawGate.cs)

        if (scope.Modal is Window { IsDialog: true } dialog && ReferenceEquals(dialog.Owner, top))
        {
            return;
        }

        scope.Blocked.Add(top);
        PlatformCounts.TryGetValue(top, out var count);
        PlatformCounts[top] = count + 1;
        if (count == 0)
        {
            if (top is Window window)
            {
                window.Closed += OnBlockedClosed;
            }

            CallPlatform(top, true);
        }
    }

    /// <summary>범위가 막은 창 하나를 푼다. 다른 범위가 아직 막고 있으면 횟수만 줄인다.</summary>
    private static void Release(Scope scope, TopLevel top)
    {
        if (!scope.Blocked.Remove(top) || !PlatformCounts.TryGetValue(top, out var count))
        {
            return; // 이 범위가 막지 않았거나 이미 닫힌 창(OnBlockedClosed)
        }

        if (count > 1)
        {
            PlatformCounts[top] = count - 1;
            return;
        }

        PlatformCounts.Remove(top);
        if (top is Window window)
        {
            window.Closed -= OnBlockedClosed;
        }

        CallPlatform(top, false);
    }

    private static void CallPlatform(TopLevel top, bool block)
    {
        try
        {
            PlatformBlock?.Invoke(top, block);
            Log($"{(block ? "block" : "release")} {(top as Window)?.Title ?? top.GetType().Name}");
        }
        catch (Exception ex)
        {
            Log($"[경고] 플랫폼 {(block ? "차단" : "해제")} 실패: {ex.Message}");
        }
    }

    /// <summary>막힌 창이 먼저 닫혔다 — 닫힌 창 핸들로 풀기를 부르지 않게 기록에서 뺀다.</summary>
    private static void OnBlockedClosed(object? sender, EventArgs e)
    {
        if (sender is not TopLevel top)
        {
            return;
        }

        (top as Window)!.Closed -= OnBlockedClosed;
        PlatformCounts.Remove(top);
        Log($"blocked-closed {(top as Window)?.Title}");
        foreach (var scope in Stack)
        {
            scope.Blocked.Remove(top);
        }
    }

    private static void EnsureHooks()
    {
        if (s_windowHookInstalled)
        {
            return;
        }

        s_windowHookInstalled = true;
        Window.WindowOpenedEvent.AddClassHandler<Window>(OnWindowOpened, RoutingStrategies.Direct, handledEventsToo: true);
    }

    /// <summary>모달 중 새로 열린 창(원격·트레이·자동 복원)도 열리는 즉시 막는다.</summary>
    private static void OnWindowOpened(Window window, RoutedEventArgs e)
    {
        if (Stack.Count == 0)
        {
            return;
        }

        foreach (var scope in Stack.ToArray())
        {
            Block(scope, window);
        }

        // 열림 알림은 표시 도중에 온다. 표시 마무리(호스트의 창 설정·활성화)가 OS 사용 가능 상태를 되돌릴 수 있어 끝난 뒤 한 번 더 건다(멱등, b3.modal.new_sdi)
        Dispatcher.UIThread.Post(() =>
        {
            if (PlatformCounts.ContainsKey(window))
            {
                CallPlatform(window, true);
            }
        }, DispatcherPriority.Background);
        RaiseChanged();
    }

    private static IEnumerable<TopLevel> DesktopWindows() =>
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Windows.ToArray()
        ?? Array.Empty<Window>();

    private sealed class Scope : IDisposable
    {
        public Scope(Control modal, string name)
        {
            Modal = modal;
            Name = name;
        }

        public Control Modal { get; }

        public string Name { get; }

        public List<TopLevel> Blocked { get; } = new();

        public List<Control> HitBlocked { get; } = new();

        public bool Ended { get; set; }

        public void OnModalGone(object? sender, EventArgs e) => End(this);

        public void Dispose() => End(this);
    }
}
