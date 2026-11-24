// 패널이 붙는 바깥 가장자리 값(옛 PocDockFactory.Panels.cs 안의 공개 열거형을 그대로 옮김)
namespace Pd.Poc.Shell.Docking;

/// <summary>패널이 붙는 바깥 가장자리. 가운데(문서 영역)는 값으로 존재하지 않는다.</summary>
public enum PanelEdge
{
    Top,
    Bottom,
    Left,
    Right,
}
