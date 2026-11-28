// 브라우저 호스트의 JS 상호운용 — 미디어 쿼리·기기 정보·렌더러 판정·임베드 글꼴 경로·앱 빌더 기본 구성
using System.Runtime.InteropServices.JavaScript;
using Avalonia;
using Avalonia.Media;

namespace xkxk.frameworks.ui.hosting.browser;

public static partial class BrowserHost
{
    /// <summary>UI 설정 localStorage 키(테마·글자·언어·시스템 메뉴 설정).</summary>
    private const string SettingsKey = "pd-poc-settings";

    /// <summary>시험 강제 인자(on|off, 값 생략 시 on). 사용자 OS 설정은 바꾸지 않는다.</summary>
    private const string ReducedMotionArg = "--reduced-motion";

    [JSImport("globalThis.location.reload")]
    private static partial void Reload();

    [JSImport("globalThis.matchMedia")]
    private static partial JSObject MatchMedia(string query);

    /// <summary>주 포인터가 거친 포인터(터치)인지 — CSS 미디어 기능 pointer: coarse(MDN). 질의 실패·matches 가 불리언이 아니면(undefined 등) null.</summary>
    private static bool? IsCoarsePointer()
    {
        try
        {
            using var mql = MatchMedia("(pointer: coarse)");
            return mql is not null && mql.GetTypeOfProperty("matches") == "boolean" ? mql.GetPropertyAsBoolean("matches") : null;
        }
        catch (JSException)
        {
            return null;
        }
    }

    /// <summary>미디어 쿼리 결과(matches). 질의 실패·불리언이 아니면 null.</summary>
    private static bool? QueryMatches(string query)
    {
        try
        {
            using var mql = MatchMedia(query);
            return mql is not null && mql.GetTypeOfProperty("matches") == "boolean" ? mql.GetPropertyAsBoolean("matches") : null;
        }
        catch (JSException)
        {
            return null;
        }
    }

    /// <summary>
    /// 브라우저가 알려 주는 기기 정보를 DeviceTraits 로 모은다. 근거 MDN pointer·any-pointer·hover 미디어 쿼리
    /// (https://developer.mozilla.org/docs/Web/CSS/@media/any-pointer). 모르는 값은 null 로 둔다.
    /// </summary>
    private static xkxk.frameworks.ui.shell.Platform.DeviceTraits CollectTraits(bool? primaryCoarse)
    {
        // HasTouch: 기존 판정(주 포인터 coarse)이 참이면 터치. 아니면 any-pointer: coarse, 마지막 보조로 maxTouchPoints.
        bool? hasTouch = primaryCoarse;
        if (hasTouch != true)
        {
            var anyCoarse = QueryMatches("(any-pointer: coarse)");
            var touchPoints = ReadDouble(JSHost.GlobalThis.GetPropertyAsJSObject("navigator"), "maxTouchPoints");
            hasTouch = anyCoarse == true || touchPoints > 0 ? true : (primaryCoarse ?? anyCoarse ?? (touchPoints is null ? null : false));
        }

        double? smallest = null;
        try
        {
            using var screen = JSHost.GlobalThis.GetPropertyAsJSObject("screen");
            var w = ReadDouble(screen, "width");
            var h = ReadDouble(screen, "height");
            if (w is > 0 && h is > 0)
            {
                smallest = Math.Min(w.Value, h.Value); // CSS px 로 dp 근사(Android smallestScreenWidthDp 와 같은 단위 관례)
            }
        }
        catch (JSException)
        {
        }

        var inner = ReadDouble(JSHost.GlobalThis, "innerWidth");
        return new xkxk.frameworks.ui.shell.Platform.DeviceTraits(
            xkxk.frameworks.ui.shell.Platform.PlatformKind.Browser,
            HasTouch: hasTouch,
            PrecisePointer: QueryMatches("(any-pointer: fine)"),
            HoverCapable: QueryMatches("(hover: hover)"),
            SmallestWidthDp: smallest,
            WindowWidthDp: inner is > 0 ? inner : null);
    }

    /// <summary>JS 객체의 숫자 속성. 없거나 숫자가 아니면 null.</summary>
    private static double? ReadDouble(JSObject? obj, string name)
    {
        try
        {
            return obj is not null && obj.GetTypeOfProperty(name) == "number" ? obj.GetPropertyAsDouble(name) : null;
        }
        catch (JSException)
        {
            return null;
        }
    }

    /// <summary>CSS 미디어 기능 prefers-reduced-motion: reduce 가 참인지(MDN https://developer.mozilla.org/docs/Web/CSS/@media/prefers-reduced-motion). 질의 실패면 false.</summary>
    private static bool IsReducedMotion()
    {
        try
        {
            using var mql = MatchMedia("(prefers-reduced-motion: reduce)");
            return mql is not null && mql.GetTypeOfProperty("matches") == "boolean" && mql.GetPropertyAsBoolean("matches");
        }
        catch (JSException)
        {
            return false;
        }
    }

    /// <summary>main.js __pdRenderer: 캔버스가 쓰는 컨텍스트 "webgl2"|"webgl1"|"2d"|"none".</summary>
    [JSImport("globalThis.__pdRenderer")]
    private static partial string GetRenderer();

    // 임베드 한글 폰트(NotoSansKR-Regular-subset.ttf, 패밀리명 "Noto Sans KR"). 기본은 글꼴 전용 어셈블리 xkxk.frameworks.ui.fonts(-p:PdFontAssembly=false 면 실행 어셈블리 xkxk.frameworks.ui.browser, B11-3 에서 이름 전환)
#if PD_FONT_ASSEMBLY
    private const string KoreanFont = "avares://xkxk.frameworks.ui.fonts/Assets/Fonts#Noto Sans KR";
#else
    private const string KoreanFont = "avares://xkxk.frameworks.ui.browser/Assets/Fonts#Noto Sans KR";
#endif

    private static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<xkxk.frameworks.ui.views.PdApp>()
            .With(new FontManagerOptions
            {
                DefaultFamilyName = KoreanFont,
                FontFallbacks = [new FontFallback { FontFamily = new FontFamily(KoreanFont) }],
            });
}
