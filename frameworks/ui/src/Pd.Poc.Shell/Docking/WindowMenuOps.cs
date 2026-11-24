// 창 메뉴 규칙·실행(메인·SDI·MDI·제목 탭 공용) — 항목 활성은 순수 함수로 계산하고, 실행은 캡션 버튼과 같은 경로를 부르며, 기본 위치 이동은 요건 5 계산을 그대로 쓴다
using Pd.Client.Data.Logging;
using Pd.Poc.Shell.Platform;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;

namespace Pd.Poc.Shell.Docking;

/// <summary>창 메뉴가 붙는 네 위치.</summary>
public enum WindowMenuKind
{
    None, // 첨부 속성 기본값. 값이 바뀔 때만 항목을 만든다
    Main,
    Sdi,
    Mdi,
    Tab,
}

/// <summary>창 메뉴 공통 항목.</summary>
public enum WindowMenuAction
{
    Restore,
    Minimize,
    Maximize,
    MoveToDefault,
    Close,
}

/// <summary>Windows 시스템 메뉴 규칙에 쓰는 창 상태.</summary>
public enum WindowMenuShowState
{
    Normal,
    Minimized,
    Maximized,
}

/// <summary>항목별 활성 여부.</summary>
public readonly record struct WindowMenuFacts(bool Restore, bool Minimize, bool Maximize, bool Move, bool Close)
{
    public bool IsEnabled(WindowMenuAction action) => action switch
    {
        WindowMenuAction.Restore => Restore,
        WindowMenuAction.Minimize => Minimize,
        WindowMenuAction.Maximize => Maximize,
        WindowMenuAction.MoveToDefault => Move,
        _ => Close,
    };
}

/// <summary>메뉴를 적용할 창. Main 은 Main 창, 나머지는 Document 를 쓴다. 패널 탭만 남은 분리 창은 Document 없이 PanelWindow 를 쓴다.</summary>
public sealed record WindowMenuTarget(WindowMenuKind Kind, PocDocument? Document, Window? Main, IDockWindow? PanelWindow = null);

/// <summary>항목 활성 규칙(순수 함수). 지금 상태로 가는 항목만 비활성이다. 기본 위치 이동은 복원을 함께 하므로 최소화·최대화 상태에서도 쓸 수 있다.</summary>
public static class WindowMenuRules
{
    public static WindowMenuFacts Compute(WindowMenuShowState state, bool canChangeState, bool canMove) => new(
        Restore: canChangeState && state != WindowMenuShowState.Normal,
        Minimize: canChangeState && state != WindowMenuShowState.Minimized,
        Maximize: canChangeState && state != WindowMenuShowState.Maximized,
        Move: canMove,
        Close: true);
}

/// <summary>
/// 화면 층은 항목을 그리고 눌림을 여기로 넘기기만 한다. 최소화·최대화·닫기는 캡션 버튼과 같은 경로(메인 = Window.WindowState·Close, 문서 = IShellCommands)를 부른다.
/// 위치 고정 창도 기본 위치로 옮길 수 있다. 사용자가 그 창을 지정해 고른 명령이고, 고정 상태는 유지한다(요건 5 와 같은 취급).
/// </summary>
public static partial class WindowMenuOps
{
    /// <summary>우클릭한 제목 탭의 문서. 활성화하지 않고 대상만 기억한다(최소화 탭을 복원해 버리면 메뉴 상태가 틀어진다). 없으면 활성 문서가 대상이다.</summary>
    public static PocDocument? TabTarget { get; set; }

    /// <summary>대상의 항목 활성 여부를 계산한다.</summary>
    public static WindowMenuFacts Facts(WindowMenuTarget target)
    {
        if (target.Kind == WindowMenuKind.Main)
        {
            return target.Main is { } main
                ? WindowMenuRules.Compute(ShowState(main.WindowState), true, App.WindowHost?.CanExecute(WindowHostCommand.ResetMainWindow) == true)
                : default;
        }

        if (target.Document is null && target.PanelWindow is { } panelWindow)
        {
            return WindowMenuRules.Compute(StateOfWindow(panelWindow), true, true);
        }

        if (target.Document is not { } doc || App.Shell is not WindowCommands shell)
        {
            return default;
        }

        var window = shell.SdiPlacement.WindowOf(doc);
        var group = MdiTabGroup.GroupOf(doc); // 탭 묶음 안 문서: 창(묶음)이 MDI 칸에 있고, 잠금은 묶음 안 하나라도
        var placed = window is not null || (PocDockFactory.PanelOf(doc) is null && ((IDockable?)group ?? doc).Owner is IDocumentDock { LayoutMode: DocumentLayoutMode.Mdi });
        var blocked = (group?.Documents.Any(d => WindowCommands.ChromeOf(d).IsLocked) ?? WindowCommands.ChromeOf(doc).IsLocked) && WindowCommands.LockBlocksMinMax;
        return WindowMenuRules.Compute(StateOf(doc, window), placed && !blocked, placed);
    }

