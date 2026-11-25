// 변경이력 화면 코드비하인드 — 화면 로드와 열기 진입점만 담당(내용은 사전 키, 언어 전환 즉시 반영)
using xkxk.frameworks.ui.data.Navigation;
using xkxk.frameworks.ui.shell;
using xkxk.frameworks.ui.shell.Platform;
using Avalonia.Controls;

namespace xkxk.frameworks.ui.views.SystemMenu;

public partial class ChangelogView : UserControl
{
    public ChangelogView() => InitializeComponent();

    /// <summary>변경이력 창을 연다(이미 열려 있으면 활성화).</summary>
    public static void Open(Control? anchor = null) =>
        SystemDialogHost.Open("changelog", "sysview.changelog.windowTitle", () => new ChangelogView(), anchor, 560, 480, iconKey: CmdIconKeys.Changelog);
}
