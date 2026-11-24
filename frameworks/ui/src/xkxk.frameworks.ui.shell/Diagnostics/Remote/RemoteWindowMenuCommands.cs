// 원격 명령(Mars 윈도우 메뉴 13항목 — 자동 저장·Esc 닫기·캡처 방지·SDI 일괄·메인 창·자석·레이아웃) — 실행 전후 창 bounds·상태를 함께 돌려준다
#if PD_DIAGNOSTICS // 진단 빌드 전용(PdDiagnostics=true). CaptureProtectProbe 의 제품 빌드 대체는 RemoteProductStubs.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Avalonia.Controls;
using xkxk.frameworks.ui.shell.Docking;

namespace xkxk.frameworks.ui.shell.Diagnostics.Remote;

internal static class RemoteWindowMenuCommands
{
    // 플랫폼 훅 CaptureProtectProbe·CaptureAffinityProbe 는 RemoteCommands.cs 로, Vm 은 RemoteShellAccess 로 옮겼다(W2 S 단계).
    // 공용 멤버 전달자(W2 F) — 본문은 옛 이름 그대로 쓴다. 훅은 RemoteCommands. 로 부른다.
    private static IShellCommands Shell => RemoteShellAccess.Shell;

    private static ViewModels.MainViewModel Vm => RemoteShellAccess.Vm;

    private static PocDocument Doc(JsonObject a, string key = "doc") => RemoteShellAccess.Doc(a, key);

    private static bool IsFloating(PocDocument d, int windowIndex) => RemoteShellAccess.IsFloating(d, windowIndex);

    private static PocDockFactory Factory => RemoteDockAccess.Factory;

    private static JsonObject? ManagedBounds(PocDocument d) => RemoteSdiZ.ManagedBounds(d);

    /// <summary>판정용 스냅샷: 메인 창 bounds·상태 + SDI(플로팅) 창별 bounds·상태 + 문서 수.</summary>
    internal static JsonObject WindowSnapshot()
    {
        var tops = VisualTreeInspector.TopLevels();
        var main = tops.FirstOrDefault();
        var o = new JsonObject { ["docCount"] = Shell.Documents.Count, ["main"] = TopJson(main) };
        var sdi = new JsonArray();
        foreach (var d in Shell.Documents.Where(d => IsFloating(d, -1)))
        {
            var top = VisualTreeInspector.TopLevelOf(d);
            var j = ReferenceEquals(top, main) ? new JsonObject { ["managed"] = true } : TopJson(top);
            if (j["managed"] is not null && ManagedBounds(d) is JsonObject mb) // 관리형 창 사각형 — OS 창 항목과 같은 키(x·y·w·h·pxW·pxH) + inContainer·boundsSource(⑦ 웹 판정)
            {
                foreach (var (k, v) in mb.ToList()) { j[k] = v?.DeepClone(); }
            }
            j["doc"] = d.Id;
            // 관리형 SDI 상태는 Dock 창 상태(IDockWindow.WindowState)다 — 셸 SetState 가 SDI 에는 MdiState 가 아니라 이것을 바꾼다
            // (odev-2 01:3x 분석, 판정용 API WindowCommands.SdiState). MdiState 는 MDI 창 전용이라 늘 Normal 로 읽혀 Android #8 이 오판됐다.
            if (j["state"] is null && Vm.Commands.SdiState(d) is { } ws) { j["state"] = ws.ToString(); j["stateSource"] = "SdiState"; }
            j["state"] ??= d.MdiState.ToString();
            sdi.Add(j);
        }

        o["sdi"] = sdi;
        return o;
    }

