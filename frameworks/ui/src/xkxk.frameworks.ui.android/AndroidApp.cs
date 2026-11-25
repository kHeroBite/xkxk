// Android 호스트 진입(Application) — 앱 전용 저장소·관리형 창·임베드 한글 폰트를 App 에 주입하고 Debug 빌드에서 원격 제어 서버를 켠다
using xkxk.frameworks.ui.data;
using Android.App;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;
using Avalonia.Media;
using Dock.Settings;
using xkxk.frameworks.ui.shell;
using xkxk.frameworks.ui.shell.Diagnostics.Remote;
using xkxk.frameworks.ui.shell.Theming;

namespace xkxk.frameworks.ui.android;

/// <summary>
/// Avalonia 12 Android 진입점(AvaloniaAndroidApplication&lt;App&gt;). 화면은 ISingleViewApplicationLifetime 으로 뜨고,
/// SDI 는 브라우저처럼 Dock 관리형 창으로 대체한다(Android 에는 OS 다중 창이 없다).
/// </summary>
[Application(Label = "별별UI")]
public sealed class AndroidApp : AvaloniaAndroidApplication<xkxk.frameworks.ui.views.PdApp>
{
    // 임베드 한글 폰트(브라우저판 Assets/Fonts 링크, 패밀리명 "Noto Sans KR")
    private const string KoreanFont = "avares://xkxk.frameworks.ui.android/Assets/Fonts#Noto Sans KR";

    public AndroidApp(IntPtr javaReference, JniHandleOwnership transfer)
        : base(javaReference, transfer)
    {
    }

    public override void OnCreate()
    {
        base.OnCreate(); // 플랫폼·App·MainView 생성까지 여기서 끝난다. 앱 창은 이 뒤에야 만들어진다
        StartupMarks.Mark("app_setup_done");
    }

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        Console.WriteLine($"[host] android start pid={Environment.ProcessId}");
        StartupMarks.Mark("app_builder_begin");
        ReadStartupArgsFile();
        DockSettings.UseManagedWindows = true;
        var layoutStore = new AndroidLayoutStore("layout.json");
        App.LayoutStore = layoutStore;
        App.SettingsStore = new AndroidLayoutStore("settings.json");
        // 개인 레이아웃·전체 백업(D-ANDROID-LAYOUTSTORE): layout.json 과 같은 앱 전용 폴더의 layouts — 데스크톱과 같은 FileKeyValueStore.
        // 이전엔 설정하지 않아 App 이 메모리 저장소로 대신했고, 앱을 다시 켜면 사라졌다.
        App.LayoutLibraryStore = new xkxk.frameworks.ui.data.Layout.FileKeyValueStore(
            System.IO.Path.Combine(System.IO.Path.GetDirectoryName(layoutStore.FilePath)!, "layouts"));
        xkxk.frameworks.ui.shell.Theming.DesignEffectsApplier.BlobStore = new xkxk.frameworks.ui.data.Theming.LocalBlobStore(System.IO.Path.GetDirectoryName(layoutStore.FilePath)!); // 배경 이미지 복사본은 앱 전용 폴더 backgrounds/local/local(21 §6.2)
        UiSettings.FontResolver = name =>
            name == UiSettings.DefaultFontName ? new FontFamily(KoreanFont) : new FontFamily(name);
        AttachLogSinks();
        var customized = base.CustomizeAppBuilder(builder).With(new FontManagerOptions
        {
            DefaultFamilyName = KoreanFont,
            FontFallbacks = [new FontFallback { FontFamily = new FontFamily(KoreanFont) }],
        }).AfterSetup(_ =>
        {
            StartupMarks.Mark("app_init_done"); // App.Initialize 뒤, OnFrameworkInitializationCompleted 앞이다. 이 뒤 app_setup_done 까지가 뷰모델·MainView 생성 몫이다
            xkxk.frameworks.ui.shell.Theming.SingleWindowPlatformFix.Apply(); // Android 에도 Window 가 없어 Dock 테마의 $parent[Window] 바인딩 오류가 난다
            // D-C3-19: 원격 서버는 Setup(UI 스레드 Dispatcher 초기화) 뒤에 연다. 앞에서 열면 /api/health 요청이 스레드풀에서
            // Dispatcher.UIThread 를 먼저 만들어 AndroidPlatform.Initialize 가 'different thread owns it' 으로 죽는다(c3dev_f 새 설치 2/2).
            StartRemoteControl();
        });
#if PD_DIAGNOSTICS // 바인딩 오류 등 Avalonia 경고를 Trace → Console(logcat)로 낸다(W3 런타임 바인딩 0건 확인용, 제품 빌드 불변)
        System.Diagnostics.Trace.Listeners.Add(new System.Diagnostics.TextWriterTraceListener(Console.Out)); System.Diagnostics.Trace.AutoFlush = true;
        customized = customized.LogToTrace();
#endif
        return ApplyGpuMode(customized);
    }

    /// <summary>제품 로그 싱크(UI-17): logcat(표준 출력, 항상) + 앱 전용 폴더 xkxk/frameworks/ui/logs/pd-YYYYMMDD.log(14일 보존).</summary>
    private static void AttachLogSinks()
    {
        xkxk.frameworks.ui.data.Logging.PdLog.AddSink(new xkxk.frameworks.ui.data.Logging.ConsoleLogSink(onlyInDebugMode: false));
        var dir = System.IO.Path.Combine(AppDataFolder.Root, "logs");
        xkxk.frameworks.ui.data.Logging.PdLog.FileDirectory = dir;
        xkxk.frameworks.ui.data.Logging.PdLog.AddSink(new xkxk.frameworks.ui.data.Logging.FileLogSink(dir));
        xkxk.frameworks.ui.data.Logging.PdLog.WriteSystem(xkxk.frameworks.ui.data.Logging.PdLogLevel.Information,
            $"start android channel={xkxk.frameworks.ui.data.AppChannel.Current} debug={xkxk.frameworks.ui.data.Logging.PdLog.DebugMode}");
    }

    /// <summary>
    /// GPU 가속모드(settings.json "GpuMode", 인자 --gpu)를 Avalonia 초기화 전에 읽어 렌더링 모드에 반영한다.
    /// Auto 는 옵션을 주지 않는다(Avalonia 기본 Egl→Software). Off 는 Software 만. 렌더링 모드는 기동 때 한 번만 정해지므로 바꾸면 다시 실행해야 적용된다.
    /// </summary>
    private static AppBuilder ApplyGpuMode(AppBuilder builder)
    {
        string? json = null;
        try
        {
            var path = new AndroidLayoutStore("settings.json").FilePath;
            json = System.IO.File.Exists(path) ? System.IO.File.ReadAllText(path) : null; // 동기 1회 읽기(작은 파일) — 비동기 대기로 기동을 막지 않는다
        }
        catch (Exception ex)
        {
            Console.WriteLine("[render] settings 읽기 실패 — 기본(Auto): " + ex.Message);
        }

        var mode = xkxk.frameworks.ui.data.Rendering.GpuSetting.Resolve(json, App.StartupArgs);
        App.StartupArgs = xkxk.frameworks.ui.data.Rendering.GpuSetting.StripArg(App.StartupArgs); // --gpu 는 셸에 넘기지 않는다(넘기면 자동 저장·복원이 꺼진다)
        Console.WriteLine($"[render] gpu_mode={mode} source={xkxk.frameworks.ui.data.Rendering.GpuSetting.AppliedSource}");
        return mode == xkxk.frameworks.ui.data.Rendering.GpuMode.Off
            ? builder.With(new AndroidPlatformOptions { RenderingMode = [AndroidRenderingMode.Software] })
            : builder;
    }

