// DataGrid 의 공통 휠 조작 대상 구현 — Alt=열 폭, Alt+Shift=행 높이(커서 아래 행 유지), 값은 저장하지 않는다
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using xkxk.frameworks.ui.Input;

namespace xkxk.frameworks.ui.views.Documents;

/// <summary>
/// 공개 API 만 쓴다(내장 DataGrid·ProDataGrid 공통). 세로 오프셋 설정 API 가 없어 앵커 보정은 합성 휠 1회로 한다.
/// Shift+휠 가로 스크롤은 DataGrid 자체 처리에 맡긴다.
/// <para>시험대 지원용, 공개 계약 아님(B16 생성 명세 제외 후보).</para>
/// </summary>
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
public sealed class GridDenseTarget : IDenseViewTarget
{
    /// <summary>DataGrid 의 휠 1단위 픽셀(원본 DATAGRID_mouseWheelDelta). 다르면 앵커 위치만 조금 어긋난다.</summary>
    private const double WheelUnit = 50;

    private const int SyntheticPointerId = 0x5D_0001;

    private readonly DataGrid _grid;
    private readonly GridColumnScaler _scaler;
    private readonly Action _reapply;

    internal GridDenseTarget(DataGrid grid, GridColumnScaler scaler, Action reapply)
    {
        _grid = grid;
        _scaler = scaler;
        _reapply = reapply;
    }

    /// <summary>글자 크기 비례가 없는 표(벤치)에 붙인다. 기준 행 높이를 고정값으로 두고 XAML 열 폭을 그대로 기준으로 삼는다.</summary>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public static void AttachFixed(DataGrid grid, double rowHeight)
    {
        var scaler = new GridColumnScaler(grid, rowHeight);
        DenseWheel.Attach(grid, new GridDenseTarget(grid, scaler, () => scaler.Apply(14, FontFamily.Default)));
    }

    public string Kind => "grid";

    public int RowLevel { get; private set; }

    public int ColumnLevel { get; private set; }

    public void StepRows(int direction, Point at)
    {
        var next = DenseScale.StepRow(RowLevel, direction);
        if (next == RowLevel)
        {
            return;
        }

        var anchor = RowAt(at);
        var item = anchor?.DataContext;
        var oldHeight = anchor?.Bounds.Height ?? 0;
        var ratio = anchor is not null && oldHeight > 0 ? (at.Y - TopOf(anchor)) / oldHeight : 0;
        RowLevel = next;
        _scaler.RowFactor = DenseScale.Factor(next);
        _reapply();
        if (item is not null)
        {
            KeepUnderPointer(item, at.Y, ratio);
        }
    }

    public void StepColumns(int direction, Point at)
    {
        ColumnLevel = DenseScale.StepColumn(ColumnLevel, direction);
        _scaler.ColumnFactor = DenseScale.Factor(ColumnLevel);
        _reapply();
    }

    /// <summary>표는 문서 창 안에 있어 Ctrl+휠을 전역 처리기가 받는다.</summary>
    public bool StepLocalFont(int direction) => false;

    public int? RowIndexAt(Point at) => RowAt(at)?.GetIndex();

    public IReadOnlyDictionary<string, double> Snapshot()
    {
        var bars = _grid.GetVisualDescendants().OfType<ScrollBar>().ToList();
        var snapshot = new Dictionary<string, double>
        {
            ["rowHeight"] = _grid.RowHeight,
            ["columnSum"] = _grid.Columns.Where(c => c.IsVisible).Sum(c => c.ActualWidth),
            ["hOffset"] = bars.FirstOrDefault(s => s.Orientation == Orientation.Horizontal)?.Value ?? 0,
            ["vOffset"] = bars.FirstOrDefault(s => s.Orientation == Orientation.Vertical)?.Value ?? 0,
            ["starPinned"] = _scaler.StarPinned ? 1 : 0, // K5: 넘쳐서 별 열을 절대 폭으로 고정했는가(1=고정)
            ["headerHeight"] = _grid.GetVisualDescendants().OfType<DataGridColumnHeader>().FirstOrDefault(h => h.Bounds.Height > 0)?.Bounds.Height ?? 0,
        };
        var order = 0;
        foreach (var column in _grid.Columns.Where(c => c.IsVisible).OrderBy(c => c.DisplayIndex))
        {
            snapshot[$"colWidth{order++}"] = column.ActualWidth; // 표시 순서별 폭 — 머리글 끌기 판정이 경계 위치와 비율을 잰다
        }

        return snapshot;
    }

    private DataGridRow? RowAt(Point at) =>
        (_grid.InputHitTest(at) as Visual)?.GetSelfAndVisualAncestors().OfType<DataGridRow>().FirstOrDefault();

    private DataGridRow? RealizedRow(object item) =>
        _grid.GetVisualDescendants().OfType<DataGridRow>().FirstOrDefault(r => ReferenceEquals(r.DataContext, item) && r.IsVisible);

    private double TopOf(Visual row) => row.TranslatePoint(default, _grid)?.Y ?? 0;

    private void KeepUnderPointer(object item, double pointerY, double ratio)
    {
        _grid.UpdateLayout();
        var row = RealizedRow(item);
        if (row is null)
        {
            _grid.ScrollIntoView(item, null!); // 행이 높아져 밀려난 경우
            _grid.UpdateLayout();
            row = RealizedRow(item);
        }

        if (row is null)
        {
            return;
        }

        var shift = TopOf(row) - (pointerY - (ratio * row.Bounds.Height));
        if (Math.Abs(shift) >= 0.5)
        {
            RaiseScroll(-shift / WheelUnit); // 행이 원하는 곳보다 아래면 내용을 위로(휠 아래 방향)
        }
    }

    private void RaiseScroll(double wheelDelta)
    {
        if (TopLevel.GetTopLevel(_grid) is not { } top)
        {
            return;
        }

        using var pointer = new Pointer(SyntheticPointerId, PointerType.Mouse, false);
        _grid.RaiseEvent(new PointerWheelEventArgs(_grid, pointer, top, default, (ulong)Environment.TickCount64,
            new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.Other), KeyModifiers.None, new Vector(0, wheelDelta)));
    }
}
