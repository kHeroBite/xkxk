// 레이아웃 라이브러리 구현(UI-11) — 목록(index)과 내용(layout.<id>)을 키-값 저장소에 나눠 저장하고, 자동 저장 슬롯을 읽기 전용 항목으로 함께 보인다
using Pd.Client.Data.Layout;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Pd.Poc.Shell.Localization;

namespace Pd.Poc.Shell.Docking;

/// <summary>목록 저장 형식. 이름은 여기에만 있다(이름 변경 = 이 파일만 바뀜).</summary>
public sealed class LayoutIndex
{
    public int Version { get; set; } = 1;

    public List<LayoutIndexItem> Items { get; set; } = new();
}

public sealed class LayoutIndexItem
{
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public int DocumentCount { get; set; }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(LayoutIndex))]
internal sealed partial class LayoutIndexJsonContext : JsonSerializerContext
{
}

public sealed class LayoutLibrary : ILayoutLibrary
{
    /// <summary>자동 저장 슬롯 표시 이름(team-lead 결정 ②) — 현재 언어. 개인 레이아웃 이름으로는 쓸 수 없다.</summary>
    public static string AutoSavedName => Loc.Tr("layout.autoSavedName");

    internal const string IndexKey = "index";
    internal static readonly StringComparer NameComparer = StringComparer.CurrentCultureIgnoreCase;

    private readonly IKeyValueStore _store;
    private readonly IShellCommands _shell;
    private readonly ILayoutStore _autoStore;
    private readonly Action<string> _log;
    private readonly Func<DateTimeOffset> _clock;
    private readonly SemaphoreSlim _gate = new(1, 1); // 목록 읽기-수정-쓰기 직렬화
    private LayoutIndex? _index;
    private readonly LayoutBackupService _backups; // 전체 백업·복원(윈도우 메뉴 ⑫) 협력 객체

    /// <param name="settings">UI 설정 백업 입구(전체 백업·복원 대상, 없으면 레이아웃만) — UiSettings.BackupSource.</param>
    public LayoutLibrary(IKeyValueStore store, IShellCommands shell, ILayoutStore autoStore, Action<string> log,
        ISettingsBackupSource? settings = null, Func<DateTimeOffset>? clock = null)
    {
        _store = store;
        _shell = shell;
        _autoStore = autoStore;
        _log = log;
        _clock = clock ?? (() => DateTimeOffset.Now);
        _backups = new LayoutBackupService(this, store, shell, autoStore, log, _clock, settings);
    }

    public event Action? Changed;

    public IReadOnlyList<LayoutTemplate> Templates => LayoutTemplates.All;

    public string DefaultName()
    {
        var baseName = _clock().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture); // ':' 는 문화권 시간 구분자라 고정
        var name = baseName;
        for (var n = 2; _index?.Items.Any(i => NameComparer.Equals(i.Name, name)) == true; n++)
        {
            name = $"{baseName} ({n})"; // 같은 초에 두 번 저장
        }

