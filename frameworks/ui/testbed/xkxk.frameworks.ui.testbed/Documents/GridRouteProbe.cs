// 그리드 터치 이동 1건의 라우팅 지점별 .NET 경과 시간 계수기(D-PF2 M2, 진단 빌드 전용) — 110ms 가 어느 조상 사이에서 나는지 가른다. 동작은 바꾸지 않는다
#if PD_DIAGNOSTICS
using xkxk.frameworks.ui.shell;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Dock.Avalonia.Controls;
using xkxk.frameworks.ui.shell.Diagnostics;
using xkxk.frameworks.ui.views;

namespace xkxk.frameworks.ui.testbed.Documents;

/// <summary>
/// 포인터 이동(터치) 1건이 TopLevel 에서 그리드까지 내려갔다 올라오는 동안, 아래 지점(형식별 Tunnel·Bubble 클래스 처리기)에서 시각을 찍는다.
/// 지점 순서(경로 순): tlT → mlpT(MdiLayoutPanel) → mdwT(MdiDocumentWindow) → zdT(ZoomDecorator) → dgT(DataGrid) → rpT(DataGridRowsPresenter)
/// → (셀 등 대상) → rpB → dgB → zdB → mdwB → mlpB → tlB.
/// 키는 조건과 관계없이 고정이다(odev-8t 검토 1). 탭 모드처럼 경로에 없는 지점은 -1 로 낸다.
/// - grid_probe_worst_{first|rest}_at_{지점}_ms: 총 시간(tlT→tlB)이 가장 컸던 이벤트 1건의 지점별 누적 경과(tlT 기준). 구간은 이웃 지점의 차로 읽는다(검토 2).
/// - grid_probe_worst_{first|rest}_total_ms: 그 이벤트의 총 시간. first 는 누름 뒤 첫 이동, rest 는 나머지다(검토 3).
/// - grid_probe_hit_{지점}_count: 지점이 찍힌 이벤트 수. grid_probe_seg_events: 끝까지 잰 이벤트 수. grid_probe_seg_dropped: 라우팅 중 새 이동이 시작돼 버린 이벤트 수(검토 6).
/// 모두 누적값이라 회마다 앱을 다시 띄워 읽는다. 관찰만 하고 Handled 는 바꾸지 않는다. GridScrollProbe.Install 이 1회 부른다.
/// 한계(검토 4): 끝 지점은 TopLevel Bubble '클래스' 처리기다. 같은 TopLevel 의 인스턴스 Bubble 처리기가 그 뒤에 돌면 들어가지 않는다.
/// 시작 지점(tlT)은 GridScrollProbe 의 grid_probe_move_*(표시기 처리기 진입 기준)와 다르므로 두 값을 섞지 않는다.
/// 같은 형식이 경로에 두 번 있으면 먼저 만난 것만 찍는다. Tunnel 에서는 바깥, Bubble 에서는 안쪽이다(검토 5). 이 구성에서는 중첩이 없다(추정).
/// 경로 깊이 기록(M2b, M2 판정 tlT→mlpT 후속): InputElement Tunnel 클래스 처리기가 이벤트가 지나는 요소마다 (형식 이름, 시각)을 최대 ChainMax 칸 남긴다.
/// - grid_probe_chain_{first|rest}_len, grid_probe_chain_{first|rest}_{i}_type(문자열), grid_probe_chain_{first|rest}_{i}_ms(tlT 기준 누적): worst 이벤트와 같은 1건이다.
/// - 구간 i→i+1 은 '요소 i 의 남은 처리기 + 요소 i+1 의 앞 처리기'다. 클래스·인스턴스 처리기 순서는 미확인이라 경계가 처리기 1개만큼 어긋날 수 있다.
/// - 처리기가 경로 요소마다 돌아 오버헤드가 있다. 시간값은 앞 M2 판정값과 섞지 않고 위치 판정에만 쓴다.
/// </summary>
internal static class GridRouteProbe
{
    private const int Points = 12;

    private static readonly string[] s_names =
    [
        "tlT", "mlpT", "mdwT", "zdT", "dgT", "rpT", "rpB", "dgB", "zdB", "mdwB", "mlpB", "tlB",
    ];

    private static readonly long[] s_marks = new long[Points];
    private static readonly long[] s_hits = new long[Points];
    private static readonly double[] s_worst = [0, 0]; // [0]=first, [1]=rest
    private static PointerEventArgs? s_current;
    private static bool s_firstPending;
    private static bool s_currentIsFirst;
    private static long s_events;
    private static long s_dropped;
    private static bool s_installed;
    private const int ChainMax = 128;
    private static readonly string[] s_chainTypes = new string[ChainMax];
    private static readonly long[] s_chainMarks = new long[ChainMax];
    private static int s_chainLen;

