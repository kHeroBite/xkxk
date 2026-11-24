// 디자인 테마 해석기(REQ-104) — 선택 ID·효과 실효값 합성(사용자 ?? 회사 ?? 디자인 테마 ?? 표 기본값)과 폴백(소프트웨어 렌더·움직임 줄이기·창 수 초과), 요소 속성 층 값, 전체 보기 호스트
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Pd.Client.Data.Rendering;

namespace Pd.Poc.Shell.Theming;

/// <summary>
/// 디자인 테마 해석기(18 문서 §설정 계층·§우선순위·§폴백). 저장값은 UiSettings 쪽이 Update 로 넣고, 회사 값은 ThemeElements.Policy.Values 의
/// theme.style.* 키에서 읽는다. 효과 실효값 = 사용자 덮어쓰기 ?? 효과 단계 묶음 ?? 회사 ?? 디자인 테마 값 ?? 효과 표 기본값(21 §5.1, 합성은 .Motion.cs Resolve),
/// 그 뒤 폴백이 depth.*·motion·열기·닫기·전환 효과를 낮춘다(저장값은 보존).
/// 회사 잠금(theme.style.locked=true)이면 사용자 선택·효과 단계·덮어쓰기를 건너뛰고 회사 값만 쓴다.
/// </summary>
public static partial class DesignThemeResolver
{
    private static readonly IReadOnlyDictionary<string, string> NoOverrides = new Dictionary<string, string>(StringComparer.Ordinal);

    private static string _selected = "";
    private static string _motionLevel = "";
    private static IReadOnlyDictionary<string, string> _overrides = NoOverrides;
    private static bool _simulateSoftware;
    private static string _signature = "";

    /// <summary>선택·효과 실효값·폴백 사유 중 하나라도 바뀌면 발생(Update·Refresh 가 서명 비교로 판정).</summary>
    public static event Action? Changed;

    /// <summary>열린 문서 창 수 공급자(화면 층이 등록). 없으면 창 수 폴백을 판정하지 않는다. 창 수가 바뀌면 화면 층이 Refresh 를 부른다.</summary>
    public static Func<int>? WindowCount { get; set; }

    /// <summary>
    /// 지금 화면이 NAV(폰형·모바일형)인가 공급자(화면 층이 등록). 18 문서 §6 폼팩터 규칙상 NAV 의 전체 보기는 평면 목록이므로
    /// depth.overview 실효값을 flat 으로 낸다(폴백 사유는 아님 — 저장값 보존). 바뀌면 화면 층이 Refresh 를 부른다.
    /// </summary>
    public static Func<bool>? NavLayout { get; set; }

    /// <summary>진단 전용 소프트웨어 렌더 흉내(원격 gpu.simulateSoftware). 바꾸면 곧바로 재계산한다.</summary>
    public static bool SimulateSoftware
    {
        get => _simulateSoftware;
        set
        {
            _simulateSoftware = value;
            Refresh();
        }
    }

    /// <summary>사용자가 저장한 디자인 테마 ID(비면 선택 안 함). 실효 ID 는 Current.</summary>
    public static string Selected => _selected;

    /// <summary>
    /// 회사 잠금 여부(theme.style.locked). 회사만 정하는 값이라 해석 층(Resolve)을 거치지 않고 회사 원본 값 ?? 표 기본값을 직접 읽는다.
    /// Resolve 가 효과·단계 키에서 Locked 를 읽으므로, 여기서 Resolve 를 다시 부르면 순환한다.
    /// </summary>
    public static bool Locked =>
        DesignThemes.Spec("locked") is { } spec
        && (DesignThemes.TryNormalize(spec.Key, Company(spec.SettingKey), out var v) ? v : spec.Default) == "true";

    /// <summary>실효 디자인 테마 ID. 잠금이 아니면 사용자 값 ?? 회사 값 ?? ds-classic, 잠금이면 회사 값 ?? ds-classic. 모르는 ID 는 건너뛴다.</summary>
    public static string Current =>
        (!Locked ? DesignThemes.Find(_selected) : null)?.Id ?? DesignThemes.Find(Company(DesignThemes.SettingPrefix + "id"))?.Id ?? DesignThemes.DefaultId;

