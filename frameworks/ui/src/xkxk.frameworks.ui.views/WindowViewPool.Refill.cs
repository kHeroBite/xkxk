// 창 뷰 풀의 입력 감시와 유휴 다시 채우기 — GridViewPool.cs 의 WindowViewPool 에서 300줄 한도 때문에 나눈 부분 클래스
using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using xkxk.frameworks.ui.data.Logging;
using xkxk.frameworks.ui.shell.Diagnostics;
using xkxk.frameworks.ui.shell.Docking;
using xkxk.frameworks.ui.shell.Busy;
using xkxk.frameworks.ui.shell.Diagnostics.Remote;
using xkxk.frameworks.ui.shell.Framework;

namespace xkxk.frameworks.ui.views;

public static partial class WindowViewPool
{
#if PD_DIAGNOSTICS
    private static readonly Dictionary<string, int> s_hits = new();
    private static readonly Dictionary<string, int> s_misses = new();
#endif

    /// <summary>
    /// 풀 적중·실패 누적 기록(진단 빌드만): 지표 pool_hit_{kind}·pool_miss_{kind}(perf.get 의 metrics, 열기 시도마다 +1).
    /// kind 는 데모 3종이면 옛 이름 소문자(grid·form·gantt), 그 밖은 창 ID 소문자다(NoteStep 의 prewarm_{kind}_ms 와 같은 규칙). 제품 빌드는 아무것도 하지 않는다.
    /// </summary>
    private static void NoteTake(string windowId, bool hit)
    {
#if PD_DIAGNOSTICS
        var name = WindowDisplay.LegacyOrId(windowId).ToLowerInvariant();
        var map = hit ? s_hits : s_misses;
        map[name] = map.GetValueOrDefault(name) + 1;
        PerfProbe.Current.SetMetric((hit ? "pool_hit_" : "pool_miss_") + name, map[name]);
#endif
    }

    /// <summary>꺼내기 실패(준비 뷰 없음·옵션 꺼짐) 기록 + 유휴 재채움 예약(옵션이 꺼져 있거나 KeepReady 창이 없으면 예약은 바로 끝난다). 연속 실패 중단 규칙은 NextMissing 이 적용한다.</summary>
    private static void NoteMiss(string windowId)
    {
        NoteTake(windowId, hit: false);
        ScheduleRefill();
    }

    private const int MaxFailures = 3;
    private static readonly Dictionary<string, int> s_failures = new();

    /// <summary>다시 채우기 실패를 센다. 연속 MaxFailures 회가 되면 그 창은 더 시도하지 않고(NextMissing 이 건너뜀) 진단을 1회 남긴다.</summary>
    private static void NoteFailure(string windowId, string reason)
    {
        var n = s_failures.GetValueOrDefault(windowId) + 1;
        s_failures[windowId] = n;
        if (n == MaxFailures)
        {
            PdLog.Info(PdLogCategory.System, $"준비된 뷰 채우기 {n}회 연속 실패, '{windowId}' 중단: {reason}"); // i18n-ok 진단 로그
        }
    }

    /// <summary>
    /// 창을 열 때 내용 생성 전에 부른다. 창 정의가 KeepReady 이고 준비된 뷰 옵션(GridViewPool)이 켜져 있으면 준비된 뷰를 꺼내(적중) 넘기고,
    /// 없으면 null(실패 기록 + 다시 채우기 예약) 이라 호출 쪽이 CreateContent 로 새로 만든다. KeepReady 가 아니거나 옵션이 꺼져 있으면 풀을 쓰지 않는다.
    /// </summary>
    internal static Control? TakeFor(string windowId)
    {
        if (!XkxkUi.Current.Settings.GridViewPool || !XkxkUi.Current.Windows.TryGet(windowId, out var def) || def.Prewarm != UiWindowPrewarm.KeepReady)
        {
            return null;
        }

        return TryTake(windowId); // 실패해도 TryTake 가 재채움을 예약한다(첫 열기가 예열보다 빨랐던 경우 포함)
    }

    private static void HookInput()
    {
        const RoutingStrategies routes = RoutingStrategies.Tunnel | RoutingStrategies.Bubble;
        InputElement.PointerPressedEvent.AddClassHandler<TopLevel>((_, _) => MarkInput(), routes, handledEventsToo: true);
        InputElement.PointerMovedEvent.AddClassHandler<TopLevel>((_, _) => MarkInput(), routes, handledEventsToo: true);
        InputElement.PointerWheelChangedEvent.AddClassHandler<TopLevel>((_, _) => MarkInput(), routes, handledEventsToo: true);
        InputElement.KeyDownEvent.AddClassHandler<TopLevel>((_, _) => MarkInput(), routes, handledEventsToo: true);
    }

    private static void MarkInput() => s_lastInputMs = Environment.TickCount64;

    /// <summary>준비된 뷰가 비어 있는 KeepReady 창 ID(등록 순서). 없으면 null.</summary>
    private static string? NextMissing()
    {
        foreach (var w in XkxkUi.Current.Windows.Items)
        {
            if (w.Prewarm == UiWindowPrewarm.KeepReady && !s_ready.ContainsKey(w.Id) && s_failures.GetValueOrDefault(w.Id) < MaxFailures)
            {
                return w.Id;
            }
        }

        return null;
    }

    /// <summary>바쁨 스코프 없음 + 입력 없음이 이어지면 비어 있는 창의 준비 뷰를 만든다. Background 우선순위라 입력·렌더를 앞지르지 않는다.</summary>
    private static void ScheduleRefill()
    {
        if (s_refillScheduled || !XkxkUi.Current.Settings.GridViewPool)
        {
            return;
        }

        s_refillScheduled = true;
        Poll(0);
    }

    private static void Poll(long quietSinceMs)
    {
        DispatcherTimer.RunOnce(() =>
        {
            if (!XkxkUi.Current.Settings.GridViewPool || s_host is null || NextMissing() is not { } id)
            {
                s_refillScheduled = false;
                return;
            }

            var now = Environment.TickCount64;
            var quiet = RemoteCommands.StartupRestoreDone != false && BusyHooks.Service.Snapshot().Count == 0 && now - s_lastInputMs >= InputQuietMs;
            if (!quiet)
            {
                Poll(0);
                return;
            }

            var since = quietSinceMs == 0 ? now : quietSinceMs;
            if (now - since < IdleHoldMs)
            {
                Poll(since);
                return;
            }

            s_refillScheduled = false;
            try
            {
                if (Build(id, keep: true, out _))
                {
                    s_failures.Remove(id);
                }
                else
                {
                    NoteFailure(id, "내용이 Control 이 아님"); // i18n-ok 진단 로그
                }
            }
            catch (Exception ex)
            {
                NoteFailure(id, $"{ex.GetType().Name}: {ex.Message}"); // i18n-ok 진단 로그
            }

            ScheduleRefill(); // 다음 빈 창이 있으면 이어서 채운다
        }, TimeSpan.FromMilliseconds(PollMs), DispatcherPriority.Background);
    }
}
