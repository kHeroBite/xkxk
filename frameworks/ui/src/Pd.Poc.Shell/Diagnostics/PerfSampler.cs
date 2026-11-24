// 진단판 프레임 표본 구간 관리(진단 빌드 전용) — 연결 직후·입력 뒤 2초만 프레임 체인을 돌리고 상태바 FPS 타이머를 그 구간에만 갱신한다
#if PD_DIAGNOSTICS
using System;
using Avalonia.Controls;
using Avalonia.Threading;

namespace Pd.Poc.Shell.Diagnostics;

/// <summary>
/// 진단판 유휴 연속 렌더 제거(lead 조건부 승인, otest-1 i1300: 진단판만 유휴 ~42 프레임/초·CPU 0.2~0.9초/10초, 제품판 0).
/// 프레임 체인을 상시 걸지 않고, 연결 직후와 입력(포인터 이동·누름·휠·키) 뒤 SampleWindowMs 동안만 돌린다 — 조작 중 상태바 FPS 는 계속 갱신된다.
/// KeepRendering(--perf-out·--frame-pump on)이면 지금처럼 연속이다(AutomationBridge 가 Attach). PerfProbe 가 소유한다.
/// </summary>
internal sealed class PerfSampler
{
    /// <summary>입력·연결 뒤 프레임을 표본으로 잴 구간(ms). 끝나면 체인을 멈춘다(KeepRendering 이면 멈추지 않음).</summary>
    private const double SampleWindowMs = 2000;

    private readonly PerfProbe _probe;
    private double _sampleUntil = -1;
    private bool _sampling; // AttachForSampling 연결 여부(입력 훅·자동 정지 1회 설치)

    public PerfSampler(PerfProbe probe) => _probe = probe;

    /// <summary>
    /// 1초마다 onFps 로 현재 FPS 를 넘기는 타이머를 시작한다. 반환값을 Dispose 하면 멈춘다.
    /// 상태바 FPS 는 표본 구간 안(마지막 입력·측정 대상 원격 조작 뒤 SampleWindowMs)이나 연속 계측(KeepRendering)일 때만 갱신한다(lead 승인).
    /// 체인이 입력으로만 깨므로 글자 갱신이 체인을 다시 깨우는 자기 유지 루프가 없다. 이전의 '1초 30 프레임' 게이트는 웹에서 probe 가
    /// 실제 렌더와 따로 초당 약 5.5번만 돌아 FPS 가 아예 표시되지 않던 문제(otest-w-2 i1350 nosel)가 있어 바꿨다. 웹 FPS 값은 참고치다.
    /// </summary>
    public IDisposable StartFpsTimer(Action<double> onFps)
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) =>
        {
            var busy = _probe.KeepRendering || (_sampleUntil >= 0 && _probe.NowMs <= _sampleUntil);
            if (busy && !_probe.FpsPaused) { onFps(_probe.Fps); }
        };
        timer.Start();
        return new TimerStopper(timer);
    }

    // (이전 FpsDisplayMinFrames 게이트는 표본 구간 기준으로 바뀌어 없앴다 — 위 StartFpsTimer 주석)

    /// <summary>TopLevel 을 기억하고 입력 훅·자동 정지를 1회 설치한 뒤 첫 표본 구간을 연다.</summary>
    public void AttachForSampling(TopLevel top)
    {
        _probe.SetTop(top);
        if (!_sampling)
        {
            _sampling = true;
            _probe.FrameRecorded += StopWhenSampleEnds;
            void Touch(object? s, Avalonia.Interactivity.RoutedEventArgs e) => Wake();
            void Move(object? s, Avalonia.Input.PointerEventArgs e)
            {
                Wake();
                if (IsAnyButtonPressed(e.GetCurrentPoint(null).Properties)) { _probe.NoteInput(); } // 실제 끌기만 입력 사건(commit_raw.inputs_ms — B2 누락 공백 지표)
            }

            const Avalonia.Interactivity.RoutingStrategies Tunnel = Avalonia.Interactivity.RoutingStrategies.Tunnel;
            top.AddHandler(Avalonia.Input.InputElement.PointerMovedEvent, Move, Tunnel, handledEventsToo: true);
            top.AddHandler(Avalonia.Input.InputElement.PointerPressedEvent, Touch, Tunnel, handledEventsToo: true);
            top.AddHandler(Avalonia.Input.InputElement.PointerWheelChangedEvent, Touch, Tunnel, handledEventsToo: true);
            top.AddHandler(Avalonia.Input.InputElement.KeyDownEvent, Touch, Tunnel, handledEventsToo: true);
        }

        Wake(); // 첫 프레임(기동 시간 기준점)을 잡도록 연결 직후 1구간
    }

    /// <summary>표본 구간을 연장하고, 체인이 멈춰 있으면 다시 건다.</summary>
    public void Wake()
    {
        _sampleUntil = _probe.NowMs + SampleWindowMs;
        _probe.ResumeChain();
    }

    private static bool IsAnyButtonPressed(Avalonia.Input.PointerPointProperties p)
        => p.IsLeftButtonPressed || p.IsRightButtonPressed || p.IsMiddleButtonPressed || p.IsXButton1Pressed || p.IsXButton2Pressed;

    private void StopWhenSampleEnds()
    {
        if (!_probe.KeepRendering && _sampleUntil >= 0 && _probe.NowMs > _sampleUntil) { _probe.StopAndForgetTick(); } // 다음 구간 첫 간격에 유휴 시간이 섞이지 않게
    }

    private sealed class TimerStopper : IDisposable
    {
        private DispatcherTimer? _timer;

        public TimerStopper(DispatcherTimer timer) => _timer = timer;

        public void Dispose()
        {
            _timer?.Stop();
            _timer = null;
        }
    }
}
#endif
