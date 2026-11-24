// 시스템(셸) 글자 크기·글꼴과 제목 줄(셸 탭 줄) 높이 설정 — 문서 창 밖 화면에 적용(사용자 지시 2026-11-11, 01 §2-1·§5.2)
using System;
using System.Linq;
using Pd.Client.Data.Theming;
using Pd.Poc.Shell.Platform;

namespace Pd.Poc.Shell.Theming;

/// <summary>제목 탭 닫기(×) 표시 방식(01 §2-1, 2026-11-12). 저장은 이름 문자열(UiSettingsDto.TitleTabCloseMode)이고 모르는 값·옛 저장본은 Auto 다.</summary>
public enum TitleTabCloseMode
{
    /// <summary>입력 장치로 정한다: 마우스(데스크톱·PC 브라우저)는 Hover, 터치가 주 입력인 장치는 Always(InputFormFactor.IsTouchDevice).</summary>
    Auto,

    /// <summary>모든 탭에 항상 보인다(2026-11-12 이전 동작).</summary>
    Always,

    /// <summary>탭에 포인터가 올라가 있거나 탭 안에 키보드 포커스가 있을 때만 보인다.</summary>
    Hover,

    /// <summary>선택(활성) 탭은 항상, 나머지는 Hover 와 같다.</summary>
    ActiveAndHover,
}

/// <summary>
/// 시스템 글자는 앱 전역 토큰(PdFont*·PdFontFamily)으로, 문서 창 안은 문서 래퍼가 문서 값(BaseFontSize·FontName)으로 다시 덮는다(UiSettingsApplier).
/// 적용은 UiSettingsApplier.ApplyShell(크기·높이)·Family(글꼴)이고, 저장은 UiSettingsDto 의 같은 이름 키다(옛 저장본은 UiSettings.LoadFromDto 가 기본값·문서 글꼴로 채운다).
/// </summary>
public sealed partial class UiSettings
{
    /// <summary>제목 줄(셸 탭 줄) 높이 기본값 DIP — 사용자 지시 2026-11-11 '2배 정도'(이전 19). 범위는 아래 Min~Max(01 §2-1).</summary>
    public const double DefaultShellTabStripHeight = 38;
    public const double MinShellTabStripHeight = 19;
    public const double MaxShellTabStripHeight = 48;

    private double _shellFontSize = DefaultFontSize;
    private string _shellFontName = DefaultFontName;
    private double _shellTabStripHeight = DefaultShellTabStripHeight;

    /// <summary>제목 탭 높이 = 줄 높이 × 16/19 반올림(이전 19·16 의 비율, 기본 38 → 32).</summary>
    public static double TitleTabHeightFor(double stripHeight) => Math.Round(stripHeight * 16 / 19);

    /// <summary>시스템(셸) 글자 크기 — 메뉴 줄·제목 줄 탭·패널·상태바 등 문서 창 밖. 앱 전역 PdFont* 토큰 기준값(기본 14, 10~24).</summary>
    public double ShellFontSize
    {
        get => _shellFontSize;
        set
        {
            if (SetProperty(ref _shellFontSize, Math.Clamp(Math.Round(value), MinFontSize, MaxFontSize)))
            {
                using (Op("ui.font.shellSize")) { ApplyAndSave(ApplyParts.Shell); }
            }
        }
    }

    /// <summary>시스템(셸) 글꼴 — 앱 전역 PdFontFamily. 문서 창 안은 FontName 이 덮는다.</summary>
    public string ShellFontName
    {
        get => _shellFontName;
        set
        {
            if (SetProperty(ref _shellFontName, string.IsNullOrWhiteSpace(value) ? DefaultFontName : value))
            {
                using (Op("ui.font.shellFamily")) { ApplyAndSave(ApplyParts.Family); }
            }
        }
    }

