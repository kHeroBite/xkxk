// 원격 sim.*(진단) — OS 활성화 없이 창 활성 처리기와 탭 클릭 분기를 불러 활성 표시·최소화 토글을 화면 밖에서 잰다(w12 Z4·Z5)
#if PD_DIAGNOSTICS // 진단 빌드 전용(PdDiagnostics=true)
using System.Linq;
using System.Reflection;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using Dock.Model.Core;
using xkxk.frameworks.ui.shell.Docking;
using xkxk.frameworks.ui.shell.Framework;

namespace xkxk.frameworks.ui.shell.Diagnostics.Remote;

/// <summary>
/// sim.winActive 는 Avalonia 내부 WindowBase.HandleActivated·HandleDeactivated 를 반사로 부른다(플랫폼이 WM_ACTIVATE 를 받았을 때 호출하는 처리기). OS 전경·활성 창은 바뀌지 않으므로 OS 활성 합격의 근거가 아니다.
/// sim.tabClick 은 메인 누름 기록 뒤 탭 클릭 명령을 실행한다. 포인터 입력은 만들지 않는다.
/// </summary>
internal static class RemoteSimActiveCommands
{
    internal static void Register(RemoteCommandRouter r)
    {
        r.Register("sim.winActive", "창 활성 처리기 호출(OS 활성화 아님) target=main|<문서 Id> on=true|false → sim.activeState 값", a =>
        {
            var window = FindWindow(a.ReqStr("target"));
            var name = a.Bool("on") ? "HandleActivated" : "HandleDeactivated";
            typeof(WindowBase).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
            return State();
        });
        r.Register("sim.activeState", "활성 표시 값 읽기 → {active, windows:[{title,isActive,frameActive}], mdi:[{id,isActive,state}]}", _ => State());
        r.Register("sim.tabClick", "메인 누름 기록 뒤 탭 클릭 명령 실행 doc=<문서 Id> press=true|false(기본 true) → sim.activeState 값", a =>
        {
            var shell = Shell();
            var doc = shell.OpenDocuments.FirstOrDefault(d => d.Id == a.ReqStr("doc")) ?? throw new RemoteCommandException("문서를 찾지 못했습니다.");
            if (a.Str("press") != "false")
            {
                shell.StateOps.NoteMainPressed();
            }

            shell.ActivateOrMinimize(doc);
            return State();
        });
        // w13 U4: 웹 Binding 새 종류의 원인을 가르려고 w12 의 활성 처리 2개를 각각 끈다(재기동 전까지 유지)
        r.Register("sim.switch", "진단 스위치 name=mdiActiveSync|mainPress off=true|false → {mdiActiveSyncOff, mainPressOff}", a =>
        {
            var off = a.Str("off") != "false";
            switch (a.ReqStr("name"))
            {
                case "mdiActiveSync": MdiActiveSync.DiagDisabled = off; break;
                case "mainPress": WindowStateOps.DiagPressDisabled = off; break;
                default: throw new RemoteCommandException("name 값이 잘못됐습니다.");
            }

            return new JsonObject { ["mdiActiveSyncOff"] = MdiActiveSync.DiagDisabled, ["mainPressOff"] = WindowStateOps.DiagPressDisabled };
        });
        // 패널 탭 메뉴는 열 때 붙어 dock.menuInvoke 로 찾지 못한다. 메뉴 항목이 바인딩한 Factory 명령을 같은 인자로 직접 부른다(Themes/MdiWindowMenu.axaml 와 같은 명령)
        r.Register("sim.tabMenu", "탭 메뉴 명령 실행 item=newHorizontal|newVertical|floatAll id=<문서·도구 Id> → {item,id}", a =>
        {
            var factory = RemoteDockAccess.Factory;
            var id = a.ReqStr("id");
            var dockable = factory.Root is { } root ? factory.FindDockable(root, d => d.Id == id && d is not IDock) : null;
            if (dockable is null)
            {
                throw new RemoteCommandException("문서·도구를 찾지 못했습니다.");
            }

            var item = a.ReqStr("item");
            switch (item)
            {
                case "newHorizontal": factory.NewHorizontalDocumentDock(dockable); break;
                case "newVertical": factory.NewVerticalDocumentDock(dockable); break;
                case "floatAll": factory.FloatAllDockables(dockable); break;
                default: throw new RemoteCommandException("item 값이 잘못됐습니다.");
            }

            return new JsonObject { ["item"] = item, ["id"] = id };
        });
    }

    private static WindowCommands Shell() => XkxkUi.Current.Shell as WindowCommands ?? throw new RemoteCommandException("셸이 없습니다.");

    private static Window FindWindow(string target)
    {
        var tops = VisualTreeInspector.TopLevels().OfType<Window>().ToList();
        if (target == "main")
        {
            return tops.FirstOrDefault(t => t is not HostWindow) ?? throw new RemoteCommandException("메인 창이 없습니다.");
        }

        var shell = Shell();
        var doc = shell.OpenDocuments.FirstOrDefault(d => d.Id == target) ?? throw new RemoteCommandException("문서를 찾지 못했습니다.");
        return tops.OfType<HostWindow>().FirstOrDefault(h => h.Window?.Layout is { } layout && LayoutSerializer.Contains(layout, doc))
            ?? throw new RemoteCommandException("그 문서의 분리 창이 없습니다.");
    }

    private static JsonObject State()
    {
        var tops = VisualTreeInspector.TopLevels().OfType<Window>().ToList();
        var windows = new JsonArray();
        foreach (var top in tops)
        {
            var frame = top.GetVisualDescendants().OfType<Border>().FirstOrDefault(b => b.Classes.Contains("pdSdiFrame"));
            windows.Add(new JsonObject { ["title"] = top.Title, ["sdi"] = top is HostWindow, ["isActive"] = top.IsActive, ["frameActive"] = frame?.Classes.Contains("active") });
        }

        var mdi = new JsonArray();
        // 웹은 TopLevel 이 Window 가 아니고 관리형 분리 창도 MdiDocumentWindow 라 모든 TopLevel 에서 찾는다(w13 U1·U2)
        foreach (var window in VisualTreeInspector.TopLevels().SelectMany(t => t.GetVisualDescendants().OfType<MdiDocumentWindow>()))
        {
            mdi.Add(new JsonObject
            {
                ["id"] = (window.DataContext as IDockable)?.Id, ["title"] = (window.DataContext as IDockable)?.Title, ["managed"] = window.DataContext is ManagedDockWindowDocument,
                ["isActive"] = window.IsActive, ["state"] = (window.DataContext as PocDocument)?.MdiState.ToString(),
            });
        }

        return new JsonObject { ["active"] = Shell().ActiveDocument?.Id, ["windows"] = windows, ["mdi"] = mdi };
    }
}
#endif
