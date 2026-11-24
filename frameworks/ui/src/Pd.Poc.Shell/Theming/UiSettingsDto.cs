// UI 설정 저장 형식(settings.json) — 속성 선언 순서가 곧 JSON 키 순서이므로 기존 저장본과 같은 순서를 지킨다
using System.Collections.Generic;
using Pd.Client.Data;
using Pd.Client.Data.Logging;
using Pd.Client.Data.Rendering;
using System.Text.Json.Serialization;
using Pd.Poc.Shell.Docking;
using Pd.Poc.Shell.Rendering;

namespace Pd.Poc.Shell.Theming;

public enum ThemeMode
{
    System,
    Light,
    Dark,
}

public enum CtrlWheelMode
{
    /// <summary>전체 기준 글자 크기 ±1(UI-05).</summary>
    FontSize,

    /// <summary>포인터 아래(없으면 활성) 문서 창 배율 ±10%(UI-06).</summary>
    WindowZoom,
}

/// <summary>넓은 화면(태블릿형)에서 쓸 화면 방식. 좁은 화면(바형 폰)은 항상 폰 방식이다. 데스크톱은 이 설정을 쓰지 않는다.</summary>
public enum TabletUiMode
{
    /// <summary>폭으로 판정(지금 동작 — 넓으면 MDI). 나중에 기기 종류 판정을 넣을 자리.</summary>
    Auto,

    /// <summary>태블릿 모드: MDI(PC 와 같은 창 방식).</summary>
    Tablet,

    /// <summary>폰 모드: MDI 없이 문서 1개 전체 화면.</summary>
    Phone,
}

/// <summary>저장 형식. 키 순서 = 아래 선언 순서(기본·테마 → 창 크롬 → 언어 → 시스템 메뉴 → 윈도우 메뉴).</summary>
public sealed partial class UiSettingsDto
{
    public ThemeMode Theme { get; set; }

    public CtrlWheelMode WheelMode { get; set; }

    public double BaseFontSize { get; set; } = UiSettings.DefaultFontSize;

    public double DocumentDefaultFontSize { get; set; } = UiSettings.DefaultFontSize;

    public string FontName { get; set; } = UiSettings.DefaultFontName;

    // 색 테마(UI-07). 옛 저장본에는 없으므로 null → 기본값
    public string? ThemeId { get; set; }

    public string? SystemLightThemeId { get; set; }

    public string? SystemDarkThemeId { get; set; }

    public string? UserThemesJson { get; set; }

    /// <summary>창 그림자(기본 켬). 옛 저장본에는 없으므로 기본값.</summary>
    public bool WindowShadow { get; set; } = true;

    /// <summary>창 스타일(odev-7c ChromeStyleKind). 옛 저장본에는 없음(null) → MarsFrame 으로 이전.</summary>
    public ChromeStyleKind? ChromeStyle { get; set; }

    /// <summary>옛 "Mars 수준 1px 외곽선"(읽기 호환용, true = MarsClassic). 새 저장본도 같이 써서 옛 판본이 읽을 수 있게 한다.</summary>
    public bool MarsFrame { get; set; }

    /// <summary>표시 언어 코드(ko·en …). 옛 저장본에는 없으므로 null → 기본 언어.</summary>
    public string? Language { get; set; }

    /// <summary>GPU 가속모드(GpuSetting.SettingsKey 와 같은 이름 — 호스트가 Avalonia 초기화 전에 이 키만 먼저 읽는다). null 이면 기본(Auto).</summary>
    public GpuMode? GpuMode { get; set; }

    /// <summary>프로그램 중복모드. 기본 끔(동시에 하나만 실행). 데스크톱 호스트가 기동 전에 먼저 읽는다.</summary>
    public bool AllowDuplicateInstance { get; set; }

    /// <summary>트레이 상주(최소화 시 트레이, 데스크톱 전용). 기본 끔. 비전경 시험 기동은 이 값을 무시한다(ShellPlatform.SuppressSavedTray).</summary>
    public bool TrayEnabled { get; set; }

    /// <summary>디버그모드. null 이면 채널 기본값(AppChannel.DebugDefault — dev·staging·qa 켬, prod 끔).</summary>
    public bool? DebugMode { get; set; }

    /// <summary>로그 최소 레벨. null 이면 디버그모드에 따름(켬 Debug, 끔 Information).</summary>
    public PdLogLevel? LogMinLevel { get; set; }

    /// <summary>문서 전환 가속(탭형 비활성 문서 유지·문서 본문 즉시 표시). 옛 저장본에는 없으므로 null → 켬.</summary>
    public bool? FastDocumentSwitch { get; set; }

