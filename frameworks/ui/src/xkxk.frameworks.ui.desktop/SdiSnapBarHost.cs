// SDI 창 스냅용 막대·미리보기 최상위 창 — 끄는 동안만 모니터 작업 영역을 덮는 투명·비활성·클릭 통과 창에 SnapOverlayLayer(odev-3)를 담는다(Win32 전용, 11_창배치_스냅레이아웃.md §7.3)
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using xkxk.frameworks.ui.shell.Docking;
using xkxk.frameworks.ui.views.Snap;

namespace xkxk.frameworks.ui.desktop;

/// <summary>
/// SDI 는 OS 창이라 MDI 처럼 영역 AdornerLayer 에 덮개를 붙일 수 없다. 그래서 끄는 SDI 가 있는 모니터 작업 영역 크기의 테두리 없는 투명 창을 띄우고
/// 그 내용에 MDI·웹과 같은 덮개(SnapOverlayLayer — 막대 SnapLayoutBar·미리보기)를 둔다. 그림·판정 좌표는 같은 SnapZoneContext 를 쓴다.
/// 좌표: SdiSnapHook 은 문맥을 '작업 영역 로컬 DIP'(왼쪽 위 = 0,0)로 만든다 → 덮개 Origin = (0,0). 창 위치는 작업 영역 왼쪽 위(물리 px), 크기는 작업 영역 ÷ 모니터 배율.
/// 포커스·입력: ShowActivated=false + WS_EX_NOACTIVATE·WS_EX_TOOLWINDOW·WS_EX_TRANSPARENT + WM_NCHITTEST → HTTRANSPARENT. 끄는 중에는 OS 이동 루프가 포인터를 잡고 있어
/// 이 창이 입력을 받을 일이 없지만, 남아 있어도 클릭·포커스를 가져가지 않게 한다. 작업표시줄·Alt+Tab 에도 나오지 않는다.
/// 수명: 앱 전체 1개(Shared)를 재사용한다. 끌기가 끝나면 숨기기만 한다(다음 끌기의 생성 비용 제거). 모든 호출은 UI 스레드(이동 루프 WndProc 포함)에서 한다.
/// 한계(확실하지 않음, 실기 확인 대상):
/// - Avalonia 투명 창(TransparencyLevelHint=Transparent)이 OS 이동 루프 중에도 바로 그려지는지.
/// - 배율이 다른 모니터로 옮길 때 Position→크기 순서로 지정하면 새 모니터 배율로 크기가 잡히는지(Avalonia 의 DPI 변경 처리에 기댄다).
/// - 덮개가 끄는 SDI 위에 그려진다(Windows Snap 미리보기는 끄는 창 아래). 반투명이라 창 내용은 보인다.
/// </summary>
internal sealed class SdiSnapBarHost
{
    private const int GwlExStyle = -20;
    private const long WsExTransparent = 0x00000020;
    private const long WsExToolWindow = 0x00000080;
    private const long WsExNoActivate = 0x08000000;
    private const uint WmNcHitTest = 0x0084;
    private const int HtTransparent = -1;

    private static SdiSnapBarHost? s_shared;

    private Window? _window;
    private SnapOverlayLayer? _layer;
    private PixelRect _area;
    private bool _styled;

    private SdiSnapBarHost()
    {
    }

    /// <summary>앱 전체에서 함께 쓰는 막대 창(처음 부를 때 만든다 — 창 자체는 첫 Prepare 때 만든다).</summary>
    public static SdiSnapBarHost Shared => s_shared ??= new SdiSnapBarHost();

    /// <summary>막대가 지금 보이는가(SnapZoneContext.Resolve 의 barVisible 인자).</summary>
    public bool IsBarVisible => _layer?.IsBarVisible == true;

    /// <summary>지금 덮고 있는 작업 영역(물리 px). 숨김이면 마지막 값.</summary>
    public PixelRect Area => _area;

