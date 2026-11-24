// SDI 그림자 모양(진하기·감쇠·여백·아래 이동) 값과 앱 리소스 읽기 — 켜짐 판정·활성/비활성 모양·모서리 반경을 한 곳에 모은다
using Avalonia;
using Avalonia.Media;

namespace Pd.Poc.Desktop;

/// <summary>
/// 그림자 모양(PD 설계값, 공식 수치 없음 — 05_창크롬_OS디자인_기준.md). Win11 메모장 실측(q2_16 q16_profile.json: 활성 왼쪽 감소 43→13.5,
/// 비활성 31.7→4.3, 아래가 더 넓음)에서 DWM 기본 약한 그림자(Pd 111.5)를 뺀 나머지를 지수 감쇠로 근사했다.
/// 값은 96DPI 기준 px 이고 배율을 곱해 쓴다. 실제 값은 앱 리소스 PdSdiShadowActive/Inactive(ChromeResources.axaml)이고, 아래 상수는 리소스가 없을 때의 같은 값 기본이다.
/// </summary>
internal readonly record struct SdiShadowShape(double Peak, double Tau, double Extent, double OffsetY)
{
    public static readonly SdiShadowShape Active = new(0.26, 15.0, 40, 3);
    public static readonly SdiShadowShape Inactive = new(0.15, 7.5, 24, 2);

    /// <summary>창 바깥 모서리 반경 기본(C3 정합 — 리소스 PdChromeCorner 가 없을 때. Win11 공식 창 모서리 8px).</summary>
    public const double DefaultOuterRadius = 8;

    /// <summary>그림자 켜짐 = 크롬 스타일이 문서 창 그림자를 쓰는 상태(WindowChromeStyle 이 꺼짐·Mars 클래식이면 PdMdiShadow 를 비운다).</summary>
    public static bool IsOn()
    {
        var app = Application.Current;
        return app is not null && app.TryGetResource("PdMdiShadow", app.ActualThemeVariant, out var v) && v is BoxShadows { Count: > 0 };
    }

    public static SdiShadowShape ReadActive() => Read("PdSdiShadowActive", Active);

    public static SdiShadowShape ReadInactive() => Read("PdSdiShadowInactive", Inactive);

    /// <summary>창 바깥 모서리 반경(96DPI px). 그림자 마스크 모서리 = 창 바깥 반경. 리소스 PdChromeCorner(CornerRadius)의 왼쪽 위 값이고, 없으면 기본.</summary>
    public static double ReadOuterRadius()
    {
        var app = Application.Current;
        return app is not null && app.TryGetResource("PdChromeCorner", app.ActualThemeVariant, out var v) && v is CornerRadius c ? c.TopLeft : DefaultOuterRadius;
    }

    // 그림자 모양 = 앱 리소스(PdMdiShadow 와 같은 읽기 방식). 없거나 형식 오류면 같은 값의 기본 모양.
    private static SdiShadowShape Read(string key, SdiShadowShape fallback)
    {
        var app = Application.Current;
        return app is not null && app.TryGetResource(key, app.ActualThemeVariant, out var v) ? Parse(v as string, fallback) : fallback;
    }

    /// <summary>리소스 문자열 "진하기,τ,여백,아래 이동"(InvariantCulture)을 읽는다. 형식이 틀리면 fallback.</summary>
    public static SdiShadowShape Parse(string? text, SdiShadowShape fallback)
    {
        var parts = text?.Split(',', StringSplitOptions.TrimEntries);
        if (parts is not { Length: 4 })
        {
            return fallback;
        }

        var v = new double[4];
        for (var i = 0; i < 4; i++)
        {
            if (!double.TryParse(parts[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v[i]) || v[i] < 0)
            {
                return fallback;
            }
        }

        return v[2] < 1 ? fallback : new SdiShadowShape(Math.Min(v[0], 1), Math.Max(v[1], 0.1), v[2], v[3]);
    }
}