    /// <summary>움직임 수준 저장 실효값(system|full|reduced|off). 접근성 축이라 디자인 테마 잠금과 무관하다.</summary>
    public static string MotionLevel => Stored(DesignThemes.MotionLevelKey);

    /// <summary>system 을 풀어 쓴 움직임 수준(full|reduced|off). system 은 MotionPreference.SystemReduced 를 따른다.</summary>
    public static string EffectiveMotionLevel => MotionLevel switch
    {
        "system" => MotionPreference.SystemReduced ? "reduced" : "full",
        var v => v,
    };

    /// <summary>지금 걸린 폴백 사유(software·motionOff·reducedMotion·maxWindows 를 쉼표로 결합). 없으면 null.</summary>
    public static string? FallbackReason
    {
        get
        {
            var reasons = Reasons();
            return reasons.Count == 0 ? null : string.Join(",", reasons);
        }
    }

    /// <summary>추천 색 테마 ID(1클릭 제안만, 강제 없음). theme.style.suggestColor=false 이거나 추천이 없으면 null.</summary>
    public static string? SuggestedColorThemeId => Flag("suggestColor") ? DesignThemes.FindOrDefault(Current).SuggestedColorThemeId : null;

    /// <summary>저장값 공급(UiSettings 적용 경로). id·움직임 수준·효과 덮어쓰기(키 = 짧은 효과 키 또는 전체 설정 키). 바뀌었으면 Changed.</summary>
    public static void Update(string? id, string? motionLevel, IReadOnlyDictionary<string, string>? overrides)
    {
        _selected = id?.Trim() ?? "";
        _motionLevel = motionLevel?.Trim() ?? "";
        _overrides = overrides is null ? NoOverrides : new Dictionary<string, string>(overrides, StringComparer.Ordinal);
        Refresh();
    }

    /// <summary>실효 상태를 다시 계산해 바뀌었으면 Changed(창 수·시스템 움직임 선호·회사 값 변경 뒤 호출).</summary>
    public static void Refresh()
    {
        var sig = string.Join("|", DesignThemes.Effects.Select(s => Effective(s.Key)))
            + "|" + Current + "|" + EffectiveMotionLevel + "|" + Locked + "|" + FallbackReason + "|" + EffectLevel;
        if (sig == _signature)
        {
            return;
        }

        _signature = sig;
#if PD_DIAGNOSTICS // 진단판: 구독자별 시간(ui.style.changed.<형식>.<메서드>, K13 분해)
        foreach (var d in Changed?.GetInvocationList() ?? Array.Empty<Delegate>())
        {
            using (UiSettings.Op("ui.style.changed." + d.Method.DeclaringType?.Name + "." + d.Method.Name)) { ((Action)d)(); }
        }
#else
        Changed?.Invoke();
#endif
    }

    /// <summary>폴백까지 반영한 실효값. 키는 짧은 키·전체 설정 키 모두 허용. 모르는 키는 "".</summary>
    public static string Effective(string key)
    {
        var spec = DesignThemes.Spec(key);
        if (spec is { IsAlias: true })
        {
            return AliasOf(spec, Effective(spec.AliasOf!)); // 옛 별칭은 정본 키 실효값에서 파생(depth.open ← motion.open)
        }

        var value = Stored(key);
        return spec is { IsEffect: true } ? Lower(spec.Key, value) : value;
    }

    /// <summary>폴백 전 값(저장·합성 결과). 옛 별칭은 정본 키 저장값에서 파생한다.</summary>
    public static string Stored(string key) =>
        DesignThemes.Spec(key) is { IsAlias: true } alias ? AliasOf(alias, Stored(alias.AliasOf!)) : Resolve(DesignThemes.Spec(key), out _);

    /// <summary>현재 디자인 테마가 정한 값 ?? 효과 표 기본값('디자인 테마 값으로 되돌리기' 표시용).</summary>
    public static string PresetValue(string key)
    {
        if (DesignThemes.Spec(key) is not { } spec)
        {
            return "";
        }

        return DesignThemes.TryNormalize(spec.Key, DesignThemes.FindOrDefault(Current).Effect(spec.Key), out var v) ? v : spec.Default;
    }

