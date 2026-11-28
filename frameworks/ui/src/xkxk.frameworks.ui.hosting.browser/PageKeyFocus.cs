// 웹 문서 초점 옮기기 — 문서 초점이 BODY 면 키가 앱에 오지 않으므로(화면설계 W-16), Flyout 이 열릴 때 앱 컨테이너로 옮긴다
using System.Runtime.InteropServices.JavaScript;
using xkxk.frameworks.ui.shell.Platform;

namespace xkxk.frameworks.ui.hosting.browser;

internal static partial class PageKeyFocus
{
    [JSImport("globalThis.__pdFocusAppHost")]
    private static partial void FocusAppHost();

    internal static void Install() => FittedFlyout.HostKeyFocus = FocusAppHost;
}
