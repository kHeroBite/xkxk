// UI-11 레이아웃 라이브러리 계약 — 이름 붙인 개인 레이아웃 저장·불러오기·이름 변경·삭제, 내장 템플릿, 키-값 저장소
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Pd.Client.Data.Layout;

/// <summary>키-값 문자열 저장소. 데스크톱·Android = 폴더 파일(FileKeyValueStore), 브라우저 = localStorage(호스트 구현).</summary>
public interface IKeyValueStore
{
    /// <summary>값. 없으면 null.</summary>
    Task<string?> GetAsync(string key);

    Task SetAsync(string key, string value);

    /// <summary>없는 키를 지워도 오류가 아니다.</summary>
    Task DeleteAsync(string key);
}

/// <summary>저장소 미주입 시 쓰는 메모리 저장소(프로세스 종료 시 소멸).</summary>
public sealed class MemoryKeyValueStore : IKeyValueStore
{
    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

    public Task<string?> GetAsync(string key) => Task.FromResult(_values.TryGetValue(key, out var v) ? v : null);

    public Task SetAsync(string key, string value)
    {
        _values[key] = value;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string key)
    {
        _values.Remove(key);
        return Task.CompletedTask;
    }
}

/// <summary>목록 항목 종류.</summary>
public enum LayoutEntryKind
{
    /// <summary>종료 자동 저장 슬롯("마지막 종료 화면") — 읽기 전용(불러오기·다른 이름으로 저장만).</summary>
    AutoSaved,

    /// <summary>사용자가 이름 붙여 저장한 레이아웃.</summary>
    Personal,
}

/// <summary>레이아웃 목록 항목. 이름은 목록(index)에만 있고 내용과 분리된다(이름 변경 = 내용 불변).</summary>
public sealed record LayoutEntry(string Id, string Name, LayoutEntryKind Kind, DateTimeOffset UpdatedAt, int DocumentCount)
{
    public bool IsReadOnly => Kind == LayoutEntryKind.AutoSaved;
}

/// <summary>내장 템플릿(읽기 전용, 저장소에 쓰지 않는다).</summary>
public sealed record LayoutTemplate(string Id, string Name, string Description);

public enum LayoutSaveStatus
{
    Saved,

    /// <summary>이름이 비었다(공백만 포함).</summary>
    NameEmpty,

    /// <summary>같은 이름이 있다. 저장은 overwrite:true 로 다시 부르면 덮어쓴다. 이름 변경은 거부(덮어쓰기 없음).</summary>
    NameExists,

    /// <summary>읽기 전용 항목(자동 저장 슬롯·템플릿)이다.</summary>
    ReadOnly,

    /// <summary>대상 항목이 없다.</summary>
    NotFound,

    /// <summary>저장소 쓰기 실패(로그에 원인).</summary>
    Failed,
}

public sealed record LayoutSaveResult(LayoutSaveStatus Status, string? Id = null)
{
    public bool Ok => Status == LayoutSaveStatus.Saved;
}

/// <summary>
/// 셸이 App.Layouts 로 공개하는 레이아웃 라이브러리. 메뉴·관리 창·원격(odev-6r)이 같은 표면을 쓴다. UI 스레드에서 호출한다.
/// 확인 대화(덮어쓰기·삭제)는 화면 전용이다 — 원격은 결과 상태를 보고 인자로 결정한다.
/// </summary>
public interface ILayoutLibrary
{
    /// <summary>자동 저장 슬롯 Id(목록 첫 항목, 읽기 전용).</summary>
    const string AutoSavedId = "auto";

    /// <summary>자동 저장 슬롯 + 개인 레이아웃(최근 수정 순).</summary>
    Task<IReadOnlyList<LayoutEntry>> ListAsync();

    /// <summary>새 저장 기본 이름(현재 시각 yyyy-MM-dd HH:mm:ss).</summary>
    string DefaultName();

    /// <summary>현재 화면을 이름 붙여 저장. 같은 이름이 있으면 overwrite=false → NameExists, true → 그 항목 내용 교체.</summary>
    Task<LayoutSaveResult> SaveCurrentAsync(string? name, bool overwrite);

    /// <summary>Id 또는 이름으로 불러오기(현재 문서를 닫고 저장 상태로 복원). AutoSavedId 는 자동 저장 슬롯. 없으면 false.</summary>
    Task<bool> LoadAsync(string idOrName);

    /// <summary>이름만 변경(내용 불변). 다른 항목과 같은 이름이면 NameExists 로 거부.</summary>
    Task<LayoutSaveResult> RenameAsync(string id, string newName);

    /// <summary>개인 레이아웃 삭제. 자동 저장 슬롯은 ReadOnly.</summary>
    Task<LayoutSaveResult> DeleteAsync(string id);

    IReadOnlyList<LayoutTemplate> Templates { get; }

    /// <summary>템플릿 적용(열린 문서는 유지, 배치만 바꿈). 없는 Id 면 false.</summary>
    bool ApplyTemplate(string templateId);

    /// <summary>목록이 바뀌면(저장·이름 변경·삭제·백업·복원) 알린다 — 관리 창 갱신용.</summary>
    event Action? Changed;

    /// <summary>⑫ 전체 백업 목록(최근 순).</summary>
    Task<IReadOnlyList<BackupEntry>> ListBackupsAsync();

    /// <summary>⑫ 전체 백업: 설정·자동 저장 슬롯·개인 레이아웃 전부를 이름 붙인 묶음 1개로 저장. 같은 이름이 있으면 overwrite 규칙은 SaveCurrentAsync 와 같다.</summary>
    Task<LayoutSaveResult> BackupAllAsync(string? name, bool overwrite);

    /// <summary>⑫ 전체 복원: 먼저 현재 상태를 자동 백업한 뒤 묶음을 저장소에 되돌리고, 재시작 없이 설정과 화면 배치를 다시 적용한다.</summary>
    Task<LayoutSaveResult> RestoreAllAsync(string id);

    Task<LayoutSaveResult> DeleteBackupAsync(string id);
}

/// <summary>전체 백업 목록 항목. 자동 백업(복원 직전 생성)은 IsAuto=true.</summary>
public sealed record BackupEntry(string Id, string Name, DateTimeOffset CreatedAt, int LayoutCount, bool IsAuto);
