// UI 설정 적용 담당(PERF-UI 개선) — 바뀐 부분만 적용(테마·폰트·문서 범위)하고 문서 범위 토큰은 사전 1개 교체로 일괄 반영
using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using xkxk.frameworks.ui.shell.Framework;

namespace xkxk.frameworks.ui.shell.Theming;

/// <summary>설정 변경 시 다시 적용할 부분. 글자 크기 변경에 색 테마 전체를 다시 쓰지 않게 나눈다(otest PERF FAIL 원인 ①②).</summary>
[Flags]
public enum ApplyParts
{
    None = 0,

    /// <summary>색 테마 + 라이트/다크 기반(ThemeApplier — 앱 전역 리소스 다수 갱신, 비쌈).</summary>
    Theme = 1,

    /// <summary>폰트 종류(앱 전역 리소스 2개, 이름이 실제로 바뀔 때만 씀).</summary>
    Family = 2,

    /// <summary>문서 범위 글자 크기·글꼴(Changed → 각 문서 래퍼가 ApplyDocumentScope).</summary>
    DocScope = 4,

    /// <summary>시스템(셸) 글자 크기 토큰·제목 줄 높이(앱 전역 리소스, 한 번에 씀, 2026-11-11).</summary>
    Shell = 8,

    /// <summary>디자인 테마·움직임 수준·효과 덮어쓰기(REQ-104 — Resolver 갱신 + 요소 디자인·효과 리소스, 색 테마는 다시 쓰지 않음).</summary>
    Style = 16,

    All = Theme | Family | DocScope | Shell | Style,
}

/// <summary>UiSettings 값을 앱 리소스·문서 래퍼에 반영한다. 값은 UiSettings 에서 읽는다.</summary>
internal sealed class UiSettingsApplier
{
    private readonly UiSettings _owner;
    private string? _appliedFamily; // 마지막으로 앱 리소스에 쓴 폰트 이름(같으면 다시 쓰지 않음)
    private string? _appliedThemeKey; // 마지막으로 ThemeApplier 에 넘긴 (모드·테마 Id 3개·OS 다크) — 같으면 호출 생략

    public UiSettingsApplier(UiSettings owner)
    {
        _owner = owner;
        DesignThemeResolver.Changed += ApplyMoveShadow; // 회사 잠금·효과 단계만 바뀌어도 끌기 중 그림자 실효값을 다시 정한다
    }

    /// <summary>끌기 중 그림자 실효값(잠금 → 사용자 → 효과 단계 묶음 → 회사 → hide, DesignThemeResolver.ResolveUiKey)을 Docking.MdiMoveLight.Shadow 에 넣는다.</summary>
    private void ApplyMoveShadow()
    {
        var user = _owner.MdiMoveLightShadowUser is { } us ? Docking.MdiMoveLight.ShadowKey(us) : null;
        Docking.MdiMoveLight.Shadow = Docking.MdiMoveLight.ParseShadow(DesignThemeResolver.ResolveUiKey(DesignThemes.MoveShadowKey, user, Docking.MdiMoveLight.ShadowKey(Docking.MdiMoveLight.DefaultShadow)));
    }

    /// <summary>지정한 부분만 적용한 뒤 Changed 를 1회 발생시킨다. 단계별 시간은 ui.apply.* 로 계측(PERF-UI 분해).</summary>
    public void Apply(ApplyParts parts, bool force)
    {
        // 진행 표시(요건 2): 테마 적용은 UI 스레드를 막으므로 메인 창 스피너를 먼저 그린 뒤 돌린다. 기동 중 첫 적용은 첫 렌더 전에 끝나야 해서 그대로 둔다
        if (parts.HasFlag(ApplyParts.Theme) && Busy.BusyHooks.IsMainAttached)
        {
            Busy.BusyHooks.RunAfterFrame(Busy.BusyHooks.MainScope, "theme", () => ApplyNow(parts, force), dedupe: false);
            return;
        }

        ApplyNow(parts, force);
    }

