// --cmd perf-ui 시나리오(PERF-UI) — 문서 1개·20개 조건에서 UI 조작별 10회 반복해 조작 시작→화면 반영 ms 를 operations 표로 남긴다
#if PD_DIAGNOSTICS // 진단 빌드 전용
using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using xkxk.frameworks.ui.shell.Docking;
using xkxk.frameworks.ui.shell.Theming;
using xkxk.frameworks.ui.shell.Framework;

namespace xkxk.frameworks.ui.shell.Diagnostics;

/// <summary>perf-ui 시나리오 실행. AutomationCommandRunner 의 perf-ui·perf-ui:short 명령이 부른다.</summary>
internal static class PerfUiRunner
{
    private static readonly DocumentKind[] PerfUiKinds = { DocumentKind.Grid, DocumentKind.Gantt, DocumentKind.Form };

    /// <summary>--profile short(웹·모바일): 문서 1·5개 × 조작별 3회(목표 3분 이내). 기본 full(데스크톱): 1·20개 × 10회.</summary>
    internal static bool Short { get; set; }

    /// <summary>--reps N: 조작별 반복 수. 0(기본)이면 profile 기본값(full 10 / short 3) 유지.</summary>
    internal static int Reps { get; set; }

    /// <summary>단계 시작·종료 로그(데스크톱=호스트 --log 파일, 브라우저=console). 2초 넘는 조작은 SLOW 표시 — UI 스레드 정지 구간 추적용.</summary>
    private static void PerfLog(string msg) => Console.WriteLine($"[perf-ui] {DateTime.Now:HH:mm:ss.fff} {msg}");

    /// <summary>
    /// 조작 이름 = perfui.n{문서수}.{조작}. 측정 = 호출 시작 → (Background 지연 작업 소진) → 2프레임 렌더 완료.
    /// 셸이 배치를 Background 로 미루는 경우까지 포함하려고 TimeOperation 대신 직접 잰다.
    /// 끝나면 테마·폰트·글자 크기·배율·배치를 시작 상태로 되돌린다(설정이 저장되므로).
    /// </summary>
    internal static async Task RunPerfUiAsync(IShellCommands shell, TopLevel top)
    {
        var ui = XkxkUi.Current.Settings;
        var (theme, font, size) = (ThemeApplier.Active?.Id, ui.FontName, ui.BaseFontSize);
        var altFont = ui.FontNames.FirstOrDefault(f => f != font) ?? font;
        PerfProbe.Current.ResetOperations(); // 기동·설정 복원 등 앞선 기록이 표에 섞이지 않게
        var (counts, reps) = Short ? (new[] { 1, 5 }, 3) : (new[] { 1, 20 }, 10);
        if (Reps is > 0 and var customReps) { reps = customReps; } // --reps N 이 있으면 덮어쓴다(없으면 위 기본 10/3)
        counts = PerfUiSplit.FilterCounts(counts); // --perf-split-n=<n>(분해 계측 때만) 이면 그 문서 수만 돈다. 스위치가 없으면 그대로
        PerfProbe.Current.SetText("perfui_profile", Short ? "short" : "full");
        PerfLog($"begin profile={(Short ? "short" : "full")} counts={string.Join("/", counts)} reps={reps}");
        foreach (var n in counts)
        {
            PerfLog($"stage n={n} close-all");
            shell.CloseAll();
            await SettleAsync(top);
            PerfLog($"stage n={n} open {n} docs");
            for (var i = 0; i < n; i++) { await OpAsync(top, $"perfui.n{n}.open.initial", () => shell.OpenDocument(PerfUiKinds[i % 3])); }
            shell.Arrange(ShellArrange.Cascade);
            await SettleAsync(top);
            var p = $"perfui.n{n}.";
            for (var r = 0; r < reps; r++)
            {
                PerfLog($"stage n={n} rep {r + 1}/{reps}");
                await OpAsync(top, p + "font.up", ui.Increase);
                await OpAsync(top, p + "font.down", ui.Decrease);
                await OpAsync(top, p + "font.reset", ui.ResetFontSize);
                await OpAsync(top, p + "zoom.in", () => shell.ZoomIn());
                await OpAsync(top, p + "zoom.out", () => shell.ZoomOut());
                await OpAsync(top, p + "zoom.reset", () => shell.ResetZoom());
                await OpAsync(top, p + "theme.next", () => ThemeAutomation.Next());
                await OpAsync(top, p + "font.family", () => ui.FontName = r % 2 == 0 ? altFont : font);
                await OpAsync(top, p + "doc.open", () => shell.OpenDocument(DocumentKind.Form));
                await OpAsync(top, p + "doc.close", () => { if (shell.ActiveDocument is { } d) { shell.CloseDocument(d); } });
                await OpAsync(top, p + "activate.next", shell.ActivateNext);
                await OpAsync(top, p + "activate.prev", shell.ActivatePrevious);
                await OpAsync(top, p + "arrange.tile", () => shell.Arrange(ShellArrange.TileVertical));
                await OpAsync(top, p + "arrange.tabbed", () => shell.Arrange(ShellArrange.Tabbed));
                await OpAsync(top, p + "arrange.cascade", () => shell.Arrange(ShellArrange.Cascade));
                await OpAsync(top, p + "state.max", () => SetActiveState(shell, Dock.Model.Core.MdiWindowState.Maximized));
                await OpAsync(top, p + "state.restore", () => SetActiveState(shell, Dock.Model.Core.MdiWindowState.Normal));
                await OpAsync(top, p + "state.min", () => SetActiveState(shell, Dock.Model.Core.MdiWindowState.Minimized));
                await OpAsync(top, p + "state.restoreLast", () => shell.RestoreLastMinimized());
                await OpAsync(top, p + "sdi.float", () => { if (shell.ActiveDocument is { } d) { shell.Float(d); } });
                await OpAsync(top, p + "sdi.redock", () => { foreach (var d in shell.Documents.ToList()) { shell.Redock(d); } });
                await OpAsync(top, p + "layout.save", () => shell.SaveLayoutAsync());
                await DocOpsAsync(top, p, r);
            }

            PerfLog($"stage n={n} layout.load");
            await OpAsync(top, p + "layout.load", () => shell.LoadLayoutAsync()); // 전체 재생성이라 1회만
        }

        PerfLog("restore settings");

        if (theme is not null) { ThemeAutomation.Select(theme); }
        ui.FontName = font;
        ui.BaseFontSize = size;
        shell.ResetZoom();
        shell.Arrange(ShellArrange.Cascade);
        await SettleAsync(top);
        PerfLog("end");
    }

