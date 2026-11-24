// 원격 제어용 시각 트리 조회 — 모든 최상위 창 열거, 컨트롤을 AutomationId/Name 으로 찾기, 트리를 JSON(타입·Id·텍스트·사각형)으로 덤프
#if PD_DIAGNOSTICS // 진단 빌드 전용(PdDiagnostics=true)
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using xkxk.frameworks.ui.shell.Docking;
using xkxk.frameworks.ui.Controls;

namespace xkxk.frameworks.ui.shell.Diagnostics.Remote;

public static class VisualTreeInspector
{
    private const int MaxNodes = 4000;

    /// <summary>tree.get 깊이 상한. 응답 직렬화 MaxDepth(RemoteProtocol, 1024)는 이 값의 2배(노드 1단 = JSON 2단)보다 커야 한다.</summary>
    public const int MaxDumpDepth = 400;

    /// <summary>최상위 창 전체: 데스크톱=메인+SDI 실 창(열린 순서), 브라우저=단일 뷰의 TopLevel.</summary>
    public static IReadOnlyList<TopLevel> TopLevels()
    {
        var list = new List<TopLevel>();
        switch (Application.Current?.ApplicationLifetime)
        {
            case IClassicDesktopStyleApplicationLifetime desktop:
                if (desktop.MainWindow is { } main) { list.Add(main); }
                list.AddRange(desktop.Windows.Where(w => !ReferenceEquals(w, desktop.MainWindow)));
                break;
            case ISingleViewApplicationLifetime { MainView: { } view } when TopLevel.GetTopLevel(view) is { } top:
                list.Add(top);
                break;
        }

        return list;
    }

    /// <summary>메인 TopLevel(첫 번째). 없으면 예외.</summary>
    /// <summary>
    /// 원격 준비 상태(health 용): TopLevel·뷰모델·셸이 모두 있으면 (true, null), 아니면 (false, 첫 결핍 no-toplevel|no-viewmodel|no-shell).
    /// TopLevel 목록은 UI 객체라 UI 스레드에서 확인하고, UI 가 1초 안에 답하지 않으면 (false, "ui-busy").
    /// </summary>
    public static async System.Threading.Tasks.Task<(bool Ready, string? Reason)> ReadyAsync()
    {
        var check = Avalonia.Threading.Dispatcher.UIThread.InvokeAsync<(bool, string?)>(() =>
            TopLevels().Count == 0 ? (false, "no-toplevel")
            : App.MainViewModel is null ? (false, "no-viewmodel")
            : App.Shell is null ? (false, "no-shell")
            : RemoteCommands.StartupRestoreDone == false ? (false, "restoring") // 기동 레이아웃 복원 중(셸 훅 — null 이면 판단 안 함)
            : (true, null)).GetTask();
        return await System.Threading.Tasks.Task.WhenAny(check, System.Threading.Tasks.Task.Delay(1000)) == check ? check.Result : (false, "ui-busy");
    }

    public static TopLevel Main() => TopLevels().FirstOrDefault()
        ?? throw new RemoteNotReadyException("no-toplevel", "표시된 창이 없습니다(앱 화면 준비 전 — 잠시 뒤 다시 시도).");

    public static string AutomationIdOf(Control c) => AutomationProperties.GetAutomationId(c) ?? "";

    /// <summary>문서 내용 뷰(DocumentViewLocator 가 만든 ZoomDecorator, Tag=문서). 화면에 붙어 있지 않으면 null.</summary>
    public static Control? DocumentHost(PocDocument doc) => s_hostCache is { } cache
        ? (cache.TryGetValue(doc, out var cached) ? cached : null)
        : TopLevels().SelectMany(t => t.GetVisualDescendants()).OfType<Control>().FirstOrDefault(z => z is IDocumentZoomHost && ReferenceEquals(z.Tag, doc));

    private static Dictionary<object, Control>? s_hostCache;

    /// <summary>
    /// 응답 1회(문서 목록 JSON) 동안 문서→내용 뷰 표를 시각 트리 1회 순회로 만들어 DocumentHost 가 재사용한다(진단 도구 비용 제거 — odev-perf Q5:
    /// 20창 docs.list run 503ms 는 문서마다 전 트리 순회 3~4회 탓). 결과는 순회 순서상 첫 일치라 기존과 같다. UI 스레드 전용, 범위 안에서 트리를 바꾸지 않는 읽기 전용 호출에만 쓴다.
    /// </summary>
    public static IDisposable CacheDocumentHosts()
    {
        if (s_hostCache is not null) { return NoopScope.Instance; } // 바깥 범위가 이미 표를 가졌다
        var map = new Dictionary<object, Control>(ReferenceEqualityComparer.Instance);
        foreach (var z in TopLevels().SelectMany(t => t.GetVisualDescendants()).OfType<Control>().Where(c => c is IDocumentZoomHost))
        {
            if (z.Tag is PocDocument d) { map.TryAdd(d, z); }
        }

        s_hostCache = map;
        return new HostCacheScope();
    }

