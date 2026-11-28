// 브라우저(WASM) 호스트 공개 진입점 — main.js 가 URL 쿼리에서 만든 인자·localStorage 저장소·관리형 창을 빌더 옵션(Args·Host)으로 넘겨 앱을 시작한다
using xkxk.frameworks.ui.data.Rendering;
using Avalonia;
using Avalonia.Browser;
using Avalonia.Media;
using Dock.Settings;
using xkxk.frameworks.ui.shell;
using xkxk.frameworks.ui.shell.Framework;
using xkxk.frameworks.ui.shell.Rendering;
using xkxk.frameworks.ui.shell.Theming;
using xkxk.frameworks.ui.shell.ViewModels;

namespace xkxk.frameworks.ui.hosting.browser;

/// <summary>
/// 브라우저 호스트 진입점이다. 실행 프로젝트의 Main 이 <see cref="RunAsync(string[], BrowserHostOptions)"/> 를 호출하면
/// 저장소·폼팩터·렌더 모드·창 크롬 같은 호스트 값을 연결하고 Avalonia 브라우저 앱을 시작한다(UADR-0018 D1).
/// </summary>
public static partial class BrowserHost
{
    // 호스트가 소비하는 인자(값 1개 동반). 나머지는 데스크톱과 같은 AutomationBridge 인자다.
    private const string LayoutKeyArg = "--layout-key";

#if PD_DIAGNOSTICS
    // 진단판만(URL ?diag-depth=skipElementDesign,deferBegin): DepthDiag 진단 키를 이번 실행만 켠다
    private const string DiagDepthArg = "--diag-depth";
#endif

    /// <summary>옵션 없이 브라우저 앱을 시작한다(모듈 0).</summary>
    /// <param name="args">main.js 가 URL 쿼리에서 만든 인자.</param>
    public static Task RunAsync(string[] args) => RunAsync(args, new BrowserHostOptions());

    /// <summary>옵션을 적용해 브라우저 앱을 시작한다. 앱이 첫 프레임을 그린 뒤 완료된다.</summary>
    /// <param name="args">main.js 가 URL 쿼리에서 만든 인자.</param>
    /// <param name="options">호스트 옵션(모듈 등록 등).</param>
    public static async Task RunAsync(string[] args, BrowserHostOptions options)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(options);
        StartupTiming.TryMark("main-entered"); // 시작 구간 계측(main.js __pdStartup) — 런타임이 C# Main 에 들어온 시점
        var appArgs = new List<string>();
        string? layoutKey = null;
        string? formFactor = null;
        for (var i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], LayoutKeyArg, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                layoutKey = args[++i];
                continue;
            }

            if (string.Equals(args[i], xkxk.frameworks.ui.shell.Platform.InputFormFactor.Arg, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                formFactor = args[++i]; // 시험 강제(touch|desktop) — 셸 자동화 인자로 넘기지 않는다
                continue;
            }

            if (string.Equals(args[i], ReducedMotionArg, StringComparison.OrdinalIgnoreCase))
            {
                var v = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal) ? args[++i] : "on";
                xkxk.frameworks.ui.Motion.MotionPreference.ForcedReduced = !string.Equals(v, "off", StringComparison.OrdinalIgnoreCase); // 시험 강제 — 셸 인자로 넘기지 않는다
                continue;
            }