#if PD_DIAGNOSTICS
    /// <summary>진단 인자를 앱 전용 파일에서 읽었는가(MainActivity 인텐트 pdargs 보다 우선 — 둘 다 오면 파일, lead 02:3x).</summary>
    internal static bool ArgsFromFile { get; private set; }
#endif

    /// <summary>진단 빌드에서만 동작(제품 빌드는 빈 메서드). 설명은 본문 진단 구간 안에 둔다(Q9 소스 검사 대상 문자열 0건).</summary>
    private void ReadStartupArgsFile()
    {
#if PD_DIAGNOSTICS
        // 앱 전용 파일 files/pdargs.txt 를 App 초기화(XAML 로드·CreateLayout·TrExtension 판정) 전에 읽어 App.StartupArgs 에 넣고 지운다.
        // 인텐트 pdargs(MainActivity.OnCreate)는 Application 초기화보다 늦어 --loc static·--notify-panel 이 적용되지 않았다
        // (i0156 실측: 기준·loc static·notify-panel off 시각 트리 모두 2,912 — odev-3 02:3x).
        // 시험기: adb shell run-as xkxk.frameworks.ui sh -c 'echo "--loc static" > files/pdargs.txt' 뒤 기동. 한 번 읽으면 지워 다음 기동에 남지 않는다.
        try
        {
            if (FilesDir?.AbsolutePath is not { } dir) { return; }
            var path = System.IO.Path.Combine(dir, "pdargs.txt");
            if (!System.IO.File.Exists(path)) { return; }
            var text = System.IO.File.ReadAllText(path).Trim();
            System.IO.File.Delete(path); // 1회용
            if (text.Length == 0) { return; }
            App.StartupArgs = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            ArgsFromFile = true;
            Console.WriteLine($"[host] pdargs(file)={text}");
        }
        catch (Exception ex)
        {
            Console.WriteLine("[host] pdargs 파일 읽기 실패(무시): " + ex.Message);
        }
#endif
    }

    /// <summary>
    /// 진단 빌드(PdDiagnostics=true)에서만 원격 제어 서버(기기 안 localhost:5960)를 켠다. 제품 빌드는 Debug·Release 모두 꺼짐.
    /// WSL 에서는 adb forward tcp:5960 tcp:5960 뒤 Windows curl.exe 로 호출한다. 토큰 파일은 앱 전용 폴더(run-as 로 읽음).
    /// 시작 실패(소켓 미지원 등)는 앱 동작을 막지 않고 로그만 남긴다.
    /// </summary>
    private static void StartRemoteControl()
    {
#if PD_DIAGNOSTICS // 진단 빌드(-p:PdDiagnostics=true)에서만 — 제품(기본) 빌드는 Debug 여도 꺼짐(team-lead 23:5x 결정)
        try
        {
            var server = RemoteControlServer.StartFromArgs(["--remote-control"]);
            Console.WriteLine($"[host] remote-control {(server is null ? "off" : server.Url)} info={RemoteControlServer.InfoFilePath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine("[host] remote-control 시작 실패: " + ex);
        }
#endif
    }
}
