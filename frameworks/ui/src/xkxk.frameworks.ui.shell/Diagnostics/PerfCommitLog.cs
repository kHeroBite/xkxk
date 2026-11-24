// 프레임 간격별 커밋 여부·입력/커밋 시각 원자료 기록(진단 빌드 전용) — Compositor internal AfterCommit 리플렉션 구독, perf.get commit_raw 출력
#if PD_DIAGNOSTICS
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Rendering.Composition;

namespace xkxk.frameworks.ui.shell.Diagnostics;

/// <summary>
/// B2 커밋 프레임 지표용 원자료(계획 conv_179435617365 §5 계약). 집계·판정은 판정기(poc/ui/test/desktop/judge/b2_commit_frames.py)가 한다.
/// 근거(Avalonia 12.1.4 디컴파일): MediaContext.RenderCore 는 rAF 콜백 뒤 같은 차례에 커밋하고, 커밋할 것이 없으면 16ms 타이머로만 다시 깬다.
/// 그래서 틱 N 과 N+1 사이에 AfterCommit 이 있었는지로 '그린 간격'과 '유휴 틱'을 가른다.
/// AfterCommit 은 화면 표시(present) 시점이 아니라 배치를 렌더 스레드로 넘긴 시점이다. 공개 API 로는 자연 커밋을 관찰할 수 없어(공개 요청 API 는
/// 스스로 커밋을 일으켜 측정을 바꾼다) internal 이벤트를 리플렉션으로 구독한다. 못 찾으면 예외 없이 Source="unavailable:&lt;사유&gt;" 로 남긴다.
/// Compositor 를 여러 TopLevel 이 공유하면 다른 창 커밋이 섞일 수 있다(B2 메인 창 MDI 범위에서 수용하는 한계). UI 스레드 전용.
/// </summary>
internal sealed class PerfCommitLog
{
    private const int MaxRaw = 5000;
    private const int MaxHookAttempts = 2; // 첫 시도 + 재시도 1회(첫 틱에 시각 요소가 아직 없을 수 있음)

    private readonly Func<double> _now;
    private readonly List<double> _inputs = new();
    private readonly List<double> _commits = new();
    private int _sinceTick;
    private int _hookAttempts;
    private bool _hooked;
    private bool _truncated;

    public PerfCommitLog(Func<double> now) => _now = now;

    /// <summary>"after_commit"(구독됨) 또는 "unavailable:&lt;사유&gt;". 판정기는 after_commit 이 아니면 미측정 FAIL 로 본다.</summary>
    public string Source { get; private set; } = "unavailable:not_attached";

    /// <summary>TopLevel 의 Compositor 에 AfterCommit 을 1회 구독한다. 이미 구독했거나 시도 한도를 넘었으면 아무것도 하지 않는다(매 틱 호출해도 가볍다).</summary>
    [DynamicDependency(DynamicallyAccessedMemberTypes.NonPublicEvents | DynamicallyAccessedMemberTypes.NonPublicMethods, typeof(Compositor))]
    public void EnsureHooked(TopLevel? top)
    {
        if (_hooked || _hookAttempts >= MaxHookAttempts || top is null)
        {
            return;
        }

        _hookAttempts++;
        try
        {
            if (ElementComposition.GetElementVisual(top)?.Compositor is not { } compositor)
            {
                Source = "unavailable:no_compositor";
                return;
            }

            var add = typeof(Compositor).GetEvent("AfterCommit", BindingFlags.Instance | BindingFlags.NonPublic)?.GetAddMethod(nonPublic: true);
            if (add is null)
            {
                Source = "unavailable:no_after_commit_event";
                return;
            }

            add.Invoke(compositor, new object[] { (Action)OnCommit });
            _hooked = true;
            Source = "after_commit";
        }
        catch (Exception ex)
        {
            Source = "unavailable:" + ex.GetType().Name; // 진단 계측이 앱을 멈추지 않게 삼킨다
        }
    }

    private void OnCommit()
    {
        _sinceTick++;
        Append(_commits, _now());
        MdiDragFrameDiag.OnCommit(); // 끌기 프레임별 rAF→커밋 구간(chrome.frameDiag 켬일 때만, 01a P1)
    }

    /// <summary>직전 틱 이후 커밋이 1회 이상 있었는지 돌려주고 카운터를 비운다. 간격을 기록하지 않는 틱(첫 틱·dt≤0·상한)에서도 불러야 한다.</summary>
    public bool TakeCommittedSinceLastTick()
    {
        var committed = _sinceTick > 0;
        _sinceTick = 0;
        return committed;
    }

    /// <summary>입력 사건 시각(실제 이동한 docs.moveAll·버튼 눌린 PointerMoved).</summary>
    public void NoteInput(double now) => Append(_inputs, now);

    /// <summary>perf.reset: 입력·커밋 시각을 비운다. 틱 사이 카운터는 진행 중 간격의 커밋 여부라 유지한다.</summary>
    public void Reset()
    {
        _inputs.Clear();
        _commits.Clear();
        _truncated = false;
    }

    /// <summary>commit_source·commit_raw(dt_ms·committed·inputs_ms·commits_ms·now_ms·truncated) 쓰기. dts·committed 는 같은 길이(PerfFrameLog).</summary>
    public void Write(Utf8JsonWriter w, double now, IReadOnlyList<double> dts, IReadOnlyList<bool> committed)
    {
        var n = Math.Min(Math.Min(dts.Count, committed.Count), MaxRaw);
        w.WriteString("commit_source", Source);
        w.WriteStartObject("commit_raw");
        w.WriteStartArray("dt_ms");
        for (var i = 0; i < n; i++) { w.WriteNumberValue(Math.Round(dts[i], 2)); }
        w.WriteEndArray();
        w.WriteStartArray("committed");
        for (var i = 0; i < n; i++) { w.WriteNumberValue(committed[i] ? 1 : 0); }
        w.WriteEndArray();
        WriteTimes(w, "inputs_ms", _inputs);
        WriteTimes(w, "commits_ms", _commits);
        w.WriteNumber("now_ms", Math.Round(now, 2));
        w.WriteBoolean("truncated", _truncated || dts.Count > MaxRaw || dts.Count != committed.Count);
        w.WriteEndObject();
    }

    private static void WriteTimes(Utf8JsonWriter w, string name, List<double> times)
    {
        w.WriteStartArray(name);
        foreach (var t in times) { w.WriteNumberValue(Math.Round(t, 2)); }
        w.WriteEndArray();
    }

    private void Append(List<double> list, double t)
    {
        if (list.Count < MaxRaw) { list.Add(t); }
        else { _truncated = true; }
    }
}
#endif
