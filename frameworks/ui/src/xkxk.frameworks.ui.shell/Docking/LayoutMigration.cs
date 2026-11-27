// 옛 문서 종류 이름 ↔ 창 ID 변환표, 옛 Kind(문자열·숫자) JSON 변환기, 저장본 읽기 정규화(저장 형식 6, B09·UADR-0016)
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace xkxk.frameworks.ui.shell.Docking;

/// <summary>
/// 옛 문서 종류(Grid·Gantt·Form, 숫자 0·1·2) ↔ 창 ID 변환표. 옛 이름·숫자는 영구 별칭이라
/// 데모 창이 시험대 모듈로 옮겨진 뒤에도 데이터 무결성 때문에 셸에 남는다(부록 a10 §3). 공개 계약(Framework/)은 아니다.
/// </summary>
public static class LegacyWindowKinds
{
    /// <summary>옛 Grid(0)의 창 ID.</summary>
    public const string Grid = "xkxk.demo.grid";

    /// <summary>옛 Gantt(1)의 창 ID.</summary>
    public const string Gantt = "xkxk.demo.gantt";

    /// <summary>옛 Form(2)의 창 ID.</summary>
    public const string Form = "xkxk.demo.form";

    /// <summary>옛 이름(대소문자 무시) 또는 숫자 글자 "0"·"1"·"2" → 창 ID. 모르는 값·null 이면 null.</summary>
    public static string? ToId(string? legacyName)
    {
        if (legacyName is null)
        {
            return null;
        }

        if (legacyName.Equals("Grid", StringComparison.OrdinalIgnoreCase) || legacyName == "0")
        {
            return Grid;
        }

        if (legacyName.Equals("Gantt", StringComparison.OrdinalIgnoreCase) || legacyName == "1")
        {
            return Gantt;
        }

        if (legacyName.Equals("Form", StringComparison.OrdinalIgnoreCase) || legacyName == "2")
        {
            return Form;
        }

        return null;
    }

    /// <summary>옛 숫자 0·1·2 → 창 ID. 범위 밖이면 null.</summary>
    public static string? ToId(int value) => value switch
    {
        0 => Grid,
        1 => Gantt,
        2 => Form,
        _ => null,
    };

    /// <summary>창 ID → 옛 이름("Grid"|"Gantt"|"Form"). 옛 이름이 없는 창(모르는 ID·null)은 null.</summary>
    public static string? NameOf(string? windowId) => windowId switch
    {
        Grid => "Grid",
        Gantt => "Gantt",
        Form => "Form",
        _ => null,
    };
}

/// <summary>
/// 문서 Kind 변환기. 읽기는 문자열(옛 이름·모르는 값 그대로)과 숫자(원문 글자, 예 1 → "1")를 모두 받고, 쓰기는 늘 문자열이다.
/// 옛 판 앱(열거형 DTO)이 숫자·이름 어느 쪽 저장본도 남겼으므로 둘 다 읽어야 한다.
/// </summary>
public sealed class LegacyKindJsonConverter : JsonConverter<string?>
{
    /// <inheritdoc />
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return null;
            case JsonTokenType.String:
                return reader.GetString();
            case JsonTokenType.Number:
                return reader.HasValueSequence
                    ? Encoding.UTF8.GetString(reader.ValueSequence.ToArray())
                    : Encoding.UTF8.GetString(reader.ValueSpan);
            default:
                throw new JsonException($"문서 Kind 는 문자열·숫자만 읽는다(받은 형식 {reader.TokenType}).");
        }
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStringValue(value);
    }
}

/// <summary>
/// DuplicateKinds 목록 변환기(부록 a10 §5.2). 원소는 문서 Kind 와 같은 방식으로 문자열·숫자(원문 글자)를 모두 읽고 null 원소는 뺀다.
/// 그 밖 원소·배열이 아닌 값은 JsonException(손상 JSON). 숫자 원소 하나 때문에 저장본 전체를 잃지 않게 한다. 쓰기는 문자열 배열.
/// </summary>
public sealed class LegacyKindListJsonConverter : JsonConverter<List<string>>
{
    private static readonly LegacyKindJsonConverter s_element = new();

    /// <inheritdoc />
    public override List<string> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
        {
            throw new JsonException($"DuplicateKinds 는 배열만 읽는다(받은 형식 {reader.TokenType}).");
        }

        var list = new List<string>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            var item = s_element.Read(ref reader, typeof(string), options);
            if (item is not null)
            {
                list.Add(item);
            }
        }

        if (reader.TokenType != JsonTokenType.EndArray)
        {
            throw new JsonException("DuplicateKinds 배열이 닫히지 않았다.");
        }

        return list;
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, List<string> value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var item in value)
        {
            if (item is not null)
            {
                writer.WriteStringValue(item);
            }
        }

        writer.WriteEndArray();
    }
}

/// <summary>저장본 읽기 정규화(저장 형식 6). Deserialize 끝에서 한 번 부른다. Version 은 읽은 값을 유지한다.</summary>
internal static class LayoutMigration
{
    /// <summary>
    /// 문서마다 WindowId 를 채운다 — 있으면 그 값(Kind 와 달라도 우선), 없으면 ToId(Kind), Kind 가 없으면 Grid(옛 기본값), 그 밖은 Kind 원값.
    /// DuplicateWindowIds 에는 DuplicateKinds 를 변환한 값을 합친다(모르는 ID 도 보존, 순서는 저장본 값 다음 변환 값).
    /// DuplicateOffWindowIds 는 저장본 값 그대로(null 원소만 뺀다, 키가 없으면 null 유지 — DuplicateKinds 로부터 만들지 않는다, a10 §5.2).
    /// </summary>
    internal static LayoutSnapshot Normalize(LayoutSnapshot snapshot)
    {
        snapshot.Documents ??= new List<DocumentSnapshot>();
        snapshot.Documents.RemoveAll(d => d is null);
        snapshot.DuplicateKinds ??= new List<string>();
        snapshot.DuplicateOffWindowIds?.RemoveAll(id => id is null);

        foreach (var doc in snapshot.Documents)
        {
            if (string.IsNullOrEmpty(doc.WindowId))
            {
                doc.WindowId = ResolveId(doc.Kind);
            }
        }

        var converted = snapshot.DuplicateKinds.Select(k => LegacyWindowKinds.ToId(k)).OfType<string>().ToList();
        if (snapshot.DuplicateWindowIds is null && converted.Count == 0)
        {
            return snapshot;
        }

        var merged = snapshot.DuplicateWindowIds ?? new List<string>();
        merged.RemoveAll(id => id is null);
        foreach (var id in converted)
        {
            if (!merged.Contains(id, StringComparer.Ordinal))
            {
                merged.Add(id);
            }
        }

        snapshot.DuplicateWindowIds = merged;
        return snapshot;
    }

    /// <summary>WindowId 가 없는 문서의 창 ID — 옛 이름·숫자면 변환표, Kind 가 비었으면 Grid, 그 밖은 Kind 원값(모르는 창 보존).</summary>
    internal static string ResolveId(string? kind) =>
        string.IsNullOrWhiteSpace(kind) ? LegacyWindowKinds.Grid : LegacyWindowKinds.ToId(kind) ?? kind;
}
