// 그리드 벤치 모델 — 대량 행 생성·한국어 정렬(ko-KR collation)·필터·실시간 셀 갱신·xlsx 내보내기 측정
using xkxk.frameworks.ui.shell;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Avalonia.Collections;

namespace xkxk.frameworks.ui.testbed.Bench;

/// <summary>
/// 행 원본은 행 수별로 한 번 만들어 공유한다(같은 시드). 정렬·필터 상태를 갖는 DataGridCollectionView 는 문서마다 새로 만든다.
/// 내장 DataGrid 와 ProDataGrid 는 어셈블리 이름·네임스페이스가 같아(교체형) 이 파일은 두 빌드에서 그대로 컴파일된다.
/// </summary>
public sealed class GridBenchModel
{
    private static readonly Dictionary<int, BenchRow[]> Cache = new();

#if PD_GRID_PRO
    public const string Implementation = "ProDataGrid 12.1.0.6";
#else
    public const string Implementation = "Avalonia.Controls.DataGrid 12.1.2";
#endif

#if PD_XLSX
    public const bool XlsxAvailable = true;
#else
    public const bool XlsxAvailable = false;
#endif

    public GridBenchModel(int rowCount)
    {
        var sw = Stopwatch.StartNew();
        RowCount = rowCount;
        Cached = Cache.TryGetValue(rowCount, out var rows);
        if (!Cached)
        {
            var rnd = new Random(20261029);
            rows = new BenchRow[rowCount];
            for (var i = 0; i < rowCount; i++) { rows[i] = new BenchRow(i + 1, rnd); }
            Cache[rowCount] = rows;
        }

        Source = rows!;
        SeedMs = sw.Elapsed.TotalMilliseconds;
        Rows = new DataGridCollectionView(Source);
    }

    public int RowCount { get; }
    public bool Cached { get; }
    public double SeedMs { get; }
    public BenchRow[] Source { get; }
    public DataGridCollectionView Rows { get; }

    /// <summary>
    /// 정렬. culture=ko-KR 등이면 그 문화권의 ICU/OS collation, culture=app 이면 앱 고유 비교자(HangulFirstComparer — 플랫폼 무관 같은 순서),
    /// 비우면 문화권 없는 기본 비교. 반환 ms.
    /// </summary>
    public double Sort(string property, bool descending, string? culture)
    {
        var sw = Stopwatch.StartNew();
        var dir = descending ? ListSortDirection.Descending : ListSortDirection.Ascending;
        var app = string.Equals(culture, "app", StringComparison.OrdinalIgnoreCase);
        var ordinal = string.Equals(culture, "ordinal", StringComparison.OrdinalIgnoreCase);
        CultureInfo? ci = null;
        if (!app && !ordinal && !string.IsNullOrEmpty(culture)) { ci = CultureInfo.GetCultureInfo(culture); }
        // 경로 기반 FromPath 는 ProDataGrid 에서 RequiresUnreferencedCode(반사) 라 트리밍 경고(IL2026)를 낸다.
        // 반사 없이 행 값을 꺼내는 비교자 + FromComparer(내장·Pro 공통 API, RUC 없음)로 바꾼다.
        // culture: app=앱 고유 비교자, ordinal=서수(코드 포인트 — 대문자 먼저), 문화권 이름=그 문화권, 비움=현재 문화권(서수 아님 — otest-1 bi1201 관찰).
        IComparer valueComparer = app ? HangulFirstComparer.Instance
            : ordinal ? StringComparer.Ordinal
            : ci is not null ? StringComparer.Create(ci, ignoreCase: false)
            : StringComparer.CurrentCulture;
        using (Rows.DeferRefresh())
        {
            Rows.SortDescriptions.Clear();
            Rows.SortDescriptions.Add(DataGridSortDescription.FromComparer(new RowComparer(property, valueComparer), dir));
        }

        return sw.Elapsed.TotalMilliseconds;
    }

    public double ClearSort()
    {
        var sw = Stopwatch.StartNew();
        Rows.SortDescriptions.Clear();
        return sw.Elapsed.TotalMilliseconds;
    }

    /// <summary>
    /// 이름·부서·프로젝트·고객·상태 부분 일치(대소문자 무시). 반환 ms.
    /// latinNames=true 면 이름에 영문자가 있는 행만 남긴다(영문 이름끼리의 언어 규칙 정렬 a&lt;B 확인용 — otest-1 제안). text 와 함께 쓰면 둘 다 만족.
    /// </summary>
    public double Filter(string? text, bool latinNames = false)
    {
        var sw = Stopwatch.StartNew();
        var key = text?.Trim();
        if (latinNames)
        {
            Rows.Filter = o => o is BenchRow r && HasLatin(r.Name)
                && (string.IsNullOrEmpty(key) || r.Name.Contains(key, StringComparison.OrdinalIgnoreCase));
            return sw.Elapsed.TotalMilliseconds;
        }

        Rows.Filter = string.IsNullOrEmpty(key)
            ? null
            : o => o is BenchRow r
                && (r.Name.Contains(key, StringComparison.OrdinalIgnoreCase)
                    || r.Dept.Contains(key, StringComparison.OrdinalIgnoreCase)
                    || r.Project.Contains(key, StringComparison.OrdinalIgnoreCase)
                    || r.Customer.Contains(key, StringComparison.OrdinalIgnoreCase)
                    || r.Status.Contains(key, StringComparison.OrdinalIgnoreCase));
        return sw.Elapsed.TotalMilliseconds;
    }

