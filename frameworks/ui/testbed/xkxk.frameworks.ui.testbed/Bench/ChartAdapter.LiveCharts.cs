// LiveCharts2(2.1.0-dev) 어댑터 — LineSeries 정적·롤링(ObservableCollection, 점마다 갱신 기본 / anim=batch 묶음 갱신), 애니메이션 켬/끔, 한글은 전역 SKTypeface + 축·제목 페인트
#if PD_CHART_LIVECHARTS
using xkxk.frameworks.ui.shell;
using System;
using System.Collections.ObjectModel;
using Avalonia.Controls;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Avalonia;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.SkiaSharpView.VisualElements;
using SkiaSharp;

namespace xkxk.frameworks.ui.testbed.Bench;

public sealed class LiveChartsAdapter : IChartAdapter
{
    private static bool s_configured;
    private readonly CartesianChart _chart = new();
    private ObservableCollection<double>? _rolling;
    private int _window;
    private bool _batch; // anim=batch·fair: 묶음마다 한 번만 변경 통지(공정성 보완 변형, team-lead 승인 2026-10-29)

    public LiveChartsAdapter()
    {
        if (!s_configured)
        {
            LiveCharts.Configure(c => c.HasTextSettings(new TextSettings { DefaultTypeface = BenchFonts.Korean })); // 2.1: HasGlobalSKTypeface 대체(사용 중단 경고)
            s_configured = true;
        }
    }

    public string Name => _batch ? $"LiveCharts2 2.1.0-dev-798 ({_variant})" : "LiveCharts2 2.1.0-dev-798";
    private string _variant = "batch";
    public Control View => _chart;

    public void LoadStatic(double[] ys, ChartBenchOptions o)
    {
        _rolling = null;
        _batch = false;
        Setup(o, new LineSeries<double> { Values = ys, Name = "측정값", GeometrySize = 0, LineSmoothness = 0, Fill = null });
    }

    public void StartStream(int window, ChartBenchOptions o)
    {
        _window = window;
        // 기본(anim=off 등)은 옛 방식 그대로다. anim=batch·fair 일 때만 묶음 갱신 컬렉션을 쓴다(벤치 인자 anim 을 빌려 쓴다 — 다른 어댑터는 proto 외 값을 무시)
        // batch = 묶음 갱신만, fair = 묶음 갱신 + 직선(lc_fair, team-lead 승인 22:0x — 다른 후보와 같은 직선 조건)
        _batch = o.Anim is "batch" or "fair";
        _rolling = _batch ? new BatchCollection() : new ObservableCollection<double>();
        _variant = o.Anim;
        var smooth = o.Anim == "fair" ? 0 : 0.3;
        Setup(o, new LineSeries<double> { Values = _rolling, Name = "실시간", GeometrySize = 0, LineSmoothness = smooth, Fill = null });
    }

    public void Append(double[] batch, int count)
    {
        if (_rolling is null) { return; }
        if (_rolling is BatchCollection bc)
        {
            bc.AppendRolling(batch, count, _window);
            return;
        }

        for (var i = 0; i < count; i++)
        {
            _rolling.Add(batch[i]);
            if (_rolling.Count > _window) { _rolling.RemoveAt(0); }
        }
    }

    private void Setup(ChartBenchOptions o, ISeries series)
    {
        var tf = BenchFonts.Get(o.Font); // 3종 공통 글꼴(키별)
        // 3종 공통 색(BenchChartColors) — 고정 DimGray 는 어두운 테마에서 흐렸다(otest-1 관찰)
        var strong = BenchChartColors.Text;
        var soft = BenchChartColors.Soft;
        var bg = BenchChartColors.Background;
        _chart.Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(bg.Red, bg.Green, bg.Blue));
        _chart.AnimationsSpeed = o.Animate ? TimeSpan.FromMilliseconds(300) : TimeSpan.Zero;
#pragma warning disable CS0618 // LabelVisual 은 2.1 에서 사용 중단 표시(DrawnLabelVisual 권장)지만 제목 글꼴 지정이 단순해 벤치에서는 그대로 쓴다
        _chart.Title = new LabelVisual { Text = o.Title, TextSize = 16, Paint = new SolidColorPaint(strong) { SKTypeface = tf } };
#pragma warning restore CS0618
        var grid = new SolidColorPaint(BenchChartColors.Grid);
        _chart.XAxes = new[] { new Axis { Name = o.XName, NamePaint = new SolidColorPaint(strong) { SKTypeface = tf }, LabelsPaint = new SolidColorPaint(soft) { SKTypeface = tf }, SeparatorsPaint = grid } };
        _chart.YAxes = new[] { new Axis { Name = o.YName, NamePaint = new SolidColorPaint(strong) { SKTypeface = tf }, LabelsPaint = new SolidColorPaint(soft) { SKTypeface = tf }, SeparatorsPaint = grid } };
        _chart.LegendTextPaint = new SolidColorPaint(strong) { SKTypeface = tf };
        _chart.LegendPosition = LiveChartsCore.Measure.LegendPosition.Top;
        _chart.Series = new[] { series };
    }

    public void Dispose() { }

    /// <summary>
    /// 묶음 갱신 컬렉션. 묶음 안의 추가·앞쪽 삭제는 내부 목록(List)에 바로 하고, 끝에 Reset 통지를 한 번만 낸다.
    /// LiveCharts2 는 INotifyCollectionChanged 를 받아 갱신을 10ms 단위로 묶는다(공식 문서 'Automatic Updates').
    /// 값 형식(double) 항목은 항목별 구독이 없어 Reset 도 '다시 그리기' 1회로 처리된다[CollectionDeepObserver 소스 확인, master 기준].
    /// </summary>
    private sealed class BatchCollection : ObservableCollection<double>
    {
        public void AppendRolling(double[] batch, int count, int window)
        {
            if (count <= 0) { return; }
            var list = (System.Collections.Generic.List<double>)Items; // ObservableCollection 기본 생성자의 내부 목록은 List<T>
            for (var i = 0; i < count; i++) { list.Add(batch[i]); }
            var over = list.Count - window;
            if (over > 0) { list.RemoveRange(0, over); } // 점마다 RemoveAt(0) 대신 묶음당 1회 이동
            OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs("Count"));
            OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs("Item[]"));
            OnCollectionChanged(new System.Collections.Specialized.NotifyCollectionChangedEventArgs(System.Collections.Specialized.NotifyCollectionChangedAction.Reset));
        }
    }
}
#endif
