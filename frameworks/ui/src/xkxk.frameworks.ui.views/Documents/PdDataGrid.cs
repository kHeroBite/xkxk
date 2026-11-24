// 프로젝트 공통 표 컨트롤 — 토큰 스타일·줄무늬·글자 비례 열 배율·터치 선택·휠 배율을 묶어 XAML 한 줄로 쓰게 하는 DataGrid 하위 클래스
using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using xkxk.frameworks.ui.shell.Localization;
using xkxk.frameworks.ui.shell.Theming;
using xkxk.frameworks.ui.Input;

namespace xkxk.frameworks.ui.views.Documents;

/// <summary>
/// 열 기준 폭을 XAML 값으로 기억해야 하므로 열 배율기는 열이 모두 채워진 뒤인 첫 부착 때 만든다(첫 측정 전이다).
/// 화면은 열 정의·데이터만 준다. 텍스트 열은 바인딩 없는 경량 열 PdTextColumn 으로 공통화할 수 있다(화면설계 공통원칙 §5.4).
/// </summary>
public class PdDataGrid : DataGrid
{
    /// <summary>본문 글자 크기(PdFontBody 토큰 연결용 내부 속성).</summary>
    private static readonly StyledProperty<double> BodySizeProperty = AvaloniaProperty.Register<PdDataGrid, double>("BodySize", 14);

    private IBrush? _stripeBrush;
    private IBrush? _rowBrush;
    private bool _stripesApplied;
    private GridColumnScaler? _columnScaler;
    private DataGridRowsPresenter? _rowsPresenter; // K5: 셀 영역 폭 기준(DataGrid.CellsWidth = 이 표시기 측정 폭 − 행 머리글 폭)

    public PdDataGrid()
    {
        var styles = new PdDataGridStyles();
        Styles.Add(styles);
        s_cellTextLook ??= PdCellTextLook.From(styles); // 셀 글자 모양(TextBlock.cell 스타일 값) — 그리는 셀 글자가 같은 값을 쓴다(axaml 이 정본, 한 번만 읽는다)
        _cellStyle = PdCellTemplate.CreateStyle(VerticalLines(GridLinesVisibility), _compactCells);
        Styles.Add(_cellStyle); // 셀 템플릿: Fluent 와 같은 모양, 숨은 현재 셀·포커스·오류 표시는 필요할 때만 생성(PdCellTemplate.cs, A7). 세로선을 안 쓰는 표는 세로선 없는 판(A16)
        SwapRowStyle(); // 행 템플릿: 행 머리글·행 상세를 안 쓰는 표는 경량 판(PdRowTemplate.cs, A16 C2)
        GridCellStrokes.HookHeaderSeparator(this, this); // 헤더 아래선 색(Q11 A1) — 템플릿 적용 때 로컬 바인딩(GridCellStrokes.cs)
        // 줄무늬(UI-07 PdBrushGridStripe): DataGrid 12.1.2 에 AlternatingRowBackground 가 없어 행 로딩(재활용 포함)마다 홀짝으로 지정.
        // 스크롤 비용을 줄이려고 행마다 리소스 바인딩을 만들지 않고, 캐시한 브러시 2개를 직접 넣는다(테마 변경 시 보이는 행만 다시 칠함).
        LoadingRow += (_, e) =>
        {
#if PD_DIAGNOSTICS
            GridOpenBreakdown.MarkLoadingRow(); // A16 첫 측정 행 단위 타임라인(진단 빌드만)
#endif
            ApplyStripe(e.Row);
        };
        new GridTouchSelection(this, this); // 처리기가 인스턴스를 붙잡는다. 터치 끌기·핀치는 선택을 바꾸지 않고 탭만 선택(D-W1, GridTouchSelection.cs)
        GridScrollCoalescer.Install(); // D-SC2 H1 우회 — 한 프레임의 스크롤 제스처를 합쳐 1번만 DataGrid 에 전달(GridScrollCoalescer.cs, 중복 호출 무시)
        ActualThemeVariantChanged += (_, _) => RefreshStripes();
        this.Bind(BodySizeProperty, this.GetResourceObservable("PdFontBody")); // 셸 글자 토큰 → 행 높이·열 너비 비례
    }

    /// <summary>테마·자동화 입장에서는 DataGrid 그대로다(선택자·ControlTheme 조회가 같은 키를 쓴다).</summary>
    protected override Type StyleKeyOverride => typeof(DataGrid);

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (_columnScaler is null)
        {
            _columnScaler = new GridColumnScaler(this); // 지금 열 너비를 14pt 기준값으로 기억(GridColumnScaler.cs)
            DenseWheel.Attach(this, new GridDenseTarget(this, _columnScaler, ApplyFontScale));
        }

