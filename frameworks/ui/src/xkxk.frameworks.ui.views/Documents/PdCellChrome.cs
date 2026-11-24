// 표 셀 템플릿의 단일 요소 판 — 바탕·테두리를 직접 그리고 셀 내용을 시각 자식으로만 품어 셀당 요소를 4개(Border·Panel·ContentPresenter·내용)에서 2개로 줄인다(A16)
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace xkxk.frameworks.ui.views.Documents;

/// <summary>
/// 세로선 없는 표의 셀 템플릿 뿌리(PdCellTemplate.Compact). 원본 판(Border#CellBorder > Panel#PART_CellRoot > ContentPresenter)과 같은 값을 받는다:
/// 바탕·테두리 붓·테두리 두께 = 셀 값, 내용 여백 = 셀 Padding, 내용 정렬 = 셀 Horizontal/VerticalContentAlignment.
/// 내용(셀 Content 컨트롤)의 논리 부모는 셀 그대로다 — ContentControl 이 Content 를 자기 논리 자식으로 둔다. 이 요소는 시각 자식으로만 붙인다(ContentPresenter 와 같은 규칙).
/// 현재 셀·포커스 표시(내용 아래)와 오류 표시(내용 위)는 PdCellTemplate.Overlays 가 그 상태가 될 때만 만들어 넣는다. 표시는 논리 자식이기도 해서 스타일·자원이 걸린다.
/// 근거(a16dev 진단판 계측): 첫 Measure 의 대부분이 보이는 셀 요소의 부착·스타일·측정(셀당 약 0.8ms)이고 템플릿 생성 자체는 그 1/5 남짓이다.
/// </summary>
internal sealed partial class PdCellChrome : Control
{
    public static readonly StyledProperty<IBrush?> BackgroundProperty = Border.BackgroundProperty.AddOwner<PdCellChrome>();
    public static readonly StyledProperty<IBrush?> BorderBrushProperty = Border.BorderBrushProperty.AddOwner<PdCellChrome>();
    public static readonly StyledProperty<Thickness> BorderThicknessProperty = Border.BorderThicknessProperty.AddOwner<PdCellChrome>();
    public static readonly StyledProperty<Thickness> PaddingProperty = Decorator.PaddingProperty.AddOwner<PdCellChrome>();
    public static readonly StyledProperty<object?> ContentProperty = ContentControl.ContentProperty.AddOwner<PdCellChrome>();
    public static readonly StyledProperty<HorizontalAlignment> HorizontalContentAlignmentProperty = ContentControl.HorizontalContentAlignmentProperty.AddOwner<PdCellChrome>();
    public static readonly StyledProperty<VerticalAlignment> VerticalContentAlignmentProperty = ContentControl.VerticalContentAlignmentProperty.AddOwner<PdCellChrome>();

    private DataGridCell? _cell; // 값을 받는 템플릿 부모 셀(AttachCell — TemplateBinding 7개 대신 구독 1개, K15)
    private readonly List<Control> _under = new(2); // 현재 셀·포커스 표시(그리는 순서대로)
    private readonly List<Control> _over = new(1); // 오류 표시
    private Control? _content;

    static PdCellChrome()
    {
        AffectsRender<PdCellChrome>(BackgroundProperty, BorderBrushProperty, BorderThicknessProperty);
        AffectsMeasure<PdCellChrome>(BorderThicknessProperty, PaddingProperty);
        AffectsArrange<PdCellChrome>(HorizontalContentAlignmentProperty, VerticalContentAlignmentProperty);
        RegisterTextAffects(); // 그리는 글자(PdCellChrome.Text.cs)
    }

    public IBrush? Background { get => GetValue(BackgroundProperty); set => SetValue(BackgroundProperty, value); }

    public IBrush? BorderBrush { get => GetValue(BorderBrushProperty); set => SetValue(BorderBrushProperty, value); }

    public Thickness BorderThickness { get => GetValue(BorderThicknessProperty); set => SetValue(BorderThicknessProperty, value); }

    public Thickness Padding { get => GetValue(PaddingProperty); set => SetValue(PaddingProperty, value); }

    public object? Content { get => GetValue(ContentProperty); set => SetValue(ContentProperty, value); }

    public HorizontalAlignment HorizontalContentAlignment { get => GetValue(HorizontalContentAlignmentProperty); set => SetValue(HorizontalContentAlignmentProperty, value); }

