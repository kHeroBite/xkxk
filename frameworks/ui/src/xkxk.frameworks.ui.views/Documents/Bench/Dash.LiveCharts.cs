// LiveCharts2 대시보드 어댑터 — 선·영역·막대·누적·콤보(CartesianChart)와 파이·도넛·게이지(PieChart)를 DashChartSpec 으로 만든다
#if PD_CHART_LIVECHARTS
using xkxk.frameworks.ui.shell;
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Avalonia;
using LiveChartsCore.SkiaSharpView.Extensions;
using LiveChartsCore.Themes;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.SkiaSharpView.VisualElements;
using SkiaSharp;

namespace xkxk.frameworks.ui.views.Documents.Bench;

public sealed class DashLiveCharts : IDashboardAdapter
{
    private static bool s_configured;

    public DashLiveCharts()
    {
        if (s_configured) { return; }
        LiveCharts.Configure(c => c.HasTextSettings(new TextSettings { DefaultTypeface = BenchFonts.Korean })); // LiveChartsAdapter 와 같은 한글 전역 글꼴
        s_configured = true;
    }

    public string Name => "LiveCharts2 2.1.0-dev-798";
    public bool Supports(DashKind kind) => true;

    public Control Create(DashChartSpec s, DashStyle st)
    {
        var speed = st.Animate ? TimeSpan.FromMilliseconds(500) : TimeSpan.Zero;
        Control ctl = s.Kind switch
        {
            DashKind.Pie or DashKind.Donut => PieChart(s, st, speed),
            DashKind.Gauge => GaugeChart(s, st, speed),
            _ => Cartesian(s, st, speed),
        };
        DashDragGate.Register(ctl); // L2: 끌기 중 얼림 대상 등록(Enabled=false 면 아무 일도 안 한다)
        return ctl;
    }

    private static SKColor Palette(int i)
    {
        var c = ColorPalletes.FluentDesign[i % ColorPalletes.FluentDesign.Length];
        return new SKColor(c.R, c.G, c.B, c.A);
    }

    // pd: 시리즈 색을 공용 PD 토큰(DashPdTokens)으로, default 는 라이브러리 기본 FluentDesign
    private static SKColor Col(int i, DashStyle st) => st.Pd ? DashPdTokens.Series(i) : Palette(i);

    private static CartesianChart Cartesian(DashChartSpec s, DashStyle st, TimeSpan speed)
    {
        var chart = new CartesianChart { AnimationsSpeed = speed, LegendPosition = LiveChartsCore.Measure.LegendPosition.Hidden };
        chart.Title = TitleVisual(s, st);
        var series = new List<ISeries>();
        for (var i = 0; i < s.Series.Length; i++)
        {
            var color = Col(i, st);
            var v = s.Series[i];
            switch (s.Kind)
            {
                case DashKind.Line: series.Add(new LineSeries<double> { Values = v, Name = s.SeriesNames[i], GeometrySize = 0, LineSmoothness = 0.3, Fill = null, Stroke = new SolidColorPaint(color, 2) }); break;
                case DashKind.Area: series.Add(new LineSeries<double> { Values = v, Name = s.SeriesNames[i], GeometrySize = 0, LineSmoothness = 0.3, Stroke = new SolidColorPaint(color, st.Pd ? 3 : 2), Fill = st.Pd ? AreaFill(color) : new SolidColorPaint(color.WithAlpha(60)) }); break;
                case DashKind.Bar: series.Add(new ColumnSeries<double> { Values = v, Name = s.SeriesNames[i], Fill = new SolidColorPaint(color), Rx = st.Pd ? 6 : 4, Ry = st.Pd ? 6 : 4 }); break;
                case DashKind.StackedBar: series.Add(new StackedColumnSeries<double> { Values = v, Name = s.SeriesNames[i], Fill = new SolidColorPaint(color) }); break;
                case DashKind.Combo:
                    if (i == 0) { series.Add(new ColumnSeries<double> { Values = v, Name = s.SeriesNames[i], Fill = new SolidColorPaint(color), Rx = 4, Ry = 4 }); }
                    else { series.Add(new LineSeries<double> { Values = v, Name = s.SeriesNames[i], GeometrySize = 8, ScalesYAt = 1, Stroke = new SolidColorPaint(color, 2), Fill = null }); }
                    break;
            }
        }

        chart.Series = series;
        var labels = s.Labels.Length > 0 ? s.Labels : null;
        chart.XAxes = new[] { Axis(st, labels) };
        chart.YAxes = s.Kind == DashKind.Combo ? new[] { Axis(st, null), Axis(st, null) } : new[] { Axis(st, null) };
        if (s.Series.Length > 1) { chart.LegendPosition = LiveChartsCore.Measure.LegendPosition.Bottom; }
        if (st.Pd) { chart.Background = BgBrush(); chart.LegendTextPaint = LegendPaint(); }
        return chart;
    }

