// 데스크톱 윈도우 메뉴 호스트(IWindowHostCommands) — 메인 창 초기화·모니터 맞춤·SDI 위치 초기화·화면 캡처 방지(SetWindowDisplayAffinity)
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using xkxk.frameworks.ui.shell.Docking;

namespace xkxk.frameworks.ui.desktop;

/// <summary>
/// 계약은 셸 IWindowHostCommands(odev-2). 모든 메서드는 UI 스레드에서 불리며 예외를 밖으로 던지지 않고 false 로 알린다.
/// 사양 출처: odev-1 Mars 윈도우 메뉴 실측(동작 사양만, 코드는 PD 로 새로 작성) + team-lead 결정(⑤⑥ Normal 상태·Bounds 기준, ⑬ 메인+SDI 전체).
/// ⑧⑨⑩ 은 셸 기본 경로(SDI 문서마다 SetState/CloseDocument)가 처리하므로 CanExecute=false 로 넘긴다.
/// 협력: MaxTrackHook(⑤⑥ 최대 추적 크기)·OffscreenGuardHook(진단 위치 고정·화면 밖 보호, 제품 빌드는 호출째 빠짐).
/// </summary>
public sealed class DesktopWindowHost : IWindowHostCommands
{
    private const int DefaultWidth = 1400;   // ④ 기본 크기(DIP) — 셸 MainWindow 기본값과 같음
    private const int DefaultHeight = 900;
    /// <summary>⑤⑥ 보이는 프레임 맞춤(진단 --fit-visible on 일 때만). 기본 false = 판정보고서 §⑤⑥(client=Bounds·X−7) 그대로.</summary>
    public static bool FitVisibleFrameEnabled { get; set; }

    /// <summary>진단 --primary-override x,y,w,h(px): ④ 작업 영역·⑤⑥ 맞춤 대상을 이 사각형으로 바꾼다(시험 창 화면 노출 방지). null = 실제 모니터(제품·기본).</summary>
    public static PixelRect? PrimaryOverride { get; set; }

#if PD_DIAGNOSTICS
    /// <summary>--offscreen-guard on: 실제 모니터와 겹치는 위치·크기 변경을 막는다(시험 창 화면 노출 물리 차단). 구현은 OffscreenGuardHook.</summary>
    public static bool OffscreenGuard { get; set; }

    /// <summary>메인 창(AttachMain 전에는 null). OffscreenGuardHook 이 주 창 판정에 쓴다.</summary>
    internal static Window? MainWindow => s_main;

    /// <summary>열린 SDI 창 사본. 원격 판정 훅(DesktopDiagHooks — 캡처 방지·OS Z 순서)이 쓴다.</summary>
    internal static Window[] SdiWindows => s_sdi.ToArray();
#endif

    private const int MarsFitOffsetX = 7;    // ⑤⑥ 위치 X−7(px, Mars 실측 사양 — 보이지 않는 크기 조절 테두리 폭)

    private static readonly List<Window> s_sdi = new();
    private static Window? s_main;

    /// <summary>App.ConfigureMainWindow 에서 호출.</summary>
    public static void AttachMain(Window main)
    {
        s_main = main;
#if PD_DIAGNOSTICS
        if (OffscreenGuard)
        {
            main.ShowInTaskbar = false; // 시험 주 창이 작업표시줄·Alt+Tab 에 나타나지 않게(진단 전용, 표시 전 지정)
        }
#endif
        main.Opened += (_, _) => CaptureProtection.Apply(main, "main");
        OffscreenGuardHook.InstallGuard(main); // 진단 전용(제품 빌드는 호출째 빠짐) — 훅 등록 순서: 가드 → 최대 추적 크기(옛 InstallMaxTrackHook 과 같음)
        MaxTrackHook.Install(main); // ⑤⑥ 클라이언트=Bounds 가 OS 기본 최대 추적 크기에 잘리지 않게(WM_GETMINMAXINFO)
        WindowPosTrace.Install(main); // 진단 전용 계측(Q6 #5) — ④⑤⑥ 직후 1초 위치·DPI 메시지 로그, 제품 빌드는 호출째 빠짐
        MonitorChangeWatcher.Attach(main); // 요건 5 — 실행 중 모니터 구성·메인 크기 변경 때 안 보이는 창을 기본 위치로
    }

    /// <summary>열린 SDI 창(요건 5 복구 대상).</summary>
    internal static IReadOnlyList<Window> OpenSdi => s_sdi;

    /// <summary>요건 5 복구: ④ 와 같은 규칙으로 메인 창을 기본 위치로.</summary>
    internal static bool ResetMainForRecovery(Window main) => ResetMain(main);

