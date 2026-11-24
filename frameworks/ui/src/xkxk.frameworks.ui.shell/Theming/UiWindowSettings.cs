// UI 설정의 문서 창 크롬(UI-10)·윈도우 메뉴 옵션 — 창 그림자·창 스타일·윈도우 자동저장·Esc 창닫기·자석(켜기·범위)·화면 캡처 방지의 저장·복원과 즉시 반영
using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Dock.Settings;
using xkxk.frameworks.ui.shell.Docking;

namespace xkxk.frameworks.ui.shell.Theming;

/// <summary>UiSettings 가 소유하는 창 관련 설정 묶음. 속성 변경 알림은 UiSettings 가 같은 이름으로 다시 낸다.</summary>
internal sealed class UiWindowSettings : ObservableObject
{
    /// <summary>자석 범위 규칙(UI-12 확정 사양, Mars 실측): 0~48px, 2px 단위, 기본 24.</summary>
    public const int MinMagnetRange = 0;
    public const int MaxMagnetRange = 48;
    public const int MagnetRangeStep = 2;
    public const int DefaultMagnetRange = 24;

    private readonly UiSettings _owner;
    private bool _windowShadow = true;
    private ChromeStyleKind _windowStyle = ChromeStyleKind.Auto; // 기본 = 실행 OS 를 따름(macOS·iOS → macOS 스타일)
    private bool _windowAutoSave = true;
    private bool _escClose;
    private bool _magnetEnabled = true;
    private int _magnetRange = DefaultMagnetRange;
    private bool _captureProtection;
    private string? _selectedMonitorId;
    private bool _sdiAboveMain = true;

    public UiWindowSettings(UiSettings owner) => _owner = owner;

    /// <summary>문서 창(MDI·SDI) 그림자 켜기. 바꾸면 즉시 리소스 재생성(WindowCommands.ShadowEnabled)·저장.</summary>
    public bool WindowShadow
    {
        get => _windowShadow;
        set
        {
            if (SetProperty(ref _windowShadow, value))
            {
                using (UiSettings.Op("ui.frame.shadow")) { WindowCommands.ShadowEnabled = value; }
                _owner.SaveUnlessLoading();
            }
        }
    }

    /// <summary>문서 창 스타일(Windows 11 / macOS / Mars 클래식 / 자동). 바꾸면 즉시 반영(WindowCommands.ChromeStyle)·저장.</summary>
    public ChromeStyleKind WindowStyle
    {
        get => _windowStyle;
        set
        {
            if (SetProperty(ref _windowStyle, value))
            {
                using (UiSettings.Op("ui.frame.style")) { WindowCommands.ChromeStyle = value; }
                _owner.SaveUnlessLoading();
            }
        }
    }

    /// <summary>① 윈도우 자동저장: 종료 때 레이아웃 저장, 다음 기동 때 복원.</summary>
    public bool WindowAutoSave
    {
        get => _windowAutoSave;
        set
        {
            if (SetProperty(ref _windowAutoSave, value))
            {
                _owner.SaveUnlessLoading();
            }
        }
    }

    /// <summary>② Esc 창닫기: Esc 로 활성 문서 창(MDI·SDI)을 닫는다. 입력란·콤보가 Esc 를 쓰는 경우는 가로채지 않는다.</summary>
    public bool EscClose
    {
        get => _escClose;
        set
        {
            if (SetProperty(ref _escClose, value))
            {
                _owner.SaveUnlessLoading();
            }
        }
    }

    /// <summary>③ 자석 켜기. 끄면 범위와 상관없이 붙지 않는다.</summary>
    public bool MagnetEnabled
    {
        get => _magnetEnabled;
        set
        {
            if (SetProperty(ref _magnetEnabled, value))
            {
                OnMagnetChanged();
            }
        }
    }

    /// <summary>③ 자석 범위(px). 0~48 로 자르고 2px 단위로 맞춘다. 0 은 꺼짐과 같다.</summary>
    public int MagnetRange
    {
        get => _magnetRange;
        set
        {
            if (SetProperty(ref _magnetRange, NormalizeMagnetRange(value)))
            {
                OnMagnetChanged();
            }
        }
    }

    /// <summary>MDI·SDI 자석이 실제로 쓰는 범위: 꺼져 있으면 0.</summary>
    public double EffectiveMagnetRange => _magnetEnabled ? _magnetRange : 0;

    /// <summary>⑬ 화면 캡처 방지 설정값. 실제 적용은 호스트(App.WindowHost)가 한다.</summary>
    public bool CaptureProtection
    {
        get => _captureProtection;
        set
        {
            if (SetProperty(ref _captureProtection, value))
            {
                _owner.SaveUnlessLoading();
            }
        }
    }