    public VerticalAlignment VerticalContentAlignment { get => GetValue(VerticalContentAlignmentProperty); set => SetValue(VerticalContentAlignmentProperty, value); }

    /// <summary>
    /// 셀 값(바탕·테두리 붓·두께·Padding·내용 정렬 2·내용)을 Template 우선순위로 받고, 셀 PropertyChanged 1개로 변화를 따른다.
    /// 원래 판은 같은 값을 TemplateBinding 7개로 받았다 — 셀마다 구독 7개가 생기고 셀 속성이 하나 바뀔 때마다 7개가 모두 불렸다(K15 tplattach).
    /// 템플릿을 다시 만들면 PdCellTemplate.Overlays.Detach 가 DetachCell 로 끊는다.
    /// </summary>
    public void AttachCell(DataGridCell cell)
    {
        DetachCell();
        _cell = cell;
        foreach (var p in CellProps) { Pull(p); }
        AttachText(cell); // 그리는 글자 셀이면 글자를 받는다(K15, PdCellChrome.Text.cs)
        cell.PropertyChanged += OnCellPropertyChanged;
    }

    public void DetachCell()
    {
        if (_cell is null) { return; }
        _cell.PropertyChanged -= OnCellPropertyChanged;
        DetachText();
        _cell = null;
    }

    // 셀 쪽 속성(템플릿 부모가 가진 원래 소유 형식의 속성 — 이 요소 속성은 AddOwner 로 같은 값을 받는다)
    private static readonly AvaloniaProperty[] CellProps =
    {
        TemplatedControl.BackgroundProperty, TemplatedControl.BorderBrushProperty, TemplatedControl.BorderThicknessProperty, TemplatedControl.PaddingProperty,
        ContentControl.HorizontalContentAlignmentProperty, ContentControl.VerticalContentAlignmentProperty, ContentControl.ContentProperty,
    };

