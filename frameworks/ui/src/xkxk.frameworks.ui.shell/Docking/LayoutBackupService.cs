// 레이아웃 전체 백업·복원 서비스(윈도우 메뉴 ⑫) — 설정·자동 저장 슬롯·개인 레이아웃을 이름 붙인 묶음 하나로 저장하고, 복원 전 현재 상태를 자동 백업한 뒤 재시작 없이 다시 적용한다
using xkxk.frameworks.ui.data.Layout;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using xkxk.frameworks.ui.shell.Localization;

namespace xkxk.frameworks.ui.shell.Docking;

/// <summary>
/// LayoutLibrary 가 보유하는 백업 협력 객체. 잠금(Locked)·목록 캐시·읽기 도우미는 라이브러리 것을 그대로 쓴다
/// (개인 레이아웃 작업과 백업 작업이 같은 잠금으로 직렬화되는 동작을 유지하기 위해서다).
/// </summary>
internal sealed class LayoutBackupService
{
    private const string BackupIndexKey = "backups";

    private readonly LayoutLibrary _lib;
    private readonly IKeyValueStore _store;
    private readonly IShellCommands _shell;
    private readonly ILayoutStore _autoStore;
    private readonly Action<string> _log;
    private readonly Func<DateTimeOffset> _clock;
    private readonly ISettingsBackupSource? _settings;

    public LayoutBackupService(LayoutLibrary lib, IKeyValueStore store, IShellCommands shell, ILayoutStore autoStore,
        Action<string> log, Func<DateTimeOffset> clock, ISettingsBackupSource? settings)
    {
        _lib = lib;
        _store = store;
        _shell = shell;
        _autoStore = autoStore;
        _log = log;
        _clock = clock;
        _settings = settings;
    }

    public async Task<IReadOnlyList<BackupEntry>> ListAsync() =>
        (await LoadBackupIndexAsync()).Items.OrderByDescending(i => i.CreatedAt)
            .Select(i => new BackupEntry(i.Id, i.Name, i.CreatedAt, i.LayoutCount, i.IsAuto)).ToList();

    public Task<LayoutSaveResult> BackupAllAsync(string? name, bool overwrite) =>
        _lib.Locked(() => BackupCoreAsync(name, overwrite, isAuto: false), changed: true);

    public Task<LayoutSaveResult> RestoreAllAsync(string id) => _lib.Locked(async () =>
    {
        var index = await LoadBackupIndexAsync();
        var item = index.Items.FirstOrDefault(i => i.Id == id);
        var json = item is null ? null : await _lib.SafeGetAsync(BackupKey(id));
        BackupBundle? bundle = null;
        try { bundle = json is null ? null : JsonSerializer.Deserialize(json, BackupJsonContext.Default.BackupBundle); }
        catch (JsonException ex) { _log(Loc.Tr("log.backup.corrupt", item?.Name, ex.Message)); }
        if (item is null || bundle is null) { return new LayoutSaveResult(LayoutSaveStatus.NotFound, id); }

        // 1) 되돌릴 수 있게 현재 상태를 먼저 자동 백업한다(team-lead 결정). 실패하면 복원하지 않는다.
        var stamp = _clock().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        var auto = await BackupCoreAsync(Loc.Tr("layout.backup.autoName", stamp), overwrite: true, isAuto: true);
        if (!auto.Ok) { return new LayoutSaveResult(LayoutSaveStatus.Failed, id); }

        try
        {
            // 2) 저장소에 되돌린다: 개인 레이아웃 내용 → 목록 → 자동 저장 슬롯 → 설정
            var oldIds = (await _lib.LoadIndexAsync()).Items.Select(i => i.Id).ToList();
            foreach (var (layoutId, layoutJson) in bundle.Layouts) { await _store.SetAsync(LayoutLibrary.ContentKey(layoutId), layoutJson); }
            await _store.SetAsync(LayoutLibrary.IndexKey, bundle.LibraryIndexJson ?? JsonSerializer.Serialize(new LayoutIndex(), LayoutIndexJsonContext.Default.LayoutIndex));
            foreach (var stale in oldIds.Where(o => !bundle.Layouts.ContainsKey(o))) { await _store.DeleteAsync(LayoutLibrary.ContentKey(stale)); }
            _lib.ResetIndex();
            if (bundle.AutoLayoutJson is not null) { await _autoStore.SaveAsync(bundle.AutoLayoutJson); }
            if (_settings is not null) { await _settings.WriteAsync(bundle.SettingsJson); } // null(설정 없는 옛 백업)이면 기본 설정으로 되돌린다
        }
        catch (Exception ex)
        {
            _log(Loc.Tr("log.backup.restoreFailed", item.Name, ex.Message));
            _lib.ResetIndex();
            return new LayoutSaveResult(LayoutSaveStatus.Failed, id);
        }

        // 3) 재시작 없이 다시 적용: 설정(테마·글자·언어·윈도우 메뉴 옵션) → 화면 배치(자동 저장 슬롯)
        if (_settings is not null) { await _settings.ReloadAsync(); }
        if (bundle.AutoLayoutJson is { } autoLayout) { await Busy.BusyHooks.RunLongAsync(Busy.BusyHooks.MainScope, "layout", () => _shell.ApplyLayout(autoLayout)); } // 복원: 스피너를 먼저 그린 뒤 복원한다
        _log(Loc.Tr("log.backup.restored", item.Name, bundle.Layouts.Count));
        return new LayoutSaveResult(LayoutSaveStatus.Saved, id);
    }, changed: true);

