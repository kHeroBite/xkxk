// 셸 기본 명령 61개 등록 중 B06 23개 — 메뉴 RelayCommand 와 같은 IShellCommands 호출을 명령 ID 로 노출한다(T-CMD, UADR-0013). B07 38개는 .Menu.cs
using System;
using System.Threading.Tasks;
using Dock.Model.Core;
using xkxk.frameworks.ui.data.Navigation;
using xkxk.frameworks.ui.shell.Docking;
using xkxk.frameworks.ui.shell.Theming;

namespace xkxk.frameworks.ui.shell.Framework;

/// <summary>
/// 셸 기본 명령(<see cref="ShellCommandIds"/>)을 표 순서대로 등록한다. 등록 주체는 <see cref="UiRegistryOwners.Shell"/> 이다.
/// </summary>
/// <remarks>
/// 동작은 MainViewModel 의 같은 메뉴 명령과 같은 <see cref="IShellCommands"/> 호출이다. 대상 문서는
/// <c>ctx.Parameter is PocDocument d ? d : shell.ActiveDocument</c> 다. 셸이 없으면(앱 없음) 실행할 수 없고
/// 창 전체 보기만 늘 실행할 수 있다. 메뉴가 쓰는 나머지 38개(B07)는 <c>ShellDefaultCommands.Menu.cs</c> 가 이어서 등록한다.
/// 확대·축소 명령은 만들지 않는다(메뉴 항목 없음, Ctrl+휠은 기존 입력 경로).
/// </remarks>
internal static partial class ShellDefaultCommands
{
    private const string Layout = "layout";
    private const string Window = "window";

    /// <summary>61개를 표 순서(B06 23개 → B07 38개)로 등록한다.</summary>
    internal static void Register(IUiCommandRegistry r)
    {
        ArgumentNullException.ThrowIfNull(r);
        Add(r, ShellCommandIds.LayoutSave, "menu.file.saveLayout", CmdIconKeys.SaveLayout, "Ctrl+S", Layout,
            c => Watch(c, Shell(c)?.SaveLayoutAsync()), HasShell);
        Add(r, ShellCommandIds.LayoutLoad, "menu.file.loadLayout", CmdIconKeys.LoadLayout, null, Layout,
            c => Watch(c, Shell(c)?.LoadLayoutAsync()), HasShell);
        Add(r, ShellCommandIds.WindowCascade, "menu.window.cascade", CmdIconKeys.Cascade, null, Window,
            c => Shell(c)?.Arrange(ShellArrange.Cascade), HasShell);
        Add(r, ShellCommandIds.WindowTileHorizontal, "menu.window.tileH", CmdIconKeys.TileH, null, Window,
            c => Shell(c)?.Arrange(ShellArrange.TileHorizontal), HasShell);
        Add(r, ShellCommandIds.WindowTileVertical, "menu.window.tileV", CmdIconKeys.TileV, null, Window,
            c => Shell(c)?.Arrange(ShellArrange.TileVertical), HasShell);
        Add(r, ShellCommandIds.WindowTabbed, "menu.window.tabbed", CmdIconKeys.Tabbed, null, Window,
            c => Shell(c)?.Arrange(ShellArrange.Tabbed), HasShell);
        Add(r, ShellCommandIds.WindowMinimize, "menu.window.minimize", CmdIconKeys.Minimize, null, Window,
            c => OnTarget(c, (s, d) => s.SetState(d, MdiWindowState.Minimized)), HasTarget);
        Add(r, ShellCommandIds.WindowPin, "menu.window.pin", CmdIconKeys.Pin, null, Window,
            c => OnTarget(c, (s, d) => s.TogglePin(d)), HasTarget);
        Add(r, ShellCommandIds.WindowResetLayout, "menu.window.resetLayout", CmdIconKeys.ResetLayout, null, Window,
            c => Shell(c)?.ResetLayout(), HasShell);
        Add(r, ShellCommandIds.WindowMinimizeAll, "menu.window.minimizeAll", CmdIconKeys.MinimizeAll, null, Window,
            c => Shell(c)?.MinimizeAll(), HasShell);
        Add(r, ShellCommandIds.WindowRestoreAll, "menu.window.restoreAll", CmdIconKeys.RestoreAll, null, Window,
            c => Shell(c)?.RestoreAll(), HasShell);
        Add(r, ShellCommandIds.WindowRestoreLastMinimized, "menu.window.restoreLastMin", CmdIconKeys.RestoreLastMin, null, Window,
            c => Shell(c)?.RestoreLastMinimized(), HasShell);
        Add(r, ShellCommandIds.WindowNext, "menu.window.next", CmdIconKeys.Next, "Ctrl+Tab", Window,
            c => Shell(c)?.ActivateNext(), HasShell);
        Add(r, ShellCommandIds.WindowPrevious, "menu.window.prev", CmdIconKeys.Prev, "Ctrl+Shift+Tab", Window,
            c => Shell(c)?.ActivatePrevious(), HasShell);
        Add(r, ShellCommandIds.WindowFloat, "menu.window.float", CmdIconKeys.Float, null, Window,
            c => OnTarget(c, (s, d) => s.Float(d)), HasTarget);
        Add(r, ShellCommandIds.WindowRedock, "menu.window.redock", CmdIconKeys.Redock, null, Window,
            c => OnTarget(c, (s, d) => s.Redock(d)), HasTarget);
        Add(r, ShellCommandIds.WindowOverview, "menu.window.overview", CmdIconKeys.Overview, null, Window,
            _ => WindowOverviewHost.Request(), _ => true);
        Add(r, ShellCommandIds.WindowClose, "menu.window.close", CmdIconKeys.Close, "Ctrl+F4", Window,
            c => OnTarget(c, (s, d) => s.CloseDocument(d)), HasTarget);
        Add(r, ShellCommandIds.WindowCloseOthers, "menu.window.closeOthers", CmdIconKeys.CloseOthers, null, Window,
            c => OnTarget(c, (s, d) => s.CloseOthers(d)), HasTarget);
        Add(r, ShellCommandIds.WindowCloseAll, "menu.window.closeAll", CmdIconKeys.CloseAll, null, Window,
            c => Shell(c)?.CloseAll(), HasShell);
        Add(r, ShellCommandIds.LayoutPresetMdiCascade, "menu.view.presetCascade", CmdIconKeys.Cascade, null, Layout,
            c => Shell(c)?.ApplyPreset(LayoutPreset.MdiCascade), HasShell);
        Add(r, ShellCommandIds.LayoutPresetTabbed, "menu.view.presetTabbed", CmdIconKeys.Tabbed, null, Layout,
            c => Shell(c)?.ApplyPreset(LayoutPreset.Tabbed), HasShell);
        Add(r, ShellCommandIds.LayoutPresetMdiTileVertical, "menu.view.presetTile", CmdIconKeys.TileV, null, Layout,
            c => Shell(c)?.ApplyPreset(LayoutPreset.MdiTileVertical), HasShell);
        RegisterMenuCommands(r);
    }

