// 복원 문서 뷰 지연 생성의 유휴 선생성 펌프 — 사용자 입력 기록과 1개씩 만드는 틱(RestoreViewDeferral.cs 줄 수 상한으로 나눈 부분)
using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Pd.Poc.Shell.Busy;
using Pd.Poc.Shell.Diagnostics.Remote;

namespace Pd.Poc.Shell.Docking;

public static partial class RestoreViewDeferral
{
    /// <summary>사용자 입력 시각을 기록한다(모든 창, 처리된 이벤트 포함). 입력이 있으면 선생성을 미룬다.</summary>
    private static void HookInput()
    {
        if (s_inputHooked)
        {
            return;
        }

        s_inputHooked = true;
        const RoutingStrategies routes = RoutingStrategies.Tunnel | RoutingStrategies.Bubble;
        InputElement.PointerPressedEvent.AddClassHandler<TopLevel>((_, _) => MarkInput(), routes, handledEventsToo: true);
        InputElement.PointerMovedEvent.AddClassHandler<TopLevel>((_, _) => MarkInput(), routes, handledEventsToo: true);
        InputElement.PointerWheelChangedEvent.AddClassHandler<TopLevel>((_, _) => MarkInput(), routes, handledEventsToo: true);
        InputElement.KeyDownEvent.AddClassHandler<TopLevel>((_, _) => MarkInput(), routes, handledEventsToo: true);
    }

    private static void MarkInput() => s_lastInputMs = Environment.TickCount64;

    private static void SchedulePump()
    {
        if (s_pumpScheduled)
        {
            return;
        }

        s_pumpScheduled = true;
        DispatcherTimer.RunOnce(PumpTick, TimeSpan.FromMilliseconds(PollMs), DispatcherPriority.Background);
    }

    /// <summary>
    /// 유휴 선생성 한 틱. 예열이 끝났고, 복원이 끝났고, 진행 중 바쁨 스코프가 없고, 입력이 없는 상태가 IdleHoldMs 이어지면 맨 위 문서 1개를 스코프 없이 만든다.
    /// 다음 문서는 다음 틱(PollMs 뒤 Background)이라 그 사이 입력·렌더가 먼저 돈다. 스코프를 걸지 않으므로 '한가함' 판정과 바쁨 표시를 깨지 않는다.
    /// </summary>
    private static void PumpTick()
    {
        s_pumpScheduled = false;
        if (s_deferred.Count == 0)
        {
            return;
        }

        var now = Environment.TickCount64;
        var quiet = s_prewarmDone && RemoteCommands.StartupRestoreDone != false
            && BusyHooks.Service.Snapshot().Count == 0 && now - s_lastInputMs >= InputQuietMs;
        if (!quiet)
        {
            s_quietSinceMs = 0;
        }
        else if (s_quietSinceMs == 0)
        {
            s_quietSinceMs = now;
        }
        else if (now - s_quietSinceMs >= IdleHoldMs)
        {
            var top = Enumerable.Range(0, s_deferred.Count).MaxBy(i => s_deferred[i].Doc.MdiZIndex); // 위 창부터(먼저 보일 가능성이 크다)
            Realize(top, busy: false);
            if (s_deferred.Count == 0)
            {
                if (s_pending.Count == 0)
                {
                    Unwire();
                }

                return;
            }
        }

        SchedulePump();
    }
}
