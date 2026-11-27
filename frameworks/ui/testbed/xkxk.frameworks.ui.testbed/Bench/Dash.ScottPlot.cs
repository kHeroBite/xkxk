// ScottPlot 5 대시보드 어댑터 — 선·영역·막대·누적·콤보(Cartesian)와 파이·도넛·게이지(원형)를 DashChartSpec 으로 만든다
#if PD_CHART_SCOTTPLOT
using xkxk.frameworks.ui.shell;
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using ScottPlot;
using ScottPlot.Avalonia;
using ScottPlot.TickGenerators;

namespace xkxk.frameworks.ui.testbed.Bench;

public sealed class DashScottPlot : IDashboardAdapter
{
    private const string FontKey = "nanum"; // 벤치 한정: ScottPlot 은 차트 전용 나눔고딕(ChartBenchView.DefaultFont 와 같은 정책)
    private static bool s_fontReady;

    public string Name => "ScottPlot 5.1.59";

    // 게이지는 방사형 게이지(RadialGaugePlot), 콤보는 오른쪽 보조축으로 그린다. 둘 다 컴파일 확인 전이다.
    public bool Supports(DashKind kind) => true;

    public Control Create(DashChartSpec s, DashStyle st)
    {
        EnsureFont();
        var ap = new AvaPlot();
        var p = ap.Plot;
        var font = "PdKorean-" + (st.Pd ? DashPdTokens.ScottPlotFontKey : FontKey); // pd: ScottPlot 최선 글꼴(nanum — Noto KR 은 회전 축·범례 깨짐)
        p.Font.Set(font);
        p.Legend.FontName = font;
        if (st.Pd) { p.Add.Palette = new PdPalette(); } // pd: 시리즈 색을 PD 토큰으로(이후 Add.* 전부에 적용)
        p.Title(s.Title);
        switch (s.Kind)
        {
            case DashKind.Line: Lines(p, s, fill: false); break;
            case DashKind.Area: Lines(p, s, fill: true); break;
            case DashKind.Bar: Bars(p, s, stacked: false); break;
            case DashKind.StackedBar: Bars(p, s, stacked: true); break;
            case DashKind.Combo: Combo(p, s); break;
            case DashKind.Pie: Pie(p, s, donut: false); break;
            case DashKind.Donut: Pie(p, s, donut: true); break;
            case DashKind.Gauge: Gauge(p, s); break;
        }

        if (st.Pd) { PdColors(p); }
        ap.Refresh();
        return ap;
    }

    // ScottPlotAdapter 의 한글 글꼴 해석기(생성자에서 한 번 등록)를 재사용하려고 어댑터를 한 번 만들어 둔다 — 기존 파일을 고치지 않기 위한 우회
    private static void EnsureFont()
    {
        if (s_fontReady) { return; }
        new ScottPlotAdapter().Dispose();
        s_fontReady = true;
    }

    private static void Lines(Plot p, DashChartSpec s, bool fill)
    {
        for (var i = 0; i < s.Series.Length; i++)
        {
            var color = p.Add.Palette.GetColor(i);
            if (fill)
            {
                var xs = Enumerable.Range(0, s.Series[i].Length).Select(x => (double)x).ToArray();
                var sc = p.Add.Scatter(xs, s.Series[i]);
                sc.MarkerSize = 0;
                sc.Color = color;
                sc.FillY = true;
                sc.FillYColor = color.WithAlpha(0.25);
                sc.LegendText = s.SeriesNames[i];
            }
            else
            {
                var sig = p.Add.Signal(s.Series[i]);
                sig.Color = color;
                sig.LegendText = s.SeriesNames[i];
            }
        }

        p.ShowLegend();
        p.Axes.AutoScale();
    }

    private static Tick[] CategoryTicks(string[] labels) => labels.Select((l, i) => new Tick(i, l)).ToArray();

