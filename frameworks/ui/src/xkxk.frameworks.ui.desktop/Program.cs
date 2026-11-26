// 데스크톱 호스트 진입점 — 기동 순서(인자·로그·단일 인스턴스·제품 로그·창 옵션)와 호스트 값(Host 옵션) 구성. 인자 해석은 HostArgs, 로그는 HostLog, 창 옵션은 LaunchOptions
using xkxk.frameworks.ui.data;
using xkxk.frameworks.ui.data.Logging;
using xkxk.frameworks.ui.data.Rendering;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Threading;
using Dock.Settings;
using xkxk.frameworks.ui.shell;
using xkxk.frameworks.ui.shell.Diagnostics;
using xkxk.frameworks.ui.shell.Framework;
using xkxk.frameworks.ui.shell.Rendering;

namespace xkxk.frameworks.ui.desktop;

internal static class Program
{
    private static string[] s_args = [];
    private static SingleInstance? s_instance;
    private static FileLogSink? s_fileLog;
    private static Avalonia.Controls.Window? s_main;

    [STAThread]
    public static int Main(string[] args)
    {
#if PD_DIAGNOSTICS // --drag-probe on|full: 끌기 단계별 할당·Measure 수 진단. full 의 Avalonia 진단 스위치는 Avalonia 형식 초기화 전이어야 해서 첫 줄에 둔다
        xkxk.frameworks.ui.shell.Diagnostics.MdiDragProbe.InstallFromArgs(args);
#endif
        PerfProbe.Current.MarkProcessStart(Process.GetCurrentProcess().StartTime.ToUniversalTime());
        s_args = args;
        var host = HostArgs.Parse(args);
        using var log = HostLog.Open(host.Get("--log"));
        AppIdentity.Apply("Projecd.Pd"); // 작업표시줄 식별자(AUMID) — 창 생성(BuildAvaloniaApp) 전, Windows 만(안에서 판정). 로그 열린 뒤라 hr 이 남는다
#if PD_DIAGNOSTICS // 바인딩 오류 등 Avalonia 경고(LogToTrace)를 --log 파일에도 남긴다(W3 런타임 바인딩 0건 확인용)
        System.Diagnostics.Trace.Listeners.Add(new System.Diagnostics.TextWriterTraceListener(Console.Out)); System.Diagnostics.Trace.AutoFlush = true;
#endif
        Log($"host start pid={Environment.ProcessId} args: {string.Join(' ', args)}");
        PerfProbe.Current.SetMetric("host_main_ms", PerfProbe.Current.NowMs);
        HostLog.HookUnhandled();

        try
        {
            var placement = BgPlacement.Parse(host.Get("--bg-pos"));
            var store = new FileLayoutStore(host.LayoutFile());
            Log($"layout-file={store.FilePath} bg-pos={(placement is null ? "off" : placement.Auto ? "auto" : $"{placement.X},{placement.Y}")}");
            var profileDir = Path.GetDirectoryName(store.FilePath) ?? ".";

            // UI-17: 재시작이면 옛 프로세스 종료(자동 저장·Mutex 해제)를 먼저 기다린 뒤 Avalonia 초기화 전에 settings.json 의 두 키만 읽는다
            SingleInstance.WaitForPid(host.Get(SingleInstance.WaitPidArg), Log);
            var settingsJson = ReadText(Path.Combine(profileDir, "settings.json"));
            var gpu = GpuSetting.Resolve(settingsJson, host.AppArgs); // --gpu > settings GpuMode > Auto (이번 실행만, 저장 안 함)
            host.AppArgs = GpuSetting.StripArg(host.AppArgs); // 셸에 넘기면 StartupArgs 가 생겨 자동 저장·복원이 꺼진다
            if (!AcquireSingleInstance(host, settingsJson, gpu, background: placement is not null))
            {
                return 0; // 이미 실행 중 — 기존 인스턴스에 알리고 끝낸다(PoC: 새 창을 만들지 않음)
            }

            ConfigureMotion(host);
            StartProductLog(profileDir, gpu);
            SdiWindowHost.Install(placement);
            ConfigureFloatingOwner();
            BgWindowPolicy.Apply(placement is not null, overlayPopups: placement is not null, Log); // 비전경 시험: 시스템 창·Dock 오버레이 창·(진단)새 창 감시 — 적용 값을 호스트 로그에 남긴다
            LaunchOptions.Apply(host, background: placement is not null);
            var ui = DesktopUiHost.Build(host.AppArgs, h => ConfigureHost(h, host, store), Log); // 별별UI 빌더 경유(모듈 0, 호스트 값은 Host 옵션 → UseXkxkUi 가 App 에 연결). BuildAvaloniaApp 자체는 디자이너용이라 그대로 둔다
            var builder = HostRenderOptions.Apply(BuildAvaloniaApp().UseXkxkUi(ui), host.Get("--render"), host.Get("--composition"), gpu,
                overlayPopups: placement is not null, settingsJson: settingsJson); // 화면 밖 시험: 팝업을 별도 창으로 띄우지 않는다(사용자 화면 노출 방지). settingsJson = 시작 옵션 RenderDirtyRegions
            PerfProbe.Current.SetMetric("host_lifetime_start_ms", PerfProbe.Current.NowMs);
            // UI-08 원격 제어(--remote-control[=port]) — 원본 args 기준, 꺼져 있으면 null. 명령은 UI 스레드로 전달된다.
            using var remote = xkxk.frameworks.ui.shell.Diagnostics.Remote.RemoteControlServer.StartFromArgs(args);
            var code = builder.StartWithClassicDesktopLifetime(host.AppArgs);
            Log($"host exit code={code}");
            return code;
        }
        catch (Exception ex)
        {
            Log($"FATAL host: {ex}");
            return 1;
        }
        finally
        {
            s_instance?.Dispose(); // Mutex 해제 — 재시작한 새 프로세스는 --wait-pid 로 이 종료를 기다린다
            s_fileLog?.Dispose();
        }
    }

