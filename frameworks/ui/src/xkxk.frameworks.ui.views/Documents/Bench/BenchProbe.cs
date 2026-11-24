// 벤치 공통 계측 장치 — 첫 표시 시간·프레임 간격(FPS·p50/p95)·메모리·환경 정보를 잰다(TopLevel.RequestAnimationFrame 기반)
using xkxk.frameworks.ui.shell;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Avalonia.Controls;

namespace xkxk.frameworks.ui.views.Documents.Bench;

/// <summary>
/// 프레임 계측: 매 프레임 RequestAnimationFrame 을 다시 요청해 렌더 루프를 계속 돌리고, 콜백 간격을 프레임 시간으로 본다.
/// perFrame 콜백(스크롤·갱신 구동)은 RAF 안에서 실행되므로 그 변경은 같은 프레임에 그려진다.
/// 창이 숨겨져 프레임이 오지 않으면 seconds+5초 뒤 TimedOut=true 로 끝낸다(측정 무효).
/// </summary>
public static class BenchProbe
{
    public static Task NextFrameAsync(TopLevel top)
    {
        var tcs = new TaskCompletionSource();
        top.RequestAnimationFrame(_ => tcs.TrySetResult());
        return tcs.Task;
    }

    /// <summary>action 실행 → frames 번째 프레임까지의 ms(첫 표시 시간). 첫 프레임 = 레이아웃·렌더 반영, 둘째 = 화면 표시 확정.</summary>
    public static async Task<double> TimeToFramesAsync(TopLevel top, Action action, int frames = 2)
    {
        var sw = Stopwatch.StartNew();
        action();
        for (var i = 0; i < frames; i++)
        {
            var f = NextFrameAsync(top);
            if (await Task.WhenAny(f, Task.Delay(10_000)) != f) { return -1; } // 프레임 없음(숨김 창)
        }

        return sw.Elapsed.TotalMilliseconds;
    }

    public static async Task<FrameStats> MeasureFramesAsync(TopLevel top, double seconds, Action<int>? perFrame)
    {
        var intervals = new List<double>(4096);
        var sw = Stopwatch.StartNew();
        var last = 0.0;
        var index = 0;
        var firstTick = -1.0;
        var tcs = new TaskCompletionSource();

        void Tick(TimeSpan _)
        {
            var now = sw.Elapsed.TotalMilliseconds;
            if (index > 0) { intervals.Add(now - last); }
            else { firstTick = now; } // 측정 시작 → 첫 프레임까지(한 프레임이 측정 시간보다 길면 이 값만 남는다 — Fold5 LiveCharts 10만 점 redraw 사례)
            last = now;
            try { perFrame?.Invoke(index); }
            catch (Exception ex) { tcs.TrySetException(ex); return; }
            index++;
            if (now >= seconds * 1000) { tcs.TrySetResult(); }
            else { top.RequestAnimationFrame(Tick); }
        }

        top.RequestAnimationFrame(Tick);
        var done = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(seconds + 5)));
        if (done != tcs.Task) { return FrameStats.From(intervals, sw.Elapsed.TotalMilliseconds, timedOut: true, firstTick, index); }
        await tcs.Task; // 예외 전파
        return FrameStats.From(intervals, sw.Elapsed.TotalMilliseconds, timedOut: false, firstTick, index);
    }

    /// <summary>메모리: 관리 힙(collect=true 면 전체 GC 뒤) + 프로세스 작업 집합·전용 메모리(브라우저는 관리 힙만).</summary>
    public static JsonObject Memory(bool collect)
    {
        var o = new JsonObject { ["managed_mb"] = Mb(GC.GetTotalMemory(collect)), ["collected"] = collect };
        try
        {
            var gi = GC.GetGCMemoryInfo();
            o["heap_mb"] = Mb(gi.HeapSizeBytes);
            o["committed_mb"] = Mb(gi.TotalCommittedBytes);
        }
        catch (Exception) { /* 플랫폼 미지원 */ }

        if (!OperatingSystem.IsBrowser())
        {
            try
            {
                using var p = Process.GetCurrentProcess();
                p.Refresh();
                o["working_set_mb"] = Mb(p.WorkingSet64);
                o["private_mb"] = Mb(p.PrivateMemorySize64);
            }
            catch (Exception ex) { o["process_error"] = ex.GetType().Name; }
        }

        return o;
    }

    public static double Mb(long bytes) => Math.Round(bytes / 1048576.0, 1);

    /// <summary>
    /// 두 메모리 표본(전체 GC 뒤)의 차이와 점당 바이트. 관리 힙은 모든 플랫폼, 작업 집합은 브라우저 제외.
    /// 차이가 음수일 수 있다(GC 시점·플랫폼 반환 차이) — 그대로 기록한다. (ChartBenchView 300줄 제한으로 이곳에 옮김)
    /// </summary>
    public static JsonObject MemoryDelta(JsonObject before, JsonObject after, int points)
    {
        static double? V(JsonObject o, string k) => o.TryGetPropertyValue(k, out var n) && n is JsonValue v && v.TryGetValue<double>(out var d) ? d : null;
        var r = new JsonObject { ["points"] = points };
        foreach (var k in new[] { "managed_mb", "heap_mb", "working_set_mb", "private_mb" })
        {
            var b = V(before, k);
            var e = V(after, k);
            if (b is null || e is null) { continue; }
            var dMb = Math.Round(e.Value - b.Value, 1);
            r[k] = dMb;
            if (points > 0) { r[k.Replace("_mb", "_bytes_per_point")] = Math.Round(dMb * 1048576.0 / points, 1); }
        }

        return r;
    }

    /// <summary>환경: 플랫폼·런타임·문화권·그리드 구현·포함된 차트 라이브러리.</summary>
    public static JsonObject Environment()
    {
        var libs = new JsonArray();
        // (JsonNode?) 캐스트로 비제네릭 Add(JsonNode) 를 고른다 — 제네릭 Add<T> 는 WASM(반사 직렬화 꺼짐)에서 NotSupportedException(NoMetadataForType)
        foreach (var l in ChartAdapters.Available) { libs.Add((JsonNode?)JsonValue.Create(l)); }
        // 대시보드 어댑터 목록(dash_libs) — chart_libs(ChartAdapters)와 레지스트리가 달라 TheOne 등은 여기에만 나온다. IDashboardAdapter.cs.cand 를 적용한 PD_BENCH 빌드에서만 컴파일한다(함께 적용해야 한다)
        var dashLibs = new JsonArray();
#if PD_BENCH
        foreach (var l in DashboardAdapters.Available) { dashLibs.Add((JsonNode?)JsonValue.Create(l)); }
#endif
        return new JsonObject
        {
            ["os"] = RuntimeInformation.OSDescription,
            ["arch"] = RuntimeInformation.ProcessArchitecture.ToString(),
            ["framework"] = RuntimeInformation.FrameworkDescription,
            ["browser"] = OperatingSystem.IsBrowser(),
            ["android"] = OperatingSystem.IsAndroid(),
            ["culture"] = CultureInfo.CurrentCulture.Name, // 웹은 이 값에 따라 ICU 조각(EFIGS·CJK·no_CJK)이 정해진다 — ko 규칙은 CJK 조각에만 있음
            ["ui_culture"] = CultureInfo.CurrentUICulture.Name,
            ["grid_impl"] = GridBenchModel.Implementation,
            ["xlsx"] = GridBenchModel.XlsxAvailable,
            ["chart_libs"] = libs,
            ["dash_libs"] = dashLibs,
            ["korean_typeface"] = BenchFonts.Describe(),
            ["collation"] = GridBenchModel.CollationReport(),
        };
    }
}

