// 브라우저(WASM)에서 원격 제어 명령을 JS 로 노출하는 JSExport 브리지(UI-08) — main.js 가 window.pdAutomation.invoke(json) 로 연결
#if PD_DIAGNOSTICS // 진단 빌드(PdDiagnostics=true) 전용. 제품 빌드에서는 JSExport 자체가 빠져 같은 출처 스크립트도 getAssemblyExports 로 부를 수 없다(결함 1번)
using System.Runtime.InteropServices.JavaScript;
using xkxk.frameworks.ui.shell.Diagnostics.Remote;

namespace xkxk.frameworks.ui.browser;

/// <summary>
/// WASM 은 HTTP 수신이 불가하므로, 데스크톱 원격 제어 서버와 같은 명령 라우터를 JS 함수로 노출한다.
/// 입력·출력은 같은 JSON({"cmd":"docs.list","args":{}} → 응답 JSON). Playwright 는 page.evaluate 로 호출한다.
/// main.js 는 이 export 가 있을 때만 diag.js 를 불러 window.pdAutomation 을 만든다.
/// </summary>
public static partial class AutomationExports
{
    [JSExport]
    public static Task<string> Invoke(string requestJson) => RemoteCommandRouter.Default.InvokeJsonAsync(requestJson);
}
#endif
