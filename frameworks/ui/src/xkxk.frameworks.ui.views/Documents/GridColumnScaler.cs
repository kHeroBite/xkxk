// 그리드 행 높이·열 너비를 본문 글자 크기에 비례시키고 현재 언어 머리글이 잘리지 않는 최소 폭을 보장하는 열 배율기
using xkxk.frameworks.ui.shell;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Media;

namespace xkxk.frameworks.ui.views.Documents;

/// <summary>GridDocumentView 1개에 붙는다. 생성 시점의 열 너비를 14pt 기준값으로 기억한다. UI 스레드 전용.</summary>
internal sealed class GridColumnScaler
{
    private readonly DataGrid _grid;
    private readonly double[] _baseWidths; // 14pt 기준 열 너비(XAML 값). 별(*) 열은 -1
    private readonly double[] _baseMins; // 14pt 기준 최소 너비(XAML MinWidth)
    private readonly double[] _lastWidths; // 마지막으로 지정한 폭. 지금 폭과 다르면 사용자가 끌어 바꾼 것이다
    private double _lastScale = 1;
    private readonly double? _fixedRowHeight; // 글자 비례가 없는 표(벤치)의 기준 행 높이
    private string? _fontScaleKey; // 마지막으로 적용한 글자 크기|폰트|배율|머리글 문구 — 같으면 Apply 생략(테마 변경 등 무관한 Changed)
    private bool _starPinned; // 열 합이 셀 영역을 넘어 별(*) 열을 최소 폭 절대값으로 고정했는가(K5, FitStar)

    public GridColumnScaler(DataGrid grid, double? fixedRowHeight = null)
    {
        _grid = grid;
        _fixedRowHeight = fixedRowHeight;
        _baseWidths = grid.Columns.Select(c => c.Width.IsAbsolute ? c.Width.Value : -1).ToArray();
        _baseMins = grid.Columns.Select(c => c.MinWidth).ToArray();
        _lastWidths = new double[_baseWidths.Length];
    }

    /// <summary>Alt+Shift+휠 행 높이 배율.</summary>
    public double RowFactor { get; set; } = 1;

    /// <summary>Alt+휠 열 폭 배율.</summary>
    public double ColumnFactor { get; set; } = 1;

    /// <summary>행 높이 = 글자 × 2 × 행 배율, 고정 열 너비 = XAML 값 × 글자/14 × 열 배율.</summary>
    public void Apply(double s, FontFamily family)
    {
        // 키에 실제 머리글 문구를 넣는다: 언어 변경 알림이 {loc:Tr} 머리글 갱신보다 먼저 오면 옛 문구로 재고 키만 바뀌어 다시 재지 않던 결함(Q17 en 잘림)
        var key = $"{s}|{family.Name}|{RowFactor}|{ColumnFactor}|{string.Join("\u001f", _grid.Columns.Select(c => c.Header?.ToString()))}";
        if (key == _fontScaleKey) { return; }
        _fontScaleKey = key;
        _grid.RowHeight = Math.Round((_fixedRowHeight ?? s * 2) * RowFactor);
        var scale = s / 14 * ColumnFactor;
        for (var i = 0; i < _grid.Columns.Count && i < _baseWidths.Length; i++)
        {
            var col = _grid.Columns[i];
            // 현재 언어 머리글이 잘리지 않는 최소 폭(글자 측정 + 좌우 여백·정렬 아이콘). 영어 'Progress %' 처럼 긴 머리글 대비
            var header = HeaderWidth(col.Header?.ToString() ?? "", s, family);
            if (_baseWidths[i] > 0)
            {
                // 사용자가 머리글 경계를 끌어 바꾼 폭은 기준값에 반영해 배율 조절 뒤에도 남긴다
                if (_lastWidths[i] > 0 && col.Width.IsAbsolute && Math.Abs(col.Width.Value - _lastWidths[i]) > 0.5) { _baseWidths[i] = col.Width.Value / _lastScale; }
                // MinWidth = Width: 좁은 폭(390px·접힘)에서 DataGrid 가 별(*) 열 때문에 고정 열을 최소 폭으로 찌그러뜨리지 않게 → 가로 스크롤
                var w = Math.Max(Math.Round(_baseWidths[i] * scale), header);
                col.Width = new DataGridLength(w);
                col.MinWidth = w;
                _lastWidths[i] = w;
            }
            else if (_baseMins[i] > 0)
            {
                col.MinWidth = Math.Max(Math.Round(_baseMins[i] * scale), header); // 작업명(*) 열: 남은 폭을 차지하되 최소 폭 보장
                if (_starPinned) { col.Width = new DataGridLength(col.MinWidth); } // K5: 고정 중이면 새 최소 폭을 따라간다(넘침 여부는 FitStar 가 다시 판정)
            }
        }

        _lastScale = scale;
    }

