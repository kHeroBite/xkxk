// 원격 명령 chrome.moveLight — 끌기 중 가벼운 크롬 옵션 켬/끔·조회와 K1~K3 진단 카운터(RemoteWindowsCommands.cs 에서 300줄 규칙으로 나눔)
#if PD_DIAGNOSTICS // 진단 빌드 전용(PdDiagnostics=true)
using System.Text.Json.Nodes;
using xkxk.frameworks.ui.shell.Docking;

namespace xkxk.frameworks.ui.shell.Diagnostics.Remote;

internal static partial class RemoteWindowsCommands
{
    // 끌기 중 가벼운 크롬(MdiMoveLight) 켬/끔·조회 — 측정 때 켬·끔 비교용. staticCache(정지 아래 창 캐시)·bitmapCache(이동 창 캐시)·siblings(정지 창 가벼운 크롬)·inactiveDim(비활성 흐림 방식)·hitTestOff(끌기 중 내용 히트 테스트 끔)는 이번 실행만 바꾸고 저장하지 않는다.
    // renderDirtyRegions 는 시작 옵션이라 조회만 한다(이번 실행 적용값, 바꾸려면 설정 RenderDirtyRegions 저장 후 재시작). grabZone 은 K1·K2 카운터(01a 2026-11-14), grabZoneReset=true 면 응답 뒤 0 으로 한다
    private static void RegisterMoveLight(RemoteCommandRouter r)
    {
        r.Register("chrome.moveLight", "끌기 중 가벼운 크롬 on=true|false quietMs=50~1000 staticCache=true|false bitmapCache=true|false siblings=true|false hitTestOff=true|false coalescePointer=true|false releaseFlush=true|false staticLightEarly=true|false armNear=true|false armRadius=24~400 siblingsPerFrame=0~16 restorePerFrame=0~16 inactiveDim=opacity|brush(준 값만 설정, 생략=조회) [grabZoneReset=true] [coalesceReset=true]"
            + " → {enabled,quietMs,restorePerFrame,restore{frames,flushed,queued},bitmapCache,staticCache,siblings,hitTestOff,coalescePointer,inactiveDim,renderDirtyRegions,staticCacheAppliedTotal(정지 창 캐시 건 누계),staticCacheActive,hitTestOffAppliedTotal,hitTestOffActive,grabZone{hit,read*,missSkip,foldSkip,foldRun},coalesce{held,replayed,flushed,dropped}}", a =>
        {
            if (a.ContainsKey("coalescePointer")) { MdiMoveLight.CoalescePointer = a.Bool("coalescePointer"); }
            if (a.ContainsKey("releaseFlush")) { MdiMoveLight.ReleaseFlushFrame = a.Bool("releaseFlush"); } // K6(01a)
            if (a.ContainsKey("staticLightEarly")) { MdiMoveLight.StaticLightEarly = a.Bool("staticLightEarly"); } // K7(01a)
            if (a.ContainsKey("armNear")) { MdiMoveLight.TabJoinArmNear = a.Bool("armNear"); } // 옵션 A(01a)
            if (a.Num("armRadius") is { } ar) { MdiMoveLight.TabJoinArmRadius = ar; }
            if (a.Int("siblingsPerFrame") is { } spf) { MdiMoveLight.SiblingsPerFrame = spf; } // 옵션 B(01a)
            if (a.Int("restorePerFrame") is { } rpf) { MdiMoveLight.RestorePerFrame = rpf; } // P2(01a) — 이번 실행만, 저장하지 않는다
            if (a.ContainsKey("on")) { MdiMoveLight.Enabled = a.Bool("on"); }
            if (a.Int("quietMs") is { } q) { MdiMoveLight.QuietMs = q; }
            if (a.ContainsKey("staticCache")) { MdiMoveLight.StaticCache = a.Bool("staticCache"); }
            if (a.ContainsKey("bitmapCache")) { MdiMoveLight.BitmapCache = a.Bool("bitmapCache"); }
            if (a.ContainsKey("siblings")) { MdiMoveLight.Siblings = a.Bool("siblings"); }
            if (a.ContainsKey("hitTestOff")) { MdiMoveLight.HitTestOff = a.Bool("hitTestOff"); }
            if (a.ContainsKey("inactiveDim") && !ChromeInactiveDim.Set(a.Str("inactiveDim")))
            {
                throw new RemoteCommandException("inactiveDim 은 opacity|brush 중 하나입니다.");
            }

            var result = new JsonObject
            {
                ["armNear"] = MdiMoveLight.TabJoinArmNear, ["armRadius"] = MdiMoveLight.TabJoinArmRadius, ["siblingsPerFrame"] = MdiMoveLight.SiblingsPerFrame,
                ["spread"] = new JsonObject { ["frames"] = MdiMoveLight.SpreadFrames, ["canceled"] = MdiMoveLight.SpreadCanceled },
                ["restorePerFrame"] = MdiMoveLight.RestorePerFrame, // P2 적용값(측정 전 이 값을 먼저 확인)
                ["restore"] = new JsonObject { ["frames"] = MdiMoveLight.RestoreFrames, ["flushed"] = MdiMoveLight.RestoreFlushed, ["queued"] = MdiMoveLight.RestoreQueued },
                ["enabled"] = MdiMoveLight.Enabled, ["quietMs"] = MdiMoveLight.QuietMs, ["bitmapCache"] = MdiMoveLight.BitmapCache,
                ["staticCache"] = MdiMoveLight.StaticCache, ["siblings"] = MdiMoveLight.Siblings, ["hitTestOff"] = MdiMoveLight.HitTestOff,
                ["inactiveDim"] = ChromeInactiveDim.Mode, ["renderDirtyRegions"] = MdiMoveLight.RenderDirtyRegions,
                ["staticCacheAppliedTotal"] = MdiMoveLight.StaticCacheAppliedTotal, ["staticCacheActive"] = MdiMoveLight.StaticCacheActive,
                ["hitTestOffAppliedTotal"] = MdiMoveLight.HitTestOffAppliedTotal, ["hitTestOffActive"] = MdiMoveLight.HitTestOffActive,
                ["grabZone"] = MdiGrabZoneDiag.ToJson(), ["coalescePointer"] = MdiMoveLight.CoalescePointer,
                ["coalesce"] = new JsonObject
                {
                    ["held"] = MdiMoveLight.CoalesceHeld, ["replayed"] = MdiMoveLight.CoalesceReplayed,
                    ["flushed"] = MdiMoveLight.CoalesceFlushed, ["dropped"] = MdiMoveLight.CoalesceDropped,
                },
                ["releaseFlush"] = MdiMoveLight.ReleaseFlushFrame, ["staticLightEarly"] = MdiMoveLight.StaticLightEarly,
                ["k67"] = new JsonObject
                {
                    ["releaseFlushed"] = MdiMoveLight.ReleaseFlushed, ["earlyComputed"] = MdiMoveLight.EarlyComputed,
                    ["earlyUsed"] = MdiMoveLight.EarlyUsed, ["siblingsBounded"] = MdiMoveLight.SiblingsBounded,
                },
            };
            if (a.Bool("grabZoneReset")) { MdiGrabZoneDiag.Reset(); }
            if (a.Bool("coalesceReset")) { MdiMoveLight.ResetCoalesce(); }
            return result;
        });
        // K6 진단(01a): 놓은 뒤 무엇이 언제 바뀌는지(속성 변경·레이아웃·렌더 틱·Pd 놓기 후속 처리) 시각 기록. 시간 판정 회차에는 켜지 않는다(속성 구독 부하)
        r.Register("chrome.releaseTrace", "놓기 뒤 시간선 기록 on=true|false [windowMs=150] [ticks=3](준 값만 설정) → {armed,windowMs,releases,events[{t,kind,what}]}(마지막 놓기 1회분)", a =>
        {
            if (a.Int("windowMs") is { } w) { MdiReleaseTrace.WindowMs = w; }
            if (a.Int("ticks") is { } t) { MdiReleaseTrace.Ticks = t; }
            if (a.ContainsKey("on")) { MdiReleaseTrace.Arm(a.Bool("on")); }
            return MdiReleaseTrace.ToJson();
        });
    }
}

