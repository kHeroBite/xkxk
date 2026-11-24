// 브라우저 수명주기 내보내기 — main.js 가 pagehide·visibilitychange(hidden) 때 불러 레이아웃을 즉시 저장한다(제품·진단 빌드 공통)
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using xkxk.frameworks.ui.shell;
using xkxk.frameworks.ui.shell.Docking;

namespace xkxk.frameworks.ui.browser;

[SupportedOSPlatform("browser")]
public static partial class LifecycleExports
{
    /// <summary>
    /// 즉시 저장(동기, localStorage). 마지막 저장 뒤 변경이 없거나 자동 저장이 꺼져 있으면(--fresh·시나리오 인자·사용자 설정 끔) 아무것도 하지 않는다.
    /// 브라우저는 이 이벤트에서 비동기 작업을 기다려 주지 않으므로 반환 시점에 저장이 끝나 있어야 한다. 예외는 밖으로 내보내지 않는다.
    /// </summary>
    [JSExport]
    public static void FlushLayout() => App.FlushLayoutAutoSave();

    /// <summary>탭이 가려지면(visibilitychange hidden) 열린 Snap Assist 를 끝낸다(focus 사유, 세션이 없으면 아무것도 하지 않는다).</summary>
    [JSExport]
    public static void EndSnapAssist() => SnapAssistSession.Cancel(SnapAssistEndReason.Focus);
}
