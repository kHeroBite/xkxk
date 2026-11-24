// 도움말 투어 대상 찾기(요건 6) — AutomationId·앵커로 컨트롤을 찾고 지금 화면에 보이는지 판정한다. 안 보이면 그 단계는 건너뛴다
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace xkxk.frameworks.ui.shell.Tour;

public static class TourTargets
{
    private const double MinSize = 1;

    /// <summary>범위 안에서 대상 id 의 첫 '보이는' 컨트롤. 없으면 null.</summary>
    public static Control? Find(Visual root, string targetId, Visual overlay) =>
        Candidates(root, targetId).FirstOrDefault(c => IsVisibleTarget(c, overlay));

    /// <summary>대상이 트리에 붙어 있고, 보이고, 크기가 있고, 창 영역과 겹치면 true.</summary>
    public static bool IsVisibleTarget(Control? control, Visual overlay)
    {
        if (control is null || TopLevel.GetTopLevel(control) is null || !control.IsEffectivelyVisible || control.Opacity <= 0)
        {
            return false;
        }

        return BoundsIn(control, overlay) is { } rect && rect.Width >= MinSize && rect.Height >= MinSize;
    }

    /// <summary>대상 영역을 오버레이 좌표로(창 영역과 겹치는 부분만). 겹치지 않으면 null.</summary>
    public static Rect? BoundsIn(Control control, Visual overlay)
    {
        if (control.Bounds.Width < MinSize || control.Bounds.Height < MinSize
            || control.TranslatePoint(default, overlay) is not { } origin)
        {
            return null;
        }

        var rect = new Rect(origin, control.Bounds.Size).Intersect(new Rect(overlay.Bounds.Size));
        return rect.Width >= MinSize && rect.Height >= MinSize ? rect : null;
    }

    /// <summary>찾지 못한 까닭: 컨트롤이 아예 없으면 target-missing, 있지만 숨었거나 화면 밖이면 target-hidden.</summary>
    public static string MissingReason(Visual root, string targetId) =>
        Candidates(root, targetId).Any() ? "target-hidden" : "target-missing";

    private static IEnumerable<Control> Candidates(Visual root, string targetId)
    {
        var descendants = root.GetVisualDescendants().OfType<Control>();
        return targetId == TourCatalog.ContentAnchor
            ? descendants.Where(c => c is Docking.IDocumentZoomHost)
            : descendants.Where(c => AutomationProperties.GetAutomationId(c) == targetId);
    }
}
