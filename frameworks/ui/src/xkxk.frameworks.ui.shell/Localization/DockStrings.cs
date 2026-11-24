// Dock 테마의 메뉴·표시 문자열 현지화 — Dock ControlStrings 키를 현재 언어 값(dock.*.json)으로 담은 병합 사전 1개를 앱 리소스에 넣어 덮는다. 언어를 바꾸면 사전을 통째로 바꾼다
using Avalonia;
using Avalonia.Controls;

namespace xkxk.frameworks.ui.shell.Localization;

/// <summary>
/// 근거(디컴파일): Dock.Avalonia.Themes.Fluent 12.1.0.12 는 메뉴·표시 문자열을 ControlStrings.axaml 에 키로 두고 템플릿이 DynamicResource 로 참조한다.
/// Avalonia 12.1.4 Application.TryGetResource 는 Application.Resources(병합 사전 포함)를 Styles(테마)보다 먼저 찾으므로, 같은 키를 앱 리소스 쪽에 두면 테마 값을 덮는다.
/// 대상은 사용자에게 보이는 36개(패널 ▾ 메뉴·도구 탭·핀 항목·문서 탭/MDI 창 우클릭 메뉴·끌기 미리보기, odev-7c 전수 목록과 일치)이고 디버그 창용 RootDockDebug* 는 뺀다.
/// 키마다 넣으면 리소스 알림이 키 수만큼 나므로 병합 사전 1개를 통째로 바꾼다. 넣는 시점은 시작·언어 변경 때만이다(테마 전환 경로에서는 부르지 않는다 — Q4).
/// Dock 을 올려 키 이름이 바뀌면 그 항목은 영어로 돌아오므로 ko/en 전환 캡처 시험을 둔다.
/// </summary>
public static class DockStrings
{
    private static readonly string[] Keys =
    {
        "DragPreviewControlDockString",
        "DragPreviewControlFloatString",
        "DragPreviewControlNoneString",
        "DocumentTabStripItemFloatString",
        "DocumentTabStripItemFloatAllString",
        "DocumentTabStripItemCloseString",
        "DocumentTabStripItemCloseOtherTabsString",
        "DocumentTabStripItemCloseAllTabsString",
        "DocumentTabStripItemCloseTabsLeftString",
        "DocumentTabStripItemCloseTabsRightString",
        "DocumentTabStripItemCloseTabsAboveString",
        "DocumentTabStripItemCloseTabsBelowString",
        "DocumentTabStripItemNewHorizontalDockString",
        "DocumentTabStripItemNewVerticalDockString",
        "DocumentTabStripItemTabLayoutString",
        "DocumentTabStripItemTabLayoutLeftString",
        "DocumentTabStripItemTabLayoutTopString",
        "DocumentTabStripItemTabLayoutRightString",
        "DocumentTabStripItemLayoutModeString",
        "DocumentTabStripItemLayoutModeTabbedString",
        "DocumentTabStripItemLayoutModeMdiString",
        "ToolPinItemControlFloatString",
        "ToolPinItemControlShowString",
        "ToolPinItemControlDockAsDocumentString",
        "ToolPinItemControlCloseString",
        "ToolChromeControlFloatString",
        "ToolChromeControlDockString",
        "ToolChromeControlAutoHideString",
        "ToolChromeControlDockAsDocumentString",
        "ToolChromeControlCloseString",
        "ToolTabStripItemFloatString",
        "ToolTabStripItemFloatAllString",
        "ToolTabStripItemDockString",
        "ToolTabStripItemAutoHideString",
        "ToolTabStripItemDockAsDocumentString",
        "ToolTabStripItemCloseString",
    };

    private static ResourceDictionary? s_current;

    /// <summary>앱 시작 때 1회: 지금 언어로 넣고, 언어가 바뀔 때마다 사전을 통째로 바꾼다.</summary>
    public static void Install(Application app)
    {
        if (s_current is not null)
        {
            return;
        }

        Apply(app);
        Loc.Instance.LanguageChanged += () => Apply(app);
    }

    private static void Apply(Application app)
    {
        var next = new ResourceDictionary();
        foreach (var key in Keys)
        {
            next[key] = Loc.Tr("dock." + key);
        }

        var merged = app.Resources.MergedDictionaries;
        var index = s_current is null ? -1 : merged.IndexOf(s_current);
        if (index >= 0)
        {
            merged[index] = next; // 같은 자리 교체(알림 최소화)
        }
        else
        {
            merged.Add(next);
        }

        s_current = next;
    }
}
