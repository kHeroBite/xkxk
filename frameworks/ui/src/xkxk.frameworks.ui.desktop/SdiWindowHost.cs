// 데스크톱 SDI(플로팅) 창 호스트 — 비전경(--bg-pos) 화면 밖 배치와 MDI-12 화면 밖 창 정상화
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Dock.Avalonia.Controls;
using Dock.Model.Core;
using xkxk.frameworks.ui.shell.Docking;
#if PD_DIAGNOSTICS
using xkxk.frameworks.ui.shell.Diagnostics;
#endif

namespace xkxk.frameworks.ui.desktop;

/// <summary>
/// Dock 의 OS 실 창(HostWindow)을 상속한다. 템플릿은 HostWindow 의 StyleKeyOverride 를 물려받는다.
/// - 비전경 모드(--bg-pos): 활성화 없이 열고, 레이아웃 복원이 화면 안 좌표를 줘도 VirtualScreen 밖으로 되돌린다.
/// - 일반 모드: 열림·위치 변경(레이아웃 복원 포함) 때 어느 모니터 작업영역에도 보이지 않으면 안으로 옮긴다(MDI-12).
/// - owner 없음(Dock NeverOwned): 메인 최소화·숨김에 끌려가지 않는다. 메인과의 Z 는 SdiAboveMainHook(설정 '항상 메인 창 위')이 맞춘다.
/// 협력: SdiBgPinHook(비전경 위치 고정)·MainBgPinHook(주 창 기동 구간)·SdiZOrderRaiser(Z순서)·SdiAboveMainHook(메인 밑 유지)·SdiResizeDebounce(크기 조절 끝)·WindowPlacement(MDI-12).
/// </summary>
public sealed class SdiWindowHost : HostWindow, IHostWindow
{
    private static BgPlacement? s_placement;
    private static int s_opened;

    private bool _pending;
    private bool _placedOnce;
    private PixelPoint? _requested;
    private readonly SdiBgPinHook _bgPin = new(); // K2 필드 초기화자 — 설치는 생성자(비전경 모드)에서
    private readonly SdiResizeDebounce _resize = new();
    private readonly SdiSnapHook _snap = new(); // 창 배치(스냅 레이아웃) SDI 후크
    private readonly SdiMagnetHook _magnet = new(); // UI-12 ③ SDI 자석(OS 이동·크기 조절 루프, Win32 전용)
    private readonly SdiShadowWindow _shadow = new(); // Q2 #16 활성·비활성 바깥 그림자(SDI 바로 뒤 비소유 레이어드 창, Win32 전용)
#if PD_DIAGNOSTICS
    private readonly double _ctorMs = PerfProbe.Current.NowMs; // sdi.host.* 계측 기준 시각(생성자 진입 전, 필드 초기화 시점)
#endif

    public SdiWindowHost()
    {
#if PD_DIAGNOSTICS
        if (DesktopWindowHost.OffscreenGuard)
        {
            ShowInTaskbar = false; // 시험 창이 작업표시줄·Alt+Tab 에 나타나 사용자가 전경으로 끌어오지 못하게(진단 전용, 표시 전 지정)
        }
#endif
        if (s_placement is not null)
        {
            ShowActivated = false;
            WindowStartupLocation = WindowStartupLocation.Manual;
            // 보이기 전부터 화면 밖(lead 12:3x): Manual 의 기본 위치(0,0)로 한 번이라도 그려지지 않게 생성 즉시 지정한다(OnOpened 재지정은 그대로).
            Position = ResolveOrigin(this, s_opened + 1);
            _bgPin.Install(this); // 표시·크기 변경의 OS 화면 안 보정을 WM_WINDOWPOSCHANGING 단계에서 막는다
        }

        _shadow.Install(this);
        _snap.Install(this);
        SdiSnapHook.OverTabJoinTarget ??= () => WindowTabJoinService.IsOverTarget;
        _magnet.Install(this); // 제목줄 BeginMoveDrag 는 Dock 자석을 거치지 않으므로 PD 가 WM_MOVING·WM_SIZING 에서 붙인다(Q8)
        Closed += (_, _) => _shadow.Destroy(); // 창이 닫히면 그림자 창도 함께 파괴
        PositionChanged += (_, _) => ScheduleCheck();
    }

    /// <summary>비전경 배치 설정(null = 일반 모드).</summary>
    public static BgPlacement? Placement => s_placement;

    /// <summary>지금까지 열린(비전경 배치 계단 번호를 받은) SDI 창 수. bg pin 훅이 다음 원점을 계산할 때 쓴다.</summary>
    internal static int OpenedCount => s_opened;

