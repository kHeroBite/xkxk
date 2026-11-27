// 창 레지스트리 계약 시험 C68~C73 — 등록·별칭·옛 이름 예약·진단 XKUI05xx·모듈 공유·앱 없는 Open·공개 형식 호환·옛 열거형 제거를 본다(B09)
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using xkxk.frameworks.ui.data.Layout;
using xkxk.frameworks.ui.data.Navigation;
using xkxk.frameworks.ui.shell.Framework;

namespace xkxk.frameworks.ui.test.framework.contract;

internal static class WindowRegistryChecks
{
    private const string Grid = "xkxk.demo.grid", Gantt = "xkxk.demo.gantt", Form = "xkxk.demo.form";

    /// <summary>옛 이름·숫자(영구 예약어)와 늘 풀려야 하는 창 — 시험이 따로 가진 대응표.</summary>
    private static readonly (string Name, string Id)[] Legacy = { ("Grid", Grid), ("gantt", Gantt), ("FORM", Form), ("0", Grid), ("1", Gantt), ("2", Form) };

    private const BindingFlags Declared = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    public static IEnumerable<ContractCase> All()
    {
        yield return new ContractCase("C68", "창 레지스트리 Add·Remove·Contains·TryGet·TryResolve·Items 순서·Changed·Replace, ID·별칭 이름공간·옛 이름 예약, 진단 XKUI0501~0504·0508", C68);
        yield return new ContractCase("C69", "앱·모듈 Windows 같은 저장소·셸 기본 창 3종(Owner xkxk.shell·별칭)·Build 중/뒤 진단 위치·소유", C69);
        yield return new ContractCase("C70", "앱 없음 Open: 없는 창 false+XKUI0505, 셸 없음 false+XKUI0506, configure null 은 ArgumentNullException", C70);
        yield return new ContractCase("C71", "창 공개 형식: 선택 파라미터 0·공개 생성자 0(Definition·ContentContext)·서명에 Avalonia 형식 0·기본값", C71);
        yield return new ContractCase("C72", "셸·데이터 어셈블리에 이름이 DocumentKind 인 형식 0", C72);
        yield return new ContractCase("C73", "셸 기본 grid 를 별칭 없이 Replace 해도 옛 이름·숫자가 데모 3종으로 풀림(맨 뒤 실행)", C73);
    }

    private static List<UiRegistryChangeKind> Watch(IUiWindowRegistry reg, string id, Action action) =>
        RegistryProbe.Changes(h => reg.Changed += h, h => reg.Changed -= h, id, action);

    private static bool Only(List<UiRegistryChangeKind> seen, UiRegistryChangeKind kind) => seen.Count == 1 && seen[0] == kind;

    private static int IndexOf(IUiWindowRegistry reg, string id) => reg.Items.Select(w => w.Id).ToList().IndexOf(id);

    private static string? Resolve(IUiWindowRegistry reg, string idOrAlias) => reg.TryResolve(idOrAlias, out var w) ? w.Id : null;

    /// <summary>action 동안 실행 중 진단이 id 1건만 늘고(다른 진단 0) 그 심각도가 severity 인지 본다.</summary>
    private static void Diag(string id, UiDiagnosticSeverity severity, Action action, string what)
    {
        var total = RegistryProbe.Runtime.Entries.Count;
        var delta = RegistryProbe.Delta(id, action);
        var added = RegistryProbe.Runtime.Entries.Count - total;
        Check.True(delta == 1 && added == 1, $"{what}: {id} {delta}건·전체 {added}건(각 1 기대)");
        var last = RegistryProbe.Runtime.Entries.Last(e => e.Id == id);
        Check.True(last.Severity == severity, $"{what}: {id} 심각도 {last.Severity}({severity} 기대)");
    }

