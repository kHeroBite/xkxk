// 창 사각형·보이는 프레임(DWM)·비클라이언트 크기 측정과 보이는 프레임 맞춤·스냅 창 크기 보정 — DesktopWindowHost 에서 분리
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;

namespace xkxk.frameworks.ui.hosting.desktop;

/// <summary>⑤⑥ 맞춤에 쓰는 프레임 측정. GetWindowRect·Rect32 는 DesktopWindowHost 의 MinMax·OffscreenGuard 도 함께 쓴다(internal).</summary>
internal static class WindowFrameMetrics
{
    private const int DwmwaExtendedFrameBounds = 9; // 보이는 프레임(그림자·보이지 않는 크기 조절 테두리 제외)

    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect32
    {
        public int L, T, R, B;
    }

    // 바깥 틀(제목줄·테두리) − 클라이언트 크기(DIP). FrameSize 를 모르면 0.
    public static (double W, double H) FrameExtra(Window w) =>
        w.FrameSize is { } f ? (Math.Max(0, f.Width - w.ClientSize.Width), Math.Max(0, f.Height - w.ClientSize.Height)) : (0, 0);

    /// <summary>보이는 프레임을 target 에 맞춘다. 실측: 창 사각형(GetWindowRect)·보이는 프레임(DWM)·클라이언트 크기. 결과 문자열은 로그용(실패 시 사유).</summary>
    public static string FitVisibleFrame(Window main, PixelRect target, double scale)
    {
        if (!OperatingSystem.IsWindows() || main.TryGetPlatformHandle()?.Handle is not { } h || h == IntPtr.Zero)
        {
            return "visible=skipped(no handle)";
        }

        if (!TryFrames(h, out var win, out var vis))
        {
            return "visible=skipped(DWM query failed)";
        }

        // 여백(px): 창 사각형이 보이는 프레임보다 바깥으로 나온 양. 클라이언트↔창 사각형 차이는 비클라이언트 크기.
        int left = vis.L - win.L, top = vis.T - win.T, right = win.R - vis.R, bottom = win.B - vis.B;
        var clientPxW = (int)Math.Round(main.ClientSize.Width * scale);
        var clientPxH = (int)Math.Round(main.ClientSize.Height * scale);
        int ncW = (win.R - win.L) - clientPxW, ncH = (win.B - win.T) - clientPxH;
        var winW = target.Width + left + right;
        var winH = target.Height + top + bottom;
        main.Position = new PixelPoint(target.X - left, target.Y - top);
        main.Width = Math.Max(200, (winW - ncW) / scale);
        main.Height = Math.Max(150, (winH - ncH) / scale);
        main.UpdateLayout();

        var after = TryFrames(h, out _, out var vis2) ? $"({vis2.L},{vis2.T})-({vis2.R},{vis2.B})" : "query-failed";
        return $"margin=L{left}/T{top}/R{right}/B{bottom} nc={ncW}x{ncH} visible ({vis.L},{vis.T})-({vis.R},{vis.B})→{after}";
    }

    /// <summary>
    /// ④⑤⑥ 크기 보정(Win32 전용): Avalonia 12.1.4 Win32 WindowImpl.Resize 는 요청 창 크기가 WINDOWPLACEMENT 복원 사각형(NormalPosition)과 같으면
    /// 아무것도 하지 않는다. OS 스냅(Aero Snap) 창은 실제 크기(예 960x1152)와 복원 사각형(스냅 전 1416x908)이 달라, 기본 크기로 되돌리는
    /// Width/Height 지정이 무시되고 위치만 옮겨졌다(otest-12 i2410fg). 실제 클라이언트가 요청과 다르면 SetWindowPos(NOMOVE)로 직접 맞춘다.
    /// 반환은 로그용(보정 없으면 빈 문자열). 스냅 창에서의 실효는 시험 확인 항목(추정).
    /// </summary>
    public static string EnsureClientSize(Window w, double dipW, double dipH)
    {
        if (!OperatingSystem.IsWindows() || w.WindowState != WindowState.Normal || w.TryGetPlatformHandle()?.Handle is not { } h || h == IntPtr.Zero
            || !GetClientRect(h, out var c) || !GetWindowRect(h, out var r))
        {
            return "";
        }

        int cw = (int)(dipW * w.RenderScaling), ch = (int)(dipH * w.RenderScaling); // Avalonia Resize 와 같은 환산(절사)
        int curW = c.R - c.L, curH = c.B - c.T;
        if (curW == cw && curH == ch)
        {
            return "";
        }

        SetWindowPos(h, IntPtr.Zero, 0, 0, cw + (r.R - r.L) - curW, ch + (r.B - r.T) - curH, SwpNoMove | SwpNoZOrder | SwpNoActivate);
        w.UpdateLayout();
        return $" size-fix client {curW}x{curH}→{cw}x{ch}px";
    }

