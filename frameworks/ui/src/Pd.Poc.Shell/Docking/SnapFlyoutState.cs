// 최대화·이전 크기 단추 호버 배치 칸 팝업의 상태와 시험 진입점 — 화면 층(Pd.Ui.Views.Snap.SnapFlyoutHover)이 값을 쓰고 원격 명령이 읽는다(상태 전용, 그리기 없음)
using System;
using System.Collections.Generic;
using Pd.Poc.Shell.Theming;

namespace Pd.Poc.Shell.Docking;

/// <summary>
/// 사용자 요구(2026-11-11 16:58): 최대화·이전 크기 단추 위에 포인터가 SnapFlyoutDelayMs 동안 머무르면 단추 아래에 칸 종류 팝업을 띄운다.
/// 이 클래스는 셸 층(Pd.Poc.Shell)이 화면 층(Pd.Ui.Views)을 참조하지 않게 상태와 진입점만 둔다(레이어 분리).
/// 진입점(Hover·Leave·Pick·Close)은 화면 층이 시작 때 채운다. 채우기 전이면 null 이라 원격 명령은 '미설치' 를 돌려준다.
/// 모든 호출은 UI 스레드에서 한다.
/// </summary>
public static class SnapFlyoutState
{
    /// <summary>팝업을 띄울 수 있는 옵션 상태인가(SnapEnabled 와 SnapFlyoutEnabled 가 모두 켜짐).</summary>
    public static bool Enabled => UiSettings.Current.EffectiveSnapFlyoutEnabled;

    /// <summary>지금 머무름 지연을 재는 중인가(포인터가 단추 위, 아직 팝업 전).</summary>
    public static bool Pending { get; set; }

    /// <summary>팝업이 열려 있는가.</summary>
    public static bool IsOpen { get; set; }

    /// <summary>열린(또는 지연 중인) 팝업의 대상 창. 없으면 null.</summary>
    public static WindowMenuTarget? Target { get; set; }

    /// <summary>포인터가 올라간 칸. 없으면 (null, -1).</summary>
    public static (string? LayoutId, int Cell) Highlighted { get; set; } = (null, -1);

    /// <summary>마지막으로 띄우지 않은 이유(시험·로그용). 예: touch, disabled, no-target, no-cell, left-before-delay.</summary>
    public static string? LastSkipReason { get; set; }

    /// <summary>마지막 포인터 호버 판정(enter·leave·to-popup·touch, 진단용). 실마우스 이동이 판정에 닿는지 본다.</summary>
    public static string? LastHover { get; set; }

    /// <summary>호버 판정이 받은 TopLevel 포인터 이동 수(진단용 — 0 이면 이동 이벤트가 오지 않는 것).</summary>
    public static int PointerMoves { get; set; }

    /// <summary>시험 진입점: 대상 창의 최대화 단추에 포인터가 들어온 것처럼 지연을 시작한다. 단추를 찾았으면 true.</summary>
    public static Func<WindowMenuTarget, bool>? Hover { get; set; }

    /// <summary>시험 진입점: 포인터가 단추와 팝업을 모두 벗어난 것처럼 처리한다(지연 취소·팝업 닫기).</summary>
    public static Action? Leave { get; set; }

    /// <summary>시험 진입점: 열린 팝업의 칸을 누른 것처럼 처리한다(칸 클릭과 같은 경로). 적용했으면 true.</summary>
    public static Func<string, int, bool>? Pick { get; set; }

    /// <summary>시험 진입점: 열린 팝업을 닫는다(Esc 와 같은 경로).</summary>
    public static Action? Close { get; set; }

    /// <summary>팝업에 그릴 칸 종류(창 메뉴 '배치' 와 같은 목록, 옵션 SnapLayouts 반영).</summary>
    public static IReadOnlyList<SnapLayout> Layouts => WindowMenuOps.PlacementLayouts;

    /// <summary>대상에 적용할 수 있는 칸이 하나라도 있는가(없으면 팝업을 띄우지 않는다 — 위치 고정·DOCK·NAV·기준 영역 없음).</summary>
    public static bool HasApplicableCell(WindowMenuTarget target)
    {
        foreach (var layout in Layouts)
        {
            for (var i = 0; i < layout.Cells.Count; i++)
            {
                if (WindowMenuOps.CanApplyCell(target, layout.Id, i))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
