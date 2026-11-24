// UI 스레드 타이머 시계 — 진행 표시와 배치 복구 디바운스가 함께 쓰는 IBusyClock 실구현
using System;
using System.Diagnostics;
using Avalonia.Threading;

namespace Pd.Poc.Shell.Busy;

/// <summary>Dispatcher 타이머로 예약하는 시계. 시험은 가짜 시계를 넣는다.</summary>
public sealed class DispatcherBusyClock : IBusyClock
{
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    public long NowMs => _clock.ElapsedMilliseconds;

    public IDisposable Schedule(int delayMs, Action callback) =>
        DispatcherTimer.RunOnce(callback, TimeSpan.FromMilliseconds(Math.Max(1, delayMs)), DispatcherPriority.Normal);
}
