// UI 설정 저장·복원 담당 — 호스트 저장소에 직렬화 저장(겹침 방지·종료 플러시), 기동 복원(Loaded 신호), 시험 인자 임시 항목은 저장값 유지
using Pd.Client.Data.Layout;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Threading;
using Pd.Poc.Shell.Docking;

namespace Pd.Poc.Shell.Theming;

/// <summary>UiSettings 의 저장 계층. 값 자체는 UiSettings 가 가지며, 이 클래스는 언제·무엇을 쓰고 읽는지만 맡는다.</summary>
internal sealed class UiSettingsStore : ISettingsBackupSource
{
    private readonly UiSettings _owner;
    private readonly TaskCompletionSource _loaded = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private ILayoutStore? _store;
    private bool _saving;       // 저장 진행 중(SaveAsync 직렬화)
    private bool _savePending;  // 진행 중에 들어온 저장 요청
    private IReadOnlySet<string> _transient = new HashSet<string>();
    private UiSettingsDto? _diskDto; // 기동 때 읽은 저장값(없으면 기본값)

    public UiSettingsStore(UiSettings owner) => _owner = owner;

    /// <summary>저장값 복원 중(이 동안 setter 는 저장하지 않는다).</summary>
    public bool Loading { get; private set; }

    /// <summary>저장값 복원이 끝나면 완료(저장소가 없거나 손상돼도 완료). 기동 자동 복원은 이 값을 본 뒤 판단한다.</summary>
    public Task Loaded => _loaded.Task;

    public void SetStore(ILayoutStore? store) => _store = store;

    /// <summary>
    /// 이번 실행에만 적용할 설정 이름(AutomationBridge.TransientSettingArgs 결과: theme·font·font-size·shadow·frame, zoom 은 레이아웃 값이라 무시).
    /// 앱 시작 때 Install 전에 1회 설정한다. 이 항목은 실행 중 누가 바꿔도 저장 파일에는 기동 때 값이 남는다(사용자 값 보호).
    /// </summary>
    public void SetTransient(IReadOnlySet<string> names) => _transient = names;

    public void SaveUnlessLoading()
    {
        if (!Loading)
        {
            _ = SaveAsync();
        }
    }

    /// <summary>
    /// 종료 시 플러시(App Closing): 진행 중 저장이 끝나기를 최대 max 만큼 기다린 뒤, 마지막 값을 한 번 더 써서 그 완료를 기다린다(쓰기도 max 상한).
    /// 이후의 저장 요청은 무시한다(종료 중 옛 스냅샷이 덮어쓰지 않게).
    /// </summary>
    public async Task FlushAsync(TimeSpan max)
    {
        var deadline = DateTime.UtcNow + max;
        while (_saving && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }

        _savePending = false;
        _saving = true; // 이후 SaveAsync 는 대기 표시만 남기고 실제로 쓰지 않음(종료 중)
        await Task.WhenAny(WriteSnapshotAsync(), Task.Delay(max));
    }

    private async Task WriteSnapshotAsync()
    {
        if (_store is null) { return; }
        try { await _store.SaveAsync(SnapshotJson()); }
        catch (Exception) { /* 저장 실패는 화면 동작에 영향 없음(다음 변경 때 다시 저장) */ }
    }

    /// <summary>저장할 내용 그대로의 JSON — 현재 값, 시험 인자로 바꾼 항목은 저장값 유지.</summary>
    private string SnapshotJson()
    {
        var dto = _owner.CreateDto();
        KeepTransientFromDisk(dto);
        return JsonSerializer.Serialize(dto, UiSettingsJsonContext.Default.UiSettingsDto);
    }

    /// <summary>⑫ 전체 백업: 설정 파일이 아직 없으면(새 프로필) 지금 적용 중인 설정(기본값 포함)을 담는다(D-BACKUP-NOSET).</summary>
    public async Task<string?> CaptureAsync()
    {
        if (_store is null) { return null; }
        var json = await _store.LoadAsync();
        return string.IsNullOrWhiteSpace(json) ? SnapshotJson() : json;
    }