    private static JsonObject TopJson(TopLevel? t)
    {
        var j = new JsonObject();
        if (t is Window w)
        {
            // 위치·외곽 크기는 물리 픽셀(모니터 영역과 같은 단위). ⑤⑥ 싱글/멀티 모니터 맞춤 판정용으로 모니터 작업 영역도 함께 낸다.
            var s = w.RenderScaling;
            j["x"] = w.Position.X; j["y"] = w.Position.Y; j["state"] = w.WindowState.ToString(); j["topmost"] = w.Topmost;
            j["pxW"] = Math.Round((w.FrameSize?.Width ?? w.ClientSize.Width) * s); j["pxH"] = Math.Round((w.FrameSize?.Height ?? w.ClientSize.Height) * s);
            j["clientPxW"] = Math.Round(w.ClientSize.Width * s); j["clientPxH"] = Math.Round(w.ClientSize.Height * s); // ⑤⑥ Mars 기준 = 클라이언트 크기
            j["screens"] = new JsonArray(w.Screens.All.Select(sc => (JsonNode?)new JsonObject
            {
                ["x"] = sc.Bounds.X, ["y"] = sc.Bounds.Y, ["w"] = sc.Bounds.Width, ["h"] = sc.Bounds.Height, // Bounds(작업 표시줄 포함, Mars ⑤⑥ 기준)
                ["wx"] = sc.WorkingArea.X, ["wy"] = sc.WorkingArea.Y, ["ww"] = sc.WorkingArea.Width, ["wh"] = sc.WorkingArea.Height,
                ["primary"] = sc.IsPrimary,
            }).ToArray());
        }

        if (t is not null) { j["w"] = Math.Round(t.ClientSize.Width, 1); j["h"] = Math.Round(t.ClientSize.Height, 1); }
        return j;
    }

    /// <summary>bool 설정 get/set 쌍 등록. set 은 on=true|false, 응답은 실제 값.</summary>
    private static void RegisterToggle(RemoteCommandRouter r, string name, string help, Func<bool> get, Action<bool> set)
    {
        r.Register(name + ".get", help + " 조회", _ => JsonValue.Create(get()));
        r.Register(name + ".set", help + " on=true|false → 실제 값", a =>
        {
            if (!a.ContainsKey("on")) { throw new RemoteCommandException("on=true|false 가 필요합니다."); }
            set(a.Bool("on"));
            return JsonValue.Create(get());
        });
    }

