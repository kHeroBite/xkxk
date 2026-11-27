// IViewRegistry 구현 — 화면 형식을 직접 아는 유일한 연결 지점
using xkxk.frameworks.ui.shell;
using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using xkxk.frameworks.ui.shell.Diagnostics.Remote;
using xkxk.frameworks.ui.shell.Docking;
using xkxk.frameworks.ui.shell.Framework;
using xkxk.frameworks.ui.shell.ViewModels;

namespace xkxk.frameworks.ui.views;

/// <summary>화면 층이 셸에 주는 화면 연결점.</summary>
public sealed class PdViewRegistry : IViewRegistry
{
    public IDocumentViewFactory DocumentViews { get; } = new DefaultDocumentViewFactory();

    public ISystemViews SystemViews { get; } = new xkxk.frameworks.ui.views.SystemMenu.SystemViews();

    public Type ChromeButtonsType => typeof(ChromeButtons);

    /// <summary>시험대 모듈이 벤치 명령(xkxk.bench.open)을 등록했는지로 판정한다.</summary>
    public bool BenchAvailable => XkxkUi.Current.Commands.Contains(ShellCommandIds.BenchOpen);

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
        xkxk.frameworks.ui.views.Diagnostics.Gallery.ControlGallery.RegisterRemote(router);
        xkxk.frameworks.ui.views.Diagnostics.PanelPositionMenuDiag.RegisterRemote(router);
#else
        _ = router;
#endif
    }

    /// <summary>벤치 열기 명령(xkxk.bench.open)을 target 파라미터로 실행한다(시험대가 처리, 재귀 없음).</summary>
    public void OpenBench(string target) => XkxkUi.Current.Commands.Execute(ShellCommandIds.BenchOpen, target);
}
