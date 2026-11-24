// 대시보드 벤치 측정기 — U4(차트 N개 첫 로딩·메모리)와 U5(창 이동/크기 변경 중 프레임 간격·초과 횟수)를 잰다
using xkxk.frameworks.ui.shell;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Avalonia.Controls;

namespace xkxk.frameworks.ui.views.Documents.Bench;

public sealed class DashboardBench
{
    private readonly Panel _host;
    private readonly Func<TopLevel> _top;

    public DashboardBench(Panel host, Func<TopLevel> top)
    {
        _host = host;
        _top = top;
    }

    /// <summary>호스트를 비우고(이전 대시보드 해제) 전체 GC 한다.</summary>
    public void Clear()
    {
        _host.Children.Clear();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    /// <summary>
    /// U4: 문서가 열린 뒤 차트 count 개를 만들어 격자에 붙이고 마지막 차트가 그려질 때까지(2프레임) ms. 콜드/웜 구분은 호출자가 한다.
    /// 지원하지 않는 형식은 빼고 unsupported 에 센다. ui_block_ms = 만들고 붙이는 동기 구간(그동안 UI 스레드는 응답하지 않는다).
    /// </summary>
    public async Task<JsonObject> LoadAsync(string lib, int count, int points, DashStyle style)
    {
        Clear();
        await BenchProbe.NextFrameAsync(_top());
        var memBefore = BenchProbe.Memory(collect: true);
        var adapter = DashboardAdapters.Create(lib);
        var specs = DashboardSpec.Build(count, points);
        var made = 0;
        var unsupported = new List<string>();
        var syncMs = 0.0;
        var failed = new List<string>();
        var loadMs = await BenchProbe.TimeToFramesAsync(_top(), () =>
        {
            var sw = Stopwatch.StartNew();
            foreach (var spec in specs)
            {
                if (!adapter.Supports(spec.Kind)) { unsupported.Add(spec.Kind.ToString()); continue; }
                try { _host.Children.Add(Wrap(adapter.Create(spec, style), _host.Children.Count + 1)); made++; }
                catch (Exception ex) { failed.Add($"{spec.Kind}: {ex.GetType().Name}"); }
            }

            syncMs = sw.Elapsed.TotalMilliseconds;
        });
        var memShown = BenchProbe.Memory(collect: true);
        return new JsonObject
        {
            ["adapter"] = adapter.Name, ["charts_requested"] = count, ["charts_made"] = made, ["points"] = points,
            ["style"] = style.Pd ? "pd" : "default", ["animate"] = style.Animate,
            ["unsupported_count"] = unsupported.Count, ["unsupported"] = new JsonArray(unsupported.Select(u => (JsonNode)u).ToArray()),
            ["failed"] = new JsonArray(failed.Select(u => (JsonNode)u).ToArray()),
            ["load_ms"] = Math.Round(loadMs, 1), ["ui_block_ms"] = Math.Round(syncMs, 1),
            ["memory_before"] = memBefore, ["memory_shown"] = memShown,
            ["memory_delta"] = made > 0 ? BenchProbe.MemoryDelta(memBefore, memShown, made) : null, // points 자리에 차트 수를 넘겨 '차트당 바이트'를 얻는다
        };
    }

    /// <summary>격자 칸. AutomationId = bench.dashboard.cell01~ (행 우선 번호, 만든 순서 — 형식 미지원으로 빠지면 번호가 당겨진다).</summary>
    private static Border Wrap(Control c, int index)
    {
        var b = new Border { Margin = new Avalonia.Thickness(4), MinHeight = 180, Child = c };
        Avalonia.Automation.AutomationProperties.SetAutomationId(b, $"bench.dashboard.cell{index:00}");
        return b;
    }

    /// <summary>
    /// U5(Fold5): seconds 동안 최상위 창 크기가 바뀔 때마다 그 시각부터 2프레임이 그려질 때까지의 ms 를 모은다(회전·접힘/펼침 재배치 시간).
    /// 크기 변경 자체는 외부(adb)가 일으킨다. 앞 측정이 끝나기 전의 변경은 앞 측정에 합친다.
    /// </summary>
    public async Task<JsonObject> WatchRelayoutAsync(double seconds)
    {
        var top = _top();
        var times = new List<double>();
        var sizes = new List<string>();
        var running = false;

        async void OnChange(object? s, Avalonia.AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property != TopLevel.ClientSizeProperty || running) { return; }
            running = true;
            var sw = Stopwatch.StartNew();
            sizes.Add($"{top.ClientSize.Width:0}x{top.ClientSize.Height:0}");
            await BenchProbe.NextFrameAsync(top);
            await BenchProbe.NextFrameAsync(top);
            times.Add(Math.Round(sw.Elapsed.TotalMilliseconds, 1));
            running = false;
        }

        top.PropertyChanged += OnChange;
        await Task.Delay(TimeSpan.FromSeconds(seconds));
        top.PropertyChanged -= OnChange;
        return new JsonObject
        {
            ["status"] = times.Count > 0 ? "Done" : "NoResizeSeen", ["events"] = times.Count,
            ["relayout_ms"] = new JsonArray(times.Select(t => (JsonNode)t).ToArray()),
            ["client_sizes"] = new JsonArray(sizes.Select(t => (JsonNode)t).ToArray()),
            ["max_ms"] = times.Count > 0 ? times.Max() : 0,
        };
    }

