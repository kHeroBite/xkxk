// 대시보드 벤치 화면 코드 — 원격 명령(bench.dashboard)이 부르는 시나리오 진입점(load·drag·dragsdi·resize·clear)
using xkxk.frameworks.ui.shell;
using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using xkxk.frameworks.ui.shell.Framework;

namespace xkxk.frameworks.ui.views.Documents.Bench;

public partial class DashboardBenchView : UserControl
{
    private readonly DashboardBench _bench;
    private bool _busy;

    public DashboardBenchView()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) => { if (!Open.Contains(this)) { Open.Add(this); } };
        DetachedFromVisualTree += (_, _) => Open.Remove(this);
        _bench = new DashboardBench(Host, () => TopLevel.GetTopLevel(this) ?? throw new InvalidOperationException("대시보드 화면이 아직 창에 붙지 않았습니다."));
    }

    private static readonly System.Collections.Generic.List<DashboardBenchView> Open = new();

    /// <summary>가장 최근에 화면에 붙은 대시보드 벤치 문서(없으면 null) — 원격 명령이 대상 화면을 찾는다.</summary>
    public static DashboardBenchView? Latest => Open.Count > 0 ? Open[^1] : null;

    public JsonObject? LastResult { get; private set; }

    /// <summary>
    /// scenario=load|drag|dragsdi|resize|relayout|clear. 인자: lib(scottplot|livecharts|theone) n(6|12|24) points(500) style(default|pd) anim(true|false) dragfreeze(true|false, livecharts 빌드 전용 L2 — load 에서 주면 이후 만든 차트를 drag·dragsdi 중 얼림)
    /// frames(60) dx(400) dy(250) — drag: MDI 문서 전체를 App.Shell.MoveAll 로 프레임마다 옮긴다(실입력 없음, AutomationBridge --move-all 과 같은 방식).
    /// dragsdi: 이 화면이 SDI OS 창 안에 있을 때 Window.Position 을 프레임마다 바꾼다(SetWindowPos 와 같은 이동, 관리형 창은 NotApplicable).
    /// resize: 대시보드 호스트 폭을 800→1400 으로 프레임마다 바꾼다(창 크기 변경의 근사 — 차트 재배치·재그리기 부하).
    /// relayout: seconds 동안 창 크기 변경(Fold5 회전·접힘/펼침, adb 로 외부에서 일으킨다)을 지켜보며 변경마다 '크기 변경 → 2프레임 렌더' ms 를 모은다.
    /// load 는 콜드/웜을 구분하려고 호출 횟수(run)를 결과에 남긴다. 같은 프로세스에서 두 번째부터 웜이다.
    /// </summary>
    public async Task<JsonObject> RunAsync(string scenario, JsonObject a)
    {
        if (_busy) { throw new InvalidOperationException("다른 대시보드 시나리오가 실행 중입니다."); }
        _busy = true;
        try
        {
            var lib = GridBenchView.Str(a, "lib", "scottplot");
            var style = new DashStyle(a.ContainsKey("anim") && GridBenchView.Bool(a, "anim"), GridBenchView.Str(a, "style", "default").Equals("pd", StringComparison.OrdinalIgnoreCase));
#if PD_CHART_LIVECHARTS
            if (a.ContainsKey("dragsnap")) { DashDragSnapshot.Enabled = GridBenchView.Bool(a, "dragsnap"); } // 변형 L3(데스크톱 전용): load 에 dragsnap=true
            if (a.ContainsKey("dragfreeze")) { DashDragGate.Enabled = GridBenchView.Bool(a, "dragfreeze"); } // 변형 L2: load 호출에 dragfreeze=true 를 주면 그 뒤 만든 차트만 끌기 중 얼린다(인자 없으면 기존 값 유지, 기본 false)
#endif
            var r = new JsonObject { ["scenario"] = scenario, ["lib"] = lib, ["at"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"), ["run"] = ++_runs };
            var n = GridBenchView.Int(a, "n", 12);
            var frames = GridBenchView.Int(a, "frames", 60);
            switch (scenario)
            {
                case "load": r["load"] = await _bench.LoadAsync(lib, n, GridBenchView.Int(a, "points", 500), style); break;
                case "clear": _bench.Clear(); break;
                case "drag": r["drag"] = await DragAsync(frames, GridBenchView.Num(a, "dx", 400), GridBenchView.Num(a, "dy", 250)); break;
                case "dragsdi": r["dragsdi"] = await DragSdiAsync(frames, (int)GridBenchView.Num(a, "dx", 400), (int)GridBenchView.Num(a, "dy", 250)); break;
                case "resize": r["resize"] = await ResizeAsync(frames); break;
                case "relayout": r["relayout"] = await _bench.WatchRelayoutAsync(GridBenchView.Num(a, "seconds", 20)); break;
                default: throw new ArgumentException($"알 수 없는 scenario: {scenario} (load|drag|dragsdi|resize|relayout|clear)");
            }

            r["memory_after"] = BenchProbe.Memory(collect: false);
            LastResult = r;
            ResultText.Text = r.ToJsonString(new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
            return r;
        }
        finally { _busy = false; }
    }

    private static int _runs;

    private async Task<JsonObject> DragAsync(int frames, double dx, double dy)
    {
        if (XkxkUi.Current.Shell is not { } shell) { return new JsonObject { ["status"] = "NotApplicable", ["reason"] = "셸 없음" }; }
        JsonObject r;
#if PD_CHART_LIVECHARTS
        DashDragGate.SetDragging(true); // L2: Enabled=false 면 무동작
        try { r = await _bench.SweepAsync(frames, _ => shell.MoveAll(dx / frames, dy / frames)); }
        finally { DashDragGate.SetDragging(false); }
#else
        r = await _bench.SweepAsync(frames, _ => shell.MoveAll(dx / frames, dy / frames));
#endif
        r["status"] = "Done";
        r["moved"] = new JsonObject { ["dx"] = dx, ["dy"] = dy };
        return r;
    }

    private async Task<JsonObject> DragSdiAsync(int frames, int dx, int dy)
    {
        if (TopLevel.GetTopLevel(this) is not Window w) { return new JsonObject { ["status"] = "NotApplicable", ["reason"] = "OS 창이 아님(관리형 창·브라우저)" }; }
        var start = w.Position;
        JsonObject r;
#if PD_CHART_LIVECHARTS
        DashDragGate.SetDragging(true);
        try { r = await _bench.SweepAsync(frames, i => w.Position = new PixelPoint(start.X + dx * (i + 1) / frames, start.Y + dy * (i + 1) / frames)); }
        finally { DashDragGate.SetDragging(false); }
#else
        r = await _bench.SweepAsync(frames, i => w.Position = new PixelPoint(start.X + dx * (i + 1) / frames, start.Y + dy * (i + 1) / frames));
#endif
        w.Position = start; // 원위치(offscreen-guard 유지 — 시작 위치가 화면 안이면 끝 위치도 dx,dy 가 작으면 화면 안)
        r["status"] = "Done";
        return r;
    }

    private async Task<JsonObject> ResizeAsync(int frames)
    {
        var r = await _bench.SweepAsync(frames, i => Host.Width = 800 + 600.0 * i / Math.Max(1, frames - 1));
        Host.Width = double.NaN;
        r["status"] = "Done";
        r["approximation"] = "호스트 폭 800→1400 (OS 창 크기 변경이 아님 — 차트 재배치·재그리기 부하만)";
        return r;
    }
}
