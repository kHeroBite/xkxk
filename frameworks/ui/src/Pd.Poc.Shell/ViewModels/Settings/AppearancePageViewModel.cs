// 설정 창 '모양' 페이지 뷰모델(요건 7) — 라이트/다크/시스템·색 테마·시스템 짝 테마·글꼴·글자 크기·Ctrl+휠. 바꾸면 UiSettings 가 즉시 적용·저장한다
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pd.Poc.Shell.Theming;

namespace Pd.Poc.Shell.ViewModels.Settings;

public sealed class AppearancePageViewModel : SettingsPageViewModel
{
    // 선택지 항목은 언어 구독을 해제하지 않는 앱 수명 객체라 창마다 만들지 않는다(LocalizedOption 규칙)
    private static readonly IReadOnlyList<LocalizedOption> s_themeNames =
        LocalizedOption.List("settings.theme.system", "settings.theme.light", "settings.theme.dark");

    private static readonly IReadOnlyList<LocalizedOption> s_wheelNames =
        LocalizedOption.List("settings.wheel.font", "settings.wheel.zoom");

    // 제목 탭 × 표시 방식 콤보 순서(TitleTabCloseMode 값 순서와 같다)
    private static readonly TitleTabCloseMode[] s_tabCloseModes =
        { TitleTabCloseMode.Auto, TitleTabCloseMode.Always, TitleTabCloseMode.Hover, TitleTabCloseMode.ActiveAndHover };

    private static readonly IReadOnlyList<LocalizedOption> s_tabCloseNames =
        LocalizedOption.List("settings.tabClose.auto", "settings.tabClose.always", "settings.tabClose.hover", "settings.tabClose.activeAndHover");

    public AppearancePageViewModel(MainViewModel main)
        : base(main)
    {
        SystemPair = new SystemThemePairViewModel(ThemeCatalog.Current, () => ThemeCatalog.Current.Requested);
        FontNames = Settings.FontNames; // 글꼴 목록은 호출마다 새로 만들어지므로 창이 열릴 때 1회만 읽는다
        OpenThemeEditorCommand = new RelayCommand(() => OpenThemeEditor?.Invoke());
        Settings.Changed += OnThemeApplied;
    }

    public override void Dispose()
    {
        Settings.Changed -= OnThemeApplied;
        base.Dispose();
    }

    /// <summary>적용 완료 신호. 설정 속성이 안 바뀌어도 요청 테마가 달라지는 경우(시스템 모드의 OS 라이트/다크 변경)가 있어 다시 읽게 한다.</summary>
    private void OnThemeApplied() => OnPropertyChanged(nameof(SelectedColorTheme));

    /// <summary>테마 편집기 열기. 창을 아는 뷰(SettingsView)가 채운다(뷰모델은 뷰를 참조하지 않는다).</summary>
    public Action? OpenThemeEditor { get; set; }

    public RelayCommand OpenThemeEditorCommand { get; }

    public IReadOnlyList<LocalizedOption> ThemeNames => s_themeNames;

    /// <summary>라이트/다크/시스템(ThemeMode 순서).</summary>
    public int ThemeIndex
    {
        get => (int)Settings.Theme;
        set
        {
            if (IsNewIndex(value, s_themeNames.Count, (int)Settings.Theme)) { Settings.Theme = (ThemeMode)value; }
        }
    }

    public ObservableCollection<ThemeDefinition> ColorThemes => ThemeCatalog.Current.Themes;

    /// <summary>선택한 색 테마 Id(저장값). 원격·시험 대조용으로도 쓴다.</summary>
    public string ThemeId => Settings.ThemeId;

    /// <summary>요청된 색 테마(적용은 프레임 뒤로 미뤄진다). 고르면 ThemeCatalog.Select 로 적용·저장하고 모드는 테마 기반을 따른다. 콤보가 되쓴 값은 같은 값이라 무시된다.</summary>
    public ThemeDefinition? SelectedColorTheme
    {
        get => ThemeCatalog.Current.Requested;
        set
        {
            if (value is not null && !ReferenceEquals(value, ThemeCatalog.Current.Requested))
            {
                ThemeCatalog.Current.Select(value.Id);
            }
        }
    }

    /// <summary>시스템 모드에서 OS 라이트/다크에 쓸 짝 테마.</summary>
    public SystemThemePairViewModel SystemPair { get; }

    public IReadOnlyList<string> FontNames { get; }

