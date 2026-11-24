// 설정 창 '창' 페이지 뷰모델(요건 7) — 창 스타일·그림자·항상 메인 창 위·자석·Esc·탭 더블클릭·자동 저장·캡처 방지·트레이·화면 방식
using System;
using System.Collections.Generic;
using System.ComponentModel;
using xkxk.frameworks.ui.shell.Docking;
using xkxk.frameworks.ui.shell.Localization;
using xkxk.frameworks.ui.shell.Theming;

namespace xkxk.frameworks.ui.shell.ViewModels.Settings;

public sealed partial class WindowPageViewModel : SettingsPageViewModel
{
    private static readonly IReadOnlyList<LocalizedOption> s_styleNames =
        LocalizedOption.List("settings.style.windows", "settings.style.macos", "settings.style.mars", "settings.style.auto");

    private static readonly UiStyle[] s_uiStyles = { UiStyle.Auto, UiStyle.Pc, UiStyle.Tablet, UiStyle.Mobile };

    private static readonly IReadOnlyList<LocalizedOption> s_uiStyleNames =
        LocalizedOption.List("menu.view.uiStyle.auto", "menu.view.uiStyle.pc", "menu.view.uiStyle.tablet", "menu.view.uiStyle.mobile");

    private static readonly IReadOnlyList<LocalizedOption> s_tabletNames =
        LocalizedOption.List("settings.tabletMode.auto", "settings.tabletMode.tablet", "settings.tabletMode.phone");

    private static readonly PanelFeatureMode[] s_panelFeatureModes = { PanelFeatureMode.Auto, PanelFeatureMode.On, PanelFeatureMode.Off };

    private static readonly IReadOnlyList<LocalizedOption> s_panelFeatureNames =
        LocalizedOption.List("settings.panelFeature.auto", "settings.panelFeature.on", "settings.panelFeature.off");

    private static readonly RestoreViewCreation[] s_restoreViewModes = { RestoreViewCreation.Deferred, RestoreViewCreation.Immediate };

    private static readonly IReadOnlyList<LocalizedOption> s_restoreViewNames =
        LocalizedOption.List("settings.restoreViews.deferred", "settings.restoreViews.immediate");

    public WindowPageViewModel(MainViewModel main)
        : base(main)
    {
        main.PropertyChanged += OnMainChanged;
    }

    public IReadOnlyList<LocalizedOption> WindowStyleNames => s_styleNames;

    public int WindowStyleIndex
    {
        get => (int)Settings.WindowStyle;
        set
        {
            if (IsNewIndex(value, s_styleNames.Count, (int)Settings.WindowStyle)) { Settings.WindowStyle = (ChromeStyleKind)value; }
        }
    }

    public bool WindowShadow { get => Settings.WindowShadow; set => Settings.WindowShadow = value; }

    public bool SdiAboveMain { get => Settings.SdiAboveMain; set => Settings.SdiAboveMain = value; }

    public bool MagnetEnabled { get => Settings.MagnetEnabled; set => Settings.MagnetEnabled = value; }

    public double MinMagnetRange => UiSettings.MinMagnetRange;

    public double MaxMagnetRange => UiSettings.MaxMagnetRange;

    public double MagnetRangeStep => UiSettings.MagnetRangeStep;

    public double MagnetRange
    {
        get => Settings.MagnetRange;
        set => Settings.MagnetRange = UiSettings.NormalizeMagnetRange(value);
    }

    /// <summary>슬라이더 옆 현재값 문구.</summary>
    public string MagnetRangeText => Loc.Tr("settings.magnetRange.value", Settings.MagnetRange);

    public bool EscClose { get => Settings.EscClose; set => Settings.EscClose = value; }

    /// <summary>탭 더블클릭으로 창 닫기. 이 값만 UiSettings 가 아니라 메인 뷰모델이 가진다(저장되지 않는 기존 동작 유지).</summary>
    public bool DoubleClickClose { get => Main.DoubleClickCloseEnabled; set => Main.DoubleClickCloseEnabled = value; }

    public bool WindowAutoSave { get => Settings.WindowAutoSave; set => Settings.WindowAutoSave = value; }