    /// <summary>제목 줄(셸 탭 줄) 높이 DIP(기본 38, 19~48). 제목 탭 높이는 TitleTabHeightFor 로 따라간다.</summary>
    public double ShellTabStripHeight
    {
        get => _shellTabStripHeight;
        set
        {
            if (SetProperty(ref _shellTabStripHeight, Math.Clamp(Math.Round(value), MinShellTabStripHeight, MaxShellTabStripHeight)))
            {
                using (Op("ui.shell.tabStrip")) { ApplyAndSave(ApplyParts.Shell); }
            }
        }
    }

    private TitleTabCloseMode _titleTabCloseMode = TitleTabCloseMode.Auto;

    /// <summary>제목 탭 닫기(×) 표시 방식(기본 Auto). 화면(MainView)이 PropertyChanged 로 받아 즉시 반영하고, 리소스 적용은 없어 저장만 한다.</summary>
    public TitleTabCloseMode TitleTabCloseMode
    {
        get => _titleTabCloseMode;
        set
        {
            if (SetProperty(ref _titleTabCloseMode, Enum.IsDefined(value) ? value : TitleTabCloseMode.Auto))
            {
                SaveUnlessLoading();
            }
        }
    }

    /// <summary>Auto 를 실제 방식(Always·Hover·ActiveAndHover)으로 푼다. 터치 판정은 폼팩터 판정(InputFormFactor) 한 곳을 쓴다.</summary>
    public static TitleTabCloseMode ResolveTitleTabCloseMode(TitleTabCloseMode mode, bool isTouchDevice) => mode switch
    {
        TitleTabCloseMode.Always or TitleTabCloseMode.Hover or TitleTabCloseMode.ActiveAndHover => mode,
        _ => isTouchDevice ? TitleTabCloseMode.Always : TitleTabCloseMode.Hover,
    };

    /// <summary>지금 기기에서 쓸 제목 탭 × 표시 방식.</summary>
    public TitleTabCloseMode EffectiveTitleTabCloseMode => ResolveTitleTabCloseMode(_titleTabCloseMode, InputFormFactor.IsTouchDevice);

    // 결정 143 키(제목 탭 표시·메뉴 아이콘·즐겨찾기 영역·불러오는 중 아이콘·전환 효과 출처·배경 이미지). 뜻은 09/21·22·23 설계, 저장은 UiSettingsDto.Motion.cs(희소).
    // 화면은 PropertyChanged 로 받아 반영하고 여기서는 저장만 한다. 끌기 중 그림자는 UiSystemSettings.MdiMoveLightShadow 다.
    public const MotionSwitchSources DefaultMotionSwitchSources = MotionSwitchSources.Keyboard | MotionSwitchSources.TitleTab | MotionSwitchSources.WindowList;
    public const double MaxBackgroundImageDim = 0.8;
    private const MotionSwitchSources AllMotionSwitchSources = MotionSwitchSources.Keyboard | MotionSwitchSources.TitleTab | MotionSwitchSources.WindowList | MotionSwitchSources.BodyClick;

    private TitleTabDisplay _titleTabDisplay;
    private bool _menuIconVisible = true;
    private bool _favoriteBarVisible = true;
    private FavoriteBarPosition _favoriteBarPosition;
    private FavoriteBarClick _favoriteBarClick;
    private bool _busyIconEnabled = true;
    private BusyTitleOnly _busyIconTitleOnly;
    private MotionSwitchSources? _motionSwitchSources;
    private string? _backgroundImageFile;
    private BackgroundImageFit _backgroundImageFit;
    private double _backgroundImageOpacity = 1;
    private double _backgroundImageDim;
    private readonly UiSettingsDto _operating = new(); // 운영 값 — 화면에서 바꾸지 않고 읽은 값을 그대로 다시 쓴다(희소)
    private string _effectLevel = DesignThemes.LevelTheme;

