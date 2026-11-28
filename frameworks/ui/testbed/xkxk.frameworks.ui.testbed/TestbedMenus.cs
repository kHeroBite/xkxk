// 셸 메뉴 표에서 옮겨 온 데모·벤치 메뉴 8개를 같은 ID·자리·모바일 노출·가시 조건으로 등록한다(B11-4, UTODO-0049)
using System;
using xkxk.frameworks.ui.data.Navigation;
using xkxk.frameworks.ui.shell.Framework;

namespace xkxk.frameworks.ui.testbed;

/// <summary>
/// 파일 메뉴의 데모 열기(그리드·간트·폼)·SDI 열기 묶음·벤치 열기 항목을 공개 메뉴 레지스트리로 등록한다.
/// </summary>
/// <remarks>
/// <para>ID 는 <see cref="ShellMenuIds"/> 상수를 그대로 쓴다(UADR-0009). 시험대 주체는 프레임워크 주체라 예약 접두어 검사에 걸리지 않는다.</para>
/// <para>자리: 첫 항목은 파일 메뉴 첫 구분선 앞에 끼우고, 뒤 항목은 바로 앞 형제 뒤에 이어 붙인다(옛 표의 행 순서와 같다).</para>
/// <para>가시 조건: 명령이 명령 레지스트리에 있을 때만 보인다(옛 표의 cmd 조건). SDI 묶음은 자식 명령이 하나라 그 명령으로 판정한다.</para>
/// <para>벤치 열기는 명령과 같은 진단+벤치 빌드에서만 등록한다. 다른 빌드에서는 늘 숨겨지던 항목이 레지스트리에 없다.</para>
/// </remarks>
public static class TestbedMenus
{
    /// <summary>메뉴를 등록한다. 명령 등록(<see cref="TestbedCommands.Register"/>) 뒤에 부른다.</summary>
    /// <param name="context">모듈 구성 문맥. null 이면 <see cref="ArgumentNullException"/>.</param>
    public static void Register(IUiModuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var m = context.Menus;

        AddCommand(m, ShellMenuIds.FileOpenGrid, ShellMenuIds.File, ShellCommandIds.DemoOpen, "Grid", "menu.file.openGrid",
            MenuIconCatalog.GridIconKey, "shell.mnuOpenGrid", before: ShellMenuIds.FileSep1, after: null, pcOnly: false);
        AddCommand(m, ShellMenuIds.FileOpenGantt, ShellMenuIds.File, ShellCommandIds.DemoOpen, "Gantt", "menu.file.openGantt",
            MenuIconCatalog.GanttIconKey, "shell.mnuOpenGantt", before: null, after: ShellMenuIds.FileOpenGrid, pcOnly: false);
        AddCommand(m, ShellMenuIds.FileOpenForm, ShellMenuIds.File, ShellCommandIds.DemoOpen, "Form", "menu.file.openForm",
            MenuIconCatalog.FormIconKey, "shell.mnuOpenForm", before: null, after: ShellMenuIds.FileOpenGantt, pcOnly: false);

        m.Add(ShellMenuIds.FileOpenSdiGroup, o =>
        {
            o.Parent = ShellMenuIds.File;
            o.Kind = UiMenuItemKind.Submenu;
            o.TitleKey = "menu.file.openSdi";
            o.Icon = CmdIconKeys.OpenSdi;
            o.AutomationId = "shell.mnuOpenSdiGroup";
            o.InsertAfter = ShellMenuIds.FileOpenForm;
            o.IsVisible = c => c.App.Commands.Contains(ShellCommandIds.DemoOpenSdi); // 자식 3개가 모두 같은 명령
            Mobile(o, "shell.mnuOpenSdiGroup", pcOnly: true);
        });
        AddCommand(m, ShellMenuIds.FileOpenGridSdi, ShellMenuIds.FileOpenSdiGroup, ShellCommandIds.DemoOpenSdi, "Grid", "menu.file.openGridSdi",
            MenuIconCatalog.GridIconKey, "shell.mnuOpenGridSdi", before: null, after: null, pcOnly: true);
        AddCommand(m, ShellMenuIds.FileOpenGanttSdi, ShellMenuIds.FileOpenSdiGroup, ShellCommandIds.DemoOpenSdi, "Gantt", "menu.file.openGanttSdi",
            MenuIconCatalog.GanttIconKey, "shell.mnuOpenGanttSdi", before: null, after: null, pcOnly: true);
        AddCommand(m, ShellMenuIds.FileOpenFormSdi, ShellMenuIds.FileOpenSdiGroup, ShellCommandIds.DemoOpenSdi, "Form", "menu.file.openFormSdi",
            MenuIconCatalog.FormIconKey, "shell.mnuOpenFormSdi", before: null, after: null, pcOnly: true);

#if PD_DIAGNOSTICS && PD_BENCH
        AddCommand(m, ShellMenuIds.FileOpenBench, ShellMenuIds.File, ShellCommandIds.BenchOpen, "grid", "menu.file.openBench",
            CmdIconKeys.OpenBench, "shell.mnuOpenBench", before: null, after: ShellMenuIds.FileOpenSdiGroup, pcOnly: false);
#endif
    }

    private static void AddCommand(IUiMenuRegistry m, string id, string parent, string commandId, string param, string titleKey,
        string icon, string automationId, string? before, string? after, bool pcOnly) =>
        m.Add(id, o =>
        {
            o.Parent = parent;
            o.Kind = UiMenuItemKind.Command;
            o.CommandId = commandId;
            o.CommandParameter = param;
            o.TitleKey = titleKey;
            o.Icon = icon;
            o.AutomationId = automationId;
            o.InsertBefore = before;
            o.InsertAfter = after;
            o.IsVisible = c => c.App.Commands.Contains(commandId);
            Mobile(o, automationId, pcOnly);
        });

    /// <summary>모바일 서랍 노출 — 서랍 항목(실행 가능) 또는 PC·태블릿 전용(비활성+사유). 덮는 데스크톱 ID 는 자기 AutomationId 하나다.</summary>
    private static void Mobile(UiMenuItemOptions o, string automationId, bool pcOnly)
    {
        o.Mobile.InDrawer = true;
        o.Mobile.Available = !pcOnly;
        o.Mobile.UnavailableReasonKey = pcOnly ? NavMenuCatalog.ReasonPcTablet : null;
        o.Mobile.CoveredAutomationIds.Add(automationId);
    }
}