    private static void Bars(Plot p, DashChartSpec s, bool stacked)
    {
        var n = s.Labels.Length;
        var baseValues = new double[n];
        for (var i = 0; i < s.Series.Length; i++)
        {
            var color = p.Add.Palette.GetColor(i);
            var bars = new List<Bar>();
            for (var j = 0; j < n; j++)
            {
                var v = s.Series[i][j];
                var pos = stacked ? j : j + (i - (s.Series.Length - 1) / 2.0) * (0.8 / s.Series.Length);
                bars.Add(new Bar { Position = pos, ValueBase = stacked ? baseValues[j] : 0, Value = stacked ? baseValues[j] + v : v, FillColor = color, Size = stacked ? 0.6 : 0.8 / s.Series.Length });
                if (stacked) { baseValues[j] += v; }
            }

            var bp = p.Add.Bars(bars);
            bp.LegendText = s.SeriesNames[i];
        }

        p.Axes.Bottom.TickGenerator = new NumericManual(CategoryTicks(s.Labels));
        p.Axes.Margins(bottom: 0);
        if (s.Series.Length > 1) { p.ShowLegend(); }
    }

    // 콤보: 첫 시리즈는 막대(왼쪽 축), 둘째 시리즈는 선(오른쪽 보조축)
    private static void Combo(Plot p, DashChartSpec s)
    {
        var bars = p.Add.Bars(s.Series[0]);
        bars.LegendText = s.SeriesNames[0];
        var xs = Enumerable.Range(0, s.Labels.Length).Select(x => (double)x).ToArray();
        var line = p.Add.Scatter(xs, s.Series[1]);
        line.Color = p.Add.Palette.GetColor(1);
        line.LegendText = s.SeriesNames[1];
        var right = p.Axes.AddRightAxis();
        line.Axes.YAxis = right;
        p.Axes.Bottom.TickGenerator = new NumericManual(CategoryTicks(s.Labels));
        p.ShowLegend();
    }

    private static void Pie(Plot p, DashChartSpec s, bool donut)
    {
        var slices = s.Labels.Select((l, i) => new PieSlice { Value = s.Series[0][i], FillColor = p.Add.Palette.GetColor(i), Label = l }).ToList();
        var pie = p.Add.Pie(slices);
        if (donut) { pie.DonutFraction = 0.55; }
        p.Axes.Frameless();
        p.HideGrid();
        p.ShowLegend();
    }

    private static void Gauge(Plot p, DashChartSpec s)
    {
        var g = p.Add.RadialGaugePlot(new[] { s.Gauge });
        _ = g;
        p.Axes.Frameless();
        p.HideGrid();
    }

    // pd: 배경·글자·격자·범례를 공용 PD 토큰(DashPdTokens)으로. 축 글자는 Muted, 제목·범례는 Text
    private static void PdColors(Plot p)
    {
        var bg = new Color(DashPdTokens.Surface);
        p.FigureBackground.Color = bg;
        p.DataBackground.Color = bg;
        p.Axes.Color(new Color(DashPdTokens.Muted));
        p.Axes.Title.Label.ForeColor = new Color(DashPdTokens.Text);
        p.Grid.MajorLineColor = new Color(DashPdTokens.GridLine);
        p.Legend.FontColor = new Color(DashPdTokens.Text);
        p.Legend.BackgroundColor = bg;
        p.Legend.OutlineColor = new Color(DashPdTokens.Border);
    }

    // ScottPlot 팔레트 인터페이스(IPalette: Colors·Name·Description·GetColor — 공식 소스 Interfaces/IPalette.cs, main 기준)에 PD 시리즈 토큰을 넣는다
    private sealed class PdPalette : IPalette
    {
        public Color[] Colors { get; } = Enumerable.Range(0, 6).Select(i => new Color(DashPdTokens.Series(i))).ToArray();
        public string Name => "PD";
        public string Description => "PD 테마 시리즈 토큰";
        public Color GetColor(int index) => Colors[index % Colors.Length];
    }
}
#endif
