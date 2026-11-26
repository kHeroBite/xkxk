// 디자인 테마 해석기의 요소 속성 층 값(ElementValue) — DesignThemeResolver.cs 에서 분리한 partial
using System;
using System.Collections.Generic;
using System.Globalization;

namespace xkxk.frameworks.ui.shell.Theming;

public static partial class DesignThemeResolver
{
    /// <summary>
    /// 규칙 미리 계산용(UI 스레드, ThemeDesignApplier 전환형 Styles 만 쓴다). null 이 아니면 요소 속성 층 값을 실효 디자인 테마 대신 이 ID 의 테마로 읽는다.
    /// 쓰는 쪽이 try/finally 로 바로 null 로 되돌린다. 값은 앱 컨텍스트의 DesignThemeState 가 보관한다(B05).
    /// </summary>
    internal static string? ElementThemeOverride { get => S.ElementThemeOverride; set => S.ElementThemeOverride = value; }

    /// <summary>
    /// 디자인 테마 층의 요소 속성 값(ThemeElements.Resolve 가 회사 다음·템플릿 앞에서 부른다). 현재 디자인 테마에 값이 없거나
    /// 그 요소가 속성을 갖지 않거나 검증에 실패하면 false. "+N"·"-N" 은 제품 기본값에 더하며 기본값이 상속("")이면 적용하지 않는다(현재 화면 유지).
    /// </summary>
    public static bool ElementValue(ThemeDefinition t, string el, string prop, out string value)
    {
        _ = t; // 색 테마와 직교 — 색 테마에 따라 값을 바꾸지 않는다(계약 시그니처 유지)
        value = "";
        if (DepthDiag.SkipElementDesign || !ThemeElements.Has(el, prop) || DesignThemes.FindOrDefault(ElementThemeOverride ?? Current).Element(el, prop) is not { } raw)
        {
            return false;
        }

        if (raw.Length > 1 && raw[0] is ('+' or '-') && ThemeElements.Prop(prop)?.Kind != DesignPropKind.SizeOffset)
        {
            if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var offset) || ThemeElements.Default(el, prop) is not { Length: > 0 } baseline)
            {
                return false;
            }

            var parts = baseline.Split(',', StringSplitOptions.TrimEntries);
            var shifted = new List<string>(parts.Length);
            foreach (var p in parts)
            {
                if (!double.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, out var n))
                {
                    return false;
                }

                shifted.Add((n + offset).ToString("0", CultureInfo.InvariantCulture));
            }

            raw = string.Join(",", shifted);
        }

        return ThemeElements.TryNormalize(el, prop, raw, out value);
    }
}
