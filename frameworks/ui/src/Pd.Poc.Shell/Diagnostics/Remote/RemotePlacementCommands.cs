// 원격 명령(요건 4·5 창 배치 시험 — placement.force·state·markPlaced·selftest). 진단 빌드 전용
#if PD_DIAGNOSTICS // 진단 빌드 전용(PdDiagnostics=true)
using Pd.Poc.Shell.Platform;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using Dock.Model.Core;
using Pd.Poc.Shell.Busy;
using Pd.Poc.Shell.Docking;

namespace Pd.Poc.Shell.Diagnostics.Remote;

internal static partial class RemotePlacementCommands
{
    internal static void Register(RemoteCommandRouter r)
    {
        r.Register("placement.force", "비전경(--bg-pos)에서도 복구·재결합 정책을 켠다 on=true|false → 현재 값", a =>
        {
            PlacementRecovery.ForceInBackground = a.Bool("on");
            return JsonValue.Create(PlacementRecovery.ForceInBackground);
        });
        r.Register("placement.state", "창 배치 상태: suppressed·area(MDI 영역 화면 px)·판정 횟수·문서별 bounds·screen·sdiFrame·placed·locked", _ => StateJson());
        r.Register("placement.markPlaced", "문서를 '놓은 값 유지' 창으로 만든다 doc= x= y= w= h=(DIP) → placed", a =>
        {
            var d = RemoteShellAccess.Doc(a);
            var b = new DockRect(a.Num("x") ?? 0, a.Num("y") ?? 0, a.Num("w") ?? d.MdiBounds.Width, a.Num("h") ?? d.MdiBounds.Height);
            CenterDropTitleAlign.Mark(d, b);
            d.MdiBounds = b;
            return JsonValue.Create(CenterDropTitleAlign.IsDropPlaced(d));
        });
        r.Register("placement.resizeMain", "화면 밖 시험 창 크기 변경 w= h=(DIP, 위치 고정, --primary-override 기동 필요) → {status}", a =>
        {
            var done = PlacementDiagHooks.ResizeMain?.Invoke(a.Num("w") ?? 0, a.Num("h") ?? 0) == true;
            return new JsonObject { ["status"] = done ? "Done" : "NotApplicable" };
        });
        r.Register("placement.selftest", "요건 4·5 순수 함수 표본(경계값·복구 위치·가짜 시계 디바운스) → {cases:[{name,pass,detail}], passed, total}", _ => SelfTest());
    }

    private static JsonObject StateJson()
    {
        var root = RemoteDockAccess.Factory.Root;
        var docs = new JsonArray();
        foreach (var d in RemoteShellAccess.Shell.Documents)
        {
            var window = root is null ? null : LayoutSerializer.FindWindowOf(root, d);
            docs.Add(SdiFields(window, new JsonObject // SDI 문서는 grab·stripFolded·sdiState·sdiLive 를 Sdi partial 이 채운다(MDI 키 불변)
            {
                ["id"] = d.Id,
                ["state"] = d.MdiState.ToString(),
                ["floating"] = window is not null,
                ["bounds"] = BoundsJson(d.MdiBounds),
                ["screen"] = Pixel(SdiScreenPlacement.MdiScreenRect(d)),
                ["sdiFrame"] = Pixel(SdiScreenPlacement.SdiScreenRect(window)),
                ["placed"] = CenterDropTitleAlign.IsDropPlaced(d),
                ["locked"] = WindowCommands.ChromeOf(d).IsLocked,
                ["grab"] = GrabJson(d),
            }));
        }

        return new JsonObject
        {
            ["suppressed"] = PlacementRecovery.IsSuppressed(SystemDialogHost.BackgroundMode),
            ["force"] = PlacementRecovery.ForceInBackground,
            ["background"] = SystemDialogHost.BackgroundMode,
            ["area"] = AreaJson(),
            ["mdiEvaluations"] = MdiVisibilityGuard.EvaluationCount,
            ["mdiMoved"] = MdiVisibilityGuard.MovedCount,
            ["dockMagnet"] = Dock.Settings.DockSettings.EnableWindowMagnetism, // MDI 끌기가 잠깐 끈 값이 돌아왔는지 본다(SDI 자석이 이 값을 쓴다)
            ["minVisiblePercent"] = MinVisiblePercent(), // MDI 잔존 비율 현재값(정수 %, Ratio partial) — 시험 도구 기대값 계산용
            ["docs"] = docs,
            ["groups"] = GroupsJson(), // 탭 묶음(Groups partial) — 기존 키 불변
        };
    }

