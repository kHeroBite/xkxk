// 표 셀 뿌리(PdCellChrome)가 텍스트 열 글자를 TextBlock 없이 직접 측정·그리는 부분 — 옵션 GridDrawnCellText(K15, 셀당 요소 1개 감소)
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;

namespace xkxk.frameworks.ui.views.Documents;

/// <summary>
/// 그리는 글자 셀: PdTextColumn 이 셀 내용을 만들지 않은 셀(내용 null)의 글자를 이 요소가 TextLayout 으로 그린다.
/// 모양은 지금 셀 TextBlock 과 같게 맞춘다 — 글꼴·크기·굵기·기울임·늘임·색은 셀에서 상속된 값, 여백·말줄임·세로 정렬은 PdDataGridStyles 의 TextBlock.cell 스타일 값(PdCellTextLook).
/// 배치는 TextBlock 이 Layoutable 로 받던 것과 같다: 여백을 뺀 칸에서 세로 가운데, 원하는 크기·위치는 레이아웃 반올림.
/// 행 재활용(셀 DataContext 변경) 때 글자를 다시 얻는다 — TextBlock.DataContextChanged 가 하던 일이다.
/// 자동화·진단: 셀 AutomationProperties.Name 에 글자를 둔다(자동화 피어 이름·진단 트리 덤프가 읽는다), 툴팁 열은 셀에 툴팁을 건다.
/// </summary>
internal sealed partial class PdCellChrome
{
    private PdTextColumn? _drawn; // 글자를 주는 열(그리는 글자 셀일 때만)
    private PdCellTextLook? _look;
    private string? _text;
    private TextLayout? _layout;
    private double _layoutMaxWidth = double.NaN;
    private bool _layoutDirty; // 글꼴·색이 바뀌어 다음 사용 때 다시 만든다
#if PD_DIAGNOSTICS
    private static int s_layoutBuilds;
    private static int s_relayouts;
    private static int s_textSets;
    private static int s_textChanged;
#endif
    private Size _textSize; // 반올림한 원하는 크기(여백 제외)
    private Point _textOrigin;

    // 글자 모양에 쓰는 상속 속성(바뀌면 다시 측정 — 색은 다시 그리기만)
    private static readonly AvaloniaProperty[] FontProps =
    {
        TextElement.FontFamilyProperty, TextElement.FontSizeProperty, TextElement.FontWeightProperty,
        TextElement.FontStyleProperty, TextElement.FontStretchProperty, TextBlock.TextAlignmentProperty, Visual.FlowDirectionProperty,
    };

    private static void RegisterTextAffects()
    {
        AffectsMeasure<PdCellChrome>(FontProps);
        AffectsRender<PdCellChrome>(TextElement.ForegroundProperty);
    }

    private void AttachText(DataGridCell cell)
    {
        _drawn = PdTextColumn.DrawnColumnOf(cell);
        _look = _drawn?.DrawnLook;
        if (_look is null) { _drawn = null; return; }
        RefreshText();
    }

    private void DetachText()
    {
        _drawn = null;
        _look = null;
        _text = null;
        _layout = null;
    }

    /// <summary>셀의 지금 행 항목으로 글자를 다시 얻는다(연결 때·행 재활용 때).</summary>
    private void RefreshText()
    {
        if (_drawn is null || _cell is not { } cell) { return; }
#if PD_DIAGNOSTICS
        var t = GridOpenBreakdown.Start();
#endif
        var text = _drawn.TextFor(cell.DataContext);
        _drawn.ApplyCellText(cell, text); // 자동화 이름·툴팁(셀에)
#if PD_DIAGNOSTICS
        GridOpenBreakdown.Add(GridOpenBreakdown.Part.Content, t);
#endif
        if (text == _text) { return; }
#if PD_DIAGNOSTICS
        xkxk.frameworks.ui.shell.Diagnostics.PerfProbe.Current.SetMetricMax("grid_drawn_textsets", ++s_textSets);
        if (_layout is not null) { xkxk.frameworks.ui.shell.Diagnostics.PerfProbe.Current.SetMetricMax("grid_drawn_textchanged", ++s_textChanged); } // 이미 만든 글자가 바뀜(행 재활용)
#endif
        _text = text;
        _layout = null;
        InvalidateMeasure();
        InvalidateVisual();
    }

