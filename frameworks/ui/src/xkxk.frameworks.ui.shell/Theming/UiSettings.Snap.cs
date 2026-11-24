// 창 배치(스냅) 옵션 10종 — 켜기·칸 종류·감지 거리·막대 지연·가장자리·위쪽 최대화·단축키·SDI 방식·최대화 단추 팝업·팝업 지연의 저장값과 실효값(11_창배치_스냅레이아웃.md §8)
using System;
using System.Collections.Generic;
using Avalonia;
using xkxk.frameworks.ui.shell.Docking;

namespace xkxk.frameworks.ui.shell.Theming;

/// <summary>SDI 창 스냅 방식(설정 값). 저장은 이름 문자열(UiSettingsDto.SnapSdiMode)이고 모르는 값은 Auto 다.</summary>
public enum SnapSdiMode
{
    /// <summary>Windows 이고 OS Snap 이 켜져 있으면 OS 에 맡기고, 그 밖(macOS·꺼짐·판정 불가)은 앱이 한다.</summary>
    Auto,
    /// <summary>OS 판정과 무관하게 앱 막대·미리보기를 쓴다.</summary>
    App,
    /// <summary>앱은 아무것도 하지 않는다(OS 모양이라 Pd 칸 종류와 다르다).</summary>
    Os,
    /// <summary>앱 스냅을 쓰지 않는다. OS 동작은 막지 못한다(한계).</summary>
    Off,
}

/// <summary>SDI 끌기에서 실제로 스냅을 맡는 쪽(SnapSdiMode + OS 판정 결과로 정한다).</summary>
public enum SnapSdiOwner { None, App, Os }

/// <summary>
/// 기본값은 초기 제시값(실측 근거 없음, 11 문서 머리 주의). 자석(MagnetEnabled·MagnetRange)과 같이 값이 바뀌면 저장만 하고(SaveUnlessLoading),
/// 화면 적용은 끌기 시작 때 처리기가 이 값을 읽어 SnapZoneContext 를 만든다(CreateSnapContext). 저장·복원은 FillSnapDto·LoadSnapDto.
/// </summary>
public sealed partial class UiSettings
{
    public const int MinSnapEdgeDistance = 0;
    public const int MaxSnapEdgeDistance = 48;
    public const int SnapEdgeDistanceStep = 2;
    public const int DefaultSnapEdgeDistance = 8;
    public const int MinSnapBarDelayMs = 0;
    public const int MaxSnapBarDelayMs = 1000;
    public const int DefaultSnapBarDelayMs = 150;
    public const int MinSnapFlyoutDelayMs = 0;
    public const int MaxSnapFlyoutDelayMs = 3000;
    public const int DefaultSnapFlyoutDelayMs = 1000;
    public const int MinSnapAssistSdiGraceMs = 100;
    public const int MaxSnapAssistSdiGraceMs = 2000;
    public const int DefaultSnapAssistSdiGraceMs = 400;

    /// <summary>칸 종류 기본값 — 6종 전부(쉼표 구분 id, SnapLayoutCatalog.DefaultIdList).</summary>
    public static string DefaultSnapLayouts => SnapLayoutCatalog.DefaultIdList;

    private bool _snapEnabled = true;
    private string _snapLayouts = SnapLayoutCatalog.DefaultIdList;
    private int _snapEdgeDistance = DefaultSnapEdgeDistance;
    private int _snapBarDelayMs = DefaultSnapBarDelayMs;
    private bool _snapEdgeEnabled = true;
    private bool _snapTopEdgeMaximize = true;
    private bool _snapKeyboard = true;
    private SnapSdiMode _snapSdiMode = SnapSdiMode.Auto;
    private bool _snapFlyoutEnabled = true;
    private int _snapFlyoutDelayMs = DefaultSnapFlyoutDelayMs;
    private bool _snapAssistEnabled = true;
    private bool _snapAssistOnEdge = true;
    private bool _snapAssistThumbnails = true;
    private int _snapAssistSdiGraceMs = DefaultSnapAssistSdiGraceMs;

    /// <summary>Snap Assist(칸 선택 뒤 남은 칸에 놓을 창 고르기, 기본 켬). SnapEnabled 를 끄면 함께 꺼진다.</summary>
    public bool SnapAssistEnabled
    {
        get => _snapAssistEnabled;
        set { if (SetProperty(ref _snapAssistEnabled, value)) { OnSnapChanged(); } }
    }

