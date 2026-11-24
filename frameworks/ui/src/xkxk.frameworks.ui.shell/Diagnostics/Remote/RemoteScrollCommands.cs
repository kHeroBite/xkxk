// 원격 스크롤(진단) — 그리드(DataGrid)는 행 번호로 ScrollIntoView, 그 밖은 ScrollViewer 오프셋·줄 단위로 스크롤하고 스크롤 뒤 보이는 행 범위를 돌려준다(lead 03:1x — 줄무늬 스크롤 후 새 행 확인)
#if PD_DIAGNOSTICS // 진단 빌드 전용(PdDiagnostics=true)
using System;
using System.Collections;
using System.Linq;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace xkxk.frameworks.ui.shell.Diagnostics.Remote;

internal static class RemoteScrollCommands
{
    // 공용 멤버 전달자(W2 F) — 본문은 옛 이름 그대로 쓴다
    private static xkxk.frameworks.ui.shell.Docking.PocDockFactory Factory => RemoteDockAccess.Factory;

    private static Control Target(JsonObject a) => RemoteUiTargets.Target(a);

    internal static void Register(RemoteCommandRouter r)
    {
        r.Register("ui.scroll",
            "스크롤(진단) 대상 id=|dock= [doc|window] — DataGrid: index=<행>(그 행이 보이게) | lines=<±n>(첫 보이는 행 기준 이동) | end=true, " +
            "가로 col=<표시 순서 열 번호> | colEnd=true(마지막 '비고정' 열 — 우측 고정 열 제외) — 행 인자와 함께 쓸 수 있음(픽셀 x= 는 공개 API 가 없어 미지원) / " +
            "ScrollViewer: offset=<y DIP> | dy=<±DIP> | lines=<±n>(줄 단위) → {kind, before, after, visibleRows?}(DataGrid 의 after·afterCols 는 한 프레임 뒤 레이아웃 반영 값)", async a =>
            {
                var target = Target(a);
                if (Find<DataGrid>(target) is { } grid)
                {
                    var res = GridScroll(grid, a);
                    // 스크롤 직후에는 셀 위치가 아직 이전 배치일 수 있어(otest-1 bq1153p — col=10 afterCols 에 화면 밖 열이 남음) 한 프레임 뒤 다시 읽는다
                    await AutomationBridge.WaitFramesAsync(VisualTreeInspector.Main(), 1);
                    if (grid.ItemsSource is IList after)
                    {
                        res["after"] = VisibleRows(grid, after);
                    }

                    res["afterCols"] = VisibleColumns(grid);
                    return res;
                }

                if (Find<ScrollViewer>(target) is { } sv)
                {
                    var before = sv.Offset.Y;
                    if (a.Num("offset") is { } off) { sv.Offset = new Vector(sv.Offset.X, off); }
                    else if (a.Num("dy") is { } dy) { sv.Offset = new Vector(sv.Offset.X, sv.Offset.Y + dy); }
                    else if (a.Int("lines") is { } lines)
                    {
                        for (var i = 0; i < Math.Abs(lines); i++) { if (lines > 0) { sv.LineDown(); } else { sv.LineUp(); } }
                    }
                    else { throw new RemoteCommandException("offset= | dy= | lines= 중 하나가 필요합니다."); }

                    return new JsonObject
                    {
                        ["kind"] = "ScrollViewer", ["before"] = Math.Round(before, 1), ["after"] = Math.Round(sv.Offset.Y, 1),
                        ["extent"] = Math.Round(sv.Extent.Height, 1), ["viewport"] = Math.Round(sv.Viewport.Height, 1),
                    };
                }

                throw new RemoteCommandException("대상과 그 안에 DataGrid·ScrollViewer 가 없습니다.");
            });

        RemoteDockTraceCommands.Register(r); // Q4 dock.trace(RemoteDockTraceCommands.cs)
        RemoteDockMenuCommands.Register(r); // UI-16 dock.menuHeaders(RemoteDockMenuCommands.cs)

        // Q4(Android 그리드 폭 1.1dp 진동) 원인 확정용: Dock 분할 비율을 전체 정밀도로 읽는다(읽기 전용).
        // Dock ProportionalStackPanel 이 측정·배치마다 비율을 정규화해 모델에 다시 쓰므로(디컴파일 확인), 전환 전후로 값이 바뀌는지 본다.
        r.Register("dock.proportions",
            "Dock 분할 비율(읽기 전용) → [{id, proportion(R 정밀도), collapsed, depth, bounds?}] — 테마·글꼴 전환 전후로 비교(Q4)", a =>
            {
                var list = new JsonArray();
                void Walk(Dock.Model.Core.IDockable d, int depth)
                {
                    list.Add(new JsonObject
                    {
                        ["id"] = d.Id,
                        ["proportion"] = d.Proportion.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                        ["collapsed"] = d.CollapsedProportion.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                        ["depth"] = depth,
                    });
                    if (d is Dock.Model.Core.IDock { VisibleDockables: { } children })
                    {
                        foreach (var c in children) { Walk(c, depth + 1); }
                    }
                }

                if (Factory.Root is { } root) { Walk(root, 0); }
                // 앱 배율(TopLevel.RenderScaling) — Fold5 2.7 추정을 실측으로 확인(lead)
                var scaling = VisualTreeInspector.Main().RenderScaling.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
                return new JsonObject { ["scaling"] = scaling, ["dockables"] = list };
            });

        // D-2 '같은 뷰 유지'·행 선택 유지 판정용(lead 10:2x) — 스크롤을 건드리지 않는다
        r.Register("grid.visible",
            "그리드 보기 상태 읽기(읽기 전용, 스크롤 안 함) 대상 id=|dock= [doc|window] → {count, first, last, rows(행 영역 안에 일부라도 보이는 행), selectedIndex, offsetY, gridHash·viewHash(DataGrid·GridDocumentView 인스턴스 식별값)}", a =>
                GridState(GridOf(a)));
        // 빈 칸 결함 W1 시험(lead 승인, 진단 전용): 실행 중 그리드 레이아웃 반올림 전환 — 웹·Android 에서 시작 인자 없이 대조하려고
        r.Register("grid.rounding",
            "문서 그리드 DataGrid 의 UseLayoutRounding 전환 on=true|false(생략 시 읽기만) — 화면의 모든 문서 그리드 → {count, rounding}", a =>
            {
                var grids = VisualTreeInspector.TopLevels().SelectMany(t => t.GetVisualDescendants()).OfType<Control>().Where(c => c is IGridDocumentDiagnostics)
                    .Select(v => Find<DataGrid>(v)).Where(g => g is not null).ToList();
                bool? on = a.ContainsKey("on") ? a.Bool("on") : null;
                foreach (var g in grids) { if (on is { } v) { g!.UseLayoutRounding = v; g.InvalidateMeasure(); } }
                return new JsonObject { ["count"] = grids.Count, ["rounding"] = grids.Count > 0 ? grids[0]!.UseLayoutRounding : null };
            });
        r.Register("grid.select",
            "그리드 행 선택 index=<행 번호>(정렬·필터 뒤 보이는 순서) [scroll=true 면 그 행이 보이게] 대상 id=|dock= [doc|window] → {before, after}", a =>
            {
                var grid = GridOf(a);
                var before = GridState(grid);
                var index = a.Int("index") ?? throw new RemoteCommandException("index= 가 필요합니다.");
                if (grid.ItemsSource is not IList items || index < 0 || index >= items.Count)
                {
                    throw new RemoteCommandException($"index 범위 밖입니다(0~{(grid.ItemsSource as IList)?.Count - 1}).");
                }

                grid.SelectedIndex = index;
                if (a.Bool("scroll")) { grid.ScrollIntoView(items[index], null); }
                grid.UpdateLayout();
                return new JsonObject { ["before"] = before, ["after"] = GridState(grid) };
            });
    }

