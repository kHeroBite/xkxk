// 통합 설정 창 코드비하인드(요건 7) — 열기 진입점(데스크톱 새 창·웹/Android 플라이아웃)과 Esc 닫기. 값·검색·기본값은 SettingsWindowViewModel
using Pd.Poc.Shell;
using Pd.Poc.Shell.Platform;
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Pd.Poc.Shell.ViewModels.Settings;

namespace Pd.Ui.Views.Settings;

public partial class SettingsView : UserControl, ISettingsRootView
{
    private const double WindowWidth = 760;
    private const double WindowHeight = 560;

    private TopLevel? _top;

    public SettingsView() => InitializeComponent();

    /// <summary>설정 창을 연다(이미 열려 있으면 활성화). 호출은 SystemViews 가 시스템 메뉴·톱니·☰ 공통으로 한다.</summary>
    public static void Open(Control? anchor = null)
    {
        if (App.MainViewModel is not { } main)
        {
            return;
        }

        SystemDialogHost.Open(
            SettingsWindowViewModel.DialogName,
            SettingsWindowViewModel.WindowTitleKey,
            () => new SettingsView { DataContext = new SettingsWindowViewModel(main) },
            anchor,
            WindowWidth,
            WindowHeight);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is SettingsWindowViewModel vm)
        {
            vm.Appearance.OpenThemeEditor = () => ThemeEditorView.Open(this);
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _top = TopLevel.GetTopLevel(this);
        // 터널 단계: 포커스가 없어도 받고, 전역 Esc 창닫기(EscCloseInput, 버블)가 활성 문서를 닫기 전에 처리한다
        _top?.AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _top?.RemoveHandler(KeyDownEvent, OnWindowKeyDown);
        _top = null;
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        // 시스템 Flyout 이 열려 있으면(이 화면 자신 포함) 맨 위부터 닫는 FittedFlyout 이 처리한다 — 테마 편집기 위의 Esc 로 설정이 먼저 닫히지 않게 한다
        if (e.Key != Key.Escape || e.KeyModifiers != KeyModifiers.None || FittedFlyout.AnyOpen)
        {
            return;
        }

        // 열린 콤보의 Esc 는 콤보가 닫는다(창까지 같이 닫지 않는다)
        if (_top?.FocusManager?.GetFocusedElement() is ComboBox { IsDropDownOpen: true })
        {
            return;
        }

        e.Handled = true;
        CloseSelf();
    }

    private void OnClose(object? sender, RoutedEventArgs e) => CloseSelf();

    private void CloseSelf()
    {
        if ((DataContext as SettingsWindowViewModel)?.CloseAction is { } close)
        {
            close();
        }
        else
        {
            SystemDialogHost.Close(SettingsWindowViewModel.DialogName);
        }
    }
}