    private static void C68()
    {
        var reg = XkxkUi.Current.Windows;
        Func<UiWindowContentContext, object?> content = _ => "c68";
        UiWindowOptions? held = null;
        var ok = false;
        var changes = Watch(reg, "c68.a", () => ok = reg.Add("c68.a", o =>
        {
            held = o;
            (o.TitleKey, o.Title, o.Icon, o.Category, o.DescriptionKey) = ("c68.title", "C68A", MenuIconCatalog.GridIconKey, "c68cat", "c68.desc");
            (o.Placement, o.AllowDuplicate, o.CreateContent) = (UiWindowPlacement.Sdi, true, content);
            o.Aliases.Add("c68.Alias");
        }));
        Check.True(ok && Only(changes, UiRegistryChangeKind.Added), "첫 Add 가 true·Added 1건이 아님");
        Check.True(reg.TryGet("c68.a", out var a) && a is not null, "TryGet(c68.a) 실패");
        Check.True(a!.Id == "c68.a" && a.TitleKey == "c68.title" && a.Title == "C68A" && a.Icon == MenuIconCatalog.GridIconKey && a.Category == "c68cat"
            && a.DescriptionKey == "c68.desc" && a.Placement == UiWindowPlacement.Sdi && a.AllowDuplicate && ReferenceEquals(a.CreateContent, content)
            && a.Owner == UiRegistryOwners.App && a.Aliases.SequenceEqual(new[] { "c68.Alias" }), "정의 필드 불일치");
        held!.Title = "바뀜";
        held.Aliases.Add("c68.late");
        Check.True(a.Title == "C68A" && a.Aliases.Count == 1 && Resolve(reg, "c68.late") is null, "등록 뒤 옵션 변경이 정의에 비침(복사본 아님)");
        Check.True(a.Aliases is not ICollection<string> col || col.IsReadOnly, "정의 Aliases 가 읽기 전용이 아님");

        Check.True(reg.Contains("c68.a") && !reg.Contains("C68.A") && !reg.TryGet("C68.A", out _) && !reg.TryGet("c68.alias", out _), "Contains·TryGet 은 ID(Ordinal)만 봐야 함");
        Check.True(Resolve(reg, "c68.a") == "c68.a" && Resolve(reg, "c68.alias") == "c68.a" && Resolve(reg, "C68.ALIAS") == "c68.a", "TryResolve ID·별칭(대소문자 무시) 실패");
        Check.True(Resolve(reg, "C68.A") is null && Resolve(reg, "c68.none") is null, "TryResolve 가 대소문자 다른 ID·없는 ID 를 찾음");

        Check.True(reg.Add("c68.b", o => o.Title = "B1") && reg.Add("c68.c", o => o.Title = "C1"), "c68.b·c 등록 실패");
        var at = IndexOf(reg, "c68.b");
        Check.True(IndexOf(reg, "c68.a") >= 0 && IndexOf(reg, "c68.a") < at && at < IndexOf(reg, "c68.c"), "Items 가 등록 순서가 아님");
        changes = Watch(reg, "c68.b", () => ok = reg.Add("c68.b", o => { o.Title = "B2"; o.Replace = true; o.Aliases.Add("c68.second"); }));
        Check.True(ok && Only(changes, UiRegistryChangeKind.Replaced) && IndexOf(reg, "c68.b") == at, "Replace 가 true·Replaced·제자리(P3)가 아님");
        Check.True(reg.TryGet("c68.b", out var b) && b!.Title == "B2", "교체 정의가 보이지 않음");
        Check.True(RegistryProbe.Delta(UiDiagnosticIds.WindowAliasConflict, () => reg.Add("c68.b", o => { o.Title = "B3"; o.Replace = true; o.Aliases.Add("c68.second"); })) == 0
            && Resolve(reg, "c68.second") == "c68.b", "자기 별칭을 유지한 교체가 별칭 충돌로 판정됨");
        changes = Watch(reg, "c68.new", () => ok = reg.Add("c68.new", o => o.Replace = true));
        Check.True(ok && Only(changes, UiRegistryChangeKind.Added), "새 ID Replace=true 가 Added 가 아님(P1)");

        changes = Watch(reg, "c68.a", () => ok = reg.Remove("c68.a"));
        Check.True(ok && Only(changes, UiRegistryChangeKind.Removed) && !reg.Contains("c68.a") && Resolve(reg, "c68.alias") is null, "자기 창 Remove 실패·Removed 아님·별칭 잔존");
        Check.True(reg.Add("c68.a", o => o.Title = "again") && reg.Items[reg.Items.Count - 1].Id == "c68.a", "Remove 뒤 재등록이 Items 맨 뒤가 아님");
        Check.True(RegistryProbe.Delta(UiDiagnosticIds.WindowRemoveDenied, () => ok = reg.Remove("c68.nothing")) == 0 && !ok, "없는 창 Remove 가 false·진단 없음이 아님");

        Check.True(UiDiagnosticIds.WindowIdInvalid == "XKUI0501" && UiDiagnosticIds.WindowDuplicate == "XKUI0502" && UiDiagnosticIds.WindowReservedId == "XKUI0503"
            && UiDiagnosticIds.WindowAliasConflict == "XKUI0504" && UiDiagnosticIds.WindowNotRegistered == "XKUI0505" && UiDiagnosticIds.WindowShellUnavailable == "XKUI0506"
            && UiDiagnosticIds.WindowContentFailed == "XKUI0507" && UiDiagnosticIds.WindowRemoveDenied == "XKUI0508", "창 진단 번호 상수 불일치");
        foreach (var bad in new[] { "", "   " })
        {
            Diag(UiDiagnosticIds.WindowIdInvalid, UiDiagnosticSeverity.Error, () => ok = reg.Add(bad, _ => { }), "빈·공백 ID");
            Check.True(!ok && !reg.Contains(bad), "빈·공백 ID 가 등록됨");
        }

        Diag(UiDiagnosticIds.WindowDuplicate, UiDiagnosticSeverity.Error, () => ok = reg.Add("c68.c", o => o.Title = "C2"), "중복 ID");
        Check.True(!ok && reg.TryGet("c68.c", out var c) && c!.Title == "C1", "중복 뒤 첫 등록 유지·false 가 아님");
        foreach (var replace in new[] { false, true })
        {
            Diag(UiDiagnosticIds.WindowReservedId, UiDiagnosticSeverity.Error, () => ok = reg.Add("xkxk.c68.custom", o => o.Replace = replace), $"예약 ID(Replace={replace})");
            Check.True(!ok && !reg.Contains("xkxk.c68.custom"), "예약 ID 가 등록됨");
        }

        Diag(UiDiagnosticIds.WindowReservedId, UiDiagnosticSeverity.Error, () => reg.Add("c68.resalias", o => o.Aliases.Add("xkxk.c68.alias")), "예약 별칭");
        Check.True(Resolve(reg, "xkxk.c68.alias") is null, "예약 별칭이 해석됨");
        foreach (var (id, alias, owner) in new[] { ("c68.d", "C68.SECOND", "c68.b"), ("c68.e", "c68.c", "c68.c") })
        {
            Diag(UiDiagnosticIds.WindowAliasConflict, UiDiagnosticSeverity.Warning,
                () => ok = reg.Add(id, o => { o.Aliases.Add(alias); o.Aliases.Add(id + ".free"); }), $"별칭 충돌 {alias}");
            Check.True(ok && reg.TryGet(id, out var d) && d!.Aliases.SequenceEqual(new[] { id + ".free" }), $"{id}: 충돌 별칭만 버리고 등록해야 함");
            Check.True(Resolve(reg, alias) == owner && Resolve(reg, id + ".free") == id, $"{alias} 가 {owner} 로 해석되지 않음");
        }

        Diag(UiDiagnosticIds.WindowRemoveDenied, UiDiagnosticSeverity.Error, () => ok = reg.Remove(Grid), "셸 창 Remove");
        Check.True(!ok && reg.Contains(Grid), "다른 주체 창이 지워짐(P2)");
        Check.Throws<ArgumentNullException>(() => reg.Add(null!, _ => { }), "Add(null, configure)");
        Check.Throws<ArgumentNullException>(() => reg.Add("c68.nullcfg", null!), "Add(id, null)");

        // R1·D-V2: ID·별칭은 한 이름공간, 옛 이름·숫자는 예약어. 거부 진단은 하나만(새 xkxk.* 는 충돌해도 0503, 옛 이름은 충돌해도 0502).
        var count = reg.Items.Count;
        var refused = new[] { "C68.SECOND", "c68.second", "C68.C", "xkxk.demo.GRID" }.Concat(Legacy.Select(l => l.Name));
        foreach (var (id, replace) in refused.SelectMany(i => new[] { (i, false), (i, true) }))
        {
            var code = id == "xkxk.demo.GRID" ? UiDiagnosticIds.WindowReservedId : UiDiagnosticIds.WindowDuplicate;
            Diag(code, UiDiagnosticSeverity.Error, () => ok = reg.Add(id, o => o.Replace = replace), $"거부 ID {id}(Replace={replace})");
            Check.True(!ok && !reg.Contains(id), $"{id}(Replace={replace}): 거부돼야 할 ID 가 등록됨");
        }

        Check.True(reg.Items.Count == count && Resolve(reg, "c68.second") == "c68.b" && Resolve(reg, "c68.c") == "c68.c", "ID 거부 뒤 기존 창이 바뀜");
        foreach (var (name, target) in Legacy)
        {
            var id = "c68.legacy" + name;
            Diag(UiDiagnosticIds.WindowReservedId, UiDiagnosticSeverity.Error, () => ok = reg.Add(id, o => { o.Aliases.Add(name); o.Aliases.Add(id + ".free"); }), $"옛 이름 별칭 {name}");
            Check.True(ok && reg.TryGet(id, out var d) && d!.Aliases.SequenceEqual(new[] { id + ".free" }) && Resolve(reg, name) == target, $"{name}: 그 별칭만 버리고 등록·{target} 해석이 아님");
        }

        var module = XkxkUi.CreateBuilder();
        module.AddModule(new RecordingModule("xkxk.c68.mod", new List<string>(), m => ok = m.Windows.Add("Grid", o => o.Replace = true)));
        Check.True(RegistryProbe.Count(module.Build().Diagnostics, UiDiagnosticIds.WindowDuplicate) == 1 && !ok && !reg.Contains("Grid"), "xkxk. 모듈의 옛 이름 ID 가 거부되지 않음");
    }

