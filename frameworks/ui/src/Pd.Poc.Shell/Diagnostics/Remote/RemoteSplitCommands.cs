// 원격 명령(UI-20 패널 영역 분할) — 분할 덤프(패널 수·비율·탭·화면 사각형)·가장자리 놓기 분할·분할선 끌기 재현·저장 캡처
#if PD_DIAGNOSTICS // 진단 빌드 전용(PdDiagnostics=true)
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Dock.Controls.ProportionalStackPanel;
using Dock.Model.Controls;
using Dock.Model.Core;
using Pd.Poc.Shell.Docking;

namespace Pd.Poc.Shell.Diagnostics.Remote;

internal static class RemoteSplitCommands
{
    // 공용 멤버 전달자(W2 F) — 본문은 옛 이름 그대로 쓴다
    private static IShellCommands Shell => RemoteShellAccess.Shell;

    private static ViewModels.MainViewModel Vm => RemoteShellAccess.Vm;

    private static bool EnsureRestored() => RemoteShellAccess.EnsureRestored();

    private static PocDockFactory Factory => RemoteDockAccess.Factory;

    private static IDockable FindDock(string id) => RemoteDockAccess.FindDock(id);

    private static IDockManager DockManager => RemoteDockAccess.DockManager;

    private static JsonObject Rejected(string reason, string? side = null) => RemoteDockAccess.Rejected(reason, side);

    private static global::Dock.Avalonia.Controls.ToolDockControl? PaneView(IDockable dock) => RemoteDockAccess.PaneView(dock);

    private static JsonNode? BoundsOf(Control? c) =>
        c is { IsEffectivelyVisible: true } ? VisualTreeInspector.Describe(c)["bounds"]?.DeepClone() : null;

    /// <summary>모델 dock 을 그리는 Dock 컨트롤(…DockControl, DataContext 가 그 dock). 없으면 null.</summary>
    private static Control? DockView(IDockable dock) =>
        VisualTreeInspector.Main().GetVisualDescendants().OfType<Control>()
            .FirstOrDefault(c => ReferenceEquals(c.DataContext, dock) && c.GetType().Name.EndsWith("DockControl", StringComparison.Ordinal));

    /// <summary>분할선 모델을 그리는 ProportionalStackPanelSplitter. 없으면 null.</summary>
    private static ProportionalStackPanelSplitter? SplitterView(IDockable splitter) =>
        VisualTreeInspector.Main().GetVisualDescendants().OfType<ProportionalStackPanelSplitter>()
            .FirstOrDefault(s => ReferenceEquals(s.DataContext, splitter)
                || s.GetVisualAncestors().OfType<Control>().TakeWhile(a => a is not ProportionalStackPanel).Any(a => ReferenceEquals(a.DataContext, splitter)));

    private static IEnumerable<IDockable> Walk(IDockable d)
    {
        yield return d;
        if (d is IDock { VisibleDockables: { } list })
        {
            foreach (var c in list.SelectMany(Walk))
            {
                yield return c;
            }
        }
    }

    private static double? Round4(double p) => double.IsNaN(p) ? null : Math.Round(p, 4);

    /// <summary>영역 1개 덤프: 감싸기 여부·방향·자리 비율·자리 사각형·패널(순서·비율·탭·활성·사각형)·영역 안 분할선·영역 바깥 경계 분할선.</summary>
    private static JsonObject AreaJson(PanelArea area)
    {
        var (wrapped, slotProportion, vertical, slotId) = Factory.AreaInfo(area);
        var panes = Factory.PanesOf(area);
        IDock? slot = slotId is null ? null : Factory.Root is { } r ? Factory.FindDockable(r, d => d.Id == slotId) as IDock : null;
        var inner = slot is { VisibleDockables: { } l } && wrapped ? l.Where(d => d is ISplitter).ToList() : new List<IDockable>();
        var outer = slot?.Owner is IDock { VisibleDockables: { } ol } && ol.IndexOf(slot) is var at and >= 0
            ? new[] { at - 1, at + 1 }.Where(i => i >= 0 && i < ol.Count && ol[i] is ISplitter).Select(i => ol[i]).FirstOrDefault()
            : null;
        return new JsonObject
        {
            ["wrapped"] = wrapped, ["vertical"] = vertical, ["slotId"] = slotId, ["slotProportion"] = Round4(slotProportion),
            ["slotBounds"] = slot is null ? null : BoundsOf(DockView(slot)),
            ["paneCount"] = panes.Count,
            ["panes"] = new JsonArray(panes.Select(p => (JsonNode?)new JsonObject
            {
                ["id"] = p.PaneId, ["tabsId"] = p.TabsId, ["index"] = p.Index, ["proportion"] = Round4(p.Proportion), ["isHome"] = p.IsHome,
                ["tabs"] = new JsonArray(p.Tabs.Select(t => (JsonNode?)JsonValue.Create(t)).ToArray()), ["active"] = p.Active,
                ["pinned"] = p.TabsId is { } tid && Factory.IsPanelPinned(tid),
                ["bounds"] = BoundsOf(PaneView(p.Pane)),
            }).ToArray()),
            ["splitters"] = new JsonArray(inner.Select(s => (JsonNode?)new JsonObject
            {
                ["bounds"] = BoundsOf(SplitterView(s)), ["canResize"] = (s as IProportionalDockSplitter)?.CanResize,
            }).ToArray()),
            ["outerSplitter"] = outer is null ? null : new JsonObject
            {
                ["bounds"] = BoundsOf(SplitterView(outer)), ["canResize"] = (outer as IProportionalDockSplitter)?.CanResize,
            },
        };
    }

