// 투어 마지막 레이어 내용 생성(요건 6) — 단축키·기능은 카탈로그에서, 메뉴 줄은 메뉴 카탈로그에서 만들어 메뉴가 바뀌면 설명도 함께 바뀐다
using System.Collections.Generic;
using System.Linq;
using xkxk.frameworks.ui.data.Navigation;
using xkxk.frameworks.ui.shell.Localization;
using xkxk.frameworks.ui.Controls;

namespace xkxk.frameworks.ui.shell.Tour;

internal static class TourLayerContent
{
    private const string UiKey = "sysview.tour.ui.";
    private const string MenuRowIdPrefix = "menu.";

    public static List<ShortcutRowItem> Keys(TourPlatforms platform, bool isMac) =>
        ShortcutCatalog.For(platform, isMac)
            .Select(s => new ShortcutRowItem(s.Id, s.Keys, TourCatalog.Tr(s.DescKey), platform == TourPlatforms.Web && s.WebUnavailable ? Loc.Tr(UiKey + "webUnavailable") : string.Empty))
            .ToList();

    public static List<FeatureRowItem> Features(TourModes mode, TourPlatforms platform)
    {
        var rows = ShortcutCatalog.FeaturesFor(mode, platform).Select(f => new FeatureRowItem(f.Id, TourCatalog.Tr(f.TitleKey), TourCatalog.Tr(f.BodyKey))).ToList();
        rows.AddRange(MenuRows(mode));
        return rows;
    }

    /// <summary>메뉴 카탈로그의 묶음마다 한 줄: 제목은 메뉴 이름, 본문은 그 안의 항목 이름. 하위 메뉴는 "묶음(항목·항목)" 으로 적고, 모바일형은 실행 가능한 항목만 보여 준다.</summary>
    private static IEnumerable<FeatureRowItem> MenuRows(TourModes mode)
    {
        var entries = NavMenuCatalog.Entries;
        foreach (var group in entries.Where(e => e.Depth == 0))
        {
            var members = entries.Where(e => e.Group == group.Group && e.Depth > 0).ToList();
            var names = new List<string>();
            for (var i = 0; i < members.Count; i++)
            {
                if (members[i].Depth != 1)
                {
                    continue;
                }

                var children = members.Skip(i + 1).TakeWhile(e => e.Depth > 1).Where(e => IsShown(e, mode)).Select(e => MenuLabel.Of(e.TextKey)).ToList();
                var hasChildEntries = i + 1 < members.Count && members[i + 1].Depth > 1;
                if (children.Count > 0)
                {
                    names.Add(MenuLabel.Of(members[i].TextKey) + "(" + string.Join("·", children) + ")");
                }
                else if (!hasChildEntries && IsShown(members[i], mode))
                {
                    names.Add(MenuLabel.Of(members[i].TextKey));
                }
            }

            if (names.Count > 0)
            {
                yield return new FeatureRowItem(MenuRowIdPrefix + group.Id, MenuLabel.Of(group.TextKey), string.Join(" · ", names));
            }
        }
    }

    private static bool IsShown(NavEntry entry, TourModes mode)
    {
        var rules = TourCatalog.Current.MenuRows;
        return !rules.Skip.Contains(entry.Id)
            && (!rules.Requires.TryGetValue(entry.Id, out var needs) || TourConditions.IsMet(needs))
            && (mode != TourModes.Nav || entry.Applicable);
    }
}
