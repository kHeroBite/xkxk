// 도움말 카탈로그 읽기(요건 6) — tour_catalog.json 을 레코드로 푼다. 잘못된 항목은 버리고 오류로 남겨 앱을 멈추지 않는다
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace xkxk.frameworks.ui.shell.Tour;

/// <summary>기능 레이어의 메뉴 줄 규칙. Skip 은 뺄 항목, Requires 는 조건이 참일 때만 보일 항목(메뉴에서 조건부로 숨는 항목)이다.</summary>
public sealed record TourMenuRowRules(IReadOnlySet<string> Skip, IReadOnlyDictionary<string, string> Requires);

/// <summary>읽은 카탈로그. Texts 는 사전 밖 문구(언어→키→문구)로, 원격 갱신처럼 앱 사전을 못 바꾸는 소스만 쓴다.</summary>
public sealed record TourCatalogData(
    int Version,
    IReadOnlyList<TourStep> Steps,
    IReadOnlyList<ShortcutEntry> Shortcuts,
    IReadOnlyList<FeatureEntry> Features,
    TourMenuRowRules MenuRows,
    IReadOnlyDictionary<string, Dictionary<string, string>> Texts,
    IReadOnlyList<string> Errors);

/// <summary>JsonDocument 만 써서 트리밍 웹 번들에서도 동작한다(리플렉션 없음).</summary>
internal static class TourCatalogParser
{
    private const string P = "sysview.tour.";

    public static TourCatalogData Parse(string? json, string origin)
    {
        var errors = new List<string>();
        if (json is null)
        {
            errors.Add(origin + ": no catalog");
            return Empty(errors);
        }

        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var root = doc.RootElement;
            return new TourCatalogData(
                root.TryGetProperty("version", out var v) && v.TryGetInt32(out var version) ? version : 0,
                Items(root, "steps", e => Step(e, errors), errors),
                Items(root, "shortcuts", e => Shortcut(e), errors),
                Items(root, "features", e => Feature(e, errors), errors),
                MenuRows(root),
                Texts(root),
                errors);
        }
        catch (JsonException ex)
        {
            errors.Add(origin + ": " + ex.Message);
            return Empty(errors);
        }
    }

    private static TourCatalogData Empty(IReadOnlyList<string> errors) => new(
        0, Array.Empty<TourStep>(), Array.Empty<ShortcutEntry>(), Array.Empty<FeatureEntry>(),
        new TourMenuRowRules(new HashSet<string>(), new Dictionary<string, string>()),
        new Dictionary<string, Dictionary<string, string>>(), errors);

    private static List<T> Items<T>(JsonElement root, string name, Func<JsonElement, T?> make, List<string> errors)
        where T : class
    {
        var list = new List<T>();
        if (!root.TryGetProperty(name, out var array) || array.ValueKind != JsonValueKind.Array)
        {
            errors.Add(name + ": missing");
            return list;
        }

        foreach (var element in array.EnumerateArray())
        {
            if (make(element) is { } item)
            {
                list.Add(item);
            }
            else
            {
                errors.Add(name + ": invalid item " + Str(element, "id"));
            }
        }

        return list;
    }

    private static TourStep? Step(JsonElement e, List<string> errors)
    {
        if (Str(e, "id") is not { Length: > 0 } id || Str(e, "target") is not { Length: > 0 } target
            || !Enum.TryParse<TourScope>(Str(e, "scope"), out var scope))
        {
            return null;
        }

        var text = P + (Str(e, "text") ?? id);
        return new TourStep(id, target, text + ".title", text + ".body", scope, Flags(e, "modes", TourModes.All, errors), Flags(e, "platforms", TourPlatforms.All, errors), Str(e, "requires"));
    }

    private static ShortcutEntry? Shortcut(JsonElement e)
    {
        if (Str(e, "id") is not { Length: > 0 } id || Str(e, "keys") is not { Length: > 0 } keys)
        {
            return null;
        }

        return new ShortcutEntry(id, keys, P + "key." + (Str(e, "text") ?? id), Bool(e, "webUnavailable"), Bool(e, "macOnly"), Str(e, "binding"));
    }

    private static FeatureEntry? Feature(JsonElement e, List<string> errors)
    {
        if (Str(e, "id") is not { Length: > 0 } id)
        {
            return null;
        }

        var text = P + "feature." + id;
        return new FeatureEntry(id, text + ".title", text + ".body", Flags(e, "modes", TourModes.All, errors), Flags(e, "platforms", TourPlatforms.All, errors), Str(e, "requires"));
    }

    private static TourMenuRowRules MenuRows(JsonElement root)
    {
        var skip = new HashSet<string>(StringComparer.Ordinal);
        var requires = new Dictionary<string, string>(StringComparer.Ordinal);
        if (root.TryGetProperty("menuRows", out var rows) && rows.ValueKind == JsonValueKind.Object)
        {
            if (rows.TryGetProperty("skip", out var list) && list.ValueKind == JsonValueKind.Array)
            {
                skip.UnionWith(list.EnumerateArray().Where(i => i.ValueKind == JsonValueKind.String).Select(i => i.GetString()!));
            }

            if (rows.TryGetProperty("requires", out var map) && map.ValueKind == JsonValueKind.Object)
            {
                foreach (var pair in map.EnumerateObject().Where(p => p.Value.ValueKind == JsonValueKind.String))
                {
                    requires[pair.Name] = pair.Value.GetString()!;
                }
            }
        }

        return new TourMenuRowRules(skip, requires);
    }

    private static Dictionary<string, Dictionary<string, string>> Texts(JsonElement root)
    {
        var texts = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        if (!root.TryGetProperty("texts", out var langs) || langs.ValueKind != JsonValueKind.Object)
        {
            return texts;
        }

        foreach (var lang in langs.EnumerateObject().Where(p => p.Value.ValueKind == JsonValueKind.Object))
        {
            texts[lang.Name] = lang.Value.EnumerateObject().Where(p => p.Value.ValueKind == JsonValueKind.String)
                .ToDictionary(p => p.Name, p => p.Value.GetString()!, StringComparer.Ordinal);
        }

        return texts;
    }

    /// <summary>"Mdi|Dock" 표기를 플래그로. 모르는 값은 오류로 남기고 기본값(전체)이 아니라 0(어디에도 적용 안 함)으로 둔다.</summary>
    private static T Flags<T>(JsonElement e, string name, T all, List<string> errors)
        where T : struct, Enum
    {
        if (Str(e, name) is not { } text)
        {
            return all;
        }

        var value = 0;
        foreach (var token in text.Split('|'))
        {
            if (Enum.TryParse<T>(token.Trim(), out var part))
            {
                value |= Convert.ToInt32(part);
            }
            else
            {
                errors.Add(Str(e, "id") + ": unknown " + name + " " + token);
                return default;
            }
        }

        return (T)Enum.ToObject(typeof(T), value);
    }

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool Bool(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
}
