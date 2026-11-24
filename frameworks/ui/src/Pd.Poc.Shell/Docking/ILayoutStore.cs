// Shell 공유 계약(레이아웃 저장소·문서 종류·셸 명령 표면) — Wave1 선공유 계약. 문서 뷰 생성 계약(UI)은 IDocumentViewFactory.cs
using System.Collections.Generic;
using System.Threading.Tasks;
using Dock.Model.Core;

namespace Pd.Poc.Shell.Docking;

/// <summary>PoC 샘플 문서 종류.</summary>
public enum DocumentKind
{
    Grid,
    Gantt,
    Form,
}

/// <summary>문서 종류 표시명.</summary>
public static class DocumentKindNames
{
    /// <summary>현재 언어의 종류 이름(새 문서 제목 "그리드 3" 등에 쓰인다 — 만든 뒤에는 제목이 고정된다).</summary>
    public static string DisplayName(this DocumentKind kind) => Localization.Loc.Tr(kind switch
    {
        DocumentKind.Grid => "shell.kind.grid",
        DocumentKind.Gantt => "shell.kind.gantt",
        _ => "shell.kind.form",
    });
}

/// <summary>MDI 문서 배치 방식.</summary>
public enum ShellArrange
{
    Cascade,
    TileHorizontal,
    TileVertical,
    Tabbed,
}

/// <summary>레이아웃 프리셋 3종.</summary>
public enum LayoutPreset
{
    /// <summary>MDI + 계단식.</summary>
    MdiCascade,

    /// <summary>탭 문서(MDI 해제).</summary>
    Tabbed,

    /// <summary>MDI + 바둑판(세로 나란히).</summary>
    MdiTileVertical,
}

/// <summary>
/// AutomationBridge·메뉴·단축키가 호출하는 셸 명령 표면. 구현은 WindowCommands.
/// 모든 메서드는 UI 스레드에서 호출해야 한다.
/// </summary>
public interface IShellCommands
{
    /// <summary>현재 열린 문서(도킹·플로팅 포함), 열린 순서.</summary>
    IReadOnlyList<PocDocument> Documents { get; }

    PocDocument? ActiveDocument { get; }

    /// <summary>
    /// 문서 열기(MDI-01/02/20). sdi=true 면 열자마자 플로팅(OS 창 / 브라우저 관리형 창)으로 분리.
    /// title 은 문서 키 역할: title 이 있고 allowDuplicate=false(기본)이면 같은 종류·제목 문서를 새로 만들지 않고 활성화해 반환(Mars Fn중복창확인).
    /// title=null 이면 항상 새 문서(종류별 일련번호 제목). 새 MDI 창은 기존 창과 겹치지 않게 30px 계단으로 놓는다.
    /// </summary>
    PocDocument OpenDocument(DocumentKind kind, string? title = null, bool sdi = false, bool allowDuplicate = false);

    void Arrange(ShellArrange arrange);

    /// <summary>도구 패널(탐색기·로그) 숨김/복원 — '집중' 템플릿. 알림 패널은 대상이 아니다.</summary>
    void SetToolPanelsHidden(bool hidden);
    void ApplyPreset(LayoutPreset preset);
    void MinimizeAll();
    void RestoreAll();

    /// <summary>문서 최소화·최대화·보통(MDI-06). 플로팅 문서는 호스트 창 상태를 바꾼다.</summary>
    void SetState(PocDocument document, MdiWindowState state);

    /// <summary>가장 최근 최소화한 창을 복원(MDI-07). 없으면 false.</summary>
    bool RestoreLastMinimized();

    /// <summary>작업표시줄 탭 좌클릭(MDI-03): 활성 창이면 최소화, 아니면 활성화.</summary>
    void ActivateOrMinimize(PocDocument document);

    /// <summary>MDI↔SDI 전환(MDI-09).</summary>
    void ToggleSdi(PocDocument document);

    /// <summary>창 크기·위치 초기화(MDI-19): 전체 복원 후 MDI 창을 기본 크기 30px 계단으로 재배치.</summary>
    void ResetLayout();

    /// <summary>MDI 문서 전체를 dx,dy 만큼 이동.</summary>
    void MoveAll(double dx, double dy);

    void CloseDocument(PocDocument document);
    void CloseOthers(PocDocument keep);
    void CloseAll();
    void Rename(PocDocument document, string title);
    void Activate(PocDocument document);

    /// <summary>최근 활성 순서(MRU) 기준 다음 창(MDI-16, Ctrl+Tab). 반복 호출 시 모든 창을 순환.</summary>
    void ActivateNext();

    /// <summary>최근 활성 순서 기준 이전 창(Ctrl+Shift+Tab). ActivateNext 의 역순환.</summary>
    void ActivatePrevious();

    /// <summary>SDI 분리(플로팅).</summary>
    void Float(PocDocument document);

    /// <summary>SDI 재결합(문서 영역으로 복귀).</summary>
    void Redock(PocDocument document);

    /// <summary>플로팅 창 항상 위 토글(Mars 핀 고정). 도킹 상태 문서는 false 반환.</summary>
    bool TogglePin(PocDocument document);

    /// <summary>창 단위 확대·축소(UI-06): 문서 내용 전체 배율 0.5~2.0(10% 단위로 반올림). 레이아웃 저장에 포함.</summary>
    void SetDocumentZoom(PocDocument document, double zoom);

    /// <summary>배율 +10%. document=null 이면 활성 문서.</summary>
    void ZoomIn(PocDocument? document = null);

    /// <summary>배율 -10%. document=null 이면 활성 문서.</summary>
    void ZoomOut(PocDocument? document = null);

    /// <summary>배율 100%. document=null 이면 활성 문서.</summary>
    void ResetZoom(PocDocument? document = null);

    /// <summary>Ctrl+휠 동작 모드: true=현재 창 확대·축소, false=전체 기준 글자 크기.</summary>
    bool WindowZoomMode { get; set; }

    Task SaveLayoutAsync();

    /// <summary>저장된 레이아웃(자동 저장 슬롯) 복원. 저장본이 없으면 false.</summary>
    Task<bool> LoadLayoutAsync();

    /// <summary>레이아웃 JSON(SerializeLayout 결과, Version 1·2)을 적용한다. 현재 문서를 닫고 저장 상태로 다시 연다. 비었거나 손상됐으면 false(화면 불변).</summary>
    bool ApplyLayout(string? json);

    /// <summary>현재 레이아웃 JSON(저장 없이 직렬화만).</summary>
    string SerializeLayout();
}
