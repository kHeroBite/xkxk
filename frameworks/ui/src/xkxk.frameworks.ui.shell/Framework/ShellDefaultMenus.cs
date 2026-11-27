// 셸 기본 메뉴 등록 — ShellMenuTable 행을 순서대로 메뉴 레지스트리에 넣고 조건 키를 메인 뷰모델 대리자로 바꾼다(UADR-0014)
using System;
using xkxk.frameworks.ui.data.Navigation;
using xkxk.frameworks.ui.shell.Platform;

namespace xkxk.frameworks.ui.shell.Framework;

/// <summary>
/// 셸 4메뉴(파일·창·보기·시스템)를 등록한다. 행 데이터의 유일한 출처는 <see cref="ShellMenuTable.Rows"/> 다.
/// </summary>
/// <remarks>
/// 조건 대리자는 앱이 없을 때(메인 뷰모델 null — 콘솔 계약 시험 등) 예외 없이 false·툴팁 null 을 돌려준다(R5).
/// 모르는 조건 키는 부를 때 예외를 던져 XKUI0411 로 드러나게 한다(조용히 보이거나 숨지 않는다).
/// </remarks>
internal static class ShellDefaultMenus
{
    /// <summary>행 순서대로 등록한다. menus 는 셸 주체(<see cref="UiRegistryOwners.Shell"/>) 보기다.</summary>
    internal static void Register(IUiMenuRegistry menus, IUiAppContext app)
    {
        ArgumentNullException.ThrowIfNull(menus);
        ArgumentNullException.ThrowIfNull(app);
        foreach (var row in ShellMenuTable.Rows)
        {
            menus.Add(row.Id, o => Fill(o, row));
        }
    }

    private static void Fill(UiMenuItemOptions o, ShellMenuRow row)
    {
        o.Parent = row.Parent;
        o.Kind = Kind(row.Kind);
        o.CommandId = row.CommandId;
        o.CommandParameter = row.Param;
        o.TitleKey = string.IsNullOrEmpty(row.TitleKey) ? null : row.TitleKey; // 구분선·섹션의 "" 는 null.
        o.Icon = row.Icon;
        o.AutomationId = row.AutomationId;
        o.Section = row.Section;
        o.IsVisible = row.VisibleWhen is { } visible ? Visible(visible, row) : null;
        o.ToolTip = row.ToolTipWhen is { } tip ? ToolTip(tip) : null;

        var m = row.Mobile;
        o.Mobile.InDrawer = m.InDrawer;
        o.Mobile.Available = m.Available;
        o.Mobile.UnavailableReasonKey = m.ReasonKey;
        o.Mobile.EntryId = m.EntryId;
        o.Mobile.TitleKey = m.TitleKey;
        o.Mobile.CommandId = m.CommandId;
        o.Mobile.CommandParameter = m.Param;
        o.Mobile.TabOrder = m.TabOrder;
        o.Mobile.TabTitleKey = m.TabKey;
        o.Mobile.TabIcon = m.TabIcon;
        // 모바일 데스크톱 ID 목록은 NavMenuCatalog 파생과 같은 도우미로 만든다(C60 — NavEntry.DesktopIds 와 같은 값). 서랍 행이 아니면 빈 목록.
        if (!NavMenuCatalog.IsDrawerRow(row))
        {
            return;
        }

        var mobileId = NavMenuCatalog.MobileEntryId(row.Id, row.AutomationId, m.EntryId);
        foreach (var id in NavMenuCatalog.MobileDesktopIds(mobileId, row.AutomationId, m.Covers))
        {
            o.Mobile.CoveredAutomationIds.Add(id);
        }
    }

    private static UiMenuItemKind Kind(ShellMenuRowKind kind) => kind switch
    {
        ShellMenuRowKind.Submenu => UiMenuItemKind.Submenu,
        ShellMenuRowKind.Separator => UiMenuItemKind.Separator,
        ShellMenuRowKind.Section => UiMenuItemKind.Section,
        _ => UiMenuItemKind.Command,
    };

    /// <summary>표시 조건 키 → 대리자. 앱 없음(메인 뷰모델 null)이면 false.</summary>
    private static Func<UiMenuContext, bool> Visible(string key, ShellMenuRow row) => key switch
    {
        "cmd" => c => CommandRegistered(c.App, row), // 행(Submenu 는 자식 Command 행)의 CommandId 가 명령 레지스트리에 있을 때만 보인다.
        "bench" => c => c.App.MainViewModel is { } vm && vm.Extras.BenchAvailable,
        "gpuRestartPending" => c => c.App.MainViewModel is { } vm && vm.SystemMenu.SysGpuRestartPending,
        "logTool" => c => c.App.MainViewModel is { } vm && vm.WindowMenu.IsLogToolAvailable,
        "desktopWindow" => c => c.App.MainViewModel is not null && ShellPlatformCommands.HasDesktopMainWindow(),
        "monitors" => c => c.App.MainViewModel is not null && c.App.Monitors.HasMonitors,
        "noMonitors" => c => c.App.MainViewModel is not null && !c.App.Monitors.HasMonitors,
        _ => _ => throw new InvalidOperationException($"알 수 없는 메뉴 표시 조건 키 '{key}'."), // i18n-ok 개발자 진단
    };

    /// <summary>cmd 조건 판정 — Command 행은 자기 CommandId, Submenu 행은 자식 Command 행 중 하나라도 등록돼 있으면 true.</summary>
    private static bool CommandRegistered(IUiAppContext app, ShellMenuRow row)
    {
        if (row.CommandId is { } own)
        {
            return app.Commands.Contains(own);
        }

        foreach (var child in ShellMenuTable.Rows)
        {
            if (child.Kind == ShellMenuRowKind.Command && child.Parent == row.Id && child.CommandId is { } id && app.Commands.Contains(id))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>툴팁 키 → 대리자. 앱 없음(메인 뷰모델 null)이면 null.</summary>
    private static Func<UiMenuContext, string?> ToolTip(string key) => key switch
    {
        "snap" => c => c.App.MainViewModel is null ? null : c.App.Localization.Get("menu.window.snap.tip"),
        "singleMonitor" => c => c.App.MainViewModel?.WindowMenu.SingleMonitorTip,
        "monitorMaximize" => c => c.App.MainViewModel?.WindowMenu.MonitorMaximizeTip,
        "resetMain" => c => c.App.MainViewModel?.WindowMenu.ResetMainTip,
        "logFolder" => c => c.App.MainViewModel?.SystemMenu.SysLogFolderTip,
        _ => _ => throw new InvalidOperationException($"알 수 없는 메뉴 툴팁 키 '{key}'."), // i18n-ok 개발자 진단
    };
}
