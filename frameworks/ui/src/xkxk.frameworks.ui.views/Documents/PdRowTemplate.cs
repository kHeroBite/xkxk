// 표 행 템플릿 — Fluent DataGridRow 와 같은 모양을 내되 행 머리글·상세 표시기를 빼고 오류 표시는 그 상태가 될 때만 만든다(A16 그리드 열기 비용 감소)
using System.Collections.Specialized;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Rendering;
using Avalonia.Styling;

namespace xkxk.frameworks.ui.views.Documents;

/// <summary>
/// Fluent DataGridRow 템플릿(Avalonia.Controls.DataGrid 12.1.2 디컴파일 XamlClosure_1.Build_64)은 행마다 요소 7개를 만든다:
/// Border#RowBorder > DataGridFrozenGrid#PART_Root(Auto,* / *,Auto,Auto) > Rectangle#BackgroundRectangle · Rectangle#InvalidVisualElement ·
/// DataGridRowHeader#PART_RowHeader · DataGridCellsPresenter#PART_CellsPresenter · DataGridDetailsPresenter#PART_DetailsPresenter · Rectangle#PART_BottomGridLine.
/// 이 중 행 머리글은 머리글을 끈 표(HeadersVisibility 에 Row 없음)에서 늘 숨김이고, 상세 표시기는 RowDetailsTemplate 이 없는 표에서 늘 비어 있다.
/// 오류 표시는 기본 Opacity 0 이고 :invalid 행에서만 보인다. DataGridRow 는 세 부품이 없으면 null 검사로 건너뛴다(12.1.2 디컴파일 OnApplyTemplate·
/// EnsureHeaderVisibility·DataGrid 행 머리글 폭 계산·DataGridRowsPresenter 확인).
/// 그래서 이 템플릿은 상시 요소(테두리·뿌리 격자·바탕 사각형·셀 표시기·아래 구분선)만 만들고, 오류 표시는 행 의사 클래스 :invalid 가 처음 켜질 때 같은 값으로 같은 자리에 넣는다.
/// 이름·격자 위치·색 자원 키는 원본과 같아 Fluent ControlTheme 의 /template/ 선택자(바탕 사각형 hover·선택)와 PdDataGridStyles(RowBorder·BackgroundRectangle)가 그대로 걸린다.
/// 행 머리글이나 행 상세를 쓰는 표에는 걸지 않는다(PdDataGrid.SwapRowStyle).
/// </summary>
internal static class PdRowTemplate
{
    /// <summary>행 템플릿(모든 표가 공유 — Build 는 행마다 새 요소를 만든다).</summary>
    public static readonly FuncControlTemplate<DataGridRow> Instance = new(Build);

    private static readonly ConditionalWeakTable<DataGridRow, InvalidOverlay> s_overlays = new();

    /// <summary>표 하나의 Styles 에 넣을 스타일(표마다 새 Style — Style 은 소유 컬렉션이 하나다).</summary>
    public static Style CreateStyle() => new(x => x.OfType<DataGridRow>())
    {
        Setters = { new Setter(TemplatedControl.TemplateProperty, Instance) },
    };

    /// <summary>이 스타일이 PdRowTemplate 을 거는 스타일인가(진단 대조 스위치가 걷어낼 때).</summary>
    public static bool IsOurs(IStyle style) =>
        style is Style s && s.Setters.Count == 1 && s.Setters[0] is Setter { Value: var v } && ReferenceEquals(v, Instance);

    /// <summary>이 표에 경량 행 템플릿을 걸 수 있는가 — 행 머리글을 끄고 행 상세 템플릿이 없는 표만.</summary>
    public static bool Fits(DataGrid grid) =>
        (grid.HeadersVisibility & DataGridHeadersVisibility.Row) == 0 && grid.RowDetailsTemplate is null;

    private static Control Build(DataGridRow row, INameScope scope)
    {
#if PD_DIAGNOSTICS
        var t = GridOpenBreakdown.Start(); // A16 첫 측정 분해(진단 빌드만)
        try { return BuildCore(row, scope); }
        finally { GridOpenBreakdown.Add(GridOpenBreakdown.Part.RowTemplate, t); }
#else
        return BuildCore(row, scope);
#endif
    }