    public bool CaptureProtection { get => Settings.CaptureProtection; set => Settings.CaptureProtection = value; }

    public bool TrayEnabled { get => Settings.TrayEnabled; set => Settings.TrayEnabled = value; }

    /// <summary>트레이가 없는 플랫폼(웹·Android)은 행을 비활성으로 둔다.</summary>
    public bool TrayAvailable => Main.Extras.TrayAvailable;

    public IReadOnlyList<LocalizedOption> UiStyleNames => s_uiStyleNames;

    /// <summary>화면 방식 선택값(자동·PC형·태블릿형·모바일형). 허용 밖 값은 Select 가 무시한다.</summary>
    public int UiStyleIndex
    {
        get => Array.IndexOf(s_uiStyles, Main.Compact.Style.Setting);
        set
        {
            if (IsNewIndex(value, s_uiStyles.Length, UiStyleIndex))
            {
                Main.Compact.Style.Select(s_uiStyles[value]);
            }

            OnPropertyChanged(); // 무시된 선택이면 콤보를 원래 값으로 되돌린다
        }
    }

    /// <summary>폰 기기는 모바일형뿐이라 선택 행을 비활성으로 둔다.</summary>
    public bool UiStyleSelectable => Main.Compact.Style.SelectorVisible;

    public IReadOnlyList<LocalizedOption> TabletModeNames => s_tabletNames;

    /// <summary>옛 화면 모드 저장값. 화면 방식(UiStyle)이 대신하므로 표시만 하고 행은 비활성이다(옛 저장본의 이전 원본).</summary>
    public int TabletModeIndex => (int)Settings.TabletUiMode;

    public IReadOnlyList<LocalizedOption> PanelFeatureNames => s_panelFeatureNames;

    /// <summary>패널 기능 선택값(자동·켬·끔). 바꾸면 설정을 저장하고 도킹 게이트에 즉시 반영한다.</summary>
    public int PanelFeatureIndex
    {
        get => Array.IndexOf(s_panelFeatureModes, Settings.PanelFeatureMode);
        set
        {
            if (IsNewIndex(value, s_panelFeatureModes.Length, PanelFeatureIndex))
            {
                Settings.PanelFeatureMode = s_panelFeatureModes[value];
                Main.Factory.PanelFeature.SetMode(s_panelFeatureModes[value]);
            }
        }
    }

    /// <summary>좁은 창(분할 화면 등)에서도 태블릿형을 유지할지. 기본 끔.</summary>
    public bool CompactWindowKeepsTablet { get => Settings.CompactWindowKeepsTablet; set => Settings.CompactWindowKeepsTablet = value; }

    /// <summary>진행 표시 지연(ms). 이 시간보다 오래 걸리는 작업에만 진행 표시를 보인다.</summary>
    public decimal BusyShowDelayMs { get => Settings.BusyShowDelayMs; set => Settings.BusyShowDelayMs = (int)value; }

    /// <summary>진행 표시 최소 유지 시간(ms).</summary>
    public decimal BusyMinVisibleMs { get => Settings.BusyMinVisibleMs; set => Settings.BusyMinVisibleMs = (int)value; }

    /// <summary>기동 뒤 유휴 시간에 문서 뷰를 화면 밖에서 미리 만들어 둘지.</summary>
    public bool PrewarmViews { get => Settings.PrewarmViews; set => Settings.PrewarmViews = value; }

    /// <summary>표의 화면 밖 열 셀 내용을 그 셀이 처음 화면에 들어올 때 만들지(01 §5.4, 기본 켬). 다음에 여는 표부터 반영된다.</summary>
    public bool GridDeferOffscreenCells { get => Settings.GridDeferOffscreenCells; set => Settings.GridDeferOffscreenCells = value; }

    /// <summary>세로선 없는 표의 셀을 단일 요소(PdCellChrome)로 만들지(01 §5.4, 기본 켬). 다음에 여는 표부터 반영된다.</summary>
    public bool GridCompactCells { get => Settings.GridCompactCells; set => Settings.GridCompactCells = value; }