    public Task<LayoutSaveResult> DeleteBackupAsync(string id) => _lib.Locked(async () =>
    {
        var index = await LoadBackupIndexAsync();
        var item = index.Items.FirstOrDefault(i => i.Id == id);
        if (item is null) { return new LayoutSaveResult(LayoutSaveStatus.NotFound, id); }
        index.Items.Remove(item);
        try
        {
            await SaveBackupIndexAsync(index);
            await _store.DeleteAsync(BackupKey(id));
        }
        catch (Exception ex)
        {
            _log(Loc.Tr("log.backup.deleteFailed", item.Name, ex.Message));
            return new LayoutSaveResult(LayoutSaveStatus.Failed, id);
        }

        _log(Loc.Tr("log.backup.deleted", item.Name));
        return new LayoutSaveResult(LayoutSaveStatus.Saved, id);
    }, changed: true);

    /// <summary>잠금 안에서 호출. 현재 저장소 상태(설정·자동 슬롯·개인 레이아웃)를 묶어 저장한다.</summary>
    private async Task<LayoutSaveResult> BackupCoreAsync(string? name, bool overwrite, bool isAuto)
    {
        var trimmed = name?.Trim() ?? "";
        if (trimmed.Length == 0) { return new LayoutSaveResult(LayoutSaveStatus.NameEmpty); }
        var index = await LoadBackupIndexAsync();
        var existing = index.Items.FirstOrDefault(i => LayoutLibrary.NameComparer.Equals(i.Name, trimmed));
        if (existing is not null && !overwrite) { return new LayoutSaveResult(LayoutSaveStatus.NameExists, existing.Id); }

        var library = await _lib.LoadIndexAsync();
        var bundle = new BackupBundle
        {
            SettingsJson = _settings is null ? null : await _settings.CaptureAsync(), // 설정 파일이 없는 새 프로필이면 현재 적용 중인 설정
            // 지금 화면 배치(자동 저장 슬롯 파일은 건드리지 않음 — 자동저장을 끈 사용자의 '마지막 종료 화면'을 바꾸지 않는다)
            AutoLayoutJson = _shell.SerializeLayout(),
            LibraryIndexJson = JsonSerializer.Serialize(library, LayoutIndexJsonContext.Default.LayoutIndex),
        };
        foreach (var l in library.Items)
        {
            if (await _lib.SafeGetAsync(LayoutLibrary.ContentKey(l.Id)) is { } content) { bundle.Layouts[l.Id] = content; }
        }

        var item = existing ?? new BackupIndexItem { Id = Guid.NewGuid().ToString("N"), Name = trimmed };
        item.CreatedAt = _clock();
        item.LayoutCount = bundle.Layouts.Count;
        item.IsAuto = isAuto;
        try
        {
            await _store.SetAsync(BackupKey(item.Id), JsonSerializer.Serialize(bundle, BackupJsonContext.Default.BackupBundle));
            if (existing is null) { index.Items.Add(item); }
            await SaveBackupIndexAsync(index);
        }
        catch (Exception ex)
        {
            _log(Loc.Tr("log.backup.failed", trimmed, ex.Message));
            return new LayoutSaveResult(LayoutSaveStatus.Failed, item.Id);
        }

        _log(Loc.Tr("log.backup.done", trimmed, item.LayoutCount));
        return new LayoutSaveResult(LayoutSaveStatus.Saved, item.Id);
    }

    private static string BackupKey(string id) => "backup." + id;

    private async Task<BackupIndex> LoadBackupIndexAsync()
    {
        var json = await _lib.SafeGetAsync(BackupIndexKey);
        if (string.IsNullOrWhiteSpace(json)) { return new BackupIndex(); }
        try { return JsonSerializer.Deserialize(json, BackupJsonContext.Default.BackupIndex) ?? new BackupIndex(); }
        catch (JsonException ex)
        {
            _log(Loc.Tr("log.backup.indexCorrupt", ex.Message));
            return new BackupIndex();
        }
    }

    private Task SaveBackupIndexAsync(BackupIndex index) =>
        _store.SetAsync(BackupIndexKey, JsonSerializer.Serialize(index, BackupJsonContext.Default.BackupIndex));
}
