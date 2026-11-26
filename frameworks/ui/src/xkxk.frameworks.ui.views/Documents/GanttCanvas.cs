// 간트 막대·주 단위 눈금을 Render 오버라이드로 직접 그리는 가상 캔버스(보이는 행만 그림)
using xkxk.frameworks.ui.shell;
using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using xkxk.frameworks.ui.shell.Theming;
using xkxk.frameworks.ui.Input;
using xkxk.frameworks.ui.shell.Framework;

namespace xkxk.frameworks.ui.views.Documents;

/// <summary>간트 작업 1건.</summary>
public sealed record GanttTask(int No, string Name, DateTime Start, int Days, int Progress, int? DependsOn);

/// <summary>
/// 스크롤은 자체 오프셋(OffsetX/OffsetY)으로 처리하고, 보이는 행·보이는 주 눈금만 그린다.
/// 휠=세로, Shift+휠=가로(Ctrl+휠은 전역 글자 크기용으로 통과). Alt=줌·Alt+Shift=행 높이는 공통 입력 DenseWheel 이 보낸다(GanttCanvas.Dense.cs). 외부 스크롤바는 ExtentChanged/OffsetChanged 로 동기화한다.
/// </summary>
public sealed partial class GanttCanvas : Control
{
    public const double MaxLabelWidth = 180;

    /// <summary>글자 크기(셸 토큰 PdFontBody 를 뷰가 연결). 바뀌면 행 높이·머리글 높이도 비례해 다시 그린다.</summary>
    public static readonly StyledProperty<double> TextSizeProperty = AvaloniaProperty.Register<GanttCanvas, double>(nameof(TextSize), 12);

    /// <summary>폰트(셸 토큰 PdFontFamily 를 뷰가 연결).</summary>
    public static readonly StyledProperty<FontFamily> TextFamilyProperty = AvaloniaProperty.Register<GanttCanvas, FontFamily>(nameof(TextFamily), FontFamily.Default);

    // 색 토큰(UI-07, odev-5t 정의) — 순서 = 팔레트 인덱스. 토큰이 없으면 아래 라이트/다크 대체 팔레트를 쓴다.
    private static readonly string[] BrushKeys =
    {
        "PdBrushBackground", "PdBrushText", "PdBrushGridHeader", "PdBrushTextMuted",
        "PdBrushGanttBar", "PdBrushGanttProgress", "PdBrushGridStripe", "PdBrushGridLine",
    };

