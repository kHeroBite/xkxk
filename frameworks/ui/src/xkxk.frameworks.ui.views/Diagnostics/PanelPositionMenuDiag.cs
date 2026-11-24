// 패널 '위치' 메뉴 시험 명령(진단 빌드 전용) — 실제 메뉴 인스턴스의 항목을 팝업 없이 읽고, 항목의 Click 을 일으켜 화면 메뉴와 같은 경로로 실행한다
#if PD_DIAGNOSTICS // 진단 빌드 전용(PdDiagnostics=true)
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using Dock.Model.Core;
using xkxk.frameworks.ui.shell;
using xkxk.frameworks.ui.shell.Diagnostics.Remote;
using xkxk.frameworks.ui.shell.Docking;
using xkxk.frameworks.ui.shell.ViewModels;

namespace xkxk.frameworks.ui.views.Diagnostics;

internal static class PanelPositionMenuDiag
{
    internal static void RegisterRemote(RemoteCommandRouter r)
    {
        r.Register("panelmenu.read", "패널 위치 메뉴 항목(팝업 없음) pane=<패널 Id> | tab=<탭 dockable Id> → {scope, header, visible, title, items:[{id,target,header,checked,enabled}], others}", a =>
        {
            var (menu, subject, scope, title, host) = Resolve(a);
            var has = menu is not null && PanelPositionMenu.Refresh(menu, subject, scope);
            var top = TopItem(menu);
            return new JsonObject
            {
                ["found"] = menu is not null,
                ["scope"] = scope.ToString(),
                ["subject"] = subject?.Id,
                ["visible"] = has,
                ["header"] = top?.Header?.ToString(),
                ["title"] = title,
                ["hostBounds"] = BoundsOf(host),
                ["titleBounds"] = BoundsOf(Named(host, "PART_Title")),
                ["menuButton"] = Named(host, "PART_MenuButton") is { IsEffectivelyVisible: true } button ? BoundsOf(button) : null,
                ["others"] = menu?.Items.OfType<MenuItem>().Count(i => i.Name != PanelPositionMenu.TopItemName) ?? 0,
                ["items"] = new JsonArray((top?.Items.OfType<MenuItem>() ?? Enumerable.Empty<MenuItem>()).Select(i => (JsonNode?)new JsonObject
                {
                    ["id"] = AutomationProperties.GetAutomationId(i),
                    ["target"] = i.Tag?.ToString(),
                    ["header"] = i.Header?.ToString(),
                    ["checked"] = i.IsChecked,
                    ["enabled"] = i.IsEnabled,
                }).ToArray()),
            };
        });
        r.Register("panelmenu.click", "패널 위치 메뉴 항목 실행(항목 Click, 메뉴 클릭과 같은 경로, 실행은 다음 UI 틱) pane=|tab= target= → {clicked, reason?}", a =>
        {
            var (menu, subject, scope, _, _) = Resolve(a);
            if (menu is null || !PanelPositionMenu.Refresh(menu, subject, scope))
            {
                return new JsonObject { ["clicked"] = false, ["reason"] = "no-menu" };
            }

            var target = a.ReqStr("target");
            var item = TopItem(menu)?.Items.OfType<MenuItem>().FirstOrDefault(i => i.Tag?.ToString() == target);
            if (item is null)
            {
                return new JsonObject { ["clicked"] = false, ["reason"] = "no-item" };
            }

            if (!item.IsEnabled)
            {
                return new JsonObject { ["clicked"] = false, ["reason"] = "disabled" };
            }

            item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            return new JsonObject { ["clicked"] = true };
        });
        r.Register("panelmenu.docSplit", "문서 분할을 만들거나 푼다(탭 메뉴 '새 가로 문서 영역'·'분할 해제'와 같은 명령, 탭 메뉴는 열 때 붙어 팝업 없이 못 부른다) doc= on=true|false → {done}", a =>
        {
            var doc = App.Shell?.Documents.FirstOrDefault(d => d.Id == a.ReqStr("doc")) ?? throw new RemoteCommandException("문서 없음");
            if (a.Bool("on"))
            {
                App.MainViewModel!.Factory.NewHorizontalDocumentDock(doc);
                return new JsonObject { ["done"] = true };
            }

            var can = SplitMergeService.UnsplitCommand.CanExecute(doc);
            SplitMergeService.UnsplitCommand.Execute(doc);
            return new JsonObject { ["done"] = can };
        });
        r.Register("panelmenu.notifyCount", "알림 건수를 n 건으로 맞춘다(샘플을 빼거나 복제) n= → {count, title}", a =>
        {
            var vm = App.MainViewModel?.Factory.Notifications ?? throw new RemoteCommandException("셸이 준비되지 않았습니다.");
            var n = (int)(a.Num("n") ?? throw new RemoteCommandException("n= 이 필요합니다."));
            SetCount(vm.Items, n);
            return new JsonObject { ["count"] = vm.Items.Count, ["title"] = vm.Title };
        });
    }

    private static void SetCount(ObservableCollection<NotificationItem> items, int n)
    {
        while (items.Count > n)
        {
            items.RemoveAt(items.Count - 1);
        }

        while (items.Count < n)
        {
            items.Add(new NotificationItem(System.DateTime.Now, "t", "t", "t"));
        }
    }

    private static Control? Named(Control? host, string name) => host?.GetVisualDescendants().OfType<Control>().FirstOrDefault(c => c.Name == name);

    private static JsonObject? BoundsOf(Control? control)
    {
        if (control is null || Root() is not { } root || control.TranslatePoint(default, root) is not { } at)
        {
            return null;
        }

        return new JsonObject { ["x"] = at.X, ["y"] = at.Y, ["w"] = control.Bounds.Width, ["h"] = control.Bounds.Height };
    }

    private static (ContextMenu? Menu, IDockable? Subject, PanelMoveScope Scope, string? Title, Control? Host) Resolve(JsonObject a)
    {
        var visuals = Root()?.GetVisualDescendants().ToList() ?? new();
        if (a.Str("tab") is { Length: > 0 } tab)
        {
            var strip = visuals.OfType<DocumentTabStripItem>().FirstOrDefault(i => (i.DataContext as IDockable)?.Id == tab);
            return (strip?.DocumentContextMenu, strip?.DataContext as IDockable, PanelMoveScope.Tab, null, strip);
        }

        var pane = a.ReqStr("pane");
        var chrome = visuals.OfType<ToolChromeControl>().FirstOrDefault(c => (c.DataContext as IDockable)?.Id == pane);
        var title = chrome?.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault(t => t.Name == "PART_Title")?.Text;
        return (chrome?.GetValue(PanelPositionMenu.ChromeMenuProperty), chrome?.DataContext as IDockable, PanelMoveScope.Panel, title, chrome);
    }

    private static MenuItem? TopItem(ContextMenu? menu) => menu?.Items.OfType<MenuItem>().FirstOrDefault(i => i.Name == PanelPositionMenu.TopItemName);

    private static Visual? Root() => Application.Current?.ApplicationLifetime switch
    {
        IClassicDesktopStyleApplicationLifetime d => d.MainWindow,
        ISingleViewApplicationLifetime s => s.MainView,
        _ => null,
    };
}
#endif
