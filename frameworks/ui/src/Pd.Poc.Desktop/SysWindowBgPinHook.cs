// 진단 전용 — 비전경(--bg-pos) 시험에서 시스템 창(About 등)·테마 편집기 같은 일반 창을 표시 전에 화면 밖에 두고, OS 화면 안 보정을 WM_WINDOWPOSCHANGING 단계에서 되돌린다(D-SYSWIN-PRESHOW)
#if PD_DIAGNOSTICS
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;

namespace Pd.Poc.Desktop;

/// <summary>
/// 원인(odev-fix, w14 stdio 229행 'offscreen-window Window moved (1384,40,1440x843)->(2120,40)'):
/// Avalonia 12.1.4 Window.ShowCore 는 WindowOpenedEvent 를 PlatformImpl.Show 전에 낸다. 감시기(BgWindowPolicy.Watch)는 그때 화면 밖 좌표를 보고 통과시키고,
/// 표시 순간 Win32 SetWindowPlacement 가 화면 밖 창을 작업 영역 안으로 보정해 사용자 화면에 먼저 그려졌다(SdiBgPinHook 과 같은 원인).
/// 대응: WindowOpenedEvent(표시 전)에 창별 훅을 걸어, 실제 모니터와 겹치는 위치 요청을 화면 밖(가장 오른쪽 모니터 끝 + 200)으로 바꾼다.
/// 주 창(MainBgPinHook)·SDI 창(SdiBgPinHook)은 자기 훅이 있어 대상이 아니다. 진단판 비전경 모드에서만 BgWindowPolicy 가 부른다.
/// </summary>
internal sealed class SysWindowBgPinHook
{
    private readonly Window _window;
    private readonly Action<string> _log;
    private readonly PixelRect[] _realScreens;
    private readonly PixelPoint _target;

    private SysWindowBgPinHook(Window window, Action<string> log, PixelRect[] realScreens)
    {
        _window = window;
        _log = log;
        _realScreens = realScreens;
        _target = new PixelPoint(realScreens.Max(s => s.Right) + 200, realScreens.Min(s => s.Y) + 40);
    }

    /// <summary>표시 전(WindowOpenedEvent)에 1회 호출. 위치를 화면 밖으로 먼저 옮기고 OS 보정 차단 훅을 건다.</summary>
    public static void Install(Window window, Action<string> log)
    {
        if (!OperatingSystem.IsWindows() || window.Screens?.All is not { Count: > 0 } all)
        {
            return;
        }

        var hook = new SysWindowBgPinHook(window, log, all.Select(s => s.Bounds).ToArray());
        window.WindowStartupLocation = WindowStartupLocation.Manual; // 가운데 배치(CenterOwner)가 화면 안 좌표를 계산하지 않게
        window.Position = hook._target;
        log($"offscreen-window {window.GetType().Name} pre-show pin ->({hook._target.X},{hook._target.Y})");
        Win32Properties.AddWndProcHookCallback(window, hook.Hook);
    }

    private IntPtr Hook(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != SdiBgPinHook.WmWindowPosChanging || lParam == IntPtr.Zero)
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
        if (!_realScreens.Any(s => s.Intersects(req)))
        {
            return IntPtr.Zero;
        }

        _log($"offscreen-window {_window.GetType().Name} sys bg pin ({x},{y},{w}x{h})->({_target.X},{_target.Y}) flags=0x{wp.Flags:X}");
        wp.X = _target.X;
        wp.Y = _target.Y;
        wp.Flags &= ~SdiZOrderRaiser.SwpNoMove; // 크기만 바꾸는 요청이어도 위치를 함께 적용한다
        Marshal.StructureToPtr(wp, lParam, false);
        return IntPtr.Zero;
    }
}
#endif
