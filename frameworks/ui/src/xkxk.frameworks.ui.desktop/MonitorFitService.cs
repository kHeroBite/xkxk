// 최대화 모니터 서비스 — 메인·SDI 의 일반 최대화는 OS 가 소속 모니터(겹침 면적 최대)에 하게 두고, '창 ▸ 멀티 모니터' 를 명시로 쓸 때만 고른 모니터로 옮긴다(U20)
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using xkxk.frameworks.ui.shell;
using xkxk.frameworks.ui.shell.Docking;
using xkxk.frameworks.ui.shell.Framework;
using xkxk.frameworks.ui.shell.Theming;
using xkxk.frameworks.ui.shell.ViewModels;

namespace xkxk.frameworks.ui.desktop;

/// <summary>
/// 일반 최대화(제목줄·OS 더블클릭·단축키 — 메인·SDI 공통): 창을 옮기지 않는다. OS 가 겹침 면적이 가장 큰 모니터의 작업 영역에 최대화한다(옛 동작은 메인을 고른 모니터·주 모니터로 끌고 갔다, 사용자 지시 2026-11-04).
/// 명시 동작(메뉴 '선택한 모니터로 최대화'·최대화 중 라디오 변경 — 메인만): 고른 모니터로 옮긴다. Windows 는 SetWindowPlacement(복원 사각형을 옮기고 SW_SHOWMAXIMIZED) 1회로,
/// 그 밖의 OS 는 Normal → 위치 이동 → Maximized 로. 선택이 없거나 저장된 모니터가 없어졌으면 주 모니터(MonitorSelection.Resolve).
/// 진단 가상 모니터(DesktopMonitorProvider.IsVirtual)는 OS 가 최대화할 수 없으므로 Normal 상태로 작업 영역에 맞춰 흉내 내고 로그에 virtual 로 남긴다. 일반 최대화의 대상은 MonitorSelection.LargestOverlap 이다.
/// </summary>
internal static class MonitorFitService
{
    private const int SwShowMaximized = 3;

    private static Window? s_main;
    private static bool s_applying;

    /// <summary>Program.ConfigureHost 등록 1줄: 메뉴 제공자 등록 + 메인 창 생성 때 연결(호스트 값 ConfigureMainWindow 뒤에 이어 붙임, B03).</summary>
    public static void Register(UiHostOptions host)
    {
#if PD_DIAGNOSTICS
        // 진단 전용 가상 모니터 수(환경변수 — 호스트 인자는 셸 StartupArgs 로 넘어가 자동 저장을 끄므로 쓰지 않는다). --primary-override 와 함께여야 켜진다.
        if (int.TryParse(Environment.GetEnvironmentVariable("PD_VIRTUAL_MONITORS"), out var vc) && vc >= 2)
        {
            DesktopMonitorProvider.VirtualCount = vc;
            Program.Log($"virtual-monitors {vc} override={DesktopWindowHost.PrimaryOverride}");
        }
#endif
        MonitorMenuViewModel.Provider = DesktopMonitorProvider.Instance;
        var previous = host.ConfigureMainWindow;
        host.ConfigureMainWindow = w =>
        {
            previous?.Invoke(w);
            Attach(w);
        };
    }

