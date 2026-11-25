// PoC 셸 Application 진입 — 호스트 주입점(시작 인자·레이아웃 저장소·메인 창 설정)과 문서 뷰 생성기
using xkxk.frameworks.ui.data;
using xkxk.frameworks.ui.data.Layout;
using xkxk.frameworks.ui.shell.Platform;
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Templates;
using Dock.Model.Core;
using xkxk.frameworks.ui.shell.Diagnostics;
using xkxk.frameworks.ui.shell.Docking;
using xkxk.frameworks.ui.shell.Lifecycle;
using xkxk.frameworks.ui.shell.Theming;
using xkxk.frameworks.ui.shell.ViewModels;

namespace xkxk.frameworks.ui.shell;

public class App : Application
{
    /// <summary>AutomationBridge 인자. 데스크톱=Main args, 브라우저=URL 쿼리를 호스트가 --인자 배열로 변환.</summary>
    public static IReadOnlyList<string> StartupArgs { get; set; } = Array.Empty<string>();

    /// <summary>--fresh 로 시작했는가(기동 자동 복원 생략·종료 자동 저장 끔). 브라우저는 URL ?fresh 가 같은 인자가 된다.</summary>
    public static bool FreshStart { get; private set; }

    /// <summary>레이아웃 저장소. 호스트가 시작 전에 주입(데스크톱 파일 / 브라우저 localStorage).</summary>
    public static ILayoutStore LayoutStore { get; set; } = new MemoryLayoutStore();

    /// <summary>UI 설정(테마·글자 크기·폰트) 저장소. 호스트가 주입(데스크톱 파일 / 브라우저 localStorage). null 이면 저장 안 함.</summary>
    public static ILayoutStore? SettingsStore { get; set; }

    /// <summary>문서 종류 → 뷰 생성기.</summary>
    public static IDocumentViewFactory DocumentViews
    {
        get => s_documentViews ?? ViewRegistry.Current.DocumentViews;
        set => s_documentViews = value;
    }

    private static IDocumentViewFactory? s_documentViews;

    /// <summary>데스크톱 메인 창 생성 직후 호출(화면 밖 배치·위치 정상화 등 호스트 처리).</summary>
    public static Action<Window>? ConfigureMainWindow { get; set; }

    /// <summary>창의 보이는 프레임(물리 px). 데스크톱 호스트가 준다. 없거나 null 이면 창 틀 크기로 본다(알림 패널 비켜 놓기용).</summary>
    public static Func<Window, PixelRect?>? VisibleFrameOf { get; set; }

    /// <summary>데스크톱 SDI(플로팅) 호스트 창 생성기. null 이면 Dock 기본 HostWindow. 같은 UI 스레드에서 생성된다.</summary>
    public static Func<IHostWindow>? CreateHostWindow { get; set; }

    /// <summary>윈도우 메뉴 ④~⑩·⑬ 실행 호스트(데스크톱 = odev-4b, 브라우저 관리형 창 = odev-7c). null 이면 셸 기본 경로만 쓴다.</summary>
    public static IWindowHostCommands? WindowHost { get; set; }

    /// <summary>개인 레이아웃 저장소(UI-11). 호스트가 주입(데스크톱·Android 폴더 / 브라우저 localStorage). null 이면 메모리(종료 시 소멸).</summary>
    public static IKeyValueStore? LayoutLibraryStore { get; set; }

    /// <summary>레이아웃 라이브러리(UI-11) — 메뉴·관리 창·원격 공용 표면.</summary>
    public static ILayoutLibrary? Layouts { get; private set; }

    /// <summary>레이아웃 자동 저장(디바운스, 전 플랫폼). MainView 가 기동 복원 판단 뒤 Start 한다.</summary>
    public static LayoutAutoSaver? AutoSaver { get; private set; }

    /// <summary>즉시 자동 저장(동기) — 웹 pagehide·visibilitychange, Android OnPause·OnStop. 변경이 없거나 자동 저장이 꺼져 있으면 아무것도 하지 않는다. 예외를 던지지 않는다.</summary>
    public static void FlushLayoutAutoSave() => AutoSaver?.Flush();

    /// <summary>셸이 모르는 레이아웃 변경 알림(예: 호스트의 SDI 창 크기 조절 끝) — 디바운스 저장 예약.</summary>
    public static void NotifyLayoutChanged() => AutoSaver?.MarkDirty();

