// 명령 레지스트리 계약 시험 C39~C45 — 기본 셸 명령 61개·앱 없음 동작·실행 문맥·규칙 진단(단축키·예약·소유)·빌더 지연 검사(P4)·변경 알림·제자리 교체를 본다(UADR-0013)
using System;
using System.Collections.Generic;
using System.Linq;
using xkxk.frameworks.ui.shell.Framework;

namespace xkxk.frameworks.ui.test.framework.contract;

internal static class CommandRegistryChecks
{
    /// <summary>T-GESTURE 무효 사례 — 각각 XKUI0305·Gesture null·명령은 등록.</summary>
    private static readonly string[] InvalidGestures = { "Ctrl+Nope", "Ctrl", "Ctrl+Shift", "Ctrl+999", "A+B", "Ctrl++", "Ctrl+Ctrl+A" };

    /// <summary>T-GESTURE 유효 사례 — 진단 없음·Gesture 보존.</summary>
    private static readonly string[] ValidGestures = { "Ctrl+Shift+N", "Ctrl+F4", "Alt+D1", "Ctrl+Enter", "Ctrl+PageUp" };

    public static IEnumerable<ContractCase> All()
    {
        yield return new ContractCase("C39", "기본 셸 명령: ShellCommandIds.All 61개·중복 0·Owner xkxk.shell·아이콘 해석·문구 키 존재·Commands 앞 61개 순서 = All", C39);
        yield return new ContractCase("C40", "앱 없음(Shell null): 기본 명령 CanExecute false(overview 만 true)·Execute(cascade) false·예외·Error 진단 0", C40);
        yield return new ContractCase("C41", "사용자 명령 실행: ctx.CommandId·Parameter(null·42)·App, CanExecute false 면 Execute false·대리자 미호출", C41);
        yield return new ContractCase("C42", "명령 규칙 진단 XKUI0301·0302·0303·0304·0306·0307, 단축키 표(XKUI0305), 예약 ID XKUI0308, 다른 주체 Remove XKUI0309", C42);
        yield return new ContractCase("C43", "빌더 지연 검사(모듈 순서 무관 XKUI0304·Throw 예외)·Build 뒤 붙잡아 둔 ui.Commands 등록은 실행 중 진단(P4)", C43);
        yield return new ContractCase("C44", "명령 Remove(자기)·Changed(Added/Removed/Replaced)·Commands 사본·정의 필드·새 ID Replace=true → Added(P1)·재등록은 맨 뒤", C44);
        yield return new ContractCase("C45", "셸 기본 명령 대체: Replace=true 로 Owner 변경·새 대리자 실행·Commands 자리 유지(P3, 상태를 남기는 마지막 레지스트리 시험)", C45);
    }

    private static int IndexOf(IReadOnlyList<UiCommandDefinition> list, string id)
    {
        for (var i = 0; i < list.Count; i++)
        {
            if (list[i].Id == id)
            {
                return i;
            }
        }

        return -1;
    }

    private static void C39()
    {
        var ctx = XkxkUi.Current;
        var all = ShellCommandIds.All;
        Check.True(all.Count == 61 && all.Distinct(StringComparer.Ordinal).Count() == 61, $"ShellCommandIds.All 수 {all.Count}(중복 없는 61 기대)");
        Check.True(ShellCommandIds.WindowOverview == "xkxk.window.overview" && all.Contains(ShellCommandIds.WindowCascade), "ShellCommandIds 상수 값 오류");
        foreach (var id in all)
        {
            Check.True(id.StartsWith(UiRegistryOwners.ReservedPrefix, StringComparison.Ordinal), $"{id} 가 예약 접두어로 시작하지 않음");
            Check.True(ctx.Commands.Contains(id), $"기본 명령 {id} 미등록");
            Check.True(ctx.Commands.TryGet(id, out var def) && def is not null, $"TryGet({id}) 실패");
            Check.True(def!.Owner == UiRegistryOwners.Shell && UiRegistryOwners.Shell == "xkxk.shell", $"{id} Owner={def.Owner}");
            Check.True(def.Icon is not null && ctx.Icons.Contains(def.Icon), $"{id} 아이콘 {def.Icon} 해석 불가");
            Check.True(def.TitleKey is not null && ctx.Localization.HasKey(def.TitleKey), $"{id} 문구 키 {def.TitleKey} 가 사전에 없음");
        }

        var list = ctx.Commands.Commands;
        Check.True(list.Count >= all.Count, $"Commands 수 {list.Count}");
        for (var i = 0; i < all.Count; i++)
        {
            Check.True(list[i].Id == all[i], $"Commands[{i}]={list[i].Id} ≠ All[{i}]={all[i]}");
        }
    }