    internal static void Register(RemoteCommandRouter r)
    {
        r.Register("window.snapshot", "판정용 스냅샷: 메인 창 bounds·상태·모니터 수 + SDI 창별 bounds·상태", _ => WindowSnapshot());
        RegisterToggle(r, "window.autosave", "종료 시 레이아웃 자동 저장·기동 시 복원(Mars 윈도우 자동저장)",
            () => Vm.WindowMenu.AutoSaveOnExit, v => Vm.WindowMenu.AutoSaveOnExit = v);
        RegisterToggle(r, "window.escClose", "Esc 로 활성 창 닫기(Mars Esc 닫기)", () => Vm.WindowMenu.EscCloseEnabled, v => Vm.WindowMenu.EscCloseEnabled = v);
        RegisterToggle(r, "window.dblClose", "작업표시줄 탭 더블클릭 창 닫기", () => Vm.DoubleClickCloseEnabled, v => Vm.DoubleClickCloseEnabled = v);
        r.Register("window.captureProtect.get", "화면 캡처 방지: 설정값과 실제 적용값(probe — 데스크톱 표시 친화도·Android FLAG_SECURE)", _ => CaptureJson());
        r.Register("window.captureProtect.set", "화면 캡처 방지 on=true|false → 설정값·실제 적용값", a =>
        {
            if (!a.ContainsKey("on")) { throw new RemoteCommandException("on=true|false 가 필요합니다."); }
            Vm.WindowMenu.CaptureProtection = a.Bool("on");
            return CaptureJson();
        });
        // ④~⑩ 은 메뉴와 같은 경로(MainViewModel.WindowMenuCommand — WindowHostCommand 이름, odev-2)를 탄다. 플랫폼이 못 하면 status=NotApplicable
        RegisterMenu(r, "window.main.reset", "ResetMainWindow", "④ 메인 창 초기화(기본 크기·주 모니터 중앙)");
        r.Register("window.main.maximize", "⑤⑥ 메인 창 모니터 맞춤 mode=single(주 모니터)|multi(전체 모니터 합집합) — Normal 상태로 크기만 맞춤 → before/after", a =>
            RunMenu((a.Str("mode") ?? "single").ToLowerInvariant() == "multi" ? "FitMultiMonitor" : "FitSingleMonitor"));
        // ⑦ 판정 보강용(lead 04:5x — '초기화 전 SDI 크기를 기본과 다르게'): SDI 창 1개의 위치·크기를 셸 SdiResetPositions 와 같은 Dock 경로로 바꾼다
        r.Register("sdi.setBounds", "SDI 창 위치·크기 지정(진단) doc= [x= y=] [w= h=] — Dock IDockWindow X/Y/Width/Height + Host.SetPosition/SetSize(OS 창·관리형 창 공통) → {status, before, after, settled, eventSeen…}", async a =>
        {
            var d = Doc(a);
            if (LayoutSerializer.FindWindowOf(Factory.Root ?? throw new RemoteNotReadyException("no-shell", "도킹 루트 없음"), d) is not { } win)
            {
                return new JsonObject { ["status"] = "NotApplicable", ["reason"] = "SDI 창이 아님(MDI·탭 문서)", ["doc"] = d.Id };
            }

            var before = WindowSnapshot();
            var seen = new JsonObject { ["resized"] = false, ["positionChanged"] = false };
            var windows = VisualTreeInspector.TopLevels().OfType<WindowBase>().ToList();
            EventHandler<WindowResizedEventArgs> onResized = (_, _) => seen["resized"] = true;
            EventHandler<Avalonia.Controls.PixelPointEventArgs> onMoved = (_, _) => seen["positionChanged"] = true;
            foreach (var w in windows) { w.Resized += onResized; w.PositionChanged += onMoved; }
            try
            {
                var (x, y, ww, hh) = (a.Num("x") ?? win.X, a.Num("y") ?? win.Y, a.Num("w") ?? win.Width, a.Num("h") ?? win.Height);
                (win.X, win.Y, win.Width, win.Height) = (x, y, ww, hh);
                win.Host?.SetPosition(x, y);
                win.Host?.SetSize(ww, hh);
                var sized = a.ContainsKey("w") || a.ContainsKey("h");
                var (after, ms, settled, unchanged) = await SettledSnapshotAsync(before, sized
                    ? snap => seen["resized"]?.GetValue<bool>() == true || SizesOf(snap) != SizesOf(before)
                    : snap => seen["positionChanged"]?.GetValue<bool>() == true || PositionsOf(snap) != PositionsOf(before));
                return new JsonObject
                {
                    ["status"] = "Done", ["doc"] = d.Id, ["model"] = new JsonObject { ["x"] = x, ["y"] = y, ["w"] = ww, ["h"] = hh },
                    ["before"] = before, ["after"] = after, ["settleMs"] = ms, ["settled"] = settled, ["unchanged"] = unchanged, ["eventSeen"] = seen.DeepClone(),
                };
            }
            finally
            {
                foreach (var w in windows) { w.Resized -= onResized; w.PositionChanged -= onMoved; }
            }
        });
        r.Register("monitors.setVirtual", "요건 5 시험: 실행 중 가상 모니터 수 변경 count=N(모니터 폭 유지, --primary-override 기동 필요) [force=true: 비전경에서도 복구 켬] → {status, reason}", a =>
        {
            Docking.PlacementRecovery.ForceInBackground = a.Bool("force") || Docking.PlacementRecovery.ForceInBackground;
            if (Docking.PlacementDiagHooks.SetVirtualMonitors is not { } set) { return new JsonObject { ["status"] = "NotApplicable", ["reason"] = "모니터 호스트 없음" }; }
            if (set((int)(a.Num("count") ?? 0)) is { } error) { throw new RemoteCommandException(error); }
            Docking.PlacementDiagHooks.MonitorsApplied?.Invoke();
            return new JsonObject { ["status"] = "Done", ["force"] = Docking.PlacementRecovery.ForceInBackground };
        });
        RegisterMenu(r, "sdi.resetPositions", "ResetSdiPositions", "⑦ SDI 창 기본 위치(겹침 회피·화면 안·크기 유지)");
        RegisterMenu(r, "sdi.minimizeAll", "MinimizeAllSdi", "⑧ SDI 창 모두 최소화");
        RegisterMenu(r, "sdi.restoreAll", "RestoreAllSdi", "⑨ SDI 창 모두 이전 크기로(Normal)");
        RegisterMenu(r, "sdi.closeAll", "CloseAllSdi", "⑩ SDI 창 모두 닫기(확인 없음, MDI 유지)");
        r.Register("snap.get", "③ 자석: on·range(px, 0~48·2단위)·effective", _ => SnapJson());
        r.Register("snap.set", "③ 자석 설정 [on=true|false] [range=px(셸이 0~48·2단위로 맞춤)] [reset=true(기본 24)] → 실제 적용값", a =>
        {
            var s = Theming.UiSettings.Current;
            if (a.Bool("reset")) { s.MagnetRange = Theming.UiSettings.DefaultMagnetRange; }
            if (a.ContainsKey("on")) { s.MagnetEnabled = a.Bool("on"); }
            if (a.Num("range") is { } range) { s.MagnetRange = Theming.UiSettings.NormalizeMagnetRange(range); }
            return SnapJson();
        });
    }