    private void ApplyNow(ApplyParts parts, bool force)
    {
        if (_owner.AppInstance is not { } app)
        {
            return;
        }

        if (parts.HasFlag(ApplyParts.Theme))
        {
            // 테마 Id·모드(+시스템 모드면 OS 다크 여부)가 그대로면 ThemeApplier 를 부르지 않는다(team-lead A 보강).
            var key = $"{_owner.Theme}|{_owner.ThemeId}|{_owner.SystemLightThemeId}|{_owner.SystemDarkThemeId}|{ThemeApplier.IsOsDark(app)}";
            if (force || !string.Equals(key, _appliedThemeKey, StringComparison.Ordinal))
            {
                using (UiSettings.Op("ui.apply.theme")) { ApplyColorTheme(app); } // 앱 수준 알림 일괄화는 ThemeApplier(odev-7c)
                _appliedThemeKey = key;
            }
        }

        if (parts.HasFlag(ApplyParts.Shell))
        {
            using (UiSettings.Op("ui.apply.shell")) { ApplyShell(app); }
        }

        var fontName = _owner.ShellFontName; // 앱 전역 = 시스템 글꼴. 문서 창은 래퍼가 문서 글꼴(FontName)로 덮는다(ApplyDocumentScope, 2026-11-11)
        if (parts.HasFlag(ApplyParts.Family) && !string.Equals(_appliedFamily, fontName, StringComparison.Ordinal))
        {
            using (UiSettings.Op("ui.apply.family"))
            {
                // 앱 전역에는 시스템 글꼴을 반영한다. 시스템 글자 크기는 ApplyShell, 문서 창은 래퍼 범위(ApplyDocumentScope).
                var family = UiSettings.FontResolver(fontName);
                var strong = RealWeight(family, FontWeight.SemiBold, FontWeight.Bold);
                var bold = RealWeight(family, FontWeight.Bold, FontWeight.SemiBold);
                // 키들을 SetItems 로 한 번에 쓴다 — 인덱서로 하나씩 쓰면 앱 전체 리소스 알림(시각 트리 전체 순회)이 키 수만큼 난다(perf15 C)
                if (app.Resources is ResourceDictionary resources)
                {
                    resources.SetItems(new KeyValuePair<object, object?>[]
                    {
                        new("PdFontFamily", family),
                        new("ContentControlThemeFontFamily", family),
                        new("PdFontWeightStrong", strong),
                        new("PdFontWeightBold", bold),
                    });
                }
                else
                {
                    app.Resources["PdFontFamily"] = family; // ResourceDictionary 가 아닌 구현(예상 밖)이면 예전처럼 하나씩
                    app.Resources["ContentControlThemeFontFamily"] = family;
                    app.Resources["PdFontWeightStrong"] = strong;
                    app.Resources["PdFontWeightBold"] = bold;
                }
                _appliedFamily = fontName;
            }
        }

        // 요소별 디자인(REQ-101): 테마·시스템/문서 글자 크기·글꼴 어느 쪽이 바뀌어도 실효값이 달라질 수 있어 매번 부른다(서명이 같으면 생략)
        DesignThemeResolver.SetEffectLevel(_owner.EffectLevel); // 효과 단계(D-591 해석 층) — 덮어쓰기와 따로, Update 보다 먼저 공급
        DesignThemeResolver.Update(_owner.DesignThemeId, _owner.MotionLevel.ToString().ToLowerInvariant(), _owner.EffectOverrides); // 디자인 테마 저장값 공급(호출 지점 1곳) — 요소 디자인 합성 전에
        ApplyMoveShadow(); // 사용자 값만 바뀌면 Resolver 서명이 같아 Changed 가 없으므로 여기서도 부른다
        using (UiSettings.Op("ui.apply.design")) { ThemeDesignApplier.Apply(app, XkxkUi.Current.Themes.Requested); }
        using (UiSettings.Op("ui.apply.style")) { DesignEffectsApplier.Apply(app); } // 디자인 테마 효과(REQ-104) — 색 토큰 반영 뒤, 서명이 같으면 생략

        // 문서 래퍼·간트 다시 그리기·그림자·VM 이 구독. 래퍼는 값이 같으면 건너뛰므로 테마만 바뀔 때는 거의 비용이 없다.
        // ui.apply.docscope(셸 분해) = theme.phase.changed(odev-7c 테마 분해와 같은 구간, 이름만 둘)
        using (UiSettings.Op("ui.apply.docscope"))
        using (UiSettings.Op("theme.phase.changed"))
        {
            _owner.RaiseChanged();
        }
    }

