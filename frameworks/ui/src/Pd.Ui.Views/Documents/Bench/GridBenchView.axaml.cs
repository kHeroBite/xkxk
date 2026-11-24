// 그리드 벤치 화면 코드 — 시나리오(로드·스크롤·실시간·정렬·필터·xlsx) 실행과 결과 JSON 작성. 버튼과 원격 명령(bench.grid)이 같은 경로를 탄다
using Pd.Poc.Shell;
using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Pd.Ui.Views.Documents.Bench;

public partial class GridBenchView : UserControl
{
    private readonly GridBenchPro _pro = new(); // Pro 전용 기능(필드 초기화자 — base 생성자보다 먼저 만들어져 InitializeComponent 중에도 null 이 아니다)
    private GridBenchModel? _model;
    private bool _busy;

    public GridBenchView()
    {
        InitializeComponent();
        ImplText.Text = GridBenchModel.Implementation;
        _pro.ApplyProFeatures(this, BenchGrid); // ProDataGrid 빌드에서만 내용이 있다(GridBenchPro.cs)
        GridDenseTarget.AttachFixed(BenchGrid, BenchGrid.RowHeight); // 공통 휠 조작만 붙인다. 내장·Pro 대조용이라 표 자체는 DataGrid 그대로 둔다
    }

    /// <summary>마지막 결과(bench.result 가 돌려준다).</summary>
    public JsonObject? LastResult { get; private set; }

    private TopLevel Top => TopLevel.GetTopLevel(this) ?? throw new InvalidOperationException("벤치 화면이 아직 창에 붙지 않았습니다.");

