// 아이콘 레지스트리 계약 시험 C33~C38 — 컨텍스트 노출·생성 공급자·등록/제거·규칙 진단·재정의·공급자 우선순위를 본다(UADR-0013)
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using xkxk.frameworks.ui.data.Navigation;
using xkxk.frameworks.ui.shell.Framework;

namespace xkxk.frameworks.ui.test.framework.contract;

/// <summary>레지스트리 시험 공용 도우미 — 진단 개수 차, 변경 알림 기록.</summary>
internal static class RegistryProbe
{
    /// <summary>24 격자 기준의 단순 삼각형 경로.</summary>
    public const string PathData = "M2,2 L22,2 L22,22 Z";

    /// <summary>앱 수준(실행 중) 진단 모음.</summary>
    public static UiDiagnostics Runtime => XkxkUi.Current.Diagnostics;

    /// <summary>진단 모음에서 해당 ID 항목 수.</summary>
    public static int Count(UiDiagnostics diagnostics, string id) => diagnostics.Entries.Count(e => e.Id == id);

    /// <summary>진단 모음의 Error 항목 수.</summary>
    public static int Errors(UiDiagnostics diagnostics) => diagnostics.Entries.Count(e => e.Severity == UiDiagnosticSeverity.Error);

    /// <summary>action 동안 해당 ID 진단이 늘어난 수(실행 중 진단).</summary>
    public static int Delta(string id, Action action)
    {
        var before = Count(Runtime, id);
        action();
        return Count(Runtime, id) - before;
    }

    /// <summary>action 동안 받은 변경 알림 중 id 에 해당하는 것만 돌려준다.</summary>
    public static List<UiRegistryChangeKind> Changes(Action<EventHandler<UiRegistryChangedEventArgs>> subscribe,
        Action<EventHandler<UiRegistryChangedEventArgs>> unsubscribe, string id, Action action)
    {
        var seen = new List<UiRegistryChangeKind>();
        void Handler(object? sender, UiRegistryChangedEventArgs e)
        {
            if (e.Id == id)
            {
                seen.Add(e.Kind);
            }
        }

        subscribe(Handler);
        try
        {
            action();
        }
        finally
        {
            unsubscribe(Handler);
        }

        return seen;
    }
}

/// <summary>시험 대역 아이콘 공급자 — 정해 둔 키를 PathData 정의로 돌려준다.</summary>
internal sealed class FakeIconProvider : UiIconProvider
{
    private readonly string _id;
    private readonly string[] _keys;

    public FakeIconProvider(string id, params string[] keys)
    {
        _id = id;
        _keys = keys;
    }

    public override string Id => _id;

    public override IReadOnlyCollection<string> Keys => _keys;

    public override bool TryGetIcon(string key, [NotNullWhen(true)] out UiIconDefinition? icon)
    {
        icon = Array.IndexOf(_keys, key) >= 0 ? UiIconDefinition.FromPathData(key, RegistryProbe.PathData) : null;
        return icon is not null;
    }
}

internal static class IconRegistryChecks
{
    public static IEnumerable<ContractCase> All()
    {
        yield return new ContractCase("C33", "XkxkUi.Current.Icons·Commands·Diagnostics 비 null·같은 인스턴스, 모듈 Configure 의 ui.Icons·ui.Commands 비 null", C33);
        yield return new ContractCase("C34", "기본 생성 공급자 xkxk.generated 가 Providers 맨 끝·키 수 = 생성 상수 계산값·대표 키 해석(Resource·Owner)", C34);
        yield return new ContractCase("C35", "AddGeometry·Remove·Changed(Added/Removed)·Owner(app/모듈), 다른 주체 Remove 거부 XKUI0207(P2), 새 키 Replace=true → Added(P1)", C35);
        yield return new ContractCase("C36", "아이콘 규칙 진단 XKUI0201·0202(첫 값 유지)·0203(출처 0·2개), null configure 예외", C36);
        yield return new ContractCase("C37", "생성 키 재정의(진단 없음)·Remove 시 생성 값 복귀·명시 키 Replace=true → Replaced", C37);
        yield return new ContractCase("C38", "공급자 우선순위(명시>새>옛>생성)·Id 중복 XKUI0205·예약 Id XKUI0206·생성 공급자 제거 거부 XKUI0207·자기 공급자 제거", C38);
    }

