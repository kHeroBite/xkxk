// UI 설정의 색 테마 연동(UI-07) — 선택 테마 Id·시스템 모드용 라이트/다크 테마 Id 저장과 사용자 테마 편집·OS 라이트/다크 변경 구독
using Avalonia;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Pd.Poc.Shell.Theming;

/// <summary>디자인 테마 움직임 수준(설정 키 theme.motion.level). System 은 OS·브라우저 '움직임 줄이기'를 따른다.</summary>
public enum MotionLevel
{
    System,
    Full,
    Reduced,
    Off,
}

/// <summary>UiSettings 가 소유하는 색 테마 Id 묶음. 속성 변경 알림은 UiSettings 가 같은 이름으로 다시 낸다.</summary>
internal sealed class UiThemeSettings : ObservableObject
{
    private readonly UiSettings _owner;
    private string _themeId = BuiltinThemes.LightId;
    private string _systemLightThemeId = BuiltinThemes.LightId;
    private string _systemDarkThemeId = BuiltinThemes.DarkId;
    private string _designThemeId = DefaultDesignThemeId;
    private MotionLevel _motionLevel = MotionLevel.System;
    private readonly Dictionary<string, string> _effectOverrides = new();

    /// <summary>디자인 테마 기본 Id(설정 키 theme.style.id 기본값, 계획서 §4.2 ds-classic).</summary>
    public const string DefaultDesignThemeId = "ds-classic";

    public UiThemeSettings(UiSettings owner) => _owner = owner;

    /// <summary>선택한 색 테마 Id(ThemeCatalog). 비우면 기본 밝은 테마.</summary>
    public string ThemeId
    {
        get => _themeId;
        set
        {
            if (SetProperty(ref _themeId, string.IsNullOrWhiteSpace(value) ? BuiltinThemes.LightId : value))
            {
                using (UiSettings.Op("ui.theme.select")) { _owner.ApplyAndSave(ApplyParts.Theme | ApplyParts.DocScope); } // PERF-UI 색 테마 전환
            }
        }
    }

    /// <summary>테마 모드가 System 이고 OS 가 라이트일 때 쓸 테마 Id.</summary>
    public string SystemLightThemeId
    {
        get => _systemLightThemeId;
        set
        {
            if (SetProperty(ref _systemLightThemeId, string.IsNullOrWhiteSpace(value) ? BuiltinThemes.LightId : value))
            {
                _owner.ApplyAndSave(ApplyParts.Theme | ApplyParts.DocScope);
            }
        }
    }

    /// <summary>테마 모드가 System 이고 OS 가 다크일 때 쓸 테마 Id.</summary>
    public string SystemDarkThemeId
    {
        get => _systemDarkThemeId;
        set
        {
            if (SetProperty(ref _systemDarkThemeId, string.IsNullOrWhiteSpace(value) ? BuiltinThemes.DarkId : value))
            {
                _owner.ApplyAndSave(ApplyParts.Theme | ApplyParts.DocScope);
            }
        }
    }

    /// <summary>선택한 디자인 테마 Id(설정 키 theme.style.id). 비우면 ds-classic. 색 테마와 직교.</summary>
    public string DesignThemeId
    {
        get => _designThemeId;
        set
        {
            if (SetProperty(ref _designThemeId, string.IsNullOrWhiteSpace(value) ? DefaultDesignThemeId : value))
            {
                _owner.ApplyAndSave(ApplyParts.Style);
            }
        }
    }

    /// <summary>움직임 수준(설정 키 theme.motion.level, 기본 System).</summary>
    public MotionLevel MotionLevel
    {
        get => _motionLevel;
        set
        {
            if (SetProperty(ref _motionLevel, value))
            {
                _owner.ApplyAndSave(ApplyParts.Style);
            }
        }
    }

    /// <summary>사용자 효과 덮어쓰기(효과 키 → 값, 희소). 없는 키는 디자인 테마 값을 쓴다.</summary>
    public IReadOnlyDictionary<string, string> EffectOverrides => _effectOverrides;

