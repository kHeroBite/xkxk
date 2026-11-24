// UI 설정 시스템 묶음의 시간·예열 항목(바쁨 표시 지연·최소 유지, 뷰 예열) — UiSystemSettings.cs 줄 수 상한으로 나눈 부분
using System;

namespace Pd.Poc.Shell.Theming;

internal sealed partial class UiSystemSettings
{
    // 바쁨 표시 기본값(ms) — 지금 상수(BusyService.ShowDelayMs·MinVisibleMs)와 같다. 상한 밖 값은 잘라 맞춘다.
    public const int DefaultBusyShowDelayMs = 150;
    public const int DefaultBusyMinVisibleMs = 150;
    public const int MaxBusyMs = 5000;

    private int _busyShowDelayMs = DefaultBusyShowDelayMs;
    private int _busyMinVisibleMs = DefaultBusyMinVisibleMs;
    private bool _prewarmViews = true;

    /// <summary>바쁨 표시 지연(ms, 기본 150). 이 시간 안에 끝나는 작업은 표시하지 않는다.</summary>
    public int BusyShowDelayMs
    {
        get => _busyShowDelayMs;
        set { if (SetProperty(ref _busyShowDelayMs, ClampBusy(value))) { _owner.SaveUnlessLoading(); } }
    }

    /// <summary>바쁨 표시 최소 유지(ms, 기본 150). 표시를 시작하면 이 시간 동안 유지한다.</summary>
    public int BusyMinVisibleMs
    {
        get => _busyMinVisibleMs;
        set { if (SetProperty(ref _busyMinVisibleMs, ClampBusy(value))) { _owner.SaveUnlessLoading(); } }
    }

    /// <summary>뷰 예열(첫 화면 뒤 grid·form·gantt 뷰를 화면 밖에서 만들어 둠, 기본 켬). 다음 기동부터 적용된다.</summary>
    public bool PrewarmViews
    {
        get => _prewarmViews;
        set { if (SetProperty(ref _prewarmViews, value)) { _owner.SaveUnlessLoading(); } }
    }

    private static int ClampBusy(int value) => Math.Clamp(value, 0, MaxBusyMs);

    // 표 옵션(화면 밖 셀 지연·단일 요소 셀·그리는 셀 글자·준비된 표 뷰)은 UiSystemSettings.Grid.cs 로 옮겼다(300줄 규칙)

    // 끌기 중 가벼운 크롬(기본 켬, 조용 시간 기본·범위는 Docking.MdiMoveLight 상수가 정본 — 지금 300ms, 50~1000) — 값은 Docking.MdiMoveLight 로 바로 반영한다(Theming→Docking 방향)
    public const int DefaultMdiMoveLightQuietMs = Docking.MdiMoveLight.DefaultQuietMs;
    public const int MinMdiMoveLightQuietMs = Docking.MdiMoveLight.MinQuietMs;
    public const int MaxMdiMoveLightQuietMs = Docking.MdiMoveLight.MaxQuietMs;

    private bool _mdiMoveLight = true;
    private int _mdiMoveLightQuietMs = DefaultMdiMoveLightQuietMs;

    /// <summary>끌기 중 가벼운 크롬 사용 여부(기본 켬). 바꾸면 즉시 저장하고 MdiMoveLight.Enabled 에 반영한다.</summary>
    public bool MdiMoveLight
    {
        get => _mdiMoveLight;
        set
        {
            if (SetProperty(ref _mdiMoveLight, value)) { _owner.SaveUnlessLoading(); }
            Docking.MdiMoveLight.Enabled = _mdiMoveLight;
        }
    }

    /// <summary>끌기 중 가벼운 크롬의 조용 시간(ms, 기본 300 — 정본 Docking.MdiMoveLight.DefaultQuietMs). 범위 밖 값은 50~1000 으로 잘라 맞춘다.</summary>
    public int MdiMoveLightQuietMs
    {
        get => _mdiMoveLightQuietMs;
        set
        {
            if (SetProperty(ref _mdiMoveLightQuietMs, ClampMdiMoveLightQuietMs(value))) { _owner.SaveUnlessLoading(); }
            Docking.MdiMoveLight.QuietMs = _mdiMoveLightQuietMs;
        }
    }

    public static int ClampMdiMoveLightQuietMs(int value) => Math.Clamp(value, MinMdiMoveLightQuietMs, MaxMdiMoveLightQuietMs);

