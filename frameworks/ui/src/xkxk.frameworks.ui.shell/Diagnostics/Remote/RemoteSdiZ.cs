// 원격 명령 공용 SDI 쌓임 순서(Q12, 옛 RemoteCommands.SdiZ) — SDI 문서에도 zIndex 를 준다: 호스트 OS 창 순서(데스크톱) → 관리형 창 시각 순서(웹·Android) → 모델(저장 ZOrder·핀) 순으로 출처를 고른다
#if PD_DIAGNOSTICS // 진단 빌드 전용(PdDiagnostics=true)
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.VisualTree;
using xkxk.frameworks.ui.shell.Docking;

namespace xkxk.frameworks.ui.shell.Diagnostics.Remote;

/// <summary>docs.list·문서 응답의 SDI 쌓임 순위 계산과 관리형 SDI 창 조회(옛 RemoteCommands.SdiZ). 명령 등록은 하지 않는다. 훅 SdiZOrderProbe 는 RemoteCommands 에 있다.</summary>
internal static class RemoteSdiZ
{
    /// <summary>
    /// docs.list 결과의 SDI 문서(floating=true)에 zIndex(0=맨 아래, SDI 끼리의 순위)·zScale="sdi"·zSource 를 채운다. MDI 문서는 zScale="mdi"(값 그대로).
    /// 척도가 다르다: MDI zIndex = MDI 창 레이어 안 화면 쌓임, SDI zIndex = SDI 창끼리의 순위(OS 창 또는 관리형 창). 둘을 서로 비교하지 않는다.
    /// zSource: os(호스트 OS Z 순서) | visual(관리형 창 시각 순서: 조상 ZIndex·자식 순번) | model(저장 규칙 — layout ZOrder, 항상 위 창은 위).
    /// </summary>
    internal static void ApplySdiZ(JsonArray docs)
    {
        var items = docs.OfType<JsonObject>().ToList();
        foreach (var o in items.Where(o => o["floating"]?.GetValue<bool>() != true && o["zScale"] is null)) { o["zScale"] = "mdi"; } // 패널 탭(zScale=panelTab, DocJson)은 그대로
        var sdi = items.Where(o => o["floating"]?.GetValue<bool>() == true).ToList();
        if (sdi.Count == 0) { return; }

        var byId = RemoteShellAccess.Shell.Documents.ToDictionary(d => d.Id);
        string source;
        List<JsonObject> ordered; // 아래→위
        if (RemoteCommands.SdiZOrderProbe?.Invoke() is { Count: > 0 } os)
        {
            source = "os";
            var rank = os.Select((id, i) => (id, i)).ToDictionary(t => t.id, t => t.i);
            ordered = sdi.OrderBy(o => rank.TryGetValue(Id(o), out var r) ? r : -1).ToList(); // 호스트가 모르는 창은 맨 아래
        }
        else if (sdi.All(o => byId.TryGetValue(Id(o), out var d) && ManagedKey(d) is not null))
        {
            source = "visual";
            ordered = sdi.OrderBy(o => ManagedKey(byId[Id(o)])!.Value.Z).ThenBy(o => ManagedKey(byId[Id(o)])!.Value.Child).ToList();
        }
        else
        {
            source = "model";
            var saved = JsonNode.Parse(RemoteShellAccess.Shell.SerializeLayout())?["Documents"]?.AsArray()
                .OfType<JsonObject>().Where(x => x["Id"] is not null)
                .ToDictionary(x => x["Id"]!.GetValue<string>(), x => x["ZOrder"]?.GetValue<int?>() ?? 0) ?? new Dictionary<string, int>();
            ordered = sdi.OrderBy(o => byId.TryGetValue(Id(o), out var d) && WindowCommands.ChromeOf(d).IsPinned ? 1 : 0) // 항상 위는 위
                .ThenBy(o => saved.TryGetValue(Id(o), out var z) ? z : 0).ToList();
        }

        for (var i = 0; i < ordered.Count; i++)
        {
            ordered[i]["zIndex"] = i;
            ordered[i]["zScale"] = "sdi";
            ordered[i]["zSource"] = source;
            ordered[i]["zReason"] = "sdi";
        }
    }

    private static string Id(JsonObject o) => o["id"]?.GetValue<string>() ?? "";

    /// <summary>
    /// 문서 1개 응답(docs.activate·docs.rename·status.active 등)에도 SDI 순위(zIndex·zScale·zSource)를 채운다 — SDI 순위는 문서 목록 전체를 놓고 매기므로
    /// docs.list 와 같은 계산을 한 뒤 그 문서 항목을 꺼낸다(odev-4b 03:5x: docs.activate 응답 zIndex=null·zReason=floating).
    /// </summary>
    internal static JsonObject DocJsonZ(PocDocument d) =>
        RemoteShellAccess.DocsJson().OfType<JsonObject>().FirstOrDefault(o => Id(o) == d.Id)?.DeepClone() as JsonObject ?? RemoteShellAccess.DocJson(d);

