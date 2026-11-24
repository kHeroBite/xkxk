// 웹 포인터 누름의 원래 시각 공급자 — main.js 가 창 캡처 단계에서 기록한 PointerEvent.timeStamp(__pdPress.t)를 셸 더블클릭 판정에 넘긴다
using System.Runtime.InteropServices.JavaScript;
using xkxk.frameworks.ui.shell.Docking;

namespace xkxk.frameworks.ui.browser;

/// <summary>
/// 왜: Avalonia.Browser 는 누름 시각을 처리 시점 Stopwatch 로 찍어(vendor/avalonia-12.1.4/src/Browser/Avalonia.Browser/BrowserInputHandler.cs:49)
/// 주 스레드 지연이 두 누름 간격에 더해진다. PointerEvent.timeStamp 는 이벤트가 만들어진 시각이라 처리 지연을 포함하지 않는다(Event Timing API 의 입력 지연 = processingStart − startTime).
/// 대응(확인): 단일 스레드 WASM 에서는 JS pointerdown 처리기 안에서 .NET 누름 처리(PointerPressed)가 동기로 끝난다
/// (InputHelper.RedirectInputAsync → BrowserInputHandler.ScheduleInput → DispatchInput, 관리형 디스패처는 스레드 지원일 때만).
/// 그래서 main.js 가 같은 이벤트의 앞 단계(window 캡처)에서 써 둔 값이 지금 처리 중인 누름의 시각이다. main.js 는 이 이벤트 처리가 끝나면 값을 지운다.
/// </summary>
internal sealed class BrowserPressTimeSource : IPressTimeSource
{
    private const string GlobalName = "__pdPress";

    public double? CurrentPressTime()
    {
        try
        {
            using var press = JSHost.GlobalThis.GetTypeOfProperty(GlobalName) == "object" ? JSHost.GlobalThis.GetPropertyAsJSObject(GlobalName) : null;
            if (press is null || press.GetTypeOfProperty("t") != "number")
            {
                return null;
            }

            var t = press.GetPropertyAsDouble("t");
            return t >= 0 ? t : null;
        }
        catch (JSException)
        {
            return null;
        }
    }
}