    private void OnCellPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (System.Array.IndexOf(CellProps, e.Property) >= 0) { Pull(e.Property); }
        else if (_drawn is not null && e.Property == StyledElement.DataContextProperty) { RefreshText(); } // 행 재활용: 새 행 항목의 글자
    }

    private void Pull(AvaloniaProperty p)
    {
        if (_cell is not { } c) { return; }
        if (p == TemplatedControl.BackgroundProperty) { SetValue(BackgroundProperty, c.Background, BindingPriority.Template); }
        else if (p == TemplatedControl.BorderBrushProperty) { SetValue(BorderBrushProperty, c.BorderBrush, BindingPriority.Template); }
        else if (p == TemplatedControl.BorderThicknessProperty) { SetValue(BorderThicknessProperty, c.BorderThickness, BindingPriority.Template); }
        else if (p == TemplatedControl.PaddingProperty) { SetValue(PaddingProperty, c.Padding, BindingPriority.Template); }
        else if (p == ContentControl.HorizontalContentAlignmentProperty) { SetValue(HorizontalContentAlignmentProperty, c.HorizontalContentAlignment, BindingPriority.Template); }
        else if (p == ContentControl.VerticalContentAlignmentProperty) { SetValue(VerticalContentAlignmentProperty, c.VerticalContentAlignment, BindingPriority.Template); }
        else if (p == ContentControl.ContentProperty) { SetValue(ContentProperty, c.Content, BindingPriority.Template); }
    }

    /// <summary>내용 아래 표시를 넣는다. atStart=true 면 맨 아래(현재 셀 표시), 아니면 아래 표시 중 맨 위(포커스 표시) — 원본 템플릿 순서와 같다.</summary>
    public void AddUnder(Control overlay, bool atStart)
    {
        var index = atStart ? 0 : _under.Count;
        _under.Insert(index, overlay);
        LogicalChildren.Add(overlay);
        VisualChildren.Insert(index, overlay);
        InvalidateMeasure();
    }

    /// <summary>내용 위 표시(오류 표시)를 넣는다.</summary>
    public void AddOver(Control overlay)
    {
        _over.Add(overlay);
        LogicalChildren.Add(overlay);
        VisualChildren.Add(overlay);
        InvalidateMeasure();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ContentProperty) { SetContent(change.NewValue as Control); }
        else { OnTextPropertyChanged(change); } // 상속 글꼴·색 변경(그리는 글자)
    }

    private void SetContent(Control? content)
    {
        if (ReferenceEquals(content, _content)) { return; }
        if (_content is not null) { VisualChildren.Remove(_content); }
        _content = content;
        if (content is not null)
        {
            if (content.GetVisualParent() is PdCellChrome old) { old.ReleaseContent(content); } // 템플릿을 다시 만든 셀: 옛 뿌리에서 떼어 온다
            VisualChildren.Insert(_under.Count, content);
        }

        InvalidateMeasure();
    }

    private void ReleaseContent(Control content)
    {
        if (ReferenceEquals(_content, content))
        {
            VisualChildren.Remove(content);
            _content = null;
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
#if PD_DIAGNOSTICS
        GridOpenBreakdown.Add(GridOpenBreakdown.Part.TplAttach, GridOpenBreakdown.TakeBuildEnd()); // K15 분해(진단 빌드만)
        var t = GridOpenBreakdown.Start();
        try { return MeasureContent(availableSize); }
        finally { GridOpenBreakdown.Add(GridOpenBreakdown.Part.ChromeMeasure, t); }
#else
        return MeasureContent(availableSize);
#endif
    }

    private Size MeasureContent(Size availableSize)
    {
        var inner = availableSize.Deflate(BorderThickness);
        foreach (var o in _under) { o.Measure(inner); }
        foreach (var o in _over) { o.Measure(inner); }
        var size = default(Size);
        if (_content is not null)
        {
            _content.Measure(inner.Deflate(Padding));
            size = _content.DesiredSize.Inflate(Padding);
        }
        else if (_drawn is not null)
        {
            size = MeasureText(inner.Deflate(Padding)).Inflate(Padding); // 그리는 글자: TextBlock 과 같은 원하는 크기
        }

        return size.Inflate(BorderThickness);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var root = new Rect(finalSize).Deflate(BorderThickness);
        foreach (var o in _under) { o.Arrange(root); }
        foreach (var o in _over) { o.Arrange(root); }
        if (_content is not null) { _content.Arrange(Align(root.Deflate(Padding), _content.DesiredSize)); }
        else if (_drawn is not null) { ArrangeText(root.Deflate(Padding)); }
        return finalSize;
    }

    /// <summary>ContentPresenter 를 셀 정렬 값으로 둔 원본과 같은 자리: Stretch 면 칸 전체, 아니면 원하는 크기로 칸 안에 붙인다.</summary>
    private Rect Align(Rect slot, Size desired)
    {
        double x = slot.X, y = slot.Y, w = slot.Width, h = slot.Height;
        var ha = HorizontalContentAlignment;
        if (ha != HorizontalAlignment.Stretch)
        {
            w = System.Math.Min(desired.Width, slot.Width);
            x += ha == HorizontalAlignment.Center ? (slot.Width - w) / 2 : ha == HorizontalAlignment.Right ? slot.Width - w : 0;
        }

        var va = VerticalContentAlignment;
        if (va != VerticalAlignment.Stretch)
        {
            h = System.Math.Min(desired.Height, slot.Height);
            y += va == VerticalAlignment.Center ? (slot.Height - h) / 2 : va == VerticalAlignment.Bottom ? slot.Height - h : 0;
        }

        return new Rect(x, y, w, h);
    }

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        if (Background is { } bg) { context.FillRectangle(bg, bounds); } // null 붓은 그리지 않는다(원본 Border 와 같이 hit-test 도 없음)
        var t = BorderThickness;
        if (BorderBrush is not { } bb || t == default) { RenderText(context); return; }
        // 원본 Border 와 같이 테두리를 안쪽에 그린다(셀 기본 두께는 0 — 쓰는 표가 있을 때만 그린다)
        if (t.Top > 0) { context.FillRectangle(bb, new Rect(0, 0, bounds.Width, t.Top)); }
        if (t.Bottom > 0) { context.FillRectangle(bb, new Rect(0, bounds.Height - t.Bottom, bounds.Width, t.Bottom)); }
        if (t.Left > 0) { context.FillRectangle(bb, new Rect(0, t.Top, t.Left, bounds.Height - t.Top - t.Bottom)); }
        if (t.Right > 0) { context.FillRectangle(bb, new Rect(bounds.Width - t.Right, t.Top, t.Right, bounds.Height - t.Top - t.Bottom)); }
        RenderText(context); // 그리는 글자: 바탕·테두리 위(TextBlock 자식이 그려지던 순서). 현재 셀·포커스·오류 표시는 자식이라 그 위에 그려진다(투명 바탕 테두리라 보이는 결과는 같다)
    }
}