    /// <summary>탭 머리 먼저 그리기(탭 선택 표시 먼저, 본문 교체는 다음 프레임 뒤). 옛 저장본에는 없으므로 null → 플랫폼 기본값(웹 켬, 그 밖 끔).</summary>
    public bool? TabHeaderFirst { get; set; }

    /// <summary>화면 모드(Android·웹, 넓은 화면에서만 의미). 기본 Auto.</summary>
    public TabletUiMode TabletUiMode { get; set; }

    /// <summary>UI 형 선택(자동·PC형·태블릿형·모바일형). 옛 저장본에는 없으므로 null → LoadDto 가 TabletUiMode 에서 이전(기본 자동). 값만 보관하고 해석은 UiStyleRules 가 한다.</summary>
    public UiStyle? UiStyle { get; set; }

    /// <summary>① 종료 자동 저장·기동 복원. 옛 저장본에는 없으므로 기본 켬.</summary>
    public bool WindowAutoSave { get; set; } = true;

    /// <summary>② Esc 로 활성 문서 창 닫기. 기본 끔.</summary>
    public bool EscClose { get; set; }

    /// <summary>③ 자석 켜기. 기본 켬.</summary>
    public bool MagnetEnabled { get; set; } = true;

    /// <summary>③ 자석 범위(px). 기본 24.</summary>
    public int MagnetRange { get; set; } = UiSettings.DefaultMagnetRange;

    /// <summary>U20 고른 모니터 식별자. 옛 저장본에는 없으므로 null(= 주 모니터).</summary>
    public string? SelectedMonitorId { get; set; }

    /// <summary>⑬ 화면 캡처 방지. 기본 끔.</summary>
    public bool CaptureProtection { get; set; }

    /// <summary>항상 메인 창 위(SDI 가 메인 창 위에 머문다). 기본 켬. 옛 저장본에는 없으므로 기본값.</summary>
    public bool SdiAboveMain { get; set; } = true;

    /// <summary>패널 기능 모드(자동·켬·끔). 옛 저장본에는 없으므로 null → 자동(Auto 가 enum 첫 값인지는 PanelFeatureRules 기준).</summary>
    public PanelFeatureMode? PanelFeatureMode { get; set; }

    /// <summary>창 폭 600dp 미만에서도 태블릿형 유지. 옛 저장본에는 없으므로 null → 끔.</summary>
    public bool? CompactWindowKeepsTablet { get; set; }

    /// <summary>바쁨 표시 지연(ms). 옛 저장본에는 없으므로 null → 150.</summary>
    public int? BusyShowDelayMs { get; set; }

    /// <summary>바쁨 표시 최소 유지(ms). 옛 저장본에는 없으므로 null → 150.</summary>
    public int? BusyMinVisibleMs { get; set; }

    /// <summary>뷰 예열. 옛 저장본에는 없으므로 null → 켬.</summary>
    public bool? PrewarmViews { get; set; }

    /// <summary>표 화면 밖 열 셀 지연 실현. 옛 저장본에는 없으므로 null → UiSystemSettings.DefaultGridDeferOffscreenCells(켬).</summary>
    public bool? GridDeferOffscreenCells { get; set; }

    /// <summary>표 단일 요소 셀. 옛 저장본에는 없으므로 null → UiSystemSettings.DefaultGridCompactCells(켬).</summary>
    public bool? GridCompactCells { get; set; }

    /// <summary>그리는 셀 글자. 옛 저장본에는 없으므로 null → UiSystemSettings.DefaultGridDrawnCellText(켬).</summary>
    public bool? GridDrawnCellText { get; set; }

    /// <summary>준비된 표 뷰. 옛 저장본에는 없으므로 null → UiSystemSettings.DefaultGridViewPool(끔).</summary>
    public bool? GridViewPool { get; set; }

    /// <summary>복원 뷰 생성 방식(즉시/지연). 옛 저장본에는 없으므로 null → 지연.</summary>
    public RestoreViewCreation? RestoreViewCreation { get; set; }

    /// <summary>MDI 창 끌기 잡을 곳 최소 폭(DIP, 50~120). 옛 저장본에는 없으므로 null → 50. 범위 밖 값은 LoadDto 가 잘라 맞춘다.</summary>
    public int? MdiMinGrabDip { get; set; }

    /// <summary>MDI 창 끌기 잔존 비율(%, 0~90). 옛 저장본에는 없으므로 null → 25. 0 은 비율 끔(잡을 곳 규칙만). 범위 밖 값은 LoadDto 가 잘라 맞춘다.</summary>
    public int? MdiMinVisiblePercent { get; set; }

