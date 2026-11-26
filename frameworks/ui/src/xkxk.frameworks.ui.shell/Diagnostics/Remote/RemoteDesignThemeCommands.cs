// 디자인 테마 축(REQ-104) 원격 명령 — style.set·get·effect.set·state, overview.open·select·state, depth.probe, motion.probe, gpu.simulateSoftware
#if PD_DIAGNOSTICS // 진단 빌드 전용(PdDiagnostics=true)
using System;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media;
using xkxk.frameworks.ui.shell.Theming;
using xkxk.frameworks.ui.shell.Framework;

namespace xkxk.frameworks.ui.shell.Diagnostics.Remote;

/// <summary>
/// 디자인 테마 원격 명령 군. 쓰기는 UiSettings(저장·적용 경로), 읽기는 DesignThemeResolver 실효값과 앱 리소스 계산값만 쓴다.
/// 전체 보기·depth 표본은 화면 층이 등록한 대리자(WindowOverviewHost·DepthProbeHost)만 부른다(Shell 은 Views 를 참조하지 않는다).
/// </summary>
internal static class RemoteDesignThemeCommands
{
    // depth.probe 대기 상한(진단 도구 값 — 전환이 이 안에 시작·끝나지 않으면 막힘 보고)
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(10);

    internal static void Register(RemoteCommandRouter r)
    {
        r.Register("style.set", "디자인 테마 선택 id=<ds-*> — 회사 잠금이면 무시(applied=false), 모르는 id 는 오류", a => Set(a.ReqStr("id")));
        r.Register("style.get", "style.state 와 같은 응답(디자인 테마 실효 상태)", _ => State());
        r.Register("style.state", "디자인 테마 상태 → {id,locked,motionLevel,fallbackReason,suggestedColor,glassOpacity,effective,overrides,resources,windows,active,overview}", _ => State());
        r.Register("style.effect.set", "효과 덮어쓰기 key= value= — value 비면 해제, 허용 목록 밖은 무시(applied=false)", a => EffectSet(a.ReqStr("key"), a.Str("value") ?? ""));
        r.Register("overview.open", "창 전체 보기 열기(창 0개면 opened=false)", _ => OverviewOpen());
        r.Register("overview.select", "전체 보기 카드 선택 index= 또는 x=,y=(카드 중심 좌표) — 선택 창을 활성화하고 닫는다", a => OverviewSelect(a));
        r.Register("overview.state", "전체 보기 상태 → {open,mode,cards[{windowId,x,y}]}", _ => OverviewState());
        r.Register("depth.probe", "창 RenderTransform 시점 표본 kind=open|page → {samples[{angleY}],restored}", a => Probe(a.ReqStr("kind")));
        r.Register("motion.probe", "창 움직임 효과 표본 effect=<ID|all>(기본 all) placement=<MdiOverlap|MdiMaximized|DockTab|Nav|Sdi>(기본 MdiOverlap) → {platform,placement,resolved{open,close,switch,switchExit},effects[{id,direction,samples[{o,s,x,y,a,clip}],restored}],allRestored}. hold=<0~1> 이면 effect 한 건을 그 지점에 멈춤(캡처용), hold=release 면 모두 되돌림",
            a => MotionProbe(a.Str("effect"), a.Str("placement"), a.Str("hold")));
        // K13 전환 스파이크 원인 확인용 — GC 횟수·누적 정지 시간(앞뒤로 불러 차이를 본다)
        r.Register("perf.gc", "GC 통계 → {gen0,gen1,gen2,pauseMs,heapMb}", _ => new JsonObject
        {
            ["gen0"] = GC.CollectionCount(0), ["gen1"] = GC.CollectionCount(1), ["gen2"] = GC.CollectionCount(2),
            ["pauseMs"] = Math.Round(GC.GetTotalPauseDuration().TotalMilliseconds, 1), ["heapMb"] = Math.Round(GC.GetTotalMemory(false) / 1048576.0, 1),
        });
        r.Register("gpu.simulateSoftware", "소프트웨어 렌더 모의(진단 전용) on=true|false", a =>
        {
            DesignThemeResolver.SimulateSoftware = a.Bool("on", true);
            return new JsonObject { ["on"] = DesignThemeResolver.SimulateSoftware, ["fallbackReason"] = DesignThemeResolver.FallbackReason };
        });
    }

