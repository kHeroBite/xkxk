// 문의목록 화면 코드비하인드 — 화면 로드와 열기 진입점만 담당(동작은 InquiryListViewModel)
using Pd.Poc.Shell;
using Pd.Poc.Shell.Platform;
using Avalonia.Controls;
using Pd.Poc.Shell.ViewModels.SystemMenu;

namespace Pd.Ui.Views.SystemMenu;

public partial class InquiryListView : UserControl
{
    public InquiryListView() => InitializeComponent();

    /// <summary>문의목록 창을 연다(이미 열려 있으면 활성화).</summary>
    public static void Open(Control? anchor = null) =>
        SystemDialogHost.Open("inquiryList", "sysview.inquiryList.windowTitle", () => new InquiryListView { DataContext = new InquiryListViewModel() }, anchor, 620, 520);
}
