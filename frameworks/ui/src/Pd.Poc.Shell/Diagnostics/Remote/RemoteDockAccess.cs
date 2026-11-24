// 원격 명령 공용 — 도킹 팩토리·DockManager 접근, Id 로 도킹 요소 찾기, 패널 컨트롤 찾기, 거부 응답(옛 RemoteCommands.Panels 의 공용 멤버)
#if PD_DIAGNOSTICS // 진단 빌드 전용(PdDiagnostics=true)
using System.Linq;
using System.Text.Json.Nodes;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using Dock.Model.Core;
using Pd.Poc.Shell.Docking;

namespace Pd.Poc.Shell.Diagnostics.Remote;

/// <summary>여러 원격 명령 군(패널·분할·UI·스크롤·창 메뉴·문서로 도킹)이 함께 쓰는 도킹 트리 조회. 명령 등록은 하지 않는다.</summary>
internal static class RemoteDockAccess
{
    internal static PocDockFactory Factory => RemoteShellAccess.Vm.Factory;

    /// <summary>Id 로 도킹 요소 찾기(패널·도구·문서). 없으면 예외.</summary>
    internal static IDockable FindDock(string id) =>
        (Factory.Root is { } root ? Factory.FindDockable(root, d => d.Id == id) : null)
        ?? throw new RemoteCommandException($"도킹 요소 없음: {id} (panel.list 로 확인)");

    /// <summary>화면의 DockControl.DockManager — Dock 이 실제 끌어 놓기에서 쓰는 판정기.</summary>
    internal static IDockManager DockManager =>
        VisualTreeInspector.Main().GetVisualDescendants().OfType<DockControl>().FirstOrDefault()?.DockManager
        ?? throw new RemoteCommandException("DockControl 이 화면에 없습니다.");

    internal static JsonObject Rejected(string reason, string? side = null) => new() { ["status"] = "Rejected", ["reason"] = reason, ["side"] = side };

    /// <summary>측면 패널(ToolDock)을 그리는 컨트롤 = DataContext 가 그 dock 인 ToolDockControl. 숨김·접힘이면 트리에 없어 null.</summary>
    internal static ToolDockControl? PaneView(IDockable dock) =>
        VisualTreeInspector.Main().GetVisualDescendants().OfType<ToolDockControl>().FirstOrDefault(c => ReferenceEquals(c.DataContext, dock));
}
#endif
