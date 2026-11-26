// 원격 명령(진단 구동) — Q8 자석 구동(docs.move·docs.resize, odev-2 MagnetDriver)·Q4 테마 census(theme.census)·--cmd 1개 실행(cmd.run, odev-3 AutomationBridge)
#if PD_DIAGNOSTICS // 진단 빌드 전용(PdDiagnostics=true) — 제품 빌드에는 명령 문자열 0건(Q9)
using System;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Avalonia;
using Dock.Avalonia.Mdi;
using Dock.Model.Core;
using xkxk.frameworks.ui.shell.Docking;
using xkxk.frameworks.ui.shell.Framework;

namespace xkxk.frameworks.ui.shell.Diagnostics.Remote;

/// <summary>자석 구동·테마 census·cmd.run 원격 명령 군(옛 RemoteCommands.Probe). RemoteCommandRouter 가 Register 를 부른다.</summary>
internal static class RemoteProbeCommands
{
    // 공용 멤버 전달자(W2) — 본문은 옛 이름 그대로 쓴다.
    private static xkxk.frameworks.ui.shell.Docking.PocDocument Doc(JsonObject a, string key = "doc") => RemoteShellAccess.Doc(a, key);

    internal static void Register(RemoteCommandRouter r)
    {
        // ── Q8 자석: 사용자 끌기와 같은 계산 경로(MdiLayoutPanel.LayoutManager)를 단계별로 부른다 ──
        r.Register("docs.move",
            "Q8 자석 구동 이동 [doc] dx= dy=(DIP, 메인 창 문서 영역 좌표) [steps=10] → {status: Done|NotApplicable, reason, start, steps:[{index,bounds,snappedTo,ms}], final, maxMs}",
            a => MagnetJson(MagnetDriver.MoveBy(VisualTreeInspector.Main(), Doc(a), a.Num("dx") ?? 0, a.Num("dy") ?? 0, Steps(a))));
        r.Register("docs.resize",
            "Q8 자석 구동 크기 조절 [doc] edge=left|top|right|bottom d=(DIP, 끄는 가장자리 방향 +) [steps=10] → docs.move 와 같은 응답(끄는 가장자리만 흡착)",
            a =>
            {
                var edge = a.ReqStr("edge").ToLowerInvariant() switch
                {
                    "left" => MdiResizeDirection.Left,
                    "top" => MdiResizeDirection.Top,
                    "right" => MdiResizeDirection.Right,
                    "bottom" => MdiResizeDirection.Bottom,
                    var e => throw new RemoteCommandException($"edge 는 left|top|right|bottom 중 하나: {e}"),
                };
                return MagnetJson(MagnetDriver.ResizeBy(VisualTreeInspector.Main(), Doc(a), edge, a.Num("d") ?? 0, Steps(a)));
            });

        // ── Q4 테마 census(odev-3): 결과는 PerfProbe raw theme_census — 응답에 그대로 싣는다 ──
        r.Register("theme.census", "Q4 테마 census [themeId(생략=다음 테마로 순환)] → PerfProbe raw theme_census JSON", async a =>
        {
            await AutomationBridge.ThemeCensusAsync(VisualTreeInspector.Main(), a.Str("themeId"));
            return PerfRaw("theme_census");
        });

        // ── --cmd 명령 1개 실행(odev-3) — Android 처럼 시작 인자가 없는 곳에서 끄기 스위치 대조용 ──
        r.Register("cmd.run", "--cmd 명령 1개를 그대로 실행 command=<명령>(별칭 cmd=)(예 theme-census:pd-light, perf-reset, dirty-rects-on) → {ok, cmd}", async a =>
        {
            // command= 를 기본으로 받는다: 인자 이름 cmd 는 시험 도구 Remote.call(cmd, **args) 의 첫 매개변수와 겹쳐 키워드로 못 넘긴다(otest-m-1·odev-3 02:3x)
            var cmd = a.Str("command") ?? a.Str("cmd") ?? throw new RemoteCommandException("인자 'command'(또는 'cmd')가 필요합니다.");
            await AutomationBridge.RunCommandAsync(XkxkUi.Current.Shell ?? throw new RemoteNotReadyException("no-shell", "셸 명령 표면이 아직 없습니다."),
                VisualTreeInspector.Main(), cmd);
            return new JsonObject { ["ok"] = true, ["cmd"] = cmd };
        });
    }

    private static int Steps(JsonObject a) => Math.Clamp(a.Int("steps") ?? 10, 1, 200);

    private static JsonObject RectJson(Rect b) => new()
    {
        ["x"] = Math.Round(b.X, 1), ["y"] = Math.Round(b.Y, 1), ["w"] = Math.Round(b.Width, 1), ["h"] = Math.Round(b.Height, 1),
    };

    /// <summary>MagnetRun → 원격 응답. Ok=false(문서가 MDI·관리형 창 배치에 없음 — 데스크톱 SDI OS 창·탭 모드·패널 탭)는 NotApplicable(odev-2 지정).</summary>
    private static JsonObject MagnetJson(MagnetRun run) => new()
    {
        ["status"] = run.Ok ? "Done" : "NotApplicable",
        ["reason"] = run.Reason,
        ["start"] = RectJson(run.Start),
        ["steps"] = new JsonArray(run.Steps.Select(s => (JsonNode?)new JsonObject
        {
            ["index"] = s.Index, ["bounds"] = RectJson(s.Bounds),
            ["snappedTo"] = new JsonArray(s.SnappedTo.Select(t => (JsonNode?)JsonValue.Create(t)).ToArray()),
            ["ms"] = Math.Round(s.Ms, 3),
        }).ToArray()),
        ["final"] = RectJson(run.Final),
        ["maxMs"] = Math.Round(run.MaxMs, 3),
    };

    /// <summary>PerfProbe 계측 JSON 의 raw 항목 1개(없으면 null).</summary>
    private static JsonNode? PerfRaw(string key) =>
        JsonNode.Parse(PerfProbe.Current.ToJson(AutomationBridge.Platform)) is JsonObject o && o[key] is { } v ? v.DeepClone() : null;
}
#endif