    /// <summary>SdiWindowHost.OnOpened 에서 호출 — 목록 등록 + 현재 캡처 방지 값 적용(새로 여는 창 포함 계약).</summary>
    public static void RegisterSdi(Window sdi)
    {
        if (!s_sdi.Contains(sdi))
        {
            s_sdi.Add(sdi);
            sdi.Closed += (_, _) => s_sdi.Remove(sdi);
            MonitorFitService.TrackMaximize(sdi, "sdi"); // 메인과 같은 일반 최대화 경로(옮기지 않음, 로그)
            OffscreenGuardHook.InstallGuard(sdi); // 진단 --offscreen-guard: SDI 창도 실제 모니터로 들어오는 이동을 막는다(첫 표시는 열린 뒤라 막지 못함)
        }

        if (CaptureProtection.Enabled)
        {
            CaptureProtection.Apply(sdi, $"sdi '{sdi.Title}'");
        }
    }

#if PD_DIAGNOSTICS
    /// <summary>
    /// 호스트 자동화(--host-cmd, 시험 전용): 메인 창이 열린 뒤 쉼표 목록을 간격을 두고 실행하고 각 단계 뒤 창 사각형을 로그로 남긴다.
    /// 항목: WindowHostCommand 이름 또는 capture-on / capture-off. 예: --host-cmd FitMultiMonitor,ResetMainWindow,capture-on
    /// host 는 Program 합성 지점이 만든 호스트 인스턴스(UiHostOptions.WindowHost)다(B05, 옛 단일 인스턴스 접근자 제거).
    /// </summary>
    public static void RunScripted(IWindowHostCommands host, Window main, string spec, int intervalMs = 1500)
    {
        var steps = spec.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        main.Opened += async (_, _) =>
        {
            foreach (var step in steps)
            {
                await Task.Delay(intervalMs);
                bool ok;
                if (step.Equals("capture-on", StringComparison.OrdinalIgnoreCase) || step.Equals("capture-off", StringComparison.OrdinalIgnoreCase))
                {
                    ok = host.SetCaptureProtection(step.EndsWith("on", StringComparison.OrdinalIgnoreCase));
                }
                else if (Enum.TryParse<WindowHostCommand>(step, true, out var cmd))
                {
                    ok = host.CanExecute(cmd) && host.Execute(cmd);
                }
                else
                {
                    Log($"host-cmd 알 수 없는 항목: {step}");
                    continue;
                }

                await Task.Delay(300); // OS 가 위치·크기를 반영한 뒤 기록
                var sdi = string.Join(" ", s_sdi.Select(w => $"[{w.Title} {w.Position.X},{w.Position.Y} {w.WindowState}]"));
                Log($"host-cmd {step} ok={ok} main=({main.Position.X},{main.Position.Y}) frame={main.FrameSize?.Width:F0}x{main.FrameSize?.Height:F0}dip state={main.WindowState} capture={host.GetCaptureProtection()} sdi={sdi}");
            }
        };
    }

#endif

    public bool CanExecute(WindowHostCommand command) => command switch
    {
        WindowHostCommand.ResetMainWindow or WindowHostCommand.FitSingleMonitor or WindowHostCommand.FitMultiMonitor or WindowHostCommand.MaximizeOnSelectedMonitor => s_main is not null,
        WindowHostCommand.ResetSdiPositions => s_main is not null,
        _ => false, // ⑧⑨⑩ 셸 기본 경로
    };

