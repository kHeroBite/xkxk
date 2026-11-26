// 원격 명령(아이콘·명령 레지스트리 B06) — registry.dump 레지스트리·진단 덤프, command.run 명령 실행(UADR-0013)
#if PD_DIAGNOSTICS // 진단 빌드 전용(PdDiagnostics=true) — 제품판에는 이 명령이 없다
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Avalonia;
using xkxk.frameworks.ui.shell.Framework;
using xkxk.frameworks.ui.shell.Platform;

namespace xkxk.frameworks.ui.shell.Diagnostics.Remote;

/// <summary>
/// 공개 레지스트리 표면(<see cref="XkxkUi.Current"/> 의 Icons·Commands·Diagnostics)만 읽는다.
/// 명시 등록 목록은 공개 멤버가 없으므로 해석된 정의의 Owner 가 공급자 Id 가 아닌 키를 명시 등록으로 본다.
/// 처리기는 라우터 관례대로 UI 스레드에서 실행된다.
/// </summary>
internal static class RemoteRegistryCommands
{
    internal static void Register(RemoteCommandRouter r)
    {
        r.Register("registry.dump", "아이콘·명령 레지스트리 덤프(B06) [keys=1 이면 아이콘 키 전체 포함] → {icons:{count,explicit:[{key,kind,owner}],providers:[{id,count}],keys?},commands:[{id,titleKey,title,icon,iconFound,gesture,category,owner,canExecute}],diagnostics:[{id,severity,message,source}],runtime_errors}. 읽기 전용", Dump);
        r.Register("command.run", "레지스트리 명령 실행(B06) id=<명령 ID, 예 xkxk.window.cascade> → {id,canExecute,executed}. 실행 못 하면 executed=false", Run);
    }

    private static JsonNode? Dump(JsonObject a)
    {
        var app = XkxkUi.Current;
        return new JsonObject
        {
            ["icons"] = DumpIcons(app.Icons, a.Bool("keys")),
            ["commands"] = DumpCommands(app.Commands),
            ["diagnostics"] = new JsonArray(app.Diagnostics.Entries.Select(d => (JsonNode)new JsonObject
            {
                ["id"] = d.Id,
                ["severity"] = d.Severity.ToString(),
                ["message"] = d.Message,
                ["source"] = d.Source,
            }).ToArray()),
            ["runtime_errors"] = app.Diagnostics.Entries.Count(d => d.Severity == UiDiagnosticSeverity.Error),
        };
    }

    private static JsonObject DumpIcons(IUiIconRegistry icons, bool withKeys)
    {
        var keys = icons.Keys;
        var providers = icons.Providers;
        var providerIds = new HashSet<string>(StringComparer.Ordinal);
        var providerArr = new JsonArray();
        foreach (var p in providers)
        {
            providerIds.Add(p.Id);
            int count;
            try
            {
                count = p.Keys.Count;
            }
            catch (Exception ex)
            {
                count = -1; // 공급자 키 목록 예외 — 덤프는 계속한다
                System.Diagnostics.Debug.WriteLine($"[경고][레지스트리] 공급자 '{p.Id}' 키 목록 예외 {ex.GetType().Name}");
            }

            providerArr.Add(new JsonObject { ["id"] = p.Id, ["count"] = count });
        }

        var explicitArr = new JsonArray();
        foreach (var key in keys)
        {
            if (icons.TryGet(key, out var def) && !providerIds.Contains(def.Owner))
            {
                explicitArr.Add(new JsonObject { ["key"] = key, ["kind"] = def.Kind.ToString(), ["owner"] = def.Owner });
            }
        }

        var result = new JsonObject { ["count"] = keys.Count, ["explicit"] = explicitArr, ["providers"] = providerArr };
        if (withKeys)
        {
            result["keys"] = new JsonArray(keys.Select(k => (JsonNode)JsonValue.Create(k)!).ToArray());
        }

        return result;
    }

    private static JsonArray DumpCommands(IUiCommandRegistry commands)
    {
        var app = Application.Current;
        var arr = new JsonArray();
        foreach (var c in commands.Commands)
        {
            bool? iconFound = c.Icon is not { Length: > 0 } icon ? null
                : app is null ? false
                : UiIconGeometry.Find(app, app.ActualThemeVariant, icon) is not null; // 화면과 같은 해석
            arr.Add(new JsonObject
            {
                ["id"] = c.Id,
                ["titleKey"] = c.TitleKey,
                ["title"] = c.Title,
                ["icon"] = c.Icon,
                ["iconFound"] = iconFound,
                ["gesture"] = c.Gesture,
                ["category"] = c.Category,
                ["owner"] = c.Owner,
                ["canExecute"] = SafeCanExecute(commands, c.Id),
            });
        }

        return arr;
    }

    private static bool SafeCanExecute(IUiCommandRegistry commands, string id)
    {
        try
        {
            return commands.CanExecute(id);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[경고][레지스트리] CanExecute 예외 {id}: {ex.GetType().Name}");
            return false;
        }
    }

    private static JsonNode? Run(JsonObject a)
    {
        var id = a.ReqStr("id");
        var commands = XkxkUi.Current.Commands;
        if (!commands.Contains(id))
        {
            throw new RemoteCommandException($"등록되지 않은 명령입니다: {id}");
        }

        var canExecute = SafeCanExecute(commands, id);
        var executed = canExecute && commands.Execute(id);
        System.Diagnostics.Debug.WriteLine($"[Debug2][레지스트리] command.run {id} canExecute={canExecute} executed={executed}");
        return new JsonObject { ["id"] = id, ["canExecute"] = canExecute, ["executed"] = executed };
    }
}
#endif
