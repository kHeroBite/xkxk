// 테마 요소별 디자인 적용기(REQ-101) — 지정된 (요소, 속성)만 골라 코드로 Style·Setter 를 만들고 Application.Styles 의 전용 Styles 1개를 교체한다. 미지정이면 Setter 0개, 서명이 같으면 생략(디자인 테마만 바뀌면 클래스 전환 — .Switch.cs)
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Styling;
using xkxk.frameworks.ui.shell.Framework;

namespace xkxk.frameworks.ui.shell.Theming;

/// <summary>
/// 요소 디자인 값을 앱 전역 스타일로 반영한다. 정적 DynamicResource Setter 는 키가 없을 때도 속성 기본값을 내 기존 렌더를 덮었으므로(D-4) 쓰지 않는다.
/// 어느 층도 값을 주지 않은 (요소, 속성)은 Setter 자체가 없어 컨트롤 테마 값(현재 렌더)이 그대로 쓰인다. 즉시 반영 = 전용 Styles 를 다시 만들어 교체.
/// 색은 기존 PdBrush* 토큰을 그대로 쓰므로 여기서 쓰지 않는다(ThemeApplier 담당).
/// </summary>
public static partial class ThemeDesignApplier
{
    private static string? _lastSignature = ""; // 마지막으로 만든 (셀렉터|속성|값) 목록. 시작값 "" = PdApp.axaml 의 빈 슬롯과 같음(기동 때 교체 생략)
    private static bool _baselineRead;
    private static Styles? _current; // 지금 앱에 넣어 둔 디자인 Styles(표식 리소스 없이 참조로 찾는다 — SlotIndex 주석)

    /// <summary>테마 t 의 요소 디자인 실효값을 app 스타일에 반영한다. 테마·글자 크기·글꼴 적용 뒤 매번 불러도 바뀐 것이 없으면 비용이 거의 없다.</summary>
    public static void Apply(Application app, ThemeDefinition t)
    {
        HookTopLevels();
        if (!_baselineRead)
        {
            ReadBaseline(app);
            _baselineRead = true;
        }

        List<(string Selector, AvaloniaProperty Property, object Value)> rules;
        bool switchable;
        string text;
        using (UiSettings.Op("ui.apply.design.rules"))
        {
            (rules, switchable) = Plan(t, XkxkUi.Current.Settings);
            var signature = new StringBuilder(switchable ? "switchable\n" : "");
            foreach (var (selector, property, value) in rules)
            {
                signature.Append(selector).Append('|').Append(property.Name).Append('|').Append(value).Append('\n');
            }

            text = signature.ToString();
        }

        if (string.Equals(text, _lastSignature, StringComparison.Ordinal))
        {
            // 전환형 Styles 가 그대로면 디자인 테마만 바뀐 경우다 — 최상위 창 클래스만 바꾼다(전체 재스타일 없음, K13)
            SyncSwitchClass(app);
            if (_switchable)
            {
                DiagNextFrame(app);
            }

            return;
        }

        _lastSignature = text;
        _switchable = switchable;
        SyncSwitchClass(app);

        Styles styles;
        using (UiSettings.Op("ui.apply.design.build")) { styles = ThemeDesignStyles.Build(rules); }
        // 인덱서 교체(Replace 알림)는 이전 Styles 의 값이 한 단계 늦게 빠지는 것을 실측했다(2026-11-16 격리 진단판 — 되돌린 값이 다음 변경 때까지 남음).
        // 그래서 같은 칸에서 빼고 다시 넣는다(Remove·Add 알림 — WindowChromeDiag 와 같은 방식). Setter 를 하나씩 넣고 빼지는 않는다
        var list = app.Styles;
        var at = _current is null ? -1 : list.IndexOf(_current);
        if (at >= 0)
        {
            using (UiSettings.Op("ui.apply.design.remove")) { list.RemoveAt(at); }
        }
        else
        {
            at = SlotIndex(list);
        }

        using (UiSettings.Op("ui.apply.design.insert")) { list.Insert(at, styles); }
        _current = styles;
        RestyleAll(app);
        DiagNextFrame(app);
    }

