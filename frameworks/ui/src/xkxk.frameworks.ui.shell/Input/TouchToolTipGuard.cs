// 터치 입력에서 hover 툴팁이 뜨지 않게 막는 입력 처리(D-W3) — 마지막 포인터가 터치면 툴팁 열기를 취소하고, 마우스·펜이면 그대로 둔다
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace xkxk.frameworks.ui.shell.Input;

/// <summary>
/// Avalonia 12.1.4 ToolTipService 는 포인터가 요소 위에 들어오면(터치는 누를 때) 지연 뒤 툴팁을 열고, 터치를 떼어도 포인터가 그 자리에 남은 것으로 보아
/// 툴팁이 화면에 남았다(i1505 Fold5 접힘 '알림 검토 304'). 터치에는 hover 가 없으므로 마지막 입력이 터치이면 <see cref="ToolTip.ToolTipOpeningEvent"/> 를 취소한다.
/// 마우스·펜이 다시 움직이면 원래대로 툴팁이 뜬다(데스크톱 마우스 동작 불변).
/// </summary>
public static class TouchToolTipGuard
{
    private static bool s_installed;

    /// <summary>마지막으로 누르거나 움직인 포인터가 터치인가.</summary>
    public static bool LastWasTouch { get; private set; }

    /// <summary>진단용: 터치라서 취소한 툴팁 열기 수.</summary>
    public static int Suppressed { get; private set; }

    /// <summary>앱 시작 때 1회(PinchZoom.Install 에서 함께 부른다).</summary>
    public static void Install()
    {
        if (s_installed)
        {
            return;
        }

        s_installed = true;
        InputElement.PointerPressedEvent.AddClassHandler<TopLevel>((_, e) => Track(e), RoutingStrategies.Tunnel, handledEventsToo: true);
        InputElement.PointerMovedEvent.AddClassHandler<TopLevel>((_, e) => Track(e), RoutingStrategies.Tunnel, handledEventsToo: true);
        ToolTip.ToolTipOpeningEvent.AddClassHandler<Control>((_, e) =>
        {
            if (LastWasTouch)
            {
                e.Cancel = true;
                Suppressed++;
            }
        }, handledEventsToo: true);
    }

    private static void Track(PointerEventArgs e) => LastWasTouch = e.Pointer.Type == PointerType.Touch;
}
