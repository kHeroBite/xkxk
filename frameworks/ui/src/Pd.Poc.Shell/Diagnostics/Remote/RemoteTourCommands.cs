// 원격 명령(도움말 투어, 요건 6) — 시작·단계 이동·상태·종료와 입력 차단 확인. 차단 확인은 ui.click 거부 경로를 그대로 탄다
#if PD_DIAGNOSTICS // 진단 빌드 전용(PdDiagnostics=true)
using System.Diagnostics;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Pd.Poc.Shell.Tour;

namespace Pd.Poc.Shell.Diagnostics.Remote;

/// <summary>투어 원격 명령 군. RemoteCommandRouter 가 Register 를 부른다. 키 명령은 라우팅 이벤트만 올리며 OS 입력을 주입하지 않는다.</summary>
internal static class RemoteTourCommands
{
    public static void Register(RemoteCommandRouter r)
    {
        RemoteTourCatalogCommands.Register(r);
        r.Register("tour.start", "투어 시작 scope=main | doc=(그 문서 창의 chrome.btnHelp 기준) | window=번호(그 창 기준, F1 과 같음) → started·state", Start);
        r.Register("tour.next", "다음 단계 [window=]", a => Step(a, s => s.Next()));
        r.Register("tour.prev", "이전 단계 [window=]", a => Step(a, s => s.Previous()));
        r.Register("tour.skip", "마지막 단축키 레이어로 건너뛰기 [window=]", a => Step(a, s => s.Skip()));
        r.Register("tour.end", "투어 종료(X 버튼과 같음) [window=]", a => Step(a, s => s.End()));
        r.Register("tour.state", "진행 중 세션 목록 — window·scope·stepId·onShortcutLayer·overlayHasFocus·rootEnabled", _ => State());
        r.Register("tour.esc", "세션 창의 TopLevel 에 Esc 키 이벤트(OS 입력 아님) [window=] → handled·ended·docsBefore·docsAfter", Esc);
        r.Register("tour.key", "세션 창의 TopLevel 에 키 이벤트(OS 입력 아님) key=F4 [ctrl=true shift=true alt=true] [window=] → handled·docsBefore·docsAfter(창 단축키 차단 확인)", KeyProbe);
        r.Register("tour.probe", "입력 차단 확인: ui.click 을 그대로 호출 id= [doc|window] → blocked(비활성 거부)·ok·error", ProbeAsync);
        r.Register("tour.snapshot", "복원 대조용 스냅샷 [window=]: 내용 루트 IsEnabled·초점·활성 문서·문서 수(세션 없어도 동작)", Snapshot);
        r.Register("tour.dispatchProbe", "우선순위별 Dispatcher 지연 → loadedMs·backgroundMs(도움말 시작 지연 비교용)", DispatchProbeAsync);
        r.Register("dispatch.sites", "UiDeferred 호출부별 지연 기록 [clear=true] → [{site, latencyMs, atMs}]·priority", DispatchSites);
        r.Register("dispatch.force", "UiDeferred 우선순위 비교 background=true|false → forceBackground", a =>
        {
            Pd.Poc.Shell.Platform.UiDeferred.ForceBackground = a.Bool("background");
            return new JsonObject { ["forceBackground"] = Pd.Poc.Shell.Platform.UiDeferred.ForceBackground };
        });
    }

    private static JsonNode? DispatchSites(JsonObject a)
    {
        var rows = new JsonArray();
        foreach (var (site, latency, at) in Pd.Poc.Shell.Platform.UiDeferred.Records)
        {
            rows.Add(new JsonObject { ["site"] = site, ["latencyMs"] = System.Math.Round(latency, 1), ["atMs"] = System.Math.Round(at, 1) });
        }

        if (a.Bool("clear"))
        {
            Pd.Poc.Shell.Platform.UiDeferred.ClearRecords();
        }

        return new JsonObject { ["priority"] = Pd.Poc.Shell.Platform.UiDeferred.Priority.ToString(), ["records"] = rows };
    }

