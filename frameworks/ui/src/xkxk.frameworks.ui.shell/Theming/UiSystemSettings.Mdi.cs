// UI 설정 시스템 묶음의 MDI 창 잔존 비율 항목(MdiMinVisiblePercent) — UiSystemSettings.cs 줄 수 상한으로 나눈 부분
using System;

namespace xkxk.frameworks.ui.shell.Theming;

internal sealed partial class UiSystemSettings
{
    // MDI 창 잔존 비율(정본 plan/09_화면설계/01_화면설계_공통원칙.md §4.3, 2026-11-10). 0 = 비율 끔(잡을 곳 규칙만).
    public const int DefaultMdiMinVisiblePercent = 25;
    public const int MinMdiMinVisiblePercent = 0;
    public const int MaxMdiMinVisiblePercent = 90;

    private int _mdiMinVisiblePercent = DefaultMdiMinVisiblePercent;

    /// <summary>MDI 창을 캔버스 밖으로 밀 때 남길 창 크기 비율(%, 기본 25, 0~90). 범위 밖 값은 잘라 맞춘다. 값은 MdiDragLimit 공급자가 읽는다.</summary>
    public int MdiMinVisiblePercent
    {
        get => _mdiMinVisiblePercent;
        set { if (SetProperty(ref _mdiMinVisiblePercent, ClampMdiMinVisiblePercent(value))) { _owner.SaveUnlessLoading(); } }
    }

    public static int ClampMdiMinVisiblePercent(int value) => Math.Clamp(value, MinMdiMinVisiblePercent, MaxMdiMinVisiblePercent);

    private void FillMdiDto(UiSettingsDto dto) => dto.MdiMinVisiblePercent = _mdiMinVisiblePercent;

    // 옛 저장본(필드 없음)은 25, 범위 밖 값은 setter 가 잘라 맞춘다
    private void LoadMdiDto(UiSettingsDto dto) => MdiMinVisiblePercent = dto.MdiMinVisiblePercent ?? DefaultMdiMinVisiblePercent;
}
