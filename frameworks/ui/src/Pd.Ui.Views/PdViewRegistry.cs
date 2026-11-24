// IViewRegistry 구현 — 화면 형식을 직접 아는 유일한 연결 지점
using Pd.Poc.Shell;
using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using Pd.Poc.Shell.Diagnostics.Remote;
using Pd.Poc.Shell.Docking;
using Pd.Poc.Shell.ViewModels;

namespace Pd.Ui.Views;

/// <summary>화면 층이 셸에 주는 화면 연결점.</summary>
public sealed class PdViewRegistry : IViewRegistry
{
    public IDocumentViewFactory DocumentViews { get; } = new DefaultDocumentViewFactory();

    public ISystemViews SystemViews { get; } = new Pd.Ui.Views.SystemMenu.SystemViews();

    public Type ChromeButtonsType => typeof(ChromeButtons);

    public bool BenchAvailable => Pd.Ui.Views.Documents.BenchEntry.IsAvailable;

    public Control CreateMainView(MainViewModel vm) => new MainView { DataContext = vm };

    public void InstallInput(MainViewModel vm) => EscCloseInput.Install(vm);

    public void EnsureNotificationTemplate()
    {
        NotificationPanelView.EnsureTemplate();
        PanelPositionMenu.Install(); // 탭이 만들어지기 전에 탭 메뉴 구독(InitLayout 이 base 전에 부른다)
    }

    public void OpenThemeEditor() => ThemeEditorView.Open(null);

    public IStyle CreateInactiveChromeStyle() => new Themes.ChromeInactiveStyles();

    public Action CapturePanelViewState(IReadOnlyList<Visual> roots)
    {
        var state = PanelViewState.Capture(roots);
        return () => state.RestoreAfterLayout(roots);
    }

    public IResourceProvider CreatePinnedDockNoWindowTheme() => new Themes.PinnedDockNoWindowTheme();

    public void RegisterRemote(RemoteCommandRouter router)
    {
#if PD_DIAGNOSTICS
        Pd.Ui.Views.Documents.BenchEntry.RegisterRemote(router);
        Pd.Ui.Views.Diagnostics.Gallery.ControlGallery.RegisterRemote(router);
        Pd.Ui.Views.Diagnostics.PanelPositionMenuDiag.RegisterRemote(router);
#else
        _ = router;
#endif
    }

    public void OpenBench(string target) => Pd.Ui.Views.Documents.BenchEntry.Open(target);
}
