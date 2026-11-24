// UI 설정의 시스템 메뉴 항목(UI-17) — 디버그모드·로그 최소 레벨·GPU 가속모드·프로그램 중복모드·트레이·문서 전환·화면 모드의 저장·복원과 즉시 반영(시험 인자 해석은 UiSystemSettings.Args.cs)
using xkxk.frameworks.ui.data;
using xkxk.frameworks.ui.data.Logging;
using xkxk.frameworks.ui.data.Rendering;
using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using xkxk.frameworks.ui.shell.Rendering;

namespace xkxk.frameworks.ui.shell.Theming;

/// <summary>UiSettings 가 소유하는 시스템 메뉴 설정 묶음. 속성 변경 알림은 UiSettings 가 같은 이름으로 다시 낸다.</summary>
internal sealed partial class UiSystemSettings : ObservableObject
{
    /// <summary>시험 인자 이름. 웹·Android 자동 시험이 화면 모드를 이번 실행만 강제한다(설정 파일은 바꾸지 않는다).</summary>
    public const string UiModeArg = "--ui-mode";

    /// <summary>UI 형 강제 인자(--ui-style auto|pc|tablet|mobile). --ui-mode 와 함께 주면 이쪽이 우선한다.</summary>
    public const string UiStyleArg = "--ui-style";

    private readonly UiSettings _owner;
    private GpuMode _gpuMode = xkxk.frameworks.ui.data.Rendering.GpuMode.Auto;
    private bool _allowDuplicate;
    private bool _trayEnabled;
    private bool? _debugMode;
    private PdLogLevel? _logMinLevel;
    private bool _fastDocumentSwitch = true;
    private bool _tabHeaderFirst = DefaultTabHeaderFirst;
    private TabletUiMode _tabletUiMode;
    private TabletUiMode? _tabletUiModeOverride;
    private UiStyle _uiStyle;
    private UiStyle? _uiStyleOverride;
    private PanelFeatureMode _panelFeatureMode;
    private bool _compactWindowKeepsTablet;
    private int _mdiMinGrabDip = DefaultMdiMinGrabDip;

    // MDI 창 끌기 잡을 곳 최소 폭(DIP) — 기본 50, 허용 50~120. 하한 50 은 사용자 결정(끌기 한계 50px 이상)이라 더 낮출 수 없다.
    public const int DefaultMdiMinGrabDip = 50;
    public const int MinMdiMinGrabDip = 50;
    public const int MaxMdiMinGrabDip = 120;

    public UiSystemSettings(UiSettings owner) => _owner = owner;

    /// <summary>
    /// GPU 가속모드(저장값). 바꾸면 즉시 저장만 한다 — 렌더러는 기동 때 한 번 정해지므로 다시 시작해야 적용된다.
    /// 이번 기동에 실제로 적용한 값은 GpuSetting.Applied 이며 저장값과 다를 수 있다(재시작 대기 또는 --gpu 인자).
    /// </summary>
    public GpuMode GpuMode
    {
        get => _gpuMode;
        set
        {
            if (SetProperty(ref _gpuMode, value))
            {
                OnPropertyChanged(nameof(GpuRestartPending));
                _owner.SaveUnlessLoading();
            }
        }
    }

    /// <summary>저장한 GPU 모드가 이번 기동 적용값과 달라 다시 시작해야 반영되는 상태.</summary>
    public bool GpuRestartPending => _gpuMode != GpuSetting.Applied;

    /// <summary>프로그램 중복모드. 바꾸면 즉시 저장하고 다음 실행부터 적용된다(실행 중인 인스턴스 수는 바꾸지 않는다).</summary>
    public bool AllowDuplicateInstance
    {
        get => _allowDuplicate;
        set
        {
            if (SetProperty(ref _allowDuplicate, value))
            {
                _owner.SaveUnlessLoading();
            }
        }
    }

    /// <summary>트레이 상주(저장값, 기본 끔). 바꾸면 즉시 저장한다 — 실사용자가 매번 다시 켜지 않게(team-lead 결정).</summary>
    public bool TrayEnabled
    {
        get => _trayEnabled;
        set
        {
            if (SetProperty(ref _trayEnabled, value))
            {
                _owner.SaveUnlessLoading();
            }
        }
    }

    /// <summary>디버그모드 실제 값(저장값이 없으면 채널 기본값). 바꾸면 로그 상세도에 즉시 반영하고 저장한다.</summary>
    public bool DebugMode
    {
        get => _debugMode ?? AppChannel.DebugDefault;
        set
        {
            if (_debugMode == value)
            {
                return;
            }

            _debugMode = value;
            ApplyLogSettings();
            if (!_owner.IsLoading)
            {
                // Mars 와 같이 토글 때 로그에 경계선을 남긴다(값은 남기지 않는 개인정보 규칙과 무관한 설정 상태만)
                PdLog.WriteSystem(PdLogLevel.Information, value ? "----- debug mode on -----" : "----- debug mode off -----");
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(EffectiveLogLevel));
            _owner.SaveUnlessLoading();
        }
    }