    /// <summary>
    /// 굵은 글자 굵기 결정(PdFontWeightStrong·PdFontWeightBold). 요청 굵기에 맞는 서체가 없으면 렌더러가 합성 굵게(FontSimulations.Bold — Skia Embolden)로
    /// 그리는데, 이 경로는 힌팅된 획을 잃어 번진다(8번 실측: 굴림 SemiBold 중간 회색 비율 0→0.49). 그래서 주어진 순서대로 실재 서체를 조회해
    /// 합성이 붙지 않는 첫 굵기를 쓰고, 모두 합성이면 Normal 을 쓴다. 폰트 이름으로 나누지 않는다.
    /// </summary>
    internal static FontWeight RealWeight(FontFamily family, params FontWeight[] order)
    {
        // 웹(브라우저)은 굵기 서체를 번들하지 않아 강등하면 강조가 통째로 사라진다. 흐림은 데스크톱 시스템 서체 문제였으므로 요청 굵기를 그대로 둔다(team-lead 결정).
        if (OperatingSystem.IsBrowser())
        {
            return order[0];
        }

        foreach (var weight in order)
        {
            if (!IsSynthesized(family, weight))
            {
                return weight;
            }
        }

        return FontWeight.Normal;
    }

    /// <summary>
    /// 이 굵기가 합성 굵게로 그려지는가. 조회 실패는 판단 근거가 없으므로 합성 아님으로 둔다(요청 굵기 그대로 = 기존 동작).
    /// 글꼴 캐시는 조회 순서의 영향을 받으므로 Normal 을 먼저 조회해 400 자리를 실제 Normal 서체로 채운다.
    /// FontManager.TryMatchCharacter 는 쓰지 않는다 — 플랫폼의 합성 Bold 서체를 굵기 400 으로 캐시해 그 뒤 Normal 글자까지 굵어진다(8번 실측: 굴림 획 화소 469→829).
    /// </summary>
    internal static bool IsSynthesized(FontFamily family, FontWeight weight)
    {
        var fonts = FontManager.Current;
        fonts.TryGetGlyphTypeface(new Typeface(family, FontStyle.Normal, FontWeight.Normal), out _);
        return fonts.TryGetGlyphTypeface(new Typeface(family, FontStyle.Normal, weight), out var face)
            && face.FontSimulations.HasFlag(FontSimulations.Bold);
    }

    /// <summary>
    /// 시스템(셸) 글자 크기 토큰(PdFontBase·PdFont* = 기준 + 오프셋, ControlContentThemeFontSize)과 제목 줄·제목 탭 높이를 앱 전역에 한 번에 쓴다.
    /// 문서 래퍼는 같은 키를 로컬로 덮으므로 문서 창 안은 문서 글자 크기를 유지한다. 값이 모두 같으면 쓰지 않는다(앱 전체 리소스 알림 회피).
    /// </summary>
    private void ApplyShell(Application app)
    {
        var size = _owner.ShellFontSize;
        var strip = _owner.ShellTabStripHeight;
        var items = new List<KeyValuePair<object, object?>>
        {
            new("PdFontBase", size),
            new("ControlContentThemeFontSize", size),
            new("PdTitleStripHeight", strip),
            new("PdTitleTabHeight", UiSettings.TitleTabHeightFor(strip)),
        };
        foreach (var (key, offset) in UiSettings.FontTokens)
        {
            items.Add(new(key, Math.Max(6, size + offset)));
        }

        if (app.Resources is not ResourceDictionary resources)
        {
            foreach (var kv in items) { app.Resources[kv.Key] = kv.Value; }
            return;
        }

        var changed = false;
        foreach (var kv in items)
        {
            if (!resources.TryGetValue(kv.Key, out var old) || !Equals(old, kv.Value)) { changed = true; break; }
        }

        if (changed)
        {
            resources.SetItems(items); // 하나씩 쓰면 키 수만큼 앱 전체 리소스 알림이 난다(perf15 C)
        }
    }

