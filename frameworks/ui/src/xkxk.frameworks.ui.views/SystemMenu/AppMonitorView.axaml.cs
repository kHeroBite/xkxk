// 앱 자원 모니터 화면 코드비하인드 — 화면 로드와 열기 진입점만 담당(창이 닫히면 SystemDialogHost 가 뷰모델을 Dispose 해 타이머를 멈춘다)
using xkxk.frameworks.ui.shell;
using xkxk.frameworks.ui.shell.Platform;
using Avalonia.Controls;
using xkxk.frameworks.ui.shell.ViewModels.SystemMenu;

namespace xkxk.frameworks.ui.views.SystemMenu;

public partial class AppMonitorView : UserControl
{
    public AppMonitorView() => InitializeComponent();

    /// <summary>앱 자원 모니터 창을 연다(이미 열려 있으면 활성화).</summary>
    public static void Open(Control? anchor = null) =>
        SystemDialogHost.Open("monitor", "sysview.monitor.windowTitle", () => new AppMonitorView { DataContext = new AppMonitorViewModel() }, anchor, 420, 300);
}