    /// <summary>사용자가 고른 로그 최소 레벨(null = 디버그모드에 따름). 바꾸면 즉시 반영·저장한다.</summary>
    public PdLogLevel? LogMinLevel
    {
        get => _logMinLevel;
        set
        {
            if (SetProperty(ref _logMinLevel, value))
            {
                ApplyLogSettings();
                OnPropertyChanged(nameof(EffectiveLogLevel));
                _owner.SaveUnlessLoading();
            }
        }
    }

    /// <summary>
    /// 문서 전환 가속(저장값, 기본 켬). 켜면 ① 탭형(DOCK)에서 비활성 탭 문서 뷰를 화면 트리에서 떼지 않고 숨겨 두고
    /// ② 문서 본문을 Dock 지연 표시(Background 한 바퀴 + 0.09초 투명도 효과) 없이 바로 붙인다. 끄면 Dock 기본 동작으로 돌아간다.
    /// ①은 새로 만들어지는 탭 영역(모드 전환·창 분리·재기동)부터, ②는 다음 문서 전환부터 적용된다.
    /// </summary>
    public bool FastDocumentSwitch
    {
        get => _fastDocumentSwitch;
        set
        {
            if (SetProperty(ref _fastDocumentSwitch, value))
            {
                _owner.SaveUnlessLoading();
            }
        }
    }

    /// <summary>탭 머리 먼저 그리기 기본값: 웹만 켬. 웹은 렌더 스레드가 없어 무거운 본문 교체가 끝날 때까지 탭 선택 표시도 그려지지 않는다. 데스크톱·Android 는 끔(기존 동작).</summary>
    public static bool DefaultTabHeaderFirst => OperatingSystem.IsBrowser();

    /// <summary>
    /// 탭 머리 먼저 그리기(저장값, 기본 웹만 켬). 켜면 탭형 탭 전환에서 누른 탭의 선택 표시를 먼저 그리고 본문 교체는 다음 프레임 뒤에 한다
    /// (xkxk.frameworks.ui.views/DocumentTabKeepAlive.cs). 대가: 새 본문이 약 1프레임 늦게 보인다. 문서 전환 가속이 켜진 탭 영역에서만 의미가 있고, 다음 탭 전환부터 적용된다.
    /// </summary>
    public bool TabHeaderFirst
    {
        get => _tabHeaderFirst;
        set
        {
            if (SetProperty(ref _tabHeaderFirst, value))
            {
                _owner.SaveUnlessLoading();
            }
        }
    }

    /// <summary>화면 모드(저장값). 바꾸면 즉시 저장하고 화면이 바로 바뀐다(재시작 불필요).</summary>
    public TabletUiMode TabletUiMode
    {
        get => _tabletUiMode;
        set
        {
            if (SetProperty(ref _tabletUiMode, value))
            {
                OnPropertyChanged(nameof(EffectiveTabletUiMode));
                _owner.SaveUnlessLoading();
            }
        }
    }

    /// <summary>시험 인자 --ui-mode auto|tablet|phone 로 이번 실행만 강제한 값(저장하지 않음). null 이면 저장값을 쓴다.</summary>
    public TabletUiMode? TabletUiModeOverride
    {
        get => _tabletUiModeOverride;
        set
        {
            if (SetProperty(ref _tabletUiModeOverride, value))
            {
                OnPropertyChanged(nameof(EffectiveTabletUiMode));
            }
        }
    }

    /// <summary>실제로 쓰는 화면 모드(시험 강제값 우선).</summary>
    public TabletUiMode EffectiveTabletUiMode => _tabletUiModeOverride ?? _tabletUiMode;

    /// <summary>UI 형 선택값(저장값, 기본 자동). 값만 보관하며 해석은 UiStyleRules 가 한다(폰 기기는 어떤 값이든 모바일형). 바꾸면 즉시 저장한다.</summary>
    public UiStyle UiStyle
    {
        get => _uiStyle;
        set
        {
            if (SetProperty(ref _uiStyle, value))
            {
                OnPropertyChanged(nameof(EffectiveUiStyle));
                _owner.SaveUnlessLoading();
            }
        }
    }

    /// <summary>시험 인자 --ui-style 로 이번 실행만 강제한 값(저장하지 않음, --ui-mode 별칭 포함). null 이면 저장값.</summary>
    public UiStyle? UiStyleOverride
    {
        get => _uiStyleOverride;
        set
        {
            if (SetProperty(ref _uiStyleOverride, value))
            {
                OnPropertyChanged(nameof(EffectiveUiStyle));
            }
        }
    }