    /// <summary>효과 단계 theme.style.effectLevel(theme·high·medium·low, 기본 theme). 덮어쓰기와 따로 저장하고 UiSettingsApplier 가 DesignThemeResolver.SetEffectLevel 로 공급한다(D-591).</summary>
    public string EffectLevel
    {
        get => _effectLevel;
        set
        {
            var v = DesignThemes.TryNormalize(DesignThemes.EffectLevelKey, value, out var n) ? n : DesignThemes.LevelTheme;
            if (SetProperty(ref _effectLevel, v)) { ApplyAndSave(ApplyParts.Style); }
        }
    }

    /// <summary>사용자가 고른 끌기 중 그림자(null = 고르지 않음). 실효값은 Docking.MdiMoveLight.Shadow.</summary>
    public Docking.MoveShadowMode? MdiMoveLightShadowUser { get => _system.MdiMoveLightShadowUser; set => _system.MdiMoveLightShadowUser = value; }

    /// <summary>효과 단계 표시용 ui 묶음 키 낱개 덮어쓰기 수(DesignThemeResolver.LevelDisplay 인자).</summary>
    public int MoveShadowUserOverrideCount => _system.MdiMoveLightShadowUser is null ? 0 : 1;

    /// <summary>ui.shell.title_tab.display(기본 icon_title).</summary>
    public TitleTabDisplay TitleTabDisplay { get => _titleTabDisplay; set { if (SetProperty(ref _titleTabDisplay, Enum.IsDefined(value) ? value : default)) { SaveUnlessLoading(); } } }

    /// <summary>ui.menu.icon.visible(기본 켬).</summary>
    public bool MenuIconVisible { get => _menuIconVisible; set { if (SetProperty(ref _menuIconVisible, value)) { SaveUnlessLoading(); } } }

    /// <summary>ui.shell.favorite_bar.visible(기본 켬).</summary>
    public bool FavoriteBarVisible { get => _favoriteBarVisible; set { if (SetProperty(ref _favoriteBarVisible, value)) { SaveUnlessLoading(); } } }

    /// <summary>ui.shell.favorite_bar.position(기본 end).</summary>
    public FavoriteBarPosition FavoriteBarPosition { get => _favoriteBarPosition; set { if (SetProperty(ref _favoriteBarPosition, Enum.IsDefined(value) ? value : default)) { SaveUnlessLoading(); } } }

    /// <summary>ui.shell.favorite_bar.click(기본 activate_or_open).</summary>
    public FavoriteBarClick FavoriteBarClick { get => _favoriteBarClick; set { if (SetProperty(ref _favoriteBarClick, Enum.IsDefined(value) ? value : default)) { SaveUnlessLoading(); } } }

    /// <summary>ui.shell.busy_icon.enabled(기본 켬).</summary>
    public bool BusyIconEnabled { get => _busyIconEnabled; set { if (SetProperty(ref _busyIconEnabled, value)) { SaveUnlessLoading(); } } }

    /// <summary>ui.shell.busy_icon.title_only(기본 inline).</summary>
    public BusyTitleOnly BusyIconTitleOnly { get => _busyIconTitleOnly; set { if (SetProperty(ref _busyIconTitleOnly, Enum.IsDefined(value) ? value : default)) { SaveUnlessLoading(); } } }

    /// <summary>사용자가 고른 ui.motion.switch_sources(null = 고르지 않음, None = 전부 끔). 모르는 비트는 버린다.</summary>
    public MotionSwitchSources? MotionSwitchSourcesUser
    {
        get => _motionSwitchSources;
        set
        {
            if (SetProperty(ref _motionSwitchSources, value & AllMotionSwitchSources))
            {
                OnPropertyChanged(nameof(MotionSwitchSources));
                SaveUnlessLoading();
            }
        }
    }