    /// <summary>대상(생략 시 활성 문서) 안의 DataGrid. 없으면 오류.</summary>
    internal static DataGrid GridOf(JsonObject a) =>
        Find<DataGrid>(Target(a)) ?? throw new RemoteCommandException("대상 안에 DataGrid 가 없습니다.");

    /// <summary>
    /// 그리드 보기 상태. first·last·rows 는 행 영역(DataGridRowsPresenter) 안에 일부라도 들어온 행만 센다 —
    /// 가상화로 재활용돼 영역 밖에 남은 행 컨테이너는 뺀다. offsetY = 세로 스크롤바 값(없으면 null).
    /// </summary>
    internal static JsonObject GridState(DataGrid grid)
    {
        var items = grid.ItemsSource as IList;
        var presenter = grid.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.DataGridRowsPresenter>().FirstOrDefault();
        // (행 번호, 행 영역 안 y, 높이) — 맨 위에 몇 px 만 걸친 행은 재배치 직후 실현 여부가 달라질 수 있어 first 가 1 차이 날 수 있다.
        // 그래서 완전히 보이는 첫 행(firstFull)과 맨 위 행이 가려진 px(topClip)도 함께 낸다 — '같은 뷰' 판정은 offsetY·firstFull 로 한다.
        System.Collections.Generic.List<(int Index, double? Y, double H)> candidates = items is null || presenter is null ? [] : grid.GetVisualDescendants().OfType<DataGridRow>()
            .Where(row => row.IsEffectivelyVisible && row.DataContext is not null && row.Bounds.Height > 0)
            .Select(row => (Index: items.IndexOf(row.DataContext), Y: row.TranslatePoint(default, presenter)?.Y, H: row.Bounds.Height))
            .Where(t => t.Index >= 0 && t.Y is { } y && y + t.H > 0 && y < presenter.Bounds.Height)
            .ToList();
        // D-GV1: DataGrid 12.1.2 는 포커스 행(_focusedRow)·편집 행을 재활용하지 않아(DataGridRows.cs:328) 화면 밖으로 나간 컨테이너가 옛 위치에 남고 위 필터를 통과한다.
        // 실제 배치된 행은 index×P − Y(=스크롤 위치)가 모두 같다 — 그 최빈값(P/2 단위로 묶음)에서 반 행 넘게 어긋난 행을 남은 컨테이너(staleRows)로 뺀다.
        // P 는 RowHeight(28)가 아니라 실현 행 Bounds.Height 중앙값(실측 약 31.1 — 행 테두리·가로 격자선, D-RH1)이다.
        var stale = new System.Collections.Generic.List<int>();
        if (candidates.Count > 1)
        {
            var hs = candidates.Select(t => t.H).OrderBy(x => x).ToList();
            var h = hs[hs.Count / 2];
            double Key(double y, int index) => index * h - y;
            var mode = candidates.GroupBy(t => Math.Round(Key(t.Y!.Value, t.Index) / (h / 2)))
                .OrderByDescending(g => g.Count()).First().Select(t => Key(t.Y!.Value, t.Index)).Average();
            stale = candidates.Where(t => Math.Abs(Key(t.Y!.Value, t.Index) - mode) > h / 2).Select(t => t.Index).OrderBy(i => i).ToList();
        }

        System.Collections.Generic.List<(int Index, double? Y, double H)> placed = candidates
            .Where(t => !stale.Contains(t.Index)).OrderBy(t => t.Index).ToList();
        System.Collections.Generic.List<int> rows = placed.Select(t => t.Index).ToList();
        var full = placed.Where(t => t.Y >= -0.5 && t.Y + t.H <= presenter!.Bounds.Height + 0.5).Select(t => t.Index).ToList();
        var bar = grid.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.ScrollBar>()
            .FirstOrDefault(s => s.Orientation == Avalonia.Layout.Orientation.Vertical);
        return new JsonObject
        {
            ["count"] = items?.Count ?? 0,
            ["first"] = rows.Count > 0 ? rows[0] : null,
            ["last"] = rows.Count > 0 ? rows[^1] : null,
            ["rows"] = rows.Count,
            ["firstFull"] = full.Count > 0 ? full[0] : null,
            ["lastFull"] = full.Count > 0 ? full[^1] : null,
            ["topClip"] = placed.Count > 0 && placed[0].Y < 0 ? Math.Round(-placed[0].Y!.Value, 1) : 0,
            ["staleRows"] = new JsonArray(stale.Select(i => (JsonNode?)JsonValue.Create(i)).ToArray()), // D-GV1 로 걸러낸 행 번호(보통 포커스 행 1개)
            ["rowHeight"] = double.IsNaN(grid.RowHeight) ? null : grid.RowHeight,
            ["selectedIndex"] = grid.SelectedIndex,
            ["columns"] = VisibleColumns(grid), // 보이는 열 {firstCol, lastCol, cols, frozen, colCount}(표시 순서)
            ["offsetY"] = bar is null ? null : Math.Round(bar.Value, 1),
            // 인스턴스 식별값(같은 객체면 같은 값) — 독립 창 전환 직후 스크롤·선택 초기화 결함에서 뷰가 다시 만들어졌는지 판별(odev-2r 조사, lead)
            ["gridHash"] = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(grid),
            ["viewHash"] = grid.GetVisualAncestors().OfType<IGridDocumentDiagnostics>().FirstOrDefault() is { } view
                ? System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(view) : null,
        };
    }