    private static JsonObject SnapJson()
    {
        var s = Theming.UiSettings.Current;
        return new JsonObject { ["on"] = s.MagnetEnabled, ["range"] = s.MagnetRange, ["effective"] = s.EffectiveMagnetRange };
    }

    private static void RegisterMenu(RemoteCommandRouter r, string name, string command, string help) =>
        r.Register(name, help + " → before/after(status=Done|NotApplicable)", _ => RunMenu(command));

    /// <summary>윈도우 메뉴 명령 1개를 메뉴와 같은 경로로 실행하고 전후 스냅샷을 돌려준다.</summary>
    private static async Task<JsonNode?> RunMenu(string command)
    {
        var cmd = Vm.WindowMenu.WindowMenuCommand;
        if (!cmd.CanExecute(command))
        {
            // 상태 이름 NotApplicable(lead 지정, odev-2 01:3x 전달) — 옛 이름 Unavailable 은 retest_window_menu 가 함께 받는다
            return new JsonObject { ["status"] = "NotApplicable", ["command"] = command, ["platform"] = AutomationBridge.Platform };
        }

        var before = WindowSnapshot();
        // ② 실행 뒤 창 이벤트가 한 번이라도 왔는가(진단 근거, lead 04:1x): 모든 OS 창(메인·SDI)의 Resized·PositionChanged
        var seen = new JsonObject { ["resized"] = false, ["positionChanged"] = false };
        var windows = VisualTreeInspector.TopLevels().OfType<WindowBase>().ToList();
        EventHandler<WindowResizedEventArgs> onResized = (_, _) => seen["resized"] = true;
        EventHandler<Avalonia.Controls.PixelPointEventArgs> onMoved = (_, _) => seen["positionChanged"] = true;
        foreach (var w in windows) { w.Resized += onResized; w.PositionChanged += onMoved; }
        try
        {
            cmd.Execute(command);
            // OS 창 크기 변경은 비동기로 반영된다 — 실행 직후 읽으면 위치만 갱신되고 크기는 이전 값이었다(i0339 Q6 #4: after 2576x1448, 직후 snapshot 1416x908).
            // 크기를 바꾸는 명령(lead 04:2x)은 '크기 반영 증거'(Resized 이벤트 또는 크기가 before 와 다름)가 있어야 안정값을 채택한다
            // 위치를 바꾸는 명령(⑦ SDI 위치 초기화 — 크기 유지)은 '위치 반영 증거'(PositionChanged 또는 위치가 before 와 다름)로 본다(lead 04:3x)
            var sizing = SizingCommands.Contains(command);
            var positioning = PositioningCommands.Contains(command);
            Func<JsonObject, bool>? evidence =
                sizing ? snap => seen["resized"]?.GetValue<bool>() == true || SizesOf(snap) != SizesOf(before)
                : positioning ? snap => seen["positionChanged"]?.GetValue<bool>() == true || PositionsOf(snap) != PositionsOf(before)
                : null;
            var (after, settleMs, settled, unchanged) = await SettledSnapshotAsync(before, evidence);
            return new JsonObject
            {
                ["before"] = before, ["after"] = after, ["value"] = command, ["status"] = "Done",
                ["settleMs"] = settleMs, ["settled"] = settled, ["unchanged"] = unchanged, ["eventSeen"] = seen.DeepClone(),
                ["sizing"] = sizing, ["sizeUnchanged"] = SizesOf(after) == SizesOf(before), // 원래 같은 크기여야 하는 경우와 '반영 안 됨' 구분용
                ["positioning"] = positioning, ["positionUnchanged"] = PositionsOf(after) == PositionsOf(before),
            };
        }
        finally
        {
            foreach (var w in windows) { w.Resized -= onResized; w.PositionChanged -= onMoved; }
        }
    }