        UiSettings.Current.Changed += RefreshStripes;
        UiSettings.Current.Changed += ApplyFontScale; // 폰트 종류가 바뀌면 머리글 폭도 달라진다(캐시 적중이면 측정 없음)
        Loc.Instance.LanguageChanged += OnLanguageChanged;
        RefreshStripes();
        OnLanguageChanged(); // 떨어져 있던 동안 바뀐 언어 반영
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        UiSettings.Current.Changed -= RefreshStripes;
        UiSettings.Current.Changed -= ApplyFontScale;
        Loc.Instance.LanguageChanged -= OnLanguageChanged;
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        if (_rowsPresenter is not null) { _rowsPresenter.SizeChanged -= OnRowsPresenterSizeChanged; }
        // 이름·형식은 DataGrid 12.1.2 의 TemplatePart("PART_RowsPresenter", DataGridRowsPresenter)와 같다(디컴파일 확인)
        _rowsPresenter = e.NameScope.Find<DataGridRowsPresenter>("PART_RowsPresenter");
        if (_rowsPresenter is not null) { _rowsPresenter.SizeChanged += OnRowsPresenterSizeChanged; }
#if PD_DIAGNOSTICS
        GridOpenBreakdown.Mark('T'); // K15 t_pre 분해: 표 템플릿 적용 끝
#endif
    }

    private void OnRowsPresenterSizeChanged(object? sender, SizeChangedEventArgs e) => FitStarColumn();

    /// <summary>첫 Measure 전 K5 판정을 할지(기본 켬). 진단 대조 스위치 --grid-open latefit 만 끈다(GridColumnDrafts).</summary>
    internal bool EarlyStarFit { get; set; } = true;

    /// <summary>세로 스크롤바·여백처럼 측정 제약 폭과 셀 영역 폭 사이에 끼는 몫의 상한(DIP). 이보다 경계에 가까우면 첫 배치 뒤 실제 폭으로 정한다.</summary>
    private const double EarlyFitMargin = 40;

    protected override Size MeasureOverride(Size availableSize)
    {
#if PD_DIAGNOSTICS
        GridOpenBreakdown.Mark('G'); // K15 t_pre 분해: 표 측정 시작
#endif
        // K5(A7): 열 합 넘침이 분명하면 첫 측정 전에 별 열을 고정해, 첫 배치 뒤 열 폭이 바뀌며 한 번 더 돌던 레이아웃 패스를 없앤다
        if (EarlyStarFit && _columnScaler is not null)
        {
            var rowHeader = (HeadersVisibility & DataGridHeadersVisibility.Row) != 0 && !double.IsNaN(RowHeaderWidth) ? RowHeaderWidth : 0;
            _columnScaler.FitStarIfClear(availableSize.Width - BorderThickness.Left - BorderThickness.Right - rowHeader, EarlyFitMargin);
        }

        return base.MeasureOverride(availableSize);
    }

    /// <summary>K5: 열 합이 셀 영역을 넘으면 별 열을 절대 폭으로 고정해 머리글 크기 조절을 살린다(GridColumnScaler.FitStar).</summary>
    private void FitStarColumn()
    {
        if (_columnScaler is null || _rowsPresenter is null) { return; }
        // 행 머리글을 켠 표는 DataGrid 처럼 그 폭을 뺀다. 자동 폭(NaN)은 공개 값이 없어 0 으로 둔다(이 앱 표는 행 머리글을 쓰지 않는다)
        var rowHeader = (HeadersVisibility & DataGridHeadersVisibility.Row) != 0 && !double.IsNaN(RowHeaderWidth) ? RowHeaderWidth : 0;
        _columnScaler.FitStar(_rowsPresenter.Bounds.Width - rowHeader);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == BodySizeProperty) { ApplyFontScale(); }
        if (change.Property == GridLinesVisibilityProperty) { SwapCellStyle(); }
        if (change.Property == HeadersVisibilityProperty || change.Property == RowDetailsTemplateProperty) { SwapRowStyle(); }
    }

    private Avalonia.Styling.Style? _cellStyle; // 생성자에서 넣는다(그 전 기반 생성자 알림에서는 null)
    private Avalonia.Styling.Style? _rowStyle; // 경량 행 템플릿 스타일(걸려 있을 때만)
    private bool? _deferOffscreenCells; // 첫 셀 생성 때 한 번 정한다(설정을 바꾸면 다음에 여는 표부터)

    /// <summary>
    /// A16 화면 밖 열 셀 지연 실현(01 §5.4, 옵션 GridDeferOffscreenCells 기본 켬)을 이 표에 쓰는가. 셀 템플릿이 PdCellTemplate 일 때만 쓴다 —
    /// 미룬 내용은 그 템플릿이 적용될 때 만들어지므로, 진단 대조(GridColumnDrafts fluentcell·litecell)로 셀 템플릿이 바뀐 표에는 걸면 안 된다.
    /// </summary>
    internal bool DeferOffscreenCells => _deferOffscreenCells ??= UiSettings.Current.GridDeferOffscreenCells && CellTemplateIsOurs();

    /// <summary>표 Styles 에서 템플릿을 거는 스타일 중 마지막(이기는 것)이 우리 셀 스타일인가. 우리 행 스타일은 건너뛴다.</summary>
    private bool CellTemplateIsOurs()
    {
        for (var i = Styles.Count - 1; i >= 0; i--)
        {
            if (Styles[i] is not Avalonia.Styling.Style s || PdRowTemplate.IsOurs(s)) { continue; }
            foreach (var setter in s.Setters)
            {
                if (setter is Avalonia.Styling.Setter { Property: var p } && p == TemplateProperty) { return PdCellTemplate.IsOurs(s); }
            }
        }

        return false;
    }
    private bool _rowStyleRemoved; // 진단 대조가 걷어낸 표는 다시 넣지 않는다

    /// <summary>
    /// A16 C2: 행 머리글을 끄고 행 상세가 없는 표에만 경량 행 템플릿을 건다(조건이 깨지면 Fluent 원본 행 템플릿으로 돌아간다).
    /// 이미 만든 행은 그대로라 행이 만들어지기 전(첫 부착 전)에 정해지는 것이 정상 경로다. 그 뒤에 바뀌면 새로 만드는 행부터 따른다.
    /// </summary>
    private void SwapRowStyle()
    {
        if (_cellStyle is null) { return; } // 기반 생성자 중 알림(생성자 본문에서 다시 부른다)
        if (_rowStyle is not null && !Styles.Contains(_rowStyle)) { _rowStyleRemoved = true; _rowStyle = null; } // 진단 대조(GridColumnDrafts)가 걷어냄
        if (_rowStyleRemoved) { return; }
        var fits = PdRowTemplate.Fits(this);
        if (fits && _rowStyle is null)
        {
            _rowStyle = PdRowTemplate.CreateStyle();
            Styles.Add(_rowStyle);
        }
        else if (!fits && _rowStyle is not null)
        {
            Styles.Remove(_rowStyle);
            _rowStyle = null;
        }
    }

    private static bool VerticalLines(DataGridGridLinesVisibility v) =>
        v is DataGridGridLinesVisibility.Vertical or DataGridGridLinesVisibility.All;

    /// <summary>
    /// A16: 세로선 표시 여부가 바뀌면 셀 템플릿 판을 맞춘다(XAML 의 GridLinesVisibility 는 생성자 뒤에 들어온다). 이미 만든 셀은 그대로라
    /// 행이 만들어지기 전(첫 부착 전)에 바뀌는 것이 정상 경로다. 그 뒤에 바뀌면 새로 만드는 행부터 새 판을 쓴다.
    /// </summary>
    private void SwapCellStyle()
    {
        var vertical = VerticalLines(GridLinesVisibility);
        if (_cellStyle is null
            || (PdCellTemplate.HasVerticalLine(_cellStyle) == vertical && (vertical || PdCellTemplate.IsCompact(_cellStyle) == _compactCells)))
        {
            return;
        }

        var index = Styles.IndexOf(_cellStyle);
        if (index < 0)
        {
            return; // 진단 대조(GridColumnDrafts)가 우리 셀 스타일을 걷어낸 표는 다시 넣지 않는다
        }

        _cellStyle = PdCellTemplate.CreateStyle(vertical, _compactCells);
        Styles[index] = _cellStyle;
    }

    // A16 단일 요소 셀(01 §5.4, 옵션 GridCompactCells 기본 켬 — 기본값 정본 UiSystemSettings.DefaultGridCompactCells): 세로선 없는 표의 셀 템플릿을 PdCellChrome 하나로 만든다(PdCellChrome.cs).
    // 표를 만들 때 한 번 정한다(설정을 바꾸면 다음에 여는 표부터 — GridDeferOffscreenCells 와 같은 규칙)
    private bool _compactCells = UiSettings.Current.GridCompactCells;

    private static PdCellTextLook? s_cellTextLook;
    private bool? _drawnCellText; // 첫 셀 생성 때 한 번 정한다(설정을 바꾸면 다음에 여는 표부터)

    /// <summary>그리는 셀 글자 모양 값(PdDataGridStyles 의 TextBlock.cell 스타일에서 읽음). 못 읽으면 null.</summary>
    internal PdCellTextLook? CellTextLook => s_cellTextLook;

    /// <summary>
    /// K15 그리는 셀 글자(옵션 GridDrawnCellText 기본 켬 — 기본값 정본 UiSystemSettings.DefaultGridDrawnCellText)를 이 표에 쓰는가.
    /// 단일 요소 셀 판(PdCellTemplate.Compact)이 이기는 표만 쓴다 — 글자는 그 판의 뿌리(PdCellChrome)가 그린다. 진단 대조 grid-open=tbcell 이면 끈다.
    /// </summary>
    internal bool DrawnCellText =>
        (_drawnCellText ??= UiSettings.Current.GridDrawnCellText && s_cellTextLook is not null && CellTemplateIsOurs())
        && _compactCells && _cellStyle is { } s && PdCellTemplate.IsCompact(s);

    /// <summary>그리는 셀 글자를 끈다(진단 대조 grid-open=tbcell — TextBlock 판). 행이 만들어지기 전(생성자 직후)에 불러야 한다.</summary>
    internal void DisableDrawnCellText() => _drawnCellText = false;

    /// <summary>단일 요소 셀을 켜고 끈다(진단 대조 grid-open=fullcell). 이미 만든 셀은 그대로라 행이 만들어지기 전(생성자 직후)에 불러야 한다.</summary>
    internal void SetCompactCells(bool on)
    {
        _compactCells = on;
        SwapCellStyle();
    }

    private void OnLanguageChanged()
    {
        ApplyFontScale(); // 머리글 문구가 이미 바뀌었으면 여기서 다시 계산(캐시 적중 시 측정 없음)
        Dispatcher.UIThread.Post(ApplyFontScale, DispatcherPriority.Background); // 머리글 바인딩이 이 알림 뒤에 갱신되는 경우 대비 — 문구가 같으면 키 비교로 즉시 반환
    }

    private void ApplyStripe(DataGridRow row) =>
        row.Background = row.GetIndex() % 2 == 1 ? _stripeBrush : (RowBrushIsBackdrop(row) ? null : _rowBrush);

    /// <summary>
    /// A15: 짝수 행 바탕이 표 바탕과 같은 불투명 단색이면 칠하지 않는다(null — Avalonia 는 null 붓의 그리기를 기록하지 않는다).
    /// 보이는 색은 아래 표 바탕이 그대로 내므로 같다. 행 경계 1px(투명 위·아래 테두리) hit-test 는 경량 행 템플릿의 RowBorder 가
    /// 사각형 전체로 받으므로(PdRowTemplate.RowHitBorder) 경량 템플릿 행에서만 적용한다. Fluent 원본 템플릿 행은 예전처럼 칠한다.
    /// </summary>
    private bool RowBrushIsBackdrop(DataGridRow row)
    {
        var ours = row.Template is { } t ? ReferenceEquals(t, PdRowTemplate.Instance) : _rowStyle is not null && Styles.Contains(_rowStyle);
        return ours && Background is ISolidColorBrush g && _rowBrush is ISolidColorBrush r
            && g.Color.A == 255 && g.Opacity >= 1 && r.Opacity >= 1 && g.Color == r.Color;
    }

    /// <summary>줄무늬·바탕 브러시를 현재 테마 토큰에서 다시 읽고, 화면에 있는 행만 다시 칠한다.</summary>
    private void RefreshStripes()
    {
        var stripe = this.TryFindResource("PdBrushGridStripe", ActualThemeVariant, out var s) ? s as IBrush : null;
        var row0 = this.TryFindResource("PdBrushBackground", ActualThemeVariant, out var b) ? b as IBrush : null;
        // ThemeApplier 는 브러시 인스턴스를 유지하고 Color 만 바꾼다 → 같은 인스턴스면 행에 이미 걸린 브러시가 새 색으로 그려지므로 행 순회 불필요
        if (_stripesApplied && ReferenceEquals(stripe, _stripeBrush) && ReferenceEquals(row0, _rowBrush)) { return; }
        _stripeBrush = stripe;
        _rowBrush = row0;
        _stripesApplied = true;
        foreach (var row in this.GetVisualDescendants().OfType<DataGridRow>()) { ApplyStripe(row); }
    }

    /// <summary>행 높이·고정 열 너비를 글자 크기와 휠 배율에 맞춘다(GridColumnScaler.Apply). 첫 부착 전에는 할 일이 없다.</summary>
    private void ApplyFontScale()
    {
        if (_columnScaler is null) { return; }
        var family = this.TryFindResource("PdFontFamily", ActualThemeVariant, out var f) && f is FontFamily ff ? ff : FontFamily.Default;
        _columnScaler.Apply(GetValue(BodySizeProperty), family);
        FitStarColumn(); // K5: 열 폭이 바뀌었으니 넘침을 다시 판정(셀 영역 폭은 그대로라 SizeChanged 가 오지 않는다)
    }
}