    /// <summary>효과 하나를 덮어쓴다. 값이 비면 덮어쓰기를 지운다(디자인 테마 값으로 복귀).</summary>
    public void SetEffectOverride(string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        bool changed;
        if (string.IsNullOrWhiteSpace(value))
        {
            changed = _effectOverrides.Remove(key);
        }
        else
        {
            changed = !_effectOverrides.TryGetValue(key, out var old) || old != value;
            _effectOverrides[key] = value;
        }

        if (changed)
        {
            OnPropertyChanged(nameof(EffectOverrides));
            _owner.ApplyAndSave(ApplyParts.Style);
        }
    }

    /// <summary>덮어쓰기를 전부 지운다(디자인 테마 값으로 복귀).</summary>
    public void ClearEffectOverrides()
    {
        if (_effectOverrides.Count == 0)
        {
            return;
        }

        _effectOverrides.Clear();
        OnPropertyChanged(nameof(EffectOverrides));
        _owner.ApplyAndSave(ApplyParts.Style);
    }

    /// <summary>Install 때 1회(창 크롬 옵션 설치 뒤): 사용자 테마 편집 시 저장, OS 라이트/다크 변경 시(System 모드) 재적용.</summary>
    public void InstallWatchers(Application app)
    {
        ThemeCatalog.Current.Changed += () =>
        {
            if (!_owner.IsLoading)
            {
                _ = _owner.SaveAsync();
            }
        };
        ThemeApplier.WatchOs(app, () =>
        {
            if (_owner.Theme == ThemeMode.System)
            {
                _owner.Apply();
            }
        });
    }

    public void FillDto(UiSettingsDto dto)
    {
        dto.ThemeId = _themeId;
        dto.SystemLightThemeId = _systemLightThemeId;
        dto.SystemDarkThemeId = _systemDarkThemeId;
        dto.UserThemesJson = ThemeCatalog.Current.ExportUserThemesJson();
        dto.DesignThemeId = _designThemeId == DefaultDesignThemeId ? null : _designThemeId; // 기본값이면 쓰지 않음(희소)
        dto.MotionLevel = _motionLevel == MotionLevel.System ? null : _motionLevel;
        dto.EffectOverrides = _effectOverrides.Count == 0 ? null : new Dictionary<string, string>(_effectOverrides);
        // 저장 값만 쓴다 — 진단 인자(DepthDiag.Enable)로 이번 실행만 켠 값과 진단 키 diag.depth.* 는 저장하지 않는다
        dto.DepthDeferBegin = DepthDiag.SavedDeferBegin != DepthDiag.DefaultOff ? DepthDiag.SavedDeferBegin : null; // 기본값과 다를 때만(희소)
        dto.DepthWebPrewarm = DepthDiag.SavedWebPrewarm != DepthDiag.DefaultOff ? DepthDiag.SavedWebPrewarm : null; // 기본값과 다를 때만(희소)
    }

    /// <summary>복원 시작 직후, 다른 값보다 먼저 호출(사용자 테마가 있어야 Id 해석 가능). 옛 저장본(null)은 기본값.</summary>
    public void LoadDto(UiSettingsDto dto)
    {
        ThemeCatalog.Current.LoadUserThemesJson(dto.UserThemesJson);
        ThemeId = dto.ThemeId ?? BuiltinThemes.LightId;
        SystemLightThemeId = dto.SystemLightThemeId ?? BuiltinThemes.LightId;
        SystemDarkThemeId = dto.SystemDarkThemeId ?? BuiltinThemes.DarkId;

        // 디자인 테마(옛 프로필에는 필드 없음 → ds-classic·System·빈 덮어쓰기)
        DesignThemeId = dto.DesignThemeId ?? DefaultDesignThemeId;
        MotionLevel = dto.MotionLevel ?? MotionLevel.System;
        _effectOverrides.Clear();
        if (dto.EffectOverrides is not null)
        {
            foreach (var (k, v) in dto.EffectOverrides)
            {
                if (!string.IsNullOrWhiteSpace(k) && !string.IsNullOrWhiteSpace(v))
                {
                    _effectOverrides[k] = v;
                }
            }
        }

        OnPropertyChanged(nameof(EffectOverrides));
        // 정식 후보 키만 저장 값으로 복원한다(없으면 기본 끔). 진단 인자로 켠 실행 전용 값은 따로 겹쳐지므로 건드리지 않는다
        DepthDiag.SavedDeferBegin = dto.DepthDeferBegin ?? DepthDiag.DefaultOff;
        DepthDiag.SavedWebPrewarm = dto.DepthWebPrewarm ?? DepthDiag.DefaultOff;
    }
}

