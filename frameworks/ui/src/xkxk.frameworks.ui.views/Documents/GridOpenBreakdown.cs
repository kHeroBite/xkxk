// 그리드 첫 Measure(measure1) 안의 시간을 행 템플릿·셀 템플릿·셀 내용 생성·셀 글자 측정·나머지로 나눠 재는 진단 계측(A16, 진단 빌드 전용)
using System.Diagnostics;
#if PD_DIAGNOSTICS
using Avalonia;
using Avalonia.Controls;
using xkxk.frameworks.ui.shell.Diagnostics;
#endif

namespace xkxk.frameworks.ui.views.Documents;

/// <summary>
/// GridLayoutProbe 가 첫 Measure 앞뒤로 Begin/End 를 부르고, 그 사이 PdRowTemplate·PdCellTemplate·PdTextColumn·셀 글자(CellText)가
/// 자기 구간 시간을 더한다. End 는 perf.ops 에 grid.open.m1.{part}.{first|next} 를, 지표에 grid_m1_{part}_n(호출 수)을 남긴다.
/// part: rowtpl(행 템플릿 Build), celltpl(셀 템플릿 Build — 미룬 내용 생성 포함), content(셀 내용 TextBlock 생성 — 단독·celltpl 안 모두),
/// textmeasure(셀 TextBlock 측정 = 글자 배치·모양 잡기), other(measure1 − rowtpl − celltpl − content(celltpl 밖) − textmeasure = 셀·행 객체 생성,
/// 스타일 해석, 바인딩, 그 밖의 측정).
/// 메서드는 Conditional 이라 제품 빌드에서는 호출째 빠진다(기존 perf.ops 계측과 같은 규칙). 중첩 구간은 깊이 0 일 때만 시작 시각을 잡는다.
/// </summary>
internal static class GridOpenBreakdown
{
#if PD_DIAGNOSTICS
    // TplAttach = 셀 템플릿 Build 끝 → 뿌리(PdCellChrome) 측정 시작(템플릿 부착·이름·시각 트리 연결·스타일), ChromeMeasure = 뿌리 측정 전체(textmeasure 포함 — other 계산에서 뺀다, K15)
    internal enum Part { RowTemplate, CellTemplate, Content, TextMeasure, TplAttach, ChromeMeasure, Count }

    private static readonly string[] Names = { "rowtpl", "celltpl", "content", "textmeasure", "tplattach", "chromemeasure" };
    private static long s_buildEnd;

    /// <summary>셀 템플릿 Build 끝 시각(PdCellTemplate). 다음 뿌리 측정 시작이 꺼내 간다.</summary>
    internal static void MarkBuildEnd() => s_buildEnd = s_active ? Stopwatch.GetTimestamp() : 0;

    /// <summary>직전 Build 끝 시각을 꺼내고 지운다(PdCellChrome 측정 시작). 없으면 0.</summary>
    internal static long TakeBuildEnd()
    {
        var t = s_buildEnd;
        s_buildEnd = 0;
        return t;
    }
    private static readonly long[] s_ticks = new long[(int)Part.Count];
    private static readonly int[] s_calls = new int[(int)Part.Count];
    private static long s_contentInCellTpl; // celltpl 안에서 만든 내용 시간(나머지 계산 때 이중 차감 방지)
    private static int s_cellTplDepth;
    private static bool s_active;

    /// <summary>구간 시작 시각(활성이 아니면 0 — End 쪽에서 무시).</summary>
    internal static long Start() => s_active ? Stopwatch.GetTimestamp() : 0;

    internal static void Add(Part part, long start)
    {
        if (!s_active || start == 0) { return; }
        var now = Stopwatch.GetTimestamp();
        var d = now - start;
        s_ticks[(int)part] += d;
        s_calls[(int)part]++;
        if (part == Part.Content && s_cellTplDepth > 0) { s_contentInCellTpl += d; }
        if (part == Part.RowTemplate) { s_events.Add(('r', start)); s_events.Add(('R', now)); }
        else if (part != Part.Content) { s_events.Add(('X', now)); } // 셀 단위 사건 끝(셀 템플릿·글자 측정)
    }

    // 행 단위 타임라인: L = LoadingRow(행·셀 객체 생성 끝, 트리 부착 전), r/R = 행 템플릿 시작/끝(행 측정 안), X = 셀 사건 끝
    private static readonly System.Collections.Generic.List<(char Kind, long T)> s_events = new();
    private static long s_begin;

    /// <summary>LoadingRow 시각(PdDataGrid 가 부른다). DataGrid 12.1.2 GenerateRow 는 행·셀 10개를 만들고 이 사건을 낸 뒤 행을 표시기에 붙이고 측정한다(디컴파일 확인).</summary>
    internal static void MarkLoadingRow()
    {
        if (s_active) { s_events.Add(('L', Stopwatch.GetTimestamp())); }
    }

    /// <summary>t_pre 분해 표식(K15): T = 표 템플릿 적용 끝(PdDataGrid.OnApplyTemplate), G = 표 MeasureOverride 시작.</summary>
    internal static void Mark(char kind)
    {
        if (s_active) { s_events.Add((kind, Stopwatch.GetTimestamp())); }
    }

