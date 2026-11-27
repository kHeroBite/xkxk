// 원격 명령(아이콘·명령·메뉴·창 레지스트리 B06·B07·B09) — registry.dump 레지스트리·진단 덤프, command.run 명령 실행(UADR-0013·0014·0016)
#if PD_DIAGNOSTICS // 진단 빌드 전용(PdDiagnostics=true) — 제품판에는 이 명령이 없다
using System;
using System.Linq;
using System.Text.Json.Nodes;
using Avalonia;
using xkxk.frameworks.ui.shell.Framework;
using xkxk.frameworks.ui.shell.Platform;

namespace xkxk.frameworks.ui.shell.Diagnostics.Remote;

/// <summary>
/// 공개 레지스트리 표면(<see cref="XkxkUi.Current"/> 의 Icons·Commands·Menus·Windows·Diagnostics)을 읽는다.
/// 명시 등록 키 목록만 공개 멤버가 없어 내부 저장소 목록(UiIconStore.ExplicitKeys)을 쓴다(B07).
/// 처리기는 라우터 관례대로 UI 스레드에서 실행된다.
/// </summary>
internal static class RemoteRegistryCommands
{
    internal static void Register(RemoteCommandRouter r)
    {
        r.Register("registry.dump", "아이콘·명령·메뉴 레지스트리 덤프(B06·B07) [keys=1 이면 아이콘 키 전체 포함] → {icons:{count,explicit:[키],providers:[{id,count}],keys?},commands:[{id,titleKey,title,icon,iconFound,gesture,category,owner,canExecute}],menus:[{id,parent,kind,command,automationId,owner,hidden,visible,enabled,section}],diagnostics:[{id,severity,message,source}],runtime_errors,windows(B09):[{id,owner,aliases,placement,allowDuplicate(등록 값),hasContent}]}. 읽기 전용", Dump);
        r.Register("command.run", "레지스트리 명령 실행(B06) id=<명령 ID, 예 xkxk.window.cascade> → {id,canExecute,executed}. 실행 못 하면 executed=false", Run);
    }

    private static JsonNode? Dump(JsonObject a)
    {
        var app = XkxkUi.Current;
        var windows = DumpWindows(app.Windows); // 처음 접근이면 기본 창 등록 진단이 남으므로 diagnostics 를 읽기 전에
        return new JsonObject
        {
            ["icons"] = DumpIcons(app.Icons, a.Bool("keys")),
            ["commands"] = DumpCommands(app.Commands),
            ["menus"] = DumpMenus(app.Menus),
            ["windows"] = windows,
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
        var providerArr = new JsonArray();
        foreach (var p in providers)
        {
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

        // 명시 등록 키는 저장소의 내부 목록을 그대로 쓴다(B07 — 해석 Owner 로 빼던 추정 계산 제거, a8 한계 e)
        var explicitArr = new JsonArray(UiAppContext.Instance.IconStore.ExplicitKeys.Select(k => (JsonNode)JsonValue.Create(k)!).ToArray());

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

    private static JsonArray DumpMenus(IUiMenuRegistry menus)
    {
        var arr = new JsonArray();
        foreach (var m in menus.Items)
        {
            bool? visible = null, enabled = null;
            try
            {
                var state = menus.GetState(m.Id, UiMenuSurface.Desktop); // 화면과 같은 평가(조건 예외는 저장소가 XKUI0411 로 남긴다)
                visible = state.IsVisible;
                enabled = state.IsEnabled;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[경고][레지스트리] 메뉴 상태 예외 {m.Id}: {ex.GetType().Name}");
            }

            arr.Add(new JsonObject
            {
                ["id"] = m.Id,
                ["parent"] = m.Parent,
                ["kind"] = m.Kind.ToString(),
                ["command"] = m.CommandId,
                ["automationId"] = m.AutomationId,
                ["owner"] = m.Owner,
                ["hidden"] = m.IsHidden,
                ["visible"] = visible,
                ["enabled"] = enabled,
                ["section"] = m.Section,
            });
        }

        return arr;
    }

    private static JsonArray DumpWindows(IUiWindowRegistry windows)
    {
        var arr = new JsonArray();
        foreach (var w in windows.Items)
        {
            // 제네릭 Add<T>·JsonArray 원소 자동 변환은 웹 트리밍에서 깨진다(B07) — JsonValue.Create + 비제네릭 Add(JsonNode)
            var aliases = new JsonArray(w.Aliases.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray());
            arr.Add((JsonNode)new JsonObject
            {
                ["id"] = JsonValue.Create(w.Id),
                ["owner"] = JsonValue.Create(w.Owner),
                ["aliases"] = aliases,
                ["placement"] = JsonValue.Create(w.Placement.ToString()),
                ["allowDuplicate"] = JsonValue.Create(w.AllowDuplicate),
                ["hasContent"] = JsonValue.Create(w.CreateContent is not null),
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