    private static void C69()
    {
        var reg = XkxkUi.Current.Windows;
        Check.True(reg is not null, "XkxkUi.Current.Windows 가 null");
        var shell = new (string Id, string Alias, string TitleKey, string Icon, string? Help)[]
        {
            (Grid, "Grid", "shell.kind.grid", MenuIconCatalog.GridIconKey, "chrome.help.grid"),
            ("xkxk.demo.gantt", "Gantt", "shell.kind.gantt", MenuIconCatalog.GanttIconKey, "chrome.help.gantt"),
            ("xkxk.demo.form", "Form", "shell.kind.form", MenuIconCatalog.FormIconKey, null),
        };
        foreach (var s in shell)
        {
            Check.True(reg!.TryGet(s.Id, out var w) && w is not null, $"셸 기본 창 {s.Id} 미등록");
            Check.True(w!.Owner == UiRegistryOwners.Shell && w.Aliases.Contains(s.Alias) && w.TitleKey == s.TitleKey && XkxkUi.Current.Localization.HasKey(s.TitleKey)
                && w.Icon == s.Icon && w.DescriptionKey == s.Help && !w.AllowDuplicate && w.CreateContent is null && w.Placement == UiWindowPlacement.Default,
                $"{s.Id} 정의 불일치(Owner={w.Owner})");
            Check.True(Resolve(reg, s.Alias.ToLowerInvariant()) == s.Id && Resolve(reg, s.Alias.ToUpperInvariant()) == s.Id, $"별칭 {s.Alias} 해석 실패");
        }

        Check.True(reg!.Items.Take(3).Select(w => w.Id).OrderBy(i => i, StringComparer.Ordinal)
            .SequenceEqual(shell.Select(s => s.Id).OrderBy(i => i, StringComparer.Ordinal)), "Items 앞 3개가 셸 기본 창이 아님");

        IUiWindowRegistry? module = null;
        var builder = XkxkUi.CreateBuilder();
        builder.AddModule(new RecordingModule("c69.mod", new List<string>(), c =>
        {
            module = c.Windows;
            c.Windows.Add("c69.mod.win", o => o.Title = "M");
            c.Windows.Add("", _ => { });
        }));
        var runtimeBefore = RegistryProbe.Count(RegistryProbe.Runtime, UiDiagnosticIds.WindowIdInvalid);
        var app = builder.Build();
        Check.True(RegistryProbe.Count(app.Diagnostics, UiDiagnosticIds.WindowIdInvalid) == 1
            && RegistryProbe.Count(RegistryProbe.Runtime, UiDiagnosticIds.WindowIdInvalid) == runtimeBefore, "Build 중 진단은 빌더 Diagnostics 에만 가야 함");
        Check.True(reg.TryGet("c69.mod.win", out var m) && m!.Owner == "c69.mod" && module!.Contains(Grid), "모듈 등록이 앱 저장소에 보이지 않거나 Owner 오류");
        Check.True(reg.Add("c69.app", _ => { }) && module!.TryGet("c69.app", out var p) && p!.Owner == UiRegistryOwners.App, "앱 등록이 모듈 보기에 보이지 않음");

        var builderCount = app.Diagnostics.Entries.Count;
        Diag(UiDiagnosticIds.WindowIdInvalid, UiDiagnosticSeverity.Error, () => module!.Add("", _ => { }), "Build 뒤 모듈 등록(P4)");
        Check.True(app.Diagnostics.Entries.Count == builderCount, "Build 뒤 진단이 빌더 Diagnostics 에 쌓임");
        Check.True(module!.Add("c69.mod.late", _ => { }) && reg.TryGet("c69.mod.late", out var l) && l!.Owner == "c69.mod", "Build 뒤 등록 Owner 가 모듈 ID 가 아님");
        var ok = true;
        Diag(UiDiagnosticIds.WindowRemoveDenied, UiDiagnosticSeverity.Error, () => ok = reg.Remove("c69.mod.win"), "앱이 모듈 창 Remove");
        Check.True(!ok && module!.Remove("c69.mod.win") && !reg.Contains("c69.mod.win"), "소유 모듈만 지울 수 있어야 함");
        Diag(UiDiagnosticIds.WindowRemoveDenied, UiDiagnosticSeverity.Error, () => ok = module!.Remove("c69.app"), "모듈이 앱 창 Remove");
        Check.True(!ok && reg.Add("c69.mod.late", o => o.Replace = true) && reg.TryGet("c69.mod.late", out var r) && r!.Owner == UiRegistryOwners.App, "교체 뒤 Owner 가 바뀌지 않음");
    }

