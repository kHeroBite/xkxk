// 패널 영역 값 — 기본 패널 하나와 그 분할 패널을 묶은 자리. 어느 가장자리에 붙는지는 DockLayoutState.Edges 가 정한다
namespace Pd.Poc.Shell.Docking;

/// <summary>
/// 패널 영역. 이름은 처음 담는 도구를 따른 것이고 위치와 무관하다. 가장자리는 사용자가 옮긴다(PanelMoveService).
/// Extra 는 처음에 비어 접혀 있는 네 번째 자리로, 패널이나 탭을 빈 가장자리로 옮길 때 쓴다.
/// </summary>
public enum PanelArea
{
    Explorer,
    Log,
    Notification,
    Extra,
}
