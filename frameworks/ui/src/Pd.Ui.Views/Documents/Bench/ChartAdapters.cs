// 차트 벤치 어댑터 계약과 등록부 — 라이브러리마다 같은 시나리오(대용량 정적·롤링 스트리밍)를 같은 방식으로 구동한다
using Pd.Poc.Shell;
using System;
using System.Collections.Generic;
using Avalonia.Controls;

namespace Pd.Ui.Views.Documents.Bench;

/// <summary>
/// 한 차트 라이브러리를 감싼다. View 는 어댑터가 만든 차트 컨트롤이다(라이브러리별 메모리·첫 표시를 따로 재려고
/// 선택한 라이브러리 하나만 만든다 — XAML 에 세 개를 미리 두면 측정이 섞인다).
/// </summary>
public interface IChartAdapter : IDisposable
{
    string Name { get; }
    Control View { get; }

    /// <summary>점 ys 를 선 하나로 그린다(제목·축 이름은 한글).</summary>
    void LoadStatic(double[] ys, ChartBenchOptions o);

    /// <summary>롤링 창(window 점) 스트리밍을 시작한다.</summary>
    void StartStream(int window, ChartBenchOptions o);

    /// <summary>스트리밍 점 추가 + 다시 그리기 요청.</summary>
    void Append(double[] batch, int count);
}

/// <summary>
/// 세 차트 라이브러리에 똑같이 넣는 색(공정 비교 — 라이브러리 기본색 차이로 가독성 판정이 갈리지 않게).
/// 현재 앱 테마 밝기(라이트/다크)를 따른다. 값은 sRGB 고정값이며 셸 테마 토큰과 일치시키지는 않는다(벤치 전용).
/// </summary>
public static class BenchChartColors
{
    public static bool Dark => Avalonia.Application.Current?.ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark;
    public static SkiaSharp.SKColor Background => Dark ? new SkiaSharp.SKColor(0x1E, 0x1E, 0x1E) : SkiaSharp.SKColors.White;
    public static SkiaSharp.SKColor Text => Dark ? new SkiaSharp.SKColor(0xF0, 0xF0, 0xF0) : new SkiaSharp.SKColor(0x20, 0x20, 0x20);
    public static SkiaSharp.SKColor Soft => Dark ? new SkiaSharp.SKColor(0xB0, 0xB0, 0xB0) : new SkiaSharp.SKColor(0x60, 0x60, 0x60);
    public static SkiaSharp.SKColor Grid => Dark ? new SkiaSharp.SKColor(0x40, 0x40, 0x40) : new SkiaSharp.SKColor(0xE0, 0xE0, 0xE0);
}

/// <summary>차트 시나리오 공통 옵션.</summary>
/// <remarks>Anim: "off"(기본) | "proto" — ScottPlot 경량 애니메이션 시제품(D-50 M10, team-lead 승인). 다른 어댑터는 무시한다(LiveCharts2 는 Animate 를 쓴다).</remarks>
public sealed record ChartBenchOptions(string Title, string XName, string YName, bool Animate, int MaxPoints, string Downsample, string Font = BenchFonts.DefaultKey, string Anim = "off");

public static class ChartAdapters
{
    private static readonly List<(string Key, Func<IChartAdapter> Create)> All = new();

    static ChartAdapters()
    {
#if PD_CHART_SCOTTPLOT
        All.Add(("scottplot", () => new ScottPlotAdapter()));
#endif
#if PD_CHART_PROCHARTS
        All.Add(("procharts", () => new ProChartsAdapter()));
#endif
#if PD_CHART_LIVECHARTS
        All.Add(("livecharts", () => new LiveChartsAdapter()));
#endif
#if PD_CHART_OXYPLOT
        All.Add(("oxyplot", () => new OxyPlotAdapter())); // D-50 신규 실측 후보(team-lead 승인 2026-10-29)
#endif
    }

    public static IEnumerable<string> Available
    {
        get { foreach (var (k, _) in All) { yield return k; } }
    }

    public static IChartAdapter Create(string key)
    {
        foreach (var (k, c) in All)
        {
            if (k.Equals(key, StringComparison.OrdinalIgnoreCase)) { return c(); }
        }

        throw new ArgumentException($"이 빌드에 없는 차트 라이브러리: {key} (있는 것: {string.Join(",", Available)})");
    }
}
