// 원격 명령(상단 메뉴 아이콘 판정 B1) — menu.icons 항목별 아이콘 종류·키·리소스 존재, menu.render 하위 메뉴 팝업을 PNG 로 렌더
#if PD_DIAGNOSTICS // 진단 빌드 전용(PdDiagnostics=true) — 제품판에는 이 명령이 없다
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;

namespace xkxk.frameworks.ui.shell.Diagnostics.Remote;

/// <summary>
/// 셸 층은 화면 층(WindowIcon·SnapCellGlyph·MenuIcon)을 참조할 수 없으므로 형식 이름·속성 이름으로만 읽는다(진단 전용 리플렉션).
/// menu.icons 는 읽기 전용(팝업을 열지 않는다 — 하위 항목은 논리 자식 MenuItem 이라 닫힌 채로 스타일이 적용돼 있다).
/// menu.render 는 대상 메뉴(와 조상 메뉴)를 잠깐 열어 팝업 내용을 RenderTargetBitmap 으로 그린 뒤 원래대로 닫는다.
/// </summary>
internal static class RemoteMenuIconCommands
{
    private static readonly string[] TopMenus = { "shell.mnuFile", "shell.mnuWindow", "shell.mnuView", "shell.mnuSys" };

    internal static void Register(RemoteCommandRouter r)
    {
        r.Register("menu.icons", "상단 메뉴 아이콘(B1) id=<상위 메뉴 AutomationId, 생략 시 파일·창·보기·시스템 4개> → {menus:[{id,items[{id,header,depth,visible,enabled,iconKind:window|snap|doc|none,iconKey,geometryFound,iconPresent}],total,withIcon,missing[]}]}. missing = 보이는 항목 중 아이콘 없음·리소스 없음. 읽기 전용", a =>
        {
            var ids = a.Str("id") is { Length: > 0 } one ? new[] { one } : TopMenus;
            return new JsonObject { ["menus"] = new JsonArray(ids.Select(id => (JsonNode)Describe(id)).ToArray()) };
        });
        r.Register("menu.render", "메뉴 팝업 PNG 렌더(B1 눈 확인) id=<메뉴 AutomationId(하위 메뉴 부모도 가능)> [out=<png 경로, 생략 시 앱 데이터\\shots>] [scale=배율, 기본 창 배율] → {path,w,h,items}. 조상 메뉴를 열고 그린 뒤 닫는다", RenderAsync);
    }

    private static JsonObject Describe(string id)
    {
        var top = FindMenu(id) ?? throw new RemoteCommandException($"메뉴를 찾지 못했습니다: {id}");
        var items = new JsonArray();
        var missing = new JsonArray();
        int total = 0, withIcon = 0;
        Walk(top, 1, items, missing, ref total, ref withIcon);
        return new JsonObject { ["id"] = id, ["header"] = MenuHeaderText.Of(top.Header), ["items"] = items, ["total"] = total, ["withIcon"] = withIcon, ["missing"] = missing };
    }

    private static void Walk(MenuItem parent, int depth, JsonArray into, JsonArray missing, ref int total, ref int withIcon)
    {
        foreach (var raw in parent.Items)
        {
            var mi = raw as MenuItem ?? parent.ContainerFromItem(raw!) as MenuItem;
            if (mi is null)
            {
                continue; // 구분선·아직 만들어지지 않은 ItemsSource 항목
            }

            var (kind, key, found, present) = IconOf(mi);
            var aid = VisualTreeInspector.AutomationIdOf(mi);
            total++;
            var ok = kind != "none" && present && found != false;
            if (ok) { withIcon++; }
            if (!ok && mi.IsVisible) { missing.Add((JsonNode?)JsonValue.Create(aid.Length > 0 ? aid : MenuHeaderText.Of(mi.Header))); } // 제네릭 Add<string> 은 웹(리플렉션 메타데이터 없음)에서 NotSupportedException
            into.Add(new JsonObject
            {
                ["id"] = aid,
                ["header"] = MenuHeaderText.Of(mi.Header),
                ["depth"] = depth,
                ["visible"] = mi.IsVisible,
                ["enabled"] = mi.IsEffectivelyEnabled,
                ["iconKind"] = kind,
                ["iconKey"] = key,
                ["geometryFound"] = found,
                ["iconPresent"] = present,
                ["active"] = mi.ToggleType == MenuItemToggleType.CheckBox && mi.IsChecked, // 화면에 그려지는 토글 칸 ✓(열린 창 1~9 활성)
                ["activeSource"] = mi.CommandParameter is xkxk.frameworks.ui.shell.ViewModels.OpenWindowSlot s ? (bool?)s.IsActive : null, // 원천 값(열린 창 칸만) — active 와 어긋나면 표시 꼬임
            });
            Walk(mi, depth + 1, into, missing, ref total, ref withIcon);
        }
    }

