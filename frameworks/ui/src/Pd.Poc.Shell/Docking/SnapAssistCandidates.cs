// Snap Assist 범위별 후보 창 열거와 기준 영역 계산 — 같은 영역의 다른 창만, 배치한 창·이미 놓은 창·위치 고정·칸 최소 크기 미달 제외(11 문서 §3.9, 컨트롤 레이어)
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;

namespace Pd.Poc.Shell.Docking;

/// <summary>
/// 후보 계산 결과. Candidates = 그 칸에 놓을 수 있는 후보(위가 먼저), ExcludedSmall = 칸 최소 크기 미달(CanApplyCell=false)로 뺀 수(진단).
/// </summary>
internal readonly record struct SnapAssistCandidateList(IReadOnlyList<SnapAssistCandidate> Candidates, int ExcludedSmall);

/// <summary>
/// 화면을 그리지 않는다. 대상 목록은 App.Shell(WindowCommands)에서, 썸네일 원천은 anchor 의 시각 트리에서 찾아 opaque 로 넘긴다.
/// Mdi: 배치한 창과 같은 MDI 문서 영역(IDocumentDock, LayoutMode=Mdi)의 MDI 창. 탭 묶음은 묶음 창 1개. 패널 문서 제외. 순서 = Z 순서(MRU) 위가 먼저.
/// Managed: 관리형 창 레이어의 SDI 창(OS 창 제외). Sdi: 배치한 창과 같은 모니터의 OS SDI 창(다른 모니터 제외 — 배율이 다른 경로 미검증, 11 문서 §12).
/// Managed·Sdi 순서는 열거 순서다(활성 시각 정보가 Shell 공개 API 에 없다).
/// 최소화·최대화 창은 후보에 넣는다(놓을 때 ApplyCell 이 보통 상태로 돌린다). 패널 탭만 남은 분리 창은 넣지 않는다(문서 창만, YAGNI).
/// </summary>
internal static class SnapAssistCandidates
{
    /// <summary>
    /// 칸 하나의 후보. placed = 배치한 창, exclude = 이번 세션에 이미 놓은 창 Key(배치한 창 Key 포함).
    /// </summary>
    public static SnapAssistCandidateList For(SnapAssistScope scope, object anchor, WindowMenuTarget placed, string layoutId, int cell, IReadOnlySet<string> exclude)
    {
        var list = new List<SnapAssistCandidate>();
        var small = 0;
        foreach (var c in Enumerate(scope, anchor, placed))
        {
            if (exclude.Contains(c.Key) || IsLocked(c.Target))
            {
                continue;
            }

            if (!WindowMenuOps.CanApplyCell(c.Target, layoutId, cell))
            {
                small++;
                continue;
            }

            list.Add(c);
        }

        return new SnapAssistCandidateList(list, small);
    }

    /// <summary>
    /// 기준 영역(ApplyCell 이 쓰는 영역과 같은 기준). Mdi = MDI 배치 패널 크기, Managed = 관리형 창 레이어 배치 영역, Sdi = 배치한 창 모니터의 작업 영역 DIP.
    /// 모르면 빈 사각형.
    /// </summary>
    public static Rect AreaOf(SnapAssistScope scope, object anchor)
    {
        switch (scope)
        {
            case SnapAssistScope.Mdi:
                return anchor is Control { Bounds: { Width: > 0, Height: > 0 } b } ? new Rect(b.Size) : default;
            case SnapAssistScope.Managed:
                var layer = ManagedWindowRect.LayerArea();
                if (layer.Width > 0 && layer.Height > 0)
                {
                    return layer;
                }

                return anchor is Control { Bounds: { Width: > 0, Height: > 0 } a } ? new Rect(a.Size) : default;
            default:
                if (anchor is not Window w || (w.Screens.ScreenFromWindow(w) ?? w.Screens.Primary) is not { } screen)
                {
                    return default;
                }

                var s = screen.Scaling > 0 ? screen.Scaling : 1;
                var wa = screen.WorkingArea;
                return new Rect(wa.X / s, wa.Y / s, wa.Width / s, wa.Height / s);
        }
    }

    /// <summary>
    /// 창 식별 Key(세션 안에서만 쓴다). MDI 는 화면의 MDI 창(탭 묶음이면 묶음) 객체, SDI 는 IDockWindow 객체, 그 밖은 문서. 같은 창이면 같은 Key.
    /// 객체 식별 해시를 쓴다 — 관리형 창 Id 는 창마다 같을 수 있다(SdiTitleBar.TabJoin 주석).
    /// </summary>
    public static string KeyOf(WindowMenuTarget target)
    {
        if (target.Document is { } doc)
        {
            if (App.Shell is WindowCommands shell && shell.SdiPlacement.WindowOf(doc) is { } window)
            {
                return "sdi#" + RuntimeHelpers.GetHashCode(window);
            }

            return MdiTabGroup.WindowOf(doc) is { } win ? "mdi#" + RuntimeHelpers.GetHashCode(win) : "doc:" + doc.Id;
        }

        return target.PanelWindow is { } pw ? "sdi#" + RuntimeHelpers.GetHashCode(pw) : target.Main is not null ? "main" : "none";
    }