    /// <summary>문서를 담은 MdiLayoutPanel(d 지정 시 그 문서를 담은 것)을 고른다. 없으면 자식이 있는 첫 패널, 그것도 없으면 첫 패널이다(AreaJson·GrabJson 공통 기준).</summary>
    private static MdiLayoutPanel? DocPanel(PocDocument? d = null)
    {
        var ps = VisualTreeInspector.Main()?.GetVisualDescendants().OfType<MdiLayoutPanel>().ToList() ?? new();
        bool Has(MdiLayoutPanel p) => p.Children.Any(c => WindowCommands.FindDocument(c.DataContext) is { } f && (d is null || ReferenceEquals(f, d)));
        return ps.FirstOrDefault(Has) ?? ps.FirstOrDefault(p => p.Children.Count > 0) ?? ps.FirstOrDefault();
    }

    private static JsonObject? AreaJson()
    {
        var panel = DocPanel();
        if (panel is null) { return null; }

        var tl = panel.PointToScreen(default);
        var br = panel.PointToScreen(new Point(panel.Bounds.Width, panel.Bounds.Height));
        // x·y·w·h = 도구가 입력을 보낼 좌표계 기준 MDI 영역 원점·크기(데스크톱 화면 픽셀, 웹 페이지 좌표). winX·winY = 최상위 창(TopLevel) 기준 원점(DIP). dipW·dipH = 그 원점부터 창 안에 보이는 폭·높이.
        var top = Avalonia.Controls.TopLevel.GetTopLevel(panel);
        var o = top is null ? default : panel.TranslatePoint(default, top) ?? default;
        var dipW = top is null ? panel.Bounds.Width : Math.Min(panel.Bounds.Width, top.Bounds.Width - o.X);
        var dipH = top is null ? panel.Bounds.Height : Math.Min(panel.Bounds.Height, top.Bounds.Height - o.Y);
        var web = OperatingSystem.IsBrowser();
        return new JsonObject { ["x"] = web ? o.X : tl.X, ["y"] = web ? o.Y : tl.Y, ["w"] = web ? dipW : br.X - tl.X, ["h"] = web ? dipH : br.Y - tl.Y, ["dipW"] = dipW, ["dipH"] = dipH, ["winX"] = o.X, ["winY"] = o.Y };
    }

    /// <summary>문서 창의 끌 수 있는 구간(시험 도구가 다시 잡을 자리를 고르는 데 쓴다). 패널에 없는 문서면 null.</summary>
    private static JsonObject? GrabJson(PocDocument d)
    {
        var child = DocPanel(d)?.Children.FirstOrDefault(c => ReferenceEquals(WindowCommands.FindDocument(c.DataContext), d));
        if (child is null) { return null; }

        var z = MdiGrabZoneReader.Read(child);
        return new JsonObject { ["left"] = z.Left, ["rightInset"] = z.RightInset, ["titleBottom"] = z.TitleBottom, ["foldable"] = z.Foldable };
    }

    private static JsonObject BoundsJson(DockRect b) => new() { ["x"] = b.X, ["y"] = b.Y, ["w"] = b.Width, ["h"] = b.Height };

    private static JsonObject? Pixel(PixelRect? r) =>
        r is { } p ? new JsonObject { ["x"] = p.X, ["y"] = p.Y, ["w"] = p.Width, ["h"] = p.Height } : null;

    // ── 표본: 기대값은 사람이 정한 값이다. 계산식을 다시 쓰지 않는다 ──

    private static JsonObject SelfTest()
    {
        var cases = new JsonArray();
        void Case(string name, bool pass, string detail = "") => cases.Add(new JsonObject { ["name"] = name, ["pass"] = pass, ["detail"] = detail });

        RedockCases(Case);
        RecoveryCases(Case);
        MdiCases(Case);
        DebounceCases(Case);
        var passed = cases.Count(c => c?["pass"]?.GetValue<bool>() == true);
        return new JsonObject { ["cases"] = cases, ["passed"] = passed, ["total"] = cases.Count };
    }

