// AutomationBridge 의 --cmd 명령 실행부(창 명령·성능 시험·테마/글자 크기)와 UI 설정 적용·상태 기록
#if PD_DIAGNOSTICS // 진단 빌드 전용 — 제품 빌드에는 명령 문자열·실행 본문이 들어가지 않는다
using xkxk.frameworks.ui.data.Samples;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Dock.Model.Core;
using xkxk.frameworks.ui.shell.Docking;
using xkxk.frameworks.ui.shell.Theming;
using xkxk.frameworks.ui.shell.Framework;

namespace xkxk.frameworks.ui.shell.Diagnostics;

/// <summary>--cmd 명령 1개 실행과 --font-size·--theme 등 UI 인자 적용, perf JSON 상태 기록. 공개 진입점은 AutomationBridge.RunCommandAsync 다.</summary>
internal static class AutomationCommandRunner
{
    /// <summary>--sdi 로 마지막에 분리한 문서(pin·unpin 대상 우선). AutomationBridge.FloatDocuments 가 채운다.</summary>
    internal static PocDocument? LastFloated { get; set; }

    /// <summary>--cmd 1개 실행. *-exercise 는 화면에 붙은 첫 그리드/간트 문서 뷰를 찾아 성능 시험을 돌린다.</summary>
    internal static async Task RunCmdAsync(IShellCommands shell, TopLevel top, string cmd)
    {
        var docs = new List<PocDocument>(shell.Documents);
        var lower = cmd.ToLowerInvariant();
        if (await ThemeCensusRunner.TryRunCensusAsync(top, cmd, lower)) { return; } // theme-census·dirty-rects(ThemeCensusRunner.cs)

        // 색 테마 명령(theme:<id>·theme-next·theme-clone·theme-set·theme-save·theme-delete·theme-export·theme-import·theme-open-editor)은
        // odev-5t 의 ThemeAutomation 으로 넘긴다. 라이트/다크 모드 전환(theme-dark·theme-light)만 이 파일이 처리한다.
        if (lower is not ("theme-dark" or "theme-light") && ThemeAutomation.TryRun(cmd, out var tok, out var tmsg))
        {
            RecordThemeCmd(cmd, tok, tmsg);
            return;
        }

        switch (lower)
        {
            case "close-others": if ((shell.ActiveDocument ?? (docs.Count > 0 ? docs[^1] : null)) is { } keep) { shell.CloseOthers(keep); } break;
            case "close-all": shell.CloseAll(); break;
            case "minimize-all": shell.MinimizeAll(); break;
            case "restore-all": shell.RestoreAll(); break;
            case "next": shell.ActivateNext(); break;
            case "prev": shell.ActivatePrevious(); break;
            case "redock-all": docs.ForEach(shell.Redock); break;
            case "grid-exercise": if (FindView<IGridDocumentDiagnostics>(top) is { } g) { await g.ExerciseAsync(top); } break;
            case "gantt-exercise": if (FindView<IGanttDocumentDiagnostics>(top) is { } t) { await t.ExerciseAsync(top); } break;
            case "grid-hash": // 그리드 1만 행 전체 값 해시(lazy·eager 동등성) + 예전 알고리즘 해시. 부르면 모든 행이 채워진다(state 로 표시)
                if (FindView<IGridDocumentDiagnostics>(top) is { } gh)
                {
                    var (hash, was) = gh.DiagComputeHash();
                    var legacy = GridSeedLegacy.Hash(gh.DiagRowCount);
                    PerfProbe.Current.SetText("grid_hash", hash);
                    PerfProbe.Current.SetText("grid_hash_legacy", legacy);
                    PerfProbe.Current.SetText("grid_hash_state", was ? "already-materialized" : "materialized-by-hash");
                    PerfProbe.Current.SetMetric("grid_hash_equal", hash == legacy ? 1 : 0);
                }

                break;
            case "perf-ui": await PerfUiRunner.RunPerfUiAsync(shell, top); break; // PerfUiRunner.cs(--profile short|full)
            case "perf-ui:short": PerfUiRunner.Short = true; await PerfUiRunner.RunPerfUiAsync(shell, top); break; // 웹·모바일용 짧은 모드
            case "perf-reset": PerfProbe.Current.ResetOperations(); break; // 측정 전 조작 기록 초기화
            case "fluent-census": ThemeApplier.FluentSyncCensus(); break; // M4(odev-7c): Fluent 사전 값을 모두 만들어 perf text fluent_census 에 직접 쓴다 — 측정 구간 밖(측정 뒤)에서만
            case "grid-reopen": // 그리드 열기 회귀 판별: 모두 닫고 그리드를 다시 연다(두 번째 열기 값 = grid.open.*.next)
                shell.CloseAll();
                await AutomationBridge.WaitFramesAsync(top, 2);
                shell.OpenDocument(LegacyWindowKinds.Grid);
                await AutomationBridge.WaitFramesAsync(top, 4);
                break;
            case "chrome-sdi" or "chrome-pin" or "chrome-lock" or "chrome-sync" or "chrome-dup" or "chrome-cache" or "chrome-refresh" or "chrome-help": ChromeCmd(shell, lower); break; // UI-09 제목줄 버튼
            case "maximize": if (Target(shell, false) is { } mx) { shell.SetState(mx, MdiWindowState.Maximized); } break;
            case "restore-max": if (Target(shell, false) is { } rs) { shell.SetState(rs, MdiWindowState.Normal); } break;
            case "pin" or "unpin": if (Target(shell, true) is { } pd) { SetPin(shell, pd, cmd == "pin"); } break;
            case "tray-on" or "tray-off": if (xkxk.frameworks.ui.shell.Platform.ShellPlatform.Menu is { } tm) { tm.RemoteOverride = cmd == "tray-on"; PerfProbe.Current.SetMetric("tray_after_" + cmd, tm.Visible ? 1 : 0); } break; // 이번 실행 한정(저장 안 함 — 시험 인자가 settings.json 을 바꾸지 않게)
            case "esc-close": // Esc KeyBinding 과 같은 EscCloseCommand 경로. 시험 동안만 옵션을 켠다.
                if (XkxkUi.Current.MainViewModel is { } ev) { var prev = ev.WindowMenu.EscCloseEnabled; ev.WindowMenu.EscCloseEnabled = true; ev.EscCloseCommand.Execute(null); ev.WindowMenu.EscCloseEnabled = prev; }
                break;
            case "dblclick-close": // MainView.OnTabDoubleTapped 와 같은 조건·동작(옵션 켜짐 → CloseDocument)
                if (XkxkUi.Current.MainViewModel is { DoubleClickCloseEnabled: true } && Target(shell, false) is { } dc) { shell.CloseDocument(dc); }
                break;
            case "theme-dark": XkxkUi.Current.Settings.Theme = ThemeMode.Dark; break;
            case "theme-light": XkxkUi.Current.Settings.Theme = ThemeMode.Light; break;
            case "font-up": XkxkUi.Current.Settings.Increase(); break; // Ctrl+휠 위·Ctrl++ 와 같은 경로
            case "font-down": XkxkUi.Current.Settings.Decrease(); break;
            case "font-reset": XkxkUi.Current.Settings.ResetFontSize(); break; // Ctrl+0
            case "wheel-up": WheelStep(shell, top, 1); break; // 순수 Ctrl+휠 위와 같은 경로(SettingsInput.Step)
            case "wheel-down": WheelStep(shell, top, -1); break;
            case "reset-font": WheelStep(shell, top, 0); break; // Ctrl+0
            case "zoom-in": shell.ZoomIn(); break;
            case "zoom-out": shell.ZoomOut(); break;
            case "zoom-reset": shell.ResetZoom(); break;
            case "zoom-mode-font": shell.WindowZoomMode = false; break;
            case "zoom-mode-window": shell.WindowZoomMode = true; break;
            default: throw new ArgumentException("알 수 없는 --cmd 값: " + cmd);
        }

        if (lower.StartsWith("theme-", StringComparison.Ordinal) || lower.StartsWith("font-", StringComparison.Ordinal)
            || lower.StartsWith("wheel-", StringComparison.Ordinal) || lower.StartsWith("zoom-", StringComparison.Ordinal) || lower == "reset-font")
        {
            PerfProbe.Current.SetMetric("font_after_" + cmd, XkxkUi.Current.Settings.BaseFontSize);
            PerfProbe.Current.SetText("theme_after_" + cmd, ThemeApplier.Active?.Id ?? CurrentThemeName());
            PerfProbe.Current.SetMetric("zoom_after_" + cmd, Target(shell, false)?.Zoom ?? 1);
        }
    }

