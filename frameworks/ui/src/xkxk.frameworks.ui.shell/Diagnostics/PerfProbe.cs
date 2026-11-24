// 기동·문서 열기·프레임 시간을 계측해 JSON 으로 내보내는 성능 프로브(데스크톱·브라우저 공용)
#if PD_DIAGNOSTICS // 진단 빌드 전용. 제품 빌드 대체(IsEnabled=false 빈 껍데기)는 PerfProbe.Product.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Avalonia.Controls;
using static xkxk.frameworks.ui.shell.Diagnostics.PerfStats;

namespace xkxk.frameworks.ui.shell.Diagnostics;

/// <summary>
/// 단일 인스턴스 계측기. 모든 메서드는 UI 스레드에서 호출한다.
/// 프레임 시간은 TopLevel.RequestAnimationFrame 콜백 간격(ms)이며, UI 스레드가 바쁘면 간격이 늘어난다.
/// JSON 은 Utf8JsonWriter 로 직접 쓴다(브라우저 트리밍 환경에서 리플렉션 직렬화 회피).
/// 책임 분담: 프레임·구간 PerfFrameLog, 표본 구간·FPS 타이머 PerfSampler, 조작 기록·GC PerfOperations, 문서 열기 PerfOpenTracker, 통계 쓰기 PerfStats.
/// </summary>
public sealed partial class PerfProbe
{
    private const bool Enabled = true;

    public static PerfProbe Current { get; } = new();

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly PerfFrameLog _frameLog;
    private readonly List<(string Step, double Ms)> _steps = new();
    private readonly Dictionary<string, double> _metrics = new();
    private readonly Dictionary<string, string> _texts = new();
    private readonly Dictionary<string, string> _raw = new();
    private readonly PerfOperations _operations = new();
    private readonly PerfSampler _sampler;
    private readonly PerfOpenTracker _opens;

    private TopLevel? _top;
    private bool _running;
    private TimeSpan? _lastTick;
    private double _processStartOffsetMs;
    private double _firstFrameMs = -1;

    private PerfProbe()
    {
        _frameLog = new PerfFrameLog(() => NowMs);
        _sampler = new PerfSampler(this);
        _opens = new PerfOpenTracker(this);
    }

    /// <summary>
    /// 진단 빌드(PdDiagnostics=true → PD_DIAGNOSTICS) 여부. 제품 빌드에서는 false 이며 Attach·rAF·FPS 타이머·조작 계측이 모두 비용 없이 빠진다.
    /// 컴파일 상수라 JIT/AOT 가 분기를 제거한다.
    /// </summary>
    public static bool IsEnabled => Enabled;

    /// <summary>
    /// 측정용 렌더 펌프. 켜면 매 프레임 TopLevel 을 무효화해 렌더 루프가 유휴 정지하지 않게 한다.
    /// 근거: 꺼진 상태에서 변화가 없으면 렌더 루프가 정지·재기동하면서 rAF 간격이 약 2 vsync(74Hz 에서 27~30ms)로 늘어난다.
    /// 그래서 유휴 프레임 시간이 실제 렌더 비용이 아니라 측정 아티팩트가 된다(odev-4b 실측: 펌프 ON 이면 유휴 p50 29.9→13.35ms).
    /// 비용: TopLevel 자체 그리기(배경)만 다시 기록하고 하위 시각 요소의 그리기 목록은 재사용한다(추정).
    /// </summary>
    public bool KeepRendering { get; set; }

    /// <summary>최근 프레임 기준 FPS(지수 평활). 상태바 표시용.</summary>
    public double Fps { get; private set; }

    /// <summary>프레임 수신 시 발생(상태바 갱신용, 매 프레임 호출되므로 가볍게 처리할 것).</summary>
    public event Action? FrameRecorded;

    /// <summary>원격 perf.idle 용(진단 전용): 상태바 FPS 갱신을 멈춘다(타이머는 돌되 글자를 바꾸지 않음).</summary>
    public bool FpsPaused { get; set; }

    /// <summary>원격 perf.idle 응답용 상태.</summary>
    public (bool Probe, bool FpsPaused, int Frames) IdleState => (_running, FpsPaused, _frameLog.Count);

    /// <summary>이름별 보관 상한.</summary>
    public const int MaxOperationsPerName = 2000;

