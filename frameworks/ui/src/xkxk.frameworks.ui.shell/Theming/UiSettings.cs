// UI 설정 서비스 — 라이트/다크 테마·기준 글자 크기(+오프셋 토큰)·폰트를 앱 리소스에 즉시 반영하고 호스트 저장소에 저장/복원
using xkxk.frameworks.ui.data.Layout;
using xkxk.frameworks.ui.data.Logging;
using xkxk.frameworks.ui.data.Rendering;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using xkxk.frameworks.ui.shell.Diagnostics;
using xkxk.frameworks.ui.shell.Docking;
using xkxk.frameworks.ui.shell.Rendering;

namespace xkxk.frameworks.ui.shell.Theming;

/// <summary>
/// 단일 인스턴스 설정. 값이 바뀌면 Application.RequestedThemeVariant 와 리소스(토큰)를 즉시 갱신한다.
/// 화면은 DynamicResource 로 토큰을 참조하므로 메인·SDI 실 창·관리형 창 모두 실시간 반영된다.
/// 책임별 구성: 저장·복원(UiSettingsStore)·적용(UiSettingsApplier)·색 테마 Id(UiThemeSettings)·
/// 창 크롬/윈도우 메뉴(UiWindowSettings)·언어(UiLanguageSettings)·시스템 메뉴(UiSystemSettings). 공개 속성은 이 클래스가 그대로 노출한다(구성 클래스 위임 속성은 UiSettings.Parts.cs).
/// 전역 입력 처리(Ctrl+휠·Ctrl+0·핀치)와 시작 순서는 컨트롤 계층 Input/SettingsInput 이 맡는다(D-LAYER).
/// </summary>
public sealed partial class UiSettings : ObservableObject
{
    public const double MinFontSize = 10;
    public const double MaxFontSize = 24;
    public const double DefaultFontSize = 14;
    public const string DefaultFontName = "Noto Sans KR";

    /// <summary>글자 크기 토큰 = 기준값 + 오프셋. 오프셋 정의는 이 표 한 곳뿐이다.</summary>
    public static readonly IReadOnlyList<(string Key, double Offset)> FontTokens = new[]
    {
        ("PdFontTitle", 4.0),
        ("PdFontSubtitle", 2.0),
        ("PdFontBody", 0.0),
        ("PdFontDetail", -1.0),
        ("PdFontCaption", -2.0),
    };

    private readonly UiSettingsStore _persist;
    private readonly UiSettingsApplier _applier;
    private readonly UiThemeSettings _themes;
    private readonly UiWindowSettings _window;
    private readonly UiLanguageSettings _language;
    private readonly UiSystemSettings _system;
    private CtrlWheelMode _wheelMode;
    private ThemeMode _theme;
    private double _baseFontSize = DefaultFontSize;
    private double _documentDefault = DefaultFontSize;
    private string _fontName = DefaultFontName;
    private Application? _app;

    public UiSettings()
    {
        _persist = new UiSettingsStore(this);
        _applier = new UiSettingsApplier(this);
        _themes = new UiThemeSettings(this);
        _window = new UiWindowSettings(this);
        _language = new UiLanguageSettings(this);
        _system = new UiSystemSettings(this);
        foreach (var part in new ObservableObject[] { _themes, _window, _language, _system })
        {
            // 묶음의 변경 알림을 같은 속성 이름으로 이 객체에서 다시 낸다(바인딩·구독자는 UiSettings 만 본다)
            part.PropertyChanging += RelayChanging;
            part.PropertyChanged += RelayChanged;
        }
    }

    public static UiSettings Current { get; } = new();

    /// <summary>폰트 이름 → FontFamily. 브라우저 호스트는 임베드 폰트 URI 로 바꿔 끼운다.</summary>
    public static Func<string, FontFamily> FontResolver { get; set; } = name => new FontFamily(name);

    /// <summary>값 반영 직후 알림(코드로 그리는 컨트롤의 InvalidateVisual 용).</summary>
    public event Action? Changed;

    public ThemeMode Theme
    {
        get => _theme;
        set
        {
            if (SetProperty(ref _theme, value))
            {
                using (Op("ui.theme.mode")) { ApplyAndSave(ApplyParts.Theme | ApplyParts.DocScope); }
            }
        }
    }

    public double BaseFontSize
    {
        get => _baseFontSize;
        set
        {
            if (SetProperty(ref _baseFontSize, Math.Clamp(Math.Round(value), MinFontSize, MaxFontSize)))
            {
                using (Op("ui.font.size")) { ApplyAndSave(ApplyParts.DocScope); } // Ctrl+휠·Ctrl+0·설정·자동화 공통 — 테마 재적용 없음
            }
        }
    }