    /// <summary>가장자리·모서리 스냅 뒤에도 Snap Assist 를 띄움(기본 켬).</summary>
    public bool SnapAssistOnEdge
    {
        get => _snapAssistOnEdge;
        set { if (SetProperty(ref _snapAssistOnEdge, value)) { OnSnapChanged(); } }
    }

    /// <summary>Snap Assist 카드에 창 썸네일 표시(기본 켬). 끄면 아이콘+제목 카드.</summary>
    public bool SnapAssistThumbnails
    {
        get => _snapAssistThumbnails;
        set { if (SetProperty(ref _snapAssistThumbnails, value)) { OnSnapChanged(); } }
    }

    /// <summary>SDI Snap Assist 창 표시 직후 늦은 비활성을 다시 활성화로 되돌리는 유예 ms(기본 400, 100~2000).</summary>
    public int SnapAssistSdiGraceMs
    {
        get => _snapAssistSdiGraceMs;
        set { if (SetProperty(ref _snapAssistSdiGraceMs, NormalizeSnapAssistSdiGraceMs(value))) { OnSnapChanged(); } }
    }

    /// <summary>Snap Assist 를 실제로 쓰는가(전체 끔이면 함께 꺼짐).</summary>
    public bool EffectiveSnapAssistEnabled => _snapEnabled && _snapAssistEnabled;

    /// <summary>가장자리·모서리 스냅 뒤 Snap Assist 를 실제로 쓰는가.</summary>
    public bool EffectiveSnapAssistOnEdge => EffectiveSnapAssistEnabled && _snapAssistOnEdge;

    /// <summary>Snap Assist 썸네일을 실제로 쓰는가.</summary>
    public bool EffectiveSnapAssistThumbnails => EffectiveSnapAssistEnabled && _snapAssistThumbnails;

    /// <summary>창 배치 전체 켜기(기본 켬).</summary>
    public bool SnapEnabled
    {
        get => _snapEnabled;
        set { if (SetProperty(ref _snapEnabled, value)) { OnSnapChanged(); } }
    }

    /// <summary>켜진 칸 종류 id(쉼표 구분, 목록 순서로 정규화). 빈 문자열이면 막대를 띄우지 않는다.</summary>
    public string SnapLayouts
    {
        get => _snapLayouts;
        set { if (SetProperty(ref _snapLayouts, SnapLayoutCatalog.Normalize(value ?? string.Empty))) { OnSnapChanged(); } }
    }

    /// <summary>가장자리 감지 거리 DIP(기본 8, 0~48, 2 단위). 0 이면 가장자리·모서리·막대 감지를 끈다.</summary>
    public int SnapEdgeDistance
    {
        get => _snapEdgeDistance;
        set { if (SetProperty(ref _snapEdgeDistance, NormalizeSnapEdgeDistance(value))) { OnSnapChanged(); } }
    }

    /// <summary>위쪽 머무름 뒤 막대 표시 지연 ms(기본 150, 0~1000).</summary>
    public int SnapBarDelayMs
    {
        get => _snapBarDelayMs;
        set { if (SetProperty(ref _snapBarDelayMs, NormalizeSnapBarDelayMs(value))) { OnSnapChanged(); } }
    }

    /// <summary>좌우 가장자리·모서리 배치(기본 켬). 끄면 막대·위쪽 최대화만 남는다.</summary>
    public bool SnapEdgeEnabled
    {
        get => _snapEdgeEnabled;
        set { if (SetProperty(ref _snapEdgeEnabled, value)) { OnSnapChanged(); } }
    }

    /// <summary>막대 칸 밖 위쪽 가장자리 놓기 → 최대화(기본 켬, oplan 검증 F1). 끄면 그 경우는 이동만 한다.</summary>
    public bool SnapTopEdgeMaximize
    {
        get => _snapTopEdgeMaximize;
        set { if (SetProperty(ref _snapTopEdgeMaximize, value)) { OnSnapChanged(); } }
    }

    /// <summary>앱 단축키(기본 켬). 끄면 창 메뉴 '배치' 만 남는다.</summary>
    public bool SnapKeyboard
    {
        get => _snapKeyboard;
        set { if (SetProperty(ref _snapKeyboard, value)) { OnSnapChanged(); } }
    }

    /// <summary>SDI 창 스냅 방식(기본 자동). 데스크톱만 의미가 있고 웹·모바일 설정 화면은 숨긴다.</summary>
    public SnapSdiMode SnapSdiMode
    {
        get => _snapSdiMode;
        set { if (SetProperty(ref _snapSdiMode, Enum.IsDefined(value) ? value : SnapSdiMode.Auto)) { OnSnapChanged(); } }
    }

