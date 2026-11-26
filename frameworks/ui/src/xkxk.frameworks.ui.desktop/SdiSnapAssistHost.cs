// SDI 앱 모드 Snap Assist 호스트 창 — 배치한 SDI 창이 있는 모니터 작업 영역을 덮는 최상위 투명 창에 Views 덮개(SnapAssistLayer)를 담고 포커스를 가져간다(Win32 전용, 11_창배치_스냅레이아웃.md §3.9, 계획 §5.5)
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using xkxk.frameworks.ui.shell.Docking;
using xkxk.frameworks.ui.shell.Platform;
using xkxk.frameworks.ui.shell.Theming;
using xkxk.frameworks.ui.views.Snap;
using xkxk.frameworks.ui.shell.Framework;

namespace xkxk.frameworks.ui.desktop;

/// <summary>
/// SDI 는 OS 창이라 MDI 처럼 영역 AdornerLayer 에 덮개를 붙일 수 없다. 그래서 세션마다 배치한 창 모니터의 작업 영역 크기 테두리 없는 투명 창을 만들고 내용에 SnapAssistLayer 를 둔다.
/// - 좌표: 세션 Area(SnapAssistCandidates.AreaOf Sdi) = 작업 영역 ÷ 배율 DIP. 덮개는 Origin = Area.TopLeft 를 빼서 그리므로 창 위치 = 작업 영역 왼쪽 위(물리 px), 크기 = 작업 영역 ÷ 배율이면 맞는다.
/// - 포커스: 끌기 막대 창(SdiSnapBarHost)과 달리 키 입력(Tab·Enter·Esc)을 받아야 해서 ShowActivated=true 로 띄우고 바로 Activate 한다. WS_EX_TOOLWINDOW 로 Alt+Tab 에서 뺀다.
/// - 끝: 덮개 Hidden(세션 끝) → 창을 닫는다. 창이 바깥에서 닫히면 세션을 cancel 로 끝낸다.
/// - 비활성(Deactivated): 표시·Update(Pick) 직후 옵션 SnapAssistSdiGraceMs(기본 400ms) 동안은 늦게 오는 비활성(배치가 다른 OS 창을 앞으로 가져온 결과 — odev-5 추정 위험)으로 보고
///   다시 Activate 한다(최대 <see cref="MaxReactivate"/> 회). 그 밖의 비활성은 focus 로 끝낸다(덮개 자신의 Deactivated 판정과 같은 결과, 중복 호출은 세션이 무시한다).
/// - 비전경 시험(--bg-pos)은 화면 밖 자리를 지키므로 띄우지 않는다(skip=bg-mode — WindowMenuOps.OsPlacement 와 같은 규칙). 그때 세션은 no-overlay 로 끝난다.
/// 한계(확실하지 않음, 실기 확인 대상): 유예 시간 값은 초기 제시값이다. 유예 안에 사용자가 다른 앱을 누르면 포커스를 한 번 되가져온다.
/// </summary>
internal sealed class SdiSnapAssistHost : ISnapAssistOverlay
{

    /// <summary>유예 한 번 동안 다시 활성화하는 최대 횟수(다른 앱과 포커스 다툼 방지).</summary>
    public const int MaxReactivate = 2;

    private const int GwlExStyle = -20;
    private const long WsExToolWindow = 0x00000080;

    private readonly Window _window;
    private readonly SnapAssistLayer _layer;
    private long _graceSince;
    private int _reactivated;
    private bool _shown;
    private bool _closed;

    private SdiSnapAssistHost(Window window, SnapAssistLayer layer)
    {
        _window = window;
        _layer = layer;
        _layer.Hidden += OnLayerHidden;
        _window.Deactivated += OnDeactivated;
        _window.Closed += OnClosed;
        _window.Opened += (_, _) => ApplyExStyle();
    }

    /// <summary>Views 이음매 등록(Desktop 어셈블리 적재 때 1회). 이미 등록돼 있으면 덮어쓰지 않는다.</summary>
#pragma warning disable CA2255 // 등록 지점(Program)이 배정 밖이라 어셈블리 적재 때 이음매를 채운다(SdiTabJoinHook 과 같은 방식)
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Register()
    {
        if (OperatingSystem.IsWindows())
        {
            SnapAssistLayer.SdiHostFactory ??= Create;
        }
    }

    /// <summary>배치한 창(placed) 모니터의 작업 영역을 덮는 호스트를 만든다(아직 띄우지 않음 — Show 에서 띄운다). 못 만들면 null(세션 no-overlay).</summary>
    internal static ISnapAssistOverlay? Create(Window placed)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        if (PlacementRecovery.IsSuppressed(SystemDialogHost.BackgroundMode))
        {
            Program.Log($"sdi snap assist skip=bg-mode '{placed.Title}'");
            return null;
        }