    private static RemoteNotReadyException NoHost(string what) =>
        new("no-overview-host", $"창 전체 보기 호스트({what})가 아직 등록되지 않았습니다(화면 층 시작 전).");

    private static JsonNode Set(string id)
    {
        var theme = DesignThemes.Find(id) ?? throw new RemoteCommandException($"모르는 디자인 테마: {id}");
        if (DesignThemeResolver.Locked)
        {
            return new JsonObject { ["applied"] = false, ["locked"] = true, ["id"] = DesignThemeResolver.Current };
        }

        XkxkUi.Current.Settings.DesignThemeId = theme.Id;
        return new JsonObject { ["applied"] = true, ["locked"] = false, ["id"] = DesignThemeResolver.Current };
    }

    private static JsonNode EffectSet(string key, string value)
    {
        var spec = DesignThemes.Spec(key);
        if (spec?.Key == DesignThemes.EffectLevelKey && !DesignThemeResolver.Locked)
        {
            // 효과 단계는 해석 층이라 덮어쓰기가 아니라 자기 저장 자리에 쓴다(D-591). 빈 값 = theme
            var level = string.IsNullOrWhiteSpace(value) ? DesignThemes.LevelTheme : value;
            if (!DesignThemes.TryNormalize(spec.Key, level, out var id))
            {
                return new JsonObject { ["applied"] = false, ["reason"] = "invalidValue" };
            }

            XkxkUi.Current.Settings.EffectLevel = id;
            return new JsonObject { ["applied"] = true, ["cleared"] = false, ["value"] = id };
        }

        if (spec is not { IsEffect: true } || DesignThemeResolver.Locked)
        {
            return new JsonObject { ["applied"] = false, ["reason"] = spec is { IsEffect: true } ? "locked" : "unknownKey" };
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            XkxkUi.Current.Settings.SetEffectOverride(spec.Key, null);
            return new JsonObject { ["applied"] = true, ["cleared"] = true };
        }

        if (!DesignThemes.TryNormalize(spec.Key, value, out var normalized))
        {
            return new JsonObject { ["applied"] = false, ["reason"] = "invalidValue" }; // 허용 밖 값은 저장하지 않는다(기존 덮어쓰기 보존)
        }

        XkxkUi.Current.Settings.SetEffectOverride(spec.Key, normalized);
        return new JsonObject { ["applied"] = true, ["cleared"] = false, ["value"] = normalized };
    }

    private static JsonNode State()
    {
        var effective = new JsonObject();
        var resources = new JsonObject();
        foreach (var spec in DesignThemes.Effects)
        {
            effective[spec.Key] = DesignThemeResolver.Effective(spec.Key);
            if (spec.ResourceKey is { } rk)
            {
                resources[rk] = ResourceText(rk);
            }
        }

        foreach (var alias in DesignThemes.Aliases)
        {
            effective[alias.Key] = DesignThemeResolver.Effective(alias.Key); // 읽기 전용 옛 별칭(depth.open ← motion.open, 21 §3.1)
        }

        var overrides = new JsonObject();
        foreach (var (k, v) in XkxkUi.Current.Settings.EffectOverrides)
        {
            overrides[k] = v;
        }

        var shell = RemoteShellAccess.Shell;
        var (level, customized) = DesignThemeResolver.LevelDisplay(XkxkUi.Current.Settings.MoveShadowUserOverrideCount);
        return new JsonObject
        {
            ["id"] = DesignThemeResolver.Current,
            ["effectLevel"] = DesignThemeResolver.EffectLevel,
            ["levelDisplay"] = new JsonObject { ["level"] = level, ["overrides"] = customized }, // '<단계> + 사용자 지정 N개'(21 §5)
            ["moveShadow"] = Docking.MdiMoveLight.ShadowKey(Docking.MdiMoveLight.Shadow),
            ["locked"] = DesignThemeResolver.Locked,
            ["motionLevel"] = DesignThemeResolver.MotionLevel,
            ["fallbackReason"] = DesignThemeResolver.FallbackReason,
            ["suggestedColor"] = DesignThemeResolver.SuggestedColorThemeId ?? "",
            ["glassOpacity"] = DesignThemeResolver.Number("glass.minOpacity"),
            ["effective"] = effective,
            ["overrides"] = overrides,
            ["resources"] = resources,
            ["windows"] = new JsonArray(shell.Documents.Select(d => (JsonNode?)new JsonObject { ["id"] = d.Id }).ToArray()),
            ["active"] = shell.ActiveDocument?.Id,
            ["overview"] = OverviewJson(),
        };
    }

