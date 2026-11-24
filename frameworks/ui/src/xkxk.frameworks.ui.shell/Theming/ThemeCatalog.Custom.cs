// 테마 카탈로그 요소별 사용자 지정(REQ-101) — 내장 편집 시 사용자 사본 자동 생성·요소 디자인 희소 저장·되돌리기 3단·기준 템플릿 해석
using System;
using System.Collections.Generic;
using System.Linq;
using xkxk.frameworks.ui.shell.Localization;

namespace xkxk.frameworks.ui.shell.Theming;

/// <summary>
/// 설계 정본 plan/09_화면설계/17_테마_요소별_사용자지정_설계.md. 데이터 레이어만 다룬다(화면·뷰모델은 이 메서드만 호출).
/// 사용자 값은 ThemeDefinition.Design 에 바꾼 키만 둔다. 기준 템플릿 값과 같아지면 키를 지운다(희소 유지).
/// </summary>
public sealed partial class ThemeCatalog
{
    /// <summary>설계 키 "&lt;요소&gt;.&lt;속성&gt;".</summary>
    public static string DesignKey(string el, string prop) => el + "." + prop;

    /// <summary>
    /// 되돌리기 기준 내장 템플릿. 내장이면 자신, 사용자 테마면 BaseTemplateId 가 가리키는 내장,
    /// 없거나 내장이 아니면(옛 저장본) 같은 기반의 내장(pd-light·pd-dark). null 을 돌려주지 않는다.
    /// </summary>
    public ThemeDefinition BaseTemplateOf(ThemeDefinition theme)
    {
        if (theme.IsBuiltin)
        {
            return theme;
        }

        return Find(theme.BaseTemplateId) is { IsBuiltin: true } b
            ? b
            : Find(theme.Base == ThemeBase.Dark ? BuiltinThemes.DarkId : BuiltinThemes.LightId)!;
    }

    /// <summary>
    /// 편집 대상 테마(자동 사용자 지정, D-353). 지금 요청된 테마가 사용자 테마면 그대로,
    /// 내장이면 "&lt;템플릿 이름&gt; 사용자 지정" 사본(BaseTemplateId = 내장 Id)을 1회 만들어 선택하고 돌려준다. 내장은 바뀌지 않는다.
    /// </summary>
    public ThemeDefinition EnsureEditable()
    {
        var current = Requested;
        if (!current.IsBuiltin)
        {
            return current;
        }

        var copy = Duplicate(current, Loc.Tr("theme.design.customName", current.LocalizedName));
        Select(copy.Id);
        return copy;
    }

    /// <summary>
    /// 요소 속성 1개 지정. 내장 선택 중이면 사본을 만든 뒤 넣는다. 값은 ThemeElements.TryNormalize 로 검증하고,
    /// 기준 템플릿 값과 같으면 덮어쓰기를 지운다. 모르는 요소·속성·범위 밖 값이면 false(저장·적용 없음).
    /// </summary>
    public bool SetDesign(string el, string prop, string value)
    {
        if (!ThemeElements.TryNormalize(el, prop, value, out var normalized))
        {
            return false;
        }

        var theme = EnsureEditable();
        var key = DesignKey(el, prop);
        if (string.Equals(normalized, TemplateDesignValue(theme, el, prop), StringComparison.Ordinal))
        {
            theme.Design.Remove(key);
        }
        else
        {
            theme.Design[key] = normalized;
        }

        ReapplyDesignIfActive(theme);
        Changed?.Invoke();
        return true;
    }

    /// <summary>
    /// 되돌리기. prop 지정 = 그 속성 1개 덮어쓰기 삭제, prop 생략 = 그 요소 키 전부 삭제 + 그 요소 매핑 색 토큰을 기준 템플릿 색으로.
    /// 대상은 지금 요청된 테마이며 내장이면(덮어쓰기 없음) false.
    /// </summary>
    public bool ResetDesign(string el, string? prop = null)
    {
        var theme = Requested;
        if (theme.IsBuiltin)
        {
            return false;
        }

        if (prop is not null)
        {
            theme.Design.Remove(DesignKey(el, prop));
            ReapplyDesignIfActive(theme);
            Changed?.Invoke();
            return true;
        }

        var prefix = el + ".";
        foreach (var key in theme.Design.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList())
        {
            theme.Design.Remove(key);
        }

        var template = BaseTemplateOf(theme);
        foreach (var token in ThemeElements.ColorTokens(el))
        {
            if (template.Colors.TryGetValue(token, out var hex))
            {
                theme.Colors[token] = hex;
            }
        }

        ReapplyIfActive(theme); // 색이 바뀌므로 테마 전체 재적용(디자인 적용은 UiSettingsApplier 가 테마 적용 뒤 이어 부른다)
        Changed?.Invoke();
        return true;
    }

    /// <summary>전체 되돌리기: Design 비움 + Colors 전체를 기준 템플릿 값으로. 확인 대화상자는 뷰모델 몫. 내장이면 false.</summary>
    public bool ResetAll()
    {
        var theme = Requested;
        if (theme.IsBuiltin)
        {
            return false;
        }

        theme.Design.Clear();
        theme.Colors = new Dictionary<string, string>(BaseTemplateOf(theme).Colors, StringComparer.Ordinal);
        ReapplyIfActive(theme);
        Changed?.Invoke();
        return true;
    }

    /// <summary>템플릿 층 값 = 기준 템플릿의 Design 값, 없으면 제품 기본값(ThemeElements.Default).</summary>
    private string TemplateDesignValue(ThemeDefinition theme, string el, string prop) =>
        BaseTemplateOf(theme).Design.TryGetValue(DesignKey(el, prop), out var v) ? v : ThemeElements.Default(el, prop);

    /// <summary>디자인 값만 바뀐 경우: 활성 테마면 디자인 리소스만 다시 쓴다(색·글꼴 단계 생략, 같은 값은 Applier 서명 비교가 생략).</summary>
    private void ReapplyDesignIfActive(ThemeDefinition theme)
    {
        if (ReferenceEquals(Requested, theme) && UiSettings.Current.AppInstance is { } app)
        {
            ThemeDesignApplier.Apply(app, theme);
            DesignEffectsApplier.Apply(app); // 사용자 요소 값이 디자인 테마 효과(chrome=pill)보다 우선하도록 효과도 다시 맞춘다(서명이 같으면 생략)
        }
    }

    /// <summary>
    /// Normalize 보조: 모르는 요소·속성 키, 형식·범위 밖 값은 버린다(가져오기 안전). Design 이 없거나 null 인 옛 JSON 은 빈 사전.
    /// BaseTemplateId 는 내장 Id 를 가리킬 때만 유지한다.
    /// </summary>
    private void NormalizeDesign(ThemeDefinition item, ThemeDefinition theme)
    {
        theme.BaseTemplateId = Find(item.BaseTemplateId) is { IsBuiltin: true } b ? b.Id : null;
        if (item.Design is null)
        {
            return;
        }

        foreach (var (key, value) in item.Design)
        {
            var dot = key.IndexOf('.');
            if (dot <= 0 || dot == key.Length - 1 || value is null)
            {
                continue;
            }

            var el = key[..dot];
            var prop = key[(dot + 1)..];
            if (ThemeElements.TryNormalize(el, prop, value, out var normalized))
            {
                theme.Design[DesignKey(el, prop)] = normalized;
            }
        }
    }
}
