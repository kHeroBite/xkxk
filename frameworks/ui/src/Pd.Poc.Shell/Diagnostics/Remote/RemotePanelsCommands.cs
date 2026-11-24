// 원격 명령(UI-14 패널 탭 합류·UI-15 알림 패널 — 패널 목록·가장자리 이동·탭·합류·드롭 판정) — PocDockFactory(odev-1)·Dock DockManager 공개 API 를 그대로 호출
#if PD_DIAGNOSTICS // 진단 빌드 전용(PdDiagnostics=true)
using System;
using System.Linq;
using System.Text.Json.Nodes;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using Dock.Model.Core;
using Pd.Poc.Shell.Docking;

namespace Pd.Poc.Shell.Diagnostics.Remote;

internal static class RemotePanelsCommands
{
    // 공용 멤버 전달자(W2 F) — 본문은 옛 이름 그대로 쓴다
    private static Pd.Poc.Shell.Docking.IShellCommands Shell => RemoteShellAccess.Shell;

    private static Pd.Poc.Shell.Docking.PocDocument Doc(JsonObject a, string key = "doc") => RemoteShellAccess.Doc(a, key);

    private static bool EnsureRestored() => RemoteShellAccess.EnsureRestored();

    private static PocDockFactory Factory => RemoteDockAccess.Factory;

    private static IDockable FindDock(string id) => RemoteDockAccess.FindDock(id);

    private static IDockManager DockManager => RemoteDockAccess.DockManager;

    private static JsonObject Rejected(string reason, string? side = null) => RemoteDockAccess.Rejected(reason, side);

    private static ToolDockControl? PaneView(IDockable dock) => RemoteDockAccess.PaneView(dock);

    // Factory·FindDock·DockManager·Rejected·PaneView 는 RemoteDockAccess 로 옮겼다(W2 S 단계). 아래 본문은 이 파일 위의 private 전달자를 거쳐 부른다.

    private static string SideOf(string id) => Factory.PanelEdgeOf(id)?.ToString() ?? "Unknown";

    /// <summary>
    /// 탭을 담는 도크: 측면 패널은 바깥 ToolDock("LeftPane") 안의 탭 영역("LeftPane.Tabs", PanelDocumentDock — odev-1 TabsOf),
    /// 그 밖(알림 등)은 Id 그대로. 바깥·안쪽 Id 모두 받는다.
    /// </summary>
    private static IDock? TabDock(string panelId) => Factory.TabsOf(panelId) as IDock ?? FindDock(panelId) as IDock;

    private static JsonObject TabsJson(string panelId)
    {
        var dock = TabDock(panelId);
        return new JsonObject
        {
            ["active"] = dock?.ActiveDockable?.Id,
            ["tabs"] = new JsonArray(Factory.PanelTabs(panelId).Select(t => (JsonNode?)new JsonObject
            {
                ["id"] = t, ["title"] = (Factory.Root is { } r ? Factory.FindDockable(r, d => d.Id == t)?.Title : null),
                ["active"] = dock?.ActiveDockable?.Id == t,
            }).ToArray()),
        };
    }

    // Rejected·PaneView 는 RemoteDockAccess 로 옮겼다(W2 S 단계).