    /// <summary>
    /// 처음 넣을 자리 = PdApp.axaml 빈 슬롯(표식 리소스 보유) 바로 뒤. 빈 슬롯은 빼지 않고, 넣는 Styles 에는 리소스를 두지 않는다 —
    /// 리소스가 있는 Styles 를 넣고 빼면 Avalonia 가 트리 전체에 ResourcesChanged 를 보내 모든 DynamicResource 를 다시 찾는다(K13 웹 분해에서 insert·remove 안 방송 확인).
    /// 슬롯이 없는 호스트(예상 밖)는 맨 뒤.
    /// </summary>
    private static int SlotIndex(Styles list)
    {
        for (var i = 0; i < list.Count; i++)
        {
            if (list[i] is Styles slot && slot.Resources.ContainsKey(ThemeElements.StylesSlotKey))
            {
                return i + 1;
            }
        }

        return list.Count;
    }

    /// <summary>
    /// 숨은 컨트롤(IsVisible=false — 예 모바일형의 숨은 메뉴줄, 늘 숨은 더보기 항목)은 측정이 일어나지 않아 Styles 교체가 다시 적용되지 않고 옛 값으로 남는다
    /// (2026-11-16 격리 진단판 재현: 숨은 shell.mnuMore 가 menuBar 글꼴 지정 뒤에도·재시작 뒤에도 옛 글꼴). 그래서 교체 직후 모든 최상위 창의 논리 트리에
    /// 공개 API StyledElement.ApplyStyling 을 불러 지금 적용되게 한다(이미 적용된 컨트롤은 바로 돌아온다). 비용은 조작 계측 ui.apply.design.restyle.
    /// </summary>
    private static void RestyleAll(Application app)
    {
        using (UiSettings.Op("ui.apply.design.restyle"))
        {
            foreach (var root in Roots(app))
            {
                root.ApplyStyling();
                foreach (var element in root.GetLogicalDescendants().OfType<StyledElement>())
                {
                    element.ApplyStyling();
                }
            }
        }
    }

    /// <summary>스타일 루트: 데스크톱은 모든 창(메인·SDI), 단일 뷰(웹·Android)는 메인 뷰의 최상위(관리형 창도 그 안에 있다).</summary>
    private static IEnumerable<StyledElement> Roots(Application app) => app.ApplicationLifetime switch
    {
        IClassicDesktopStyleApplicationLifetime desktop => desktop.Windows,
        ISingleViewApplicationLifetime { MainView: { } view } => new StyledElement[] { TopLevel.GetTopLevel(view) ?? (StyledElement)view },
        _ => Array.Empty<StyledElement>(),
    };

    /// <summary>시험·진단용: 다음 Apply 가 서명 비교 없이 다시 만들게 한다.</summary>
    public static void Invalidate() => _lastSignature = null;

    /// <summary>문서 창 안 되돌림 값: 문서 글꼴·문서 기준 크기(요소 기준 토큰)·Avalonia 상속 기본 굵기(Normal).</summary>
    private static object? DocValue(DesignElementSpec spec, string prop, UiSettings settings) => prop switch
    {
        "fontFamily" => UiSettings.FontResolver(settings.FontName),
        "fontSize" => settings.BaseFontSize + UiSettings.FontTokens.FirstOrDefault(x => x.Key == ThemeElements.SizeTokenOf(spec.Key)).Offset,
        "fontWeight" => FontWeight.Normal,
        _ => null,
    };

    /// <summary>정규화된 값 → Setter 값. 변환할 수 없으면 null(Setter 없음 = 지금 화면).</summary>
    internal static object? Convert(ThemeDefinition t, DesignElementSpec spec, string prop, string value, UiSettings settings)
    {
        if (ThemeElements.Prop(prop) is not { } p)
        {
            return null;
        }

