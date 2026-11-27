// 레이아웃 저장 형식 6 계약 시험 C63~C67 — 옛 저장본(v1·v3·v5) 읽기 변환·v6 쓰기 멱등·옛 판 앱 호환을 고정 표본으로 본다(B09)
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using xkxk.frameworks.ui.data.Layout;
using xkxk.frameworks.ui.shell.Docking;

namespace xkxk.frameworks.ui.test.framework.contract;

internal static class LayoutMigrationChecks
{
    private const string Grid = "xkxk.demo.grid";
    private const string Gantt = "xkxk.demo.gantt";
    private const string Form = "xkxk.demo.form";

    public static IEnumerable<ContractCase> All()
    {
        yield return new ContractCase("C63", "v1 표본 읽기: 모든 문서 WindowId=xkxk.demo.grid, 문서 수·Version 유지", C63);
        yield return new ContractCase("C64", "v3 표본 읽기: 모든 문서 WindowId=xkxk.demo.grid, 패널 값·플로팅 창 보존", C64);
        yield return new ContractCase("C65", "v5 표본 읽기: 문자열·숫자 Kind·Kind 없음·DuplicateKinds(숫자·null 원소 포함)·Groups → WindowId·DuplicateWindowIds 대응, 변환표", C65);
        yield return new ContractCase("C66", "쓰기는 늘 Version 6·WindowId, 옛 이름 창만 Kind, 중복 허용 끔 목록, 읽기→쓰기 멱등(v5·v6 표본)", C66);
        yield return new ContractCase("C67", "v6 출력을 옛 열거형 DTO(61ffef5)로 읽어도 예외 없음·문서 수 같음·DuplicateKinds 해석", C67);
    }

