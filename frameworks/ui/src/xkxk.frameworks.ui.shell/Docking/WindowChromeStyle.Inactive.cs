// 비활성 문서 창 제목줄 별칭(WindowChromeStyle 에서 분리 — 300줄 상한)
using Avalonia;

namespace xkxk.frameworks.ui.shell.Docking;

internal static partial class WindowChromeStyle
{
    /// <summary>비활성 제목줄 별칭(Themes/ChromeInactive.axaml 의 :not(:active) 전역 스타일이 참조)과 비활성 호버 별칭.</summary>
    private static void ApplyInactiveAliases(Application app, bool mars, bool accent, double inactive)
    {
        // 비활성 제목줄 별칭(Themes/ChromeInactive.axaml 의 :not(:active) 전역 스타일이 참조). Opacity 방식이면 원래 브러시 그대로(f=1),
        // 브러시 방식이면 같은 알파를 곱한 캐시 브러시 — 컨테이너 Opacity 와 같은 색이 되도록 글자·글리프·켜짐 색·hover/누름/닫기 바탕을 모두 덮는다(Q5 A~D).
        var f = s_inactiveDimBrush ? inactive : 1.0;
        DimAlias(app, "PdChromeTitleTextInactive", accent ? "PdBrushMdiTitleText" : "PdBrushText", f);
        DimAlias(app, "PdChromeTitleTextPinnedInactive", "PdBrushMdiTitleText", f);
        DimAlias(app, "PdChromeGlyphInactive", mars ? "PdBrushChromeGlyph" : accent ? "PdBrushMdiTitleText" : "PdBrushText", f);
        DimAlias(app, "PdChromePinOnInactive", "PdBrushChromePinOn", f);
        DimAlias(app, "PdChromeLockOnInactive", "PdBrushChromeLockOn", f);
        // U6: hover·누름은 바탕 없이 기호 색 — 같은 알파로 흐린 기호 색(색은 CaptionLayout 이 고른 상태 색 원본)
        var on = accent ? "OnAccent" : "OnSurface";
        DimAlias(app, "PdChromeGlyphHoverInactive", "PdBrushChromeGlyphHover" + on, f);
        DimAlias(app, "PdChromeGlyphPressedInactive", "PdBrushChromeGlyphPressed" + on, f);
        DimAlias(app, "PdChromeCloseGlyphHoverInactive", "PdBrushChromeCloseGlyphHover" + on, f);
        DimAlias(app, "PdChromeCloseGlyphPressedInactive", "PdBrushChromeCloseGlyphPressed" + on, f);
        Set(app, "PdChromeInactiveGlyphOpacity", f); // 코드가 색을 직접 넣는 캐시 아이콘만 아이콘 투명도로 흐림
        // 비활성 창 호버 전용(이월1) — 진한 띠 제목줄은 테마 토큰(회색 #A0A0A0 위 대비 ≥3.0), 표면색 제목줄은 기존 호버 그대로
        Alias(app, "PdChromeHoverInactive", accent ? "PdBrushChromeHoverInactive" : "PdBrushCaptionHover");
        SyncInactiveStyles(app);
    }
}
