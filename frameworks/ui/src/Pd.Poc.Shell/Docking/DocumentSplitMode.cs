// 문서 영역 분할 칸 맞춤(Q12·EXT-01) — Dock 이 문서 영역을 나눌 때 새로 만든 문서 칸이 중앙 문서 영역의 배치 방식(MDI)을 따르고 고유 Id 를 갖게 한다
using Dock.Model.Controls;
using Dock.Model.Core;

namespace Pd.Poc.Shell.Docking;

/// <summary>
/// 근거(Dock 12.1.0.12 디컴파일 logs/odev1r/ilspy_ui20/model):
/// ① DockService.SplitDocumentDockable(문서를 문서 영역 가장자리에 놓기)은 factory.CreateDocumentDock() 으로 새 칸을 만들고
///    Id·CanCreateDocument·EnableWindowDrag 등은 원래 소유 dock 에서 복사하지만 LayoutMode 는 복사하지 않는다.
///    DocumentLayoutMode 기본값은 Tabbed(=0)라 분할 칸 문서가 MDI 창이 아닌 탭 문서가 되고 zIndex 가 null 이다(Q12 계약 위반, otest-12 q15_4_tearoff.png).
/// ② FactoryBase.NewHorizontal/VerticalDocumentDock(탭 메뉴 '새 가로·세로 문서 dock')은 LayoutMode 는 복사하지만 Id 를 그대로 복사한다.
/// ③ 두 경로 모두 새 칸의 Id 가 원 문서 영역과 같은 "Documents" 가 되어 Id 로 찾는 코드(원격 panel.bounds 등)가 엉뚱한 칸을 집을 수 있다.
/// 두 경로 모두 PocDockFactory.SplitToDock → SplitAreaService.SplitToDock 의 Dock 기본 분할 분기를 지나므로 거기서 base 호출 직전에 Adopt 를 부른다.
/// '탭 문서' 프리셋(중앙 문서 영역 자체가 Tabbed)은 사용자가 고른 모드라 그대로 따른다(team-lead 결정 — 이번 범위 밖).
/// </summary>
internal static class DocumentSplitMode
{
    /// <summary>분할 칸 Id 접두어(중앙 문서 영역 Id 뒤에 붙인다).</summary>
    public const string SplitIdPrefix = PocDockFactory.DocumentDockId + ".Split.";

    /// <summary>
    /// 대상이 문서 칸(측면 패널 탭 영역 제외)이고 새 칸도 문서 칸이면, 새 칸의 LayoutMode 를 중앙 문서 영역과 같게 하고
    /// Id 가 "Documents" 이거나 이미 트리에 있는 Id 면 고유 Id(Documents.Split.N)로 바꾼다. 그 밖은 아무것도 하지 않는다.
    /// </summary>
    internal static void Adopt(PocDockFactory factory, IDock target, IDockable carrier)
    {
        if (target is not IDocumentDock || target is PanelDocumentDock || carrier is not IDocumentDock split || carrier is PanelDocumentDock
            || factory.Documents is not { } center)
        {
            return;
        }

        if (split.LayoutMode != center.LayoutMode)
        {
            factory.SetDocumentDockLayoutMode(split, center.LayoutMode);
        }

        if (split.Id == PocDockFactory.DocumentDockId || IdTaken(factory, split, split.Id))
        {
            split.Id = NextId(factory, split);
        }
    }

    private static string NextId(PocDockFactory factory, IDockable self)
    {
        for (var n = 1; ; n++)
        {
            var id = SplitIdPrefix + n;
            if (!IdTaken(factory, self, id))
            {
                return id;
            }
        }
    }

    /// <summary>트리(루트) 안에 같은 Id 를 가진 다른 dockable 이 있는가. 새 칸은 아직 트리 밖이다.</summary>
    private static bool IdTaken(PocDockFactory factory, IDockable self, string? id) =>
        id is not null && factory.Root is { } root && factory.FindDockable(root, d => d.Id == id && !ReferenceEquals(d, self)) is not null;
}
