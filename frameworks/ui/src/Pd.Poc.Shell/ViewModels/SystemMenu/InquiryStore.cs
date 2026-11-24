// 문의 로컬 임시함(UI-17 문의하기·문의목록) — 서버(pd-server) 구축 전까지 문의를 키-값 저장소 한 항목(JSON 배열)에 보관한다
using Pd.Client.Data.Layout;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Pd.Poc.Shell.Docking;

namespace Pd.Poc.Shell.ViewModels.SystemMenu;

/// <summary>문의 한 건. Status 는 지금은 항상 "local"(미전송)이다. 서버 연동 뒤 "sent"·"answered" 등을 쓴다.</summary>
public sealed record InquiryRecord(string Id, DateTimeOffset Time, string Category, string Title, string Body, bool AttachAppInfo, string Status);

/// <summary>
/// 문의 임시함. 저장소는 App.LayoutLibraryStore(데스크톱·Android 폴더 파일, 웹 localStorage, --fresh 격리 포함)를 같이 쓰고 키 하나만 차지한다.
/// JSON 은 Utf8JsonWriter·JsonDocument 로 직접 다룬다(브라우저 트리밍 환경에서 리플렉션 직렬화를 피한다).
/// </summary>
public sealed class InquiryStore
{
    /// <summary>저장 키. 레이아웃 키와 겹치지 않게 접두사를 따로 둔다.</summary>
    public const string Key = "sysinquiry.box";

    private readonly Func<IKeyValueStore?> _store;

    public InquiryStore(Func<IKeyValueStore?> store)
    {
        _store = store;
    }

    /// <summary>앱 기본 저장소를 쓰는 임시함.</summary>
    public static InquiryStore Default { get; } = new(() => App.LayoutLibraryStore);

    /// <summary>최신 순 목록. 저장소가 없거나 값이 깨졌으면 빈 목록이다.</summary>
    public async Task<IReadOnlyList<InquiryRecord>> ListAsync()
    {
        var list = new List<InquiryRecord>();
        if (_store() is not { } store || await store.GetAsync(Key) is not { Length: > 0 } json)
        {
            return list;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            foreach (var e in doc.RootElement.EnumerateArray())
            {
                list.Add(new InquiryRecord(
                    Str(e, "id"),
                    DateTimeOffset.TryParse(Str(e, "time"), out var t) ? t : DateTimeOffset.MinValue,
                    Str(e, "category"),
                    Str(e, "title"),
                    Str(e, "body"),
                    e.TryGetProperty("attachAppInfo", out var a) && a.ValueKind == JsonValueKind.True,
                    Str(e, "status")));
            }
        }
        catch (JsonException)
        {
            return new List<InquiryRecord>(); // 깨진 값은 빈 임시함으로 본다(덮어쓰기는 다음 저장 때)
        }

        list.Sort((x, y) => y.Time.CompareTo(x.Time));
        return list;
    }

    /// <summary>한 건을 더한다. 저장소가 없으면 false.</summary>
    public async Task<bool> AddAsync(InquiryRecord record)
    {
        var all = new List<InquiryRecord>(await ListAsync()) { record };
        return await SaveAsync(all);
    }

    /// <summary>Id 가 같은 건을 지운다. 없으면 false.</summary>
    public async Task<bool> DeleteAsync(string id)
    {
        var all = new List<InquiryRecord>(await ListAsync());
        return all.RemoveAll(r => r.Id == id) > 0 && await SaveAsync(all);
    }

    private async Task<bool> SaveAsync(IReadOnlyList<InquiryRecord> all)
    {
        if (_store() is not { } store)
        {
            return false;
        }

        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartArray();
            foreach (var r in all)
            {
                w.WriteStartObject();
                w.WriteString("id", r.Id);
                w.WriteString("time", r.Time.ToString("O"));
                w.WriteString("category", r.Category);
                w.WriteString("title", r.Title);
                w.WriteString("body", r.Body);
                w.WriteBoolean("attachAppInfo", r.AttachAppInfo);
                w.WriteString("status", r.Status);
                w.WriteEndObject();
            }

            w.WriteEndArray();
        }

        await store.SetAsync(Key, Encoding.UTF8.GetString(ms.ToArray()));
        return true;
    }

    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
}
