// 메뉴 레지스트리 규칙 계약 시험 C54~C59·C62 — 소유 Remove(P2)·Move·SetHidden·조건 대리자·명령 없음·빌더 지연 검사·단축키 충돌·변경 종류 값을 본다(UADR-0014)
using System;
using System.Collections.Generic;
using xkxk.frameworks.ui.shell.Framework;

namespace xkxk.frameworks.ui.test.framework.contract;

internal static class MenuRuleChecks
{
    private const string OpenForm = "xkxk.file.openForm";

    public static IEnumerable<ContractCase> All()
    {
        yield return new ContractCase("C54", "Remove: 다른 주체 항목 XKUI0408·남음, 자기 항목 Removed, 다른 주체 자식이 있는 자기 하위 메뉴 거부·안 지움, 같은 주체 자식만이면 자식째 제거", C54);
        yield return new ContractCase("C55", "Move: 다른 부모·InsertAfter → Moved, ToRoot → 최상위, 순환·없는 대상 → false + XKUI0410", C55);
        yield return new ContractCase("C56", "SetHidden(true) → GetState.IsVisible false·VisibilityChanged, 다른 주체 항목 허용, SetHidden(false) 복귀", C56);
        yield return new ContractCase("C57", "IsVisible·ToolTip 대리자가 UiMenuContext(App·Item·Surface)를 받음, 대리자 예외 → IsVisible false + XKUI0411 Warning(키당 1회)", C57);
        yield return new ContractCase("C58", "실행 중 Add 의 CommandId 가 없는 명령 → true(항목 유지) + XKUI0405, GetState.IsEnabled false", C58);
        yield return new ContractCase("C59", "Build: 뒤 모듈 명령을 가리키는 메뉴 진단 없음(Build 끝 검사), 끝까지 없는 명령은 Collect 면 XKUI0405, Throw 면 UiConfigurationException", C59);
        yield return new ContractCase("C62", "단축키 충돌 XKUI0310 Warning(Ctrl+Tab·정규화 ctrl+shift+tab·Shift+Ctrl+Tab), 겹치지 않으면 진단 없음, ChangeKind Moved=5·VisibilityChanged=6", C62);
    }

    private static void Noop(UiCommandContext _)
    {
    }

    private static void C54()
    {
        var menus = XkxkUi.Current.Menus;
        var cmd = MenuProbe.Command();
        var r = true;
        Check.True(RegistryProbe.Delta(UiDiagnosticIds.MenuRemoveDenied, () => r = menus.Remove(OpenForm)) == 1
            && UiDiagnosticIds.MenuRemoveDenied == "XKUI0408" && !r && menus.Contains(OpenForm), "셸 메뉴 Remove 에 XKUI0408·false·유지가 아님");
        Check.True(MenuProbe.LastSeverity(UiDiagnosticIds.MenuRemoveDenied) == UiDiagnosticSeverity.Error, "XKUI0408 이 Error 가 아님");

        Check.True(menus.Add("c54.own", o => { o.Parent = ShellMenuIds.File; o.CommandId = cmd; }), "c54.own 등록 실패");
        var changes = RegistryProbe.Changes(h => menus.Changed += h, h => menus.Changed -= h, "c54.own", () => r = menus.Remove("c54.own"));
        Check.True(r && !menus.Contains("c54.own") && changes.Count == 1 && changes[0] == UiRegistryChangeKind.Removed, "자기 항목 Remove 실패 또는 Removed 알림 아님");

        Check.True(menus.Add("c54.sub", o => o.Kind = UiMenuItemKind.Submenu), "c54.sub(최상위 하위 메뉴) 등록 실패");
        XkxkUi.CreateBuilder().AddModule(new RecordingModule("c54.module", new List<string>(),
            c => c.Menus.Add("c54.sub.mod", o => { o.Parent = "c54.sub"; o.CommandId = cmd; }))).Build();
        Check.True(menus.TryGet("c54.sub.mod", out var modChild) && modChild!.Owner == "c54.module", "모듈 등록 메뉴 Owner 가 모듈 ID 가 아님");
        Check.True(RegistryProbe.Delta(UiDiagnosticIds.MenuRemoveDenied, () => r = menus.Remove("c54.sub")) == 1 && !r,
            "다른 주체 자식이 있는 하위 메뉴 Remove 에 XKUI0408·false 가 아님");
        Check.True(menus.Contains("c54.sub") && menus.Contains("c54.sub.mod"), "거부된 Remove 가 무언가를 지움");

        Check.True(menus.Add("c54.sub2", o => o.Kind = UiMenuItemKind.Submenu)
            && menus.Add("c54.sub2.a", o => { o.Parent = "c54.sub2"; o.CommandId = cmd; }), "c54.sub2 등록 실패");
        Check.True(menus.Remove("c54.sub2") && !menus.Contains("c54.sub2") && !menus.Contains("c54.sub2.a"), "같은 주체 자식만 있는 하위 메뉴가 자식째 제거되지 않음");
    }