    public static void Install()
    {
        if (s_installed)
        {
            return;
        }

        s_installed = true;
        const RoutingStrategies T = RoutingStrategies.Tunnel;
        const RoutingStrategies B = RoutingStrategies.Bubble;
        InputElement.PointerPressedEvent.AddClassHandler<TopLevel>((_, e) => { if (e.Pointer.Type == PointerType.Touch) { s_firstPending = true; } },
            T, handledEventsToo: true);
        InputElement.PointerMovedEvent.AddClassHandler<TopLevel>((_, e) => Start(e), T, handledEventsToo: true);
        // 경로 깊이 기록은 진단판 중에서도 PdWasmProfile 판(런타임 스위치 Pd.Diagnostics.RouteChain=true)에서만 켠다. 시간 판정판에는 처리기를 등록하지 않는다
        var chainOn = System.AppContext.TryGetSwitch("Pd.Diagnostics.RouteChain", out var sw) && sw;
        PerfProbe.Current.SetMetric("grid_probe_chain_enabled", chainOn ? 1 : 0);
        if (chainOn)
        {
            InputElement.PointerMovedEvent.AddClassHandler<InputElement>((el, e) => Chain(el, e), T, handledEventsToo: true); // Start 뒤에 등록해 TopLevel 칸이 0 근처가 되게 한다
        }

        InputElement.PointerMovedEvent.AddClassHandler<MdiLayoutPanel>((_, e) => Mark(e, 1), T, handledEventsToo: true);
        InputElement.PointerMovedEvent.AddClassHandler<MdiDocumentWindow>((_, e) => Mark(e, 2), T, handledEventsToo: true);
        InputElement.PointerMovedEvent.AddClassHandler<ZoomDecorator>((_, e) => Mark(e, 3), T, handledEventsToo: true);
        InputElement.PointerMovedEvent.AddClassHandler<DataGrid>((_, e) => Mark(e, 4), T, handledEventsToo: true);
        InputElement.PointerMovedEvent.AddClassHandler<DataGridRowsPresenter>((_, e) => Mark(e, 5), T, handledEventsToo: true);
        InputElement.PointerMovedEvent.AddClassHandler<DataGridRowsPresenter>((_, e) => Mark(e, 6), B, handledEventsToo: true);
        InputElement.PointerMovedEvent.AddClassHandler<DataGrid>((_, e) => Mark(e, 7), B, handledEventsToo: true);
        InputElement.PointerMovedEvent.AddClassHandler<ZoomDecorator>((_, e) => Mark(e, 8), B, handledEventsToo: true);
        InputElement.PointerMovedEvent.AddClassHandler<MdiDocumentWindow>((_, e) => Mark(e, 9), B, handledEventsToo: true);
        InputElement.PointerMovedEvent.AddClassHandler<MdiLayoutPanel>((_, e) => Mark(e, 10), B, handledEventsToo: true);
        InputElement.PointerMovedEvent.AddClassHandler<TopLevel>((_, e) => End(e), B, handledEventsToo: true);
    }

    private static void Start(PointerEventArgs e)
    {
        if (e.Pointer.Type != PointerType.Touch)
        {
            return;
        }

        if (s_current is not null)
        {
            PerfProbe.Current.SetMetric("grid_probe_seg_dropped", ++s_dropped); // 앞 이벤트가 끝나기 전에 새 이동이 시작됨(재진입)
        }

        System.Array.Clear(s_marks);
        s_chainLen = 0;
        s_current = e;
        s_currentIsFirst = s_firstPending;
        s_firstPending = false;
        s_marks[0] = Stopwatch.GetTimestamp();
    }

    private static void Mark(PointerEventArgs e, int index)
    {
        if (ReferenceEquals(e, s_current) && s_marks[index] == 0)
        {
            s_marks[index] = Stopwatch.GetTimestamp();
        }
    }

    private static void Chain(InputElement element, PointerEventArgs e)
    {
        if (ReferenceEquals(e, s_current) && s_chainLen < ChainMax)
        {
            s_chainMarks[s_chainLen] = Stopwatch.GetTimestamp();
            s_chainTypes[s_chainLen++] = element.GetType().Name;
        }
    }

    private static void WriteChain(PerfProbe p, string kind)
    {
        p.SetMetric($"grid_probe_chain_{kind}_len", s_chainLen);
        for (var i = 0; i < s_chainLen; i++)
        {
            p.SetText($"grid_probe_chain_{kind}_{i}_type", s_chainTypes[i]);
            p.SetMetric($"grid_probe_chain_{kind}_{i}_ms", Ms(s_marks[0], s_chainMarks[i]));
        }
    }

    private static void End(PointerEventArgs e)
    {
        if (!ReferenceEquals(e, s_current))
        {
            return;
        }

        s_current = null;
        s_marks[Points - 1] = Stopwatch.GetTimestamp();
        var p = PerfProbe.Current;
        p.SetMetric("grid_probe_seg_events", ++s_events);
        for (var i = 0; i < Points; i++)
        {
            if (s_marks[i] != 0)
            {
                p.SetMetric($"grid_probe_hit_{s_names[i]}_count", ++s_hits[i]);
            }
        }

        var total = Ms(s_marks[0], s_marks[Points - 1]);
        var slot = s_currentIsFirst ? 0 : 1;
        if (total <= s_worst[slot])
        {
            return;
        }

        s_worst[slot] = total; // 최악 이벤트 1건의 지점별 값을 한 묶음으로 바꿔 쓴다
        var kind = slot == 0 ? "first" : "rest";
        p.SetMetric($"grid_probe_worst_{kind}_total_ms", total);
        for (var i = 0; i < Points; i++)
        {
            p.SetMetric($"grid_probe_worst_{kind}_at_{s_names[i]}_ms", s_marks[i] == 0 ? -1 : Ms(s_marks[0], s_marks[i]));
        }

        WriteChain(p, kind); // 같은 worst 이벤트의 경로 깊이 기록
    }

    private static double Ms(long from, long to) => (to - from) * 1000.0 / Stopwatch.Frequency;
}
#endif