/// <summary>
/// K13 열기 비용 진단·수정 후보 키(기본 끔 = 현재 동작 — REQ-99 설정 키 + 기본값). 끄면 측정·화면 경로에 영향이 없다.
/// SkipElementDesign(diag.depth.skipElementDesign) = 디자인 테마 층 요소 디자인(예 ds-depth *.cornerRadius) 생략 — 진단 전용, 저장하지 않음,
/// DeferBegin(정식 설정 키 depth.deferBegin — 후보 2, H4 진단 겸용) = 문서 창 열기 기울기 시작을 다음 프레임으로 미룸, WebPrewarm(depth.webPrewarm, 후보 1) = 웹 기울기 예열.
/// 값은 두 층이다: 저장 값(Saved*, 프로필 저장·복원)과 실행 전용 겹침(Enable 로 켠 값, 저장하지 않음). 실효 값 = 저장 값 || 실행 전용 값.
/// </summary>
public static class DepthDiag
{
    /// <summary>진단 키 기본값(끔).</summary>
    public const bool DefaultOff = false;

    private static bool s_runSkipElementDesign = DefaultOff;
    private static bool s_runDeferBegin = DefaultOff;
    private static bool s_runWebPrewarm = DefaultOff;

    /// <summary>진단 키 diag.depth.skipElementDesign 실효 값 — 실행 전용 겹침만(저장 층 없음).</summary>
    public static bool SkipElementDesign => s_runSkipElementDesign;

    /// <summary>설정 키 depth.deferBegin 의 저장 값(사용자 설정, 프로필에 저장·복원).</summary>
    public static bool SavedDeferBegin { get; set; } = DefaultOff;

    /// <summary>설정 키 depth.webPrewarm 의 저장 값(사용자 설정, 프로필에 저장·복원).</summary>
    public static bool SavedWebPrewarm { get; set; } = DefaultOff;

    /// <summary>depth.deferBegin 실효 값 = 저장 값 || 실행 전용 값.</summary>
    public static bool DeferBegin => SavedDeferBegin || s_runDeferBegin;

    /// <summary>설정 키 depth.webPrewarm(수정 후보 1) 실효 값: 웹(브라우저 호스트)에서도 기울기 첫 렌더 예열을 한다. 기본값 = 현재 동작(끔).</summary>
    public static bool WebPrewarm => SavedWebPrewarm || s_runWebPrewarm;

    /// <summary>진단 시작 인자 값(쉼표 목록 skipElementDesign·deferBegin·webPrewarm)으로 이번 실행만 켠다(실행 전용 겹침, 저장되지 않음). 모르는 이름은 무시한다.</summary>
    public static void Enable(string names)
    {
        foreach (var n in names.Split(',', System.StringSplitOptions.TrimEntries | System.StringSplitOptions.RemoveEmptyEntries))
        {
            if (string.Equals(n, nameof(SkipElementDesign), System.StringComparison.OrdinalIgnoreCase))
            {
                s_runSkipElementDesign = true;
            }
            else if (string.Equals(n, nameof(DeferBegin), System.StringComparison.OrdinalIgnoreCase))
            {
                s_runDeferBegin = true;
            }
            else if (string.Equals(n, nameof(WebPrewarm), System.StringComparison.OrdinalIgnoreCase))
            {
                s_runWebPrewarm = true;
            }
        }
    }
}