    /// <summary>최대화·이전 크기 단추 위에 머무르면 칸 종류 팝업 표시(기본 켬, 사용자 요구 2026-11-11 16:58).</summary>
    public bool SnapFlyoutEnabled
    {
        get => _snapFlyoutEnabled;
        set { if (SetProperty(ref _snapFlyoutEnabled, value)) { OnSnapChanged(); } }
    }

    /// <summary>최대화 단추 위 머무름 뒤 팝업 표시 지연 ms(기본 1000, 0~3000).</summary>
    public int SnapFlyoutDelayMs
    {
        get => _snapFlyoutDelayMs;
        set { if (SetProperty(ref _snapFlyoutDelayMs, NormalizeSnapFlyoutDelayMs(value))) { OnSnapChanged(); } }
    }

    // ── 실효값(끄기 조합 반영) — 처리기는 이 값만 본다 ─────────────────────

    /// <summary>최대화 단추 팝업을 실제로 쓰는가(전체 끔이면 함께 꺼짐).</summary>
    public bool EffectiveSnapFlyoutEnabled => _snapEnabled && _snapFlyoutEnabled;

    /// <summary>실제 감지 거리(전체 끔이면 0).</summary>
    public double EffectiveSnapEdgeDistance => _snapEnabled ? _snapEdgeDistance : 0;

    /// <summary>실제로 쓰는 칸 종류(전체 끔이면 없음).</summary>
    public IReadOnlyList<SnapLayout> EffectiveSnapLayouts => _snapEnabled ? SnapLayoutCatalog.Parse(_snapLayouts) : Array.Empty<SnapLayout>();

    /// <summary>좌우·모서리를 실제로 쓰는가.</summary>
    public bool EffectiveSnapEdgeEnabled => _snapEnabled && _snapEdgeEnabled && _snapEdgeDistance > 0;

    /// <summary>위쪽 가장자리 최대화를 실제로 쓰는가.</summary>
    public bool EffectiveSnapTopEdgeMaximize => _snapEnabled && _snapTopEdgeMaximize && _snapEdgeDistance > 0;

    /// <summary>앱 단축키를 실제로 쓰는가.</summary>
    public bool EffectiveSnapKeyboard => _snapEnabled && _snapKeyboard;

    /// <summary>
    /// 끌기 시작 때 1회 — 현재 옵션으로 판정 문맥을 만든다. area = 기준 영역(DIP), minSize = 창 최소 크기(WindowMinSize.Current 등).
    /// 스냅 끔이면 SnapZoneContext.Disabled.
    /// </summary>
    public SnapZoneContext CreateSnapContext(Rect area, Size minSize) =>
        _snapEnabled
            ? SnapZoneContext.Create(area, EffectiveSnapEdgeDistance, EffectiveSnapLayouts, minSize, EffectiveSnapEdgeEnabled, EffectiveSnapTopEdgeMaximize)
            : SnapZoneContext.Disabled;

    /// <summary>
    /// SDI 끌기에서 스냅을 맡는 쪽. osSnapOn = OsSnapProbe 결과(true 켜짐·false 꺼짐·null 판정 불가). 판정 불가는 앱이다(11 문서 §7.3).
    /// </summary>
    public static SnapSdiOwner ResolveSdiOwner(bool snapEnabled, SnapSdiMode mode, bool? osSnapOn) =>
        !snapEnabled ? SnapSdiOwner.None : mode switch
        {
            SnapSdiMode.App => SnapSdiOwner.App,
            SnapSdiMode.Os => SnapSdiOwner.Os,
            SnapSdiMode.Off => SnapSdiOwner.None,
            _ => osSnapOn == true ? SnapSdiOwner.Os : SnapSdiOwner.App,
        };

    /// <summary>현재 설정 기준 SDI 담당(ResolveSdiOwner 입구).</summary>
    public SnapSdiOwner EffectiveSdiOwner(bool? osSnapOn) => ResolveSdiOwner(_snapEnabled, _snapSdiMode, osSnapOn);

    /// <summary>감지 거리 정규화: 0~48 로 자르고 2 단위로 반올림(자석 범위와 같은 방식).</summary>
    public static int NormalizeSnapEdgeDistance(int value)
    {
        var clamped = Math.Clamp(value, MinSnapEdgeDistance, MaxSnapEdgeDistance);
        return (int)(Math.Round(clamped / (double)SnapEdgeDistanceStep, MidpointRounding.AwayFromZero) * SnapEdgeDistanceStep);
    }

