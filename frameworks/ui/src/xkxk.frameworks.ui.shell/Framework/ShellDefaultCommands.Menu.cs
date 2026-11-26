// 셸 기본 명령 B07 38개 등록 — 셸 4메뉴·모바일형 메뉴가 쓰던 뷰모델 명령을 명령 ID 로 노출한다(UADR-0014, a8 §9)
using System;
using System.Windows.Input;
using xkxk.frameworks.ui.data.Navigation;
using xkxk.frameworks.ui.shell.Localization;
using xkxk.frameworks.ui.shell.Platform;
using xkxk.frameworks.ui.shell.ViewModels;

namespace xkxk.frameworks.ui.shell.Framework;

/// <remarks>
/// B07 38개의 동작은 옛 메뉴 항목이 바인딩하던 <see cref="MainViewModel"/>(하위 뷰모델 포함) 명령을 같은 파라미터로 부르는 것이다.
/// 실행 가능 여부는 셸이 있고(앱 없음이면 false·예외 없음) 그 뷰모델 명령의 CanExecute 가 true 일 때다.
/// 대상 문서 명령은 B06 과 같은 규칙(<c>ctx.Parameter is PocDocument d ? d : shell.ActiveDocument</c>)으로 대상을 정한다.
/// Avalonia 창이 필요한 종료·글자 초기화는 <see cref="ShellPlatformCommands"/> 의 대리자를 쓴다.
/// </remarks>
internal static partial class ShellDefaultCommands
{
    private const string FileCategory = "file";
    private const string View = "view";
    private const string SystemCategory = "system";
    private const string Nav = "nav";

    /// <summary>
    /// 모바일형 화면이 자기 패널 열기 동작을 걸어 두는 자리. 파라미터는 explorer·docs·notify·settings·menu 중 하나다.
    /// 모바일형 화면이 없으면 null 이고, 그때 <see cref="ShellCommandIds.NavOpenPanel"/> 은 실행할 수 없다.
    /// </summary>
    internal static Action<string>? NavPanelOpener { get; set; }

