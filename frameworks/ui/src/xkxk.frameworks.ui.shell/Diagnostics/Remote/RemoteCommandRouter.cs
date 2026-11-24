// 원격 명령 이름 → 처리기 등록부. 모든 처리기를 UI 스레드에서 실행하고 JSON 응답 문자열을 만든다(HTTP·브라우저 브리지 공용)
#if PD_DIAGNOSTICS // 진단 빌드 전용(PdDiagnostics=true). 제품 빌드 대체(모든 요청 거부)는 RemoteProductStubs.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace xkxk.frameworks.ui.shell.Diagnostics.Remote;

/// <summary>
/// 명령 표. 이름은 "영역.동작"(docs.open). HTTP 경로형 /api/docs/open 은 '/' → '.' 로 바꿔 같은 처리기로 간다.
/// 처리기는 UI 스레드에서 실행된다(Dispatcher.UIThread.InvokeAsync). 실입력(SendInput)은 쓰지 않는다.
/// </summary>
public sealed class RemoteCommandRouter
{
    private readonly Dictionary<string, (string Help, Func<JsonObject, Task<JsonNode?>> Run)> _commands =
        new(StringComparer.OrdinalIgnoreCase);

    private static RemoteCommandRouter? _default;

    /// <summary>기본 명령 전체가 등록된 공용 라우터(처음 접근 시 생성).</summary>
    public static RemoteCommandRouter Default => _default ??= CreateDefault();

    /// <summary>명령 실행 기록(호스트 로그) — 기본은 Console. 브라우저는 개발자 도구 콘솔로 나간다.</summary>
    public static Action<string> Trace { get; set; } = line => Console.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [remote] {line}");

    public IReadOnlyCollection<string> Names => _commands.Keys;

    public void Register(string name, string help, Func<JsonObject, Task<JsonNode?>> run) => _commands[name] = (help, run);

    /// <summary>동기 처리기 등록(대부분의 명령).</summary>
    public void Register(string name, string help, Func<JsonObject, JsonNode?> run) =>
        _commands[name] = (help, a => Task.FromResult(run(a)));

