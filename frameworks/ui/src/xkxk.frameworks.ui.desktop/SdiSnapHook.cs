// 데스크톱 SDI 창 배치(스냅) — OS 이동 루프(WM_ENTERSIZEMOVE·WM_MOVING·WM_EXITSIZEMOVE)에서 커서를 모니터 작업 영역 로컬 DIP 로 바꿔 SnapZoneContext 로 판정하고, 놓을 때 SetWindowPos 로 칸을 적용한다(Win32 전용, 11_창배치_스냅레이아웃.md §3·§5·§7.3)
using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Dock.Model.Core;
using xkxk.frameworks.ui.shell.Docking;
using xkxk.frameworks.ui.shell.Theming;

namespace xkxk.frameworks.ui.desktop;

/// <summary>
/// SdiTitleBar 끌기는 Window.BeginMoveDrag(OS 이동 루프)라 앱 포인터 이벤트가 오지 않는다 — SdiTabJoinHook·SdiMagnetHook 과 같은 이동 루프 메시지 방식이다.
/// - 담당: 끌기 시작 때 1회 UiSettings.EffectiveSdiOwner(OsSnapProbe.IsOsSnapOn()) 가 App 일 때만 동작한다. WM_SIZING(크기 조절)이면 시작하지 않는다.
/// - 기준 영역: 커서가 있는 모니터 작업 영역, 문맥 좌표는 작업 영역 로컬 DIP(현재 모니터 배율). 모니터를 넘으면 문맥·막대 창을 다시 만든다(11 문서 §3.7).
/// - 순서(11 문서 §5·§7.3): 끄는 중 위치 보정은 SdiMagnetHook(WM_MOVING lParam)만 한다. 이 훅은 lParam 을 고치지 않고 WM_EXITSIZEMOVE 뒤(Post)
///   SetWindowPos 로 스냅 사각형을 적용한다(스냅이 자석 결과를 대신). 위쪽 가장자리(TopEdge)는 최대화한다.
/// - 끝: 창 사각형이 시작과 같으면(Esc 복귀) 적용하지 않는다. 같은 끌기의 탭 합류가 받아들여졌으면(WindowTabJoinService.LastOutcome) 적용하지 않는다.
/// 한계(확실하지 않음): 끄는 중 탭 합치기 표적 위인지 알 공개 API 가 없어 <see cref="OverTabJoinTarget"/> 이음매로 받는다(미연결이면 false).
///   막대 지연 타이머가 OS 이동 루프 중에도 도는지는 실기 확인 대상이라 WM_MOVING 마다도 경과를 본다. 스냅 전 크기 복원(§3.6)은 자석 보정과 겹쳐 넣지 않았다.
/// 설치: SdiWindowHost 생성자에서 _magnet.Install 다음에 <see cref="Install"/>(Wave 3 odev-9). 창 Closed 때 자동 해제, 명시 해제는 <see cref="Uninstall"/>.
/// </summary>
internal sealed class SdiSnapHook
{
    private const uint WmSizing = 0x0214;
    private const uint WmMoving = 0x0216;
    private const uint WmEnterSizeMove = 0x0231;
    private const uint WmExitSizeMove = 0x0232;
    private const int DwmwaExtendedFrameBounds = 9;
    private const uint SwpFlags = 0x0004 | 0x0010 | 0x0200; // SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOOWNERZORDER

    [StructLayout(LayoutKind.Sequential)]
    private struct Point32 { public int X, Y; }

    private Window? _window;
    private bool _installed, _hooked, _entered, _active;
    private SdiBgPinHook.Rect32 _startRect;
    private SnapZoneContext _ctx = SnapZoneContext.Disabled;
    private PixelRect _monitor, _work;
    private double _scale = 1;
    private Point _last;
    private SnapZone _zone = SnapZone.None;
    private long _barSince = -1;
    private string _joinBefore = string.Empty;
    private DispatcherTimer? _barTimer;

    /// <summary>끄는 중 탭 합치기 표적 위인가(true 면 막대·미리보기를 숨기고 적용하지 않음 — 11 문서 §5 ①). null(기본, 미연결)이면 false.</summary>
    public static Func<bool>? OverTabJoinTarget { get; set; }

