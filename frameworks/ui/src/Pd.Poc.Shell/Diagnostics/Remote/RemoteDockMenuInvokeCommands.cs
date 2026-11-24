// 원격 dock.menuInvoke(진단) — Dock 우클릭·▾·📌 메뉴 항목을 팝업을 열지 않고 실행한다. 메뉴 항목에 바인딩된 Command 를 사용자 클릭과 같이 부른다(전경·실입력 없음)
#if PD_DIAGNOSTICS // 진단 빌드 전용(PdDiagnostics=true)
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace Pd.Poc.Shell.Diagnostics.Remote;

internal static class RemoteDockMenuInvokeCommands
{
    /// <summary>
    /// 배경(team-lead 2026-10-30): L2 PostMessage 우클릭이 앱을 사용자 화면 맨 앞으로 올려 금지됐다. 그래서 D-PW2(📌 핀 메뉴)·Q16-17(📌 '탭 문서로 도킹')을
    /// 시험할 경로가 없었다. 이 명령은 메뉴 항목을 찾아 그 Command 를 CanExecute 확인 뒤 Execute(CommandParameter) 한다.
    /// 사용자가 메뉴 항목을 눌렀을 때 MenuItem 이 하는 일(Command 실행)과 같고, 팝업·새 TopLevel·OS 입력은 만들지 않는다.
    /// 다른 점: 메뉴가 열리지 않아 Command 바인딩이 아직 안 풀렸으면, 메뉴가 열릴 때처럼 대상 컨트롤의 DataContext 를 항목에 잠시 넣어 바인딩을 푼 뒤 되돌린다(injected=true 로 표시).
    /// </summary>
    internal static void Register(RemoteCommandRouter r)
    {
        r.Register("dock.menuInvoke",
            "Dock 메뉴 항목 실행(팝업 없음 — 항목 Command 를 클릭과 같이 실행) target=tab|mdi|tool|toolchrome|pin doc=<문서·도구 Id> " +
            "(key=<헤더 리소스 키, 예 ToolPinItemControlDockAsDocumentString> | index=<dock.menuHeaders 의 index> | header=<표시 문구>) [dry=true 면 실행 안 함] → " +
            "{status: Executed|DryRun|CannotExecute|NoCommand, index, key, header, command, parameterId, injected} — 항목 목록·순번은 dock.menuHeaders 로 본다", a =>
            {
                var target = (a.Str("target") ?? "pin").ToLowerInvariant();
                var host = FindMenuHost(target, a.ReqStr("doc"));
                var items = MenuItemsOf(host) ?? throw new RemoteCommandException($"{host.GetType().Name} 에 메뉴가 없습니다(Android 는 열 때 붙음).");
                var flat = new List<MenuItem>();
                Flatten(items, flat);
                RemoteDockMenuCommands.MenuMap.TryGetValue(target, out var map);
                var mapUsable = map is not null && map.Length == flat.Count; // 항목 수가 대응표와 다르면(Dock 판 변경 등) 키로 고르지 않는다
                var index = PickIndex(a, flat, mapUsable ? map : null);
                var mi = flat[index];
                var result = new JsonObject
                {
                    ["index"] = index, ["key"] = mapUsable ? map![index] : null, ["header"] = MenuHeaderText.Of(mi.Header),
                };
                return InvokeItem(host, mi, a.Bool("dry"), result);
            });
    }

    /// <summary>dock.menuHeaders 와 같은 규칙으로 메뉴를 가진 컨트롤을 찾는다(형식 이름 + DataContext Id).</summary>
    private static Control FindMenuHost(string target, string id)
    {
        var typeHint = target switch
        {
            "tab" => "DocumentTabStripItem",
            "mdi" => "MdiDocumentWindow",
            "tool" => "ToolTabStripItem",
            "toolchrome" => "ToolChromeControl",
            "pin" => "ToolPinItemControl",
            _ => throw new RemoteCommandException("target=tab|mdi|tool|toolchrome|pin 이 필요합니다."),
        };
        return VisualTreeInspector.TopLevels()
            .SelectMany(t => t.GetVisualDescendants().OfType<Control>())
            .FirstOrDefault(c => c.GetType().Name.Contains(typeHint) && (c.DataContext as Dock.Model.Core.IDockable)?.Id == id)
            ?? throw new RemoteCommandException($"{typeHint} 중 Id={id} 인 컨트롤을 찾지 못했습니다(숨김·접힘이면 트리에 없음 — pin 은 먼저 panel.pin on=true).");
    }

    private static IEnumerable? MenuItemsOf(Control host) => host.ContextMenu is { } menu ? menu.Items
        : host.ContextFlyout is MenuFlyout flyout ? flyout.ItemsSource ?? flyout.Items
        : null;

    /// <summary>MenuItem 을 깊이 우선 순서로 편다(구분선 제외) — dock.menuHeaders 의 index 와 같은 번호.</summary>
    private static void Flatten(IEnumerable items, List<MenuItem> flat)
    {
        foreach (var item in items)
        {
            if (item is not MenuItem mi) { continue; }
            flat.Add(mi);
            if (mi.Items.Count > 0) { Flatten(mi.Items, flat); }
        }
    }

    private static int PickIndex(JsonObject a, List<MenuItem> flat, string?[]? map)
    {
        if (a.Int("index") is { } i)
        {
            return i >= 0 && i < flat.Count ? i : throw new RemoteCommandException($"index 범위 밖입니다(0~{flat.Count - 1}).");
        }

        if (a.Str("key") is { Length: > 0 } key)
        {
            var k = map is null ? -1 : System.Array.IndexOf(map, key);
            return k >= 0 ? k : throw new RemoteCommandException($"key 를 대응표에서 찾지 못했습니다: {key}(대응표 {(map is null ? "사용 불가" : string.Join(",", map))}).");
        }

        var header = a.ReqStr("header");
        var h = flat.FindIndex(m => MenuHeaderText.Of(m.Header) == header);
        return h >= 0 ? h : throw new RemoteCommandException($"header 일치 항목 없음: {header}(열리기 전 헤더가 비면 key= 나 index= 를 쓴다).");
    }

    /// <summary>항목 Command 를 클릭과 같이 실행한다. 바인딩이 안 풀렸으면 대상 DataContext 를 잠시 넣는다(메뉴가 열릴 때와 같은 문맥).</summary>
    private static JsonObject InvokeItem(Control host, MenuItem mi, bool dry, JsonObject result)
    {
        var injected = mi.Command is null && host.DataContext is not null;
        if (injected) { mi.DataContext = host.DataContext; }
        try
        {
            var cmd = mi.Command;
            var param = mi.CommandParameter;
            result["command"] = cmd?.GetType().Name;
            result["parameterId"] = (param as Dock.Model.Core.IDockable)?.Id ?? param?.GetType().Name;
            result["injected"] = injected;
            if (!mi.IsEnabled) { result["status"] = "CannotExecute"; result["reason"] = "item-disabled"; return result; }
            if (cmd is null) { result["status"] = "NoCommand"; return result; }
            if (!cmd.CanExecute(param)) { result["status"] = "CannotExecute"; return result; }
            if (dry) { result["status"] = "DryRun"; return result; }
            cmd.Execute(param);
            result["status"] = "Executed";
            return result;
        }
        finally
        {
            if (injected) { mi.ClearValue(StyledElement.DataContextProperty); }
        }
    }
}
#endif