    private bool _mdiMoveLightBitmapCache = Docking.MdiMoveLight.DefaultBitmapCache;

    /// <summary>끌기 중 비트맵 캐시(MdiMoveLight 의 하위 옵션, 기본 끔 — 정본 Docking.MdiMoveLight.DefaultBitmapCache, 근거 01 §4.2). 바꾸면 즉시 저장하고 반영한다.</summary>
    public bool MdiMoveLightBitmapCache
    {
        get => _mdiMoveLightBitmapCache;
        set
        {
            if (SetProperty(ref _mdiMoveLightBitmapCache, value)) { _owner.SaveUnlessLoading(); }
            Docking.MdiMoveLight.BitmapCache = _mdiMoveLightBitmapCache;
        }
    }

    private bool _mdiMoveLightStaticCache = Docking.MdiMoveLight.DefaultStaticCache;

    /// <summary>끌기 중 정지 아래 창 캐시(기본 끔 — 정본 Docking.MdiMoveLight.DefaultStaticCache, 근거 01 §4.2). 바꾸면 즉시 저장하고 반영한다.</summary>
    public bool MdiMoveLightStaticCache
    {
        get => _mdiMoveLightStaticCache;
        set
        {
            if (SetProperty(ref _mdiMoveLightStaticCache, value)) { _owner.SaveUnlessLoading(); }
            Docking.MdiMoveLight.StaticCache = _mdiMoveLightStaticCache;
        }
    }

    private bool _mdiMoveLightSiblings = Docking.MdiMoveLight.DefaultSiblings;

    /// <summary>끌기 중 겹친 정지 창 가벼운 크롬(기본 켬 — 정본 Docking.MdiMoveLight.DefaultSiblings, 근거 01 §4.2 N2). 바꾸면 즉시 저장하고 반영한다.</summary>
    public bool MdiMoveLightSiblings
    {
        get => _mdiMoveLightSiblings;
        set
        {
            if (SetProperty(ref _mdiMoveLightSiblings, value)) { _owner.SaveUnlessLoading(); }
            Docking.MdiMoveLight.Siblings = _mdiMoveLightSiblings;
        }
    }

    private bool _mdiMoveLightSquareCorners = Docking.MdiMoveLight.DefaultSquareCorners;

    /// <summary>끌기 중 모서리 반경 0(기본 끔 — 정본 Docking.MdiMoveLight.DefaultSquareCorners, 근거 01 §4.2 2026-11-11 모서리 유지). 바꾸면 즉시 저장하고 반영한다.</summary>
    public bool MdiMoveLightSquareCorners
    {
        get => _mdiMoveLightSquareCorners;
        set
        {
            if (SetProperty(ref _mdiMoveLightSquareCorners, value)) { _owner.SaveUnlessLoading(); }
            Docking.MdiMoveLight.SquareCorners = _mdiMoveLightSquareCorners;
        }
    }

    private bool _mdiMoveLightHitTestOff = Docking.MdiMoveLight.DefaultHitTestOff;

    /// <summary>끌기 중 끄는 창 내용 히트 테스트 끔(기본 켬 — 정본 Docking.MdiMoveLight.DefaultHitTestOff, 근거 01a K3 2026-11-14). 바꾸면 즉시 저장하고 반영한다.</summary>
    public bool MdiMoveLightHitTestOff
    {
        get => _mdiMoveLightHitTestOff;
        set
        {
            if (SetProperty(ref _mdiMoveLightHitTestOff, value)) { _owner.SaveUnlessLoading(); }
            Docking.MdiMoveLight.HitTestOff = _mdiMoveLightHitTestOff;
        }
    }

    private bool _mdiMoveCoalescePointer = Docking.MdiMoveLight.DefaultCoalescePointer;

    /// <summary>끌기 중 포인터 이동 합치기(기본 끔 — 정본 Docking.MdiMoveLight.DefaultCoalescePointer, 근거 01a K5 2026-11-14). 바꾸면 즉시 저장하고 반영한다.</summary>
    public bool MdiMoveCoalescePointer
    {
        get => _mdiMoveCoalescePointer;
        set
        {
            if (SetProperty(ref _mdiMoveCoalescePointer, value)) { _owner.SaveUnlessLoading(); }
            Docking.MdiMoveLight.CoalescePointer = _mdiMoveCoalescePointer;
        }
    }