    /// <summary>행 비교자: 속성 이름 → 값 꺼내기를 switch 로 고정(반사 없음). 숫자·날짜 열은 원래 형식 값으로 비교한다.</summary>
    private sealed class RowComparer : IComparer
    {
        private readonly Func<BenchRow, object?> _get;
        private readonly IComparer _value;
        private readonly bool _text;

        public RowComparer(string property, IComparer valueComparer)
        {
            static (Func<BenchRow, object?>, bool) T(Func<BenchRow, object?> f) => (f, true);  // 문자열 열
            static (Func<BenchRow, object?>, bool) N(Func<BenchRow, object?> f) => (f, false); // 숫자·날짜 열
            (_get, _text) = property switch
            {
                nameof(BenchRow.No) => N(r => r.No),
                nameof(BenchRow.Code) => T(r => r.Code),
                nameof(BenchRow.Name) => T(r => r.Name),
                nameof(BenchRow.Dept) => T(r => r.Dept),
                nameof(BenchRow.Grade) => T(r => r.Grade),
                nameof(BenchRow.Project) => T(r => r.Project),
                nameof(BenchRow.Status) => T(r => r.Status),
                nameof(BenchRow.Region) => T(r => r.Region),
                nameof(BenchRow.Start) => N(r => r.Start),
                nameof(BenchRow.End) => N(r => r.End),
                nameof(BenchRow.Progress) => N(r => r.Progress),
                nameof(BenchRow.Hours) => N(r => r.Hours),
                nameof(BenchRow.Budget) => N(r => r.Budget),
                nameof(BenchRow.Rate) => N(r => r.Rate),
                nameof(BenchRow.Price) => N(r => r.Price),
                nameof(BenchRow.Change) => N(r => r.Change),
                nameof(BenchRow.Volume) => N(r => r.Volume),
                nameof(BenchRow.Customer) => T(r => r.Customer),
                nameof(BenchRow.Email) => T(r => r.Email),
                nameof(BenchRow.Note) => T(r => r.Note),
                _ => throw new ArgumentException($"정렬할 수 없는 속성: {property}"),
            };
            _value = valueComparer;
        }

        public int Compare(object? x, object? y)
        {
            var a = x is BenchRow rx ? _get(rx) : null;
            var b = y is BenchRow ry ? _get(ry) : null;
            return _text ? _value.Compare(a, b) : Comparer<object>.Default.Compare(a, b);
        }
    }

