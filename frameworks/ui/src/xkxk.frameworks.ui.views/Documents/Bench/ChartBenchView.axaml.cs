// 차트 벤치 화면 코드 — 정적(첫 표시·전체 다시 그리기 FPS·메모리)과 롤링 스트리밍(FPS·실제 반영 점/초) 시나리오. 버튼과 원격 명령(bench.chart)이 같은 경로
using xkxk.frameworks.ui.shell;
using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace xkxk.frameworks.ui.views.Documents.Bench;

public partial class ChartBenchView : UserControl
{
    private IChartAdapter? _adapter;
    private bool _busy;

    public ChartBenchView()
    {
        InitializeComponent();
        LibBox.ItemsSource = ChartAdapters.Available.ToArray();
        LibBox.SelectedIndex = 0;
    }

    public JsonObject? LastResult { get; private set; }

    private TopLevel Top => TopLevel.GetTopLevel(this) ?? throw new InvalidOperationException("벤치 화면이 아직 창에 붙지 않았습니다.");

    /// <summary>
    /// scenario=static|stream|all. 인자: lib(scottplot|procharts|livecharts) points(100000) redrawSeconds(2)
    /// seconds(5) rate(점/초, 1000) window(10000) animate(정적 false·스트림 true 기본) maxPoints(2000, ProCharts 다운샘플 상한 — 0=끔) downsample(Lttb).
    /// v44 공정 비교 인자(team-lead 승인 2026-10-29, D-50):
    ///   ds = legacy(기본, 옛 동작: ProCharts 만 자체 다운샘플) | raw(모든 라이브러리 원본 그대로, ProCharts 다운샘플 끔)
    ///        | lttb(벤치가 같은 LTTB 로 dsPoints 점까지 줄여 모든 라이브러리에 같은 배열을 넘김, ProCharts 자체 다운샘플 끔). dsPoints(2000).
    ///   fill = true 면 스트리밍 측정 전에 창(window)을 먼저 채운다 — '창 1만 점이 찬 뒤' 구간만 잰다. 60초 판정은 seconds=60.
    ///   정적·스트리밍 모두 이전 차트를 치운 뒤 전체 GC 를 하고 기준 메모리를 잰다(memory_delta — 라이브러리별 증가량·점당 바이트).
    /// </summary>
    public async Task<JsonObject> RunAsync(string scenario, JsonObject a)
    {
        if (_busy) { throw new InvalidOperationException("다른 벤치 시나리오가 실행 중입니다."); }
        _busy = true;
        try
        {
            var lib = GridBenchView.Str(a, "lib", LibBox.SelectedItem as string ?? "scottplot");
            if (LibBox.ItemsSource is string[] libs && Array.IndexOf(libs, lib) >= 0) { LibBox.SelectedItem = lib; } // 원격 lib 를 화면 콤보에도 반영(otest-1 관찰)
            var r = new JsonObject { ["scenario"] = scenario, ["lib"] = lib, ["at"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") };
            switch (scenario)
            {
                case "static": r["static"] = await StaticAsync(lib, a); break;
                case "stream": r["stream"] = await StreamAsync(lib, a); break;
                case "all":
                    r["static"] = await StaticAsync(lib, a);
                    r["stream"] = await StreamAsync(lib, a);
                    break;
                default: throw new ArgumentException($"알 수 없는 scenario: {scenario} (static|stream|all)");
            }

            r["memory_after"] = BenchProbe.Memory(collect: false);
            LastResult = r;
            ResultText.Text = r.ToJsonString(new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
            return r;
        }
        finally { _busy = false; }
    }

    /// <summary>
    /// 글꼴 기본값(team-lead 승인 2026-10-29, 벤치 한정): ScottPlot 만 차트 전용 나눔고딕(nanum)이다. ScottPlot × Noto KR 조합에서
    /// 회전 축 이름·범례가 깨지기 때문이다(logs/odevgc/spfont). LiveCharts2·ProCharts 는 앱 글꼴 Noto(noto)를 쓴다. font= 를 주면 그 글꼴로 대조할 수 있다.
    /// </summary>
    private static string DefaultFont(string lib) => lib.Equals("scottplot", StringComparison.OrdinalIgnoreCase) ? "nanum" : BenchFonts.DefaultKey;

    private static ChartBenchOptions Options(JsonObject a, bool animateDefault, string lib) => new(
        Title: GridBenchView.Str(a, "title", "실시간 공정률 추이 (단위: %)"),
        XName: "시간(샘플)",
        YName: "측정값",
        Animate: a.ContainsKey("animate") ? GridBenchView.Bool(a, "animate") : animateDefault,
        MaxPoints: GridBenchView.Int(a, "maxPoints", 2000),
        Downsample: GridBenchView.Str(a, "downsample", "Lttb"),
        Font: GridBenchView.Str(a, "font", DefaultFont(lib)), // noto(앱 글꼴) | nanum(차트 전용) | os
        Anim: GridBenchView.Str(a, "anim", "off").ToLowerInvariant()); // off | proto(ScottPlot 경량 애니메이션 시제품, M10)

    private IChartAdapter Fresh(string lib)
    {
        ChartHost.Content = null;
        _adapter?.Dispose();
        _adapter = null;
        return ChartAdapters.Create(lib);
    }

    private async Task<JsonObject> StaticAsync(string lib, JsonObject a)
    {
        var points = GridBenchView.Int(a, "points", 100_000);
        var ds = GridBenchView.Str(a, "ds", "legacy").ToLowerInvariant();
        var dsPoints = GridBenchView.Int(a, "dsPoints", 2000);
        var raw = BenchSignal.RandomWalk(points, 1);
        // lttb 모드: 벤치가 같은 알고리즘으로 줄인 같은 배열을 세 라이브러리에 넘긴다(x 는 균등 간격으로 단순화 — 벤치 한정)
        var ys = ds == "lttb" ? BenchSignal.Lttb(raw, dsPoints) : raw;
        var o = Options(a, animateDefault: false, lib);
        if (ds is "raw" or "lttb") { o = o with { MaxPoints = 0 }; } // ProCharts 자체 다운샘플 끔 → 세 라이브러리 같은 조건
        // 이전 차트를 먼저 치우고(Fresh) 전체 GC 뒤 기준을 잰다 — 옛 순서는 기준을 잰 뒤 이전 차트를 치워 증가량이 작게 나왔다
        var adapter = Fresh(lib);
        await BenchProbe.NextFrameAsync(Top);
        var memBefore = BenchProbe.Memory(collect: true);
        var showMs = await BenchProbe.TimeToFramesAsync(Top, () =>
        {
            ChartHost.Content = adapter.View;
            adapter.LoadStatic(ys, o);
        });
        _adapter = adapter;
        var memShown = BenchProbe.Memory(collect: true);
        // 전체 다시 그리기 비용: 매 프레임 InvalidateVisual(데이터는 그대로) — 패닝·리사이즈 같은 재렌더 상한을 본다
        var allocStart = AllocMark.Now();
        var redraw = await BenchProbe.MeasureFramesAsync(Top, GridBenchView.Num(a, "redrawSeconds", 2), _ => adapter.View.InvalidateVisual());
        var redrawAlloc = AllocMark.Now().Since(allocStart, redraw.Frames);
        return new JsonObject
        {
            ["adapter"] = adapter.Name,
            ["font"] = BenchFonts.SourceOf(o.Font),
            ["points"] = points,
            ["animate"] = o.Animate,
            ["anim"] = o.Anim,
            ["max_points"] = o.MaxPoints,
            ["ds"] = ds,
            ["drawn_points"] = ys.Length,
            ["first_show_ms"] = Math.Round(showMs, 1),
            ["memory_before"] = memBefore,
            ["memory_shown"] = memShown,
            ["memory_delta"] = MemoryDelta(memBefore, memShown, ys.Length),
            ["redraw"] = redraw.ToJson(),
            ["redraw_alloc"] = redrawAlloc,
        };
    }

    private async Task<JsonObject> StreamAsync(string lib, JsonObject a)
    {
        var window = GridBenchView.Int(a, "window", 10_000);
        var rate = GridBenchView.Int(a, "rate", 1000);
        var seconds = GridBenchView.Num(a, "seconds", 5);
        var fill = a.ContainsKey("fill") && GridBenchView.Bool(a, "fill");
        var walk = BenchSignal.RandomWalk(Math.Max(window, rate) * 4, 2);
        var buffer = new double[Math.Max(16, rate)];
        var adapter = Fresh(lib);
        var o = Options(a, animateDefault: true, lib);
        await BenchProbe.NextFrameAsync(Top);
        var memBase = BenchProbe.Memory(collect: true); // 이전 차트를 치운 뒤 기준
        ChartHost.Content = adapter.View;
        adapter.StartStream(window, o);
        _adapter = adapter;
        await BenchProbe.NextFrameAsync(Top);
        long done = 0;
        JsonObject? memFilled = null;
        if (fill)
        {
            // 창을 먼저 채운다(측정 밖). 묶음마다 다음 프레임을 기다려 라이브러리가 실제로 반영하게 한다
            while (done < window)
            {
                var n = (int)Math.Min(window - done, buffer.Length);
                for (var i = 0; i < n; i++) { buffer[i] = walk[(done + i) % walk.Length]; }
                adapter.Append(buffer, n);
                done += n;
                await BenchProbe.NextFrameAsync(Top);
            }

            memFilled = BenchProbe.Memory(collect: true);
        }

        var prefilled = done;
        var allocStart = AllocMark.Now();
        var started = DateTime.UtcNow;
        var stats = await BenchProbe.MeasureFramesAsync(Top, seconds, _ =>
        {
            var due = (long)((DateTime.UtcNow - started).TotalSeconds * rate) - (done - prefilled);
            if (due <= 0) { return; }
            var n = (int)Math.Min(due, buffer.Length);
            for (var i = 0; i < n; i++) { buffer[i] = walk[(done + i) % walk.Length]; }
            adapter.Append(buffer, n);
            done += n;
        });
        var streamAlloc = AllocMark.Now().Since(allocStart, stats.Frames);
        var j = stats.ToJson();
        j["alloc"] = streamAlloc;
        j["adapter"] = adapter.Name;
        j["font"] = BenchFonts.SourceOf(o.Font);
        j["window"] = window;
        j["animate"] = o.Animate;
        j["anim"] = o.Anim;
        j["target_points_per_s"] = rate;
        j["fill"] = fill;
        j["prefilled_points"] = prefilled;
        j["done_points"] = done - prefilled; // 측정 구간에 넣은 점(옛 판과 같은 뜻 — fill=false 면 prefilled=0)
        j["achieved_points_per_s"] = Math.Round((done - prefilled) / Math.Max(0.001, stats.ElapsedMs / 1000.0), 0);
        j["memory"] = BenchProbe.Memory(collect: false);
        var memEnd = BenchProbe.Memory(collect: true);
        j["memory_base"] = memBase;
        if (memFilled is not null) { j["memory_filled"] = memFilled; }
        j["memory_end"] = memEnd;
        // 창이 찬 뒤 측정 구간의 증가량(누수·버퍼 증가 판정). fill=false 면 기준 대비 전체 증가량
        j["memory_delta"] = MemoryDelta(memFilled ?? memBase, memEnd, Math.Min(window, (int)Math.Min(int.MaxValue, done)));
        if (memFilled is not null) { j["memory_delta_fill"] = MemoryDelta(memBase, memFilled, window); }
        return j;
    }

    /// <summary>
    /// 두 메모리 표본(전체 GC 뒤)의 차이와 점당 바이트. 관리 힙은 모든 플랫폼, 작업 집합은 브라우저 제외.
    /// 차이가 음수일 수 있다(GC 시점·플랫폼 반환 차이) — 그대로 기록한다.
    /// </summary>
    // 본문은 BenchProbe.MemoryDelta 로 옮겼다(이 파일 300줄 제한).
    private static JsonObject MemoryDelta(JsonObject before, JsonObject after, int points) => BenchProbe.MemoryDelta(before, after, points);

    private async void OnStatic(object? s, RoutedEventArgs e) =>
        await SafeRun("static", new JsonObject { ["points"] = int.TryParse(PointsBox.Text, out var n) ? n : 100_000 });

    private async void OnStream(object? s, RoutedEventArgs e) => await SafeRun("stream", new JsonObject());

    private async Task SafeRun(string scenario, JsonObject a)
    {
        try { await RunAsync(scenario, a); }
        catch (Exception ex) { ResultText.Text = "Error: " + ex.Message; }
    }
}