    private static void Attach(Window main)
    {
        s_main = main;
        DesktopMonitorProvider.Instance.Attach(main);
        main.Opened += (_, _) => DesktopMonitorProvider.Instance.Attach(main); // 표시 전에는 Screens 가 비어 있을 수 있다 — 열린 뒤 목록 다시 알림
        TrackMaximize(main, "main");
        UiSettings.Current.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(UiSettings.SelectedMonitorId) && main.WindowState == WindowState.Maximized)
            {
                Apply(main, "select"); // 최대화 중에 모니터를 바꾸면 바로 옮긴다
            }
        };
    }

    /// <summary>메인·SDI 공통 일반 최대화 경로: 옮기지 않고, OS 결과와 겹침 면적 판정을 로그로 남긴다(가상 모니터 메인만 흉내 냄).</summary>
    public static void TrackMaximize(Window window, string kind)
    {
        window.PropertyChanged += (_, e) =>
        {
            if (e.Property == Window.WindowStateProperty && e.GetNewValue<WindowState>() == WindowState.Maximized && !s_applying)
            {
                Dispatcher.UIThread.Post(() => OnMaximized(window, kind)); // OS 최대화가 끝난 뒤
            }
        };
    }

    private static void OnMaximized(Window window, string kind)
    {
        var normal = NormalRect(window);
        var owner = MonitorSelection.LargestOverlap(DesktopMonitorProvider.Instance.GetMonitors(), normal);
        if (owner is null)
        {
            Program.Log($"monitor-max {kind} 실패: 모니터 정보 없음");
            return;
        }

        var head = $"monitor-max {kind} owner={owner.Id} work={Rect(owner.WorkingArea)} normal={Rect(normal)}";
        if (DesktopMonitorProvider.IsVirtual && kind == "main")
        {
            s_applying = true;
            try
            {
                EmulateVirtual(window, owner.WorkingArea, head);
            }
            finally
            {
                s_applying = false;
            }

            return;
        }

        Program.Log($"{head} window={WindowRect(window)}");
    }

    /// <summary>IWindowHostCommands MaximizeOnSelectedMonitor 위임(DesktopWindowHost 1줄): 최대화가 아니면 최대화한 뒤 고른 모니터로 맞춘다. 사용자가 고른 명시 동작이다.</summary>
    public static bool MaximizeOnSelected(Window main)
    {
        if (main.WindowState != WindowState.Maximized && !DesktopMonitorProvider.IsVirtual)
        {
            s_applying = true; // 일반 최대화 처리가 뒤따르지 않게
            try
            {
                main.WindowState = WindowState.Maximized;
            }
            finally
            {
                s_applying = false;
            }
        }

        return Apply(main, "command");
    }

    private static bool Apply(Window main, string tag)
    {
        var monitors = DesktopMonitorProvider.Instance.GetMonitors();
        if (MonitorSelection.Resolve(monitors, UiSettings.Current.SelectedMonitorId, out var fallback) is not { } target)
        {
            Program.Log($"monitor-max {tag} 실패: 모니터 정보 없음");
            return false;
        }

        var head = $"monitor-max {tag} selected={UiSettings.Current.SelectedMonitorId ?? "(none)"} target={target.Id} fallback={fallback} work={Rect(target.WorkingArea)}";
        s_applying = true;
        try
        {
            if (DesktopMonitorProvider.IsVirtual)
            {
                return EmulateVirtual(main, target.WorkingArea, head);
            }

            var current = main.Screens.ScreenFromWindow(main);
            if (current is null || current.Bounds == target.Bounds)
            {
                Program.Log($"{head} moved=false(same monitor) window={WindowRect(main)}");
                return true;
            }

            var moved = OperatingSystem.IsWindows() && PlaceMaximized(main, current.WorkingArea, target.WorkingArea);
            var via = "placement";
            if (!moved || main.Screens.ScreenFromWindow(main)?.Bounds != target.Bounds)
            {
                moved = MoveThenMaximize(main, current.WorkingArea, target.WorkingArea); // 배치 API 가 없거나(비 Windows) 옮겨지지 않았으면 Normal→이동→최대화
                via = "normal-move";
            }

            Program.Log($"{head} from={Rect(current.Bounds)} moved={moved} via={via} window={WindowRect(main)}");
            return moved;
        }
        catch (Exception ex)
        {
            Program.Log($"{head} 예외: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
        finally
        {
            s_applying = false;
        }
    }

    // Windows: 복원 사각형(rcNormalPosition, 작업 공간 좌표)을 같은 상대 위치로 선택 모니터 작업 영역에 옮기고 최대화 상태로 한 번에 배치한다(깜박임 없음).
    private static bool PlaceMaximized(Window main, PixelRect from, PixelRect to)
    {
        if (main.TryGetPlatformHandle()?.Handle is not { } hwnd || hwnd == IntPtr.Zero)
        {
            return false;
        }

        var wp = new WindowPlacementData { Length = (uint)Marshal.SizeOf<WindowPlacementData>() };
        if (!GetWindowPlacement(hwnd, ref wp))
        {
            return false;
        }

        var off = WorkspaceOffset(main);
        var normal = new PixelRect(wp.Normal.L + off.X, wp.Normal.T + off.Y, wp.Normal.R - wp.Normal.L, wp.Normal.B - wp.Normal.T);
        var placed = MonitorSelection.Relocate(normal, from, to);
        wp.Normal = new Rect32 { L = placed.X - off.X, T = placed.Y - off.Y, R = placed.Right - off.X, B = placed.Bottom - off.Y };
        wp.ShowCmd = SwShowMaximized;
        wp.Flags = 0;
        return SetWindowPlacement(hwnd, ref wp);
    }

    // 작업 공간 좌표 = 화면 좌표 − (주 모니터 작업 영역 − 주 모니터 전체) 원점 차이(작업표시줄이 위·왼쪽일 때만 0 이 아님)
    private static PixelPoint WorkspaceOffset(Window window) =>
        window.Screens.Primary is { } primary ? new PixelPoint(primary.WorkingArea.X - primary.Bounds.X, primary.WorkingArea.Y - primary.Bounds.Y) : default;

    // 최대화 전 보통 상태 사각형(물리 px, 화면 좌표). 최대화 뒤에도 OS 가 복원 사각형을 들고 있다. 그 값을 못 얻는 OS 는 현재 위치·크기.
    private static PixelRect NormalRect(Window window)
    {
        if (OperatingSystem.IsWindows() && window.TryGetPlatformHandle()?.Handle is { } hwnd && hwnd != IntPtr.Zero)
        {
            var wp = new WindowPlacementData { Length = (uint)Marshal.SizeOf<WindowPlacementData>() };
            if (GetWindowPlacement(hwnd, ref wp))
            {
                var off = WorkspaceOffset(window);
                return new PixelRect(wp.Normal.L + off.X, wp.Normal.T + off.Y, wp.Normal.R - wp.Normal.L, wp.Normal.B - wp.Normal.T);
            }
        }

        return new PixelRect(window.Position, PixelSize.FromSize(window.FrameSize ?? window.ClientSize, window.DesktopScaling));
    }

    // Windows 외: Normal 로 돌려 선택 모니터로 옮긴 뒤 다시 최대화한다.
    private static bool MoveThenMaximize(Window main, PixelRect from, PixelRect to)
    {
        main.WindowState = WindowState.Normal;
        var size = PixelSize.FromSize(main.FrameSize ?? main.ClientSize, main.DesktopScaling);
        main.Position = MonitorSelection.Relocate(new PixelRect(main.Position, size), from, to).Position;
        main.WindowState = WindowState.Maximized;
        return true;
    }

    // 진단 가상 모니터: Normal 상태로 클라이언트 = 작업 영역(최대화 창과 같은 크기·위치)에 맞춘다. 상태가 Normal 로 남는 점은 로그에 명시한다.
    private static bool EmulateVirtual(Window main, PixelRect work, string head)
    {
        main.WindowState = WindowState.Normal;
        var scale = main.DesktopScaling;
        main.Position = work.Position;
        OffscreenGuardHook.PinBegin(main.Position); // 화면 밖 크기 변경이 실제 모니터로 끌려오지 않게(진단 override)
        MaxTrackHook.AllowFitTrack(main, work, scale);
        main.Width = work.Width / scale;
        main.Height = work.Height / scale;
        main.UpdateLayout();
        OffscreenGuardHook.PinEnd();
        Program.Log($"{head} moved=virtual-emulated state={main.WindowState} window={WindowRect(main)} client={main.ClientSize.Width * scale:F0}x{main.ClientSize.Height * scale:F0}px");
        return true;
    }

    private static string Rect(PixelRect r) => $"{r.X},{r.Y},{r.Width}x{r.Height}";

    private static string WindowRect(Window w) => WindowFrameMetrics.GetWindowRect(w.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero, out var r)
        ? $"{r.L},{r.T},{r.R - r.L}x{r.B - r.T} state={w.WindowState}"
        : $"pos=({w.Position.X},{w.Position.Y}) state={w.WindowState}";

    [StructLayout(LayoutKind.Sequential)]
    private struct Point32
    {
        public int X, Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect32
    {
        public int L, T, R, B;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowPlacementData
    {
        public uint Length, Flags, ShowCmd;
        public Point32 MinPosition, MaxPosition;
        public Rect32 Normal;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowPlacement(IntPtr hWnd, ref WindowPlacementData lpwndpl);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPlacement(IntPtr hWnd, ref WindowPlacementData lpwndpl);
}