    /// <summary>
    /// 실입력과 같은 경로: SettingsInput.Step(원천 시각 요소, delta). 원천은 활성 문서의 래퍼(ZoomDecorator, Tag=문서),
    /// 없으면 첫 문서 래퍼다. 문서가 없으면 원천이 메인 창(top)이 되어 셸 규칙대로 무시된다(handled=false).
    /// 결과 wheel_handled_&lt;n&gt; = 1/0.
    /// </summary>
    private static void WheelStep(IShellCommands shell, TopLevel top, int delta)
    {
        Avalonia.Visual source = top;
        foreach (var v in top.GetVisualDescendants().Where(x => x is IDocumentZoomHost))
        {
            if (ReferenceEquals(((IDocumentZoomHost)v).Tag, shell.ActiveDocument)) { source = v; break; }
            if (ReferenceEquals(source, top)) { source = v; }
        }

        var handled = xkxk.frameworks.ui.shell.Input.SettingsInput.Step(source, delta);
        PerfProbe.Current.SetMetric($"wheel_handled_{++_wheelSeq}", handled ? 1 : 0);
    }

    private static int _wheelSeq;

    /// <summary>
    /// UI-09 제목줄 버튼(odev-7c, WindowCommands.Chrome.cs)과 같은 메서드를 활성 문서(Target 규칙)에 호출한다.
    /// 결과: chrome_after_&lt;명령&gt; = "sdi=..,pin=..,dup=..,cache=..", 도움말은 chrome_help.
    /// </summary>
    private static void ChromeCmd(IShellCommands shell, string cmd)
    {
        if (shell is not WindowCommands wc || Target(shell, false) is not { } d)
        {
            PerfProbe.Current.SetText("chrome_after_" + cmd, "대상 없음");
            return;
        }

        switch (cmd)
        {
            case "chrome-sdi": wc.ChromeToggleSdi(d); break;
            case "chrome-pin": wc.ChromeTogglePin(d); break;
            case "chrome-lock": wc.ChromeToggleLock(d); break; // 위치 고정
            case "chrome-sync": wc.ChromeToggleSync(d); break;
            case "chrome-dup": wc.ChromeToggleDuplicate(d); break;
            case "chrome-cache": wc.ChromeToggleCache(d); break; // 내부에서 새로고침 → 문서가 새 객체로 바뀐다
            case "chrome-refresh": d = wc.ChromeRefresh(d); break; // 닫고 같은 위치·상태로 다시 연 새 문서
            case "chrome-help": PerfProbe.Current.SetText("chrome_help", wc.ChromeHelpText(d)); break;
        }

        if (!shell.Documents.Contains(d) && Target(shell, false) is { } nd) { d = nd; } // 새로고침으로 바뀐 문서를 다시 잡는다
        var s = WindowCommands.ChromeOf(d);
        PerfProbe.Current.SetText("chrome_after_" + cmd,
            $"sdi={wc.IsFloating(d)},pin={s.IsPinned},lock={s.IsLocked},sync={s.SyncEnabled},dup={s.AllowDuplicate},cache={s.CacheHits}/{s.CacheTotal}");
    }

