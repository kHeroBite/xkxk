// 문서 칸 정리 몫 반환(D-SPLITGROW) — 같은 방향 분할로 생긴 문서 칸이 비어 정리될 때 그 칸의 몫을 이웃 문서 칸에 돌려준다
using System;
using System.Linq;
using Dock.Model.Controls;
using Dock.Model.Core;

namespace xkxk.frameworks.ui.shell.Docking;

/// <summary>
/// 근거(Dock 12.1.0.12 디컴파일 logs/odev1r/ilspy_ui20, 실측 logs/odev_tab/split_cause_w13.md):
/// ① FactoryBase.SplitToDock 은 대상의 소유자가 같은 방향 ProportionalDock 이면 대상 몫을 반으로 나눠 새 칸에 준다(Documents 0.8 → 0.4+0.4).
/// ② FactoryBase.CollapseDock 은 빈 칸과 분할선만 빼고 그 몫을 아무에게도 돌려주지 않는다(0.4:0.2 가 남는다).
/// ③ ProportionalStackPanel(ProportionManager.NormalizeProportions)이 합을 1 로 맞춰 모델에 다시 쓰므로 옆 패널이 커진다(0.6667:0.3333).
///    분할·정리를 되풀이할 때마다 패널 몫이 p → 2p/(1+p) 로 자란다.
/// 그래서 정리되는 칸이 문서 칸이면 Dock 기본 정리 뒤 그 몫을 이웃 문서 칸(분할 원본)에 더한다. 앞 이웃을 먼저 보고 없으면 뒤 이웃을 본다.
/// 측면 패널 탭 영역·알림 탭 영역은 대상이 아니다(패널 분할 정리는 SplitAreaCleanup 몫).
/// 감싸기 풀기(CleanupProportionalDockTree)로 이웃이 소유자 밖으로 나간 경우(방향이 다른 분할)는 Dock 이 감싸기 몫을 그대로 되돌리므로 건드리지 않는다.
/// </summary>
internal static class DocumentSplitCollapse
{
    /// <summary>PocDockFactory.CollapseDock(override) 본문. baseCollapse 는 Dock 기본 정리(base.CollapseDock)다.</summary>
    internal static void Collapse(IDock dock, Action<IDock> baseCollapse)
    {
        var owner = dock.Owner as IProportionalDock;
        var share = dock.Proportion;
        var heir = owner is null ? null : HeirOf(owner, dock);
        baseCollapse(dock);
        if (heir is null || owner?.VisibleDockables is not { } list || list.Contains(dock) || !ReferenceEquals(heir.Owner, owner)
            || !list.Contains(heir) || !IsShare(heir.Proportion))
        {
            return; // 정리되지 않았거나(비지 않음·접을 수 없음) 이웃이 감싸기 풀기로 옮겨 갔다
        }

        PocDockFactory.SetShare(heir, heir.Proportion + share);
    }

    /// <summary>정리될 문서 칸의 몫을 받을 이웃 문서 칸. 대상이 문서 칸이 아니거나 몫이 없거나 이웃 문서 칸이 없으면 null.</summary>
    private static IDockable? HeirOf(IProportionalDock owner, IDock dock)
    {
        if (!IsDocumentArea(dock) || !IsShare(dock.Proportion) || owner.VisibleDockables is not { } list)
        {
            return null;
        }

        var at = list.IndexOf(dock);
        if (at < 0)
        {
            return null;
        }

        var before = list.Take(at).LastOrDefault(d => d is not IProportionalDockSplitter);
        var after = list.Skip(at + 1).FirstOrDefault(d => d is not IProportionalDockSplitter);
        if (before is not null && IsDocumentArea(before))
        {
            return before;
        }

        return after is not null && IsDocumentArea(after) ? after : null;
    }

    /// <summary>문서 칸이거나 문서 칸을 품은 분할인가. 측면 패널·알림 탭 영역은 제외한다. 빈 분할(자식 0)은 문서 칸이 아니다.</summary>
    private static bool IsDocumentArea(IDockable d) => d switch
    {
        PanelDocumentDock => false,
        IDocumentDock => true,
        IProportionalDock { VisibleDockables: { } children } => children.Any(IsDocumentArea),
        _ => false,
    };

    private static bool IsShare(double p) => !double.IsNaN(p) && p > 0;
}
