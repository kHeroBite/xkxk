// 앱 진입점 — 전역 스타일·DataTemplate(PdApp.axaml)을 싣고 셸에 화면 층을 등록한다. 시작 절차는 기반 클래스 App 이 맡는다
using Avalonia.Markup.Xaml;
using xkxk.frameworks.ui.shell;
using xkxk.frameworks.ui.shell.Diagnostics.Remote;

namespace xkxk.frameworks.ui.views;

/// <summary>호스트가 시작하는 Application. 셸의 App 을 상속한다.</summary>
public partial class PdApp : App
{
    public PdApp()
    {
        ViewRegistry.Current = new PdViewRegistry();
        ViewRegistry.Current.RegisterRemote(RemoteCommandRouter.Default); // 라우터는 호스트 Main 이 Application 보다 먼저 만든다. 화면 층 진단 명령은 여기서 더한다
    }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);
}
