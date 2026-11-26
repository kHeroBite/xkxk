// 시스템 메뉴 창 제공자(ISystemViews 구현, UI-17) — 메뉴가 고른 SystemView 를 해당 창·동작으로 잇고, 플랫폼별 가능 여부를 알려 준다
using xkxk.frameworks.ui.views.Settings;
using xkxk.frameworks.ui.shell;
using xkxk.frameworks.ui.data.Logging;
using xkxk.frameworks.ui.shell.Platform;
using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage; // LauncherExtensions.LaunchDirectoryInfoAsync(ILauncher, DirectoryInfo) 확장 메서드
using Avalonia.Threading;
using xkxk.frameworks.ui.shell.Tour;
using xkxk.frameworks.ui.shell.ViewModels;
using xkxk.frameworks.ui.shell.Framework;

namespace xkxk.frameworks.ui.views.SystemMenu;

/// <summary>
/// 기동 때 <c>SystemHost.Views = new SystemViews();</c> 한 줄로 연결한다(App.axaml.cs — odev-2r).
/// 프로그램 정보용 채널·렌더러 공급 함수(SystemInfo)는 App.axaml.cs 가 따로 잇는다(지역화된 렌더러 문구 — odev-2r).
/// </summary>
public sealed class SystemViews : ISystemViews
{

    /// <summary>LogFolder 는 로그 파일 폴더가 있는 플랫폼(데스크톱)만 된다. 나머지는 모든 플랫폼에서 된다.</summary>
    public bool CanOpen(SystemView view) => view switch
    {
        SystemView.LogFolder => PdLog.FileDirectory is { Length: > 0 } dir && Directory.Exists(dir),
        _ => true,
    };

    public void Open(SystemView view)
    {
        PdLog.WriteUser(PdLogLevel.Information, "sysview.open " + view); // 사용자 행동 로그(값 없이 명령 이름만). 'sys.open' 은 진단 원격 명령 이름이라 제품 Q9 금지 문자열과 겹치지 않게 다른 이름을 쓴다
        switch (view)
        {
            case SystemView.LogView: LogViewerView.Open(); break;
            case SystemView.LogFolder: OpenLogFolder(); break;
            case SystemView.LogExport: Export(); break;
            case SystemView.AppMonitor: AppMonitorView.Open(); break;
            case SystemView.Inquiry: InquiryView.Open(); break;
            case SystemView.InquiryList: InquiryListView.Open(); break;
            case SystemView.Update: UpdateCheckView.Open(); break;
            case SystemView.Changelog: ChangelogView.Open(); break;
            case SystemView.Help: OpenHelpTour(); break;
            case SystemView.About: AboutView.Open(); break;
            case SystemView.Settings: OpenSettings(); break;
            case SystemView.RestartRequired: RestartRequiredView.Open(); break;
        }
    }

    /// <summary>
    /// 메인 창 도움말 투어(요건 6). 투어는 메인 창 위에 뜨므로 트레이 메뉴에서 불렀을 때 숨었거나 최소화된 창을 먼저 되살린다.
    /// 메뉴·서랍은 이 호출이 끝난 뒤 같은 입력 처리 안에서 닫히므로, 다음 Dispatcher 차례에 시작하면 닫힌 뒤가 된다. 좌표는 Begin 의 UpdateLayout 이 맞춘다.
    /// Android 는 화면에 그릴 일이 없으면 Background 가 약 2초 뒤에 돌고, 처리할 일이 이어지면 수십 초까지 밀린다(실측). 그래서 UiDeferred 를 거친다.
    /// </summary>
    private static void OpenHelpTour()
    {
        if (MainTopLevel is Window main && (!main.IsVisible || main.WindowState == WindowState.Minimized))
        {
            ShellPlatform.RestoreMain(main, activate: !SystemDialogHost.BackgroundMode);
        }

        xkxk.frameworks.ui.shell.Platform.UiDeferred.Post(() => TourController.StartMain());
    }

    /// <summary>스마트폰 NAV 는 ☰ 설정 시트, 그 밖은 새 창(데스크톱)·플라이아웃(웹·Android)으로 연다.</summary>
    private static void OpenSettings()
    {
        if (XkxkUi.Current.MainViewModel?.Compact.IsCompact == true && SystemHost.OpenSettingsSheet is { } openSheet)
        {
            openSheet();
            return;
        }

        SettingsView.Open();
    }

    private static TopLevel? MainTopLevel => Application.Current?.ApplicationLifetime switch
    {
        IClassicDesktopStyleApplicationLifetime d => d.MainWindow,
        ISingleViewApplicationLifetime s => TopLevel.GetTopLevel(s.MainView),
        _ => null,
    };

    private static async void OpenLogFolder()
    {
        if (PdLog.FileDirectory is not { Length: > 0 } dir)
        {
            return;
        }

        if (SystemDialogHost.BackgroundMode)
        {
            SystemHost.LastSuppressed = "LogFolder " + dir; // 시험 모드: 탐색기를 띄우지 않는다(전경·화면 노출 방지)
            return;
        }

        if (MainTopLevel?.Launcher is not { } launcher)
        {
            return;
        }

        try
        {
            await launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(dir));
        }
        catch (Exception ex)
        {
            PdLog.Error(PdLogCategory.System, "log.openFolder failed", ex); // 탐색기 실행 실패는 앱을 멈추지 않는다
        }
    }

    /// <summary>저장 대화로 내보낸다. 저장 대화를 쓸 수 없는 환경(웹 일부)은 로그 보기 창을 대신 연다.</summary>
    private static void Export() => Dispatcher.UIThread.Post(async () =>
    {
        if (SystemDialogHost.BackgroundMode)
        {
            // 시험 모드: 파일 대화상자를 띄우지 않고 기본 제안 경로만 기록한다(대화상자 기본 폴더는 OS 가 정하므로 로그 폴더 기준으로 적는다)
            SystemHost.LastSuppressed = "LogExport " + Path.Combine(PdLog.FileDirectory ?? "", LogExport.SuggestedFileName());
            return;
        }

        try
        {
            if (await LogViewerView.ExportAllAsync(MainTopLevel) == false)
            {
                LogViewerView.Open();
            }
        }
        catch (Exception ex)
        {
            PdLog.Error(PdLogCategory.System, "log.export failed", ex);
        }
    });
}