    private bool _mdiReleaseFlushFrame = Docking.MdiMoveLight.DefaultReleaseFlushFrame;

    /// <summary>놓기 반영을 다음 프레임에(기본 끔 — 정본 Docking.MdiMoveLight.DefaultReleaseFlushFrame, 근거 01a K6 2026-11-14). 바꾸면 즉시 저장하고 반영한다.</summary>
    public bool MdiReleaseFlushFrame
    {
        get => _mdiReleaseFlushFrame;
        set
        {
            if (SetProperty(ref _mdiReleaseFlushFrame, value)) { _owner.SaveUnlessLoading(); }
            Docking.MdiMoveLight.ReleaseFlushFrame = _mdiReleaseFlushFrame;
        }
    }

    private bool _mdiStaticLightEarly = Docking.MdiMoveLight.DefaultStaticLightEarly;

    /// <summary>끌기 시작 계산 미리 하기(기본 끔 — 정본 Docking.MdiMoveLight.DefaultStaticLightEarly, 근거 01a K7 2026-11-14). 바꾸면 즉시 저장하고 반영한다.</summary>
    public bool MdiStaticLightEarly
    {
        get => _mdiStaticLightEarly;
        set
        {
            if (SetProperty(ref _mdiStaticLightEarly, value)) { _owner.SaveUnlessLoading(); }
            Docking.MdiMoveLight.StaticLightEarly = _mdiStaticLightEarly;
        }
    }

    private Docking.MoveShadowMode? _mdiMoveLightShadowUser;

    /// <summary>
    /// 사용자가 고른 끌기 중 그림자 hide|keep(ui.mdi.move_light.shadow, 근거 01a §2·§3·D-589·D-591). null = 고르지 않음.
    /// 실효값은 UiSettingsApplier 가 DesignThemeResolver.ResolveUiKey(잠금 → 사용자 → 효과 단계 묶음 → 회사 → hide)로 정해 Docking.MdiMoveLight.Shadow 에 넣는다.
    /// </summary>
    public Docking.MoveShadowMode? MdiMoveLightShadowUser
    {
        get => _mdiMoveLightShadowUser;
        set
        {
            var v = value is { } m && !Enum.IsDefined(m) ? null : value;
            if (SetProperty(ref _mdiMoveLightShadowUser, v)) { _owner.ApplyAndSave(ApplyParts.Style); }
        }
    }

    private string _chromeInactiveDim = Docking.ChromeInactiveDim.DefaultMode;

    /// <summary>비활성 창 제목줄 흐림 방식 opacity|brush(기본 opacity — 정본 Docking.ChromeInactiveDim.DefaultMode, 근거 01 §4.2 N1). 모르는 값은 기본값. 바꾸면 즉시 저장하고 반영한다.</summary>
    public string ChromeInactiveDim
    {
        get => _chromeInactiveDim;
        set
        {
            var v = Docking.ChromeInactiveDim.Normalize(value) ?? Docking.ChromeInactiveDim.DefaultMode;
            if (SetProperty(ref _chromeInactiveDim, v)) { _owner.SaveUnlessLoading(); }
            Docking.ChromeInactiveDim.Set(_chromeInactiveDim);
        }
    }

    private bool _renderDirtyRegions = Docking.MdiMoveLight.DefaultRenderDirtyRegions;

    /// <summary>영역 dirty 추적(시작 옵션, 기본 끔). 저장만 하고 다음 시작부터 반영된다 — 이번 실행 적용값은 Docking.MdiMoveLight.RenderDirtyRegions(호스트가 기동 때 기록).</summary>
    public bool RenderDirtyRegions
    {
        get => _renderDirtyRegions;
        set
        {
            if (SetProperty(ref _renderDirtyRegions, value)) { _owner.SaveUnlessLoading(); }
        }
    }

