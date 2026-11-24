// 포인터 누름의 원래 입력 시각 공급 계약 — 플랫폼 호스트가 앱 처리 지연과 무관한 누름 시각을 알려 주고, 더블클릭 판정(TitleDragRestore)은 그 간격만 쓴다
namespace Pd.Poc.Shell.Docking;

/// <summary>
/// 웹 호스트가 구현해 TitleDragRestore.PressTimeSource 에 넣는다(BrowserPressTimeSource — 브라우저 PointerEvent.timeStamp).
/// 데스크톱·모바일은 구현이 없다(null → OS 누름 횟수만 쓰는 기존 판정 그대로).
/// UI 스레드에서 누름 처리 중에 불린다. 예외를 밖으로 던지지 않는다.
/// </summary>
public interface IPressTimeSource
{
    /// <summary>
    /// 지금 처리 중인 누름이 입력된 원래 시각(ms). 같은 공급자의 값끼리만 빼서 간격으로 쓴다(기준점은 공급자 고유).
    /// 지금 처리 중인 누름이 실제 입력에서 온 것이 아니거나(자동화 주입 등) 알 수 없으면 null.
    /// </summary>
    double? CurrentPressTime();
}
