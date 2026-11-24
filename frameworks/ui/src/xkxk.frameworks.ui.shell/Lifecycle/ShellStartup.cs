// 셸 기동 절차 — 설정 로드 대기·지난 레이아웃 복원 판단·복원 완료 표시·자동 저장 시작·시작 인자 자동화 실행(화면 MainView 에서 분리, D-LAYER)
using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using xkxk.frameworks.ui.shell.Diagnostics;
using xkxk.frameworks.ui.shell.Diagnostics.Remote;
using xkxk.frameworks.ui.shell.Theming;
using xkxk.frameworks.ui.shell.ViewModels;
using xkxk.frameworks.ui.Text;

namespace xkxk.frameworks.ui.shell.Lifecycle;

/// <summary>MainView 가 첫 렌더 뒤 한 번 부른다. 화면은 언제 부를지만 정하고, 무엇을 어떤 순서로 할지는 여기서 정한다.</summary>
public static class ShellStartup
{
    /// <summary>기동 복원 중 표시 뒤 아래 절차를 지금 작업 뒤(UiDeferred — Android 는 Loaded, 그 밖은 Background)로 예약한다.</summary>
    public static void Start(MainViewModel vm, TopLevel topLevel)
    {
        // 순수 UI 층 컨트롤은 다국어 서비스를 모른다 — 문자열 조회를 여기서 잇는다
        UiText.Provider = Localization.Loc.Tr;
        Docking.MdiVisibilityGuard.Install(); // 요건 5 — MDI 영역이 줄어 안 보이게 된 문서 창을 기본 위치로(전 플랫폼 공용)

        // 기동 복원 중 표시(원격 panel.list·docs.list 가 복원 전 상태를 읽지 않게 NotReady — odev-6r 훅, 제품 빌드는 빈 스텁)
        RemoteCommands.StartupRestoreDone = false;

        // 시작 인자(데스크톱 args / 브라우저 URL 쿼리 변환본)를 첫 렌더 이후 실행한다.
        xkxk.frameworks.ui.shell.Platform.UiDeferred.Post(async () => await RunAsync(vm, topLevel));
    }

    /// <summary>복원 판단 → 복원 완료 표시 → 자동 저장 시작 → 자동화 실행. 예외는 셸 로그로 남기고 밖으로 던지지 않는다.</summary>
    private static async Task RunAsync(MainViewModel vm, TopLevel topLevel)
    {
        try
        {
            bool? restored = null; // null=복원 시도 안 함(진단 기록용)
            Exception? restoreError = null;
            try
            {
                // 자동화 인자 없이 기동했고 '윈도우 자동저장'이 켜져 있으면 지난 종료 시 레이아웃을 복원(MDI-10).
                // 저장된 설정을 읽은 뒤 판단한다(설정 복원은 비동기 — 끈 값이 늦게 오면 복원해 버리던 순서 문제 방지).
                await UiSettings.Current.Loaded;
                App.ApplyStartupArgs(); // Android 는 앱 초기화 뒤에 pdargs 가 들어온다 — 판단 직전에 다시 읽는다
                if (vm.WindowMenu.ShouldAutoSave) // --fresh·자동화 인자 실행이면 SuppressAutoSave 로 꺼져 있다
                {
                    restored = await vm.Commands.LoadLayoutAsync();
                }
            }
            catch (Exception ex)
            {
                restoreError = ex;
                throw;
            }
            finally
            {
#if PD_DIAGNOSTICS
                // 원격 health 의 restore 값(restored·skipped:사유·failed:예외) — 준비 완료 표시 전에 채운다
                await RemoteCommands.NoteStartupRestoreAsync(restored, restoreError);
#endif
                // 복원을 했든 건너뛰었든(--fresh·시나리오 인자) 실패했든 반드시 준비 완료 — 영원히 NotReady 로 남지 않게
                RemoteCommands.StartupRestoreDone = true;
            }

            // 복원 판단이 끝난 뒤부터 변경을 받아 디바운스 저장(그 전의 빈 화면이 저장본을 덮지 않게)
            App.AutoSaver?.Start();

            await AutomationBridge.RunAsync(vm.Commands, topLevel, App.StartupArgs);
        }
        catch (Exception ex)
        {
            vm.AddLog(Localization.Loc.Tr("log.automation.error", ex.Message));
        }
    }
}
