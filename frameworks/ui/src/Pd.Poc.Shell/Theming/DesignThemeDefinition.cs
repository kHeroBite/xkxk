// 디자인 테마 모델(REQ-104) — 내장 디자인 테마 1종(요소 속성·효과 값·추천 색)과 효과·설정 키 1행 정의, 시스템 움직임 줄이기 선호
using System;
using System.Collections.Generic;
using Pd.Poc.Shell.Localization;

namespace Pd.Poc.Shell.Theming;

/// <summary>효과·설정 값 종류. Choice = 허용 목록 중 하나, Number = 범위 안 숫자, Flag = true|false, Text = 자유 문자열(형식은 소비자가 해석).</summary>
public enum DesignEffectKind { Choice, Number, Flag, Text }

/// <summary>
/// 효과·설정 키 1행(DesignThemes.EffectTable). Key = 접두 뒤 짧은 키(예 shadow·depth.tiltDeg), SettingKey = 전체 설정 키,
/// Allowed = Choice 허용 목록, Default = 표 기본값, ResourceKey = 적용기가 쓰는 앱 리소스 키(PdStyle&lt;효과&gt;, 없으면 null), Min·Max = Number 범위,
/// AliasOf = 읽기 전용 옛 별칭이면 정본 짧은 키(depth.open → motion.open, 21 문서 §3.1). 별칭 행은 편집기 효과 행·덮어쓰기 대상이 아니다.
/// 값 범위·허용 목록은 이 표의 데이터이며 코드 분기에 박지 않는다(REQ-99).
/// </summary>
public sealed record DesignEffectSpec(
    string Key, string SettingKey, DesignEffectKind Kind, string[] Allowed, string Default, string? ResourceKey, double Min = 0, double Max = 0,
    string? AliasOf = null)
{
    /// <summary>사용자가 효과 행에서 덮어쓸 수 있는 효과 키인지(theme.style.effect.* Choice, 옛 별칭 제외).</summary>
    public bool IsEffect => AliasOf is null && Kind == DesignEffectKind.Choice && SettingKey.StartsWith(DesignThemes.SettingPrefix + "effect.", StringComparison.Ordinal);

    /// <summary>읽기 전용 옛 별칭 행인지(값은 정본 키에서 파생한다).</summary>
    public bool IsAlias => AliasOf is not null;

    /// <summary>표시명 다국어 키(theme.style.effect.&lt;Key&gt;).</summary>
    public string LabelKey => DesignThemes.SettingPrefix + "effect." + Key;
}

/// <summary>
/// 디자인 테마 1종(색과 직교하는 모양·효과 묶음). 색 토큰은 바꾸지 않는다.
/// Elements 키 = "&lt;요소&gt;.&lt;속성&gt;"(REQ-101 키) 또는 "*.&lt;속성&gt;"(그 속성을 가진 모든 요소), 값 "+N"·"-N" = 제품 기본값에 더하는 오프셋.
/// Effects 키 = 효과 짧은 키(shadow·surface·decor·motion·motion.open·motion.close·motion.switch·depth.overview·depth.page·chrome). 없는 키는 효과 표 기본값.
/// </summary>
public sealed record DesignThemeDefinition(
    string Id,
    IReadOnlyDictionary<string, string> Elements,
    IReadOnlyDictionary<string, string> Effects,
    string? SuggestedColorThemeId)
{
    /// <summary>이름 다국어 키(theme.style.preset.&lt;Id&gt;).</summary>
    public string NameKey => DesignThemes.SettingPrefix + "preset." + Id;

    /// <summary>설명 다국어 키(theme.style.preset.&lt;Id&gt;.desc).</summary>
    public string DescriptionKey => NameKey + ".desc";

    /// <summary>현재 언어 이름. 읽을 때마다 번역하므로 언어 전환 뒤 다시 읽으면 새 언어다.</summary>
    public string LocalizedName => Loc.Tr(NameKey);

    public string LocalizedDescription => Loc.Tr(DescriptionKey);

    /// <summary>이 디자인 테마가 정한 효과 값. 없으면 null(호출자가 효과 표 기본값을 쓴다).</summary>
    public string? Effect(string key) => Effects.TryGetValue(key, out var v) ? v : null;

    /// <summary>요소 속성 원값(정확한 키 우선, 다음 "*.속성"). 없으면 null. 오프셋 해석·검증은 DesignThemeResolver.ElementValue.</summary>
    public string? Element(string el, string prop) =>
        Elements.TryGetValue(el + "." + prop, out var exact) ? exact
        : Elements.TryGetValue("*." + prop, out var any) ? any : null;

    public override string ToString() => LocalizedName;

    internal static IReadOnlyDictionary<string, string> Map(params (string Key, string Value)[] pairs)
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (k, v) in pairs)
        {
            d[k] = v;
        }

        return d;
    }
}

/// <summary>시스템 움직임 줄이기 선호(웹은 prefers-reduced-motion 을 JSImport 로 읽어 설정, 데스크톱·Android OS 연동은 TODO-182). 바뀌면 해석기를 재계산한다.</summary>
public static class MotionPreference
{
    private static bool _systemReduced;

    public static bool SystemReduced
    {
        get => _systemReduced;
        set
        {
            if (_systemReduced == value)
            {
                return;
            }

            _systemReduced = value;
            DesignThemeResolver.Refresh();
        }
    }
}