    private static void C40()
    {
        var ctx = XkxkUi.Current;
        Check.True(ctx.Shell is null, "콘솔 시험에서 Shell 이 null 이 아님(앱 없음 전제 깨짐)");
        var errors = RegistryProbe.Errors(RegistryProbe.Runtime);
        foreach (var id in ShellCommandIds.All)
        {
            var can = ctx.Commands.CanExecute(id);
            if (id == ShellCommandIds.WindowOverview)
            {
                Check.True(can, "앱 없음에서 xkxk.window.overview CanExecute 가 false");
            }
            else
            {
                Check.True(!can, $"앱 없음에서 {id} CanExecute 가 true");
            }
        }

        Check.True(!ctx.Commands.Execute(ShellCommandIds.WindowCascade), "앱 없음에서 Execute(cascade) 가 true");
        Check.True(!ctx.Commands.Execute(ShellCommandIds.WindowCascade, null), "앱 없음에서 Execute(cascade, null) 가 true");
        Check.True(RegistryProbe.Errors(RegistryProbe.Runtime) == errors, "앱 없음 기본 명령 동작이 Error 진단을 남김");
    }

    private static void C41()
    {
        var ctx = XkxkUi.Current;
        UiCommandContext? seen = null;
        var calls = 0;
        var allow = true;
        Check.True(ctx.Commands.Add("c41.run", o =>
        {
            o.Title = "C41";
            o.Execute = c => { seen = c; calls++; };
            o.CanExecute = _ => allow;
        }), "사용자 명령 Add 실패");
        Check.True(ctx.Commands.CanExecute("c41.run") && ctx.Commands.CanExecute("c41.run", 1), "CanExecute 가 false");
        Check.True(ctx.Commands.Execute("c41.run") && calls == 1, "Execute(id) 가 대리자를 부르지 않음");
        Check.True(seen is not null && seen.CommandId == "c41.run" && seen.Parameter is null && ReferenceEquals(seen.App, XkxkUi.Current),
            "ctx.CommandId·Parameter(null)·App 불일치");
        Check.True(ctx.Commands.Execute("c41.run", 42) && calls == 2 && seen!.Parameter is int p && p == 42, "Execute(id, 42) 의 Parameter 가 42 가 아님");
        allow = false;
        Check.True(!ctx.Commands.CanExecute("c41.run"), "CanExecute 대리자 false 가 반영되지 않음");
        Check.True(!ctx.Commands.Execute("c41.run") && calls == 2, "CanExecute false 인데 Execute 가 true 이거나 대리자가 불림");
    }

