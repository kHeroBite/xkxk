// 효과(부하) 단계 묶음 표(REQ-116) — 높음·중간·낮음 단계마다 효과 키 → 값(디자인 테마 키와 ui.mdi.move_light.shadow), 해석 층에서만 쓴다
using System;
using System.Collections.Generic;
using System.Linq;

namespace xkxk.frameworks.ui.shell.Theming;

/// <summary>
/// 효과 단계 묶음의 유일한 데이터 표(21 문서 §5.2, D-591·D-593). 단계를 고르면 해석기(DesignThemeResolver)가 디자인 테마 값 위에 이 묶음을 얹는다.
/// 덮어쓰기 저장소에는 아무것도 쓰지 않는다. 묶음에 없는 키는 단계 층을 건너뛴다(중간의 decor 는 테마 값 유지).
/// 키는 효과 짧은 키(theme.style.effect.* 뒤) 또는 횡단 접두 ui 의 전체 키다. 값 수정은 21 §5.2 표와 함께 고친다.
/// </summary>
public static partial class DesignThemes
{
    /// <summary>효과 단계 짧은 키(theme.style.effectLevel).</summary>
    public const string EffectLevelKey = "effectLevel";

    /// <summary>단계 층을 건너뛰는 값(기본 — 테마 기본).</summary>
    public const string LevelTheme = "theme";

    /// <summary>끌기 중 그림자 키(UI 설정 저장소, 01a §3). 단계 묶음이 이 키에도 값을 준다.</summary>
    public const string MoveShadowKey = "ui.mdi.move_light.shadow";

    /// <summary>단계 허용 값(표시 순서). 표 초기화 순서와 무관하도록 계산 속성으로 둔다.</summary>
    public static string[] LevelIds => new[] { LevelTheme, "high", "medium", "low" };

    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> LevelBundles =
        new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
        {
            ["high"] = DesignThemeDefinition.Map(
                ("shadow", "float"), ("surface", "gradient"), ("decor", "aurora"), ("motion", "cinematic"),
                ("motion.open", "fadeZoom"), ("motion.close", "fadeZoomOut"), ("motion.switch", MotionEffects.SameAsOpen),
                ("depth.overview", "carousel3d"), ("depth.page", "flip3d"), (MoveShadowKey, "keep")),
            // 중간(권장): 초기 권장안. PERF-17 실측으로 PERF-12·13·02 를 못 맞추면 이 행을 고친다(D-592). decor 는 묶음 밖
            ["medium"] = DesignThemeDefinition.Map(
                ("shadow", "soft"), ("surface", "flat"), ("motion", "subtle"),
                ("motion.open", "fade"), ("motion.close", "fade"), ("motion.switch", MotionEffects.None),
                ("depth.overview", "flat"), ("depth.page", "inherit"), (MoveShadowKey, "hide")),
            ["low"] = DesignThemeDefinition.Map(
                ("shadow", "none"), ("surface", "flat"), ("decor", "none"), ("motion", "none"),
                ("motion.open", MotionEffects.None), ("motion.close", MotionEffects.None), ("motion.switch", MotionEffects.None),
                ("depth.overview", "flat"), ("depth.page", "inherit"), (MoveShadowKey, "hide")),
        };

    /// <summary>단계 묶음이 정한 값. 단계가 theme·모르는 값이거나 묶음에 없는 키면 null(단계 층을 건너뛴다).</summary>
    public static string? LevelValue(string? level, string key) =>
        level is not null && LevelBundles.TryGetValue(level, out var bundle) && bundle.TryGetValue(key, out var value) ? value : null;

    /// <summary>단계 묶음이 정하는 키 목록(표시·덮어쓰기 개수 집계용). theme·모르는 값은 빈 목록.</summary>
    public static IReadOnlyList<string> LevelKeys(string? level) =>
        level is not null && LevelBundles.TryGetValue(level, out var bundle) ? bundle.Keys.ToArray() : Array.Empty<string>();

    /// <summary>어느 단계에든 나오는 키 전체(묶음 키 합집합).</summary>
    public static IReadOnlyList<string> AllLevelKeys { get; } = LevelBundles.Values.SelectMany(b => b.Keys).Distinct().ToArray();
}