    /// <summary>막대 표시 지연 정규화: 0~1000 ms.</summary>
    public static int NormalizeSnapBarDelayMs(int value) => Math.Clamp(value, MinSnapBarDelayMs, MaxSnapBarDelayMs);

    /// <summary>최대화 단추 팝업 지연 정규화: 0~3000 ms.</summary>
    public static int NormalizeSnapFlyoutDelayMs(int value) => Math.Clamp(value, MinSnapFlyoutDelayMs, MaxSnapFlyoutDelayMs);

    /// <summary>SDI Snap Assist 유예 정규화: 100~2000 ms.</summary>
    public static int NormalizeSnapAssistSdiGraceMs(int value) => Math.Clamp(value, MinSnapAssistSdiGraceMs, MaxSnapAssistSdiGraceMs);

    /// <summary>저장 스냅샷에 Snap 값을 채운다(UiSettings.CreateDto 에서 호출).</summary>
    internal void FillSnapDto(UiSettingsDto dto)
    {
        dto.SnapEnabled = _snapEnabled;
        dto.SnapLayouts = _snapLayouts;
        dto.SnapEdgeDistance = _snapEdgeDistance;
        dto.SnapBarDelayMs = _snapBarDelayMs;
        dto.SnapEdgeEnabled = _snapEdgeEnabled;
        dto.SnapTopEdgeMaximize = _snapTopEdgeMaximize;
        dto.SnapKeyboard = _snapKeyboard;
        dto.SnapSdiMode = _snapSdiMode.ToString();
        dto.SnapFlyoutEnabled = _snapFlyoutEnabled;
        dto.SnapFlyoutDelayMs = _snapFlyoutDelayMs;
        dto.SnapAssistEnabled = _snapAssistEnabled;
        dto.SnapAssistOnEdge = _snapAssistOnEdge;
        dto.SnapAssistThumbnails = _snapAssistThumbnails;
        dto.SnapAssistSdiGraceMs = _snapAssistSdiGraceMs;
    }

    /// <summary>복원(UiSettings.LoadFromDto 에서 호출). 옛 저장본(키 없음, null)은 기본값, 범위 밖 값은 setter 가 잘라 맞춘다.</summary>
    internal void LoadSnapDto(UiSettingsDto dto)
    {
        SnapEnabled = dto.SnapEnabled ?? true;
        SnapLayouts = dto.SnapLayouts ?? SnapLayoutCatalog.DefaultIdList;
        SnapEdgeDistance = dto.SnapEdgeDistance ?? DefaultSnapEdgeDistance;
        SnapBarDelayMs = dto.SnapBarDelayMs ?? DefaultSnapBarDelayMs;
        SnapEdgeEnabled = dto.SnapEdgeEnabled ?? true;
        SnapTopEdgeMaximize = dto.SnapTopEdgeMaximize ?? true;
        SnapKeyboard = dto.SnapKeyboard ?? true;
        SnapSdiMode = Enum.TryParse<SnapSdiMode>(dto.SnapSdiMode, ignoreCase: true, out var mode) && Enum.IsDefined(mode) ? mode : SnapSdiMode.Auto;
        SnapFlyoutEnabled = dto.SnapFlyoutEnabled ?? true;
        SnapFlyoutDelayMs = dto.SnapFlyoutDelayMs ?? DefaultSnapFlyoutDelayMs;
        SnapAssistEnabled = dto.SnapAssistEnabled ?? true;
        SnapAssistOnEdge = dto.SnapAssistOnEdge ?? true;
        SnapAssistThumbnails = dto.SnapAssistThumbnails ?? true;
        SnapAssistSdiGraceMs = dto.SnapAssistSdiGraceMs ?? DefaultSnapAssistSdiGraceMs;
    }

    /// <summary>Snap 값 변경 → 실효값 알림 + 저장(복원 중에는 저장하지 않는다).</summary>
    private void OnSnapChanged()
    {
        OnPropertyChanged(nameof(EffectiveSnapEdgeDistance));
        OnPropertyChanged(nameof(EffectiveSnapLayouts));
        OnPropertyChanged(nameof(EffectiveSnapEdgeEnabled));
        OnPropertyChanged(nameof(EffectiveSnapTopEdgeMaximize));
        OnPropertyChanged(nameof(EffectiveSnapKeyboard));
        OnPropertyChanged(nameof(EffectiveSnapFlyoutEnabled));
        OnPropertyChanged(nameof(EffectiveSnapAssistEnabled));
        OnPropertyChanged(nameof(EffectiveSnapAssistOnEdge));
        OnPropertyChanged(nameof(EffectiveSnapAssistThumbnails));
        SaveUnlessLoading();
    }
}