    /// <summary>끌기 중 가벼운 크롬 사용 여부. 옛 저장본에는 없으므로 null → 켬.</summary>
    public bool? MdiMoveLight { get; set; }

    /// <summary>끌기 중 가벼운 크롬 조용 시간(ms, 50~1000). 옛 저장본에는 없으므로 null → MdiMoveLight.DefaultQuietMs(300). 범위 밖 값은 LoadDto 가 잘라 맞춘다.</summary>
    public int? MdiMoveLightQuietMs { get; set; }

    /// <summary>끌기 중 비트맵 캐시(하위 옵션). 옛 저장본에는 없으므로 null → MdiMoveLight.DefaultBitmapCache(끔).</summary>
    public bool? MdiMoveLightBitmapCache { get; set; }

    /// <summary>끌기 중 정지 아래 창 캐시. 옛 저장본에는 없으므로 null → MdiMoveLight.DefaultStaticCache(끔).</summary>
    public bool? MdiMoveLightStaticCache { get; set; }

    /// <summary>끌기 중 겹친 정지 창 가벼운 크롬. 옛 저장본에는 없으므로 null → MdiMoveLight.DefaultSiblings(켬).</summary>
    public bool? MdiMoveLightSiblings { get; set; }

    /// <summary>비활성 창 제목줄 흐림 방식("opacity"|"brush"). 옛 저장본에는 없으므로 null → ChromeInactiveDim.DefaultMode. 모르는 값도 기본값.</summary>
    public string? ChromeInactiveDim { get; set; }

    /// <summary>영역 dirty 추적(시작 옵션, MdiMoveLight.RenderDirtyRegionsSettingsKey 와 같은 이름 — 호스트가 Avalonia 초기화 전에 이 키만 먼저 읽는다). null → 끔.</summary>
    public bool? RenderDirtyRegions { get; set; }

    /// <summary>끌기 중 모서리 반경 0. 옛 저장본에는 없으므로 null → MdiMoveLight.DefaultSquareCorners(끔, 2026-11-11).</summary>
    public bool? MdiMoveLightSquareCorners { get; set; }

    /// <summary>끌기 중 끄는 창 내용 히트 테스트 끔. 옛 저장본에는 없으므로 null → MdiMoveLight.DefaultHitTestOff(켬, 2026-11-14 01a K3).</summary>
    public bool? MdiMoveLightHitTestOff { get; set; }

    /// <summary>끌기 중 포인터 이동 합치기. 옛 저장본에는 없으므로 null → MdiMoveLight.DefaultCoalescePointer(끔, 2026-11-14 01a K5).</summary>
    public bool? MdiMoveCoalescePointer { get; set; }

    /// <summary>놓기 반영을 다음 프레임에. 옛 저장본에는 없으므로 null → MdiMoveLight.DefaultReleaseFlushFrame(끔, 2026-11-14 01a K6).</summary>
    public bool? MdiReleaseFlushFrame { get; set; }

    /// <summary>끌기 시작 계산 미리 하기. 옛 저장본에는 없으므로 null → MdiMoveLight.DefaultStaticLightEarly(끔, 2026-11-14 01a K7).</summary>
    public bool? MdiStaticLightEarly { get; set; }

    /// <summary>탭 합류 표적 무장을 창 근접 때만(옵션 A). 옛 저장본에는 없으므로 null → MdiMoveLight.DefaultTabJoinArmNear(켬, 2026-11-15 G3·D-46).</summary>
    public bool? MdiTabJoinArmNear { get; set; }

    /// <summary>탭 합류 표적 무장 반경(px, 옵션 A). 옛 저장본에는 없으므로 null → 기본 96.</summary>
    public double? MdiTabJoinArmRadius { get; set; }

    /// <summary>끌기 중 정지 창 가벼운 크롬을 프레임당 처리하는 창 수(옵션 B, 0=한 번에 전부). 옛 저장본에는 없으므로 null → 기본 0.</summary>
    public int? MdiMoveLightSiblingsPerFrame { get; set; }

    /// <summary>놓은 뒤 정지 창 복원을 프레임당 처리하는 창 수(01a P2, 0=한 번에 전부). 옛 저장본에는 없으므로 null → MdiMoveLight.DefaultRestorePerFrame(0).</summary>
    public int? MdiMoveLightRestorePerFrame { get; set; }

    /// <summary>시스템(셸) 글자 크기. 옛 저장본에는 없으므로 null → UiSettings.DefaultFontSize(14, 2026-11-11 이전 셸 고정값).</summary>
    public double? ShellFontSize { get; set; }