    /// <summary>같은 시점에 두 우선순위로 빈 작업을 올려 실행까지 걸린 시간을 잰다. 도움말 경로의 Post 우선순위 비교 근거다.</summary>
    private static async Task<JsonNode?> DispatchProbeAsync(JsonObject a)
    {
        var start = Stopwatch.GetTimestamp();
        var loaded = Dispatcher.UIThread.InvokeAsync(() => Stopwatch.GetElapsedTime(start).TotalMilliseconds, DispatcherPriority.Loaded);
        var background = Dispatcher.UIThread.InvokeAsync(() => Stopwatch.GetElapsedTime(start).TotalMilliseconds, DispatcherPriority.Background);
        return new JsonObject { ["loadedMs"] = System.Math.Round(await loaded, 1), ["backgroundMs"] = System.Math.Round(await background, 1) };
    }

    /// <summary>스냅샷·키 대상 창: window= 가 있으면 그 창, 없으면 세션이 하나인 창, 그 밖에는 메인 창.</summary>
    private static TopLevel AnyTop(JsonObject a)
    {
        if (a.Int("window") is not null)
        {
            return (TopLevel)RemoteUiTargets.Scope(a)!;
        }

        var active = VisualTreeInspector.TopLevels().Where(t => TourController.SessionOf(t) is not null).ToList();
        return active.Count == 1 ? active[0] : VisualTreeInspector.Main();
    }

    private static JsonNode? Snapshot(JsonObject a)
    {
        var top = AnyTop(a);
        var root = TourInputGate.ContentRootOf(top);
        var focused = top.FocusManager?.GetFocusedElement() as Control;
        return new JsonObject
        {
            ["windowVisible"] = top.IsVisible,
            ["rootEnabled"] = root?.IsEnabled,
            ["rootEffectivelyEnabled"] = root?.IsEffectivelyEnabled,
            ["focusedType"] = focused?.GetType().Name,
            ["focusedId"] = focused is null ? null : Avalonia.Automation.AutomationProperties.GetAutomationId(focused),
            ["activeDocument"] = App.MainViewModel?.ActiveDocument?.Id,
            ["docCount"] = App.MainViewModel?.OpenDocuments.Count ?? 0,
            ["inSession"] = TourController.SessionOf(top) is not null,
        };
    }

    private static JsonNode? Start(JsonObject a)
    {
        bool started;
        if (a.ContainsKey("doc"))
        {
            started = TourController.StartFrom(VisualTreeInspector.Require("chrome.btnHelp", RemoteUiTargets.Scope(a)));
        }
        else if (a.ContainsKey("window"))
        {
            started = TourController.StartFrom(RemoteUiTargets.Scope(a)!);
        }
        else
        {
            started = TourController.StartMain();
        }

        return new JsonObject { ["started"] = started, ["state"] = State() };
    }

    private static JsonNode? Step(JsonObject a, System.Action<TourController> action)
    {
        action(Session(a).Session);
        return State();
    }

    private static JsonArray State()
    {
        var tops = VisualTreeInspector.TopLevels();
        var list = new JsonArray();
        for (var i = 0; i < tops.Count; i++)
        {
            if (TourController.SessionOf(tops[i]) is { } s)
            {
                list.Add(new JsonObject
                {
                    ["window"] = i,
                    ["scope"] = s.Scope.ToString(),
                    ["stepId"] = s.CurrentStepId,
                    ["onShortcutLayer"] = s.OnShortcutLayer,
                    ["index"] = s.CurrentIndex,
                    ["count"] = s.Count,
                    ["skipped"] = new JsonArray(s.Skipped.Select(k => (JsonNode?)new JsonObject { ["id"] = k.StepId, ["reason"] = k.Reason }).ToArray()),
                    ["reducedMotion"] = s.IsMotionReduced,
                    ["overlayHasFocus"] = s.OverlayHasFocus,
                    ["rootEnabled"] = tops[i].GetVisualDescendants().OfType<VisualLayerManager>().FirstOrDefault()?.Child?.IsEnabled,
                });
            }
        }

        return list;
    }