    /// <summary>ui.motion.switch_sources 실효값(21 §3.2): 잠금이면 회사 값 ?? 기본, 아니면 사용자 ?? 회사 ?? 기본. 효과 단계 묶음은 이 키를 정하지 않는다.</summary>
    public MotionSwitchSources MotionSwitchSources
    {
        get
        {
            var user = _motionSwitchSources is { } v ? (v == MotionSwitchSources.None ? "none" : UiKeyNames.OfFlags(v)) : null; // 해석기는 빈 문자열을 '고르지 않음'으로 본다
            return UiKeyNames.ParseFlags(DesignThemeResolver.ResolveUiKey("ui.motion.switch_sources", user, UiKeyNames.OfFlags(DefaultMotionSwitchSources)), DefaultMotionSwitchSources);
        }
    }

    /// <summary>ui.background.image.file — 해시 이름(BackgroundImageStore.NamePattern)만 받고 그 밖의 값은 없음(null)으로 둔다.</summary>
    public string? BackgroundImageFile { get => _backgroundImageFile; set { if (SetProperty(ref _backgroundImageFile, BackgroundImageStore.IsValidName(value) ? value : null)) { SaveUnlessLoading(); } } }

    /// <summary>ui.background.image.fit(기본 fill).</summary>
    public BackgroundImageFit BackgroundImageFit { get => _backgroundImageFit; set { if (SetProperty(ref _backgroundImageFit, Enum.IsDefined(value) ? value : default)) { SaveUnlessLoading(); } } }

    /// <summary>ui.background.image.opacity(0~1, 기본 1).</summary>
    public double BackgroundImageOpacity { get => _backgroundImageOpacity; set { if (SetProperty(ref _backgroundImageOpacity, double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 1)) { SaveUnlessLoading(); } } }

    /// <summary>ui.background.image.dim(0~0.8, 기본 0).</summary>
    public double BackgroundImageDim { get => _backgroundImageDim; set { if (SetProperty(ref _backgroundImageDim, double.IsFinite(value) ? Math.Clamp(value, 0, MaxBackgroundImageDim) : 0)) { SaveUnlessLoading(); } } }

    // 운영 값(읽기 전용, 기본값은 plt_2 §12.1). 음수 같은 손 편집 오류만 막고 범위는 정하지 않는다.
    public int TitleTabIconOnlyWidthDp => Math.Max(1, _operating.TitleTabIconOnlyWidthDp ?? 32);
    public int FavoriteBarMaxWidthPct => Math.Clamp(_operating.FavoriteBarMaxWidthPct ?? 30, 1, 100);
    public int BusyIconShowDelayMs => Math.Max(0, _operating.BusyIconShowDelayMs ?? 0);
    public int BusyIconMinVisibleMs => Math.Max(0, _operating.BusyIconMinVisibleMs ?? 200);
    public int BusyIconSlowSeconds => Math.Max(1, _operating.BusyIconSlowSeconds ?? 10);
    public int BusyIconErrorBadgeSeconds => Math.Max(0, _operating.BusyIconErrorBadgeSeconds ?? 5);
    public int BackgroundImageMaxDimPx => Math.Max(1, _operating.BackgroundImageMaxDimPx ?? 3840);

    /// <summary>배경 이미지 가져오기 상한(max_bytes·max_source_px·formats).</summary>
    public BackgroundImageLimits BackgroundImageLimits
    {
        get
        {
            var formats = _operating.BackgroundImageFormats?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(f => f.ToLowerInvariant()).ToArray();
            return new BackgroundImageLimits(
                Math.Max(1, _operating.BackgroundImageMaxBytes ?? BackgroundImageLimits.DefaultMaxBytes),
                Math.Max(1, _operating.BackgroundImageMaxSourcePx ?? BackgroundImageLimits.DefaultMaxSourcePx),
                formats is { Length: > 0 } ? formats : BackgroundImageLimits.DefaultFormats);
        }
    }

