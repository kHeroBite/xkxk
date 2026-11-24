// UI 설정 시스템 묶음의 MDI 끌기 옵션 A(탭 합류 근접 무장)·B(정지 창 크롬 프레임 분산) 항목 — UiSystemSettings.Timing.cs 줄 수 상한으로 나눈 부분
namespace xkxk.frameworks.ui.shell.Theming;

internal sealed partial class UiSystemSettings
{
    private bool _mdiTabJoinArmNear = Docking.MdiMoveLight.DefaultTabJoinArmNear;

    /// <summary>탭 합류 표적 무장을 창 근접 때만(기본 켬 — 정본 Docking.MdiMoveLight.DefaultTabJoinArmNear, 01a 옵션 A). 바꾸면 즉시 저장하고 반영한다.</summary>
    public bool MdiTabJoinArmNear
    {
        get => _mdiTabJoinArmNear;
        set
        {
            if (SetProperty(ref _mdiTabJoinArmNear, value)) { _owner.SaveUnlessLoading(); }
            Docking.MdiMoveLight.TabJoinArmNear = _mdiTabJoinArmNear;
        }
    }

    private double _mdiTabJoinArmRadius = Docking.MdiMoveLight.DefaultTabJoinArmRadius;

    /// <summary>근접 무장 반경 px(기본 96 — 정본 Docking.MdiMoveLight.DefaultTabJoinArmRadius, 범위 밖 값은 정본이 24~400 으로 잘라 맞춘다). 바꾸면 즉시 저장하고 반영한다.</summary>
    public double MdiTabJoinArmRadius
    {
        get => _mdiTabJoinArmRadius;
        set
        {
            Docking.MdiMoveLight.TabJoinArmRadius = value;
            if (SetProperty(ref _mdiTabJoinArmRadius, Docking.MdiMoveLight.TabJoinArmRadius)) { _owner.SaveUnlessLoading(); }
        }
    }

    private int _mdiMoveLightSiblingsPerFrame = Docking.MdiMoveLight.DefaultSiblingsPerFrame;

    /// <summary>정지 창 가벼운 크롬 프레임당 창 수(기본 0=한 번에 전부, 범위 0~16 — 정본 Docking.MdiMoveLight.DefaultSiblingsPerFrame, 옵션 B). 바꾸면 즉시 저장하고 반영한다.</summary>
    public int MdiMoveLightSiblingsPerFrame
    {
        get => _mdiMoveLightSiblingsPerFrame;
        set
        {
            Docking.MdiMoveLight.SiblingsPerFrame = value;
            if (SetProperty(ref _mdiMoveLightSiblingsPerFrame, Docking.MdiMoveLight.SiblingsPerFrame)) { _owner.SaveUnlessLoading(); }
        }
    }

    private int _mdiMoveLightRestorePerFrame = Docking.MdiMoveLight.DefaultRestorePerFrame;

    /// <summary>놓은 뒤 정지 창 복원 프레임당 창 수(기본 0=한 번에 전부, 범위 0~16 — 정본 Docking.MdiMoveLight.DefaultRestorePerFrame, 01a P2). 바꾸면 즉시 저장하고 반영한다.</summary>
    public int MdiMoveLightRestorePerFrame
    {
        get => _mdiMoveLightRestorePerFrame;
        set
        {
            Docking.MdiMoveLight.RestorePerFrame = value;
            if (SetProperty(ref _mdiMoveLightRestorePerFrame, Docking.MdiMoveLight.RestorePerFrame)) { _owner.SaveUnlessLoading(); }
        }
    }

    private void FillSpreadDto(UiSettingsDto dto)
    {
        dto.MdiTabJoinArmNear = _mdiTabJoinArmNear;
        dto.MdiTabJoinArmRadius = _mdiTabJoinArmRadius;
        dto.MdiMoveLightSiblingsPerFrame = _mdiMoveLightSiblingsPerFrame;
        dto.MdiMoveLightRestorePerFrame = _mdiMoveLightRestorePerFrame;
    }

    private void LoadSpreadDto(UiSettingsDto dto)
    {
        MdiTabJoinArmNear = dto.MdiTabJoinArmNear ?? Docking.MdiMoveLight.DefaultTabJoinArmNear; // 옛 저장본(필드 없음)은 기본값(켬), 저장된 false 는 그대로
        MdiTabJoinArmRadius = dto.MdiTabJoinArmRadius ?? Docking.MdiMoveLight.DefaultTabJoinArmRadius; // 옛 저장본(필드 없음)은 96
        MdiMoveLightSiblingsPerFrame = dto.MdiMoveLightSiblingsPerFrame ?? Docking.MdiMoveLight.DefaultSiblingsPerFrame; // 옛 저장본(필드 없음)은 0
        MdiMoveLightRestorePerFrame = dto.MdiMoveLightRestorePerFrame ?? Docking.MdiMoveLight.DefaultRestorePerFrame; // 옛 저장본(필드 없음)은 0
    }
}