    private static void C55()
    {
        var menus = XkxkUi.Current.Menus;
        var cmd = MenuProbe.Command();
        const string id = "c55.a";
        Check.True(menus.Add(id, o => { o.Parent = ShellMenuIds.File; o.CommandId = cmd; }), "c55.a 등록 실패");
        var ok = false;
        var changes = RegistryProbe.Changes(h => menus.Changed += h, h => menus.Changed -= h, id,
            () => ok = menus.Move(id, p => { p.Parent = ShellMenuIds.Window; p.InsertAfter = "xkxk.window.cascade"; }));
        Check.True(ok && changes.Count == 1 && changes[0] == UiRegistryChangeKind.Moved, "다른 부모로 Move 실패 또는 Moved 알림 아님");
        var win = MenuProbe.Children(ShellMenuIds.Window);
        Check.True(menus.TryGet(id, out var d) && d!.Parent == ShellMenuIds.Window && win.IndexOf(id) == win.IndexOf("xkxk.window.cascade") + 1,
            "Move 뒤 부모·InsertAfter 자리 불일치");
        Check.True(!MenuProbe.Children(ShellMenuIds.File).Contains(id), "Move 뒤 옛 부모에 남음");
        Check.True(menus.Move(id, p => p.ToRoot = true) && menus.TryGet(id, out var root) && root!.Parent is null && MenuProbe.Children(null).Contains(id),
            "ToRoot Move 뒤 최상위가 아님");

        Check.True(menus.Add("c55.p", o => o.Kind = UiMenuItemKind.Submenu)
            && menus.Add("c55.p.c", o => { o.Parent = "c55.p"; o.Kind = UiMenuItemKind.Submenu; }), "c55 하위 메뉴 등록 실패");
        var r = true;
        Check.True(RegistryProbe.Delta(UiDiagnosticIds.MenuMoveInvalid, () => r = menus.Move("c55.p", p => p.Parent = "c55.p.c")) == 1
            && UiDiagnosticIds.MenuMoveInvalid == "XKUI0410" && !r, "자기 자손 아래로 Move(순환)에 XKUI0410·false 가 아님");
        Check.True(menus.TryGet("c55.p", out var p0) && p0!.Parent is null, "거부된 순환 Move 가 부모를 바꿈");
        Check.True(RegistryProbe.Delta(UiDiagnosticIds.MenuMoveInvalid, () => r = menus.Move("c55.no.such", p => p.ToRoot = true)) == 1 && !r,
            "없는 대상 Move 에 XKUI0410·false 가 아님");
    }

    private static void C56()
    {
        var menus = XkxkUi.Current.Menus;
        const string id = "c56.item";
        Check.True(menus.Add(id, o => { o.Parent = ShellMenuIds.File; o.CommandId = MenuProbe.Command(); }), "c56.item 등록 실패");
        Check.True(menus.GetState(id, UiMenuSurface.Desktop).IsVisible, "숨기기 전 IsVisible 이 false");
        var ok = false;
        var changes = RegistryProbe.Changes(h => menus.Changed += h, h => menus.Changed -= h, id, () => ok = menus.SetHidden(id, true));
        Check.True(ok && changes.Count == 1 && changes[0] == UiRegistryChangeKind.VisibilityChanged, "SetHidden(true) 실패 또는 VisibilityChanged 알림 아님");
        Check.True(!menus.GetState(id, UiMenuSurface.Desktop).IsVisible && menus.TryGet(id, out var d) && d!.IsHidden, "SetHidden(true) 뒤에도 보임");
        Check.True(menus.SetHidden(id, false) && menus.GetState(id, UiMenuSurface.Desktop).IsVisible, "SetHidden(false) 뒤 복귀하지 않음");

        var shellHidden = menus.SetHidden(OpenForm, true);
        var shellFound = menus.TryGet(OpenForm, out var s);
        Check.True(shellHidden && shellFound && s!.IsHidden, "다른 주체(셸) 항목 SetHidden 이 거부됨");
        Check.True(s!.Owner == UiRegistryOwners.Shell, "SetHidden 이 Owner 를 바꿈");
        Check.True(menus.SetHidden(OpenForm, false) && menus.TryGet(OpenForm, out var s2) && !s2!.IsHidden, "셸 항목 SetHidden(false) 원복 실패");
    }