/// <summary>
/// 프레임 통계. Fps = 프레임 간격 수 ÷ 간격 합(초), 백분위는 프레임 간격(ms).
/// FirstTickMs = 측정 시작 → 첫 프레임, Ticks = 받은 프레임 수. 간격이 0개(첫 프레임이 측정 시간보다 길었음)면 Fps 는 Ticks ÷ 경과 초로 낸다 —
/// 예전에는 이 경우 fps 0 으로만 보여 "멈춤"과 "아주 느림"을 구분할 수 없었다(otest-m-1 bp1146a LiveCharts redraw frames=0·elapsed 5065ms).
/// </summary>
public sealed record FrameStats(int Frames, double ElapsedMs, double Fps, double P50Ms, double P95Ms, double P99Ms, double MaxMs, bool TimedOut, double FirstTickMs = -1, int Ticks = 0)
{
    public static FrameStats From(List<double> intervals, double elapsedMs, bool timedOut, double firstTickMs = -1, int ticks = 0)
    {
        if (intervals.Count == 0)
        {
            var fps = ticks > 0 && elapsedMs > 0 ? ticks / (elapsedMs / 1000.0) : 0;
            return new FrameStats(0, elapsedMs, fps, 0, 0, 0, firstTickMs, timedOut, firstTickMs, ticks);
        }

        var s = intervals.OrderBy(x => x).ToArray();
        double P(double q) => s[Math.Min(s.Length - 1, (int)Math.Floor(q * (s.Length - 1)))];
        return new FrameStats(s.Length, elapsedMs, s.Length / (intervals.Sum() / 1000.0), P(0.5), P(0.95), P(0.99), Math.Max(s[^1], firstTickMs), timedOut, firstTickMs, ticks);
    }

    public JsonObject ToJson() => new()
    {
        ["frames"] = Frames,
        ["elapsed_ms"] = Math.Round(ElapsedMs, 1),
        ["fps"] = Math.Round(Fps, 1),
        ["p50_ms"] = Math.Round(P50Ms, 2),
        ["p95_ms"] = Math.Round(P95Ms, 2),
        ["p99_ms"] = Math.Round(P99Ms, 2),
        ["max_ms"] = Math.Round(MaxMs, 2),
        ["timed_out"] = TimedOut,
        ["first_tick_ms"] = Math.Round(FirstTickMs, 1),
        ["ticks"] = Ticks,
    };
}
