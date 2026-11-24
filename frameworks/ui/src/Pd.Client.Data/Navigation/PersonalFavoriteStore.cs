// 개인 즐겨찾기 저장소 계약(REQ-111 ① plt.personal_favorite)과 기기 로컬 구현 — 서버 구현은 plt 개인화 WBS 에서 같은 계약으로 교체한다. UI 비의존
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Pd.Client.Data.Layout;

namespace Pd.Client.Data.Navigation;

/// <summary>즐겨찾기 한 건. TargetType 은 대상 종류(제목줄 즐겨찾기는 "screen"), TargetId 는 대상 ID(화면 ID), Rank 는 사전순 순서 키.</summary>
public sealed record PersonalFavorite(string TargetType, string TargetId, string Rank, DateTimeOffset AddedAt);

/// <summary>추가 결과.</summary>
public enum FavoriteAddResult
{
    Added,
    AlreadyExists,
    LimitReached,
    Invalid,
}

/// <summary>개인 즐겨찾기 저장소(사용자 1명 범위). 목록은 Rank 사전순 = 넣은 순서(끌어서 바꾸면 그 순서).</summary>
public interface IPersonalFavoriteStore
{
    /// <summary>사용자당 최대 개수(plt.personal.favorite.max_items).</summary>
    int MaxItems { get; }

    /// <summary>목록이 바뀌면 알린다(추가·빼기·순서).</summary>
    event Action? Changed;

    Task<IReadOnlyList<PersonalFavorite>> ListAsync(string targetType);

    Task<bool> ContainsAsync(string targetType, string targetId);

    Task<FavoriteAddResult> AddAsync(string targetType, string targetId);

    /// <summary>없는 항목을 빼도 오류가 아니다(false).</summary>
    Task<bool> RemoveAsync(string targetType, string targetId);

    /// <summary>같은 종류 안에서 targetId 를 newIndex(0부터, 범위 밖은 끝으로 맞춤) 자리로 옮긴다. 없으면 false.</summary>
    Task<bool> MoveAsync(string targetType, string targetId, int newIndex);
}

/// <summary>
/// 기기 로컬 구현 — IKeyValueStore 의 키 하나(StorageKey)에 JSON 으로 둔다(데스크톱·Android 파일, 웹 localStorage).
/// Rank 는 고정 폭 10진 문자열이라 사전순 = 숫자순이다. 옮기기는 같은 종류 전체 Rank 를 다시 매긴다(로컬 목록은 작다).
/// </summary>
public sealed class LocalPersonalFavoriteStore : IPersonalFavoriteStore
{
    public const string StorageKey = "plt.personal_favorite";

    /// <summary>기본 최대 개수 — 설정 키 plt.personal.favorite.max_items 기본값(정본 10_API설계/plt.md, D-561). 호스트가 키 값을 넘기면 그 값을 쓴다.</summary>
    public const int DefaultMaxItems = 200;

    private const int RankStep = 1000;
    private readonly IKeyValueStore _store;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private List<PersonalFavorite>? _items;

    public LocalPersonalFavoriteStore(IKeyValueStore store, int maxItems = DefaultMaxItems)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        MaxItems = Math.Max(1, maxItems);
    }

    public int MaxItems { get; }

    public event Action? Changed;

    public async Task<IReadOnlyList<PersonalFavorite>> ListAsync(string targetType)
    {
        var all = await LoadAsync().ConfigureAwait(false);
        return Of(all, targetType);
    }

    public async Task<bool> ContainsAsync(string targetType, string targetId)
    {
        var all = await LoadAsync().ConfigureAwait(false);
        return all.Any(f => f.TargetType == targetType && f.TargetId == targetId);
    }

    public Task<FavoriteAddResult> AddAsync(string targetType, string targetId) => MutateAsync(all =>
    {
        if (string.IsNullOrWhiteSpace(targetType) || string.IsNullOrWhiteSpace(targetId)) { return (FavoriteAddResult.Invalid, false); }
        if (all.Any(f => f.TargetType == targetType && f.TargetId == targetId)) { return (FavoriteAddResult.AlreadyExists, false); }
        if (all.Count >= MaxItems) { return (FavoriteAddResult.LimitReached, false); }
        var last = Of(all, targetType).LastOrDefault();
        var next = (last is null ? 0 : ParseRank(last.Rank)) + RankStep;
        all.Add(new PersonalFavorite(targetType, targetId, FormatRank(next), DateTimeOffset.UtcNow));
        return (FavoriteAddResult.Added, true);
    });

    public Task<bool> RemoveAsync(string targetType, string targetId) => MutateAsync(all =>
    {
        var removed = all.RemoveAll(f => f.TargetType == targetType && f.TargetId == targetId) > 0;
        return (removed, removed);
    });

    public Task<bool> MoveAsync(string targetType, string targetId, int newIndex) => MutateAsync(all =>
    {
        var list = Of(all, targetType).ToList();
        var item = list.FirstOrDefault(f => f.TargetId == targetId);
        if (item is null) { return (false, false); }
        list.Remove(item);
        list.Insert(Math.Clamp(newIndex, 0, list.Count), item);
        all.RemoveAll(f => f.TargetType == targetType);
        all.AddRange(list.Select((f, i) => f with { Rank = FormatRank((i + 1) * RankStep) }));
        return (true, true);
    });

    private static List<PersonalFavorite> Of(IEnumerable<PersonalFavorite> all, string targetType) =>
        all.Where(f => f.TargetType == targetType).OrderBy(f => f.Rank, StringComparer.Ordinal).ToList();

    private static string FormatRank(long value) => value.ToString("D12", System.Globalization.CultureInfo.InvariantCulture);

    private static long ParseRank(string rank) => long.TryParse(rank, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0;

    private async Task<T> MutateAsync<T>(Func<List<PersonalFavorite>, (T Result, bool Changed)> change)
    {
        bool changed;
        T result;
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var all = await LoadCoreAsync().ConfigureAwait(false);
            (result, changed) = change(all);
            if (changed)
            {
                await _store.SetAsync(StorageKey, JsonSerializer.Serialize(all, FavoriteJsonContext.Default.ListPersonalFavorite)).ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }

        if (changed) { Changed?.Invoke(); }
        return result;
    }

    private async Task<List<PersonalFavorite>> LoadAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try { return new List<PersonalFavorite>(await LoadCoreAsync().ConfigureAwait(false)); }
        finally { _gate.Release(); }
    }

    private async Task<List<PersonalFavorite>> LoadCoreAsync()
    {
        if (_items is not null) { return _items; }
        var json = await _store.GetAsync(StorageKey).ConfigureAwait(false);
        try
        {
            _items = string.IsNullOrWhiteSpace(json) ? new List<PersonalFavorite>() : JsonSerializer.Deserialize(json, FavoriteJsonContext.Default.ListPersonalFavorite) ?? new List<PersonalFavorite>();
        }
        catch (JsonException)
        {
            _items = new List<PersonalFavorite>(); // 손상된 저장값은 빈 목록으로 시작하고 다음 저장 때 덮는다
        }

        _items.RemoveAll(f => string.IsNullOrWhiteSpace(f.TargetType) || string.IsNullOrWhiteSpace(f.TargetId));
        return _items;
    }
}

[JsonSerializable(typeof(List<PersonalFavorite>))]
internal sealed partial class FavoriteJsonContext : JsonSerializerContext
{
}