    /// <summary>B07 38개를 <see cref="ShellCommandIds.All"/> 순서대로 등록한다.</summary>
    private static void RegisterMenuCommands(IUiCommandRegistry r)
    {
        Add(r, ShellCommandIds.DemoOpen, "menu.file.openGrid", MenuIconCatalog.GridIconKey, null, FileCategory,
            c => Do(c, v => v.OpenCommand, Text(c)), c => Can(c, v => v.OpenCommand, Text(c)));
        Add(r, ShellCommandIds.DemoOpenSdi, "menu.file.openGridSdi", CmdIconKeys.OpenSdi, null, FileCategory,
            c => Do(c, v => v.OpenSdiCommand, Text(c)), c => Can(c, v => v.OpenSdiCommand, Text(c)));
        Add(r, ShellCommandIds.BenchOpen, "menu.file.openBench", CmdIconKeys.OpenBench, null, FileCategory,
            c => Do(c, v => v.Extras.OpenBenchCommand, Text(c)),
            c => Vm(c) is { Extras.BenchAvailable: true } && Can(c, v => v.Extras.OpenBenchCommand, Text(c)));
        Add(r, ShellCommandIds.LayoutSaveCurrent, "menu.ws.layoutSave", CmdIconKeys.LayoutSave, null, Layout,
            c => Do(c, v => v.WindowMenu.SaveCurrentLayoutCommand, null), c => Can(c, v => v.WindowMenu.SaveCurrentLayoutCommand, null));
        Add(r, ShellCommandIds.LayoutManage, "menu.ws.layoutManage", CmdIconKeys.LayoutManage, null, Layout,
            c => Do(c, v => v.WindowMenu.ManageLayoutsCommand, null), c => Can(c, v => v.WindowMenu.ManageLayoutsCommand, null));
        Add(r, ShellCommandIds.AppExit, "menu.file.exit", CmdIconKeys.Exit, "Alt+F4", FileCategory,
            ShellPlatformCommands.Exit, ShellPlatformCommands.CanExit);
        Add(r, ShellCommandIds.WindowMaximizeToggle, "menu.window.maximizeToggle", CmdIconKeys.Maximize, null, Window,
            c => DoTarget(c, v => v.MaximizeToggleCommand), c => CanTarget(c, v => v.MaximizeToggleCommand));
        Add(r, ShellCommandIds.WindowRestore, "menu.window.restore", CmdIconKeys.Restore, null, Window,
            c => DoTarget(c, v => v.RestoreCommand), c => CanTarget(c, v => v.RestoreCommand));
        Add(r, ShellCommandIds.WindowRename, "menu.window.rename", CmdIconKeys.Rename, null, Window,
            c => DoTarget(c, v => v.BeginRenameCommand), c => CanTarget(c, v => v.BeginRenameCommand));
        Add(r, ShellCommandIds.FavoriteToggle, "favorite.toggle", "PdShellIconFavorite", null, Window,
            c => DoTarget(c, v => v.FavoriteBar.ToggleCommand), c => CanTarget(c, v => v.FavoriteBar.ToggleCommand));
        Add(r, ShellCommandIds.WindowToggleSdi, "menu.window.toggleSdi", CmdIconKeys.ToggleSdi, null, Window,
            c => DoTarget(c, v => v.ToggleSdiCommand), c => CanTarget(c, v => v.ToggleSdiCommand));
        Add(r, ShellCommandIds.SdiResetPositions, "menu.ws.sdiResetPos", CmdIconKeys.SdiResetPos, null, Window,
            c => Do(c, WindowMenuCommand, "ResetSdiPositions"), c => Can(c, WindowMenuCommand, "ResetSdiPositions"));
        Add(r, ShellCommandIds.SdiMinimizeAll, "menu.ws.sdiMinimize", CmdIconKeys.SdiMinimize, null, Window,
            c => Do(c, WindowMenuCommand, "MinimizeAllSdi"), c => Can(c, WindowMenuCommand, "MinimizeAllSdi"));
        Add(r, ShellCommandIds.SdiRestoreAll, "menu.ws.sdiRestore", CmdIconKeys.SdiRestore, null, Window,
            c => Do(c, WindowMenuCommand, "RestoreAllSdi"), c => Can(c, WindowMenuCommand, "RestoreAllSdi"));
        Add(r, ShellCommandIds.SdiCloseAll, "menu.ws.sdiCloseAll", CmdIconKeys.SdiCloseAll, null, Window,
            c => Do(c, WindowMenuCommand, "CloseAllSdi"), c => Can(c, WindowMenuCommand, "CloseAllSdi"));
        Add(r, ShellCommandIds.MonitorSingle, "menu.ws.singleMonitor", CmdIconKeys.SingleMonitor, null, Window,
            c => Do(c, WindowMenuCommand, "FitSingleMonitor"), c => Can(c, WindowMenuCommand, "FitSingleMonitor"));
        Add(r, ShellCommandIds.MonitorMaximizeSelected, "menu.monitor.maximize", CmdIconKeys.MonitorMaximize, null, Window,
            c => Do(c, WindowMenuCommand, "MaximizeOnSelectedMonitor"), c => Can(c, WindowMenuCommand, "MaximizeOnSelectedMonitor"));
        Add(r, ShellCommandIds.MainWindowReset, "menu.ws.resetMain", CmdIconKeys.ResetMain, null, Window,
            c => Do(c, WindowMenuCommand, "ResetMainWindow"), c => Can(c, WindowMenuCommand, "ResetMainWindow"));
        Add(r, ShellCommandIds.WindowFlash, "menu.window.flash", CmdIconKeys.Flash, null, Window,
            c => DoTarget(c, v => v.Extras.FlashCommand), c => CanTarget(c, v => v.Extras.FlashCommand));
        Add(r, ShellCommandIds.ViewShowExplorer, "menu.view.showExplorer", "PdShellIconExplorer", null, View,
            c => Do(c, v => v.WindowMenu.ShowToolCommand, "Explorer"), c => Can(c, v => v.WindowMenu.ShowToolCommand, "Explorer"));
        Add(r, ShellCommandIds.ViewShowLog, "menu.view.showLog", "PdShellIconLog", null, View,
            c => Do(c, v => v.WindowMenu.ShowToolCommand, "Log"),
            c => Vm(c) is { WindowMenu.IsLogToolAvailable: true } && Can(c, v => v.WindowMenu.ShowToolCommand, "Log"));
        Add(r, ShellCommandIds.ViewShowNotifications, "menu.view.showNotifications", "PdShellIconNotify", null, View,
            c => Do(c, v => v.WindowMenu.ShowToolCommand, "Notifications"), c => Can(c, v => v.WindowMenu.ShowToolCommand, "Notifications"));
        Add(r, ShellCommandIds.ViewFontReset, "menu.view.fontReset", CmdIconKeys.FontReset, "Ctrl+D0", View,
            ShellPlatformCommands.ResetFont, ShellPlatformCommands.CanResetFont);
        Add(r, ShellCommandIds.ThemeToggle, "menu.view.toggleTheme", CmdIconKeys.ToggleTheme, null, View,
            c => Do(c, v => v.SettingsPanel.ToggleThemeCommand, null), c => Can(c, v => v.SettingsPanel.ToggleThemeCommand, null));
        Add(r, ShellCommandIds.ThemeNext, "menu.view.nextTheme", CmdIconKeys.NextTheme, null, View, NextTheme, c => Vm(c) is not null);
        Add(r, ShellCommandIds.SystemSettings, "menu.sys.settings", "PdShellIconSettings", null, SystemCategory,
            c => Do(c, SystemViewCommand, "Settings"), c => Can(c, SystemViewCommand, "Settings"));
        Add(r, ShellCommandIds.SystemRestartRequired, "sys.restartRequired.title", CmdIconKeys.GpuRestart, null, SystemCategory,
            c => Do(c, SystemViewCommand, "RestartRequired"), c => Can(c, SystemViewCommand, "RestartRequired"));
        Add(r, ShellCommandIds.SystemAppMonitor, "menu.sys.monitor.app", CmdIconKeys.AppMonitor, null, SystemCategory,
            c => Do(c, SystemViewCommand, "AppMonitor"), c => Can(c, SystemViewCommand, "AppMonitor"));
        Add(r, ShellCommandIds.SystemLogView, "menu.sys.log.view", "PdShellIconLog", null, SystemCategory,
            c => Do(c, SystemViewCommand, "LogView"), c => Can(c, SystemViewCommand, "LogView"));
        Add(r, ShellCommandIds.SystemLogFolder, "menu.sys.log.openFolder", CmdIconKeys.LogFolder, null, SystemCategory,
            c => Do(c, SystemViewCommand, "LogFolder"), c => Can(c, SystemViewCommand, "LogFolder"));
        Add(r, ShellCommandIds.SystemLogExport, "menu.sys.log.export", CmdIconKeys.LogExport, null, SystemCategory,
            c => Do(c, SystemViewCommand, "LogExport"), c => Can(c, SystemViewCommand, "LogExport"));
        Add(r, ShellCommandIds.SystemInquiry, "menu.sys.inquiry", CmdIconKeys.Inquiry, null, SystemCategory,
            c => Do(c, SystemViewCommand, "Inquiry"), c => Can(c, SystemViewCommand, "Inquiry"));
        Add(r, ShellCommandIds.SystemInquiryList, "menu.sys.inquiryList", CmdIconKeys.InquiryList, null, SystemCategory,
            c => Do(c, SystemViewCommand, "InquiryList"), c => Can(c, SystemViewCommand, "InquiryList"));
        Add(r, ShellCommandIds.SystemUpdate, "menu.sys.update", CmdIconKeys.Update, null, SystemCategory,
            c => Do(c, SystemViewCommand, "Update"), c => Can(c, SystemViewCommand, "Update"));
        Add(r, ShellCommandIds.SystemChangelog, "menu.sys.changelog", CmdIconKeys.Changelog, null, SystemCategory,
            c => Do(c, SystemViewCommand, "Changelog"), c => Can(c, SystemViewCommand, "Changelog"));
        Add(r, ShellCommandIds.SystemHelp, "menu.sys.help", "PdShellIconHelp", null, SystemCategory,
            c => Do(c, SystemViewCommand, "Help"), c => Can(c, SystemViewCommand, "Help"));
        Add(r, ShellCommandIds.SystemAbout, "menu.sys.about", CmdIconKeys.About, null, SystemCategory,
            c => Do(c, SystemViewCommand, "About"), c => Can(c, SystemViewCommand, "About"));
        Add(r, ShellCommandIds.NavOpenPanel, "nav.tab.more", CmdIconKeys.WinListMore, null, Nav, OpenNavPanel, CanOpenNavPanel);
    }