    /// <summary>
    /// 레이아웃·OS 창 반영 뒤 스냅샷: 2프레임 대기 → 스냅샷, 직전과 같으면 채택(lead 04:0x).
    /// ① 그 안정값이 실행 전(before)과 같으면 곧바로 채택하지 않고 1초까지 계속 읽는다 — OS 반영이 2프레임보다 늦으면 낡은 값이 연속 2회 같게 읽혀
    ///    '안정' 으로 채택될 수 있다(lead 04:1x, 이번 #4). 1초 뒤에도 그대로면 settled=true·unchanged=true.
    /// 1초 안에 연속 2회 같아지지 않으면 마지막 값과 settled=false.
    /// </summary>
    private static async Task<(JsonObject Snap, long Ms, bool Settled, bool Unchanged)> SettledSnapshotAsync(JsonObject before, Func<JsonObject, bool>? sizeEvidence)
    {
        var beforeJson = before.ToJsonString();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        string? prev = null;
        while (true)
        {
            await AutomationBridge.WaitFramesAsync(VisualTreeInspector.Main(), 2);
            var snap = WindowSnapshot();
            var cur = snap.ToJsonString();
            var stable = cur == prev;
            var timeUp = sw.ElapsedMilliseconds >= 1000;
            if (sizeEvidence is not null)
            {
                // 크기 변경 명령: 안정 + 크기 반영 증거가 있어야 채택. 증거 없이 만료되면 settled=false(sizeUnchanged 로 '원래 같은 크기' 구분)
                if (stable && sizeEvidence(snap)) { return (snap, sw.ElapsedMilliseconds, true, cur == beforeJson); }
                if (timeUp) { return (snap, sw.ElapsedMilliseconds, false, cur == beforeJson); }
            }
            else
            {
                if (stable && (cur != beforeJson || timeUp)) { return (snap, sw.ElapsedMilliseconds, true, cur == beforeJson); }
                if (timeUp) { return (snap, sw.ElapsedMilliseconds, false, cur == beforeJson); }
            }

            prev = cur;
        }
    }

    /// <summary>크기를 바꾸는 윈도우 메뉴 명령(lead 04:2x): ④ 메인 초기화·⑤⑥ 모니터 맞춤.</summary>
    private static readonly HashSet<string> SizingCommands = new(StringComparer.Ordinal) { "ResetMainWindow", "FitSingleMonitor", "FitMultiMonitor" };

    /// <summary>위치만 바꾸는 윈도우 메뉴 명령(lead 04:3x): ⑦ SDI 위치 초기화(크기 유지 — 크기 증거를 요구하면 매번 1초 만료).</summary>
    private static readonly HashSet<string> PositioningCommands = new(StringComparer.Ordinal) { "ResetSdiPositions" };

    /// <summary>스냅샷에서 크기 필드만(메인·SDI 각 창의 pxW·pxH·clientPxW·clientPxH·w·h) 뽑은 비교용 문자열.</summary>
    private static string SizesOf(JsonObject? snap) => Fields(snap, "pxW", "pxH", "clientPxW", "clientPxH", "w", "h");

    /// <summary>스냅샷에서 위치 필드만(메인·SDI 각 창의 x·y) 뽑은 비교용 문자열.</summary>
    private static string PositionsOf(JsonObject? snap) => Fields(snap, "x", "y");

    private static string Fields(JsonObject? snap, params string[] keys)
    {
        string Pick(JsonNode? o) => o is JsonObject j ? string.Join(",", keys.Select(k => j[k]?.ToJsonString() ?? "")) : "";
        return Pick(snap?["main"]) + "|" + string.Join(";", (snap?["sdi"] as JsonArray ?? new JsonArray()).Select(s => (s?["doc"]?.ToJsonString() ?? "") + ":" + Pick(s)));
    }

    private static JsonObject CaptureJson() => new()
    {
        ["setting"] = Vm.WindowMenu.CaptureProtection,
        // 플랫폼 훅(Android FLAG_SECURE·데스크톱 호스트가 설정)이 없으면 셸 호스트 조회(App.WindowHost) — 둘 다 없으면 null(판정 N/A)
        ["applied"] = RemoteCommands.CaptureProtectProbe is { } probe ? probe() : App.WindowHost?.GetCaptureProtection(),
        ["perWindow"] = RemoteCommands.CaptureAffinityProbe?.Invoke() is { } list
            ? new JsonArray(list.Select(w => (JsonNode?)new JsonObject { ["window"] = w.Window, ["affinity"] = w.Affinity }).ToArray())
            : null,
        ["platform"] = AutomationBridge.Platform,
    };
}
#endif
