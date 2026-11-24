// 브라우저(WASM) 호스트 진입점 — main.js 가 URL 쿼리에서 만든 인자·localStorage 저장소·관리형 창을 App 에 주입
using xkxk.frameworks.ui.data.Rendering;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Browser;
using Avalonia.Media;
using Dock.Settings;
using xkxk.frameworks.ui.shell;
using xkxk.frameworks.ui.shell.Rendering;
using xkxk.frameworks.ui.shell.Theming;
using xkxk.frameworks.ui.shell.ViewModels;

[assembly: SupportedOSPlatform("browser")]

namespace xkxk.frameworks.ui.browser;

internal sealed partial class Program
{
    // 호스트가 소비하는 인자(값 1개 동반). 나머지는 데스크톱과 같은 AutomationBridge 인자다.
    private const string LayoutKeyArg = "--layout-key";

#if PD_DIAGNOSTICS
    // 진단판만(URL ?diag-depth=skipElementDesign,deferBegin): DepthDiag 진단 키를 이번 실행만 켠다
    private const string DiagDepthArg = "--diag-depth";
#endif

    private static async Task Main(string[] args)
    {
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
        App.StartupArgs = appArgs;
        App.LayoutStore = new BrowserLayoutStore(layoutKey);
        // UI 설정은 별도 localStorage 키. 브라우저엔 시스템 폰트가 없어 "Noto Sans KR" 은 임베드 폰트로 연결
        App.SettingsStore = settingsStore;
        // 개인 레이아웃(UI-11): 자동 저장 슬롯 키와 같은 접미사 규칙으로 시험 간 격리
        App.LayoutLibraryStore = new BrowserKeyValueStore((layoutKey ?? BrowserLayoutStore.DefaultKey) + "-lib");
        await InstallBlobStoreAsync(layoutKey); // 배경 이미지 IndexedDB 저장소(Program.Storage.cs)
        // 윈도우 메뉴 호스트(관리형 창, odev-7c 구현): ⑦ 독립모드 위치 초기화만 실행, ④⑤⑥·⑬ 은 브라우저에서 불가
        App.WindowHost = new xkxk.frameworks.ui.shell.Docking.ManagedWindowHostCommands();
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
        var options = gpu == GpuMode.Off ? new BrowserPlatformOptions { RenderingMode = [BrowserRenderingMode.Software2D] } : null;
        var app = BuildAvaloniaApp().AfterSetup(_ => SingleWindowPlatformFix.Apply()); // 웹에는 Window 가 없어 Dock 도구 크롬·고정 패널의 $parent[Window] 바인딩이 오류 로그를 남긴다
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
        await app.StartBrowserAppAsync("out", options);
        StartupTiming.TryMark("app-started"); // Avalonia 초기화·MainView 연결 끝
        StartupTiming.MarkFirstFrame(); // 첫 프레임이 화면에 반영되면 first-frame(로딩 문구 제거 신호 겸용)
    }

    /// <summary>UI 설정 localStorage 키(테마·글자·언어·시스템 메뉴 설정).</summary>
    private const string SettingsKey = "pd-poc-settings";

    [JSImport("globalThis.location.reload")]
    private static partial void Reload();

    [JSImport("globalThis.matchMedia")]
    private static partial JSObject MatchMedia(string query);

    /// <summary>주 포인터가 거친 포인터(터치)인지 — CSS 미디어 기능 pointer: coarse(MDN). 질의 실패·matches 가 불리언이 아니면(undefined 등) null.</summary>
    private static bool? IsCoarsePointer()
    {
        try
        {
            using var mql = MatchMedia("(pointer: coarse)");
            return mql is not null && mql.GetTypeOfProperty("matches") == "boolean" ? mql.GetPropertyAsBoolean("matches") : null;
        }
        catch (JSException)
        {
            return null;
        }
    }

    /// <summary>미디어 쿼리 결과(matches). 질의 실패·불리언이 아니면 null.</summary>
    private static bool? QueryMatches(string query)
    {
        try
        {
            using var mql = MatchMedia(query);
            return mql is not null && mql.GetTypeOfProperty("matches") == "boolean" ? mql.GetPropertyAsBoolean("matches") : null;
        }
        catch (JSException)
        {
            return null;
        }
    }