    internal static void Register(RemoteCommandRouter r)
    {
        r.Register("panel.list", "도구 패널(LeftPane·BottomPane·NotificationPane): id·title·side·locked·visible·bounds(보일 때 — 측면 패널 포함)·tabs", _ => !EnsureRestored() ? null : // 기동 복원 중이면 NotReady(restoring) — 복원 전 패널 상태를 결과로 읽지 않게(lead 03:1x)
            new JsonArray(Factory.PanelIds.Select(id =>
            {
                var tabs = Factory.TabsOf(id);
                var o = new JsonObject
                {
                    // 제목은 활성 탭 제목이다(머리줄과 같다). 숨겨진 패널도 목록에 낸다(visible=false 는 아래에서 채운다).
                    ["id"] = id, ["title"] = tabs?.ActiveDockable?.Title, ["side"] = SideOf(id),
                    ["locked"] = false, // 위치 고정 패널은 없다
                };

                // 패널 사각형(웹·데스크톱·Android 공통). 숨김·접힘이면 트리에 없어 visible=false·bounds 없음
                var view = Factory.Root is { } r0 && Factory.FindDockable(r0, d => d.Id == id) is { } dk ? PaneView(dk) : null;
                o["visible"] = view?.IsEffectivelyVisible ?? false;
                if (view is { IsEffectivelyVisible: true }) { o["bounds"] = VisualTreeInspector.Describe(view)["bounds"]?.DeepClone(); }

                o["pinned"] = Factory.IsPanelPinned(id); // 핀(자동 숨김) 상태(odev-1 IsPanelPinned) — 트리 글자 대신 판정용
                o["tabs"] = TabsJson(id)["tabs"]?.DeepClone();
                return (JsonNode?)o;
            }).ToArray()));
        r.Register("panel.bounds", "화면 사각형(메인 창 DIP) → {LeftPane, BottomPane, NotificationPane, Documents(dock Id \"Documents\")} 각 {x,y,w,h} 또는 null(숨김·접힘) + DocumentsPick{dockId,dockType,control,match} + Documents_largest(참고)·docDocks", _ =>
        {
            EnsureRestored(); // 기동 복원 중이면 NotReady(restoring)
            var o = new JsonObject();
            foreach (var id in Factory.PanelIds)
            {
                Avalonia.Controls.Control? view = Factory.Root is { } r0 && Factory.FindDockable(r0, d => d.Id == id) is { } dk ? PaneView(dk) : null;
                o[id] = view is { IsEffectivelyVisible: true } ? VisualTreeInspector.Describe(view)["bounds"]?.DeepClone() : null;
            }

            // 문서 영역 = DataContext 의 dock Id 가 "Documents"(PocDockFactory.DocumentDockId)인 DocumentDockControl.
            // 이전엔 '보이는 가장 큰 DocumentDockControl'을 골라 패널 안쪽 탭 영역(LeftPane.Tabs·BottomPane.Tabs, 같은 형식)을 잡을 수 있었다(odev-1·lead, Android i0315 #125).
            // 가장 큰 것은 참고로만 Documents_largest 에 낸다. 고른 컨트롤의 dock Id·형식은 *Pick 에 낸다.
            var docViews = VisualTreeInspector.Main().GetVisualDescendants().OfType<DocumentDockControl>().Where(c => c.IsEffectivelyVisible).ToList();
            var doc = docViews.FirstOrDefault(c => (c.DataContext as IDockable)?.Id == PocDockFactory.DocumentDockId);
            var largest = docViews.OrderByDescending(c => c.Bounds.Width * c.Bounds.Height).FirstOrDefault();
            static JsonObject? Pick(DocumentDockControl? c) => c is null ? null : new JsonObject
            {
                ["dockId"] = (c.DataContext as IDockable)?.Id, ["dockType"] = c.DataContext?.GetType().Name, ["control"] = c.GetType().Name,
            };
            o["Documents"] = doc is null ? null : VisualTreeInspector.Describe(doc)["bounds"]?.DeepClone();
            o["DocumentsPick"] = Pick(doc);
            if (o["DocumentsPick"] is JsonObject dp) { dp["match"] = "dock-id"; }
            o["Documents_largest"] = largest is null ? null : VisualTreeInspector.Describe(largest)["bounds"]?.DeepClone();
            o["Documents_largestPick"] = Pick(largest);
            o["docDocks"] = new JsonArray(docViews.Select(c => (JsonNode?)Pick(c)).ToArray()); // 보이는 DocumentDockControl 전부(원인 분리용)
            return o;
        });
        r.Register("panel.close", "패널 닫기(머리줄 ✕ 와 같은 경로) id=LeftPane|BottomPane|NotificationPane → {status: Done, id}. 다시 열기는 보기 메뉴(shell.mnuShowExplorer·shell.mnuShowNotifications)", a =>
        {
            var id = a.ReqStr("id");
            Factory.CloseDockable(TabDock(id));
            return new JsonObject { ["status"] = "Done", ["id"] = id };
        });
        r.Register("panel.pin", "측면·알림 패널 핀(자동 숨김) id=LeftPane|BottomPane|NotificationPane on=true|false → {status: Done|Rejected, id, pinned}(접혀 있으면 Rejected)", a =>
        {
            var id = a.ReqStr("id");
            if (!a.ContainsKey("on")) { throw new RemoteCommandException("on=true|false 가 필요합니다."); }
            // 패널 기능 끔이면 다른 판정보다 먼저 전용 사유로 거절한다(끔이면 핀이 없다 — PanelFeatureGate.RefusesPin)
            var on = a.Bool("on");
            var featureOff = Factory.PanelFeature.RefusesPin(on);
            // odev-1 SetPanelPinned: 이미 같은 상태면 true, 집중 숨김으로 접혀 있으면 false
            var ok = !featureOff && Factory.SetPanelPinned(id, on);
            var o = ok ? new JsonObject { ["status"] = "Done" } : Rejected(featureOff ? PanelFeatureGate.PinOffReason : "panel-hidden", SideOf(id));
            o["id"] = id;
            o["pinned"] = Factory.IsPanelPinned(id);
            return o;
        });
        // Q15 진단(odev-1 PanelJoinDrag.Probe, 진단 빌드 전용): 점 아래 합류 대상 판정 과정 한 줄. 원격 처리는 UI 스레드 사이 틈에 돌아
        // 끌기 도중(누른 채 이동 중)에도 부를 수 있다 — pressed·moved·hover 를 그 순간 값으로 본다.
        r.Register("panel.joinProbe", "Q15 합류 대상 판정 진단 x= y=(메인 창 DIP) → {probe: installed·hidden·pressed·moved·hover·후보 ToolDockControl·target}", a =>
            new JsonObject
            {
                ["probe"] = PanelJoinDrag.Probe(VisualTreeInspector.Main(),
                    new Avalonia.Point(a.Num("x") ?? throw new RemoteCommandException("x= 가 필요합니다."),
                                       a.Num("y") ?? throw new RemoteCommandException("y= 가 필요합니다."))),
                // 작업 표시줄 탭(shell.tab) 끌기 상태(odev-2 TaskbarTabDrag.State): "doc=… dragging=… touch=… last=…" — 끌기 기구가 없으면 null
                ["tabDrag"] = Pd.Poc.Shell.Docking.TaskbarTabDrag.Current?.State,
            });
        r.Register("panel.show", "보기 메뉴 도구 드러내기 tool=Explorer|Log|Notifications(메뉴와 같은 ShowTool — 집중도 푼다) → {shown}", a => new JsonObject { ["shown"] = RemoteShellAccess.Vm.Commands.ShowTool(a.ReqStr("tool")) });
        r.Register("panel.tabs", "패널 탭 목록 id= → {active, tabs:[{id,title,active}]}", a => TabsJson(a.ReqStr("id")));
        r.Register("panel.activate", "패널 탭 활성화 id= tab= → panel.tabs 와 같은 응답", a =>
        {
            Factory.SetActiveDockable(FindDock(a.ReqStr("tab")));
            return TabsJson(a.ReqStr("id"));
        });
        r.Register("panel.dock", "패널 가장자리 이동 id= side=left|right|top|bottom|center → {status: Docked|Rejected, side, reason}(가운데는 Rejected, 알림 도구 Id 는 그 도구가 든 패널)", a =>
        {
            var id = a.ReqStr("id");
            if (id == ViewModels.NotificationPanelViewModel.ToolId && Factory.Notifications.Owner?.Id is { } holder)
            {
                id = holder;
            }

            var side = a.ReqStr("side");
            if (!Enum.TryParse<PanelEdge>(side, true, out var edge)) { return Rejected("center-not-allowed", SideOf(id)); }
            Factory.Moves.MovePanelToEdge(id, edge);
            var moved = string.Equals(SideOf(id), side, StringComparison.OrdinalIgnoreCase);
            return moved ? new JsonObject { ["status"] = "Docked", ["side"] = SideOf(id) } : Rejected("cannot-move", SideOf(id));
        });
        r.Register("panel.join", "탭 합류 id=<패널> doc=<문서 Id>(SendDocumentToPanel) | tool=<도구 Id>(Dock ValidateDockable Move·Fill 실행) → {status: Joined|Rejected, tabs}", a =>
        {
            var id = a.ReqStr("id");
            bool ok;
            if (a.Str("doc") is { } docId)
            {
                ok = Shell.Documents.FirstOrDefault(d => d.Id == docId) is { } doc && Factory.SendDocumentToPanel(doc, id);
            }
            else
            {
                // 도구 합류 드롭 대상은 안쪽 탭 영역(TabsOf) — 바깥 ToolDock 이 아니다(odev-1 00:1x 구조 변경)
                var target = TabDock(id) ?? throw new RemoteCommandException("탭 영역 없음: " + id);
                ok = DockManager.ValidateDockable(FindDock(a.ReqStr("tool")), target, DragAction.Move, DockOperation.Fill, true);
            }

            var o = TabsJson(id);
            o["status"] = ok ? "Joined" : "Rejected";
            return o;
        });
        r.Register("panel.returnDoc", "패널 탭에 있는 문서를 중앙 문서 영역으로 doc= → 성공 여부", a =>
            JsonValue.Create(Shell.Documents.FirstOrDefault(d => d.Id == a.ReqStr("doc")) is { } doc && Factory.ReturnPanelDocument(doc)));
        r.Register("panel.canDrop", "드롭 판정(실행 없음) source= target= [op=Fill|Left|Right|Top|Bottom|Window] → policy(셸 CanDrop)·validate(Dock ValidateDockable)·targetVisible(IsDockTargetVisible)", a =>
        {
            var source = FindDock(a.ReqStr("source"));
            // inner=true 면 측면 패널 Id 를 안쪽 탭 영역으로 바꿔 판정(실제 탭 합류 드롭 대상)
            var target = a.Bool("inner") ? TabDock(a.ReqStr("target")) ?? FindDock(a.ReqStr("target")) : FindDock(a.ReqStr("target"));
            var op = a.EnumArg<DockOperation>("op") ?? DockOperation.Fill;
            var dm = DockManager;
            return new JsonObject
            {
                ["op"] = op.ToString(), ["policy"] = Factory.CanDrop(source, target, op),
                ["validate"] = dm.ValidateDockable(source, target, DragAction.Move, op, false),
                ["targetVisible"] = dm.IsDockTargetVisible(source, target, op),
            };
        });
        r.Register("panel.resize",
            "분할선 끌기 원격 대리(모델 Proportion 조정 — 실제 마우스 끌기 아님) id=패널 Id px=(왼쪽·오른쪽 가장자리=너비, 위·아래 가장자리=높이 DIP) 또는 ratio=(0~1) → {status: Resized|Rejected, id, reason?, before, after, proportion, emulated}", async a =>
            {
                var id = a.ReqStr("id");
                if (Factory.PanelEdgeOf(id) is not { } edge) { return Rejected("unknown-panel"); }
                // 숨김 중(집중 모드)·접힘(트리에 없음)이면 거부 — 비율을 직접 쓰면 되살릴 때 숨기기 전 값과 충돌(odev-1)
                if (Factory.SidePanelsHidden) { return Rejected("panel-hidden", SideOf(id)); }
                var dock = Factory.Root is { } root ? Factory.FindDockable(root, d => d.Id == id) : null;
                if (dock is null || PaneView(dock) is not { } view || !view.IsEffectivelyVisible) { return Rejected("panel-hidden", SideOf(id)); }
                var before = VisualTreeInspector.Describe(view)["bounds"]?.DeepClone();
                var cur = edge is PanelEdge.Left or PanelEdge.Right ? view.Bounds.Width : view.Bounds.Height;
                var old = dock.Proportion;
                double target;
                if (a.Num("ratio") is { } ratio) { target = ratio; }
                else if (a.Num("px") is { } px && cur > 0 && !double.IsNaN(old) && old > 0) { target = old * px / cur; }
                else { return Rejected("need-px-or-ratio", SideOf(id)); }
                target = Math.Clamp(target, 0.05, 0.9);
                PocDockFactory.SetShare(dock, target); // CollapsedProportion 도 함께 — 끌기 뒤 남은 값이 새 비율을 덮지 않게(UI-20 SetShare)
                // 같은 부모 안 형제(분할선 제외) 중 비율이 정해진 것은 늘어난 만큼 줄여 합을 유지 — 분할선 끌기와 같은 효과
                if (!double.IsNaN(old) && dock.Owner is IDock { VisibleDockables: { } sibs })
                {
                    foreach (var s in sibs.Where(s => !ReferenceEquals(s, dock) && s is not Dock.Model.Controls.IProportionalDockSplitter && !double.IsNaN(s.Proportion)))
                    {
                        PocDockFactory.SetShare(s, Math.Max(0.05, s.Proportion - (target - old)));
                    }
                }

                await AutomationBridge.WaitFramesAsync(VisualTreeInspector.Main(), 2);
                return new JsonObject
                {
                    ["status"] = "Resized", ["id"] = id, ["before"] = before,
                    ["after"] = VisualTreeInspector.Describe(view)["bounds"]?.DeepClone(),
                    ["proportion"] = new JsonObject { ["before"] = double.IsNaN(old) ? null : old, ["after"] = target }, ["emulated"] = "model-proportion",
                };
            });
    }
}
#endif
