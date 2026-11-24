// 1만 행 가상화 그리드 문서 화면의 코드 비하인드(데이터 연결·필터·정렬/스크롤 계측·열기 시간 계측)
using xkxk.frameworks.ui.shell;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using xkxk.frameworks.ui.shell.Diagnostics;
using xkxk.frameworks.ui.shell.Docking;
using xkxk.frameworks.ui.shell.Localization;

namespace xkxk.frameworks.ui.views.Documents;

/// <summary>그리드 문서 화면. XAML 생성 partial 짝은 이 파일 1개뿐이며, 표 공통 동작은 PdDataGrid, 계측·셀 선은 Grid* 클래스에 맡긴다.</summary>
public partial class GridDocumentView : UserControl, IContentLoadReporter
#if PD_DIAGNOSTICS
    , IGridDocumentDiagnostics, IGridLoadDiagnostics
#endif
{
    public const int DefaultRows = 10_000;

    public GridDocumentView()
        : this(DefaultRows)
    {
    }

    public GridDocumentView(int rows)
    {
        _layout = new GridLayoutProbe(this);
        _baseMeasure = base.MeasureOverride; // base 구현을 계측기에 넘긴다(GridLayoutProbe)
        _baseArrange = base.ArrangeOverride;
        PerfProbe.Current.TrackOpen(this, "grid");
        var perf = PerfProbe.Current;
        var t0 = perf.NowMs;
        InitializeComponent();
        GridColumnDrafts.ApplyControlColumns(this, RowsGrid); // 대조 빌드에서만 열 교체·진단 초안(GridColumnDrafts.cs)
        var stableStrokes = GridCellStrokes.ApplyStableCellStrokes(this); // Q4 셀 숨은 테두리 Stroke 고정 — 기본 꺼짐(GridCellStrokes.cs)
#if PD_DIAGNOSTICS
        PerfProbe.Current.SetMetric("grid_stable_strokes", stableStrokes ? 1 : 0); // 실제 적용 확인(요청만 되고 토큰이 없으면 0 — 예: --theme-new-tokens off)
#endif
        var t1 = perf.NowMs;
        Model = new GridDocumentViewModel(rows);
        var t2 = perf.NowMs;
        PerfProbe.Current.SetMetricMax("grid_rows", rows);
        RowsGrid.ItemsSource = Model.Rows;
        UpdateCount();
#if PD_DIAGNOSTICS
        GridScrollProbe.Install(); // D-SC2·D-TP1 진단 계수기 — 관찰만 한다(GridScrollProbe.cs, 중복 호출 무시)
#endif
        AttachedToVisualTree += (_, _) =>
        {
            Loc.Instance.LanguageChanged += OnLanguageChanged;
            OnLanguageChanged(); // 떨어져 있던 동안 바뀐 언어 반영
        };
        DetachedFromVisualTree += (_, _) => Loc.Instance.LanguageChanged -= OnLanguageChanged;
        // 생성 단계별 계측(grid_open_analysis.md): init=XAML·열 생성, seed=시드(캐시 적중이면 거의 0), bind=데이터 연결·열 너비. 첫 렌더까지는 doc.open.grid
        var t3 = perf.NowMs;
        if (!PerfProbe.SuppressOpenTrack) // 예열·준비된 표 뷰(GridViewPool)는 만들 때 열기 계측을 하지 않고, 실제 열기에 넘겨질 때 BeginOpenTracking 으로 시작한다
        {
            perf.RecordOperation("grid.ctor.init", t1 - t0, t1 - t0);
            perf.RecordOperation(Model.SeedCached ? "grid.ctor.seed.cached" : "grid.ctor.seed", t2 - t1, t2 - t1);
            perf.RecordOperation("grid.ctor.bind", t3 - t2, t3 - t2);
            _layout.BeginOpenBreakdown(); // 부착·첫 측정·첫 배치·첫 렌더 분해(GridLayoutProbe.cs)
        }

        FilterBox.TextChanged += (_, _) => Search();

        // 머리글 클릭 정렬: 클릭 → 다음 프레임 렌더까지 시간(grid_sort_click)
        RowsGrid.Sorting += (_, _) =>
        {
            Model.EnsureMaterialized(); // 지연 생성 행을 정렬 전에 모두 채운다(첫 정렬만 비용, grid.seed.materialize)
            PerfProbe.Current.TimeOperation("doc.grid.sort").Dispose(); // 머리글 클릭 → 정렬 반영 프레임까지(PERF-UI)
            var sw = Stopwatch.StartNew();
            var top = TopLevel.GetTopLevel(this);
            top?.RequestAnimationFrame(_ => top.RequestAnimationFrame(_ =>
            {
                PerfProbe.Current.RecordStep("grid_sort_click", sw.Elapsed.TotalMilliseconds);
                var ms = sw.Elapsed.TotalMilliseconds;
                ShowTiming(() => Loc.Tr("doc.grid.sort.status", ms));
            }));
        };
    }

    public GridDocumentViewModel Model { get; }

    /// <summary>불러오는 중 계약은 뷰모델이 구현하고 화면은 넘기기만 한다(23 §2.2).</summary>
    void IContentLoadReporter.Attach(ILoadToken openToken, Func<string, ILoadToken> begin) => Model.Attach(openToken, begin);

    private void Search() => _ = Model.RunTrackedAsync("search", NextFrame, () =>
    {
        using var op = PerfProbe.Current.TimeOperation("doc.grid.filter");
        var ms = Model.ApplyFilter(FilterBox.Text);
        ShowTiming(() => Loc.Tr("doc.grid.filter.status", ms));
        UpdateCount();
    });

    private void Refresh() => _ = Model.RunTrackedAsync("refresh", NextFrame, () =>
    {
        Model.Refresh();
        UpdateCount();
    });

    private Task NextFrame()
    {
        var drawn = new TaskCompletionSource();
        xkxk.frameworks.ui.shell.Busy.BusyHooks.AfterRender(this, () => drawn.TrySetResult());
        return drawn.Task;
    }

#if PD_DIAGNOSTICS
    void IGridLoadDiagnostics.DiagSearch(string text)
    {
        if (string.Equals(FilterBox.Text ?? "", text, StringComparison.Ordinal)) { Search(); } // 같은 글이면 변경 사건이 없다
        else { FilterBox.Text = text; }
    }

    void IGridLoadDiagnostics.DiagRefresh() => Refresh();

    int IGridLoadDiagnostics.DiagVisibleCount => Model.VisibleCount;
#endif

    private readonly GridLayoutProbe _layout;
    private readonly Func<Size, Size> _baseMeasure;
    private readonly Func<Size, Size> _baseArrange;

    protected override Size MeasureOverride(Size availableSize)
    {
#if PD_DIAGNOSTICS
        GridMeasureTrace.Trace(this, availableSize); // 측정 호출 단위 기록(GridMeasureTrace.cs) — 스택 수집 깊이 유지를 위해 여기서 직접 부른다
#endif
        return _layout.Measure(availableSize, _baseMeasure);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        GridViewPool.NoteSize(this, finalSize); // 준비된 표 뷰를 다음에 만들 크기(실제 문서 영역 크기)
        return _layout.Arrange(finalSize, _baseArrange);
    }

    /// <summary>준비된 표 뷰가 실제 열기에 넘겨질 때 열기 계측(doc.open.grid·부착/첫 측정/첫 렌더 분해)을 시작한다(GridViewPool.TryTake).</summary>
    internal void BeginOpenTracking()
    {
        PerfProbe.Current.TrackOpen(this, "grid");
        _layout.BeginOpenBreakdown();
    }

#if PD_DIAGNOSTICS
    /// <summary>진단(dock.trace): 측정 기록 사본·초기화(모든 그리드 공유), 첫 열기 뒤 마지막 측정 때 받은 크기.</summary>
    public Size DiagLastMeasureGiven => _layout.DiagLastMeasureGiven;

    public int DiagRowCount => Model.RowCount;

    public (string Hash, bool WasMaterialized) DiagComputeHash() => Model.ComputeHash();

    public void DiagSortByName(bool descending) => Model.Sort(nameof(GridRow.Name), descending);

    public void DiagScrollStep(int round) => this.FindControl<DataGrid>("RowsGrid")?.ScrollIntoView(Model.Rows.GetItemAt(Math.Min(Model.VisibleCount - 1, 30 + (round * 3))), null);
#endif

#if PD_DIAGNOSTICS // 자동 시험은 진단 빌드 전용(AutomationBridge.Commands 만 호출)
    /// <summary>
    /// 자동 시험(--cmd grid-exercise): 이름 정렬(정렬+첫 렌더 시간) → 60프레임 동안 끝까지 스크롤.
    /// 프레임 시간은 phase "grid-sort"·"grid-scroll" 로 남는다.
    /// </summary>
    public async Task ExerciseAsync(TopLevel top)
    {
        var perf = PerfProbe.Current;
        perf.BeginPhase("grid-sort");
        var start = perf.NowMs;
        using (perf.TimeOperation("doc.grid.sort")) { Model.Sort(nameof(GridRow.Name)); }
        await AutomationBridge.WaitFramesAsync(top, 2);
        perf.SetMetricMax("grid_sort_render_ms", perf.NowMs - start);

        perf.BeginPhase("grid-scroll");
        start = perf.NowMs;
        const int frames = 60;
        var count = Model.VisibleCount;
        for (var i = 1; i <= frames && count > 0; i++)
        {
            var item = Model.Rows.GetItemAt(Math.Min(count - 1, (int)((long)count * i / frames)));
            using (perf.TimeOperation("doc.grid.scroll.jump")) { RowsGrid.ScrollIntoView(item, null); }
            await AutomationBridge.WaitFramesAsync(top, 1);
        }

        perf.RecordStep("grid-scroll", perf.NowMs - start);

        // 실사용형 연속 스크롤: 프레임마다 3행씩 아래로(휠 1칸 ≈ 3행) — 컨테이너 재활용 경로를 잰다.
        perf.BeginPhase("grid-scroll-smooth");
        start = perf.NowMs;
        RowsGrid.ScrollIntoView(Model.Rows.GetItemAt(0), null);
        await AutomationBridge.WaitFramesAsync(top, 1);
        var visible = Math.Max(1, (int)(RowsGrid.Bounds.Height / RowsGrid.RowHeight) - 2);
        for (var i = 1; i <= frames && count > visible; i++)
        {
            using (perf.TimeOperation("doc.grid.scroll")) { RowsGrid.ScrollIntoView(Model.Rows.GetItemAt(Math.Min(count - 1, visible + (i * 3))), null); }
            await AutomationBridge.WaitFramesAsync(top, 1);
        }

        perf.RecordStep("grid-scroll-smooth", perf.NowMs - start);
    }
#endif

    private void UpdateCount() => CountText.Text = Loc.Tr("doc.grid.count.status", Model.VisibleCount, Model.RowCount);

    // 마지막 시간 문구(필터·정렬)를 만드는 함수 — 언어가 바뀌면 같은 값으로 현재 언어 문장을 다시 만든다
    private Func<string>? _timing;

    private void ShowTiming(Func<string> timing)
    {
        _timing = timing;
        TimingText.Text = timing();
    }

    private void OnLanguageChanged()
    {
        UpdateCount();
        if (_timing is not null) { TimingText.Text = _timing(); }
    }
}
