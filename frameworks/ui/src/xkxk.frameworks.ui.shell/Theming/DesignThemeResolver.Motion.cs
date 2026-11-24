// 디자인 테마 해석기 분할(REQ-112·116) — 층 합성(사용자·효과 단계·회사·디자인 테마·기본값), 옛 별칭 변환, 움직임 효과 낮춤·배치 규칙, 효과 단계 표시
using System;
using System.Collections.Generic;
using System.Linq;

namespace xkxk.frameworks.ui.shell.Theming;

/// <summary>
/// 해석기의 움직임·효과 단계 부분(21 문서 §3~§5·§7). 합성 순서(D-591): 사용자 낱개 덮어쓰기 ?? 효과 단계 묶음 ?? 회사 ?? 디자인 테마 ?? 표 기본값,
/// 회사 잠금이면 사용자·단계를 건너뛴다. 단계는 덮어쓰기 저장소에 아무것도 쓰지 않는 해석 층이다.
/// 옛 별칭 depth.open 은 motion.open 의 읽기 전용 이름이다(저장된 tilt 는 tilt3d 로 읽고, 실효값은 tilt3d 면 tilt).
/// </summary>
public static partial class DesignThemeResolver
{
    private const string MotionOpen = "motion.open";
    private const string MotionClose = "motion.close";
    private const string MotionSwitch = "motion.switch";

    /// <summary>옛 별칭 값 대응(별칭 짧은 키, 별칭 값, 정본 값). 대응이 없는 별칭 값은 '미지정'이다(21 §3.1 대응표).</summary>
    private static readonly (string Alias, string AliasValue, string CanonicalValue)[] AliasValues =
    {
        ("depth.open", "tilt", "tilt3d"),
    };

    private static string _effectLevel = "";

    /// <summary>지금 플랫폼(잘라냄 효과 플랫폼 낮춤 판정). 기본은 실행 중인 OS 판정이고 시험만 바꾼다.</summary>
    public static MotionPlatform Platform { get; set; } =
        System.OperatingSystem.IsBrowser() ? MotionPlatform.Web : System.OperatingSystem.IsAndroid() ? MotionPlatform.Android : MotionPlatform.Desktop;

    /// <summary>효과 단계 실효값(theme|high|medium|low). 잠금이면 회사 값 ?? theme.</summary>
    public static string EffectLevel => Stored(DesignThemes.EffectLevelKey);

    /// <summary>사용자가 고른 효과 단계 공급(UiSettings 적용 경로). 덮어쓰기 저장소와 따로 둔다. 바뀌었으면 Changed.</summary>
    public static void SetEffectLevel(string? level)
    {
        _effectLevel = level?.Trim() ?? "";
        Refresh();
    }

    /// <summary>지금 효과 단계 묶음이 이 키에 주는 값(잠금·theme·묶음 밖이면 null). 키는 효과 짧은 키 또는 ui 전체 키.</summary>
    public static string? LevelValue(string key) => Locked ? null : DesignThemes.LevelValue(EffectLevel, key);

    /// <summary>
    /// ui 키의 같은 층 합성(21 §5.1 — 묶음이 ui.mdi.move_light.shadow 처럼 UI 설정 저장소 키에도 걸칠 때).
    /// 잠금이면 회사 값 ?? 기본값, 아니면 사용자 값 ?? 단계 묶음 ?? 회사 값 ?? 기본값. 값 검증은 호출자(키 소유 설정)가 한다.
    /// </summary>
    public static string ResolveUiKey(string key, string? userValue, string defaultValue)
    {
        if (Locked)
        {
            return Company(key) ?? defaultValue;
        }

        return !string.IsNullOrWhiteSpace(userValue) ? userValue.Trim() : DesignThemes.LevelValue(EffectLevel, key) ?? Company(key) ?? defaultValue;
    }

    /// <summary>
    /// 효과 단계 표시 상태(21 §5.3). Level = theme|high|medium|low, Overrides = 낱개 덮어쓰기 수(효과 키 + 호출자가 센 ui 묶음 키 덮어쓰기 수).
    /// 잠금이면 낱개 덮어쓰기를 쓰지 않으므로 0 이다.
    /// </summary>
    public static (string Level, int Overrides) LevelDisplay(int uiOverrides = 0) =>
        (EffectLevel, Locked ? 0 : uiOverrides + DesignThemes.Effects.Count(s => DesignThemes.TryNormalize(s.Key, Override(s) ?? AliasRaw(s, Override), out _)));

    /// <summary>열기 효과 최종 ID(폴백·배치·플랫폼 낮춤 반영, 21 §4).</summary>
    public static string OpenEffect(MotionPlacement placement) => Place(placement, MotionDirection.Enter, Effective(MotionOpen), false);

    /// <summary>닫기 효과 최종 ID.</summary>
    public static string CloseEffect(MotionPlacement placement) => Place(placement, MotionDirection.Exit, Effective(MotionClose), false);

    /// <summary>
    /// 전환으로 들어오는 창의 효과 최종 ID. origin = 활성화 출처 이름(keyboard·title_tab·window_list·body_click·program),
    /// sources = ui.motion.switch_sources 실효 목록. 목록에 없는 출처(program 포함)는 none 이다. same_as_open 은 열기 효과를 쓴다.
    /// </summary>
    public static string SwitchEffect(MotionPlacement placement, string origin, IEnumerable<string> sources)
    {
        if (!sources.Contains(origin, StringComparer.OrdinalIgnoreCase))
        {
            return MotionEffects.None;
        }

        var value = Effective(MotionSwitch);
        return Place(placement, MotionDirection.Enter, value == MotionEffects.SameAsOpen ? Effective(MotionOpen) : value, true);
    }