    private static void C57()
    {
        var menus = XkxkUi.Current.Menus;
        var cmd = MenuProbe.Command();
        UiMenuContext? seen = null;
        var visible = true;
        const string id = "c57.item";
        Check.True(menus.Add(id, o =>
        {
            o.Parent = ShellMenuIds.File;
            o.CommandId = cmd;
            o.IsVisible = c => { seen = c; return visible; };
            o.ToolTip = c => "c57.tip:" + c.Surface;
        }), "조건 대리자 항목 등록 실패");
        Check.True(menus.TryGet(id, out var def) && def!.HasVisibilityCondition && def.HasToolTip, "HasVisibilityCondition·HasToolTip 이 true 가 아님");
        var st = menus.GetState(id, UiMenuSurface.Mobile);
        Check.True(st.IsVisible && st.ToolTip == "c57.tip:Mobile", $"GetState(Mobile) IsVisible={st.IsVisible}·ToolTip={st.ToolTip}");
        Check.True(seen is not null && ReferenceEquals(seen.App, XkxkUi.Current) && seen.Item.Id == id && seen.Surface == UiMenuSurface.Mobile,
            "UiMenuContext 의 App·Item·Surface 불일치");
        visible = false;
        Check.True(!menus.GetState(id, UiMenuSurface.Desktop).IsVisible && seen!.Surface == UiMenuSurface.Desktop, "IsVisible 대리자 false 가 반영되지 않음");

        const string bad = "c57.throw";
        Check.True(menus.Add(bad, o => { o.Parent = ShellMenuIds.File; o.CommandId = cmd; o.IsVisible = _ => throw new FormatException("일부러"); }), "예외 대리자 항목 등록 실패");
        var shown = true;
        var delta = RegistryProbe.Delta(UiDiagnosticIds.MenuConditionFailed, () =>
        {
            shown = menus.GetState(bad, UiMenuSurface.Desktop).IsVisible;
            shown |= menus.GetState(bad, UiMenuSurface.Desktop).IsVisible;
        });
        Check.True(!shown && delta == 1 && UiDiagnosticIds.MenuConditionFailed == "XKUI0411", $"대리자 예외에 숨김·XKUI0411 1건이 아님(진단 {delta}건)");
        Check.True(MenuProbe.LastSeverity(UiDiagnosticIds.MenuConditionFailed) == UiDiagnosticSeverity.Warning, "XKUI0411 이 Warning 이 아님");
    }

    private static void C58()
    {
        var menus = XkxkUi.Current.Menus;
        var ok = false;
        Check.True(RegistryProbe.Delta(UiDiagnosticIds.MenuCommandMissing, () => ok = menus.Add("c58.item",
            o => { o.Parent = ShellMenuIds.File; o.CommandId = "c58.no.such.command"; })) == 1
            && UiDiagnosticIds.MenuCommandMissing == "XKUI0405" && ok && menus.Contains("c58.item"), "없는 명령 항목에 XKUI0405·true·유지가 아님");
        Check.True(!menus.GetState("c58.item", UiMenuSurface.Desktop).IsEnabled, "없는 명령 항목 GetState.IsEnabled 가 true");
        Check.True(MenuProbe.LastSeverity(UiDiagnosticIds.MenuCommandMissing) == UiDiagnosticSeverity.Error, "XKUI0405 가 Error 가 아님");
    }

