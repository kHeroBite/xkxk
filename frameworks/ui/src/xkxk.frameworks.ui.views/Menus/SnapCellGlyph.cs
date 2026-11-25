// '창 ▸ 배치' 메뉴 항목 아이콘 — 칸 종류(Tag "half")는 칸 테두리만, 칸 항목(Tag "half|0")은 그 칸을 채워 그린다(셸 SnapLayoutCatalog 칸 정의 재사용)
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using xkxk.frameworks.ui.shell.Docking;

namespace xkxk.frameworks.ui.views.Menus;

/// <summary>
/// 16×16 칸 그림. <see cref="Spec"/> 은 메뉴 항목 Tag 와 같은 형식("kind" 또는 "kind|칸번호")이다.
/// 칸 비율은 <see cref="SnapLayoutCatalog"/> 에서 읽어 배치 막대·플라이아웃과 같은 모양이 된다. 모르는 종류면 아무것도 그리지 않는다.
/// </summary>
public sealed class SnapCellGlyph : Control
{
    private const double Side = 16;
    private const double Inset = 1.5;
    private const double Gap = 1.5;

    public static readonly StyledProperty<string?> SpecProperty =
        AvaloniaProperty.Register<SnapCellGlyph, string?>(nameof(Spec));

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner<SnapCellGlyph>();

    static SnapCellGlyph()
    {
        AffectsRender<SnapCellGlyph>(SpecProperty, ForegroundProperty);
    }

    /// <summary>"kind" 또는 "kind|칸번호"(메뉴 항목 Tag).</summary>
    public string? Spec { get => GetValue(SpecProperty); set => SetValue(SpecProperty, value); }

    /// <summary>그림 색(글자색 상속).</summary>
    public IBrush? Foreground { get => GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }

    // 칸 배치는 화면 방향과 무관한 그림이라 RTL 에서 뒤집지 않는다
    protected override bool BypassFlowDirectionPolicies => true;

    protected override Size MeasureOverride(Size availableSize) =>
        new(Math.Min(Side, availableSize.Width), Math.Min(Side, availableSize.Height));

    public override void Render(DrawingContext context)
    {
        if (Foreground is not { } brush || !TryParse(Spec, out var layout, out var picked))
        {
            return;
        }

        double side = Math.Min(Bounds.Width, Bounds.Height);
        double box = side - Inset * 2;
        if (box <= 2)
        {
            return;
        }

        var origin = new Point((Bounds.Width - side) / 2 + Inset, (Bounds.Height - side) / 2 + Inset);
        var pen = new Pen(brush, 1);
        for (var i = 0; i < layout.Cells.Count; i++)
        {
            var rect = CellRect(layout.Cells[i], origin, box);
            if (i == picked)
            {
                context.FillRectangle(brush, rect);
            }
            else
            {
                // 1px 선이 픽셀 경계에 맞게 반 픽셀 들인다
                context.DrawRectangle(null, pen, rect.Deflate(0.5));
            }
        }
    }

    // 이웃 칸과 틈(Gap)을 반씩 나눠 갖는다(SnapLayoutFlyout.CreateCell 과 같은 규칙, 바깥 테두리 쪽은 들이지 않는다)
    private static Rect CellRect(SnapCellRatio r, Point origin, double box)
    {
        var left = r.X * box + (r.X > 0 ? Gap / 2 : 0);
        var top = r.Y * box + (r.Y > 0 ? Gap / 2 : 0);
        var right = (r.X + r.Width) * box - (r.X + r.Width < 0.999 ? Gap / 2 : 0);
        var bottom = (r.Y + r.Height) * box - (r.Y + r.Height < 0.999 ? Gap / 2 : 0);
        return new Rect(origin.X + left, origin.Y + top, Math.Max(1, right - left), Math.Max(1, bottom - top));
    }

    private static bool TryParse(string? spec, out SnapLayout layout, out int picked)
    {
        layout = null!;
        picked = -1;
        if (string.IsNullOrWhiteSpace(spec))
        {
            return false;
        }

        var parts = spec.Split('|');
        if (SnapLayoutCatalog.Find(parts[0]) is not { } found)
        {
            return false;
        }

        layout = found;
        if (parts.Length == 2 && int.TryParse(parts[1], out var index) && index >= 0 && index < found.Cells.Count)
        {
            picked = index;
        }

        return true;
    }
}