    /// <summary>--font-size·--font·--theme·--zoom 적용(설정·셸 명령 경유).</summary>
    internal static void ApplyUi(IShellCommands shell, AutomationOptions o, bool afterOpen)
    {
        if (!afterOpen)
        {
            if (o.Shadow is { } sh) { WindowCommands.ShadowEnabled = sh; } // UI-10 그림자 on/off(PERF-UI 비교)
            if (o.Frame is { } fr) { WindowCommands.MarsFrame = fr == "mars"; } // UI-10 외곽선 mars|pd
            // 리소스 키를 지우고 테마를 재적용하므로 측정 전 1회만(ui-settings 단계) — 생략값 on(odev-7c)
            if (o.FluentOverride is not null || o.ThemeNewTokens is not null) { ThemeApplier.SetDiagnosticOverrides(o.FluentOverride ?? true, o.ThemeNewTokens ?? true); }
            if (o.FluentPalette is { } fp && !ThemeApplier.SetFluentPaletteMode(fp)) { PerfProbe.Current.SetText("fluent_palette_error", fp); } // M3·M4 — 1회 호출, 기록은 theme_overrides 에 자동 포함(odev-7c). 모르는 값이면 오류 기록
            if (o.ChromeButtons is { } cb && !WindowCommands.SetChromeButtonsMode(cb)) { PerfProbe.Current.SetText("chrome_buttons_error", cb); } // odev-7c 제목줄 분리 스위치
            if (o.ChromeInactiveDim is { } dim && !WindowCommands.SetInactiveDimMode(dim)) { PerfProbe.Current.SetText("chrome_diag_error", "dim=" + dim); }
            if (o.ChromeInactiveOpacity is { } op && !WindowCommands.SetInactiveOpacity(op)) { PerfProbe.Current.SetText("chrome_diag_error", "inactiveOpacity=" + op); }
            ApplyFont(o.FontSize, o.Font);
            if (o.Theme is { } id) { var (ok, msg) = ThemeAutomation.Select(id); RecordThemeCmd("theme:" + id, ok, msg); PerfProbe.Current.SetMetric("theme_selected", ok ? 1 : 0); }
        }
        else if (o.Zoom is { } pct && Target(shell, false) is { } d)
        {
            shell.SetDocumentZoom(d, pct / 100.0);
        }
    }

