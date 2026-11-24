// 원격 제어 플랫폼 훅(캡처 방지 판정·창별 affinity·SDI OS Z 순서·기동 복원 완료)과 진단 빌드의 기동 복원 결과 기록 — 명령 코드는 진단 빌드의 Remote*Commands 에 있다
using System;
#if PD_DIAGNOSTICS
using System.Collections.Generic;
using System.Threading.Tasks;
#endif

namespace Pd.Poc.Shell.Diagnostics.Remote;

/// <summary>
/// 셸 기동 절차(Lifecycle/ShellStartup)가 채우는 기동 복원 완료 표시(제품·진단 공통)와, 진단 빌드 전용 원격 판정 훅(호스트 DesktopDiagHooks·Android MainActivity 가 채움)·
/// 기동 복원 결과 기록(StartupRestore·NoteStartupRestoreAsync). 원격 명령은 Remote{군}Commands 가 맡는다.
/// </summary>
public static class RemoteCommands
{
    /// <summary>
    /// 기동 복원 완료 여부(셸 기동 절차가 채운다 — odev-2): 복원 판단 시작 때 false, LoadLayoutAsync(또는 복원 생략 판단) 뒤 true.
    /// null 은 '셸이 이 훅을 아직 채우지 않는 판' — 그때는 막지 않는다(영원히 NotReady 가 되지 않게).
    /// </summary>
    public static bool? StartupRestoreDone { get; set; }
#if PD_DIAGNOSTICS // 원격 판정 훅 — 제품 빌드에는 설치도 선언도 없다(team-lead 16:10 결정, D-LAYER)

    /// <summary>
    /// 캡처 방지의 실제 적용 상태를 읽는 플랫폼 훅(null=모름). 데스크톱은 ShellPlatform(GetWindowDisplayAffinity),
    /// Android 는 MainActivity(FLAG_SECURE)가 설정한다. 설정값(MainViewModel.CaptureProtection)과 대조해 실제 반영을 판정한다.
    /// </summary>
    public static Func<bool?>? CaptureProtectProbe { get; set; }

    /// <summary>창별 캡처 방지 실제 값(데스크톱 호스트 DesktopDiagHooks → CaptureProtection.Affinities — Window="main"|"sdi '제목'", Affinity 0·1(MONITOR)·0x11(EXCLUDEFROMCAPTURE)·null=조회 실패).</summary>
    public static Func<IReadOnlyList<(string Window, uint? Affinity)>>? CaptureAffinityProbe { get; set; }

    /// <summary>
    /// 호스트가 채우는 SDI OS 창 쌓임 순서(문서 Id, 아래→위 — 항상 위 창 포함 실제 OS Z 순서). 데스크톱 호스트(odev-4b SdiWindowHost.ZOrder)가 지정한다.
    /// 없거나 null 을 돌려주면 관리형 창 시각 순서·모델 순서로 대신한다.
    /// </summary>
    public static Func<IReadOnlyList<string>?>? SdiZOrderProbe { get; set; }

    // ── 기동 자동 복원 결과 기록(옛 RemoteCommands.RestoreResult — 진단 빌드 전용. MainView·RemoteControlServer 가 이 이름으로 부른다) ──

    /// <summary>
    /// 기동 자동 복원 결과. null=아직 판단 전(StartupRestoreDone=false 구간).
    /// restored / skipped:fresh / skipped:scenario / skipped:setting-off / skipped:no-file / failed:parse / failed:&lt;예외형&gt;: &lt;메시지&gt;
    /// </summary>
    public static string? StartupRestore { get; private set; }

    /// <summary>
    /// MainView 가 복원 판단 직후(StartupRestoreDone=true 직전) 부른다.
    /// restored: LoadLayoutAsync 결과(null=시도 안 함), error: 복원 중 예외.
    /// </summary>
    public static async Task NoteStartupRestoreAsync(bool? restored, Exception? error)
    {
        try
        {
            StartupRestore = error is not null ? "failed:" + Summarize(error)
                : restored == true ? "restored"
                : restored == false ? await ClassifyEmptyAsync()
                : "skipped:" + SkipReason();
        }
        catch (Exception ex)
        {
            // 진단 기록 실패가 기동을 막지 않게 — 결과에 그대로 남긴다
            StartupRestore = "failed:note " + Summarize(ex);
        }

        System.Diagnostics.Debug.WriteLine($"[shell] startup-restore={StartupRestore}");
    }

    private static string SkipReason() =>
        App.FreshStart ? "fresh"
        : AutomationBridge.HasScenarioArgs(App.StartupArgs) ? "scenario"
        : "setting-off"; // '윈도우 자동저장' 사용자 설정 끔

    /// <summary>LoadLayoutAsync 가 false 면 저장본이 없거나(파일 없음·빈 값) 해석에 실패한 것 — 진단에서만 한 번 더 읽어 가른다.</summary>
    private static async Task<string> ClassifyEmptyAsync()
    {
        var json = await App.LayoutStore.LoadAsync();
        return string.IsNullOrWhiteSpace(json) ? "skipped:no-file" : "failed:parse";
    }

    private static string Summarize(Exception ex)
    {
        var msg = ex.Message.ReplaceLineEndings(" ");
        return $"{ex.GetType().Name}: {(msg.Length > 160 ? msg[..160] : msg)}";
    }
#endif
}