    /// <summary>대상 자신 또는 자손 중 첫 T.</summary>
    private static T? Find<T>(Control c) where T : Control =>
        c as T ?? c.GetVisualDescendants().OfType<T>().FirstOrDefault();

    /// <summary>DataGrid 스크롤: 행 번호로 ScrollIntoView. 응답 visibleRows = 지금 화면에 그려진 행의 항목 번호 범위(첫·끝·개수).</summary>
    private static JsonObject GridScroll(DataGrid grid, JsonObject a)
    {
        if (grid.ItemsSource is not IList items || items.Count == 0)
        {
            throw new RemoteCommandException("DataGrid 항목이 없습니다(ItemsSource 가 IList 가 아님 또는 0건).");
        }

        var before = VisibleRows(grid, items);
        var beforeCols = VisibleColumns(grid);
        // 가로(열): col=<표시 순서 번호> | colEnd=true(마지막 열). DataGrid 에 공개 가로 오프셋 API 가 없어 공개 ScrollIntoView(항목, 열)로 그 열이 보이게 한다(픽셀 단위 x= 는 미지원).
        DataGridColumn? column = null;
        if (a.Bool("colEnd") || a.Int("col") is not null)
        {
            var ordered = grid.Columns.Where(c => c.IsVisible).OrderBy(c => c.DisplayIndex).ToList();
            if (ordered.Count == 0) { throw new RemoteCommandException("보이는 열이 없습니다."); }
            // colEnd = 마지막 '비고정' 열(우측 고정 열은 늘 보이므로 스크롤 대상이 아니다 — otest-1 bq1153p)
            var ci = a.Bool("colEnd") ? Math.Max(0, ordered.Count - 1 - FrozenRight(grid)) : a.Int("col")!.Value;
            column = ordered[Math.Clamp(ci, 0, ordered.Count - 1)];
        }

        int index;
        if (a.Bool("end")) { index = items.Count - 1; }
        else if (a.Int("index") is { } ix) { index = ix; }
        else if (a.Int("lines") is { } lines)
        {
            // 아래로 n 줄: 마지막 보이는 행 + n 을 보이게, 위로: 첫 보이는 행 − n 을 보이게(ScrollIntoView 는 최소 이동이라 방향별로 기준을 다르게 잡는다)
            var first = before?["first"]?.GetValue<int>() ?? 0;
            var last = before?["last"]?.GetValue<int>() ?? 0;
            index = lines >= 0 ? last + lines : first + lines;
        }
        else if (column is not null) { index = before?["first"]?.GetValue<int>() ?? 0; } // 열만 지정: 세로 위치는 지금 첫 보이는 행 그대로
        else { throw new RemoteCommandException("index= | lines= | end=true | col=<n> | colEnd=true 중 하나가 필요합니다."); }

        index = Math.Clamp(index, 0, items.Count - 1);
        grid.ScrollIntoView(items[index], column);
        grid.UpdateLayout(); // 행 가상화가 새 행을 만든 뒤 범위를 읽는다
        return new JsonObject
        {
            ["kind"] = "DataGrid", ["count"] = items.Count, ["target"] = index, ["targetCol"] = column?.DisplayIndex,
            ["before"] = before, ["after"] = VisibleRows(grid, items),
            ["beforeCols"] = beforeCols, ["afterCols"] = VisibleColumns(grid),
        };
    }

