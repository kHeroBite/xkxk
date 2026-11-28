// 데스크톱 SDI 창 자석(UI-12 ③) — OS 이동·크기 조절 루프의 WM_MOVING·WM_SIZING 에서 보이는 프레임(DWM)을 다른 SDI 창·메인 창·모니터 작업영역 가장자리에 붙인다(Win32 전용)
using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using xkxk.frameworks.ui.shell.Diagnostics;
using xkxk.frameworks.ui.shell.Theming;
using xkxk.frameworks.ui.shell.Framework;

namespace xkxk.frameworks.ui.hosting.desktop;

/// <summary>
/// 원인(odev-dnd 분석, otest-12 i2410fg Q8 FAIL): SdiTitleBar 는 Window.BeginMoveDrag(OS 이동 루프)를 쓰는데, Dock HostWindow 자석은
/// 자체 끌기(TryBeginMoveDrag)로 켜지는 _mouseDown 일 때만 돈다. 그래서 EnableWindowMagnetism 이 켜져 있어도 SDI 는 붙지 않았다.
/// 대응(B안, team-lead): OS 이동 루프가 보내는 제안 사각형(WM_MOVING 이동, WM_SIZING 크기 조절)을 고친다. 계산·이력은 SdiMagnetSnapper.
/// - 기준: 보이는 프레임(DWMWA_EXTENDED_FRAME_BOUNDS). 끌기 시작(WM_ENTERSIZEMOVE) 때 테두리 폭·후보(다른 SDI·메인 Normal 창, 모니터 작업영역)를 1회 캐시한다.
/// - raw(붙이지 않았을 위치)는 OS 제안이 아니라 '시작 사각형 + 커서 이동량'으로 구한다. OS 는 직전에 고친 사각형에 이동량을 더해 다음 제안을 만들므로,
///   제안을 그대로 쓰면 한 번 붙은 뒤 계속 붙어 떼어낼 수 없었다(w4 B-2 FAIL, 60px 끌어도 snaps=24).
/// - 켜기·범위: UiSettings.EffectiveMagnetRange(꺼짐·0 이면 그대로) × 창 배율(px). 위치 고정 창은 SdiTitleBar 가 이동을 시작하지 않는다.
/// - 놓은 위치 = 마지막 WM_MOVING 결과라 끄는 중 보인 위치와 같다.
/// 한계: 끌기 중 배율이 다른 모니터로 넘어가면 테두리 폭은 시작 때 값을 쓴다. 도킹(Dock 안내·재도킹)과는 무관한 경로다.
/// </summary>
internal sealed class SdiMagnetHook
{
    private const uint WmSizing = 0x0214;
    private const uint WmMoving = 0x0216;
    private const uint WmEnterSizeMove = 0x0231;
    private const uint WmExitSizeMove = 0x0232;
    private const int DwmwaExtendedFrameBounds = 9;

    [StructLayout(LayoutKind.Sequential)]
    private struct Point32
    {
        public int X, Y;
    }

    private static readonly List<SdiMagnetHook> s_hooks = new();

    private Window? _window;
    private SdiMagnetSnapper? _snapper;
    private SdiBgPinHook.Rect32 _inset; // 보이는 프레임 - 창 사각형(각 변의 차이)
    private SdiBgPinHook.Rect32 _start; // 끌기 시작 때 창 사각형
    private Point32 _startCursor;
    private int _snaps;
    private double _maxMs;

