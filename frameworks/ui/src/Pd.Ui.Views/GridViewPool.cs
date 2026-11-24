// 화면 밖 예열 호스트와 준비된 표 뷰 1개 보관 — 뷰를 실제 크기로 붙여 데우고, 표 뷰 1개를 다음 표 열기에 넘긴 뒤 유휴 때 다시 채운다(A16, 옵션 PrewarmViews·GridViewPool(기본 끔))
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Pd.Client.Data.Logging;
using Pd.Poc.Shell.Busy;
using Pd.Poc.Shell.Diagnostics;
using Pd.Poc.Shell.Diagnostics.Remote;
using Pd.Poc.Shell.Theming;
using Pd.Ui.Views.Documents;

namespace Pd.Ui.Views;

/// <summary>
/// 규칙 정본: plan/09_화면설계/01_화면설계_공통원칙.md §5.4(문서 뷰 예열과 준비된 표 뷰).
/// 화면 밖 호스트는 메인 화면 DockPanel 맨 앞에 넣는 높이 0·잘라냄·입력 제외 Canvas 다. 그 안에 뷰를 실제 문서 영역 크기의 칸(Border)으로 붙이면
/// 테마·스타일·템플릿·행/셀 생성·글자 측정이 실제 열기와 같은 경로로 돈다(붙지 않은 뷰는 테마가 적용되지 않는다 — StyledElement 는 논리 트리에 붙을 때 스타일을 적용).
/// 그림은 화면 밖 비트맵(RenderTargetBitmap)에 한 번 그려 글자 모양·글리프 캐시를 데운다(화면에는 그리지 않는다).
/// 준비된 표 뷰는 실제 열기 때 칸에서 떼어 문서 칸에 붙인다. 옮겨 붙일 때 Avalonia 는 스타일을 지웠다 다시 매칭하고 측정을 다시 하지만,
/// 템플릿은 같은 인스턴스라 다시 만들지 않고(TemplatedControl.ApplyTemplate) 행·셀 요소도 재사용한다(12.1.4 디컴파일 확인).
/// 만들 때는 PerfProbe.SuppressOpenTrack 로 열기 계측을 끄고, 넘길 때 GridDocumentView.BeginOpenTracking 으로 켠다.
/// </summary>
public static class GridViewPool
{
    /// <summary>다시 채우기 전 입력이 없어야 하는 시간(ms). 채우기는 UI 스레드를 수백 ms 막으므로 사용자가 조작 중이면 미룬다.</summary>
    private const int InputQuietMs = 2000;
    private const int IdleHoldMs = 1000;
    private const int PollMs = 250;

    private static Canvas? s_host;
    private static Border? s_readySlot;
    private static GridDocumentView? s_ready;
    private static Size s_lastSize;
    private static long s_lastInputMs;
    private static bool s_refillScheduled;

    /// <summary>준비된 표 뷰가 있는가(DocumentViewLocator 가 빈 칸 프레임 없이 바로 붙일지 판단).</summary>
    public static bool HasReady => s_ready is not null && UiSettings.Current.GridViewPool;

    /// <summary>화면 밖 호스트를 메인 화면 패널 맨 앞에 넣는다(1회). 패널이 DockPanel 이면 위쪽에 높이 0 으로 붙어 배치에 영향이 없다.</summary>
    public static void AttachHost(Panel parent)
    {
        if (s_host is not null)
        {
            return;
        }

        var host = new Canvas { Height = 0, ClipToBounds = true, IsHitTestVisible = false, Focusable = false };
        DockPanel.SetDock(host, Avalonia.Controls.Dock.Top);
        KeyboardNavigation.SetTabNavigation(host, KeyboardNavigationMode.None); // Tab 이 화면 밖 뷰(필터 입력칸)로 들어가지 않게
        UiSettings.Current.ApplyDocumentScope(host); // 문서 범위 글자 토큰을 실제 문서 칸과 같게 — 옮겨 붙일 때 글자 크기가 바뀌어 열 폭을 다시 계산하지 않게
        UiSettings.Current.Changed += () =>
        {
            UiSettings.Current.ApplyDocumentScope(host);
            if (!UiSettings.Current.GridViewPool) { Release(); }
        };
        parent.Children.Insert(0, host);
        s_host = host;
        HookInput();
    }

