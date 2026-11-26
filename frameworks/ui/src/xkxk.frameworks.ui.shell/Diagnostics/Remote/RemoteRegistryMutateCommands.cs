// 원격 명령(메뉴·아이콘 레지스트리 변경 B07) — registry.menu.add·remove, registry.icon.add·remove 를 실행 중 앱 주체(app)로 호출(UADR-0013·0014)
#if PD_DIAGNOSTICS // 진단 빌드 전용(PdDiagnostics=true) — 제품판에는 이 명령의 라우트 자체가 없다
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using xkxk.frameworks.ui.shell.Framework;

namespace xkxk.frameworks.ui.shell.Diagnostics.Remote;

/// <summary>
/// 실행 중 레지스트리(<see cref="XkxkUi.Current"/> 의 Menus·Icons — 등록 주체 <see cref="UiRegistryOwners.App"/>)를 바꾸는 시험용 명령.
/// 공개 표면만 부르므로 솔루션이 실행 중에 같은 호출을 했을 때와 같은 규칙·진단(XKUI02xx·04xx)을 탄다.
/// 응답은 {ok, diagnostics[]} 이고 diagnostics 는 이 호출 동안 새로 쌓인 진단만이다. 처리기는 라우터 관례대로 UI 스레드에서 실행된다.
/// </summary>
internal static class RemoteRegistryMutateCommands
{
    internal static void Register(RemoteCommandRouter r)
    {
        r.Register("registry.menu.add", "실행 중 메뉴 추가(B07, 주체 app) id=<메뉴 ID> parent=<부모 메뉴 ID, 예 xkxk.file> command=<명령 ID, 예 xkxk.window.cascade> [icon=<아이콘 키>] [title=<제목>] → {ok,id,automationId,diagnostics[]}. AutomationId 는 menu.<id>", MenuAdd);
        r.Register("registry.menu.remove", "실행 중 메뉴 제거(B07, 주체 app — 앱이 추가한 항목만) id=<메뉴 ID> → {ok,id,diagnostics[]}", MenuRemove);
        r.Register("registry.icon.add", "실행 중 아이콘 명시 등록(B07, 주체 app) key=<아이콘 키> path=<PathData> → {ok,key,diagnostics[]}. 같은 키를 쓰는 메뉴는 Icons.Changed 로 다시 그린다", IconAdd);
        r.Register("registry.icon.remove", "실행 중 아이콘 명시 등록 제거(B07, 주체 app — 앱이 등록한 키만) key=<아이콘 키> → {ok,key,diagnostics[]}", IconRemove);
    }

    private static JsonNode? MenuAdd(JsonObject a)
    {
        var id = a.ReqStr("id");
        var parent = a.ReqStr("parent");
        var command = a.ReqStr("command");
        var icon = a.Str("icon");
        var title = a.Str("title");
        var menus = XkxkUi.Current.Menus;
        var (ok, diags) = Track(() => menus.Add(id, o =>
        {
            o.Parent = parent;
            o.Kind = UiMenuItemKind.Command;
            o.CommandId = command;
            o.Icon = string.IsNullOrEmpty(icon) ? null : icon;
            o.Title = string.IsNullOrEmpty(title) ? id : title; // 제목 키 없이 고정 문구(시험 항목)
        }));
        var automationId = menus.TryGet(id, out var def) ? def.AutomationId : null;
        System.Diagnostics.Debug.WriteLine($"[Debug2][레지스트리] registry.menu.add {id} parent={parent} command={command} icon={icon} ok={ok} diag={diags.Count}");
        return new JsonObject { ["ok"] = ok, ["id"] = id, ["automationId"] = automationId, ["diagnostics"] = diags };
    }

    private static JsonNode? MenuRemove(JsonObject a)
    {
        var id = a.ReqStr("id");
        var (ok, diags) = Track(() => XkxkUi.Current.Menus.Remove(id));
        System.Diagnostics.Debug.WriteLine($"[Debug2][레지스트리] registry.menu.remove {id} ok={ok} diag={diags.Count}");
        return new JsonObject { ["ok"] = ok, ["id"] = id, ["diagnostics"] = diags };
    }

    private static JsonNode? IconAdd(JsonObject a)
    {
        var key = a.ReqStr("key");
        var path = a.ReqStr("path");
        var (ok, diags) = Track(() => XkxkUi.Current.Icons.AddGeometry(key, path));
        System.Diagnostics.Debug.WriteLine($"[Debug2][레지스트리] registry.icon.add {key} ok={ok} diag={diags.Count}");
        return new JsonObject { ["ok"] = ok, ["key"] = key, ["diagnostics"] = diags };
    }

    private static JsonNode? IconRemove(JsonObject a)
    {
        var key = a.ReqStr("key");
        var (ok, diags) = Track(() => XkxkUi.Current.Icons.Remove(key));
        System.Diagnostics.Debug.WriteLine($"[Debug2][레지스트리] registry.icon.remove {key} ok={ok} diag={diags.Count}");
        return new JsonObject { ["ok"] = ok, ["key"] = key, ["diagnostics"] = diags };
    }

    /// <summary>호출 전후 진단 목록을 비교해 이번 호출이 새로 남긴 진단만 돌려준다(참조 비교 — 목록 상한으로 앞이 밀려도 새 항목만 남는다).</summary>
    private static (bool Ok, JsonArray Diagnostics) Track(Func<bool> action)
    {
        var diagnostics = XkxkUi.Current.Diagnostics;
        var before = new HashSet<object>(diagnostics.Entries.Cast<object>(), ReferenceEqualityComparer.Instance);
        var ok = action();
        var added = diagnostics.Entries.Where(d => !before.Contains(d)).Select(d => (JsonNode)new JsonObject
        {
            ["id"] = d.Id,
            ["severity"] = d.Severity.ToString(),
            ["message"] = d.Message,
            ["source"] = d.Source,
        }).ToArray();
        return (ok, new JsonArray(added));
    }
}
#endif