    /// <summary>항목 실행. 비활성 항목은 실행하지 않고 false 를 돌려준다.</summary>
    public static bool Run(WindowMenuTarget target, WindowMenuAction action)
    {
        if (!Facts(target).IsEnabled(action))
        {
            return false;
        }

        var done = target.Kind == WindowMenuKind.Main ? RunMain(target.Main!, action)
            : target.Document is null ? RunPanelWindow(target.PanelWindow!, action)
            : RunDocument((WindowCommands)App.Shell!, target.Document, action);
        PdLog.Info("window-menu", $"{target.Kind} {action} doc={target.Document?.Id} done={done}");
        return done;
    }

    private static bool RunMain(Window main, WindowMenuAction action)
    {
        switch (action)
        {
            case WindowMenuAction.Restore:
                main.WindowState = WindowState.Normal;
                return true;
            case WindowMenuAction.Minimize:
                main.WindowState = WindowState.Minimized;
                return true;
            case WindowMenuAction.Maximize:
                main.WindowState = WindowState.Maximized;
                return true;
            case WindowMenuAction.MoveToDefault:
                var moved = App.WindowHost?.MoveMainToDefault() == true;
                if (moved && !SystemDialogHost.BackgroundMode)
                {
                    main.Activate(); // 비전경 시험은 전경을 가져가지 않는다
                }

                return moved;
            default:
                main.Close(); // App 종료 처리(자동 저장·설정 플러시)를 그대로 탄다
                return true;
        }
    }

    /// <summary>패널 탭만 남은 분리 창. 제목줄 버튼과 창 메뉴가 함께 쓴다. 닫으면 패널은 숨은 상태가 되고 보기 메뉴로 되찾는다.</summary>
    private static bool RunPanelWindow(IDockWindow window, WindowMenuAction action)
    {
        switch (action)
        {
            case WindowMenuAction.Restore:
                SetPanelWindowState(window, DockWindowState.Normal);
                return true;
            case WindowMenuAction.Minimize:
                SetPanelWindowState(window, DockWindowState.Minimized);
                return true;
            case WindowMenuAction.Maximize:
                SetPanelWindowState(window, DockWindowState.Maximized);
                return true;
            case WindowMenuAction.MoveToDefault:
                if (StateOfWindow(window) != WindowMenuShowState.Normal)
                {
                    SetPanelWindowState(window, DockWindowState.Normal);
                }

                return App.WindowHost?.MoveSdiToDefault(window) == true;
            default:
                window.Host?.Exit();
                return true;
        }
    }

    private static void SetPanelWindowState(IDockWindow window, DockWindowState state)
    {
        window.WindowState = state;
        window.Host?.SetWindowState(state);
    }

    private static bool RunDocument(WindowCommands shell, PocDocument doc, WindowMenuAction action)
    {
        switch (action)
        {
            case WindowMenuAction.Restore:
                shell.SetState(doc, MdiWindowState.Normal);
                shell.Activate(doc);
                return true;
            case WindowMenuAction.Minimize:
                shell.SetState(doc, MdiWindowState.Minimized);
                return true;
            case WindowMenuAction.Maximize:
                shell.SetState(doc, MdiWindowState.Maximized);
                shell.Activate(doc);
                return true;
            case WindowMenuAction.MoveToDefault:
                return MoveDocument(shell, doc);
            default:
                shell.CloseDocument(doc);
                return true;
        }
    }