    private static RemoteCommandRouter CreateDefault()
    {
        var r = new RemoteCommandRouter();
        r.Register("help", "명령 목록과 설명", _ => r.Help());
        r.Register("perf.reset", "조작 기록·프레임 표본 초기화(PerfProbe.ResetOperations+ResetFrames — 측정 시나리오 시작 전, 시행별 frame_p50/p95/max 가 누적되지 않게)", _ =>
        {
            PerfProbe.Current.ResetOperations();
            PerfProbe.Current.ResetFrames();
            return JsonValue.Create(true);
        });
        // 유휴 렌더 출처 가르기(lead 승인, 진단 전용): 같은 실행 안에서 프레임 콜백·상태바 FPS 를 껐다 켜며 frame_samples·rAF 증가를 비교
        r.Register("perf.idle", "유휴 렌더 출처 가르기 stop=probe|fps|all 또는 resume=probe|fps|all(생략 시 읽기만) → {probe(프레임 콜백 동작), fpsPaused, frames}", a =>
        {
            var p = PerfProbe.Current;
            var stop = a.Str("stop")?.ToLowerInvariant();
            var resume = a.Str("resume")?.ToLowerInvariant();
            if (stop is "probe" or "all") { p.SetProbeRunning(false); }
            if (stop is "fps" or "all") { p.FpsPaused = true; }
            if (resume is "probe" or "all") { p.SetProbeRunning(true); }
            if (resume is "fps" or "all") { p.FpsPaused = false; }
            var s = p.IdleState;
            return new JsonObject { ["probe"] = s.Probe, ["fpsPaused"] = s.FpsPaused, ["frames"] = s.Frames };
        });
        r.Register("perf.ops", "조작별 속도 통계(PerfProbe operations: count·sync/frame p50·p95·max) [op=이름 접두, 예 remote.docs.]", a =>
        {
            var ops = JsonNode.Parse(PerfProbe.Current.ToJson(AutomationBridge.Platform))?["operations"] as JsonArray ?? new JsonArray();
            var prefix = a.Str("op");
            return new JsonArray(ops.Where(o => string.IsNullOrEmpty(prefix)
                    || (o?["name"]?.GetValue<string>() ?? "").StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .Select(o => o?.DeepClone()).ToArray());
        });
        RemoteWindowsCommands.Register(r);
        RemoteUiCommands.Register(r);
        RemoteSettingsCommands.Register(r);
        RemoteChromeCommands.Register(r);
        RemoteSdiShadowCommands.Register(r); // chrome.sdiShadow(W1 그림자 모양 리소스 조회·변경)
        RemoteWindowMenuCommands.Register(r);
        RemotePlacementCommands.Register(r); // 요건 4·5 placement.force·state·markPlaced·selftest
        RemoteLangCommands.Register(r);
        RemoteLayoutsCommands.Register(r);
        RemotePanelsCommands.Register(r);
        RemoteProbeCommands.Register(r); // Q8 자석 구동·Q4 theme.census·cmd.run
        RemoteMonitorCommands.Register(r); // U20 monitor.list·monitor.select·monitor.maximize
        RemoteScrollCommands.Register(r); // ui.scroll(그리드 행·ScrollViewer) — 안에서 dock.trace·dock.menuHeaders 도 등록
        RemoteUiModeCommands.Register(r); // ui.mode(태블릿/폰 모드 조회·전환, odev-2r 값 정의)
        // 화면 층 진단(벤치 bench.*·컨트롤 갤러리 gallery.*)은 화면 층이 Application 생성 때 이 라우터에 더한다(IViewRegistry.RegisterRemote)
        xkxk.frameworks.ui.shell.Diagnostics.PinchZoomDiag.RegisterRemote(r); // 핀치 줌 pinch.sim|pinch.state(odev-7c)
        RemoteSystemCommands.Register(r); // UI-17 시스템 메뉴 sys.get·sys.open·sys.close(odev-1r, RemoteSystemCommands.cs)
        RemoteSplitCommands.Register(r); // UI-20 패널 영역 분할 split.dump·split.do·split.drag·split.capture(odev-1r, RemoteSplitCommands.cs)
        RemoteDockAsDocumentCommands.Register(r); // D-DAD1 판정 dock.asDocument(odev-1s, RemoteDockAsDocumentCommands.cs)
        RemoteTrayCommands.Register(r); // 시스템 트레이 tray.state·tray.invoke·tray.enable(odev-tray, RemoteTrayCommands.cs)
        RemoteSimActiveCommands.Register(r); // 창 활성 처리기·탭 클릭 분기 시험 sim.*
        RemoteDockMenuInvokeCommands.Register(r); // Dock 메뉴 항목 실행 dock.menuInvoke(odev-chrome, RemoteDockMenuInvokeCommands.cs — L2 우클릭 대체)
        ContextMenuInspector.Register(r); // ContextMenu 항목 조회·열기 ui.contextMenu(odev-7i, ContextMenuInspector.cs 단독 클래스 — 제목줄 시스템 메뉴 id 대조)
        RemotePointerInputCommands.Register(r); // 앱 내부 포인터 grid.clickRow·ui.pointerClick·ui.pointerDrag(odev-chrome, RemotePointerInputCommands.cs — L2 클릭 대체)
        RemoteDockMenuOpenCommands.Register(r); // 문서 우클릭 메뉴 팝업 없이 열거·실행 dock.menu(odev-dock, RemoteDockMenuOpenCommands.cs — U14 판정)
        RemoteRenderProbeCommands.Register(r); // U3 MDI 오렌더 원인 가르기 render.probe·render.switch·render.invalidate(odev-render, RemoteRenderProbeCommands.cs)
        RemoteTextRenderCommands.Register(r); // 8번 글자 흐림 원인 A/B text.query·text.render·text.options(odev-font, RemoteTextRenderCommands.cs)
        RemotePanelCornerCommands.Register(r); // 요건 3 패널 끝 확장 panel.corner.state·click·release·rules·views(odev-dock, RemotePanelCornerCommands.cs)
        RemoteTitleMenuCommands.Register(r); // 창 메뉴 winmenu.state·run·noteTab·selftest(odev-fix-winmenu, RemoteTitleMenuCommands.cs)
        RemoteTourCommands.Register(r); // 요건 6 도움말 투어 tour.start·next·prev·skip·end·state·esc·key·probe(odev-tour-wire, RemoteTourCommands.cs)
        RemoteNotifyCommands.Register(r); // F03 시험용 새 알림 notify.post(RemoteNotifyCommands.cs)
        RemoteSnapCommands.Register(r); // 창 배치(스냅) snap.get·set·state·apply(RemoteSnapCommands.cs)
        RemoteSnapAssistCommands.Register(r); // Snap Assist snapassist.state·pick·zone·skip·cancel(RemoteSnapAssistCommands.cs)
        RemoteThemeDesignCommands.Register(r); // 테마 요소별 사용자 지정 themedesign.elements·get·set·reset·resetall·contrast(RemoteThemeDesignCommands.cs)
        RemoteDesignThemeCommands.Register(r); // 디자인 테마 축 style.set·get·state·effect.set, overview.open·select·state, depth.probe, gpu.simulateSoftware(RemoteDesignThemeCommands.cs)
        return r;
    }

    private JsonNode Help()
    {
        var arr = new JsonArray();
        foreach (var (name, entry) in _commands.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            arr.Add(new JsonObject { ["cmd"] = name, ["help"] = entry.Help });
        }

        return arr;
    }

    // 측정에서 뺄 명령(조회 자체·대기·종료 — 조작 속도 통계를 흐리지 않게)
    private static readonly HashSet<string> Unmeasured = new(StringComparer.OrdinalIgnoreCase) { "perf.ops", "perf.reset", "perf.get", "perf.idle", "wait.for", "app.shutdown", "app.closeMain", "help", "ping" };

    /// <summary>
    /// 명령 실행 → 응답 JSON. 예외는 {ok:false,error} 로 바꾼다(서버는 계속 동작).
    /// PERF-UI: 응답에 elapsed_ms(요청 수신 receivedTimestamp → 처리 + 렌더 2프레임 반영)와 ui_frame_ms(처리 종료 → 렌더 반영)를 넣고 PerfProbe operations("remote.명령")에 누적한다(perf.ops·perf.get 으로 조회).
    /// 창이 숨겨져 프레임이 오지 않으면 500ms 뒤 포기하고 ui_frame_ms=null.
    /// </summary>
    public async Task<string> InvokeAsync(string cmd, JsonObject args, long? receivedTimestamp = null)
    {
        cmd = cmd.Trim().Replace('/', '.');
        if (!_commands.TryGetValue(cmd, out var entry))
        {
            return RemoteProtocol.Error(cmd, $"알 수 없는 명령입니다: {cmd} (help 로 목록 확인)");
        }

        var start = receivedTimestamp ?? Stopwatch.GetTimestamp();
        try
        {
            // 측정 대상 조작은 프레임 표본 구간을 연다(유휴 연속 체인 제거 뒤에도 조작 중 프레임·FPS 계측이 예전처럼 돌게). 조회·대기 명령(Unmeasured)은 유휴 측정을 깨지 않게 제외
            if (!Unmeasured.Contains(cmd)) { await Dispatcher.UIThread.InvokeAsync(PerfProbe.Current.Wake); }
            var result = Dispatcher.UIThread.CheckAccess()
                ? await entry.Run(args)
                : await Dispatcher.UIThread.InvokeAsync<JsonNode?>(() => entry.Run(args));
            var runMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            double? frameMs = null;
            if (!Unmeasured.Contains(cmd))
            {
                var frameStart = Stopwatch.GetTimestamp();
                var rendered = Dispatcher.UIThread.CheckAccess()
                    ? await WaitRenderedAsync()
                    : await Dispatcher.UIThread.InvokeAsync<bool>(WaitRenderedAsync);
                if (rendered)
                {
                    frameMs = Stopwatch.GetElapsedTime(frameStart).TotalMilliseconds;
                }
            }

            var elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            if (!Unmeasured.Contains(cmd))
            {
                // PerfProbe.Operations(odev-3) 정의와 맞춤: sync=시작→처리 끝, frame=시작→처리 뒤 두 번째 프레임. 시작=요청 수신.
                // TimeOperation 범위는 값을 호출자에게 돌려주지 않으므로 같은 2프레임 정의로 잰 값을 RecordOperation 으로 넣는다.
                var (op, sync, frame) = ("remote." + cmd, runMs, frameMs is null ? runMs : elapsed);
                Dispatcher.UIThread.Post(() => PerfProbe.Current.RecordOperation(op, sync, frame));
            }
            Trace($"{cmd} ok run={runMs:0}ms elapsed={elapsed:0}ms frame={frameMs:0}");
            return RemoteProtocol.Ok(cmd, result, runMs, elapsed, frameMs);
        }
        catch (RemoteNotReadyException nr)
        {
            Trace($"{cmd} NOT-READY {nr.Reason}");
            return RemoteProtocol.NotReady(cmd, nr.Reason, nr.Message); // 일반 오류와 구분(lead 02:5x)
        }
        catch (Exception ex)
        {
            var msg = ex is RemoteCommandException or ArgumentException ? ex.Message : ex.ToString();
            Trace($"{cmd} FAIL {ex.Message}");
            return RemoteProtocol.Error(cmd, msg);
        }
    }

    /// <summary>메인 창 렌더 2프레임 대기(UI 스레드). 500ms 안에 프레임이 오지 않으면 false(최소화·숨김 창).</summary>
    private static async Task<bool> WaitRenderedAsync()
    {
        var frames = AutomationBridge.WaitFramesAsync(VisualTreeInspector.Main(), 2);
        return await Task.WhenAny(frames, Task.Delay(500)) == frames;
    }

    /// <summary>요청 JSON 1개 실행: {"cmd":"docs.open","args":{...}}. 브라우저 브리지(window.pdAutomation.invoke)용.</summary>
    public Task<string> InvokeJsonAsync(string requestJson)
    {
        JsonObject req;
        try
        {
            req = RemoteProtocol.ParseObject(requestJson);
        }
        catch (Exception ex)
        {
            return Task.FromResult(RemoteProtocol.Error("", "요청 JSON 해석 실패: " + ex.Message));
        }

        var cmd = req.Str("cmd") ?? "";
        var args = req["args"] as JsonObject ?? new JsonObject();
        return InvokeAsync(cmd, (JsonObject)args.DeepClone());
    }
}
#endif
