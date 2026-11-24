// ⑤⑥ 모니터 맞춤 동안 메인 창의 최대 추적 크기(WM_GETMINMAXINFO ptMaxTrackSize)를 맞춤 크기까지 늘리는 훅
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;

namespace Pd.Poc.Desktop;

/// <summary>
/// 원인(Q6 #5 제목줄 off, i0352 실측): 창 높이가 OS 기본 최대 추적 크기(SM_CYMAXTRACK — 가상 화면 기준)에 잘려
/// 클라이언트가 1440 이 아니라 1426 이 된다(창 1465 = 가상 화면 높이 1445 + 테두리, 추정).
/// Microsoft 문서: MINMAXINFO.ptMaxTrackSize 는 가상 화면 크기 기준 기본값이며 WM_GETMINMAXINFO 처리로 바꿀 수 있다
/// (learn.microsoft.com/windows/win32/winmsg/wm-getminmaxinfo, .../api/winuser/ns-winuser-minmaxinfo, GetSystemMetrics SM_CYMAXTRACK).
/// 맞춤(⑤⑥) 상태일 때만 그 창 크기까지 허용하고, ④ 초기화 때 해제한다. Avalonia 의 자체 처리(Min/MaxWidth)는 훅 뒤에 그대로 돈다(handled=false).
/// </summary>
internal static class MaxTrackHook
{
    private const uint WmGetMinMaxInfo = 0x0024;
    private const uint WmExitSizeMove = 0x0232; // 사용자 이동·크기 조절 모달 루프 종료

    // 맞춤 중 허용할 창 사각형 크기(px, 보이지 않는 테두리 포함). null = OS 기본.
    private static PixelSize? s_fitMaxTrack;

    [StructLayout(LayoutKind.Sequential)]
    private struct Point32
    {
        public int X, Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public Point32 Reserved, MaxSize, MaxPosition, MinTrackSize, MaxTrackSize;
    }

    /// <summary>메인 창에 WM_GETMINMAXINFO 훅을 건다(Windows 만).</summary>
    public static void Install(Window main)
    {
        if (OperatingSystem.IsWindows())
        {
            Win32Properties.AddWndProcHookCallback(main, Hook);
        }
    }

    /// <summary>④ 초기화: 맞춤 해제 — 최대 추적 크기를 OS 기본으로 되돌린다.</summary>
    public static void Release() => s_fitMaxTrack = null;

    private static IntPtr Hook(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // 사용자가 테두리 끌기로 크기를 바꾸고 놓으면(WM_EXITSIZEMOVE) 맞춤 상태를 벗어난 것으로 보고 OS 기본 한도로 되돌린다(lead 04:2x).
        // 끌기 도중에는 늘린 한도를 유지한다 — 시작 때 풀면 맞춤 크기 창이 끌기 시작과 동시에 기본 한도로 줄어든다.
        if (msg == WmExitSizeMove && s_fitMaxTrack is not null)
        {
            s_fitMaxTrack = null;
            Program.Log("max-track released (user resize end)");
        }

        if (msg == WmGetMinMaxInfo && s_fitMaxTrack is { } need && lParam != IntPtr.Zero)
        {
            var info = Marshal.PtrToStructure<MinMaxInfo>(lParam);
            info.MaxTrackSize.X = Math.Max(info.MaxTrackSize.X, need.Width);
            info.MaxTrackSize.Y = Math.Max(info.MaxTrackSize.Y, need.Height);
            Marshal.StructureToPtr(info, lParam, false);
        }

        return IntPtr.Zero; // handled 는 false 그대로 — Avalonia·기본 처리가 이어서 돈다
    }

    /// <summary>맞춤 직전: 대상 클라이언트 크기 + 현재 비클라이언트 크기(+여유) 만큼 최대 추적 크기를 허용한다.</summary>
    public static void AllowFitTrack(Window main, PixelRect target, double scale)
    {
        if (!OperatingSystem.IsWindows() || main.TryGetPlatformHandle()?.Handle is not { } h || h == IntPtr.Zero || !WindowFrameMetrics.GetWindowRect(h, out var r))
        {
            return;
        }

        var ncW = (r.R - r.L) - (int)Math.Round(main.ClientSize.Width * scale);
        var ncH = (r.B - r.T) - (int)Math.Round(main.ClientSize.Height * scale);
        s_fitMaxTrack = new PixelSize(target.Width + Math.Max(0, ncW) + 8, target.Height + Math.Max(0, ncH) + 8);
    }
}
