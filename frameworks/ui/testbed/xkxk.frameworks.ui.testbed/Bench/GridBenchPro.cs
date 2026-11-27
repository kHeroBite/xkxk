// 그리드 벤치의 ProDataGrid 전용 기능(우측 고정 열·조건부 서식·cell_style 강조) — 내장 DataGrid 빌드에서는 "미지원" 결과만 돌려준다
using xkxk.frameworks.ui.shell;
using System;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Avalonia.Controls;
#if PD_GRID_PRO
using Avalonia.Controls.DataGridConditionalFormatting;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Styling;
#endif

namespace xkxk.frameworks.ui.testbed.Bench;

/// <summary>
/// GridBenchView 가 필드 초기화자로 보유하는 Pro 전용 기능 객체. 생성자는 인자가 없고 뷰·그리드는 메서드 인자로 받는다
/// (필드 초기화자는 base 생성자보다 먼저 실행되므로 InitializeComponent 중 호출에도 null 이 아니다).
/// </summary>
internal sealed class GridBenchPro
{
#if PD_GRID_PRO
    private const string LateValue = "지연";

    // 강조 색 — 조건부 서식 테마와 cell_style 템플릿이 같은 브러시 인스턴스를 쓴다(강조 판정을 참조 비교로 하기 위함)
    private readonly IBrush _lateBg = new SolidColorBrush(Color.Parse("#33E74C3C"));
    private readonly IBrush _lateFg = new SolidColorBrush(Color.Parse("#C0392B"));
    private ConditionalFormattingModel? _proCf;
    private ConditionalFormattingModel? _emptyCf;
    private ControlTheme? _lateTheme;
    private Avalonia.Controls.Templates.IDataTemplate? _statusTplOrig;
    private Avalonia.Controls.Templates.IDataTemplate? _statusTplStyle;

    private static DataGridTemplateColumn StatusColumn(DataGrid grid) => (DataGridTemplateColumn)grid.Columns[6]; // GridBenchView.axaml 7번째 열(상태)

    /// <summary>우측 고정 1열(비고), 상태=지연 셀 강조(조건부 서식 모델).</summary>
    internal void ApplyProFeatures(Control view, DataGrid grid)
    {
        grid.FrozenColumnCountRight = 1;
        var baseTheme = view.TryFindResource(typeof(DataGridCell), out var t) ? t as ControlTheme : null;
        var late = new ControlTheme(typeof(DataGridCell)) { BasedOn = baseTheme };
        late.Setters.Add(new Setter(TemplatedControl.BackgroundProperty, _lateBg));
        late.Setters.Add(new Setter(TemplatedControl.ForegroundProperty, _lateFg));
        _lateTheme = late;
        var model = new ConditionalFormattingModel();
        model.Apply(new[]
        {
            new ConditionalFormattingDescriptor(
                ruleId: "late",
                @operator: ConditionalFormattingOperator.Equals,
                columnId: grid.Columns[6], // 상태 열(XAML 열은 x:Name 을 받지 않아 순서로 찾는다 — GridBenchView.axaml 7번째)
                propertyPath: nameof(BenchRow.Status),
                value: "지연",
                theme: late,
                target: ConditionalFormattingTarget.Cell,
                valueSource: ConditionalFormattingValueSource.Item),
        });
        grid.ConditionalFormattingModel = model;
        _proCf = model;
    }

    /// <summary>
    /// cell_style 모드의 상태 열 셀 템플릿. 조건부 서식 모델 대신 셀 내용(Border+TextBlock)이 행 값을 보고 직접 강조한다.
    /// 바인딩 대신 DataContextChanged 로 값을 반영하고, 실행 중 Status 변경(SetStatus)은 행의 PropertyChanged(Status)를 구독해 따라간다.
    /// 구독은 셀이 화면 트리에 붙어 있는 동안만 둔다(떨어지면 해제 — 행이 셀을 붙잡아 누수되지 않게).
    /// </summary>
    private Avalonia.Controls.Templates.IDataTemplate StatusStyleTemplate() =>
        _statusTplStyle ??= new Avalonia.Controls.Templates.FuncDataTemplate<BenchRow>((_, _) =>
        {
            var tb = new TextBlock();
            tb.Classes.Add("cell");
            var host = new Border { Child = tb };
            BenchRow? sub = null;
            var attached = false; // 화면 트리 부착 여부(부착 이벤트로 직접 추적 — 템플릿은 부착 전에 만들어진다)
            void Apply()
            {
                var r = host.DataContext as BenchRow;
                tb.Text = r?.Status;
                if (r is not null && r.Status == LateValue)
                {
                    host.Background = _lateBg;
                    tb.Foreground = _lateFg;
                }
                else
                {
                    host.ClearValue(Border.BackgroundProperty);
                    tb.ClearValue(TextBlock.ForegroundProperty);
                }
            }
            void OnRowChanged(object? _, System.ComponentModel.PropertyChangedEventArgs e)
            {
                if (e.PropertyName == nameof(BenchRow.Status)) { Apply(); }
            }
            void Unsubscribe()
            {
                if (sub is not null) { sub.PropertyChanged -= OnRowChanged; sub = null; }
            }
            void Rebind()
            {
                Unsubscribe();
                if (attached && host.DataContext is BenchRow r) { sub = r; r.PropertyChanged += OnRowChanged; }
                Apply();
            }
            host.DataContextChanged += (_, _) => Rebind();
            host.AttachedToVisualTree += (_, _) => { attached = true; Rebind(); };
            host.DetachedFromVisualTree += (_, _) => { attached = false; Unsubscribe(); };
            return host;
        }, supportsRecycling: true);