    /// <summary>U20 '창 ▸ 멀티 모니터' 에서 고른 모니터 식별자. null = 주 모니터(분리된 모니터면 호스트가 주 모니터로 대체).</summary>
    public string? SelectedMonitorId
    {
        get => _selectedMonitorId;
        set
        {
            if (SetProperty(ref _selectedMonitorId, value))
            {
                _owner.SaveUnlessLoading();
            }
        }
    }

    /// <summary>항상 메인 창 위: 켜면 SDI(독립 창)가 메인 창 위에 머문다(기본 켬). 개별 창 '항상 위'와 별개. SdiAboveMainHook 이 읽는다.</summary>
    public bool SdiAboveMain
    {
        get => _sdiAboveMain;
        set
        {
            if (SetProperty(ref _sdiAboveMain, value))
            {
                _owner.SaveUnlessLoading();
            }
        }
    }

    public static int NormalizeMagnetRange(double value)
    {
        var clamped = Math.Clamp(value, MinMagnetRange, MaxMagnetRange);
        return (int)(Math.Round(clamped / MagnetRangeStep, MidpointRounding.AwayFromZero) * MagnetRangeStep);
    }

    /// <summary>Install 때 1회: 기본값을 크롬 쪽 정적 상태와 SDI 자석(Dock 설정)에 맞춘다(저장값 복원 전).</summary>
    public void Install()
    {
        WindowCommands.ShadowEnabled = _windowShadow;
        WindowCommands.ChromeStyle = _windowStyle;

        // 자동화(--frame)·원격(chrome.frame)이 WindowCommands 를 직접 바꿔도 설정 콤보·저장값이 따라오게 한다(odev-7c 이벤트).
        WindowCommands.ChromeStyleChanged += OnChromeStyleChanged;
        SyncDockMagnet();
    }

    /// <summary>크롬 쪽 스타일 변경 수신: 값이 같으면 무시(반복 방지), 다르면 설정값만 맞추고 저장(WindowCommands 는 다시 쓰지 않음).</summary>
    private void OnChromeStyleChanged(ChromeStyleKind style)
    {
        if (_windowStyle == style)
        {
            return;
        }

        SetProperty(ref _windowStyle, style, nameof(WindowStyle));
        _owner.SaveUnlessLoading();
    }

    private void OnMagnetChanged()
    {
        using (UiSettings.Op("ui.magnet.setting")) { SyncDockMagnet(); }
        OnPropertyChanged(nameof(EffectiveMagnetRange));
        _owner.SaveUnlessLoading();
    }

    /// <summary>SDI(OS 창·관리형 창) 자석은 Dock 설정을 따른다 — 같은 켜기·범위로 맞춘다.</summary>
    private void SyncDockMagnet()
    {
        DockSettings.EnableWindowMagnetism = EffectiveMagnetRange > 0;
        DockSettings.WindowMagnetDistance = EffectiveMagnetRange;
    }

    public void FillDto(UiSettingsDto dto)
    {
        dto.WindowShadow = _windowShadow;
        dto.ChromeStyle = _windowStyle;
        dto.MarsFrame = _windowStyle == ChromeStyleKind.MarsClassic;
        dto.WindowAutoSave = _windowAutoSave;
        dto.EscClose = _escClose;
        dto.MagnetEnabled = _magnetEnabled;
        dto.MagnetRange = _magnetRange;
        dto.CaptureProtection = _captureProtection;
        dto.SelectedMonitorId = _selectedMonitorId;
        dto.SdiAboveMain = _sdiAboveMain;
    }

    public void LoadDto(UiSettingsDto dto)
    {
        WindowShadow = dto.WindowShadow;
        // 옛 저장본(ChromeStyle 없음): MarsFrame=true → Mars 클래식, 아니면 자동(실행 OS).
        // 한계(결정 ③): 이미 Windows 로 저장된 값은 그대로 둔다(사용자 설정 파일은 보호 대상이라 옮겨 쓰지 않음) — 그런 사용자는 macOS 에서도 Windows 스타일이며, 설정에서 '자동'을 고르면 OS 를 따른다
        WindowStyle = dto.ChromeStyle ?? (dto.MarsFrame ? ChromeStyleKind.MarsClassic : ChromeStyleKind.Auto);
        WindowAutoSave = dto.WindowAutoSave;
        EscClose = dto.EscClose;
        MagnetEnabled = dto.MagnetEnabled;
        MagnetRange = dto.MagnetRange;
        CaptureProtection = dto.CaptureProtection;
        SelectedMonitorId = dto.SelectedMonitorId;
        SdiAboveMain = dto.SdiAboveMain;
    }
}