    /// <summary>
    /// 시나리오 실행. scenario=load|scroll|realtime|sort|filter|xlsx|bands|all.
    /// 인자: rows(100000) seconds(5) step(행/프레임, 3) rate(셀/초, 3000) focus(갱신 대상 앞쪽 행 수, 60) statusPct(realtime: 갱신 행 중 Status 변경 비율 %, 0)
    /// property(Name) culture(ko-KR | app=앱 고유 비교자 | ordinal=서수 | 빈 값=현재 문화권) desc(false) text(필터어) latin(filter: 영문 이름 행만 — 영문 정렬 확인용) xlsxRows(10000).
    /// </summary>
    public async Task<JsonObject> RunAsync(string scenario, JsonObject a)
    {
        if (_busy) { throw new InvalidOperationException("다른 벤치 시나리오가 실행 중입니다."); }
        _busy = true;
        try
        {
            var r = new JsonObject { ["scenario"] = scenario, ["impl"] = GridBenchModel.Implementation, ["at"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") };
            switch (scenario)
            {
                case "load": r["load"] = await LoadAsync(Int(a, "rows", 100_000)); break;
                case "scroll": r["scroll"] = await ScrollAsync(Num(a, "seconds", 5), Int(a, "step", 3)); break;
                case "realtime": r["realtime"] = await RealtimeAsync(Num(a, "seconds", 5), Int(a, "rate", 3000), Int(a, "focus", 60), Num(a, "statusPct", 0)); break;
                case "sort": r["sort"] = await SortAsync(Str(a, "property", "Name"), Str(a, "culture", "ko-KR"), Bool(a, "desc")); break;
                case "filter": r["filter"] = await FilterAsync(Str(a, "text", Bool(a, "latin") ? "" : "김"), Bool(a, "latin")); break;
                case "xlsx": r["xlsx"] = Model.ExportXlsx(Int(a, "xlsxRows", 10_000)); break;
                case "bands": r["bands"] = await BandsAsync(); break;
                case "frozen": r["frozen"] = await FrozenAsync(Int(a, "left", 2), Int(a, "right", -1)); break;
                case "pro_features": r["pro_features"] = await _pro.ProFeaturesAsync(BenchGrid, () => Top, _model, Str(a, "mode", "on")); break;
                case "all":
                    r["load"] = await LoadAsync(Int(a, "rows", 100_000));
                    r["scroll"] = await ScrollAsync(Num(a, "seconds", 5), Int(a, "step", 3));
                    r["realtime"] = await RealtimeAsync(Num(a, "seconds", 5), Int(a, "rate", 3000), Int(a, "focus", 60));
                    r["sort"] = await SortAsync("Name", "ko-KR", false);
                    r["filter"] = await FilterAsync(Str(a, "text", "김"));
                    r["filter_clear"] = await FilterAsync("");
                    r["xlsx"] = Model.ExportXlsx(Int(a, "xlsxRows", 10_000));
                    break;
                default: throw new ArgumentException($"알 수 없는 scenario: {scenario} (load|scroll|realtime|sort|filter|xlsx|bands|frozen|pro_features|all)");
            }

            r["memory_after"] = BenchProbe.Memory(collect: false);
            LastResult = r;
            ResultText.Text = Summary(r);
            return r;
        }
        finally { _busy = false; }
    }

    private GridBenchModel Model => _model ?? throw new InvalidOperationException("먼저 scenario=load 로 행을 로드하세요.");

    private async Task<JsonObject> LoadAsync(int rows)
    {
        BenchGrid.ItemsSource = null!; // 다시 로드 전 비우기(ProDataGrid 는 비 null 주석)
        _model = null;
        var memBefore = BenchProbe.Memory(collect: true);
        GridBenchModel? m = null;
        var seedMs = await BenchProbe.TimeToFramesAsync(Top, () => m = new GridBenchModel(rows), frames: 0);
        var showMs = await BenchProbe.TimeToFramesAsync(Top, () => BenchGrid.ItemsSource = m!.Rows);
        _model = m;
        return new JsonObject
        {
            ["rows"] = rows,
            ["seed_ms"] = Math.Round(seedMs, 1),
            ["seed_cached"] = m!.Cached,
            ["first_show_ms"] = Math.Round(showMs, 1),
            ["memory_before"] = memBefore,
            ["memory_loaded"] = BenchProbe.Memory(collect: true),
        };
    }

    private async Task<JsonObject> ScrollAsync(double seconds, int step)
    {
        var m = Model;
        var count = m.Rows.Count;
        var stats = await BenchProbe.MeasureFramesAsync(Top, seconds, i =>
        {
            var idx = Math.Min(count - 1, (i + 1) * Math.Max(1, step) + 30); // 아래로 계속 내려간다(보이는 영역 아래 행을 끌어올림)
            BenchGrid.ScrollIntoView(m.Source[idx % m.Source.Length], null!); // 열 미지정(ProDataGrid 는 비 null 주석)
        });
        var o = stats.ToJson();
        o["step_rows_per_frame"] = step;
        return o;
    }

    private async Task<JsonObject> RealtimeAsync(double seconds, int cellsPerSecond, int focusRows, double statusPct = 0)
    {
        var m = Model;
        BenchGrid.ScrollIntoView(m.Source[0], null!);
        var started = DateTime.UtcNow;
        var flipsBefore = m.StatusFlips;
        long done = 0;
        var stats = await BenchProbe.MeasureFramesAsync(Top, seconds, _ =>
        {
            var due = (long)((DateTime.UtcNow - started).TotalSeconds * cellsPerSecond) - done;
            if (due >= 3) { done += m.UpdateLive((int)(due / 3), focusRows, statusPct); }
        });
        var o = stats.ToJson();
        o["target_cells_per_s"] = cellsPerSecond;
        o["done_cells"] = done;
        o["achieved_cells_per_s"] = Math.Round(done / Math.Max(0.001, stats.ElapsedMs / 1000.0), 0);
        o["focus_rows"] = focusRows;
        if (statusPct > 0)
        {
            // Status 변경 시나리오: 갱신이 끝난 뒤 1프레임 기다려(마지막 변경이 그려질 시간) 강조 셀을 대조한다
            await BenchProbe.NextFrameAsync(Top);
            o["status_pct"] = statusPct;
            o["status_flips"] = m.StatusFlips - flipsBefore;
            o["highlight"] = _pro.LiveHighlight(BenchGrid, _model);
        }
        return o;
    }

    private async Task<JsonObject> SortAsync(string property, string culture, bool desc)
    {
        var m = Model;
        double sortMs = 0;
        var showMs = await BenchProbe.TimeToFramesAsync(Top, () => sortMs = m.Sort(property, desc, culture));
        return new JsonObject
        {
            ["property"] = property,
            ["culture"] = culture,
            ["sort_ms"] = Math.Round(sortMs, 1),
            ["sort_to_frame_ms"] = Math.Round(showMs, 1),
            ["head"] = m.Head(property, 10),
            ["head_distinct"] = m.Head(property, 10, distinct: true), // 서로 다른 값 기준 앞 10개 — 정렬 순서 판정은 이것으로 본다
        };
    }

    private async Task<JsonObject> FilterAsync(string text, bool latinNames = false)
    {
        var m = Model;
        double filterMs = 0;
        var showMs = await BenchProbe.TimeToFramesAsync(Top, () => filterMs = m.Filter(text, latinNames));
        return new JsonObject
        {
            ["text"] = text,
            ["latin_names"] = latinNames,
            ["filter_ms"] = Math.Round(filterMs, 1),
            ["filter_to_frame_ms"] = Math.Round(showMs, 1),
            ["visible_rows"] = m.Rows.Count,
        };
    }

    /// <summary>
    /// 고정 열 수를 바꾼다(가로 스크롤 결함 대조용 — 고정 열이 방아쇠인지 가른다, team-lead 긴급 2026-10-29).
    /// left = 왼쪽 고정 열 수(기본 2), right = 오른쪽 고정 열 수(ProDataGrid 전용, -1 이면 그대로). 바꾼 뒤 2프레임을 기다린다.
    /// </summary>
    private async Task<JsonObject> FrozenAsync(int left, int right)
    {
        BenchGrid.FrozenColumnCount = Math.Max(0, left);
        var rightApplied = _pro.SetFrozenRight(BenchGrid, right);
        await BenchProbe.NextFrameAsync(Top);
        await BenchProbe.NextFrameAsync(Top);
        return new JsonObject { ["left"] = BenchGrid.FrozenColumnCount, ["right"] = rightApplied };
    }

    /// <summary>ProDataGrid 빌드는 GridBenchBands 가 병합 헤더(ColumnBand) 그리드를 만든다. 내장 빌드는 미지원 결과.</summary>
    private Task<JsonObject> BandsAsync() => GridBenchBands.RunAsync(() => Top, GridHost, () => Model);

    private static string Summary(JsonObject r) => r.ToJsonString(new JsonSerializerOptions { WriteIndented = false, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

    // ── 버튼(사람이 직접 볼 때) — 원격 명령과 같은 시나리오 ──
    private async void OnLoad(object? s, RoutedEventArgs e) => await SafeRun("load", new JsonObject { ["rows"] = int.TryParse(RowsBox.Text, out var n) ? n : 100_000 });
    private async void OnScroll(object? s, RoutedEventArgs e) => await SafeRun("scroll", new JsonObject());
    private async void OnLive(object? s, RoutedEventArgs e) => await SafeRun("realtime", new JsonObject());
    private async void OnSort(object? s, RoutedEventArgs e) => await SafeRun("sort", new JsonObject());
    private async void OnFilter(object? s, RoutedEventArgs e) => await SafeRun("filter", new JsonObject { ["text"] = FilterBox.Text ?? "" });
    private async void OnXlsx(object? s, RoutedEventArgs e) => await SafeRun("xlsx", new JsonObject());

    private async Task SafeRun(string scenario, JsonObject a)
    {
        try { await RunAsync(scenario, a); }
        catch (Exception ex) { ResultText.Text = "Error: " + ex.Message; }
    }

    // ── 인자 읽기(원격 JSON) ──
    internal static int Int(JsonObject a, string k, int d) => a[k] is JsonValue v && (v.TryGetValue<int>(out var i) || (v.TryGetValue<string>(out var s) && int.TryParse(s, out i))) ? i : d;
    internal static double Num(JsonObject a, string k, double d) => a[k] is JsonValue v && (v.TryGetValue<double>(out var x) || (v.TryGetValue<string>(out var s) && double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out x))) ? x : d;
    internal static string Str(JsonObject a, string k, string d) => a[k] is JsonValue v && v.TryGetValue<string>(out var s) ? s : d;
    internal static bool Bool(JsonObject a, string k) => a[k] is JsonValue v && ((v.TryGetValue<bool>(out var b) && b) || (v.TryGetValue<string>(out var s) && s.Equals("true", StringComparison.OrdinalIgnoreCase)));
}
