// 간트 캔버스의 공통 휠 조작 대상 구현 — Alt=날짜 폭, Alt+Shift=행 높이(커서 아래 행 유지), 값은 저장하지 않는다
using System;
using System.Collections.Generic;
using Avalonia;
using xkxk.frameworks.ui.Input;

namespace xkxk.frameworks.ui.views.Documents;

public sealed partial class GanttCanvas : IDenseViewTarget
{
    private int _rowLevel;

    string IDenseViewTarget.Kind => "gantt";

    public int RowLevel => _rowLevel;

    int IDenseViewTarget.ColumnLevel => (int)Math.Round(Math.Log(_dayWidth / 6, DenseScale.Ratio));

    /// <summary>행 높이 한 단계. 왼쪽 작업명과 오른쪽 막대는 RowHeight 하나만 쓰므로 정렬이 어긋나지 않는다.</summary>
    public void StepRows(int direction, Point at)
    {
        var next = DenseScale.StepRow(_rowLevel, direction);
        if (next == _rowLevel)
        {
            return;
        }

        using var op = xkxk.frameworks.ui.shell.Diagnostics.PerfProbe.Current.TimeOperation("doc.gantt.rows");
        var y = Math.Max(0, at.Y - HeaderHeight);
        var row = (_offsetY + y) / RowHeight; // 커서 아래 행(소수 포함)
        _rowLevel = next;
        ScrollTo(_offsetX, (row * RowHeight) - y);
    }

    /// <summary>Alt+휠과 도구줄 버튼이 같은 비율을 쓴다(왼쪽 끝 날짜 유지).</summary>
    public void StepColumns(int direction, Point at)
    {
        using var op = xkxk.frameworks.ui.shell.Diagnostics.PerfProbe.Current.TimeOperation("doc.gantt.zoom");
        SetZoom(_dayWidth * (direction > 0 ? DenseScale.Ratio : 1 / DenseScale.Ratio));
    }

    /// <summary>간트는 항상 문서 창 안이라 Ctrl+휠을 전역 처리기가 받는다.</summary>
    bool IDenseViewTarget.StepLocalFont(int direction) => false;

    public int? RowIndexAt(Point at)
    {
        if (at.Y < HeaderHeight || _tasks.Count == 0)
        {
            return null;
        }

        var i = (int)((_offsetY + at.Y - HeaderHeight) / RowHeight);
        return i >= 0 && i < _tasks.Count ? i : null;
    }

    IReadOnlyDictionary<string, double> IDenseViewTarget.Snapshot() => new Dictionary<string, double>
    {
        ["rowHeight"] = RowHeight,
        ["headerHeight"] = HeaderHeight,
        ["dayWidth"] = _dayWidth,
        ["offsetX"] = _offsetX,
        ["offsetY"] = _offsetY,
    };
}
