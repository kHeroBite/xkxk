// 디자인 테마 카탈로그(REQ-104) — 내장 5종 프리셋 표와 효과·설정 키 표(키·허용값·기본값·리소스 키 PdStyle<효과>), 키 정규화·값 검증
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using static xkxk.frameworks.ui.shell.Theming.DesignThemeDefinition;

namespace xkxk.frameworks.ui.shell.Theming;

/// <summary>
/// 디자인 테마 카탈로그. 프리셋·효과 추가는 아래 표에 행만 더한다. 값(각도·원근·카드 크기·불투명도 하한·전환 ms 포함)은
/// 효과 표의 설정 키 + 기본값으로만 두고 코드에 숫자 상수로 박지 않는다(REQ-99, review M-3). 정본은 plan/09_화면설계/18_디자인테마_설계.md,
/// 움직임 효과·배경·효과 단계는 21_창_움직임효과_배경_설계.md. 효과 단계 묶음 표는 DesignThemes.Levels.cs 한 곳이다.
/// </summary>
public static partial class DesignThemes
{
    /// <summary>설정 키 접두. 효과 덮어쓰기 = theme.style.effect.&lt;효과&gt;, 표시명 키도 같은 접두.</summary>
    public const string SettingPrefix = "theme.style.";

    /// <summary>기본 디자인 테마(현 관리도구형 — 현재 화면과 같은 값). 알 수 없는 ID 도 이것으로 본다.</summary>
    public const string DefaultId = "ds-classic";

    /// <summary>움직임 수준 설정 키(system|full|reduced|off). 디자인 테마와 무관한 접근성 축이라 접두가 다르다.</summary>
    public const string MotionLevelKey = "theme.motion.level";

    /// <summary>창 전체 보기 단축키 설정 키.</summary>
    public const string OverviewShortcutKey = "shortcut.window.overview";

    private const string EffectPrefix = SettingPrefix + "effect.";

    private static readonly string[] None = Array.Empty<string>();