    /// <summary>window= 의 세션. 생략하면 진행 중인 세션이 하나일 때만 그것을 쓴다(둘 이상이면 어느 창인지 모호하므로 오류).</summary>
    private static (TopLevel Top, TourController Session) Session(JsonObject a)
    {
        var active = VisualTreeInspector.TopLevels().Where(t => TourController.SessionOf(t) is not null).ToList();
        if (a.Int("window") is not null)
        {
            var top = (TopLevel)RemoteUiTargets.Scope(a)!;
            return (top, TourController.SessionOf(top) ?? throw new RemoteCommandException("이 창에 진행 중인 투어가 없습니다."));
        }

        return active.Count switch
        {
            0 => throw new RemoteCommandException("진행 중인 투어가 없습니다."),
            1 => (active[0], TourController.SessionOf(active[0])!),
            _ => throw new RemoteCommandException("진행 중인 투어가 둘 이상입니다. window= 로 창을 고르세요."),
        };
    }

    private static JsonNode? Esc(JsonObject a)
    {
        var (top, _) = Session(a);
        var docsBefore = App.MainViewModel?.OpenDocuments.Count ?? 0;
        var handled = Raise(top, Key.Escape, KeyModifiers.None);
        return new JsonObject
        {
            ["handled"] = handled,
            ["ended"] = TourController.SessionOf(top) is null,
            ["docsBefore"] = docsBefore,
            ["docsAfter"] = App.MainViewModel?.OpenDocuments.Count ?? 0,
        };
    }

    private static JsonNode? KeyProbe(JsonObject a)
    {
        var top = AnyTop(a); // 세션이 없을 때도 쓴다(F1 시작·처리기 순서 양성 대조)
        var key = a.EnumArg<Key>("key") ?? throw new RemoteCommandException("key= 가 필요합니다(Avalonia Key 이름).");
        var mods = KeyModifiers.None;
        if (a.Bool("ctrl")) { mods |= KeyModifiers.Control; }
        if (a.Bool("shift")) { mods |= KeyModifiers.Shift; }
        if (a.Bool("alt")) { mods |= KeyModifiers.Alt; }

        var docsBefore = App.MainViewModel?.OpenDocuments.Count ?? 0;
        var handled = Raise(top, key, mods);
        return new JsonObject
        {
            ["handled"] = handled,
            ["sessionAlive"] = TourController.SessionOf(top) is not null,
            ["docsBefore"] = docsBefore,
            ["docsAfter"] = App.MainViewModel?.OpenDocuments.Count ?? 0,
        };
    }

    private static bool Raise(TopLevel top, Key key, KeyModifiers mods)
    {
        var args = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = mods };
        // 실제 키보드처럼 초점 요소에서 올린다(Enter 가 초점 버튼을 누르는 경로까지 재현). TopLevel 터널 처리기는 어느 경로든 먼저 받는다.
        // 초점 조회가 다른 창의 컨트롤을 돌려주면(비전경 기동 실측) 지정한 창에서 올린다. 그래야 window= 가 뜻대로 그 창에 간다
        var focused = top.FocusManager?.GetFocusedElement() as Visual;
        var target = focused is Avalonia.Interactivity.Interactive inside && ReferenceEquals(TopLevel.GetTopLevel(focused), top) ? inside : top;
        target.RaiseEvent(args);
        target.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyUpEvent, Key = key, KeyModifiers = mods }); // Alt 떼기로 메뉴가 열리는 경로까지 확인
        return args.Handled;
    }

    /// <summary>판정이 실제 거부 경로와 어긋나지 않게 자체 검사 없이 ui.click 명령을 그대로 호출하고 그 응답을 돌려준다.</summary>
    private static async Task<JsonNode?> ProbeAsync(JsonObject a)
    {
        var json = await RemoteCommandRouter.Default.InvokeAsync("ui.click", (JsonObject)a.DeepClone());
        var response = JsonNode.Parse(json)?.AsObject() ?? new JsonObject();
        var ok = response["ok"]?.GetValue<bool>() == true;
        var error = response["error"]?.GetValue<string>();
        return new JsonObject
        {
            ["id"] = a.Str("id"),
            ["ok"] = ok,
            ["blocked"] = !ok && error?.Contains("비활성") == true,
            ["error"] = error,
        };
    }
}
#endif
