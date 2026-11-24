// 성능 프로브 JSON 통계 도우미(진단 빌드 전용) — 백분위·반올림·{prefix}_avg/p50/p95/p99/max 묶음·이름별 ms 배열 쓰기
#if PD_DIAGNOSTICS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Pd.Poc.Shell.Diagnostics;

/// <summary>PerfProbe·PerfOperations 가 함께 쓰는 통계 계산과 JSON 쓰기. 키 이름·반올림(소수 2자리) 규칙은 판정 스크립트가 읽으므로 바꾸지 않는다.</summary>
internal static class PerfStats
{
    public static void WriteNamedMs(Utf8JsonWriter w, string array, string nameKey, List<(string, double)> items)
    {
        w.WriteStartArray(array);
        foreach (var (name, t) in items) { w.WriteStartObject(); w.WriteString(nameKey, name); w.WriteNumber("ms", Round(t)); w.WriteEndObject(); }
        w.WriteEndArray();
    }

    public static void WriteStats(Utf8JsonWriter w, string prefix, List<double> values)
    {
        w.WriteNumber(prefix + "_samples", values.Count);
        if (values.Count == 0)
        {
            return;
        }

        var sorted = values.OrderBy(v => v).ToList();
        w.WriteNumber(prefix + "_avg_ms", Round(sorted.Average()));
        w.WriteNumber(prefix + "_p50_ms", Round(Percentile(sorted, 0.50)));
        w.WriteNumber(prefix + "_p95_ms", Round(Percentile(sorted, 0.95)));
        w.WriteNumber(prefix + "_p99_ms", Round(Percentile(sorted, 0.99)));
        w.WriteNumber(prefix + "_p95_interp_ms", Round(PercentileInterp(sorted, 0.95))); // 추가 필드(기존 p95 는 nearest-rank 유지)
        w.WriteNumber(prefix + "_max_ms", Round(sorted[^1]));
    }

    public static double Percentile(List<double> sorted, double p)
    {
        var idx = (int)Math.Ceiling(p * sorted.Count) - 1;
        return sorted[Math.Clamp(idx, 0, sorted.Count - 1)];
    }

    /// <summary>선형보간(R-7, numpy 기본) 백분위. 기존 Percentile(nearest-rank)은 판정기·동결 도구가 읽으므로 그대로 두고, 추가 필드(_p95_interp_ms)에만 쓴다.</summary>
    public static double PercentileInterp(List<double> sorted, double p)
    {
        if (sorted.Count == 1) { return sorted[0]; }
        var pos = p * (sorted.Count - 1);
        var lo = (int)Math.Floor(pos);
        var hi = Math.Min(lo + 1, sorted.Count - 1);
        return sorted[lo] + (sorted[hi] - sorted[lo]) * (pos - lo);
    }

    public static double Round(double v) => Math.Round(v, 2);
}
#endif
