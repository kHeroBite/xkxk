// 패널 '위치' 메뉴 — 패널 제목 줄과 패널 안 탭의 우클릭 메뉴에 같은 하위 메뉴를 붙인다. 항목과 실행은 PanelPositionOps 가 정하고 여기서는 메뉴 항목만 만든다
using xkxk.frameworks.ui.shell;
using xkxk.frameworks.ui.shell.Platform;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Dock.Avalonia.Controls;
using Dock.Model.Core;
using xkxk.frameworks.ui.shell.Docking;
using xkxk.frameworks.ui.shell.Localization;

namespace xkxk.frameworks.ui.views;

/// <summary>
/// 제목 줄(ToolChromeControl PART_Grip)은 이 파일이 만든 메뉴, 탭은 Dock 기본 탭 메뉴(DocumentContextMenu)에 항목을 덧붙인다(DocumentTabMenu 의 '분할 해제'와 같은 방식).
/// 터치 기기는 우클릭이 없어 측면 패널 머리줄 ▾(PART_MenuButton, 데스크톱에서는 숨김)가 같은 메뉴를 연다(06 모바일: 길게 누름에만 기능을 두지 않는다).
/// 항목은 열 때마다 다시 만든다. 탭 메뉴는 대상이 정해진 뒤인 Opened 에서, 제목 줄은 항목이 없으면 열지 않도록 Opening 에서 맞춘다(디컴파일 12.1.4: Opening 은 포인터 경로에서 대상이 정해지기 전에 온다. 제목 줄은 대상을 직접 안다).
/// </summary>
public static class PanelPositionMenu
{
    internal const string TopItemName = "PdPanelPosition";
    private const string SeparatorName = "PdPanelPositionSeparator";
    private const string SidePanelClass = "pdSidePanel";

    /// <summary>머리줄이 가진 메뉴(시험이 팝업 없이 읽는다).</summary>
    internal static readonly AttachedProperty<ContextMenu?> ChromeMenuProperty =
        AvaloniaProperty.RegisterAttached<TemplatedControl, Control, ContextMenu?>("ChromeMenu");

    private static readonly AttachedProperty<Button?> TouchButtonProperty =
        AvaloniaProperty.RegisterAttached<TemplatedControl, Control, Button?>("TouchButton");

    private static bool s_installed;

    /// <summary>탭 메뉴 인스턴스가 정해질 때 항목을 덧붙이도록 구독한다. 탭이 만들어지기 전에 부른다(EnsureNotificationTemplate).</summary>
    public static void Install()
    {
        if (s_installed)
        {
            return;
        }

        s_installed = true;
        DocumentTabMenu.Install(); // 문서 탭 우클릭 공통 항목(F06)
        DocumentTabStripItem.DocumentContextMenuProperty.Changed.AddClassHandler<DocumentTabStripItem>((_, e) => AttachToTabMenu(e.NewValue as ContextMenu));
    }

    /// <summary>머리줄 템플릿 적용 뒤 제목 줄에 메뉴를 단다. 패널이 아닌 머리줄은 항목이 없어 열리지 않는다.</summary>
    internal static void AttachToChrome(TemplatedControl chrome, INameScope scope)
    {
        if (scope.Find<Control>("PART_Grip") is not { } grip)
        {
            return;
        }

        var menu = new ContextMenu();
        menu.Items.Add(NewTopItem());
        menu.Opening += (_, e) => e.Cancel = !Refresh(menu, chrome.DataContext as IDockable, PanelMoveScope.Panel);
        grip.ContextMenu = menu;
        chrome.SetValue(ChromeMenuProperty, menu);
        if (InputFormFactor.IsTouchDevice && scope.Find<Button>("PART_MenuButton") is { } button)
        {
            menu.Placement = PlacementMode.Bottom;
            menu.PlacementTarget = button; // ▾ 아래에 연다. Open 은 메뉴가 붙은 컨트롤(제목 줄)만 받는다
            chrome.SetValue(TouchButtonProperty, button);
            button.Click += (_, _) =>
            {
                if (chrome.Classes.Contains(SidePanelClass) && Refresh(menu, chrome.DataContext as IDockable, PanelMoveScope.Panel))
                {
                    menu.Open(grip);
                }
            };
            SyncTouchButton(chrome);
        }
    }

    /// <summary>측면 패널이면 ▾ 의 Dock 기본 메뉴(떠 있게·닫기)를 떼고, 측면 패널이 아니면 기본 메뉴로 돌려놓는다. 값은 템플릿 바인딩보다 우선한다.</summary>
    internal static void SyncTouchButton(Control chrome)
    {
        if (chrome.GetValue(TouchButtonProperty) is not { } button)
        {
            return;
        }

        if (chrome.Classes.Contains(SidePanelClass))
        {
            button.Flyout = null;
        }
        else
        {
            button.ClearValue(Button.FlyoutProperty);
        }
    }

