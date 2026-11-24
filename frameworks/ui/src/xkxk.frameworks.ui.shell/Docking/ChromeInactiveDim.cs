// 비활성 창 제목줄 흐림 방식 옵션(opacity|brush) 한 곳 — 설정·원격·진단 인자가 모두 이 경로로 바꾼다(01 §4.2 N1, 화면 조작 없음)

namespace xkxk.frameworks.ui.shell.Docking;

/// <summary>
/// 비활성 MDI 창 제목줄 흐림 방식. opacity = 컨테이너 투명도(현재 기본 — 그릴 때마다 투명도 층이 생긴다), brush = 같은 알파의 색 브러시(층 없음, 겉모습 같게 설계).
/// 상태의 정본은 WindowChromeStyle.s_inactiveDimBrush 이고, 바꾸면 WindowChromeStyle.ApplyShadow 로 리소스·전역 스타일을 다시 건다.
/// 기본값은 측정 뒤 정한다(상한 측정에서 효과가 확인되면 01 §4.2 를 먼저 고치고 DefaultMode 만 brush 로 바꾼다).
/// </summary>
public static class ChromeInactiveDim
{
    public const string OpacityMode = "opacity";
    public const string BrushMode = "brush";

    /// <summary>기본 방식(현재 동작 그대로 opacity). 01_화면설계_공통원칙.md §4.2 와 같다.</summary>
    public const string DefaultMode = OpacityMode;

    /// <summary>지금 적용 중인 방식(opacity|brush).</summary>
    public static string Mode => WindowChromeStyle.s_inactiveDimBrush ? BrushMode : OpacityMode;

    /// <summary>입력을 정식 값으로 맞춘다(대소문자·공백 무시). 모르는 값이면 null.</summary>
    public static string? Normalize(string? mode) => mode?.Trim().ToLowerInvariant() switch
    {
        OpacityMode => OpacityMode,
        BrushMode => BrushMode,
        _ => null,
    };

    /// <summary>방식을 바꾼다. 모르는 값이면 바꾸지 않고 false. 앱이 아직 없으면 값만 기록되고 다음 크롬 적용 때 반영된다.</summary>
    public static bool Set(string? mode)
    {
        if (Normalize(mode) is not { } m)
        {
            return false;
        }

        var brush = m == BrushMode;
        if (WindowChromeStyle.s_inactiveDimBrush != brush)
        {
            WindowChromeStyle.s_inactiveDimBrush = brush;
            WindowChromeStyle.ApplyShadow();
        }

        return true;
    }
}