    /// <summary>대상 창만 복원해 기본 위치로 옮기고 활성화해 맨 앞으로 가져온다. 크기는 유지한다.</summary>
    private static bool MoveDocument(WindowCommands shell, PocDocument doc)
    {
        var window = shell.SdiPlacement.WindowOf(doc);
        if (StateOf(doc, window) != WindowMenuShowState.Normal)
        {
            shell.SetState(doc, MdiWindowState.Normal);
        }

        var moved = window is not null ? App.WindowHost?.MoveSdiToDefault(window) == true : MoveMdi(doc, shell.Documents);
        shell.Activate(doc);
        if (moved)
        {
            App.NotifyLayoutChanged(); // 끌기 없는 위치 변경 — 자동 저장 대상
        }

        return moved;
    }

    /// <summary>MDI 영역 안 첫 빈 계단 칸으로 옮긴다. 판정은 복구와 같은 PlacementRecovery.MdiDefaultSlot 이고, 자기 옛 자리는 빈 자리로 본다.</summary>
    private static bool MoveMdi(PocDocument doc, IReadOnlyList<PocDocument> documents)
    {
        var (panel, child) = FindMdi(doc);
        var win = MdiTabGroup.WindowOf(doc)!; // 탭 묶음 안이면 묶음이 화면의 MDI 창이다
        var own = win.MdiBounds;
        var size = own is { Width: > 0, Height: > 0 } ? own : new DockRect(0, 0, child?.Bounds.Width ?? PocDockFactory.DefaultMdiWidth, child?.Bounds.Height ?? PocDockFactory.DefaultMdiHeight);
        var taken = documents.Select(d => MdiTabGroup.WindowOf(d)!).Distinct()
            .Where(w => !ReferenceEquals(w, win) && ReferenceEquals(w.Owner, win.Owner)).Select(w => w.MdiBounds).ToList();
        win.MdiBounds = panel is null
            ? PlacementRecovery.MdiDefaultSlot(new Size(double.MaxValue, double.MaxValue), MdiGrabZone.Whole, size, taken, PocDockFactory.NextMdiBounds)
            : PlacementRecovery.MdiDefaultSlot(panel.Bounds.Size, child is null ? MdiGrabZone.Whole : MdiGrabZoneReader.Read(child), size, taken, PocDockFactory.NextMdiBounds);
        return true;
    }

    private static (MdiLayoutPanel? Panel, Control? Child) FindMdi(PocDocument doc)
    {
        Visual? root = Application.Current?.ApplicationLifetime switch
        {
            IClassicDesktopStyleApplicationLifetime desktop => desktop.MainWindow,
            ISingleViewApplicationLifetime single => single.MainView,
            _ => null,
        };
        foreach (var panel in root?.GetVisualDescendants().OfType<MdiLayoutPanel>() ?? Enumerable.Empty<MdiLayoutPanel>())
        {
            var win = MdiTabGroup.WindowOf(doc); // 묶음 창은 활성 탭이 doc 가 아니어도 그 창이다
            var child = panel.Children.OfType<Control>().FirstOrDefault(c => ReferenceEquals(c.DataContext, win)
                || ReferenceEquals(WindowCommands.FindDocument(c.DataContext), doc));
            if (child is not null)
            {
                return (panel, child);
            }
        }

        return (null, null);
    }

    private static WindowMenuShowState StateOf(PocDocument doc, IDockWindow? window)
    {
        if (window is null)
        {
            return MdiTabGroup.WindowOf(doc)!.MdiState switch
            {
                MdiWindowState.Minimized => WindowMenuShowState.Minimized,
                MdiWindowState.Maximized => WindowMenuShowState.Maximized,
                _ => WindowMenuShowState.Normal,
            };
        }

        return StateOfWindow(window);
    }

    private static WindowMenuShowState StateOfWindow(IDockWindow window)
    {
        if (window.Host is Window host)
        {
            return ShowState(host.WindowState); // OS 창은 스냅 등으로 모델 값과 다를 수 있어 실제 값을 읽는다
        }

        return window.WindowState switch
        {
            DockWindowState.Minimized => WindowMenuShowState.Minimized,
            DockWindowState.Maximized => WindowMenuShowState.Maximized,
            _ => WindowMenuShowState.Normal,
        };
    }

    private static WindowMenuShowState ShowState(WindowState state) => state switch
    {
        WindowState.Minimized => WindowMenuShowState.Minimized,
        WindowState.Maximized or WindowState.FullScreen => WindowMenuShowState.Maximized,
        _ => WindowMenuShowState.Normal,
    };
}