    private static readonly DesignEffectSpec[] Table =
    {
        // 효과(Choice) — 기본값 = ds-classic 값(inherit = 기존 설정·동작 그대로). 리소스 키는 적용기(DesignEffectsApplier)가 채운다
        new("shadow", EffectPrefix + "shadow", DesignEffectKind.Choice, new[] { "inherit", "none", "soft", "float" }, "inherit", "PdStyleShadow"),
        new("surface", EffectPrefix + "surface", DesignEffectKind.Choice, new[] { "flat", "gradient", "glass" }, "flat", "PdStyleSurface"),
        new("decor", EffectPrefix + "decor", DesignEffectKind.Choice,
            new[] { "none", "dots", "aurora", "waves", "grid", "diagonal", "hex", "topo", "gradient", "image" }, "none", "PdStyleDecor"),
        new("motion", EffectPrefix + "motion", DesignEffectKind.Choice, new[] { "inherit", "none", "subtle", "bouncy", "cinematic" }, "inherit", "PdStyleMotionMs"),
        // 창 열기·닫기·전환 효과(21 §3.1). 허용 값은 MotionEffects 카탈로그에서 온다
        new("motion.open", EffectPrefix + "motion.open", DesignEffectKind.Choice, MotionEffects.EnterIds, MotionEffects.None, null),
        new("motion.close", EffectPrefix + "motion.close", DesignEffectKind.Choice, MotionEffects.ExitIds, MotionEffects.None, null),
        new("motion.switch", EffectPrefix + "motion.switch", DesignEffectKind.Choice, MotionEffects.SwitchIds, MotionEffects.None, null),
        // 읽기 전용 옛 별칭(motion.open 에서 파생 — tilt3d 면 tilt, 그 밖은 none). 편집기 효과 행·덮어쓰기 대상이 아니다
        new("depth.open", EffectPrefix + "depth.open", DesignEffectKind.Choice, new[] { "none", "tilt" }, "none", null, AliasOf: "motion.open"),
        new("depth.overview", EffectPrefix + "depth.overview", DesignEffectKind.Choice, new[] { "flat", "carousel3d" }, "flat", "PdStyleDepthOverview"),
        new("depth.page", EffectPrefix + "depth.page", DesignEffectKind.Choice, new[] { "inherit", "flip3d" }, "inherit", "PdStyleDepthPage"),
        new("chrome", EffectPrefix + "chrome", DesignEffectKind.Choice, new[] { "standard", "pill", "minimal" }, "standard", "PdStyleChrome"),

        // 선택·잠금·제안·효과 단계(21 §5 — theme 는 단계 층 생략)
        new("id", SettingPrefix + "id", DesignEffectKind.Text, None, DefaultId, null),
        new("locked", SettingPrefix + "locked", DesignEffectKind.Flag, None, "false", null),
        new("suggestColor", SettingPrefix + "suggestColor", DesignEffectKind.Flag, None, "true", null),
        new(EffectLevelKey, SettingPrefix + EffectLevelKey, DesignEffectKind.Choice, LevelIds, LevelTheme, null),

        // 폴백·3D·전체 보기·재질·전환 시간(전부 초기 제시값, 18 문서 §효과 키)
        new("depth.maxWindows", SettingPrefix + "depth.maxWindows", DesignEffectKind.Number, None, "12", null, 1, 200),
        new("depth.softwareFallback", SettingPrefix + "depth.softwareFallback", DesignEffectKind.Flag, None, "true", null),
        new("depth.tiltDeg", SettingPrefix + "depth.tiltDeg", DesignEffectKind.Number, None, "14", null, 0, 60),
        new("depth.perspective", SettingPrefix + "depth.perspective", DesignEffectKind.Number, None, "800", null, 100, 5000),
        new("overview.cardSize", SettingPrefix + "overview.cardSize", DesignEffectKind.Text, None, "280,180", null),
        new("shadow.soft", SettingPrefix + "shadow.soft", DesignEffectKind.Text, None, "0 2 6 0 #22000000, 0 6 16 0 #1A000000", null),
        new("shadow.float", SettingPrefix + "shadow.float", DesignEffectKind.Text, None, "0 4 12 0 #2A000000, 0 14 32 0 #26000000", null),
        new("decor.tile", SettingPrefix + "decor.tile", DesignEffectKind.Number, None, "24", null, 8, 96),
        new("decor.dotSize", SettingPrefix + "decor.dotSize", DesignEffectKind.Number, None, "2", null, 1, 8),
        new("decor.opacity", SettingPrefix + "decor.opacity", DesignEffectKind.Number, None, "0.12", null, 0, 0.5),
        new("decor.minDelta", SettingPrefix + "decor.minDelta", DesignEffectKind.Number, None, "18", null, 0, 255),
        new("glass.minOpacity", SettingPrefix + "glass.minOpacity", DesignEffectKind.Number, None, "0.85", null, 0, 1),
        new("motion.subtle.ms", SettingPrefix + "motion.subtle.ms", DesignEffectKind.Number, None, "120", null, 0, 2000),
        new("motion.bouncy.ms", SettingPrefix + "motion.bouncy.ms", DesignEffectKind.Number, None, "220", null, 0, 2000),
        new("motion.cinematic.ms", SettingPrefix + "motion.cinematic.ms", DesignEffectKind.Number, None, "320", null, 0, 2000),
        new("motion.switch.visibleStartOpacity", SettingPrefix + "motion.switch.visibleStartOpacity", DesignEffectKind.Number, None, "0.6", null, 0, 1),

        // 접두가 다른 공용 설정(키 = 전체 설정 키)
        new(MotionLevelKey, MotionLevelKey, DesignEffectKind.Choice, new[] { "system", "full", "reduced", "off" }, "system", null),
        new(OverviewShortcutKey, OverviewShortcutKey, DesignEffectKind.Text, None, "Ctrl+Alt+O", null),
    };

    private static readonly DesignThemeDefinition[] Presets =
    {
        // 내장 5종(18 문서 §디자인 테마 목록, 초기 제시값). 요소 속성 키는 REQ-101(ThemeElements) 키이며 그 속성을 가진 요소에만 적용된다.
        // 움직임 효과 열기·닫기·전환은 21 §3.1 프리셋(D-571). ds-classic 은 none 3개라 화면이 그대로다
        new("ds-classic", Map(), Fx("inherit", "flat", "none", "inherit", ("none", "none", "none"), "flat", "inherit", "standard"), null),
        new("ds-cute", Map(("*.cornerRadius", "12"), ("*.padding", "+2"), ("docTitle.fontWeight", "SemiBold")),
            Fx("soft", "flat", "dots", "bouncy", ("fadeZoom", "fadeZoomOut", MotionEffects.SameAsOpen), "flat", "inherit", "pill"), "pd-pastel"),
        // 심플의 '도구 막대·상태 막대 테두리 0' 은 REQ-101 요소 속성에 없어 chrome=minimal 스타일 클래스로 표현한다
        new("ds-simple", Map(("*.cornerRadius", "2"), ("*.padding", "+2")),
            Fx("none", "flat", "none", "subtle", ("fade", "fade", MotionEffects.SameAsOpen), "flat", "inherit", "minimal"), BuiltinThemes.LightId),
        new("ds-rich", Map(("*.cornerRadius", "10")),
            Fx("float", "glass", "aurora", "cinematic", ("flyUp", "fade", "fade"), "flat", "inherit", "standard"), "pd-aurora"),
        new("ds-depth", Map(("*.cornerRadius", "8")),
            Fx("float", "gradient", "waves", "cinematic", ("tilt3d", "tilt3d", MotionEffects.SameAsOpen), "carousel3d", "flip3d", "standard"), null),
    };

