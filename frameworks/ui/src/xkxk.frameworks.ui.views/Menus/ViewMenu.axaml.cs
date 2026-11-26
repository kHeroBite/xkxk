// 제목줄 '보기' 상위 메뉴 — 배치 프리셋·도구 되돌리기·화면 모드·글꼴·테마(편집기·다음 테마)·설정 코드 비하인드
using xkxk.frameworks.ui.shell;
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using xkxk.frameworks.ui.shell.Input;
using xkxk.frameworks.ui.shell.Localization;
using xkxk.frameworks.ui.shell.Theming;
using xkxk.frameworks.ui.shell.ViewModels;
using xkxk.frameworks.ui.shell.Framework;

namespace xkxk.frameworks.ui.views.Menus;

/// <summary>제목줄 '보기' 상위 메뉴 — 배치 프리셋·도구 되돌리기·화면 모드·글꼴·테마(편집기·다음 테마)·설정. MainTitleBar.MainMenu 의 직속 MenuItem 이라 더보기 재배치(ReflowMenu)·키보드 탐색이 그대로 동작한다.</summary>
public partial class ViewMenu : MenuItem
{
    // 파생 형식은 기본 MenuItem 테마를 찾지 못하므로 테마 키를 MenuItem 으로 둔다(ChromeLogo 와 같은 방식)
    protected override Type StyleKeyOverride => typeof(MenuItem);

    public ViewMenu()
    {
        InitializeComponent();
        // U1 표기: 메뉴 템플릿(PlatformKeyGestureConverter, 'p' 형식)이 D0 을 '0' 으로 바꿔 Windows·웹·Android 는 'Ctrl+0' 으로 보인다(Avalonia 12.1.4 KeyGestureFormatInfo 확인).
        // 실제 키는 플랫폼 명령 수정키(SettingsInput.IsCommandModifier — macOS = Cmd)라 표기도 같은 수정키로 맞춘다 → macOS '⌘+0'
        if (Application.Current?.PlatformSettings?.HotkeyConfiguration.CommandModifiers is { } cmd && cmd != KeyModifiers.None)
        {
            FontResetItem.InputGesture = new KeyGesture(Key.D0, cmd);
        }
    }

    // U1: Ctrl+0 키 입력과 같은 경로(글자 크기 모드 = 기본 글자, 창 배율 모드 = 활성/마지막 휠 창 100%)
    private void OnResetFont(object? sender, RoutedEventArgs e) => SettingsInput.ResetFromMenu(TopLevel.GetTopLevel(this));

    // UI-07 색 테마: 다음 테마 순환(MainTitleBar 에서 옮김)
    private void OnNextTheme(object? sender, RoutedEventArgs e)
    {
        var theme = XkxkUi.Current.Themes.SelectNext();
        (DataContext as MainViewModel)?.AddLog(Loc.Tr("log.settings.theme", theme.Name));
    }
}
