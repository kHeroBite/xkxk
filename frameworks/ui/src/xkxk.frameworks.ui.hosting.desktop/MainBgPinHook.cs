// 비전경(--bg-pos) 주 창 위치 고정 — 창이 사는 동안 WM_WINDOWPOSCHANGING 에서 실제 모니터와 겹치는 이동을 화면 밖 원점으로 바꿔 화면 노출을 막는다(최대화·최소화 제외)
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace xkxk.frameworks.ui.hosting.desktop;

/// <summary>
/// 원인(lead 14:1x, i1350·i1402 --offscreen-guard 실측 'blocked req=(1144,0,1416x908) flags=0x14' 2회, 둘 다 Opened 전):
/// ConfigureMain 이 화면 밖 원점을 둔 뒤 Avalonia Win32 가 크기를 SetWindowPlacement 로 적용하고, Microsoft 문서(SetWindowPlacement Remarks)대로
/// 결과 창이 화면 밖에 완전히 놓이면 시스템이 보이도록 좌표를 고친다(주 모니터 오른쪽 끝 = 2560-1416). layout.json 복원과는 무관하다.
/// 대응(lead 안 A): SDI 의 bg pin(SdiBgPinHook)과 같은 방식으로, 주 창 기동 구간 동안 OS 가 옮기기 전에 x,y 를 화면 밖 원점으로 바꾼다.
/// bg 모드(--bg-pos)에서만 설치하므로 제품 일반 기동 동작은 그대로다.
/// 기동 구간 뒤에도 Normal 창은 고정을 유지한다: 3초 뒤 풀던 때 window.resize(Width 변경)가 같은 경로로 창을 (0,484) 작업 영역 안에 끌어왔다(odev-fix-viewmode 16:15 실측).
/// 기동 구간 뒤 최대화·최소화 상태의 이동은 그대로 둔다(최대화 시험의 동작 불변 — 실제 모니터 최대화는 여전히 노출되므로 그런 시험은 --primary-override 를 쓴다).
/// 진단 빌드는 기동 구간의 WM_WINDOWPOSCHANGED(실제 배치)를 x,y,cx,cy·flags(SWP_SHOWWINDOW)·IsWindowVisible·모니터 겹침과 함께 남긴다(검증용).
/// </summary>
internal static class MainBgPinHook
{
    private static readonly TimeSpan MainBgPinSettle = TimeSpan.FromSeconds(3);
    private static bool s_mainBgPin;
    private static bool s_mainBgSettled;
    private static PixelRect[] s_mainScreens = [];
    private static Window? s_mainBgWindow;

    /// <summary>SdiWindowHost.ConfigureMain(비전경 모드)에서 호출 — 기동 구간(생성~Opened+3초) OS 화면 안 보정 차단.</summary>
    public static void Install(Window main)
    {
        if (SdiWindowHost.Placement is null || !OperatingSystem.IsWindows())
        {
            return;
        }

        s_mainBgWindow = main;
        s_mainBgPin = true;
        Win32Properties.AddWndProcHookCallback(main, Hook);
        ApplyNoActivate(main, "install");
        main.Opened += (_, _) =>
        {
            ApplyNoActivate(main, "opened");
            DispatcherTimer.RunOnce(() =>
            {
                s_mainBgSettled = true;
                Console.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [host] main bg pin settled (normal window stays pinned)");
                ApplyNoActivate(main, "pin-end");
            }, MainBgPinSettle);
        };
    }

    // 화면 밖 기동이 사용자의 전경을 가져가지 않게 WS_EX_NOACTIVATE 를 켠다(안 B). SetForegroundWindow 직접 호출은 막지 못한다. Avalonia 가 덮어쓰는지 단계마다 값을 남긴다.
    private static void ApplyNoActivate(Window main, string stage)
    {
        if (main.TryGetPlatformHandle()?.Handle is not { } hwnd)
        {
            return;
        }

        var style = GetWindowLongPtr(hwnd, GwlExStyle);
        var had = (style.ToInt64() & WsExNoActivate) != 0;
        if (!had)
        {
            SetWindowLongPtr(hwnd, GwlExStyle, new IntPtr(style.ToInt64() | WsExNoActivate));
        }

        Console.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [host] main bg exstyle {stage} before=0x{style.ToInt64():X} after=0x{GetWindowLongPtr(hwnd, GwlExStyle).ToInt64():X} hadNoActivate={had} showInTaskbar={main.ShowInTaskbar}");
    }

