// '창 더 보기…' 대화상자 코드 비하인드 — 목록 더블클릭을 확인과 같은 처리로 넘긴다(목록 데이터·활성화는 OpenWindowListViewModel)
using Pd.Poc.Shell;
using Avalonia.Controls;
using Avalonia.Input;
using Pd.Poc.Shell.ViewModels;

namespace Pd.Ui.Views;

/// <summary>'창 더 보기…' 대화상자. MainTitleBar 의 Popup 안에 놓인다(데스크톱·웹·Android 공통).</summary>
public partial class MoreWindowsView : UserControl
{
    public MoreWindowsView() => InitializeComponent();

    // 항목 위 더블클릭만 확인으로 본다(빈 곳 더블클릭은 무시)
    private void OnListDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (e.Source is Control { DataContext: OpenWindowSlot entry } && DataContext is OpenWindowListViewModel vm)
        {
            vm.ConfirmMore(entry);
            e.Handled = true;
        }
    }
}
