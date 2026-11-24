// 메인 창 제목 줄 우클릭 연결(Windows) — 끌기 빈칸·캡션 버튼은 비클라이언트 영역이라 앱 메뉴 대신 OS 시스템 메뉴가 뜨는 경로를 Pd 창 메뉴로 돌린다
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using xkxk.frameworks.ui.views;

namespace xkxk.frameworks.ui.desktop;

/// <summary>
/// 근거(Avalonia.Win32 12.1.4 디컴파일): ElementRole=TitleBar 는 WM_NCHITTEST 에서 HTCAPTION, 캡션 버튼은 HTMINBUTTON·HTMAXBUTTON·HTCLOSE 를 돌려준다.
/// WM_NCRBUTTONUP 은 처리하지 않아 DefWindowProc 로 가고, 이어지는 WM_CONTEXTMENU 에서 OS 가 시스템 메뉴를 띄운다(Learn: WM_CONTEXTMENU Remarks). 앱의 ContextMenu 는 클라이언트 영역(HTCLIENT)에서만 열린다.
/// Win32Properties 훅은 Avalonia WndProc 보다 먼저 불리므로(WndProcMessageHandler) 처리됨으로 끝내면 OS 메뉴로 가지 않는다. 작업 표시줄의 시스템 메뉴(Shift+우클릭)는 이 경로가 아니라 그대로다.
/// 누름도 함께 처리됨으로 끝낸다. 비클라이언트 누름만 앱에 전달되면 뗌이 없어 눌림 상태가 남는다.
/// </summary>
internal static class MainNcMenuHook
{
    private const uint WmNcRButtonDown = 0x00A4;
    private const uint WmNcRButtonUp = 0x00A5;
    private const int HtCaption = 2;
    private const int HtSysMenu = 3;
    private const int HtMinButton = 8;
    private const int HtMaxButton = 9;
    private const int HtClose = 20;

    public static void Install(Window main)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        Win32Properties.AddWndProcHookCallback(main, (IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam, ref bool handled) =>
        {
            if (msg is WmNcRButtonDown or WmNcRButtonUp && IsCaptionHit((int)wParam))
            {
                handled = true;
                if (msg == WmNcRButtonUp)
                {
                    var at = new PixelPoint((short)((long)lParam & 0xFFFF), (short)(((long)lParam >> 16) & 0xFFFF));
                    Program.Log($"window-menu nc-rclick hit={(int)wParam} at=({at.X},{at.Y})");
                    Dispatcher.UIThread.Post(() => WindowSystemMenu.OpenWindowMenu(main, at));
                }
            }

            return IntPtr.Zero;
        });
    }

    private static bool IsCaptionHit(int hit) => hit is HtCaption or HtSysMenu or HtMinButton or HtMaxButton or HtClose;
}