    private const int GwlExStyle = -20;
    private const long WsExNoActivate = 0x08000000;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int index, IntPtr value);

    private const uint WmStyleChanging = 0x007C;

    // bg 핀 동안 확장 스타일 변경 요청(WM_STYLECHANGING, wParam=GWL_EXSTYLE)의 STYLESTRUCT.styleNew 에 WS_EX_NOACTIVATE 를 다시 넣는다.
    // 원인: Avalonia 가 opened 전에 확장 스타일을 덮어써 ApplyNoActivate 로 켠 비트가 사라졌다(retest_noactivate_observe H_OVERWRITE).
    // 출처(Microsoft Learn, 2026-11-09 확인): https://learn.microsoft.com/windows/win32/winmsg/wm-stylechanging —
    //   "Sent to a window when the SetWindowLong function is about to change one or more of the window's styles."
    //   lParam: "A pointer to a STYLESTRUCT structure that contains the proposed new styles for the window. An application can examine the styles and, if necessary, change them."
    //   반환: "An application should return zero if it processes this message."
    //   STYLESTRUCT(https://learn.microsoft.com/windows/win32/api/winuser/ns-winuser-stylestruct) = { DWORD styleOld; DWORD styleNew; }.
    // 추정(문서에 명시 없음): 바꾼 styleNew 가 그대로 적용된다는 점, SetWindowLongPtr 경로에서도 같은 메시지가 온다는 점 — 앱 로그 exstyle 단계값으로 확인한다.
    private static void KeepNoActivate(IntPtr wParam, IntPtr lParam)
    {
        if (unchecked((int)wParam.ToInt64()) != GwlExStyle)
        {
            return;
        }

        var styleNew = unchecked((uint)Marshal.ReadInt32(lParam, 4));
        if ((styleNew & WsExNoActivate) != 0)
        {
            return;
        }

        var styleOld = unchecked((uint)Marshal.ReadInt32(lParam, 0));
        var kept = styleNew | (uint)WsExNoActivate;
        Marshal.WriteInt32(lParam, 4, unchecked((int)kept));
        Console.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [host] main bg stylechanging keep NOACTIVATE old=0x{styleOld:X} new=0x{styleNew:X} kept=0x{kept:X}");
    }

    private static IntPtr Hook(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (!s_mainBgPin || lParam == IntPtr.Zero || s_mainBgWindow is not { } main)
        {
            return IntPtr.Zero;
        }

        if (s_mainScreens.Length == 0 && main.Screens?.All is { Count: > 0 } all)
        {
            s_mainScreens = all.Select(s => s.Bounds).ToArray();
        }

        if (msg == WmStyleChanging)
        {
            KeepNoActivate(wParam, lParam);
            return IntPtr.Zero;
        }

#if PD_DIAGNOSTICS
        if (msg == WmWindowPosChanged)
        {
            if (!s_mainBgSettled)
            {
                LogMainPosChanged(hWnd, lParam);
            }

            return IntPtr.Zero;
        }
#endif
        if (msg != SdiBgPinHook.WmWindowPosChanging)
        {
            return IntPtr.Zero;
        }

        if (s_mainBgSettled && (SdiShadowNative.IsZoomed(hWnd) || SdiShadowNative.IsIconic(hWnd)))
        {
            return IntPtr.Zero;
        }

        var wp = Marshal.PtrToStructure<SdiBgPinHook.WindowPos>(lParam);
        if ((wp.Flags & SdiZOrderRaiser.SwpNoMove) != 0 && (wp.Flags & SdiZOrderRaiser.SwpNoSize) != 0)
        {
            return IntPtr.Zero;
        }

        SdiBgPinHook.GetWindowRect(hWnd, out var cur);
        var x = (wp.Flags & SdiZOrderRaiser.SwpNoMove) != 0 ? cur.L : wp.X;
        var y = (wp.Flags & SdiZOrderRaiser.SwpNoMove) != 0 ? cur.T : wp.Y;
        var w = (wp.Flags & SdiZOrderRaiser.SwpNoSize) != 0 ? cur.R - cur.L : wp.Cx;
        var h = (wp.Flags & SdiZOrderRaiser.SwpNoSize) != 0 ? cur.B - cur.T : wp.Cy;
        var req = new PixelRect(x, y, Math.Max(1, w), Math.Max(1, h));
        // 모니터 정보를 아직 모르면(표시 전) 안전하게 언제나 화면 밖 원점으로 둔다
        if (s_mainScreens.Length > 0 && !s_mainScreens.Any(s => s.Intersects(req)))
        {
            return IntPtr.Zero;
        }

        var target = SdiWindowHost.ResolveOrigin(main, 0);
        if (target.X == x && target.Y == y)
        {
            return IntPtr.Zero;
        }

        Console.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [host] main bg pin ({x},{y},{w}x{h})->({target.X},{target.Y}) flags=0x{wp.Flags:X}");
        wp.X = target.X;
        wp.Y = target.Y;
        wp.Flags &= ~SdiZOrderRaiser.SwpNoMove; // 크기만 바꾸는 요청이어도 위치를 함께 적용한다
        Marshal.StructureToPtr(wp, lParam, false);
        return IntPtr.Zero;
    }

#if PD_DIAGNOSTICS
    private const uint WmWindowPosChanged = 0x0047;
    private const uint SwpShowWindow = 0x0040;

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    // 실제 배치(WM_WINDOWPOSCHANGED) 기록 — onscreen=1 이면 실제 모니터와 겹친 좌표에 놓인 것(검증 PASS 조건: 기동 구간 0건)
    private static void LogMainPosChanged(IntPtr hWnd, IntPtr lParam)
    {
        var wp = Marshal.PtrToStructure<SdiBgPinHook.WindowPos>(lParam);
        SdiBgPinHook.GetWindowRect(hWnd, out var cur);
        var rect = new PixelRect(cur.L, cur.T, Math.Max(1, cur.R - cur.L), Math.Max(1, cur.B - cur.T));
        var onscreen = s_mainScreens.Any(s => s.Intersects(rect)) ? 1 : 0;
        Console.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [host] main poschanged x={wp.X} y={wp.Y} cx={wp.Cx} cy={wp.Cy} flags=0x{wp.Flags:X} show={((wp.Flags & SwpShowWindow) != 0 ? 1 : 0)} visible={(IsWindowVisible(hWnd) ? 1 : 0)} rect=({rect.X},{rect.Y},{rect.Width}x{rect.Height}) onscreen={onscreen} screens={s_mainScreens.Length}");
    }
#endif
}