    private const uint SwpNoMove = 0x0002, SwpNoZOrder = 0x0004, SwpNoActivate = 0x0010;
    private const int SwShowNoActivate = 4, SwRestore = 9;
    private static bool s_noArrangedApi; // IsWindowArranged 미제공(Windows 10 1903 미만) — 한 번 실패하면 다시 찾지 않는다

    /// <summary>
    /// ④⑤⑥ 앞: OS 스냅(배열) 상태면 먼저 복원한다. Microsoft Learn(IsWindowArranged): 배열은 최대화·최소화와 배타적인 별도 상태이고
    /// 배열 창도 GetWindowPlacement showCmd 가 SW_SHOWNORMAL 로 나온다 → Avalonia 는 Normal 로 보고 크기 변경을 건너뛰었다(otest-12).
    /// IsWindowArranged 는 가져오기 라이브러리가 없어 user32 에서 런타임에 찾는다(없으면 건너뜀). 활성 창은 SW_RESTORE,
    /// 비활성 창은 전경을 뺏지 않게 SW_SHOWNOACTIVATE(배열 해제 실효는 시험 확인 항목). EnsureClientSize 는 안전망으로 남긴다.
    /// </summary>
    public static void RestoreIfArranged(Window w)
    {
        if (!OperatingSystem.IsWindows() || s_noArrangedApi || w.TryGetPlatformHandle()?.Handle is not { } h || h == IntPtr.Zero)
        {
            return;
        }

        try
        {
            if (!IsWindowArranged(h))
            {
                return;
            }
        }
        catch (EntryPointNotFoundException)
        {
            s_noArrangedApi = true;
            return;
        }

        GetWindowRect(h, out var before);
        var cmd = w.IsActive ? SwRestore : SwShowNoActivate;
        ShowWindow(h, cmd);
        GetWindowRect(h, out var after);
        HostLog.Write($"arranged-restore cmd={(cmd == SwRestore ? "SW_RESTORE" : "SW_SHOWNOACTIVATE")} ({before.L},{before.T},{before.R - before.L}x{before.B - before.T})→({after.L},{after.T},{after.R - after.L}x{after.B - after.T}) arranged={IsWindowArranged(h)}");
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowArranged(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    /// <summary>보이는 프레임(DWM, 물리 px). 값을 못 얻으면 null.</summary>
    public static PixelRect? VisibleFrame(Window w) =>
        OperatingSystem.IsWindows() && w.TryGetPlatformHandle()?.Handle is { } h && h != IntPtr.Zero && TryFrames(h, out _, out var v)
            ? new PixelRect(v.L, v.T, v.R - v.L, v.B - v.T)
            : null;

    private static bool TryFrames(IntPtr h, out Rect32 window, out Rect32 visible)
    {
        visible = default;
        if (!GetWindowRect(h, out window))
        {
            return false;
        }

        return DwmGetWindowAttribute(h, DwmwaExtendedFrameBounds, out visible, Marshal.SizeOf<Rect32>()) == 0;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetWindowRect(IntPtr hWnd, out Rect32 rect);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(IntPtr hWnd, out Rect32 rect);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hWnd, int attribute, out Rect32 value, int size);
}
