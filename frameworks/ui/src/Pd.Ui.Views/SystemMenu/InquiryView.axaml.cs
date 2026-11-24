// 문의하기 화면 코드비하인드 — 화면 로드와 열기 진입점만 담당(동작은 InquiryViewModel)
using Pd.Poc.Shell;
using Pd.Poc.Shell.Platform;
using Avalonia.Controls;
using Pd.Poc.Shell.ViewModels.SystemMenu;

namespace Pd.Ui.Views.SystemMenu;

public partial class InquiryView : UserControl
{
    public InquiryView() => InitializeComponent();

    /// <summary>문의하기 창을 연다(이미 열려 있으면 활성화).</summary>
    public static void Open(Control? anchor = null) =>
        SystemDialogHost.Open("inquiry", "sysview.inquiry.windowTitle", () => new InquiryView { DataContext = new InquiryViewModel() }, anchor, 520, 520);
}