    private static void RedockCases(Action<string, bool, string> add)
    {
        var area = new Rect(0, 0, 2000, 1000);
        void Check(string name, Rect? frame, Rect? zone, SdiRedockOutcome expected)
        {
            var got = SdiRedockPolicy.Decide(frame, zone);
            add(name, got == expected, got.ToString());
        }

        Check("redock_ratio_049_clamp", new Rect(1510, 100, 1000, 400), area, SdiRedockOutcome.Clamp);
        Check("redock_ratio_050_keep", new Rect(1500, 100, 1000, 400), area, SdiRedockOutcome.Keep);
        Check("redock_ratio_051_keep", new Rect(1490, 100, 1000, 400), area, SdiRedockOutcome.Keep);
        Check("redock_ratio_ok_title_above_clamp", new Rect(0, -450, 1000, 1000), area, SdiRedockOutcome.Clamp);
        Check("redock_ratio_ok_title_narrow_clamp", new Rect(1900, 100, 200, 300), area, SdiRedockOutcome.Clamp);
        Check("redock_frame_null_unavailable", null, area, SdiRedockOutcome.FrameUnavailable);
        Check("redock_area_null_unavailable", new Rect(0, 0, 100, 100), null, SdiRedockOutcome.FrameUnavailable);
        add("redock_max_drift_2px", SdiRedockPolicy.MaxDriftPx == 2, SdiRedockPolicy.MaxDriftPx.ToString());
    }

    private static void RecoveryCases(Action<string, bool, string> add)
    {
        var dflt = new Point(30, 30);
        Rect[] one = [new Rect(0, 0, 1920, 1032)];
        var mainOff = new RecoveryWindow(new Rect(2000, 100, 1400, 900));
        var mainOn = new RecoveryWindow(new Rect(100, 50, 1400, 900));
        var sdiOff = new RecoveryWindow(new Rect(2100, 200, 640, 480));
        var sdiOn = new RecoveryWindow(new Rect(300, 300, 640, 480));

        // 모니터 제거: 사라진 모니터의 메인·SDI 는 기본 위치, 남은 모니터의 SDI 는 그대로
        var removed = PlacementRecovery.Evaluate(one, mainOff, [sdiOff, sdiOn], dflt);
        add("monitor_removed_main_hidden", removed.MainHidden, "");
        add("monitor_removed_sdi_default", removed.Sdi[0] == dflt, removed.Sdi[0]?.ToString() ?? "null");
        add("monitor_removed_visible_stays", removed.Sdi[1] is null, removed.Sdi[1]?.ToString() ?? "null");

        // 해상도 축소·DPI 변경: 물리 영역이 줄어 밖으로 나간 창만 옮긴다
        var small = PlacementRecovery.Evaluate([new Rect(0, 0, 1280, 680)], mainOn, [new RecoveryWindow(new Rect(1500, 800, 640, 480))], dflt);
        add("resolution_shrink_moves_outside", !small.MainHidden && small.Sdi[0] == dflt, small.Sdi[0]?.ToString() ?? "null");
        var dpi = PlacementRecovery.Evaluate([new Rect(0, 0, 1536, 826)], null, [new RecoveryWindow(new Rect(1700, 100, 640, 480)), new RecoveryWindow(new Rect(200, 200, 640, 480))], dflt);
        add("dpi_change_moves_outside_only", dpi.Sdi[0] == dflt && dpi.Sdi[1] is null, "");

        // 그대로인 경우
        var stay = PlacementRecovery.Evaluate(one, mainOn, [sdiOn], dflt);
        add("all_visible_no_move", !stay.MainHidden && stay.Sdi[0] is null, "");
        var edge = PlacementRecovery.Evaluate(one, null, [new RecoveryWindow(new Rect(1800, 100, 640, 480)), new RecoveryWindow(new Rect(1801, 100, 640, 480))], dflt);
        add("title_120px_boundary", edge.Sdi[0] is null && edge.Sdi[1] == dflt, "");

        // 위치 고정 창도 안 보이면 옮긴다
        var locked = PlacementRecovery.Evaluate(one, null, [new RecoveryWindow(sdiOff.Bounds, IsLocked: true)], dflt);
        add("locked_hidden_moves", locked.Sdi[0] == dflt, "");

        // 대상 제외
        var maxed = PlacementRecovery.Evaluate(one, new RecoveryWindow(mainOff.Bounds, IsMaximized: true), [new RecoveryWindow(sdiOff.Bounds, IsMinimized: true)], dflt);
        add("maximized_minimized_excluded", !maxed.MainHidden && maxed.Sdi[0] is null, "");
        var empty = PlacementRecovery.Evaluate([], mainOff, [sdiOff], dflt);
        add("no_monitor_info_no_move", !empty.MainHidden && empty.Sdi[0] is null, "");

        // 겹치지 않게: 보이는 창이 기본 위치에 있으면 30px 비킨다
        var cascade = PlacementRecovery.Evaluate(one, null, [new RecoveryWindow(new Rect(30, 30, 640, 480)), sdiOff], dflt);
        add("cascade_avoids_visible", cascade.Sdi[0] is null && cascade.Sdi[1] == new Point(60, 60), cascade.Sdi[1]?.ToString() ?? "null");
    }

