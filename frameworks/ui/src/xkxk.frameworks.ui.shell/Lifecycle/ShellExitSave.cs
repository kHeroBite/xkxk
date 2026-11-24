// 셸 종료 저장 절차 — 첫 Closing 취소 → 레이아웃 자동 저장 → 설정 플러시 → 앱 종료(App.axaml.cs 에서 분리, D-LAYER)
using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using xkxk.frameworks.ui.shell.Theming;
using xkxk.frameworks.ui.shell.ViewModels;

namespace xkxk.frameworks.ui.shell.Lifecycle;

/// <summary>App 이 메인 창 Closing 에 연결한다. 저장 순서·상한·예외 처리는 여기서 정한다.</summary>
public static class ShellExitSave
{
    /// <summary>종료 저장을 이미 시작했는가(두 번째 Closing 통과·App.RequestExit 판단용).</summary>
    public static bool Done { get; private set; }

    /// <summary>
    /// 종료 처리(한 경로): 첫 Closing 을 취소하고 ① 레이아웃 자동 저장(MDI-10, 켜진 경우) → ② 설정 플러시(진행 중 저장 대기 + 최신 값 1회 쓰기,
    /// 각 상한 1.5초 — Ctrl+휠 모드 등 마지막 변경 유실 방지) 순서로 끝낸 뒤 앱을 종료한다. 두 번째 Closing 은 그대로 통과.
    /// 브라우저는 종료 이벤트가 없어 미적용.
    /// </summary>
    public static async Task OnClosingAsync(Window window, MainViewModel vm, WindowClosingEventArgs e)
    {
        if (Done)
        {
            return;
        }

        e.Cancel = true;
        Done = true;
        try
        {
            if (App.AutoSaver is { } saver)
            {
                await saver.StopAsync(TimeSpan.FromSeconds(1)); // 디바운스 저장과 종료 저장이 같은 파일에 겹쳐 쓰지 않게
            }

            if (vm.WindowMenu.ShouldAutoSave)
            {
                await vm.Commands.SaveLayoutAsync();
            }
        }
        catch (Exception ex)
        {
            vm.AddLog(Localization.Loc.Tr("log.layout.exitSaveFailed", ex.Message));
        }

        await UiSettings.Current.FlushAsync(TimeSpan.FromSeconds(1.5));
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
        else
        {
            window.Close();
        }
    }
}