    /// <summary>
    /// 브라우저가 알려 주는 기기 정보를 DeviceTraits 로 모은다. 근거 MDN pointer·any-pointer·hover 미디어 쿼리
    /// (https://developer.mozilla.org/docs/Web/CSS/@media/any-pointer). 모르는 값은 null 로 둔다.
    /// </summary>
    private static xkxk.frameworks.ui.shell.Platform.DeviceTraits CollectTraits(bool? primaryCoarse)
    {
        // HasTouch: 기존 판정(주 포인터 coarse)이 참이면 터치. 아니면 any-pointer: coarse, 마지막 보조로 maxTouchPoints.
        bool? hasTouch = primaryCoarse;
        if (hasTouch != true)
        {
            var anyCoarse = QueryMatches("(any-pointer: coarse)");
            var touchPoints = ReadDouble(JSHost.GlobalThis.GetPropertyAsJSObject("navigator"), "maxTouchPoints");
            hasTouch = anyCoarse == true || touchPoints > 0 ? true : (primaryCoarse ?? anyCoarse ?? (touchPoints is null ? null : false));
        }

        double? smallest = null;
        try
        {
            using var screen = JSHost.GlobalThis.GetPropertyAsJSObject("screen");
            var w = ReadDouble(screen, "width");
            var h = ReadDouble(screen, "height");
            if (w is > 0 && h is > 0)
            {
                smallest = Math.Min(w.Value, h.Value); // CSS px 로 dp 근사(Android smallestScreenWidthDp 와 같은 단위 관례)
            }
        }
        catch (JSException)
        {
        }

        var inner = ReadDouble(JSHost.GlobalThis, "innerWidth");
        return new xkxk.frameworks.ui.shell.Platform.DeviceTraits(
            xkxk.frameworks.ui.shell.Platform.PlatformKind.Browser,
            HasTouch: hasTouch,
            PrecisePointer: QueryMatches("(any-pointer: fine)"),
            HoverCapable: QueryMatches("(hover: hover)"),
            SmallestWidthDp: smallest,
            WindowWidthDp: inner is > 0 ? inner : null);
    }

    /// <summary>JS 객체의 숫자 속성. 없거나 숫자가 아니면 null.</summary>
    private static double? ReadDouble(JSObject? obj, string name)
    {
        try
        {
            return obj is not null && obj.GetTypeOfProperty(name) == "number" ? obj.GetPropertyAsDouble(name) : null;
        }
        catch (JSException)
        {
            return null;
        }
    }

    /// <summary>시험 강제 인자(on|off, 값 생략 시 on). 사용자 OS 설정은 바꾸지 않는다.</summary>
    private const string ReducedMotionArg = "--reduced-motion";

    /// <summary>CSS 미디어 기능 prefers-reduced-motion: reduce 가 참인지(MDN https://developer.mozilla.org/docs/Web/CSS/@media/prefers-reduced-motion). 질의 실패면 false.</summary>
    private static bool IsReducedMotion()
    {
        try
        {
            using var mql = MatchMedia("(prefers-reduced-motion: reduce)");
            return mql is not null && mql.GetTypeOfProperty("matches") == "boolean" && mql.GetPropertyAsBoolean("matches");
        }
        catch (JSException)
        {
            return false;
        }
    }

    /// <summary>main.js __pdRenderer: 캔버스가 쓰는 컨텍스트 "webgl2"|"webgl1"|"2d"|"none".</summary>
    [JSImport("globalThis.__pdRenderer")]
    private static partial string GetRenderer();

    // 임베드 한글 폰트(NotoSansKR-Regular-subset.ttf, 패밀리명 "Noto Sans KR"). 기본은 글꼴 전용 어셈블리 xkxk.frameworks.ui.fonts(-p:PdFontAssembly=false 면 이 어셈블리)
#if PD_FONT_ASSEMBLY
    private const string KoreanFont = "avares://xkxk.frameworks.ui.fonts/Assets/Fonts#Noto Sans KR";
#else
    private const string KoreanFont = "avares://xkxk.frameworks.ui.browser/Assets/Fonts#Noto Sans KR";
#endif

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<xkxk.frameworks.ui.views.PdApp>()
            .With(new FontManagerOptions
            {
                DefaultFamilyName = KoreanFont,
                FontFallbacks = [new FontFallback { FontFamily = new FontFamily(KoreanFont) }],
            });
}