    /// <summary>단일 요소 셀의 텍스트 열 글자를 셀이 직접 그릴지(01 §5.4, 기본 켬). 다음에 여는 표부터 반영된다.</summary>
    public bool GridDrawnCellText { get => Settings.GridDrawnCellText; set => Settings.GridDrawnCellText = value; }

    /// <summary>유휴 시간에 표 뷰 1개를 미리 만들어 두었다가 다음 표 열기에 쓸지(01 §5.4, 기본 끔).</summary>
    public bool GridViewPool { get => Settings.GridViewPool; set => Settings.GridViewPool = value; }

    public IReadOnlyList<LocalizedOption> RestoreViewNames => s_restoreViewNames;

    /// <summary>복원 뷰 생성 방식(지연·즉시). 바꾸면 저장하고 다음 레이아웃 복원부터 적용된다.</summary>
    public int RestoreViewIndex
    {
        get => Array.IndexOf(s_restoreViewModes, Settings.RestoreViewCreation);
        set
        {
            if (IsNewIndex(value, s_restoreViewModes.Length, RestoreViewIndex))
            {
                Settings.RestoreViewCreation = s_restoreViewModes[value];
            }
        }
    }

    private static readonly SnapSdiMode[] s_snapSdiModes = { SnapSdiMode.Auto, SnapSdiMode.App, SnapSdiMode.Os, SnapSdiMode.Off };

    private static readonly IReadOnlyList<LocalizedOption> s_snapSdiNames =
        LocalizedOption.List("settings.snapSdiMode.auto", "settings.snapSdiMode.app", "settings.snapSdiMode.os", "settings.snapSdiMode.off");

    // ── 창 배치(Snap) ─────────────────────────────────────────────

    public bool SnapEnabled { get => Settings.SnapEnabled; set => Settings.SnapEnabled = value; }

    /// <summary>칸 종류 id 목록(쉼표 구분). 빈 값이면 막대를 띄우지 않는다.</summary>
    public string SnapLayouts { get => Settings.SnapLayouts; set => Settings.SnapLayouts = value ?? string.Empty; }

    public double MinSnapEdgeDistance => UiSettings.MinSnapEdgeDistance;

    public double MaxSnapEdgeDistance => UiSettings.MaxSnapEdgeDistance;

    public double SnapEdgeDistance
    {
        get => Settings.SnapEdgeDistance;
        set => Settings.SnapEdgeDistance = UiSettings.NormalizeSnapEdgeDistance((int)Math.Round(value));
    }

    public string SnapEdgeDistanceText => Loc.Tr("settings.snapEdgeDistance.value", Settings.SnapEdgeDistance);

    public decimal SnapBarDelayMs
    {
        get => Settings.SnapBarDelayMs;
        set => Settings.SnapBarDelayMs = UiSettings.NormalizeSnapBarDelayMs((int)value);
    }

    public bool SnapEdgeEnabled { get => Settings.SnapEdgeEnabled; set => Settings.SnapEdgeEnabled = value; }

    public bool SnapTopEdgeMaximize { get => Settings.SnapTopEdgeMaximize; set => Settings.SnapTopEdgeMaximize = value; }

    public bool SnapKeyboard { get => Settings.SnapKeyboard; set => Settings.SnapKeyboard = value; }

    public bool SnapFlyoutEnabled { get => Settings.SnapFlyoutEnabled; set => Settings.SnapFlyoutEnabled = value; }

    public decimal SnapFlyoutDelayMs
    {
        get => Settings.SnapFlyoutDelayMs;
        set => Settings.SnapFlyoutDelayMs = UiSettings.NormalizeSnapFlyoutDelayMs((int)value);
    }

    public IReadOnlyList<LocalizedOption> SnapSdiNames => s_snapSdiNames;

    public int SnapSdiModeIndex
    {
        get => Array.IndexOf(s_snapSdiModes, Settings.SnapSdiMode);
        set
        {
            if (IsNewIndex(value, s_snapSdiModes.Length, SnapSdiModeIndex)) { Settings.SnapSdiMode = s_snapSdiModes[value]; }
        }
    }

