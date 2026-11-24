// 배치 뒤로 미루는 UI 작업의 우선순위 — Android 의 Background 는 유휴 기기에서도 약 2초 뒤에 돌아, 사용자에게 보이는 작업은 여기를 거친다
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Avalonia.Threading;

namespace Pd.Poc.Shell.Platform;

/// <summary>
/// Android 만 Loaded 로 올리고 Desktop·웹은 Background 그대로 둔다. Loaded 는 입력 처리보다 먼저 돌므로 오래 걸리는 작업은 조각내어 올린다.
/// </summary>
public static class UiDeferred
{
    /// <summary>지금 플랫폼에서 쓸 우선순위.</summary>
    public static DispatcherPriority Priority =>
#if PD_DIAGNOSTICS
        ForceBackground ? DispatcherPriority.Background :
#endif
        OperatingSystem.IsAndroid() ? DispatcherPriority.Loaded : DispatcherPriority.Background;

    /// <summary>action 을 지금 작업과 배치 뒤로 미룬다.</summary>
    public static void Post(Action action, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
    {
#if PD_DIAGNOSTICS
        var start = System.Diagnostics.Stopwatch.GetTimestamp();
        var site = System.IO.Path.GetFileName(file) + ":" + line;
        var inner = action;
        action = () =>
        {
            Record(site, System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            inner();
        };
#endif
        Dispatcher.UIThread.Post(action, Priority);
    }

#if PD_DIAGNOSTICS
    private const int MaxRecords = 4000;
    private static readonly List<(string Site, double LatencyMs, double AtMs)> s_records = new();
    private static readonly long s_origin = System.Diagnostics.Stopwatch.GetTimestamp();

    /// <summary>진단: 수정 전후 비교용으로 Background 를 강제한다.</summary>
    internal static bool ForceBackground { get; set; }

    /// <summary>진단: 호출부별 (위치, 지연 ms, 실행 시각 ms).</summary>
    internal static IReadOnlyList<(string Site, double LatencyMs, double AtMs)> Records => s_records;

    /// <summary>진단: 기록을 비운다.</summary>
    internal static void ClearRecords() => s_records.Clear();

    private static void Record(string site, double latencyMs)
    {
        if (s_records.Count < MaxRecords)
        {
            s_records.Add((site, latencyMs, System.Diagnostics.Stopwatch.GetElapsedTime(s_origin).TotalMilliseconds));
        }
    }
#endif
}
