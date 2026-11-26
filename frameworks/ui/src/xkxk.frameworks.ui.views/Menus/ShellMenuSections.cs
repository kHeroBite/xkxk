// 메뉴 레지스트리 섹션 자리를 채우는 화면 쪽 표 — 스냅 배치 칸·모니터 목록(UADR-0014, 옛 WindowMenu.axaml 이전). 열린 창 목록·창 루트 열기 훅은 ShellMenuSections.Window.cs
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Threading;
using xkxk.frameworks.ui.data.Navigation;
using xkxk.frameworks.ui.shell.Docking;
using xkxk.frameworks.ui.shell.Framework;
using xkxk.frameworks.ui.shell.ViewModels;

namespace xkxk.frameworks.ui.views.Menus;

/// <summary>
/// 섹션(<see cref="UiMenuItemKind.Section"/>) 키 → 채우기 대리자 표. MenuBuilder 가 섹션 정의를 만나면 최상위 생성과 같은 때(D4) <see cref="Build"/> 를 부르고,
/// 돌려받은 컨트롤을 섹션 자리에 그대로 끼운다. 결과 컨트롤의 표시·활성·문구·자동화 이름은 섹션이 맡는다(GetState 로 덮지 않는다).
/// </summary>
/// <remarks>
/// 자동화 이름은 옛 axaml 과 같은 값이다. 판정기(menu_registry_check ②)·카탈로그 규칙이 코드의 리터럴을 읽으므로
/// 칸 번호가 붙는 ID 도 접두("shell.mnuWinSnapMax" 등)는 리터럴로 둔다.
/// </remarks>
internal static partial class ShellMenuSections
{
    private static readonly Dictionary<string, Func<MenuItem, MenuItem, IReadOnlyList<Control>>> Fillers = new(StringComparer.Ordinal)
    {
        [ShellMenuIds.SectionSnap] = BuildSnap,
        [ShellMenuIds.SectionMonitors] = BuildMonitors,
        [ShellMenuIds.SectionOpenWindows] = BuildOpenWindows,
    };

    // 배치 칸 종류 6개(옛 axaml 순서) — (칸 종류 Id, 자동화 이름, 칸 수). 칸 항목은 자동화 이름 뒤에 1부터 번호를 붙인다
    private static readonly (string Layout, string AutomationId, int Cells)[] SnapLayouts =
    {
        ("max", "shell.mnuWinSnapMax", 1),
        ("half", "shell.mnuWinSnapHalf", 2),
        ("twothird", "shell.mnuWinSnapTwoThird", 2),
        ("thirds", "shell.mnuWinSnapThirds", 3),
        ("leftsplit", "shell.mnuWinSnapLeftSplit", 3),
        ("quad", "shell.mnuWinSnapQuad", 4),
    };

    private const string SnapIconClass = "pdSnapIcon";

    private static readonly HashSet<string> s_reportedUnknown = new(StringComparer.Ordinal);
    private static readonly object s_reportLock = new();

    // 모니터 라디오 항목 ↔ 구독 중인 모니터 뷰모델(컨테이너 재사용 때 해제)
    private static readonly ConditionalWeakTable<MenuItem, MonitorLink> s_monitorLinks = new();

    /// <summary>섹션 키에 맞는 컨트롤을 즉시 만든다. 모르는 키는 XKUI0409 를 키별 1회 보고하고 빈 목록을 돌려준다(그 자리는 비움).</summary>
    /// <param name="sectionKey">섹션 정의의 키(<see cref="UiMenuItemDefinition.Section"/>).</param>
    /// <param name="host">섹션의 부모 MenuItem(결과를 끼울 Items 의 주인). null 이면 <see cref="ArgumentNullException"/>.</param>
    /// <param name="root">host 가 속한 최상위 MenuItem(열기 훅·DataContext 기준). null 이면 <see cref="ArgumentNullException"/>.</param>
    public static IReadOnlyList<Control> Build(string? sectionKey, MenuItem host, MenuItem root)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(root);
        if (sectionKey is not null && Fillers.TryGetValue(sectionKey, out var fill))
        {
            return fill(host, root);
        }

