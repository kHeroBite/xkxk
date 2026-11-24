// 실행 인자로 문서 열기·배치·SDI·레이아웃 저장/복원·스크린샷·계측을 수행하는 L0 자동화 브리지
#if PD_DIAGNOSTICS // 진단 빌드 전용. 제품 빌드 대체(셸이 부르는 시그니처만 있는 빈 껍데기)는 AutomationBridge.Product.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Pd.Poc.Shell.Docking;

namespace Pd.Poc.Shell.Diagnostics;

/// <summary>
/// 인자: --open grid:N,gantt:N,form:N  --layout cascade|tile|tile-h|tile-v|tabbed  --sdi N  --move-all dx,dy
/// --save-layout  --load-layout  --screenshot path  --perf-out path  --exit-after 초  --bg-pos x,y  --dedupe
/// --cmd close-others|close-all|minimize-all|restore-all|next|prev|redock-all|grid-exercise|gantt-exercise
///       |maximize|restore-max|pin|unpin|tray-on|tray-off|esc-close|dblclick-close
///       |theme-dark|theme-light|theme-next|font-up|font-down|font-reset|wheel-up|wheel-down|reset-font
///       |zoom-in|zoom-out|zoom-reset|zoom-mode-font|zoom-mode-window (쉼표로 여러 개)
/// --font-size n  --font 이름  --theme 테마Id  --zoom 퍼센트(활성 문서 창 배율)
/// ("--key value" 와 "--key=value" 모두 허용, 플래그는 값 1/true 도 허용). bg-pos 는 데스크톱 호스트가 창 표시 전에 소비한다.
/// 실행 순서: load-layout → open → layout → move-all → sdi → cmd → screenshot → save-layout → exit-after 대기 → perf-out → 종료.
/// 인자 해석·분류는 AutomationArgs, --cmd 실행은 AutomationCommandRunner, perf-ui 는 PerfUiRunner, theme-census 는 ThemeCensusRunner.
/// </summary>
public static class AutomationBridge
{
    /// <summary>파일 쓰기 재정의(브라우저 호스트가 다운로드 등으로 바꿀 때). 기본은 File.WriteAllBytesAsync.</summary>
    public static Func<string, byte[], Task>? FileWriter { get; set; }

    /// <summary>perf JSON 의 platform 값.</summary>
    public static string Platform => OperatingSystem.IsBrowser() ? "browser"
        : OperatingSystem.IsAndroid() ? "android" // IsLinux 보다 먼저 판정(Android 도 Linux 계열)
        : OperatingSystem.IsIOS() ? "ios"
        : OperatingSystem.IsWindows() ? "desktop-windows"
        : OperatingSystem.IsMacOS() ? "desktop-macos" : "desktop-linux";

    public static AutomationOptions Parse(IReadOnlyList<string> args) => AutomationArgs.Parse(args);

    /// <summary>시나리오 인자가 하나라도 있으면 true.</summary>
    public static bool HasScenarioArgs(IReadOnlyList<string> args) => AutomationArgs.HasScenarioArgs(args);

    /// <summary>"theme"·"--theme"·"--theme=dark" 어느 모양이든 임시 설정 인자면 true.</summary>
    public static bool IsTransientSettingArg(string name) => AutomationArgs.IsTransientSettingArg(name);

    /// <summary>실제로 넘어온 임시 설정 인자 이름(하이픈 접두 없는 소문자, 예: theme·zoom).</summary>
    public static IReadOnlySet<string> TransientSettingArgs(IReadOnlyList<string> args) => AutomationArgs.TransientSettingArgs(args);

    /// <summary>원격 제어(cmd.run)가 --cmd 명령 1개를 실행하는 진입점 — 시작 인자를 받지 않는 호스트(Android)용.</summary>
    public static Task RunCommandAsync(IShellCommands shell, TopLevel top, string cmd) => AutomationCommandRunner.RunCmdAsync(shell, top, cmd);

    /// <summary>theme-census[:테마Id] 실행(원격 명령 theme.census 진입점). 본문은 ThemeCensusRunner.</summary>
    public static Task ThemeCensusAsync(TopLevel top, string? themeId) => ThemeCensusRunner.ThemeCensusAsync(top, themeId);

