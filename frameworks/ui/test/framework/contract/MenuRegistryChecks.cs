// 메뉴 레지스트리 계약 시험 C46~C53·C60·C61 — 컨텍스트 노출·셸 기본 메뉴·Add 자리·규칙 진단·예약 ID·모바일 힌트 공개 대조·셸 명령 61개(이관 3 제외 58 등록)를 본다(UADR-0014)
using System;
using System.Collections.Generic;
using System.Linq;
using xkxk.frameworks.ui.data.Navigation;
using xkxk.frameworks.ui.shell.Framework;

namespace xkxk.frameworks.ui.test.framework.contract;

/// <summary>메뉴 시험 공용 도우미 — 시험용 소비자 명령, 자식 순서, 모바일 항목 ID 규칙.</summary>
internal static class MenuProbe
{
    /// <summary>메뉴 Command 항목이 가리키는 시험용 소비자 명령 ID(Title·Icon 기본값을 가진다).</summary>
    public const string CommandId = "c4x.menu.cmd";

    public const string CommandTitle = "C48 명령";

    /// <summary>시험용 소비자 명령을 한 번만 등록하고 ID 를 돌려준다.</summary>
    public static string Command()
    {
        var cmds = XkxkUi.Current.Commands;
        if (!cmds.Contains(CommandId))
        {
            Check.True(cmds.Add(CommandId, o => { o.Title = CommandTitle; o.Icon = CmdIconKeys.Exit; o.Execute = _ => { }; }), "시험용 소비자 명령 등록 실패");
        }

        return CommandId;
    }

    /// <summary>부모 아래 자식 ID 순서.</summary>
    public static List<string> Children(string? parent) => XkxkUi.Current.Menus.GetChildren(parent).Select(d => d.Id).ToList();

    /// <summary>해당 ID 진단의 마지막 심각도(없으면 null).</summary>
    public static UiDiagnosticSeverity? LastSeverity(string id) => RegistryProbe.Runtime.Entries.LastOrDefault(e => e.Id == id)?.Severity;

    /// <summary>
    /// 모바일 항목 ID 규칙(계약 C60 을 시험 안에 다시 쓴다): Mobile.EntryId 가 있으면 그것,
    /// Separator·Section 은 EntryId 가 없으면 항목 아님(null), 그 밖은 AutomationId 가 shell.mnu 로 시작하면 그 뒤, 아니면 메뉴 ID.
    /// </summary>
    public static string? MobileId(UiMenuItemDefinition d)
    {
        if (d.Mobile.EntryId is not null)
        {
            return d.Mobile.EntryId;
        }

        if (d.Kind == UiMenuItemKind.Separator || d.Kind == UiMenuItemKind.Section)
        {
            return null;
        }

        const string prefix = "shell.mnu";
        return d.AutomationId.StartsWith(prefix, StringComparison.Ordinal) ? d.AutomationId.Substring(prefix.Length) : d.Id;
    }
}

internal static class MenuRegistryChecks
{
    private const string OpenForm = "xkxk.file.openForm";

    /// <summary>B06 기본 명령 23개(순서·값 고정 — 뒤 판은 끝에만 더한다).</summary>
    private static readonly string[] B06Commands =
    {
        "xkxk.layout.save", "xkxk.layout.load", "xkxk.window.cascade", "xkxk.window.tileHorizontal", "xkxk.window.tileVertical", "xkxk.window.tabbed", "xkxk.window.minimize", "xkxk.window.pin",
        "xkxk.window.resetLayout", "xkxk.window.minimizeAll", "xkxk.window.restoreAll", "xkxk.window.restoreLastMinimized", "xkxk.window.next", "xkxk.window.previous", "xkxk.window.float", "xkxk.window.redock",
        "xkxk.window.overview", "xkxk.window.close", "xkxk.window.closeOthers", "xkxk.window.closeAll", "xkxk.layout.preset.mdiCascade", "xkxk.layout.preset.tabbed", "xkxk.layout.preset.mdiTileVertical",
    };