    /// <summary>--font-size / --font 적용(설정 서비스 경유 → 모든 화면 토큰 즉시 갱신).</summary>
    private static void ApplyFont(double? size, string? font)
    {
        if (size is { } s) { XkxkUi.Current.Settings.BaseFontSize = s; }
        if (!string.IsNullOrWhiteSpace(font)) { XkxkUi.Current.Settings.FontName = font; }
    }

    /// <summary>현재 테마·기준 글자 크기·폰트를 perf JSON 에 기록.</summary>
    private static int _themeCmdSeq;

    /// <summary>테마 명령 결과를 theme_cmd_&lt;순번&gt; = "ok|fail 명령 — 메시지" 로 기록(실패 사유 포함), 실패 수는 theme_cmd_fail.</summary>
    private static void RecordThemeCmd(string cmd, bool ok, string msg)
    {
        PerfProbe.Current.SetText($"theme_cmd_{++_themeCmdSeq}", $"{(ok ? "ok" : "fail")} {cmd} — {msg}");
        if (!ok) { PerfProbe.Current.SetMetric("theme_cmd_fail", ++_themeCmdFail); }
    }

    private static int _themeCmdFail;

    internal static void RecordUi(IShellCommands shell)
    {
        PerfProbe.Current.SetText("theme_variant", CurrentThemeName());
        PerfProbe.Current.SetRawJson("theme", ThemeAutomation.DescribeJson()); // activeThemeId·mode·builtinThemes·userThemes 등(odev-5t)
        // 실제 채택 렌더링 백엔드(세 플랫폼 공통 — 웹·Android 는 이전에 기록이 없었다) + GPU 가속모드 설정·출처
        var gfx = Rendering.RenderInfo.GraphicsTypeName();
        PerfProbe.Current.SetText("render_graphics", gfx);
        PerfProbe.Current.SetText("render_kind", Rendering.RenderInfo.ActiveKind); // 웹은 호스트 판정(캔버스 컨텍스트) 우선
        PerfProbe.Current.SetText("gpu_mode", $"{xkxk.frameworks.ui.data.Rendering.GpuSetting.Applied}/{xkxk.frameworks.ui.data.Rendering.GpuSetting.AppliedSource}");
        PerfProbe.Current.SetText("theme_id", ThemeApplier.Active?.Id ?? "");
        PerfProbe.Current.SetText("theme_setting", XkxkUi.Current.Settings.Theme.ToString());
        PerfProbe.Current.SetMetric("base_font_size", XkxkUi.Current.Settings.BaseFontSize);
        PerfProbe.Current.SetText("font_family", XkxkUi.Current.Settings.FontName);
        PerfProbe.Current.SetText("wheel_mode", XkxkUi.Current.Settings.WheelMode.ToString());
        PerfProbe.Current.SetMetric("shadow", WindowCommands.ShadowEnabled ? 1 : 0);
        PerfProbe.Current.SetText("frame", WindowCommands.MarsFrame ? "mars" : "pd");
        PerfProbe.Current.SetText("chrome_buttons", WindowCommands.ChromeButtonsMode); // full|min(odev-7c 진단 스위치)
        PerfProbe.Current.SetText("chrome_diag", WindowCommands.ChromeDiagState); // buttons=…;dim=…;inactiveOpacity=…(odev-7c, Q5 적용 확인 기준)
        PerfProbe.Current.SetText("theme_overrides", ThemeApplier.DiagnosticOverrides); // fluent=on|off;newTokens=on|off(odev-7c)
        PerfProbe.Current.SetMetric("notify_panel", DockFactoryDiag.NotificationPanelDisabled ? 0 : 1); // 1=알림 패널 있음, 0=--notify-panel off(odev-1)
        PerfProbe.Current.SetMetric("notify_content", DockFactoryDiag.NotificationContentEmpty ? 0 : 1); // 0=--notify-panel empty(틀만, 내용 없음)
        PerfProbe.Current.SetText("notify_view", NotificationPanelProbe.LayoutMode ?? "none"); // Dock 패널에 실제로 붙은 알림 화면의 방식 new|none(odev-1) — 위 둘은 인자 판독값
        PerfProbe.Current.SetMetric("notify_view_instances", NotificationPanelProbe.CreatedCount); // 지금까지 만들어진 알림 화면 수(odev-1)
        PerfProbe.Current.SetMetric("notify_view_attached", NotificationPanelProbe.AttachedCount); // Dock 알림 패널에 붙어 있는 수(정상 1, C·E 0)
        // 현재 레이아웃(Documents[].ZOrder·ActiveId 포함, odev-2) — 저장/복원 실행 결과를 비교해 Z 순서 복원을 기계 판정
        PerfProbe.Current.SetRawJson("layout", shell.SerializeLayout());
        // 창별 배율: zooms = "Grid-1=1.2;Form-1=1" (문서 Id 기준)
        PerfProbe.Current.SetText("zooms", string.Join(";", shell.Documents.Select(d => $"{d.Id}={d.Zoom:0.##}")));
    }

