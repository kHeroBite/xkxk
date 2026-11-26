// 원격 명령(메뉴 열기 측정 B08) — menu.open 팝업 첫 프레임 시간, perf.modules 빌더 모듈 구성 시간
#if PD_DIAGNOSTICS // 진단 빌드 전용(PdDiagnostics=true) — 제품판에는 이 명령이 없다
using System;
using System.Diagnostics;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using xkxk.frameworks.ui.shell.Framework;

namespace xkxk.frameworks.ui.shell.Diagnostics.Remote;

/// <summary>
/// menu.open 은 지표 "메뉴 열기 명령 → 팝업 첫 프레임"(a3 §7, B08 D1)을 잰다.
/// T0 = 처리기 진입, T1 = IsSubMenuOpen=true 동기 구간 끝(MenuBuilder 열림 처리 포함), T2 = 팝업 TopLevel 첫 프레임 콜백.
/// 실입력 클릭 지연은 포함하지 않는다(한계). perf.modules 는 XkxkUiBuilder 마지막 Build 의 모듈별 구성 시간을 돌려준다.
/// </summary>
internal static class RemoteMenuOpenCommands
{
    private const string FirstFrameOp = "ui.menu.open.firstframe";

    public static void Register(RemoteCommandRouter r)
    {
        r.Register("menu.open", "메뉴 열기 측정(B08) id=<메뉴 AutomationId> [close=true] [timeoutMs=1000] → {id,opened,sync_ms,first_frame_ms,timed_out,items,popup_kind:popup_root|overlay}. 이미 열린 메뉴는 오류(닫고 다시 잰다). PerfProbe 조작 ui.menu.open.firstframe(sync=열기 동기 구간, frame=첫 프레임까지)", OpenAsync);
        r.Register("perf.modules", "빌더 모듈 구성 시간(B08) → {recorded,build_ms,total_configure_ms,count,modules:[{order,id,configure_ms,ok}]}. 마지막 Build 1회분, Build 전이면 recorded=false", _ => Modules());
    }