    private static void C44()
    {
        var cmds = XkxkUi.Current.Commands;
        const string id = "c44.x";
        var ok = false;
        var changes = RegistryProbe.Changes(h => cmds.Changed += h, h => cmds.Changed -= h, id, () => ok = cmds.Add(id, o =>
        {
            o.TitleKey = "c44.title";
            o.Title = "C44";
            o.Icon = RegistryIconKey;
            o.Gesture = "Ctrl+Shift+N";
            o.Category = "c44cat";
            o.DescriptionKey = "c44.desc";
            o.Execute = _ => { };
        }));
        Check.True(ok && changes.Count == 1 && changes[0] == UiRegistryChangeKind.Added, "Add 실패 또는 Added 알림 아님");
        Check.True(cmds.TryGet(id, out var d) && d is not null, "TryGet 실패");
        Check.True(d!.Id == id && d.TitleKey == "c44.title" && d.Title == "C44" && d.Icon == RegistryIconKey && d.Gesture == "Ctrl+Shift+N"
            && d.Category == "c44cat" && d.DescriptionKey == "c44.desc" && d.Owner == UiRegistryOwners.App, "정의 필드 불일치");

        changes = RegistryProbe.Changes(h => cmds.Changed += h, h => cmds.Changed -= h, id, () => ok = cmds.Add(id, o => { o.Title = "C44b"; o.Replace = true; o.Execute = _ => { }; }));
        Check.True(ok && changes.Count == 1 && changes[0] == UiRegistryChangeKind.Replaced, "Replace=true 가 Replaced 알림이 아님");
        Check.True(cmds.TryGet(id, out var r) && r!.Title == "C44b", "교체 정의가 보이지 않음");

        var snap = cmds.Commands;
        var n = snap.Count;
        Check.True(cmds.Add("c44.y", o => o.Execute = _ => { }), "c44.y 등록 실패");
        Check.True(snap.Count == n, "이전에 받은 Commands 목록이 이후 등록에 따라 바뀜(사본 아님)");
        if (snap is ICollection<UiCommandDefinition> col && !col.IsReadOnly)
        {
            var total = cmds.Commands.Count;
            col.Clear();
            Check.True(cmds.Commands.Count == total, "돌려받은 Commands 목록 수정이 레지스트리에 비침");
        }

        const string fresh = "c44.new";
        changes = RegistryProbe.Changes(h => cmds.Changed += h, h => cmds.Changed -= h, fresh, () => ok = cmds.Add(fresh, o => { o.Replace = true; o.Execute = _ => { }; }));
        Check.True(ok && cmds.Contains(fresh) && changes.Count == 1 && changes[0] == UiRegistryChangeKind.Added, "새 ID Replace=true 가 Added 가 아님(P1)");

        changes = RegistryProbe.Changes(h => cmds.Changed += h, h => cmds.Changed -= h, id, () => ok = cmds.Remove(id));
        Check.True(ok && !cmds.Contains(id) && changes.Count == 1 && changes[0] == UiRegistryChangeKind.Removed, "자기 명령 Remove 실패 또는 Removed 알림 아님");
        Check.True(cmds.Add(id, o => o.Execute = _ => { }), "Remove 뒤 재등록 실패");
        var list = cmds.Commands;
        Check.True(list[list.Count - 1].Id == id, "Remove 뒤 재등록이 Commands 맨 뒤가 아님");
    }

    /// <summary>C44 가 쓰는 해석 가능한 아이콘 키(생성 공급자 키).</summary>
    private const string RegistryIconKey = xkxk.frameworks.ui.data.Navigation.CmdIconKeys.Exit;

    private static void C45()
    {
        var cmds = XkxkUi.Current.Commands;
        var id = ShellCommandIds.WindowCascade;
        var index = IndexOf(cmds.Commands, id);
        Check.True(index >= 0, "교체 전 cascade 가 없음");
        var called = 0;
        Check.True(cmds.Add(id, o => { o.Title = "C45"; o.Replace = true; o.Execute = _ => called++; }), "셸 기본 명령 Replace=true 교체 실패");
        Check.True(cmds.TryGet(id, out var d) && d!.Owner == UiRegistryOwners.App && d.Owner != UiRegistryOwners.Shell, "교체 뒤 Owner 가 바뀌지 않음");
        Check.True(cmds.Execute(id) && called == 1, "교체 뒤 Execute 가 새 대리자를 부르지 않음");
        Check.True(IndexOf(cmds.Commands, id) == index, $"교체 뒤 자리 {IndexOf(cmds.Commands, id)} ≠ 교체 전 {index}(P3)");
    }

    private static void Noop(UiCommandContext _)
    {
    }

