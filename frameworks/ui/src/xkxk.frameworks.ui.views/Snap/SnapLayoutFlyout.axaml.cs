// 최대화·이전 크기 단추 호버 배치 칸 팝업 내용 동작 — 칸 종류 목록으로 작은 칸 그림을 만들고, 칸 포인터 진입·클릭을 알린다(그리기·입력 전달 전용, 배치는 WindowMenuOps)
using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using xkxk.frameworks.ui.shell.Docking;

namespace xkxk.frameworks.ui.views.Snap;

/// <summary>
/// 타일 1개 = 칸 종류 1개. 칸 위치는 SnapLayoutCatalog 의 비율(가로가 긴 영역 기준)을 타일 크기에 맞춰 놓는다.
/// 세로가 긴 기준 영역에서는 실제 칸이 위아래로 바뀌지만 그림은 가로 기준으로 그린다(한계 — 그림은 칸 종류 구분용).
/// 적용 가능 여부는 열 때 한 번 받는다(canApply). 적용 불가 칸은 IsEnabled=false 라 :disabled 스타일로 흐려지고 입력을 받지 않는다.
/// </summary>
public partial class SnapLayoutFlyout : UserControl
{
    /// <summary>타일 안 그림 크기(DIP, 초기 제시값 — 3:2 화면 비율).</summary>
    public const double TileWidth = 60;
    public const double TileHeight = 40;

    /// <summary>이웃 칸 사이 틈(DIP).</summary>
    public const double CellGap = 2;

    /// <summary>한 줄 타일 수.</summary>
    public const int TilesPerRow = 3;

    public SnapLayoutFlyout()
    {
        InitializeComponent();
    }

    /// <summary>포인터가 칸에 들어옴(layoutId, cell). 칸을 벗어나면 (null, -1).</summary>
    public event Action<string?, int>? CellHovered;

    /// <summary>칸을 누름(layoutId, cell).</summary>
    public event Action<string, int>? CellPicked;

    /// <summary>포인터가 팝업 안에 있는가(단추↔팝업 사이 이동 판정용).</summary>
    public bool IsPointerInside { get; private set; }

    /// <summary>칸 종류 목록과 칸별 적용 가능 여부로 타일을 다시 만든다.</summary>
    public void SetLayouts(IReadOnlyList<SnapLayout> layouts, Func<string, int, bool> canApply)
    {
        Tiles.Children.Clear();
        // 타일 폭 = 그림 + 안쪽 여백 4×2 + 바깥 여백 3×2
        Tiles.Width = TilesPerRow * (TileWidth + 14);
        foreach (var layout in layouts)
        {
            Tiles.Children.Add(CreateTile(layout, canApply));
        }
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        IsPointerInside = true;
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        IsPointerInside = false;
        CellHovered?.Invoke(null, -1);
    }

    private Border CreateTile(SnapLayout layout, Func<string, int, bool> canApply)
    {
        var canvas = new Canvas { Width = TileWidth, Height = TileHeight };
        for (var i = 0; i < layout.Cells.Count; i++)
        {
            canvas.Children.Add(CreateCell(layout.Id, i, layout.Cells[i], canApply(layout.Id, i)));
        }

        var tile = new Border { Child = canvas };
        tile.Classes.Add("pdSnapTile");
        AutomationProperties.SetAutomationId(tile, $"snap.flyout.{layout.Id}");
        return tile;
    }

    private Border CreateCell(string layoutId, int index, SnapCellRatio r, bool enabled)
    {
        // 이웃 칸이 같은 경계를 쓰도록 비율 경계에서 틈의 절반씩 안으로 들인다(바깥 테두리 쪽은 들이지 않는다)
        var left = r.X * TileWidth + (r.X > 0 ? CellGap / 2 : 0);
        var top = r.Y * TileHeight + (r.Y > 0 ? CellGap / 2 : 0);
        var right = (r.X + r.Width) * TileWidth - (r.X + r.Width < 0.999 ? CellGap / 2 : 0);
        var bottom = (r.Y + r.Height) * TileHeight - (r.Y + r.Height < 0.999 ? CellGap / 2 : 0);
        var cell = new Border
        {
            Width = Math.Max(1, right - left),
            Height = Math.Max(1, bottom - top),
            IsEnabled = enabled,
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        cell.Classes.Add("pdSnapCell");
        Canvas.SetLeft(cell, left);
        Canvas.SetTop(cell, top);
        AutomationProperties.SetAutomationId(cell, $"snap.flyout.{layoutId}.{index}");
        cell.PointerEntered += (_, _) => CellHovered?.Invoke(layoutId, index);
        cell.PointerExited += (_, _) => CellHovered?.Invoke(null, -1);
        cell.PointerReleased += (_, e) =>
        {
            if (e.InitialPressMouseButton == MouseButton.Left && cell.IsPointerOver)
            {
                e.Handled = true;
                CellPicked?.Invoke(layoutId, index);
            }
        };
        return cell;
    }
}
