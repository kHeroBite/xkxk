// 표 셀 템플릿 — Fluent DataGridCell 과 같은 모양을 내되 평소 숨어 있는 현재 셀·포커스·오류 표시를 그 상태가 될 때만 만든다(A7 그리드 열기 비용 감소)
using System.Collections.Specialized;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Styling;

namespace xkxk.frameworks.ui.views.Documents;

/// <summary>
/// Fluent DataGridCell 템플릿(Avalonia.Controls.DataGrid 12.1.2 디컴파일 XamlClosure_1.Build_56)은 셀마다 요소 9개와 DynamicResource 5개를 만든다:
/// Border#CellBorder > Grid#PART_CellRoot(*,Auto) > Rectangle#CurrencyVisual · Grid#FocusVisual(Rectangle 2) · ContentPresenter · Rectangle#InvalidVisualElement · Rectangle#PART_RightGridLine.
/// 이 중 CurrencyVisual·FocusVisual·InvalidVisualElement 는 기본 숨김이고 :current·:focus·:invalid 셀(보통 1개 이하)에서만 보인다.
/// 이 템플릿은 상시 요소(테두리·뿌리 격자·내용·오른쪽 세로선)만 만들고, 숨은 세 표시는 셀 의사 클래스가 처음 켜질 때 같은 값으로 만들어 같은 자리에 넣는다.
/// 모양·순서·색 자원 키는 원본과 같다. 세로선 색은 DataGrid 가 VerticalGridLinesBrush 로 넣는다(DataGridCell.EnsureGridLine).
/// 근거: headless 진단판 litecell 대조(숨은 표시를 아예 뺀 초안)에서 첫 그리드 열기 중앙값 526→425ms, 첫 Measure 330→262ms. 이 판은 표시를 유지한다.
/// </summary>
internal static class PdCellTemplate
{
    /// <summary>셀 템플릿(모든 표가 공유 — Build 는 셀마다 새 요소를 만든다).</summary>
    public static readonly FuncControlTemplate<DataGridCell> Instance = new((cell, scope) => Build(cell, scope, verticalLine: true));

    /// <summary>
    /// 세로선을 쓰지 않는 표(GridLinesVisibility = None·Horizontal)용 판(A16). 오른쪽 세로선 Rectangle 은 그런 표에서 늘 숨김이고
    /// (DataGridCell.EnsureGridLine 이 Vertical·All 일 때만 보임, 선이 없으면 null 검사로 건너뜀 — 12.1.2 디컴파일 확인), 2열 Grid 는 그 선 때문에만 필요했다.
    /// 그래서 셀마다 Rectangle 1개를 빼고 뿌리를 Panel 로 둔다. 보이는 모양은 같다. 첫 그리드 열기 비용은 보이는 행 × 전체 열(열 가상화 없음) 셀 요소 수에 비례한다
    /// (a16diag: 첫 측정 308ms 중 행 생성 262ms — 요소 부착·스타일 매칭·템플릿).
    /// </summary>
    public static readonly FuncControlTemplate<DataGridCell> NoVerticalLine = new((cell, scope) => Build(cell, scope, verticalLine: false));

    /// <summary>
    /// 세로선 없는 표의 단일 요소 판(A16, 옵션 GridCompactCells). 뿌리가 PdCellChrome 하나라 셀 요소가 Border·Panel·ContentPresenter·내용 4개에서 Chrome·내용 2개로 준다.
    /// 값(바탕·테두리·여백·정렬)과 지연 표시 3종은 NoVerticalLine 판과 같다. 값은 TemplateBinding 대신 PdCellChrome.AttachCell 의 구독 1개로 받는다(K15). DataGridCell 이 템플릿에서 찾는 부품은 PART_RightGridLine 뿐이고 없으면 건너뛴다(12.1.2 디컴파일 OnApplyTemplate).
    /// </summary>
    public static readonly FuncControlTemplate<DataGridCell> Compact = new((cell, scope) => Build(cell, scope, verticalLine: false, compact: true));

    private static readonly IBrush TransparentWhite = new ImmutableSolidColorBrush(0x00FFFFFFu); // 원본 Fill="#00FFFFFF"
    private static readonly ConditionalWeakTable<DataGridCell, Overlays> s_overlays = new();

    /// <summary>
    /// 표 하나의 Styles 에 넣을 스타일(표마다 새 Style — Style 은 소유 컬렉션이 하나다). verticalLine=false 면 세로선 없는 판,
    /// 그중 compact=true 면 단일 요소 판(세로선 있는 표는 compact 를 무시한다 — 세로선은 원본 템플릿의 PART_RightGridLine 이 맡는다).
    /// </summary>
    public static Style CreateStyle(bool verticalLine = true, bool compact = false) => new(x => x.OfType<DataGridCell>())
    {
        Setters = { new Setter(TemplatedControl.TemplateProperty, verticalLine ? Instance : compact ? Compact : NoVerticalLine) },
    };