    /// <summary>문서 내부 조작: 그리드 정렬·스크롤, 간트 줌·스크롤(열린 첫 뷰 대상).</summary>
    private static async Task DocOpsAsync(TopLevel top, string p, int r)
    {
        if (AutomationCommandRunner.FindView<IGridDocumentDiagnostics>(top) is { } g)
        {
            await OpAsync(top, p + "grid.sort", () => g.DiagSortByName(r % 2 == 1));
            await OpAsync(top, p + "grid.scroll", () => g.DiagScrollStep(r));
        }

        if (AutomationCommandRunner.FindView<IGanttDocumentDiagnostics>(top) is { } t)
        {
            await OpAsync(top, p + "gantt.zoom", () => t.DiagZoomStep(r));
            await OpAsync(top, p + "gantt.scroll", () => t.DiagScrollStep());
        }
    }

    private static void SetActiveState(IShellCommands shell, Dock.Model.Core.MdiWindowState state)
    {
        if (shell.ActiveDocument is { } d) { shell.SetState(d, state); }
    }

    private static Task OpAsync(TopLevel top, string name, Action action) => OpAsync(top, name, () => { action(); return Task.CompletedTask; });

    private static Task OpAsync(TopLevel top, string name, Func<object?> action) => OpAsync(top, name, () => { action(); return Task.CompletedTask; });

    /// <summary>조작 1회: 동기 시간(sync)과 화면 반영까지(frame)를 RecordOperation 으로 기록. 예외는 이름.error 로 세고 계속한다.</summary>
    private static async Task OpAsync(TopLevel top, string name, Func<Task> action)
    {
        var perf = PerfProbe.Current;
        if (PerfUiSplit.Enabled) // 원인 분해 계측(--perf-split 일 때만, PerfUiSplit.cs). 스위치가 없으면 아래 기본 경로 그대로
        {
            await PerfUiSplit.RunAsync(top, name, action, (n, ex) => { perf.RecordOperation(n + ".error", 0, 0); PerfLog($"error {n} {ex.GetType().Name}: {ex.Message}"); });
            return;
        }

        var start = perf.NowMs;
        PerfLog("start " + name);
        try { await action(); }
        catch (Exception ex) { perf.RecordOperation(name + ".error", 0, 0); PerfLog($"error {name} {ex.GetType().Name}: {ex.Message}"); }
        var sync = perf.NowMs - start;
        await SettleAsync(top);
        var frame = perf.NowMs - start;
        perf.RecordOperation(name, sync, frame);
        PerfLog($"end {name} sync={sync:0}ms frame={frame:0}ms{(frame > 2000 ? " SLOW" : "")}");
    }

    private static async Task SettleAsync(TopLevel top)
    {
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        await AutomationBridge.WaitFramesAsync(top, 2);
    }
}
#endif
