// 제목 탭 패널 — TitleTabWidth 규칙으로 탭 폭을 정해 가로로 늘어놓고, 넘칠 때만 스크롤 영역보다 넓게 보고한다
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace xkxk.frameworks.ui.Controls;

/// <summary>
/// 제목 줄의 제목 탭 목록 전용 패널. 규칙 정본은 plan/09_화면설계/01_화면설계_공통원칙.md §2-1 이다.
/// 가로 스크롤이 켜진 목록 안에서는 측정 폭이 무한대로 오므로 바깥 ScrollViewer 의 실제 폭을 가용 폭으로 쓰고, 그 폭이 바뀌면 다시 측정한다.
/// </summary>
public sealed class TitleTabPanel : Panel
{
    public static readonly StyledProperty<double> MinTabWidthProperty =
        AvaloniaProperty.Register<TitleTabPanel, double>(nameof(MinTabWidth), TitleTabWidth.DefaultMin);

    public static readonly StyledProperty<double> MaxTabWidthProperty =
        AvaloniaProperty.Register<TitleTabPanel, double>(nameof(MaxTabWidth), TitleTabWidth.DefaultMax);

    public static readonly StyledProperty<double> SpacingProperty =
        AvaloniaProperty.Register<TitleTabPanel, double>(nameof(Spacing));

    private ScrollViewer? _host;
    private double[] _widths = Array.Empty<double>();

    static TitleTabPanel() => AffectsMeasure<TitleTabPanel>(MinTabWidthProperty, MaxTabWidthProperty, SpacingProperty);

    public double MinTabWidth { get => GetValue(MinTabWidthProperty); set => SetValue(MinTabWidthProperty, value); }

    public double MaxTabWidth { get => GetValue(MaxTabWidthProperty); set => SetValue(MaxTabWidthProperty, value); }

    /// <summary>탭 사이 간격(DIP). 가용 폭에서 먼저 뺀 뒤 탭 폭을 계산한다.</summary>
    public double Spacing { get => GetValue(SpacingProperty); set => SetValue(SpacingProperty, value); }

    protected override Size MeasureOverride(Size availableSize)
    {
        var count = Children.Count;
        var natural = new double[count];
        var unbounded = new Size(double.PositiveInfinity, availableSize.Height);
        for (var i = 0; i < count; i++)
        {
            Children[i].Measure(unbounded);
            natural[i] = Children[i].DesiredSize.Width;
        }

        var gaps = Spacing * Math.Max(0, count - 1);
        var available = AvailableWidth(availableSize.Width);
        var (widths, overflow) = TitleTabWidth.Compute(natural, available - gaps, MinTabWidth, MaxTabWidth);
        _widths = widths;

        var total = gaps;
        var height = 0d;
        for (var i = 0; i < count; i++)
        {
            Children[i].Measure(new Size(widths[i], availableSize.Height));
            total += widths[i];
            height = Math.Max(height, Children[i].DesiredSize.Height);
        }

        // 넘치지 않으면 소수점 오차로 스크롤 막대가 생기지 않게 가용 폭 안으로 보고한다
        return new Size(overflow ? total : Math.Min(total, available), height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var x = 0d;
        for (var i = 0; i < Children.Count; i++)
        {
            var width = i < _widths.Length ? _widths[i] : Children[i].DesiredSize.Width;
            Children[i].Arrange(new Rect(x, 0, width, finalSize.Height));
            x += width + Spacing;
        }

        return finalSize;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _host = this.FindAncestorOfType<ScrollViewer>();
        if (_host is not null)
        {
            _host.SizeChanged += OnHostSizeChanged;
            InvalidateMeasure();
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_host is not null)
        {
            _host.SizeChanged -= OnHostSizeChanged;
            _host = null;
        }

        base.OnDetachedFromVisualTree(e);
    }

    // 첫 배치 전에는 폭을 모르므로 자연 폭으로 측정하고, 폭이 정해지면 OnHostSizeChanged 가 다시 측정한다.
    private double AvailableWidth(double measured)
    {
        if (!double.IsInfinity(measured))
        {
            return measured;
        }

        return _host is { Bounds.Width: > 0 } host ? host.Bounds.Width : double.PositiveInfinity;
    }

    private void OnHostSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (e.WidthChanged)
        {
            InvalidateMeasure();
        }
    }
}
