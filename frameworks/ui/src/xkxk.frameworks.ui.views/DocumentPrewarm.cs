// 문서 뷰 예열 — 복원 완료·busy idle 1초 지속 뒤에 등록 창 중 Prewarm 이 켜진 뷰를 화면 밖 호스트에 실제 크기로 붙여 한 번 데워 첫 열기 비용(템플릿·스타일·글자)을 앞당긴다
using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Threading;
using xkxk.frameworks.ui.data.Logging;
using xkxk.frameworks.ui.shell.Busy;
using xkxk.frameworks.ui.shell.Diagnostics;
using xkxk.frameworks.ui.shell.Diagnostics.Remote;
using xkxk.frameworks.ui.shell.Docking;
using xkxk.frameworks.ui.shell.Framework;

namespace xkxk.frameworks.ui.views;

/// <summary>
/// 규칙(켬/끔)은 UiSettings.PrewarmViews 가 갖고, 이 클래스는 뷰 생성만 한다(레이어 분리).
/// 시작 조건은 '레이아웃 복원 완료 + BusyService 에 살아 있는 스코프 없음(idle)이 IdleHoldMs 동안 지속'이다.
/// 뷰마다 한 틱씩 나눠 만들고, 단계 사이에 busy 가 시작되면(사용자가 문서를 열었거나 복원·전환 중) 남은 예열을 미룬다.
/// 예열은 BusyService.Begin 을 부르지 않아 바쁨 표시·idle 판정에 잡히지 않는다. 만든 뷰는 화면 밖 호스트(GridViewPool)에 실제 크기로 붙여
/// 측정·배치·비트맵 그리기까지 한 뒤 뗀다(A16 — 붙이지 않은 뷰는 테마가 적용되지 않아 예전 예열은 첫 표 열기 비용을 줄이지 못했다).
/// 표 뷰는 옵션 GridViewPool 이 켜져 있으면 버리지 않고 준비된 표 뷰로 보관한다. 규칙 정본 01 §5.4.
/// 복원 지연(B5)과의 순서: 복원에서 미룬 문서의 유휴 선생성(RestoreViewDeferral)은 예열이 끝난 뒤(꺼짐·실패 포함) 시작한다.
/// 미룬 문서가 있는 종류는 그 문서를 실제로 만드는 일이 같은 템플릿·JIT 를 데우므로 버리는 뷰를 따로 만들지 않고 건너뛴다.
/// </summary>
public static class DocumentPrewarm
{
    private const int IdleHoldMs = 1000;
    private const int PollMs = 250;
    private static bool s_started;

    /// <summary>
    /// 첫 프레임 뒤 1회만 예열을 예약한다. 설정이 꺼져 있거나 이미 시작했으면 아무것도 하지 않는다(꺼져 있으면 바로 끝남을 알린다).
    /// host 는 화면 밖 예열 호스트를 넣을 메인 화면 패널이다(GridViewPool.AttachHost). 없으면 예전처럼 붙이지 않고 측정만 한다.
    /// </summary>
    public static void StartOnce(Panel? host = null)
    {
        if (s_started)
        {
            return;
        }

        s_started = true;
        try
        {
            if (host is not null && (XkxkUi.Current.Settings.PrewarmViews || XkxkUi.Current.Settings.GridViewPool))
            {
                WindowViewPool.AttachHost(host); // 준비된 뷰 다시 채우기도 이 호스트를 쓴다
            }

            if (!Enabled)
            {
                RestoreViewDeferral.NotifyPrewarmDone();
                return;
            }

            Poll(new Queue<string>(Targets()), 0);
        }
        catch (Exception ex)
        {
            PdLog.Info(xkxk.frameworks.ui.data.Logging.PdLogCategory.System, $"뷰 예열 예약 실패: {ex.GetType().Name}: {ex.Message}"); // i18n-ok 진단 로그
            RestoreViewDeferral.NotifyPrewarmDone();
        }
    }

    /// <summary>
    /// 예열 대상(등록 순서). 등록 창 중 Prewarm 이 KeepReady 인 창은 늘, Warm 인 창은 설정 PrewarmViews 가 켜져 있을 때만 넣는다.
    /// 특정 데모 형식은 직접 참조하지 않는다(등록 옵션 UiWindowOptions.Prewarm 이 기준, 01 §5.4).
    /// </summary>
    private static IEnumerable<string> Targets()
    {
        foreach (var w in XkxkUi.Current.Windows.Items)
        {
            if (w.Prewarm == UiWindowPrewarm.KeepReady || (w.Prewarm == UiWindowPrewarm.Warm && XkxkUi.Current.Settings.PrewarmViews))
            {
                yield return w.Id;
            }
        }
    }

