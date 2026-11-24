// 원격 dock.menuHeaders(진단) — Dock 문서 탭·MDI 창·도구 탭의 우클릭 메뉴 항목 Header 를 팝업을 열지 않고 읽는다(UI-16 Dock 메뉴 다국어 판정, lead A안)
#if PD_DIAGNOSTICS // 진단 빌드 전용(PdDiagnostics=true)
using System.Collections;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Diagnostics;
using Avalonia.VisualTree;

namespace xkxk.frameworks.ui.shell.Diagnostics.Remote;

internal static class RemoteDockMenuCommands
{
    /// <summary>RemoteScrollCommands.Register 가 부른다(라우터 변경 없이 등록).</summary>
    internal static void Register(RemoteCommandRouter r)
    {
        r.Register("dock.menuHeaders",
            "Dock 우클릭 메뉴 Header 읽기(팝업·새 TopLevel 생성 없음) target=tab|mdi|tool doc=<문서·도구 Id> → " +
            "{control, source(ContextMenu|ContextFlyout), items:[{depth, header, resolved_via, key?, diagnostic?}]} — " +
            "메뉴가 열리기 전이라 DynamicResource 헤더가 비면, 바인딩 진단 문자열의 '…String' 키를 대상 컨트롤 기준 TryFindResource 로 해석(resolved_via=resource)", a =>
            {
                var target = (a.Str("target") ?? "tab").ToLowerInvariant();
                var id = a.ReqStr("doc");
                var typeHint = target switch
                {
                    "tab" => "DocumentTabStripItem",
                    "mdi" => "MdiDocumentWindow",
                    "tool" => "ToolTabStripItem",
                    "toolchrome" => "ToolChromeControl",
                    "pin" => "ToolPinItemControl",
                    _ => throw new RemoteCommandException("target=tab|mdi|tool|toolchrome|pin 이 필요합니다."),
                };
                var host = VisualTreeInspector.TopLevels()
                    .SelectMany(t => t.GetVisualDescendants().OfType<Control>())
                    .FirstOrDefault(c => c.GetType().Name.Contains(typeHint) && (c.DataContext as Dock.Model.Core.IDockable)?.Id == id)
                    ?? throw new RemoteCommandException($"{typeHint} 중 Id={id} 인 컨트롤을 찾지 못했습니다(숨김·접힘이면 트리에 없음).");

                IEnumerable? items = null;
                // 한계(otest-m-1 i1222, lead): Android 는 ContextMenu/Flyout 이 열 때 붙어 열기 전에는 source=none·items=[] — 판정은 캡처로 한다(보강 안 함).
                var source = "none";
                if (host.ContextMenu is { } menu) { items = menu.Items; source = "ContextMenu"; }
                else if (host.ContextFlyout is MenuFlyout flyout) { items = flyout.ItemsSource ?? flyout.Items; source = "ContextFlyout"; }

                var list = new JsonArray();
                var ordinal = 0;
                MenuMap.TryGetValue(target, out var map);
                var mapUsable = items is not null && map is not null && CountItems(items) == map.Length; // 항목 수가 표와 다르면(Dock 판 변경 등) 대응표를 쓰지 않는다
                if (items is not null) { Collect(host, items, 0, list, mapUsable ? map : null, ref ordinal); }
                return new JsonObject
                {
                    ["control"] = host.GetType().Name, ["source"] = source, ["items"] = list,
                    ["map"] = map is null ? "none" : mapUsable ? "used-if-needed" : $"skipped(count {CountItems(items)}≠{map.Length})",
                };
            });
    }

