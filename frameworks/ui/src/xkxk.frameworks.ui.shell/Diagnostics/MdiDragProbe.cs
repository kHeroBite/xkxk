// MDI 끌기 단계별(배치·크롬·레이아웃) UI 스레드 할당·Measure/Arrange 호출 수·컴포지터 렌더 시간을 모으는 진단 계측(진단 빌드 전용, A11 원인 확정용)
#if PD_DIAGNOSTICS
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json.Nodes;
using System.Threading;

namespace xkxk.frameworks.ui.shell.Diagnostics;

/// <summary>
/// 켜는 법: 데스크톱 시작 인자 <c>--drag-probe on|full</c>(또는 <c>--drag-probe=on</c>, 환경변수 PD_DRAG_PROBE=on|full). 없으면 모든 진입점이 bool 하나만 보고 끝난다.
/// on   = 단계별 UI 스레드 할당(GC.GetAllocatedBytesForCurrentThread)과 레이아웃 패스 대기 수·간트 그리기 횟수만 센다(Avalonia 동작 불변).
/// full = on + Avalonia 내장 진단(AppContext 스위치 Avalonia.Diagnostics.Diagnostic.IsEnabled)을 켜서 Layoutable.Measure/Arrange 실제 호출 수(ActivitySource 표본 콜백, 활동 객체는 만들지 않음)와
///        컴포지터 렌더·갱신 패스 시간(렌더 스레드, Meter 히스토그램)·UI 측정/배치/그리기 기록 패스 시간을 모은다. 스위치는 Avalonia 의 Diagnostic 형식 초기화 전에만 효과가 있어 Main 첫 줄에서 건다.
///        full 은 콜백 비용이 붙으므로 판정 회차가 아니라 진단 회차에서만 쓴다.
/// 단계: place = MoveAll 의 경계값 변경(PropertyChanged 처리기 포함 — 크롬 단계와 Dock InvalidateArrange 가 이 안에서 돈다), chrome = MdiMoveLightener 이동 판정·원복,
///       layout = Avalonia 레이아웃 패스 1회(PerfLayoutLog 가 받는 시작·끝 로그 사이). 나머지 UI 스레드 할당(그리기 기록·타이머·원격 처리)은 ui_other_bytes 로 남는다.
/// 근거(디컴파일, Dock.Avalonia 12.1.0.12·Avalonia.Base): MdiDocumentWindow 는 MdiBounds 변경마다 MdiLayoutPanel.InvalidateArrange 만 부르고,
/// Layoutable.Arrange 는 같은 사각형이면 바로 돌아간다. 위치만 바뀐 창의 안쪽(간트 등)은 측정·배치·Render 가 다시 돌지 않아야 한다 — 이 계측으로 실측 확인한다.
/// 읽기·초기화는 원격 명령 perf.dragProbe(UI 스레드). 카운터 대부분은 UI 스레드 전용이고, 리스너 콜백(다른 스레드 가능)만 Interlocked·lock 을 쓴다.
/// </summary>
public static class MdiDragProbe
{
    private const string AvaloniaSwitch = "Avalonia.Diagnostics.Diagnostic.IsEnabled";
    private const string AvaloniaSource = "Avalonia.Diagnostic.Source";
    private const string AvaloniaMeter = "Avalonia.Diagnostic.Meter";
    private const int MaxSamples = 100_000;

    private static readonly object s_lock = new();
    private static readonly Dictionary<string, List<double>> s_hist = new()
    {
        ["avalonia.comp.render.time"] = new(), ["avalonia.comp.update.time"] = new(),
        ["avalonia.ui.render.time"] = new(), ["avalonia.ui.measure.time"] = new(), ["avalonia.ui.arrange.time"] = new(),
    };

    private static ActivityListener? s_activity;
    private static MeterListener? s_meter;
    private static bool s_instrumentsSeen;