    /// <summary>⑫ 전체 복원: 설정 없이 만든 옛 백업(null)이면 기본 설정을 쓴다 — 이어지는 ReloadAsync 가 기본값으로 되돌린다.</summary>
    public Task WriteAsync(string? json) =>
        _store?.SaveAsync(json ?? JsonSerializer.Serialize(new UiSettingsDto(), UiSettingsJsonContext.Default.UiSettingsDto)) ?? Task.CompletedTask;

    Task ISettingsBackupSource.ReloadAsync() => LoadAsync();

    /// <summary>
    /// 저장 직렬화: 저장이 진행 중이면 표시만 해 두고, 끝나면 그 시점의 최신 값으로 한 번 더 저장한다(UI 스레드 전용).
    /// 겹쳐 실행되면 먼저 시작한 옛 스냅샷이 나중에 끝나 최신 값(예: Ctrl+휠 모드)을 덮어쓸 수 있었다(otest-1 복원 결함 원인 후보).
    /// </summary>
    public async Task SaveAsync()
    {
        if (_store is null) { return; }
        if (_saving) { _savePending = true; return; }
        _saving = true;
        try
        {
            do
            {
                _savePending = false;
                await WriteSnapshotAsync(); // 그 시점 최신 값 스냅샷
            }
            while (_savePending);
        }
        finally { _saving = false; }
    }

    public async Task LoadAsync()
    {
        if (_store is null)
        {
            return;
        }

        try
        {
            var json = await _store.LoadAsync();
            if (string.IsNullOrWhiteSpace(json)
                || JsonSerializer.Deserialize(json, UiSettingsJsonContext.Default.UiSettingsDto) is not { } dto)
            {
                return;
            }

            Loading = true;
            _diskDto = dto;
            _owner.LoadFromDto(dto);
        }
        catch (Exception) { /* 손상된 설정은 무시하고 기본값 유지 */ }
        finally
        {
            Loading = false;
        }
    }

    /// <summary>저장값 복원 후 Loaded 를 완료한다(실패해도 완료 — 기동이 멈추지 않게).</summary>
    public async Task LoadAndSignalAsync()
    {
        try
        {
            await LoadAsync();
        }
        finally
        {
            _loaded.TrySetResult();
            // 저장 직렬화 예열: 첫 저장의 1회 비용(DTO·JSON 직렬화기 초기화, headless 웹 33~44ms)이 첫 설정 변경(예: 디자인 테마 전환) 프레임에 들어가지 않게
            // 유휴 시간에 스냅샷을 한 번 만들어 버린다. 쓰지는 않는다(저장소·파일 무변경, K13)
            Dispatcher.UIThread.Post(() =>
            {
                try { _ = SnapshotJson(); }
                catch (Exception) { /* 예열 실패는 무시 — 첫 저장이 그대로 초기화한다 */ }
            }, DispatcherPriority.ApplicationIdle);
        }
    }

    /// <summary>저장 직전: 임시 항목은 디스크 값(없으면 기본값)으로 되돌린 사본을 쓴다.</summary>
    private void KeepTransientFromDisk(UiSettingsDto dto)
    {
        if (_transient.Count == 0)
        {
            return;
        }

        var disk = _diskDto ?? new UiSettingsDto();
        if (Has("theme"))
        {
            dto.Theme = disk.Theme;
            dto.ThemeId = disk.ThemeId;
            dto.SystemLightThemeId = disk.SystemLightThemeId;
            dto.SystemDarkThemeId = disk.SystemDarkThemeId;
            dto.UserThemesJson = disk.UserThemesJson;
        }

        if (Has("font"))
        {
            dto.FontName = disk.FontName;
        }

        if (Has("font-size"))
        {
            dto.BaseFontSize = disk.BaseFontSize;
            dto.DocumentDefaultFontSize = disk.DocumentDefaultFontSize;
        }

        if (Has("shadow"))
        {
            dto.WindowShadow = disk.WindowShadow;
        }

        if (Has("frame"))
        {
            dto.ChromeStyle = disk.ChromeStyle;
            dto.MarsFrame = disk.MarsFrame;
        }
    }

    private bool Has(string name) => _transient.Contains(name);
}
