// 대량 행(기본 1만 행 × 10열) 가상화 그리드 문서의 샘플 데이터·필터 모델
using xkxk.frameworks.ui.shell;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia.Collections;
using xkxk.frameworks.ui.shell.Diagnostics;
using xkxk.frameworks.ui.shell.Docking;

namespace xkxk.frameworks.ui.views.Documents;

/// <summary>
/// 그리드 문서 모델. 행은 결정적(시드 고정) 샘플이라 실행마다 같은 화면이 나온다.
/// 행 값은 기본으로 지연 생성한다(lazy, GridRow.cs) — 보이는 행만 처음 읽힐 때 만들고, 정렬·필터 직전에 남은 행을 한 번에 채운다(그 시간은 grid.seed.materialize).
/// 기본값 전환 근거(perf15 D1, team-lead 승인): eager·lazy grid-hash 같음(47a02769eb326235, 예전 알고리즘 해시와도 같음), 첫 화면 그리드 영역 화소 같음(logs/odev_perf2/perf15_changes.md).
/// 진단 빌드 `--grid-seed eager` 는 예전처럼 생성자에서 모두 채운다(대조용).
/// </summary>
public sealed class GridDocumentViewModel : IContentLoadReporter
{
    private Func<string, ILoadToken>? _begin;
    private ILoadToken? _work; // 진행 중인 검색·새로고침 — 새 작업이 시작되면 취소한다
    private int _workVersion;

    // 시드 공유 캐시(행 수별 1회 생성). 행은 값을 한 번만 채우고 이후 바뀌지 않으며 그리드는 IsReadOnly 라 문서끼리 공유해도 서로 영향이 없다.
    // 원본은 읽기 전용 목록으로 공유하고, 정렬·필터 상태를 갖는 DataGridCollectionView 는 문서마다 새로 만든다.
    private static readonly Dictionary<int, IReadOnlyList<GridRow>> SeedCache = new();
    private static readonly HashSet<int> Materialized = new(); // 모든 행 값을 채운 행 수(캐시 키)

    private readonly IReadOnlyList<GridRow> _seed;

    public GridDocumentViewModel(int rowCount)
    {
        RowCount = rowCount;
        SeedCached = SeedCache.TryGetValue(rowCount, out var seed);
        if (!SeedCached)
        {
            seed = CreateSeed(rowCount);
            SeedCache[rowCount] = seed;
        }

        _seed = seed!;
        if (!LazySeed) { EnsureMaterialized(); } // 기본 lazy(값 동등성·화소 비교 통과 뒤 전환). 진단 `--grid-seed eager` 만 생성자에서 모두 채운다
        Rows = new DataGridCollectionView(_seed); // 원본을 열거해 항목 객체만 담는다(값은 아직 채우지 않음)
    }

    /// <summary>이번 생성이 캐시를 썼는가(계측용).</summary>
    public bool SeedCached { get; }

    /// <summary>지연 생성 여부. 제품·기본은 lazy. 진단 빌드 `--grid-seed eager` 만 예전처럼 생성자에서 모두 채운다(eager 대조용).</summary>
    private static bool LazySeed
    {
        get
        {
#if PD_DIAGNOSTICS
            var a = App.StartupArgs;
            for (var i = 0; i < a.Count; i++)
            {
                if (a[i].Equals("--grid-seed=eager", StringComparison.OrdinalIgnoreCase)
                    || (a[i].Equals("--grid-seed", StringComparison.OrdinalIgnoreCase) && i + 1 < a.Count && a[i + 1].Equals("eager", StringComparison.OrdinalIgnoreCase)))
                {
                    return false;
                }
            }
#endif
            return true;
        }
    }

#if PD_DIAGNOSTICS
    /// <summary>
    /// 1만 행 전체 값(10열 모두)의 FNV-1a 64비트 해시(원본 순서 — 정렬·필터 무관). 부르면 모든 행이 채워진다.
    /// wasMaterialized = 부르기 전에 이미 모두 채워져 있었는가. lazy·eager 값 동등성 시험용(grid-hash 명령).
    /// </summary>
    public (string Hash, bool WasMaterialized) ComputeHash()
    {
        var was = Materialized.Contains(RowCount);
        EnsureMaterialized();
        var h = 14695981039346656037UL;
        void Add(string s) { foreach (var c in s) { h = (h ^ c) * 1099511628211UL; } h = (h ^ 0x1F) * 1099511628211UL; }
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        foreach (var r in _seed)
        {
            Add(r.No.ToString(inv)); Add(r.NoText); Add(r.Code); Add(r.Name); Add(r.Owner); Add(r.Status);
            Add(r.Start.ToString("o", inv)); Add(r.End.ToString("o", inv)); Add(r.StartText); Add(r.EndText);
            Add(r.Progress.ToString(inv)); Add(r.ProgressText); Add(r.Hours.ToString("R", inv)); Add(r.HoursText); Add(r.Note);
        }

        return (h.ToString("x16", inv), was);
    }
#endif

    private static IReadOnlyList<GridRow> CreateSeed(int rowCount)
    {
        var gen = new GridSeed(rowCount);
        var rows = new GridRow[rowCount];
        for (var i = 0; i < rowCount; i++) { rows[i] = new GridRow(gen, i + 1); }
#if PD_DIAGNOSTICS
        PerfProbe.Current.SetText("grid_seed_mode", LazySeed ? "lazy" : "eager");
#endif
        return Array.AsReadOnly(rows); // ReadOnlyCollection(IList, IsReadOnly=true) — 뷰가 원본을 추가·삭제하지 못하게
    }

