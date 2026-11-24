// Snap Assist 세션 상태 줄·썸네일 진단(State·backdrop=<alpha>/<테마> 포함) — 원격 snapassist.* 진단용
using System;
using System.Collections.Generic;
using System.Linq;

namespace Pd.Poc.Shell.Docking;

public static partial class SnapAssistSession
{
    /// <summary>진단 1줄: active, scope, layout, cells, filled, active cell, candidates n, excludedSmall n, thumb, lastEnd.</summary>
    public static string State
    {
        get
        {
            if (s_run is not { } r)
            {
                return $"active=False lastEnd={s_lastEnd} lastSkip={LastSkipReason}";
            }

            var filled = string.Join(",", r.Zones.Where(z => z.Filled).Select(z => z.Cell));
            var skipped = string.Join(",", r.Skipped);
            return $"active=True scope={r.Scope} layout={r.LayoutId} cells={r.Zones.Count} filled=[{filled}] skipped=[{skipped}] activeCell={r.ActiveCell} "
                + $"candidates={r.Current.Candidates.Count} excludedSmall={r.Current.ExcludedSmall} focus={r.Focus} thumb={ThumbField(r)} backdrop={DiagBackdrop ?? "-"} lastEnd={s_lastEnd}";
        }
    }

    /// <summary>
    /// 썸네일 결과를 세션에 알린다(Views → Shell 단방향 이음매 — Shell 은 Views 를 모른다).
    /// status 는 "bitmap" 또는 "fallback:&lt;사유&gt;". 세션이 없거나 키가 비면 무시한다. 같은 키는 마지막 값으로 덮는다.
    /// </summary>
    public static void ReportThumb(string key, string status, string? detail = null)
    {
        if (s_run is not { } r || string.IsNullOrEmpty(key) || string.IsNullOrEmpty(status))
        {
            return;
        }

        r.ThumbStatus[key] = status;
        if (!string.IsNullOrEmpty(detail))
        {
            r.ThumbDetail[key] = detail;
        }
    }

    /// <summary>
    /// 현재 활성 칸 후보별 썸네일 진단(번호·제목·결과·찍은 대상/크기). 세션이 없으면 빈 목록. 원격 snapassist.state 의 thumbs 필드.
    /// </summary>
    public static IReadOnlyList<(int Index, string Title, string Status, string Detail)> ThumbDiagnostics
    {
        get
        {
            if (s_run is not { } r)
            {
                return Array.Empty<(int, string, string, string)>();
            }

            return r.Current.Candidates.Select((c, i) => (i, c.Title,
                r.ThumbStatus.TryGetValue(c.Key, out var s) ? s : "pending",
                r.ThumbDetail.TryGetValue(c.Key, out var d) ? d : string.Empty)).ToList();
        }
    }

    /// <summary>
    /// 진단 전용 이음매(Views 가 등록 — Shell 은 Views 를 모른다). 인자 = 후보 ThumbSource, 저장 경로 앞부분(확장자 없음). 돌려주는 값 = 진단 1줄.
    /// </summary>
    public static Func<object, string, string>? ThumbDumpHook { get; set; }

    /// <summary>
    /// 진단 전용: 비어 있지 않으면 썸네일 찍기를 이 사유로 실패시킨다(웹 폴백 경로·대체 카드 시험). 원격 snapassist.thumbfail 로만 바꾼다.
    /// </summary>
    public static string? DiagThumbFail { get; set; }

    /// <summary>진단: 남은 칸 판을 마지막으로 그린 값(실효 alpha/테마, 예 0.96/Dark — State 의 backdrop 필드, 비침 판정용).</summary>
    public static string? DiagBackdrop { get; set; }

    /// <summary>진단 전용: 활성 칸 후보마다 ThumbDumpHook 을 불러 dir 에 thumb_&lt;번호&gt;_* 로 저장하고 진단 줄을 돌려준다. 세션·이음매가 없으면 빈 목록.</summary>
    public static IReadOnlyList<string> DumpThumbs(string dir)
    {
        if (s_run is not { } r || ThumbDumpHook is not { } hook)
        {
            return Array.Empty<string>();
        }

        var lines = new List<string>();
        for (var i = 0; i < r.Current.Candidates.Count; i++)
        {
            var c = r.Current.Candidates[i];
            if (c.ThumbSource is null)
            {
                lines.Add($"{i} title={c.Title} no-source");
                continue;
            }

            try
            {
                lines.Add($"{i} title={c.Title} " + hook(c.ThumbSource, System.IO.Path.Combine(dir, $"thumb_{i}")));
            }
            catch (Exception ex)
            {
                lines.Add($"{i} title={c.Title} error={ex.GetType().Name}: {ex.Message}");
            }
        }

        return lines;
    }

    /// <summary>thumb 필드: 옵션 끔이면 off, 켜짐이면 현재 후보 순서대로 [i:bitmap|i:fallback:사유|i:pending].</summary>
    private static string ThumbField(Run r)
    {
        if (!r.Thumbnails)
        {
            return "off";
        }

        var cards = r.Current.Candidates.Select((c, i) => $"{i}:{(r.ThumbStatus.TryGetValue(c.Key, out var s) ? s : "pending")}");
        return $"[{string.Join(",", cards)}]";
    }

    /// <summary>마지막 TryStart 가 세션을 띄우지 않은 사유: disabled|max|no-remaining|no-candidates|no-overlay|scope-off|layout-off. 띄웠으면 빈 문자열.</summary>
    public static string LastSkipReason { get; private set; } = string.Empty;

    /// <summary>끝 사유(SnapAssistEndReason): filled|esc|outside|focus|resize|mode|closed|cancel|no-candidates. 오버레이를 숨긴 뒤 1회 낸다.</summary>
    public static event Action<string>? Ended;
}