    private static async Task<JsonNode?> OpenAsync(JsonObject a)
    {
        var t0 = Stopwatch.GetTimestamp();
        var id = a.ReqStr("id");
        var close = a.Bool("close", true);
        var timeoutMs = Math.Max(1, a.Int("timeoutMs") ?? 1000);
        var menu = FindMenu(id) ?? throw new RemoteCommandException($"메뉴를 찾지 못했습니다: {id}");
        if (menu.IsSubMenuOpen)
        {
            throw new RemoteCommandException($"이미 열린 메뉴 — 닫은 뒤 다시 재세요: {id}"); // 측정 오염 방지, 사용자가 연 메뉴는 닫지 않는다
        }

        var openTops = OpenTopMenus(menu);
        if (openTops.Length > 0)
        {
            throw new RemoteCommandException($"같은 메뉴 표면의 최상위 메뉴가 열려 있습니다 — 닫은 뒤 다시 재세요: {string.Join(",", openTops)}"); // 열면 그 메뉴가 닫혀 측정이 오염된다, 닫지 않는다
        }

        if (!menu.IsEffectivelyVisible || menu.ItemCount == 0)
        {
            throw new RemoteCommandException($"메뉴 팝업이 없습니다: {id}(숨겨졌거나 하위 항목 0개)");
        }

        menu.IsSubMenuOpen = true;
        var t1 = Stopwatch.GetTimestamp();
        try
        {
            var popup = FindPopup(menu);
            if (popup?.Child is null || TopLevel.GetTopLevel(popup.Child) is null)
            {
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background); // 템플릿·팝업 호스트가 아직이면 1회 양보
                popup = FindPopup(menu);
            }

            var top = popup?.Child is { } child ? TopLevel.GetTopLevel(child) : null;
            if (popup is null || top is null)
            {
                throw new RemoteCommandException($"메뉴 팝업을 찾지 못했습니다: {id}");
            }

            var kind = top is PopupRoot ? "popup_root" : "overlay"; // 웹·오버레이 팝업은 메인 TopLevel 이 같은 합성 경로로 그린다
            var frame = AutomationBridge.WaitFramesAsync(top, 1);
            var timedOut = await Task.WhenAny(frame, Task.Delay(timeoutMs)) != frame;
            var t2 = Stopwatch.GetTimestamp();
            var syncMs = Stopwatch.GetElapsedTime(t0, t1).TotalMilliseconds;
            double? frameMs = timedOut ? null : Stopwatch.GetElapsedTime(t0, t2).TotalMilliseconds;
            if (frameMs is { } f)
            {
                PerfProbe.Current.RecordOperation(FirstFrameOp, syncMs, f); // 시간 초과 시행은 기록하지 않는다(INVALID 사유)
            }

            Debug.WriteLine($"[menu.open] {id} sync={syncMs:0.0}ms frame={frameMs:0.0}ms timedOut={timedOut} kind={kind}");
            return new JsonObject
            {
                ["id"] = id,
                ["opened"] = menu.IsSubMenuOpen,
                ["sync_ms"] = Math.Round(syncMs, 3),
                ["first_frame_ms"] = frameMs is { } v ? Math.Round(v, 3) : null,
                ["timed_out"] = timedOut,
                ["items"] = menu.Items.Count(i => i is MenuItem || menu.ContainerFromItem(i!) is MenuItem),
                ["popup_kind"] = kind,
            };
        }
        catch
        {
            menu.IsSubMenuOpen = false; // 실패 시 이 명령이 연 메뉴만 닫는다
            throw;
        }
        finally
        {
            if (close && menu.IsSubMenuOpen)
            {
                menu.IsSubMenuOpen = false;
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background); // 닫힘 반영 뒤 다음 시행
            }
        }
    }

    private static JsonObject Modules()
    {
        var (recorded, buildMs, modules) = UiModuleTimings.Snapshot();
        var arr = new JsonArray();
        var total = 0.0;
        foreach (var m in modules)
        {
            total += m.ConfigureMs;
            arr.Add((JsonNode)new JsonObject { ["order"] = m.Order, ["id"] = m.Id, ["configure_ms"] = Math.Round(m.ConfigureMs, 3), ["ok"] = m.Ok }); // JsonNode 로 받아 비제네릭 Add(JsonNode?) 를 고른다 — 제네릭 Add<T> 는 트리밍 경고 IL2026
        }

        return new JsonObject
        {
            ["recorded"] = recorded,
            ["build_ms"] = buildMs is { } b ? Math.Round(b, 3) : null,
            ["total_configure_ms"] = Math.Round(total, 3),
            ["count"] = modules.Count,
            ["modules"] = arr,
        };
    }

    /// <summary>대상이 속한 메뉴 표면(가장 가까운 MenuBase)의 최상위 항목 중 열린 것의 ID(없으면 머리글). 표면이 없으면 빈 배열.</summary>
    private static string[] OpenTopMenus(MenuItem menu)
    {
        var surface = menu.GetLogicalAncestors().OfType<MenuBase>().FirstOrDefault();
        if (surface is null)
        {
            return Array.Empty<string>();
        }

        return surface.Items
            .Select(i => i as MenuItem ?? surface.ContainerFromItem(i!) as MenuItem)
            .Where(m => m is { IsSubMenuOpen: true })
            .Select(m => VisualTreeInspector.AutomationIdOf(m!) is { Length: > 0 } aid ? aid : MenuHeaderText.Of(m!.Header) ?? "?")
            .ToArray();
    }

    private static Popup? FindPopup(MenuItem menu) => menu.GetVisualDescendants().OfType<Popup>().FirstOrDefault();

    /// <summary>RemoteMenuIconCommands.FindMenu 와 같은 규칙(AutomationId 로 찾고, 없으면 논리 후손 대조).</summary>
    private static MenuItem? FindMenu(string id) =>
        VisualTreeInspector.Find(id) as MenuItem
        ?? VisualTreeInspector.TopLevels().SelectMany(t => t.GetLogicalDescendants()).OfType<MenuItem>()
            .FirstOrDefault(m => VisualTreeInspector.AutomationIdOf(m) == id);
}
#endif
