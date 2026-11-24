// MDI 끌기 프레임별 내부 원인(GC 세대별 증가·UI 할당·레이아웃 패스·rAF→커밋 구간·디스패처 대기·놓기 처리)을 고정 버퍼에 모으는 진단 계측(PD_DIAGNOSTICS 전용, 01a P1, 원격 chrome.frameDiag)
#if PD_DIAGNOSTICS
using System;
using System.Diagnostics;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Dock.Avalonia.Controls;
using xkxk.frameworks.ui.shell.Diagnostics.Remote; // JsonObject 인자 확장 메서드 Int·Bool(RemoteProtocol)

namespace xkxk.frameworks.ui.shell.Diagnostics;

/// <summary>
/// 목적: r12d doc5 '중간 구간 긴 렌더(rAF 12~20ms)'와 놓기 간격의 내부 원인을 가른다(GC 인지·레이아웃인지·그리기 기록인지·디스패처 작업 적체인지).
/// 켜면(on=true) MDI 문서 창 누름마다 끌기 번호를 올리고, 누름부터 놓은 뒤 tailMs 까지 rAF 마다 직전 프레임 1행을 고정 배열(구조체, 켤 때 1회 할당)에 쓴다.
/// 행 k 는 rAF_k 시작부터 rAF_k+1 시작까지 사이에 일어난 일이다. 기록 경로는 할당이 없다(콜백·디스패처 탐침 대리자는 정적 1개를 재사용).
/// 예외: dispatch=true 면 프레임마다 Dispatcher.Post 1회가 Avalonia 내부 작업 객체를 만든다 — 그래서 기본 끔이며, GC 를 볼 회차에는 켜지 않는다.
/// 레이아웃 값은 PerfLayoutLog 싱크, 커밋 값은 PerfCommitLog(AfterCommit) 가 걸린 뒤에만 들어온다(둘 다 진단판 프레임 표본기가 첫 입력 때 건다). 응답의 layoutPassesSeen·commitsSeen 이 0 이면 그 열은 무효다.
/// 누름·이동·놓기 단계 시간은 MdiDragFrameDiag.Stages.cs(TopLevel 터널~버블 전체, 캡처 해제 시점, Pd 처리기별 Reset·로그·Apply, 끌기 시작 표적 수집, 정지 창 찾기·걸기).
/// 제품 빌드에는 이 파일 전체가 들어가지 않는다. 판정 회차가 아니라 진단 회차에서만 켠다(rAF 체인을 놓은 뒤 tailMs 까지 유지한다).
/// </summary>
public static partial class MdiDragFrameDiag
{
    public const int DefaultCapacity = 4096, MinCapacity = 256, MaxCapacity = 32768;
    public const int DefaultTailMs = 600, MinTailMs = 0, MaxTailMs = 3000; // 놓은 뒤 QuietMs(기본 300) 원복과 P2 분산 복원 프레임까지 보려고 600

    // 행 플래그
    private const byte FlagRelease = 1, FlagRestore = 2, FlagSpread = 4, FlagNear = 8, FlagDragging = 16, FlagFirst = 32;

    private struct Row
    {
        public int Drag, Moves, Gc0, Gc1, Gc2, LayoutPasses, QueuedMeasure, QueuedArrange, Commits;
        public float T, Interval, LayoutMs, UiSpanMs, DispatchMs, RelTotalMs;
        public float DownMs, MoveMs, MoveMaxMs, TjBeginMs, FlushStartedMs, RelCapLostMs, RelTabJoinMs, RelTabJoinResetMs, RelSnapMs, RelSnapResetMs, RelSnapLogMs, ApplyMs; // 단계 시간(MdiDragFrameDiag.Stages.cs)
        public long AllocBytes;
        public byte Flags;
    }