    private void FillTimingDto(UiSettingsDto dto)
    {
        dto.MdiMoveLight = _mdiMoveLight;
        dto.MdiMoveLightQuietMs = _mdiMoveLightQuietMs;
        dto.MdiMoveLightBitmapCache = _mdiMoveLightBitmapCache;
        dto.MdiMoveLightStaticCache = _mdiMoveLightStaticCache;
        dto.MdiMoveLightSiblings = _mdiMoveLightSiblings;
        dto.MdiMoveLightSquareCorners = _mdiMoveLightSquareCorners;
        dto.MdiMoveLightHitTestOff = _mdiMoveLightHitTestOff;
        dto.MdiMoveCoalescePointer = _mdiMoveCoalescePointer;
        dto.MdiReleaseFlushFrame = _mdiReleaseFlushFrame;
        dto.MdiStaticLightEarly = _mdiStaticLightEarly;
        dto.MdiMoveLightShadow = _mdiMoveLightShadowUser is { } shadow ? Docking.MdiMoveLight.ShadowKey(shadow) : null; // 희소 저장(고르지 않았으면 쓰지 않음)
        FillSpreadDto(dto); // 옵션 A·B (UiSystemSettings.Spread.cs)
        dto.ChromeInactiveDim = _chromeInactiveDim;
        dto.RenderDirtyRegions = _renderDirtyRegions;
        dto.BusyShowDelayMs = _busyShowDelayMs;
        dto.BusyMinVisibleMs = _busyMinVisibleMs;
        dto.PrewarmViews = _prewarmViews;
        FillGridDto(dto); // 표 옵션(UiSystemSettings.Grid.cs)
    }

    private void LoadTimingDto(UiSettingsDto dto)
    {
        BusyShowDelayMs = dto.BusyShowDelayMs ?? DefaultBusyShowDelayMs;
        BusyMinVisibleMs = dto.BusyMinVisibleMs ?? DefaultBusyMinVisibleMs;
        PrewarmViews = dto.PrewarmViews ?? true;
        LoadGridDto(dto); // 표 옵션(UiSystemSettings.Grid.cs)
        MdiMoveLight = dto.MdiMoveLight ?? true; // 옛 저장본(필드 없음)은 켬
        MdiMoveLightQuietMs = dto.MdiMoveLightQuietMs ?? DefaultMdiMoveLightQuietMs; // 옛 저장본(필드 없음)은 MdiMoveLight.DefaultQuietMs(300), 범위 밖 값은 setter 가 잘라 맞춘다
        MdiMoveLightBitmapCache = dto.MdiMoveLightBitmapCache ?? Docking.MdiMoveLight.DefaultBitmapCache; // 옛 저장본(필드 없음)은 끔
        MdiMoveLightStaticCache = dto.MdiMoveLightStaticCache ?? Docking.MdiMoveLight.DefaultStaticCache; // 옛 저장본(필드 없음)은 끔
        MdiMoveLightSiblings = dto.MdiMoveLightSiblings ?? Docking.MdiMoveLight.DefaultSiblings; // 옛 저장본(필드 없음)은 기본값(켬)
        MdiMoveLightSquareCorners = dto.MdiMoveLightSquareCorners ?? Docking.MdiMoveLight.DefaultSquareCorners; // 옛 저장본(필드 없음)은 끔 = 끌기 중에도 둥근 모서리
        MdiMoveLightHitTestOff = dto.MdiMoveLightHitTestOff ?? Docking.MdiMoveLight.DefaultHitTestOff; // 옛 저장본(필드 없음)은 켬
        MdiMoveCoalescePointer = dto.MdiMoveCoalescePointer ?? Docking.MdiMoveLight.DefaultCoalescePointer; // 옛 저장본(필드 없음)은 끔
        MdiReleaseFlushFrame = dto.MdiReleaseFlushFrame ?? Docking.MdiMoveLight.DefaultReleaseFlushFrame; // 옛 저장본(필드 없음)은 끔
        MdiStaticLightEarly = dto.MdiStaticLightEarly ?? Docking.MdiMoveLight.DefaultStaticLightEarly; // 옛 저장본(필드 없음)은 끔
        MdiMoveLightShadowUser = dto.MdiMoveLightShadow is null ? null : Docking.MdiMoveLight.ParseShadow(dto.MdiMoveLightShadow); // 옛 저장본(필드 없음)은 고르지 않음
        LoadSpreadDto(dto); // 옵션 A·B (UiSystemSettings.Spread.cs)
        ChromeInactiveDim = dto.ChromeInactiveDim ?? Docking.ChromeInactiveDim.DefaultMode; // 옛 저장본(필드 없음)·모르는 값은 opacity
        RenderDirtyRegions = dto.RenderDirtyRegions ?? Docking.MdiMoveLight.DefaultRenderDirtyRegions; // 저장값만 — 적용은 다음 시작
    }
}