    private static void C42()
    {
        var cmds = XkxkUi.Current.Commands;
        var r = true;
        Check.True(RegistryProbe.Delta(UiDiagnosticIds.CommandIdEmpty, () => r = cmds.Add("", o => o.Execute = Noop)) == 1
            && UiDiagnosticIds.CommandIdEmpty == "XKUI0301" && !r, "빈 ID 에 XKUI0301·false 가 아님");

        Check.True(cmds.Add("c42.dup", o => { o.Title = "first"; o.Execute = Noop; }), "첫 등록 실패");
        Check.True(RegistryProbe.Delta(UiDiagnosticIds.CommandDuplicate, () => r = cmds.Add("c42.dup", o => { o.Title = "second"; o.Execute = Noop; })) == 1
            && UiDiagnosticIds.CommandDuplicate == "XKUI0302" && !r, "중복 ID 에 XKUI0302·false 가 아님");
        Check.True(cmds.TryGet("c42.dup", out var dup) && dup!.Title == "first", "중복 뒤 첫 등록이 유지되지 않음");

        Check.True(RegistryProbe.Delta(UiDiagnosticIds.CommandExecuteMissing, () => r = cmds.Add("c42.noexec", o => o.Title = "x")) == 1
            && UiDiagnosticIds.CommandExecuteMissing == "XKUI0303" && !r && !cmds.Contains("c42.noexec"), "Execute 없음에 XKUI0303·미등록이 아님");

        Check.True(RegistryProbe.Delta(UiDiagnosticIds.CommandIconMissing, () => cmds.Add("c42.noicon", o => { o.Icon = "c42.no.such.icon"; o.Execute = Noop; })) == 1
            && UiDiagnosticIds.CommandIconMissing == "XKUI0304" && cmds.Contains("c42.noicon"), "없는 아이콘에 XKUI0304·등록 유지가 아님");

        Check.True(cmds.Add("c42.throw", o => o.Execute = _ => throw new FormatException("일부러")), "예외 명령 등록 실패");
        Check.True(RegistryProbe.Delta(UiDiagnosticIds.CommandFailed, () => r = cmds.Execute("c42.throw")) == 1
            && UiDiagnosticIds.CommandFailed == "XKUI0306" && !r, "실행 예외에 XKUI0306·false 가 아님");

        Check.True(RegistryProbe.Delta(UiDiagnosticIds.CommandNotFound, () => r = cmds.Execute("c42.no.such.command")) == 1
            && UiDiagnosticIds.CommandNotFound == "XKUI0307" && !r, "없는 ID 실행에 XKUI0307·false 가 아님");
        Check.True(RegistryProbe.Runtime.Entries[RegistryProbe.Runtime.Entries.Count - 1].Severity == UiDiagnosticSeverity.Warning, "XKUI0307 이 Warning 이 아님");

        for (var i = 0; i < InvalidGestures.Length; i++)
        {
            var id = "c42.gx." + i;
            var g = InvalidGestures[i];
            Check.True(RegistryProbe.Delta(UiDiagnosticIds.CommandGestureInvalid, () => cmds.Add(id, o => { o.Gesture = g; o.Execute = Noop; })) == 1
                && UiDiagnosticIds.CommandGestureInvalid == "XKUI0305", $"무효 단축키 \"{g}\" 에 XKUI0305 1건이 아님");
            Check.True(cmds.TryGet(id, out var d) && d!.Gesture is null, $"무효 단축키 \"{g}\" 명령이 없거나 Gesture 가 남음");
        }

        for (var i = 0; i < ValidGestures.Length; i++)
        {
            var id = "c42.gv." + i;
            var g = ValidGestures[i];
            Check.True(RegistryProbe.Delta(UiDiagnosticIds.CommandGestureInvalid, () => cmds.Add(id, o => { o.Gesture = g; o.Execute = Noop; })) == 0,
                $"유효 단축키 \"{g}\" 에 XKUI0305 가 생김");
            Check.True(cmds.TryGet(id, out var d) && d!.Gesture == g, $"유효 단축키 \"{g}\" 가 보존되지 않음");
        }

        foreach (var replace in new[] { false, true })
        {
            Check.True(RegistryProbe.Delta(UiDiagnosticIds.CommandIdReserved, () => r = cmds.Add("xkxk.c42.custom", o => { o.Replace = replace; o.Execute = Noop; })) == 1
                && UiDiagnosticIds.CommandIdReserved == "XKUI0308" && !r && !cmds.Contains("xkxk.c42.custom"), $"예약 ID(Replace={replace}) 에 XKUI0308·false·미등록이 아님");
        }

        Check.True(RegistryProbe.Delta(UiDiagnosticIds.CommandRemoveDenied, () => r = cmds.Remove(ShellCommandIds.WindowTabbed)) == 1
            && UiDiagnosticIds.CommandRemoveDenied == "XKUI0309" && !r && cmds.Contains(ShellCommandIds.WindowTabbed), "셸 명령 Remove 에 XKUI0309·false·유지가 아님");
        foreach (var id in new[] { UiDiagnosticIds.CommandIdEmpty, UiDiagnosticIds.CommandDuplicate, UiDiagnosticIds.CommandExecuteMissing, UiDiagnosticIds.CommandIconMissing,
            UiDiagnosticIds.CommandGestureInvalid, UiDiagnosticIds.CommandFailed, UiDiagnosticIds.CommandIdReserved, UiDiagnosticIds.CommandRemoveDenied })
        {
            var last = RegistryProbe.Runtime.Entries;
            for (var k = last.Count - 1; k >= 0; k--)
            {
                if (last[k].Id == id)
                {
                    Check.True(last[k].Severity == UiDiagnosticSeverity.Error, $"{id} 가 Error 가 아님");
                    break;
                }
            }
        }
    }