    private static readonly double s_msPerTick = 1000.0 / Stopwatch.Frequency;
    private static readonly Action<TimeSpan> s_onFrame = OnFrame;
    private static readonly Action s_onDispatch = OnDispatchProbe;
    private static Row[] s_rows = Array.Empty<Row>();
    private static Row s_cur; // 진행 중인 행(rAF_k 이후 누적)
    private static int s_postRow = -1;
    private static float s_pendDownMs = -1;
    private static int s_count, s_dropped, s_drag, s_capacity = DefaultCapacity, s_tailMs = DefaultTailMs;
    private static bool s_armed, s_installed, s_active, s_running, s_released, s_dispatchProbe, s_dispatchPending, s_haveFrame;
    private static long s_pressTs, s_frameTs, s_tailUntil, s_relStart, s_postTs, s_alloc;
    private static int s_gc0, s_gc1, s_gc2;
    private static long s_commitsSeen, s_layoutPassesSeen;
    private static TopLevel? s_top;

    private static long Now => Stopwatch.GetTimestamp();

    private static float Ms(long ticks) => (float)(ticks * s_msPerTick);

    /// <summary>P2 분산 복원 1프레임(MdiStaticLight.Restore).</summary>
    public static void NoteRestoreStep() { if (s_active) { s_cur.Flags |= FlagRestore; } }

    /// <summary>옵션 B 분산 적용 1프레임(MdiStaticLight).</summary>
    public static void NoteSpreadStep() { if (s_active) { s_cur.Flags |= FlagSpread; } }

    /// <summary>옵션 A 합류 아이콘 켬/끔 1회(MdiTabJoinDiag.NoteNear).</summary>
    public static void NoteNearToggle() { if (s_active) { s_cur.Flags |= FlagNear; } }

    /// <summary>레이아웃 패스 시작(PerfLayoutLog — 측정·배치 대기 수).</summary>
    internal static void OnLayoutStarted(int measure, int arrange)
    {
        if (!s_active) { return; }
        s_cur.QueuedMeasure = Math.Max(s_cur.QueuedMeasure, measure);
        s_cur.QueuedArrange = Math.Max(s_cur.QueuedArrange, arrange);
    }

    /// <summary>레이아웃 패스 끝(PerfLayoutLog).</summary>
    internal static void OnLayoutFinished(TimeSpan elapsed)
    {
        s_layoutPassesSeen++;
        if (!s_active) { return; }
        s_cur.LayoutPasses++;
        s_cur.LayoutMs += (float)elapsed.TotalMilliseconds;
    }

    /// <summary>합성 커밋(PerfCommitLog AfterCommit). 이 행의 첫 커밋까지 rAF 시작부터 걸린 시간을 남긴다.</summary>
    internal static void OnCommit()
    {
        s_commitsSeen++;
        if (!s_active || !s_haveFrame) { return; }
        if (s_cur.Commits++ == 0) { s_cur.UiSpanMs = Ms(Now - s_frameTs); }
    }