    /// <summary>앱 리소스 계산값 문자열(없으면 null). 브러시·두께는 사람이 읽는 형태로 바꾼다.</summary>
    private static string? ResourceText(string key)
    {
        var app = Application.Current;
        if (app is null || !app.TryGetResource(key, app.ActualThemeVariant, out var v) || v is null)
        {
            return null;
        }

        return v switch
        {
            ISolidColorBrush s => s.Color.ToString(),
            IBrush b => b.GetType().Name,
            _ => Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture),
        };
    }

    private static JsonObject OverviewJson()
    {
        var raw = WindowOverviewHost.State?.Invoke();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new JsonObject { ["open"] = false, ["mode"] = "", ["cards"] = new JsonArray() };
        }

        return JsonNode.Parse(raw) as JsonObject ?? new JsonObject { ["open"] = false, ["mode"] = "", ["cards"] = new JsonArray() };
    }

    private static JsonNode OverviewState() => OverviewJson();

    private static JsonNode OverviewOpen()
    {
        var opened = (WindowOverviewHost.Open ?? throw NoHost("Open"))();
        var o = OverviewJson();
        o["opened"] = opened;
        return o;
    }

    private static JsonNode OverviewSelect(JsonObject a)
    {
        var select = WindowOverviewHost.Select ?? throw NoHost("Select");
        int index;
        if (a.Int("index") is { } i)
        {
            index = i;
        }
        else if (a.Num("x") is { } x && a.Num("y") is { } y)
        {
            // 좌표 적중(3D 히트 테스트) — 화면 층 SelectAt 이 맞은 창 id 를 돌려주고 활성화·닫기까지 한다
            var at = WindowOverviewHost.SelectAt ?? throw NoHost("SelectAt");
            var hit = at(x, y);
            return new JsonObject { ["hit"] = hit, ["active"] = RemoteShellAccess.Shell.ActiveDocument?.Id };
        }
        else
        {
            throw new RemoteCommandException("index= 또는 x=,y= 가 필요합니다.");
        }

        var ok = select(index);
        return new JsonObject { ["selected"] = ok, ["index"] = index, ["active"] = RemoteShellAccess.Shell.ActiveDocument?.Id };
    }

    private static async Task<JsonNode?> Probe(string kind)
    {
        if (kind is not ("open" or "page"))
        {
            throw new RemoteCommandException("kind 는 open|page 입니다.");
        }

        var probe = DepthProbeHost.Probe ?? throw new RemoteNotReadyException("no-depth-probe", "depth 표본 대리자가 아직 등록되지 않았습니다(화면 층 시작 전).");
        var task = probe(kind);
        if (await Task.WhenAny(task, Task.Delay(ProbeTimeout)) != task)
        {
            throw new RemoteCommandException($"depth.probe 시간 초과({ProbeTimeout.TotalSeconds:0}초) — 전환이 시작되지 않았습니다.");
        }

        return JsonNode.Parse(await task);
    }

    private static async Task<JsonNode?> MotionProbe(string? effect, string? placement, string? hold)
    {
        var probe = DepthProbeHost.MotionProbe ?? throw new RemoteNotReadyException("no-motion-probe", "motion 표본 대리자가 아직 등록되지 않았습니다(화면 층 시작 전).");
        var task = probe(effect, placement, hold);
        if (await Task.WhenAny(task, Task.Delay(ProbeTimeout)) != task)
        {
            throw new RemoteCommandException($"motion.probe 시간 초과({ProbeTimeout.TotalSeconds:0}초) — UI 스레드가 응답하지 않습니다.");
        }

        return JsonNode.Parse(await task);
    }
}
#endif