    /// <summary>자동화 실행(첫 렌더 이후 UI 스레드에서 1회). 인자에 자동화 동작이 없으면 계측 연결만 하고 끝난다.</summary>
    public static Task RunAsync(IShellCommands shell, TopLevel top, IReadOnlyList<string> args)
    { Rendering.RenderInfo.LogOnce(); return PerfProbe.IsEnabled ? RunCoreAsync(shell, top, args) : Task.CompletedTask; } // 실제 렌더 백엔드 로그 1회(제품과 같음) 뒤 진행. 제품 빌드(PdDiagnostics=false)는 자동화 인자를 해석·실행하지 않는다

    private static async Task RunCoreAsync(IShellCommands shell, TopLevel top, IReadOnlyList<string> args)
    {
        var o = Parse(args);
        var perf = PerfProbe.Current;
        perf.KeepRendering = o.FramePump ?? o.PerfOut is not null; // 계측 실행이면 기본 연속 렌더(유휴 정지 아티팩트 제거)
        if (perf.KeepRendering) { perf.Attach(top); } else { perf.AttachForSampling(top); } // 계측 실행만 연속 체인, 그 밖은 입력 뒤 표본 구간(유휴 연속 렌더 제거)
        var exitCode = 0;
        try
        {
            await WaitFramesAsync(top, 2);
            if (o.FontSize is not null || o.Font is not null || o.Theme is not null || o.Shadow is not null || o.Frame is not null || o.ChromeButtons is not null || o.ChromeInactiveDim is not null || o.ChromeInactiveOpacity is not null || o.FluentOverride is not null || o.ThemeNewTokens is not null || o.FluentPalette is not null) { await Step("ui-settings", top, () => Sync(() => AutomationCommandRunner.ApplyUi(shell, o, false))); }
            if (o.LoadLayout) { await Step("load-layout", top, async () => perf.SetMetric("layout_loaded", await shell.LoadLayoutAsync() ? 1 : 0)); }
            if (o.Open.Count > 0) { await Step("open", top, () => Sync(() => OpenAll(shell, o))); }
            if (o.Layout is { } layout) { await Step("layout:" + layout, top, () => Sync(() => shell.Arrange(ToArrange(layout)))); }
            if (o.Zoom is not null) { await Step("zoom", top, () => Sync(() => AutomationCommandRunner.ApplyUi(shell, o, true))); }
            if (o.MoveAll is { } mv) { await AnimateMoveAsync(shell, top, mv.Dx, mv.Dy); }
            if (o.Sdi > 0) { await Step("sdi", top, () => Sync(() => FloatDocuments(shell, o.Sdi))); }
            foreach (var cmd in o.Cmds)
            {
                await Step("cmd:" + cmd, top, () => AutomationCommandRunner.RunCmdAsync(shell, top, cmd));
                perf.SetMetric("docs_after_" + cmd, shell.Documents.Count);
                perf.SetText("state_after_" + cmd, shell.ActiveDocument?.MdiState.ToString() ?? "none");
            }

            perf.SetMetric("documents_open", shell.Documents.Count);
            perf.SetMetric("doc_count", shell.Documents.Count);
            perf.SetText("mode", o.Layout == "tabbed" ? "tabbed" : "mdi");
            AutomationCommandRunner.RecordUi(shell);
            if (o.Screenshot is { } shot) { await WriteAsync(shot, Capture(top)); }
            if (o.SaveLayout) { await Step("save-layout", top, shell.SaveLayoutAsync); }
            perf.BeginPhase("idle");
            if (o.ExitAfter is { } wait && wait > 0) { await Task.Delay(TimeSpan.FromSeconds(wait)); }
        }
        catch (Exception ex)
        {
            exitCode = 1;
            perf.SetMetric("automation_error", 1);
            await WriteAsync((o.PerfOut ?? o.Screenshot ?? "automation") + ".error.txt", Encoding.UTF8.GetBytes(ex.ToString()));
        }

        perf.BeginPhase(null);
        if (o.PerfOut is { } perfOut)
        {
            var json = perf.ToJson(Platform);
            if (OperatingSystem.IsBrowser()) { Console.WriteLine("PERF_JSON:" + json.ReplaceLineEndings(" ")); } // headless 시험이 console 에서 회수
            await WriteAsync(perfOut, Encoding.UTF8.GetBytes(json));
        }

        if (o.ExitAfter is not null || exitCode != 0) { perf.Stop(); App.RequestExit(); }
    }