    /// <summary>t_pre 를 시작→T(뷰 측정·표 템플릿 생성)·T→G(템플릿 뒤 측정 진입 전)·G→첫 L(머리글·열 준비·첫 행 생성)로 나눈다. 표식이 없으면 앞 구간에 합친다.</summary>
    private static void RecordPre(PerfProbe perf, string tag)
    {
        static double Ms(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;
        long t = -1, g = -1, l = -1;
        foreach (var (kind, at) in s_events)
        {
            if (kind == 'L') { l = at; break; }
            if (kind == 'T' && t < 0) { t = at; }
            if (kind == 'G' && g < 0) { g = at; }
        }

        if (l < 0) { return; }
        var tt = t < 0 ? s_begin : t;
        var gg = g < 0 ? tt : g;
        perf.RecordOperation($"grid.open.m1.pre_tpl.{tag}", Ms(tt - s_begin), Ms(tt - s_begin));
        perf.RecordOperation($"grid.open.m1.pre_enter.{tag}", Ms(gg - tt), Ms(gg - tt));
        perf.RecordOperation($"grid.open.m1.pre_rows.{tag}", Ms(l - gg), Ms(l - gg));
    }

    /// <summary>
    /// 타임라인을 단계 합으로 바꾼다. pre = 시작→첫 L(머리글·열 준비 + 첫 행 생성), gen = 직전 행 마지막 사건→L(다음 행·셀 생성),
    /// attach = L→r(행 부착·행/셀 스타일 해석·바인딩), cells = R→그 행 마지막 사건(셀 부착 뒤 측정·셀 템플릿·글자), post = 마지막 사건→끝.
    /// </summary>
    private static void RecordTimeline(PerfProbe perf, string tag, long end)
    {
        double Ms(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;
        long pre = 0, gen = 0, attach = 0, cells = 0, post;
        var last = s_begin;
        var rows = 0;
        long rowStart = -1, rStart = -1, rEnd = -1;
        void CloseRow()
        {
            if (rowStart < 0) { return; }
            if (rStart >= 0) { attach += rStart - rowStart; }
            if (rEnd >= 0) { cells += last - rEnd; }
        }

        foreach (var (kind, t) in s_events)
        {
            if (kind == 'L')
            {
                CloseRow();
                if (rows == 0) { pre = t - s_begin; } else { gen += t - last; }
                rows++;
                rowStart = t;
                rStart = rEnd = -1;
            }
            else if (kind == 'r' && rowStart >= 0 && rStart < 0) { rStart = t; }
            else if (kind == 'R' && rowStart >= 0 && rEnd < 0) { rEnd = t; }

            last = t;
        }

        CloseRow();
        post = end - last;
        perf.RecordOperation($"grid.open.m1.t_pre.{tag}", Ms(pre), Ms(pre));
        perf.RecordOperation($"grid.open.m1.t_gen.{tag}", Ms(gen), Ms(gen));
        perf.RecordOperation($"grid.open.m1.t_attach.{tag}", Ms(attach), Ms(attach));
        perf.RecordOperation($"grid.open.m1.t_cells.{tag}", Ms(cells), Ms(cells));
        perf.RecordOperation($"grid.open.m1.t_post.{tag}", Ms(post), Ms(post));
        perf.SetMetric("grid_m1_rows_n", rows);
        RecordPre(perf, tag);
    }

    internal static void EnterCellTemplate() => s_cellTplDepth++;

    internal static void LeaveCellTemplate() => s_cellTplDepth--;
#endif

    [Conditional("PD_DIAGNOSTICS")]
    internal static void Begin()
    {
#if PD_DIAGNOSTICS
        System.Array.Clear(s_ticks);
        System.Array.Clear(s_calls);
        s_buildEnd = 0;
        s_contentInCellTpl = 0;
        s_cellTplDepth = 0;
        s_events.Clear();
        s_begin = Stopwatch.GetTimestamp();
        s_active = true;
#endif
    }

    [Conditional("PD_DIAGNOSTICS")]
    internal static void End(string tag, double measureMs)
    {
#if PD_DIAGNOSTICS
        if (!s_active) { return; }
        s_active = false;
        var perf = PerfProbe.Current;
        RecordTimeline(perf, tag, Stopwatch.GetTimestamp());
        double Ms(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;
        var known = 0.0;
        for (var i = 0; i < Names.Length; i++)
        {
            var ms = Ms(s_ticks[i]);
            perf.RecordOperation($"grid.open.m1.{Names[i]}.{tag}", ms, ms);
            perf.SetMetric($"grid_m1_{Names[i]}_n", s_calls[i]);
            if (i != (int)Part.ChromeMeasure) { known += ms; } // 뿌리 측정은 textmeasure 를 품으므로 나머지 계산에서 뺀다
        }

        known -= Ms(s_contentInCellTpl); // celltpl 이 이미 포함한 내용 시간
        var other = System.Math.Max(0, measureMs - known);
        perf.RecordOperation($"grid.open.m1.other.{tag}", other, other);
#endif
    }
}

#if PD_DIAGNOSTICS
/// <summary>진단 빌드의 셀 글자 — 측정 시간만 GridOpenBreakdown 에 더한다. 스타일 키는 TextBlock 이라 TextBlock.cell 스타일이 그대로 걸린다.</summary>
internal sealed class CellText : TextBlock
{
    protected override System.Type StyleKeyOverride => typeof(TextBlock);

    protected override Size MeasureOverride(Size availableSize)
    {
        var t = GridOpenBreakdown.Start();
        var size = base.MeasureOverride(availableSize);
        GridOpenBreakdown.Add(GridOpenBreakdown.Part.TextMeasure, t);
        return size;
    }
}
#endif