    private static JsonObject SplitDump()
    {
        var all = Factory.Root is { } root ? Walk(root).ToList() : new List<IDockable>();
        var toolDocks = all.OfType<IToolDock>().ToList();
        var emptyToolDocks = toolDocks.Where(t => (t.VisibleDockables?.Count ?? 0) == 0).Select(t => (JsonNode?)JsonValue.Create(t.Id)).ToArray();
        var emptyPanels = Factory.SplitPaneIds.Where(id => (Factory.TabsOf(id)?.VisibleDockables?.Count ?? 0) == 0)
            .Select(id => (JsonNode?)JsonValue.Create(id)).ToArray();
        var docs = Factory.Documents is { } dd ? DockView(dd) : null;
        return new JsonObject
        {
            ["compact"] = Vm.Compact.IsCompact, ["touchFormFactor"] = Pd.Poc.Shell.Platform.InputFormFactor.IsTouchDevice, ["platform"] = OperatingSystem.IsBrowser() ? "web" : OperatingSystem.IsAndroid() ? "android" : "desktop",
            ["notificationEdge"] = Factory.PanelEdgeOf(PocDockFactory.NotificationPaneId)?.ToString(), ["sidePanelsHidden"] = Factory.SidePanelsHidden,
            ["toolDockCount"] = toolDocks.Count, ["emptyToolDocks"] = new JsonArray(emptyToolDocks), ["emptySplitPanels"] = new JsonArray(emptyPanels),
            ["splitPaneIds"] = new JsonArray(Factory.SplitPaneIds.Select(i => (JsonNode?)JsonValue.Create(i)).ToArray()),
            ["documentsBounds"] = BoundsOf(docs),
            ["areas"] = new JsonObject
            {
                ["Left"] = AreaJson(PanelArea.Explorer), ["Bottom"] = AreaJson(PanelArea.Log), ["Right"] = AreaJson(PanelArea.Notification), ["Extra"] = AreaJson(PanelArea.Extra),
            },
        };
    }

    /// <summary>놓기 대상: 패널은 안쪽 탭 영역(Dock 이 실제 가장자리 안내를 띄우는 곳).</summary>
    /// <summary>놓는 대상. doc:&lt;문서 Id&gt; 는 그 문서가 든 dock 이다(분리 창 합류 재현, U15).</summary>
    private static IDockable SplitTarget(string id) => id.StartsWith("doc:", System.StringComparison.Ordinal)
        ? Shell.Documents.FirstOrDefault(d => d.Id == id[4..])?.Owner ?? throw new RemoteCommandException("문서 없음: " + id)
        : Factory.TabsOf(id) as IDockable ?? FindDock(id);