    /// <summary>SDI 방식 행은 독립 OS 창이 있는 데스크톱에서만 보인다.</summary>
    public bool SnapSdiAvailable => xkxk.frameworks.ui.shell.Platform.ShellPlatform.IsSdiWindowSupported;

    /// <summary>Snap Assist(칸 선택 뒤 남은 칸에 놓을 창 고르기). 창 배치 전체를 끄면 함께 꺼진다.</summary>
    public bool SnapAssistEnabled { get => Settings.SnapAssistEnabled; set => Settings.SnapAssistEnabled = value; }

    /// <summary>가장자리·모서리 스냅 뒤에도 Snap Assist 를 띄울지.</summary>
    public bool SnapAssistOnEdge { get => Settings.SnapAssistOnEdge; set => Settings.SnapAssistOnEdge = value; }

    /// <summary>Snap Assist 카드에 창 썸네일을 보일지. 끄면 아이콘+제목 카드.</summary>
    public bool SnapAssistThumbnails { get => Settings.SnapAssistThumbnails; set => Settings.SnapAssistThumbnails = value; }

    public decimal SnapAssistSdiGraceMs
    {
        get => Settings.SnapAssistSdiGraceMs;
        set => Settings.SnapAssistSdiGraceMs = UiSettings.NormalizeSnapAssistSdiGraceMs((int)value);
    }

    public override void ResetToDefaults()
    {
        Settings.SnapEnabled = true;
        Settings.SnapLayouts = UiSettings.DefaultSnapLayouts;
        Settings.SnapEdgeDistance = UiSettings.DefaultSnapEdgeDistance;
        Settings.SnapBarDelayMs = UiSettings.DefaultSnapBarDelayMs;
        Settings.SnapEdgeEnabled = true;
        Settings.SnapTopEdgeMaximize = true;
        Settings.SnapKeyboard = true;
        Settings.SnapFlyoutEnabled = true;
        Settings.SnapFlyoutDelayMs = UiSettings.DefaultSnapFlyoutDelayMs;
        Settings.SnapSdiMode = SnapSdiMode.Auto;
        Settings.SnapAssistEnabled = true;
        Settings.SnapAssistOnEdge = true;
        Settings.SnapAssistThumbnails = true;
        Settings.SnapAssistSdiGraceMs = UiSettings.DefaultSnapAssistSdiGraceMs;
        Settings.RestoreViewCreation = RestoreViewCreation.Deferred;
        Settings.PanelFeatureMode = PanelFeatureMode.Auto;
        Main.Factory.PanelFeature.SetMode(PanelFeatureMode.Auto);
        Settings.CompactWindowKeepsTablet = false;
        Settings.BusyShowDelayMs = UiSettings.DefaultBusyShowDelayMs;
        Settings.BusyMinVisibleMs = UiSettings.DefaultBusyMinVisibleMs;
        Settings.PrewarmViews = true;
        Settings.GridDeferOffscreenCells = UiSystemSettings.DefaultGridDeferOffscreenCells;
        Settings.GridCompactCells = UiSystemSettings.DefaultGridCompactCells;
        Settings.GridDrawnCellText = UiSystemSettings.DefaultGridDrawnCellText;
        Settings.GridViewPool = UiSystemSettings.DefaultGridViewPool;
        Settings.WindowStyle = ChromeStyleKind.Auto; // 기본값 = 실행 OS 를 따름
        Settings.WindowShadow = true;
        Settings.SdiAboveMain = true;
        Settings.MagnetEnabled = true;
        Settings.MagnetRange = UiSettings.DefaultMagnetRange;
        Settings.EscClose = false;
        Main.DoubleClickCloseEnabled = true;
        Settings.WindowAutoSave = true;
        Settings.CaptureProtection = false;
        Settings.TrayEnabled = false;
        Settings.TabletUiMode = TabletUiMode.Auto;
        Main.Compact.Style.Select(UiStyle.Auto);
        ResetShellToDefaults();
    }

    public override void Dispose()
    {
        Main.PropertyChanged -= OnMainChanged;
        base.Dispose();
    }

    private void OnMainChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.DoubleClickCloseEnabled))
        {
            OnPropertyChanged(nameof(DoubleClickClose));
        }
    }
}
