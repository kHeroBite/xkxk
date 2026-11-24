// 웹 시스템 '움직임 줄이기'(prefers-reduced-motion) 값을 읽어 MotionPreference.SystemReduced 에 시작 때와 변경 때 반영하는 호스트 연결
using System;
using System.Runtime.InteropServices.JavaScript;
using xkxk.frameworks.ui.shell.Theming;

namespace xkxk.frameworks.ui.browser;

internal static partial class BrowserMotionPreference
{
    /// <summary>host_bridge.js __pdReducedMotion: 미디어 쿼리 결과. 미지원·예외면 false.</summary>
    [JSImport("globalThis.__pdReducedMotion")]
    private static partial bool ReadReducedMotion();

    /// <summary>host_bridge.js __pdReducedMotionWatch: 쿼리 change 때 새 값을 handler 로 넘긴다.</summary>
    [JSImport("globalThis.__pdReducedMotionWatch")]
    private static partial void Watch([JSMarshalAs<JSType.Function<JSType.Boolean>>] Action<bool> handler);

    /// <summary>시작 때 한 번 읽어 넣고 변경을 구독한다. JS 쪽 실패는 false(애니메이션 사용)로 처리한다.</summary>
    internal static void Install()
    {
        try
        {
            MotionPreference.SystemReduced = ReadReducedMotion();
            Watch(reduced => MotionPreference.SystemReduced = reduced);
        }
        catch (JSException)
        {
            MotionPreference.SystemReduced = false;
        }
    }
}