    /// <summary>
    /// K5: DataGrid 12.1.2 DataGridColumnHeader.CanResizeColumn 은 별 열이 있고(UsesStarSizing) 열 합 ≠ CellsWidth 이면 모든 열의 크기 조절을 막는다(소스 확인).
    /// 고정 열 합 + 별 열 최소 폭이 셀 영역 폭(available)을 넘으면 별 열을 최소 폭 절대값으로 바꿔 별 크기 모드를 끄고, 다시 들어가면 별(1*)로 되돌린다.
    /// 판정은 고정 여부와 무관한 값(고정 열 Width·별 열 MinWidth)만 써서 전환이 오가며 떨지 않는다.
    /// </summary>
    public void FitStar(double available)
    {
        if (available <= 0 || NeededWidth() is not { } need) { return; }
        SetStarPinned(need > available + 0.5);
    }

    /// <summary>
    /// K5 첫 Measure 전 판정(A7): 셀 영역 폭의 상한 추정(estimate — 세로 스크롤바·테두리를 빼기 전 값)으로 넘침이 분명할 때만 FitStar 와 같은 결정을 미리 내린다.
    /// 첫 배치 뒤 SizeChanged 에서 정하던 것을 첫 측정 전에 정해 열 폭 변경으로 생기던 두 번째 레이아웃 패스를 없앤다.
    /// 실제 폭은 estimate−margin 과 estimate 사이라고 보고, 그 구간에 판정 경계가 걸리면 건너뛴다(SizeChanged 의 FitStar 가 실제 폭으로 정한다 — 판정이 서로 엇갈리지 않는다).
    /// </summary>
    public void FitStarIfClear(double estimate, double margin)
    {
        if (estimate <= 0 || double.IsInfinity(estimate) || NeededWidth() is not { } need) { return; }
        if (need > estimate + 0.5) { SetStarPinned(true); }
        else if (need <= estimate - margin + 0.5) { SetStarPinned(false); }
    }

    // 보이는 고정 열 Width 합 + 별 열 MinWidth 합(별 열이 없으면 null). 고정 여부와 무관한 값만 써서 전환이 오가며 떨지 않는다
    private double? NeededWidth()
    {
        double fixedSum = 0, starMin = 0;
        var hasStar = false;
        for (var i = 0; i < _grid.Columns.Count && i < _baseWidths.Length; i++)
        {
            var col = _grid.Columns[i];
            if (!col.IsVisible) { continue; }
            if (_baseWidths[i] > 0) { fixedSum += col.Width.Value; }
            else { hasStar = true; starMin += col.MinWidth; }
        }

        return hasStar ? fixedSum + starMin : null;
    }

    private void SetStarPinned(bool overflow)
    {
        if (overflow == _starPinned) { return; }
        _starPinned = overflow;
        for (var i = 0; i < _grid.Columns.Count && i < _baseWidths.Length; i++)
        {
            if (_baseWidths[i] > 0) { continue; }
            var col = _grid.Columns[i];
            col.Width = overflow ? new DataGridLength(col.MinWidth) : new DataGridLength(1, DataGridLengthUnitType.Star);
        }
    }

    /// <summary>판정·시험용 — 별 열을 절대 폭으로 고정했는가(K5).</summary>
    public bool StarPinned => _starPinned;

    // 머리글 폭 캐시(언어·글자 크기·폰트·문구별 1회 측정, 모든 그리드 문서가 공유 — 두 번째 열기부터 측정 0회). UI 스레드 전용.
    private static readonly Dictionary<string, double> HeaderWidthCache = new();

    /// <summary>
    /// 머리글 1개의 필요 폭: 텍스트 폭(머리글과 같은 보통 굵기·현재 글자 크기) + 머리글 틀 고정 몫 HeaderChrome.
    /// 틀 몫은 DataGrid Fluent 테마 값이다 — 왼쪽 Padding 12 + 정렬 아이콘 열 MinWidth 32(DataGridSortIconMinWidth) + 세로 구분선 1 = 45px,
    /// 글자 크기와 무관한 고정값(반올림 여유 1). 이전 값(14pt 기준 36px 비례)은 9px 모자라 en 머리글이 잘렸다(Q17).
    /// </summary>
    private const double HeaderChrome = 46;

    private static double HeaderWidth(string text, double size, FontFamily family)
    {
        if (text.Length == 0) { return 0; }
        var key = $"{size}|{family.Name}|{text}";
        if (!HeaderWidthCache.TryGetValue(key, out var w))
        {
            var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface(family, FontStyle.Normal, FontWeight.Normal, FontStretch.Normal), size, Brushes.Black);
            w = Math.Ceiling(ft.WidthIncludingTrailingWhitespace + HeaderChrome);
            HeaderWidthCache[key] = w;
        }

        return w;
    }
}