    public static void Install(BgPlacement? placement) => s_placement = placement;

    /// <summary>App.ConfigureMainWindow 에 연결 — 비전경이면 화면 밖, 아니면 열린 뒤 MDI-12 정상화.</summary>
    public static void ConfigureMain(Window window)
    {
        SdiAboveMainHook.Install(window); // SDI 는 owner 가 없다(Program.ConfigureFloatingOwner) — '항상 메인 창 위' Z 유지는 이 훅(일반·비전경 공통)
        if (s_placement is null)
        {
            window.Opened += (_, _) => WindowPlacement.EnsureVisible(window, "main");
            return;
        }

        window.ShowActivated = false;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        MainBgPinHook.Install(window); // 기동 구간(생성~Opened+3초) OS 화면 안 보정 차단
        window.Position = ResolveOrigin(window, 0);
        // 일부 OS 는 표시 시점에 위치를 보정하므로 열린 직후 한 번 더 적용한다.
        window.Opened += (_, _) => window.Position = ResolveOrigin(window, 0);
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        SdiZOrderRaiser.RaiseZ(this); // Z순서 계약 ①: 나중에 표시된 창이 위(활성화 없이)
#if PD_DIAGNOSTICS
        SdiHostPlacement.TrackOpenCost(this, _ctorMs); // sdi.host.* 계측
#endif
        DesktopWindowHost.RegisterSdi(this); // 윈도우 메뉴 ⑦·⑬ 대상 목록 + 새 창 캡처 방지 적용
        SdiAboveMainHook.Register(this); // '항상 메인 창 위' 대상 SDI(닫히면 자동 제거)
        // Opened 는 Show 마다 온다(트레이 숨기기→보이기 포함, 실측 sdi_tray). 새 창 배치·크기 맞춤은 처음 한 번만 해서 복원 때 위치·크기가 바뀌지 않게 한다.
        var first = !_placedOnce;
        _placedOnce = true;
        if (s_placement is not null)
        {
            if (first)
            {
                var origin = ResolveOrigin(this, Interlocked.Increment(ref s_opened));
                if (!PlacementRecovery.ForceInBackground) // 진단 시험(요건 4 왕복)은 PlaceSdi 가 놓은 자리를 되돌리지 않는다. 그 자리는 화면 밖 MDI 창 좌표에서 온다
                {
                    Position = origin;
                }

                WindowPlacement.FitNewWindow(this, $"sdi '{Title}'", move: false); // 비전경은 화면 밖 위치를 지키고 크기만 맞춘다
            }

            return;
        }

        WindowPlacement.EnsureVisible(this, $"sdi '{Title}'", SdiHostPlacement.TakeRequested(ref _requested)); // 숨긴 사이 모니터 구성이 바뀌었을 수 있어 매번 점검한다
        if (first)
        {
            WindowPlacement.FitNewWindow(this, $"sdi '{Title}'", move: true);
        }
    }

    /// <summary>
    /// 셸 계약(odev-2): 복원은 ZOrder 오름차순으로 Show 하고 마지막에 활성 문서의 창을 SetActive 한다.
    /// 일반 모드: Dock 기본 동작(활성화 = 맨 위 + 전경). 비전경 모드(--bg-pos): 전경 획득 없이 Z만 맨 위로.
    /// </summary>
    void IHostWindow.SetActive()
    {
        // 셸 복원 경로(IDockWindow.SetActive → Host.SetActive)가 실제로 닿는지 실측용 로그(odev-2 요청)
        Console.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [host] sdi SetActive '{Title}' bg={s_placement is not null}");
        if (s_placement is not null)
        {
            SdiZOrderRaiser.RaiseZ(this);
            return;
        }

        if (!SdiZOrderRaiser.TryBaseSetActive(this))
        {
            Activate();
        }
    }

    // 셸의 창 상태 명령(WindowStateOps.SetHostState)이 인터페이스로 부른다. 활성 아닌 창 최소화와 화면 밖 최소화·복원은 다른 창을 전경으로 올리지 않는다(w13 Z6, SdiQuietState).
    void IHostWindow.SetWindowState(DockWindowState windowState)
    {
        if (!SdiQuietState.TryApply(this, windowState, s_placement is not null))
        {
            SetWindowState(windowState);
        }
    }

    protected override void OnResized(WindowResizedEventArgs e)
    {
        base.OnResized(e);
        if (e.Reason != WindowResizeReason.User || s_placement is not null)
        {
            return;
        }

        _resize.Restart(this);
    }

