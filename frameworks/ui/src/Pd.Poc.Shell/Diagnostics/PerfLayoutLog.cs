// 레이아웃 패스 시간 기록(진단 빌드 전용) — Avalonia 로그(Layout 영역 Information)로 패스마다 걸린 시간·측정/배치 대기 수를 받아 프레임별 layout_pass_* 진단 필드로 낸다
#if PD_DIAGNOSTICS
using System;
using System.Collections.Generic;
using System.Text.Json;
using Avalonia.Logging;
using static Pd.Poc.Shell.Diagnostics.PerfStats;

namespace Pd.Poc.Shell.Diagnostics;

/// <summary>
/// rAF 렌더 시간 안의 UI 레이아웃 몫을 나누려는 진단(판정식·기존 frame_* 필드 불변, 새 필드만 추가).
/// 근거(Avalonia 12.1.4 디컴파일, odev-10): LayoutManager.ExecuteLayoutPass 는 Logger.IsEnabled(Information, "Layout") 이면
/// 패스 시작에 "Started layout pass. To measure: {Measure} To arrange: {Arrange}", 끝에 "Layout pass finished in {Time}"(TimeSpan)를 남긴다.
/// 공개 API RendererDiagnostics.LastLayoutPassTiming 은 화면에 그래프를 그리는 디버그 오버레이(LayoutTimeGraph)를 켤 때만 채워져 측정을 오염시키므로 쓰지 않는다.
/// 기존 로그 싱크는 감싸서 그대로 전달한다(Layout·Information 만 가로챔). UI 스레드 전용(레이아웃 패스는 UI 스레드).
/// </summary>
internal sealed class PerfLayoutLog
{
    private readonly List<double> _perFrameMs = new(); // 프레임 사이에 끝난 레이아웃 패스 시간 합(패스 없는 프레임은 0)
    private double _pendingMs;
    private int _pendingPasses;
    private int _passes;
    private int _framesWithPass;
    private int _maxMeasure;
    private int _maxArrange;
    private bool _installed;

    /// <summary>첫 프레임에 싱크를 건다(앱 시작 중 LogToTrace 등이 Logger.Sink 를 바꾼 뒤에 감싸도록 지연).</summary>
    public void OnFrame()
    {
        if (!_installed)
        {
            _installed = true;
            Logger.Sink = new Sink(this, Logger.Sink);
            return;
        }

        _perFrameMs.Add(_pendingMs);
        if (_pendingPasses > 0) { _framesWithPass++; }
        _pendingMs = 0;
        _pendingPasses = 0;
    }

    public void Reset()
    {
        _perFrameMs.Clear();
        _pendingMs = 0;
        _pendingPasses = 0;
        _passes = 0;
        _framesWithPass = 0;
        _maxMeasure = 0;
        _maxArrange = 0;
    }

    /// <summary>진단 필드: layout_pass_samples·_avg_ms·_p50_ms·_p95_ms·…(프레임당 레이아웃 ms), layout_pass_count, layout_pass_frames, layout_pass_measure_max, layout_pass_arrange_max.</summary>
    public void Write(Utf8JsonWriter w)
    {
        WriteStats(w, "layout_pass", _perFrameMs);
        w.WriteNumber("layout_pass_count", _passes);
        w.WriteNumber("layout_pass_frames", _framesWithPass);
        w.WriteNumber("layout_pass_measure_max", _maxMeasure);
        w.WriteNumber("layout_pass_arrange_max", _maxArrange);
    }

    private void OnStarted(int measure, int arrange)
    {
        _maxMeasure = Math.Max(_maxMeasure, measure);
        _maxArrange = Math.Max(_maxArrange, arrange);
        MdiDragProbe.OnLayoutStarted(measure, arrange); // 끌기 단계 '레이아웃' 할당·대기 수(--drag-probe 일 때만)
        MdiDragFrameDiag.OnLayoutStarted(measure, arrange); // 끌기 프레임별 기록(chrome.frameDiag 켬일 때만, 01a P1)
    }

    private void OnFinished(TimeSpan elapsed)
    {
        _passes++;
        _pendingPasses++;
        _pendingMs += elapsed.TotalMilliseconds;
        MdiDragProbe.OnLayoutFinished(elapsed);
        MdiDragFrameDiag.OnLayoutFinished(elapsed);
    }

    /// <summary>Layout·Information 두 메시지만 가로채고 나머지는 원래 싱크로 넘긴다.</summary>
    private sealed class Sink(PerfLayoutLog owner, ILogSink? inner) : ILogSink
    {
        public bool IsEnabled(LogEventLevel level, string area) =>
            (level == LogEventLevel.Information && area == LogArea.Layout) || (inner?.IsEnabled(level, area) ?? false);

        public void Log(LogEventLevel level, string area, object? source, string messageTemplate)
        {
            if (!Capture(level, area, null)) { inner?.Log(level, area, source, messageTemplate); }
        }

        public void Log(LogEventLevel level, string area, object? source, string messageTemplate, params object?[] propertyValues)
        {
            if (!Capture(level, area, propertyValues)) { inner?.Log(level, area, source, messageTemplate, propertyValues); }
        }

        // 우리 몫이면 기록하고 true. 원래 싱크가 Layout Information 을 원했으면 그쪽에도 넘긴다(false 반환으로)
        private bool Capture(LogEventLevel level, string area, object?[]? values)
        {
            if (level != LogEventLevel.Information || area != LogArea.Layout)
            {
                return false;
            }

            if (values is [TimeSpan t])
            {
                owner.OnFinished(t);
            }
            else if (values is [int m, int a])
            {
                owner.OnStarted(m, a);
            }

            return !(inner?.IsEnabled(level, area) ?? false);
        }
    }
}
#endif