    private static void MdiCases(Action<string, bool, string> add)
    {
        var area = new Size(800, 600);
        RecoveryWindow[] drawn =
        [
            new(new Rect(1000, 100, 300, 200)),
            new(new Rect(10, 10, 300, 200)),
            new(new Rect(1100, 200, 300, 200), IsLocked: true),
            new(new Rect(-100000, -100000, 0, 0), IsMinimized: true),
            new(new Rect(1200, 0, 300, 200), IsMaximized: true),
        ];
        DockRect[] bounds = [new(1000, 100, 300, 200), new(10, 10, 300, 200), new(1100, 200, 300, 200), new(2000, 0, 300, 200), new(1200, 0, 300, 200)];
        var got = PlacementRecovery.EvaluateMdi(area, drawn, bounds, PocDockFactory.NextMdiBounds);
        add("mdi_hidden_goes_to_cascade", got[0] is { X: 0, Y: 0, Width: 300, Height: 200 }, got[0]?.ToString() ?? "null");
        add("mdi_visible_stays", got[1] is null, "");
        add("mdi_locked_hidden_moves", got[2] is { X: 30, Y: 30 }, got[2]?.ToString() ?? "null");
        add("mdi_minimized_maximized_stay", got[3] is null && got[4] is null, "");
        add("mdi_empty_area_no_move", PlacementRecovery.EvaluateMdi(default, drawn, bounds, PocDockFactory.NextMdiBounds).All(t => t is null), "");
        using (MdiDragLimit.UseFixedMinGrab(50)) { using (MdiDragLimit.UseFixedMinVisibleRatio(0)) { DragLimitCases(add, area); } RatioCases(add, area); } // MinGrab 50·비율 0(11-08 규칙) 고정 — 설정값(mdiMinGrabDip·mdiMinVisiblePercent)과 무관(결함 B). 비율 표본은 비율 고정 밖에서 스스로 고정한다
    }