    /// <summary>
    /// 보이는 열(가로 스크롤·고정 열 판정용) {firstCol, lastCol, cols[], frozen}. 열 번호는 표시 순서(DisplayIndex).
    /// 첫 실현 행의 셀 위치로 판정한다 — 셀이 행 영역(DataGridRowsPresenter) 가로 범위에 일부라도 들어오면 보이는 것으로 센다.
    /// 셀↔열 대응은 셀 표시자의 자식 순서 = Columns 순서(열 재배치가 없을 때)에 기댄다. 셀 소유 열(OwningColumn)은 공개 API 가 아니라 쓰지 않는다.
    /// </summary>
    private static JsonObject? VisibleColumns(DataGrid grid)
    {
        var presenter = grid.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.DataGridRowsPresenter>().FirstOrDefault();
        var row = grid.GetVisualDescendants().OfType<DataGridRow>().FirstOrDefault(r => r.IsEffectivelyVisible && r.DataContext is not null && r.Bounds.Height > 0);
        var cellsPresenter = row?.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.DataGridCellsPresenter>().FirstOrDefault();
        if (presenter is null || cellsPresenter is null)
        {
            return null;
        }

        var width = presenter.Bounds.Width;
        var shown = new System.Collections.Generic.List<int>();
        var cells = cellsPresenter.Children.OfType<DataGridCell>().ToList();
        for (var i = 0; i < cells.Count && i < grid.Columns.Count; i++)
        {
            var cell = cells[i];
            if (!cell.IsVisible || cell.Bounds.Width <= 0 || cell.TranslatePoint(default, presenter) is not { } p)
            {
                continue;
            }

            if (p.X + cell.Bounds.Width > 0.5 && p.X < width - 0.5)
            {
                shown.Add(grid.Columns[i].DisplayIndex);
            }
        }

        shown.Sort();
        return new JsonObject
        {
            ["firstCol"] = shown.Count > 0 ? shown[0] : null,
            ["lastCol"] = shown.Count > 0 ? shown[^1] : null,
            ["cols"] = new JsonArray(shown.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray()),
            ["frozen"] = grid.FrozenColumnCount,
            ["frozenRight"] = FrozenRight(grid),
            ["colCount"] = grid.Columns.Count(c => c.IsVisible),
        };
    }

    /// <summary>
    /// 우측 고정 열 수. ProDataGrid 에만 있는 등록 속성 FrozenColumnCountRight 를 이름으로 찾는다(내장 DataGrid 에는 없어 0) —
    /// 두 그리드 모두에서 컴파일되게 형식 멤버를 직접 참조하지 않는다.
    /// </summary>
    private static int FrozenRight(DataGrid grid) =>
        AvaloniaPropertyRegistry.Instance.FindRegistered(grid, "FrozenColumnCountRight") is { } p && grid.GetValue(p) is int n ? n : 0;

    /// <summary>화면에 그려진(보이는) DataGridRow 들의 항목 번호 {first, last, rows}. 없으면 null.</summary>
    private static JsonObject? VisibleRows(DataGrid grid, IList items)
    {
        var rows = grid.GetVisualDescendants().OfType<DataGridRow>()
            .Where(row => row.IsEffectivelyVisible && row.DataContext is not null && row.Bounds.Height > 0)
            .Select(row => items.IndexOf(row.DataContext)).Where(i => i >= 0).OrderBy(i => i).ToList();
        return rows.Count == 0 ? null : new JsonObject { ["first"] = rows[0], ["last"] = rows[^1], ["rows"] = rows.Count };
    }
}
#endif
