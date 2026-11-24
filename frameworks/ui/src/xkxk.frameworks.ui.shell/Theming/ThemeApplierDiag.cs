// 테마 적용 회귀 분리용 진단 스위치(Q4·M3) — Fluent 키 3개 덮어쓰기·23:10 이후 추가 토큰 8개·Fluent 팔레트 교체를 끄고 켠다. 진단 빌드 전용
#if PD_DIAGNOSTICS
using System;
using System.Linq;
using Avalonia;
#endif

namespace xkxk.frameworks.ui.shell.Theming;

/// <summary>
/// ThemeApplier 의 진단 훅과 진단 스위치. 훅 3개(Filter·SkipPalette·PaletteAlways)는 Conditional 메서드라서
/// 제품 빌드에서는 ThemeApplier 의 호출 자체가 지워진다(예전 partial void 훅과 같은 성질 — SetBrush 경로에 분기 0건).
/// 나머지 멤버는 진단 빌드에만 있다.
/// </summary>
internal static class ThemeApplierDiag
{
    /// <summary>진단 빌드 전용 키 제외(Q4 회귀 분리).</summary>
    [System.Diagnostics.Conditional("PD_DIAGNOSTICS")]
    internal static void Filter(string key, ref bool skip)
    {
#if PD_DIAGNOSTICS
        skip = (s_noFluentOverride && Array.IndexOf(FluentOverrideKeys, key) >= 0)
            || (s_noNewTokens && Array.IndexOf(NewTokenKeys, key) >= 0);
#endif
    }

    /// <summary>진단 빌드 전용 Fluent 팔레트 교체 생략(M3 분리).</summary>
    [System.Diagnostics.Conditional("PD_DIAGNOSTICS")]
    internal static void SkipPalette(ref bool skip)
    {
#if PD_DIAGNOSTICS
        skip = s_noFluentPalette;
#endif
    }

    /// <summary>진단 빌드 전용: true 면 M4 이전 방식(매 적용 팔레트 교체·동적 브러시 동기화 없음) — 시각 회귀 대조용.</summary>
    [System.Diagnostics.Conditional("PD_DIAGNOSTICS")]
    internal static void PaletteAlways(ref bool always)
    {
#if PD_DIAGNOSTICS
        always |= s_paletteAlways;
#endif
    }
#if PD_DIAGNOSTICS

    /// <summary>App.Resources 에 넣어 Fluent 테마 사전을 덮는 Fluent 기본 컨트롤 키(DockMap 의 ButtonBackground·ButtonBorderBrush·TextControlBorderBrush 3개, 창 배경 키는 제외).</summary>
    private static readonly string[] FluentOverrideKeys = { "ButtonBackground", "ButtonBorderBrush", "TextControlBorderBrush" };

    /// <summary>23:10 판 이후 추가한 토큰 8개(47 → 55).</summary>
    private static readonly string[] NewTokenKeys =
    {
        "PdBrushMdiBackground", "PdBrushMenuBar", "PdBrushToolBar", "PdBrushGridHeaderLine",
        "PdBrushSelectionLine", "PdBrushButton", "PdBrushButtonBorder", "PdBrushInputBorder",
    };

    private static bool s_noFluentOverride;
    private static bool s_noNewTokens;
    private static bool s_noFluentPalette;
    private static bool s_paletteAlways;

    /// <summary>계측 기록용: "fluent=on|off;newTokens=on|off;fluent_palette=on|off".</summary>
    internal static string DiagnosticOverrides =>
        $"fluent={(s_noFluentOverride ? "off" : "on")};newTokens={(s_noNewTokens ? "off" : "on")};fluent_palette={(s_noFluentPalette ? "off" : s_paletteAlways ? "always" : "on")}";

    /// <summary>
    /// --fluent-palette on|off(M3): off 면 테마 적용 때 Fluent 팔레트(14색 ColorPaletteResources) 교체를 건너뛴다 — theme.apply 비용에서 팔레트 몫을 가른다.
    /// 이미 붙은 팔레트는 떼지 않는다(떼는 순간 알림이 나 측정을 흐림) → off 동안 Fluent 기본 컨트롤 색은 마지막으로 붙은 팔레트 색에 머문다.
    /// 측정 시작 전에 1회 부를 것. 현재 테마를 다시 적용한다.
    /// </summary>
    internal static void SetFluentPalette(bool on) => SetFluentPaletteMode(on ? "on" : "off");

    /// <summary>
    /// --fluent-palette on|off|always(M4): on = M4 기본(변형 바뀔 때만 팔레트 교체 + 동적 브러시 85개 Color 동기화), off = 팔레트 교체·동기화 안 함,
    /// always = M4 이전 방식(매 적용 교체, 동기화 없음). 비교는 기동 인자로 프로세스를 따로 띄워서 할 것 — 실행 중 on→always 로 바꾸면
    /// 이미 동기화로 값을 넣은 브러시는 팔레트 바인딩이 풀려 있을 수 있다(미확인). 모르는 값이면 false.
    /// </summary>
    internal static bool SetFluentPaletteMode(string? mode)
    {
        switch (mode?.Trim().ToLowerInvariant())
        {
            case "on": s_noFluentPalette = false; s_paletteAlways = false; break;
            case "off" or "0" or "false": s_noFluentPalette = true; s_paletteAlways = false; break;
            case "always": s_noFluentPalette = false; s_paletteAlways = true; break;
            default: return false;
        }

        if (Application.Current is not { } app)
        {
            return true;
        }

        ThemeApplier.ResetSignature();
        if (ThemeApplier.Active is { } theme)
        {
            ThemeApplier.Apply(app, theme);
        }

        return true;
    }