/// <summary>K6 진단 기록기(진단 빌드 전용). 놓기(TopLevel 터널) 시각을 0 으로 windowMs 동안 사건을 모은다.</summary>
internal static class MdiReleaseTrace
{
    public static int WindowMs { get; set; } = 150;
    public static int Ticks { get; set; } = 3;
    private const int Cap = 400;
    private static readonly System.Diagnostics.Stopwatch s_clock = System.Diagnostics.Stopwatch.StartNew();
    private static readonly System.Collections.Generic.List<(double T, string Kind, string What)> s_events = new();
    private static readonly System.Collections.Generic.HashSet<Avalonia.AvaloniaProperty> s_subscribed = new();
    private static double s_t0 = -1;
    private static bool s_armed, s_installed;
    private static int s_releases;

    private static bool Active => s_armed && s_t0 >= 0 && s_clock.Elapsed.TotalMilliseconds - s_t0 <= WindowMs;

    /// <summary>Pd 놓기 후속 처리 표시(MdiSnapDrag·MdiTabJoinDrag 등에서 부른다). 기록 중이 아니면 무시.</summary>
    public static void Mark(string what) { if (Active) { Add("pd", what); } }

    private static void Add(string kind, string what)
    {
        if (s_events.Count < Cap) { s_events.Add((System.Math.Round(s_clock.Elapsed.TotalMilliseconds - s_t0, 2), kind, what)); }
    }

