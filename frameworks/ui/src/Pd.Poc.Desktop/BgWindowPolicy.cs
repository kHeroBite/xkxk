// 비전경(--bg-pos) 시험 기동의 별도 창 정책 — 시스템·Dock 오버레이 창을 창 안으로 돌리고(제품 코드), 진단판은 모든 새 창을 감시해 화면 안이면 화면 밖으로 옮긴다
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Dock.Settings;

namespace Pd.Poc.Desktop;

/// <summary>
/// lead 승인(10:5x) 떠 있는 창 대책.
/// (가) --bg-pos 일 때만 DockSettings.UseFloatingDockAdorner=false·UsePinnedDockWindow=false — Dock.Settings 12.1.0.12 문서상
///      DockAdornerWindow·PinnedDockWindow 는 'floating overlay' 를 쓸 때의 별도 창이다. 끄면 창 안에 그린다. 제품 기동(--bg-pos 없음)은 건드리지 않는다.
/// (나) 진단판 전용 안전망: Window.WindowOpenedEvent(Avalonia 12.1.4 문서 "global tracking of opening windows")로 모든 창이 열릴 때 잡아,
///      메인이 아닌 창이 실제 모니터와 겹치면 화면 밖으로 옮기고 'offscreen-window' 로그를 남긴다(DragPreviewWindow 처럼 위치가 계속 바뀌는 창은 PositionChanged 마다 다시 본다).
/// </summary>
public static class BgWindowPolicy
{
    /// <summary>주 창(감시 대상에서 제외 — 주 창 위치는 SdiWindowHost.ConfigureMain·OffscreenGuard 가 맡는다).</summary>
    public static Window? Main { get; set; }

    public static void Apply(bool background, bool overlayPopups, Action<string> log)
    {
        Pd.Poc.Shell.Platform.SystemDialogHost.BackgroundMode = background; // 시스템 창 비전경(odev-1r) — 테마 편집기도 이 값을 쓴다(odev-7c)
        if (background)
        {
            DockSettings.UseFloatingDockAdorner = false;
            DockSettings.UsePinnedDockWindow = false;
        }

        log($"bg-window-policy background={background} overlay-popups={overlayPopups} dock-floating-adorner={DockSettings.UseFloatingDockAdorner} dock-pinned-window={DockSettings.UsePinnedDockWindow}");
#if PD_DIAGNOSTICS
        if (background)
        {
            Window.WindowOpenedEvent.AddClassHandler(typeof(Window), (s, _) => Watch(s as Window, log));
            log("offscreen-window watch on");
        }
#endif
    }

#if PD_DIAGNOSTICS
    private static bool s_onscreenLogged;

    private static void Watch(Window? w, Action<string> log)
    {
        if (w is null || ReferenceEquals(w, Main))
        {
            return;
        }

        if (w is not SdiWindowHost)
        {
            SysWindowBgPinHook.Install(w, log); // 표시 전(이 이벤트는 PlatformImpl.Show 앞)에 화면 밖 배치·OS 보정 차단(D-SYSWIN-PRESHOW). SDI 는 SdiBgPinHook 이 맡는다
        }

        Push(w, log, "opened");
        w.PositionChanged += (_, _) => Push(w, log, "moved");
    }

    // 창 사각형(물리 px)이 실제 모니터와 겹치면 가장 오른쪽 모니터 바깥(+200)으로 옮긴다. 이미 화면 밖이면 그대로 둔다. 화면 안 예외 기동이면 옮기지 않는다.
    private static void Push(Window w, Action<string> log, string when)
    {
        var screens = w.Screens.All;
        if (screens.Count == 0)
        {
            return;
        }

        // 화면 안 예외 기동: 시험 창을 일부러 그 모니터에 띄운 것이므로 밀어내지 않는다. 화면 밖 기동(auto 또는 모니터 밖 x,y)은 아래 기존 보호를 그대로 쓴다.
        if (SdiWindowHost.IsOnscreenPlacement(w))
        {
            if (!s_onscreenLogged)
            {
                s_onscreenLogged = true;
                log($"offscreen-window push skipped: bg-pos ({SdiWindowHost.Placement?.X},{SdiWindowHost.Placement?.Y}) is on a real monitor (onscreen exception run)");
            }

            return;
        }

        var s = w.RenderScaling;
        var size = w.FrameSize ?? w.ClientSize;
        var rect = new PixelRect(w.Position, new PixelSize(Math.Max(1, (int)(size.Width * s)), Math.Max(1, (int)(size.Height * s))));
        if (!screens.Any(sc => sc.Bounds.Intersects(rect)))
        {
            return;
        }

        var target = new PixelPoint(screens.Max(sc => sc.Bounds.Right) + 200, rect.Y);
        log($"offscreen-window {w.GetType().Name} {when} ({rect.X},{rect.Y},{rect.Width}x{rect.Height})->({target.X},{target.Y})");
        w.Position = target;
    }
#endif
}