    /// <summary>
    /// 프로세스 시작 시각을 알려 기동 시간 기준점을 앞당긴다(데스크톱: Process.StartTime).
    /// 호출하지 않으면 이 클래스가 처음 쓰인 시점이 기준이다.
    /// </summary>
    public void MarkProcessStart(DateTime processStartUtc)
    {
        var ahead = (DateTime.UtcNow - processStartUtc).TotalMilliseconds - _clock.Elapsed.TotalMilliseconds;
        _processStartOffsetMs = Math.Max(0, ahead);
    }

    /// <summary>기준점 이후 경과 ms.</summary>
    public double NowMs => _clock.Elapsed.TotalMilliseconds + _processStartOffsetMs;

    /// <summary>프레임 타이머 연결. 같은 TopLevel 에 여러 번 호출해도 1회만 동작한다.</summary>
    public void Attach(TopLevel top)
    {
        if (!IsEnabled) { return; } // 제품 빌드: 프레임 계측 없음(유휴 시 rAF 재등록 방지)
        _top = top;
        if (_running)
        {
            return;
        }

        _running = true;
        top.RequestAnimationFrame(OnFrame);
    }

    /// <summary>
    /// 진단 빌드에서만 프레임 계측을 연결하고, onFps 가 있으면 1초마다 현재 FPS 를 넘긴다(상태바 표시용, PerfSampler.StartFpsTimer).
    /// 제품 빌드는 아무것도 하지 않고 null 을 돌려준다(유휴 시 rAF 재등록·타이머 없음). 반환값을 Dispose 하면 FPS 타이머가 멈춘다.
    /// </summary>
    public IDisposable? AttachIfEnabled(TopLevel top, Action<double>? onFps = null)
    {
        if (!IsEnabled)
        {
            return null;
        }

        AttachForSampling(top); // 연속 체인 대신 입력 뒤 짧은 표본 구간만(유휴 연속 렌더 제거 — PerfSampler). --perf-out/--frame-pump 실행은 AutomationBridge 가 연속 Attach
        return onFps is null ? null : _sampler.StartFpsTimer(onFps);
    }

    /// <summary>입력·연결 뒤 짧은 표본 구간만 프레임 체인을 돌린다(PerfSampler).</summary>
    public void AttachForSampling(TopLevel top) => _sampler.AttachForSampling(top);

    /// <summary>표본 구간을 연장하고, 체인이 멈춰 있으면 다시 건다.</summary>
    public void Wake() => _sampler.Wake();

    /// <summary>입력 사건 시각 기록(commit_raw.inputs_ms — B2 누락 공백 지표). 실제 이동한 docs.moveAll·버튼 눌린 PointerMoved 에서 부른다.</summary>
    public void NoteInput() => _frameLog.Commits.NoteInput(NowMs);

    /// <summary>프레임 타이머 정지(종료 직전).</summary>
    public void Stop() => _running = false;

    /// <summary>원격 perf.idle 용: 프레임 콜백 체인 정지(Stop)·재개. 재개는 정지 상태일 때만 마지막 TopLevel 에 다시 건다.</summary>
    public void SetProbeRunning(bool run)
    {
        if (!run) { Stop(); return; }
        ResumeChain();
    }

    internal void SetTop(TopLevel top) => _top = top;

    /// <summary>체인이 멈춰 있으면 마지막 TopLevel 에 다시 건다(Wake·SetProbeRunning 공용).</summary>
    internal void ResumeChain()
    {
        if (_top is not null && !_running) { Attach(_top); }
    }

    /// <summary>표본 구간 끝: 체인을 멈추고 다음 구간 첫 간격에 유휴 시간이 섞이지 않게 마지막 tick 을 잊는다.</summary>
    internal void StopAndForgetTick()
    {
        Stop();
        _lastTick = null;
    }

    private void OnFrame(TimeSpan tick)
    {
        if (_firstFrameMs < 0)
        {
            _firstFrameMs = NowMs;
        }

        _frameLog.Commits.EnsureHooked(_top); // AfterCommit 구독(첫 틱 + 재시도 1회, PerfCommitLog)
        var committed = _frameLog.Commits.TakeCommittedSinceLastTick(); // 간격을 기록하지 않는 틱(첫 틱·dt≤0·Full)에서도 카운터를 비운다
        if (_lastTick is { } last && !_frameLog.Full)
        {
            var dt = (tick - last).TotalMilliseconds;
            if (dt > 0)
            {
                if (dt > 1000) { Console.WriteLine($"[perf] {DateTime.Now:HH:mm:ss.fff} frame gap {dt:0}ms phase={_frameLog.PhaseName}"); } // UI 스레드 정지 구간 추적
                _frameLog.Add(dt, committed);
                _operations.NoteFrameGc(dt); // 긴 프레임에 GC 가 겹쳤는지(PerfOperations)
                var fps = 1000.0 / dt;
                Fps = Fps <= 0 ? fps : (Fps * 0.9) + (fps * 0.1);
            }
        }

        _lastTick = tick;
        FrameRecorded?.Invoke();
        if (_running && _top is not null)
        {
            if (KeepRendering) { _top.InvalidateVisual(); }
            _top.RequestAnimationFrame(OnFrame);
        }
    }