    /// <summary>끌기 한계 표본: 실제 구간 MdiGrabZone(55, 305, 33, 196)(폭 640·기능 버튼 줄 196 접힘 가능)은 2026-11-08 사용자 결정 기준값이다. 폭 200 은 좁은 창이다.</summary>
    private static void DragLimitCases(Action<string, bool, string> add, Size area)
    {
        var z = new MdiGrabZone(55, 305, 33, 196);
        var far = new Rect(-5000, 0, 640, 420);
        var left = MdiDragLimit.ClampMove(far, area, z);
        add("drag_left_keeps_grab", left.X == -481, left.ToString()); // 잔존 159 = 캡션 109 + 잡을 곳 50
        add("drag_right_keeps_grab", MdiDragLimit.ClampMove(new Rect(5000, 0, 640, 420), area, z).X == 695, ""); // 잔존 105 = 왼쪽 55 + 잡을 곳 50
        add("drag_left_folds_strip", MdiDragLimit.ShouldFoldStrip(left, area, z, false), left.ToString());
        add("drag_left_never_buttons_only", (left.Right - (z.RightInset - z.Foldable)) - Math.Max(0, left.X + z.Left) >= MdiDragLimit.CurrentMinGrab, left.ToString());
        add("unfold_hysteresis", MdiDragLimit.ShouldFoldStrip(new Rect(-280, 0, 640, 420), area, z, true) && !MdiDragLimit.ShouldFoldStrip(new Rect(-280, 0, 640, 420), area, z, false) && !MdiDragLimit.ShouldFoldStrip(new Rect(-276, 0, 640, 420), area, z, true), "");
        add("drag_wide_no_ratio", MdiDragLimit.ClampMove(new Rect(5000, 0, 2000, 420), area, new MdiGrabZone(55, 0, 33)).X == 695, "");
        add("drag_top_blocked", MdiDragLimit.ClampMove(new Rect(0, -50, 640, 420), area, z).Y == 0, "");
        add("drag_bottom_title_stays", MdiDragLimit.ClampMove(new Rect(0, 5000, 640, 420), area, z).Y == 567, "");
        var narrow = MdiDragLimit.ClampMove(new Rect(-5000, 0, 200, 300), area, new MdiGrabZone(55, 115, 33));
        add("drag_narrow_grab_remains", narrow.X == -55, narrow.ToString());
        add("resize_top_blocked", MdiDragLimit.ClampResize(new Rect(0, -40, 640, 460), area, z, false, true, false) == new Rect(0, 0, 640, 420), "");
        add("resize_left_keeps_grab", MdiDragLimit.ClampResize(new Rect(790, 0, 200, 300), area, z, true, false, false).X < 800, "");

        // 복구: 끌어서 갈 수 있는 자리는 옮기지 않고, 잡을 곳이 없어졌을 때만 옮긴다
        RecoveryWindow[] drawn = [new(new Rect(-481, 0, 640, 420), Grab: z), new(new Rect(695, 0, 640, 420), Grab: z), new(new Rect(-520, 0, 640, 420), Grab: z), new(new Rect(760, 0, 640, 420), Grab: z)];
        add("folded_not_recovered", MdiDragLimit.IsGrabbable(new Rect(-481, 0, 640, 420), area, z), "");
        var got = PlacementRecovery.EvaluateMdi(area, drawn, drawn.Select(w => new DockRect(w.Bounds.X, w.Bounds.Y, w.Bounds.Width, w.Bounds.Height)).ToArray(), PocDockFactory.NextMdiBounds);
        add("mdi_pushed_left_limit_stays", got[0] is null, got[0]?.ToString() ?? "null");
        add("mdi_pushed_right_limit_stays", got[1] is null, got[1]?.ToString() ?? "null");
        add("mdi_caption_only_moves", got[2] is not null, got[2]?.ToString() ?? "null");
        add("mdi_grab_outside_moves", got[3] is not null, got[3]?.ToString() ?? "null");
    }

    private static void DebounceCases(Action<string, bool, string> add)
    {
        var clock = new FakeClock();
        var runs = 0;
        var debounce = new RecoveryDebounce(400, clock, () => runs++);

        // 간격 400ms 미만 3회 → 마지막 이벤트 뒤 1회(뒤따름 디바운스 정상 동작, 400ms 이상 벌어진 이벤트는 따로 평가한다)
        debounce.Trigger();
        clock.Advance(150);
        debounce.Trigger();
        clock.Advance(350);
        debounce.Trigger();
        add("debounce_not_before_quiet", runs == 0, runs.ToString());
        clock.Advance(399);
        add("debounce_waits_400ms", runs == 0, runs.ToString());
        clock.Advance(1);
        add("debounce_three_events_one_run", runs == 1, runs.ToString());
        clock.Advance(2000);
        add("debounce_no_extra_run", runs == 1, runs.ToString());
        add("debounce_no_timer_when_idle", clock.PendingCount == 0, clock.PendingCount.ToString());
    }

    private sealed class FakeClock : IBusyClock
    {
        private readonly List<Entry> _entries = new();

        public long NowMs { get; private set; }

        public int PendingCount => _entries.Count;

        public IDisposable Schedule(int delayMs, Action callback)
        {
            var e = new Entry(this, NowMs + delayMs, callback);
            _entries.Add(e);
            return e;
        }

        public void Advance(long ms)
        {
            var end = NowMs + ms;
            while (_entries.Where(e => e.Due <= end).OrderBy(e => e.Due).FirstOrDefault() is { } next)
            {
                NowMs = next.Due;
                _entries.Remove(next);
                next.Callback();
            }

            NowMs = end;
        }

        private sealed record Entry(FakeClock Owner, long Due, Action Callback) : IDisposable
        {
            public void Dispose() => Owner._entries.Remove(this);
        }
    }
}
#endif
