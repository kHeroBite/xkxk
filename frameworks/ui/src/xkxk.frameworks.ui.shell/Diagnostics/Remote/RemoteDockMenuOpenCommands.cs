// 원격 dock.menu(진단) — 문서 탭·MDI 창 우클릭 메뉴(DocumentContextMenu)를 팝업 없이 열거·실행한다(U14 '분할 해제'·'독립 창으로 분리' 판정)
#if PD_DIAGNOSTICS // 진단 빌드 전용(PdDiagnostics=true)
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using Dock.Model.Core;

namespace xkxk.frameworks.ui.shell.Diagnostics.Remote;

/// <summary>
/// 근거(Dock.Avalonia 12.1.0.12 디컴파일): 문서 탭(DocumentTabStripItem)·MDI 창(MdiDocumentWindow)의 우클릭 메뉴는 Control.ContextMenu 가 아니라
/// DocumentContextMenu 속성(테마 DynamicResource)이고, 템플릿 제목 영역이 TemplateBinding 으로 ContextMenu 에 쓴다. 그래서 dock.menuHeaders(host.ContextMenu)는 source=none 이 된다.
/// 이 명령은 그 메뉴 인스턴스를 직접 잡아 DataContext 를 대상 문서로 맞춘 뒤(실제 열 때 PlacementTarget 이 주는 것과 같음) 항목을 열거하거나,
/// 항목의 Command(없으면 Click 이벤트)를 클릭과 같이 실행한다. 팝업(새 TopLevel)은 만들지 않는다.
/// 등록은 RemoteCommandRouter 가 Register 를 부른다(라우터 1줄 — odev-render).
/// </summary>
internal static class RemoteDockMenuOpenCommands
{
    internal static void Register(RemoteCommandRouter r)
    {
        r.Register("dock.menu",
            "문서 우클릭 메뉴(DocumentContextMenu) 팝업 없이 action=open|items(열거)|invoke(실행) target=tab|mdi doc=<문서 Id> "
            + "[invoke: index=<items 의 index> | header=<표시 문구> | name=<항목 Name>] → open/items: {control, items:[{index, depth, header, name, visible, enabled, hasCommand}]} "
            + "/ invoke: {status: Executed|CannotExecute|Clicked|NotFound|Hidden, index, header}",
            a =>
            {
                var target = (a.Str("target") ?? "mdi").ToLowerInvariant();
                var id = a.ReqStr("doc");
                var (host, menu) = Resolve(target, id);
                menu.DataContext = null; // 실제 열기처럼 바인딩을 새로 계산시킨다(같은 값 재대입은 다시 계산되지 않는다)
                menu.DataContext = host.DataContext; // 실제 열기와 같은 바인딩 대상(공유 인스턴스면 마지막 대상이 남아 있을 수 있다)
                var flat = Flatten(menu.Items, 0).ToList();
                var action = (a.Str("action") ?? "items").ToLowerInvariant();
                if (action is "open" or "items")
                {
                    return new JsonObject
                    {
                        ["control"] = host.GetType().Name,
                        ["items"] = new JsonArray(flat.Select((x, i) => (JsonNode?)new JsonObject
                        {
                            ["index"] = i, ["depth"] = x.Depth, ["header"] = HeaderOf(x.Item), ["name"] = x.Item.Name,
                            ["visible"] = x.Item.IsVisible, ["enabled"] = x.Item.IsEnabled, ["hasCommand"] = x.Item.Command is not null,
                        }).ToArray()),
                    };
                }

                if (action != "invoke")
                {
                    throw new RemoteCommandException("action=open|items|invoke 가 필요합니다.");
                }

                var at = a.Int("index") is { } ix ? ix
                    : a.Str("name") is { Length: > 0 } name ? flat.FindIndex(x => x.Item.Name == name)
                    : a.Str("header") is { Length: > 0 } header ? flat.FindIndex(x => HeaderOf(x.Item) == header)
                    : -1;
                if (at < 0 || at >= flat.Count)
                {
                    return new JsonObject { ["status"] = "NotFound", ["index"] = at };
                }

                var item = flat[at].Item;
                var status = !item.IsVisible ? "Hidden"
                    : item.Command is { } cmd ? (cmd.CanExecute(item.CommandParameter) ? Execute(cmd, item) : "CannotExecute")
                    : Click(item);
                return new JsonObject { ["status"] = status, ["index"] = at, ["header"] = HeaderOf(item) };
            });
    }

    private static (Control Host, ContextMenu Menu) Resolve(string target, string id)
    {
        var hosts = VisualTreeInspector.TopLevels().SelectMany(t => t.GetVisualDescendants().OfType<Control>())
            .Where(c => (c.DataContext as IDockable)?.Id == id);
        Control? host = target switch
        {
            "tab" => hosts.OfType<DocumentTabStripItem>().FirstOrDefault(),
            "mdi" => hosts.OfType<MdiDocumentWindow>().FirstOrDefault(),
            _ => throw new RemoteCommandException("target=tab|mdi 가 필요합니다."),
        };
        var menu = host switch
        {
            DocumentTabStripItem tab => tab.DocumentContextMenu,
            MdiDocumentWindow mdi => mdi.DocumentContextMenu,
            _ => null,
        } ?? host?.ContextMenu;
        if (host is null || menu is null)
        {
            throw new RemoteCommandException($"{target} 대상 Id={id} 의 컨트롤 또는 메뉴를 찾지 못했습니다(숨김·접힘·탭 줄 숨김이면 트리에 없음).");
        }

        return (host, menu);
    }

    private static IEnumerable<(MenuItem Item, int Depth)> Flatten(ItemCollection items, int depth)
    {
        foreach (var item in items.OfType<MenuItem>())
        {
            yield return (item, depth);
            foreach (var child in Flatten(item.Items, depth + 1))
            {
                yield return child;
            }
        }
    }

    private static string? HeaderOf(MenuItem item) => item.Header as string ?? (item.Header as TextBlock)?.Text ?? item.Header?.ToString();

    private static string Execute(System.Windows.Input.ICommand cmd, MenuItem item)
    {
        cmd.Execute(item.CommandParameter);
        return "Executed";
    }

    private static string Click(MenuItem item)
    {
        item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        return "Clicked";
    }
}
#endif