    /// <summary>
    /// 대응표(근거: Dock.Avalonia.Themes.Fluent 12.1.0.12 디컴파일 — logs/odev3/ilspy_theme, 메뉴 리소스 생성 순서).
    /// 메뉴 항목(MenuItem)을 깊이 우선 순서로 센 번호 → 헤더 리소스 키. null = 테마가 고정 영어 문자열을 넣는 항목(키 없음 — 다국어 사전으로 바뀌지 않음).
    /// 실값·진단 키로 못 풀 때만 쓰고 resolved_via=mapped 로 표시한다(판정자가 추정 대응임을 알 수 있게).
    /// </summary>
    internal static readonly System.Collections.Generic.Dictionary<string, string?[]> MenuMap = new()
    {
        ["tab"] = // DocumentTabStripItemContextMenu
        [
            "DocumentTabStripItemFloatString", "DocumentTabStripItemFloatAllString",
            "DocumentTabStripItemCloseString", "DocumentTabStripItemCloseOtherTabsString", "DocumentTabStripItemCloseAllTabsString",
            "DocumentTabStripItemCloseTabsLeftString", "DocumentTabStripItemCloseTabsAboveString",
            "DocumentTabStripItemCloseTabsRightString", "DocumentTabStripItemCloseTabsBelowString",
            "DocumentTabStripItemNewHorizontalDockString", "DocumentTabStripItemNewVerticalDockString",
            "DocumentTabStripItemTabLayoutString", "DocumentTabStripItemTabLayoutLeftString", "DocumentTabStripItemTabLayoutTopString", "DocumentTabStripItemTabLayoutRightString",
            "DocumentTabStripItemLayoutModeString", "DocumentTabStripItemLayoutModeTabbedString", "DocumentTabStripItemLayoutModeMdiString",
        ],
        ["mdi"] = // MdiDocumentWindowContextMenu — Cascade·Tile Horizontal·Tile Vertical·Restore 는 테마 고정 영어(키 없음)
        [
            "DocumentTabStripItemFloatString", "DocumentTabStripItemFloatAllString",
            "DocumentTabStripItemCloseString", "DocumentTabStripItemCloseOtherTabsString", "DocumentTabStripItemCloseAllTabsString",
            "DocumentTabStripItemNewHorizontalDockString", "DocumentTabStripItemNewVerticalDockString",
            null, null, null, null,
            "DocumentTabStripItemLayoutModeString", "DocumentTabStripItemLayoutModeTabbedString", "DocumentTabStripItemLayoutModeMdiString",
        ],
        ["tool"] = // ToolTabStripItem 메뉴
        [
            "ToolTabStripItemFloatString", "ToolTabStripItemFloatAllString", "ToolTabStripItemDockString",
            "ToolTabStripItemDockAsDocumentString", "ToolTabStripItemAutoHideString", "ToolTabStripItemCloseString",
        ],
        ["toolchrome"] = // ToolChromeControlContextMenu(MenuFlyout)
        [
            "ToolChromeControlFloatString", "ToolChromeControlDockString", "ToolChromeControlDockAsDocumentString",
            "ToolChromeControlAutoHideString", "ToolChromeControlCloseString",
        ],
        ["pin"] = // ToolPinItemControlContextMenu
        [
            "ToolPinItemControlFloatString", "ToolPinItemControlShowString", "ToolPinItemControlDockAsDocumentString", "ToolPinItemControlCloseString",
        ],
    };

    private static int CountItems(IEnumerable? items)
    {
        var n = 0;
        if (items is null) { return 0; }
        foreach (var item in items)
        {
            if (item is MenuItem mi) { n += 1 + CountItems(mi.Items); }
        }

        return n;
    }

    // 바인딩 진단 문자열에서 Dock 문자열 리소스 키(…String) 를 찾는다(예: DocumentTabStripItemCloseString)
    private static readonly Regex DockStringKey = new(@"\b([A-Za-z]+String)\b", RegexOptions.Compiled);

    /// <summary>
    /// 해석 순서: ① header — Header 실값이 이미 문자열(메뉴가 붙어 DynamicResource 가 풀렸거나 고정 문자열)
    /// ② resource — Header 바인딩 진단 문자열(GetDiagnostic)에서 …String 키를 찾음(M4 때 GetDiagnostic 이 null 이던 전례가 있어 안 될 수 있음)
    /// ③ mapped — 대응표(디컴파일 근거, 항목 수 일치 시)로 키를 정함 ④ literal — 대응표상 테마 고정 영어(키 없음) ⑤ unresolved.
    /// ②·③ 은 대상 컨트롤 기준 TryFindResource(현재 테마 변형)로 값을 읽는다 — 앱 리소스의 다국어 사전(DockStrings)이 테마 값을 덮으므로 화면에 나올 문자열과 같다.
    /// </summary>
    private static void Collect(Control host, IEnumerable items, int depth, JsonArray list, string?[]? map, ref int ordinal)
    {
        foreach (var item in items)
        {
            if (item is not MenuItem mi)
            {
                if (item is Separator) { list.Add(new JsonObject { ["depth"] = depth, ["header"] = "-", ["resolved_via"] = "separator" }); }
                continue;
            }

            var index = ordinal++;
            var entry = new JsonObject { ["depth"] = depth, ["index"] = index };
            var header = MenuHeaderText.Of(mi.Header);
            if (!string.IsNullOrEmpty(header))
            {
                entry["header"] = header;
                entry["resolved_via"] = "header";
            }
            else
            {
                var diag = mi.GetDiagnostic(Avalonia.Controls.Primitives.HeaderedSelectingItemsControl.HeaderProperty)?.Diagnostic;
                entry["diagnostic"] = diag;
                string? key = null;
                if (diag is not null && DockStringKey.Match(diag) is { Success: true } m)
                {
                    key = m.Groups[1].Value;
                    entry["resolved_via"] = "resource";
                }
                else if (map is not null && index < map.Length)
                {
                    key = map[index];
                    entry["resolved_via"] = key is null ? "literal" : "mapped";
                }
                else
                {
                    entry["resolved_via"] = "unresolved";
                }

                entry["key"] = key;
                entry["header"] = key is not null && host.TryFindResource(key, host.ActualThemeVariant, out var v) ? MenuHeaderText.Of(v) : null;
            }

            list.Add(entry);
            if (mi.Items.Count > 0) { Collect(host, mi.Items, depth + 1, list, map, ref ordinal); }
        }
    }
}
#endif
