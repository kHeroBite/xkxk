// 원격 dock.trace(진단) — 테마 전환 중 Dock 분할 패널의 배치 크기·각 칸 비율·그리드 크기를 프레임마다 기록해 Q4 가설 H-a(입력 폭 반올림)·H-b(비율 진동)를 가른다(lead 승인). grid.mtrace — 그리드 측정 호출 단위 기록(원인 확정용)
#if PD_DIAGNOSTICS // 진단 빌드 전용(PdDiagnostics=true)
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using xkxk.frameworks.ui.shell.Theming;
using DockPanelHost = Dock.Controls.ProportionalStackPanel.ProportionalStackPanel;

namespace xkxk.frameworks.ui.shell.Diagnostics.Remote;

internal static class RemoteDockTraceCommands
{
    /// <summary>RemoteScrollCommands.Register 가 부른다(라우터 변경 없이 등록).</summary>
    internal static void Register(RemoteCommandRouter r)
    {
        r.Register("dock.trace",
            "Q4 판별: action=theme-next(기본)|none, frames=<n>(기본 8) — 동작 전 1회 + 동작 뒤 프레임마다 스냅숏 " +
            "[{frame, scaling, panels:[{orientation, w, h, children:[{id, type, proportion(R), w, h}]}], grids:[{w, h, measureGiven}]}]. " +
            "proportion 이 고정인데 폭만 오가면 H-a(입력 폭 반올림), proportion 이 오가면 H-b(비율 재기록 진동)", async a =>
            {
                var top = VisualTreeInspector.Main();
                var frames = System.Math.Clamp(a.Int("frames") ?? 8, 1, 60);
                var shots = new JsonArray { Snapshot(top, -1) };
                var action = (a.Str("action") ?? "theme-next").ToLowerInvariant();
                if (action == "theme-next")
                {
                    ThemeAutomation.Next(); // theme.census 와 같은 경로(ThemeAutomation)
                }

                for (var i = 0; i < frames; i++)
                {
                    shots.Add(Snapshot(top, i)); // 동기 직후(i=0)부터 프레임마다
                    await AutomationBridge.WaitFramesAsync(top, 1);
                }

                shots.Add(Snapshot(top, frames));
                return new JsonObject { ["action"] = action, ["shots"] = shots };
            });

        r.Register("grid.mtrace",
            "Q4 원인 확정: 그리드 측정 호출 단위 기록. action=theme-next(기본: 기록 비우고 전환 1회 뒤 frames 만큼 대기)|get|clear, frames=<n>(기본 8), limit=<n>(기본 120). " +
            "calls:[{seq, pass, inPass, t, raw, stack, chain}] — stack 은 측정 계열 호출 프레임(바깥이 오른쪽), chain 은 조상 '형식#이름=직전측정제약폭/Bounds폭'. " +
            "byWidth 는 받은 폭별 횟수, byStack 은 (폭, 스택) 묶음별 횟수", async a =>
            {
                var top = VisualTreeInspector.Main();
                var action = (a.Str("action") ?? "theme-next").ToLowerInvariant();
                if (action is "clear" or "theme-next") { GridMeasureTrace.Clear(); }
                if (action == "theme-next")
                {
                    ThemeAutomation.Next();
                    await AutomationBridge.WaitFramesAsync(top, System.Math.Clamp(a.Int("frames") ?? 8, 1, 60));
                }

                var all = GridMeasureTrace.Calls();
                var calls = new JsonArray();
                foreach (var c in all.Take(System.Math.Clamp(a.Int("limit") ?? 120, 0, 400)))
                {
                    calls.Add(new JsonObject
                    {
                        ["seq"] = c.Seq, ["pass"] = c.Pass, ["inPass"] = c.InPass, ["t"] = R(System.Math.Round(c.TimeMs, 2)),
                        ["raw"] = $"{R(c.RawW)}x{R(c.RawH)}", ["stack"] = c.Stack, ["chain"] = c.Chain,
                    });
                }

                var byWidth = new JsonObject();
                foreach (var g in all.GroupBy(c => R(c.RawW))) { byWidth[g.Key] = g.Count(); }
                var byStack = new JsonArray();
                foreach (var g in all.GroupBy(c => (W: R(c.RawW), c.Stack)).OrderByDescending(g => g.Count()))
                {
                    byStack.Add(new JsonObject { ["w"] = g.Key.W, ["count"] = g.Count(), ["stack"] = g.Key.Stack, ["chain"] = g.First().Chain });
                }

                return new JsonObject
                {
                    ["action"] = action, ["total"] = all.Count, ["passes"] = all.Select(c => c.Pass).Distinct().Count(),
                    ["byWidth"] = byWidth, ["byStack"] = byStack, ["calls"] = calls,
                };
            });
    }

    private static string R(double v) => v.ToString("R", CultureInfo.InvariantCulture);

    /// <summary>한 시점 스냅숏. 분할 패널은 시각 트리의 Dock ProportionalStackPanel 전부(깊이 순), 그리드는 문서 그리드 전부.</summary>
    private static JsonObject Snapshot(TopLevel top, int frame)
    {
        var panels = new JsonArray();
        foreach (var p in top.GetVisualDescendants().OfType<DockPanelHost>().Where(p => p.IsEffectivelyVisible))
        {
            var children = new JsonArray();
            foreach (var c in p.Children.OfType<Control>())
            {
                children.Add(new JsonObject
                {
                    ["id"] = (c.DataContext as Dock.Model.Core.IDockable)?.Id,
                    ["type"] = c.GetType().Name,
                    ["proportion"] = R(DockPanelHost.GetProportion(c)),
                    ["w"] = R(c.Bounds.Width),
                    ["h"] = R(c.Bounds.Height),
                });
            }

            panels.Add(new JsonObject
            {
                ["orientation"] = p.Orientation.ToString(),
                ["w"] = R(p.Bounds.Width),
                ["h"] = R(p.Bounds.Height),
                ["children"] = children,
            });
        }

        var grids = new JsonArray();
        foreach (var g in top.GetVisualDescendants().Where(v => v is IGridDocumentDiagnostics))
        {
            var given = ((IGridDocumentDiagnostics)g).DiagLastMeasureGiven;
            grids.Add(new JsonObject
            {
                ["w"] = R(g.Bounds.Width),
                ["h"] = R(g.Bounds.Height),
                ["measureGiven"] = $"{R(given.Width)}x{R(given.Height)}",
            });
        }

        return new JsonObject { ["frame"] = frame, ["scaling"] = R(top.RenderScaling), ["panels"] = panels, ["grids"] = grids };
    }
}
#endif