    public string FontName
    {
        get => _fontName;
        set
        {
            if (SetProperty(ref _fontName, string.IsNullOrWhiteSpace(value) ? DefaultFontName : value))
            {
                using (Op("ui.font.family")) { ApplyAndSave(ApplyParts.DocScope); } // 문서 글꼴 — 문서 래퍼가 로컬로 덮는다(앱 전역은 ShellFontName, UiSettings.Shell.cs, 2026-11-11)
            }
        }
    }

    /// <summary>선택 가능한 폰트: "Noto Sans KR" + 시스템 폰트(브라우저는 시스템 폰트 없음).</summary>
    public IReadOnlyList<string> FontNames =>
        new[] { DefaultFontName }
            .Concat(FontManager.Current.SystemFonts.Select(f => f.Name).OrderBy(n => n, StringComparer.CurrentCulture))
            .Distinct()
            .ToList();

    /// <summary>Ctrl+휠·Ctrl+0 동작: 전체 기준 글자 크기(UI-05) 또는 현재 창 확대·축소(UI-06).</summary>
    public CtrlWheelMode WheelMode
    {
        get => _wheelMode;
        set
        {
            if (SetProperty(ref _wheelMode, value) && !IsLoading)
            {
                _ = SaveAsync();
            }
        }
    }

    /// <summary>문서 기본 글자 크기(설정 패널 입력값, Ctrl+0 이 돌아갈 값). 바꾸면 현재 크기도 그 값으로 맞춘다.</summary>
    public double DocumentDefaultFontSize
    {
        get => _documentDefault;
        set
        {
            if (SetProperty(ref _documentDefault, Math.Clamp(Math.Round(value), MinFontSize, MaxFontSize)))
            {
                BaseFontSize = _documentDefault;
                if (!IsLoading) { _ = SaveAsync(); }
            }
        }
    }

    public void Increase() => BaseFontSize += 1;
    public void Decrease() => BaseFontSize -= 1;
    public void ResetFontSize() => BaseFontSize = _documentDefault;

    public void ToggleTheme() => Theme = Theme == ThemeMode.Dark || (Theme != ThemeMode.Light && EffectiveIsDark()) ? ThemeMode.Light : ThemeMode.Dark; // 적용은 프레임 뒤로 미뤄지므로 적용된 색이 아니라 고른 값을 기준으로 뒤집는다(빠른 연속 전환)

    // 앱 시작 시 1회 — 전체 순서(입력 처리기 등록 포함)는 컨트롤 계층 Input/SettingsInput.Install 이 정한다(D-LAYER).

    /// <summary>시작 1단계: 저장소 → 창 크롬 기본값·크롬 스타일 구독·SDI 자석(UiWindowSettings) → 표시 언어(UiLanguageSettings).</summary>
    public void InstallBegin(Application app, ILayoutStore? store)
    {
        _app = app;
        _persist.SetStore(store);
        if (_app is not null)
        {
            _window.Install();
            _language.Install(_app);
        }
    }

    /// <summary>시작 2단계(핀치 등록 뒤): 색 테마 감시 → 기본값 전체 반영.</summary>
    public void InstallThemesAndApply()
    {
        if (_app is not null)
        {
            _themes.InstallWatchers(_app);
        }

        Apply();
    }

    /// <summary>시작 3단계(휠·키 처리기 등록 뒤): 저장값 비동기 복원. 끝나면 Loaded 완료.</summary>
    public void StartLoad() => _ = _persist.LoadAndSignalAsync();

    private static bool EffectiveIsDark() => ThemeCatalog.Current.Requested.Base == ThemeBase.Dark;

    /// <summary>종료 시 플러시(App Closing) — UiSettingsStore.FlushAsync.</summary>
    public Task FlushAsync(TimeSpan max) => _persist.FlushAsync(max);

    /// <summary>저장값 복원이 끝나면 완료(저장소가 없거나 손상돼도 완료). 기동 자동 복원은 이 값을 본 뒤 판단한다.</summary>
    public Task Loaded => _persist.Loaded;

    /// <summary>⑫ 전체 백업·복원의 설정 입구(백업 때 현재 설정, 복원 때 쓰기·재적용) — UiSettingsStore.</summary>
    internal ISettingsBackupSource BackupSource => _persist;

    /// <summary>이번 실행에만 적용할 설정 이름(시험 인자) — UiSettingsStore.SetTransient.</summary>
    public void SetTransient(IReadOnlySet<string> names) => _persist.SetTransient(names);