    private static void C33()
    {
        var ctx = XkxkUi.Current;
        Check.True(ctx.Icons is not null && ctx.Commands is not null && ctx.Diagnostics is not null, "Icons·Commands·Diagnostics 중 null 이 있음");
        Check.True(ReferenceEquals(ctx.Icons, XkxkUi.Current.Icons), "Icons 가 부를 때마다 다른 인스턴스");
        Check.True(ReferenceEquals(ctx.Commands, XkxkUi.Current.Commands), "Commands 가 부를 때마다 다른 인스턴스");
        Check.True(ReferenceEquals(ctx.Diagnostics, XkxkUi.Current.Diagnostics), "Diagnostics 가 부를 때마다 다른 인스턴스");

        IUiIconRegistry? icons = null;
        IUiCommandRegistry? commands = null;
        XkxkUi.CreateBuilder().AddModule(new RecordingModule("c33.module", new List<string>(), c =>
        {
            icons = c.Icons;
            commands = c.Commands;
        })).Build();
        Check.True(icons is not null && commands is not null, "모듈 Configure 안의 ui.Icons·ui.Commands 가 null");
    }

    private static void C34()
    {
        var icons = XkxkUi.Current.Icons;
        var providers = icons.Providers;
        Check.True(providers.Count >= 1 && providers[providers.Count - 1].Id == UiRegistryOwners.Generated
            && UiRegistryOwners.Generated == "xkxk.generated", "Providers 맨 끝이 xkxk.generated 가 아님");

        var expected = new HashSet<string>(StringComparer.Ordinal);
        expected.UnionWith(CmdIconKeys.All);
        expected.UnionWith(MenuIconCatalog.IconKeyByMenuId.Values);
        expected.UnionWith(MenuIconCatalog.ShellIconNames.Select(n => MenuIconCatalog.ShellIconKeyPrefix + n));
        expected.Add(MenuIconCatalog.BusyStaticKey);
        expected.Add(MenuIconCatalog.BadgeSlowKey);
        expected.Add(MenuIconCatalog.BadgeErrorKey);
        var generated = providers[providers.Count - 1];
        var actual = new HashSet<string>(generated.Keys, StringComparer.Ordinal);
        Check.True(actual.SetEquals(expected), $"생성 공급자 키 {actual.Count}개 ≠ 생성 상수 계산 {expected.Count}개");

        foreach (var key in new[] { CmdIconKeys.Exit, MenuIconCatalog.MenuIconKeyPrefix + "1009", MenuIconCatalog.DefaultDocumentIconKey, MenuIconCatalog.BusyStaticKey })
        {
            Check.True(icons.Contains(key), $"Contains({key}) 가 false");
            Check.True(icons.TryGet(key, out var def) && def is not null, $"TryGet({key}) 실패");
            Check.True(def!.Kind == UiIconKind.Resource && def.ResourceKey == key && def.Key == key, $"{key} 정의가 Resource·같은 키가 아님");
            Check.True(def.Owner == UiRegistryOwners.Generated, $"{key} Owner={def.Owner}");
        }

        Check.True(icons.Keys.Count >= expected.Count, $"Keys 수 {icons.Keys.Count} 가 생성 키 수보다 적음");
    }

