// 진단 전용 — --primary-override 위치 고정(SetWindowPlacement 화면 안 보정 되돌림)과 --offscreen-guard(실제 모니터와 겹치는 이동·크기 변경 차단·로그). 제품 빌드에서는 호출째 빠진다(Conditional)
using Avalonia;
using Avalonia.Controls;
#if PD_DIAGNOSTICS
using System.Runtime.InteropServices;
using Avalonia.Threading;
#endif

namespace Pd.Poc.Desktop;

/// <summary>
/// 원인(05:07 실측 + 소스): Avalonia Win32 WindowImpl.Resize 는 크기를 SetWindowPlacement 로 바꾼다.
/// Microsoft 문서(SetWindowPlacement Remarks): 결과 창이 화면 밖에 완전히 놓이면 시스템이 보이도록 좌표를 자동 조정한다.
/// → 화면 밖 창의 크기가 바뀌는 ⑤ single·두 번째 ④ 에서만 X 가 0 으로 끌려왔다(크기 불변인 첫 ④·⑥ 은 화면 밖 유지).
/// 앱 자신의 보정(MDI-12 EnsureVisible·알림 패널 비켜 놓기)은 메인 창에 돌지 않았다(--bg-pos 경로, 로그에 MDI-12 check main 없음).
/// 대응: ① override 중 맞춤·초기화 동안 WM_WINDOWPOSCHANGING 의 x,y 를 의도 좌표로 되돌린다(Pin).
///       ② --offscreen-guard on: 실제 모니터 Bounds 와 조금이라도 겹치는 이동·크기 변경은 SWP_NOMOVE|SWP_NOSIZE 로 막고 요청 좌표를 로그로 남긴다(창은 그 자리에 머문다).
/// 옛 DesktopWindowHost 의 partial void 훅(PinBegin·PinEnd·InstallGuard)을 Conditional 메서드로 바꾼 것이다(P4). 켜는 값은 DesktopWindowHost.OffscreenGuard·PrimaryOverride 다.
/// </summary>
internal static class OffscreenGuardHook
{
#if PD_DIAGNOSTICS
    private const uint WmWindowPosChanging = 0x0046;
    private const uint SwpNoSize = 0x0001, SwpNoMove = 0x0002;

    private static PixelPoint? s_pin;
    private static PixelRect[] s_realScreens = [];

    /// <summary>override 기동 구간(창 생성 ~ 첫 Opened + 크기 확정 3초): 실제 모니터로 끌려오는 이동을 현재 좌표로 되돌린다(i0944 Run1 blocked 2건 대책).</summary>
    private static bool s_startupPin;
    private static readonly TimeSpan StartupPinSettle = TimeSpan.FromSeconds(3);

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowPos
    {
        public IntPtr Hwnd, InsertAfter;
        public int X, Y, Cx, Cy;
        public uint Flags;
    }
#endif

    [System.Diagnostics.Conditional("PD_DIAGNOSTICS")]
    public static void PinBegin(PixelPoint position)
    {
#if PD_DIAGNOSTICS
        s_pin = DesktopWindowHost.PrimaryOverride is null ? null : position;
#endif
    }

    [System.Diagnostics.Conditional("PD_DIAGNOSTICS")]
    public static void PinEnd()
    {
#if PD_DIAGNOSTICS
        s_pin = null;
#endif
    }

