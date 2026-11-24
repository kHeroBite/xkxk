// SDI 창 비활성 최소화·복원 — 활성 아닌 창의 최소화와 화면 밖(--bg-pos) 최소화·복원이 다른 창을 전경으로 올리지 않게 user32 ShowWindow 를 직접 부른다(w13 Z6)
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Dock.Model.Core;

namespace Pd.Poc.Desktop;

/// <summary>
/// 근거: Avalonia.Win32 12.1.4 WindowImpl 은 Minimized 를 늘 SW_MINIMIZE 로 보내고(디컴파일 ShowWindow), MS 문서상 SW_MINIMIZE 는 Z 순서의 다음 최상위 창을 활성화한다.
/// 비활성 명령 뒤 WM_SIZE 가 Avalonia 의 창 상태를 맞추므로(디컴파일 WndProc WM_SIZE·Window.HandleWindowStateChanged) WindowState 를 다시 넣지 않는다.
/// 범위(team-lead 승인 B+C): 사용자가 활성 SDI 를 최소화하면(제목줄 _ 버튼) 지금처럼 SW_MINIMIZE 다. 복원 비활성은 화면 밖 모드만이다(일반 기동 복원 활성화는 범위 밖).
/// </summary>
internal static class SdiQuietState
{
    private const int SwShowNoActivate = 4;
    private const int SwShowMinNoActive = 7;

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    /// <summary>비활성 명령으로 처리했으면 true, 아니면 false(호출자가 Dock 기본 경로를 탄다). 보이지 않는 창은 처리하지 않는다.</summary>
    public static bool TryApply(Window window, DockWindowState state, bool offscreen)
    {
        if (!OperatingSystem.IsWindows() || !window.IsVisible || window.TryGetPlatformHandle()?.Handle is not { } hwnd || hwnd == IntPtr.Zero)
        {
            return false;
        }

        var cmd = state switch
        {
            DockWindowState.Minimized when offscreen || !window.IsActive => SwShowMinNoActive,
            DockWindowState.Normal when offscreen && window.WindowState != WindowState.Normal => SwShowNoActivate,
            _ => 0,
        };
        if (cmd == 0)
        {
            return false;
        }

        ShowWindow(hwnd, cmd);
        Console.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [host] sdi quiet state '{window.Title}' cmd={cmd} after={window.WindowState} offscreen={offscreen}");
        return true;
    }
}