    private static bool Enabled => XkxkUi.Current.Settings.PrewarmViews || XkxkUi.Current.Settings.GridViewPool;

    /// <summary>복원이 끝났고(StartupRestoreDone 가 false 가 아님) 진행 중 바쁨 스코프가 없으면 true. 상태를 읽기만 한다.</summary>
    private static bool IsQuiet() =>
        RemoteCommands.StartupRestoreDone != false && BusyHooks.Service.Snapshot().Count == 0;

    /// <summary>PollMs 마다 idle 을 확인해 IdleHoldMs 지속되면 한 단계를 만든다. Background 우선순위라 입력·렌더를 앞지르지 않는다.</summary>
    private static void Poll(Queue<string> pending, long quietSinceMs)
    {
        DispatcherTimer.RunOnce(() =>
        {
            if (pending.Count == 0 || !Enabled)
            {
                RestoreViewDeferral.NotifyPrewarmDone();
                return;
            }

            var now = Environment.TickCount64;
            if (!IsQuiet())
            {
                Poll(pending, 0); // busy 중 — 지속 시간을 처음부터 다시 센다
                return;
            }

            var since = quietSinceMs == 0 ? now : quietSinceMs;
            if (now - since < IdleHoldMs)
            {
                Poll(pending, since);
                return;
            }

            if (RunStep(pending))
            {
                Poll(pending, since); // 다음 단계도 직전까지 idle 이 이어졌으므로 지속 시간을 유지하되 단계 직전에 다시 확인한다
            }
            else
            {
                RestoreViewDeferral.NotifyPrewarmDone();
            }
        }, TimeSpan.FromMilliseconds(PollMs), DispatcherPriority.Background);
    }

    /// <summary>한 창의 내용을 만들어 데운다(미룬 복원 문서가 있는 종류는 건너뜀). 남은 단계가 있으면 true, 끝났거나 실패하면 false(예열 중단).</summary>
    private static bool RunStep(Queue<string> pending)
    {
        try
        {
            while (pending.Count > 0 && RestoreViewDeferral.HasDeferred(pending.Peek()))
            {
                pending.Dequeue(); // 그 종류는 선생성이 데운다
            }

            if (pending.Count == 0)
            {
                return false;
            }

            var id = pending.Dequeue();
            var t0 = Environment.TickCount64;
            var keep = XkxkUi.Current.Settings.GridViewPool && XkxkUi.Current.Windows.TryGet(id, out var def) && def.Prewarm == UiWindowPrewarm.KeepReady;
            if (WindowViewPool.Build(id, keep, out var attached)) // 실제 크기로 화면 밖에 붙여 데우고(호스트 없으면 측정만), KeepReady 이고 옵션 GridViewPool 이 켜져 있으면 준비된 뷰로 보관한다
            {
                NoteStep(id, Environment.TickCount64 - t0, attached);
            }
            else
            {
                PdLog.Info(xkxk.frameworks.ui.data.Logging.PdLogCategory.System, $"뷰 예열 건너뜀: 창 '{id}' 내용이 Control 이 아니거나 만들지 못함"); // i18n-ok 진단 로그
            }

            return pending.Count > 0;
        }
        catch (Exception ex)
        {
            PdLog.Info(xkxk.frameworks.ui.data.Logging.PdLogCategory.System, $"뷰 예열 실패, 중단: {ex.GetType().Name}: {ex.Message}"); // i18n-ok 진단 로그
            return false;
        }
    }

    /// <summary>
    /// 예열 단계 성공 기록(진단 빌드만): 로그 한 줄 '뷰 예열 성공 kind ms 붙임' 과 지표 prewarm_{kind}_ms·prewarm_{kind}_attached(perf.get 의 metrics).
    /// a16diag 가 예열이 실제로 돌았는지·실제 크기로 붙었는지를 확인하는 근거다(열기 계측은 SuppressOpenTrack 으로 빠지므로 따로 남긴다).
    /// </summary>
    private static void NoteStep(string windowId, long ms, bool attached)
    {
#if PD_DIAGNOSTICS
        var name = WindowDisplay.LegacyOrId(windowId).ToLowerInvariant(); // 데모 3종은 옛 이름 소문자(grid·form·gantt) — 지표 키 유지
        PdLog.Info(xkxk.frameworks.ui.data.Logging.PdLogCategory.System, $"뷰 예열 성공 {name} {ms}ms 붙임={(attached ? 1 : 0)}"); // i18n-ok 진단 로그
        PerfProbe.Current.SetMetric("prewarm_" + name + "_ms", ms);
        PerfProbe.Current.SetMetric("prewarm_" + name + "_attached", attached ? 1 : 0);
#endif
    }
}
