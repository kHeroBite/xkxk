// 측면 패널 안쪽 탭 영역(UI-14 대안 A) — 탭 모드 DocumentDock 으로 도구·문서 모두 끌어 놓기 합류를 받는다(옛 PocDockFactory.PanelTabs.cs 안의 공개 클래스를 그대로 옮김)
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;

namespace xkxk.frameworks.ui.shell.Docking;

/// <summary>
/// 측면 패널 안쪽의 탭 영역. 근거(Dock 12.1.0.12 디컴파일):
/// ① DockManager.ValidateDocument 는 대상이 ITool·IToolDock 이면 거부하지만 IDocumentDock 은 허용한다(Fill → MoveDockable).
/// ② ValidateTool 은 대상 IDocumentDock 을 CanDockAsDocument(기본 true) 조건으로 허용한다.
/// ③ 핀(자동 숨김)은 FactoryBase.PinDockableCore 가 dockable.Owner 가 IToolDock 일 때만 처리한다.
/// 그래서 바깥은 ToolDock(핀·도구 창 머리줄 유지), 안쪽은 이 탭 영역(합류 대상)으로 둔다. 중앙 MDI 문서 영역과는 별개 인스턴스다.
/// </summary>
public sealed class PanelDocumentDock : DocumentDock
{
    public PanelDocumentDock()
    {
        LayoutMode = DocumentLayoutMode.Tabbed;
        CanCreateDocument = false;
        IsCollapsable = false;
        EmptyContent = "";
    }
}
