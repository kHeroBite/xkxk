// 원격 ui.contextMenu(진단, 단독 클래스 — partial 아님) — 컨트롤에 붙은 ContextMenu(제목줄 시스템 메뉴 등)의 항목 id·상태를 팝업 없이 읽고, 필요하면 Avalonia API 로 열고 닫는다(OS 입력 없음)
#if PD_DIAGNOSTICS // 진단 빌드 전용(PdDiagnostics=true). 제품 빌드는 RemoteProductStubs 가 모든 요청을 거부한다
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;

namespace xkxk.frameworks.ui.shell.Diagnostics.Remote;

internal static class ContextMenuInspector
{
    /// <summary>
    /// 배경(team-lead 2026-10-30): ContextMenu 는 Border.ContextMenu 속성에 있어 호스트의 논리·시각 트리에 항목이 없다. menu.list 는 MenuItem/Menu 의
    /// AutomationId 로만 찾으므로 shell.sysClose 등 제목줄 시스템 메뉴를 볼 수 없었다. 이 명령은 ContextMenu 를 가진 호스트를 찾아 Items 를 직접 읽는다.
    /// 기본(open 생략)은 읽기만 한다 — 팝업을 만들지 않는다. open=true|false 는 ContextMenu.Open/Close(호스트가 MenuItem 이면 IsSubMenuOpen)를 호출한다.
    /// </summary>
    internal static void Register(RemoteCommandRouter r)
    {
        r.Register("ui.contextMenu",
            "컨트롤의 ContextMenu 항목 조회·열기(OS 입력 없음) [id=<호스트 AutomationId·Name 또는 ContextMenu 의 x:Name, 예 SystemMenu>] [open=true|false] → " +
            "{host, kind, isOpen, items:[{id, header, headerId, headerType, enabled, checked, value, hasCommand, depth, separator}]}. id 생략=ContextMenu 를 가진 호스트 목록. " +
            "open 생략이면 팝업을 열지 않는다(읽기 전용). id 가 항목 없는 MenuItem 이면 그 항목 하나(헤더 컨트롤 id 포함, 예 shell.mnuMagnetSliderItem → shell.sldMagnetRange)를 돌려준다", a =>
            {
                var id = a.Str("id");
                var hosts = Hosts().ToList();
                if (string.IsNullOrEmpty(id))
                {
                    return new JsonObject { ["hosts"] = new JsonArray(hosts.Select(h => (JsonNode?)HostInfo(h)).ToArray()) };
                }

                var host = hosts.FirstOrDefault(h => Match(h, id))
                    ?? throw new RemoteCommandException($"ContextMenu 호스트 '{id}' 를 찾지 못했습니다(id 생략으로 목록 확인).");
                if (a.Str("open") is { Length: > 0 })
                {
                    SetOpen(host, a.Bool("open"));
                }

                var items = new JsonArray();
                var source = ItemsOf(host);
                if (source.Count == 0 && host is MenuItem self)
                {
                    source = new List<object> { self };
                }

                Walk(source, 1, items);
                var info = HostInfo(host);
                info["count"] = items.Count(n => n?["separator"] is null);
                info["items"] = items;
                return info;
            });
    }

    /// <summary>ContextMenu 를 가진 컨트롤과 항목을 가진 MenuItem — 모든 TopLevel 의 시각·논리 트리에서 모은다(중복 제거).</summary>
    private static IEnumerable<Control> Hosts() => VisualTreeInspector.TopLevels()
        .SelectMany(t => t.GetVisualDescendants().OfType<Control>().Concat(t.GetLogicalDescendants().OfType<Control>()))
        .Distinct()
        .Where(c => c.ContextMenu is not null || c is MenuItem { ItemCount: > 0 });

    private static bool Match(Control c, string id) =>
        AutomationProperties.GetAutomationId(c) == id || c.Name == id || c.ContextMenu?.Name == id;

    private static List<object> ItemsOf(Control host)
    {
        IEnumerable? raw = host is MenuItem mi ? mi.Items : host.ContextMenu?.Items;
        return raw?.Cast<object>().ToList() ?? new List<object>();
    }

    private static JsonObject HostInfo(Control host) => new()
    {
        ["host"] = AutomationProperties.GetAutomationId(host) is { Length: > 0 } hid ? hid : host.Name ?? host.GetType().Name,
        ["hostType"] = host.GetType().Name,
        ["menuName"] = host.ContextMenu?.Name,
        ["kind"] = host is MenuItem ? "menuItem" : "contextMenu",
        ["isOpen"] = host is MenuItem m ? m.IsSubMenuOpen : host.ContextMenu?.IsOpen,
    };

    private static void SetOpen(Control host, bool open)
    {
        if (host is MenuItem mi)
        {
            mi.IsSubMenuOpen = open;
        }
        else if (open)
        {
            host.ContextMenu!.Open(host);
        }
        else
        {
            host.ContextMenu!.Close();
        }
    }

    private static void Walk(IEnumerable<object> source, int depth, JsonArray items)
    {
        foreach (var child in source)
        {
            if (child is Separator)
            {
                items.Add(new JsonObject { ["separator"] = true, ["depth"] = depth });
                continue;
            }

            if (child is not MenuItem m)
            {
                continue;
            }

            var headerCtl = m.Header as Control;
            items.Add(new JsonObject
            {
                ["id"] = AutomationProperties.GetAutomationId(m),
                ["header"] = headerCtl is null ? MenuHeaderText.Of(m.Header) : null,
                ["headerId"] = headerCtl is null ? null : AutomationProperties.GetAutomationId(headerCtl),
                ["headerType"] = headerCtl?.GetType().Name,
                ["enabled"] = m.IsEffectivelyEnabled,
                ["checked"] = m.ToggleType == MenuItemToggleType.None ? null : m.IsChecked,
                ["value"] = (headerCtl as Avalonia.Controls.Primitives.RangeBase)?.Value,
                ["hasCommand"] = m.Command is not null,
                ["depth"] = depth,
            });
            if (m.ItemCount > 0)
            {
                Walk(m.Items.Cast<object>(), depth + 1, items);
            }
        }
    }
}
#endif