    /// <summary>
    /// M4 ② 기계 점검: 현재 변형의 Fluent 테마 사전을 전부 만들어 보고 분류한다. 결과는 perf text "fluent_census" 와 반환값.
    /// keys=사전 키 수, table=동기화 표 수, found=표 중 실제 SolidColorBrush, mismatch=표 브러시 중 현재 색 ≠ 기대 색(참조 팔레트),
    /// static=표 밖 SolidColorBrush(StaticResource 고정 — 팔레트와 무관), other=브러시가 아닌 값·별칭.
    /// bound_check=unsupported: 바인딩 여부 판별은 할 수 없다. Avalonia 12.1.4 GetDiagnostic 은 Diagnostic 문자열을 항상 null 로 돌려주고(ValueStore.GetDiagnostic 디컴파일 확인),
    /// 공개 API 에 바인딩/직접 값 구분이 없다(i1113 실측 table_bound=0·0 으로 판별식이 죽어 있음을 확인해 제거). '동기화가 빠진 동적 키 0' 의 근거는
    /// 디컴파일 정적 대조(동적 색 참조 85건 = 표 41+44)와 화소 비교(on/always 차이 0)다. 사전 값을 모두 만들므로(지연 생성 해제) 측정 실행 중에는 부르지 말 것.
    /// </summary>
    internal static string FluentSyncCensus()
    {
        if (Application.Current is not { } app || app.Styles.OfType<Avalonia.Themes.Fluent.FluentTheme>().FirstOrDefault() is not { } fluent || ThemeApplier.Active is not { } theme)
        {
            return "fluent_census=unavailable";
        }

        var variant = app.ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark ? Avalonia.Styling.ThemeVariant.Dark : Avalonia.Styling.ThemeVariant.Light;
        var dictKey = variant == Avalonia.Styling.ThemeVariant.Dark ? Avalonia.Styling.ThemeVariant.Dark : Avalonia.Styling.ThemeVariant.Default;
        if (!fluent.Resources.ThemeDictionaries.TryGetValue(dictKey, out var provider) || provider is not Avalonia.Controls.ResourceDictionary dict)
        {
            return "fluent_census=nodict";
        }

        var table = FluentDynamicSync.DynamicKeys(variant);
        var tableKeys = new System.Collections.Generic.HashSet<string>(table.Select(e => e.Key), StringComparer.Ordinal);
        var reference = ThemeApplier.FillPalette(new Avalonia.Themes.Fluent.ColorPaletteResources(), theme, theme.Base == ThemeBase.Dark ? Avalonia.Media.Colors.White : Avalonia.Media.Colors.Black);
        int keys = 0, found = 0, mismatch = 0, stat = 0, other = 0;
        foreach (var k in dict.Keys.ToArray())
        {
            keys++;
            var value = dict[k];
            if (value is not Avalonia.Media.SolidColorBrush brush)
            {
                other++;
                continue;
            }

            if (k is string s && tableKeys.Contains(s))
            {
                found++;
                var colorKey = table.First(e => e.Key == s).ColorKey;
                if ((reference.TryGetResource(colorKey, variant, out var c) || fluent.TryGetResource(colorKey, variant, out c)) && c is Avalonia.Media.Color expected && brush.Color != expected)
                {
                    mismatch++;
                }

                continue;
            }

            stat++;
        }

        var text = $"variant={variant};keys={keys};table={table.Length};found={found};mismatch={mismatch};static={stat};other={other};bound_check=unsupported";
        xkxk.frameworks.ui.shell.Diagnostics.PerfProbe.Current.SetText("fluent_census", text);
        return text;
    }

    /// <summary>
    /// fluentOverride=false: Fluent 키 3개를 App.Resources 에 넣지 않는다(Fluent 테마 사전 값 사용).
    /// newTokens=false: 추가 토큰 8개(브러시·Color 짝)를 넣지 않는다(참조처는 ChromeResources 대체값 또는 미해결).
    /// 이미 들어간 키는 지우고 현재 테마를 다시 적용한다 — 측정 시작 전에 1회 부를 것(지우는 순간 리소스 알림이 난다).
    /// </summary>
    internal static void SetDiagnosticOverrides(bool fluentOverride, bool newTokens)
    {
        s_noFluentOverride = !fluentOverride;
        s_noNewTokens = !newTokens;
        if (Application.Current is not { } app)
        {
            return;
        }

        foreach (var key in Skipped())
        {
            ThemeApplier.ForgetBrush(key);
            app.Resources.Remove(key);
        }

        ThemeApplier.ResetSignature(); // 같은 테마라도 다시 칠한다
        if (ThemeApplier.Active is { } theme)
        {
            ThemeApplier.Apply(app, theme);
        }
    }

    private static string[] Skipped() =>
        [.. s_noFluentOverride ? FluentOverrideKeys : Array.Empty<string>(), .. s_noNewTokens ? NewTokenKeys : Array.Empty<string>()];
#endif
}