    private static void Add(IUiCommandRegistry r, string id, string titleKey, string icon, string? gesture, string category,
        Action<UiCommandContext> execute, Func<UiCommandContext, bool> canExecute)
    {
        r.Add(id, o =>
        {
            o.TitleKey = titleKey;
            o.Icon = icon;
            o.Gesture = gesture;
            o.Category = category;
            o.Execute = execute;
            o.CanExecute = canExecute;
        });
    }

    private static IShellCommands? Shell(UiCommandContext c) => c.App.Shell;

    private static bool HasShell(UiCommandContext c) => c.App.Shell is not null;

    private static PocDocument? Target(IShellCommands shell, UiCommandContext c) =>
        c.Parameter is PocDocument d ? d : shell.ActiveDocument;

    private static bool HasTarget(UiCommandContext c) => c.App.Shell is { } s && Target(s, c) is not null;

    private static void OnTarget(UiCommandContext c, Action<IShellCommands, PocDocument> action)
    {
        if (c.App.Shell is { } s && Target(s, c) is { } d)
        {
            action(s, d);
        }
    }

    /// <summary>비동기 셸 동작의 실패를 이어 붙인 continuation 에서 XKUI0306 으로 남긴다.</summary>
    private static void Watch(UiCommandContext c, Task? task)
    {
        if (task is null)
        {
            return;
        }

        var app = c.App;
        var id = c.CommandId;
        _ = task.ContinueWith(
            t => app.Diagnostics.Report(new UiDiagnostic(UiDiagnosticIds.CommandFailed, UiDiagnosticSeverity.Error,
                $"명령 '{id}' 의 비동기 실행이 실패했다: {t.Exception?.GetBaseException().GetType().FullName}: {t.Exception?.GetBaseException().Message}", // i18n-ok 개발자 진단
                UiRegistryOwners.Shell)),
            TaskContinuationOptions.OnlyOnFaulted);
    }
}
