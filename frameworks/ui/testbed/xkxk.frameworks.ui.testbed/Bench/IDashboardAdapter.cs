// 대시보드 벤치 어댑터 계약과 등록부 — 라이브러리가 DashChartSpec 하나를 받아 차트 컨트롤 하나를 만든다
using xkxk.frameworks.ui.shell;
using System;
using System.Collections.Generic;
using Avalonia.Controls;

namespace xkxk.frameworks.ui.testbed.Bench;

/// <summary>Pd=false 면 라이브러리 기본 모양(한글 글꼴만 지정), true 면 PD 테마 색(BenchChartColors)을 입힌다.</summary>
public sealed record DashStyle(bool Animate, bool Pd);

public interface IDashboardAdapter
{
    string Name { get; }

    /// <summary>이 라이브러리가 해당 형식을 공식 API 로 그릴 수 있는가(못 그리면 대시보드에서 빼고 '형식 미지원'으로 센다).</summary>
    bool Supports(DashKind kind);

    Control Create(DashChartSpec spec, DashStyle style);
}

public static class DashboardAdapters
{
    private static readonly List<(string Key, Func<IDashboardAdapter> Create)> All = new();

    static DashboardAdapters()
    {
        All.Add(("baseline", () => new DashBaseline())); // 차트 없는 기준선(항상 포함)
#if PD_CHART_SCOTTPLOT
        All.Add(("scottplot", () => new DashScottPlot()));
#endif
#if PD_CHART_LIVECHARTS
        All.Add(("livecharts", () => new DashLiveCharts()));
#endif
#if PD_CHART_THEONE
        All.Add(("theone", () => new DashTheOne()));
#endif
    }

    public static IEnumerable<string> Available
    {
        get { foreach (var (k, _) in All) { yield return k; } }
    }

    public static IDashboardAdapter Create(string key)
    {
        foreach (var (k, c) in All)
        {
            if (k.Equals(key, StringComparison.OrdinalIgnoreCase)) { return c(); }
        }

        throw new ArgumentException($"이 빌드에 없는 대시보드 라이브러리: {key} (있는 것: {string.Join(",", Available)})");
    }
}