        switch (p.Kind)
        {
            case DesignPropKind.Family:
                return value.Length == 0 ? null : UiSettings.FontResolver(value);
            case DesignPropKind.SizeOffset:
                if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var offset))
                {
                    return null;
                }

                // D-3: 요소 자기 기본 크기(문서 범위면 문서, 아니면 시스템 글자 크기 + 기준 토큰 오프셋 — 토큰은 실제 무지정 렌더 크기와 같다) + 오프셋을 최소~최대로 자른다
                return Math.Clamp(ThemeElements.BaseFontSize(spec.Key, settings) + offset, UiSettings.MinFontSize, UiSettings.MaxFontSize);
            case DesignPropKind.Weight:
                if (!Enum.TryParse<FontWeight>(value, ignoreCase: true, out var weight))
                {
                    return null;
                }

                // 합성 굵게 금지(01 §5.2): 요소 글꼴(없으면 시스템 글꼴)에 실재하는 가까운 굵기로 바꾼다
                var familyName = ThemeElements.Has(spec.Key, "fontFamily") && ThemeElements.Effective(t, spec.Key, "fontFamily") is { Length: > 0 } f ? f
                    : spec.DocScope ? settings.FontName : settings.ShellFontName;
                return UiSettingsApplier.RealWeight(UiSettings.FontResolver(familyName), WeightOrder(weight));
            case DesignPropKind.Corner:
                return TryNumbers(value, out var c) && c.Length == 1 ? new CornerRadius(c[0]) : null;
            case DesignPropKind.Border:
                return TryNumbers(value, out var b) && b.Length == 1 ? new Thickness(b[0]) : null;
            case DesignPropKind.Padding:
                if (!TryNumbers(value, out var n))
                {
                    return null;
                }

                return n.Length switch
                {
                    1 => new Thickness(n[0]),
                    2 => new Thickness(n[0], n[1]),
                    4 => new Thickness(n[0], n[1], n[2], n[3]),
                    _ => null,
                };
            default:
                return null;
        }
    }

    /// <summary>요청 굵기와 가까운 굵기 순서(합성이면 다음 후보, 모두 합성이면 Normal — RealWeight).</summary>
    private static FontWeight[] WeightOrder(FontWeight w) => w switch
    {
        FontWeight.Bold => new[] { FontWeight.Bold, FontWeight.SemiBold },
        FontWeight.SemiBold => new[] { FontWeight.SemiBold, FontWeight.Bold },
        FontWeight.Medium => new[] { FontWeight.Medium, FontWeight.Normal },
        _ => new[] { w },
    };

    /// <summary>제품 기본값 표시용으로 Fluent 리소스의 현재 렌더 값을 1회 읽는다(이 키들은 쓰지 않으므로 늘 원래 값).</summary>
    private static void ReadBaseline(Application app)
    {
        foreach (var (designKey, resourceKey) in ThemeElements.BaselineResources)
        {
            if (!app.TryGetResource(resourceKey, app.ActualThemeVariant, out var raw))
            {
                continue;
            }

            var text = raw switch
            {
                CornerRadius r => Num(r.TopLeft),
                Thickness th when th.Left == th.Top && th.Top == th.Right && th.Right == th.Bottom => Num(th.Left),
                Thickness th => $"{Num(th.Left)},{Num(th.Top)},{Num(th.Right)},{Num(th.Bottom)}",
                double d => Num(d),
                _ => null,
            };
            var dot = designKey.IndexOf('.');
            if (text is not null && ThemeElements.TryNormalize(designKey[..dot], designKey[(dot + 1)..], text, out var normalized))
            {
                ThemeElements.SetBaseline(designKey, normalized);
            }
        }
    }

    private static string Num(double v) => Math.Round(v).ToString("0", CultureInfo.InvariantCulture);

    private static bool TryNumbers(string value, out double[] numbers)
    {
        var parts = value.Split(',', StringSplitOptions.TrimEntries);
        numbers = new double[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out numbers[i]))
            {
                return false;
            }
        }

        return parts.Length > 0;
    }
}