    /// <summary>B07 신규 셸 명령 38개(설계 계약 new_commands 표 순서).</summary>
    private static readonly string[] B07Commands =
    {
        "xkxk.demo.open", "xkxk.demo.openSdi", "xkxk.bench.open", "xkxk.layout.saveCurrent", "xkxk.layout.manage", "xkxk.app.exit", "xkxk.window.maximizeToggle", "xkxk.window.restore",
        "xkxk.window.rename", "xkxk.favorite.toggle", "xkxk.window.toggleSdi", "xkxk.sdi.resetPositions", "xkxk.sdi.minimizeAll", "xkxk.sdi.restoreAll", "xkxk.sdi.closeAll", "xkxk.monitor.single",
        "xkxk.monitor.maximizeSelected", "xkxk.mainWindow.reset", "xkxk.window.flash", "xkxk.view.showExplorer", "xkxk.view.showLog", "xkxk.view.showNotifications", "xkxk.view.fontReset", "xkxk.theme.toggle",
        "xkxk.theme.next", "xkxk.system.settings", "xkxk.system.restartRequired", "xkxk.system.appMonitor", "xkxk.system.logView", "xkxk.system.logFolder", "xkxk.system.logExport", "xkxk.system.inquiry",
        "xkxk.system.inquiryList", "xkxk.system.update", "xkxk.system.changelog", "xkxk.system.help", "xkxk.system.about", "xkxk.nav.openPanel",
    };

    /// <summary>시험대 이관 3명령(셸만 있는 앱에서 미등록, C78 과 같은 기대).</summary>
    private static readonly string[] MovedToTestbed = { ShellCommandIds.DemoOpen, ShellCommandIds.DemoOpenSdi, ShellCommandIds.BenchOpen };

    public static IEnumerable<ContractCase> All()
    {
        yield return new ContractCase("C46", "XkxkUi.Current.Menus 비 null·같은 인스턴스, 모듈 Configure 의 ui.Menus 비 null", C46);
        yield return new ContractCase("C47", "기본 셸 메뉴: 최상위 앞 4개 file·window·view·system, 창 첫 자식 cascade, ShellMenuIds.All 등록·Owner xkxk.shell·AutomationId shell.mnu·중복 0", C47);
        yield return new ContractCase("C48", "Add: InsertAfter 바로 뒤 자리·Changed(Added)·GetState Title·Icon 이 명령 기본값·AutomationId 기본 menu.<id>", C48);
        yield return new ContractCase("C49", "InsertBefore 바로 앞 자리, 없는 앵커 → 맨 뒤 + XKUI0404 Warning·true", C49);
        yield return new ContractCase("C50", "Parent 없음·Parent 가 Command 종류 → false + XKUI0403", C50);
        yield return new ContractCase("C51", "Kind 위반(Command 에 CommandId 없음·Section 에 Section 없음·Separator 에 CommandId) → false + XKUI0406", C51);
        yield return new ContractCase("C52", "같은 ID 재등록 XKUI0402·첫 등록 유지, Replace=true 자리 유지 + Replaced(P3), 없는 ID Replace=true → Added(P1)", C52);
        yield return new ContractCase("C53", "새 xkxk.* 메뉴 ID → false + XKUI0407, 기존 xkxk.* Replace=true → true·Owner 교체 주체(상태를 남기는 마지막 메뉴 시험)", C53);
        yield return new ContractCase("C60", "모바일 힌트(공개 경로): NavMenuCatalog.Entries 63개 ↔ Menus.Items Mobile 정의 1:1 대조·셸 여분 0·소비자 항목 Mobile 사본", C60);
        yield return new ContractCase("C61", "ShellCommandIds.All 61개·앞 23 B06 순서·신규 38 중 이관 3 제외 35 Owner xkxk.shell·셸 메뉴 CommandId 전부 등록(VisibleWhen=cmd 행 제외)·확대/축소 ID 없음", C61);
    }

    private static void C46()
    {
        var ctx = XkxkUi.Current;
        Check.True(ctx.Menus is not null && ReferenceEquals(ctx.Menus, XkxkUi.Current.Menus), "XkxkUi.Current.Menus 가 null 이거나 부를 때마다 다른 인스턴스");
        IUiMenuRegistry? seen = null;
        XkxkUi.CreateBuilder().AddModule(new RecordingModule("c46.module", new List<string>(), c => seen = c.Menus)).Build();
        Check.True(seen is not null, "모듈 Configure 안의 ui.Menus 가 null");
    }

