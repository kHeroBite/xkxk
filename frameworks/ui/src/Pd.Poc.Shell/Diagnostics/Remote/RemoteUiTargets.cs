// 원격 명령 공용 — 검색 범위(doc·window 인자)와 대상 컨트롤(id·dock 인자) 찾기(옛 RemoteCommands.Ui 의 Scope·Target)
#if PD_DIAGNOSTICS // 진단 빌드 전용(PdDiagnostics=true)
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;

namespace Pd.Poc.Shell.Diagnostics.Remote;

/// <summary>UI·스크롤·언어 명령 군이 함께 쓰는 원격 UI 대상 찾기. 명령 등록은 하지 않는다.</summary>
internal static class RemoteUiTargets
{
    /// <summary>검색 범위: doc=그 문서 창(MDI 창 틀·SDI 실 창 전체, 제목줄 버튼 포함) | window=창 번호 | 생략=모든 창.</summary>
    internal static Visual? Scope(JsonObject a)
    {
        if (a.ContainsKey("doc"))
        {
            return VisualTreeInspector.DocumentScope(RemoteShellAccess.Doc(a)) ?? throw new RemoteCommandException("문서가 화면에 표시돼 있지 않습니다.");
        }

        if (a.Int("window") is { } wi)
        {
            var tops = VisualTreeInspector.TopLevels();
            return wi >= 0 && wi < tops.Count ? tops[wi] : throw new RemoteCommandException($"window 범위 0~{tops.Count - 1}");
        }

        return null;
    }

    /// <summary>
    /// 대상 컨트롤: id=AutomationId|Name, 또는 dock=<Dock Id>(LeftPane·BottomPane 등 — DataContext 가 그 dock 인 ToolDockControl, odev-1 03:1x:
    /// Dock 템플릿이 만든 바깥 패널 컨트롤은 AutomationId·Name 이 없어 id 로 못 찾는다).
    /// </summary>
    internal static Control Target(JsonObject a)
    {
        if (a.Str("dock") is { Length: > 0 } dockId)
        {
            var dock = RemoteDockAccess.Factory.Root is { } root ? RemoteDockAccess.Factory.FindDockable(root, d => d.Id == dockId) : null;
            return (dock is null ? null : RemoteDockAccess.PaneView(dock)) ?? throw new RemoteCommandException($"dock 컨트롤을 찾지 못했습니다: {dockId}(숨김·접힘이면 트리에 없음)");
        }

        return VisualTreeInspector.Require(a.ReqStr("id"), Scope(a));
    }
}
#endif
