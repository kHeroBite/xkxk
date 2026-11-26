// 모바일형 하단 탭·삼선 서랍이 쓰는 셸 메뉴 전체 카탈로그 — UI 비의존 순수 데이터 레이어(ShellMenuTable 에서 파생, 데스크톱 메뉴 AutomationId 1:1 대응표 포함)
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace xkxk.frameworks.ui.data.Navigation;

/// <summary>메뉴 묶음 구분(제목줄 상위 메뉴 4개). Custom 은 셸 4메뉴가 아닌 최상위(소비자 메뉴)다.</summary>
public enum NavGroup
{
    File,
    Window,
    View,
    System,
    Custom,
}

/// <summary>
/// 카탈로그 항목 1개. Applicable=false 이면 모바일형에서 실행하지 않고 비활성+사유(ReasonKey)로 서랍에 둔다.
/// Command 는 레지스트리 명령 ID 다(모바일 명령이 있으면 그것). DesktopIds 는 이 항목이 대신하는 데스크톱 메뉴 AutomationId 들이다(도달률 판정용).
/// </summary>
public sealed record NavEntry(string Id, string TextKey, NavGroup Group, int Depth, bool IsGroup, string? Command, string? Param,
    int TabOrder, string? TabKey, string? Icon, bool Applicable, string? ReasonKey, string[] DesktopIds);

/// <summary>셸 메뉴 전수 + 하단 탭 배분 데이터. 행은 <see cref="ShellMenuTable"/> 에서 파생한다(탭을 바꾸려면 표의 TabOrder 만 고친다, 결정 12).</summary>
public static class NavMenuCatalog
{
    public const string ReasonPcTablet = "nav.reason.pcTablet";
    public const string ReasonUnavailable = "nav.reason.unavailable";

    /// <summary>하단 '더보기' 탭(항목이 아니라 서랍 열기).</summary>
    public const string MoreIcon = "M2,7 H5 V10 H2 Z M7.5,7 H10.5 V10 H7.5 Z M13,7 H16 V10 H13 Z";
    public const string MoreTabKey = "nav.tab.more";

    private const string AutomationPrefix = "shell.mnu";

    /// <summary>카탈로그 전체(서랍 표시 순서). 하단 탭은 TabOrder >= 0 인 항목.</summary>
    public static IReadOnlyList<NavEntry> Entries { get; } = Build(ShellMenuTable.Rows);

    /// <summary>하단 탭(순서대로, '더보기'는 포함하지 않음).</summary>
    public static IReadOnlyList<NavEntry> Tabs => TabsOf(Entries);

    /// <summary>최상위 메뉴 ID → 묶음(셸 4메뉴가 아니면 Custom).</summary>
    public static NavGroup GroupOf(string? rootId) => rootId switch
    {
        "xkxk.file" => NavGroup.File,
        "xkxk.window" => NavGroup.Window,
        "xkxk.view" => NavGroup.View,
        "xkxk.system" => NavGroup.System,
        _ => NavGroup.Custom,
    };

    /// <summary>모바일 항목 id = EntryId ?? (AutomationId 가 shell.mnu 로 시작하면 그 뒤) ?? 메뉴 ID.</summary>
    public static string MobileEntryId(string id, string? automationId, string? entryId) =>
        entryId ?? (automationId is { } a && a.StartsWith(AutomationPrefix, StringComparison.Ordinal) && a.Length > AutomationPrefix.Length
            ? a.Substring(AutomationPrefix.Length) : id);

    /// <summary>모바일 항목이 대신하는 데스크톱 AutomationId 목록 = [AutomationId ?? shell.mnu+항목 id] + covers(shell. 로 시작하지 않으면 shell.mnu 접두, 중복은 앞의 것만).</summary>
    public static string[] MobileDesktopIds(string mobileId, string? automationId, IEnumerable<string> covers)
    {
        var list = new List<string> { automationId ?? AutomationPrefix + mobileId };
        foreach (var c in covers)
        {
            var d = c.StartsWith("shell.", StringComparison.Ordinal) ? c : AutomationPrefix + c;
            if (!list.Contains(d))
            {
                list.Add(d);
            }
        }

        return list.ToArray();
    }