    public string FontName
    {
        get => Settings.FontName;
        set { if (!string.IsNullOrEmpty(value)) { Settings.FontName = value; } }
    }

    /// <summary>시스템(셸) 폰트 — 메뉴 줄·제목 줄·패널 등 문서 창 밖(01 §5.2, 2026-11-11).</summary>
    public string ShellFontName
    {
        get => Settings.ShellFontName;
        set { if (!string.IsNullOrEmpty(value)) { Settings.ShellFontName = value; } }
    }

    /// <summary>시스템(셸) 글자 크기.</summary>
    public decimal? ShellFontSize
    {
        get => (decimal)Settings.ShellFontSize;
        set { if (value is { } v) { Settings.ShellFontSize = (double)v; } }
    }

    /// <summary>제목 줄(셸 탭 줄) 높이 DIP(01 §2-1).</summary>
    public decimal? TabStripHeight
    {
        get => (decimal)Settings.ShellTabStripHeight;
        set { if (value is { } v) { Settings.ShellTabStripHeight = (double)v; } }
    }

    public decimal MinTabStripHeight => (decimal)UiSettings.MinShellTabStripHeight;

    public decimal MaxTabStripHeight => (decimal)UiSettings.MaxShellTabStripHeight;

    public IReadOnlyList<LocalizedOption> TabCloseModeNames => s_tabCloseNames;

    /// <summary>제목 탭 닫기(×) 표시 방식(01 §2-1). 바꾸면 제목 탭에 즉시 반영된다.</summary>
    public int TabCloseModeIndex
    {
        get => Array.IndexOf(s_tabCloseModes, Settings.TitleTabCloseMode);
        set
        {
            if (IsNewIndex(value, s_tabCloseModes.Length, TabCloseModeIndex)) { Settings.TitleTabCloseMode = s_tabCloseModes[value]; }
        }
    }

    public decimal MinFontSize => (decimal)UiSettings.MinFontSize;

    public decimal MaxFontSize => (decimal)UiSettings.MaxFontSize;

    /// <summary>문서 기본 글자 크기(Ctrl+0 이 돌아갈 값).</summary>
    public decimal? DocumentFontSize
    {
        get => (decimal)Settings.DocumentDefaultFontSize;
        set { if (value is { } v) { Settings.DocumentDefaultFontSize = (double)v; } }
    }

    /// <summary>지금 문서 글자 크기(Ctrl+휠·Ctrl+0 이 바꾸는 값).</summary>
    public decimal? CurrentFontSize
    {
        get => (decimal)Settings.BaseFontSize;
        set { if (value is { } v) { Settings.BaseFontSize = (double)v; } }
    }

    public IReadOnlyList<LocalizedOption> WheelModeNames => s_wheelNames;

    public int WheelModeIndex
    {
        get => (int)Settings.WheelMode;
        set
        {
            if (IsNewIndex(value, s_wheelNames.Count, (int)Settings.WheelMode)) { Settings.WheelMode = (CtrlWheelMode)value; }
        }
    }

    public override void ResetToDefaults()
    {
        Settings.Theme = ThemeMode.System;
        Settings.ThemeId = BuiltinThemes.LightId;
        ThemeCatalog.Current.SetSystemPair(BuiltinThemes.LightId, BuiltinThemes.DarkId);
        Settings.FontName = UiSettings.DefaultFontName;
        Settings.DocumentDefaultFontSize = UiSettings.DefaultFontSize;
        Settings.BaseFontSize = UiSettings.DefaultFontSize; // 문서 기본값이 이미 같으면 현재 크기가 따라오지 않으므로 직접 맞춘다
        Settings.WheelMode = CtrlWheelMode.FontSize;
        Settings.ShellFontName = UiSettings.DefaultFontName;
        Settings.ShellFontSize = UiSettings.DefaultFontSize;
        Settings.ShellTabStripHeight = UiSettings.DefaultShellTabStripHeight;
        Settings.TitleTabCloseMode = TitleTabCloseMode.Auto;
    }

    protected override void OnSettingChanged(object? sender, PropertyChangedEventArgs e)
    {
        base.OnSettingChanged(sender, e);
        if (e.PropertyName is nameof(UiSettings.SystemLightThemeId) or nameof(UiSettings.SystemDarkThemeId))
        {
            SystemPair.Refresh();
        }
    }

    protected override void OnLanguageChanged()
    {
        base.OnLanguageChanged();
        SystemPair.Refresh();
    }
}
