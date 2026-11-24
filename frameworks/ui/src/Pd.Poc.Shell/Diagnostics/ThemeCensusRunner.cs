// 테마 변경 직후 다시 측정·배치될 컨트롤을 형식별로 세는 진단(theme-census)과 다시 그린 영역 표시(dirty-rects) 명령
#if PD_DIAGNOSTICS // 진단 빌드 전용 — 제품 빌드에는 명령 이름·본문이 들어가지 않는다
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Rendering;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Pd.Poc.Shell.Theming;

namespace Pd.Poc.Shell.Diagnostics;

/// <summary>theme-census·dirty-rects 명령 실행. 공개 진입점은 AutomationBridge.ThemeCensusAsync(원격 theme.census)다.</summary>
internal static class ThemeCensusRunner
{
    private const int CensusTopTypes = 30;

    /// <summary>census 명령이면 실행하고 true. AutomationCommandRunner.RunCmdAsync 가 테마 명령 위임보다 먼저 부른다.</summary>
    internal static async Task<bool> TryRunCensusAsync(TopLevel top, string cmd, string lower)
    {
        if (lower.StartsWith("theme-census", StringComparison.Ordinal))
        {
            var colon = cmd.IndexOf(':');
            await ThemeCensusAsync(top, colon >= 0 ? cmd[(colon + 1)..].Trim() : null);
            return true;
        }

        if (lower is "dirty-rects-on" or "dirty-rects-off")
        {
            // 다시 그린 사각형을 렌더러가 화면에 덧그린다(OS 창 캡처로 확인 — 시각 트리 캡처에는 안 보일 수 있다)
            top.RendererDiagnostics.DebugOverlays = lower == "dirty-rects-on" ? RendererDebugOverlays.DirtyRects : RendererDebugOverlays.None;
            PerfProbe.Current.SetText("dirty_rects", lower == "dirty-rects-on" ? "on" : "off");
            await AutomationBridge.WaitFramesAsync(top, 2);
            return true;
        }

        return false;
    }

