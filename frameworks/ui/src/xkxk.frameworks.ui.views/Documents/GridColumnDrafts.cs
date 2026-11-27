// 그리드 문서 열 구성 대조·초안 — 대조 빌드(PD_GRID_TEXTCOLUMNS) 열 교체와 진단 인자 --grid-open 첫 측정 감소 초안(notrim·staticstroke·litecell·noround·fixedname)
using xkxk.frameworks.ui.shell;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Styling;
using xkxk.frameworks.ui.shell.Diagnostics;
using xkxk.frameworks.ui.shell.Localization;

namespace xkxk.frameworks.ui.views.Documents;

/// <summary>GridDocumentView 생성자가 InitializeComponent 직후 1회 부른다. 기본 빌드는 열 방식 문자열 기록만 한다. 시험대 지원용, 공개 계약 아님(B16 생성 명세 제외 후보).</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class GridColumnDrafts
{
    /// <summary>시험대 지원용, 공개 계약 아님(B16 생성 명세 제외 후보). 대조 빌드(-p:PdGridTextColumns=true)에서만 열을 이전 방식(DataGridTextColumn + 리플렉션 바인딩)으로 교체한다. 진단 빌드는 --grid-open 초안도 적용한다.</summary>
    public static void ApplyControlColumns(Control view, DataGrid grid)
    {
#if PD_GRID_TEXTCOLUMNS
        grid.Columns.Clear();
        void Add(string header, string path, string sort, double width, double min = 0)
        {
            var col = new DataGridTextColumn
            {
                Header = header,
                Binding = new Binding(path),
                SortMemberPath = sort,
                Width = width > 0 ? new DataGridLength(width) : new DataGridLength(1, DataGridLengthUnitType.Star),
            };
            if (min > 0) { col.MinWidth = min; }
            grid.Columns.Add(col);
        }

        Add(Loc.Tr("doc.grid.col.no"), nameof(GridRow.NoText), nameof(GridRow.No), 70);
        Add(Loc.Tr("doc.grid.col.code"), nameof(GridRow.Code), nameof(GridRow.Code), 100);
        Add(Loc.Tr("doc.grid.col.name"), nameof(GridRow.Name), nameof(GridRow.Name), 0, 160);
        Add(Loc.Tr("doc.grid.col.owner"), nameof(GridRow.Owner), nameof(GridRow.Owner), 80);
        Add(Loc.Tr("doc.grid.col.status"), nameof(GridRow.Status), nameof(GridRow.Status), 70);
        Add(Loc.Tr("doc.grid.col.start"), nameof(GridRow.StartText), nameof(GridRow.Start), 100);
        Add(Loc.Tr("doc.grid.col.end"), nameof(GridRow.EndText), nameof(GridRow.End), 100);
        Add(Loc.Tr("doc.grid.col.progress"), nameof(GridRow.ProgressText), nameof(GridRow.Progress), 70);
        Add(Loc.Tr("doc.grid.col.hours"), nameof(GridRow.HoursText), nameof(GridRow.Hours), 80);
        Add(Loc.Tr("doc.grid.col.note"), nameof(GridRow.Note), nameof(GridRow.Note), 160);
        PerfProbe.Current.SetText("grid_columns", "text-reflection");
#else
        PerfProbe.Current.SetText("grid_columns", "text-lite"); // G1 경량 텍스트 열(PdTextColumn.cs). 이전 기본값은 template-compiled
#endif
#if PD_DIAGNOSTICS
        ApplyOpenDrafts(view, grid);
#endif
    }

#if PD_DIAGNOSTICS
    /// <summary>
    /// 첫 측정(measure1) 감소 초안을 진단 인자로 골라 켠다(Q10). 제품에 넣을지는 측정 뒤 결정한다.
    /// --grid-open notrim,fixedname  (쉼표로 여러 개)
    ///   notrim    = 셀 TextTrimming 끔(말줄임 계산 없음 — 긴 문구는 잘려 보인다)
    ///   fixedname = 작업명 열을 별(*) 대신 고정 폭(220, 글자 크기 비례)으로 — 별 열 폭 분배 계산 생략
    ///   staticstroke = 셀 템플릿 숨은 Rectangle 의 Stroke 를 우리 토큰 브러시 인스턴스로 고정(Q4 — 테마 전환 때 셀 측정 무효 제거 초안)
    ///   litecell  = 셀 템플릿을 가벼운 판(LiteCellTemplate)으로 교체(Q4 수정안 2 — 셀 단위 포커스 테두리 없음, 대조 측정용)
    ///   fluentcell = 기본 셀 템플릿(PdCellTemplate, 숨은 표시 지연 생성)을 빼고 Fluent 원본 셀 템플릿으로(A7 대조 측정용)
    ///   latefit   = K5 별 열 판정을 첫 Measure 전에 하지 않는 이전 동작(A7 대조 측정용)
    ///   noround   = 그리드(DataGrid)만 UseLayoutRounding=false(빈 칸 결함 W1 시험)
    ///   litetheme = 행·셀 Fluent ControlTheme 을 최소 테마로 교체(A16 대조 — 테마 setter·중첩 스타일 비용 상한)
    ///   fewcols   = 화면 밖 열 4개 제거(A16 대조 — 숨은 셀 비용)
    ///   fullcell  = 단일 요소 셀(PdCellChrome, 기본 켬)을 끄고 이전 셀 템플릿으로(A16 전후 대조)
    ///   tbcell    = 그리는 셀 글자(기본 켬)를 끄고 셀마다 TextBlock(K15 (b) 전후 대조 — notrim 은 이 판에서만 효과)
    ///   notext    = 셀 글자를 아예 만들지 않음(K15 (b) 셀 글자 비용 상한, 겉모양 다름)
    /// 기록: perf text grid_open_mode(적용된 초안 목록, 없으면 default).
    /// </summary>
    private static void ApplyOpenDrafts(Control view, DataGrid grid)
    {
        var mode = "default";
        var args = App.StartupArgs;
        for (var i = 0; i < args.Count; i++)
        {
            var a = args[i];
            if (a.StartsWith("--grid-open=", System.StringComparison.OrdinalIgnoreCase)) { mode = a[12..]; }
            else if (a.Equals("--grid-open", System.StringComparison.OrdinalIgnoreCase) && i + 1 < args.Count) { mode = args[i + 1]; }
        }

        var applied = new System.Collections.Generic.List<string>();
        foreach (var part in mode.Split(',', System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "notrim":
                    view.Styles.Add(new Avalonia.Styling.Style(x => x.OfType<TextBlock>().Class("cell"))
                    {
                        Setters = { new Avalonia.Styling.Setter(TextBlock.TextTrimmingProperty, Avalonia.Media.TextTrimming.None) },
                    });
                    applied.Add(part);
                    break;
                case "staticstroke": // Q4 셀 숨은 테두리 Stroke 고정(본체는 제품 코드 GridCellStrokes.cs — 검증 뒤 기본값 전환)
                    GridCellStrokes.UseStableCellStrokes = true; // 적용은 생성자의 ApplyStableCellStrokes 호출이 한다(제품 경로와 같은 한 곳)
                    applied.Add(part);
                    break;
                case "fluentcell": // A7 대조 — 기본 셀 템플릿(PdCellTemplate)을 걷어 Fluent 원본 셀 템플릿으로 되돌린다(같은 빌드 A/B)
                    foreach (var s in System.Linq.Enumerable.ToList(grid.Styles)) { if (PdCellTemplate.IsOurs(s)) { grid.Styles.Remove(s); } }
                    applied.Add(part);
                    break;
                case "latefit" when grid is PdDataGrid pd: // A7 대조 — K5 별 열 판정을 첫 Measure 전에 하지 않고 첫 배치 뒤(SizeChanged)에만 한다(이전 동작)
                    pd.EarlyStarFit = false;
                    applied.Add(part);
                    break;
                case "litecell": // Q4 수정안 2 — 셀 템플릿을 가벼운 판으로 교체(셀 단위 포커스·현재 셀·오류 테두리 없음). 기본값 불변. 표 자신의 기본 셀 템플릿보다 뒤에 걸어야 이긴다
                    grid.Styles.Add(new Avalonia.Styling.Style(x => x.OfType<DataGridCell>())
                    {
                        Setters = { new Avalonia.Styling.Setter(TemplatedControl.TemplateProperty, LiteCellTemplate()) },
                    });
                    applied.Add(part);
                    break;
                case "noround": // 빈 칸 결함 W1 — 그리드만 레이아웃 반올림 끔(비정수 배율 '유령 가로 범위' 제거 시험, 기본값 불변). 실행 중 전환은 원격 grid.rounding
                    grid.UseLayoutRounding = false;
                    applied.Add(part);
                    break;
                case "litetheme": // A16 대조 — Fluent 행·셀 ControlTheme 대신 최소 테마(중첩 스타일·$parent 바인딩·DynamicResource 없음). 겉모양 일부 다름, 비용 상한 측정용
                    grid.CellTheme = new ControlTheme(typeof(DataGridCell))
                    {
                        Setters =
                        {
                            new Setter(TemplatedControl.TemplateProperty, PdCellTemplate.NoVerticalLine),
                            new Setter(ContentControl.HorizontalContentAlignmentProperty, Avalonia.Layout.HorizontalAlignment.Stretch),
                            new Setter(ContentControl.VerticalContentAlignmentProperty, Avalonia.Layout.VerticalAlignment.Stretch),
                            new Setter(Layoutable.MinHeightProperty, 32.0),
                        },
                    };
                    grid.RowTheme = new ControlTheme(typeof(DataGridRow))
                    {
                        Setters = { new Setter(TemplatedControl.TemplateProperty, PdRowTemplate.Instance) },
                    };
                    applied.Add(part);
                    break;
                case "fullcell" when grid is PdDataGrid pdc: // A16 대조 — 단일 요소 셀(PdCellChrome)을 끄고 이전 판(Border·Panel·ContentPresenter)으로
                    pdc.SetCompactCells(false);
                    applied.Add(part);
                    break;
                case "tbcell" when grid is PdDataGrid pdt: // K15 (b) 대조 — 그리는 셀 글자를 끄고 셀마다 TextBlock(이전 판)
                    pdt.DisableDrawnCellText();
                    applied.Add(part);
                    break;
                case "notext": // K15 (b) 대조 — 셀 TextBlock 을 만들지 않는다(글자 없음). 셀 글자 요소 비용 상한 측정용
                    PdTextColumn.DiagNoText = true;
                    applied.Add(part);
                    break;
                case "fewcols" when grid.Columns.Count > 6: // A16 대조 — 화면 밖 열 4개를 빼 숨은 셀(행마다 생성·스타일 해석, 템플릿 없음) 비용을 잰다. 겉모양 다름
                    while (grid.Columns.Count > 6) { grid.Columns.RemoveAt(grid.Columns.Count - 1); }
                    applied.Add(part);
                    break;
                case "fixedname" when grid.Columns.Count > 2:
                    grid.Columns[2].Width = new DataGridLength(220); // 생성자의 GridColumnScaler 가 이 값을 14pt 기준 폭으로 읽는다
                    applied.Add(part);
                    break;
            }
        }

        PerfProbe.Current.SetText("grid_open_mode", applied.Count == 0 ? "default" : string.Join(",", applied));
    }

    /// <summary>
    /// litecell 셀 템플릿: Border > Grid(*,Auto) > ContentPresenter + 오른쪽 세로선(PART_RightGridLine, 색은 DataGrid 코드가 넣는다).
    /// Fluent 원본의 CurrencyVisual·FocusVisual(Rectangle 2)·InvalidVisualElement 를 뺀다 — 셀마다 걸리던 DynamicResource 구독이 템플릿에서 0개가 된다.
    /// 값은 모두 셀 속성 관찰(템플릿 우선순위)로 받는다.
    /// </summary>
    private static int s_liteCellsBuilt; // 실제 적용 확인: 가벼운 템플릿으로 만든 셀 수(perf metric grid_lite_cells)

    private static FuncControlTemplate<DataGridCell> LiteCellTemplate() => new((cell, scope) =>
    {
        PerfProbe.Current.SetMetricMax("grid_lite_cells", ++s_liteCellsBuilt);
        var presenter = new ContentPresenter();
        presenter.Bind(ContentPresenter.ContentProperty, cell.GetObservable(ContentControl.ContentProperty), BindingPriority.Template);
        presenter.Bind(ContentPresenter.ContentTemplateProperty, cell.GetObservable(ContentControl.ContentTemplateProperty), BindingPriority.Template);
        presenter.Bind(Layoutable.MarginProperty, cell.GetObservable(TemplatedControl.PaddingProperty), BindingPriority.Template);
        presenter.Bind(ContentPresenter.HorizontalContentAlignmentProperty, cell.GetObservable(ContentControl.HorizontalContentAlignmentProperty), BindingPriority.Template);
        presenter.Bind(ContentPresenter.VerticalContentAlignmentProperty, cell.GetObservable(ContentControl.VerticalContentAlignmentProperty), BindingPriority.Template);
        presenter.Bind(ContentPresenter.ForegroundProperty, cell.GetObservable(TemplatedControl.ForegroundProperty), BindingPriority.Template);
        var line = new Avalonia.Controls.Shapes.Rectangle { Width = 1, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch };
        Grid.SetColumn(line, 1);
        scope.Register("PART_RightGridLine", line);
        var root = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Children = { presenter, line } };
        var border = new Border { Child = root };
        border.Bind(Border.BackgroundProperty, cell.GetObservable(TemplatedControl.BackgroundProperty), BindingPriority.Template);
        border.Bind(Border.BorderBrushProperty, cell.GetObservable(TemplatedControl.BorderBrushProperty), BindingPriority.Template);
        border.Bind(Border.BorderThicknessProperty, cell.GetObservable(TemplatedControl.BorderThicknessProperty), BindingPriority.Template);
        return border;
    });
#endif
}