    private static void AttachToTabMenu(ContextMenu? menu)
    {
        if (menu is null || menu.Items.OfType<Control>().Any(c => c.Name == TopItemName))
        {
            return;
        }

        menu.Items.Add(new Separator { Name = SeparatorName });
        menu.Items.Add(NewTopItem());
        menu.Opened += (_, _) => Refresh(menu, menu.DataContext as IDockable, PanelMoveScope.Tab);
    }

    private static MenuItem NewTopItem()
    {
        var top = new MenuItem { Name = TopItemName };
        AutomationProperties.SetAutomationId(top, "panel.pos.menu");
        return top;
    }

    /// <summary>대상 상태로 항목을 다시 만든다. 낼 항목이 있으면 true.</summary>
    internal static bool Refresh(ContextMenu menu, IDockable? subject, PanelMoveScope scope)
    {
        var factory = (subject?.Factory ?? subject?.Owner?.Factory) as PocDockFactory;
        var options = factory is null ? System.Array.Empty<PanelMoveOption>() : PanelPositionOps.OptionsFor(factory, subject, scope);
        var has = options.Count > 0;
        foreach (var control in menu.Items.OfType<Control>().Where(c => c.Name is TopItemName or SeparatorName))
        {
            control.IsVisible = has;
        }

        if (menu.Items.OfType<MenuItem>().FirstOrDefault(i => i.Name == TopItemName) is not { } top)
        {
            return false;
        }

        top.Items.Clear();
        if (!has)
        {
            return false;
        }

        top.Header = Loc.Tr(scope == PanelMoveScope.Panel ? "panel.pos.panel" : "panel.pos.tab");
        var kind = options[0].Kind;
        foreach (var option in options)
        {
            if (option.Kind != kind)
            {
                kind = option.Kind;
                top.Items.Add(new Separator()); // 가장자리와 패널 목록을 나눈다
            }

            var item = new MenuItem
            {
                Header = option.Label,
                ToggleType = MenuItemToggleType.CheckBox,
                IsChecked = option.IsCurrent,
                IsEnabled = option.IsEnabled,
                Tag = option.TargetId,
            };
            AutomationProperties.SetAutomationId(item, "panel.pos." + option.TargetId);
            // 메뉴가 닫히고 포커스가 돌아온 뒤에 실행한다(창 메뉴와 같다)
            item.Click += (_, _) => Dispatcher.UIThread.Post(() => PanelPositionOps.Move(factory!, subject!, scope, option.TargetId));
            top.Items.Add(item);
        }

        if (scope == PanelMoveScope.Panel && factory is not null)
        {
            AddTabMoveItems(top, factory, subject);
        }

        return true;
    }

    /// <summary>패널 ▾ 메뉴의 탭별 '이 탭 옮기기'(F07) — 탭이 둘 이상일 때 탭마다 왼·오른·위·아래 패널과 독립 창 항목을 낸다. 터치에는 탭 우클릭이 없어 이 경로가 유일하다.</summary>
    private static void AddTabMoveItems(MenuItem top, PocDockFactory factory, IDockable? panel)
    {
        var tabs = PanelPositionOps.MovableTabs(factory, panel);
        if (tabs.Count == 0)
        {
            return;
        }

        top.Items.Add(new Separator());
        var targets = new (string Id, string Key)[]
        {
            ("Left", "panel.moveTab.left"), ("Right", "panel.moveTab.right"), ("Top", "panel.moveTab.top"),
            ("Bottom", "panel.moveTab.bottom"), (PanelPositionOps.FloatTarget, "panel.moveTab.float"),
        };
        foreach (var tab in tabs)
        {
            var tabItem = new MenuItem { Header = Loc.Tr("panel.moveTab") + " — " + tab.Title };
            AutomationProperties.SetAutomationId(tabItem, "panel.moveTab." + tab.Id);
            foreach (var (id, key) in targets)
            {
                var item = new MenuItem { Header = Loc.Tr(key) };
                AutomationProperties.SetAutomationId(item, "panel.moveTab." + tab.Id + "." + id);
                var dest = id;
                var moved = tab;
                item.Click += (_, _) => Dispatcher.UIThread.Post(() => PanelPositionOps.MoveTabTo(factory, moved, dest));
                tabItem.Items.Add(item);
            }

            top.Items.Add(tabItem);
        }
    }
}
