// 설정 창 '창' 페이지의 셸 표시 설정(결정 143) — 제목 탭 표시·메뉴 아이콘·제목 줄 즐겨찾기·불러오는 중 아이콘·끌기 중 그림자
using System;
using System.Collections.Generic;
using xkxk.frameworks.ui.shell.Docking;
using xkxk.frameworks.ui.shell.Localization;
using xkxk.frameworks.ui.shell.Theming;

namespace xkxk.frameworks.ui.shell.ViewModels.Settings;

public sealed partial class WindowPageViewModel
{
    private static readonly TitleTabDisplay[] s_tabDisplays = { TitleTabDisplay.IconTitle, TitleTabDisplay.Icon, TitleTabDisplay.Title };

    private static readonly IReadOnlyList<LocalizedOption> s_tabDisplayNames =
        LocalizedOption.List("settings.titleTabDisplay.iconTitle", "settings.titleTabDisplay.icon", "settings.titleTabDisplay.title");

    private static readonly FavoriteBarPosition[] s_favoritePositions = { FavoriteBarPosition.End, FavoriteBarPosition.BeforeSdi };

    private static readonly IReadOnlyList<LocalizedOption> s_favoritePositionNames =
        LocalizedOption.List("settings.favoritePosition.end", "settings.favoritePosition.beforeSdi");

    private static readonly FavoriteBarClick[] s_favoriteClicks = { FavoriteBarClick.ActivateOrOpen, FavoriteBarClick.AlwaysNew };

    private static readonly IReadOnlyList<LocalizedOption> s_favoriteClickNames =
        LocalizedOption.List("settings.favoriteClick.activateOrOpen", "settings.favoriteClick.alwaysNew");

    private static readonly BusyTitleOnly[] s_busyTitleOnly = { BusyTitleOnly.Inline, BusyTitleOnly.Underline, BusyTitleOnly.None };

    private static readonly IReadOnlyList<LocalizedOption> s_busyTitleOnlyNames =
        LocalizedOption.List("settings.busyTitleOnly.inline", "settings.busyTitleOnly.underline", "settings.busyTitleOnly.none");

    private static readonly MoveShadowMode[] s_moveShadows = { MoveShadowMode.Hide, MoveShadowMode.Keep };

    private static readonly IReadOnlyList<LocalizedOption> s_moveShadowNames =
        LocalizedOption.List("settings.moveShadow.hide", "settings.moveShadow.keep");

    public IReadOnlyList<LocalizedOption> TitleTabDisplayNames => s_tabDisplayNames;

    public int TitleTabDisplayIndex
    {
        get => Array.IndexOf(s_tabDisplays, Settings.TitleTabDisplay);
        set
        {
            if (IsNewIndex(value, s_tabDisplays.Length, TitleTabDisplayIndex)) { Settings.TitleTabDisplay = s_tabDisplays[value]; }
        }
    }

    public bool MenuIconVisible { get => Settings.MenuIconVisible; set => Settings.MenuIconVisible = value; }

    public bool FavoriteBarVisible { get => Settings.FavoriteBarVisible; set => Settings.FavoriteBarVisible = value; }

    public IReadOnlyList<LocalizedOption> FavoritePositionNames => s_favoritePositionNames;

    public int FavoritePositionIndex
    {
        get => Array.IndexOf(s_favoritePositions, Settings.FavoriteBarPosition);
        set
        {
            if (IsNewIndex(value, s_favoritePositions.Length, FavoritePositionIndex)) { Settings.FavoriteBarPosition = s_favoritePositions[value]; }
        }
    }

    public IReadOnlyList<LocalizedOption> FavoriteClickNames => s_favoriteClickNames;

    public int FavoriteClickIndex
    {
        get => Array.IndexOf(s_favoriteClicks, Settings.FavoriteBarClick);
        set
        {
            if (IsNewIndex(value, s_favoriteClicks.Length, FavoriteClickIndex)) { Settings.FavoriteBarClick = s_favoriteClicks[value]; }
        }
    }

    public bool BusyIconEnabled { get => Settings.BusyIconEnabled; set => Settings.BusyIconEnabled = value; }

    public IReadOnlyList<LocalizedOption> BusyTitleOnlyNames => s_busyTitleOnlyNames;

    /// <summary>대체 표시는 제목 탭이 '제목만'일 때만 쓰인다.</summary>
    public bool BusyTitleOnlyAvailable => Settings.BusyIconEnabled && Settings.TitleTabDisplay == TitleTabDisplay.Title;

    public int BusyTitleOnlyIndex
    {
        get => Array.IndexOf(s_busyTitleOnly, Settings.BusyIconTitleOnly);
        set
        {
            if (IsNewIndex(value, s_busyTitleOnly.Length, BusyTitleOnlyIndex)) { Settings.BusyIconTitleOnly = s_busyTitleOnly[value]; }
        }
    }

    public IReadOnlyList<LocalizedOption> MoveShadowNames => s_moveShadowNames;

    /// <summary>보이는 값은 실효값(테마·효과 단계·회사 값 포함)이고, 고르면 사용자 값으로 저장한다.</summary>
    public int MoveShadowIndex
    {
        get => Array.IndexOf(s_moveShadows, MdiMoveLight.Shadow);
        set
        {
            if (IsNewIndex(value, s_moveShadows.Length, MoveShadowIndex)) { Settings.MdiMoveLightShadowUser = s_moveShadows[value]; }

            OnPropertyChanged(); // 회사 잠금으로 실효값이 그대로면 콤보를 되돌린다
        }
    }

    // 전환 효과를 쓸 활성화 출처(ui.motion.switch_sources, 21 §3.2) — 보이는 값은 실효값, 바꾸면 사용자 값으로 저장
    public bool SwitchSourcesEditable => !DesignThemeResolver.Locked;

    public string SwitchSourcesTip => Loc.Tr(SwitchSourcesEditable ? "settings.switchSources.tip" : "settings.switchSources.locked");

    public bool SwitchByKeyboard { get => HasSource(MotionSwitchSources.Keyboard); set => SetSource(MotionSwitchSources.Keyboard, value); }

    public bool SwitchByTitleTab { get => HasSource(MotionSwitchSources.TitleTab); set => SetSource(MotionSwitchSources.TitleTab, value); }

    public bool SwitchByWindowList { get => HasSource(MotionSwitchSources.WindowList); set => SetSource(MotionSwitchSources.WindowList, value); }

    public bool SwitchByBodyClick { get => HasSource(MotionSwitchSources.BodyClick); set => SetSource(MotionSwitchSources.BodyClick, value); }

    private bool HasSource(MotionSwitchSources source) => Settings.MotionSwitchSources.HasFlag(source);

    private void SetSource(MotionSwitchSources source, bool on)
    {
        if (SwitchSourcesEditable && HasSource(source) != on)
        {
            Settings.MotionSwitchSourcesUser = on ? Settings.MotionSwitchSources | source : Settings.MotionSwitchSources & ~source;
        }
    }

    private void ResetShellToDefaults()
    {
        Settings.TitleTabDisplay = TitleTabDisplay.IconTitle;
        Settings.MenuIconVisible = true;
        Settings.FavoriteBarVisible = true;
        Settings.FavoriteBarPosition = FavoriteBarPosition.End;
        Settings.FavoriteBarClick = FavoriteBarClick.ActivateOrOpen;
        Settings.BusyIconEnabled = true;
        Settings.BusyIconTitleOnly = BusyTitleOnly.Inline;
        Settings.MdiMoveLightShadowUser = null;
        Settings.MotionSwitchSourcesUser = null;
    }
}
