// 셸 메인 화면의 제목 줄 옵션 부분 — 제목 탭 표시 방식 3종(아이콘+제목·아이콘·제목) 클래스·아이콘만 탭 폭, 즐겨찾기 자리(end·before_sdi)·폭 상한(22 §3·§4)
using System;
using Avalonia.Controls;
using xkxk.frameworks.ui.shell.Theming;

namespace xkxk.frameworks.ui.views;

public partial class MainView
{
    private const double DefaultTitleTabMinWidth = 72; // MainView.axaml 리소스 PdTitleTabMinWidth·MaxWidth 기본값(아이콘+제목·제목 방식)
    private const double DefaultTitleTabMaxWidth = 240;
    private bool _stripSizeHooked;

    private static bool IsTitleStripOption(string? name) => name is nameof(UiSettings.TitleTabDisplay) or nameof(UiSettings.BusyIconTitleOnly) or nameof(UiSettings.BusyIconEnabled)
        or nameof(UiSettings.FavoriteBarPosition) or nameof(UiSettings.FavoriteBarVisible);

    /// <summary>설정을 제목 줄에 반영한다. 로드 때(ApplyTabCloseMode 경유)와 관련 설정이 바뀔 때 부른다.</summary>
    private void ApplyTitleStripOptions()
    {
        var settings = UiSettings.Current;

        // 표시 방식(ui.shell.title_tab.display): 클래스는 MainView.axaml ListBox 스타일이 읽는다
        var display = settings.TitleTabDisplay;
        TaskbarTabs.Classes.Set("tabsIcon", display == TitleTabDisplay.Icon);
        TaskbarTabs.Classes.Set("tabsTitle", display == TitleTabDisplay.Title);
        TaskbarTabs.Classes.Set("busyInline", settings.BusyIconEnabled && settings.BusyIconTitleOnly == BusyTitleOnly.Inline);
        TaskbarTabs.Classes.Set("busyUnderline", settings.BusyIconEnabled && settings.BusyIconTitleOnly == BusyTitleOnly.Underline);

        // 아이콘만이면 탭 폭을 운영 값(ui.shell.title_tab.icon_only_width_dp)으로 고정한다 — 제목 탭 패널은 이 리소스를 최소·최대 폭으로 쓴다
        var iconOnly = display == TitleTabDisplay.Icon;
        Resources["PdTitleTabMinWidth"] = iconOnly ? settings.TitleTabIconOnlyWidthDp : DefaultTitleTabMinWidth;
        Resources["PdTitleTabMaxWidth"] = iconOnly ? settings.TitleTabIconOnlyWidthDp : DefaultTitleTabMaxWidth;

        // 즐겨찾기 자리(ui.shell.favorite_bar.position): end = [독립 창][구분선][즐겨찾기], before_sdi = [즐겨찾기][구분선][독립 창]
        var beforeSdi = settings.FavoriteBarPosition == FavoriteBarPosition.BeforeSdi;
        Grid.SetColumn(SdiIcons, beforeSdi ? 3 : 1);
        Grid.SetColumn(FavoriteBarHost, beforeSdi ? 1 : 3);

        if (!_stripSizeHooked)
        {
            _stripSizeHooked = true;
            TitleStripGrid.SizeChanged += (_, _) => UpdateFavoriteBarWidth();
        }

        UpdateFavoriteBarWidth();
    }

    // 폭 상한(ui.shell.favorite_bar.max_width_pct, 운영 값): 제목 줄 폭의 그 비율. 넘치면 FavoriteBarView 가 » 로 보낸다
    private void UpdateFavoriteBarWidth()
    {
        var width = TitleStripGrid.Bounds.Width;
        FavoriteBarHost.MaxAllowedWidth = width > 0 ? Math.Floor(width * UiSettings.Current.FavoriteBarMaxWidthPct / 100.0) : double.PositiveInfinity;
    }
}