    /// <summary>실행 위치에서 위로 올라가며 test/common/testdata 의 표본을 찾는다.</summary>
    private static string Fixture(string name)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var path = Path.Combine(dir.FullName, "test", "common", "testdata", name);
            if (File.Exists(path))
            {
                return File.ReadAllText(path);
            }
        }

        throw new CheckFailedException($"표본 {name} 을 찾지 못함(기준 {AppContext.BaseDirectory})");
    }

    private static LayoutSnapshot Read(string json, string what) =>
        LayoutSerializer.Deserialize(json) ?? throw new CheckFailedException($"{what}: Deserialize 가 null");

    /// <summary>시험이 따로 가진 옛 이름 대응표(구현의 변환표를 쓰지 않는다).</summary>
    private static string? ExpectedId(string kind) => kind.ToLowerInvariant() switch
    {
        "grid" or "0" => Grid,
        "gantt" or "1" => Gantt,
        "form" or "2" => Form,
        _ => null,
    };

    private static string? ExpectedName(string? windowId) => windowId switch { Grid => "Grid", Gantt => "Gantt", Form => "Form", _ => null };

    /// <summary>원본 JSON 문서 1건의 속성 글자(문자열이면 값, 숫자면 원문, 없거나 null 이면 null).</summary>
    private static string? Raw(JsonElement doc, string name) =>
        !doc.TryGetProperty(name, out var v) || v.ValueKind == JsonValueKind.Null ? null : v.ValueKind == JsonValueKind.String ? v.GetString() : v.GetRawText();

    private static List<JsonElement> Items(JsonElement root, string name) =>
        root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array ? v.EnumerateArray().ToList() : new List<JsonElement>();

    private static bool SameSet(IEnumerable<string>? actual, IEnumerable<string> expected)
    {
        var a = (actual ?? Enumerable.Empty<string>()).OrderBy(x => x, StringComparer.Ordinal).ToList();
        return a.SequenceEqual(expected.Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal));
    }

    private static LayoutSnapshot AllGrid(string name, int version)
    {
        var json = Fixture(name);
        using var raw = JsonDocument.Parse(json);
        var s = Read(json, name);
        var count = Items(raw.RootElement, "Documents").Count;
        Check.True(count > 0 && s.Documents.Count == count, $"{name}: 문서 수 {s.Documents.Count}(원본 {count})");
        Check.True(s.Version == version, $"{name}: Version {s.Version}(읽은 값 {version} 유지 기대)");
        var wrong = s.Documents.Where(d => d.WindowId != Grid).Select(d => $"{d.Id}={d.WindowId}").ToList();
        Check.True(wrong.Count == 0, $"{name}: WindowId 불일치 {string.Join(", ", wrong)}");
        Check.True(s.DuplicateWindowIds is null || s.DuplicateWindowIds.Count == 0, $"{name}: 없던 중복 허용 창이 생김");
        return s;
    }

    private static void C63()
    {
        var s = AllGrid("layout_v1_z5_20261028_2333.json", 1);
        Check.True(s.ActiveId == "Grid-32" && s.Documents.Single(d => d.Id == "Grid-30").IsPinned, "v1 활성 문서·핀 값이 달라짐");
    }

    private static void C64()
    {
        var s = AllGrid("layout_v3_right.json", 3);
        var p = s.Panels;
        Check.True(p is not null && p.ExplorerProportion == 0.18 && p.LogProportion == 0.2 && p.NotificationEdge == "Right" && p.NotificationProportion == 0.2, "v3 패널 비율·알림 위치가 달라짐");
        Check.True(p!.Tabs is { Count: 2 } && p.Tabs.Select(t => t.PanelId).SequenceEqual(new[] { "LeftPane.Tabs", "BottomPane.Tabs" })
            && p.Tabs[0].ToolIds.SequenceEqual(new[] { "Explorer" }) && p.Tabs[1].ActiveToolId == "Log", "v3 패널 탭 구성이 달라짐");
        Check.True(s.SidePanelsHidden == false && s.ActiveId == "Grid-6" && s.Documents.Single(d => d.Id == "Grid-2").Window is { X: 21660, Width: 640 }, "v3 활성 문서·플로팅 창 값이 달라짐");
    }

    private static void C65()
    {
        const string name = "layout_v5_kinds.json";
        var json = Fixture(name);
        using var raw = JsonDocument.Parse(json);
        var root = raw.RootElement;
        var s = Read(json, name);
        var docs = Items(root, "Documents");
        Check.True(root.GetProperty("Version").GetInt32() == 5 && s.Version == 5, "v5 표본 Version 이 5 가 아니거나 읽은 값이 유지되지 않음");
        Check.True(s.Documents.Count == docs.Count && docs.Count > 0, $"문서 수 {s.Documents.Count}(원본 {docs.Count})");
        Check.True(docs.Any(d => d.TryGetProperty("Kind", out var k) && k.ValueKind == JsonValueKind.String) && docs.Any(d => d.TryGetProperty("Kind", out var k) && k.ValueKind == JsonValueKind.Number)
            && docs.Any(d => !d.TryGetProperty("Kind", out _)), "표본에 문자열 Kind·숫자 Kind·Kind 없음 문서가 모두 있어야 함");
        foreach (var d in docs)
        {
            var kind = Raw(d, "Kind");
            var expected = Raw(d, "WindowId") ?? (kind is null ? Grid : ExpectedId(kind) ?? kind);
            var item = s.Documents.Single(x => x.Id == Raw(d, "Id"));
            Check.True(item.WindowId == expected, $"{item.Id}: Kind={kind ?? "(없음)"} → WindowId={item.WindowId}({expected} 기대)");
        }

        var dupKinds = Items(root, "DuplicateKinds").Select(k => k.ValueKind == JsonValueKind.String ? k.GetString()! : k.GetRawText()).ToList();
        var dup = dupKinds.Select(ExpectedId).OfType<string>().Concat(Items(root, "DuplicateWindowIds").Select(k => k.GetString()!)).ToList();
        Check.True(dupKinds.Count > 0 && SameSet(s.DuplicateWindowIds, dup), $"DuplicateWindowIds=[{string.Join(",", s.DuplicateWindowIds ?? new())}], 기대 [{string.Join(",", dup)}]");
        var groups = Items(root, "Groups");
        Check.True(groups.Count > 0 && s.Groups is not null && s.Groups.Count == groups.Count, "표본 Groups 가 없거나 묶음 수가 다름");
        foreach (var g in groups)
        {
            var ids = Items(g, "DocIds").Select(x => x.GetString()!).ToList();
            var read = s.Groups!.Single(x => x.Id == Raw(g, "Id"));
            Check.True(ids.Count >= 2 && read.DocIds.SequenceEqual(ids) && read.ActiveId == Raw(g, "ActiveId"), $"묶음 {read.Id}: DocIds·ActiveId 가 보존되지 않음");
        }

        CheckInlineRules();
        CheckDuplicateKindElements();
        Check.True(LegacyWindowKinds.Grid == Grid && LegacyWindowKinds.Gantt == Gantt && LegacyWindowKinds.Form == Form, "LegacyWindowKinds 상수 값 불일치");
        Check.True(LegacyWindowKinds.ToId("grid") == Grid && LegacyWindowKinds.ToId("GANTT") == Gantt && LegacyWindowKinds.ToId("2") == Form && LegacyWindowKinds.ToId(0) == Grid
            && LegacyWindowKinds.ToId(1) == Gantt && LegacyWindowKinds.ToId(3) is null && LegacyWindowKinds.ToId("acme.none") is null && LegacyWindowKinds.ToId((string?)null) is null, "LegacyWindowKinds.ToId 대응 불일치");
        Check.True(LegacyWindowKinds.NameOf(Grid) == "Grid" && LegacyWindowKinds.NameOf(Gantt) == "Gantt" && LegacyWindowKinds.NameOf(Form) == "Form"
            && LegacyWindowKinds.NameOf("acme.none") is null && LegacyWindowKinds.NameOf(null) is null, "LegacyWindowKinds.NameOf 대응 불일치");
    }

    /// <summary>표본에 없을 수 있는 경계(대소문자·범위 밖 숫자·모르는 이름·WindowId 우선·합집합·손상 JSON)를 본다.</summary>
    private static void CheckInlineRules()
    {
        const string json = """
            {"Version":5,"Documents":[{"Id":"a","Kind":"gAnTt"},{"Id":"b","Kind":2},{"Id":"c","Kind":0},{"Id":"d","Kind":"acme.none"},{"Id":"e","Kind":7},{"Id":"f"},
              {"Id":"g","Kind":"Grid","WindowId":"xkxk.demo.form"},{"Id":"h","Kind":null}],
             "DuplicateKinds":["Gantt"],"DuplicateWindowIds":["acme.dup","xkxk.demo.gantt"]}
            """;
        var s = Read(json, "경계 JSON");
        var got = string.Join(",", s.Documents.Select(d => d.WindowId));
        var want = string.Join(",", Gantt, Form, Grid, "acme.none", "7", Grid, Form, Grid);
        Check.True(got == want, $"경계 WindowId={got}, 기대 {want}");
        Check.True(s.DuplicateWindowIds is { Count: 2 } && SameSet(s.DuplicateWindowIds, new[] { "acme.dup", Gantt }), "DuplicateWindowIds 합집합 불일치");
        Check.True(LayoutSerializer.Deserialize("{") is null && LayoutSerializer.Deserialize("") is null, "손상·빈 JSON 이 null 이 아님");
    }

    /// <summary>DuplicateKinds 원소(R6): 숫자·null 원소는 저장본 전체를 잃지 않고, 문자열·숫자가 아닌 원소는 손상 JSON 이다.</summary>
    private static void CheckDuplicateKindElements()
    {
        static string With(string elements) => $$"""{"Version":5,"Documents":[{"Id":"a"}],"DuplicateKinds":{{elements}}}""";
        foreach (var (elements, want) in new[] { ("[1]", Gantt), ("[null,\"Form\"]", Form) })
        {
            var s = Read(With(elements), "DuplicateKinds " + elements);
            Check.True(s.Documents.Count == 1 && s.DuplicateWindowIds is { Count: 1 } && s.DuplicateWindowIds[0] == want,
                $"DuplicateKinds {elements}: DuplicateWindowIds=[{string.Join(",", s.DuplicateWindowIds ?? new())}], 기대 [{want}]");
        }

        Check.True(LayoutSerializer.Deserialize(With("[{}]")) is null, "DuplicateKinds [{}] 가 null(손상 JSON)이 아님");
    }

    /// <summary>중복 허용 끔 목록(R4): 쓰기는 늘 키를 쓰고 빈 항목·중복·켬 목록 항목을 뺀다. 끔 키가 없는 v6 를 다시 쓰면 [] 이다.</summary>
    private static void CheckDuplicateOffList()
    {
        var made = Unknown(); // 켬: acme.dup·Form
        made.DuplicateOffWindowIds = new() { "a", "a", "", Form, "acme.dup" };
        var output = LayoutSerializer.Serialize(made);
        var off = OffList(output);
        Check.True(off.SequenceEqual(new[] { "a" }), $"출력 DuplicateOffWindowIds=[{string.Join(",", off)}], 기대 [a]");
        var read = Read(output, "끔 목록 출력");
        Check.True(read.DuplicateOffWindowIds is { Count: 1 } && read.DuplicateOffWindowIds[0] == "a" && LayoutSerializer.Serialize(read) == output, "끔 목록이 읽기→쓰기에서 보존되지 않음");

        // 끔 키 없는 저장본: DuplicateKinds 로부터 끔 목록을 만들지 않는다.
        var noKey = Read("""{"Version":6,"Documents":[{"Id":"a","WindowId":"xkxk.demo.grid"}],"DuplicateKinds":["Gantt"]}""", "끔 키 없는 v6");
        Check.True(noKey.DuplicateOffWindowIds is null or { Count: 0 }, "끔 키가 없는데 끔 목록이 생김");
        Check.True(OffList(LayoutSerializer.Serialize(noKey)).Count == 0, "끔 키 없는 v6 를 다시 쓴 끔 목록이 [] 이 아님");
    }

    /// <summary>출력 JSON 의 DuplicateOffWindowIds — 키가 없거나 배열이 아니면 실패.</summary>
    private static List<string> OffList(string output)
    {
        using var parsed = JsonDocument.Parse(output);
        Check.True(parsed.RootElement.TryGetProperty("DuplicateOffWindowIds", out var v) && v.ValueKind == JsonValueKind.Array, "출력에 DuplicateOffWindowIds 배열이 없음");
        return v.EnumerateArray().Select(x => x.GetString()!).ToList();
    }

    /// <summary>Serialize 출력이 형식 6 규칙(Version·WindowId·옛 이름 Kind·중복 허용 목록)을 지키는지 본다.</summary>
    private static void CheckOutput(string output, LayoutSnapshot source, string what)
    {
        using var parsed = JsonDocument.Parse(output);
        var root = parsed.RootElement;
        Check.True(root.GetProperty("Version").GetInt32() == 6, $"{what}: 출력 Version 이 6 이 아님");
        var docs = Items(root, "Documents");
        Check.True(docs.Count == source.Documents.Count, $"{what}: 출력 문서 수 {docs.Count}");
        foreach (var d in docs)
        {
            var id = Raw(d, "Id");
            var windowId = Raw(d, "WindowId");
            Check.True(!string.IsNullOrEmpty(windowId) && windowId == source.Documents.Single(x => x.Id == id).WindowId, $"{what} {id}: WindowId 없음·다름({windowId})");
            var name = ExpectedName(windowId);
            Check.True(name is null ? !d.TryGetProperty("Kind", out _) : Raw(d, "Kind") == name, $"{what} {id}: Kind={Raw(d, "Kind") ?? "(없음)"}, 기대 {name ?? "(키 없음)"}");
        }

        var dup = (source.DuplicateWindowIds ?? new()).ToList();
        Check.True(SameSet(Items(root, "DuplicateWindowIds").Select(x => x.GetString()!), dup), $"{what}: 출력 DuplicateWindowIds 가 전부가 아님");
        Check.True(SameSet(Items(root, "DuplicateKinds").Select(x => x.GetString()!), dup.Select(ExpectedName).OfType<string>()), $"{what}: 출력 DuplicateKinds 는 옛 이름만이어야 함");
    }

    private static LayoutSnapshot Unknown() => new()
    {
        Version = 1,
        Documents = { new DocumentSnapshot { Id = "x", Kind = "Grid", WindowId = "acme.none" }, new DocumentSnapshot { Id = "y", WindowId = Gantt } },
        DuplicateWindowIds = new() { "acme.dup", Form },
    };

    private static void C66()
    {
        Check.True(LayoutSnapshot.CurrentVersion == 6 && new LayoutSnapshot().Version == 6, "CurrentVersion·기본 Version 이 6 이 아님");
        foreach (var name in new[] { "layout_v5_kinds.json", "layout_v6_mixed.json" })
        {
            var s = Read(Fixture(name), name);
            var first = LayoutSerializer.Serialize(s);
            CheckOutput(first, s, name);
            var second = LayoutSerializer.Serialize(Read(first, name + " 1차 출력"));
            Check.True(second == first, $"{name}: 두 번째 출력이 첫 번째와 다름(멱등 아님)");
        }

        var json = Fixture("layout_v6_mixed.json");
        using var raw = JsonDocument.Parse(json);
        var mixed = Read(json, "v6");
        var docs = Items(raw.RootElement, "Documents");
        Check.True(raw.RootElement.GetProperty("Version").GetInt32() == 6 && docs.Count == mixed.Documents.Count
            && docs.Any(d => ExpectedName(Raw(d, "WindowId")) is not null) && docs.Any(d => Raw(d, "WindowId") is { } w && ExpectedName(w) is null), "v6 표본은 Version 6 이고 옛 이름 창과 새 창이 모두 있어야 함");
        Check.True(docs.All(d => Raw(d, "WindowId") is not { } w || mixed.Documents.Single(x => x.Id == Raw(d, "Id")).WindowId == w), "v6 원본 WindowId 가 읽기에서 바뀜");

        var made = Unknown();
        CheckOutput(LayoutSerializer.Serialize(made), made, "코드로 만든 스냅샷");
        CheckDuplicateOffList();
    }

    private static void C67()
    {
        var sources = new[] { Read(Fixture("layout_v5_kinds.json"), "v5"), Read(Fixture("layout_v6_mixed.json"), "v6"), Unknown() };
        foreach (var s in sources)
        {
            var old = JsonSerializer.Deserialize(LayoutSerializer.Serialize(s), LegacyV5JsonContext.Default.LegacyV5LayoutSnapshot);
            Check.True(old is not null && old.Documents.Count == s.Documents.Count && (old.Groups?.Count ?? 0) == (s.Groups?.Count ?? 0), "옛 DTO 의 문서·묶음 수가 다름");
            foreach (var d in s.Documents)
            {
                // 옛 이름이 없는 창은 Kind 키가 빠져 옛 앱의 기본값(Grid)이 된다.
                var want = Enum.Parse<LegacyKindV5>(ExpectedName(d.WindowId) ?? "Grid");
                var got = old!.Documents.Single(x => x.Id == d.Id).Kind;
                Check.True(got == want, $"{d.Id}: 옛 DTO Kind={got}({want} 기대)");
            }

            var dup = (s.DuplicateWindowIds ?? new()).Select(ExpectedName).OfType<string>();
            Check.True(SameSet(old!.DuplicateKinds.Select(k => k.ToString()), dup), "옛 DTO DuplicateKinds 불일치");
        }

        // 양성 대조 — 옛 DTO 는 모르는 Kind 문자열을 실제로 거부한다(그래서 v6 는 그런 값을 쓰면 안 된다).
        Check.Throws<JsonException>(() => JsonSerializer.Deserialize("""{"Documents":[{"Id":"a","Kind":"acme.none"}]}""", LegacyV5JsonContext.Default.LegacyV5LayoutSnapshot), "옛 DTO 의 모르는 Kind");
    }
}
