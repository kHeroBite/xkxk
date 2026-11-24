// 전체 백업·복원(윈도우 메뉴 ⑫)이 UI 설정을 다루는 데이터 계약 — 백업 때 현재 설정 JSON, 복원 때 쓰기(null 이면 기본값)·다시 적용
using System.Threading.Tasks;

namespace Pd.Client.Data.Layout;

/// <summary>LayoutBackupService 가 설정 저장소 대신 쓰는 입구. 구현은 Theming.UiSettingsStore 다.</summary>
public interface ISettingsBackupSource
{
    /// <summary>백업할 설정 JSON. 저장 파일이 아직 없으면 현재 적용 중인 설정(기본값 포함)을 직렬화해 돌려준다.</summary>
    Task<string?> CaptureAsync();

    /// <summary>복원할 설정 JSON 을 저장소에 쓴다. null(설정 없이 만든 옛 백업)이면 기본 설정을 쓴다.</summary>
    Task WriteAsync(string? json);

    /// <summary>저장소의 설정을 다시 읽어 화면에 적용한다(재시작 없이 반영).</summary>
    Task ReloadAsync();
}