    /// <summary>프레임 구간 이름 지정. 이전 구간은 자동 종료된다. null 이면 종료만(PerfFrameLog).</summary>
    public void BeginPhase(string? name) => _frameLog.BeginPhase(name);

    /// <summary>문서 뷰 생성 시점 기록. kind 는 grid|gantt|form.</summary>
    public void BeginOpen(string kind)
    {
        if (!IsEnabled) { return; } // 제품 빌드: 짝(EndOpen) 없이 대기열이 쌓이지 않게
        _opens.Begin(kind);
    }

    /// <summary>문서 뷰 첫 렌더 시점 기록(같은 kind 의 가장 오래된 BeginOpen 과 짝짓는다).</summary>
    public void EndOpen(string kind) => _opens.End(kind);

    /// <summary>뷰 생성자에서 호출: 첫 프레임이 실제로 그려진 다음 EndOpen 한다(PerfOpenTracker.Track).</summary>
    /// <summary>true 인 동안 만든 뷰는 열기 기록을 하지 않는다(예열·준비된 표 뷰 — 화면 밖에 붙였다 떼는 뷰가 doc.open 으로 잡히지 않게, A16).</summary>
    public static bool SuppressOpenTrack { get; set; }

    public void TrackOpen(Control view, string kind)
    {
        if (!IsEnabled || SuppressOpenTrack) { return; } // 제품 빌드: 열기 계측·구독 없음
        _opens.Track(view, kind);
    }

    // 단계·지표 기록과 조작 계측(RecordStep·SetMetric·TimeOperation·RecordOperation 등)은 PerfProbe.Counters.cs

    /// <summary>계측 결과 JSON.</summary>
    public string ToJson(string platform)
    {
        BeginPhase(_frameLog.PhaseName); // 진행 중 구간을 현재 프레임까지 확정(구간은 이어서 유지)
        var opens = _opens.Opens;
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            w.WriteString("platform", platform);
            w.WriteString("timestamp", DateTime.Now.ToString("o", CultureInfo.InvariantCulture));
            foreach (var kv in _texts) { w.WriteString(kv.Key, kv.Value); }
            w.WriteNumber("frame_pump", KeepRendering ? 1 : 0); // 1 = 연속 렌더 기준 프레임 간격(유휴 정지 아티팩트 제거)
            // startup_ms = 기준점(MarkProcessStart 시 프로세스 시작) → 첫 애니메이션 프레임
            w.WriteNumber("startup_ms", Round(_firstFrameMs));
            w.WriteNumber("first_frame_ms", Round(_firstFrameMs));
            w.WriteNumber("grid_rows", Metric("grid_rows"));
            w.WriteNumber("gantt_tasks", Metric("gantt_tasks"));
            foreach (var kv in _metrics.Where(k => k.Key is not ("grid_rows" or "gantt_tasks")))
            {
                w.WriteNumber(kv.Key, Round(kv.Value));
            }

            _frameLog.WriteFrameStats(w, NowMs); // frame_* + commit_source·commit_raw
            w.WriteNumber("open_count", opens.Count);
            w.WriteNumber("open_ms", opens.Count == 0 ? 0 : Round(opens.Average(o => o.Ms))); // 문서 1개 평균 열기 시간
            WriteStats(w, "open", opens.Select(o => o.Ms).ToList());
            WriteNamedMs(w, "opens", "kind", opens);
            _frameLog.WritePhases(w); // phases 배열(PerfFrameLog)
            WriteNamedMs(w, "steps", "step", _steps);
            _operations.Write(w); // operations 배열 + gc 객체(PerfOperations)
            foreach (var kv in _raw) { w.WritePropertyName(kv.Key); w.WriteRawValue(kv.Value); } // 다른 모듈이 만든 JSON 객체(예: theme)
            w.WriteEndObject();
        }

        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private double Metric(string key) => _metrics.TryGetValue(key, out var v) ? v : 0;
}
#endif