    private static readonly IBrush Bar = new SolidColorBrush(Color.FromRgb(0x4A, 0x90, 0xD9));
    private static readonly IBrush Done = new SolidColorBrush(Color.FromRgb(0x2E, 0x6B, 0xB0));
    private static readonly IBrush Stripe = new SolidColorBrush(Color.FromArgb(0x10, 0x80, 0x80, 0x80));
    private static readonly IBrush Line = new SolidColorBrush(Color.FromArgb(0x40, 0x80, 0x80, 0x80));
    private static readonly IBrush[] LightPalette =
        { Brushes.White, Brushes.Black, new SolidColorBrush(Color.FromRgb(0xEE, 0xEE, 0xEE)), Brushes.DimGray, Bar, Done, Stripe, Line };
    private static readonly IBrush[] DarkPalette =
    {
        new SolidColorBrush(Color.FromRgb(0x1F, 0x1F, 0x1F)), new SolidColorBrush(Color.FromRgb(0xE6, 0xE6, 0xE6)),
        new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x2D)), new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xA0)), Bar, Done, Stripe, Line,
    };

    private IBrush[] _pal = LightPalette; // Render 시작마다 토큰에서 다시 읽는다(테마 전환·색 편집 즉시 반영)
    private IPen _gridPen = new Pen(Line, 1);

    static GanttCanvas() => AffectsRender<GanttCanvas>(TextSizeProperty, TextFamilyProperty);

    public double TextSize { get => GetValue(TextSizeProperty); set => SetValue(TextSizeProperty, value); }

    public FontFamily TextFamily { get => GetValue(TextFamilyProperty); set => SetValue(TextFamilyProperty, value); }

    /// <summary>행 높이 = 글자 크기 × 1.7(14pt → 24px) × 행 배율 단계.</summary>
    public double RowHeight => Math.Round(TextSize * 1.7 * DenseScale.Factor(_rowLevel));

    public double HeaderHeight => Math.Round(TextSize * 2.1);

    /// <summary>색 토큰 조회(없으면 라이트/다크 대체 색).</summary>
    private IBrush[] ResolvePalette()
    {
        var fallback = ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark ? DarkPalette : LightPalette;
        var pal = new IBrush[BrushKeys.Length];
        for (var i = 0; i < pal.Length; i++)
        {
            pal[i] = this.TryFindResource(BrushKeys[i], ActualThemeVariant, out var o) && o is IBrush b ? b : fallback[i];
        }

        return pal;
    }

    private readonly List<GanttTask> _tasks;
    private readonly DateTime _origin;
    private readonly int _totalDays;
    private double _dayWidth = 6;
    private double _offsetX;
    private double _offsetY;

    public GanttCanvas(IReadOnlyList<GanttTask> tasks)
    {
        _tasks = new List<GanttTask>(tasks);
        _origin = DateTime.MaxValue;
        var end = DateTime.MinValue;
        foreach (var t in _tasks)
        {
            if (t.Start < _origin) { _origin = t.Start; }
            var e = t.Start.AddDays(t.Days);
            if (e > end) { end = e; }
        }

        if (_tasks.Count == 0) { _origin = DateTime.Today; end = _origin.AddDays(7); }
        _origin = _origin.AddDays(-(((int)_origin.DayOfWeek + 6) % 7)); // 월요일 정렬
        _totalDays = (int)Math.Ceiling((end - _origin).TotalDays) + 7;
        ClipToBounds = true;
        Focusable = true;
        ActualThemeVariantChanged += (_, _) => InvalidateVisual(); // 라이트/다크 팔레트 전환
    }

    public int TaskCount => _tasks.Count;

    public double DayWidth => _dayWidth;

    public double OffsetX => _offsetX;

    public double OffsetY => _offsetY;

    /// <summary>전체 내용 크기(막대 영역 기준, 라벨·머리글 제외).</summary>
    public Size Extent => new(_totalDays * _dayWidth, _tasks.Count * RowHeight);

    /// <summary>작업명 열 폭. 최대 180px(12pt 기준, 글자 크기 비례), 창 폭의 40% 이하(좁은 바둑판 창에서도 막대 영역 확보).</summary>
    private double LabelWidth => Math.Min(MaxLabelWidth * TextSize / 12, Math.Floor(Bounds.Width * 0.4));

    /// <summary>보이는 막대 영역 크기.</summary>
    public Size Viewport => new(Math.Max(0, Bounds.Width - LabelWidth), Math.Max(0, Bounds.Height - HeaderHeight));

    /// <summary>오프셋·줌·크기 변화 알림(스크롤바 동기화용).</summary>
    public event Action? ScrollChanged;

    public void ScrollTo(double x, double y)
    {
        var maxX = Math.Max(0, Extent.Width - Viewport.Width);
        var maxY = Math.Max(0, Extent.Height - Viewport.Height);
        _offsetX = Math.Clamp(x, 0, maxX);
        _offsetY = Math.Clamp(y, 0, maxY);
        InvalidateVisual();
        ScrollChanged?.Invoke();
    }

    /// <summary>줌(일당 픽셀). 화면 왼쪽 끝 날짜를 유지한다.</summary>
    public void SetZoom(double dayWidth)
    {
        var leftDay = _offsetX / _dayWidth;
        _dayWidth = Math.Clamp(dayWidth, 1.5, 60);
        ScrollTo(leftDay * _dayWidth, _offsetY);
    }

    // 테마 적용·색 편집·글자 설정은 모두 UiSettings.Apply → Changed 를 거친다(odev-5t 설계). 화면에 붙은 동안만 구독.
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        UiSettings.Current.Changed += InvalidateVisual;
        xkxk.frameworks.ui.shell.Framework.XkxkUi.Current.Localization.LanguageChanged += InvalidateVisual; // 머리글("작업") 언어 전환
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        UiSettings.Current.Changed -= InvalidateVisual;
        xkxk.frameworks.ui.shell.Framework.XkxkUi.Current.Localization.LanguageChanged -= InvalidateVisual;
        base.OnDetachedFromVisualTree(e);
    }

    /// <summary>받은 공간을 모두 원한다(유한할 때). 원하는 크기로 배치하는 부모(예: LayoutTransformControl)에서도 본문이 접히지 않게.</summary>
    protected override Size MeasureOverride(Size availableSize)
    {
#if PD_DIAGNOSTICS
        xkxk.frameworks.ui.shell.Diagnostics.MdiDragProbe.NoteGanttMeasure(); // 끌기 중 간트 재측정 여부 확인(--drag-probe 일 때만)
#endif
        return new(double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width, double.IsInfinity(availableSize.Height) ? 0 : availableSize.Height);
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        ScrollTo(_offsetX, _offsetY);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextSizeProperty)
        {
            ScrollTo(_offsetX, _offsetY); // 행 높이가 바뀌어 전체 높이·스크롤바 범위가 달라진다
        }
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            return; // Ctrl+휠 = 전역 글자 크기(셸). 처리하지 않고 위로 전달한다.
        }

        using var op = xkxk.frameworks.ui.shell.Diagnostics.PerfProbe.Current.TimeOperation("doc.gantt.scroll");
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift) || e.Delta.X != 0)
        {
            var d = e.Delta.X != 0 ? e.Delta.X : e.Delta.Y;
            ScrollTo(_offsetX - (d * 60), _offsetY);
        }
        else
        {
            ScrollTo(_offsetX, _offsetY - (e.Delta.Y * RowHeight * 3));
        }

        e.Handled = true;
    }

    public override void Render(DrawingContext context)
    {
#if PD_DIAGNOSTICS
        using var probe = xkxk.frameworks.ui.shell.Diagnostics.MdiDragProbe.GanttRender(); // 끌기 중 간트 다시 그리기 횟수·할당(--drag-probe 일 때만)
#endif
        var w = Bounds.Width;
        var h = Bounds.Height;
        _pal = ResolvePalette();
        if (!ReferenceEquals(_gridPen.Brush, _pal[7])) { _gridPen = new Pen(_pal[7], 1); } // 같은 붓이면 펜 재사용(A15)
        var pal = _pal;
        context.FillRectangle(pal[0], new Rect(0, 0, w, h));
        if (_tasks.Count == 0 || w <= LabelWidth || h <= HeaderHeight)
        {
            return;
        }

        var first = Math.Max(0, (int)(_offsetY / RowHeight));
        var last = Math.Min(_tasks.Count - 1, (int)((_offsetY + h - HeaderHeight) / RowHeight));
        var firstDay = (int)(_offsetX / _dayWidth);
        var lastDay = Math.Min(_totalDays, (int)((_offsetX + w - LabelWidth) / _dayWidth) + 1);

        // 막대 영역(보이는 행만). 층마다 기하 1개로 묶어 그린다(A15 — 그리기 호출 수 감소). 층 순서는 예전 행별 그리기와 같다.
        using (context.PushClip(new Rect(LabelWidth, HeaderHeight, w - LabelWidth, h - HeaderHeight)))
        {
            DrawBarLayers(context, first, last, firstDay, lastDay, w, h);
        }

        // 왼쪽 작업명 열
        using (context.PushClip(new Rect(0, HeaderHeight, LabelWidth, h - HeaderHeight)))
        {
            for (var i = first; i <= last; i++)
            {
                var y = HeaderHeight + (i * RowHeight) - _offsetY;
                if (i % 2 == 1) { context.FillRectangle(_pal[6], new Rect(0, y, LabelWidth, RowHeight)); }
                DrawText(context, $"{_tasks[i].No}. {_tasks[i].Name}", new Point(4, y + (RowHeight - (TextSize * 1.35)) / 2), TextSize, pal[1]);
            }
        }

        // 상단 주 눈금 머리글
        var headerY = (HeaderHeight - (TextSize * 1.35)) / 2;
        context.FillRectangle(pal[2], new Rect(0, 0, w, HeaderHeight));
        DrawText(context, xkxk.frameworks.ui.shell.Localization.Loc.Tr("doc.gantt.taskHeader"), new Point(6, headerY), TextSize, pal[1]);
        using (context.PushClip(new Rect(LabelWidth, 0, w - LabelWidth, HeaderHeight)))
        {
            var weekWidth = _dayWidth * 7;
            var labelW = TextSize * 3.2; // "MM/dd" 폭 근사
            var step = weekWidth < labelW ? (int)Math.Ceiling(labelW / weekWidth) : 1; // 좁으면 몇 주씩 건너 라벨
            for (var d = firstDay - (firstDay % 7); d <= lastDay; d += 7)
            {
                if ((d / 7) % step != 0) { continue; }
                var x = LabelWidth + (d * _dayWidth) - _offsetX;
                DrawText(context, _origin.AddDays(d).ToString("MM/dd", CultureInfo.InvariantCulture), new Point(x + 2, headerY), TextSize - 1, pal[3]);
            }
        }

        context.DrawLine(_gridPen, new Point(LabelWidth, 0), new Point(LabelWidth, h));
        context.DrawLine(_gridPen, new Point(0, HeaderHeight), new Point(w, HeaderHeight));
    }
}
