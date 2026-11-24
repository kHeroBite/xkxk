// 최대화 단추 호버의 포인터 판정 — TopLevel 포인터 이동마다 포인터 아래의 등록 단추·팝업 여부를 계산해 진입·이탈·유예를 만든다(웹 실마우스에서 단추 PointerEntered 로 호버가 시작되지 않은 AC-025 FAIL 대응)
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using Pd.Poc.Shell.Docking;

namespace Pd.Ui.Views.Snap;

/// <summary>
/// 판정: 단추 사각형(TopLevel 좌표) 안 + 그 자리에서 맨 위로 맞는 요소가 같은 창(MDI 창·SDI 제목줄·메인 제목줄) 소속이면 '단추 위'.
/// 다른 창이 단추를 덮고 있으면 단추 위가 아니다. 팝업 틀 안이면 '팝업 위'. 데스크톱 팝업은 별도 TopLevel 이라 그쪽 이동으로 판정된다.
/// 끌기 중(왼쪽 단추 누른 채 캡처 중)과 터치는 판정하지 않는다. 원격 hover(s_simulated)는 실제 포인터 이동으로 취소되지 않는다.
/// </summary>
public static partial class SnapFlyoutHover
{
    private static Button? s_hovered;
    private static bool s_inPopup;
    private static bool s_simulated;

    private static void InstallPointer()
    {
        InputElement.PointerMovedEvent.AddClassHandler<TopLevel>(OnTopMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        // 창(TopLevel) 밖으로 나감 = 단추·팝업 둘 다 아님(데스크톱 팝업 창으로 넘어가면 그쪽 첫 이동이 곧 '팝업 위' 로 되돌린다)
        InputElement.PointerExitedEvent.AddClassHandler<TopLevel>((top, e) => { if (ReferenceEquals(e.Source, top)) { Track(null, false); } },
            RoutingStrategies.Direct, handledEventsToo: true);
    }

    private static void OnTopMoved(TopLevel top, PointerEventArgs e)
    {
        SnapFlyoutState.PointerMoves++;
        if (e.Pointer.Type == PointerType.Touch)
        {
            SnapFlyoutState.LastHover = "touch";
            Track(null, false);
            return;
        }

        if (e.Pointer.Captured is not null && e.GetCurrentPoint(top).Properties.IsLeftButtonPressed)
        {
            return; // 끌기 중
        }

        var pos = e.GetPosition(top);
        var inPopup = s_presenter is Visual pv && ReferenceEquals(TopLevel.GetTopLevel(pv), top) && Contains(pv, top, pos);
        Track(inPopup ? null : ButtonAt(top, pos), inPopup);
    }

    /// <summary>포인터 아래 상태가 바뀌었을 때 진입·이탈·유예를 정한다.</summary>
    private static void Track(Button? b, bool inPopup)
    {
        var prev = s_hovered;
        s_hovered = b;
        s_inPopup = inPopup;
        if (b is not null || inPopup)
        {
            s_grace.Stop();
        }

        if (b is not null && !ReferenceEquals(b, prev))
        {
            s_simulated = false;
            SnapFlyoutState.LastHover = "enter";
            if (!(SnapFlyoutState.IsOpen && ReferenceEquals(b, s_button)))
            {
                Start(b);
            }

            return;
        }

        if (b is null && prev is not null)
        {
            SnapFlyoutState.LastHover = inPopup ? "to-popup" : "leave";
            if (!SnapFlyoutState.IsOpen)
            {
                CancelPending("left-before-delay");
                return;
            }
        }

        if (b is null && !inPopup && SnapFlyoutState.IsOpen && !s_simulated && !s_grace.IsEnabled)
        {
            s_grace.Start(); // 단추↔팝업 사이를 지나는 중일 수 있다
        }
    }

    private static Button? ButtonAt(TopLevel top, Point pos)
    {
        foreach (var weak in s_buttons)
        {
            if (weak.TryGetTarget(out var b) && b.IsEffectivelyVisible && b.IsEffectivelyEnabled && ReferenceEquals(TopLevel.GetTopLevel(b), top)
                && Contains(b, top, pos) && SameOwnerAsHit(top, pos, b))
            {
                return b;
            }
        }

        return null;
    }

    private static bool Contains(Visual v, TopLevel top, Point pos) =>
        v.TranslatePoint(default, top) is { } p && new Rect(p, v.Bounds.Size).Contains(pos);

    /// <summary>그 자리 맨 위 요소가 단추와 같은 창 소속인가(소속 창이 없는 겹침 층이면 통과).</summary>
    private static bool SameOwnerAsHit(TopLevel top, Point pos, Button b) =>
        top.InputHitTest(pos) is not Visual hit || OwnerOf(hit) is not { } owner || ReferenceEquals(owner, OwnerOf(b));

    private static Visual? OwnerOf(Visual v) =>
        (Visual?)v.FindAncestorOfType<MdiDocumentWindow>(includeSelf: true)
        ?? (Visual?)v.FindAncestorOfType<SdiTitleBar>(includeSelf: true)
        ?? v.FindAncestorOfType<MainTitleBar>(includeSelf: true);
}