    // IHostWindow.SetPosition 을 재구현(인터페이스 재구현)해 요구 좌표를 보관한다(Dock 의 표시 전 배치, 레이아웃 복원 ApplyWindow).
    // 실측: 보이는 창에 화면 밖 좌표를 주면 즉시 주 모니터 쪽으로 보정되고 Dock 모델 X·Y 도 보정값으로 덮여 원래 요구 좌표를 알 수 없다.
    void IHostWindow.SetPosition(double x, double y)
    {
        if (double.IsFinite(x) && double.IsFinite(y))
        {
            _requested = new PixelPoint((int)x, (int)y);
        }

        if (s_placement is not null && !IsVisible)
        {
            // 비전경 시험: Dock 의 표시 전 배치(화면 안 좌표일 수 있음)를 따르지 않고 화면 밖 원점으로 둔다 — 표시 순간 노출 방지
            Position = ResolveOrigin(this, s_opened + 1);
            Console.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [host] sdi bg pre-show request ({x},{y}) -> ({Position.X},{Position.Y})");
            return;
        }

        SetPosition(x, y);
        Console.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [host] MDI-12 request ({x},{y}) pos-after-set=({Position.X},{Position.Y}) visible={IsVisible}");
        // 이미 표시된 창(레이아웃 복원 ApplyWindow)은 보정된 위치가 같으면 PositionChanged 가 안 올 수 있어 직접 점검을 예약한다.
        ScheduleCheck();
    }

    /// <summary>이 SDI 창에 위치 고정 문서가 있는가(⑦·요건 5 복구 공용).</summary>
    internal static bool IsLocked(Window w)
    {
        var docs = new List<PocDocument>();
        if (w is IHostWindow { Window.Layout: { } root })
        {
            CollectDocuments(root, docs);
        }

        return docs.Any(d => WindowCommands.ChromeOf(d).IsLocked);
    }

    private static void CollectDocuments(IDockable dockable, List<PocDocument> docs)
    {
        if (dockable is PocDocument doc)
        {
            docs.Add(doc);
            return;
        }

        if (dockable is IDock { VisibleDockables: { } children })
        {
            foreach (var child in children)
            {
                CollectDocuments(child, docs);
            }
        }
    }

    /// <summary>요건 5 복구 진입점: 보통 상태로 돌려 기본 위치로 옮긴다. 레이아웃이 요구했던 옛 좌표는 버린다(뒤따르는 점검이 그 좌표로 되돌아가지 않게).</summary>
    internal void RecoverTo(PixelPoint position)
    {
        if (WindowState != WindowState.Normal)
        {
            WindowState = WindowState.Normal;
        }

        _requested = null;
        Position = position;
    }

    // 레이아웃 복원은 Position 을 직접 바꾸므로 변경 이벤트 뒤 한 번만(합류) 점검한다.
    private void ScheduleCheck()
    {
        if (_pending || !IsVisible)
        {
            return;
        }

        _pending = true;
        Dispatcher.UIThread.Post(() =>
        {
            _pending = false;
            if (s_placement is null)
            {
                WindowPlacement.EnsureVisible(this, $"sdi '{Title}'", SdiHostPlacement.TakeRequested(ref _requested));
            }
            else if (!IsOnscreenPlacement(this) && WindowPlacement.IsVisibleOnAnyScreen(this))
            {
                Position = ResolveOrigin(this, Interlocked.Increment(ref s_opened));
            }
        }, DispatcherPriority.Background);
    }

    /// <summary>
    /// 화면 안 예외 기동인가(진단판 전용, run_desktop_bg.sh PD_ALLOW_ONSCREEN): --bg-pos 고정 좌표가 실제 모니터 안이면 시험 창을 일부러 그 모니터에 띄운 것이다.
    /// 화면 밖으로 되돌리는 처리(BgWindowPolicy·SdiBgPinHook·ScheduleCheck)가 이 값을 보고 건너뛴다. 제품판은 항상 false 로 기존 동작 그대로다.
    /// </summary>
    internal static bool IsOnscreenPlacement(Window window)
    {
#if PD_DIAGNOSTICS
        return s_placement is { Auto: false } p && (window.Screens?.All.Any(s => s.Bounds.Contains(new PixelPoint(p.X, p.Y))) ?? false);
#else
        return false;
#endif
    }

    /// <summary>비전경 원점(SdiHostPlacement 위임). bg pin 훅·MainBgPinHook 이 부른다.</summary>
    public static PixelPoint ResolveOrigin(Window window, int index) => SdiHostPlacement.ResolveOrigin(s_placement, window, index);
}