    public static void Arm(bool on)
    {
        s_armed = on;
        if (!on || s_installed) { return; }
        s_installed = true;
        Avalonia.Input.InputElement.PointerReleasedEvent.AddClassHandler<Avalonia.Controls.TopLevel>(OnReleased, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
        foreach (var top in TopLevels()) { Subscribe(top); }
    }

    private static System.Collections.Generic.IEnumerable<Avalonia.Controls.TopLevel> TopLevels()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.ISingleViewApplicationLifetime { MainView: { } v } && Avalonia.Controls.TopLevel.GetTopLevel(v) is { } t1) { yield return t1; }
        if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime { MainWindow: { } w }) { yield return w; }
    }

    // 지금 시각 트리에 있는 형식들의 등록 속성(일반·부착) Changed 를 한 번씩 구독한다
    private static void Subscribe(Avalonia.Controls.TopLevel top)
    {
        top.LayoutUpdated += (_, _) => { if (Active) { Add("layout", "LayoutUpdated"); } };
        var types = new System.Collections.Generic.HashSet<System.Type>();
        foreach (var v in Avalonia.VisualTree.VisualExtensions.GetSelfAndVisualDescendants(top)) { types.Add(v.GetType()); }
        var reg = Avalonia.AvaloniaPropertyRegistry.Instance;
        foreach (var ty in types)
        {
            foreach (var p in reg.GetRegistered(ty)) { Watch(p); }
            foreach (var p in reg.GetRegisteredAttached(ty)) { Watch(p); }
        }
    }

    private static void Watch(Avalonia.AvaloniaProperty p)
    {
        if (!s_subscribed.Add(p)) { return; }
        p.Changed.Subscribe(new Obs(p.Name));
    }

    private sealed class Obs(string name) : System.IObserver<Avalonia.AvaloniaPropertyChangedEventArgs>
    {
        public void OnNext(Avalonia.AvaloniaPropertyChangedEventArgs e)
        {
            if (!Active) { return; }
            var s = e.Sender;
            Add("prop", $"{s.GetType().Name}{((s as Avalonia.StyledElement)?.Name is { Length: > 0 } n ? "#" + n : "")}.{name}={Short(e.NewValue)}");
        }

        public void OnCompleted() { }
        public void OnError(System.Exception error) { }
    }

    private static string Short(object? v) { var s = v?.ToString() ?? "null"; return s.Length > 40 ? s[..40] : s; }

    private static void OnReleased(Avalonia.Controls.TopLevel top, Avalonia.Input.PointerReleasedEventArgs e)
    {
        if (!s_armed) { return; }
        s_events.Clear();
        s_t0 = s_clock.Elapsed.TotalMilliseconds;
        s_releases++;
        Add("release", $"source={(e.Source as Avalonia.StyledElement)?.GetType().Name}");
        Avalonia.Threading.Dispatcher.UIThread.Post(() => Mark("post.normal(놓기 처리 직후)"), Avalonia.Threading.DispatcherPriority.Normal);
        Avalonia.Threading.Dispatcher.UIThread.Post(() => Mark("post.background"), Avalonia.Threading.DispatcherPriority.Background);
        var left = Ticks;
        void Tick(System.TimeSpan ts)
        {
            Add("tick", $"rAF {ts.TotalMilliseconds:0.0}");
            if (--left > 0) { top.RequestAnimationFrame(Tick); }
        }
        if (left > 0) { top.RequestAnimationFrame(Tick); }
    }

    public static JsonObject ToJson()
    {
        var arr = new JsonArray();
        foreach (var (t, k, w) in s_events) { arr.Add(new JsonObject { ["t"] = t, ["kind"] = k, ["what"] = w }); }
        return new JsonObject { ["armed"] = s_armed, ["windowMs"] = WindowMs, ["ticks"] = Ticks, ["releases"] = s_releases, ["subscribed"] = s_subscribed.Count, ["events"] = arr };
    }
}
#endif
