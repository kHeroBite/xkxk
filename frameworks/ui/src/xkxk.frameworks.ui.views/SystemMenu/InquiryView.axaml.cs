// 문의하기 화면 코드비하인드 — 화면 로드와 열기 진입점만 담당(동작은 InquiryViewModel)
using xkxk.frameworks.ui.shell;
using xkxk.frameworks.ui.shell.Platform;
using Avalonia.Controls;
using xkxk.frameworks.ui.shell.ViewModels.SystemMenu;

namespace xkxk.frameworks.ui.views.SystemMenu;

public partial class InquiryView : UserControl
{
    public InquiryView() => InitializeComponent();

    /// <summary>문의하기 창을 연다(이미 열려 있으면 활성화).</summary>
    public static void Open(Control? anchor = null) =>
        SystemDialogHost.Open("inquiry", "sysview.inquiry.windowTitle", () => new InquiryView { DataContext = new InquiryViewModel() }, anchor, 520, 520);
}
