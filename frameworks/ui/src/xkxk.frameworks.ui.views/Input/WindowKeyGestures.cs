// 창 관리 단축키(U9 ⑦⑧⑨) — SDI 창의 Ctrl+F4·Ctrl+Tab·Ctrl+F6, 창 메뉴 Alt+Space·문서 창 메뉴 Alt+-, macOS Cmd+W·Cmd+M·Cmd+`
using xkxk.frameworks.ui.shell;
using xkxk.frameworks.ui.shell.Input;
using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using xkxk.frameworks.ui.shell.Docking;
using xkxk.frameworks.ui.shell.ViewModels;
using xkxk.frameworks.ui.shell.Framework;

namespace xkxk.frameworks.ui.views.Input;

/// <summary>
/// 메인 창의 Ctrl+Tab·Ctrl+F6·Ctrl+F4 는 MainView KeyBindings 가 정본이고, SDI 창(별도 TopLevel)에는 그 바인딩이 없어 여기서 같은 명령으로 잇는다.
/// 근거: Win32 MDI(learn.microsoft.com about-the-multiple-document-interface — Alt+- 창 메뉴, Ctrl+F4 닫기, Ctrl+F6 다음),
/// Windows 단축키(support.microsoft.com — Alt+Space 창 메뉴, Alt+F4 창 닫기는 OS 처리), macOS 단축키(support.apple.com/102650 — Cmd+W·Cmd+M·Cmd+`).
/// 웹은 브라우저가 Ctrl+Tab·Ctrl+F4·Ctrl+W 를 먼저 가로채므로 해당 키는 동작 대상이 아니다(관리형 창 판정은 제목줄 동작만).
/// </summary>
public static class WindowKeyGestures
{
    private static MainViewModel? s_vm;

    /// <summary>앱 시작 시 1회(EscCloseInput.Install 에서 호출). 제목줄 끌어 복원·로고 더블클릭 처리기도 함께 건다.</summary>
    public static void Install(MainViewModel vm)
    {
        var first = s_vm is null;
        s_vm = vm;
        if (!first)
        {
            return;
        }

        InputElement.KeyDownEvent.AddClassHandler<TopLevel>(OnKeyDown, RoutingStrategies.Tunnel);
        TitleDragRestore.Install();
        WindowSystemMenu.Install();
        WindowMinSize.Install(); // U9 ⑩ 창 최소 크기(크롬 수치 계산)
    }