#if PD_DIAGNOSTICS
            if (string.Equals(args[i], DiagDepthArg, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                DepthDiag.Enable(args[++i]); // 진단 키 diag.depth.*(K13 열기 비용 판별) 이번 실행만 켬 — 셸 인자로 넘기지 않는다
                continue;
            }
#endif

            appArgs.Add(args[i]);
        }

        // OS '애니메이션 줄이기'(MDN prefers-reduced-motion) — 호출 때마다 다시 읽어 실행 중 설정 변경도 반영
        xkxk.frameworks.ui.Motion.MotionPreference.Provider = IsReducedMotion;
        BrowserMotionPreference.Install(); // 디자인 테마 움직임 수준 system 해석용 — 시작 때 값과 변경 구독

        // 폼팩터(REQ-25·D-UX1): PC 브라우저는 데스크톱 앱과, 모바일 브라우저는 같은 기기 앱과 같게 동작하도록 주 포인터가 터치인지 셸에 알린다.
        // 우선순위(lead D-UX1): ① --form-factor 강제값 ② (pointer: coarse) ③ 질의 실패·undefined 면 desktop(로그 1줄).
        var forced = xkxk.frameworks.ui.shell.Platform.InputFormFactor.Parse(formFactor);
        var pointer = forced is null ? IsCoarsePointer() : null;
        xkxk.frameworks.ui.shell.Platform.InputFormFactor.HostTouch = forced ?? pointer ?? false;
        // 기기 정보(정본 08 §1-3-1): 강제값이 없을 때만 웹이 알 수 있는 값을 모아 넣는다. 접힘은 웹에서 판정 불가라 IsFoldableCapable 은 null.
        xkxk.frameworks.ui.shell.Platform.InputFormFactor.HostTraits = forced is null ? CollectTraits(pointer) : null;
        PagePointerLeave.Install();
        PageKeyFocus.Install();

        if (forced is null && pointer is null)
        {
            Console.WriteLine("[host] form-factor: (pointer: coarse) 질의 실패 또는 undefined — desktop 으로 판정");
        }

        Console.WriteLine($"[host] form-factor={(xkxk.frameworks.ui.shell.Platform.InputFormFactor.HostTouch == true ? "touch" : "desktop")} source={(forced is not null ? "arg" : pointer is not null ? "pointer" : "fallback")}");

        // GPU 가속모드(UI-17): Avalonia 초기화 전에 설정 localStorage 를 동기로 읽어 렌더 모드를 정한다(main.js 가 __pdStorage 를 runMain 전에 정의).
        // 우선순위는 인자 --gpu > 설정 "GpuMode" > Auto. --gpu 는 셸 인자로 넘기지 않는다(넘기면 자동화 인자로 보아 레이아웃 자동 저장이 꺼진다).
        var settingsStore = new BrowserLayoutStore(SettingsKey);
        var settingsJson = settingsStore.LoadAsync().Result; // LoadAsync 는 동기 완료(Task.FromResult)
        var gpu = GpuSetting.Resolve(settingsJson, appArgs);
        // 영역 dirty 추적(시작 옵션, 01 §4.2 RenderDirtyRegions): Avalonia CompositionOptions 는 앱 시작 전에만 정할 수 있어 설정을 같은 방식으로 먼저 읽는다
        xkxk.frameworks.ui.shell.Docking.MdiMoveLight.RenderDirtyRegions =
            xkxk.frameworks.ui.shell.Docking.MdiMoveLight.RenderDirtyRegionsFromSettingsJson(settingsJson) ?? xkxk.frameworks.ui.shell.Docking.MdiMoveLight.DefaultRenderDirtyRegions;
        appArgs = [.. GpuSetting.StripArg(appArgs)];
        Console.WriteLine($"[host] browser start args: {string.Join(' ', appArgs)} gpu={gpu}({GpuSetting.AppliedSource})");

        // 제목줄 더블클릭 판정(U9): 누름 간격을 처리 시점이 아니라 브라우저 이벤트 원래 시각으로 잰다(D-C2-19)
        xkxk.frameworks.ui.shell.Docking.TitleDragRestore.PressTimeSource = new BrowserPressTimeSource();
        xkxk.frameworks.ui.shell.Docking.ChromePlatform.HostIsApple = BrowserHostOs.IsApple(); // 창 크롬(결정 ②): Apple 브라우저면 macOS 스타일, 판별 실패면 Windows
        // 브라우저에는 OS 창이 없으므로 SDI 는 Dock 관리형 창으로 대체한다(sprint_contract 보강 2).
        DockSettings.UseManagedWindows = true;
        await InstallBlobStoreAsync(layoutKey); // 배경 이미지 IndexedDB 저장소(BrowserHost.Storage.cs)
        // 브라우저엔 시스템 폰트가 없어 "Noto Sans KR" 은 임베드 폰트로 연결
        UiSettings.FontResolver = name =>
            name == UiSettings.DefaultFontName ? new FontFamily(KoreanFont) : new FontFamily(name);

        // 재시작(GPU 모드 적용): 레이아웃을 즉시 저장한 뒤 페이지를 새로고침한다(같은 URL — 쿼리 인자 유지)
        SystemHost.Restart = () =>
        {
            App.FlushLayoutAutoSave();
            Reload();
        };

        // 실제 렌더러 판정(odev-3): 웹은 WebGL 로 그려도 IPlatformGraphics 가 없어 형식명으로 못 가린다 → 캔버스 컨텍스트를 묻는다.
        // 대입만 기동 전에 하고 호출은 첫 렌더 뒤 RenderInfo 가 한다(그리기 전에 getContext 를 부르면 새 컨텍스트가 생긴다).
        RenderInfo.HostProbe = GetRenderer;

        // Off 면 소프트웨어(Canvas 2D)만. Auto 는 Avalonia 기본(WebGL2 → WebGL1 → Software2D)을 그대로 둔다.
        var platformOptions = gpu == GpuMode.Off ? new BrowserPlatformOptions { RenderingMode = [BrowserRenderingMode.Software2D] } : null;
        // 별별UI 빌더 경유(모듈은 옵션 ConfigureUi 가 등록). 인자·저장소·윈도우 메뉴 호스트는 App 에 직접 대입하지 않고 Host·Args 로 넘긴다(B03) — UseXkxkUi 가 Setup 전에 App 에 연결한다
        var ui = BrowserUiHost.Build(appArgs, h =>
        {
            h.LayoutStore = new BrowserLayoutStore(layoutKey);
            // UI 설정은 별도 localStorage 키
            h.SettingsStore = settingsStore;
            // 개인 레이아웃(UI-11): 자동 저장 슬롯 키와 같은 접미사 규칙으로 시험 간 격리
            h.LayoutLibraryStore = new BrowserKeyValueStore((layoutKey ?? BrowserLayoutStore.DefaultKey) + "-lib");
            // 윈도우 메뉴 호스트(관리형 창, odev-7c 구현): ⑦ 독립모드 위치 초기화만 실행, ④⑤⑥·⑬ 은 브라우저에서 불가
            h.WindowHost = new xkxk.frameworks.ui.shell.Docking.ManagedWindowHostCommands();
        }, m => Console.WriteLine("[host] " + m), options.ConfigureUi);
        var app = BuildAvaloniaApp().UseXkxkUi(ui).AfterSetup(_ => SingleWindowPlatformFix.Apply()); // 웹에는 Window 가 없어 Dock 도구 크롬·고정 패널의 $parent[Window] 바인딩이 오류 로그를 남긴다
        if (xkxk.frameworks.ui.shell.Docking.MdiMoveLight.RenderDirtyRegions)
        {
            // Compositor 가 AvaloniaLocator 에서 읽는다(12.1.4 확인). Skia 는 영역을 지원해 MultiDirtyRectTracker(최대 8개)가 쓰인다
            app = app.With(new Avalonia.Rendering.Composition.CompositionOptions { UseRegionDirtyRectClipping = true });
        }

        Console.WriteLine($"[host] render-dirty-regions={xkxk.frameworks.ui.shell.Docking.MdiMoveLight.RenderDirtyRegions}");
#if PD_DIAGNOSTICS // 바인딩 오류 등 Avalonia 경고를 Trace → Console(브라우저 console)로 낸다(W3 런타임 바인딩 0건 확인용, 제품 빌드 불변)
        System.Diagnostics.Trace.Listeners.Add(new System.Diagnostics.TextWriterTraceListener(Console.Out)); System.Diagnostics.Trace.AutoFlush = true;
        app = app.LogToTrace();
#endif
        await app.StartBrowserAppAsync("out", platformOptions);
        StartupTiming.TryMark("app-started"); // Avalonia 초기화·MainView 연결 끝
        StartupTiming.MarkFirstFrame(); // 첫 프레임이 화면에 반영되면 first-frame(로딩 문구 제거 신호 겸용)
    }
}