    /// <summary>이 스타일이 PdCellTemplate 을 거는 스타일인가(진단 대조 스위치가 걷어낼 때). 세 판 모두 해당한다.</summary>
    public static bool IsOurs(IStyle style) =>
        style is Style s && s.Setters.Count == 1 && s.Setters[0] is Setter { Value: var v }
        && (ReferenceEquals(v, Instance) || ReferenceEquals(v, NoVerticalLine) || ReferenceEquals(v, Compact));

    /// <summary>우리 스타일이 세로선 있는 판인가(PdDataGrid 가 판을 바꿀지 판단).</summary>
    public static bool HasVerticalLine(IStyle style) =>
        style is Style s && s.Setters.Count == 1 && s.Setters[0] is Setter { Value: var v } && ReferenceEquals(v, Instance);

    /// <summary>우리 스타일이 단일 요소 판인가(PdDataGrid 가 옵션과 판이 맞는지 판단).</summary>
    public static bool IsCompact(IStyle style) =>
        style is Style s && s.Setters.Count == 1 && s.Setters[0] is Setter { Value: var v } && ReferenceEquals(v, Compact);

    private static Control Build(DataGridCell cell, INameScope scope, bool verticalLine, bool compact = false)
    {
#if PD_DIAGNOSTICS
        var t = GridOpenBreakdown.Start(); // A16 첫 측정 분해(진단 빌드만)
        GridOpenBreakdown.EnterCellTemplate();
        try { return compact ? BuildCompact(cell, scope) : BuildCore(cell, scope, verticalLine); }
        finally
        {
            GridOpenBreakdown.LeaveCellTemplate();
            GridOpenBreakdown.Add(GridOpenBreakdown.Part.CellTemplate, t);
            GridOpenBreakdown.MarkBuildEnd(); // K15: Build 끝 → 뿌리 측정 시작(템플릿 부착) 구간
        }
#else
        return compact ? BuildCompact(cell, scope) : BuildCore(cell, scope, verticalLine);
#endif
    }

    /// <summary>단일 요소 판: PdCellChrome 이 원본 판의 Border(바탕·테두리)·ContentPresenter(내용·여백·정렬) 값을 같은 TemplateBinding 으로 받는다.</summary>
    private static Control BuildCompact(DataGridCell cell, INameScope scope)
    {
        var chrome = new PdCellChrome { Name = "CellBorder" };
        scope.Register("CellBorder", chrome);
        var overlays = new Overlays(cell, chrome);
        if (s_overlays.TryGetValue(cell, out var old)) { old.Detach(); } // 템플릿이 다시 적용되면 옛 구독을 끊는다
        s_overlays.AddOrUpdate(cell, overlays);
        overlays.Sync();
        PdTextColumn.RealizeDeferred(cell); // A16: 화면 밖이라 내용을 미뤘던 셀이 처음 화면에 들어왔다
        chrome.AttachCell(cell); // 셀 값 7개를 구독 1개로 받는다(TemplateBinding 7개 대신, K15). 미룬 내용을 채운 뒤 연결해 내용 교체 알림 1회 절약
        return chrome;
    }

    private static Control BuildCore(DataGridCell cell, INameScope scope, bool verticalLine)
    {
        var presenter = new ContentPresenter();
        Tb(presenter, Layoutable.MarginProperty, TemplatedControl.PaddingProperty);
        Tb(presenter, Layoutable.HorizontalAlignmentProperty, ContentControl.HorizontalContentAlignmentProperty);
        Tb(presenter, Layoutable.VerticalAlignmentProperty, ContentControl.VerticalContentAlignmentProperty);
        Tb(presenter, ContentPresenter.ContentProperty, ContentControl.ContentProperty);
        Tb(presenter, ContentPresenter.ContentTemplateProperty, ContentControl.ContentTemplateProperty);
        Tb(presenter, ContentPresenter.ForegroundProperty, TemplatedControl.ForegroundProperty);
        Panel root;
        Rectangle? line = null;
        if (verticalLine)
        {
            line = new Rectangle { Name = "PART_RightGridLine", Width = 1, VerticalAlignment = VerticalAlignment.Stretch };
            Grid.SetColumn(line, 1);
            root = new Grid { Name = "PART_CellRoot", ColumnDefinitions = new ColumnDefinitions("*,Auto"), Children = { presenter, line } };
        }
        else
        {
            root = new Panel { Name = "PART_CellRoot", Children = { presenter } }; // 세로선 열이 없으면 2열 Grid 와 같은 배치(내용·표시가 셀 전체)
        }

        var border = new Border { Name = "CellBorder", Child = root };
        Tb(border, Border.BackgroundProperty, TemplatedControl.BackgroundProperty);
        Tb(border, Border.BorderBrushProperty, TemplatedControl.BorderBrushProperty);
        Tb(border, Border.BorderThicknessProperty, TemplatedControl.BorderThicknessProperty);
        Tb(border, Border.CornerRadiusProperty, TemplatedControl.CornerRadiusProperty);
        scope.Register("CellBorder", border);
        scope.Register("PART_CellRoot", root);
        if (line is not null) { scope.Register("PART_RightGridLine", line); }
        var overlays = new Overlays(cell, root, presenter);
        if (s_overlays.TryGetValue(cell, out var old)) { old.Detach(); } // 템플릿이 다시 적용되면 옛 구독을 끊는다
        s_overlays.AddOrUpdate(cell, overlays);
        overlays.Sync();
        PdTextColumn.RealizeDeferred(cell); // A16: 화면 밖이라 내용을 미뤘던 셀이 처음 화면에 들어왔다(템플릿은 보이는 셀에만 적용된다)
        return border;
    }