    private static IEnumerable<SnapAssistCandidate> Enumerate(SnapAssistScope scope, object anchor, WindowMenuTarget placed)
    {
        if (App.Shell is not WindowCommands shell)
        {
            return Array.Empty<SnapAssistCandidate>();
        }

        var placedKey = KeyOf(placed);
        return scope switch
        {
            SnapAssistScope.Mdi => Mdi(shell, anchor, placed, placedKey),
            SnapAssistScope.Managed => Windows(shell, anchor, placedKey, managed: true, screen: null),
            _ => Windows(shell, anchor, placedKey, managed: false, screen: ScreenBounds(anchor as Window)),
        };
    }

    private static IEnumerable<SnapAssistCandidate> Mdi(WindowCommands shell, object anchor, WindowMenuTarget placed, string placedKey)
    {
        if (placed.Document is not { } placedDoc || MdiTabGroup.WindowOf(placedDoc) is not { } placedWin
            || ((IDockable)placedWin).Owner is not IDocumentDock { LayoutMode: DocumentLayoutMode.Mdi } area)
        {
            yield break;
        }

        var seen = new HashSet<IMdiDocument>();
        var order = shell.GetMdiZOrder(); // 아래→위 — 위가 먼저가 되게 뒤에서부터
        for (var i = order.Count - 1; i >= 0; i--)
        {
            var doc = order[i];
            if (MdiTabGroup.WindowOf(doc) is not { } win || !seen.Add(win) || !ReferenceEquals(((IDockable)win).Owner, area))
            {
                continue;
            }

            // 묶음은 활성 탭 문서를 대표로 쓴다(ApplyCell·Placement 가 묶음 창을 찾는다)
            var rep = win is MdiTabGroupDock g ? (g.ActiveDockable as PocDocument ?? g.Documents.FirstOrDefault() ?? doc) : doc;
            var target = new WindowMenuTarget(WindowMenuKind.Mdi, rep, null);
            var key = KeyOf(target);
            if (key == placedKey)
            {
                continue;
            }

            yield return new SnapAssistCandidate(key, ((IDockable)win).Title ?? rep.Title ?? string.Empty, target, MdiChild(anchor, win, rep));
        }
    }

    private static IEnumerable<SnapAssistCandidate> Windows(WindowCommands shell, object anchor, string placedKey, bool managed, PixelRect? screen)
    {
        var seen = new HashSet<IDockWindow>();
        foreach (var doc in shell.SdiDocuments())
        {
            if (shell.SdiPlacement.WindowOf(doc) is not { } window || !seen.Add(window))
            {
                continue;
            }

            var host = window.Host as Window;
            if (managed ? host is not null : host is null)
            {
                continue; // 관리형 범위는 OS 창을, SDI 범위는 관리형 창을 뺀다
            }

            if (!managed && (screen is null || ScreenBounds(host) != screen))
            {
                continue; // 같은 모니터만
            }

            var target = new WindowMenuTarget(WindowMenuKind.Sdi, doc, null);
            var key = KeyOf(target);
            if (key == placedKey)
            {
                continue;
            }

            var title = window.Title is { Length: > 0 } t ? t : doc.Title ?? string.Empty;
            yield return new SnapAssistCandidate(key, title, target, managed ? ManagedChild(anchor, window) : host);
        }
    }

    /// <summary>위치 고정 창인가(WindowMenuOps.IsSnapLocked 와 같은 기준 — 묶음은 안 문서 하나라도). CanApplyCell 도 false 가 되지만 최소 크기 미달과 구분해 센다.</summary>
    private static bool IsLocked(WindowMenuTarget target)
    {
        if (target.Document is not { } doc)
        {
            return false;
        }

        return MdiTabGroup.GroupOf(doc) is { } group
            ? group.Documents.Any(d => WindowCommands.ChromeOf(d).IsLocked)
            : WindowCommands.ChromeOf(doc).IsLocked;
    }

    private static PixelRect? ScreenBounds(Window? w) =>
        w is not null && w.Screens.ScreenFromWindow(w) is { } screen ? screen.Bounds : null;

    /// <summary>MDI 배치 패널 안 그 창의 화면 요소(WindowMenuOps.FindMdi 와 같은 판정). 못 찾으면 null.</summary>
    private static Control? MdiChild(object anchor, IMdiDocument win, PocDocument rep) =>
        (anchor as MdiLayoutPanel)?.Children.OfType<Control>()
            .FirstOrDefault(c => ReferenceEquals(c.DataContext, win) || ReferenceEquals(WindowCommands.FindDocument(c.DataContext), rep));

    /// <summary>관리형 창 레이어 안 그 창의 MdiDocumentWindow(ManagedWindowRect.FindManaged 와 같은 판정). 못 찾으면 null.</summary>
    private static Control? ManagedChild(object anchor, IDockWindow window)
    {
        var root = anchor is Visual v ? (Visual?)TopLevel.GetTopLevel(v) ?? v : null;
        return root?.GetVisualDescendants().OfType<MdiDocumentWindow>()
            .FirstOrDefault(w => w.DataContext is ManagedDockWindowDocument m && ReferenceEquals(m.Window, window));
    }
}
