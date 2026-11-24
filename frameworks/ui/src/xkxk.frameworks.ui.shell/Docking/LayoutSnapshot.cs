// 레이아웃 스냅샷 DTO(문서·도구 패널·플로팅 창)와 그 JSON 소스 생성 컨텍스트 — LayoutSerializer 에서 분리
using xkxk.frameworks.ui.data.Layout;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Dock.Avalonia.Controls;
using Dock.Model.Core;

namespace xkxk.frameworks.ui.shell.Docking;

/// <summary>레이아웃 전체 스냅샷. Dock 내부 모델 대신 PoC 가 통제하는 DTO 로 저장한다(WASM 트리밍 안전).</summary>
public sealed class LayoutSnapshot
{
    /// <summary>1 = 단일 슬롯 형식, 2 = 도구 패널 비율 추가(UI-11), 3 = 알림 가장자리·탭 구성, 4 = 모든 패널 가장자리, 5 = MDI 탭 묶음(Groups). 읽기는 모든 형식을 지원한다.</summary>
    public int Version { get; set; } = 5;

    public DocumentLayoutMode Mode { get; set; } = DocumentLayoutMode.Mdi;

    /// <summary>도구 패널 비율(Version 2). Version 1 저장본은 null → 현재 비율 유지.</summary>
    public PanelsSnapshot? Panels { get; set; }

    /// <summary>측면 패널(탐색기·로그 쪽 바깥 패널) 숨김 — '집중' 템플릿(odev-1 SetSidePanelsHidden). 옛 저장본은 null → 현재 상태 유지.</summary>
    public bool? SidePanelsHidden { get; set; }

    public string? ActiveId { get; set; }

    public List<DocumentSnapshot> Documents { get; set; } = new();

    /// <summary>제목줄 '중복 허용'이 켜진 문서 종류(UI-09). 옛 저장본은 빈 목록.</summary>
    public List<DocumentKind> DuplicateKinds { get; set; } = new();

    /// <summary>
    /// MDI 탭 묶음(Version 5). 묶음 안 문서도 Documents 에 전부 그대로 기록한다(옛 판 앱이 읽어도 문서가 유실되지 않게).
    /// 옛 저장본(Version ≤4)은 null → 묶음 없이 문서별 MDI 창으로 복원한다.
    /// </summary>
    public List<MdiGroupSnapshot>? Groups { get; set; }
}

/// <summary>MDI 탭 묶음 1건(Version 5). 위치·크기·상태·Z 는 묶음 창 값이고, DocIds 는 탭 순서다.</summary>
public sealed class MdiGroupSnapshot
{
    public string Id { get; set; } = "";

    public double X { get; set; }

    public double Y { get; set; }

    public double Width { get; set; }

    public double Height { get; set; }

    public MdiWindowState State { get; set; }

    public int ZIndex { get; set; }

    /// <summary>활성 탭 문서 Id. null 이면 첫 탭.</summary>
    public string? ActiveId { get; set; }

    /// <summary>탭 순서대로 문서 Id(DocumentSnapshot.Id). 복원 때 없는 Id 는 건너뛰고, 남은 문서가 2개 미만이면 묶음을 만들지 않는다.</summary>
    public List<string> DocIds { get; set; } = new();
}

/// <summary>문서 1건 상태.</summary>
public sealed class DocumentSnapshot
{
    public string Id { get; set; } = "";

    public DocumentKind Kind { get; set; }

    public int Seq { get; set; }

    public string Title { get; set; } = "";

    public double X { get; set; }

    public double Y { get; set; }

    public double Width { get; set; }

    public double Height { get; set; }

    public MdiWindowState MdiState { get; set; }

    public int ZIndex { get; set; }

    /// <summary>전체 창(MDI·SDI) 앞뒤 순번 — 0=맨 아래, 클수록 위(최근 활성 순서 기준). 옛 저장본은 null → ZIndex 순 복원.</summary>
    public int? ZOrder { get; set; }

    /// <summary>창 단위 배율(UI-06). 구버전 저장본에는 없으므로 기본 1.</summary>
    public double Zoom { get; set; } = 1.0;

    /// <summary>제목줄 핀(항상 위) 상태(UI-09). SDI 는 Window.Topmost 와 함께 저장된다.</summary>
    public bool IsPinned { get; set; }

    /// <summary>제목줄 '동기화'(창 간 프로젝트 동기화 수신, UI-09 — Mars 창별 설정). 옛 저장본에는 없으므로 기본 true.</summary>
    public bool SyncEnabled { get; set; } = true;

    /// <summary>제목줄 '위치 고정'(끌기·크기 조절 차단, 사용자 22:22). 옛 저장본은 false.</summary>
    public bool IsLocked { get; set; }

    /// <summary>플로팅(SDI) 창 상태. 도킹 문서는 null.</summary>
    public WindowSnapshot? Window { get; set; }

    /// <summary>제목이 종류 이름+번호로 자동 생성된 것인가(언어가 바뀌면 다시 계산). 옛 저장본은 null → 제목 모양으로 추정.</summary>
    public bool? AutoTitle { get; set; }

    /// <summary>문서가 측면 패널 탭 영역에 있으면 그 Id(PocDockFactory.PanelOf). null = 문서 영역(Version 3).</summary>
    public string? PanelId { get; set; }
}

/// <summary>도구 패널(탐색기·로그) 비율. 0 이하·NaN 은 무시(현재 값 유지).</summary>
public sealed class PanelsSnapshot
{
    public double ExplorerProportion { get; set; }

    public double LogProportion { get; set; }

    /// <summary>알림 패널 가장자리 "Top"|"Bottom"|"Left"|"Right"(Version 3, odev-1 Q16). null 이면 현재 유지.</summary>
    public string? NotificationEdge { get; set; }

    /// <summary>알림 패널 비율(Version 3). null 이면 현재 유지.</summary>
    public double? NotificationProportion { get; set; }

    /// <summary>탐색기 패널 가장자리(Version 4). null 이면 왼쪽.</summary>
    public string? ExplorerEdge { get; set; }

    /// <summary>로그 패널 가장자리(Version 4). null 이면 아래.</summary>
    public string? LogEdge { get; set; }

    /// <summary>여분 패널 가장자리(Version 4). null 이면 현재 유지.</summary>
    public string? ExtraEdge { get; set; }

    /// <summary>여분 패널 비율(Version 4). null 이면 현재 유지.</summary>
    public double? ExtraProportion { get; set; }

    /// <summary>측면 패널 탭 구성(안쪽 탭 영역 Id·탭 순서·활성 탭, Version 3, odev-1 Q15). null 이면 현재 유지.</summary>
    public List<PanelTabsSnapshot>? Tabs { get; set; }

    /// <summary>패널 모서리 소유(요건 3, 모서리 이름 → 패널 Id). 기본 배치·옛 저장본은 null → 기본 배치.</summary>
    [JsonPropertyName("cornerOwners")]
    public Dictionary<string, string>? CornerOwners { get; set; }
}

/// <summary>플로팅 창 상태.</summary>
public sealed class WindowSnapshot
{
    public double X { get; set; }

    public double Y { get; set; }

    public double Width { get; set; }

    public double Height { get; set; }

    public bool Topmost { get; set; }

    public DockWindowState State { get; set; }
}

[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(LayoutSnapshot))]
internal sealed partial class LayoutJsonContext : JsonSerializerContext
{
}