    /// <summary>SdiWindowHost 생성자에서 1회 호출(자석 훅 다음). Windows 가 아니면 아무것도 하지 않는다. 같은 인스턴스로 두 번 불러도 한 번만 붙는다.</summary>
    public void Install(Window window)
    {
        if (!OperatingSystem.IsWindows() || _installed)
        {
            return;
        }

        _window = window;
        _installed = true;
        if (!_hooked)
        {
            _hooked = true; // Uninstall 뒤 다시 Install 해도 콜백은 하나만 붙는다
            window.Closed += (_, _) => Uninstall();
            Win32Properties.AddWndProcHookCallback(window, Hook);
        }
    }

    /// <summary>스냅 처리를 멈춘다(메시지 훅 콜백은 남지만 바로 돌아간다). 진행 중인 막대·미리보기는 닫는다.</summary>
    public void Uninstall()
    {
        _installed = false;
        Cancel();
        _window = null;
    }

    private IntPtr Hook(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (!_installed) { return IntPtr.Zero; }

        switch (msg)
        {
            case WmEnterSizeMove:
                Cancel();
                _entered = GetCursorPos(out _) && SdiBgPinHook.GetWindowRect(hWnd, out _startRect);
                break;
            case WmSizing:
                Cancel(); // 크기 조절 루프 — 스냅 대상 아님(이동 끌기만)
                break;
            case WmMoving when _entered:
                Begin();
                break;
            case WmMoving when _active && GetCursorPos(out var p):
                Update(p);
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
        if (_window is not { WindowState: WindowState.Normal } || !GetCursorPos(out var p))
        {
            return;
        }

        var owner = xkxk.frameworks.ui.shell.Framework.XkxkUi.Current.Settings.EffectiveSdiOwner(OsSnapProbe.IsOsSnapOn());
        if (owner != SnapSdiOwner.App)
        {
            Program.Log($"sdi snap skip '{_window.Title}' owner={owner} mode={xkxk.frameworks.ui.shell.Framework.XkxkUi.Current.Settings.SnapSdiMode} {OsSnapProbe.Describe()}");
            return;
        }

        _joinBefore = WindowTabJoinService.LastOutcome;
        (_monitor, _work) = (default, default);
        _active = true;
        Update(p);
    }

    private void Update(Point32 cursor)
    {
        if (!Retarget(cursor)) { return; }

        _last = new Point((cursor.X - _work.X) / _scale, (cursor.Y - _work.Y) / _scale);
        Resolve();
    }

    /// <summary>커서가 있는 모니터가 바뀌었으면 그 작업 영역·배율로 문맥과 막대 창을 다시 만든다. 모니터를 못 찾으면 false(이번 이동은 건너뜀).</summary>
    private bool Retarget(Point32 cursor)
    {
        var pt = new PixelPoint(cursor.X, cursor.Y);
        if (_work.Width > 0 && _monitor.Contains(pt))
        {
            return true;
        }

        if (_window is not { } w || w.Screens.ScreenFromPoint(pt) is not { } screen || screen.WorkingArea.Width <= 0 || screen.WorkingArea.Height <= 0)
        {
            return _work.Width > 0; // 화면 사이 틈 등 — 직전 모니터 기준 유지
        }

        _monitor = screen.Bounds;
        _work = screen.WorkingArea;
        _scale = screen.Scaling > 0 ? screen.Scaling : 1;
        var area = new Rect(0, 0, _work.Width / _scale, _work.Height / _scale);
        var min = WindowMinSize.Current;
        var minSize = new Size(Math.Max(min.Width, w.MinWidth), Math.Max(min.Height, w.MinHeight));
        _ctx = xkxk.frameworks.ui.shell.Framework.XkxkUi.Current.Settings.CreateSnapContext(area, minSize);
        _barSince = -1;
        var host = SdiSnapBarHost.Shared;
        host.Prepare(_work, _scale);
        host.HideBar();
        Program.Log($"sdi snap area '{w.Title}' work={_work} scale={_scale:F2} hasBar={_ctx.HasBar}");
        return true;
    }

    /// <summary>현재 포인터(_last)로 판정하고 막대 머무름·미리보기를 맞춘다.</summary>
    private void Resolve()
    {
        var host = SdiSnapBarHost.Shared;
        var over = OverTabJoinTarget?.Invoke() == true;
        var zone = _ctx.Resolve(_last, host.IsBarVisible, over);
        if (zone.WantsBar && !host.IsBarVisible)
        {
            if (_barSince < 0)
            {
                _barSince = Stopwatch.GetTimestamp();
                StartBarTimer();
            }

            if (Stopwatch.GetElapsedTime(_barSince).TotalMilliseconds >= xkxk.frameworks.ui.shell.Framework.XkxkUi.Current.Settings.SnapBarDelayMs)
            {
                _barTimer?.Stop();
                host.ShowBar(_ctx);
                zone = _ctx.Resolve(_last, host.IsBarVisible, over);
            }
        }
        else if (!zone.WantsBar)
        {
            _barSince = -1;
            _barTimer?.Stop();
            if (host.IsBarVisible)
            {
                host.HideBar(); // 막대·감지 띠를 벗어나면 닫는다(11 문서 §3.1)
            }
        }

        _zone = zone;
        host.Show(zone);
    }

    // 포인터가 멈춰 WM_MOVING 이 오지 않아도 지연 뒤 막대를 띄운다(이동 루프 중 타이머 동작은 실기 확인 대상).
    private void StartBarTimer()
    {
        var delay = xkxk.frameworks.ui.shell.Framework.XkxkUi.Current.Settings.SnapBarDelayMs;
        if (delay <= 0)
        {
            return;
        }

        if (_barTimer is null)
        {
            _barTimer = new DispatcherTimer(DispatcherPriority.Input);
            _barTimer.Tick += (_, _) =>
            {
                _barTimer.Stop();
                if (_active && _barSince >= 0 && !SdiSnapBarHost.Shared.IsBarVisible) { Resolve(); }
            };
        }

        _barTimer.Interval = TimeSpan.FromMilliseconds(delay);
        _barTimer.Start();
    }

    private void End(IntPtr hWnd)
    {
        var active = _active;
        if (active && GetCursorPos(out var p)) { Update(p); } // 놓은 점에서 마지막 판정

        var (zone, work, scale, joinBefore) = (_zone, _work, _scale, _joinBefore);
        Cancel();
        if (!active || _window is not { } window)
        {
            return;
        }

        var moved = !SdiBgPinHook.GetWindowRect(hWnd, out var now)
            || now.L != _startRect.L || now.T != _startRect.T || now.R != _startRect.R || now.B != _startRect.B;
        Program.Log($"sdi snap end '{window.Title}' moved={moved} zone={zone.Kind} layout={zone.LayoutId ?? "-"} cell={zone.Cell} target={zone.Target}");
        if (!moved || !zone.HasTarget || zone.Kind == SnapZoneKind.TabJoin)
        {
            return; // Esc 복귀·막대 밖(일반 이동)·탭 합치기 우선
        }

        // 이동 루프·자석·탭 합류 End 처리가 모두 끝난 뒤 적용한다(같은 끌기의 합류 결과 확인).
        Dispatcher.UIThread.Post(() => Apply(window, hWnd, zone, work, scale, joinBefore));
    }

    private static void Apply(Window window, IntPtr hWnd, SnapZone zone, PixelRect work, double scale, string joinBefore)
    {
        var join = WindowTabJoinService.LastOutcome;
        if (!window.IsVisible || (!ReferenceEquals(join, joinBefore) && join.Contains("accepted=True", StringComparison.Ordinal)))
        {
            Program.Log($"sdi snap apply skipped '{window.Title}' visible={window.IsVisible} join={join}");
            return;
        }

        if (zone.Kind == SnapZoneKind.TopEdge)
        {
            window.WindowState = WindowState.Maximized; // 창이 속한 모니터 작업 영역 최대화(03 §5)
            Program.Log($"sdi snap apply '{window.Title}' maximize");
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
        Program.Log($"sdi snap apply '{window.Title}' {zone.Kind} rect=({l},{tp},{r - l}x{b - tp}) ok={ok}");
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
            Program.Log($"sdi snap assist skip '{window.Title}' mode={xkxk.frameworks.ui.shell.Framework.XkxkUi.Current.Settings.SnapSdiMode}");
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
            Program.Log($"sdi snap assist '{window.Title}' zone={zone.Kind} started={started}{(started ? string.Empty : " skip=" + SnapAssistSession.LastSkipReason)}");
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

    /// <summary>진행 중 상태를 비우고 막대 창을 숨긴다(크기 조절 전환·새 이동 루프·끝·해제).</summary>
    private void Cancel()
    {
        _entered = false;
        _barTimer?.Stop();
        _barSince = -1;
        (_zone, _ctx) = (SnapZone.None, SnapZoneContext.Disabled);
        if (_active)
        {
            _active = false;
            SdiSnapBarHost.Shared.Hide();
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out Point32 point);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hWnd, int attribute, out SdiBgPinHook.Rect32 value, int size);
}