    /// <summary>
    /// theme-census[:테마Id] — 테마를 바꾼 직후(레이아웃 패스 전) 측정·배치가 무효화된 컨트롤을 형식별로 센다.
    /// Id 가 없으면 다음 테마로 순환한다. 결과는 perf JSON 의 theme_census(동기 직후 sync·한 번 양보한 뒤 posted·렌더 후 settled).
    /// </summary>
    internal static async Task ThemeCensusAsync(TopLevel top, string? themeId) // 원격 명령(theme.census)도 AutomationBridge 를 거쳐 부른다
    {
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        await AutomationBridge.WaitFramesAsync(top, 2);
        var baseline = Census(top);
        var variantBefore = Avalonia.Application.Current?.ActualThemeVariant?.ToString() ?? "";
        var start = PerfProbe.Current.NowMs;
        if (string.IsNullOrEmpty(themeId)) { ThemeAutomation.Next(); }
        else { ThemeAutomation.Select(themeId); }

        var syncMs = PerfProbe.Current.NowMs - start;
        var sync = Census(top); // 적용 함수가 돌아온 직후(동기 무효화분)
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Send);
        var posted = Census(top); // 디스패처에 올린 후속 작업까지(적용이 지연 처리될 때 대비)
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        await AutomationBridge.WaitFramesAsync(top, 2);
        var settledMs = PerfProbe.Current.NowMs - start;
        var settled = Census(top);

        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            w.WriteString("theme", themeId ?? "next");
            w.WriteString("theme_id_after", ThemeApplier.Active?.Id ?? "");
            w.WriteString("variant_before", variantBefore); // Light↔Dark 전환이 끼었는지(변형 전환은 테마 사전 전체 재평가를 부를 수 있다 — 대조용)
            w.WriteString("variant_after", Avalonia.Application.Current?.ActualThemeVariant?.ToString() ?? "");
            w.WriteNumber("sync_ms", Math.Round(syncMs, 2));
            w.WriteNumber("settled_ms", Math.Round(settledMs, 2));
            WriteCensus(w, "baseline", baseline);
            WriteCensus(w, "sync", sync);
            WriteCensus(w, "posted", posted);
            WriteCensus(w, "settled", settled);
            w.WriteEndObject();
        }

        PerfProbe.Current.SetRawJson("theme_census", Encoding.UTF8.GetString(ms.ToArray()));
    }

    private sealed record CensusResult(int Total, int MeasureInvalid, int ArrangeInvalid, List<(string Type, int Count, int Measure, int Arrange)> ByType,
        List<(string Type, int Count)> Largest, List<(string Key, int Count)> WhoMeasure,
        int MeasureInvalidVisible, int ArrangeInvalidVisible, List<(string Key, int Count)> WhoMeasureVisible);

    private static CensusResult Census(TopLevel top)
    {
        var map = new Dictionary<string, (int Count, int Measure, int Arrange)>(StringComparer.Ordinal);
        var who = new Dictionary<string, int>(StringComparer.Ordinal); // 측정 무효 컨트롤의 '형식#이름@템플릿 주인 형식' — 같은 형식 중 어느 템플릿 부품인지
        // 보이는 것만(IsEffectivelyVisible) 따로 센다 — 숨은 컨트롤은 레이아웃이 측정하지 않아 늘 무효로 남아(상시 무효) 판정을 흐린다
        var whoVisible = new Dictionary<string, int>(StringComparer.Ordinal);
        int total = 0, measure = 0, arrange = 0, measureVisible = 0, arrangeVisible = 0;
        foreach (var l in top.GetVisualDescendants().OfType<Layoutable>())
        {
            total++;
            var m = l.IsMeasureValid ? 0 : 1;
            var a = l.IsArrangeValid ? 0 : 1;
            measure += m;
            arrange += a;
            var visible = l.IsEffectivelyVisible;
            if (visible) { measureVisible += m; arrangeVisible += a; }
            var name = l.GetType().Name;
            var cur = map.TryGetValue(name, out var c) ? c : default;
            map[name] = (cur.Count + 1, cur.Measure + m, cur.Arrange + a);
            if (m == 1)
            {
                var key = $"{name}#{(l as StyledElement)?.Name ?? "-"}@{(l as StyledElement)?.TemplatedParent?.GetType().Name ?? "-"}";
                who[key] = who.TryGetValue(key, out var n) ? n + 1 : 1;
                if (visible) { whoVisible[key] = whoVisible.TryGetValue(key, out var nv) ? nv + 1 : 1; }
            }
        }

        var by = map.Select(kv => (kv.Key, kv.Value.Count, kv.Value.Measure, kv.Value.Arrange))
            .Where(t => t.Measure > 0 || t.Arrange > 0)
            .OrderByDescending(t => t.Measure).ThenByDescending(t => t.Arrange).ThenBy(t => t.Key, StringComparer.Ordinal)
            .Take(CensusTopTypes).ToList();
        var largest = map.OrderByDescending(kv => kv.Value.Count).ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Take(CensusTopTypes).Select(kv => (kv.Key, kv.Value.Count)).ToList(); // 시각 트리 구성(판본 간 트리 크기 비교용)
        static List<(string, int)> Top(Dictionary<string, int> d) =>
            d.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).Take(CensusTopTypes).Select(kv => (kv.Key, kv.Value)).ToList();
        return new CensusResult(total, measure, arrange, by, largest, Top(who), measureVisible, arrangeVisible, Top(whoVisible));
    }

    private static void WriteCensus(Utf8JsonWriter w, string name, CensusResult r)
    {
        w.WriteStartObject(name);
        w.WriteNumber("total", r.Total);
        w.WriteNumber("measure_invalid", r.MeasureInvalid);
        w.WriteNumber("arrange_invalid", r.ArrangeInvalid);
        w.WriteNumber("measure_invalid_visible", r.MeasureInvalidVisible); // 보이는 컨트롤만(상시 무효인 숨은 부품 제외) — 판정은 이 값으로
        w.WriteNumber("arrange_invalid_visible", r.ArrangeInvalidVisible);
        w.WriteStartArray("by_type");
        foreach (var (type, count, m, a) in r.ByType)
        {
            w.WriteStartObject();
            w.WriteString("type", type);
            w.WriteNumber("count", count);
            w.WriteNumber("measure", m);
            w.WriteNumber("arrange", a);
            w.WriteEndObject();
        }

        w.WriteEndArray();
        w.WriteStartArray("largest");
        foreach (var (type, count) in r.Largest)
        {
            w.WriteStartObject();
            w.WriteString("type", type);
            w.WriteNumber("count", count);
            w.WriteEndObject();
        }

        w.WriteEndArray();
        w.WriteStartArray("who_measure");
        foreach (var (key, count) in r.WhoMeasure)
        {
            w.WriteStartObject();
            w.WriteString("key", key);
            w.WriteNumber("count", count);
            w.WriteEndObject();
        }

        w.WriteEndArray();
        w.WriteStartArray("who_measure_visible");
        foreach (var (key, count) in r.WhoMeasureVisible)
        {
            w.WriteStartObject();
            w.WriteString("key", key);
            w.WriteNumber("count", count);
            w.WriteEndObject();
        }

        w.WriteEndArray();
        w.WriteEndObject();
    }
}
#endif