    // UI 스레드 전용
    private static long s_moves, s_placeBytes, s_placeTicks;
    private static long s_chromeCalls, s_chromeBytes;
    private static int s_chromeDepth;
    private static long s_passes, s_passesFinished, s_layoutBytes, s_queuedMeasure, s_queuedArrange;
    private static double s_layoutMs;
    private static long s_layoutStart = -1;
    private static long s_ganttRenders, s_ganttRenderBytes, s_ganttMeasures;
    private static long s_uiAtReset, s_totalAtReset, s_resetTs;
    private static int s_gc0, s_gc1, s_gc2;

    // 리스너 콜백(스레드 무관)
    private static long s_measureCalls, s_arrangeCalls;

    /// <summary>on 이상이면 true. 끄면 모든 진입점이 이 값만 보고 돌아간다.</summary>
    public static bool Enabled { get; private set; }

    /// <summary>full(Avalonia 내장 진단) 요청 여부.</summary>
    public static bool Full { get; private set; }

    /// <summary>Main 첫 줄에서 부른다(Avalonia Diagnostic 형식 초기화 전이어야 full 이 효과가 있다).</summary>
    public static void InstallFromArgs(string[] args)
    {
        var mode = ArgValue(args, "--drag-probe") ?? Environment.GetEnvironmentVariable("PD_DRAG_PROBE");
        mode = mode?.Trim().ToLowerInvariant();
        if (mode is not ("on" or "full"))
        {
            return;
        }

        Enabled = true;
        Full = mode == "full";
        if (Full)
        {
            AppContext.SetSwitch(AvaloniaSwitch, true);
            s_activity = new ActivityListener
            {
                ShouldListenTo = s => s.Name == AvaloniaSource,
                Sample = OnSample,
            };
            ActivitySource.AddActivityListener(s_activity);
            s_meter = new MeterListener
            {
                InstrumentPublished = (inst, l) =>
                {
                    if (inst.Meter.Name == AvaloniaMeter && s_hist.ContainsKey(inst.Name))
                    {
                        s_instrumentsSeen = true;
                        l.EnableMeasurementEvents(inst);
                    }
                },
            };
            s_meter.SetMeasurementEventCallback<double>(OnMeasurement);
            s_meter.Start();
        }

        Reset();
        Console.WriteLine($"[host] drag-probe={mode}");
    }

    // 표본 판단 콜백에서 이름만 세고 활동 객체는 만들지 않는다(None) — 측정 대상 할당을 늘리지 않게
    private static ActivitySamplingResult OnSample(ref ActivityCreationOptions<ActivityContext> o)
    {
        switch (o.Name)
        {
            case "Avalonia.MeasuringLayoutable": Interlocked.Increment(ref s_measureCalls); break;
            case "Avalonia.ArrangingLayoutable": Interlocked.Increment(ref s_arrangeCalls); break;
        }

        return ActivitySamplingResult.None;
    }

    private static void OnMeasurement(Instrument inst, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags, object? state)
    {
        if (!s_hist.TryGetValue(inst.Name, out var list))
        {
            return;
        }

        lock (s_lock)
        {
            if (list.Count < MaxSamples) { list.Add(value); }
        }
    }

    /// <summary>UI 스레드 구간 할당 측정용(박싱 없는 구조체 — using var 로 쓴다).</summary>
    public readonly struct Scope : IDisposable
    {
        private readonly int _kind; // 0 = 없음, 1 = place, 2 = chrome, 3 = gantt render
        private readonly long _bytes;
        private readonly long _ts;

        internal Scope(int kind)
        {
            _kind = kind;
            _bytes = GC.GetAllocatedBytesForCurrentThread();
            _ts = Stopwatch.GetTimestamp();
        }

        public void Dispose()
        {
            if (_kind == 0) { return; }
            var bytes = GC.GetAllocatedBytesForCurrentThread() - _bytes;
            switch (_kind)
            {
                case 1: s_moves++; s_placeBytes += bytes; s_placeTicks += Stopwatch.GetTimestamp() - _ts; break;
                case 2: s_chromeDepth--; s_chromeCalls++; s_chromeBytes += bytes; break;
                case 3: s_ganttRenders++; s_ganttRenderBytes += bytes; break;
            }
        }
    }