    [System.Diagnostics.Conditional("PD_DIAGNOSTICS")]
    public static void InstallGuard(Window window)
    {
#if PD_DIAGNOSTICS
        if (!OperatingSystem.IsWindows() || (!DesktopWindowHost.OffscreenGuard && DesktopWindowHost.PrimaryOverride is null))
        {
            return;
        }

        if (s_realScreens.Length == 0 && window.Screens?.All is { Count: > 0 } all)
        {
            s_realScreens = all.Select(s => s.Bounds).ToArray(); // 실제 모니터(override 와 무관)
        }

        Win32Properties.AddWndProcHookCallback(window, GuardHook);

        if (DesktopWindowHost.PrimaryOverride is not null && window == DesktopWindowHost.MainWindow && !s_startupPin)
        {
            s_startupPin = true; // 기동 때 1400x900 크기 변경(SetWindowPlacement)이 X 를 0 으로 끌어오는 경로 — ④⑤⑥ 의 PinBegin 범위 밖
            Program.Log("override-pin startup begin");
            window.Opened += (_, _) => DispatcherTimer.RunOnce(() =>
            {
                s_startupPin = false;
                Program.Log("override-pin startup end");
            }, StartupPinSettle);
        }
#endif
    }

#if PD_DIAGNOSTICS
    private static IntPtr GuardHook(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WmWindowPosChanging || lParam == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        var main = DesktopWindowHost.MainWindow;
        if (s_realScreens.Length == 0 && main?.Screens?.All is { Count: > 0 } all)
        {
            s_realScreens = all.Select(s => s.Bounds).ToArray(); // 설치 시점(창 표시 전)에 비어 있으면 첫 메시지 때 채운다
        }

        var wp = Marshal.PtrToStructure<WindowPos>(lParam);
        if ((wp.Flags & (SwpNoMove | SwpNoSize)) == (SwpNoMove | SwpNoSize) || !WindowFrameMetrics.GetWindowRect(hWnd, out var cur))
        {
            return IntPtr.Zero;
        }

        var changed = false;
        if (s_pin is { } pin && hWnd == (main?.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero) && (wp.Flags & SwpNoMove) == 0 && (wp.X != pin.X || wp.Y != pin.Y))
        {
            Program.Log($"override-pin ({wp.X},{wp.Y})->({pin.X},{pin.Y}) size={wp.Cx}x{wp.Cy}");
            wp.X = pin.X;
            wp.Y = pin.Y;
            changed = true;
        }
        else if (s_startupPin && hWnd == (main?.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero) && (wp.Flags & SwpNoMove) == 0)
        {
            var sw = (wp.Flags & SwpNoSize) != 0 ? cur.R - cur.L : wp.Cx;
            var sh = (wp.Flags & SwpNoSize) != 0 ? cur.B - cur.T : wp.Cy;
            if (s_realScreens.Any(s => s.Intersects(new PixelRect(wp.X, wp.Y, Math.Max(1, sw), Math.Max(1, sh)))))
            {
                Program.Log($"override-pin startup ({wp.X},{wp.Y})->({cur.L},{cur.T}) size={sw}x{sh}"); // 화면 밖으로 가는 정상 이동(ResolveOrigin)은 그대로 둔다
                wp.X = cur.L;
                wp.Y = cur.T;
                changed = true;
            }
        }

        if (DesktopWindowHost.OffscreenGuard)
        {
            var x = (wp.Flags & SwpNoMove) != 0 ? cur.L : wp.X;
            var y = (wp.Flags & SwpNoMove) != 0 ? cur.T : wp.Y;
            var w = (wp.Flags & SwpNoSize) != 0 ? cur.R - cur.L : wp.Cx;
            var h = (wp.Flags & SwpNoSize) != 0 ? cur.B - cur.T : wp.Cy;
            var req = new PixelRect(x, y, Math.Max(1, w), Math.Max(1, h));
            if (s_realScreens.Any(s => s.Intersects(req)))
            {
                Program.Log($"offscreen-guard blocked hwnd=0x{hWnd:X} req=({x},{y},{w}x{h}) flags=0x{wp.Flags:X}");
                wp.Flags |= SwpNoMove | SwpNoSize;
                changed = true;
            }
        }

        if (changed)
        {
            Marshal.StructureToPtr(wp, lParam, false);
        }

        return IntPtr.Zero; // handled=false — Avalonia·기본 처리는 고친 WINDOWPOS 로 이어서 돈다
    }
#endif
}
