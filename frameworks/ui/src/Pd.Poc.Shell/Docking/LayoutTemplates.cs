// 내장 레이아웃 템플릿(UI-11, 읽기 전용) — 열린 문서는 그대로 두고 배치 방식만 바꾼다(위치 고정 창은 배치에서 제외). 이름·설명은 다국어 사전 키
using Pd.Client.Data.Layout;
using System;
using System.Collections.Generic;
using System.Linq;
using Pd.Poc.Shell.Localization;

namespace Pd.Poc.Shell.Docking;

public static class LayoutTemplates
{
    // HidePanels: '집중' 은 탐색기·로그를 숨긴다(알림 패널 제외). 나머지 템플릿은 숨긴 패널을 되돌린 뒤 배치한다.
    private static readonly (string Id, string Key, ShellArrange Arrange, bool HidePanels)[] Items =
    {
        ("tpl.mdi-cascade", "layout.tpl.mdiCascade", ShellArrange.Cascade, false),
        ("tpl.mdi-tile-v", "layout.tpl.mdiTileV", ShellArrange.TileVertical, false),
        ("tpl.mdi-tile-h", "layout.tpl.mdiTileH", ShellArrange.TileHorizontal, false),
        ("tpl.tabbed", "layout.tpl.tabbed", ShellArrange.Tabbed, false),
        ("tpl.focus", "layout.tpl.focus", ShellArrange.Tabbed, true),
    };

    /// <summary>현재 언어로 만든 템플릿 목록(언어가 바뀌면 다시 읽는다).</summary>
    public static IReadOnlyList<LayoutTemplate> All =>
        Items.Select(i => new LayoutTemplate(i.Id, Loc.Tr(i.Key), Loc.Tr(i.Key + ".desc"))).ToList();

    public static bool IsTemplateId(string? id) => id is not null && id.StartsWith("tpl.", StringComparison.Ordinal);

    /// <summary>템플릿 적용. 없는 Id 면 false.</summary>
    public static bool Apply(IShellCommands shell, string templateId)
    {
        foreach (var (id, _, arrange, hidePanels) in Items)
        {
            if (string.Equals(id, templateId, StringComparison.Ordinal))
            {
                shell.SetToolPanelsHidden(hidePanels);
                shell.Arrange(arrange);
                return true;
            }
        }

        return false;
    }
}
