// 비전경(--bg-pos) SDI 창 위치 고정 — WM_WINDOWPOSCHANGING 단계에서 실제 모니터와 겹치는 위치를 화면 밖 원점으로 바꿔, 표시·크기 변경 때 OS 보정으로 화면 안에 그려지지 않게 한다
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;

namespace Pd.Poc.Desktop;

/// <summary>
/// 원인(lead 12:5x, i1239 실측 'offscreen-window SdiWindowHost moved (1904,40,1920x1023)'): Avalonia Win32 는 표시·크기 변경을 SetWindowPlacement 로 하고,
/// Microsoft 문서(SetWindowPlacement Remarks)대로 결과 창이 화면 밖에 완전히 놓이면 시스템이 보이게 좌표를 고친다. 그래서 표시 직후 화면 안으로 끌려왔다.
/// 대응: 주 창 override-pin(OffscreenGuardHook, i0955~i1137 실측 노출 0)과 같은 방식으로, OS 가 창을 옮기기 전에 x,y 를 바꾼다.
/// bg 모드(--bg-pos)에서만 설치하므로 제품 일반 기동 동작은 그대로다. 창마다 1개(호스트 필드 초기화자, 실제 모니터 목록을 창별로 보관).
/// WindowPos·Rect32·GetWindowRect 는 주 창 bg pin(MainBgPinHook)도 같이 쓴다.
/// </summary>
internal sealed class SdiBgPinHook
{
    internal const uint WmWindowPosChanging = 0x0046; // SwpNoSize·SwpNoMove 는 SdiZOrderRaiser 의 정의를 같이 쓴다

    [StructLayout(LayoutKind.Sequential)]
    internal struct WindowPos
    {
        public IntPtr Hwnd, InsertAfter;
        public int X, Y, Cx, Cy;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect32
    {
        public int L, T, R, B;
    }

    [DllImport("user32.dll")]
    internal static extern bool GetWindowRect(IntPtr hWnd, out Rect32 rect);

    private SdiWindowHost? _host;
    private bool _opened;
    private PixelRect[] _realScreens = [];

    /// <summary>SdiWindowHost 생성자(비전경 모드)에서 호출 — 표시·크기 변경의 OS 화면 안 보정을 WM_WINDOWPOSCHANGING 단계에서 막는다.</summary>
    public void Install(SdiWindowHost host)
    {
        if (SdiWindowHost.Placement is null || !OperatingSystem.IsWindows())
        {
            return;
        }

        _host = host;
        host.WindowState = WindowState.Normal; // 복원 상태·최대화가 작업 영역(화면 안) 크기로 놓이지 않게
        host.Opened += (_, _) => _opened = true;
        Win32Properties.AddWndProcHookCallback(host, Hook);
    }

    private IntPtr Hook(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WmWindowPosChanging || lParam == IntPtr.Zero || _host is not { } host)
        {
            return IntPtr.Zero;
        }

        // 화면 안 예외 기동(진단): 첫 표시는 --bg-pos 모니터로 고정하고(터미널 모니터 노출 방지), 열린 뒤에는 재고정하지 않는다(이동마다 다음 계단 좌표로 밀리던 현상 방지)
        if (_opened && SdiWindowHost.IsOnscreenPlacement(host))
        {
            return IntPtr.Zero;
        }

        var wp = Marshal.PtrToStructure<WindowPos>(lParam);
        if ((wp.Flags & SdiZOrderRaiser.SwpNoMove) != 0 && (wp.Flags & SdiZOrderRaiser.SwpNoSize) != 0)
        {
            return IntPtr.Zero;
        }

        if (_realScreens.Length == 0 && host.Screens?.All is { Count: > 0 } all)
        {
            _realScreens = all.Select(s => s.Bounds).ToArray();
        }

        GetWindowRect(hWnd, out var cur);
        var x = (wp.Flags & SdiZOrderRaiser.SwpNoMove) != 0 ? cur.L : wp.X;
        var y = (wp.Flags & SdiZOrderRaiser.SwpNoMove) != 0 ? cur.T : wp.Y;
        var w = (wp.Flags & SdiZOrderRaiser.SwpNoSize) != 0 ? cur.R - cur.L : wp.Cx;
        var h = (wp.Flags & SdiZOrderRaiser.SwpNoSize) != 0 ? cur.B - cur.T : wp.Cy;
        var req = new PixelRect(x, y, Math.Max(1, w), Math.Max(1, h));
        // 모니터 정보를 아직 모르면(표시 전) 안전하게 언제나 화면 밖으로 둔다
        if (_realScreens.Length > 0 && !_realScreens.Any(s => s.Intersects(req)))
        {
            return IntPtr.Zero;
        }

        var target = Pd.Poc.Shell.Docking.PlacementRecovery.ForceInBackground && _realScreens.Length > 0 && !_realScreens.Any(s => s.Intersects(new PixelRect(cur.L, cur.T, Math.Max(1, cur.R - cur.L), Math.Max(1, cur.B - cur.T)))) ? new PixelPoint(cur.L, cur.T) : SdiWindowHost.ResolveOrigin(host, SdiWindowHost.OpenedCount + 1); // 진단 강제(요건 4 왕복 시험)만 현재 화면 밖 자리를 유지한다. 제품·일반 비전경은 값이 false 라 기존 원점
        if (target.X == x && target.Y == y)
        {
            return IntPtr.Zero;
        }

        Console.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [host] sdi bg pin ({x},{y},{w}x{h})->({target.X},{target.Y}) flags=0x{wp.Flags:X}");
        wp.X = target.X;
        wp.Y = target.Y;
        wp.Flags &= ~SdiZOrderRaiser.SwpNoMove; // 크기만 바꾸는 요청이어도 위치를 함께 적용한다
        Marshal.StructureToPtr(wp, lParam, false);
        return IntPtr.Zero;
    }
}