    /// <summary>항목 아이콘: Icon 안의 WindowIcon(Document 있으면 doc) → window, SnapCellGlyph → snap, 없으면 none. 첨부 키(MenuIcon.Key)는 보조로 읽는다.</summary>
    internal static (string Kind, string? Key, bool? Found, bool Present) IconOf(MenuItem mi)
    {
        var attached = AttachedKey(mi);
        var icon = mi.Icon as Control;
        var glyph = icon is null ? null : SelfAndLogical(icon).FirstOrDefault(c => c.GetType().Name is "WindowIcon" or "SnapCellGlyph");
        if (glyph?.GetType().Name == "SnapCellGlyph")
        {
            return ("snap", mi.Tag?.ToString() ?? attached, true, true);
        }

        if (glyph is not null)
        {
            var key = Prop(glyph, "EffectiveIconKey") as string ?? attached;
            return (Prop(glyph, "Document") is null ? "window" : "doc", key, HasGeometry(key), true);
        }

        return attached is null ? ("none", null, null, icon is not null) : ("window", attached, HasGeometry(attached), false);
    }

    private static IEnumerable<Control> SelfAndLogical(Control c) => new[] { c }.Concat(c.GetLogicalDescendants().OfType<Control>());

    private static object? Prop(object o, string name) => o.GetType().GetProperty(name)?.GetValue(o);

    /// <summary>화면 층 첨부 속성 MenuIcon.Key 를 등록부에서 이름으로 찾는다(형식 참조 없음).</summary>
    private static string? AttachedKey(MenuItem mi)
    {
        var prop = AvaloniaPropertyRegistry.Instance.GetRegisteredAttached(mi.GetType())
            .FirstOrDefault(p => p.Name == "Key" && p.OwnerType.Name == "MenuIcon");
        return prop is null ? null : mi.GetValue(prop) as string;
    }

    internal static bool? HasGeometry(string? key)
    {
        if (string.IsNullOrEmpty(key) || Application.Current is not { } app)
        {
            return null;
        }

        return xkxk.frameworks.ui.shell.Platform.UiIconGeometry.Find(app, app.ActualThemeVariant, key) is not null; // 화면과 같은 해석(레지스트리 → 리소스 폴백, UADR-0013)
    }

    private static MenuItem? FindMenu(string id) =>
        VisualTreeInspector.Find(id) as MenuItem
        ?? VisualTreeInspector.TopLevels().SelectMany(t => t.GetLogicalDescendants()).OfType<MenuItem>()
            .FirstOrDefault(m => VisualTreeInspector.AutomationIdOf(m) == id);

    private static async Task<JsonNode?> RenderAsync(JsonObject a)
    {
        var id = a.ReqStr("id");
        var target = FindMenu(id) ?? throw new RemoteCommandException($"메뉴를 찾지 못했습니다: {id}");
        var chain = new List<MenuItem>();
        for (ILogical? l = target; l is not null; l = l.LogicalParent)
        {
            if (l is MenuItem m) { chain.Insert(0, m); }
        }

        var opened = new List<MenuItem>();
        try
        {
            foreach (var m in chain)
            {
                if (!m.IsSubMenuOpen) { m.IsSubMenuOpen = true; opened.Add(m); }
                await Settle(m);
            }

            var popup = target.GetVisualDescendants().OfType<Popup>().FirstOrDefault()
                ?? throw new RemoteCommandException($"메뉴 팝업을 찾지 못했습니다: {id}(하위 항목이 없는 메뉴일 수 있음)");
            var content = popup.Child as Control ?? throw new RemoteCommandException("팝업 내용이 없습니다.");
            var scale = a.Num("scale") ?? (TopLevel.GetTopLevel(content)?.RenderScaling ?? 1.0);
            var size = new PixelSize(Math.Max(1, (int)(content.Bounds.Width * scale)), Math.Max(1, (int)(content.Bounds.Height * scale)));
            byte[] png;
            using (var rtb = new RenderTargetBitmap(size, new Vector(96 * scale, 96 * scale)))
            {
                rtb.Render(content);
                using var ms = new MemoryStream();
                rtb.Save(ms, (int?)null);
                png = ms.ToArray();
            }

            var dataDir = Path.GetDirectoryName(xkxk.frameworks.ui.data.Logging.PdLog.FileDirectory ?? "") is { Length: > 0 } d ? d : Path.GetDirectoryName(RemoteControlServer.InfoFilePath)!;
            var path = Path.GetFullPath(a.Str("out") ?? Path.Combine(dataDir, "shots", $"{DateTime.Now:yyyyMMdd_HHmmss_fff}_menu_{id}.png"));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllBytesAsync(path, png);
            return new JsonObject { ["path"] = path, ["w"] = size.Width, ["h"] = size.Height, ["items"] = target.Items.OfType<MenuItem>().Count() };
        }
        finally
        {
            for (var i = opened.Count - 1; i >= 0; i--)
            {
                opened[i].IsSubMenuOpen = false; // 연 것만 닫는다(사용자가 열어 둔 메뉴는 그대로)
            }
        }
    }

    private static async Task Settle(Visual v)
    {
        if (TopLevel.GetTopLevel(v) is { } top)
        {
            var frames = AutomationBridge.WaitFramesAsync(top, 2);
            await Task.WhenAny(frames, Task.Delay(500));
        }

        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, Avalonia.Threading.DispatcherPriority.Background);
    }
}
#endif