        return name;
    }

    public async Task<IReadOnlyList<LayoutEntry>> ListAsync()
    {
        var list = new List<LayoutEntry>();
        var auto = LayoutSerializer.Deserialize(await SafeLoadAutoAsync());
        if (auto is not null)
        {
            list.Add(new LayoutEntry(ILayoutLibrary.AutoSavedId, AutoSavedName, LayoutEntryKind.AutoSaved, DateTimeOffset.MinValue, auto.Documents.Count));
        }

        var index = await LoadIndexAsync();
        list.AddRange(index.Items.OrderByDescending(i => i.UpdatedAt)
            .Select(i => new LayoutEntry(i.Id, i.Name, LayoutEntryKind.Personal, i.UpdatedAt, i.DocumentCount)));
        return list;
    }

    public Task<LayoutSaveResult> SaveCurrentAsync(string? name, bool overwrite) => Locked(async () =>
    {
        var trimmed = name?.Trim() ?? "";
        if (trimmed.Length == 0) { return new LayoutSaveResult(LayoutSaveStatus.NameEmpty); }
        if (NameComparer.Equals(trimmed, AutoSavedName))
        {
            // 예약 이름: 먼저 "같은 이름이 있다"(다른 항목과 같은 판정), 그래도 덮어쓰려 하면 읽기 전용으로 거부
            return new LayoutSaveResult(overwrite ? LayoutSaveStatus.ReadOnly : LayoutSaveStatus.NameExists, ILayoutLibrary.AutoSavedId);
        }

        var index = await LoadIndexAsync();
        var existing = index.Items.FirstOrDefault(i => NameComparer.Equals(i.Name, trimmed));
        if (existing is not null && !overwrite) { return new LayoutSaveResult(LayoutSaveStatus.NameExists, existing.Id); }

        var json = _shell.SerializeLayout();
        var now = _clock();
        var item = existing ?? new LayoutIndexItem { Id = Guid.NewGuid().ToString("N"), Name = trimmed, CreatedAt = now };
        item.UpdatedAt = now;
        item.DocumentCount = _shell.Documents.Count;
        try
        {
            await _store.SetAsync(ContentKey(item.Id), json); // 내용 먼저 → 목록(목록에 있는데 내용이 없는 상태를 만들지 않음)
            if (existing is null) { index.Items.Add(item); }
            await SaveIndexAsync(index);
        }
        catch (Exception ex)
        {
            _log(Loc.Tr("log.layout.saveFailed", trimmed, ex.Message));
            _index = null; // 다음 호출 때 저장소에서 다시 읽는다
            return new LayoutSaveResult(LayoutSaveStatus.Failed, item.Id);
        }

        _log(Loc.Tr(existing is null ? "log.layout.saved" : "log.layout.overwritten", trimmed, item.DocumentCount));
        return new LayoutSaveResult(LayoutSaveStatus.Saved, item.Id);
    }, changed: true);

    public async Task<bool> LoadAsync(string idOrName)
    {
        string? json;
        string label;
        if (string.IsNullOrWhiteSpace(idOrName) || idOrName == ILayoutLibrary.AutoSavedId)
        {
            json = await SafeLoadAutoAsync();
            label = AutoSavedName;
        }
        else
        {
            var index = await LoadIndexAsync();
            var item = index.Items.FirstOrDefault(i => i.Id == idOrName)
                ?? index.Items.FirstOrDefault(i => NameComparer.Equals(i.Name, idOrName.Trim()));
            if (item is null)
            {
                _log(Loc.Tr("log.layout.notFound", idOrName));
                return false;
            }

            json = await SafeGetAsync(ContentKey(item.Id));
            label = item.Name;
        }

        var ok = await Busy.BusyHooks.RunLongAsync(Busy.BusyHooks.MainScope, "layout", () => _shell.ApplyLayout(json)); // 복원: 스피너를 먼저 그린 뒤 복원한다
        _log(Loc.Tr(ok ? "log.layout.loaded" : "log.layout.loadFailed", label));
        return ok;
    }

    public Task<LayoutSaveResult> RenameAsync(string id, string newName) => Locked(async () =>
    {
        if (IsReadOnlyId(id)) { return new LayoutSaveResult(LayoutSaveStatus.ReadOnly, id); }
        var trimmed = newName?.Trim() ?? "";
        if (trimmed.Length == 0) { return new LayoutSaveResult(LayoutSaveStatus.NameEmpty, id); }

        var index = await LoadIndexAsync();
        var item = index.Items.FirstOrDefault(i => i.Id == id);
        if (item is null) { return new LayoutSaveResult(LayoutSaveStatus.NotFound, id); }
        if (NameComparer.Equals(trimmed, AutoSavedName)
            || index.Items.Any(i => i.Id != id && NameComparer.Equals(i.Name, trimmed)))
        {
            return new LayoutSaveResult(LayoutSaveStatus.NameExists, id); // 이름 변경은 덮어쓰지 않고 거부(team-lead 결정 ①)
        }

        var old = item.Name;
        item.Name = trimmed;
        item.UpdatedAt = _clock();
        if (!await TrySaveIndexAsync(index, "log.layout.renameFailed", old)) { return new LayoutSaveResult(LayoutSaveStatus.Failed, id); }
        _log(Loc.Tr("log.layout.renamed", old, trimmed));
        return new LayoutSaveResult(LayoutSaveStatus.Saved, id);
    }, changed: true);

    public Task<LayoutSaveResult> DeleteAsync(string id) => Locked(async () =>
    {
        if (IsReadOnlyId(id)) { return new LayoutSaveResult(LayoutSaveStatus.ReadOnly, id); }
        var index = await LoadIndexAsync();
        var item = index.Items.FirstOrDefault(i => i.Id == id);
        if (item is null) { return new LayoutSaveResult(LayoutSaveStatus.NotFound, id); }

        index.Items.Remove(item);
        if (!await TrySaveIndexAsync(index, "log.layout.deleteFailed", item.Name)) { return new LayoutSaveResult(LayoutSaveStatus.Failed, id); }
        try { await _store.DeleteAsync(ContentKey(id)); } // 목록 먼저 → 내용(실패해도 목록에서는 이미 빠짐)
        catch (Exception ex) { _log(Loc.Tr("log.layout.contentDeleteFailed", item.Name, ex.Message)); }
        _log(Loc.Tr("log.layout.deleted", item.Name));
        return new LayoutSaveResult(LayoutSaveStatus.Saved, id);
    }, changed: true);

    // 전체 백업·복원은 LayoutBackupService 에 있다(공개 API 는 그대로 두고 1줄 전달).
    public Task<IReadOnlyList<BackupEntry>> ListBackupsAsync() => _backups.ListAsync();

    public Task<LayoutSaveResult> BackupAllAsync(string? name, bool overwrite) => _backups.BackupAllAsync(name, overwrite);

    public Task<LayoutSaveResult> RestoreAllAsync(string id) => _backups.RestoreAllAsync(id);

    public Task<LayoutSaveResult> DeleteBackupAsync(string id) => _backups.DeleteBackupAsync(id);

    /// <summary>복원·실패 뒤 목록 캐시를 버려 다음 호출 때 저장소에서 다시 읽게 한다.</summary>
    internal void ResetIndex() => _index = null;

    public bool ApplyTemplate(string templateId)
    {
        var ok = LayoutTemplates.Apply(_shell, templateId);
        _log(ok
            ? Loc.Tr("log.layout.templateApplied", LayoutTemplates.All.First(t => t.Id == templateId).Name)
            : Loc.Tr("log.layout.templateMissing", templateId));
        return ok;
    }

    internal static string ContentKey(string id) => "layout." + id;

    /// <summary>이름 변경·삭제를 거부하는 읽기 전용 항목: 자동 저장 슬롯과 내장 템플릿(tpl.*) — 없는 항목(NotFound)과 구분한다(Android #122).</summary>
    private static bool IsReadOnlyId(string id) => id == ILayoutLibrary.AutoSavedId || LayoutTemplates.IsTemplateId(id);

    internal async Task<T> Locked<T>(Func<Task<T>> body, bool changed)
    {
        await _gate.WaitAsync();
        T result;
        try { result = await body(); }
        finally { _gate.Release(); }
        if (changed) { Changed?.Invoke(); }
        return result;
    }

    internal async Task<LayoutIndex> LoadIndexAsync()
    {
        if (_index is not null) { return _index; }
        var json = await SafeGetAsync(IndexKey);
        if (string.IsNullOrWhiteSpace(json)) { return _index = new LayoutIndex(); }
        try
        {
            _index = JsonSerializer.Deserialize(json, LayoutIndexJsonContext.Default.LayoutIndex) ?? new LayoutIndex();
        }
        catch (JsonException ex)
        {
            // 손상된 목록은 보관 키로 옮겨 두고 빈 목록으로 시작한다(다음 저장이 원본을 지우지 않게).
            _log(Loc.Tr("log.layout.indexCorrupt", ex.Message));
            try { await _store.SetAsync("index.corrupt-" + _clock().ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture), json); }
            catch (Exception backupEx) { _log(Loc.Tr("log.layout.indexBackupFailed", backupEx.Message)); }
            _index = new LayoutIndex();
        }

        return _index;
    }

    private Task SaveIndexAsync(LayoutIndex index) =>
        _store.SetAsync(IndexKey, JsonSerializer.Serialize(index, LayoutIndexJsonContext.Default.LayoutIndex));

    /// <summary>목록 저장. 실패하면 failureKey(형식 {0}=항목 이름, {1}=원인)로 로그를 남기고 false.</summary>
    private async Task<bool> TrySaveIndexAsync(LayoutIndex index, string failureKey, string name)
    {
        try
        {
            await SaveIndexAsync(index);
            return true;
        }
        catch (Exception ex)
        {
            _log(Loc.Tr(failureKey, name, ex.Message));
            _index = null;
            return false;
        }
    }

    internal async Task<string?> SafeGetAsync(string key)
    {
        try { return await _store.GetAsync(key); }
        catch (Exception ex)
        {
            _log(Loc.Tr("log.layout.readFailed", key, ex.Message));
            return null;
        }
    }

    private async Task<string?> SafeLoadAutoAsync()
    {
        try { return await _autoStore.LoadAsync(); }
        catch (Exception ex)
        {
            _log(Loc.Tr("log.layout.autoReadFailed", ex.Message));
            return null;
        }
    }
}
