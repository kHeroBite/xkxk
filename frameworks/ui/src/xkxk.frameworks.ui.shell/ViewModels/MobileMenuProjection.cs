// 메뉴 레지스트리 → 모바일형 서랍·하단 탭 항목(NavEntry) 투영 — 셸 메뉴와 소비자 메뉴를 같은 규칙으로 바꾼다(기본 상태에서 NavMenuCatalog.Entries 와 같은 목록)
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using xkxk.frameworks.ui.data.Navigation;
using xkxk.frameworks.ui.shell.Framework;

namespace xkxk.frameworks.ui.shell.ViewModels;

/// <summary>
/// 레지스트리 트리를 전위 순회(형제 순서 = 레지스트리 순서)하며 Mobile.InDrawer 항목만 NavEntry 로 바꾼다.
/// Separator·Section 은 EntryId 가 있을 때만 항목이 된다. SetHidden(true) 항목은 그 하위까지 서랍에서 뺀다(조건 미충족은 숨기지 않는다 — 활성·사유는 소비 측 GetState 몫).
/// 모르는 Kind 값은 그 하위까지 그리지 않는다.
/// </summary>
internal static class MobileMenuProjection
{
    /// <summary>서랍 항목 전체(표시 순서).</summary>
    public static IReadOnlyList<NavEntry> Entries(IUiMenuRegistry menus)
    {
        ArgumentNullException.ThrowIfNull(menus);
        var output = new List<NavEntry>();
        foreach (var root in menus.GetChildren(null))
        {
            Walk(menus, root, 0, NavMenuCatalog.GroupOf(root.Id), output);
        }

        return output;
    }

    /// <summary>하단 탭(TabOrder 순서, '더보기'는 포함하지 않음).</summary>
    public static IReadOnlyList<NavEntry> Tabs(IUiMenuRegistry menus) =>
        Entries(menus).Where(e => e.TabOrder >= 0).OrderBy(e => e.TabOrder).ToList();

    private static void Walk(IUiMenuRegistry menus, UiMenuItemDefinition d, int depth, NavGroup group, List<NavEntry> output)
    {
        if (d.IsHidden || d.Kind is < UiMenuItemKind.Command or > UiMenuItemKind.Section)
        {
            return;
        }

        var m = d.Mobile;
        if (m.InDrawer && (d.Kind is UiMenuItemKind.Command or UiMenuItemKind.Submenu || m.EntryId is not null))
        {
            output.Add(ToEntry(menus, d, depth, group));
        }

        foreach (var c in menus.GetChildren(d.Id))
        {
            Walk(menus, c, depth + 1, group, output);
        }
    }

    private static NavEntry ToEntry(IUiMenuRegistry menus, UiMenuItemDefinition d, int depth, NavGroup group)
    {
        var m = d.Mobile;
        var id = NavMenuCatalog.MobileEntryId(d.Id, d.AutomationId, m.EntryId);
        var text = m.TitleKey ?? d.TitleKey ?? d.Title ?? menus.GetState(d.Id, UiMenuSurface.Mobile).Title ?? d.Id;
        var reason = m.Available ? null : m.UnavailableReasonKey ?? NavMenuCatalog.ReasonPcTablet;
        // 모바일 대체 명령이 있으면 파라미터도 모바일 값만 쓴다(원래 명령용 파라미터가 대체 명령에 넘어가지 않게)
        var param = m.CommandId is not null ? m.CommandParameter : d.CommandParameter;
        return new NavEntry(id, text, group, depth, d.Kind == UiMenuItemKind.Submenu, m.CommandId ?? d.CommandId,
            ParamText(param), m.TabOrder, m.TabTitleKey, m.TabIcon, m.Available, reason, DesktopIds(id, d));
    }

    // 셸 기본 메뉴는 CoveredAutomationIds 에 자기 AutomationId 를 맨 앞에 둔 전체 목록을 담는다(C60). 그 밖(소비자·덮는 목록 없음)은 자기 AutomationId 를 앞에 붙인다.
    // 구분선·섹션의 AutomationId 는 기본값("menu."+ID)이라 화면 컨트롤이 아니므로 덮는 목록이 있으면 그것만 쓴다
    private static string[] DesktopIds(string id, UiMenuItemDefinition d)
    {
        var covered = d.Mobile.CoveredAutomationIds;
        var selfIncluded = covered.Contains(d.AutomationId, StringComparer.Ordinal) || d.Kind is UiMenuItemKind.Separator or UiMenuItemKind.Section;
        if (covered.Count > 0 && selfIncluded)
        {
            var first = covered[0];
            return NavMenuCatalog.MobileDesktopIds(id, first.StartsWith("shell.", StringComparison.Ordinal) ? first : "shell.mnu" + first, covered.Skip(1));
        }

        return NavMenuCatalog.MobileDesktopIds(id, d.AutomationId, covered);
    }

    private static string? ParamText(object? value) => value as string ?? Convert.ToString(value, CultureInfo.InvariantCulture);
}