    /// <summary>시스템(셸) 글꼴. 옛 저장본에는 없으므로 null → 문서 글꼴(FontName)과 같게(2026-11-11 이전 동작).</summary>
    public string? ShellFontName { get; set; }

    /// <summary>제목 줄(셸 탭 줄) 높이 DIP. 옛 저장본에는 없으므로 null → UiSettings.DefaultShellTabStripHeight(38).</summary>
    public double? ShellTabStripHeight { get; set; }

    /// <summary>Snap(창 배치) 전체 켜기. 옛 저장본에는 없으므로 null → 켬.</summary>
    public bool? SnapEnabled { get; set; }

    /// <summary>Snap 칸 종류 id 목록(쉼표 구분). null → 6종 전부, 빈 문자열 → 막대 없음.</summary>
    public string? SnapLayouts { get; set; }

    /// <summary>Snap 가장자리 감지 거리(DIP, 0~48). null → 8. 범위 밖 값은 UiSettings 가 잘라 맞춘다.</summary>
    public int? SnapEdgeDistance { get; set; }

    /// <summary>Snap 막대 표시 지연(ms, 0~1000). null → 150.</summary>
    public int? SnapBarDelayMs { get; set; }

    /// <summary>Snap 좌우·모서리 배치. null → 켬.</summary>
    public bool? SnapEdgeEnabled { get; set; }

    /// <summary>Snap 단축키. null → 켬.</summary>
    public bool? SnapKeyboard { get; set; }

    /// <summary>SDI 창 Snap 방식(자동·앱·OS·끔 — 값 이름은 UiSettings.Snap 정의). null → 자동.</summary>
    public string? SnapSdiMode { get; set; }

    /// <summary>위쪽 가장자리 놓기 → 최대화. null → 켬.</summary>
    public bool? SnapTopEdgeMaximize { get; set; }

    /// <summary>최대화·이전 크기 단추 위 머무름 → 칸 종류 팝업. null → 켬.</summary>
    public bool? SnapFlyoutEnabled { get; set; }

    /// <summary>최대화 단추 팝업 표시 지연(ms, 0~3000). null → 1000.</summary>
    public int? SnapFlyoutDelayMs { get; set; }

    /// <summary>Snap Assist(칸 선택 뒤 남은 칸에 놓을 창 고르기). null → 켬.</summary>
    public bool? SnapAssistEnabled { get; set; }

    /// <summary>가장자리·모서리 스냅 뒤에도 Snap Assist 를 띄움. null → 켬.</summary>
    public bool? SnapAssistOnEdge { get; set; }

    /// <summary>Snap Assist 카드에 창 미리보기 썸네일 표시. null → 켬(끄면 아이콘+제목).</summary>
    public bool? SnapAssistThumbnails { get; set; }

    /// <summary>SDI Snap Assist 창 표시 직후 늦은 비활성 유예(ms, 100~2000). null → 400.</summary>
    public int? SnapAssistSdiGraceMs { get; set; }

    /// <summary>제목 탭 닫기(×) 표시 방식(Auto·Always·Hover·ActiveAndHover — 값 이름은 UiSettings.Shell 의 TitleTabCloseMode). null·모르는 값 → Auto.</summary>
    public string? TitleTabCloseMode { get; set; }

    /// <summary>디자인 테마 Id(theme.style.id). 옛 저장본·기본이면 없음(null) → ds-classic.</summary>
    public string? DesignThemeId { get; set; }

    /// <summary>움직임 수준(theme.motion.level). null → System.</summary>
    public MotionLevel? MotionLevel { get; set; }

    /// <summary>효과 덮어쓰기(효과 키 → 값, 희소). null·빈 → 디자인 테마 값 그대로.</summary>
    public Dictionary<string, string>? EffectOverrides { get; set; }

    // 진단 키 diag.depth.*(K13 H2 판별)는 이번 실행만 쓰므로 저장 필드를 두지 않는다 — 옛 저장본의 DiagDepthSkipElementDesign 은 읽을 때 무시되고 다음 저장 때 사라진다

    /// <summary>설정 키 depth.deferBegin(열기 기울기 시작을 다음 프레임으로 — K13 수정 후보 2, H4 진단 겸용). null → 끔(현재 동작).</summary>
    public bool? DepthDeferBegin { get; set; }

    /// <summary>설정 키 depth.webPrewarm(웹에서도 기울기 첫 렌더 예열, K13 수정 후보 1). null → DepthDiag.WebPrewarm 기본값.</summary>
    public bool? DepthWebPrewarm { get; set; }
}

[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(UiSettingsDto))]
internal sealed partial class UiSettingsJsonContext : JsonSerializerContext
{
}