    private sealed class HostCacheScope : IDisposable
    {
        public void Dispose() => s_hostCache = null;
    }

    private sealed class NoopScope : IDisposable
    {
        public static readonly NoopScope Instance = new();

        public void Dispose() { }
    }

    /// <summary>
    /// 문서 조작 범위: MDI 면 문서를 품은 MdiDocumentWindow(제목줄 버튼 포함), SDI 실 창이면 그 창 전체, 탭이면 문서 내용.
    /// </summary>
    public static Visual? DocumentScope(PocDocument doc)
    {
        if (DocumentHost(doc) is not { } host) { return null; }
        if (host.GetVisualAncestors().OfType<Dock.Avalonia.Controls.MdiDocumentWindow>().FirstOrDefault() is { } mdi) { return mdi; }
        var top = TopLevel.GetTopLevel(host);
        return top is not null && !ReferenceEquals(top, TopLevels().FirstOrDefault()) ? top : host;
    }

    /// <summary>
    /// 화면상의 MDI 쌓임 순서: (ZIndex, 부모 패널 안 자식 순번). 렌더는 ZIndex → 자식 순서로 위에 그려진다. MDI 창이 아니면 null.
    /// </summary>
    public static (int ZIndex, int ChildIndex)? MdiStacking(PocDocument doc)
    {
        if (DocumentHost(doc)?.GetVisualAncestors().OfType<Dock.Avalonia.Controls.MdiDocumentWindow>().FirstOrDefault() is not { } mdi)
        {
            return null;
        }

        var parent = mdi.GetVisualParent();
        var index = parent is null ? -1 : parent.GetVisualChildren().ToList().IndexOf(mdi);
        return (mdi.ZIndex, index);
    }

    /// <summary>문서가 들어 있는 TopLevel(메인 또는 SDI 실 창).</summary>
    public static TopLevel? TopLevelOf(PocDocument doc) => DocumentHost(doc) is { } h ? TopLevel.GetTopLevel(h) : null;

    /// <summary>
    /// 컨트롤 찾기. 우선순위: AutomationId 일치 → Name 일치. root 가 없으면 모든 창을 순서대로 본다.
    /// "id#2" 처럼 쓰면 같은 Id 의 2번째(1부터) — 같은 문서를 여러 개 열었을 때 구분용.
    /// </summary>
    public static Control? Find(string id, Visual? root = null)
    {
        var nth = 1;
        var hash = id.LastIndexOf('#');
        if (hash > 0 && int.TryParse(id[(hash + 1)..], out var n) && n > 0) { nth = n; id = id[..hash]; }

        var roots = root is null ? TopLevels().Cast<Visual>() : new[] { root };
        var all = roots.SelectMany(r => r.GetSelfAndVisualDescendants()).OfType<Control>().ToList();
        var byId = all.Where(c => AutomationIdOf(c) == id).ToList();
        var hits = byId.Count > 0 ? byId : all.Where(c => c.Name == id).ToList();
        return hits.Count >= nth ? hits[nth - 1] : null;
    }

    public static Control Require(string id, Visual? root = null) =>
        Find(id, root) ?? throw new RemoteCommandException($"컨트롤을 찾지 못했습니다: {id} (tree.get 으로 Id 확인)");

    /// <summary>컨트롤의 표시 텍스트(TextBlock/TextBox/Content 문자열/Header/선택 항목). 없으면 null.</summary>
    public static string? TextOf(Control c) => c switch
    {
        TextBlock tb => tb.Text,
        TextBox tx => tx.Text,
        // 선택 항목이 컨트롤이면(Menu 의 선택 = FileMenu 등) 형식 이름(ToString)이 아니라 그 컨트롤의 표시 텍스트 규칙을 따른다(D-C2-26: 'xkxk.frameworks.ui.views.Menus.FileMenu' 오표기)
        SelectingItemsControl s => s.SelectedItem is Control sc ? TextOf(sc) : s.SelectedItem?.ToString(),
        // 그리는 글자 셀(K15 GridDrawnCellText — 셀 내용 없음, 글자는 셀 뿌리가 그림): 셀 자동화 이름이 그 글자다(PdTextColumn.ApplyCellText)
        DataGridCell { Content: null } dc => AutomationProperties.GetName(dc) is { Length: > 0 } dn ? dn : null,
        HeaderedContentControl { Header: string h } => h,
        ContentControl { Content: string s2 } => s2,
        ContentControl { Content: TextBlock t2 } => t2.Text,
        _ => null,
    };