    private static void C43()
    {
        // 모듈 A 가 아이콘 t.late 를 쓰는 명령을 먼저, 모듈 B 가 그 아이콘을 나중에 등록 — Build 끝 검사라 XKUI0304 없음. 모듈 C 는 없는 아이콘.
        var b = XkxkUi.CreateBuilder();
        b.AddModule(new RecordingModule("c43.modA", new List<string>(), c => c.Commands.Add("c43.a", o => { o.Icon = "c43.t.late"; o.Execute = Noop; })))
         .AddModule(new RecordingModule("c43.modB", new List<string>(), c => c.Icons.AddGeometry("c43.t.late", RegistryProbe.PathData)))
         .AddModule(new RecordingModule("c43.modC", new List<string>(), c => c.Commands.Add("c43.c", o => { o.Icon = "c43.no.such.icon"; o.Execute = Noop; })));
        var runtimeBefore = RegistryProbe.Count(RegistryProbe.Runtime, UiDiagnosticIds.CommandIconMissing);
        var app = b.Build();
        var built = app.Diagnostics.Entries;
        var missing = 0;
        foreach (var e in built)
        {
            if (e.Id == UiDiagnosticIds.CommandIconMissing)
            {
                missing++;
                Check.True(e.Severity == UiDiagnosticSeverity.Error, "빌더 XKUI0304 가 Error 가 아님");
            }
        }

        Check.True(missing == 1, $"빌더 XKUI0304 {missing}건(모듈 C 1건 기대 — 모듈 순서가 바뀐 참조는 진단 없음)");
        Check.True(XkxkUi.Current.Commands.Contains("c43.a") && XkxkUi.Current.Commands.Contains("c43.c"), "모듈 명령이 등록되지 않음");
        Check.True(RegistryProbe.Count(RegistryProbe.Runtime, UiDiagnosticIds.CommandIconMissing) == runtimeBefore, "Build 중 진단이 앱 진단에 쌓임");

        var tb = XkxkUi.CreateBuilder(new XkxkUiBuilderOptions { ErrorPolicy = UiErrorPolicy.Throw });
        tb.AddModule(new RecordingModule("c43.modT", new List<string>(), c => c.Commands.Add("c43.t", o => { o.Icon = "c43.no.such.icon2"; o.Execute = Noop; })));
        UiConfigurationException? caught = null;
        try
        {
            tb.Build();
        }
        catch (UiConfigurationException ex)
        {
            caught = ex;
        }

        Check.True(caught is not null, "ErrorPolicy=Throw 인데 없는 아이콘 명령으로 예외가 나지 않음");
        var inEx = false;
        foreach (var e in caught!.Diagnostics)
        {
            inEx |= e.Id == UiDiagnosticIds.CommandIconMissing;
        }

        Check.True(inEx, "예외 Diagnostics 에 XKUI0304 없음");

        // P4: Build 뒤 붙잡아 둔 모듈 레지스트리로의 등록은 실행 중 등록과 같다.
        IUiCommandRegistry? held = null;
        var db = XkxkUi.CreateBuilder();
        db.AddModule(new RecordingModule("c43.modD", new List<string>(), c => held = c.Commands));
        var dApp = db.Build();
        Check.True(held is not null, "모듈 D 가 ui.Commands 를 받지 못함");
        var builderCount = dApp.Diagnostics.Entries.Count;
        var delta = RegistryProbe.Delta(UiDiagnosticIds.CommandIconMissing, () => held!.Add("c43.d.after", o => { o.Icon = "c43.no.such.icon3"; o.Execute = Noop; }));
        Check.True(delta == 1, $"Build 뒤 등록의 XKUI0304 가 앱 진단에 {delta}건(1 기대)");
        Check.True(dApp.Diagnostics.Entries.Count == builderCount, "Build 뒤 등록 진단이 빌더 Diagnostics 에 쌓임");
        Check.True(XkxkUi.Current.Commands.TryGet("c43.d.after", out var d) && d!.Owner == "c43.modD", "Build 뒤 등록 명령의 Owner 가 모듈 D ID 가 아님");
    }
}