    private static void C59()
    {
        var b = XkxkUi.CreateBuilder();
        b.AddModule(new RecordingModule("c59.modA", new List<string>(), c => c.Menus.Add("c59.a", o => { o.Parent = ShellMenuIds.File; o.CommandId = "c59.late"; })))
         .AddModule(new RecordingModule("c59.modB", new List<string>(), c => c.Commands.Add("c59.late", o => o.Execute = Noop)))
         .AddModule(new RecordingModule("c59.modC", new List<string>(), c => c.Menus.Add("c59.c", o => { o.Parent = ShellMenuIds.File; o.CommandId = "c59.never"; })));
        var runtimeBefore = RegistryProbe.Count(RegistryProbe.Runtime, UiDiagnosticIds.MenuCommandMissing);
        var app = b.Build();
        var missing = 0;
        foreach (var e in app.Diagnostics.Entries)
        {
            if (e.Id == UiDiagnosticIds.MenuCommandMissing)
            {
                missing++;
                Check.True(e.Severity == UiDiagnosticSeverity.Error, "빌더 XKUI0405 가 Error 가 아님");
            }
        }

        Check.True(missing == 1, $"빌더 XKUI0405 {missing}건(모듈 C 1건 기대 — 뒤 모듈 명령 참조는 진단 없음)");
        Check.True(XkxkUi.Current.Menus.Contains("c59.a") && XkxkUi.Current.Menus.Contains("c59.c"), "모듈 메뉴가 등록되지 않음");
        Check.True(RegistryProbe.Count(RegistryProbe.Runtime, UiDiagnosticIds.MenuCommandMissing) == runtimeBefore, "Build 중 메뉴 진단이 앱 진단에 쌓임");

        var tb = XkxkUi.CreateBuilder(new XkxkUiBuilderOptions { ErrorPolicy = UiErrorPolicy.Throw });
        tb.AddModule(new RecordingModule("c59.modT", new List<string>(), c => c.Menus.Add("c59.t", o => { o.Parent = ShellMenuIds.File; o.CommandId = "c59.never2"; })));
        UiConfigurationException? caught = null;
        try
        {
            tb.Build();
        }
        catch (UiConfigurationException ex)
        {
            caught = ex;
        }

        Check.True(caught is not null, "ErrorPolicy=Throw 인데 없는 명령 메뉴로 예외가 나지 않음");
        var inEx = false;
        foreach (var e in caught!.Diagnostics)
        {
            inEx |= e.Id == UiDiagnosticIds.MenuCommandMissing;
        }

        Check.True(inEx, "예외 Diagnostics 에 XKUI0405 없음");
    }

    private static void C62()
    {
        var cmds = XkxkUi.Current.Commands;
        Check.True(UiDiagnosticIds.CommandGestureConflict == "XKUI0310", "CommandGestureConflict 값이 XKUI0310 이 아님");
        var conflicts = new[] { ("c62.a", "Ctrl+Tab"), ("c62.b", "ctrl+shift+tab"), ("c62.c", "Shift+Ctrl+Tab") };
        foreach (var (id, gesture) in conflicts)
        {
            var ok = false;
            var delta = RegistryProbe.Delta(UiDiagnosticIds.CommandGestureConflict, () => ok = cmds.Add(id, o => { o.Gesture = gesture; o.Execute = Noop; }));
            Check.True(ok && cmds.Contains(id) && delta == 1, $"\"{gesture}\" 충돌에 true·XKUI0310 1건이 아님(진단 {delta}건)");
            Check.True(MenuProbe.LastSeverity(UiDiagnosticIds.CommandGestureConflict) == UiDiagnosticSeverity.Warning, "XKUI0310 이 Warning 이 아님");
            Check.True(cmds.Remove(id), $"{id} 정리(Remove) 실패");
        }

        Check.True(RegistryProbe.Delta(UiDiagnosticIds.CommandGestureConflict, () => cmds.Add("c62.free", o => { o.Gesture = "Ctrl+Alt+Shift+F12"; o.Execute = Noop; })) == 0,
            "겹치지 않는 단축키에 XKUI0310 이 생김");
        Check.True(cmds.Remove("c62.free"), "c62.free 정리(Remove) 실패");
        Check.True((int)UiRegistryChangeKind.Moved == 5 && (int)UiRegistryChangeKind.VisibilityChanged == 6, "UiRegistryChangeKind Moved=5·VisibilityChanged=6 이 아님");
    }
}
