// UI 설정 서비스의 구성 클래스 위임 속성 — 색 테마 Id·창 크롬·언어·시스템 메뉴 값을 UiSettings 공개 속성으로 그대로 노출한다(UiSettings.cs 에서 줄 수 상한으로 나눔, 동작 동일)
using xkxk.frameworks.ui.data.Logging;
using xkxk.frameworks.ui.data.Rendering;
using System.Collections.Generic;
using xkxk.frameworks.ui.shell.Docking;
using xkxk.frameworks.ui.shell.Rendering;

namespace xkxk.frameworks.ui.shell.Theming;

public sealed partial class UiSettings
{
    // 색 테마 Id(UiThemeSettings)
    public string ThemeId { get => _themes.ThemeId; set => _themes.ThemeId = value; }
    public string SystemLightThemeId { get => _themes.SystemLightThemeId; set => _themes.SystemLightThemeId = value; }
    public string SystemDarkThemeId { get => _themes.SystemDarkThemeId; set => _themes.SystemDarkThemeId = value; }

    // 디자인 테마(UiThemeSettings, theme.style.* 키)
    public string DesignThemeId { get => _themes.DesignThemeId; set => _themes.DesignThemeId = value; }
    public MotionLevel MotionLevel { get => _themes.MotionLevel; set => _themes.MotionLevel = value; }
    public IReadOnlyDictionary<string, string> EffectOverrides => _themes.EffectOverrides;
    public void SetEffectOverride(string key, string? value) => _themes.SetEffectOverride(key, value);
    public void ClearEffectOverrides() => _themes.ClearEffectOverrides();

    // 창 크롬·윈도우 메뉴(UiWindowSettings)
    public const int MinMagnetRange = UiWindowSettings.MinMagnetRange;
    public const int MaxMagnetRange = UiWindowSettings.MaxMagnetRange;
    public const int MagnetRangeStep = UiWindowSettings.MagnetRangeStep;
    public const int DefaultMagnetRange = UiWindowSettings.DefaultMagnetRange;
    public bool WindowShadow { get => _window.WindowShadow; set => _window.WindowShadow = value; }
    public ChromeStyleKind WindowStyle { get => _window.WindowStyle; set => _window.WindowStyle = value; }
    public bool WindowAutoSave { get => _window.WindowAutoSave; set => _window.WindowAutoSave = value; }
    public bool EscClose { get => _window.EscClose; set => _window.EscClose = value; }
    public bool MagnetEnabled { get => _window.MagnetEnabled; set => _window.MagnetEnabled = value; }
    public int MagnetRange { get => _window.MagnetRange; set => _window.MagnetRange = value; }
    public string? SelectedMonitorId { get => _window.SelectedMonitorId; set => _window.SelectedMonitorId = value; } // U20, null = 주 모니터
    public double EffectiveMagnetRange => _window.EffectiveMagnetRange;
    public bool CaptureProtection { get => _window.CaptureProtection; set => _window.CaptureProtection = value; }
    public bool SdiAboveMain { get => _window.SdiAboveMain; set => _window.SdiAboveMain = value; }
    public static int NormalizeMagnetRange(double value) => UiWindowSettings.NormalizeMagnetRange(value);

    // 표시 언어(UiLanguageSettings)
    public string Language { get => _language.Language; set => _language.Language = value; }

