// 창 배치(스냅) 상단 레이아웃 막대 동작 — 판정 문맥(SnapZoneContext)의 막대 모양을 받아 칸 배치도를 그리고 현재 칸을 강조한다(그리기 전용, 판정은 SnapZoneResolver)
using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Pd.Poc.Shell.Docking;

namespace Pd.Ui.Views.Snap;

/// <summary>
/// 상단 레이아웃 막대. 크기·칸 위치는 <see cref="SnapZoneContext.BarRect"/>·<see cref="SnapZoneContext.Tiles"/> 를 그대로 쓴다(막대 로컬 좌표 = 문맥 좌표 − BarRect.TopLeft).
/// 막대를 띄울 수 없는 문맥(HasBar=false·null)이면 숨긴다. 모든 호출은 UI 스레드에서 한다.
/// </summary>
public partial class SnapLayoutBar : UserControl
{
    public SnapLayoutBar()
    {
        InitializeComponent();
    }

    /// <summary>지금 그리고 있는 판정 문맥. 숨김 상태면 null.</summary>
    public SnapZoneContext? Context { get; private set; }

    /// <summary>막대 바깥 사각형(문맥 좌표). 숨김이면 빈 사각형.</summary>
    public Rect BarRect => Context?.BarRect ?? default;

    /// <summary>판정 문맥을 받아 막대 모양을 다시 그린다. null 이거나 HasBar=false 면 숨긴다.</summary>
    public void SetContext(SnapZoneContext? ctx)
    {
        if (ctx is null || !ctx.HasBar)
        {
            Context = null;
            Cells.SetTiles(Array.Empty<SnapBarTile>(), default);
            IsVisible = false;
            return;
        }

        Context = ctx;
        Width = ctx.BarRect.Width;
        Height = ctx.BarRect.Height;
        Cells.SetTiles(ctx.Tiles, ctx.BarRect.TopLeft);
        IsVisible = true;
    }

    /// <summary>막대의 칸 하나를 강조한다(포인터가 그 칸 위). layoutId=null 이거나 cell&lt;0 이면 강조를 지운다.</summary>
    public void Highlight(string? layoutId, int cell) => Cells.SetHighlight(layoutId, cell);

    /// <summary>지금 강조 중인 칸(시험·원격 상태용). 없으면 (null, -1).</summary>
    public (string? LayoutId, int Cell) Highlighted => (Cells.HighlightLayoutId, Cells.HighlightCell);
}

/// <summary>
/// 막대 안 칸 배치도 그리기. 칸 사각형은 <see cref="SnapBarCell.HitRect"/> 를 막대 로컬로 옮긴 값이라 포인터 판정 영역과 같다.
/// 색은 axaml 에서 테마 토큰(DynamicResource)으로 넣는다. 비활성 칸(Enabled=false)은 흐리게 그리고 강조하지 않는다.
/// </summary>
public sealed class SnapBarCells : Control
{
    /// <summary>비활성 칸 불투명도(초기 제시값).</summary>
    public const double DisabledOpacity = 0.35;

    /// <summary>칸 모서리 반지름(DIP, 초기 제시값).</summary>
    public const double CellRadius = 2;

    public static readonly StyledProperty<IBrush?> CellBrushProperty =
        AvaloniaProperty.Register<SnapBarCells, IBrush?>(nameof(CellBrush));

    public static readonly StyledProperty<IBrush?> CellBorderBrushProperty =
        AvaloniaProperty.Register<SnapBarCells, IBrush?>(nameof(CellBorderBrush));

    public static readonly StyledProperty<IBrush?> HighlightBrushProperty =
        AvaloniaProperty.Register<SnapBarCells, IBrush?>(nameof(HighlightBrush));

    public static readonly StyledProperty<IBrush?> DisabledBorderBrushProperty =
        AvaloniaProperty.Register<SnapBarCells, IBrush?>(nameof(DisabledBorderBrush));

    private IReadOnlyList<SnapBarTile> _tiles = Array.Empty<SnapBarTile>();
    private Point _origin;

    static SnapBarCells()
    {
        AffectsRender<SnapBarCells>(CellBrushProperty, CellBorderBrushProperty, HighlightBrushProperty, DisabledBorderBrushProperty);
    }

    public SnapBarCells()
    {
        IsHitTestVisible = false;
        Focusable = false;
    }

    public IBrush? CellBrush { get => GetValue(CellBrushProperty); set => SetValue(CellBrushProperty, value); }

    public IBrush? CellBorderBrush { get => GetValue(CellBorderBrushProperty); set => SetValue(CellBorderBrushProperty, value); }

    public IBrush? HighlightBrush { get => GetValue(HighlightBrushProperty); set => SetValue(HighlightBrushProperty, value); }

    public IBrush? DisabledBorderBrush { get => GetValue(DisabledBorderBrushProperty); set => SetValue(DisabledBorderBrushProperty, value); }

    public string? HighlightLayoutId { get; private set; }

    public int HighlightCell { get; private set; } = -1;

    /// <summary>타일 목록과 막대 원점(문맥 좌표의 BarRect.TopLeft)을 바꾼다. 강조는 지운다.</summary>
    public void SetTiles(IReadOnlyList<SnapBarTile> tiles, Point origin)
    {
        _tiles = tiles;
        _origin = origin;
        HighlightLayoutId = null;
        HighlightCell = -1;
        InvalidateVisual();
    }

    /// <summary>강조 칸을 바꾼다. 같은 칸이면 다시 그리지 않는다(끌기 이동마다 불려도 비용 없음).</summary>
    public void SetHighlight(string? layoutId, int cell)
    {
        if (layoutId is null || cell < 0)
        {
            layoutId = null;
            cell = -1;
        }

        if (string.Equals(HighlightLayoutId, layoutId, StringComparison.Ordinal) && HighlightCell == cell)
        {
            return;
        }

        HighlightLayoutId = layoutId;
        HighlightCell = cell;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        if (_tiles.Count == 0)
        {
            return;
        }

        var pen = CellBorderBrush is { } b ? new Pen(b, 1) : null;
        var disabledPen = DisabledBorderBrush is { } d ? new Pen(d, 1, DashStyle.Dash) : pen;
        foreach (var tile in _tiles)
        {
            foreach (var cell in tile.Cells)
            {
                var r = cell.HitRect.Translate(new Vector(-_origin.X, -_origin.Y));
                if (r.Width <= 0 || r.Height <= 0)
                {
                    continue;
                }

                if (!cell.Enabled)
                {
                    // 최소 크기 미달 칸: 흐리게 + 점선 테두리, 강조하지 않는다(11 문서 §10)
                    using (context.PushOpacity(DisabledOpacity))
                    {
                        context.DrawRectangle(CellBrush, disabledPen, r, CellRadius, CellRadius);
                    }

                    continue;
                }

                var on = cell.Cell == HighlightCell && string.Equals(cell.LayoutId, HighlightLayoutId, StringComparison.Ordinal);
                context.DrawRectangle(on ? HighlightBrush ?? CellBrush : CellBrush, pen, r, CellRadius, CellRadius);
            }
        }
    }
}
