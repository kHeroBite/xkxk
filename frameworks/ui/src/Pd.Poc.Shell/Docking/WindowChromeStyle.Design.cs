// 문서 창 크롬의 디자인 테마 그림자 공급·그림자 2키 값 제공·설정 변경 때 입력이 같으면 다시 적용 생략(K13 — WindowChromeStyle 300줄 상한으로 나눈 부분)
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Pd.Poc.Shell.Theming;

namespace Pd.Poc.Shell.Docking;

internal static partial class WindowChromeStyle
{
    private static string? s_appliedInputs; // 마지막으로 크롬 리소스를 다시 건 때의 입력 서명(설정 변경 알림 때 비교)

    /// <summary>디자인 테마 그림자 덮어쓰기 공급자(REQ-104, DesignEffectsApplier 가 등록). null 이면 크롬 스타일 그림자. 그림자를 끄거나 Mars 클래식이면 'none'(0겹)만 따른다.</summary>
    public static Func<BoxShadows?>? DesignShadowOverride { get; set; }

    /// <summary>
    /// 문서 창 그림자 2키 값(디자인 테마 그림자 반영). DesignEffectsApplier 가 효과 리소스와 같은 SetItems 1회로 쓰고 NotifyShadowApplied 를 부른다
    /// (그림자만 따로 쓰면 앱 전체 리소스 알림이 1회 더 난다, K13).
    /// </summary>
    public static IEnumerable<KeyValuePair<object, object?>> ShadowResources(Application app) =>
        ShadowPair(app, EffectiveStyle == ChromeStyleKind.MarsClassic).Select(kv => new KeyValuePair<object, object?>(kv.Key, kv.Value));

    /// <summary>ShadowResources 를 쓴 뒤 그림자 구독자(SDI 그림자 등)에게 알린다.</summary>
    public static void NotifyShadowApplied() => Applied?.Invoke();

    /// <summary>디자인 테마 그림자가 있으면 크롬 값 대신 쓴다. 비활성 = 가까운 겹 1개(크롬 스타일과 같은 원칙). 그림자 꺼짐·Mars 클래식이면 0겹만 따른다.</summary>
    private static void WithDesignShadow(bool on, ref BoxShadows active, ref BoxShadows inactive)
    {
        if (DesignShadowOverride?.Invoke() is { } s && (on || s.Count == 0))
        {
            active = s;
            inactive = s.Count > 0 ? new BoxShadows(s[0]) : s;
        }
    }

    /// <summary>
    /// UiSettings.Changed 처리기. 이 알림은 디자인 테마·움직임·문서 글자 크기처럼 크롬과 무관한 변경에도 오므로, 크롬 리소스가 읽는 입력
    /// (실효 스타일·라이트/다크·색 테마·창 그림자 색·기준 글자 크기·터치 여부)이 그대로면 다시 걸지 않는다(headless 웹 디자인 테마 전환 26~30ms, K13).
    /// 크롬 옵션 setter·강제 지정은 ApplyShadow 를 직접 부르므로 생략되지 않는다. 비활성 흐림 브러시 방식은 원본 색을 복사해 두므로 늘 다시 건다.
    /// </summary>
    private static void OnSettingsChanged()
    {
        if (Application.Current is not { } app)
        {
            return;
        }

        if (!s_inactiveDimBrush && string.Equals(Inputs(app), s_appliedInputs, StringComparison.Ordinal))
        {
            return;
        }

        ApplyShadow();
    }

    /// <summary>
    /// 크롬 리소스 입력 서명. 색 별칭은 ThemeApplier 가 브러시 인스턴스를 유지하고 색만 바꾸므로 테마 Id·라이트/다크·브러시 세대(인스턴스가 새로 생기거나 잊힘)만 넣고,
    /// 그림자는 색을 복사하므로 색을 넣는다.
    /// </summary>
    private static string Inputs(Application app)
    {
        var shadow = app.TryGetResource("PdBrushWindowShadow", app.ActualThemeVariant, out var res) && res is ISolidColorBrush b ? b.Color.ToString() : "-";
        return string.Join("|", EffectiveStyle, app.ActualThemeVariant, ThemeCatalog.Current.Requested.Id, ThemeApplier.BrushGeneration, shadow,
            UiSettings.Current.BaseFontSize, Pd.Poc.Shell.Platform.InputFormFactor.IsTouchDevice);
    }

    /// <summary>바뀐 키만 한 번에 쓴다(키마다 쓰면 키 수만큼 앱 전체 리소스 알림 — perf15 C). 같으면 쓰지 않는다.</summary>
    private static void SetMany(Application app, params (string Key, object Value)[] items)
    {
        var changed = items.Where(kv => !app.Resources.TryGetValue(kv.Key, out var old) || !Equals(old, kv.Value))
            .Select(kv => new KeyValuePair<object, object?>(kv.Key, kv.Value)).ToList();
        if (changed.Count == 0)
        {
            return;
        }

        if (app.Resources is ResourceDictionary resources)
        {
            resources.SetItems(changed);
            return;
        }

        foreach (var kv in changed)
        {
            app.Resources[kv.Key] = kv.Value;
        }
    }
}