    /// <summary>SdiWindowHost 생성자에서 호출. Windows 가 아니면 아무것도 하지 않는다.</summary>
    public void Install(Window window)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        _window = window;
        s_hooks.Add(this);
        window.Closed += (_, _) => s_hooks.Remove(this);
        Win32Properties.AddWndProcHookCallback(window, Hook);
    }

    private IntPtr Hook(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case WmEnterSizeMove:
                Begin(hWnd);
                break;
            case WmExitSizeMove:
                End();
                break;
            case WmMoving when _snapper is not null && lParam != IntPtr.Zero:
                Snap(lParam, 0);
                break;
            case WmSizing when _snapper is not null && lParam != IntPtr.Zero:
                Snap(lParam, (int)wParam);
                break;
        }

        return IntPtr.Zero;
    }

    // 무거운 조회(DWM 프레임·화면 목록·커서)와 첫 호출 비용(JIT·P/Invoke 스텁)은 여기서 끝낸다 — WM_MOVING 은 계산만 한다(w4 첫 끌기 maxMs 1.216).
    private void Begin(IntPtr hWnd)
    {
        _snapper = null;
        _snaps = 0;
        _maxMs = 0;
        var range = XkxkUi.Current.Settings.EffectiveMagnetRange;
        if (_window is not { WindowState: WindowState.Normal } w || range <= 0 || !TryFrames(hWnd, out var win, out var vis)
            || w.Screens?.All is not { Count: > 0 } screens || !GetCursorPos(out _startCursor))
        {
            return;
        }

        _start = win;
        _inset = new SdiBgPinHook.Rect32 { L = vis.L - win.L, T = vis.T - win.T, R = vis.R - win.R, B = vis.B - win.B };
        var others = new List<Rect>();
        foreach (var hook in s_hooks)
        {
            if (hook != this && hook._window is { } other)
            {
                AddTarget(others, other);
            }
        }

        // DesktopWindowHost.MainWindow 는 진단 빌드 전용이라 제품판에서도 있는 앱 수명 주기의 메인 창을 쓴다(w2 제품판 CS0117).
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } main })
        {
            AddTarget(others, main);
        }

        var works = screens.Select(s => new Rect(s.WorkingArea.X, s.WorkingArea.Y, s.WorkingArea.Width, s.WorkingArea.Height)).ToArray();
        var snapper = new SdiMagnetSnapper(works, others, range * w.RenderScaling);
        // 예열: 계산 경로와 구조체 마샬링을 한 번 돌려 첫 WM_MOVING 에 JIT 비용이 실리지 않게 한다.
        snapper.Apply(ToRect(vis), 0);
        snapper.Apply(ToRect(vis), 8);
        snapper.Reset();
        var tmp = Marshal.AllocHGlobal(Marshal.SizeOf<SdiBgPinHook.Rect32>());
        try
        {
            Marshal.StructureToPtr(win, tmp, false);
            _ = Marshal.PtrToStructure<SdiBgPinHook.Rect32>(tmp);
        }
        finally
        {
            Marshal.FreeHGlobal(tmp);
        }

        _ = Stopwatch.GetElapsedTime(Stopwatch.GetTimestamp());
        _snapper = snapper;
    }

    private void End()
    {
        if (_snapper is not { } snapper)
        {
            return;
        }

        _snapper = null;
        Console.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [host] sdi magnet end '{_window?.Title}' snaps={_snaps} targets={snapper.TargetCount} release=x{SdiMagnetSnapper.ReleaseFactor} maxMs={_maxMs:F3}");
    }

    /// <summary>edge 0 = 이동(창 전체), 1~8 = WMSZ_* 크기 조절 가장자리(끄는 변만 붙음).</summary>
    private void Snap(IntPtr lParam, int edge)
    {
        var t0 = Stopwatch.GetTimestamp();
        if (_snapper is not { } snapper || !GetCursorPos(out var p))
        {
            return;
        }

        var req = Marshal.PtrToStructure<SdiBgPinHook.Rect32>(lParam);
        int dx = p.X - _startCursor.X, dy = p.Y - _startCursor.Y;
        // raw: 이동은 시작 사각형 + 커서 이동량. 크기 조절은 끄는 변만 그렇게, 나머지 변은 OS 제안(최소 크기 등 OS 보정 유지).
        bool left = edge is 0 or 1 or 4 or 7, top = edge is 0 or 3 or 4 or 5, right = edge is 0 or 2 or 5 or 8, bottom = edge is 0 or 6 or 7 or 8;
        var raw = new SdiBgPinHook.Rect32
        {
            L = left ? _start.L + dx : req.L,
            T = top ? _start.T + dy : req.T,
            R = right ? _start.R + dx : req.R,
            B = bottom ? _start.B + dy : req.B,
        };
        var rawVis = new Rect(raw.L + _inset.L, raw.T + _inset.T, (raw.R + _inset.R) - (raw.L + _inset.L), (raw.B + _inset.B) - (raw.T + _inset.T));
        var s = snapper.Apply(rawVis, edge);
        var dl = (int)Math.Round(s.Left - rawVis.Left);
        var dt = (int)Math.Round(s.Top - rawVis.Top);
        var dr = (int)Math.Round(s.Right - rawVis.Right);
        var db = (int)Math.Round(s.Bottom - rawVis.Bottom);
        if ((dl | dt | dr | db) != 0)
        {
            _snaps++;
        }

        var outRect = new SdiBgPinHook.Rect32 { L = raw.L + dl, T = raw.T + dt, R = raw.R + dr, B = raw.B + db };
        if (outRect.L != req.L || outRect.T != req.T || outRect.R != req.R || outRect.B != req.B)
        {
            Marshal.StructureToPtr(outRect, lParam, false); // 직전 보정이 OS 제안에 누적돼 있어도 raw 기준으로 덮어쓴다
        }

        var ms = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
        _maxMs = Math.Max(_maxMs, ms);
        PerfProbe.Current.RecordOperation(edge == 0 ? "ui.magnet.sdi.drag" : "ui.magnet.sdi.resize", ms, ms);
    }

    private static void AddTarget(List<Rect> list, Window w)
    {
        if (w.IsVisible && w.WindowState == WindowState.Normal && w.TryGetPlatformHandle()?.Handle is { } h && h != IntPtr.Zero
            && TryFrames(h, out _, out var vis))
        {
            list.Add(ToRect(vis));
        }
    }

    private static Rect ToRect(SdiBgPinHook.Rect32 r) => new(r.L, r.T, r.R - r.L, r.B - r.T);

    private static bool TryFrames(IntPtr h, out SdiBgPinHook.Rect32 window, out SdiBgPinHook.Rect32 visible)
    {
        visible = default;
        return SdiBgPinHook.GetWindowRect(h, out window)
            && DwmGetWindowAttribute(h, DwmwaExtendedFrameBounds, out visible, Marshal.SizeOf<SdiBgPinHook.Rect32>()) == 0;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hWnd, int attribute, out SdiBgPinHook.Rect32 value, int size);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out Point32 point);
}