    /// <summary>여러 문서 응답(docs.open count=n)을 한 번의 목록 계산으로 SDI 순위까지 채운다.</summary>
    internal static JsonArray DocsJsonZ(IEnumerable<PocDocument> docs)
    {
        var all = RemoteShellAccess.DocsJson().OfType<JsonObject>().ToDictionary(Id);
        return new JsonArray(docs.Select(d => all.TryGetValue(d.Id, out var o) ? o.DeepClone() : RemoteShellAccess.DocJson(d)).ToArray());
    }

    /// <summary>관리형 SDI 창(메인 TopLevel 안)의 시각 쌓임 키: 관리형 창 컨트롤의 (ZIndex, 부모 안 순번).</summary>
    private static (int Z, int Child)? ManagedKey(PocDocument d) =>
        ManagedWindowControl(d) is { } c && c.GetVisualParent() is Panel p ? (c.ZIndex, p.Children.IndexOf(c)) : null;

    /// <summary>
    /// 관리형 SDI 창(웹·Android — 메인 TopLevel 안에 그려짐)의 창 컨트롤: 문서 화면에서 위로 올라가며 이름에 Managed 가 든 첫 컨트롤(부모가 Panel).
    /// OS 창 SDI·MDI 문서는 null. 사각형은 VisualTreeInspector.Describe 의 bounds(메인 창 DIP)로 쓴다(otest-w-2 03:0x — Q6 #7 웹 판정).
    /// </summary>
    internal static Control? ManagedWindowControl(PocDocument d)
    {
        if (!global::Dock.Settings.DockSettings.UseManagedWindows || !RemoteShellAccess.IsFloating(d, -1) || VisualTreeInspector.DocumentHost(d) is not { } host) { return null; }
        // 개별 창 = 관리형 창 레이어(MdiLayoutPanel)의 자식인 조상. 이름에 Managed 가 든 첫 조상을 고르던 예전 방식은 레이어(컨테이너 전체)를 잡아
        // 두 창이 모두 {0,114,1280,649} 로 나왔다(otest-w-2 i0315 웹 — Q6 #7 판정 불가). 창 안에 다시 MDI 가 있을 수 있어 가장 바깥(루트 쪽) 것을 고른다.
        // MdiLayoutPanel 형식의 네임스페이스는 확인하지 못해 형식 이름으로 비교한다(MagnetDriver 도 같은 패널로 관리형 창을 다룬다 — odev-2).
        return host.GetVisualAncestors().OfType<Control>()
            .LastOrDefault(c => c.GetVisualParent() is { } p && p.GetType().Name == "MdiLayoutPanel");
    }

    /// <summary>
    /// 관리형 SDI 창 사각형(lead 04:5x): x·y·w·h = 메인 창(화면) 기준 DIP — 데스크톱 OS 창 항목과 같은 필드 이름,
    /// pxW·pxH = w·h × RenderScaling 반올림(물리 px), inContainer{x,y,w,h} = 관리형 창 레이어(MdiLayoutPanel) 기준 DIP, boundsSource="managed-window".
    /// 관리형 창이 아니면 null.
    /// </summary>
    internal static JsonObject? ManagedBounds(PocDocument d)
    {
        if (ManagedWindowControl(d) is not { } c || VisualTreeInspector.Describe(c)["bounds"] is not JsonObject b) { return null; }
        var s = TopLevel.GetTopLevel(c)?.RenderScaling ?? 1.0;
        return new JsonObject
        {
            ["x"] = b["x"]?.DeepClone(), ["y"] = b["y"]?.DeepClone(), ["w"] = b["w"]?.DeepClone(), ["h"] = b["h"]?.DeepClone(),
            ["pxW"] = Math.Round(c.Bounds.Width * s), ["pxH"] = Math.Round(c.Bounds.Height * s),
            ["inContainer"] = new JsonObject
            {
                ["x"] = Math.Round(c.Bounds.X, 1), ["y"] = Math.Round(c.Bounds.Y, 1),
                ["w"] = Math.Round(c.Bounds.Width, 1), ["h"] = Math.Round(c.Bounds.Height, 1),
            },
            ["boundsSource"] = "managed-window",
        };
    }

    /// <summary>
    /// Dock 창 모델 값(IDockWindow X/Y/Width/Height/WindowState) — 읽기 전용. 웹 관리형 창은 ManagedHostWindow.SetPosition/SetSize,
    /// 데스크톱은 호스트 OS 창이 갱신한다. 레이아웃 저장(Documents[].Window)이 이 값을 쓴다. 도킹(MDI) 문서·루트 미초기화면 null.
    /// </summary>
    internal static JsonObject? DockWindowJson(PocDocument d)
    {
        if (RemoteDockAccess.Factory.Root is not { } root || LayoutSerializer.FindWindowOf(root, d) is not { } w) { return null; }
        return new JsonObject
        {
            ["x"] = Math.Round(w.X, 1), ["y"] = Math.Round(w.Y, 1), ["w"] = Math.Round(w.Width, 1), ["h"] = Math.Round(w.Height, 1),
            ["state"] = w.WindowState.ToString(), ["host"] = w.Host?.GetType().Name,
        };
    }
}
#endif
