// 도움말 투어 창 판별(요건 6) — 눌린 컨트롤이 어느 창에 속하는지, 지금 UI 방식·플랫폼이 무엇인지 정한다
using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using xkxk.frameworks.ui.shell.Docking;
using xkxk.frameworks.ui.shell.Theming;

namespace xkxk.frameworks.ui.shell.Tour;

internal static class TourScopeResolver
{
    public static (TourScope? Scope, Visual Root) Resolve(TopLevel top, Visual origin)
    {
        if (origin.GetSelfAndVisualAncestors().OfType<MdiDocumentWindow>().FirstOrDefault() is { } mdi)
        {
            return (TourScope.MdiDocument, mdi);
        }

        if (top is HostWindow)
        {
            return (TourScope.SdiWindow, top);
        }

        var isMain = Application.Current?.ApplicationLifetime is not Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            || ReferenceEquals(desktop.MainWindow, top);
        if (isMain)
        {
            return (CurrentMode() == TourModes.Nav ? TourScope.Nav : TourScope.MainWindow, top);
        }

        var isSettings = top.GetVisualDescendants().OfType<xkxk.frameworks.ui.Controls.SettingsShell>().Any();
        return (isSettings ? TourScope.Settings : null, top);
    }

    public static TourModes CurrentMode() => App.MainViewModel?.Compact.Style.Current switch
    {
        UiStyle.Mobile => TourModes.Nav,
        UiStyle.Tablet => TourModes.Dock,
        _ => TourModes.Mdi,
    };

    public static TourPlatforms CurrentPlatform() =>
        OperatingSystem.IsBrowser() ? TourPlatforms.Web : OperatingSystem.IsAndroid() ? TourPlatforms.Android : TourPlatforms.Desktop;
}
