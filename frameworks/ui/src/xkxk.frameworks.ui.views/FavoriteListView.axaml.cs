// 세로 즐겨찾기 구역 코드 비하인드 — 항목 누름·빼기를 뷰모델로 넘기고, 연 뒤 ItemOpened 로 알린다(서랍 닫기는 쓰는 쪽)
using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using xkxk.frameworks.ui.shell.ViewModels;

namespace xkxk.frameworks.ui.views;

/// <summary>삼선 메뉴·탐색 패널 맨 위 즐겨찾기 구역. DataContext 는 FavoriteBarViewModel.</summary>
public partial class FavoriteListView : UserControl
{
    public FavoriteListView() => InitializeComponent();

    /// <summary>항목을 눌러 열거나 활성화한 뒤.</summary>
    public event EventHandler? ItemOpened;

    private void OnItemClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: FavoriteItem item } && DataContext is FavoriteBarViewModel vm)
        {
            vm.OpenCommand.Execute(item);
            ItemOpened?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnRemoveClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: FavoriteItem item } && DataContext is FavoriteBarViewModel vm)
        {
            vm.RemoveCommand.Execute(item);
        }
    }
}
