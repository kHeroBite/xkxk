// 원격 명령(창 메뉴 시험 — winmenu.state·run·noteTab·selftest). 진단 빌드 전용. 항목 활성·실행은 화면 메뉴와 같은 WindowMenuOps 를 부른다
#if PD_DIAGNOSTICS // 진단 빌드 전용(PdDiagnostics=true)
using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Dock.Model.Core;
using Pd.Poc.Shell.Docking;

namespace Pd.Poc.Shell.Diagnostics.Remote;

internal static class RemoteTitleMenuCommands
{
    internal static void Register(RemoteCommandRouter r)
    {
        r.Register("winmenu.state", "창 메뉴 항목 활성(팝업 없음) kind=main|sdi|mdi|tab [doc=] → {kind, doc, restore, minimize, maximize, move, close}", a =>
        {
            var target = Target(a);
            var f = WindowMenuOps.Facts(target);
            return new JsonObject
            {
                ["kind"] = target.Kind.ToString(),
                ["doc"] = target.Document?.Id,
                ["restore"] = f.Restore,
                ["minimize"] = f.Minimize,
                ["maximize"] = f.Maximize,
                ["move"] = f.Move,
                ["close"] = f.Close,
            };
        });
        r.Register("winmenu.run", "창 메뉴 항목 실행(메뉴 클릭과 같은 경로) kind=main|sdi|mdi|tab [doc=] action=restore|minimize|maximize|movedefault|close → {done}", a =>
        {
            var action = a.ReqStr("action").ToLowerInvariant() switch
            {
                "restore" => WindowMenuAction.Restore,
                "minimize" => WindowMenuAction.Minimize,
                "maximize" => WindowMenuAction.Maximize,
                "movedefault" => WindowMenuAction.MoveToDefault,
                "close" => WindowMenuAction.Close,
                var other => throw new RemoteCommandException($"알 수 없는 action: {other}"),
            };
            return new JsonObject { ["done"] = WindowMenuOps.Run(Target(a), action) };
        });
        r.Register("winmenu.noteTab", "탭 우클릭 대상 지정(우클릭 입력 대신, 활성화 없음) doc= → 문서 id", a =>
        {
            var doc = RemoteShellAccess.Doc(a);
            WindowMenuOps.TabTarget = doc;
            return JsonValue.Create(doc.Id);
        });
        r.Register("winmenu.selftest", "창 메뉴 순수 함수 표본(상태별 활성·MDI 기본 칸·SDI 한 창 이동·소속 모니터) → {cases:[{name,pass,detail}], passed, total}", _ => SelfTest());
    }

    private static WindowMenuTarget Target(JsonObject a)
    {
        var kind = a.ReqStr("kind").ToLowerInvariant() switch
        {
            "main" => WindowMenuKind.Main,
            "sdi" => WindowMenuKind.Sdi,
            "mdi" => WindowMenuKind.Mdi,
            "tab" => WindowMenuKind.Tab,
            var other => throw new RemoteCommandException($"알 수 없는 kind: {other}"),
        };
        return kind == WindowMenuKind.Main
            ? new WindowMenuTarget(kind, null, VisualTreeInspector.Main() as Window)
            : new WindowMenuTarget(kind, RemoteShellAccess.Doc(a), null);
    }

    private static JsonObject SelfTest()
    {
        var cases = new JsonArray();
        void Case(string name, bool pass, string detail) => cases.Add(new JsonObject { ["name"] = name, ["pass"] = pass, ["detail"] = detail });

        var normal = WindowMenuRules.Compute(WindowMenuShowState.Normal, true, true);
        Case("normal", !normal.Restore && normal.Minimize && normal.Maximize && normal.Move && normal.Close, normal.ToString());
        var max = WindowMenuRules.Compute(WindowMenuShowState.Maximized, true, true);
        Case("maximized", max.Restore && max.Minimize && !max.Maximize && max.Move && max.Close, max.ToString());
        var min = WindowMenuRules.Compute(WindowMenuShowState.Minimized, true, true);
        Case("minimized", min.Restore && !min.Minimize && min.Maximize && min.Move && min.Close, min.ToString());
        var panel = WindowMenuRules.Compute(WindowMenuShowState.Normal, false, false);
        Case("panel-or-tabbed", !panel.Restore && !panel.Minimize && !panel.Maximize && !panel.Move && panel.Close, panel.ToString());
        var locked = WindowMenuRules.Compute(WindowMenuShowState.Normal, false, true);
        Case("locked-blocks-state-only", !locked.Minimize && !locked.Maximize && locked.Move && locked.Close, locked.ToString());

        var area = new Size(1000, 700);
        var size = new DockRect(5000, 5000, 400, 300);
        var free = PlacementRecovery.MdiDefaultSlot(area, MdiGrabZone.Whole, size, new List<DockRect>(), PocDockFactory.NextMdiBounds);
        Case("mdi-free", free.X == 0 && free.Y == 0 && free.Width == 400 && free.Height == 300, free.ToString());
        var taken = PlacementRecovery.MdiDefaultSlot(area, MdiGrabZone.Whole, size, new List<DockRect> { new(0, 0, 400, 300) }, PocDockFactory.NextMdiBounds);
        Case("mdi-next-step", taken.X > 0 && taken.Y > 0 && taken.Width == 400, taken.ToString());
        var tiny = PlacementRecovery.MdiDefaultSlot(new Size(20, 20), MdiGrabZone.Whole, size, new List<DockRect> { new(0, 0, 400, 300) }, PocDockFactory.NextMdiBounds);
        Case("mdi-slot-out-goes-origin", tiny.X == 0 && tiny.Y == 0, tiny.ToString());

        var areas = new[] { new Rect(0, 0, 1920, 1080) };
        var sdi = new[]
        {
            new SdiResetWindow(new Point(40, 40), new Size(400, 300), true),
            new SdiResetWindow(new Point(9000, 9000), new Size(400, 300), false),
            new SdiResetWindow(new Point(500, 500), new Size(400, 300), true),
        };
        var placed = SdiResetLayout.Compute(areas, new Point(40, 40), sdi);
        Case("sdi-only-target-moves", placed[0] is null && placed[2] is null && placed[1] is { X: 70, Y: 70 }, string.Join(";", placed));

        var monitors = new[]
        {
            new MonitorInfo("A", "A", new PixelRect(0, 0, 1000, 800), new PixelRect(0, 0, 1000, 760), true),
            new MonitorInfo("B", "B", new PixelRect(1000, 0, 1000, 800), new PixelRect(1000, 0, 1000, 760), false),
        };
        Case("owner-monitor", MonitorSelection.LargestOverlap(monitors, new PixelRect(1200, 100, 300, 200))?.Id == "B", "rect on B");
        Case("owner-monitor-hidden-nearest", MonitorSelection.LargestOverlap(monitors, new PixelRect(5000, 100, 300, 200))?.Id == "B", "rect right of B");

        var passed = 0;
        foreach (var c in cases)
        {
            passed += c!["pass"]!.GetValue<bool>() ? 1 : 0;
        }

        return new JsonObject { ["cases"] = cases, ["passed"] = passed, ["total"] = cases.Count };
    }
}
#endif