    /// <summary>창이 보이는가(시험·로그용).</summary>
    public bool IsShown => _window?.IsVisible == true;

    /// <summary>
    /// 작업 영역에 창을 맞추고 보인다(막대·미리보기는 아직 숨김). workArea = 모니터 작업 영역(물리 px), scale = 그 모니터 배율.
    /// 같은 영역이면 위치·크기를 다시 지정하지 않는다(끌기 중 모니터를 넘을 때만 다시 맞춘다).
    /// </summary>
    public void Prepare(PixelRect workArea, double scale)
    {
        if (!OperatingSystem.IsWindows() || workArea.Width <= 0 || workArea.Height <= 0)
        {
            return;
        }

        scale = scale > 0 ? scale : 1;
        var window = _window ?? Create();
        if (workArea != _area || !window.IsVisible)
        {
            _area = workArea;
            _layer!.Hide();
            window.Position = workArea.Position;
            window.Width = workArea.Width / scale;
            window.Height = workArea.Height / scale;
        }

        if (!window.IsVisible)
        {
            window.Show();
        }

        ApplyExStyle(window);
    }

    /// <summary>판정 문맥의 막대를 띄운다(문맥은 작업 영역 로컬 DIP). Prepare 전이면 아무것도 하지 않는다.</summary>
    public void ShowBar(SnapZoneContext ctx) => _layer?.ShowBar(ctx);

    /// <summary>막대를 숨긴다(미리보기는 그대로).</summary>
    public void HideBar() => _layer?.HideBar();

    /// <summary>판정 결과로 미리보기·칸 강조를 맞춘다. 탭 합치기 결과면 막대·미리보기를 모두 숨긴다(SnapOverlayLayer.Show 규칙).</summary>
    public void Show(SnapZone zone) => _layer?.Show(zone);

    /// <summary>막대·미리보기를 숨기고 창도 숨긴다(놓기·Esc·크기 조절 전환·캡처 잃음).</summary>
    public void Hide()
    {
        _layer?.Hide();
        if (_window is { IsVisible: true } w)
        {
            w.Hide();
        }
    }

    private Window Create()
    {
        _layer = new SnapOverlayLayer { Origin = default };
        var window = new Window
        {
            Title = "pd-sdi-snap-bar",
            WindowDecorations = WindowDecorations.None,
            CanResize = false,
            ShowInTaskbar = false,
            ShowActivated = false,
            Topmost = true,
            Focusable = false,
            IsHitTestVisible = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Background = Brushes.Transparent,
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent },
            Content = _layer,
        };
        if (OperatingSystem.IsWindows())
        {
            Win32Properties.AddWndProcHookCallback(window, HitTestHook);
        }

        window.Opened += (_, _) => ApplyExStyle(window);
        _window = window;
        Program.Log("sdi snap bar host created");
        return window;
    }

    // 창 위 어디를 눌러도 아래 창으로 넘긴다(같은 스레드 창 — SDI·메인). 다른 프로세스 창은 WS_EX_TRANSPARENT 에 기댄다.
    private static IntPtr HitTestHook(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmNcHitTest)
        {
            handled = true;
            return new IntPtr(HtTransparent);
        }

        return IntPtr.Zero;
    }

    private void ApplyExStyle(Window window)
    {
        if (_styled || !OperatingSystem.IsWindows() || window.TryGetPlatformHandle()?.Handle is not { } h || h == IntPtr.Zero)
        {
            return;
        }

        var ex = GetWindowLongPtrW(h, GwlExStyle).ToInt64();
        var want = ex | WsExTransparent | WsExToolWindow | WsExNoActivate;
        if (want != ex)
        {
            SetWindowLongPtrW(h, GwlExStyle, new IntPtr(want));
        }

        _styled = true;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindowLongPtrW(IntPtr hWnd, int index);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWindowLongPtrW(IntPtr hWnd, int index, IntPtr value);
}
