// ScottPlot 5 어댑터 — Signal(대용량 정적)·DataStreamer(롤링 스트리밍), 한글은 IFontResolver 로 BenchFonts 글꼴을 넘긴다
#if PD_CHART_SCOTTPLOT
using xkxk.frameworks.ui.shell;
using System;
using Avalonia.Controls;
using ScottPlot;
using ScottPlot.Avalonia;
using ScottPlot.Plottables;
using SkiaSharp;

namespace xkxk.frameworks.ui.views.Documents.Bench;

public sealed class ScottPlotAdapter : IChartAdapter
{
    private const string KoreanFontPrefix = "PdKorean-"; // 글꼴 이름 = 접두 + BenchFonts 키(noto|nanum|os)
    private static bool s_resolverAdded;
    private readonly AvaPlot _plot = new();
    private DataStreamer? _streamer;

    public ScottPlotAdapter()
    {
        if (!s_resolverAdded)
        {
            Fonts.FontResolvers.Insert(0, new KoreanResolver());
            s_resolverAdded = true;
        }
    }

    public string Name => "ScottPlot 5.1.59";
    public Control View => _plot;

    public void LoadStatic(double[] ys, ChartBenchOptions o)
    {
        _plot.Reset();
        var p = _plot.Plot;
        _streamer = null;
        var sig = p.Add.Signal(ys);
        sig.LegendText = "측정값";
        Decorate(p, o);
        p.Axes.AutoScale();
        _plot.Refresh();
        _proto = o.Anim == "proto";
        if (_proto) { StartEntryAnimation(p); }
    }

    public void StartStream(int window, ChartBenchOptions o)
    {
        _plot.Reset();
        var p = _plot.Plot;
        _streamer = p.Add.DataStreamer(window);
        _streamer.LegendText = "실시간";
        _streamer.ViewScrollLeft();
        Decorate(p, o);
        _plot.Refresh();
        _proto = o.Anim == "proto";
        _animIdx = -1;
    }

    public void Append(double[] batch, int count)
    {
        if (_streamer is null || count <= 0) { return; }
        if (!_proto)
        {
            for (var i = 0; i < count; i++) { _streamer.Add(batch[i]); }
            _plot.Refresh();
            return;
        }

        // 시제품(마지막 구간 보간): 진행 중이던 보간은 목표값으로 확정하고, 새 묶음의 마지막 점만 직전 값 → 새 값으로 보간한다.
        // 점당 객체 없이 배열 칸 1개만 고쳐 쓴다(DataStreamerSource.Data·NewestIndex)
        var src = _streamer.Data;
        if (_animIdx >= 0) { src.Data[_animIdx] = _animTarget; }
        var start = src.CountTotal > 0 ? src.Data[src.NewestIndex] : batch[count - 1];
        for (var i = 0; i < count; i++) { _streamer.Add(batch[i]); }
        _animIdx = src.NewestIndex;
        _animTarget = batch[count - 1];
        _animStart = start;
        _animClock.Restart();
        src.Data[_animIdx] = start;
        _plot.Refresh();
        RequestTick();
    }

    // ---- 경량 애니메이션 시제품(D-50 M10, anim=proto 일 때만) — 프레임 콜백으로만 돌고, 끝나면 멈춘다 ----
    private const double SegmentMs = 200; // 마지막 구간 보간 시간
    private const double EntryMs = 300;   // 진입 효과 시간
    private bool _proto;
    private bool _tickPending;
    private int _animIdx = -1;
    private double _animStart, _animTarget;
    private readonly System.Diagnostics.Stopwatch _animClock = new();
    private double _entryBottom, _entryTop, _entryFromBottom, _entryFromTop;
    private bool _entryActive;

    private static double EaseOut(double t) => 1 - Math.Pow(1 - Math.Clamp(t, 0, 1), 3);