    private static Control BuildCore(DataGridRow row, INameScope scope)
    {
        var background = new Rectangle { Name = "BackgroundRectangle" };
        Grid.SetRowSpan(background, 2);
        Grid.SetColumnSpan(background, 2);
        // 원본과 같은 DynamicResource 키·Template 우선순위 — hover·선택 스타일(StyleTrigger)이 이 값을 덮는다.
        // 다만 완전 투명 붓(Fluent 기본 SystemControlTransparentBrush)은 null 로 바꾼다(A15): Avalonia 는 null 붓만 그리기를 건너뛰고
        // 투명 붓은 행마다 DrawGeometry 를 기록한다(12.1.4 디컴파일 RenderDataDrawingContext·Shape.Render 확인). 보이는 결과는 같다.
        // 행 hit-test 는 행 바탕(RowBorder 배경, OuterBorderEdge)과 셀 바탕이 맡으므로 이 사각형이 비어도 hover 판정은 그대로다.
        background.Bind(Shape.FillProperty, background.GetResourceObservable("DataGridRowBackgroundBrush", DropTransparent), BindingPriority.Template);

        var cells = new DataGridCellsPresenter { Name = "PART_CellsPresenter" };
        Grid.SetColumn(cells, 1);
        cells.SetValue(DataGridFrozenGrid.IsFrozenProperty, true, BindingPriority.Template); // 원본과 같이 가로 스크롤 때 RenderTransform 대상

        var bottomLine = new Rectangle { Name = "PART_BottomGridLine", Height = 1, HorizontalAlignment = HorizontalAlignment.Stretch };
        Grid.SetRow(bottomLine, 2);
        Grid.SetColumn(bottomLine, 1);

        var root = new DataGridFrozenGrid
        {
            Name = "PART_Root",
            ColumnDefinitions = new ColumnDefinitions("Auto,*"), // 원본과 같은 격자(0열은 행 머리글 자리 — 비어 있어 폭 0)
            RowDefinitions = new RowDefinitions("*,Auto,Auto"),
            Children = { background, cells, bottomLine },
        };

        var border = new RowHitBorder { Name = "RowBorder", Child = root };
        Tb(border, Border.BackgroundProperty, TemplatedControl.BackgroundProperty);
        Tb(border, Border.BorderBrushProperty, TemplatedControl.BorderBrushProperty);
        Tb(border, Border.BorderThicknessProperty, TemplatedControl.BorderThicknessProperty);
        Tb(border, Border.CornerRadiusProperty, TemplatedControl.CornerRadiusProperty);

        scope.Register("RowBorder", border);
        scope.Register("PART_Root", root);
        scope.Register("BackgroundRectangle", background);
        scope.Register("PART_CellsPresenter", cells);
        scope.Register("PART_BottomGridLine", bottomLine);

        var overlay = new InvalidOverlay(row, root);
        if (s_overlays.TryGetValue(row, out var old)) { old.Detach(); } // 템플릿이 다시 적용되면 옛 구독을 끊는다
        s_overlays.AddOrUpdate(row, overlay);
        overlay.Sync();
        return border;
    }

    /// <summary>
    /// 행 뿌리 Border — 바탕 붓이 없어도 자기 사각형 전체로 hit-test 된다(A15).
    /// 짝수 행은 표 바탕과 같은 색이라 바탕을 칠하지 않는데(PdDataGrid.RowBrushIsBackdrop), 그러면 위·아래 1px 테두리 띠에 그린 것이 없어
    /// 포인터가 그 띠에서 행을 잃고 hover 가 깜박인다. Avalonia 12.1.4 합성 hit-test 는 ICustomHitTest 를 그리기 목록보다 먼저 본다
    /// (CompositionDrawListVisual.HitTest 디컴파일 확인). StyleKey 는 Border 라 /template/ Border#RowBorder 선택자가 그대로 걸린다.
    /// </summary>
    private sealed class RowHitBorder : Border, ICustomHitTest
    {
        protected override System.Type StyleKeyOverride => typeof(Border);

        public bool HitTest(Point point) => new Rect(Bounds.Size).Contains(point);
    }

    /// <summary>완전 투명 단색 붓(알파 0)이면 null — 그리기 호출을 남기지 않는다. 그 밖의 값은 그대로.</summary>
    internal static object? DropTransparent(object? value) =>
        value is ISolidColorBrush { Color.A: 0 } ? null : value;

    // 원본 XAML 과 같은 TemplateBinding(행 = 템플릿 부모)
    private static void Tb<T>(AvaloniaObject target, StyledProperty<T> prop, StyledProperty<T> source) =>
        target.Bind(prop, new TemplateBinding(source));

    /// <summary>행 1개의 지연 오류 표시. 의사 클래스 변경(Classes 목록 알림)마다 :invalid 를 보고, 처음 켜질 때 만든다.</summary>
    private sealed class InvalidOverlay
    {
        private readonly DataGridRow _row;
        private readonly Panel _root;
        private Rectangle? _invalid;

        public InvalidOverlay(DataGridRow row, Panel root)
        {
            _row = row;
            _root = root;
            row.Classes.CollectionChanged += OnClassesChanged;
        }

        public void Detach() => _row.Classes.CollectionChanged -= OnClassesChanged;

        private void OnClassesChanged(object? sender, NotifyCollectionChangedEventArgs e) => Sync();

        public void Sync()
        {
            var invalid = _row.Classes.Contains(":invalid");
            if (invalid && _invalid is null)
            {
                // 템플릿 빌드 뒤에 넣는 요소라 /template/ 선택자(:invalid → Opacity 0.4)가 걸리지 않을 수 있다.
                // 그래서 원본이 :invalid 때 보이는 값(Opacity 0.4)을 직접 두고 보이기만 맞춘다(셀 템플릿 지연 표시와 같은 방식)
                _invalid = new Rectangle { Name = "InvalidVisualElement", Opacity = 0.4, IsHitTestVisible = false };
                Grid.SetColumnSpan(_invalid, 2);
                _invalid.Bind(Shape.FillProperty, _invalid.GetResourceObservable("DataGridRowInvalidBrush"), BindingPriority.Template);
                _root.Children.Insert(1, _invalid); // 원본 순서: 바탕 사각형 다음, 셀 표시기 앞
            }

            if (_invalid is not null) { _invalid.IsVisible = invalid; }
        }
    }
}