        if ((placed.Screens.ScreenFromWindow(placed) ?? placed.Screens.Primary) is not { } screen
            || screen.WorkingArea.Width <= 0 || screen.WorkingArea.Height <= 0)
        {
            Program.Log($"sdi snap assist skip=no-screen '{placed.Title}'");
            return null;
        }

        var scale = screen.Scaling > 0 ? screen.Scaling : 1;
        var work = screen.WorkingArea;
        var layer = new SnapAssistLayer();
        var window = new Window
        {
            Title = "pd-sdi-snap-assist",
            WindowDecorations = WindowDecorations.None,
            CanResize = false,
            ShowInTaskbar = false,
            ShowActivated = true,
            Topmost = true,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Background = Brushes.Transparent,
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent },
            Position = work.Position,
            Width = work.Width / scale,
            Height = work.Height / scale,
            Content = layer,
        };
        Program.Log($"sdi snap assist host created '{placed.Title}' work={work} scale={scale:F2}");
        return new SdiSnapAssistHost(window, layer);
    }

    // ── ISnapAssistOverlay ──

    /// <inheritdoc/>
    public void Show(SnapAssistView view)
    {
        if (_closed)
        {
            return;
        }

        StartGrace();
        _window.Show(); // 덮개가 TopLevel(이 창)을 찾을 수 있게 먼저 띄운다
        _window.Activate();
        _shown = true;
        _layer.Show(view); // 덮개가 포커스를 가져가고, 비활성이면 다음 프레임에 창도 다시 활성화한다
        Program.Log($"sdi snap assist show layout={view.LayoutId} active={view.ActiveCell} candidates={view.Candidates.Count} active-window={_window.IsActive}");
    }

    /// <inheritdoc/>
    public void Update(SnapAssistView view)
    {
        if (_closed)
        {
            return;
        }

        StartGrace(); // Pick 의 배치가 다른 SDI 창을 앞으로 가져와 늦은 비활성이 올 수 있다
        if (!_window.IsActive)
        {
            _window.Activate();
        }

        _layer.Update(view);
    }

    /// <inheritdoc/>
    public void Hide() => _layer.Hide(); // Hidden → OnLayerHidden 이 창을 닫는다

    // ── 내부 ──

    private void StartGrace()
    {
        _graceSince = Stopwatch.GetTimestamp();
        _reactivated = 0;
    }

    private bool InGrace => Stopwatch.GetElapsedTime(_graceSince).TotalMilliseconds < XkxkUi.Current.Settings.SnapAssistSdiGraceMs;

    private void OnDeactivated(object? sender, EventArgs e)
    {
        if (!_shown || _closed)
        {
            return;
        }

        if (InGrace && _reactivated < MaxReactivate)
        {
            _reactivated++;
            Program.Log($"sdi snap assist late deactivate → reactivate #{_reactivated}");
            Dispatcher.UIThread.Post(() =>
            {
                if (!_closed && !_window.IsActive)
                {
                    _window.Activate();
                }
            }, DispatcherPriority.Send);
            return;
        }

        // 덮개도 같은 판정을 하지만(OnWindowDeactivated), 재포커스 대기 중이면 건너뛰므로 여기서 한 번 더 확인한다
        Dispatcher.UIThread.Post(() =>
        {
            if (!_closed && _shown && !_window.IsActive && !InGrace && SnapAssistSession.IsActive)
            {
                Program.Log("sdi snap assist deactivated → cancel focus");
                SnapAssistSession.Cancel(SnapAssistEndReason.Focus);
            }
        }, DispatcherPriority.Background);
    }

    private void OnLayerHidden()
    {
        if (_closed)
        {
            return;
        }

        _closed = true;
        _shown = false;
        Program.Log("sdi snap assist host close (session ended)");
        _window.Close();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        var wasOpen = !_closed;
        _closed = true;
        _shown = false;
        _layer.Hidden -= OnLayerHidden;
        _window.Deactivated -= OnDeactivated;
        _window.Closed -= OnClosed;
        if (wasOpen && SnapAssistSession.IsActive)
        {
            Program.Log("sdi snap assist host closed externally → cancel");
            SnapAssistSession.Cancel(SnapAssistEndReason.Cancel);
        }
    }

    private void ApplyExStyle()
    {
        if (_window.TryGetPlatformHandle()?.Handle is not { } h || h == IntPtr.Zero)
        {
            return;
        }

        var ex = GetWindowLongPtrW(h, GwlExStyle).ToInt64();
        if ((ex & WsExToolWindow) == 0)
        {
            SetWindowLongPtrW(h, GwlExStyle, new IntPtr(ex | WsExToolWindow));
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindowLongPtrW(IntPtr hWnd, int index);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWindowLongPtrW(IntPtr hWnd, int index, IntPtr value);
}
