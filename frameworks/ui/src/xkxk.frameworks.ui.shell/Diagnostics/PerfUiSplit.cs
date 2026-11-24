// perf-ui 단계 분해 계측(진단 빌드 전용, --perf-split 켤 때만) — 조작별 동기·즉시 레이아웃·Background 소진·렌더 프레임 ms 와 Loaded·템플릿 적용 횟수를 따로 남긴다
#if PD_DIAGNOSTICS
using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace xkxk.frameworks.ui.shell.Diagnostics;

/// <summary>
/// 원인 확정용 분해 계측. 기본 perf-ui 측정(PerfUiRunner.OpAsync)은 그대로 두고, 스위치가 켜졌을 때만 이 경로로 잰다.
/// 단계: sync(조작 호출) → layout1(즉시 레이아웃 패스) → bg(Background 지연 작업 소진) → layout2(그 뒤 레이아웃) → frame1·frame2(렌더 프레임 대기).
/// 레이아웃은 다음 프레임에 어차피 일어날 일을 앞당겨 재는 것이라 총 작업량은 바뀌지 않는다(시간 배분만 나뉜다).
/// </summary>
internal static class PerfUiSplit
{
    private static bool? s_enabled;
    private static bool s_hooked;
    private static long s_loaded;
    private static long s_templated;

    /// <summary>--perf-split(웹: ?perf-split) 이 있으면 켠다.</summary>
    internal static bool Enabled => s_enabled ??= ReadSwitch();

    private static bool ReadSwitch()
    {
        foreach (var a in App.StartupArgs)
        {
            if (a.Equals("--perf-split", StringComparison.OrdinalIgnoreCase) || a.Equals("--perf-split=on", StringComparison.OrdinalIgnoreCase))
            {
                PerfProbe.Current.SetText("perf_split", "on");
                return true;
            }
        }

        return false;
    }

    /// <summary>분해 계측일 때 --perf-split-n=&lt;n&gt; 이 있으면 그 문서 수만 남긴다(ui.* 분해 값이 한 조건만 담기게). 그 밖에는 받은 그대로 돌려준다.</summary>
    internal static int[] FilterCounts(int[] counts)
    {
        if (!Enabled)
        {
            return counts;
        }

        var args = App.StartupArgs;
        for (var i = 0; i < args.Count; i++)
        {
            // 데스크톱 --perf-split-n=20, 웹 ?perf-split-n=20 → "--perf-split-n","20"(diag.js) 둘 다 받는다
            var text = args[i].StartsWith("--perf-split-n=", StringComparison.OrdinalIgnoreCase) ? args[i]["--perf-split-n=".Length..]
                : args[i].Equals("--perf-split-n", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Count ? args[i + 1] : null;
            if (text is not null && int.TryParse(text, out var n) && Array.IndexOf(counts, n) >= 0)
            {
                PerfProbe.Current.SetText("perf_split_n", n.ToString(System.Globalization.CultureInfo.InvariantCulture));
                return new[] { n };
            }
        }

        return counts;
    }

    private static void HookOnce()
    {
        if (s_hooked)
        {
            return;
        }

        s_hooked = true;
        Control.LoadedEvent.AddClassHandler<Control>((_, _) => s_loaded++, RoutingStrategies.Direct);
        TemplatedControl.TemplateAppliedEvent.AddClassHandler<TemplatedControl>((_, _) => s_templated++, RoutingStrategies.Direct);
    }

    /// <summary>조작 1회를 단계별로 잰다. 기본 지표(name 의 sync·frame)도 같은 정의(시작→Background 소진→2프레임)로 함께 남긴다.</summary>
    internal static async Task RunAsync(TopLevel top, string name, Func<Task> action, Action<string, Exception> onError)
    {
        HookOnce();
        var perf = PerfProbe.Current;
        var (loaded0, templated0) = (s_loaded, s_templated);
        var t0 = perf.NowMs;
        try { await action(); }
        catch (Exception ex) { onError(name, ex); }
        var t1 = perf.NowMs;
        top.UpdateLayout();
        var t2 = perf.NowMs;
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        var t3 = perf.NowMs;
        top.UpdateLayout();
        var t4 = perf.NowMs;
        await AutomationBridge.WaitFramesAsync(top, 1);
        var t5 = perf.NowMs;
        await AutomationBridge.WaitFramesAsync(top, 1);
        var t6 = perf.NowMs;
        perf.RecordOperation(name, t1 - t0, t6 - t0);
        Part(name, "sync", t1 - t0);
        Part(name, "layout1", t2 - t1);
        Part(name, "bg", t3 - t2);
        Part(name, "layout2", t4 - t3);
        Part(name, "frame1", t5 - t4);
        Part(name, "frame2", t6 - t5);
        Part(name, "loaded", s_loaded - loaded0);
        Part(name, "templated", s_templated - templated0);
        Console.WriteLine($"[perf-split] {name} sync={t1 - t0:0} layout1={t2 - t1:0} bg={t3 - t2:0} layout2={t4 - t3:0} frame1={t5 - t4:0} frame2={t6 - t5:0} loaded={s_loaded - loaded0} templated={s_templated - templated0}");
    }

    private static void Part(string name, string part, double value) => PerfProbe.Current.RecordOperation("split." + name + "." + part, value, value);
}
#endif