    private static void C70()
    {
        var reg = XkxkUi.Current.Windows;
        Check.True(XkxkUi.Current.Shell is null, "콘솔 시험에서 Shell 이 null 이 아님(앱 없음 전제 깨짐)");
        var ok = true;
        Diag(UiDiagnosticIds.WindowNotRegistered, UiDiagnosticSeverity.Warning, () => ok = reg.Open("c70.none"), "없는 창 Open");
        Check.True(!ok, "없는 창 Open 이 true");
        ok = true;
        Diag(UiDiagnosticIds.WindowNotRegistered, UiDiagnosticSeverity.Warning, () => ok = reg.Open("c70.none", o => o.Parameter = 1), "없는 창 Open(configure)");
        Check.True(!ok, "없는 창 Open(configure) 이 true");
        foreach (var id in new[] { Grid, "Grid" })
        {
            ok = true;
            Diag(UiDiagnosticIds.WindowShellUnavailable, UiDiagnosticSeverity.Warning, () => ok = reg.Open(id), $"셸 없음 Open({id})");
            Check.True(!ok, $"셸 없는 Open({id}) 이 true");
        }

        ok = true;
        Diag(UiDiagnosticIds.WindowShellUnavailable, UiDiagnosticSeverity.Warning, () => ok = reg.Open(Grid, o => o.Title = "C70"), "셸 없음 Open(configure)");
        Check.True(!ok, "셸 없는 Open(configure) 이 true");
        Check.Throws<ArgumentNullException>(() => reg.Open(Grid, null!), "Open(id, null)");
    }

