// 모바일형 서랍·하단 탭의 메뉴 투영과 실행 — 메뉴 레지스트리 투영(MobileMenuProjection)에서 항목을 만들고 레지스트리 명령 ID 로 실행한다
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Threading;
using xkxk.frameworks.ui.data.Navigation;
using xkxk.frameworks.ui.shell.Framework;

namespace xkxk.frameworks.ui.shell.ViewModels;

public sealed partial class MobileNavViewModel
{
    private IReadOnlyList<NavEntry> _entries = Array.Empty<NavEntry>();
    private Dictionary<string, UiMenuItemDefinition> _sources = new(StringComparer.Ordinal);

    /// <summary>원격 진단 nav.menu 용 — 현재 투영 목록과 런타임 상태(활성·사유)를 담은 JSON.</summary>
    public string ToJson() => NavMenuCatalog.ToJson(_entries, e => { var (en, why, _) = Evaluate(e); return (en, why); });

    /// <summary>레지스트리 투영으로 서랍 항목·하단 탭을 다시 만든다(메뉴 Changed 때마다).</summary>
    private void RebuildMenu()
    {
        var menus = XkxkUi.Current.Menus;
        _entries = MobileMenuProjection.Entries(menus);
        _sources = SourceMap(menus);
        MenuItems.Clear();
        foreach (var e in _entries)
        {
            MenuItems.Add(new MobileNavItem(e, Execute));
        }

        Tabs.Clear();
        foreach (var t in _entries.Where(e => e.TabOrder >= 0).OrderBy(e => e.TabOrder))
        {
            Tabs.Add(new MobileNavTab(t.Id, t.TabKey ?? t.TextKey, t.Icon ?? NavMenuCatalog.MoreIcon, () => Run(t)));
        }

        Tabs.Add(new MobileNavTab("more", NavMenuCatalog.MoreTabKey, NavMenuCatalog.MoreIcon, () => TogglePanel(PanelMenu)));
    }

    private void OnMenusChanged(object? sender, UiRegistryChangedEventArgs e) => OnUiThread(() => { RebuildMenu(); Refresh(); });

    private void OnCommandsChanged(object? sender, UiRegistryChangedEventArgs e) => OnUiThread(Refresh);

    private static void OnUiThread(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess()) { action(); } else { Dispatcher.UIThread.Post(action); }
    }

    /// <summary>서랍 항목 실행 — 그룹·비활성은 무시, 레지스트리 명령(모바일 명령 우선)을 실행한 뒤 패널 열기가 아니면 서랍을 닫는다.</summary>
    private void Execute(NavEntry e)
    {
        if (e.IsGroup || !Evaluate(e).Enabled || e.Command is null) { return; }
        Run(e);
        if (e.Command != ShellCommandIds.NavOpenPanel)
        {
            Compact.IsDrawerOpen = false; // 실행한 뒤에는 서랍을 닫아 결과 화면을 보인다
        }
    }

    private void Run(NavEntry e)
    {
        if (e.Command is { } id)
        {
            XkxkUi.Current.Commands.Execute(id, ParamOf(e));
        }
    }

    /// <summary>
    /// 항목의 (활성, 비활성 사유 키, 켜짐) 판정. 모바일형 비적용(Available=false)은 그 사유, 조건 미충족(GetState.IsVisible=false)은 숨기지 않고 nav.reason.unavailable(R3).
    /// 셸 기본 항목은 e8950ea 와 같게 명령 실행 가능 여부로 막지 않고, 소비자 항목은 명령이 실행 가능할 때만 활성이다.
    /// </summary>
    private (bool Enabled, string? Reason, bool On) Evaluate(NavEntry e)
    {
        if (!e.Applicable) { return (false, e.ReasonKey, false); }
        if (!_sources.TryGetValue(e.Id, out var d)) { return (true, null, false); }
        var ui = XkxkUi.Current;
        if (!ui.Menus.GetState(d.Id, UiMenuSurface.Mobile).IsVisible) { return Gate(false); }
        if (e.IsGroup || d.Owner == UiRegistryOwners.Shell) { return (true, null, false); }
        return Gate(e.Command is { } id && ui.Commands.CanExecute(id, ParamOf(e)));
    }

    private static (bool, string?, bool) Gate(bool ok) => (ok, ok ? null : NavMenuCatalog.ReasonUnavailable, false);

    /// <summary>실행 파라미터 — 원래 객체(모바일 명령이 있으면 모바일 파라미터)를 쓰고, 정의를 못 찾으면 항목의 문자열 값.</summary>
    private object? ParamOf(NavEntry e) =>
        _sources.TryGetValue(e.Id, out var d) ? (d.Mobile.CommandId is not null ? d.Mobile.CommandParameter : d.CommandParameter) : e.Param;

    // 모바일 항목 id → 메뉴 정의. MobileMenuProjection 과 같은 순회·제외 규칙이라 같은 id 는 앞의 정의가 이긴다
    private static Dictionary<string, UiMenuItemDefinition> SourceMap(IUiMenuRegistry menus)
    {
        var map = new Dictionary<string, UiMenuItemDefinition>(StringComparer.Ordinal);
        Walk(null);
        return map;

        void Walk(string? parent)
        {
            foreach (var d in menus.GetChildren(parent))
            {
                if (d.IsHidden || d.Kind is < UiMenuItemKind.Command or > UiMenuItemKind.Section) { continue; }
                if (d.Mobile.InDrawer && (d.Kind is UiMenuItemKind.Command or UiMenuItemKind.Submenu || d.Mobile.EntryId is not null))
                {
                    map.TryAdd(NavMenuCatalog.MobileEntryId(d.Id, d.AutomationId, d.Mobile.EntryId), d);
                }

                Walk(d.Id);
            }
        }
    }
}