        ReportUnknown(sectionKey ?? "");
        return Array.Empty<Control>();
    }

    private static void ReportUnknown(string key)
    {
        lock (s_reportLock)
        {
            if (!s_reportedUnknown.Add(key))
            {
                return;
            }
        }

        XkxkUi.Current.Diagnostics.Report(UiDiagnosticIds.MenuSectionUnknown, UiDiagnosticSeverity.Warning,
            $"화면이 모르는 메뉴 섹션 키 '{key}' 라 그 자리를 비운다."); // i18n-ok 개발자 진단
    }

    private static MenuItem NewItem(string automationId)
    {
        var item = new MenuItem();
        AutomationProperties.SetAutomationId(item, automationId);
        return item;
    }

    // 사전 문구를 관찰로 묶어 언어를 바꾸면 즉시 다시 그린다({loc:Tr} 와 같은 길)
    private static void BindTitle(MenuItem item, string key) =>
        item.Bind(MenuItem.HeaderProperty, XkxkUi.Current.Localization.Observe(key));

    // ── 스냅: 하위 메뉴 xkxk.window.snap 안 칸 종류 6개 × 칸 ─────────────────────────────

    private static IReadOnlyList<Control> BuildSnap(MenuItem host, MenuItem root)
    {
        var layouts = new List<MenuItem>(SnapLayouts.Length);
        foreach (var (layoutId, automationId, count) in SnapLayouts)
        {
            var layout = NewItem(automationId);
            layout.Tag = layoutId;
            layout.Classes.Add(SnapIconClass);
            BindTitle(layout, "snap.layout." + layoutId);
            for (var i = 0; i < count; i++)
            {
                var index = i;
                var number = (i + 1).ToString(CultureInfo.InvariantCulture);
                var cell = NewItem(automationId + number);
                cell.Header = number;
                cell.Tag = layoutId + "|" + i.ToString(CultureInfo.InvariantCulture);
                cell.Classes.Add(SnapIconClass);
                cell.Click += (_, _) => OnSnapCell(root, layoutId, index);
                layout.Items.Add(cell);
            }

            layouts.Add(layout);
        }

        HooksOf(root).Snaps.Add((host, layouts));
        return layouts;
    }

    // 옵션에 켜진 칸 종류만 보이고, 칸마다 대상 창에 적용할 수 있을 때만 활성. 켜진 종류가 0개면 하위 메뉴(host)를 숨긴다
    private static void RefreshSnap(MenuItem host, IReadOnlyList<MenuItem> layouts, MenuItem root, MainViewModel vm)
    {
        var target = SnapTarget(vm, root);
        var shown = WindowMenuOps.PlacementLayouts.Select(l => l.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (shown.Count == 0)
        {
            host.IsVisible = false;
        }

        foreach (var layout in layouts)
        {
            layout.IsVisible = shown.Contains(layout.Tag as string ?? "");
            foreach (var cell in layout.Items.OfType<MenuItem>())
            {
                cell.IsEnabled = target is not null && cell.Tag is string tag && TryParseCell(tag, out var layoutId, out var index)
                    && WindowMenuOps.CanApplyCell(target, layoutId, index);
            }
        }
    }

    private static void OnSnapCell(MenuItem root, string layoutId, int index)
    {
        if (root.DataContext is MainViewModel vm && SnapTarget(vm, root) is { } target)
        {
            Dispatcher.UIThread.Post(() => WindowMenuOps.ApplyCell(target, layoutId, index));
        }
    }

    // 대상: 활성 문서(MDI·독립 창 구분은 WindowMenuOps 가 판별), 없으면 메인 창
    private static WindowMenuTarget? SnapTarget(MainViewModel vm, MenuItem root) =>
        vm.ActiveDocument is { } doc ? new WindowMenuTarget(WindowMenuKind.Mdi, doc, null)
        : TopLevel.GetTopLevel(root) is Window main ? new WindowMenuTarget(WindowMenuKind.Main, null, main)
        : null;

    private static bool TryParseCell(string tag, out string layoutId, out int index)
    {
        var parts = tag.Split('|');
        layoutId = parts[0];
        index = 0;
        return parts.Length == 2 && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out index);
    }

    // ── 모니터: 하위 메뉴 xkxk.window.monitor 안 멀티 모니터 라디오 목록 + 없음 안내 ─────────

    private static IReadOnlyList<Control> BuildMonitors(MenuItem host, MenuItem root)
    {
        var monitors = XkxkUi.Current.Monitors;
        var list = NewItem("shell.mnuWinMultiMonitor");
        BindTitle(list, "menu.ws.multiMonitor");
        MenuIcon.SetKey(list, CmdIconKeys.MultiMonitor);
        list.ItemsSource = monitors.Items;
        list.ContainerPrepared += OnMonitorContainerPrepared;
        list.ContainerClearing += (_, e) => UnlinkMonitor(e.Container as MenuItem);

        // 모니터 목록을 못 얻은 경우(웹·정보 없음) 안내 1줄
        var none = NewItem("shell.mnuWinMonitorNone");
        BindTitle(none, "menu.monitor.none");
        MenuIcon.SetKey(none, CmdIconKeys.MonitorNone);
        none.IsEnabled = false;

        // monitors·noMonitors 조건은 섹션이 판정한다. 구독은 논리 트리에 붙어 있는 동안만(다시 만든 메뉴의 옛 항목이 남지 않게)
        void Sync()
        {
            list.IsVisible = monitors.HasMonitors;
            none.IsVisible = !monitors.HasMonitors;
        }

        PropertyChangedEventHandler changed = (_, e) =>
        {
            if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(MonitorMenuViewModel.HasMonitors))
            {
                Sync();
            }
        };
        list.AttachedToLogicalTree += (_, _) =>
        {
            monitors.PropertyChanged -= changed;
            monitors.PropertyChanged += changed;
            Sync();
        };
        list.DetachedFromLogicalTree += (_, _) => monitors.PropertyChanged -= changed;
        Sync();
        return new Control[] { list, none };
    }

    // 목록이 만든 항목(모니터 1개 = 라디오 1개). 라디오 표시는 토글 칸, 아이콘은 아이콘 칸이라 함께 보인다(MenuIconStyles)
    private static void OnMonitorContainerPrepared(object? sender, ContainerPreparedEventArgs e)
    {
        if (e.Container is not MenuItem item)
        {
            return;
        }

        var monitor = item.DataContext as MonitorItemViewModel
            ?? (sender is ItemsControl owner && e.Index >= 0 && e.Index < owner.Items.Count ? owner.Items[e.Index] as MonitorItemViewModel : null);
        UnlinkMonitor(item);
        if (monitor is null)
        {
            return;
        }

        item.Header = monitor.Header;
        item.ToggleType = MenuItemToggleType.Radio;
        item.GroupName = "pd.monitor";
        MenuIcon.SetKey(item, CmdIconKeys.MonitorItem);
        item.Command = monitor.SelectCommand;
        AutomationProperties.SetAutomationId(item, monitor.AutomationId);
        item.IsChecked = monitor.IsSelected;
        PropertyChangedEventHandler handler = (_, a) =>
        {
            if (string.IsNullOrEmpty(a.PropertyName) || a.PropertyName == nameof(MonitorItemViewModel.IsSelected))
            {
                item.IsChecked = monitor.IsSelected;
            }
        };
        monitor.PropertyChanged += handler;
        s_monitorLinks.AddOrUpdate(item, new MonitorLink(monitor, handler));
    }

    private static void UnlinkMonitor(MenuItem? item)
    {
        if (item is not null && s_monitorLinks.TryGetValue(item, out var link))
        {
            link.Monitor.PropertyChanged -= link.Handler;
            s_monitorLinks.Remove(item);
        }
    }

    private sealed record MonitorLink(MonitorItemViewModel Monitor, PropertyChangedEventHandler Handler);
}