    /// <summary>전체 적용 — 강제(외부 호출은 테마 Id 가 같아도 색이 바뀌었을 수 있어 항상 ThemeApplier 를 부른다. setter 경로 ApplyAndSave 는 바뀐 경우에만).</summary>
    public void Apply() => Apply(ApplyParts.All, force: true);

    /// <summary>지정한 부분만 적용한 뒤 Changed 를 1회 발생시킨다(UiSettingsApplier).</summary>
    public void Apply(ApplyParts parts, bool force = false) => _applier.Apply(parts, force);

    /// <summary>문서 래퍼 범위에만 글자 크기를 적용(UiSettingsApplier).</summary>
    public void ApplyDocumentScope(StyledElement host) => _applier.ApplyDocumentScope(host);

    // 구성 클래스용 내부 입구
    internal Application? AppInstance => _app;
    internal bool IsLoading => _persist.Loading;
    internal void RaiseChanged()
    {
#if PD_DIAGNOSTICS // 진단판: 구독자별 시간(ui.changed.<형식>.<메서드>) — 전환 settle 분해(K13). 제품판은 한 번에 부른다
        foreach (var d in Changed?.GetInvocationList() ?? Array.Empty<Delegate>())
        {
            using (Op("ui.changed." + d.Method.DeclaringType?.Name + "." + d.Method.Name)) { ((Action)d)(); }
        }
#else
        Changed?.Invoke();
#endif
    }
    internal Task SaveAsync() => _persist.SaveAsync();
    internal void SaveUnlessLoading() => _persist.SaveUnlessLoading();

    /// <summary>PERF-UI 계측 범위(설정 변경 → 테마·토큰·문서 범위 반영 → 다음 프레임). 각 setter 가 사용.</summary>
    internal static IDisposable Op(string name) => PerfProbe.Current.TimeOperation(name);

    internal void ApplyAndSave(ApplyParts parts)
    {
        Apply(parts);
        if (!IsLoading) { using (Op("ui.save.start")) { _ = SaveAsync(); } } // 저장 시작(동기 부분) 계측 — 제품판은 빈 범위
    }

    /// <summary>저장 스냅샷. 채우는 순서는 분할 전과 같다(기본 → 테마 → 창 크롬·윈도우 → 언어 → 시스템).</summary>
    internal UiSettingsDto CreateDto()
    {
        var dto = new UiSettingsDto
        {
            Theme = _theme, WheelMode = _wheelMode, BaseFontSize = _baseFontSize,
            DocumentDefaultFontSize = _documentDefault, FontName = _fontName,
            ShellFontSize = _shellFontSize, ShellFontName = _shellFontName, ShellTabStripHeight = _shellTabStripHeight,
        };
        _themes.FillDto(dto);
        _window.FillDto(dto);
        _language.FillDto(dto);
        _system.FillDto(dto);
        _system.FillRestoreDto(dto);
        FillSnapDto(dto);
        FillShellDto(dto); // 셸 옵션 중 이름 문자열 저장분(TitleTabCloseMode — UiSettings.Shell)
        return dto;
    }

    /// <summary>복원(UiSettingsStore.LoadAsync 가 Loading=true 로 호출). 사용자 테마를 먼저 불러와야 테마 Id 를 해석할 수 있다.</summary>
    internal void LoadFromDto(UiSettingsDto dto)
    {
        _themes.LoadDto(dto);
        _window.LoadDto(dto);
        _language.LoadDto(dto);
        _system.LoadDto(dto);
        _system.LoadRestoreDto(dto);
        LoadSnapDto(dto);
        Theme = dto.Theme;
        WheelMode = dto.WheelMode;
        DocumentDefaultFontSize = dto.DocumentDefaultFontSize; // 현재 크기도 이 값으로 맞춘 뒤
        BaseFontSize = dto.BaseFontSize;                       // 저장된 현재 크기로 덮는다
        FontName = dto.FontName;
        ShellFontName = dto.ShellFontName ?? dto.FontName; // 옛 저장본(키 없음)은 문서 글꼴과 같게 — 2026-11-11 이전 화면과 같다
        ShellFontSize = dto.ShellFontSize ?? DefaultFontSize;
        ShellTabStripHeight = dto.ShellTabStripHeight ?? DefaultShellTabStripHeight;
        LoadShellDto(dto); // 옛 저장본(키 없음)은 Auto
        Apply(); // 모드 값이 같아 setter 가 건너뛰어도 복원한 테마 Id 를 반영
    }

    private void RelayChanging(object? sender, PropertyChangingEventArgs e) => OnPropertyChanging(e);
    private void RelayChanged(object? sender, PropertyChangedEventArgs e) => OnPropertyChanged(e);
}