    /// <summary>값을 정한 층: user · level(효과 단계) · company · style(디자인 테마) · default. 옛 별칭은 정본 키의 층.</summary>
    public static string Source(string key)
    {
        var spec = DesignThemes.Spec(key);
        Resolve(spec is { IsAlias: true } ? DesignThemes.Spec(spec.AliasOf) : spec, out var source);
        return source;
    }

    /// <summary>숫자 설정 값(형식 오류면 0).</summary>
    public static double Number(string key) =>
        double.TryParse(Stored(key), NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : 0;

    /// <summary>참·거짓 설정 값.</summary>
    public static bool Flag(string key) => string.Equals(Stored(key), "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>실효 motion 의 전환 시간(theme.style.motion.&lt;값&gt;.ms). inherit·none 이면 0(디자인 테마 전환 없음).</summary>
    public static int MotionMs()
    {
        var motion = Effective("motion");
        return DesignThemes.Spec("motion." + motion + ".ms") is not null ? (int)Math.Round(Number("motion." + motion + ".ms")) : 0;
    }

    private static string? Override(DesignEffectSpec spec) =>
        _overrides.TryGetValue(spec.Key, out var v) ? v : _overrides.TryGetValue(spec.SettingKey, out var full) ? full : null;

    private static string? Company(string settingKey) =>
        ThemeElements.Policy.Values.TryGetValue(settingKey, out var v) ? v : null;

    private static List<string> Reasons()
    {
        var reasons = new List<string>(4);
        if (Flag("depth.softwareFallback") && (_simulateSoftware || GpuSetting.Applied == GpuMode.Off))
        {
            reasons.Add("software");
        }

        var level = EffectiveMotionLevel;
        if (level == "off")
        {
            reasons.Add("motionOff");
        }
        else if (level == "reduced")
        {
            reasons.Add("reducedMotion");
        }

        int count;
        try
        {
            count = WindowCount?.Invoke() ?? 0;
        }
        catch (InvalidOperationException)
        {
            count = 0; // 화면 층이 아직 준비되지 않음 — 창 수 폴백 판정 생략
        }

        if (count > Number("depth.maxWindows"))
        {
            reasons.Add("maxWindows");
        }

        return reasons;
    }

    /// <summary>폴백 낮춤(18 문서 §폴백, 21 §7): motion.open·close·switch 는 LowerMove, depth.overview→flat, depth.page→inherit, motion→none(움직임 줄이기만이면 subtle). inherit(기존 동작)은 움직임 끔에서만 none.</summary>
    private static string Lower(string key, string value)
    {
        if (key == "depth.overview" && NavLayout?.Invoke() == true)
        {
            return "flat"; // 폼팩터 규칙(18 §6): NAV 전체 보기 = 평면 목록
        }

        var reasons = Reasons();
        if (reasons.Count == 0)
        {
            return value;
        }

        var off = reasons.Contains("motionOff");
        var hard = off || reasons.Contains("software") || reasons.Contains("maxWindows");
        return key switch
        {
            _ when IsMoveKey(key) => LowerMove(key, value, reasons), // 열기·닫기·전환 효과(21 §7)
            "depth.overview" => "flat",
            "depth.page" => "inherit",
            "motion" when value == "inherit" => off ? "none" : value,
            "motion" when value == "none" => value,
            "motion" => hard ? "none" : "subtle",
            _ => value,
        };
    }
}

/// <summary>
/// 창 전체 보기 호스트(review H-1). Pd.Poc.Shell 은 Pd.Ui.Views 를 참조하지 않으므로 화면 층(WindowOverview3D)이 시작할 때 대리자를 등록하고,
/// 셸 쪽(원격 명령·메뉴)은 이 호스트만 부른다. Open = 열기(창 0개면 false), Select = n번째 카드 선택, State = 상태 JSON, Requested = 열기 요청 알림.
/// </summary>
public static class WindowOverviewHost
{
    public static Func<bool>? Open { get; set; }

    public static Func<int, bool>? Select { get; set; }

    public static Func<string>? State { get; set; }

    /// <summary>덮개 좌표(x, y)에서 히트한 창 windowId(없으면 null).</summary>
    public static Func<double, double, string?>? SelectAt { get; set; }

    public static event Action? Requested;

    /// <summary>열기 요청을 화면 층에 알린다(단축키·메뉴·원격).</summary>
    public static void Request() => Requested?.Invoke();
}