    private static ShellArrange ToArrange(string layout) => layout switch
    {
        "cascade" => ShellArrange.Cascade,
        "tile-h" => ShellArrange.TileHorizontal,
        "tile" or "tile-v" => ShellArrange.TileVertical,
        "tabbed" => ShellArrange.Tabbed,
        _ => throw new ArgumentException("알 수 없는 --layout 값: " + layout),
    };

    private static Task Sync(Action action) { action(); return Task.CompletedTask; }

    /// <summary>--open 실행. --dedupe 면 종류명을 고정 제목으로 쓰고 중복 열기를 막는다.</summary>
    private static void OpenAll(IShellCommands shell, AutomationOptions o)
    {
        foreach (var (kind, count) in o.Open)
        {
            for (var i = 0; i < count; i++) { _ = o.Dedupe ? shell.OpenDocument(kind, kind.ToString(), allowDuplicate: false) : shell.OpenDocument(kind); }
        }
    }

    /// <summary>문서 n 개를 SDI 로 분리한다. 열린 문서가 모자라면 그리드 문서를 SDI 로 새로 연다.</summary>
    private static void FloatDocuments(IShellCommands shell, int n)
    {
        var docs = new List<PocDocument>(shell.Documents);
        for (var i = 0; i < n; i++)
        {
            if (i < docs.Count) { shell.Float(docs[i]); AutomationCommandRunner.LastFloated = docs[i]; }
            else { AutomationCommandRunner.LastFloated = shell.OpenDocument(DocumentKind.Grid, sdi: true); }
        }
    }

    /// <summary>MDI 전체 이동을 60프레임에 나눠 적용(드래그 근사) — 그 구간 프레임 시간이 phase "move-all" 로 남는다.</summary>
    private static async Task AnimateMoveAsync(IShellCommands shell, TopLevel top, double dx, double dy)
    {
        const int frames = 60;
        var start = PerfProbe.Current.NowMs;
        PerfProbe.Current.BeginPhase("move-all");
        for (var i = 0; i < frames; i++) { shell.MoveAll(dx / frames, dy / frames); await WaitFramesAsync(top, 1); }
        PerfProbe.Current.RecordStep("move-all", PerfProbe.Current.NowMs - start);
    }

    /// <summary>단계 실행 + 2프레임 렌더 대기까지의 소요 ms 를 기록하고 해당 구간을 phase 로 지정.</summary>
    private static async Task Step(string name, TopLevel top, Func<Task> action)
    {
        var start = PerfProbe.Current.NowMs;
        PerfProbe.Current.BeginPhase(name);
        await action();
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background); // Arrange 등 Background 지연 작업 소진
        await WaitFramesAsync(top, 2);
        PerfProbe.Current.RecordStep(name, PerfProbe.Current.NowMs - start);
    }

    /// <summary>애니메이션 프레임 n 개 대기(레이아웃·렌더 반영 보장용).</summary>
    public static Task WaitFramesAsync(TopLevel top, int n)
    {
        var tcs = new TaskCompletionSource();
        var left = n;
        void Tick(TimeSpan _) { if (--left <= 0) { tcs.TrySetResult(); } else { top.RequestAnimationFrame(Tick); } }

        top.RequestAnimationFrame(Tick);
        return tcs.Task;
    }

    /// <summary>주 창(TopLevel) 내용을 PNG 로 캡처. 화면 밖·비전경이어도 시각 트리를 직접 렌더한다(플로팅 OS 창은 미포함).</summary>
    public static byte[] Capture(TopLevel top)
    {
        var scale = top.RenderScaling;
        var size = new PixelSize(Math.Max(1, (int)(top.Bounds.Width * scale)), Math.Max(1, (int)(top.Bounds.Height * scale)));
        using var rtb = new RenderTargetBitmap(size, new Vector(96 * scale, 96 * scale));
        rtb.Render(top);
        using var ms = new MemoryStream();
        rtb.Save(ms, (int?)null);
        return ms.ToArray();
    }

    private static Task WriteAsync(string path, byte[] data)
    {
        if (FileWriter is { } writer) { return writer(path, data); }
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) { Directory.CreateDirectory(dir); }
        return File.WriteAllBytesAsync(path, data);
    }
}
#endif
