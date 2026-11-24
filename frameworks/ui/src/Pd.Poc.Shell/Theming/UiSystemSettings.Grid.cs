// UI 설정 시스템 묶음의 표 항목(화면 밖 셀 지연·단일 요소 셀·그리는 셀 글자·준비된 표 뷰) — UiSystemSettings.Timing.cs 줄 수 상한으로 나눈 부분
namespace Pd.Poc.Shell.Theming;

internal sealed partial class UiSystemSettings
{
    /// <summary>표 화면 밖 열 셀 지연 실현 기본값(켬 — 정본 plan/09_화면설계/01_화면설계_공통원칙.md §5.4, A16).</summary>
    public const bool DefaultGridDeferOffscreenCells = true;

    private bool _gridDeferOffscreenCells = DefaultGridDeferOffscreenCells;

    /// <summary>표 화면 밖 열 셀 지연 실현(기본 켬). 셀 내용을 그 셀이 처음 화면에 들어올 때 만든다. 다음에 여는 표부터 반영된다.</summary>
    public bool GridDeferOffscreenCells
    {
        get => _gridDeferOffscreenCells;
        set { if (SetProperty(ref _gridDeferOffscreenCells, value)) { _owner.SaveUnlessLoading(); } }
    }

    /// <summary>표 단일 요소 셀 기본값(켬 — 정본 plan/09_화면설계/01_화면설계_공통원칙.md §5.4, A16. 세로선 없는 표의 셀 템플릿을 PdCellChrome 하나로 만들어 첫 측정 셀 구간을 줄인다).</summary>
    public const bool DefaultGridCompactCells = true;

    private bool _gridCompactCells = DefaultGridCompactCells;

    /// <summary>표 단일 요소 셀(기본 켬). 세로선 없는 표만 해당한다. 다음에 여는 표부터 반영된다.</summary>
    public bool GridCompactCells
    {
        get => _gridCompactCells;
        set { if (SetProperty(ref _gridCompactCells, value)) { _owner.SaveUnlessLoading(); } }
    }

    /// <summary>
    /// 그리는 셀 글자 기본값(켬 — 정본 plan/09_화면설계/01_화면설계_공통원칙.md §5.4 단일 요소 셀 항목, K15).
    /// 단일 요소 셀(GridCompactCells)의 텍스트 열 셀에서 TextBlock 을 만들지 않고 셀 뿌리(PdCellChrome)가 글자를 직접 측정·그린다.
    /// </summary>
    public const bool DefaultGridDrawnCellText = true;

    private bool _gridDrawnCellText = DefaultGridDrawnCellText;

    /// <summary>그리는 셀 글자(기본 켬). 단일 요소 셀이 켜진 표만 해당한다. 다음에 여는 표부터 반영된다.</summary>
    public bool GridDrawnCellText
    {
        get => _gridDrawnCellText;
        set { if (SetProperty(ref _gridDrawnCellText, value)) { _owner.SaveUnlessLoading(); } }
    }

    /// <summary>준비된 표 뷰 기본값(끔 — 정본 plan/09_화면설계/01_화면설계_공통원칙.md §5.4, A16. headless 대조에서 옮겨 붙일 때 스타일 재적용 비용이 생성 절감보다 커 켬이 더 느렸다, team-lead 결정 2026-11-14).</summary>
    public const bool DefaultGridViewPool = false;

    private bool _gridViewPool = DefaultGridViewPool;

    /// <summary>준비된 표 뷰(기본 끔). 유휴 시간에 표 뷰 1개를 화면 밖에서 미리 만들어 두었다가 다음 표 열기에 쓴다. 끄면 보관 중인 뷰를 버린다.</summary>
    public bool GridViewPool
    {
        get => _gridViewPool;
        set { if (SetProperty(ref _gridViewPool, value)) { _owner.SaveUnlessLoading(); } }
    }

    private void FillGridDto(UiSettingsDto dto)
    {
        dto.GridDeferOffscreenCells = _gridDeferOffscreenCells;
        dto.GridCompactCells = _gridCompactCells;
        dto.GridDrawnCellText = _gridDrawnCellText;
        dto.GridViewPool = _gridViewPool;
    }

    private void LoadGridDto(UiSettingsDto dto)
    {
        GridDeferOffscreenCells = dto.GridDeferOffscreenCells ?? DefaultGridDeferOffscreenCells; // 옛 저장본(필드 없음)은 켬
        GridCompactCells = dto.GridCompactCells ?? DefaultGridCompactCells; // 옛 저장본(필드 없음)은 켬
        GridDrawnCellText = dto.GridDrawnCellText ?? DefaultGridDrawnCellText; // 옛 저장본(필드 없음)은 켬
        GridViewPool = dto.GridViewPool ?? DefaultGridViewPool; // 옛 저장본(필드 없음)은 끔
    }
}
