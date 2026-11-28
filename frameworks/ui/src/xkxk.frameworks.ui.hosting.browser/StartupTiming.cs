// 웹 시작 구간 계측의 C# 쪽 — 앱이 첫 프레임을 그린 시점을 main.js 의 __pdStartupMark 로 알린다(호스트 층, UI 코드와 분리)
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;

namespace xkxk.frameworks.ui.hosting.browser;

/// <summary>
/// 시작 구간 표식. 기록 여부(설정 pd-startup-options.timing)는 main.js 가 판정하고, 이 클래스는 시점만 알린다.
/// first-frame 은 로딩 문구를 걷는 신호이기도 해서 기록이 꺼져 있어도 항상 부른다.
/// </summary>
[SupportedOSPlatform("browser")]
internal static partial class StartupTiming
{
    /// <summary>MainView 가 아직 없을 때 다시 볼 최대 횟수(Background 우선순위 Post 1회 = 1번).</summary>
    private const int MaxViewWait = 120;

    [JSImport("globalThis.__pdStartupMark")]
    private static partial void Mark(string name);

    /// <summary>JS 표식. main.js 가 정의하지 않은 상태(다른 진입 페이지 등)여도 기동을 막지 않는다.</summary>
    public static void TryMark(string name)
    {
        try { Mark(name); }
        catch (JSException) { /* 표식 실패는 무시 */ }
    }

    /// <summary>
    /// 첫 프레임 표식: MainView 의 TopLevel 에서 프레임 콜백 2번 뒤(그 프레임이 화면에 반영된 뒤)에 first-frame 을 남긴다.
    /// 같은 이중 프레임 방식은 셸 PerfOpenTracker 의 창 열림 측정과 같다.
    /// </summary>
    public static void MarkFirstFrame() => Dispatcher.UIThread.Post(() => WaitView(0), DispatcherPriority.Background);

    private static void WaitView(int tries)
    {
        var view = (Application.Current?.ApplicationLifetime as ISingleViewApplicationLifetime)?.MainView;
        if (view is not null && TopLevel.GetTopLevel(view) is { } top)
        {
            top.RequestAnimationFrame(_ => top.RequestAnimationFrame(_ => TryMark("first-frame")));
            return;
        }

        if (tries >= MaxViewWait)
        {
            TryMark("first-frame-fallback"); // 화면 판정 실패 — 로딩 문구는 걷되, 측정값으로 쓰지 않게 이름을 달리한다
            return;
        }

        Dispatcher.UIThread.Post(() => WaitView(tries + 1), DispatcherPriority.Background);
    }
}
