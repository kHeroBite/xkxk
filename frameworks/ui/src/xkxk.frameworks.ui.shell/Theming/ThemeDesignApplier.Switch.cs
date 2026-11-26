// 테마 요소 디자인 적용기의 규칙 계산·디자인 테마 전환형 Styles(K13) — 내장 디자인 테마별 규칙을 한 Styles 에 미리 넣고 최상위 창 클래스로 고른다. 사용자·회사 값이 있으면 단일 테마 경로
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using xkxk.frameworks.ui.shell.Framework;

namespace xkxk.frameworks.ui.shell.Theming;

public static partial class ThemeDesignApplier
{
    /// <summary>전환형 Styles 의 디자인 테마 표식 클래스 접두(최상위 창에 '접두 + 디자인 테마 ID' 를 단다).</summary>
    public const string SwitchClassPrefix = "pdDesign-";

    /// <summary>디자인 규칙이 걸리는 최상위 창 표식(팝업 루트가 아닌 TopLevel 에 늘 켠다 — 공통·단일 경로 규칙의 등급을 전환형과 같게 두려고).</summary>
    public const string DesignOnClass = "pdDesignOn";

    private const string TopLevelSelector = ":is(TopLevel)";
    private static bool _switchable; // 지금 앱에 넣어 둔 Styles 가 전환형인가(false = 현재 디자인 테마 하나의 규칙)
    private static bool _singleOnly; // 첫 프레임 표식 어긋남으로 조건 없는 단일 테마 경로에 고정(안전 폴백, 재시작 전까지)
    private static bool _topLevelHooked;
    private static bool _viewHooked; // 단일 뷰의 TopLevel 연결 훅(1회)
    private static readonly ConditionalWeakTable<TopLevel, object> SeenTemplated = new(); // 훅 등록 뒤 TemplateApplied 를 거친 TopLevel(약한 참조 — 닫힌 창을 붙잡지 않음)

    /// <summary>규칙 1개와 원래 열거 순서 키(단계·요소·속성·대상 순번). 키 순서가 곧 Style 우선순위(뒤가 이김) 순서다.</summary>
    private readonly record struct KeyedRule(int Key, string Selector, AvaloniaProperty Property, object Value)
    {
        public string Text => Selector + "|" + Property.Name + "|" + Value;
    }

    /// <summary>
    /// 지정된 (요소, 속성)의 Setter 목록. 순서: ① 셸 요소 대상 ② 셸 요소 글자 속성의 문서 창 안 되돌림 ③ 문서 범위 요소(표 머리·칸).
    /// 같은 속성은 뒤 스타일이 이기므로 이 순서를 바꾸지 않는다. 기본 층(어느 층도 값 없음)은 넣지 않는다.
    /// </summary>
    internal static List<(string Selector, AvaloniaProperty Property, object Value)> Rules(ThemeDefinition t, UiSettings settings) =>
        KeyedRules(t, settings).Select(r => (r.Selector, r.Property, r.Value)).ToList();

    private static List<KeyedRule> KeyedRules(ThemeDefinition t, UiSettings settings)
    {
        var picked = new List<(int Key, DesignElementSpec Spec, string Prop, object Value)>();
        var elements = ThemeElements.All.ToList();
        for (var e = 0; e < elements.Count; e++)
        {
            var spec = ThemeElements.Element(elements[e])!;
            var props = ThemeElements.Props(elements[e]).ToList();
            for (var p = 0; p < props.Count; p++)
            {
                if (ThemeElements.Resolve(t, elements[e], props[p], out var value) != DesignLayer.Default && Convert(t, spec, props[p], value, settings) is { } converted)
                {
                    picked.Add((e * 10_000 + p * 100, spec, props[p], converted));
                }
            }
        }

        var rules = new List<KeyedRule>();
        void AddTargets(int pass, bool docScope)
        {
            foreach (var (key, spec, prop, value) in picked.Where(x => x.Spec.DocScope == docScope))
            {
                var targets = ThemeElements.Targets(spec.Key);
                for (var i = 0; i < targets.Count; i++)
                {
                    if (targets[i].Takes(prop) && ThemeDesignStyles.PropertyOf(prop) is { } property)
                    {
                        rules.Add(new(pass * 1_000_000 + key + i, targets[i].Selector, property, value));
                    }
                }
            }
        }

        AddTargets(0, docScope: false);
        foreach (var (key, spec, prop, _) in picked.Where(x => !x.Spec.DocScope))
        {
            var targets = ThemeElements.DocReverts(spec.Key);
            for (var i = 0; i < targets.Count; i++)
            {
                if (targets[i].Takes(prop) && ThemeDesignStyles.PropertyOf(prop) is { } property && DocValue(spec, prop, settings) is { } value)
                {
                    rules.Add(new(1_000_000 + key + i, targets[i].Selector, property, value));
                }
            }
        }

        AddTargets(2, docScope: true);
        return rules;
    }

