// 자석 검증 구동기(Q8) — 사용자 끌기와 같은 계산 경로(MdiLayoutPanel 의 LayoutManager.GetDragBounds/GetResizeBounds)를 단계별로 불러 위치·흡착 대상·계산 시간을 돌려준다(MDI·웹 관리형 창 공통)
#if PD_DIAGNOSTICS // 시험 구동기 — 진단 빌드 전용(Q9). 흡착 계산(MagnetMdiLayoutManager·MagnetSnap)은 제품 경로 그대로
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using Dock.Avalonia.Mdi;
using Dock.Model.Controls;
using Dock.Model.Core;

namespace xkxk.frameworks.ui.shell.Docking;

/// <summary>한 단계 결과: 그 단계의 창 영역, 붙은 대상(예: "area.left", "doc:Grid-2.right"), 보정 계산 시간(ms).</summary>
public sealed record MagnetStep(int Index, Rect Bounds, IReadOnlyList<string> SnappedTo, double Ms);

/// <summary>구동 결과. Ok=false 면 Reason 에 사유(문서가 MDI·관리형 창 배치에 없음 등).</summary>
public sealed record MagnetRun(bool Ok, string? Reason, Rect Start, IReadOnlyList<MagnetStep> Steps, Rect Final, double MaxMs);

/// <summary>
/// 원격 docs.move/docs.resize(odev-6r, 진단 빌드)용. 실제 끌기는 MdiLayoutPanel 이 "시작 경계 + 누적 이동량"으로 매 프레임 관리자를 부르고
/// 놓을 때 마지막 경계를 문서에 확정한다 — 여기서도 그 순서를 그대로 따른다(제품 경로는 바꾸지 않음).
/// 데스크톱 SDI(OS 창) 자석은 Dock 창 자석(DockSettings.EnableWindowMagnetism) 경로라 이 구동기 대상이 아니다.
/// </summary>
public static class MagnetDriver
{
    /// <summary>문서를 (dx, dy) 만큼 steps 단계로 끈다.</summary>
    public static MagnetRun MoveBy(TopLevel top, PocDocument document, double dx, double dy, int steps) =>
        Run(top, document, steps, (manager, mdi, start, i, n, size, entries) =>
            manager.GetDragBounds(mdi, start, new Vector(dx * i / n, dy * i / n), size, entries));

    /// <summary>문서의 한 가장자리(Left·Top·Right·Bottom)를 d 만큼 steps 단계로 끈다(끄는 가장자리만 흡착).</summary>
    public static MagnetRun ResizeBy(TopLevel top, PocDocument document, MdiResizeDirection edge, double d, int steps) =>
        Run(top, document, steps, (manager, mdi, start, i, n, size, entries) =>
        {
            var v = d * i / n;
            var delta = edge is MdiResizeDirection.Left or MdiResizeDirection.Right ? new Vector(v, 0) : new Vector(0, v);
            return manager.GetResizeBounds(mdi, start, delta, edge, size, entries);
        });

    private delegate Rect StepFunc(IMdiLayoutManager manager, IMdiDocument mdi, Rect start, int i, int n, Size size, IReadOnlyList<MdiLayoutEntry> entries);

    private static MagnetRun Run(TopLevel top, PocDocument document, int steps, StepFunc step)
    {
        steps = Math.Max(1, steps);
        var found = FindPanel(top, document);
        if (found is null)
        {
            return new MagnetRun(false, "문서가 MDI·관리형 창 배치에 없음(SDI OS 창·탭 모드·패널 탭)", default, Array.Empty<MagnetStep>(), default, 0); // i18n-ok 진단 응답
        }

        var (panel, mdi, entries) = found.Value;

        var manager = panel.LayoutManager ?? ClassicMdiLayoutManager.Instance;
        (manager as MagnetMdiLayoutManager)?.InvalidateDragSnapshot(); // 구동 1회 = 끌기 1회 — 직전 구동의 스냅샷(다른 창 사각형)을 쓰지 않게 한다
        var size = panel.Bounds.Size;
        var b = mdi.MdiBounds;
        var start = new Rect(b.X, b.Y, b.Width, b.Height);
        var list = new List<MagnetStep>(steps);
        var current = start;
        for (var i = 1; i <= steps; i++)
        {
            var t0 = Stopwatch.GetTimestamp();
            current = step(manager, mdi, start, i, steps, size, entries);
            var ms = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
            list.Add(new MagnetStep(i, current, Targets(current, mdi, size, entries), ms));
        }

        mdi.MdiBounds = new DockRect(current.X, current.Y, current.Width, current.Height); // 놓을 때 확정(Dock 끌기 끝과 같음)
        return new MagnetRun(true, null, start, list, current, list.Max(s => s.Ms));
    }

    /// <summary>문서가 놓인 MdiLayoutPanel(문서 영역 MDI 또는 웹 관리형 창 레이어)과 그 패널의 배치 항목.</summary>
    private static (MdiLayoutPanel Panel, IMdiDocument Mdi, IReadOnlyList<MdiLayoutEntry> Entries)? FindPanel(TopLevel top, PocDocument document)
    {
        foreach (var panel in top.GetVisualDescendants().OfType<MdiLayoutPanel>())
        {
            var entries = panel.Children.OfType<Control>()
                .Where(c => c.DataContext is IMdiDocument)
                .Select(c => new MdiLayoutEntry(c, (IMdiDocument)c.DataContext!))
                .ToList();
            var mine = entries.FirstOrDefault(e => ReferenceEquals(WindowCommands.FindDocument(e.Document), document));
            if (mine.Document is not null)
            {
                return (panel, mine.Document, entries);
            }
        }

        return null;
    }

    /// <summary>창 가장자리가 정확히 겹친(±0.5) 기준선 이름 — 영역 경계 "area.*", 다른 창 "doc:{Id}.{가장자리}".</summary>
    private static IReadOnlyList<string> Targets(Rect r, IMdiDocument self, Size area, IReadOnlyList<MdiLayoutEntry> entries)
    {
        var hits = new List<string>();
        static bool Eq(double a, double b) => Math.Abs(a - b) <= 0.5;
        if (Eq(r.Left, 0)) { hits.Add("area.left"); }
        if (Eq(r.Top, 0)) { hits.Add("area.top"); }
        if (Eq(r.Right, area.Width)) { hits.Add("area.right"); }
        if (Eq(r.Bottom, area.Height)) { hits.Add("area.bottom"); }
        foreach (var e in entries)
        {
            if (ReferenceEquals(e.Document, self) || e.Document.MdiState != MdiWindowState.Normal) { continue; }
            var o = e.Document.MdiBounds;
            var id = WindowCommands.FindDocument(e.Document)?.Id ?? e.Document.Id;
            foreach (var (name, line) in new[] { ("left", o.X), ("right", o.X + o.Width) })
            {
                if (Eq(r.Left, line) || Eq(r.Right, line)) { hits.Add($"doc:{id}.{name}"); }
            }

            foreach (var (name, line) in new[] { ("top", o.Y), ("bottom", o.Y + o.Height) })
            {
                if (Eq(r.Top, line) || Eq(r.Bottom, line)) { hits.Add($"doc:{id}.{name}"); }
            }
        }

        return hits;
    }
}
#endif