    /// <summary>
    /// U5: frames 프레임 동안 step(i) 을 불러(창 이동·크기 변경) 프레임 간격을 모은다. step 은 호출자가 넘긴다(MDI 이동 = App.Shell.MoveAll, 크기 = 호스트 폭).
    /// 16.7ms·33.3ms·66.7ms 초과 횟수를 같이 낸다(PerfProbe 에 없는 지표).
    /// </summary>
    public async Task<JsonObject> SweepAsync(int frames, Action<int> step)
    {
        var intervals = new List<double>(frames);
        var tcs = new TaskCompletionSource();
        var sw = Stopwatch.StartNew();
        var last = -1.0;
        var i = 0;
        var top = _top();

        void Tick(TimeSpan _)
        {
            var now = sw.Elapsed.TotalMilliseconds;
            if (last >= 0) { intervals.Add(now - last); }
            last = now;
            try { step(i); }
            catch (Exception ex) { tcs.TrySetException(ex); return; }
            i++;
            if (i >= frames) { tcs.TrySetResult(); }
            else { top.RequestAnimationFrame(Tick); }
        }

        top.RequestAnimationFrame(Tick);
        var done = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(frames / 5.0 + 10)));
        if (done != tcs.Task) { return new JsonObject { ["timed_out"] = true, ["frames"] = intervals.Count }; }
        await tcs.Task;
        var s = intervals.OrderBy(x => x).ToArray();
        double P(double q) => s.Length == 0 ? 0 : s[Math.Min(s.Length - 1, (int)Math.Floor(q * (s.Length - 1)))];
        return new JsonObject
        {
            ["frames"] = s.Length, ["p50_ms"] = Math.Round(P(0.5), 2), ["p95_ms"] = Math.Round(P(0.95), 2), ["p99_ms"] = Math.Round(P(0.99), 2),
            ["max_ms"] = Math.Round(s.Length == 0 ? 0 : s[^1], 2),
            ["over_16_7"] = s.Count(x => x > 16.7), ["over_33_3"] = s.Count(x => x > 33.3), ["over_66_7"] = s.Count(x => x > 66.7),
            ["over_33_3_ratio"] = s.Length == 0 ? 0 : Math.Round(s.Count(x => x > 33.3) / (double)s.Length, 3),
            // 원 간격(측정 순서 그대로) — 수직동기 배수 분포·히스토그램용(i2410, team-lead 02:57)
            ["intervals_ms"] = new JsonArray(intervals.Select(x => (JsonNode)Math.Round(x, 2)).ToArray()),
        };
    }
}