    /// <summary>색 테마 적용(라이트/다크 기반 RequestedThemeVariant 포함).</summary>
    private void ApplyColorTheme(Application app)
    {
        ThemeApplier.Apply(app, XkxkUi.Current.Themes.Requested);
    }

    /// <summary>
    /// 문서 래퍼 범위에만 글자 크기를 적용(메인 창 크롬은 App.axaml 고정 토큰 유지). 같은 토큰 이름을 래퍼 로컬 리소스로
    /// 재정의하므로 문서 안 DynamicResource 는 이 값을, 셸은 App 고정값을 잡는다. 상속 글자 크기도 래퍼에서 다시 설정한다.
    /// 성능(원인 ③): 키 7개를 하나씩 쓰면 래퍼 하위 트리 재평가가 7번 일어나므로, 새 사전을 만들어 MergedDictionaries 한 칸을
    /// 한 번에 교체한다(래퍼당 알림 1회). 값이 같으면 아무것도 하지 않는다.
    /// </summary>
    public void ApplyDocumentScope(StyledElement host)
    {
        // 요소별 디자인(REQ-101): 문서 래퍼 표식 클래스(문서 안 되돌림 셀렉터 대상) — 값 비교로 아래에서 돌아가기 전에 단다
        if (!host.Classes.Contains(ThemeElements.DocHostClass))
        {
            host.Classes.Add(ThemeElements.DocHostClass);
        }

        var size = _owner.BaseFontSize;
        var docFont = _owner.FontName;
        var merged = host.Resources.MergedDictionaries;
        if (merged.Count > 0 && merged[0] is ResourceDictionary current
            && current.TryGetValue("PdFontBase", out var v) && v is double d && d == size
            && current.TryGetValue(DocFontNameKey, out var n) && n is string s && string.Equals(s, docFont, StringComparison.Ordinal))
        {
            return;
        }

        // 문서 글꼴(FontName)도 래퍼 로컬로 덮는다 — 앱 전역 PdFontFamily 는 시스템 글꼴(ShellFontName)이다(2026-11-11, 01 §5.2)
        var family = UiSettings.FontResolver(docFont);
        var dict = new ResourceDictionary
        {
            ["PdFontBase"] = size, ["ControlContentThemeFontSize"] = size,
            ["PdFontFamily"] = family, ["ContentControlThemeFontFamily"] = family, [DocFontNameKey] = docFont,
        };
        foreach (var (key, offset) in UiSettings.FontTokens)
        {
            dict[key] = Math.Max(6, size + offset);
        }

        if (merged.Count == 0)
        {
            merged.Add(dict);
        }
        else
        {
            merged[0] = dict;
        }

        host.SetValue(TextElement.FontSizeProperty, size);
        host.SetValue(TextElement.FontFamilyProperty, family); // 상속 글꼴도 래퍼에서 다시 설정(창 TopLevel 은 시스템 글꼴)
    }

    /// <summary>래퍼 로컬 사전에 적용한 문서 글꼴 이름(같으면 다시 쓰지 않는 비교용).</summary>
    private const string DocFontNameKey = "PdDocFontName";
}