    /// <summary>창 메뉴 호스트 명령(WindowMenu.WindowMenuCommand). 파라미터는 창 메뉴 동작 이름이다.</summary>
    private static ICommand WindowMenuCommand(MainViewModel v) => v.WindowMenu.WindowMenuCommand;

    /// <summary>시스템 화면 열기(SystemMenu.SystemViewCommand). 파라미터는 시스템 화면 이름이다.</summary>
    private static ICommand SystemViewCommand(MainViewModel v) => v.SystemMenu.SystemViewCommand;

    /// <summary>셸이 있을 때의 메인 뷰모델. 앱 없음(셸 null)이면 null 이다.</summary>
    private static MainViewModel? Vm(UiCommandContext c) => c.App.Shell is null ? null : c.App.MainViewModel;

    /// <summary>파라미터가 문자열이면 그 값, 아니면 null(뷰모델 명령에 다른 형식을 넘겨 예외가 나지 않게 한다).</summary>
    private static string? Text(UiCommandContext c) => c.Parameter as string;

    private static bool Can(UiCommandContext c, Func<MainViewModel, ICommand> pick, object? parameter) =>
        Vm(c) is { } vm && pick(vm).CanExecute(parameter);

    private static void Do(UiCommandContext c, Func<MainViewModel, ICommand> pick, object? parameter)
    {
        if (Vm(c) is { } vm)
        {
            pick(vm).Execute(parameter);
        }
    }

