// 원격 명령 공용 — 셸·뷰모델 접근, 문서 지정(doc 인자)·문서 JSON, 기동 복원 대기(옛 RemoteCommands.Windows·Ready·WindowMenu 의 공용 멤버)
#if PD_DIAGNOSTICS // 진단 빌드 전용(PdDiagnostics=true)
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using xkxk.frameworks.ui.shell.Docking;
using xkxk.frameworks.ui.shell.Framework;

namespace xkxk.frameworks.ui.shell.Diagnostics.Remote;

/// <summary>여러 원격 명령 군이 함께 쓰는 앱 서비스 접근과 문서 조회. 명령 등록은 하지 않는다.</summary>
internal static class RemoteShellAccess
{
    internal static IShellCommands Shell => XkxkUi.Current.Shell ?? throw new RemoteNotReadyException("no-shell", "셸이 아직 준비되지 않았습니다.");

    internal static ViewModels.MainViewModel Vm => XkxkUi.Current.MainViewModel ?? throw new RemoteNotReadyException("no-viewmodel", "뷰모델이 아직 없습니다.");

    /// <summary>문서 지정: doc = Id | 제목 | 1부터 순번. 생략하면 활성 문서(없으면 마지막 문서).</summary>
    internal static PocDocument Doc(JsonObject a, string key = "doc")
    {
        var docs = Shell.Documents;
        var s = a.Str(key);
        if (string.IsNullOrEmpty(s))
        {
            return Shell.ActiveDocument ?? (docs.Count > 0 ? docs[^1] : throw new RemoteCommandException("열린 문서가 없습니다."));
        }

        return docs.FirstOrDefault(d => d.Id == s) ?? docs.FirstOrDefault(d => d.Title == s)
            ?? (int.TryParse(s, out var i) && i >= 1 && i <= docs.Count ? docs[i - 1] : null)
            ?? throw new RemoteCommandException($"문서를 찾지 못했습니다: {s} (docs.list 로 확인)");
    }

    internal static JsonObject DocJson(PocDocument d)
    {
        var tops = VisualTreeInspector.TopLevels();
        var top = VisualTreeInspector.TopLevelOf(d);
        var idx = top is null ? -1 : IndexOf(tops, top);
        var stack = VisualTreeInspector.MdiStacking(d); // 실제 렌더 쌓임(MDI 만). 저장 값 ZOrder 는 layout.get 의 Documents[].ZOrder
        var floating = IsFloating(d, idx);
        // zIndex 가 비는 이유(진단, team-lead 23:4x 승인): 분리창 / 문서 래퍼 없음 / 래퍼 조상에 MDI 창 없음
        var zReason = stack is not null ? "ok" : floating ? "floating"
            : VisualTreeInspector.DocumentHost(d) is null ? "noHost" : "notMdi";
        // 패널 탭 문서(Q12 ④, lead 승인): Dock 모델의 패널 탭 순번(PanelDocumentDock.VisibleDockables, 0=첫 탭 — 저장 Panels.Tabs[].ToolIds 순서로 복원)을
        // zIndex 로 준다. 척도는 zScale=panelTab(MDI 전역 MRU 와 비교 금지). zReason 은 notMdi 유지 — w3c 규칙 M 이 이 값으로 패널 탭을 뺀다.
        var panel = stack is null && !floating ? PocDockFactory.PanelOf(d) : null;
        int? tabIndex = panel is not null && d.Owner is global::Dock.Model.Core.IDock { VisibleDockables: { } tabs } && tabs.IndexOf(d) is var t and >= 0 ? t : null;
        var json = new JsonObject
        {
            ["zIndex"] = stack?.ZIndex ?? tabIndex, ["childIndex"] = stack?.ChildIndex, ["zReason"] = zReason,
            ["modelZIndex"] = d.MdiZIndex, // Dock 모델 값(화면 zIndex·저장 ZOrder 와 3자 대조용)
            ["id"] = d.Id, ["title"] = d.Title, ["kind"] = d.Kind.ToString().ToLowerInvariant(), ["seq"] = d.Seq,
            ["active"] = ReferenceEquals(d, Shell.ActiveDocument), ["state"] = d.MdiState.ToString(),
            // floating 은 셸 판정(Dock 플로팅 창 소속)을 쓴다. 브라우저 관리형 창은 메인 TopLevel 안에 그려져 idx 로는 판별 불가(otest-w-2 22:4x 제보)
            ["window"] = idx, ["floating"] = floating, ["managedWindow"] = floating && global::Dock.Settings.DockSettings.UseManagedWindows, // 열린 직후(아직 미배치)에도 맞게 — Android 23:28 실측 보정
            ["visible"] = top is not null, ["zoom"] = d.Zoom, ["project"] = d.Project,
            ["managedBounds"] = RemoteSdiZ.ManagedBounds(d), // 관리형 SDI 창 사각형{x,y,w,h(메인 창 DIP)·pxW·pxH·inContainer·boundsSource}, 그 밖은 null(Q6 #7)
            ["dockWindow"] = RemoteSdiZ.DockWindowJson(d), // Dock 창 모델 IDockWindow{x,y,w,h,state}(SDI 저장 값의 출처), 도킹 문서는 null
        };
        if (tabIndex is not null)
        {
            json["zScale"] = "panelTab"; // RemoteSdiZ.ApplySdiZ 가 mdi 로 덮지 않는다
            json["zSource"] = "model";
            json["zPanel"] = panel;
        }

        return json;
    }

    /// <summary>SDI(플로팅) 여부: 셸 구현이 WindowCommands 면 그 판정(OS 창·관리형 창 공통), 아니면 별도 TopLevel 소속 여부.</summary>
    internal static bool IsFloating(PocDocument d, int windowIndex) =>
        Shell is WindowCommands wc ? wc.IsFloating(d) : windowIndex > 0;

    private static int IndexOf(IReadOnlyList<TopLevel> tops, TopLevel t)
    {
        for (var i = 0; i < tops.Count; i++) { if (ReferenceEquals(tops[i], t)) { return i; } }
        return -1;
    }

    internal static JsonArray DocsJson()
    {
        EnsureRestored(); // 기동 복원 중이면 NotReady(restoring) — 복원 전 문서 목록을 결과로 읽지 않게
        using var hosts = VisualTreeInspector.CacheDocumentHosts(); // 문서마다 전 트리 순회 대신 1회(진단 도구 비용, odev-perf Q5)
        var arr = new JsonArray(Shell.Documents.Select(d => (JsonNode)DocJson(d)).ToArray());
        RemoteSdiZ.ApplySdiZ(arr); // SDI 문서에도 zIndex(SDI 끼리 순위, zScale=sdi·zSource) — Q12 'zIndex null 0건'(lead 02:3x)
        return arr;
    }

    /// <summary>기동 복원 중이면 NotReady(reason=restoring), 아니면 true. 식 람다 앞에서 `EnsureRestored() ? … : null` 꼴로 쓴다.</summary>
    internal static bool EnsureRestored() => RemoteCommands.StartupRestoreDone != false
        ? true
        : throw new RemoteNotReadyException("restoring", "기동 레이아웃 복원 중입니다(패널·문서 상태가 아직 저장 상태가 아님 — 잠시 뒤 다시 시도).");
}
#endif
