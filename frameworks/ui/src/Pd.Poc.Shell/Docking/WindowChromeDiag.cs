// 제목줄 성능 분리용 진단 스위치(--chrome-buttons min|transparent|full, --chrome-inactive-dim brush|opacity, --chrome-inactive-opacity 0~1) — 진단 빌드 전용
#if PD_DIAGNOSTICS // 진단 빌드 전용(PdDiagnostics=true) — 제품 빌드에는 이 스위치가 없다
using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;

namespace Pd.Poc.Shell.Docking;

/// <summary>정적 상태만 가진다(K4 — 옛 WindowCommands.ChromeDiag). 흐림 상태·재적용은 WindowChromeStyle 의 internal 멤버를 쓴다.</summary>
[WindowCommandsPart]
internal static class WindowChromeDiag
{
    private static Style? s_chromeButtonsMinStyle;
    private static string s_chromeButtonsMode = "full";

    /// <summary>현재 제목줄 버튼 모드("full" 기본 | "min" | "transparent"). 계측 결과에 함께 기록한다.</summary>
    public static string ChromeButtonsMode => s_chromeButtonsMode;

    /// <summary>Q5 판별 조건 전체(계측 text "chrome_diag" 용): buttons=…;dim=opacity|brush;inactiveOpacity=default|값.</summary>
    public static string ChromeDiagState =>
        $"buttons={s_chromeButtonsMode};dim={(WindowChromeStyle.s_inactiveDimBrush ? "brush" : "opacity")};inactiveOpacity={(WindowChromeStyle.s_inactiveOpacityOverride is { } o ? o.ToString(System.Globalization.CultureInfo.InvariantCulture) : "default")}";

    /// <summary>--chrome-inactive-dim brush|opacity: 비활성 제목줄 흐림을 색 브러시(층 없음)로 할지 컨테이너 Opacity(기본)로 할지. 모르는 값이면 false.</summary>
    public static bool SetInactiveDimMode(string? mode) => ChromeInactiveDim.Set(mode); // 설정 ChromeInactiveDim 과 같은 경로(01 §4.2 N1)

    /// <summary>--chrome-inactive-opacity 0~1: 비활성 흐림 값 덮어쓰기(1 = 흐림 없음 → Opacity 방식에서 층이 사라짐). 범위 밖·숫자 아님이면 false.</summary>
    public static bool SetInactiveOpacity(string? value)
    {
        if (!double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) || v < 0 || v > 1)
        {
            return false;
        }

        WindowChromeStyle.s_inactiveOpacityOverride = v;
        WindowChromeStyle.ApplyShadow();
        return true;
    }

    /// <summary>
    /// "min" = 제목줄 기능 버튼(독립·항상 위·위치 고정·동기화·중복·새로고침·도움말) 숨김, 최소·최대·닫기만 남는다. "full" = 원래대로.
    /// 앱 전역 스타일 1개(ChromeButtons.IsVisible=false)를 넣고 빼는 방식이라 ChromeButtons·템플릿 파일은 바꾸지 않는다.
    /// 숨긴 컨트롤은 측정·배치·렌더에서 빠지지만, 문서 연결 구독(상태 갱신·잠금 끌기 차단)은 그대로 돈다 — 그 비용은 분리 대상이 아니다.
    /// </summary>
    /// <returns>알 수 없는 값이면 false(모드 유지).</returns>
    public static bool SetChromeButtonsMode(string? mode)
    {
        if (Application.Current is not { } app)
        {
            return false;
        }

        var m = mode?.Trim().ToLowerInvariant();
        if (m is not ("min" or "full" or "transparent"))
        {
            return false;
        }

        if (s_chromeButtonsMinStyle is { } old)
        {
            app.Styles.Remove(old);
            s_chromeButtonsMinStyle = null;
        }

        // min = 숨김(측정·배치·그리기 모두 제외), transparent = Opacity 0(배치는 남고 그리기만 제외) — 둘의 차이로 배치 몫과 그리기 몫을 가른다(Q5).
        if (m != "full")
        {
            s_chromeButtonsMinStyle = new Style(x => x.OfType(ViewRegistry.Current.ChromeButtonsType))
            {
                Setters = { m == "min" ? new Setter(Visual.IsVisibleProperty, false) : new Setter(Visual.OpacityProperty, 0.0) },
            };
            app.Styles.Add(s_chromeButtonsMinStyle);
        }

        s_chromeButtonsMode = m;
        return true;
    }
}
#endif