    private static void OnKeyDown(TopLevel top, KeyEventArgs e)
    {
        var mods = e.KeyModifiers;
        if (e.Handled || s_vm is not { } vm || (mods & (KeyModifiers.Alt | KeyModifiers.Control | KeyModifiers.Meta)) == 0
            || e.Key is not (Key.Space or Key.OemMinus or Key.Subtract or Key.F4 or Key.Tab or Key.F6 or Key.W or Key.M or Key.OemTilde or Key.Left or Key.Right or Key.Up or Key.Down))
        {
            return; // 대상 키가 아니면 시각 트리를 뒤지지 않는다(입력 중 매 키 비용 0)
        }

        if (e.Key is Key.Tab or Key.F6 && (mods & KeyModifiers.Control) != 0)
        {
            MainView.MarkOrigin(ActivationOrigin.Keyboard); // Ctrl+Tab·Ctrl+F6 = keyboard 출처(KeyBindings 가 처리한 키는 여기 오지 않는다 — 그 경로는 ShortcutBindings 가 출처를 넘긴다)
        }

        // SDI 문서 창 = Dock HostWindow 중 앱 제목줄(SdiTitleBar)을 가진 창. 메인 창은 HostWindow 가 아니다.
        var sdi = top is Dock.Avalonia.Controls.HostWindow ? top.GetVisualDescendants().OfType<SdiTitleBar>().FirstOrDefault() : null;
        e.Handled = (e.Key, mods) switch
        {
            // 창 메뉴: 메인·SDI 모두 앱이 연다. Windows 메인 창도 OS 메뉴가 뜨지 않는다. Avalonia 가 키보드 시스템 메뉴(SC_KEYMENU)를 삼킨다(Avalonia.Win32 12.1.4 AppWndProc WM_SYSCOMMAND 디컴파일)
            (Key.Space, KeyModifiers.Alt) => WindowSystemMenu.OpenWindowMenu(top),
            (Key.OemMinus or Key.Subtract, KeyModifiers.Alt) when sdi is null => WindowSystemMenu.OpenDocumentMenu(top, vm.ActiveDocument),
            (Key.F4, KeyModifiers.Control) when sdi is not null => Run(() => Close(SdiDocument(sdi))),
            (Key.Tab or Key.F6, KeyModifiers.Control) when sdi is not null => Run(() => XkxkUi.Current.Shell?.ActivateNext()),
            (Key.Tab or Key.F6, KeyModifiers.Control | KeyModifiers.Shift) when sdi is not null => Run(() => XkxkUi.Current.Shell?.ActivatePrevious()),
            (Key.W, KeyModifiers.Meta) when OperatingSystem.IsMacOS() => Run(() => Close(sdi is not null ? SdiDocument(sdi) : vm.ActiveDocument)),
            (Key.M, KeyModifiers.Meta) when OperatingSystem.IsMacOS() && top is Window w => Run(() => w.WindowState = WindowState.Minimized),
            (Key.OemTilde, KeyModifiers.Meta) when OperatingSystem.IsMacOS() => Run(() => CycleWindows(top, 1)),
            (Key.OemTilde, KeyModifiers.Meta | KeyModifiers.Shift) when OperatingSystem.IsMacOS() => Run(() => CycleWindows(top, -1)),
            // 창 배치(스냅) 키보드 대안: Ctrl+Alt+←/→ 반쪽, ↑ 최대화, ↓ 복원. SnapKeyboard 옵션이 꺼져 있으면 키를 소비하지 않는다
            (Key.Left or Key.Right or Key.Up or Key.Down, KeyModifiers.Control | KeyModifiers.Alt) => SnapKey(top, sdi, vm, e.Key),
            _ => false,
        };
    }

    private static bool SnapKey(TopLevel top, SdiTitleBar? sdi, MainViewModel vm, Key key)
    {
        var direction = key switch
        {
            Key.Left => SnapKeyDirection.Left,
            Key.Right => SnapKeyDirection.Right,
            Key.Up => SnapKeyDirection.Up,
            _ => SnapKeyDirection.Down,
        };

        // SDI 창 = 그 창의 문서, 메인 창 = 활성 문서(없으면 메인 창 자체)
        var target = sdi is not null && SdiDocument(sdi) is { } sdiDoc ? new WindowMenuTarget(WindowMenuKind.Sdi, sdiDoc, null)
            : vm.ActiveDocument is { } active && sdi is null ? new WindowMenuTarget(WindowMenuKind.Mdi, active, null)
            : top is Window main && sdi is null ? new WindowMenuTarget(WindowMenuKind.Main, null, main)
            : null;
        return SnapKeyboardCommands.Execute(target, direction);
    }

    private static bool Run(Action action)
    {
        action();
        return true;
    }

    private static PocDocument? SdiDocument(SdiTitleBar bar) => WindowCommands.FindDocument(bar.DataContext);

    private static void Close(PocDocument? document)
    {
        if (document is not null)
        {
            XkxkUi.Current.Shell?.CloseDocument(document);
        }
    }

    /// <summary>앱의 보이는 최상위 창(메인·SDI) 사이를 열린 순서로 돌아가며 활성화(macOS Cmd+`).</summary>
    private static void CycleWindows(TopLevel current, int step)
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            return;
        }

        var windows = desktop.Windows.Where(w => w.IsVisible && w.WindowState != WindowState.Minimized).ToList();
        var index = windows.FindIndex(w => ReferenceEquals(w, current));
        if (windows.Count > 1)
        {
            windows[((index < 0 ? 0 : index) + step + windows.Count) % windows.Count].Activate();
        }
    }
}
