// 간트 막대·주 단위 눈금을 Render 오버라이드로 직접 그리는 가상 캔버스(보이는 행만 그림)
using Pd.Poc.Shell;
using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Pd.Poc.Shell.Theming;
using Pd.Ui.Input;

namespace Pd.Ui.Views.Documents;

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
        Pd.Poc.Shell.Localization.Loc.Instance.LanguageChanged += InvalidateVisual; // 머리글("작업") 언어 전환
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        UiSettings.Current.Changed -= InvalidateVisual;
        Pd.Poc.Shell.Localization.Loc.Instance.LanguageChanged -= InvalidateVisual;
        base.OnDetachedFromVisualTree(e);
    }

    /// <summary>받은 공간을 모두 원한다(유한할 때). 원하는 크기로 배치하는 부모(예: LayoutTransformControl)에서도 본문이 접히지 않게.</summary>
    protected override Size MeasureOverride(Size availableSize)
    {
#if PD_DIAGNOSTICS
        Pd.Poc.Shell.Diagnostics.MdiDragProbe.NoteGanttMeasure(); // 끌기 중 간트 재측정 여부 확인(--drag-probe 일 때만)
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

        using var op = Pd.Poc.Shell.Diagnostics.PerfProbe.Current.TimeOperation("doc.gantt.scroll");
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
        using var probe = Pd.Poc.Shell.Diagnostics.MdiDragProbe.GanttRender(); // 끌기 중 간트 다시 그리기 횟수·할당(--drag-probe 일 때만)
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
        DrawText(context, Pd.Poc.Shell.Localization.Loc.Tr("doc.gantt.taskHeader"), new Point(6, headerY), TextSize, pal[1]);
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

    /// <summary>
    /// 막대 영역을 층 순서(주 눈금선 → 줄무늬 → 막대·진척 → 선행선)로 그린다. 주 눈금선·줄무늬는 층마다 기하 1개·그리기 1번이다(A15).
    /// 예전에는 눈금선마다, 줄무늬 행마다 따로 그려 보이는 행·주 수에 비례해 호출이 늘었다(A15 프로파일 — 그리기 호출이 프레임 비용의 대부분).
    /// 눈금선끼리·줄무늬끼리는 겹치지 않아 묶어도 화소가 같다. 막대·진척·선행선은 묶으면 화소가 달라져 그대로 따로 그린다(각 층 주석).
    /// </summary>
    private void DrawBarLayers(DrawingContext context, int first, int last, int firstDay, int lastDay, double w, double h)
    {
        var weeks = new StreamGeometry();
        using (var g = weeks.Open())
        {
            for (var d = firstDay - (firstDay % 7); d <= lastDay; d += 7)
            {
                var x = LabelWidth + (d * _dayWidth) - _offsetX;
                AddLine(g, new Point(x, HeaderHeight), new Point(x, h));
            }
        }

        context.DrawGeometry(null, _gridPen, weeks);

        var stripes = new StreamGeometry();
        var anyStripe = false;
        using (var gs = stripes.Open())
        {
            for (var i = first + ((first & 1) == 0 ? 1 : 0); i <= last; i += 2)
            {
                AddRect(gs, new Rect(LabelWidth, HeaderHeight + (i * RowHeight) - _offsetY, w - LabelWidth, RowHeight));
                anyStripe = true;
            }
        }

        if (anyStripe) { context.DrawGeometry(_pal[6], null, stripes); }

        // 막대·진척은 행마다 둥근 사각형 그대로 그린다. 기하로 묶으면 모서리 안티에일리어싱이 둥근 사각형 전용 그리기와 화소 단위로 달라졌다
        // (A15 대조 2026-11-14: 묶음판 간트 영역 400~460화소·채널 최대 80 차이, 행별판 0).
        var pad = Math.Round(RowHeight * 0.18);
        for (var i = first; i <= last; i++)
        {
            var t = _tasks[i];
            var y = HeaderHeight + (i * RowHeight) - _offsetY;
            var x = BarX(t);
            var bw = Math.Max(2, t.Days * _dayWidth);
            context.FillRectangle(_pal[4], new Rect(x, y + pad, bw, RowHeight - (2 * pad)), 3);
            context.FillRectangle(_pal[5], new Rect(x, y + pad, bw * t.Progress / 100.0, RowHeight - (2 * pad)), 3);
        }

        // 선행 작업 종료 → 이 작업 시작 연결선(화면 내 항목만 — 선행 행이 화면 밖이면 생략). 선분마다 따로 그린다:
        // 같은 선행 작업에서 나온 세로선이 겹칠 때 따로 그리면 반투명 가장자리가 두 번 섞이는데, 한 기하로 묶으면 한 번만 섞여
        // 웹에서 화소가 달라졌다(A15 대조 — 라이트 524·다크 682화소, 채널 1단계). 펜만 재사용한다.
        // 선행선은 앞 행에서 내려와 뒤 행 막대와 겹치지 않으므로 막대를 모두 그린 뒤 그려도 예전(행마다 막대 뒤)과 겹침 순서가 같다.
        if (!ReferenceEquals(_depPen?.Brush, _pal[3])) { _depPen = new Pen(_pal[3], 1); }
        for (var i = first; i <= last; i++)
        {
            var t = _tasks[i];
            if (t.DependsOn is not int p || p < 0 || p >= _tasks.Count || Math.Abs(p - i) >= 60) { continue; }
            var y = HeaderHeight + (i * RowHeight) - _offsetY + (RowHeight / 2);
            var pt = _tasks[p];
            var px = LabelWidth + ((pt.Start.AddDays(pt.Days) - _origin).TotalDays * _dayWidth) - _offsetX;
            var py = HeaderHeight + (p * RowHeight) - _offsetY + (RowHeight / 2);
            context.DrawLine(_depPen, new Point(px, py), new Point(px + 4, py));
            context.DrawLine(_depPen, new Point(px + 4, py), new Point(px + 4, y));
            context.DrawLine(_depPen, new Point(px + 4, y), new Point(BarX(t), y));
        }
    }

    private IPen? _depPen;

    private double BarX(GanttTask t) => LabelWidth + ((t.Start - _origin).TotalDays * _dayWidth) - _offsetX;

    /// <summary>열린 선분 하나(끝 모양은 펜 기본 Flat — 예전 DrawLine 과 같다).</summary>
    private static void AddLine(StreamGeometryContext g, Point a, Point b)
    {
        g.BeginFigure(a, false);
        g.LineTo(b);
        g.EndFigure(false);
    }

    /// <summary>모서리가 각진 사각형 하나(채움 도형)를 더한다.</summary>
    private static void AddRect(StreamGeometryContext g, Rect r)
    {
        g.BeginFigure(r.TopLeft, true);
        g.LineTo(r.TopRight);
        g.LineTo(r.BottomRight);
        g.LineTo(r.BottomLeft);
        g.EndFigure(true);
    }

    // 글자 배치 캐시(A15) — 같은 문구·크기·붓(같은 인스턴스)이면 FormattedText 를 다시 만들지 않는다.
    // 글꼴·문화권이 바뀌거나 항목이 많아지면 통째로 비운다. 테마 색 편집은 붓 인스턴스의 Color 만 바꾸므로(ThemeApplier) 캐시된 글자도 새 색으로 그려진다.
    private readonly Dictionary<(string Text, double Size, IBrush Brush), FormattedText> _textCache = new(TextKeyComparer.Instance);
    private FontFamily? _textCacheFamily;
    private CultureInfo? _textCacheCulture;

    private void DrawText(DrawingContext context, string text, Point at, double size, IBrush brush)
    {
        var culture = CultureInfo.CurrentCulture;
        if (!ReferenceEquals(_textCacheFamily, TextFamily) || !ReferenceEquals(_textCacheCulture, culture) || _textCache.Count > 1024)
        {
            _textCache.Clear();
            _textCacheFamily = TextFamily;
            _textCacheCulture = culture;
        }

        var key = (text, size, brush);
        if (!_textCache.TryGetValue(key, out var ft))
        {
            ft = new FormattedText(text, culture, FlowDirection.LeftToRight, new Typeface(TextFamily), size, brush);
            _textCache[key] = ft;
        }

        context.DrawText(ft, at);
    }

    /// <summary>캐시 키 비교 — 붓은 인스턴스(참조)로 비교한다(값 비교면 색이 같은 다른 붓을 섞어 쓰게 된다).</summary>
    private sealed class TextKeyComparer : IEqualityComparer<(string Text, double Size, IBrush Brush)>
    {
        public static readonly TextKeyComparer Instance = new();

        public bool Equals((string Text, double Size, IBrush Brush) a, (string Text, double Size, IBrush Brush) b) =>
            a.Size.Equals(b.Size) && ReferenceEquals(a.Brush, b.Brush) && string.Equals(a.Text, b.Text, StringComparison.Ordinal);

        public int GetHashCode((string Text, double Size, IBrush Brush) k) =>
            HashCode.Combine(StringComparer.Ordinal.GetHashCode(k.Text), k.Size, System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(k.Brush));
    }
}