    private static bool CanTarget(UiCommandContext c, Func<MainViewModel, ICommand> pick) =>
        Vm(c) is { } vm && c.App.Shell is { } s && Target(s, c) is { } d && pick(vm).CanExecute(d);

    private static void DoTarget(UiCommandContext c, Func<MainViewModel, ICommand> pick)
    {
        if (Vm(c) is { } vm && c.App.Shell is { } s && Target(s, c) is { } d)
        {
            pick(vm).Execute(d);
        }
    }

    /// <summary>다음 색 테마로 바꾸고 로그를 남긴다(옛 보기 메뉴 OnNextTheme 과 같다).</summary>
    private static void NextTheme(UiCommandContext c)
    {
        if (Vm(c) is not { } vm)
        {
            return;
        }

        var theme = c.App.Themes.SelectNext();
        vm.AddLog(Loc.Tr("log.settings.theme", theme.Name));
    }

    private static string? NavPanel(UiCommandContext c) => c.Parameter switch
    {
        null => "menu",
        string s when s is "explorer" or "docs" or "notify" or "settings" or "menu" => s,
        _ => null,
    };

    private static bool CanOpenNavPanel(UiCommandContext c) => c.App.Shell is not null && NavPanelOpener is not null && NavPanel(c) is not null;

    private static void OpenNavPanel(UiCommandContext c)
    {
        if (c.App.Shell is not null && NavPanelOpener is { } open && NavPanel(c) is { } panel)
        {
            open(panel);
        }
    }
}