    private void OnTextPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        if (_drawn is null) { return; }
        if (System.Array.IndexOf(FontProps, change.Property) >= 0 || change.Property == TextElement.ForegroundProperty) { _layoutDirty = true; }
    }

    /// <summary>TextBlock 의 원하는 크기(여백 포함)와 같은 값을 돌려준다. slot = 셀 안쪽(테두리·Padding 뺀 칸).</summary>
    private Size MeasureText(Size slot)
    {
#if PD_DIAGNOSTICS
        var t = GridOpenBreakdown.Start();
        try { return MeasureTextCore(slot); }
        finally { GridOpenBreakdown.Add(GridOpenBreakdown.Part.TextMeasure, t); }
#else
        return MeasureTextCore(slot);
#endif
    }

    private Size MeasureTextCore(Size slot)
    {
        var look = _look!;
        var scale = LayoutHelper.GetLayoutScale(this);
        var margin = UseLayoutRounding ? LayoutHelper.RoundLayoutThickness(look.Margin, scale) : look.Margin;
        var avail = slot.Deflate(margin);
        var layout = EnsureLayout(double.IsInfinity(avail.Width) ? double.PositiveInfinity : System.Math.Max(0, avail.Width));
        var size = new Size(layout.OverhangLeading + layout.WidthIncludingTrailingWhitespace + layout.OverhangTrailing, layout.Height);
        if (UseLayoutRounding) { size = LayoutHelper.RoundLayoutSizeUp(size, scale); }
        _textSize = size;
        return size.Inflate(margin);
    }

    /// <summary>TextBlock 이 받던 자리와 같게 글자 원점을 정한다. slot = 셀 안쪽 사각형(테두리·Padding 뺀 칸).</summary>
    private void ArrangeText(Rect slot)
    {
        var look = _look!;
        var scale = LayoutHelper.GetLayoutScale(this);
        var round = UseLayoutRounding;
        var margin = round ? LayoutHelper.RoundLayoutThickness(look.Margin, scale) : look.Margin;
        var inner = slot.Deflate(margin);
        var w = System.Math.Max(0, inner.Width); // TextBlock 가로 정렬 Stretch — 칸 폭 전체
        var h = System.Math.Min(_textSize.Height, System.Math.Max(0, inner.Height));
        var y = inner.Y + look.VerticalAlignment switch
        {
            VerticalAlignment.Center => (inner.Height - h) / 2,
            VerticalAlignment.Bottom => inner.Height - h,
            _ => 0,
        };
        var x = inner.X;
        if (round)
        {
            x = LayoutHelper.RoundLayoutValue(x, scale);
            y = LayoutHelper.RoundLayoutValue(y, scale);
            w = LayoutHelper.RoundLayoutValue(w, scale);
        }

        EnsureLayout(w); // TextBlock 도 배치 폭이 측정 제약과 다르면 그 폭으로 다시 만든다(말줄임 위치)
        _textOrigin = new Point(x, y);
    }

    private TextLayout EnsureLayout(double maxWidth)
    {
        if (_layout is not null && !_layoutDirty && _layoutMaxWidth.Equals(maxWidth)) { return _layout; }
        _layoutDirty = false;
#if PD_DIAGNOSTICS
        xkxk.frameworks.ui.shell.Diagnostics.PerfProbe.Current.SetMetricMax("grid_drawn_layouts", ++s_layoutBuilds); // 만든 TextLayout 누계(셀 수보다 크면 다시 만든 것)
        if (_layout is not null) { xkxk.frameworks.ui.shell.Diagnostics.PerfProbe.Current.SetMetricMax("grid_drawn_relayouts", ++s_relayouts); }
#endif
        var look = _look!;
        var typeface = new Typeface(TextElement.GetFontFamily(this), TextElement.GetFontStyle(this), TextElement.GetFontWeight(this), TextElement.GetFontStretch(this));
        _layout = new TextLayout(_text ?? string.Empty, typeface, TextElement.GetFontSize(this), TextElement.GetForeground(this),
            TextBlock.GetTextAlignment(this), TextWrapping.NoWrap, look.Trimming, null, FlowDirection, maxWidth); // 정렬은 TextBlock 과 같은 상속값(기본 Start — RTL 이면 오른쪽)
        _layoutMaxWidth = maxWidth;
        return _layout;
    }

    private void RenderText(DrawingContext context)
    {
        if (_drawn is null || string.IsNullOrEmpty(_text) || _layout is null) { return; }
        if (_layoutDirty) { EnsureLayout(_layoutMaxWidth); } // 색만 바뀐 경우(다시 측정 없이 다시 그리기만 옴)
        _layout.Draw(context, _textOrigin); // 폭은 말줄임으로 칸 안에 맞춘다(TextBlock 과 같이 잘라 내기 없음)
    }
}

/// <summary>
/// 셀 글자 모양 값(여백·말줄임·세로 정렬) — 정본은 PdDataGridStyles.axaml 의 'TextBlock.cell' 스타일이다. 그 스타일의 Setter 를 한 번 읽어 그리는 글자 셀과 TextBlock 셀이 같은 값을 쓴다.
/// 스타일을 찾지 못하면 null — 그때 표는 그리는 글자를 쓰지 않고 TextBlock 판으로 돈다.
/// </summary>
internal sealed class PdCellTextLook
{
    public Thickness Margin { get; private init; }

    public TextTrimming Trimming { get; private init; } = TextTrimming.None;

    public VerticalAlignment VerticalAlignment { get; private init; } = VerticalAlignment.Stretch;

    public static PdCellTextLook? From(Avalonia.Styling.Styles styles)
    {
        foreach (var item in styles)
        {
            if (item is not Avalonia.Styling.Style s || s.Selector?.ToString() != "TextBlock.cell") { continue; }
            var look = new PdCellTextLook();
            foreach (var setter in s.Setters)
            {
                if (setter is not Avalonia.Styling.Setter { Property: { } p, Value: var v }) { continue; }
                if (p == Layoutable.MarginProperty && v is Thickness m) { look = look.With(margin: m); }
                else if (p == TextBlock.TextTrimmingProperty && v is TextTrimming tt) { look = look.With(trimming: tt); }
                else if (p == Layoutable.VerticalAlignmentProperty && v is VerticalAlignment va) { look = look.With(valign: va); }
            }

            return look;
        }

        return null;
    }

    private PdCellTextLook With(Thickness? margin = null, TextTrimming? trimming = null, VerticalAlignment? valign = null) => new()
    {
        Margin = margin ?? Margin, Trimming = trimming ?? Trimming, VerticalAlignment = valign ?? VerticalAlignment,
    };
}