    /// <summary>화면 밖 호스트가 준비됐는가(예열이 붙여서 데울 수 있는가).</summary>
    internal static bool HasHost => s_host is not null;

    /// <summary>실제 화면에 배치된 표 문서 크기를 기억한다(다음 준비 뷰 크기). 화면 밖 칸에 있는 뷰는 건너뛴다.</summary>
    internal static void NoteSize(GridDocumentView view, Size size)
    {
        if (!ReferenceEquals(view, s_ready) && size.Width >= 100 && size.Height >= 100)
        {
            s_lastSize = size;
        }
    }

    /// <summary>
    /// 뷰를 화면 밖 칸에 붙여 측정·배치하고 비트맵에 한 번 그린다. keep=false 면 바로 떼어 버린다. 실패하면 false.
    /// 크기는 마지막 표 문서 크기, 아직 없으면 메인 화면 크기다(크게 잡으면 넘길 때 줄어드는 쪽이라 다시 측정이 싸다).
    /// </summary>
    internal static bool Warm(Control view, bool keep, out Border? slot)
    {
        slot = null;
        if (s_host is null)
        {
            return false;
        }

        var size = TargetSize();
        var s = new Border { Width = size.Width, Height = size.Height, Child = view };
        s_host.Children.Add(s);
        try
        {
            s.Measure(size);
            s.Arrange(new Rect(size));
            RenderOnce(s, size);
        }
        catch (Exception ex)
        {
            PdLog.Info(PdLogCategory.System, $"뷰 예열 그리기 실패: {ex.GetType().Name}: {ex.Message}"); // i18n-ok 진단 로그
        }

        if (!keep)
        {
            s.Child = null;
            s_host.Children.Remove(s);
            return true;
        }

        slot = s;
        return true;
    }

    /// <summary>준비된 표 뷰로 보관한다(예열 또는 다시 채우기에서).</summary>
    internal static void Keep(GridDocumentView view, Border slot)
    {
        Release();
        MaskAutomationIds(view, on: true);
        s_ready = view;
        s_readySlot = slot;
    }

    private const string PoolIdPrefix = "pool:";

    /// <summary>
    /// 보관 중에는 뷰 안 AutomationId 앞에 'pool:' 을 붙인다. 원격 진단·UI 자동화가 범위 없이 id(grid.dgMain 등)로 찾거나 트리 안 개수를 셀 때
    /// 화면 밖 준비 뷰를 실제 문서로 잡지 않게 한다(VisualTreeInspector.Find 는 트리 순서상 첫 번째를 고른다). 넘길 때 원래 id 로 되돌린다.
    /// </summary>
    private static void MaskAutomationIds(Control view, bool on)
    {
        foreach (var c in Avalonia.LogicalTree.LogicalExtensions.GetSelfAndLogicalDescendants(view))
        {
            if (c is not Control control || Avalonia.Automation.AutomationProperties.GetAutomationId(control) is not { Length: > 0 } id)
            {
                continue;
            }

            if (on && !id.StartsWith(PoolIdPrefix, StringComparison.Ordinal))
            {
                Avalonia.Automation.AutomationProperties.SetAutomationId(control, PoolIdPrefix + id);
            }
            else if (!on && id.StartsWith(PoolIdPrefix, StringComparison.Ordinal))
            {
                Avalonia.Automation.AutomationProperties.SetAutomationId(control, id[PoolIdPrefix.Length..]);
            }
        }
    }

    /// <summary>
    /// 준비된 표 뷰를 꺼낸다(DefaultDocumentViewFactory). 없거나 옵션이 꺼져 있으면 null — 호출 쪽이 일반 경로로 새로 만든다.
    /// 꺼낸 뒤 유휴 시간에 다음 1개를 다시 채우도록 예약한다(그 전 연속 열기는 일반 경로).
    /// </summary>
    public static GridDocumentView? TryTake()
    {
        if (s_ready is null)
        {
            return null;
        }

        if (!UiSettings.Current.GridViewPool)
        {
            Release();
            return null;
        }

        var view = s_ready;
        var slot = s_readySlot;
        s_ready = null;
        s_readySlot = null;
        if (slot is not null)
        {
            slot.Child = null;
            s_host?.Children.Remove(slot);
        }

        MaskAutomationIds(view, on: false);
        view.BeginOpenTracking();
        ScheduleRefill();
        return view;
    }