    /// <summary>MoveAll 한 번(경계값 변경 + 동기 PropertyChanged 처리기).</summary>
    public static Scope Place() => Enabled ? new Scope(1) : default;

    /// <summary>MdiMoveLightener 이동 판정·원복(place 안에서 불리면 place 에도 함께 잡힌다). 재진입은 바깥 한 번만 센다.</summary>
    public static Scope Chrome()
    {
        if (!Enabled || s_chromeDepth > 0) { return default; }
        s_chromeDepth++;
        return new Scope(2);
    }

    /// <summary>GanttCanvas.Render 한 번(그리기 기록, UI 스레드).</summary>
    public static Scope GanttRender() => Enabled ? new Scope(3) : default;

    /// <summary>GanttCanvas.MeasureOverride 호출 1회.</summary>
    public static void NoteGanttMeasure()
    {
        if (Enabled) { s_ganttMeasures++; }
    }

    /// <summary>레이아웃 패스 시작(PerfLayoutLog — \"Started layout pass. To measure: {m} To arrange: {a}\").</summary>
    internal static void OnLayoutStarted(int measure, int arrange)
    {
        if (!Enabled) { return; }
        s_passes++;
        s_queuedMeasure += measure;
        s_queuedArrange += arrange;
        s_layoutStart = GC.GetAllocatedBytesForCurrentThread();
    }

    /// <summary>레이아웃 패스 끝(PerfLayoutLog — \"Layout pass finished in {t}\").</summary>
    internal static void OnLayoutFinished(TimeSpan elapsed)
    {
        if (!Enabled) { return; }
        s_passesFinished++;
        s_layoutMs += elapsed.TotalMilliseconds;
        if (s_layoutStart >= 0)
        {
            s_layoutBytes += GC.GetAllocatedBytesForCurrentThread() - s_layoutStart;
            s_layoutStart = -1;
        }
    }

    /// <summary>모든 카운터를 0 으로(UI 스레드에서 부른다 — UI 스레드 할당 기준점을 함께 잡는다).</summary>
    public static void Reset()
    {
        s_moves = s_placeBytes = s_placeTicks = 0;
        s_chromeCalls = s_chromeBytes = 0;
        s_passes = s_passesFinished = s_layoutBytes = s_queuedMeasure = s_queuedArrange = 0;
        s_layoutMs = 0;
        s_layoutStart = -1;
        s_ganttRenders = s_ganttRenderBytes = s_ganttMeasures = 0;
        Interlocked.Exchange(ref s_measureCalls, 0);
        Interlocked.Exchange(ref s_arrangeCalls, 0);
        lock (s_lock)
        {
            foreach (var l in s_hist.Values) { l.Clear(); }
        }

        s_uiAtReset = GC.GetAllocatedBytesForCurrentThread();
        s_totalAtReset = GC.GetTotalAllocatedBytes(false);
        s_gc0 = GC.CollectionCount(0);
        s_gc1 = GC.CollectionCount(1);
        s_gc2 = GC.CollectionCount(2);
        s_resetTs = Stopwatch.GetTimestamp();
    }