    private static void C71()
    {
        var shell = typeof(XkxkUi).Assembly;
        var types = shell.GetExportedTypes().Where(t => t.Name == "IUiWindowRegistry" || t.Name.StartsWith("UiWindow", StringComparison.Ordinal)).ToList();
        foreach (var t in new[] { typeof(IUiWindowRegistry), typeof(UiWindowOptions), typeof(UiWindowDefinition), typeof(UiWindowPlacement), typeof(UiWindowOpenOptions), typeof(UiWindowContentContext) })
        {
            Check.True(types.Contains(t) && t.Namespace == typeof(XkxkUi).Namespace, $"{t.Name} 이 Framework 공개 형식이 아님");
        }

        // 양성 대조 — 검출기가 선택 파라미터·Avalonia 형식(직접·제네릭 인자·배열)을 실제로 잡는지 본다.
        Check.True(typeof(RecordingModule).GetConstructors().SelectMany(c => c.GetParameters()).Count(IsOptionalParameter) == 1, "선택 파라미터 검출기가 죽어 있음");
        Check.True(UsesAvalonia(typeof(Avalonia.Rect)) && UsesAvalonia(typeof(Func<Avalonia.Rect, object>)) && UsesAvalonia(typeof(Avalonia.Rect[])) && !UsesAvalonia(typeof(List<string>)), "Avalonia 검출기가 죽어 있음");

        var optional = new List<string>();
        var leaks = new List<string>();
        var checkedCount = 0;
        foreach (var t in types)
        {
            foreach (var m in t.GetMethods(Declared).Cast<MethodBase>().Concat(t.GetConstructors(Declared)))
            {
                checkedCount++;
                var ps = m.GetParameters();
                optional.AddRange(ps.Where(IsOptionalParameter).Select(p => $"{t.Name}.{m.Name}({p.Name})"));
                if (ps.Any(p => UsesAvalonia(p.ParameterType)) || (m is MethodInfo mi && UsesAvalonia(mi.ReturnType)))
                {
                    leaks.Add($"{t.Name}.{m.Name}");
                }
            }

            leaks.AddRange(t.GetProperties(Declared).Where(p => UsesAvalonia(p.PropertyType)).Select(p => $"{t.Name}.{p.Name}"));
            leaks.AddRange(t.GetEvents(Declared).Where(e => UsesAvalonia(e.EventHandlerType!)).Select(e => $"{t.Name}.{e.Name}"));
            leaks.AddRange(t.GetFields(Declared).Where(f => UsesAvalonia(f.FieldType)).Select(f => $"{t.Name}.{f.Name}"));
        }

        Check.True(checkedCount > 0, "검사한 공개 메서드·생성자가 0개");
        Check.True(optional.Count == 0, $"선택 파라미터 {optional.Count}건: {string.Join("; ", optional)}");
        Check.True(leaks.Count == 0, $"공개 서명의 Avalonia 형식 {leaks.Count}건: {string.Join("; ", leaks)}");
        foreach (var t in new[] { typeof(UiWindowDefinition), typeof(UiWindowContentContext) })
        {
            Check.True(t.GetConstructors(BindingFlags.Public | BindingFlags.Instance).Length == 0, $"{t.Name} 공개 생성자가 있음");
            Check.True(t.GetProperties().Length > 0 && t.GetProperties().All(p => p.SetMethod is not { IsPublic: true }), $"{t.Name} 에 공개 setter 가 있음");
        }

        var o = new UiWindowOptions();
        Check.True(o.Placement == UiWindowPlacement.Default && !o.AllowDuplicate && !o.Replace && o.Aliases.Count == 0 && o.CreateContent is null && o.Title is null && o.TitleKey is null, "UiWindowOptions 기본값 불일치");
        var open = new UiWindowOpenOptions();
        Check.True(open.Placement == UiWindowPlacement.Default && open.AllowDuplicate is null && open.Title is null && open.Parameter is null, "UiWindowOpenOptions 기본값 불일치");
        Check.True((int)UiWindowPlacement.Default == 0 && (int)UiWindowPlacement.Sdi == 1, "UiWindowPlacement 값 불일치");
        Check.True(typeof(IUiAppContext).GetProperty("Windows")?.PropertyType == typeof(IUiWindowRegistry)
            && typeof(IUiModuleContext).GetProperty("Windows")?.PropertyType == typeof(IUiWindowRegistry), "IUiAppContext·IUiModuleContext.Windows 형식 불일치");
    }

