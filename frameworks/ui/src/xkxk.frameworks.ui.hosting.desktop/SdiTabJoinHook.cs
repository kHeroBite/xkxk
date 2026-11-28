// 데스크톱 SDI 창 탭 합류 원천 — OS 이동 루프(WM_ENTERSIZEMOVE·WM_MOVING·WM_EXITSIZEMOVE)에서 커서(GetCursorPos)를 화면 DIP 로 바꿔 WindowTabJoinService 에 넘긴다(Win32 전용)
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using xkxk.frameworks.ui.shell.Docking;

namespace xkxk.frameworks.ui.hosting.desktop;

/// <summary>
/// SdiTitleBar 끌기는 Window.BeginMoveDrag(OS 이동 루프)라 앱 포인터 이벤트가 오지 않는다(SdiMagnetHook 과 같은 사정). 그래서 이동 루프 메시지에서 커서를 읽는다.
/// - 시작: WM_ENTERSIZEMOVE 때 커서·창 사각형을 기억하고, 첫 WM_MOVING 에서 서비스 Begin(크기 조절 WM_SIZING 이면 시작하지 않는다).
/// - 좌표: 화면 물리 px ÷ 시작 때 창 배율. 서비스가 표적 사각형도 같은 나눗수로 바꾸므로 포함 판정이 정확하다(모니터를 넘어도 같은 기준).
/// - Z: 시작 때 OS 창 순서(GetTopWindow→GW_HWNDNEXT)로 등록된 SDI 창의 순위를 1회 계산한다(위가 큰 값 — 가림 판정).
/// - 끝: WM_EXITSIZEMOVE. 창 사각형이 시작과 같으면(Esc 취소로 제자리 복귀) 합류하지 않는다.
/// 설치: SdiTitleBar 가 서비스에 등록될 때 알림(SurfaceRegistered)을 받아 그 OS 창에 1회 붙인다 — 등록 지점(SdiWindowHost)이 배정 밖이라 모듈 초기화로 구독한다.
/// WM_MOVING 처리는 표적 고르기(목록 몇 개)와 머무름 판정만 한다 — 표적 모으기는 서비스가 끌기 판정 순간 1회만 한다.
/// </summary>
internal sealed class SdiTabJoinHook
{
    private const uint WmSizing = 0x0214;
    private const uint WmMoving = 0x0216;
    private const uint WmEnterSizeMove = 0x0231;
    private const uint WmExitSizeMove = 0x0232;
    private const uint GwHwndNext = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct Point32
    {
        public int X, Y;
    }

    private static readonly ConditionalWeakTable<Window, SdiTabJoinHook> s_installed = new();

    private readonly Window _window;
    private bool _entered;
    private bool _began;
    private double _scale = 1;
    private Point32 _startCursor;
    private SdiBgPinHook.Rect32 _startRect;

    private SdiTabJoinHook(Window window) => _window = window;

#pragma warning disable CA2255 // 등록 지점(SdiWindowHost·Program)이 배정 밖이라 어셈블리 적재 때 구독한다(MdiTabGroupDiag·TourInputGate 와 같은 방식)
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Init()
    {
        if (OperatingSystem.IsWindows())
        {
            WindowTabJoinService.SurfaceRegistered += OnSurfaceRegistered;
        }
    }

    private static void OnSurfaceRegistered(IWindowTabJoinSurface surface)
    {
        if (surface.Frame is not Window window || s_installed.TryGetValue(window, out _))
        {
            return;
        }

        var hook = new SdiTabJoinHook(window);
        s_installed.Add(window, hook);
        Win32Properties.AddWndProcHookCallback(window, hook.Hook);
    }

    private IntPtr Hook(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case WmEnterSizeMove:
                _began = false;
                _entered = GetCursorPos(out _startCursor) && SdiBgPinHook.GetWindowRect(hWnd, out _startRect);
                break;
            case WmSizing:
                _entered = false; // 크기 조절 루프 — 합류 대상 아님
                break;
            case WmMoving when _entered:
                if (!_began)
                {
                    Begin();
                }

                if (_began && GetCursorPos(out var p))
                {
                    WindowTabJoinService.Update(ToDip(p));
                }

                break;
            case WmExitSizeMove:
                End(hWnd);
                break;
        }

        return IntPtr.Zero;
    }

    private void Begin()
    {
        _entered = false;
        if (_window.WindowState != WindowState.Normal || WindowTabJoinService.SurfaceOf(_window) is not { } source)
        {
            return;
        }

        _scale = _window.RenderScaling > 0 ? _window.RenderScaling : 1;
        var ranks = ZRanks();
        WindowTabJoinService.Begin(source, ToDip(_startCursor), _scale, null, s => ranks.TryGetValue(s, out var z) ? z : -1);
        _began = WindowTabJoinService.IsActive;
        _entered = _began;
    }

    private void End(IntPtr hWnd)
    {
        var began = _began;
        _entered = false;
        _began = false;
        if (!began)
        {
            return;
        }

        var moved = !SdiBgPinHook.GetWindowRect(hWnd, out var now)
            || now.L != _startRect.L || now.T != _startRect.T || now.R != _startRect.R || now.B != _startRect.B;
        WindowTabJoinService.End(GetCursorPos(out var p) ? ToDip(p) : null, commit: moved);
        HostLog.Write($"sdi tabjoin end '{_window.Title}' moved={moved} {WindowTabJoinService.LastOutcome}");
    }

    private Point ToDip(Point32 p) => new(p.X / _scale, p.Y / _scale);

    /// <summary>등록된 SDI 창의 OS Z 순위(아래 0 → 위 큰 값). 창 핸들이 없거나 순서에서 못 찾은 창은 빠진다(서비스가 -1 로 본다).</summary>
    private static Dictionary<IWindowTabJoinSurface, int> ZRanks()
    {
        var byHandle = new Dictionary<IntPtr, IWindowTabJoinSurface>();
        foreach (var s in WindowTabJoinService.Surfaces)
        {
            if (s.Frame is Window w && w.TryGetPlatformHandle()?.Handle is { } h && h != IntPtr.Zero)
            {
                byHandle[h] = s;
            }
        }

        var topDown = new List<IWindowTabJoinSurface>();
        var guard = 0;
        for (var h = GetTopWindow(IntPtr.Zero); h != IntPtr.Zero && guard < 20000 && topDown.Count < byHandle.Count; h = GetWindow(h, GwHwndNext), guard++)
        {
            if (byHandle.TryGetValue(h, out var s))
            {
                topDown.Add(s);
            }
        }

        var ranks = new Dictionary<IWindowTabJoinSurface, int>();
        for (var i = 0; i < topDown.Count; i++)
        {
            ranks[topDown[i]] = topDown.Count - 1 - i;
        }

        return ranks;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out Point32 point);

    [DllImport("user32.dll")]
    private static extern IntPtr GetTopWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hWnd, uint cmd);
}