    // 시스템 메뉴(UiSystemSettings)
    public const string UiModeArg = UiSystemSettings.UiModeArg;
    public GpuMode GpuMode { get => _system.GpuMode; set => _system.GpuMode = value; }
    public bool GpuRestartPending => _system.GpuRestartPending;
    public bool AllowDuplicateInstance { get => _system.AllowDuplicateInstance; set => _system.AllowDuplicateInstance = value; }
    public bool TrayEnabled { get => _system.TrayEnabled; set => _system.TrayEnabled = value; }
    public bool DebugMode { get => _system.DebugMode; set => _system.DebugMode = value; }
    public PdLogLevel? LogMinLevel { get => _system.LogMinLevel; set => _system.LogMinLevel = value; }
    public bool FastDocumentSwitch { get => _system.FastDocumentSwitch; set => _system.FastDocumentSwitch = value; }
    public bool TabHeaderFirst { get => _system.TabHeaderFirst; set => _system.TabHeaderFirst = value; }
    public static bool DefaultTabHeaderFirst => UiSystemSettings.DefaultTabHeaderFirst;
    public TabletUiMode TabletUiMode { get => _system.TabletUiMode; set => _system.TabletUiMode = value; }
    public TabletUiMode? TabletUiModeOverride { get => _system.TabletUiModeOverride; set => _system.TabletUiModeOverride = value; }
    public UiStyle UiStyle { get => _system.UiStyle; set => _system.UiStyle = value; }
    public UiStyle? UiStyleOverride { get => _system.UiStyleOverride; set => _system.UiStyleOverride = value; }
    public PanelFeatureMode PanelFeatureMode { get => _system.PanelFeatureMode; set => _system.PanelFeatureMode = value; }
    public bool CompactWindowKeepsTablet { get => _system.CompactWindowKeepsTablet; set => _system.CompactWindowKeepsTablet = value; }
    public const int DefaultBusyShowDelayMs = UiSystemSettings.DefaultBusyShowDelayMs;
    public const int DefaultBusyMinVisibleMs = UiSystemSettings.DefaultBusyMinVisibleMs;
    public int BusyShowDelayMs { get => _system.BusyShowDelayMs; set => _system.BusyShowDelayMs = value; }
    public int BusyMinVisibleMs { get => _system.BusyMinVisibleMs; set => _system.BusyMinVisibleMs = value; }
    public const int DefaultMdiMinGrabDip = UiSystemSettings.DefaultMdiMinGrabDip;
    public const int MinMdiMinGrabDip = UiSystemSettings.MinMdiMinGrabDip;
    public const int MaxMdiMinGrabDip = UiSystemSettings.MaxMdiMinGrabDip;
    public int MdiMinGrabDip { get => _system.MdiMinGrabDip; set => _system.MdiMinGrabDip = value; } // MDI 창 끌기 잡을 곳 폭(DIP, 50~120)
    public const int DefaultMdiMinVisiblePercent = UiSystemSettings.DefaultMdiMinVisiblePercent;
    public const int MinMdiMinVisiblePercent = UiSystemSettings.MinMdiMinVisiblePercent;
    public const int MaxMdiMinVisiblePercent = UiSystemSettings.MaxMdiMinVisiblePercent;
    public int MdiMinVisiblePercent { get => _system.MdiMinVisiblePercent; set => _system.MdiMinVisiblePercent = value; } // MDI 창 끌기 잔존 비율(%, 0~90, 기본 25 — 01 §4.3)
    public const int DefaultMdiMoveLightQuietMs = UiSystemSettings.DefaultMdiMoveLightQuietMs;
    public const int MinMdiMoveLightQuietMs = UiSystemSettings.MinMdiMoveLightQuietMs;
    public const int MaxMdiMoveLightQuietMs = UiSystemSettings.MaxMdiMoveLightQuietMs;
    public bool MdiMoveLight { get => _system.MdiMoveLight; set => _system.MdiMoveLight = value; } // 끌기 중 가벼운 크롬 사용(기본 켬)
    public int MdiMoveLightQuietMs { get => _system.MdiMoveLightQuietMs; set => _system.MdiMoveLightQuietMs = value; } // 조용 시간(ms, 50~1000, 기본 300 — 정본 Docking.MdiMoveLight.DefaultQuietMs)
    public bool MdiMoveLightBitmapCache { get => _system.MdiMoveLightBitmapCache; set => _system.MdiMoveLightBitmapCache = value; } // 끌기 중 비트맵 캐시(하위 옵션, 기본 끔 — 추가 측정 뒤 재결정)
    public bool MdiMoveLightStaticCache { get => _system.MdiMoveLightStaticCache; set => _system.MdiMoveLightStaticCache = value; } // 끌기 중 정지 아래 창 캐시(기본 끔 — 측정 뒤 결정)
    public bool MdiMoveLightSiblings { get => _system.MdiMoveLightSiblings; set => _system.MdiMoveLightSiblings = value; } // 끌기 중 겹친 정지 창 가벼운 크롬(기본 켬, 01 §4.2 N2)
    public bool MdiMoveLightHitTestOff { get => _system.MdiMoveLightHitTestOff; set => _system.MdiMoveLightHitTestOff = value; } // 끌기 중 끄는 창 내용 히트 테스트 끔(기본 켬, 01a K3)
    public bool MdiMoveCoalescePointer { get => _system.MdiMoveCoalescePointer; set => _system.MdiMoveCoalescePointer = value; } // 끌기 중 포인터 이동 프레임당 1회 합치기(기본 끔, 01a K5)
    public bool MdiReleaseFlushFrame { get => _system.MdiReleaseFlushFrame; set => _system.MdiReleaseFlushFrame = value; } // 놓기 반영을 다음 프레임에(기본 끔, 01a K6)
    public bool MdiStaticLightEarly { get => _system.MdiStaticLightEarly; set => _system.MdiStaticLightEarly = value; } // 끌기 시작 계산 미리 하기(기본 끔, 01a K7)
    public bool MdiTabJoinArmNear { get => _system.MdiTabJoinArmNear; set => _system.MdiTabJoinArmNear = value; } // 탭 합류 표적 무장을 근접 때만(기본 켬, 옵션 A)
    public double MdiTabJoinArmRadius { get => _system.MdiTabJoinArmRadius; set => _system.MdiTabJoinArmRadius = value; } // 근접 무장 반경 px(기본 96, 옵션 A)
    public int MdiMoveLightSiblingsPerFrame { get => _system.MdiMoveLightSiblingsPerFrame; set => _system.MdiMoveLightSiblingsPerFrame = value; } // 정지 창 가벼운 크롬 프레임당 창 수(기본 0=전부, 옵션 B)
    public int MdiMoveLightRestorePerFrame { get => _system.MdiMoveLightRestorePerFrame; set => _system.MdiMoveLightRestorePerFrame = value; } // 놓은 뒤 정지 창 복원 프레임당 창 수(기본 0=전부, 01a P2)
    public string ChromeInactiveDim { get => _system.ChromeInactiveDim; set => _system.ChromeInactiveDim = value; } // 비활성 제목줄 흐림 opacity|brush(기본 opacity, 01 §4.2 N1)
    public bool RenderDirtyRegions { get => _system.RenderDirtyRegions; set => _system.RenderDirtyRegions = value; } // 영역 dirty 추적(시작 옵션, 재시작 반영, 기본 끔)
    public bool PrewarmViews { get => _system.PrewarmViews; set => _system.PrewarmViews = value; }
    public bool GridDeferOffscreenCells { get => _system.GridDeferOffscreenCells; set => _system.GridDeferOffscreenCells = value; } // 표 화면 밖 열 셀 지연 실현(기본 켬, 01 §5.4 A16)
    public bool GridCompactCells { get => _system.GridCompactCells; set => _system.GridCompactCells = value; } // 표 단일 요소 셀(기본 켬, 세로선 없는 표만, 01 §5.4 A16)
    public bool GridDrawnCellText { get => _system.GridDrawnCellText; set => _system.GridDrawnCellText = value; } // 그리는 셀 글자(기본 켬, 단일 요소 셀 표만, 01 §5.4 K15)
    public bool GridViewPool { get => _system.GridViewPool; set => _system.GridViewPool = value; } // 준비된 표 뷰(기본 끔, 01 §5.4 A16)
    public RestoreViewCreation RestoreViewCreation { get => _system.RestoreViewCreation; set => _system.RestoreViewCreation = value; }
    public UiStyle EffectiveUiStyle => _system.EffectiveUiStyle;
    public TabletUiMode EffectiveTabletUiMode => _system.EffectiveTabletUiMode;
    public PdLogLevel EffectiveLogLevel => _system.EffectiveLogLevel;
    public static IReadOnlyList<string> TakeUiModeArg(IReadOnlyList<string> args) => UiSystemSettings.TakeUiModeArg(args);

    public static IReadOnlyList<string> TakeUiStyleArg(IReadOnlyList<string> args) => UiSystemSettings.TakeUiStyleArg(args);
}
