// '다시 시작해야 적용됩니다' 대화 코드비하인드 — 재시작 훅(SystemHost.Restart) 유무로 버튼을 바꾸고, [지금 다시 시작]은 설정 저장을 기다린 뒤 재시작한다
using Pd.Poc.Shell;
using Pd.Poc.Shell.Platform;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Pd.Poc.Shell.Localization;
using Pd.Poc.Shell.ViewModels;

namespace Pd.Ui.Views.SystemMenu;

public partial class RestartRequiredView : UserControl
{
    /// <summary>창·플라이아웃 이름(SystemDialogHost). AutomationId 는 sys.dlg.restartRequired 다.</summary>
    public const string DialogName = "restartRequired";

    public RestartRequiredView()
    {
        InitializeComponent();
        var canRestart = SystemHost.Restart is not null;
        NowButton.IsVisible = canRestart;
        NextLaunchText.IsVisible = !canRestart;
        BodyText.IsVisible = canRestart;
        if (!canRestart)
        {
            LaterButton.Content = Loc.Tr("common.ok"); // 재시작할 수 없는 플랫폼은 '다음 실행부터' 안내와 확인만 둔다
        }
    }

    /// <summary>대화를 연다(이미 열려 있으면 활성화).</summary>
    public static void Open(Control? anchor = null) =>
        SystemDialogHost.Open(DialogName, "sys.restartRequired.title", () => new RestartRequiredView(), anchor, 420, 220);

    private async void OnNow(object? sender, RoutedEventArgs e)
    {
        NowButton.IsEnabled = false;
        await SystemHost.RestartAsync(); // 설정 저장을 최대 1.5초 기다린 뒤 호스트 재시작(odev-2r 계약)
    }

    private void OnLater(object? sender, RoutedEventArgs e) => SystemDialogHost.Close(DialogName);
}