    /// <summary>중복모드 끔이면 단일 인스턴스 Mutex 를 잡고 두 번째 실행 알림을 듣는다. 이미 실행 중이면 false.</summary>
    private static bool AcquireSingleInstance(HostArgs host, string? settingsJson, GpuMode gpu, bool background)
    {
        var allowDuplicate = SingleInstance.AllowDuplicateFromSettings(settingsJson) ?? false;
#if PD_DIAGNOSTICS // 연속 기동 시험 전용 우회(lead 승인) — run_desktop_bg.sh 는 기본으로 붙이지 않는다
        allowDuplicate |= host.IsOn("--multi-instance");
#endif
        Log($"gpu={gpu} source={GpuSetting.AppliedSource} allow-duplicate={allowDuplicate} channel={AppChannel.Current}");
        if (allowDuplicate)
        {
            return true;
        }

        s_instance = SingleInstance.TryAcquire(AppChannel.Current, Log);
        s_instance?.Listen(() => Dispatcher.UIThread.Post(() => ActivateMain(background)));
        return s_instance is not null;
    }

    private const string ReducedMotionArg = "--reduced-motion"; // 시험 강제(on|off) — OS 설정은 바꾸지 않는다. 셸 인자로 넘기지 않는다

    // SystemParametersInfo — SPI_GETCLIENTAREAANIMATION(0x1042): BOOL 에 '클라이언트 영역 애니메이션 켜짐' 을 받는다.
    // 출처 https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-systemparametersinfow
    private const uint SpiGetClientAreaAnimation = 0x1042;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, ref int pvParam, uint fWinIni);

    /// <summary>OS '애니메이션 줄이기' 공급자를 MotionPreference 에 넣고 시험 강제 인자 --reduced-motion[=on|off] 를 소비한다. Windows 만 OS 조회, 그 밖(macOS·Linux)은 공급자 없음(애니메이션 사용).</summary>
    private static void ConfigureMotion(HostArgs host)
    {
        var rest = new List<string>(host.AppArgs.Length);
        for (var i = 0; i < host.AppArgs.Length; i++)
        {
            var a = host.AppArgs[i];
            string? value = null;
            if (a.StartsWith(ReducedMotionArg + "=", StringComparison.OrdinalIgnoreCase))
            {
                value = a[(ReducedMotionArg.Length + 1)..];
            }
            else if (string.Equals(a, ReducedMotionArg, StringComparison.OrdinalIgnoreCase))
            {
                value = i + 1 < host.AppArgs.Length && !host.AppArgs[i + 1].StartsWith("--", StringComparison.Ordinal) ? host.AppArgs[++i] : "on";
            }
            else
            {
                rest.Add(a);
                continue;
            }

            xkxk.frameworks.ui.Motion.MotionPreference.ForcedReduced = !string.Equals(value, "off", StringComparison.OrdinalIgnoreCase);
        }

        host.AppArgs = [.. rest];
        if (OperatingSystem.IsWindows())
        {
            xkxk.frameworks.ui.Motion.MotionPreference.Provider = () =>
            {
                var enabled = 1;
                return SystemParametersInfo(SpiGetClientAreaAnimation, 0, ref enabled, 0) && enabled == 0; // 애니메이션이 꺼져 있으면 줄이기
            };
        }

        Log($"motion forced={xkxk.frameworks.ui.Motion.MotionPreference.ForcedReduced?.ToString() ?? "none"} provider={(xkxk.frameworks.ui.Motion.MotionPreference.Provider is null ? "none" : "windows-spi")}");
    }

    /// <summary>
    /// SDI(플로팅 OS 창) owner 끊기 — 메인 최소화·숨김에 SDI 가 끌려가지 않게 한다. Z 는 설정 '항상 메인 창 위'(SdiAboveMainHook)가 앱에서 유지한다.
    /// Dock.Settings 12.1.0.12 문서: NeverOwned = "Never assign an owner for floating windows." 웹·Android 는 관리형 창이라 무관(이 호스트만 지정).
    /// owner 가 없으면 OS 가 메인과 함께 SDI 를 닫지 않으므로 메인 닫힘 때 Dock 이 SDI 를 닫게 한다(기본값 false — 디컴파일 DockSettings 확인).
    /// </summary>
    private static void ConfigureFloatingOwner()
    {
        // OwnerMode 우선순위 확인: Dock.Avalonia 12.1.0.12 HostWindow.ResolveOwnerWindow(ilspycmd 디컴파일) — 창별 IDockWindow.OwnerMode 가 Default(0)일 때만
        // FloatingWindowOwnerPolicy==NeverOwned 가 null(owner 없음)을 돌려준다. OwnerMode 를 명시(ParentWindow·RootWindow·DockableWindow)한 창은 그 값이 우선한다.
        // Pd 는 OwnerMode 를 어디서도 지정하지 않아(src 전체 Grep 0건) 모든 SDI 가 Default → NeverOwned 적용. Present(isDialog) 대체 owner 경로도 NeverOwned 면 건너뛴다.
        DockSettings.FloatingWindowOwnerPolicy = DockFloatingWindowOwnerPolicy.NeverOwned;
        DockSettings.CloseFloatingWindowsOnMainWindowClose = true;
        Log($"dock floating owner policy={DockSettings.FloatingWindowOwnerPolicy} close-with-main={DockSettings.CloseFloatingWindowsOnMainWindowClose}");
    }

    /// <summary>UI-17 제품 로그: 프로필 폴더의 logs\pd-YYYYMMDD.log(보존 14일).</summary>
    private static void StartProductLog(string profileDir, GpuMode gpu)
    {
        var logDir = Path.Combine(profileDir, "logs");
        PdLog.FileDirectory = logDir;
        s_fileLog = new FileLogSink(logDir);
        PdLog.AddSink(s_fileLog);
        PdLog.WriteSystem(PdLogLevel.Information, $"host start channel={AppChannel.Current} gpu={gpu} source={GpuSetting.AppliedSource}");
    }

    /// <summary>호스트 값(UiHostOptions — 저장소·주 창 설정·보이는 프레임·SDI 호스트·윈도우 메뉴 호스트)과 App 밖 호스트 훅(재시작·아이콘·모달·원격 판정)을 채운다. 시작 인자는 어댑터가 Args 로 넘긴다(B03).</summary>
    private static void ConfigureHost(UiHostOptions h, HostArgs host, FileLayoutStore store)
    {
        var folder = Path.GetDirectoryName(store.FilePath) ?? ".";
        h.LayoutStore = store;
        h.SettingsStore = new FileLayoutStore(Path.Combine(folder, "settings.json")); // UI 설정(테마·글자 크기·폰트)은 레이아웃 파일과 같은 폴더
        xkxk.frameworks.ui.shell.Theming.DesignEffectsApplier.BlobStore = new xkxk.frameworks.ui.data.Theming.LocalBlobStore(folder); // 배경 이미지 복사본은 같은 앱 로컬 폴더 backgrounds/local/local(21 §6.2)
        h.VisibleFrameOf = WindowFrameMetrics.VisibleFrame;
        SdiDocumentIcon.Install(); // B2: SDI OS 창 아이콘 = 활성 문서 메뉴 아이콘(AppWindowIcon 은 SDI 를 건너뛴다)
        AppWindowIcon.Install(); // AC-026: 작업 표시줄에 뜨는 창 전부에 앱 아이콘(창 생성 전 등록)
        Win32ModalBlock.Install(); // B3: 설정 창 앱 모달의 OS 수준 차단(ModalScope.PlatformBlock = EnableWindow, Windows 만)
        var windowHost = new DesktopWindowHost(); // B05: 윈도우 메뉴 호스트도 합성 지점에서 1회 만든다(진단 자동화와 공유)
        h.ConfigureMainWindow = w =>
        {
            s_main = BgWindowPolicy.Main = w;
            PerfProbe.Current.SetMetric("host_main_created_ms", PerfProbe.Current.NowMs); // 플랫폼 초기화 끝·주 창 생성 시점
            SdiWindowHost.ConfigureMain(w);
            MainWindowChrome.Apply(w); // Q14 확장 제목줄(켜져 있을 때만)
            DesktopWindowHost.AttachMain(w); // 윈도우 메뉴 ④~⑦·⑬ 호스트(메인 창 참조·캡처 방지 적용)
#if PD_DIAGNOSTICS // 시험 빌드 전용 호스트 자동화(제품 빌드에서는 인자를 무시)
            if (LaunchOptions.MainSize is { } mainSize)
            {
                (w.Width, w.Height) = (mainSize.Width, mainSize.Height); // 진단 --main-size: 표시 전 클라이언트 크기(화면 밖 큰 창 캡처)
            }

            if (host.Get("--host-cmd") is { Length: > 0 } hostCmd)
            {
                DesktopWindowHost.RunScripted(windowHost, w, hostCmd); // 시험 전용 호스트 자동화
            }
#endif
            w.Opened += (_, _) => HostRenderOptions.LogActive(w, Log);
        };
        // UI-17 GPU [지금 다시 시작]: 새 프로세스(--wait-pid) 기동 뒤 평소대로 종료(레이아웃 자동 저장 → Mutex 해제)
        xkxk.frameworks.ui.shell.ViewModels.SystemHost.Restart = () =>
        {
            if (StartSuccessor() && Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime lt)
            {
                lt.Shutdown();
            }
        };
        h.CreateHostWindow = () => new SdiWindowHost();
        var monitors = new DesktopMonitorProvider(); // B05: 호스트 인스턴스는 합성 지점에서 1회 만든다(옛 단일 인스턴스 접근자 제거)
        h.WindowHost = windowHost; // 윈도우 메뉴 ④~⑦·⑬ 호스트 계약(IWindowHostCommands)
        h.MonitorProvider = monitors; // U20 모니터 목록 제공자(창 ▸ 멀티 모니터) — UseXkxkUi 가 앱 컨텍스트에 연결(B05)
        MonitorFitService.Register(h, monitors); // 최대화 때 선택 모니터로 옮김 + 호스트 내부 도우미 공유
#if PD_DIAGNOSTICS
        DesktopDiagHooks.Install(); // 원격 판정 훅(캡처 방지·SDI OS Z 순서) — 진단 빌드 전용
#endif
        // 사용자 레이아웃 라이브러리(이름 저장·불러오기·이름 변경·삭제): layout.json 과 같은 폴더의 layouts
        h.LayoutLibraryStore = new xkxk.frameworks.ui.data.Layout.FileKeyValueStore(Path.Combine(folder, "layouts"));
    }

    /// <summary>GPU [지금 다시 시작](UI-17 §3.5) 데스크톱 진입점 — 셸이 설정 저장 뒤 부르고, true 면 평소대로 앱을 종료한다.</summary>
    public static bool StartSuccessor() => SingleInstance.StartSuccessor(s_args, Log);

    /// <summary>두 번째 실행 알림: 주 창을 복원·활성화한다. 비전경 시험 모드(--bg-pos)에서는 전경을 뺏지 않고 로그만 남긴다.</summary>
    private static void ActivateMain(bool background)
    {
        Log($"single-instance activate request background={background}");
        if (background || s_main is not { } w)
        {
            return;
        }

        xkxk.frameworks.ui.shell.Platform.ShellPlatform.RestoreMain(w, activate: true); // 트레이로 숨긴 창(MDI-21)도 Show — 최소화 복원·활성화와 함께
    }

    private static string? ReadText(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (Exception)
        {
            return null; // 읽기 실패는 기본값으로 — 기동을 막지 않는다
        }
    }

    // Avalonia 디자이너도 이 메서드를 사용한다.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<xkxk.frameworks.ui.views.PdApp>()
            .UsePlatformDetect()
            .LogToTrace();

    /// <summary>호스트 로그 한 줄(DesktopWindowHost·MainWindowChrome·SdiWindowHost 가 부른다). 실제 출력은 HostLog.</summary>
    internal static void Log(string message) => HostLog.Write(message);
}