    /// <summary>저장(UiSettings.CreateDto 가 부른다). 이름 문자열로 둬 enum 순서가 바뀌어도 저장본이 깨지지 않게 한다. 결정 143 키는 기본값이면 쓰지 않는다.</summary>
    private void FillShellDto(UiSettingsDto dto)
    {
        dto.TitleTabCloseMode = _titleTabCloseMode.ToString();
        dto.TitleTabDisplay = _titleTabDisplay == default ? null : UiKeyNames.Of(_titleTabDisplay);
        dto.MenuIconVisible = _menuIconVisible ? null : false;
        dto.FavoriteBarVisible = _favoriteBarVisible ? null : false;
        dto.FavoriteBarPosition = _favoriteBarPosition == default ? null : UiKeyNames.Of(_favoriteBarPosition);
        dto.FavoriteBarClick = _favoriteBarClick == default ? null : UiKeyNames.Of(_favoriteBarClick);
        dto.BusyIconEnabled = _busyIconEnabled ? null : false;
        dto.BusyIconTitleOnly = _busyIconTitleOnly == default ? null : UiKeyNames.Of(_busyIconTitleOnly);
        dto.MotionSwitchSources = _motionSwitchSources is { } sources ? UiKeyNames.OfFlags(sources) : null;
        dto.BackgroundImageFile = _backgroundImageFile;
        dto.BackgroundImageFit = _backgroundImageFit == default ? null : UiKeyNames.Of(_backgroundImageFit);
        dto.BackgroundImageOpacity = _backgroundImageOpacity == 1 ? null : _backgroundImageOpacity;
        dto.BackgroundImageDim = _backgroundImageDim == 0 ? null : _backgroundImageDim;
        dto.EffectLevel = _effectLevel == DesignThemes.LevelTheme ? null : _effectLevel;
        _operating.CopyOperatingValuesTo(dto);
    }

    /// <summary>복원(UiSettings.LoadFromDto 가 부른다). 옛 저장본(null)·모르는 값은 기본값이다(TitleTabCloseMode 는 숫자 문자열도 Auto).</summary>
    private void LoadShellDto(UiSettingsDto dto)
    {
        TitleTabCloseMode = !string.IsNullOrWhiteSpace(dto.TitleTabCloseMode)
            && !char.IsDigit(dto.TitleTabCloseMode.Trim()[0])
            && Enum.TryParse<TitleTabCloseMode>(dto.TitleTabCloseMode, ignoreCase: true, out var m) && Enum.IsDefined(m)
                ? m : TitleTabCloseMode.Auto;
        TitleTabDisplay = UiKeyNames.Parse(dto.TitleTabDisplay, TitleTabDisplay.IconTitle);
        MenuIconVisible = dto.MenuIconVisible ?? true;
        FavoriteBarVisible = dto.FavoriteBarVisible ?? true;
        FavoriteBarPosition = UiKeyNames.Parse(dto.FavoriteBarPosition, FavoriteBarPosition.End);
        FavoriteBarClick = UiKeyNames.Parse(dto.FavoriteBarClick, FavoriteBarClick.ActivateOrOpen);
        BusyIconEnabled = dto.BusyIconEnabled ?? true;
        BusyIconTitleOnly = UiKeyNames.Parse(dto.BusyIconTitleOnly, BusyTitleOnly.Inline);
        MotionSwitchSourcesUser = dto.MotionSwitchSources is { } sources ? UiKeyNames.ParseFlags(sources, DefaultMotionSwitchSources) : null;
        BackgroundImageFile = dto.BackgroundImageFile;
        BackgroundImageFit = UiKeyNames.Parse(dto.BackgroundImageFit, BackgroundImageFit.Fill);
        BackgroundImageOpacity = dto.BackgroundImageOpacity ?? 1;
        BackgroundImageDim = dto.BackgroundImageDim ?? 0;
        EffectLevel = dto.EffectLevel ?? DesignThemes.LevelTheme;
        dto.CopyOperatingValuesTo(_operating);
        Docking.WindowLoadState.ConfigureTiming(() => BusyIconSlowSeconds); // 지연 판정(ui.shell.busy_icon.slow_seconds)을 불러오는 중 계약에 잇는다 — 공급자라 운영 값이 바뀌면 다음 판정부터 따른다
    }
}