    /// <summary>보관 중인 준비 뷰를 버린다(옵션 끔·다시 채우기 교체).</summary>
    private static void Release()
    {
        if (s_readySlot is { } slot)
        {
            slot.Child = null;
            s_host?.Children.Remove(slot);
        }

        s_ready = null;
        s_readySlot = null;
    }

    /// <summary>예열 단계(DocumentPrewarm) 또는 다시 채우기에서 표 뷰 1개를 만들어 데우고, 옵션이 켜져 있으면 보관한다.</summary>
    internal static void BuildGrid()
    {
        var pool = UiSettings.Current.GridViewPool;
        GridDocumentView view;
        PerfProbe.SuppressOpenTrack = true;
        try
        {
            view = new GridDocumentView();
        }
        finally
        {
            PerfProbe.SuppressOpenTrack = false;
        }

        if (Warm(view, pool, out var slot) && pool && slot is not null)
        {
            Keep(view, slot);
        }
    }

    private static Size TargetSize()
    {
        if (s_lastSize.Width >= 100 && s_lastSize.Height >= 100)
        {
            return s_lastSize;
        }

        var b = s_host?.Parent is Visual p ? p.Bounds.Size : default;
        return b.Width >= 100 && b.Height >= 100 ? b : new Size(800, 600);
    }

    /// <summary>화면 밖 비트맵에 한 번 그린다(글자 모양·글리프 캐시). 화면에는 그리지 않는다.</summary>
    private static void RenderOnce(Control target, Size size)
    {
        var w = Math.Clamp((int)Math.Ceiling(size.Width), 1, 4096);
        var h = Math.Clamp((int)Math.Ceiling(size.Height), 1, 4096);
        using var bitmap = new RenderTargetBitmap(new PixelSize(w, h));
        bitmap.Render(target);
    }

    private static void HookInput()
    {
        const RoutingStrategies routes = RoutingStrategies.Tunnel | RoutingStrategies.Bubble;
        InputElement.PointerPressedEvent.AddClassHandler<TopLevel>((_, _) => MarkInput(), routes, handledEventsToo: true);
        InputElement.PointerMovedEvent.AddClassHandler<TopLevel>((_, _) => MarkInput(), routes, handledEventsToo: true);
        InputElement.PointerWheelChangedEvent.AddClassHandler<TopLevel>((_, _) => MarkInput(), routes, handledEventsToo: true);
        InputElement.KeyDownEvent.AddClassHandler<TopLevel>((_, _) => MarkInput(), routes, handledEventsToo: true);
    }

    private static void MarkInput() => s_lastInputMs = Environment.TickCount64;

    /// <summary>바쁨 스코프 없음 + 입력 없음이 이어지면 다음 준비 뷰를 만든다. Background 우선순위라 입력·렌더를 앞지르지 않는다.</summary>
    private static void ScheduleRefill()
    {
        if (s_refillScheduled || !UiSettings.Current.GridViewPool)
        {
            return;
        }

        s_refillScheduled = true;
        Poll(0);
    }

    private static void Poll(long quietSinceMs)
    {
        DispatcherTimer.RunOnce(() =>
        {
            if (!UiSettings.Current.GridViewPool || s_ready is not null || s_host is null)
            {
                s_refillScheduled = false;
                return;
            }

            var now = Environment.TickCount64;
            var quiet = RemoteCommands.StartupRestoreDone != false && BusyHooks.Service.Snapshot().Count == 0 && now - s_lastInputMs >= InputQuietMs;
            if (!quiet)
            {
                Poll(0);
                return;
            }

            var since = quietSinceMs == 0 ? now : quietSinceMs;
            if (now - since < IdleHoldMs)
            {
                Poll(since);
                return;
            }

            s_refillScheduled = false;
            try
            {
                BuildGrid();
            }
            catch (Exception ex)
            {
                PdLog.Info(PdLogCategory.System, $"준비된 표 뷰 채우기 실패: {ex.GetType().Name}: {ex.Message}"); // i18n-ok 진단 로그
            }
        }, TimeSpan.FromMilliseconds(PollMs), DispatcherPriority.Background);
    }
}