    /// <summary>진입 효과: Y 범위를 넓게(데이터가 납작하게) 시작해 실제 범위로 줄인다 — 시리즈 Y 배율 0→1 과 같은 모양.</summary>
    private void StartEntryAnimation(Plot p)
    {
        var lim = p.Axes.GetLimits();
        _entryBottom = lim.Bottom;
        _entryTop = lim.Top;
        var mid = (lim.Bottom + lim.Top) / 2;
        var half = Math.Max(1e-9, (lim.Top - lim.Bottom) / 2) * 8;
        _entryFromBottom = mid - half;
        _entryFromTop = mid + half;
        _entryActive = true;
        _animClock.Restart();
        p.Axes.SetLimitsY(_entryFromBottom, _entryFromTop);
        _plot.Refresh();
        RequestTick();
    }

    private void RequestTick()
    {
        if (_tickPending) { return; }
        var top = Avalonia.Controls.TopLevel.GetTopLevel(_plot);
        if (top is null) { return; }
        _tickPending = true;
        top.RequestAnimationFrame(_ => OnTick());
    }

    private void OnTick()
    {
        _tickPending = false;
        var ms = _animClock.Elapsed.TotalMilliseconds;
        var again = false;
        if (_entryActive)
        {
            var k = EaseOut(ms / EntryMs);
            _plot.Plot.Axes.SetLimitsY(_entryFromBottom + (_entryBottom - _entryFromBottom) * k, _entryFromTop + (_entryTop - _entryFromTop) * k);
            if (ms >= EntryMs) { _entryActive = false; } else { again = true; }
        }

        if (_streamer is not null && _animIdx >= 0)
        {
            var k = EaseOut(ms / SegmentMs);
            _streamer.Data.Data[_animIdx] = _animStart + (_animTarget - _animStart) * k;
            if (ms >= SegmentMs) { _streamer.Data.Data[_animIdx] = _animTarget; _animIdx = -1; } else { again = true; }
        }

        _plot.Refresh();
        if (again) { RequestTick(); }
    }

    private static void Decorate(Plot p, ChartBenchOptions o)
    {
        var fontName = KoreanFontPrefix + o.Font;
        p.Font.Set(fontName);
        p.Legend.FontName = fontName; // Font.Set 은 범례 글꼴 이름을 비워 둔다(재현 도구 logs/odevgc/spfont 확인) — WASM 은 기본 글꼴에 한글이 없으므로 명시
        p.Title(o.Title);
        p.XLabel(o.XName);
        p.YLabel(o.YName);
        p.ShowLegend();
        // 3종 공통 색(BenchChartColors)
        var bg = new Color(BenchChartColors.Background);
        p.FigureBackground.Color = bg;
        p.DataBackground.Color = bg;
        p.Axes.Color(new Color(BenchChartColors.Text));
        p.Grid.MajorLineColor = new Color(BenchChartColors.Grid);
        p.Legend.FontColor = new Color(BenchChartColors.Text);
        p.Legend.BackgroundColor = bg;
        p.Legend.OutlineColor = new Color(BenchChartColors.Soft);
    }

    public void Dispose()
    {
        _entryActive = false;
        _animIdx = -1;
        _streamer = null;
    }

    /// <summary>"PdKorean-<키>" 이름을 BenchFonts.Get(키)(3종 공통 임베드 글꼴)로 풀어 준다.</summary>
    private sealed class KoreanResolver : IFontResolver
    {
        private static SKTypeface? Map(string fontName) =>
            fontName.StartsWith(KoreanFontPrefix, StringComparison.Ordinal) ? BenchFonts.Get(fontName[KoreanFontPrefix.Length..]) : null;

        public SKTypeface? CreateTypeface(string fontName, FontWeight weight, FontSlant slant, FontSpacing spacing) => Map(fontName);

        public SKTypeface? CreateTypeface(string fontName, bool bold, bool italic) => Map(fontName);
    }
}
#endif
