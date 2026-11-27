// 그리드 벤치의 병합 헤더(2단 열 밴드) 시나리오 — ProDataGrid 빌드에서만 동작하고 내장 DataGrid 빌드에서는 "미지원" 결과만 돌려준다
using xkxk.frameworks.ui.shell;
using System;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Avalonia.Controls;
#if PD_GRID_PRO
using System.Diagnostics;
using Avalonia.Controls.DataGridBanding;
#endif

namespace xkxk.frameworks.ui.testbed.Bench;

internal static class GridBenchBands
{
#if PD_GRID_PRO
    /// <summary>
    /// 병합 헤더(2단: 기본정보·일정·실적·시세) 그리드를 새로 만들어 같은 자리에 바꿔 끼우고 첫 표시 시간을 잰다.
    /// 컨트롤을 코드로 만드는 이유: 열 정의 소스(ColumnDefinitionsSource) 방식은 XAML 열과 섞을 수 없고, 두 방식의 측정을 분리해야 하기 때문이다.
    /// </summary>
    internal static async Task<JsonObject> RunAsync(Func<TopLevel> top, Panel gridHost, Func<GridBenchModel> model)
    {
        var m = model();
        var bands = new ColumnBandModel { HeaderLayout = ColumnBandHeaderLayout.Grouped };
        // 열 폭은 GridBenchView.axaml 과 같은 고정 픽셀(공정 비교 — 별 열 * 없음)
        ColumnBand Leaf(string header, System.Linq.Expressions.Expression<Func<BenchRow, string>> expr, double width) =>
            new() { Header = header, ColumnDefinition = new DataGridTextColumnDefinition { Header = header, Binding = DataGridBindingDefinition.Create(expr), Width = new DataGridLength(width) } };
        ColumnBand Group(string header, params ColumnBand[] children)
        {
            var g = new ColumnBand { Header = header };
            foreach (var c in children) { g.Children.Add(c); }
            return g;
        }

        // 그룹 경계를 고정 열(2) 경계에 맞춘다. 문서: "Grouped headers split automatically at frozen-column boundaries"
        // (docfx/articles/column-banding.md) — 예전 '기본정보'(번호·이름·부서·직급)는 고정 경계에서 잘려 상위 헤더가 두 번 나왔다(otest-1 b1142pro·bp1146p).
        bands.Bands.Add(Group("식별", Leaf("번호", r => r.NoText, 80), Leaf("이름", r => r.Name, 100)));
        bands.Bands.Add(Group("소속", Leaf("부서", r => r.Dept, 100), Leaf("직급", r => r.Grade, 80)));
        bands.Bands.Add(Group("일정", Leaf("시작", r => r.StartText, 100), Leaf("종료", r => r.EndText, 100), Leaf("진척", r => r.ProgressText, 80)));
        bands.Bands.Add(Group("실적", Leaf("공수(h)", r => r.HoursText, 96), Leaf("예산", r => r.BudgetText, 120), Leaf("증감률", r => r.RateText, 96)));
        bands.Bands.Add(Group("시세", Leaf("가격", r => r.PriceText, 90), Leaf("등락", r => r.ChangeText, 80), Leaf("거래량", r => r.VolumeText, 100)));

        var grid = new DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            FrozenColumnCount = 2,
            ColumnDefinitionsSource = bands.ColumnDefinitions,
        };
        var sw = Stopwatch.StartNew();
        var showMs = await BenchProbe.TimeToFramesAsync(top(), () =>
        {
            gridHost.Children.Clear();
            gridHost.Children.Add(grid);
            grid.ItemsSource = m.Rows;
        });
        return new JsonObject
        {
            ["supported"] = true,
            ["layout"] = "Grouped",
            ["groups"] = bands.Bands.Count,
            ["leaf_columns"] = bands.ColumnDefinitions.Count,
            ["first_show_ms"] = Math.Round(showMs, 1),
            ["total_ms"] = Math.Round(sw.Elapsed.TotalMilliseconds, 1),
            ["note"] = "원래 그리드로 돌아가려면 문서를 닫고 다시 연다",
        };
    }
#else
    internal static Task<JsonObject> RunAsync(Func<TopLevel> top, Panel gridHost, Func<GridBenchModel> model)
    {
        _ = top;
        _ = gridHost;
        _ = model;
        return Task.FromResult(new JsonObject { ["supported"] = false, ["reason"] = "내장 Avalonia DataGrid 는 병합 헤더(열 밴드)를 제공하지 않는다" });
    }
#endif
}