    /// <summary>실제 적용 중인 테마(light|dark). 설정이 System 이어도 실제 값을 낸다.</summary>
    private static string CurrentThemeName() =>
        Avalonia.Application.Current?.ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark ? "dark" : "light";

    /// <summary>명령 대상: 활성 문서(없으면 마지막 문서). preferFloating 이면 --sdi 로 마지막 분리한 문서를 우선한다.</summary>
    private static PocDocument? Target(IShellCommands shell, bool preferFloating)
    {
        var docs = shell.Documents;
        if (preferFloating && LastFloated is { } f && docs.Contains(f)) { return f; }
        return shell.ActiveDocument ?? (docs.Count > 0 ? docs[^1] : null);
    }

    /// <summary>TogglePin 으로 원하는 핀 상태에 맞춘다. 결과(pinned 0/1)를 기록 — 도킹 문서는 항상 0.</summary>
    private static void SetPin(IShellCommands shell, PocDocument d, bool on)
    {
        var state = shell.TogglePin(d);
        if (state != on) { state = shell.TogglePin(d); }
        PerfProbe.Current.SetMetric(on ? "pinned_after_pin" : "pinned_after_unpin", state ? 1 : 0);
    }

    internal static T? FindView<T>(TopLevel top) where T : class
    {
        foreach (var v in top.GetVisualDescendants()) { if (v is T t) { return t; } }
        return null;
    }
}
#endif