    private static bool HasLatin(string s)
    {
        foreach (var c in s)
        {
            if (c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z')) { return true; }
        }

        return false;
    }

    /// <summary>현재 뷰 순서의 앞 n개 값(정렬 결과 확인용).</summary>
    /// <summary>
    /// 현재 뷰 순서의 앞 n개 값. distinct=true 면 서로 다른 값만 앞에서부터 n개(같은 이름 반복 때문에 순서 판정이 어려웠던 문제 — otest-1·otest-m-1).
    /// distinct 는 최대 maxScan 행까지만 훑는다.
    /// </summary>
    public JsonArray Head(string property, int n, bool distinct = false, int maxScan = 200_000)
    {
        var arr = new JsonArray();
        var prop = typeof(BenchRow).GetProperty(property);
        var seen = distinct ? new HashSet<string?>() : null;
        var scanned = 0;
        foreach (var o in Rows)
        {
            if (arr.Count >= n || scanned++ >= maxScan) { break; }
            var v = prop?.GetValue(o)?.ToString();
            if (seen is not null && !seen.Add(v)) { continue; }
            arr.Add((JsonNode?)JsonValue.Create(v)); // 비제네릭 Add(JsonNode) — 제네릭 Add<T> 는 WASM 에서 NoMetadataForType 예외
        }

        return arr;
    }

    // ── 실시간 갱신: 시세판처럼 행 일부의 가격·등락·거래량을 바꾼다(행당 셀 3개) ──
    private readonly Random _live = new(7);
    private int _cursor;
    private int _statusCursor;

    /// <summary>지금까지 바꾼 Status 횟수(누적). 시나리오는 전후 차이로 이번 회차 횟수를 낸다.</summary>
    public int StatusFlips { get; private set; }

    /// <summary>
    /// rows 개 행을 갱신한다. focusRows&gt;0 이면 앞쪽 focusRows 행(화면에 보이는 영역)에서 고르고, 0 이면 전체에서 고른다.
    /// 반환 = 갱신한 셀 수(행×3).
    /// </summary>
    public int UpdateLive(int rows, int focusRows, double statusPct = 0)
    {
        var range = focusRows > 0 ? Math.Min(focusRows, Source.Length) : Source.Length;
        // statusPct = 갱신 행 중 Status 를 '지연'↔'진행'으로 바꾸는 비율(%). 0 이면 기존과 같다. 바꿀 행은 별도 커서(×31)로 골라 집중 범위 전체를 돈다
        var stride = statusPct > 0 ? Math.Max(1, (int)Math.Round(100 / statusPct)) : 0;
        for (var k = 0; k < rows; k++)
        {
            var r = Source[(_cursor++ * 7919) % range];
            var delta = Math.Round((_live.NextDouble() - 0.5) * 400, 0);
            r.SetLive(Math.Max(100, r.Price + delta), delta, r.Volume + _live.Next(1, 500));
            if (stride > 0 && _cursor % stride == 0)
            {
                var s = Source[(_statusCursor++ * 31) % range];
                s.SetStatus(s.Status == "지연" ? "진행" : "지연");
                StatusFlips++;
            }
        }

        return rows * 3;
    }

    // ── 한국어 collation 보고(문화권 데이터가 있는가 — WASM·Android ICU 확인) ──
    public static JsonObject CollationReport()
    {
        var o = new JsonObject();
        try
        {
            var ko = CultureInfo.GetCultureInfo("ko-KR");
            var cmp = ko.CompareInfo;
            o["ko_culture"] = ko.Name;
            o["ga_lt_na"] = cmp.Compare("가", "나") < 0;
            o["na_lt_da"] = cmp.Compare("나다", "다라") < 0;
            o["case_insensitive_eq"] = cmp.Compare("abc", "ABC", CompareOptions.IgnoreCase) == 0;
            // 언어 규칙 확인: 서수 비교는 'B'(0x42) < 'a'(0x61), 언어 규칙은 a < B
            o["linguistic_a_lt_B"] = cmp.Compare("a", "B") < 0;
            var sample = new[] { "하은", "Acme", "가온", "bright", "나래", "다온", "Zeta" };
            o["sorted_sample"] = new JsonArray(sample.OrderBy(s => s, StringComparer.Create(ko, false)).Select(s => (JsonNode?)s).ToArray());
            o["app_sorted_sample"] = new JsonArray(sample.OrderBy(s => s, HangulFirstComparer.Instance).Select(s => (JsonNode?)s).ToArray()); // 앱 고유 비교자(플랫폼 무관)
        }
        catch (Exception ex)
        {
            o["error"] = ex.GetType().Name + ": " + ex.Message; // 인바리언트 전역화 모드 등
        }

        return o;
    }

    // ── xlsx 내보내기(ClosedXML) — WASM 크기·동작 영향 확인용 ──
    public JsonObject ExportXlsx(int maxRows)
    {
#if PD_XLSX
        var sw = Stopwatch.StartNew();
        var before = GC.GetTotalMemory(false);
        using var wb = new ClosedXML.Excel.XLWorkbook();
        var ws = wb.Worksheets.Add("벤치");
        string[] headers = { "번호", "코드", "이름", "부서", "직급", "프로젝트", "상태", "지역", "시작", "종료", "진척", "공수", "예산", "증감률", "가격", "등락", "거래량", "고객", "이메일", "비고" };
        for (var c = 0; c < headers.Length; c++) { ws.Cell(1, c + 1).Value = headers[c]; }
        var rowIndex = 2;
        foreach (var o in Rows)
        {
            if (rowIndex - 1 > maxRows || o is not BenchRow r) { break; }
            var x = rowIndex++;
            ws.Cell(x, 1).Value = r.No; ws.Cell(x, 2).Value = r.Code; ws.Cell(x, 3).Value = r.Name; ws.Cell(x, 4).Value = r.Dept;
            ws.Cell(x, 5).Value = r.Grade; ws.Cell(x, 6).Value = r.Project; ws.Cell(x, 7).Value = r.Status; ws.Cell(x, 8).Value = r.Region;
            ws.Cell(x, 9).Value = r.Start; ws.Cell(x, 10).Value = r.End; ws.Cell(x, 11).Value = r.Progress; ws.Cell(x, 12).Value = r.Hours;
            ws.Cell(x, 13).Value = r.Budget; ws.Cell(x, 14).Value = r.Rate; ws.Cell(x, 15).Value = r.Price; ws.Cell(x, 16).Value = r.Change;
            ws.Cell(x, 17).Value = r.Volume; ws.Cell(x, 18).Value = r.Customer; ws.Cell(x, 19).Value = r.Email; ws.Cell(x, 20).Value = r.Note;
        }

        var buildMs = sw.Elapsed.TotalMilliseconds;
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return new JsonObject
        {
            ["ok"] = true,
            ["rows"] = rowIndex - 2,
            ["build_ms"] = Math.Round(buildMs, 1),
            ["total_ms"] = Math.Round(sw.Elapsed.TotalMilliseconds, 1),
            ["bytes"] = ms.Length,
            ["managed_delta_mb"] = BenchProbe.Mb(GC.GetTotalMemory(false) - before),
        };
#else
        _ = maxRows;
        return new JsonObject { ["ok"] = false, ["error"] = "PocXlsx=false 빌드(ClosedXML 미포함)" };
#endif
    }
}