    /// <summary>내장 디자인 테마(표시 순서).</summary>
    public static IReadOnlyList<DesignThemeDefinition> All => Presets;

    /// <summary>효과·설정 키 표 전체(효과 + 옛 별칭 + 설정).</summary>
    public static IReadOnlyList<DesignEffectSpec> EffectTable => Table;

    /// <summary>사용자가 덮어쓸 수 있는 효과(편집기 효과 행 순서, 옛 별칭 제외).</summary>
    public static IReadOnlyList<DesignEffectSpec> Effects { get; } = Table.Where(s => s.IsEffect).ToArray();

    /// <summary>읽기 전용 옛 별칭 행(상태 보고·호환 읽기용).</summary>
    public static IReadOnlyList<DesignEffectSpec> Aliases { get; } = Table.Where(s => s.IsAlias).ToArray();

    /// <summary>ID 로 찾기(대소문자 무시). 모르면 null.</summary>
    public static DesignThemeDefinition? Find(string? id) =>
        string.IsNullOrWhiteSpace(id) ? null : Presets.FirstOrDefault(p => string.Equals(p.Id, id.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>ID 로 찾되 모르면 기본(ds-classic) — 엣지: 알 수 없는 저장값.</summary>
    public static DesignThemeDefinition FindOrDefault(string? id) => Find(id) ?? Presets[0];

    /// <summary>
    /// 키 1행 찾기. 짧은 키(shadow·depth.tiltDeg)·전체 설정 키(theme.style.effect.shadow·theme.style.depth.tiltDeg)·접두 뒤 effect. 형태 모두 허용. 모르면 null.
    /// </summary>
    public static DesignEffectSpec? Spec(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        var k = key.Trim();
        return Table.FirstOrDefault(s => s.Key == k || s.SettingKey == k)
            ?? (k.StartsWith("effect.", StringComparison.Ordinal) ? Table.FirstOrDefault(s => s.SettingKey == EffectPrefix + k.Substring(7)) : null);
    }

    /// <summary>효과 덮어쓰기 설정 키(theme.style.effect.&lt;효과&gt;).</summary>
    public static string EffectSettingKey(string effect) => EffectPrefix + effect;

    /// <summary>값 표시명 다국어 키(theme.style.value.&lt;값&gt;). 움직임 수준은 theme.style.motionLevel.&lt;값&gt;.</summary>
    public static string ValueLabelKey(string key, string value) =>
        Spec(key)?.Key == MotionLevelKey ? SettingPrefix + "motionLevel." + value : SettingPrefix + "value." + value;

    /// <summary>값 검증·정규화. 모르는 키·허용 목록 밖·범위 밖·형식 오류는 false(호출자는 무시하고 저장값은 보존 — 엣지 §9).</summary>
    public static bool TryNormalize(string? key, string? value, out string normalized)
    {
        normalized = "";
        if (Spec(key) is not { } spec || value is null)
        {
            return false;
        }

        var text = value.Trim();
        switch (spec.Kind)
        {
            case DesignEffectKind.Choice:
                normalized = spec.Allowed.FirstOrDefault(a => string.Equals(a, text, StringComparison.OrdinalIgnoreCase)) ?? "";
                return normalized.Length > 0;
            case DesignEffectKind.Flag:
                if (!bool.TryParse(text, out var flag))
                {
                    return false;
                }

                normalized = flag ? "true" : "false";
                return true;
            case DesignEffectKind.Number:
                if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) || !double.IsFinite(n) || n < spec.Min || n > spec.Max)
                {
                    return false;
                }

                normalized = n.ToString("0.###", CultureInfo.InvariantCulture);
                return true;
            default:
                normalized = text;
                return text.Length > 0;
        }
    }

    private static IReadOnlyDictionary<string, string> Fx(string shadow, string surface, string decor, string motion,
        (string Open, string Close, string Switch) moves, string depthOverview, string depthPage, string chrome) =>
        Map(("shadow", shadow), ("surface", surface), ("decor", decor), ("motion", motion),
            ("motion.open", moves.Open), ("motion.close", moves.Close), ("motion.switch", moves.Switch),
            ("depth.overview", depthOverview), ("depth.page", depthPage), ("chrome", chrome));
}