    /// <summary>
    /// 트리 덤프. named=true(기본)면 Id·Name·텍스트가 없는 노드와 템플릿 내부(PART_*)는 건너뛰고 자식만 올린다.
    /// bounds 는 해당 TopLevel 기준 DIP 좌표.
    /// </summary>
    public static JsonObject Dump(Visual root, bool named = true, int maxDepth = 60)
    {
        var top = TopLevel.GetTopLevel(root);
        maxDepth = Math.Clamp(maxDepth, 1, MaxDumpDepth);
        var count = 0;
        var children = new JsonArray();
        Walk(root, top, named, maxDepth, 0, children, ref count);
        return new JsonObject
        {
            ["root"] = root.GetType().Name,
            ["nodes"] = count,
            ["truncated"] = count >= MaxNodes,
            ["tree"] = children,
        };
    }

    private static void Walk(Visual v, TopLevel? top, bool named, int maxDepth, int depth, JsonArray into, ref int count)
    {
        if (count >= MaxNodes || depth > maxDepth) { return; }
        var target = into;
        if (v is Control c && (!named || IsInteresting(c)))
        {
            var node = Describe(c, top);
            count++;
            var kids = new JsonArray();
            node["children"] = kids;
            into.Add(node);
            target = kids;
        }

        // 그리는 글자 셀은 글자 TextBlock 이 없다 — 시험이 셀 글자를 TextBlock 노드로 읽으므로 같은 모양의 노드(drawn=true, 사각형은 셀)를 낸다
        if (v is DataGridCell { Content: null } dc && TextOf(dc) is { } drawnText && count < MaxNodes)
        {
            var tn = Describe(dc, top);
            tn["type"] = nameof(TextBlock);
            tn["text"] = drawnText;
            tn["drawn"] = true;
            tn.Remove("classes");
            target.Add(tn);
            count++;
        }

        foreach (var child in v.GetVisualChildren())
        {
            Walk(child, top, named, maxDepth, depth + 1, target, ref count);
        }

        if (target != into && target.Count == 0 && target.Parent is JsonObject parent) { parent.Remove("children"); }
    }

    private static bool IsInteresting(Control c)
    {
        if (c.Name is { } n && n.StartsWith(TemplateParts.Prefix, StringComparison.Ordinal) && AutomationIdOf(c).Length == 0) { return false; }
        // 앱 고유 스타일 클래스(pd 로 시작 — 예 pdJoinTarget)가 붙은 컨트롤은 이름이 없어도 named 트리에 낸다(Q15 강조 확인)
        if (c.Classes.Any(x => x.StartsWith("pd", StringComparison.Ordinal))) { return true; }
        return AutomationIdOf(c).Length > 0 || !string.IsNullOrEmpty(c.Name) || c is Button or TextBox or ToggleButton
            or SelectingItemsControl or Slider || (c is TextBlock tb && !string.IsNullOrWhiteSpace(tb.Text));
    }

    /// <summary>컨트롤 1개 설명: type·id·name·text·enabled·visible·bounds(+checked/selectedIndex/value).</summary>
    public static JsonObject Describe(Control c, TopLevel? top = null)
    {
        top ??= TopLevel.GetTopLevel(c);
        var o = new JsonObject { ["type"] = c.GetType().Name };
        if (AutomationIdOf(c) is { Length: > 0 } id) { o["id"] = id; }
        if (!string.IsNullOrEmpty(c.Name)) { o["name"] = c.Name; }
        if (TextOf(c) is { } text) { o["text"] = text; }
        o["enabled"] = c.IsEffectivelyEnabled;
        o["visible"] = c.IsEffectivelyVisible; // 기존 키(호환) — 값은 IsEffectivelyVisible(부모가 숨으면 false)
        o["isVisible"] = c.IsVisible; // 자기 IsVisible 만(otest-m-1 01:5x 요청: 부모 숨김과 구분)
        o["effectivelyVisible"] = c.IsEffectivelyVisible;
        // 스타일 클래스(StyledElement.Classes — ':' 로 시작하는 것은 의사 클래스). 비었으면 생략해 트리 크기를 줄인다(lead 03:0x — Q15 pdJoinTarget 확인)
        if (c.Classes.Count > 0) { o["classes"] = new JsonArray(c.Classes.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray()); }
        switch (c)
        {
            case ToggleButton t: o["checked"] = t.IsChecked; break;
            case SelectingItemsControl s: o["selectedIndex"] = s.SelectedIndex; o["itemCount"] = s.ItemCount; break;
            case RangeBase r: o["value"] = r.Value; break;
        }

        var p = top is null ? null : c.TranslatePoint(default, top);
        o["bounds"] = new JsonObject
        {
            ["x"] = Math.Round(p?.X ?? c.Bounds.X, 1), ["y"] = Math.Round(p?.Y ?? c.Bounds.Y, 1),
            ["w"] = Math.Round(c.Bounds.Width, 1), ["h"] = Math.Round(c.Bounds.Height, 1),
        };
        return o;
    }
}
#endif