    /// <summary>실제로 쓰는 UI 형 선택값(시험 강제값 우선).</summary>
    public UiStyle EffectiveUiStyle => _uiStyleOverride ?? _uiStyle;

    /// <summary>패널 기능 모드(저장값, 기본 자동). 해석은 PanelFeatureRules.IsEnabled 가 한다. 바꾸면 즉시 저장한다.</summary>
    public PanelFeatureMode PanelFeatureMode
    {
        get => _panelFeatureMode;
        set { if (SetProperty(ref _panelFeatureMode, value)) { _owner.SaveUnlessLoading(); } }
    }

    /// <summary>창 폭이 600dp 미만이어도 태블릿형을 유지(저장값, 기본 끔). 바꾸면 즉시 저장한다.</summary>
    public bool CompactWindowKeepsTablet
    {
        get => _compactWindowKeepsTablet;
        set { if (SetProperty(ref _compactWindowKeepsTablet, value)) { _owner.SaveUnlessLoading(); } }
    }

    /// <summary>
    /// MDI 창 끌기 잡을 곳 최소 폭(DIP, 저장값, 기본 50). 범위 밖 값은 거부하지 않고 50~120 으로 잘라 맞춘다.
    /// 끌기 경계·기능 버튼 줄 접기 판정이 이 값을 읽는다. 바꾸면 즉시 저장한다.
    /// </summary>
    public int MdiMinGrabDip
    {
        get => _mdiMinGrabDip;
        set { if (SetProperty(ref _mdiMinGrabDip, ClampMdiMinGrabDip(value))) { _owner.SaveUnlessLoading(); } }
    }

    public static int ClampMdiMinGrabDip(int value) => Math.Clamp(value, MinMdiMinGrabDip, MaxMdiMinGrabDip);

    /// <summary>현재 유효한 로그 최소 레벨(메뉴 체크 표시용).</summary>
    public PdLogLevel EffectiveLogLevel => PdLog.MinLevel;

    private void ApplyLogSettings() => PdLog.Configure(_debugMode, _logMinLevel);

    public void FillDto(UiSettingsDto dto)
    {
        dto.GpuMode = _gpuMode;
        dto.AllowDuplicateInstance = _allowDuplicate;
        dto.TrayEnabled = _trayEnabled;
        dto.DebugMode = _debugMode;
        dto.LogMinLevel = _logMinLevel;
        dto.FastDocumentSwitch = _fastDocumentSwitch;
        dto.TabHeaderFirst = _tabHeaderFirst;
        dto.TabletUiMode = _tabletUiMode; // 시험 강제값(_tabletUiModeOverride)은 저장하지 않는다
        dto.UiStyle = _uiStyle; // 시험 강제값(_uiStyleOverride)도 저장하지 않는다
        dto.PanelFeatureMode = _panelFeatureMode;
        dto.CompactWindowKeepsTablet = _compactWindowKeepsTablet;
        FillTimingDto(dto);
        dto.MdiMinGrabDip = _mdiMinGrabDip;
        FillMdiDto(dto);
    }

    public void LoadDto(UiSettingsDto dto)
    {
        GpuMode = dto.GpuMode ?? xkxk.frameworks.ui.data.Rendering.GpuMode.Auto;
        AllowDuplicateInstance = dto.AllowDuplicateInstance;
        TrayEnabled = dto.TrayEnabled;
        FastDocumentSwitch = dto.FastDocumentSwitch ?? true; // 옛 저장본(필드 없음)은 켬
        TabHeaderFirst = dto.TabHeaderFirst ?? DefaultTabHeaderFirst; // 옛 저장본(필드 없음)은 플랫폼 기본값
        TabletUiMode = dto.TabletUiMode;
        UiStyle = dto.UiStyle ?? FromLegacy(dto.TabletUiMode); // 옛 저장본(필드 없음)은 옛 키에서 이전, 옛 키도 기본이면 자동
        PanelFeatureMode = dto.PanelFeatureMode ?? default; // 옛 저장본(필드 없음)은 자동
        CompactWindowKeepsTablet = dto.CompactWindowKeepsTablet ?? false; // 옛 저장본(필드 없음)은 끔
        LoadTimingDto(dto);
        MdiMinGrabDip = dto.MdiMinGrabDip ?? DefaultMdiMinGrabDip; // 옛 저장본(필드 없음)은 50, 범위 밖 값은 setter 가 잘라 맞춘다
        LoadMdiDto(dto);
        _debugMode = dto.DebugMode;
        _logMinLevel = dto.LogMinLevel;
        ApplyLogSettings();
        OnPropertyChanged(nameof(DebugMode));
        OnPropertyChanged(nameof(LogMinLevel));
        OnPropertyChanged(nameof(EffectiveLogLevel));
    }
}
