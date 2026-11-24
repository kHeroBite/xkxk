// 패널 가장자리 표시 이름(현재 언어, 사전 키 notify.pos.*) — 옛 PocDockFactory.Panels.cs 안의 공개 클래스를 그대로 옮김
namespace Pd.Poc.Shell.Docking;

public static class PanelEdgeNames
{
    /// <summary>가장자리 표시 이름(현재 언어, 사전 키 notify.pos.*).</summary>
    public static string DisplayName(PanelEdge edge) => Localization.Loc.Tr(edge switch
    {
        PanelEdge.Top => "notify.pos.top",
        PanelEdge.Bottom => "notify.pos.bottom",
        PanelEdge.Left => "notify.pos.left",
        _ => "notify.pos.right",
    });
}