    /// <summary>원격 perf.dragProbe 응답. UI 스레드에서 부른다(ui_thread_bytes 가 UI 스레드 기준).</summary>
    public static JsonObject ToJson()
    {
        var ui = GC.GetAllocatedBytesForCurrentThread() - s_uiAtReset;
        var total = GC.GetTotalAllocatedBytes(false) - s_totalAtReset;
        var moves = Math.Max(1, s_moves);
        var o = new JsonObject
        {
            ["enabled"] = Enabled, ["mode"] = !Enabled ? "off" : Full ? "full" : "on",
            ["avalonia_diag_switch"] = AppContext.TryGetSwitch(AvaloniaSwitch, out var sw) && sw,
            ["avalonia_instruments_seen"] = s_instrumentsSeen, // full 인데 false 면 스위치가 늦게 걸려 Avalonia 진단이 꺼진 채다(Measure·컴포지터 값 무효)
            ["elapsed_ms"] = Math.Round(Stopwatch.GetElapsedTime(s_resetTs).TotalMilliseconds, 1),
            ["moves"] = s_moves,
            ["place_bytes"] = s_placeBytes, ["place_bytes_per_move"] = s_placeBytes / moves,
            ["place_ms_per_move"] = Math.Round(Stopwatch.GetElapsedTime(0, s_placeTicks).TotalMilliseconds / moves, 4),
            ["chrome_calls"] = s_chromeCalls, ["chrome_bytes"] = s_chromeBytes, ["chrome_bytes_per_move"] = s_chromeBytes / moves,
            ["layout_passes"] = s_passes, ["layout_passes_finished"] = s_passesFinished,
            ["layout_bytes"] = s_layoutBytes, ["layout_bytes_per_move"] = s_layoutBytes / moves,
            ["layout_ms_total"] = Math.Round(s_layoutMs, 2),
            ["layout_queued_measure"] = s_queuedMeasure, ["layout_queued_arrange"] = s_queuedArrange,
            ["measure_calls"] = Interlocked.Read(ref s_measureCalls), ["arrange_calls"] = Interlocked.Read(ref s_arrangeCalls), // full 일 때만 센다(Layoutable.Measure/Arrange 실제 실행 수)
            ["measure_calls_per_move"] = Interlocked.Read(ref s_measureCalls) / (double)moves,
            ["gantt_renders"] = s_ganttRenders, ["gantt_render_bytes"] = s_ganttRenderBytes, ["gantt_measures"] = s_ganttMeasures,
            ["ui_thread_bytes"] = ui, ["ui_thread_bytes_per_move"] = ui / moves,
            ["ui_other_bytes"] = ui - s_placeBytes - s_layoutBytes - s_ganttRenderBytes, // 그리기 기록·타이머·원격 처리 등(place 에는 chrome 이 포함돼 있어 따로 빼지 않는다)
            ["process_bytes"] = total, ["process_bytes_per_move"] = total / moves,
            ["other_thread_bytes"] = total - ui, // 렌더 스레드 등 UI 밖(근사 — GetTotalAllocatedBytes(false))
            ["gc0"] = GC.CollectionCount(0) - s_gc0, ["gc1"] = GC.CollectionCount(1) - s_gc1, ["gc2"] = GC.CollectionCount(2) - s_gc2,
        };
        lock (s_lock)
        {
            foreach (var (name, list) in s_hist)
            {
                o[name.Replace("avalonia.", string.Empty).Replace(".time", string.Empty).Replace('.', '_')] = Stats(list);
            }
        }

        return o;
    }

    private static JsonObject Stats(List<double> v)
    {
        if (v.Count == 0) { return new JsonObject { ["n"] = 0 }; }
        var s = v.ToArray();
        Array.Sort(s);
        double sum = 0;
        foreach (var x in s) { sum += x; }
        double P(double q) => s[Math.Min(s.Length - 1, (int)Math.Ceiling(q * s.Length) - 1)];
        return new JsonObject
        {
            ["n"] = s.Length, ["sum_ms"] = Math.Round(sum, 2), ["avg_ms"] = Math.Round(sum / s.Length, 3),
            ["p50_ms"] = Math.Round(P(0.5), 3), ["p95_ms"] = Math.Round(P(0.95), 3), ["max_ms"] = Math.Round(s[^1], 3),
        };
    }

    private static string? ArgValue(string[] args, string name)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == name && i + 1 < args.Length) { return args[i + 1]; }
            if (args[i].StartsWith(name + "=", StringComparison.Ordinal)) { return args[i][(name.Length + 1)..]; }
        }

        return null;
    }
}
#endif
