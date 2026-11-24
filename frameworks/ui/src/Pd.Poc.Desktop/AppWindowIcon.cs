// 작업 표시줄에 뜨는 모든 창에 앱 아이콘 지정(AC-026)과 진단 window.icon 의 Win32 아이콘 핸들 조회
using Avalonia.Controls;
using Pd.Poc.Shell.Platform;
#if PD_DIAGNOSTICS
using System.Runtime.InteropServices;
#endif

namespace Pd.Poc.Desktop;

/// <summary>
/// 창 열림 클래스 처리기 하나로 메인·SDI·대화·편집기 창에 ShellPlatform.AppIcon 을 넣는다(창 생성 지점마다 지정하지 않는다).
/// 작업 표시줄 밖 도구 창(ShowInTaskbar=false — 스냅 바·스냅 도우미)과 이미 아이콘을 지정한 창은 건드리지 않는다.
/// </summary>
internal static class AppWindowIcon
{
    /// <summary>창을 만들기 전에 한 번 부른다.</summary>
    public static void Install()
    {
        Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) => Apply(window));
#if PD_DIAGNOSTICS
        ShellPlatform.WindowIconProbe = Probe;
#endif
    }

    private static void Apply(Window window)
    {
        if (!window.ShowInTaskbar || window.Icon is not null)
        {
            return;
        }

        try
        {
            window.Icon = ShellPlatform.AppIcon;
            Program.Log($"app-icon set window={window.GetType().Name} embedded={ShellPlatform.HasEmbeddedIcon}");
        }
        catch (Exception ex)
        {
            Program.Log($"app-icon failed window={window.GetType().Name}: {ex.Message}"); // 아이콘 실패가 창 열기를 막지 않는다
        }
    }

#if PD_DIAGNOSTICS
    // WM_GETICON·GetClassLongPtr·확장 스타일 — https://learn.microsoft.com/en-us/windows/win32/winmsg/wm-geticon
    private const uint WmGetIcon = 0x007F;
    private const nint IconSmall = 0, IconBig = 1, IconSmall2 = 2;
    private const int GclpHIcon = -14, GclpHIconSm = -34, GwlExStyle = -20;
    private const long WsExAppWindow = 0x40000, WsExToolWindow = 0x80;

    private static WindowIconState? Probe(Window window)
    {
        if (!OperatingSystem.IsWindows() || window.TryGetPlatformHandle()?.Handle is not { } hwnd || hwnd == 0)
        {
            return null;
        }

        var ex = (long)GetWindowLongPtr(hwnd, GwlExStyle);
        return new WindowIconState(
            Big: SendMessage(hwnd, WmGetIcon, IconBig, 0) != 0,
            Small: SendMessage(hwnd, WmGetIcon, IconSmall, 0) != 0,
            Small2: SendMessage(hwnd, WmGetIcon, IconSmall2, 0) != 0,
            ClassBig: GetClassLongPtr(hwnd, GclpHIcon) != 0,
            ClassSmall: GetClassLongPtr(hwnd, GclpHIconSm) != 0,
            AppWindow: (ex & WsExAppWindow) != 0,
            ToolWindow: (ex & WsExToolWindow) != 0);
    }

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern nint SendMessage(nint hwnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll", EntryPoint = "GetClassLongPtrW")]
    private static extern nint GetClassLongPtr(nint hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint hwnd, int index);
#endif
}