    /// <summary>MainView 생성 후 설정되는 셸 명령 표면.</summary>
    public static IShellCommands? Shell { get; private set; }

    public static MainViewModel? MainViewModel { get; private set; }

    public override void OnFrameworkInitializationCompleted()
    {
        // 시작 인자 해석(--fresh·임시 설정·자동 저장 억제). Android 는 이 시점 뒤에 MainActivity 가 pdargs 를 넣으므로
        // MainView 가 기동 복원을 판단하기 직전에 ApplyStartupArgs 를 한 번 더 부른다(같은 인자면 결과 동일).
        ParseStartupArgs();

        // 실행 격리: 사용자 레이아웃·설정·개인 레이아웃 저장소를 감싼다(FreshGuardStores.cs). --fresh 면 실행 중 메모리,
        // (진단) --profile-dir 면 그 폴더 파일을 쓰고 사용자 파일은 읽지도 쓰지도 않는다. --fresh 가 우선한다.
        // 판정은 호출 때마다 하므로 Android 처럼 인자가 늦게 들어와도 그 뒤 접근부터 적용된다.
        static bool Isolated() => FreshStart;
#if PD_DIAGNOSTICS
        static ILayoutStore? ProfileLayout() => ProfileStores.Layout;
        static ILayoutStore? ProfileSettings() => ProfileStores.Settings;
        static IKeyValueStore? ProfileLibrary() => ProfileStores.Library;
#else
        static ILayoutStore? ProfileLayout() => null;
        static ILayoutStore? ProfileSettings() => null;
        static IKeyValueStore? ProfileLibrary() => null;
#endif
        LayoutStore = new FreshGuardLayoutStore(LayoutStore, Isolated, ProfileLayout);
        SettingsStore = new FreshGuardLayoutStore(SettingsStore ?? new MemoryLayoutStore(), Isolated, ProfileSettings);
        LayoutLibraryStore = new FreshGuardKeyValueStore(LayoutLibraryStore ?? new MemoryKeyValueStore(), Isolated, ProfileLibrary);

        // 앱 모달 입력 관문(설정 창 — 03_2 §8.2)을 Ctrl+휠/Ctrl+0 처리보다 먼저 등록한다(같은 터널 단계 클래스 처리기는 등록 순서대로 돈다).
        ModalScope.InstallGate();

        // 테마·글자 크기·폰트를 화면 생성 전에 반영하고 Ctrl+휠/Ctrl+0 전역 처리를 등록한다.
        Input.SettingsInput.Install(UiSettings.Current, this, SettingsStore); // 설정 시작 순서·전역 입력 처리기(컨트롤 계층)

        var vm = new MainViewModel(LayoutStore);
        MainViewModel = vm;
        ApplyStartupArgs();
        Shell = vm.Commands;
        Layouts = new LayoutLibrary(LayoutLibraryStore ?? new MemoryKeyValueStore(), vm.Commands, LayoutStore, vm.AddLog,
            UiSettings.Current.BackupSource); // ⑫ 전체 백업·복원에 설정 포함(설정 파일 없으면 현재 설정, D-BACKUP-NOSET)
        ViewRegistry.Current.InstallInput(vm); // ② Esc 창닫기 — 메인·SDI 창 공통
        ViewModels.SystemMenu.SystemInfo.ChannelProvider = () => AppChannel.Current; // UI-17 프로그램 정보·업데이트 확인(odev-1r)
        ViewModels.SystemMenu.SystemInfo.RendererProvider = ViewModels.SystemMenuViewModel.RendererText;
        ViewModels.SystemHost.Views = ViewRegistry.Current.SystemViews; // 시스템 메뉴 창 제공자(odev-1r) — 이전엔 창 항목 비활성
        AutoSaver = new LayoutAutoSaver(vm.Commands, vm.Factory, LayoutStore, () => vm.WindowMenu.ShouldAutoSave, vm.AddLog);
        Input.SettingsInput.WindowZoomHandler = vm.Commands.HandleZoomInput; // Ctrl+휠 창 확대 모드(UI-06)
        var view = ViewRegistry.Current.CreateMainView(vm);

        switch (ApplicationLifetime)
        {
            case IClassicDesktopStyleApplicationLifetime desktop:
                var window = new Window { Title = "별별UI", Width = 1400, Height = 900, Content = view };
                ConfigureMainWindow?.Invoke(window);
                window.Closing += async (_, e) => await ShellExitSave.OnClosingAsync(window, vm, e); // 종료 저장 절차는 Lifecycle/ShellExitSave
                ShellPlatform.Attach(this, window, vm); // 트레이(데스크톱 전용, 기본 끔). 캡처 방지는 App.WindowHost
                desktop.MainWindow = window;
                break;
            case ISingleViewApplicationLifetime single:
                single.MainView = view;
                break;
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// 시작 인자를 다시 읽어 이번 실행의 자동 저장 억제·임시 설정을 맞춘다. 앱 초기화 때와 MainView 의 기동 복원 판단 직전에 불린다.
    /// Android 는 Application 초기화가 MainActivity.OnCreate(인텐트 pdargs → StartupArgs) 보다 먼저라, 초기화 때 판단하면
    /// 시나리오 인자를 못 보고 자동 복원이 돌았다(i0156 문서 3→6→9 누적, odev-3 보고). 여러 번 불러도 결과가 같다.
    /// </summary>
    public static void ApplyStartupArgs()
    {
        ParseStartupArgs();

        // 레이아웃 자동 저장·기동 복원은 --fresh 또는 화면 상태를 만드는 시나리오 인자(--open·--cmd 등)일 때만 이번 실행에서 끈다.
        // 환경·계측 인자(--frame-pump·--perf-out 등)는 끄지 않는다(RETEST 3차 FAIL 원인). 제품 빌드는 HasScenarioArgs 가 항상 false.
        // 저장된 '윈도우 자동저장' 설정값은 바꾸지 않는다.
        if (MainViewModel is { } vm)
        {
            vm.WindowMenu.SuppressAutoSave = FreshStart || AutomationBridge.HasScenarioArgs(StartupArgs);
        }
    }

    /// <summary>--fresh 를 소비(자동화 인자에서 뺌)하고 임시 설정 인자(--theme·--font 등 — settings.json 에 저장하지 않음)를 지정한다.</summary>
    private static void ParseStartupArgs()
    {
#if PD_DIAGNOSTICS
        StartupArgs = ProfileStores.Take(StartupArgs); // 진단 --profile-dir: 소비하고 저장소를 그 폴더로(자동화 인자에서는 뺀다)
        StartupArgs = CellFontDiag.Take(StartupArgs); // 진단 --cell-font family|both: 셀 글꼴 셀렉터 몫 측정(Q4 R1_sel, Theming/CellFontDiag.cs)
#endif
        if (StartupArgs.Any(a => string.Equals(a, "--fresh", StringComparison.OrdinalIgnoreCase)))
        {
            FreshStart = true; // 한 번 켜지면 이번 실행 동안 유지(뒤에 다시 불려도 --fresh 는 이미 빠져 있다)
            StartupArgs = StartupArgs.Where(a => !string.Equals(a, "--fresh", StringComparison.OrdinalIgnoreCase)).ToList();
        }

        StartupArgs = xkxk.frameworks.ui.shell.Platform.InputFormFactor.TakeArg(StartupArgs); // 시험 인자 --form-factor touch|desktop: 폼팩터 강제(D-UX1 — 강제값 > 플랫폼)
        StartupArgs = UiSettings.TakeUiModeArg(StartupArgs); // 시험 인자 --ui-mode tablet|phone: 화면 모드를 이번 실행만 강제(저장 안 함)
        StartupArgs = UiSettings.TakeUiStyleArg(StartupArgs); // 시험 인자 --ui-style auto|pc|tablet|mobile: UI 형을 이번 실행만 강제(저장 안 함, --ui-mode 보다 우선)
        UiSettings.Current.SetTransient(AutomationBridge.TransientSettingArgs(StartupArgs));
    }

    /// <summary>앱 종료 요청(자동화 --exit-after·원격). 데스크톱은 메인 창 Close → 위 종료 처리(저장·플러시) → Shutdown. 브라우저는 무동작.</summary>
    public static void RequestExit()
    {
        if (Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (desktop.MainWindow is { } main && !ShellExitSave.Done)
            {
                main.Close();
            }
            else
            {
                desktop.Shutdown();
            }
        }
    }
}
