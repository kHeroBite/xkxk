// '창 더 보기…' 대화상자 코드 비하인드 — 목록 더블클릭·Enter 를 확인과 같은 처리로 넘긴다(목록 데이터·활성화는 OpenWindowListViewModel)
using xkxk.frameworks.ui.shell;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using xkxk.frameworks.ui.shell.ViewModels;

namespace xkxk.frameworks.ui.views;

/// <summary>'창 더 보기…' 대화상자. MainTitleBar 의 Popup 안에 놓인다(데스크톱·웹·Android 공통).</summary>
public partial class MoreWindowsView : UserControl
{
    public MoreWindowsView()
    {
        InitializeComponent();
        // Enter 는 터널링 단계에서 받는다(UTODO-0020). Avalonia 12.1.4 는 초점 가진 ListBoxItem 이 Enter KeyDown 을 선택 처리로 소비(Handled)해서
        // (ItemSelectionEventTriggers.ShouldTriggerSelection → SelectingItemsControl.UpdateSelectionFromEvent) 버블 단계 처리기·IsDefault 단추·KeyBindings 가 모두 못 받는다.
        // XAML 에는 터널링 구독 문법이 없어 여기서 단다
        List.AddHandler(KeyDownEvent, OnListKeyDown, RoutingStrategies.Tunnel);
    }

    // 항목 위 더블클릭만 확인으로 본다(빈 곳 더블클릭은 무시)
    private void OnListDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (e.Source is Control { DataContext: OpenWindowSlot entry } && DataContext is OpenWindowListViewModel vm)
        {
            vm.ConfirmMore(entry);
            e.Handled = true;
        }
    }

    // 목록에 초점이 있을 때 Enter = 확인(UTODO-0020) — 지금 선택된 항목을 확정한다
    private void OnListKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None
            && DataContext is OpenWindowListViewModel { SelectedEntry: { } entry } vm)
        {
            vm.ConfirmMore(entry);
            e.Handled = true;
        }
    }
}
