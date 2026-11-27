// 그리드 벤치의 강조 셀 대조(ProDataGrid 빌드 전용) — 화면에 실현된 상태 열 셀마다 규칙상 강조 여부와 실제 강조 여부를 비교한다
#if PD_GRID_PRO
using xkxk.frameworks.ui.shell;
using System;
using System.Text.Json.Nodes;
using Avalonia; // TranslatePoint(Avalonia.VisualExtensions) — 진단 필드
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace xkxk.frameworks.ui.testbed.Bench;

internal static class GridBenchHighlight
{
    private const string LateValue = "지연";

    /// <summary>
    /// 강조 셀 대조 — 지금 화면에 실현된 상태 열 셀마다 "규칙상 강조돼야 하는가"와 "실제로 강조됐는가"를 비교한다.
    /// 실제 강조는 방식과 무관하게 판정한다: 셀 테마가 조건부 서식 테마(lateTheme)이거나, 셀·셀 내용의 배경이 강조 브러시(lateBg) 인스턴스이면 강조다.
    /// cf_only 와 cell_style 의 강조 셀 집합이 같은지(wrong=0, 같은 화면 위치에서 highlighted 같음)를 이 값으로 확인한다.
    /// </summary>
    internal static JsonObject Check(DataGrid grid, bool ruleActive, GridBenchModel? model, IBrush lateBg, ControlTheme? lateTheme)
    {
        var col = (DataGridTemplateColumn)grid.Columns[6]; // GridBenchView.axaml 7번째 열(상태)
        int realized = 0, expected = 0, highlighted = 0, wrong = 0;
        var rowsSeen = new System.Collections.Generic.HashSet<BenchRow>(); // 진단: 실현 행 수(전체 열 기준)
        DataGridCell? firstStatus = null; // 진단: 상태 열 가로 위치(첫 실현 셀 기준)
        foreach (var v in Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(grid))
        {
            if (v is DataGridCell any && any.IsVisible && any.DataContext is BenchRow anyRow) { rowsSeen.Add(anyRow); }
            if (v is not DataGridCell cell || !ReferenceEquals(cell.OwningColumn, col) || !cell.IsVisible) { continue; }
            if (cell.DataContext is not BenchRow row) { continue; }
            firstStatus ??= cell;
            realized++;
            var shouldBe = ruleActive && row.Status == LateValue;
            var isHi = ReferenceEquals(cell.Theme, lateTheme) && lateTheme is not null
                || ReferenceEquals(cell.Background, lateBg)
                || (cell.Content is Border b && ReferenceEquals(b.Background, lateBg))
                || (cell.Presenter?.Child is Border pb && ReferenceEquals(pb.Background, lateBg));
            if (shouldBe) { expected++; }
            if (isHi) { highlighted++; }
            if (shouldBe != isHi) { wrong++; }
        }

        double? statusX = null, statusW = null;
        if (firstStatus is not null && firstStatus.TranslatePoint(default, grid) is { } sp)
        {
            statusX = sp.X;
            statusW = firstStatus.Bounds.Width;
        }

        int dataLate = 0, dataRows = 0;
        if (model?.Rows is System.Collections.IEnumerable rows) // 로드 전에는 data_rows=0(i1720rcgp 에서 Model 접근 예외 — cell_style 절차 v2 원인)
        {
            foreach (var o in rows)
            {
                dataRows++;
                if (o is BenchRow r && r.Status == LateValue) { dataLate++; }
            }
        }

        return new JsonObject
        {
            ["rule_active"] = ruleActive,
            ["data_rows"] = dataRows,
            ["data_rows_late"] = dataLate,
            ["realized_cells"] = realized,
            ["expected_highlight"] = expected,
            ["highlighted"] = highlighted,
            ["wrong"] = wrong,
            ["match"] = wrong == 0,
            // 진단(다음 판): realized_cells 가 작은 원인 — 실현 행 수와 상태 열이 그리드 가로 범위 안에 있는지
            ["realized_rows"] = rowsSeen.Count,
            ["status_col_display_index"] = col.DisplayIndex,
            ["grid_width"] = Math.Round(grid.Bounds.Width, 1),
            ["status_col_x"] = statusX is null ? null : Math.Round(statusX.Value, 1),
            ["status_col_width"] = statusW is null ? null : Math.Round(statusW.Value, 1),
            ["status_col_in_view"] = statusX is null || statusW is null ? null : statusX.Value >= 0 && statusX.Value + statusW.Value <= grid.Bounds.Width + 0.5,
        };
    }
}
#endif
