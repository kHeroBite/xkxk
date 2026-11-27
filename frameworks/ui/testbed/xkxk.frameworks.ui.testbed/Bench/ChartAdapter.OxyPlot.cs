// OxyPlot.Avalonia 2.2.0 어댑터 — LineSeries 정적·롤링 스트리밍. 글자는 Avalonia 텍스트 스택으로 그려지므로 글꼴은 FontFamily 이름으로 넘긴다
#if PD_CHART_OXYPLOT
using xkxk.frameworks.ui.shell;
using System;
using System.Collections.Generic;
using Avalonia.Controls;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Legends;
using OxyPlot.Series;

namespace xkxk.frameworks.ui.testbed.Bench;

/// <summary>
/// 공정 비교 규칙은 다른 어댑터와 같다. 공통 색(BenchChartColors), 같은 제목·축 이름·범례, 선 하나다.
/// - 정적: DataPoint(x=인덱스) 목록을 한 번에 넣는다. OxyPlot 의 기본 입력 방식이며, 점당 16바이트 구조체다.
/// - 스트리밍: 창(window)을 넘으면 앞에서 지운다. 내장 애니메이션이 없어 animate 는 무시한다.
/// - 글꼴: noto 는 앱 기본 글꼴(Avalonia 폰트 폴백)을 따른다. nanum 은 벤치 임베드 나눔고딕, os 는 맑은 고딕이다.
/// 첫 사용 때 OxyPlot 테마(Default.axaml)를 Application.Styles 에 1번 붙인다. 이 테마가 없으면 PlotView 템플릿이 그려지지 않는다(패키지 README).
/// </summary>
public sealed class OxyPlotAdapter : IChartAdapter
{
    private static bool s_themeAdded;
    private readonly OxyPlot.Avalonia.PlotView _view = new();
    private PlotModel? _model;
    private LineSeries? _series;
    private int _window;
    private double _nextX;

    public OxyPlotAdapter()
    {
        if (!s_themeAdded && Avalonia.Application.Current is { } app)
        {
            app.Styles.Add(new Avalonia.Markup.Xaml.Styling.StyleInclude(new Uri("avares://xkxk.frameworks.ui.testbed/"))
            {
                Source = new Uri("avares://OxyPlot.Avalonia/Themes/Default.axaml"),
            });
            s_themeAdded = true;
        }
    }

    public string Name => "OxyPlot.Avalonia 2.2.0";
    public Control View => _view;

    public void LoadStatic(double[] ys, ChartBenchOptions o)
    {
        var (m, s) = Build(o, "측정값");
        var pts = new List<DataPoint>(ys.Length);
        for (var i = 0; i < ys.Length; i++) { pts.Add(new DataPoint(i, ys[i])); }
        s.Points.AddRange(pts);
        _series = null;
        _view.Model = m;
        m.InvalidatePlot(true);
    }

    public void StartStream(int window, ChartBenchOptions o)
    {
        var (m, s) = Build(o, "실시간");
        _series = s;
        _window = window;
        _nextX = 0;
        _view.Model = m;
        m.InvalidatePlot(true);
    }

    public void Append(double[] batch, int count)
    {
        if (_series is null || _model is null) { return; }
        var pts = _series.Points;
        for (var i = 0; i < count; i++) { pts.Add(new DataPoint(_nextX++, batch[i])); }
        var over = pts.Count - _window;
        if (over > 0) { pts.RemoveRange(0, over); }
        _model.InvalidatePlot(true);
    }

    private (PlotModel, LineSeries) Build(ChartBenchOptions o, string seriesName)
    {
        var text = Oxy(BenchChartColors.Text);
        var soft = Oxy(BenchChartColors.Soft);
        var grid = Oxy(BenchChartColors.Grid);
        var bg = Oxy(BenchChartColors.Background);
        var m = new PlotModel
        {
            Title = o.Title,
            DefaultFont = FontFor(o.Font),
            TextColor = text,
            TitleColor = text,
            Background = bg,
            PlotAreaBorderColor = soft,
        };
        m.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Bottom, Title = o.XName, TextColor = text, TitleColor = text,
            AxislineColor = soft, TicklineColor = soft, MajorGridlineStyle = LineStyle.Solid, MajorGridlineColor = grid,
        });
        m.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Left, Title = o.YName, TextColor = text, TitleColor = text,
            AxislineColor = soft, TicklineColor = soft, MajorGridlineStyle = LineStyle.Solid, MajorGridlineColor = grid,
        });
        m.Legends.Add(new Legend
        {
            LegendPosition = LegendPosition.TopRight, LegendTextColor = text, LegendBackground = bg, LegendBorder = soft,
        });
        var s = new LineSeries { Title = seriesName, StrokeThickness = 1 };
        m.Series.Add(s);
        _model = m;
        return (m, s);
    }

    /// <summary>글꼴 키 → Avalonia FontFamily 이름. noto 는 null(앱 기본 글꼴·폴백)이다.</summary>
    private static string? FontFor(string key) => key.ToLowerInvariant() switch
    {
        "nanum" => "avares://xkxk.frameworks.ui.testbed/Bench/Assets/Fonts#NanumGothic",
        "os" => "Malgun Gothic",
        _ => null,
    };

    private static OxyColor Oxy(SkiaSharp.SKColor c) => OxyColor.FromRgb(c.Red, c.Green, c.Blue);

    public void Dispose()
    {
        _view.Model = null;
        _model = null;
        _series = null;
    }
}
#endif
