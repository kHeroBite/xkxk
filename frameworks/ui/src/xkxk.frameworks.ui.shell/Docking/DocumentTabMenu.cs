// 문서 탭 메뉴 보정(w12) — '새 가로·세로 문서 영역' 의 패널 문서 전처리, '모두 독립 창으로 분리' 의 문서별 창 분리, 탭 메뉴 '분할 해제' 덧붙이기(U14)
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Dock.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;

namespace xkxk.frameworks.ui.shell.Docking;

/// <summary>PocDockFactory 의 override 가 부른다. 상태를 갖지 않는다.</summary>
internal static class DocumentTabMenu
{
    /// <summary>
    /// '새 가로·세로 문서 영역' 전처리(Z2). Dock 기본은 빈 새 칸을 대상 탭 영역 옆에 만든 뒤 문서를 옮기는데, 패널 탭 영역은 빈 운반 칸을 받지 않아 문서가 트리 밖 칸으로 빠진다(디컴파일 NewHorizontalDocumentDock).
    /// 패널 문서는 먼저 중앙 문서 영역으로 옮겨 중앙 문서와 같은 경로를 태운다. 패널의 도구는 분리 대상이 아니라 false 를 돌려 호출자가 아무것도 하지 않게 한다.
    /// </summary>
    internal static bool PrepareNewArea(PocDockFactory factory, IDockable dockable) =>
        dockable.Owner is not PanelDocumentDock || (dockable is PocDocument doc && factory.ReturnPanelDocument(doc));

    /// <summary>
    /// '모두 독립 창으로 분리'(Z3). 문서마다 창 1개로 띄우고 도구는 그 자리에 둔다. 문서 영역 소속이 아니면 false 를 돌려 Dock 기본에 맡긴다.
    /// 문서마다 창 1개인 근거: 단일 '독립 창으로 분리' 와 같은 단위이고, LayoutSnapshot 의 창 상태가 문서 단위이며, SdiTitleBar 가 창마다 문서 1개를 전제한다. 겹치지 않게 MDI 와 같은 간격으로 비켜 놓는다.
    /// </summary>
    internal static bool FloatAllDocuments(PocDockFactory factory, IDockable dockable)
    {
        if (dockable.Owner is not IDocumentDock { VisibleDockables: not null } dock)
        {
            return false;
        }

        dock.GetPointerScreenPosition(out var x, out var y);
        var pointerKnown = !double.IsNaN(x) && !double.IsNaN(y);
        if (!pointerKnown)
        {
            dock.GetVisibleBounds(out x, out y, out _, out _);
        }

        var offset = 0.0;
        foreach (var doc in MdiTabGroup.Documents(dock).ToList()) // 문서 단위 — MDI 칸이면 탭 묶음 안 문서도 띄운다(묶음 탭에서 부르면 dock 이 묶음이라 그 묶음 문서만)
        {
            doc.SetPointerScreenPosition(x + offset, y + offset); // NaN 이면 Dock 이 다음 후보 위치를 쓴다
            factory.FloatDockable(doc);
            if (!pointerKnown)
            {
                SdiScreenPlacement.PlaceOnMainMonitor(factory.Root, doc); // 대체 좌표(GetVisibleBounds)는 화면 좌표가 아니라 주 모니터에 뜰 수 있다
            }

            offset += PocDockFactory.CascadeStep;
        }

        MdiTabGroup.Tidy(factory, (dock as MdiTabGroupDock)?.Owner as IDocumentDock ?? dock); // 비거나 1개 남은 묶음을 푼다(빈 묶음 창이 남지 않게, 멱등)
        return true;
    }

    /// <summary>탭 메뉴에 덧붙이는 항목 템플릿 키(Themes/MdiWindowMenu.axaml)와 붙인 항목 이름(중복 방지).</summary>
    private const string UnsplitTemplateKey = "PdUnsplitTabMenuItem";
    private const string UnsplitItemName = "PdUnsplit";
    private static bool _unsplitInstalled;

    /// <summary>
    /// 탭 문서 모드 '분할 해제'(U14). Dock 기본 탭 우클릭 메뉴(테마 리소스 DocumentTabStripItemContextMenu)는 그대로 두고,
    /// 메뉴 인스턴스는 DocumentTabStripItem.ContextMenu 가 아니라 DocumentContextMenu 속성(테마가 DynamicResource 로 넣고 템플릿 제목 영역이 TemplateBinding 으로 쓴다 — 디컴파일)이므로
    /// 그 속성이 정해질 때 axaml 템플릿 항목을 메뉴 인스턴스마다 1회 덧붙인다(공유 인스턴스면 한 번만).
    /// 항목 표시 여부는 SplitMergeService.UnsplitVisible(문서 칸이고 분할이 있을 때)이 정한다 — 패널 탭에서는 숨는다. PanelJoinDrag.Install 이 부른다.
    /// </summary>
    internal static void InstallUnsplit()
    {
        if (_unsplitInstalled)
        {
            return;
        }

        _unsplitInstalled = true;
        DocumentTabStripItem.DocumentContextMenuProperty.Changed.AddClassHandler<DocumentTabStripItem>((_, e) => AttachUnsplit(e.NewValue as ContextMenu));
    }

    private static void AttachUnsplit(ContextMenu? menu)
    {
        if (menu is null || menu.Items.OfType<Control>().Any(c => c.Name == UnsplitItemName)
            || Application.Current?.TryGetResource(UnsplitTemplateKey, null, out var res) != true
            || res is not Avalonia.Styling.ITemplate template || template.Build() is not MenuItem item)
        {
            return;
        }

        menu.Items.Add(item);
    }
}