    // 원본 XAML 과 같은 TemplateBinding(셀 = 템플릿 부모). GetObservable 바인딩은 셀마다 10개라 A/B 에서 원본보다 느렸다(11-11 headless doc 541 대 485ms)
    private static void Tb<T>(AvaloniaObject target, StyledProperty<T> prop, StyledProperty<T> source) =>
        target.Bind(prop, new TemplateBinding(source));

    private static Rectangle Frame(string? name, string brushKey, double stroke, Thickness margin = default)
    {
        var r = new Rectangle
        {
            Name = name, Margin = margin, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch,
            Fill = TransparentWhite, IsHitTestVisible = false, StrokeThickness = stroke,
        };
        r.Bind(Shape.StrokeProperty, r.GetResourceObservable(brushKey), BindingPriority.Template); // 원본과 같은 DynamicResource 키·Template 우선순위(테마 전환을 따라간다)
        return r;
    }

    /// <summary>셀 1개의 지연 표시 3종. 의사 클래스 변경(Classes 목록 알림)마다 보이기만 맞추고, 처음 켜질 때 만든다.</summary>
    private sealed class Overlays
    {
        private readonly DataGridCell _cell;
        private readonly Panel? _root; // 세로선 판은 Grid, 세로선 없는 판은 Panel(단일 요소 판은 null)
        private readonly ContentPresenter? _presenter;
        private readonly PdCellChrome? _chrome; // 단일 요소 판 뿌리
        private Rectangle? _currency;
        private Grid? _focus;
        private Rectangle? _invalid;

        public Overlays(DataGridCell cell, Panel root, ContentPresenter presenter)
        {
            _cell = cell;
            _root = root;
            _presenter = presenter;
            cell.Classes.CollectionChanged += OnClassesChanged;
        }

        public Overlays(DataGridCell cell, PdCellChrome chrome)
        {
            _cell = cell;
            _chrome = chrome;
            cell.Classes.CollectionChanged += OnClassesChanged;
        }

        public void Detach()
        {
            _cell.Classes.CollectionChanged -= OnClassesChanged;
            _chrome?.DetachCell(); // 단일 요소 판: 옛 뿌리의 셀 값 구독도 끊는다
        }

        private void OnClassesChanged(object? sender, NotifyCollectionChangedEventArgs e) => Sync();

        public void Sync()
        {
            var classes = _cell.Classes;
            var current = classes.Contains(":current");
            var focus = classes.Contains(":focus");
            var invalid = classes.Contains(":invalid");
            if (current && _currency is null)
            {
                _currency = Frame("CurrencyVisual", "DataGridCurrencyVisualPrimaryBrush", 1);
                if (_chrome is not null) { _chrome.AddUnder(_currency, atStart: true); }
                else { _root!.Children.Insert(0, _currency); } // 원본 순서: 내용보다 먼저(아래에) 그린다
            }

            if (focus && _focus is null)
            {
                _focus = new Grid
                {
                    Name = "FocusVisual", IsHitTestVisible = false,
                    Children = { Frame(null, "DataGridCellFocusVisualPrimaryBrush", 2), Frame(null, "DataGridCellFocusVisualSecondaryBrush", 1, new Thickness(2)) },
                };
                if (_chrome is not null) { _chrome.AddUnder(_focus, atStart: false); }
                else { _root!.Children.Insert(_currency is null ? 0 : 1, _focus); } // CurrencyVisual 다음, 내용 앞
            }

            if (invalid && _invalid is null)
            {
                _invalid = Frame("InvalidVisualElement", "DataGridCellInvalidBrush", 1);
                if (_chrome is not null) { _chrome.AddOver(_invalid); }
                else { _root!.Children.Insert(_root.Children.IndexOf(_presenter!) + 1, _invalid); } // 내용 위, 세로선 앞
            }

            if (_currency is not null) { _currency.IsVisible = current; }
            if (_focus is not null) { _focus.IsVisible = focus; }
            if (_invalid is not null) { _invalid.IsVisible = invalid; }
        }
    }
}
