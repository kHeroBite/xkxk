// Snap Assist 표시 이음매와 값 형식 — Shell 은 화면을 모르고 ISnapAssistOverlay 로만 그리게 한다(11 문서 §3.9)
using System;
using System.Collections.Generic;
using Avalonia;

namespace xkxk.frameworks.ui.shell.Docking;

/// <summary>남은 칸 창 고르기를 띄우는 기준 영역 범위. Mdi = MDI 영역, Managed = 웹·Android 관리형 창 레이어, Sdi = 데스크톱 SDI(같은 모니터 작업 영역).</summary>
public enum SnapAssistScope { Mdi, Managed, Sdi }

/// <summary>
/// 세션을 연 진입 경로. BarCell = 끌기 막대 칸, Edge = 좌우 가장자리 반쪽, Corner = 모서리 4분의 1, Flyout = 최대화 단추 호버 팝업 칸.
/// batch 2 계약: WindowMenuOps.ApplyCell(target, layoutId, cell, SnapAssistSource? assist = null) 의 선택 인자 형식이다.
/// null(창 메뉴·단축키·원격 snapcell.apply·세션 Pick)이면 세션을 띄우지 않는다.
/// </summary>
public enum SnapAssistSource { BarCell, Edge, Corner, Flyout }

/// <summary>칸 하나. Cell = 칸 번호(칸 종류 안 0부터), Rect = 기준 영역 좌표의 칸 사각형(SnapZoneContext.CellTarget), Filled = 배치한 칸·이번 세션에 고른 칸.</summary>
public sealed record SnapAssistZone(int Cell, Rect Rect, bool Filled);

/// <summary>
/// 후보 창 하나. Key = 창 식별(세션 안 제외 목록 기준), Title = 카드 제목(창 제목), Target = 배치 대상(ApplyCell 인자),
/// ThumbSource = 썸네일을 찍을 화면 객체(MDI 창 Control·관리형 창 Control·OS Window)를 opaque 로 넘긴다. 비트맵 생성은 Views 가 한다. 못 찾으면 null(아이콘+제목 카드).
/// </summary>
public sealed record SnapAssistCandidate(string Key, string Title, WindowMenuTarget Target, object? ThumbSource);

/// <summary>
/// 오버레이가 그릴 한 장면. Area = 기준 영역(anchor 좌표 DIP — Mdi/Managed 는 anchor Control 기준, Sdi 는 모니터 작업 영역 DIP),
/// Zones = 남은 칸과 채운 칸(채운 칸은 그리지 않는다), ActiveCell = 후보 목록을 붙일 칸, Candidates = 활성 칸 후보(위가 먼저),
/// FocusIndex = 초점 후보 번호(후보 0개면 -1), Thumbnails = 썸네일 옵션(EffectiveSnapAssistThumbnails).
/// </summary>
public sealed record SnapAssistView(SnapAssistScope Scope, Rect Area, string LayoutId,
    IReadOnlyList<SnapAssistZone> Zones, int ActiveCell, IReadOnlyList<SnapAssistCandidate> Candidates,
    int FocusIndex, bool Thumbnails);

/// <summary>
/// Views 가 구현하는 표시 이음매. 모든 호출은 UI 스레드다.
/// 입력은 오버레이가 받아 SnapAssistSession API 로 넘긴다: 카드 클릭·Enter·Space → Pick, 흐린 칸 클릭 → SelectZone,
/// Tab·방향키 → MoveFocus, Esc → Cancel(SnapAssistEndReason.Esc), 바깥 클릭 → Cancel(Outside), 포커스 이탈·TopLevel 비활성 → Cancel(Focus).
/// Pick 처리 중(배치가 활성 창을 바꿔 생기는) Outside·Focus 취소는 세션이 무시하므로, 오버레이는 Update 를 받으면 포커스를 다시 가져간다.
/// </summary>
public interface ISnapAssistOverlay
{
    /// <summary>처음 표시 + 포커스 가져가기.</summary>
    void Show(SnapAssistView view);

    /// <summary>칸·후보·초점 바뀜.</summary>
    void Update(SnapAssistView view);

    /// <summary>세션 끝. 그 뒤 이 인스턴스는 다시 쓰지 않는다(썸네일 해제 시점).</summary>
    void Hide();
}

/// <summary>오버레이 생성 등록 지점(SnapOverlayHost 와 같은 방식 — Views 의 [ModuleInitializer] 가 ??= 로 등록).</summary>
public static class SnapAssistHost
{
    /// <summary>
    /// anchor: Mdi = MDI 배치 패널(MdiLayoutPanel, Control), Managed = 관리형 창 레이어의 배치 패널(Control), Sdi = 배치한 창(Window).
    /// 등록이 없거나 null 을 돌려주면 표시 없이 끝난다(LastSkipReason = no-overlay).
    /// </summary>
    public static Func<SnapAssistScope, object, ISnapAssistOverlay?>? Factory { get; set; }
}

/// <summary>세션 끝 사유(SnapAssistSession.Ended 인자·Cancel 인자). 오버레이와 원격 진단이 같은 문자열을 쓴다.</summary>
public static class SnapAssistEndReason
{
    /// <summary>남은 칸이 다 찼다.</summary>
    public const string Filled = "filled";
    /// <summary>Esc 키.</summary>
    public const string Esc = "esc";
    /// <summary>카드·흐린 칸 밖 클릭(배치한 창 포함), 세션 중 다른 창 끌기 시작.</summary>
    public const string Outside = "outside";
    /// <summary>포커스 이탈(TopLevel 비활성·오버레이 밖으로 포커스 이동, 웹 탭 숨김).</summary>
    public const string Focus = "focus";
    /// <summary>기준 영역 크기 변경.</summary>
    public const string Resize = "resize";
    /// <summary>UI 방식 전환(PC형 → 태블릿형·모바일형).</summary>
    public const string Mode = "mode";
    /// <summary>배치한 창이 닫혔다.</summary>
    public const string Closed = "closed";
    /// <summary>원격 취소·새 세션이 이전 세션을 대신함.</summary>
    public const string Cancel = "cancel";
    /// <summary>남은 칸이 있지만 고를 후보가 더 없다(건너뛴 칸 포함, 11 문서 §5.7 — filled 와 구분).</summary>
    public const string NoCandidates = "no-candidates";
}
