// 프로그램 정보 화면 코드비하인드 — 화면 로드와 열기 진입점만 담당(값은 AboutViewModel·SystemInfo)
using Pd.Poc.Shell;
using Pd.Poc.Shell.Platform;
using Avalonia.Controls;
using Pd.Poc.Shell.ViewModels.SystemMenu;

namespace Pd.Ui.Views.SystemMenu;

public partial class AboutView : UserControl
{
    public AboutView() => InitializeComponent();

    /// <summary>프로그램 정보 창을 연다(이미 열려 있으면 활성화).</summary>
    public static void Open(Control? anchor = null) =>
        SystemDialogHost.Open("about", "sysview.about.windowTitle", () => new AboutView { DataContext = new AboutViewModel() }, anchor, 520, 460);
}
