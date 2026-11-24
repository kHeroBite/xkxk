// 문서 창 열기 시작부(DepthTransitions 의 partial, 300줄 규칙으로 분리) — 처음 붙는 창에서만 창 움직임 재생기에 열기를 넘기고, 설정 키 depth.deferBegin 이면 다음 프레임에 넘긴다
using Avalonia.Controls;
using Avalonia.Interactivity;
using Dock.Avalonia.Controls;
using xkxk.frameworks.ui.shell.Theming;

namespace xkxk.frameworks.ui.views.Depth;

public static partial class DepthTransitions
{
    private static void OnWindowLoaded(MdiDocumentWindow window, RoutedEventArgs e)
    {
        // 같은 문서(또는 묶음) 창이 다시 붙는 경우(레이아웃 재구성·캐시 복귀)는 열기가 아니다 — 처음 붙을 때만
        if (window.DataContext is not { } context || s_seenWindows.TryGetValue(context, out _))
        {
            return;
        }

        s_seenWindows.Add(context, s_marker);

        // 열기 효과(기울기 tilt3d 포함 motion.open 전 효과)의 선택·폴백·배치 낮춤·재생은 창 움직임 재생기가 맡는다(REQ-112). 효과가 none 이면 재생기가 아무것도 하지 않는다
        if (DepthDiag.DeferBegin && TopLevel.GetTopLevel(window) is { } top)
        {
            // 설정 키 depth.deferBegin(K13 수정 후보 2·H4 진단, 기본 끔): 열기 동기 구간 밖(다음 렌더 프레임)에서 효과를 건다 — 첫 프레임은 효과 없이 그려질 수 있다
            top.RequestAnimationFrame(_ =>
            {
                // 빠른 열고 닫기: 다음 프레임 전에 창이 시각 트리에서 떨어졌으면 아무것도 하지 않는다
                if (TopLevel.GetTopLevel(window) is null) // 12.1.4 에는 GetVisualRoot 확장이 없다(PinchZoom 과 같은 공개 API 판정)
                {
                    return;
                }

                WindowTransitions.OnWindowOpened(window);
            });
            return;
        }

        WindowTransitions.OnWindowOpened(window);
    }
}
