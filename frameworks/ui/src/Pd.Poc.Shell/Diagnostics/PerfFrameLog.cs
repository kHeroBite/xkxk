// 프레임 시간 기록(진단 빌드 전용) — rAF 간격 ms 목록(상한 5만)과 이름 붙은 구간(phase), frame_* 통계·phases 배열 JSON 출력
#if PD_DIAGNOSTICS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using static Pd.Poc.Shell.Diagnostics.PerfStats;

namespace Pd.Poc.Shell.Diagnostics;

/// <summary>PerfProbe 가 소유하는 프레임·구간 기록. 공개 진입점(BeginPhase)은 PerfProbe 에 있다. UI 스레드 전용.</summary>
internal sealed class PerfFrameLog
{
    private const int MaxFrames = 50_000;
    private readonly List<double> _frames = new();
    private readonly List<bool> _committed = new(); // _frames 와 같은 길이 — 그 간격 안에 AfterCommit 이 있었는가(commit_raw.committed)
    private readonly List<(string Name, int Start, int End)> _phases = new();
    private int _phaseStart;
    private readonly PerfLayoutLog _layout = new(); // 프레임별 레이아웃 패스 시간(진단 필드 layout_pass_*, 판정식 불변)

    public PerfFrameLog(Func<double> now) => Commits = new PerfCommitLog(now);

    /// <summary>커밋 사건·입력 시각 원자료(PerfCommitLog — B2 커밋 프레임 지표).</summary>
    public PerfCommitLog Commits { get; }

    /// <summary>진행 중 구간 이름(없으면 null).</summary>
    public string? PhaseName { get; private set; }

    public int Count => _frames.Count;

    /// <summary>상한(MaxFrames)에 닿아 더 기록하지 않는가.</summary>
    public bool Full => _frames.Count >= MaxFrames;

    public void Add(double dt, bool committed)
    {
        _frames.Add(dt);
        _committed.Add(committed);
        _layout.OnFrame();
    }

    /// <summary>프레임 표본·구간 기록을 비운다(원격 perf.reset — 시행별 frame_p50/p95/max 를 누적 없이 받기 위함). 진행 중 구간 이름은 유지하고 시작점만 0 으로 맞춘다.</summary>
    public void Reset()
    {
        _frames.Clear();
        _committed.Clear();
        _phases.Clear();
        _phaseStart = 0;
        _layout.Reset();
        Commits.Reset(); // commit_raw 도 함께 비운다(계약 §5)
    }

    /// <summary>프레임 구간 이름 지정. 이전 구간은 자동 종료된다. null 이면 종료만.</summary>
    public void BeginPhase(string? name)
    {
        if (PhaseName is not null)
        {
            _phases.Add((PhaseName, _phaseStart, _frames.Count));
        }

        PhaseName = name;
        _phaseStart = _frames.Count;
    }

    /// <summary>전체 프레임 통계(frame_samples·frame_avg_ms·…).</summary>
    public void WriteFrameStats(Utf8JsonWriter w, double nowMs)
    {
        WriteStats(w, "frame", _frames);
        WriteHistogram(w);
        _layout.Write(w); // 추가 필드 layout_pass_*(접두가 frame_ 이 아니라 frame_* 를 훑는 기존 도구에 걸리지 않는다)
        Commits.Write(w, nowMs, _frames, _committed); // 추가 키 commit_source·commit_raw(frame_ 접두 아님, 기존 키·값 불변)
    }

    /// <summary>
    /// 추가 필드(판정식·기존 필드 불변): 프레임 간격 구간별 개수 frame_hist_*. 긴 간격이 타이머 분해능 2틱(31.25ms = 15.625×2)과
    /// 60Hz 2 vsync(33.33ms) 중 어디에 몰리는지 가르려고 30~32·32~34.5 경계를 둔다. 평평한 숫자 필드라 frame_* 를 훑는 기존 도구가 깨지지 않는다.
    /// </summary>
    private void WriteHistogram(Utf8JsonWriter w)
    {
        int le17 = 0, to30 = 0, to32 = 0, to345 = 0, to50 = 0, gt50 = 0;
        foreach (var dt in _frames)
        {
            if (dt <= 17) { le17++; }
            else if (dt <= 30) { to30++; }
            else if (dt <= 32) { to32++; }
            else if (dt <= 34.5) { to345++; }
            else if (dt <= 50) { to50++; }
            else { gt50++; }
        }

        w.WriteNumber("frame_hist_le17", le17);
        w.WriteNumber("frame_hist_17_30", to30);
        w.WriteNumber("frame_hist_30_32", to32);
        w.WriteNumber("frame_hist_32_345", to345);
        w.WriteNumber("frame_hist_345_50", to50);
        w.WriteNumber("frame_hist_gt50", gt50);
    }

    /// <summary>phases: [{name, frame_*, phase_raw{dt_ms,committed}}] 배열. phase_raw 는 구간 안 커밋 프레임 판정용 추가 필드다(기존 키·값 불변, 커밋 판정기가 훑는 commit_raw 와 이름이 다르다).</summary>
    public void WritePhases(Utf8JsonWriter w)
    {
        w.WriteStartArray("phases");
        foreach (var (name, start, end) in MergedPhases())
        {
            w.WriteStartObject();
            w.WriteString("name", name);
            WriteStats(w, "frame", _frames.GetRange(start, end - start));
            w.WriteStartObject("phase_raw");
            w.WriteStartArray("dt_ms");
            for (var i = start; i < end; i++) { w.WriteNumberValue(Math.Round(_frames[i], 2)); }
            w.WriteEndArray();
            w.WriteStartArray("committed");
            for (var i = start; i < end; i++) { w.WriteNumberValue(_committed[i] ? 1 : 0); }
            w.WriteEndArray();
            w.WriteEndObject();
            w.WriteEndObject();
        }

        w.WriteEndArray();
    }

    /// <summary>같은 이름이 연속 기록된 구간(ToJson 재호출 등)을 하나로 합친다.</summary>
    private IEnumerable<(string Name, int Start, int End)> MergedPhases()
    {
        var merged = new List<(string Name, int Start, int End)>();
        foreach (var p in _phases.Where(p => p.End > p.Start))
        {
            if (merged.Count > 0 && merged[^1].Name == p.Name && merged[^1].End == p.Start)
            {
                merged[^1] = (p.Name, merged[^1].Start, p.End);
            }
            else
            {
                merged.Add(p);
            }
        }

        return merged;
    }
}
#endif