    /// <summary>전환으로 나가는 창의 효과(21 §4): 최대화·DOCK 탭은 짝 나감, NAV 는 밀어내기만 짝, 겹침·SDI 는 none.</summary>
    public static string SwitchExitEffect(MotionPlacement placement, string enterId)
    {
        var partner = placement switch
        {
            MotionPlacement.MdiMaximized or MotionPlacement.DockTab => MotionEffects.PartnerExit(enterId),
            MotionPlacement.Nav when MotionEffects.Find(MotionDirection.Enter, enterId) is { Moves: true, NavAllowed: true } => MotionEffects.PartnerExit(enterId),
            _ => MotionEffects.None,
        };
        return MotionEffects.LowerPlatform(MotionDirection.Exit, partner, Platform);
    }

    private static string Place(MotionPlacement placement, MotionDirection direction, string value, bool isSwitch)
    {
        var placed = placement switch
        {
            MotionPlacement.Sdi => MotionEffects.None,
            MotionPlacement.Nav when direction == MotionDirection.Exit => value == MotionEffects.None ? value : MotionEffects.Fade,
            MotionPlacement.Nav => MotionEffects.LowerNav(direction, value),
            MotionPlacement.MdiOverlap when isSwitch => MotionEffects.LowerOverlapSwitch(value),
            _ => value,
        };
        return MotionEffects.LowerPlatform(direction, placed, Platform);
    }

    private static bool IsMoveKey(string key) => key is MotionOpen or MotionClose or MotionSwitch;

    /// <summary>열기·닫기·전환 효과 폴백(21 §7): 소프트웨어·움직임 끔 → none, 움직임 줄이기 → 무거운 효과는 fade, 창 수 초과 → 전환만 none.</summary>
    private static string LowerMove(string key, string value, List<string> reasons)
    {
        if (reasons.Contains("motionOff") || reasons.Contains("software"))
        {
            return MotionEffects.None;
        }

        var direction = key == MotionClose ? MotionDirection.Exit : MotionDirection.Enter;
        var lowered = reasons.Contains("reducedMotion") && value != MotionEffects.SameAsOpen ? MotionEffects.LowerReduced(direction, value) : value;
        return key == MotionSwitch && reasons.Contains("maxWindows") ? MotionEffects.None : lowered;
    }

    private static string Resolve(DesignEffectSpec? spec, out string source)
    {
        source = "default";
        if (spec is null)
        {
            return "";
        }

        // Locked 는 회사 원본 값을 직접 읽어 Resolve 를 다시 부르지 않는다(순환 없음). 효과·단계 키에서만 잠금을 적용한다
        var userRaw = spec.Key == DesignThemes.MotionLevelKey ? _motionLevel
            : spec.Key == DesignThemes.EffectLevelKey ? (!Locked ? _effectLevel : null)
            : spec.IsEffect && !Locked ? Override(spec) ?? AliasRaw(spec, Override) : null;
        if (DesignThemes.TryNormalize(spec.Key, userRaw, out var user))
        {
            source = "user";
            return user;
        }

        if (spec.IsEffect && !Locked && DesignThemes.TryNormalize(spec.Key, DesignThemes.LevelValue(EffectLevel, spec.Key), out var level))
        {
            source = "level";
            return level;
        }

        if (DesignThemes.TryNormalize(spec.Key, Company(spec.SettingKey) ?? AliasRaw(spec, a => Company(a.SettingKey)), out var company))
        {
            source = "company";
            return company;
        }

        var preset = DesignThemes.FindOrDefault(Current);
        if (spec.IsEffect && DesignThemes.TryNormalize(spec.Key, preset.Effect(spec.Key) ?? AliasRaw(spec, a => preset.Effect(a.Key)), out var style))
        {
            source = "style";
            return style;
        }

        return spec.Default;
    }

    /// <summary>정본 키 spec 의 옛 별칭에 저장된 값을 정본 값으로 읽는다(대응 없는 값·저장 없음은 null = 미지정).</summary>
    private static string? AliasRaw(DesignEffectSpec spec, Func<DesignEffectSpec, string?> read)
    {
        foreach (var alias in DesignThemes.Aliases.Where(a => a.AliasOf == spec.Key))
        {
            var raw = read(alias)?.Trim();
            var hit = AliasValues.FirstOrDefault(m => m.Alias == alias.Key && string.Equals(m.AliasValue, raw, StringComparison.OrdinalIgnoreCase));
            if (hit.Alias is not null)
            {
                return hit.CanonicalValue;
            }
        }

        return null;
    }

    /// <summary>정본 값 → 옛 별칭 값(대응이 없으면 별칭 표 기본값).</summary>
    private static string AliasOf(DesignEffectSpec alias, string canonicalValue)
    {
        var hit = AliasValues.FirstOrDefault(m => m.Alias == alias.Key && m.CanonicalValue == canonicalValue);
        return hit.Alias is not null ? hit.AliasValue : alias.Default;
    }
}
