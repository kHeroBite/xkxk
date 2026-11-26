// 메인 뷰모델의 하위 뷰모델 — 빠른 접근 명령(글자 초기화·라이트/다크 토글)과 설정 변경 로그. 설정 값 화면은 통합 설정 창(ViewModels/Settings)
using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using xkxk.frameworks.ui.shell.Localization;
using xkxk.frameworks.ui.shell.Theming;
using xkxk.frameworks.ui.shell.Framework;

namespace xkxk.frameworks.ui.shell.ViewModels;

/// <summary>빠른 접근 명령·설정 변경 로그. 바인딩 경로는 MainViewModel.SettingsPanel.X.</summary>
public sealed class SettingsPanelViewModel : ObservableObject
{
    private readonly ShellContext _ctx;

    public SettingsPanelViewModel(ShellContext ctx)
    {
        _ctx = ctx;
        ResetFontCommand = new RelayCommand(Settings.ResetFontSize);
        ToggleThemeCommand = new RelayCommand(Settings.ToggleTheme);

        // 설정 값이 어디서 바뀌든(Ctrl+휠·단축키·설정 창) 로그에 남긴다.
        Settings.PropertyChanged += (_, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(UiSettings.BaseFontSize):
                    ctx.AddLog(Loc.Tr("log.settings.fontSize", Settings.BaseFontSize.ToString("0", System.Globalization.CultureInfo.CurrentCulture)));
                    break;
                case nameof(UiSettings.Theme):
                    OnPropertyChanged(nameof(IsDarkTheme)); // 타이틀바 테마 아이콘(해/달) 갱신
                    ctx.AddLog(Loc.Tr("log.settings.theme", ThemeNames[(int)Settings.Theme]));
                    break;
                case nameof(UiSettings.FontName):
                    ctx.AddLog(Loc.Tr("log.settings.font", Settings.FontName));
                    break;
                case nameof(UiSettings.WindowStyle):
                    ctx.AddLog(Loc.Tr("log.settings.windowStyle", WindowStyleNames[(int)Settings.WindowStyle]));
                    break;
                case nameof(UiSettings.WheelMode):
                    ctx.AddLog(Loc.Tr("log.settings.wheelMode", WheelModeNames[(int)Settings.WheelMode]));
                    break;
            }
        };
    }

    /// <summary>언어 구독 시작. 원래 순서(윈도우 메뉴 초기화 뒤)를 지키려고 MainViewModel 생성자가 따로 부른다.</summary>
    internal void InitLanguage()
    {
        XkxkUi.Current.Localization.LanguageChanged += OnLanguageChanged;
    }

    /// <summary>언어가 바뀐 직후 발생 — MainViewModel 이 윈도우 메뉴 라벨(MagnetRangeLabel)을 다시 알리게 한다.</summary>
    public event Action? LanguageChanged;

    private UiSettings Settings => _ctx.Settings;

    /// <summary>테마 이름(ThemeMode 순서와 같음).</summary>
    private IReadOnlyList<LocalizedOption> ThemeNames { get; } =
        LocalizedOption.List("settings.theme.system", "settings.theme.light", "settings.theme.dark");

    /// <summary>창 스타일 이름(ChromeStyleKind 순서와 같음 — Windows, MacOS, MarsClassic, Auto).</summary>
    private IReadOnlyList<LocalizedOption> WindowStyleNames { get; } =
        LocalizedOption.List("settings.style.windows", "settings.style.macos", "settings.style.mars", "settings.style.auto");

    /// <summary>Ctrl+휠 동작 이름(CtrlWheelMode 순서와 같음).</summary>
    private IReadOnlyList<LocalizedOption> WheelModeNames { get; } = LocalizedOption.List("settings.wheel.font", "settings.wheel.zoom");

    public RelayCommand ResetFontCommand { get; }
    public RelayCommand ToggleThemeCommand { get; }

    /// <summary>지금 어두운 테마인가 — 타이틀바 테마 아이콘이 누르면 바뀔 쪽(어두우면 해, 밝으면 달)을 고르는 판정. UiSettings.ToggleTheme 과 같은 기준이다.</summary>
    public bool IsDarkTheme => Settings.Theme == ThemeMode.Dark || (Settings.Theme != ThemeMode.Light && ThemeCatalog.Current.Requested.Base == ThemeBase.Dark);

    private void OnLanguageChanged() => LanguageChanged?.Invoke();
}