    private static bool IsOptionalParameter(ParameterInfo p) => p.IsOptional || p.HasDefaultValue;

    private static bool UsesAvalonia(Type t) =>
        (t.Assembly.GetName().Name?.StartsWith("Avalonia", StringComparison.Ordinal) ?? false)
        || (t.HasElementType && UsesAvalonia(t.GetElementType()!))
        || (t.IsGenericType && t.GetGenericArguments().Any(UsesAvalonia));

    private static void C72()
    {
        var shell = typeof(XkxkUi).Assembly;
        var data = typeof(PanelTabsSnapshot).Assembly;
        Check.True(shell != data, "셸과 데이터 어셈블리가 같게 잡힘");
        foreach (var assembly in new[] { shell, data })
        {
            var all = assembly.GetTypes();
            Check.True(all.Length > 20, $"{assembly.GetName().Name} 형식 {all.Length}개 — 검사 대상이 비어 공허한 PASS 위험");
            var found = all.Where(t => t.Name == "DocumentKind").Select(t => t.FullName).ToList();
            Check.True(found.Count == 0, $"{assembly.GetName().Name} 에 DocumentKind 형식이 남음: {string.Join(", ", found)}");
        }
    }

    private static void C73()
    {
        var reg = XkxkUi.Current.Windows;
        Check.True(reg.Add(Grid, o => { o.Title = "C73"; o.Replace = true; }) && reg.TryGet(Grid, out var g) && g!.Title == "C73" && g.Aliases.Count == 0, "셸 기본 grid 의 별칭 없는 교체 실패");
        var wrong = Legacy.Where(l => Resolve(reg, l.Name) != l.Id).Select(l => $"{l.Name}→{Resolve(reg, l.Name) ?? "(없음)"}").ToList();
        Check.True(wrong.Count == 0, $"교체 뒤 옛 이름·숫자 해석 불일치: {string.Join(", ", wrong)}");
    }
}