    /// <summary>
    /// 만들 규칙 결정. 사용자·회사 값이 없고 폴백 고정이 아니면 전환형: 카탈로그의 내장 디자인 테마마다 규칙을 계산해 원래 순서 키로 합친다.
    /// 모든 테마에서 같은 규칙은 그대로, 다른 규칙은 그 테마 표식 클래스를 단 최상위 창 아래로만 걸리게 감싼다. 어느 테마가 켜져도 걸리는 규칙 집합과 순서가
    /// 그 테마 하나로 만든 규칙(예전 방식)과 같다. 사용자·회사 값이 있으면 예전처럼 현재 디자인 테마 규칙만 만든다(REQ-101 경로 무변경).
    /// 등급 통일(리뷰 r2): 클래스 조건이 붙으면 Setter 등급이 Style → StyleTrigger 로 오른다. 그래서 공통 규칙과 단일 경로 규칙에도 늘 켜 둔
    /// 표식(DesignOnClass)을 같은 방식으로 붙여 두 경로의 등급을 같게 둔다. 안전 폴백(_singleOnly)만 조건 없는 예전 규칙이다(클래스에 기대지 않음).
    /// </summary>
    private static (List<(string Selector, AvaloniaProperty Property, object Value)> Rules, bool Switchable) Plan(ThemeDefinition t, UiSettings settings)
    {
        if (_singleOnly)
        {
            return (Rules(t, settings), false);
        }

        if (DesignThemes.Find(DesignThemeResolver.Current) is null || HasPinnedValues(t))
        {
            return (Rules(t, settings).Select(r => (Guard(r.Selector, null), r.Property, r.Value)).ToList(), false);
        }

        var perTheme = new List<(string Id, Dictionary<int, KeyedRule> Rules)>();
        foreach (var theme in DesignThemes.All)
        {
            DesignThemeResolver.ElementThemeOverride = theme.Id;
            try
            {
                perTheme.Add((theme.Id, KeyedRules(t, settings).ToDictionary(r => r.Key)));
            }
            finally
            {
                DesignThemeResolver.ElementThemeOverride = null;
            }
        }

        var merged = new List<(string, AvaloniaProperty, object)>();
        foreach (var key in perTheme.SelectMany(x => x.Rules.Keys).Distinct().OrderBy(k => k))
        {
            var first = perTheme[0].Rules.TryGetValue(key, out var r0) ? r0.Text : null;
            if (first is not null && perTheme.All(x => x.Rules.TryGetValue(key, out var r) && r.Text == first))
            {
                merged.Add((Guard(r0.Selector, null), r0.Property, r0.Value));
                continue;
            }

            foreach (var (id, map) in perTheme)
            {
                if (map.TryGetValue(key, out var r))
                {
                    merged.Add((Guard(r.Selector, id), r.Property, r.Value));
                }
            }
        }

        return (merged, true);
    }

    /// <summary>사용자·회사 층이 값을 준 (요소, 속성)이 하나라도 있는가(그때는 단일 테마 경로).</summary>
    private static bool HasPinnedValues(ThemeDefinition t) =>
        ThemeElements.All.Any(el => ThemeElements.Props(el).Any(p => ThemeElements.Resolve(t, el, p, out _) is DesignLayer.User or DesignLayer.Company));

    /// <summary>
    /// 셀렉터 목록의 각 항목을 '표식 클래스를 단 최상위 창' 아래로 한정한다(themeId 가 null 이면 늘 켜 둔 DesignOnClass). 최상위 창 자신을 고르는 항목(:is(TopLevel)…)은
    /// 표식 창 자신과 그 아래 최상위 창(팝업 루트 — 표식을 달지 않고 스타일 부모(PopupRoot.StylingParent)로 원래 창 표식을 따른다)을 함께 고른다.
    /// </summary>
    private static string Guard(string selector, string? themeId)
    {
        var mark = TopLevelSelector + "." + (themeId is null ? DesignOnClass : SwitchClassPrefix + themeId);
        var items = new List<string>();
        foreach (var raw in SplitTopLevel(selector))
        {
            var item = raw.Trim();
            if (item.StartsWith(TopLevelSelector, StringComparison.Ordinal))
            {
                items.Add(mark + item[TopLevelSelector.Length..]);
                items.Add(mark + " " + item);
            }
            else
            {
                items.Add(mark + " " + item);
            }
        }

        return string.Join(", ", items);
    }

    /// <summary>괄호 밖 쉼표로만 나눈다(:not(…) 안 쉼표는 나누지 않음).</summary>
    private static IEnumerable<string> SplitTopLevel(string selector)
    {
        var depth = 0;
        var start = 0;
        for (var i = 0; i < selector.Length; i++)
        {
            depth += selector[i] switch { '(' => 1, ')' => -1, _ => 0 };
            if (selector[i] == ',' && depth == 0)
            {
                yield return selector[start..i];
                start = i + 1;
            }
        }

        yield return selector[start..];
    }