    /// <summary>남은 행 값을 모두 채운다(정렬·필터 직전). 처음 한 번만 비용이 들고, 그 시간을 grid.seed.materialize 로 기록한다.</summary>
    public void EnsureMaterialized()
    {
        if (!Materialized.Add(RowCount)) { return; }
        var t = PerfProbe.Current.NowMs;
        foreach (var r in _seed) { r.Fill(); }
        var ms = PerfProbe.Current.NowMs - t;
        PerfProbe.Current.RecordOperation("grid.seed.materialize", ms, ms);
    }

    /// <summary>불러오는 중 계약(23 §2.2): 첫 조회가 끝나야 열기를 완료로 알린다.</summary>
    public void Attach(ILoadToken openToken, Func<string, ILoadToken> begin)
    {
        _begin = begin;
        if (!openToken.IsEnded) { _ = CompleteFirstQueryAsync(openToken); }
    }

    private static async Task CompleteFirstQueryAsync(ILoadToken open)
    {
        try
        {
            await SampleQueryAsync();
            open.Complete();
        }
        catch (Exception ex)
        {
            open.Fail(ex);
        }
    }

    /// <summary>
    /// 검색·새로고침 1건. 표시를 먼저 그린 뒤(afterFrame) 일하고, 그사이 새 작업이 오거나 창이 닫히면 버린다.
    /// 셸에 붙지 않은 표(갤러리·예열)는 그 자리에서 돈다.
    /// </summary>
    /// <remarks>시험대 지원용, 공개 계약 아님(B16 생성 명세 제외 후보).</remarks>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public async Task RunTrackedAsync(string reason, Func<Task> afterFrame, Action work)
    {
        var version = ++_workVersion;
        var previous = _work;
        var token = _work = _begin?.Invoke(reason);
        previous?.Cancel(); // 새 토큰을 건 뒤 취소해야 연속 입력 중 표시가 끊기지 않는다
        if (token is null)
        {
            work();
            return;
        }

        try
        {
            await afterFrame();
            await SampleQueryAsync();
            if (version != _workVersion || token.IsEnded) { return; }
            work();
            token.Complete();
        }
        catch (Exception ex)
        {
            token.Fail(ex);
            xkxk.frameworks.ui.data.Logging.PdLog.Write(xkxk.frameworks.ui.data.Logging.PdLogLevel.Warning, "grid", $"{reason} 실패 {ex.GetType().Name}: {ex.Message}"); // i18n-ok 진단 로그
        }
    }

    private static Task SampleQueryAsync() =>
#if PD_DIAGNOSTICS
        xkxk.frameworks.ui.data.Samples.SampleLoadDelay.WaitAsync();
#else
        Task.CompletedTask;
#endif

    /// <summary>지금 필터·정렬로 다시 조회한다.</summary>
    public void Refresh() => Rows.Refresh();

    public int RowCount { get; }

    /// <summary>DataGrid.ItemsSource. 정렬은 DataGrid 열 머리글 클릭이 이 뷰에 반영한다(그 전에 뷰가 EnsureMaterialized 를 부른다).</summary>
    public DataGridCollectionView Rows { get; }

    /// <summary>현재 필터를 통과한 행 수.</summary>
    public int VisibleCount => Rows.Count;

    /// <summary>속성 하나로 정렬(기존 정렬 대체). 소요 ms 를 반환하고 PerfProbe 에 grid_sort 로 기록한다.</summary>
    public double Sort(string property, bool descending = false)
    {
        var sw = Stopwatch.StartNew();
        EnsureMaterialized();
        using (Rows.DeferRefresh())
        {
            Rows.SortDescriptions.Clear();
            Rows.SortDescriptions.Add(DataGridSortDescription.FromPath(
                property, descending ? ListSortDirection.Descending : ListSortDirection.Ascending));
        }

        var ms = sw.Elapsed.TotalMilliseconds;
        PerfProbe.Current.RecordStep("grid_sort", ms);
        PerfProbe.Current.SetMetricMax("grid_sort_ms", ms);
        return ms;
    }

    /// <summary>이름·담당자·상태·코드에 대한 부분 일치 필터. 소요 ms 를 반환하고 PerfProbe 에 기록한다.</summary>
    public double ApplyFilter(string? text)
    {
        var sw = Stopwatch.StartNew();
        var key = text?.Trim();
        if (!string.IsNullOrEmpty(key)) { EnsureMaterialized(); }
        Rows.Filter = string.IsNullOrEmpty(key)
            ? null
            : o => o is GridRow r
                && (r.Name.Contains(key, StringComparison.OrdinalIgnoreCase)
                    || r.Owner.Contains(key, StringComparison.OrdinalIgnoreCase)
                    || r.Status.Contains(key, StringComparison.OrdinalIgnoreCase)
                    || r.Code.Contains(key, StringComparison.OrdinalIgnoreCase));
        var ms = sw.Elapsed.TotalMilliseconds;
        PerfProbe.Current.RecordStep("grid_filter", ms);
        return ms;
    }
}
