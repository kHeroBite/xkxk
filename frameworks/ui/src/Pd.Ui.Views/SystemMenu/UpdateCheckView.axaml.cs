// 업데이트 확인 화면 코드비하인드 — 열 때 바로 1회 확인하고, 진입점만 담당(값은 AboutViewModel)
using Pd.Poc.Shell;
using Pd.Poc.Shell.Platform;
using Avalonia.Controls;
using Pd.Poc.Shell.ViewModels.SystemMenu;

namespace Pd.Ui.Views.SystemMenu;

public partial class UpdateCheckView : UserControl
{
    public UpdateCheckView() => InitializeComponent();

    /// <summary>업데이트 확인 창을 연다(이미 열려 있으면 활성화). 열면서 확인을 1회 실행한다.</summary>
    public static void Open(Control? anchor = null) =>
        SystemDialogHost.Open("update", "sysview.update.windowTitle", () =>
        {
            var vm = new AboutViewModel();
            vm.CheckUpdateCommand.Execute(null);
            return new UpdateCheckView { DataContext = vm };
        }, anchor, 480, 320);
}