    internal JsonObject LiveHighlight(DataGrid grid, GridBenchModel? model)
    {
        var rules = grid.ConditionalFormattingModel?.Descriptors.Count ?? 0;
        var cellStyle = _statusTplStyle is not null && ReferenceEquals(StatusColumn(grid).CellTemplate, _statusTplStyle);
        var o = GridBenchHighlight.Check(grid, rules > 0 || cellStyle, model, _lateBg, _lateTheme);
        o["method"] = cellStyle ? "cell_style" : rules > 0 ? "cf" : "none";
        return o;
    }

    /// <summary>
    /// Pro 전용 기능 끄기/켜기(원인 대조용). off = 조건부 서식 모델 해제 + 우측 고정 0, on = 생성 때 상태(모델 다시 연결 + 우측 고정 1).
    /// Android Pro 실시간 갱신 붕괴가 이 두 기능 비용인지 ProDataGrid 갱신 경로인지 가르려고 둔다.
    /// cf_only = 조건부 서식만 켬(우측 고정 0), frozen_only = 우측 고정 1만 켬(빈 모델), cell_style = 모델 없이 상태 열 셀 템플릿이 직접 강조.
    /// </summary>
    internal async Task<JsonObject> ProFeaturesAsync(DataGrid grid, Func<TopLevel> top, GridBenchModel? model, string mode)
    {
        var off = string.Equals(mode, "off", StringComparison.OrdinalIgnoreCase);
        var cfOnly = string.Equals(mode, "cf_only", StringComparison.OrdinalIgnoreCase);
        var frozenOnly = string.Equals(mode, "frozen_only", StringComparison.OrdinalIgnoreCase);
        var cellStyle = string.Equals(mode, "cell_style", StringComparison.OrdinalIgnoreCase);
        var on = !off && !cfOnly && !frozenOnly && !cellStyle; // 그 밖의 값은 기존처럼 on 으로 본다
        var useCf = on || cfOnly;
        var useFrozen = on || frozenOnly;
        // 끌 때는 null 대신 규칙 0개 모델을 명시로 넣는다 — null 을 넣으면 그리드가 기본 모델을 스스로 붙여 상태가 불명확했다(i1500gp)
        grid.ConditionalFormattingModel = useCf ? _proCf! : (_emptyCf ??= new ConditionalFormattingModel());
        grid.FrozenColumnCountRight = useFrozen ? 1 : 0;
        // cell_style = 조건부 서식 모델 없이(빈 모델), 우측 고정 0, 상태 열 셀 템플릿이 직접 강조. 다른 모드로 가면 원래 템플릿으로 되돌린다
        var column = StatusColumn(grid);
        _statusTplOrig ??= column.CellTemplate;
        var wantTpl = cellStyle ? StatusStyleTemplate() : _statusTplOrig;
        if (!ReferenceEquals(column.CellTemplate, wantTpl)) { column.CellTemplate = wantTpl; }
        var showMs = await BenchProbe.TimeToFramesAsync(top(), () => { });
        // "켜짐"은 모델 존재가 아니라 규칙 수로 판정한다(모델은 off 에서도 빈 모델로 붙어 있다)
        var cf = grid.ConditionalFormattingModel;
        var rules = cf?.Descriptors.Count ?? 0;
        return new JsonObject
        {
            ["supported"] = true,
            ["mode"] = on ? "on" : off ? "off" : cfOnly ? "cf_only" : frozenOnly ? "frozen_only" : "cell_style",
            ["conditional_formatting"] = rules > 0,
            ["cf_rules"] = rules,
            ["cf_model"] = cf is null ? "null" : ReferenceEquals(cf, _proCf) ? "bench" : ReferenceEquals(cf, _emptyCf) ? "empty" : cf.GetType().Name,
            ["frozen_right"] = grid.FrozenColumnCountRight,
            ["cell_style"] = ReferenceEquals(column.CellTemplate, _statusTplStyle) && _statusTplStyle is not null,
            ["to_frame_ms"] = Math.Round(showMs, 1),
            // 강조 셀 대조(화면에 실현된 상태 열 셀 기준). 규칙이 켜진 모드는 on·cf_only·cell_style 이다
            ["highlight"] = GridBenchHighlight.Check(grid, on || cfOnly || cellStyle, model, _lateBg, _lateTheme),
        };
    }

    internal int? SetFrozenRight(DataGrid grid, int right)
    {
        if (right >= 0) { grid.FrozenColumnCountRight = right; }
        return grid.FrozenColumnCountRight;
    }
#else
    internal void ApplyProFeatures(Control view, DataGrid grid)
    {
        _ = view;
        _ = grid; // 내장 DataGrid 빌드에는 Pro 전용 기능이 없다
    }

    internal int? SetFrozenRight(DataGrid grid, int right)
    {
        _ = grid;
        _ = right;
        return null; // 내장 DataGrid 에는 오른쪽 고정 열이 없다
    }

    internal Task<JsonObject> ProFeaturesAsync(DataGrid grid, Func<TopLevel> top, GridBenchModel? model, string mode)
    {
        _ = grid;
        _ = top;
        _ = model;
        return Task.FromResult(new JsonObject { ["supported"] = false, ["mode"] = mode, ["reason"] = "내장 DataGrid 빌드에는 Pro 전용 기능(조건부 서식·우측 고정)이 없다" });
    }

    internal JsonObject LiveHighlight(DataGrid grid, GridBenchModel? model)
    {
        _ = grid;
        _ = model;
        return new() { ["supported"] = false, ["reason"] = "내장 DataGrid 빌드에는 강조(조건부 서식·cell_style)가 없다" };
    }
#endif
}
