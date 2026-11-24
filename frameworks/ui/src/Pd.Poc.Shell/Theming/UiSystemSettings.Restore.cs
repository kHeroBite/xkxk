// UI 설정 시스템 묶음의 복원 뷰 생성 방식(즉시/지연, B5) — UiSystemSettings.cs 줄 수 상한으로 나눈 부분
namespace Pd.Poc.Shell.Theming;

/// <summary>
/// 레이아웃 복원 때 문서 뷰를 언제 만들지. Deferred(기본): 보이는 문서만 즉시 만들고 나머지는 보게 될 때·유휴 시간에 1개씩.
/// Immediate: 복원한 문서를 전부 바로 만든다(이전 동작). 판정·순서는 Docking/RestoreViewDeferral 이 한다.
/// </summary>
public enum RestoreViewCreation
{
    Deferred,
    Immediate,
}

internal sealed partial class UiSystemSettings
{
    private RestoreViewCreation _restoreViewCreation;

    /// <summary>복원 뷰 생성 방식(저장값, 기본 지연). 바꾸면 즉시 저장하고 다음 레이아웃 복원부터 적용된다.</summary>
    public RestoreViewCreation RestoreViewCreation
    {
        get => _restoreViewCreation;
        set { if (SetProperty(ref _restoreViewCreation, value)) { _owner.SaveUnlessLoading(); } }
    }

    public void FillRestoreDto(UiSettingsDto dto) => dto.RestoreViewCreation = _restoreViewCreation;

    public void LoadRestoreDto(UiSettingsDto dto) => RestoreViewCreation = dto.RestoreViewCreation ?? default; // 옛 저장본(필드 없음)은 지연
}