    /// <summary>
    /// 동기화 지점(하나): 최상위 창 1개의 표식을 지금 상태로 맞춘다. 팝업 루트가 아닌 TopLevel 은 DesignOnClass 를 켜고, 전환형이면 현재 디자인 테마 표식만 켠다.
    /// 팝업 루트는 모든 표식을 끈다 — 열린 채 전환돼도 옛 표식이 남지 않고, 스타일 부모로 원래 창 표식을 따른다.
    /// 부르는 시점: Apply(최상위 창 전부), 새 TopLevel 의 TemplateApplied(첫 측정 안 — 첫 렌더 전), Loaded(검사 뒤).
    /// </summary>
    private static void SyncSwitchClass(StyledElement root)
    {
        if (root is not TopLevel top)
        {
            return; // 단일 뷰가 아직 TopLevel 에 붙기 전(웹 기동) — TopLevel 의 TemplateApplied 때 맞춘다
        }

        var on = top is not PopupRoot;
        top.Classes.Set(DesignOnClass, on);
        var active = on && _switchable ? DesignThemeResolver.Current : null;
        foreach (var theme in DesignThemes.All)
        {
            top.Classes.Set(SwitchClassPrefix + theme.Id, string.Equals(theme.Id, active, StringComparison.Ordinal));
        }
    }

    /// <summary>표식이 지금 상태와 같은가(SyncSwitchClass 가 만들 상태와 비교 — 바꾸지 않는다).</summary>
    private static bool IsSynced(TopLevel top)
    {
        var on = top is not PopupRoot;
        var active = on && _switchable ? DesignThemeResolver.Current : null;
        return top.Classes.Contains(DesignOnClass) == on
            && DesignThemes.All.All(theme => top.Classes.Contains(SwitchClassPrefix + theme.Id) == string.Equals(theme.Id, active, StringComparison.Ordinal));
    }

    private static void SyncSwitchClass(Application app)
    {
        foreach (var root in Roots(app))
        {
            SyncSwitchClass(root);
            if (root is Visual view and not TopLevel && !_viewHooked)
            {
                // 단일 뷰(웹·Android)가 아직 TopLevel 에 붙기 전이면, 붙는 순간(뷰 내용의 첫 측정 전) 그 TopLevel 에 표식을 맞춘다.
                // 웹 실측: TopLevel 의 TemplateApplied 가 첫 Apply(훅 등록) 전에 지나가 첫 프레임 표식이 빠졌다(firstframe.miss → 폴백)
                _viewHooked = true;
                view.AttachedToVisualTree += (_, _) =>
                {
                    if (TopLevel.GetTopLevel(view) is { } top)
                    {
                        SyncSwitchClass(top);
                    }
                };
            }
        }
    }

    /// <summary>새 TopLevel 표식 훅(1회). TemplateApplied 는 Layoutable.MeasureCore 안 ApplyTemplate 에서 오므로 첫 렌더 전이다(Loaded 는 레이아웃·렌더 뒤).</summary>
    private static void HookTopLevels()
    {
        if (_topLevelHooked)
        {
            return;
        }

        _topLevelHooked = true;
        TemplatedControl.TemplateAppliedEvent.AddClassHandler<TopLevel>((top, _) =>
        {
            SeenTemplated.AddOrUpdate(top, true);
            DiagTemplated(top);
            SyncSwitchClass(top);
        });
        Control.LoadedEvent.AddClassHandler<TopLevel>((top, _) => CheckLoaded(top), RoutingStrategies.Direct);
        // 웹 기동: 첫 Apply 는 MainView 생성 전(SettingsInput.Install)이고 TopLevel 은 그 전에 템플릿이 붙어, 위 훅이 첫 프레임을 맡지 못한다.
        // MainView 가 TopLevel.Content 로 들어오는 순간(뷰의 첫 측정 전) 표식을 맞춘다
        ContentControl.ContentProperty.Changed.AddClassHandler<TopLevel>((top, _) => SyncSwitchClass(top));
    }

    /// <summary>
    /// 안전 폴백 판정: Loaded(첫 렌더 뒤)에 표식이 이미 맞는지 먼저 본다. 어긋났으면 그 창은 첫 프레임을 다른 규칙으로 그린 것이므로,
    /// 표식을 맞춘 뒤 클래스에 기대지 않는 예전 방식(조건 없는 단일 테마 규칙)으로 바꾼다. 진단판은 결과를 firstframe.hit|miss 로 남긴다.
    /// </summary>
    private static void CheckLoaded(TopLevel top)
    {
        var ok = IsSynced(top);
        DiagFirstFrame(top, ok);
        SyncSwitchClass(top);
        // 훅 등록 전에 템플릿이 붙은 창(기동 초 단일 뷰 TopLevel)은 이 훅이 첫 프레임을 맡지 못했으므로 폴백 판정에서 뺀다(표식만 맞춘다)
        if (!ok && !_singleOnly && top is not PopupRoot && SeenTemplated.TryGetValue(top, out _) && Application.Current is { } app)
        {
            FallBackToSingle(app, XkxkUi.Current.Themes.Requested);
        }
    }

    /// <summary>안전 폴백: 전환형을 끄고(재시작 전까지) 클래스 조건 없는 현재 디자인 테마 규칙으로 다시 만든다(예전 방식 전체 교체).</summary>
    private static void FallBackToSingle(Application app, ThemeDefinition t)
    {
        _singleOnly = true;
        _switchable = false;
        _lastSignature = null;
        Apply(app, t);
    }
}
