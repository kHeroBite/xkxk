// 데스크톱 SDI 창 배치(스냅) 적용 부분 — 놓은 뒤 SetWindowPos 로 칸 적용·최대화, Snap Assist 진입, 배치 대상 문서 찾기(SdiSnapHook.cs 의 partial 분할, 로직 변경 없음)
using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Dock.Model.Core;
using xkxk.frameworks.ui.shell.Docking;
using xkxk.frameworks.ui.shell.Theming;

namespace xkxk.frameworks.ui.hosting.desktop;

/// <summary>SdiSnapHook 의 칸 적용·Snap Assist 진입 부분(partial). 설명은 SdiSnapHook.cs 에 있다.</summary>
internal sealed partial class SdiSnapHook
{
    private static void Apply(Window window, IntPtr hWnd, SnapZone zone, PixelRect work, double scale, string joinBefore)
    {
        var join = WindowTabJoinService.LastOutcome;
        if (!window.IsVisible || (!ReferenceEquals(join, joinBefore) && join.Contains("accepted=True", StringComparison.Ordinal)))
        {
            HostLog.Write($"sdi snap apply skipped '{window.Title}' visible={window.IsVisible} join={join}");
            return;
        }

        if (zone.Kind == SnapZoneKind.TopEdge)
        {
            window.WindowState = WindowState.Maximized; // 창이 속한 모니터 작업 영역 최대화(03 §5)
            HostLog.Write($"sdi snap apply '{window.Title}' maximize");
            return;
        }

        var t = zone.Target;
        int l = work.X + (int)Math.Round(t.X * scale), tp = work.Y + (int)Math.Round(t.Y * scale);
        int r = work.X + (int)Math.Round(t.Right * scale), b = work.Y + (int)Math.Round(t.Bottom * scale);
        // 칸 = 보이는 프레임(DWM). 창 사각형은 보이지 않는 테두리만큼 바깥으로 넓힌다(SdiMagnetHook 과 같은 기준).
        if (SdiBgPinHook.GetWindowRect(hWnd, out var win)
            && DwmGetWindowAttribute(hWnd, DwmwaExtendedFrameBounds, out var vis, Marshal.SizeOf<SdiBgPinHook.Rect32>()) == 0)
        {
            l -= vis.L - win.L;
            tp -= vis.T - win.T;
            r += win.R - vis.R;
            b += win.B - vis.B;
        }

        var ok = SetWindowPos(hWnd, IntPtr.Zero, l, tp, r - l, b - tp, SwpFlags);
        HostLog.Write($"sdi snap apply '{window.Title}' {zone.Kind} rect=({l},{tp},{r - l}x{b - tp}) ok={ok}");
        if (ok)
        {
            StartAssist(window, zone, work, scale);
        }
    }

    /// <summary>
    /// 칸 배치 성공 뒤 Snap Assist 진입(11 문서 §3.9, 계획 §5.3). SnapSdiMode 가 OS·끔이면 띄우지 않는다(끌기 시작 때 App 판정을 거쳤지만 놓기 사이 옵션이 바뀔 수 있다).
    /// 판정 사각형은 작업 영역 로컬 DIP 라 세션 기준 영역(작업 영역 ÷ 배율 DIP, 모니터 오프셋 포함) 좌표로 옮겨 넘긴다(가장 가까운 칸 판정용).
    /// 배치·활성화가 끝난 다음 프레임(Background)에 연다. 제외 판정(옵션·칸 종류·후보 0개 등)은 SnapAssistSession 이 한다(LastSkipReason).
    /// </summary>
    private static void StartAssist(Window window, SnapZone zone, PixelRect work, double scale)
    {
        if (xkxk.frameworks.ui.shell.Framework.XkxkUi.Current.Settings.SnapSdiMode is SnapSdiMode.Os or SnapSdiMode.Off)
        {
            HostLog.Write($"sdi snap assist skip '{window.Title}' mode={xkxk.frameworks.ui.shell.Framework.XkxkUi.Current.Settings.SnapSdiMode}");
            return;
        }

        var target = TargetOf(window);
        var shifted = zone with { Target = zone.Target.Translate(new Vector(work.X / scale, work.Y / scale)) };
        Dispatcher.UIThread.Post(() =>
        {
            if (!window.IsVisible)
            {
                return;
            }

            var started = SnapAssistSession.TryStartFromZone(target, shifted, SnapAssistScope.Sdi, window);
            HostLog.Write($"sdi snap assist '{window.Title}' zone={zone.Kind} started={started}{(started ? string.Empty : " skip=" + SnapAssistSession.LastSkipReason)}");
        }, DispatcherPriority.Background);
    }

    /// <summary>배치한 SDI 창의 대상. 활성 문서(없으면 첫 문서)로 만들고, 문서가 없으면 그 창(IDockWindow)으로 만든다(WindowMenuOps·SnapAssistCandidates 와 같은 Key 기준).</summary>
    private static WindowMenuTarget TargetOf(Window window)
    {
        if (window is IHostWindow { Window: { } dockWindow })
        {
            return dockWindow.Layout is { } root && FindDocument(root) is { } doc
                ? new WindowMenuTarget(WindowMenuKind.Sdi, doc, null)
                : new WindowMenuTarget(WindowMenuKind.Sdi, null, null, dockWindow);
        }

        return new WindowMenuTarget(WindowMenuKind.Sdi, null, null);
    }

    private static PocDocument? FindDocument(IDockable dockable)
    {
        if (dockable is PocDocument doc)
        {
            return doc;
        }

        if (dockable is not IDock dock)
        {
            return null;
        }

        if (dock.ActiveDockable is { } active && FindDocument(active) is { } found)
        {
            return found;
        }

        if (dock.VisibleDockables is { } children)
        {
            foreach (var child in children)
            {
                if (FindDocument(child) is { } d)
                {
                    return d;
                }
            }
        }

        return null;
    }
}