    private static PieChart PieChart(DashChartSpec s, DashStyle st, TimeSpan speed)
    {
        var chart = new PieChart { AnimationsSpeed = speed, LegendPosition = LiveChartsCore.Measure.LegendPosition.Right };
        chart.Title = TitleVisual(s, st);
        var donut = s.Kind == DashKind.Donut;
        chart.Series = s.Labels.Select((l, i) => (ISeries)new PieSeries<double> { Values = new[] { s.Series[0][i] }, Name = l, InnerRadius = donut ? 50 : 0, Fill = new SolidColorPaint(Col(i, st)) }).ToList();
        if (st.Pd) { chart.Background = BgBrush(); chart.LegendTextPaint = LegendPaint(); }
        return chart;
    }

    private static PieChart GaugeChart(DashChartSpec s, DashStyle st, TimeSpan speed)
    {
        var chart = new PieChart { AnimationsSpeed = speed, InitialRotation = -225, MaxAngle = 270, MinValue = 0, MaxValue = 100 };
        chart.Title = TitleVisual(s, st);
        chart.Series = GaugeGenerator.BuildSolidGauge(
            new GaugeItem(s.Gauge, series => { series.MaxRadialColumnWidth = 24; series.Fill = new SolidColorPaint(Col(0, st)); }),
            new GaugeItem(GaugeItem.Background, series => { series.InnerRadius = 20; series.Fill = new SolidColorPaint(st.Pd ? DashPdTokens.GridLine : new SKColor(0xC8, 0xC8, 0xC8, 80)); })).ToList();
        if (st.Pd) { chart.Background = BgBrush(); chart.LegendTextPaint = LegendPaint(); }
        return chart;
    }

    private static Axis Axis(DashStyle st, string[]? labels)
    {
        var a = new Axis { Labels = labels, TextSize = 12 };
        if (st.Pd)
        {
            a.LabelsPaint = new SolidColorPaint(DashPdTokens.Muted) { SKTypeface = DashPdTokens.LiveChartsFont };
            a.SeparatorsPaint = new SolidColorPaint(DashPdTokens.GridLine);
        }

        return a;
    }

    private static LabelVisual TitleVisual(DashChartSpec s, DashStyle st)
    {
        var paint = new SolidColorPaint(st.Pd ? DashPdTokens.Text : new SKColor(0x30, 0x30, 0x30)) { SKTypeface = st.Pd ? DashPdTokens.LiveChartsFont : BenchFonts.Korean };
#pragma warning disable CS0618 // LabelVisual 은 2.1 에서 사용 중단 표시지만 LiveChartsAdapter 와 같은 방식으로 제목을 단다
        return new LabelVisual { Text = s.Title, TextSize = 15, Paint = paint };
#pragma warning restore CS0618
    }

    private static Avalonia.Media.IBrush BgBrush()
    {
        var bg = DashPdTokens.Surface;
        return new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(bg.Red, bg.Green, bg.Blue));
    }

    private static SolidColorPaint LegendPaint() => new(DashPdTokens.Text) { SKTypeface = DashPdTokens.LiveChartsFont };

    // L1(pd): 영역 채움을 위쪽 진하게→아래 투명 그라데이션으로(확인 필요: LinearGradientPaint 생성자 시그니처, 컴파일 전)
    private static LinearGradientPaint AreaFill(SKColor c) => new(c.WithAlpha(110), c.WithAlpha(0), new SKPoint(0.5f, 0), new SKPoint(0.5f, 1));
}
#endif