    internal static void Register(RemoteCommandRouter r)
    {
        r.Register("split.dump", "UI-20 분할 덤프 → 영역(Left·Bottom·Right)별 wrapped·vertical·slotProportion·slotBounds·paneCount·panes[id·tabsId·index·proportion·isHome·tabs·active·pinned·bounds]·splitters[bounds·canResize]·outerSplitter + toolDockCount·emptyToolDocks·emptySplitPanels·compact·platform",
            _ => !EnsureRestored() ? null : SplitDump());
        r.Register("split.do", "UI-20 가장자리 놓기 분할(Dock DockManager.ValidateDockable Move 실행 — 끌어 놓기와 같은 경로) target=<패널 Id: LeftPane|BottomPane|NotificationPane|분할 패널 Id> op=Top|Bottom|Left|Right tool=<도구 Id>|doc=<문서 Id> → {status: Split|Rejected, policy, validate, dump}",
            async a =>
            {
                if (Vm.Compact.IsCompact)
                {
                    return Rejected("phone-mode"); // 바형 폰 모드는 Dock 화면이 없어 분할 진입점이 없다(lead 16:2x) — 원격으로도 만들지 않는다
                }

                var target = SplitTarget(a.ReqStr("target"));
                var source = a.Str("doc") is { } docId
                    ? (IDockable?)Shell.Documents.FirstOrDefault(d => d.Id == docId) ?? throw new RemoteCommandException("문서 없음: " + docId)
                    : FindDock(a.ReqStr("tool"));
                var op = a.EnumArg<DockOperation>("op") ?? throw new RemoteCommandException("op=Top|Bottom|Left|Right 가 필요합니다.");
                var policy = Factory.CanDrop(source, target, op);
                var dm = DockManager;
                var visible = dm.IsDockTargetVisible(source, target, op);
                var ok = dm.ValidateDockable(source, target, DragAction.Move, op, true);
                await AutomationBridge.WaitFramesAsync(VisualTreeInspector.Main(), 3); // 놓기 뒤 Post 된 정리(SyncSidePanels)·배치까지 기다린다
                return new JsonObject { ["status"] = ok ? "Split" : "Rejected", ["policy"] = policy, ["targetVisible"] = visible, ["validate"] = ok, ["dump"] = SplitDump() };
            });
        r.Register("split.drag", "UI-20 분할선 끌기 재현 area=Left|Bottom|Right at=outer(문서 영역과의 경계)|<영역 안 분할선 번호 0..> delta=<px, +는 오른쪽/아래> → ProportionalStackPanelSplitter 의 실제 크기 조절 코드(SetTargetProportion)를 부른다. 찾지 못하면 Rejected. → {status, via, before, after}",
            async a =>
            {
                if (!PanelAreaNames.TryParse(a.Str("area"), out var area))
                {
                    throw new RemoteCommandException("area=Left|Bottom|Right|Extra 가 필요합니다.");
                }

                var delta = a.Num("delta") ?? throw new RemoteCommandException("delta=<px> 가 필요합니다.");
                var before = AreaJson(area);
                var slotId = Factory.AreaInfo(area).SlotId;
                var slot = slotId is not null && Factory.Root is { } root ? Factory.FindDockable(root, d => d.Id == slotId) as IDock : null;
                IDockable? model = null;
                var at = a.Str("at") ?? "outer";
                if (at == "outer" && slot?.Owner is IDock { VisibleDockables: { } ol } && ol.IndexOf(slot) is var i and >= 0)
                {
                    model = new[] { i - 1, i + 1 }.Where(k => k >= 0 && k < ol.Count && ol[k] is ISplitter).Select(k => ol[k]).FirstOrDefault();
                }
                else if (int.TryParse(at, out var n) && slot is { VisibleDockables: { } l } && Factory.AreaInfo(area).Wrapped)
                {
                    model = l.Where(d => d is ISplitter).ElementAtOrDefault(n);
                }

                if (model is null || SplitterView(model) is not { } view)
                {
                    return Rejected("splitter-not-found");
                }

                var m = typeof(ProportionalStackPanelSplitter).GetMethod("SetTargetProportion", BindingFlags.Instance | BindingFlags.NonPublic);
                if (m is null)
                {
                    return Rejected("SetTargetProportion-missing(trimmed?)");
                }

                m.Invoke(view, new object[] { delta });
                await AutomationBridge.WaitFramesAsync(VisualTreeInspector.Main(), 3);
                return new JsonObject { ["status"] = "Dragged", ["via"] = "ProportionalStackPanelSplitter.SetTargetProportion", ["delta"] = delta, ["before"] = before, ["after"] = AreaJson(area) };
            });
        r.Register("split.capture", "UI-20 저장 캡처(CapturePanels — 레이아웃 저장과 같은 값) → explorer/log/notification 비율 + tabs[panelId·toolIds·active·area·index·proportion]",
            _ =>
            {
                var s = Factory.CapturePanels();
                return s is null ? null : new JsonObject
                {
                    ["explorerProportion"] = Round4(s.ExplorerProportion), ["logProportion"] = Round4(s.LogProportion),
                    ["notificationProportion"] = s.NotificationProportion is double np ? Round4(np) : null, ["notificationEdge"] = s.NotificationEdge,
                    ["tabs"] = new JsonArray((s.Tabs ?? new()).Select(t => (JsonNode?)new JsonObject
                    {
                        ["panelId"] = t.PanelId, ["toolIds"] = new JsonArray(t.ToolIds.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray()),
                        ["active"] = t.ActiveToolId, ["area"] = t.Area, ["index"] = t.Index, ["proportion"] = t.Proportion,
                    }).ToArray()),
                };
            });
    }
}
#endif
