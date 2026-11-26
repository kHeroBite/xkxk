// 창이 필요한 셸 기본 명령(종료·글자 초기화)의 실행·CanExecute 대리자 — 등록은 ShellDefaultCommands 가 한다(UADR-0014, R2)
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using xkxk.frameworks.ui.shell.Framework;
using xkxk.frameworks.ui.shell.Input;
using xkxk.frameworks.ui.shell.Theming;

namespace xkxk.frameworks.ui.shell.Platform;

/// <summary>
/// <see cref="ShellCommandIds.AppExit"/>·<see cref="ShellCommandIds.ViewFontReset"/> 의 동작. Avalonia 창(데스크톱 수명의 메인 창)을 써야 해서
/// Framework 등록 파일과 나눴다. 이 형식은 명령을 등록하지 않는다.
/// </summary>
internal static class ShellPlatformCommands
{
    /// <summary>데스크톱 수명(IClassicDesktopStyleApplicationLifetime)의 메인 창이 있으면 true. 웹·Android·콘솔(앱 없음)은 false.</summary>
    internal static bool HasDesktopMainWindow() => DesktopMainWindow() is not null;

    /// <summary>종료는 셸이 있고 데스크톱 메인 창이 있을 때만 실행할 수 있다(옛 파일 메뉴 종료 항목의 표시 조건과 같다).</summary>
    internal static bool CanExit(UiCommandContext c) => c.App.Shell is not null && HasDesktopMainWindow();

    /// <summary>메인 창 닫기와 같은 경로로 끝낸다 — 앱 종료 처리(자동 저장·설정 플러시)를 그대로 탄다.</summary>
    internal static void Exit(UiCommandContext c)
    {
        if (c.App.Shell is not null && DesktopMainWindow() is { } window)
        {
            window.Close();
        }
    }

    /// <summary>글자 초기화는 셸이 있고 메뉴 TopLevel 또는 메인 뷰모델이 있을 때 실행할 수 있다.</summary>
    internal static bool CanResetFont(UiCommandContext c) =>
        c.App.Shell is not null && (MenuTopLevel() is not null || c.App.MainViewModel is not null);

    /// <summary>
    /// 메뉴가 붙은 TopLevel(데스크톱은 메인 창, 웹·Android 는 단일 뷰의 TopLevel — 옛 보기 메뉴의 TopLevel.GetTopLevel(메뉴) 와 같은 대상)을 얻을 수 있으면
    /// Ctrl+0 키와 같은 경로(SettingsInput.ResetFromMenu — 글자 크기 모드는 기본 글자, 창 배율 모드는 대상 창 100%)를 탄다.
    /// 모바일형 화면(옛 모바일 메뉴 경로)이거나 TopLevel 을 얻을 수 없으면 설정 패널의 글자 초기화(SettingsPanel.ResetFontCommand)를 실행한다.
    /// </summary>
    internal static void ResetFont(UiCommandContext c)
    {
        if (c.App.Shell is null)
        {
            return;
        }

        var vm = c.App.MainViewModel;
        if (vm?.Compact.Style.Current != UiStyle.Mobile && MenuTopLevel() is { } top)
        {
            SettingsInput.ResetFromMenu(top);
        }
        else
        {
            vm?.SettingsPanel.ResetFontCommand.Execute(null);
        }
    }

    private static Window? DesktopMainWindow() =>
        Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } window } ? window : null;

    /// <summary>셸 메뉴가 붙는 TopLevel. 데스크톱은 메인 창, 단일 뷰 수명(웹·Android)은 MainView 의 TopLevel. 없으면 null.</summary>
    private static TopLevel? MenuTopLevel() => DesktopMainWindow() as TopLevel
        ?? (Application.Current?.ApplicationLifetime is ISingleViewApplicationLifetime { MainView: { } view } ? TopLevel.GetTopLevel(view) : null);
}
