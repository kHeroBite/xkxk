// ProCharts 어댑터 — StreamingChartDataSource(정적은 용량=점 수, 스트리밍은 용량=창 크기) + 요청 단위 다운샘플링(LTTB 등)
#if PD_CHART_PROCHARTS
using Pd.Poc.Shell;
using System;
using Avalonia.Controls;
using ProCharts;
using ProCharts.Avalonia;
using ProCharts.Skia;

namespace Pd.Ui.Views.Documents.Bench;

/// <summary>
/// ProCharts 공개 API 에는 글꼴(SKTypeface) 지정이 없다(SkiaChartStyle 에 크기·색만 있음 — 2026-10-29 공개 API 덤프로 확인).
/// 그래서 한글 렌더링은 라이브러리 기본 글꼴 처리에 맡기고, 결과를 캡처로 판정한다.
/// </summary>
public sealed class ProChartsAdapter : IChartAdapter
{
    private readonly ProChartView _view = new();
    private ChartModel? _model;
    private StreamingChartDataSource? _source;
    private long _x;

    public string Name => "ProCharts 12.1.0.6";
    public Control View => _view;

    public void LoadStatic(double[] ys, ChartBenchOptions o)
    {
        var src = new StreamingChartDataSource(Math.Max(1, ys.Length), "측정값", ChartSeriesKind.Line);
        var samples = new ChartSample[ys.Length];
        for (var i = 0; i < ys.Length; i++) { samples[i] = new ChartSample(i, ys[i]); }
        src.AppendRange(samples);
        _x = ys.Length;
        Attach(src, o);
    }

    public void StartStream(int window, ChartBenchOptions o)
    {
        _x = 0;
        Attach(new StreamingChartDataSource(Math.Max(1, window), "실시간", ChartSeriesKind.Line), o);
    }

    public void Append(double[] batch, int count)
    {
        if (_source is null) { return; }
        var samples = new ChartSample[count];
        for (var i = 0; i < count; i++) { samples[i] = new ChartSample(_x++, batch[i]); }
        _source.AppendRange(samples); // DataInvalidated → 모델 AutoRefresh
        if (_model is { AutoRefresh: false }) { _model.Refresh(); }
    }

    private void Attach(StreamingChartDataSource src, ChartBenchOptions o)
    {
        _model?.Dispose();
        _source = src;
        var m = new ChartModel { DataSource = src };
        m.CategoryAxis.Title = o.XName;
        m.ValueAxis.Title = o.YName;
        m.Request.MaxPoints = o.MaxPoints > 0 ? o.MaxPoints : null;
        m.Request.DownsampleMode = Enum.TryParse<ChartDownsampleMode>(o.Downsample, true, out var mode) ? mode : ChartDownsampleMode.Lttb;
        _model = m;
        _view.ChartStyle = new SkiaChartStyle
        {
            ShowLegend = true,
            Background = BenchChartColors.Background, // 3종 공통 색
            Text = BenchChartColors.Text,
            Axis = BenchChartColors.Soft,
            Gridline = BenchChartColors.Grid,
        };
        _view.ChartModel = m;
        m.Refresh();
    }

    public void Dispose() => _model?.Dispose();
}
#endif