    public bool Execute(WindowHostCommand command)
    {
        try
        {
            if (s_main is not { } main || main.Screens.All.Count == 0)
            {
                Log($"window-cmd {command} 실패: 메인 창 또는 모니터 정보 없음");
                return false;
            }

            var ok = command switch
            {
                WindowHostCommand.ResetMainWindow => ResetMain(main),
                // 진단 가상 모니터(--virtual-monitors)면 ⑤ 는 주 가상 모니터, ⑥ 은 가상 모니터 합집합(D-C2-46 — 예전엔 둘 다 PrimaryOverride 전체라 같은 값). --primary-override 만 있으면 둘 다 그 사각형
                WindowHostCommand.FitSingleMonitor => FitTo(main, DesktopMonitorProvider.IsVirtual ? DesktopMonitorProvider.VirtualFitTarget(false) : PrimaryOverride ?? (main.Screens.ScreenFromWindow(main) ?? main.Screens.Primary ?? main.Screens.All[0]).Bounds, "fit-single"),
                WindowHostCommand.FitMultiMonitor => FitTo(main, DesktopMonitorProvider.IsVirtual ? DesktopMonitorProvider.VirtualFitTarget(true) : PrimaryOverride ?? main.Screens.All.Select(s => s.Bounds).Aggregate((a, b) => a.Union(b)), "fit-multi"),
                WindowHostCommand.ResetSdiPositions => ResetSdi(main),
                WindowHostCommand.MaximizeOnSelectedMonitor => MonitorFitService.MaximizeOnSelected(main), // U20 선택 모니터 최대화
                _ => false,
            };
            return ok;
        }
        catch (Exception ex)
        {
            Log($"window-cmd {command} 예외: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    public bool SetCaptureProtection(bool enabled)
    {
        CaptureProtection.Enabled = enabled;
        var ok = s_main is null || CaptureProtection.Apply(s_main, "main");
        foreach (var w in s_sdi.ToArray())
        {
            ok &= CaptureProtection.Apply(w, $"sdi '{w.Title}'");
        }

        return ok;
    }

    public bool? GetCaptureProtection() => CaptureProtection.IsProtected(s_main);

    public bool MoveMainToDefault() => s_main is { } main && WindowDefaultMove.Main(main);

    public bool MoveSdiToDefault(Dock.Model.Core.IDockWindow window) => s_main is { } main && WindowDefaultMove.Sdi(main, window);

    // ④ 주 모니터 작업 영역 가운데, 기본 크기(작업 영역보다 크면 맞춤), 최대화·최소화 해제
    private static bool ResetMain(Window main)
    {
        WindowPosTrace.Begin("reset");
        var screen = main.Screens.Primary ?? main.Screens.All[0];
        var wa = PrimaryOverride ?? screen.WorkingArea; // 진단 --primary-override: 시험 창이 사용자 화면에 나오지 않게 기준 사각형을 바꾼다
        MaxTrackHook.Release(); // 맞춤 해제 — 최대 추적 크기를 OS 기본으로 되돌린다
        var scale = screen.Scaling;
        WindowFrameMetrics.RestoreIfArranged(main); // OS 스냅 상태면 먼저 복원(otest-12, 로그 arranged-restore)
        main.WindowState = WindowState.Normal;
        var (fw, fh) = WindowFrameMetrics.FrameExtra(main);
        var w = Math.Min(DefaultWidth, wa.Width / scale - fw);
        var h = Math.Min(DefaultHeight, wa.Height / scale - fh);
        main.Width = w;
        main.Height = h;
        var pxW = (int)((w + fw) * scale);
        var pxH = (int)((h + fh) * scale);
        main.Position = new PixelPoint(wa.X + (wa.Width - pxW) / 2, wa.Y + (wa.Height - pxH) / 2);
        OffscreenGuardHook.PinBegin(main.Position); // 진단 override: 이어지는 크기 변경(SetWindowPlacement)의 화면 안 보정을 되돌린다
        main.UpdateLayout(); // ⑤⑥ 과 같이 OS 창 크기를 즉시 반영(초기화 직후 한 프레임 옛 크기 배치 방지, lead 03:5x)
        var sizeFix = WindowFrameMetrics.EnsureClientSize(main, w, h); // OS 스냅 창은 Avalonia 가 크기 변경을 건너뛴다(otest-12) — Win32 직접 보정
        OffscreenGuardHook.PinEnd();
        Log($"window-cmd ④ 메인 초기화 screen={Rect(wa)} size={w:F0}x{h:F0}dip pos=({main.Position.X},{main.Position.Y}){sizeFix}");
        return true;
    }

    // ⑤⑥ Normal 상태를 유지한 채 바깥 틀이 target(px)과 같아지도록 위치·크기를 맞춘다(Mars 실측: OS 최대화 아님, Bounds 기준 → 작업표시줄을 가릴 수 있음)
    private static bool FitTo(Window main, PixelRect target, string tag)
    {
        WindowPosTrace.Begin(tag);
        // 판정보고서 §⑤⑥(Mars 동일): Normal 상태, 클라이언트 크기 = 대상 Bounds, 위치 = (X−7, Y). Avalonia Width/Height 는 클라이언트 크기(DIP)라 틀 크기를 빼지 않는다.
        WindowFrameMetrics.RestoreIfArranged(main); // ④ 와 같은 스냅 상태 복원
        main.WindowState = WindowState.Normal;
        var scale = main.DesktopScaling;
        var (fw, fh) = WindowFrameMetrics.FrameExtra(main);
        var beforeClient = main.ClientSize;
        main.Position = new PixelPoint(target.X - MarsFitOffsetX, target.Y);
        OffscreenGuardHook.PinBegin(main.Position); // 진단 override: 크기 변경(SetWindowPlacement)이 화면 밖 창을 화면 안으로 보정하는 것을 되돌린다
        MaxTrackHook.AllowFitTrack(main, target, scale); // 제목줄 off 에서 창 높이가 SM_CYMAXTRACK 에 잘려 client 1426 이 되던 Q6 #5 FAIL 대응
        main.Width = Math.Max(200, target.Width / scale);
        main.Height = Math.Max(150, target.Height / scale);
        // Width/Height 는 다음 레이아웃 패스에서 OS 창 크기로 반영된다 → 같은 턴의 원격 after 스냅샷이 옛 크기(1400x900)를 읽던 Q6 #5·#6 FAIL. 레이아웃을 즉시 돌려 동기 반영한다.
        main.UpdateLayout();
        var sizeFix = WindowFrameMetrics.EnsureClientSize(main, main.Width, main.Height); // ④ 와 같은 스냅 창 크기 보정
        OffscreenGuardHook.PinEnd();
        // 진단 --fit-visible on 일 때만: 보이는 프레임(DWMWA_EXTENDED_FRAME_BOUNDS)을 대상 Bounds 에 맞춘다(i0339 실측 +2/+1px 보정 후보).
        // 실측 여백(창 사각형 − 보이는 프레임, 창 사각형 − 클라이언트)을 쓴다. 기준(§⑤⑥) 재확인 전까지 기본은 끔(lead 03:4x 보류).
        var fitted = FitVisibleFrameEnabled ? WindowFrameMetrics.FitVisibleFrame(main, target, scale) : "visible=off(spec 3.5 client=Bounds, X-7)";
        var covers = main.Screens.All.Any(s => s.WorkingArea != s.Bounds && target.Intersects(s.Bounds) && !s.WorkingArea.Contains(target.Intersect(s.Bounds)));
        Log($"window-cmd {tag} target={Rect(target)} scale={scale} frameExtra={fw:F0}x{fh:F0}dip pos=({main.Position.X},{main.Position.Y}) client {beforeClient.Width:F0}x{beforeClient.Height:F0}→{main.ClientSize.Width:F0}x{main.ClientSize.Height:F0}dip {fitted} 작업표시줄가림={covers}{sizeFix}");
        return true;
    }


    // ⑦ SDI 창 전부를 메인 창 왼쪽 위 기준 30px 계단식으로 옮기고(크기 유지), 화면 밖이면 MDI-12 정상화
    // ⑦ 공용 공식(셸 SdiResetLayout.Compute, v30 — 웹 관리형 창과 같은 규칙): 호스트는 작업 영역 제공과 창 이동만 맡는다.
    // 단위는 물리 px(PixelPoint·WorkingArea 와 같게 Size = FrameSize×RenderScaling). 위치 고정(IsLocked) 창은 옮기지 않는다. 크기는 바꾸지 않는다.
    private static bool ResetSdi(Window main)
    {
        var wins = s_sdi.Where(w => w.IsVisible).ToArray();
        var inputs = new List<SdiResetWindow>(wins.Length);
        foreach (var w in wins)
        {
            var locked = SdiWindowHost.IsLocked(w);
            if (!locked && w.WindowState != WindowState.Normal)
            {
                w.WindowState = WindowState.Normal;
            }

            var s = w.RenderScaling;
            var size = w.FrameSize ?? w.ClientSize;
            inputs.Add(new SdiResetWindow(new Point(w.Position.X, w.Position.Y), new Size(size.Width * s, size.Height * s), locked));
        }

        var primary = main.Screens.Primary ?? main.Screens.All[0];
        var areas = new List<Rect>();
        if (PrimaryOverride is { } po)
        {
            areas.Add(new Rect(po.X, po.Y, po.Width, po.Height)); // 진단: 시험 창이 사용자 화면에 나오지 않게
        }
        else
        {
            areas.AddRange(main.Screens.All.OrderBy(sc => sc == primary ? 0 : 1).Select(sc => new Rect(sc.WorkingArea.X, sc.WorkingArea.Y, sc.WorkingArea.Width, sc.WorkingArea.Height)));
        }

        var result = SdiResetLayout.Compute(areas, WindowCommands.SdiDefaultPosition(areas[0]), inputs);
        var moved = 0;
        for (var i = 0; i < wins.Length && i < result.Count; i++)
        {
            if (result[i] is { } p)
            {
                wins[i].Position = new PixelPoint((int)Math.Round(p.X), (int)Math.Round(p.Y));
                moved++;
            }
        }

        Log($"window-cmd ⑦ SDI reset moved={moved} locked={inputs.Count(x => x.Locked)} area0={areas[0]} [{string.Join(" ", result.Select(p => p is { } q ? $"{q.X:F0},{q.Y:F0}" : "locked"))}]");
        if (moved > 0)
        {
            xkxk.frameworks.ui.shell.App.NotifyLayoutChanged(); // 끌기 없는 위치 변경 — 자동 저장 dirty(D-5 와 같은 규칙, lead 승인)
        }

        return true;
    }

    private static string Rect(PixelRect r) => $"{r.X},{r.Y},{r.Width}x{r.Height}";

    private static void Log(string message) => Program.Log(message);
}