    private static void C35()
    {
        var icons = XkxkUi.Current.Icons;
        const string key = "c35.app";
        var added = false;
        var changes = RegistryProbe.Changes(h => icons.Changed += h, h => icons.Changed -= h, key, () => added = icons.AddGeometry(key, RegistryProbe.PathData));
        Check.True(added && icons.Contains(key), "AddGeometry(새 키) 실패");
        Check.True(icons.TryGet(key, out var def) && def!.Kind == UiIconKind.PathData && def.PathData == RegistryProbe.PathData, "TryGet 이 PathData 정의가 아님");
        Check.True(def.Owner == UiRegistryOwners.App && UiRegistryOwners.App == "app", $"XkxkUi.Current 경유 Owner={def.Owner}");
        Check.True(changes.Count == 1 && changes[0] == UiRegistryChangeKind.Added, $"Added 알림 {changes.Count}건");
        var removed = false;
        changes = RegistryProbe.Changes(h => icons.Changed += h, h => icons.Changed -= h, key, () => removed = icons.Remove(key));
        Check.True(removed && !icons.Contains(key), "Remove 뒤에도 Contains true");
        Check.True(changes.Count == 1 && changes[0] == UiRegistryChangeKind.Removed, $"Removed 알림 {changes.Count}건");

        XkxkUi.CreateBuilder().AddModule(new RecordingModule("c35.module", new List<string>(), c => c.Icons.AddGeometry("c35.mod", RegistryProbe.PathData))).Build();
        Check.True(icons.TryGet("c35.mod", out var mod) && mod!.Owner == "c35.module", "모듈 등록 아이콘 Owner 가 모듈 ID 가 아님");
        var denied = true;
        Check.True(RegistryProbe.Delta(UiDiagnosticIds.IconRemoveDenied, () => denied = icons.Remove("c35.mod")) == 1
            && UiDiagnosticIds.IconRemoveDenied == "XKUI0207", "다른 주체 아이콘 Remove 에 XKUI0207 1건이 아님");
        Check.True(!denied && icons.Contains("c35.mod"), "다른 주체 아이콘이 지워짐(P2 위반)");

        const string fresh = "c35.replace.new";
        var ok = false;
        changes = RegistryProbe.Changes(h => icons.Changed += h, h => icons.Changed -= h, fresh,
            () => ok = icons.Add(fresh, o => { o.PathData = RegistryProbe.PathData; o.Replace = true; }));
        Check.True(ok && icons.Contains(fresh), "새 키 Replace=true 등록 실패(P1)");
        Check.True(changes.Count == 1 && changes[0] == UiRegistryChangeKind.Added, "새 키 Replace=true 가 Added 가 아님(P1)");
    }

    private static void C36()
    {
        var icons = XkxkUi.Current.Icons;
        var r = true;
        Check.True(RegistryProbe.Delta(UiDiagnosticIds.IconKeyEmpty, () => r = icons.AddGeometry(" ", RegistryProbe.PathData)) == 1
            && UiDiagnosticIds.IconKeyEmpty == "XKUI0201" && !r, "빈 키에 XKUI0201·false 가 아님");

        Check.True(icons.AddGeometry("c36.dup", RegistryProbe.PathData), "첫 등록 실패");
        Check.True(RegistryProbe.Delta(UiDiagnosticIds.IconDuplicate, () => r = icons.AddGeometry("c36.dup", "M0,0 L24,24")) == 1
            && UiDiagnosticIds.IconDuplicate == "XKUI0202" && !r, "명시 중복에 XKUI0202·false 가 아님");
        Check.True(icons.TryGet("c36.dup", out var first) && first!.PathData == RegistryProbe.PathData, "중복 뒤 첫 값이 유지되지 않음");

        Check.True(RegistryProbe.Delta(UiDiagnosticIds.IconSourceInvalid, () => r = icons.Add("c36.none", o => o.Description = "없음")) == 1
            && UiDiagnosticIds.IconSourceInvalid == "XKUI0203" && !r && !icons.Contains("c36.none"), "출처 0개에 XKUI0203·false 가 아님");
        Check.True(RegistryProbe.Delta(UiDiagnosticIds.IconSourceInvalid, () => r = icons.Add("c36.both", o => { o.PathData = RegistryProbe.PathData; o.ResourceKey = "X"; })) == 1
            && !r && !icons.Contains("c36.both"), "출처 2개에 XKUI0203·false 가 아님");
        foreach (var id in new[] { UiDiagnosticIds.IconKeyEmpty, UiDiagnosticIds.IconDuplicate, UiDiagnosticIds.IconSourceInvalid })
        {
            Check.True(RegistryProbe.Runtime.Entries.Last(e => e.Id == id).Severity == UiDiagnosticSeverity.Error, $"{id} 가 Error 가 아님");
        }

        Check.Throws<ArgumentNullException>(() => icons.Add("c36.null", null!), "Add(key, null)");
        Check.Throws<ArgumentNullException>(() => icons.Add(null!, o => o.PathData = RegistryProbe.PathData), "Add(null, configure)");
    }