    private static void C47()
    {
        var menus = XkxkUi.Current.Menus;
        var roots = MenuProbe.Children(null);
        var expectRoots = new[] { ShellMenuIds.File, ShellMenuIds.Window, ShellMenuIds.View, ShellMenuIds.System };
        Check.True(string.Join(",", expectRoots) == "xkxk.file,xkxk.window,xkxk.view,xkxk.system", "ShellMenuIds 루트 상수 값 오류");
        Check.True(roots.Count >= 4 && roots.Take(4).SequenceEqual(expectRoots), $"최상위 앞 4개={string.Join(",", roots.Take(4))}");
        Check.True(ShellMenuIds.Roots.SequenceEqual(expectRoots), $"ShellMenuIds.Roots={string.Join(",", ShellMenuIds.Roots)}");
        var win = MenuProbe.Children(ShellMenuIds.Window);
        Check.True(win.Count > 0 && win[0] == "xkxk.window.cascade", $"창 메뉴 첫 자식={(win.Count > 0 ? win[0] : "없음")}");
        Check.True(ShellMenuIds.All.Count > 0 && ShellMenuIds.All.Distinct(StringComparer.Ordinal).Count() == ShellMenuIds.All.Count, "ShellMenuIds.All 이 비었거나 중복");
        var autoIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in ShellMenuIds.All)
        {
            var found = menus.TryGet(id, out var def);
            Check.True(menus.Contains(id) && found && def is not null, $"셸 메뉴 {id} 미등록");
            Check.True(def!.Owner == UiRegistryOwners.Shell, $"{id} Owner={def.Owner}");
            if (def.Kind != UiMenuItemKind.Separator && def.Kind != UiMenuItemKind.Section)
            {
                Check.True(def.AutomationId.StartsWith("shell.mnu", StringComparison.Ordinal), $"{id} AutomationId={def.AutomationId}(shell.mnu 기대)");
            }

            Check.True(autoIds.Add(def.AutomationId), $"셸 메뉴 AutomationId {def.AutomationId} 중복");
        }
    }

    private static void C48()
    {
        var menus = XkxkUi.Current.Menus;
        var cmd = MenuProbe.Command();
        const string id = "c48.item";
        var ok = false;
        var changes = RegistryProbe.Changes(h => menus.Changed += h, h => menus.Changed -= h, id,
            () => ok = menus.Add(id, o => { o.Parent = ShellMenuIds.File; o.CommandId = cmd; o.InsertAfter = OpenForm; }));
        Check.True(ok && menus.Contains(id), "소비자 Command 항목 Add 실패");
        Check.True(changes.Count == 1 && changes[0] == UiRegistryChangeKind.Added, $"Added 알림 {changes.Count}건");
        var kids = MenuProbe.Children(ShellMenuIds.File);
        Check.True(kids.IndexOf(OpenForm) >= 0 && kids.IndexOf(id) == kids.IndexOf(OpenForm) + 1, $"InsertAfter 자리 {kids.IndexOf(id)} ≠ openForm+1");
        Check.True(menus.TryGet(id, out var d) && d!.Kind == UiMenuItemKind.Command && d.Parent == ShellMenuIds.File && d.CommandId == cmd
            && d.Owner == UiRegistryOwners.App, "정의 Kind·Parent·CommandId·Owner 불일치");
        Check.True(d!.AutomationId == "menu." + id, $"AutomationId 기본값={d.AutomationId}");
        var s = menus.GetState(id, UiMenuSurface.Desktop);
        Check.True(s.IsVisible && s.IsEnabled, "GetState 가 보임·활성이 아님");
        Check.True(s.Title == MenuProbe.CommandTitle && s.Icon == CmdIconKeys.Exit, $"GetState Title={s.Title}·Icon={s.Icon}(명령 기본값 기대)");
    }

    private static void C49()
    {
        var menus = XkxkUi.Current.Menus;
        var cmd = MenuProbe.Command();
        Check.True(menus.Add("c49.before", o => { o.Parent = ShellMenuIds.File; o.CommandId = cmd; o.InsertBefore = OpenForm; }), "InsertBefore Add 실패");
        var kids = MenuProbe.Children(ShellMenuIds.File);
        Check.True(kids.IndexOf("c49.before") >= 0 && kids.IndexOf("c49.before") == kids.IndexOf(OpenForm) - 1, "InsertBefore 자리가 openForm 바로 앞이 아님");
        var ok = false;
        Check.True(RegistryProbe.Delta(UiDiagnosticIds.MenuAnchorMissing, () => ok = menus.Add("c49.tail",
            o => { o.Parent = ShellMenuIds.File; o.CommandId = cmd; o.InsertAfter = "c49.no.anchor"; })) == 1
            && UiDiagnosticIds.MenuAnchorMissing == "XKUI0404" && ok, "없는 앵커에 XKUI0404 1건·true 가 아님");
        Check.True(MenuProbe.LastSeverity(UiDiagnosticIds.MenuAnchorMissing) == UiDiagnosticSeverity.Warning, "XKUI0404 가 Warning 이 아님");
        kids = MenuProbe.Children(ShellMenuIds.File);
        Check.True(kids[kids.Count - 1] == "c49.tail", "없는 앵커 항목이 맨 뒤가 아님");
    }

    private static void C50()
    {
        var menus = XkxkUi.Current.Menus;
        var cmd = MenuProbe.Command();
        var r = true;
        Check.True(RegistryProbe.Delta(UiDiagnosticIds.MenuParentMissing, () => r = menus.Add("c50.orphan", o => { o.Parent = "c50.no.such.parent"; o.CommandId = cmd; })) == 1
            && UiDiagnosticIds.MenuParentMissing == "XKUI0403" && !r && !menus.Contains("c50.orphan"), "없는 Parent 에 XKUI0403·false·미등록이 아님");
        Check.True(RegistryProbe.Delta(UiDiagnosticIds.MenuParentMissing, () => r = menus.Add("c50.undercmd", o => { o.Parent = OpenForm; o.CommandId = cmd; })) == 1
            && !r && !menus.Contains("c50.undercmd"), "Command 종류 Parent 에 XKUI0403·false·미등록이 아님");
        Check.True(MenuProbe.LastSeverity(UiDiagnosticIds.MenuParentMissing) == UiDiagnosticSeverity.Error, "XKUI0403 이 Error 가 아님");
    }

    private static void C51()
    {
        var menus = XkxkUi.Current.Menus;
        var cmd = MenuProbe.Command();
        var cases = new (string Id, Action<UiMenuItemOptions> Configure)[]
        {
            ("c51.nocmd", o => { o.Parent = ShellMenuIds.File; o.Kind = UiMenuItemKind.Command; }),
            ("c51.nosection", o => { o.Parent = ShellMenuIds.File; o.Kind = UiMenuItemKind.Section; }),
            ("c51.sepcmd", o => { o.Parent = ShellMenuIds.File; o.Kind = UiMenuItemKind.Separator; o.CommandId = cmd; }),
        };
        foreach (var (id, configure) in cases)
        {
            var r = true;
            Check.True(RegistryProbe.Delta(UiDiagnosticIds.MenuKindInvalid, () => r = menus.Add(id, configure)) == 1
                && UiDiagnosticIds.MenuKindInvalid == "XKUI0406" && !r && !menus.Contains(id), $"{id} 에 XKUI0406·false·미등록이 아님");
        }
    }

    private static void C52()
    {
        var menus = XkxkUi.Current.Menus;
        var cmd = MenuProbe.Command();
        Check.True(menus.Add("c52.a", o => { o.Parent = ShellMenuIds.File; o.CommandId = cmd; o.Title = "first"; })
            && menus.Add("c52.b", o => { o.Parent = ShellMenuIds.File; o.CommandId = cmd; }), "c52 항목 등록 실패");
        var r = true;
        Check.True(RegistryProbe.Delta(UiDiagnosticIds.MenuDuplicate, () => r = menus.Add("c52.a", o => { o.Parent = ShellMenuIds.File; o.CommandId = cmd; o.Title = "second"; })) == 1
            && UiDiagnosticIds.MenuDuplicate == "XKUI0402" && !r, "중복 ID 에 XKUI0402·false 가 아님");
        Check.True(menus.TryGet("c52.a", out var first) && first!.Title == "first", "중복 뒤 첫 등록이 유지되지 않음");
        var before = MenuProbe.Children(ShellMenuIds.File).IndexOf("c52.a");
        var changes = RegistryProbe.Changes(h => menus.Changed += h, h => menus.Changed -= h, "c52.a",
            () => r = menus.Add("c52.a", o => { o.Parent = ShellMenuIds.File; o.CommandId = cmd; o.Title = "third"; o.Replace = true; }));
        Check.True(r && changes.Count == 1 && changes[0] == UiRegistryChangeKind.Replaced, "Replace=true 가 Replaced 알림이 아님");
        Check.True(menus.TryGet("c52.a", out var rep) && rep!.Title == "third", "교체 정의가 보이지 않음");
        Check.True(MenuProbe.Children(ShellMenuIds.File).IndexOf("c52.a") == before, "Replace=true 뒤 자리가 바뀜(P3)");
        changes = RegistryProbe.Changes(h => menus.Changed += h, h => menus.Changed -= h, "c52.fresh",
            () => r = menus.Add("c52.fresh", o => { o.Parent = ShellMenuIds.File; o.CommandId = cmd; o.Replace = true; }));
        Check.True(r && menus.Contains("c52.fresh") && changes.Count == 1 && changes[0] == UiRegistryChangeKind.Added, "새 ID Replace=true 가 Added 가 아님(P1)");
    }

    private static void C53()
    {
        var menus = XkxkUi.Current.Menus;
        var cmd = MenuProbe.Command();
        var r = true;
        Check.True(RegistryProbe.Delta(UiDiagnosticIds.MenuIdReserved, () => r = menus.Add("xkxk.c53.custom", o => { o.Parent = ShellMenuIds.File; o.CommandId = cmd; })) == 1
            && UiDiagnosticIds.MenuIdReserved == "XKUI0407" && !r && !menus.Contains("xkxk.c53.custom"), "새 예약 메뉴 ID 에 XKUI0407·false·미등록이 아님");
        const string target = "xkxk.window.cascade";
        var index = MenuProbe.Children(ShellMenuIds.Window).IndexOf(target);
        Check.True(index >= 0, "교체 전 xkxk.window.cascade 메뉴가 없음");
        Check.True(menus.Add(target, o => { o.Parent = ShellMenuIds.Window; o.CommandId = ShellCommandIds.WindowCascade; o.Title = "C53"; o.Replace = true; }),
            "기존 xkxk.* 메뉴 Replace=true 가 false");
        Check.True(menus.TryGet(target, out var d) && d!.Owner == UiRegistryOwners.App && d.Title == "C53", $"교체 뒤 Owner={d?.Owner}(app 기대)");
        Check.True(MenuProbe.Children(ShellMenuIds.Window).IndexOf(target) == index, "교체 뒤 자리가 바뀜(P3)");
    }

    private static void C60()
    {
        var menus = XkxkUi.Current.Menus;
        var entries = NavMenuCatalog.Entries;
        Check.True(entries.Count == 63, $"NavMenuCatalog.Entries {entries.Count}개(63 기대)");
        var items = menus.Items;
        var entryIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in entries)
        {
            Check.True(entryIds.Add(e.Id), $"Entries 의 id {e.Id} 중복");
            var hits = items.Where(d => d.Mobile.InDrawer && MenuProbe.MobileId(d) == e.Id).ToList();
            Check.True(hits.Count == 1, $"모바일 항목 {e.Id} 에 맞는 메뉴 정의 {hits.Count}개(1 기대)");
            var m = hits[0].Mobile;
            Check.True((m.TitleKey ?? hits[0].TitleKey) == e.TextKey, $"{e.Id} 문구 키 {m.TitleKey ?? hits[0].TitleKey} ≠ {e.TextKey}");
            Check.True(m.Available == e.Applicable, $"{e.Id} Available={m.Available} ≠ Applicable={e.Applicable}");
            Check.True(e.Applicable || m.UnavailableReasonKey == e.ReasonKey, $"{e.Id} 사유 키 {m.UnavailableReasonKey} ≠ {e.ReasonKey}");
            Check.True(m.TabOrder == e.TabOrder, $"{e.Id} TabOrder {m.TabOrder} ≠ {e.TabOrder}");
            Check.True(m.CoveredAutomationIds.SequenceEqual(e.DesktopIds), $"{e.Id} CoveredAutomationIds [{string.Join(",", m.CoveredAutomationIds)}] ≠ DesktopIds [{string.Join(",", e.DesktopIds)}]");
        }

        var shellIds = new HashSet<string>(ShellMenuIds.All, StringComparer.Ordinal);
        var extras = items.Where(d => shellIds.Contains(d.Id) && d.Mobile.InDrawer && MenuProbe.MobileId(d) is { } mid && !entryIds.Contains(mid))
            .Select(d => d.Id).ToList();
        Check.True(extras.Count == 0, $"Entries 에 없는 셸 서랍 정의 {extras.Count}개: {string.Join(",", extras)}");

        const string id = "c60.item";
        var ok = false;
        var changes = RegistryProbe.Changes(h => menus.Changed += h, h => menus.Changed -= h, id, () => ok = menus.Add(id, o =>
        {
            o.Parent = ShellMenuIds.File;
            o.CommandId = MenuProbe.Command();
            o.Mobile.InDrawer = true;
            o.Mobile.Available = false;
            o.Mobile.UnavailableReasonKey = "c60.reason";
        }));
        Check.True(ok && changes.Count == 1 && changes[0] == UiRegistryChangeKind.Added, "소비자 항목 Add 실패 또는 Added 알림 아님");
        Check.True(menus.TryGet(id, out var def) && def!.Mobile.InDrawer && !def.Mobile.Available && def.Mobile.UnavailableReasonKey == "c60.reason",
            "소비자 항목 정의의 Mobile 사본이 넣은 값이 아님");
    }

    private static void C61()
    {
        var all = ShellCommandIds.All;
        var cmds = XkxkUi.Current.Commands;
        Check.True(all.Count == B06Commands.Length + B07Commands.Length && all.Count == 61, $"ShellCommandIds.All {all.Count}개(61 기대)");
        Check.True(all.Take(B06Commands.Length).SequenceEqual(B06Commands), $"All 앞 {B06Commands.Length}개가 B06 순서와 다름: {string.Join(",", all.Take(B06Commands.Length))}");
        for (var i = 0; i < B07Commands.Length; i++)
        {
            var id = all[B06Commands.Length + i];
            Check.True(id == B07Commands[i], $"All[{B06Commands.Length + i}]={id} ≠ 신규 {B07Commands[i]}");
            Check.True(MovedToTestbed.Contains(id) ? !cmds.Contains(id) : cmds.TryGet(id, out var d) && d!.Owner == UiRegistryOwners.Shell, $"신규 명령 {id}: 이관은 미등록·그 밖은 등록+Owner xkxk.shell 기대");
        }

        var menus = XkxkUi.Current.Menus;
        var cmdRows = // VisibleWhen=cmd 행(데모 6행)은 명령이 시험대에서 오므로 셸만 있는 앱에서 CommandId 미등록
            new HashSet<string>(ShellMenuTable.Rows.Where(r => r.VisibleWhen == "cmd").Select(r => r.Id), StringComparer.Ordinal);
        foreach (var mid in ShellMenuIds.All)
        {
            if (!cmdRows.Contains(mid) && menus.TryGet(mid, out var m) && m.Kind == UiMenuItemKind.Command)
            {
                Check.True(m.CommandId is not null && cmds.Contains(m.CommandId), $"셸 메뉴 {mid} 의 CommandId {m.CommandId} 미등록");
            }
        }

        Check.True(!all.Any(id => id.Contains("zoom", StringComparison.OrdinalIgnoreCase)), "확대·축소 명령 ID 가 있음(a8 §15 위반)");
    }
}