    /// <summary>표 행 1개가 서랍 항목이 되는가(InDrawer 이고, Separator·Section 은 EntryId 가 있을 때만).</summary>
    public static bool IsDrawerRow(ShellMenuRow row) =>
        row.Mobile.InDrawer && (row.Kind is ShellMenuRowKind.Command or ShellMenuRowKind.Submenu || row.Mobile.EntryId is not null);

    // 트리 전위 순회(형제 순서 = 행 순서) 중 서랍 행만 NavEntry 로
    private static List<NavEntry> Build(IReadOnlyList<ShellMenuRow> rows)
    {
        var kids = new Dictionary<string, List<ShellMenuRow>>(StringComparer.Ordinal);
        var roots = new List<ShellMenuRow>();
        foreach (var r in rows)
        {
            if (r.Parent is null)
            {
                roots.Add(r);
            }
            else
            {
                if (!kids.TryGetValue(r.Parent, out var list))
                {
                    kids[r.Parent] = list = new List<ShellMenuRow>();
                }

                list.Add(r);
            }
        }

        var output = new List<NavEntry>();
        foreach (var root in roots)
        {
            Walk(root, 0, GroupOf(root.Id), kids, output);
        }

        return output;
    }

    private static void Walk(ShellMenuRow r, int depth, NavGroup group, Dictionary<string, List<ShellMenuRow>> kids, List<NavEntry> output)
    {
        if (IsDrawerRow(r))
        {
            var m = r.Mobile;
            var id = MobileEntryId(r.Id, r.AutomationId, m.EntryId);
            output.Add(new NavEntry(id, m.TitleKey ?? r.TitleKey, group, depth, r.Kind == ShellMenuRowKind.Submenu, m.CommandId ?? r.CommandId, m.CommandId is not null ? m.Param : r.Param,
                m.TabOrder, m.TabKey, m.TabIcon, m.Available, m.Available ? null : m.ReasonKey, MobileDesktopIds(id, r.AutomationId, m.Covers)));
        }

        if (kids.TryGetValue(r.Id, out var children))
        {
            foreach (var c in children)
            {
                Walk(c, depth + 1, group, kids, output);
            }
        }
    }

    private static List<NavEntry> TabsOf(IReadOnlyList<NavEntry> entries) => entries.Where(e => e.TabOrder >= 0).OrderBy(e => e.TabOrder).ToList();

    /// <summary>정적 덤프(JSON) — 판정기·원격 진단 nav.menu 의 기본값(셸 메뉴만). enabled 는 Applicable 기준(런타임 상태는 MobileNavViewModel.ToJson).</summary>
    public static string ToJson(Func<NavEntry, (bool Enabled, string? Reason)>? state = null) => ToJson(Entries, state);

    /// <summary>주어진 목록(예: 레지스트리 투영 목록)의 JSON 덤프. 형식은 기본 덤프와 같다. state 가 null 이면 Applicable 기준이다.</summary>
    public static string ToJson(IReadOnlyList<NavEntry> entries, Func<NavEntry, (bool Enabled, string? Reason)>? state)
    {
        var sb = new StringBuilder("{\"tabs\":[");
        sb.Append(string.Join(",", TabsOf(entries).Select(t => "\"" + t.Id + "\"")));
        sb.Append("],\"drawer\":[");
        sb.Append(string.Join(",", entries.Select(e =>
        {
            var (en, why) = state?.Invoke(e) ?? (e.Applicable, e.Applicable ? null : e.ReasonKey);
            return "{\"id\":\"" + e.Id + "\",\"group\":" + (e.IsGroup ? "true" : "false") + ",\"enabled\":" + (en ? "true" : "false")
                + ",\"reason\":" + (why is null ? "null" : "\"" + why + "\"")
                + ",\"desktopIds\":[" + string.Join(",", e.DesktopIds.Select(d => "\"" + d + "\"")) + "]}";
        })));
        return sb.Append("]}").ToString();
    }
}