    private static void C37()
    {
        var icons = XkxkUi.Current.Icons;
        var key = CmdIconKeys.About;
        var before = RegistryProbe.Runtime.Entries.Count;
        Check.True(icons.AddGeometry(key, RegistryProbe.PathData), "생성 키 재정의 Add 실패");
        Check.True(RegistryProbe.Runtime.Entries.Count == before, "생성 키 재정의에 진단이 생김");
        Check.True(icons.TryGet(key, out var mine) && mine!.Kind == UiIconKind.PathData, "재정의 뒤 TryGet 이 명시 값이 아님");
        Check.True(icons.Remove(key), "재정의 Remove 실패");
        Check.True(icons.TryGet(key, out var gen) && gen!.Kind == UiIconKind.Resource && gen.Owner == UiRegistryOwners.Generated, "Remove 뒤 생성 값이 돌아오지 않음");

        Check.True(icons.AddGeometry("c37.k", RegistryProbe.PathData), "c37.k 등록 실패");
        before = RegistryProbe.Runtime.Entries.Count;
        var ok = false;
        var changes = RegistryProbe.Changes(h => icons.Changed += h, h => icons.Changed -= h, "c37.k",
            () => ok = icons.Add("c37.k", o => { o.PathData = "M0,0 L24,24"; o.Replace = true; }));
        Check.True(ok && RegistryProbe.Runtime.Entries.Count == before, "Replace=true 재등록 실패 또는 진단 생김");
        Check.True(changes.Count == 1 && changes[0] == UiRegistryChangeKind.Replaced, "Replace=true 재등록이 Replaced 가 아님");
        Check.True(icons.TryGet("c37.k", out var rep) && rep!.PathData == "M0,0 L24,24", "교체 값이 보이지 않음");
    }

    private static void C38()
    {
        var icons = XkxkUi.Current.Icons;
        var oldP = new FakeIconProvider("c38.old", "c38.shared", "c38.oldonly", CmdIconKeys.Exit);
        var newP = new FakeIconProvider("c38.new", "c38.shared", "c38.newonly");
        Check.True(icons.AddProvider(oldP) && icons.AddProvider(newP), "AddProvider 실패");
        Check.True(ReferenceEquals(icons.Providers[0], newP), "새 공급자가 Providers[0] 이 아님");
        Check.True(icons.TryGet("c38.newonly", out var n) && n!.Owner == "c38.new", "새 공급자 키 해석·Owner 오류");
        Check.True(icons.TryGet("c38.shared", out var s) && s!.Owner == "c38.new", "새 공급자가 옛 공급자보다 우선하지 않음");
        Check.True(icons.TryGet(CmdIconKeys.Exit, out var e) && e!.Owner == "c38.old", "옛 공급자가 생성 공급자보다 우선하지 않음");
        Check.True(icons.AddGeometry("c38.shared", RegistryProbe.PathData) && icons.TryGet("c38.shared", out var x) && x!.Owner == UiRegistryOwners.App, "명시 등록이 공급자보다 우선하지 않음");

        var r = true;
        Check.True(RegistryProbe.Delta(UiDiagnosticIds.IconProviderRejected, () => r = icons.AddProvider(new FakeIconProvider("c38.new", "c38.z"))) == 1
            && UiDiagnosticIds.IconProviderRejected == "XKUI0205" && !r, "같은 Id 공급자에 XKUI0205·false 가 아님");
        Check.True(RegistryProbe.Delta(UiDiagnosticIds.IconProviderIdReserved, () => r = icons.AddProvider(new FakeIconProvider("xkxk.c38", "c38.r"))) == 1
            && UiDiagnosticIds.IconProviderIdReserved == "XKUI0206" && !r && !icons.Contains("c38.r"), "예약 Id 공급자에 XKUI0206·false 가 아님");
        Check.True(RegistryProbe.Delta(UiDiagnosticIds.IconRemoveDenied, () => r = icons.RemoveProvider(UiRegistryOwners.Generated)) == 1
            && !r && icons.Contains(CmdIconKeys.About), "생성 공급자 제거가 거부되지 않음(XKUI0207)");

        var changes = RegistryProbe.Changes(h => icons.Changed += h, h => icons.Changed -= h, "c38.new", () => r = icons.RemoveProvider("c38.new"));
        Check.True(r && !icons.Contains("c38.newonly"), "자기 공급자 제거 뒤 키가 남음");
        Check.True(changes.Count == 1 && changes[0] == UiRegistryChangeKind.ProviderRemoved, "ProviderRemoved 알림이 아님");
        Check.True(icons.RemoveProvider("c38.old") && icons.Remove("c38.shared"), "정리(옛 공급자·명시 키 제거) 실패");
    }
}