    private static void Install()
    {
        if (s_installed) { return; }
        s_installed = true;
        InputElement.PointerPressedEvent.AddClassHandler<MdiDocumentWindow>(OnPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        InputElement.PointerMovedEvent.AddClassHandler<MdiDocumentWindow>((_, _) => { if (s_active && !s_released) { s_cur.Moves++; } }, RoutingStrategies.Bubble, handledEventsToo: true);
        InputElement.PointerReleasedEvent.AddClassHandler<TopLevel>((_, _) => { if (s_active && !s_released) { s_relStart = Now; } }, RoutingStrategies.Tunnel, handledEventsToo: true);
        InputElement.PointerReleasedEvent.AddClassHandler<TopLevel>(OnReleasedEnd, RoutingStrategies.Bubble, handledEventsToo: true);
        InstallStages();
    }

    private static void OnPressed(MdiDocumentWindow w, PointerPressedEventArgs e)
    {
        if (!s_armed || !e.GetCurrentPoint(w).Properties.IsLeftButtonPressed || TopLevel.GetTopLevel(w) is not { } top) { return; }
        s_drag++;
        s_pressTs = Now;
        s_released = false;
        s_tailUntil = long.MaxValue;
        if (!s_active)
        {
            s_active = true;
            s_haveFrame = false;
            s_cur = default;
        }

        s_cur.Flags |= FlagDragging;
        s_top = top;
        if (!s_running)
        {
            s_running = true;
            top.RequestAnimationFrame(s_onFrame);
        }
    }

    private static void OnReleasedEnd(TopLevel _, PointerReleasedEventArgs e)
    {
        if (!s_active || s_released) { return; }
        s_released = true;
        s_cur.Flags |= FlagRelease;
        if (s_relStart > 0) { s_cur.RelTotalMs = Ms(Now - s_relStart); }
        s_relStart = 0;
        s_tailUntil = Now + (long)(s_tailMs / s_msPerTick);
    }

    private static void OnFrame(TimeSpan _)
    {
        var now = Now;
        if (s_haveFrame) { Commit(now); }
        if (!s_armed || now > s_tailUntil)
        {
            (s_active, s_running, s_haveFrame) = (false, false, false);
            return;
        }

        // 다음 행 시작: 기준점(GC·할당·시각)을 잡는다
        var first = !s_haveFrame;
        s_cur = default;
        s_cur.Drag = s_drag;
        s_cur.T = Ms(now - s_pressTs);
        s_cur.UiSpanMs = s_cur.DispatchMs = s_cur.RelTotalMs = s_cur.RelCapLostMs = s_cur.DownMs = -1;
        if (first && s_pendDownMs >= 0) { s_cur.DownMs = s_pendDownMs; } // 첫 rAF 전에 끝난 누름 처리는 첫 행에 싣는다
        s_pendDownMs = -1;
        if (!s_released) { s_cur.Flags = FlagDragging; }
        if (first) { s_cur.Flags |= FlagFirst; }
        s_frameTs = now;
        s_gc0 = GC.CollectionCount(0);
        s_gc1 = GC.CollectionCount(1);
        s_gc2 = GC.CollectionCount(2);
        s_alloc = GC.GetAllocatedBytesForCurrentThread();
        s_haveFrame = true;
        if (s_dispatchProbe && !s_dispatchPending)
        {
            s_dispatchPending = true;
            s_postTs = now;
            s_postRow = s_count; // 이 행이 마감되면 들어갈 자리
            Dispatcher.UIThread.Post(s_onDispatch, DispatcherPriority.Normal);
        }

        s_top?.RequestAnimationFrame(s_onFrame);
    }

    // 진행 중인 행을 마감해 배열에 쓴다(rAF_k+1 시작 시각 기준)
    private static void Commit(long now)
    {
        s_cur.Interval = Ms(now - s_frameTs);
        s_cur.Gc0 = GC.CollectionCount(0) - s_gc0;
        s_cur.Gc1 = GC.CollectionCount(1) - s_gc1;
        s_cur.Gc2 = GC.CollectionCount(2) - s_gc2;
        s_cur.AllocBytes = GC.GetAllocatedBytesForCurrentThread() - s_alloc;
        if (s_count < s_rows.Length) { s_rows[s_count++] = s_cur; }
        else { s_dropped++; }
    }

    // 탐침은 올린 행(s_postRow)에 귀속한다 — 다음 rAF 뒤에 돌았으면 이미 마감된 그 행에 쓴다(값 = 올린 뒤 실행까지 대기 ms)
    private static void OnDispatchProbe()
    {
        s_dispatchPending = false;
        var ms = Ms(Now - s_postTs);
        if (s_postRow == s_count && s_active && s_haveFrame) { s_cur.DispatchMs = ms; }
        else if (s_postRow >= 0 && s_postRow < s_count) { s_rows[s_postRow].DispatchMs = ms; }
    }

    private static readonly string[] s_fields =
    {
        "drag", "t_ms", "interval_ms", "moves", "gc0", "gc1", "gc2", "alloc_bytes", "layout_ms", "layout_passes", "queued_measure_max", "queued_arrange_max",
        "commits", "ui_span_ms", "dispatch_ms", "flags", "down_ms", "move_ms", "move_max_ms", "tj_begin_ms", "flush_started_ms",
        "rel_total_ms", "rel_caplost_ms", "rel_tabjoin_ms", "rel_tabjoin_reset_ms", "rel_snap_ms", "rel_snap_reset_ms", "rel_snap_log_ms", "apply_ms",
    };

#pragma warning disable CA2255 // 등록 파일(RemoteCommandRouter.cs)이 배정 밖이라 모듈 초기화로 붙인다(MdiTabJoinDiag 와 같은 방식)
    [System.Runtime.CompilerServices.ModuleInitializer]
#pragma warning restore CA2255
    internal static void Init() =>
        Remote.RemoteCommandRouter.Default.Register("chrome.frameDiag",
            "끌기 프레임별 내부 원인 기록(진단) on=true|false capacity=256~32768 tailMs=0~3000 dispatch=true|false(준 값만 설정) [dump=true 행 포함] [reset=true 응답 뒤 비움]"
            + " → {armed,capacity,tailMs,dispatch,drags,rows_n,dropped,commitsSeen,layoutPassesSeen,flags{release=1,restore=2,spread=4,near=8,dragging=16,first=32},fields[],rows[][]}", Handle);

    private static JsonNode? Handle(JsonObject a)
    {
        if (a.Int("capacity") is { } cap) { s_capacity = Math.Clamp(cap, MinCapacity, MaxCapacity); }
        if (a.Int("tailMs") is { } tail) { s_tailMs = Math.Clamp(tail, MinTailMs, MaxTailMs); }
        if (a.ContainsKey("dispatch")) { s_dispatchProbe = a.Bool("dispatch"); }
        if (a.ContainsKey("on"))
        {
            s_armed = a.Bool("on");
            if (s_armed)
            {
                Install();
                if (s_rows.Length != s_capacity) { (s_rows, s_count, s_dropped) = (new Row[s_capacity], 0, 0); } // 켤 때 1회 할당(기록 중 할당 없음)
            }
        }

        var o = new JsonObject
        {
            ["armed"] = s_armed, ["capacity"] = s_capacity, ["tailMs"] = s_tailMs, ["dispatch"] = s_dispatchProbe, ["drags"] = s_drag,
            ["rows_n"] = s_count, ["dropped"] = s_dropped, ["commitsSeen"] = s_commitsSeen, ["layoutPassesSeen"] = s_layoutPassesSeen,
            ["flags"] = new JsonObject { ["release"] = FlagRelease, ["restore"] = FlagRestore, ["spread"] = FlagSpread, ["near"] = FlagNear, ["dragging"] = FlagDragging, ["first"] = FlagFirst },
        };
        if (a.Bool("dump"))
        {
            var fields = new JsonArray();
            foreach (var f in s_fields) { fields.Add((JsonNode?)f); } // 비제네릭 Add(JsonNode?) 를 고른다(트리밍 경고 회피)
            var rows = new JsonArray();
            for (var i = 0; i < s_count; i++)
            {
                var r = s_rows[i];
                rows.Add(new JsonArray(r.Drag, R(r.T), R(r.Interval), r.Moves, r.Gc0, r.Gc1, r.Gc2, r.AllocBytes, R(r.LayoutMs), r.LayoutPasses, r.QueuedMeasure, r.QueuedArrange,
                    r.Commits, R(r.UiSpanMs), R(r.DispatchMs), (int)r.Flags, R(r.DownMs), R(r.MoveMs), R(r.MoveMaxMs), R(r.TjBeginMs), R(r.FlushStartedMs),
                    R(r.RelTotalMs), R(r.RelCapLostMs), R(r.RelTabJoinMs), R(r.RelTabJoinResetMs), R(r.RelSnapMs), R(r.RelSnapResetMs), R(r.RelSnapLogMs), R(r.ApplyMs)));
            }

            o["fields"] = fields;
            o["rows"] = rows;
        }

        if (a.Bool("reset")) { (s_count, s_dropped, s_drag, s_commitsSeen, s_layoutPassesSeen) = (0, 0, 0, 0, 0); }
        return o;
    }

    private static double R(float v) => Math.Round(v, 2);
}
#endif
