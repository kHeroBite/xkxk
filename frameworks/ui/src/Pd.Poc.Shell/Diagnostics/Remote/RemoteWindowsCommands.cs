// 원격 명령(창·문서·레이아웃·스크린샷·성능·로그·상태) — IShellCommands·AutomationBridge·PerfProbe 를 그대로 호출
#if PD_DIAGNOSTICS // 진단 빌드 전용(PdDiagnostics=true)
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Dock.Model.Core;
using Pd.Poc.Shell.Docking;
using Pd.Poc.Shell.Theming;

namespace Pd.Poc.Shell.Diagnostics.Remote;

/// <summary>창·문서·레이아웃·스크린샷·성능·로그·상태 원격 명령 군(옛 RemoteCommands.Windows). RemoteCommandRouter 가 Register 를 부른다.</summary>
internal static partial class RemoteWindowsCommands
{
    // 공용 멤버 전달자(W2) — 본문은 옛 이름 그대로 쓴다.
    private static IShellCommands Shell => RemoteShellAccess.Shell;

    private static PocDocument Doc(JsonObject a, string key = "doc") => RemoteShellAccess.Doc(a, key);

    private static JsonObject DocJson(PocDocument d) => RemoteShellAccess.DocJson(d);

    private static bool IsFloating(PocDocument d, int windowIndex) => RemoteShellAccess.IsFloating(d, windowIndex);

    private static JsonArray DocsJson() => RemoteShellAccess.DocsJson();

    private static JsonObject DocJsonZ(PocDocument d) => RemoteSdiZ.DocJsonZ(d);

    private static JsonArray DocsJsonZ(IEnumerable<PocDocument> docs) => RemoteSdiZ.DocsJsonZ(docs);

    private static JsonObject? ManagedBounds(PocDocument d) => RemoteSdiZ.ManagedBounds(d);

    private static JsonObject? DockWindowJson(PocDocument d) => RemoteSdiZ.DockWindowJson(d);

    internal static void Register(RemoteCommandRouter r)
    {
        RemoteRenderOverlayCommands.Register(r); // render.overlay(다시 그린 영역 표시, D-1) — 라우터를 바꾸지 않으려고 여기서 함께 등록한다
        r.Register("status", "앱 상태 요약(pid·플랫폼·문서 수·활성 문서·테마·글자 크기·서버 포트)", _ => new JsonObject
        {
            ["pid"] = Environment.ProcessId, ["platform"] = AutomationBridge.Platform,
            ["windows"] = VisualTreeInspector.TopLevels().Count, ["docCount"] = Shell.Documents.Count,
            ["active"] = Shell.ActiveDocument is { } ad ? DocJson(ad) : null,
            ["theme"] = UiSettings.Current.Theme.ToString(), ["baseFontSize"] = UiSettings.Current.BaseFontSize,
            ["font"] = UiSettings.Current.FontName, ["wheelMode"] = UiSettings.Current.WheelMode.ToString(),
            ["remotePort"] = RemoteControlServer.Current?.Port,
        });
        r.Register("ping", "생존 확인", _ => JsonValue.Create("pong"));
        r.Register("windows.list", "최상위 창 목록(0=메인, 1~=SDI 실 창) + 관리형 SDI 창(managed=true, index=null — 브라우저·Android): 제목·활성·위치·크기·상태·포함 문서", _ => WindowsJson());
        r.Register("docs.list", "열린 문서 목록(열린 순서): id·title·kind·active·state·window·floating·zoom·zIndex·zScale·zSource — zIndex 는 zScale=mdi 면 MDI 레이어 화면 쌓임, zScale=sdi 면 SDI 창끼리 순위(0=맨 아래, zSource=os|visual|model). 두 척도는 서로 비교하지 않는다", _ => DocsJson());
        r.Register("docs.open", "문서 열기 kind=grid|gantt|form [title] [sdi] [allowDuplicate] [count]", a =>
        {
            var kind = a.EnumArg<DocumentKind>("kind") ?? throw new RemoteCommandException("kind=grid|gantt|form 이 필요합니다.");
            var opened = new List<PocDocument>();
            for (var i = 0; i < Math.Max(1, a.Int("count") ?? 1); i++)
            {
                opened.Add(Shell.OpenDocument(kind, a.Str("title"), a.Bool("sdi"), a.Bool("allowDuplicate")));
            }

            using (PerfProbe.Current.TimeOperation("docs.open.json")) { return DocsJsonZ(opened); } // SDI 순위까지(한 번의 목록 계산), 응답 비용 계측(K15)
        });
        r.Register("docs.close", "문서 닫기 [doc] | all=true | others=true(doc 만 남김)", a =>
        {
            if (a.Bool("all")) { Shell.CloseAll(); }
            else if (a.Bool("others")) { Shell.CloseOthers(Doc(a)); }
            else { Shell.CloseDocument(Doc(a)); }
            return DocsJson();
        });
        r.Register("docs.activate", "문서 활성화 [doc] | dir=next|prev(MRU 순환)", a =>
        {
            switch (a.Str("dir"))
            {
                case "next": Shell.ActivateNext(); break;
                case "prev": Shell.ActivatePrevious(); break;
                default: Shell.Activate(Doc(a)); break;
            }

            return Shell.ActiveDocument is { } d ? DocJsonZ(d) : null; // SDI 순위 포함(odev-4b 03:5x)
        });
        r.Register("docs.arrange", "배치 mode=cascade|tile-h|tile-v|tabbed | preset=MdiCascade|Tabbed|MdiTileVertical", a =>
        {
            if (a.EnumArg<LayoutPreset>("preset") is { } p) { Shell.ApplyPreset(p); }
            else { Shell.Arrange(ToArrange(a.Str("mode") ?? "cascade")); }
            return DocsJson();
        });
        r.Register("docs.sdi", "SDI 전환 [doc] [on=true(분리)|false(재결합), 생략=토글]", a =>
        {
            var d = Doc(a);
            if (!a.ContainsKey("on")) { Shell.ToggleSdi(d); }
            else if (a.Bool("on")) { Shell.Float(d); }
            else { Shell.Redock(d); }
            return DocJsonZ(d);
        });
        r.Register("docs.redock", "SDI 재결합 [doc] | all=true", a =>
        {
            foreach (var d in a.Bool("all") ? Shell.Documents.ToList() : new List<PocDocument> { Doc(a) }) { Shell.Redock(d); }
            return DocsJson();
        });
        r.Register("docs.state", "창 상태 state=minimized|maximized|normal [doc] | all=minimize|restore | restoreLast=true", a =>
        {
            switch (a.Str("all"))
            {
                case "minimize": Shell.MinimizeAll(); return DocsJson();
                case "restore": Shell.RestoreAll(); return DocsJson();
            }

            if (a.Bool("restoreLast")) { return JsonValue.Create(Shell.RestoreLastMinimized()); }
            var d = Doc(a);
            Shell.SetState(d, a.EnumArg<MdiWindowState>("state") ?? throw new RemoteCommandException("state=minimized|maximized|normal"));
            return DocJsonZ(d);
        });
        r.Register("docs.rename", "제목 변경 [doc] title=", a => { var d = Doc(a); Shell.Rename(d, a.ReqStr("title")); return DocJsonZ(d); });
        r.Register("docs.pin", "SDI 항상 위 토글 [doc] → pinned", a => JsonValue.Create(Shell.TogglePin(Doc(a))));
        r.Register("docs.zoom", "창 배율(UI-06) [doc] zoom=0.5~2.0 | step=in|out|reset", a =>
        {
            var d = Doc(a);
            switch (a.Str("step"))
            {
                case "in": Shell.ZoomIn(d); break;
                case "out": Shell.ZoomOut(d); break;
                case "reset": Shell.ResetZoom(d); break;
                default: Shell.SetDocumentZoom(d, a.Num("zoom") ?? throw new RemoteCommandException("zoom 또는 step 이 필요합니다.")); break;
            }

            return DocJsonZ(d);
        });
        r.Register("docs.resetLayout", "창 크기·위치 초기화(MDI-19)", _ => { Shell.ResetLayout(); return DocsJson(); });
        // 기본 응답은 가볍게 한다: DocsJson(시각 트리 전체 순회)이 UI 스레드에서 이동 1회당 약 19ms 를 써 측정 부하가 됐다(odebug-perf2 실측). 예전 목록이 필요하면 full=1
        r.Register("docs.moveAll", "MDI 문서 전체 이동 dx= dy= [full=1 이면 docs.list 와 같은 목록 반환, 기본은 {moved,dx,dy,docCount}]", a =>
        {
            double dx = a.Num("dx") ?? 0, dy = a.Num("dy") ?? 0;
            // moved = 실제로 위치가 바뀐 문서 수(최소/최대·SDI·고정 창 제외 판정은 MoveAll 그대로). 경계값 비교만 해서 시각 트리는 돌지 않는다
            var docs = Shell.Documents;
            var before = new DockRect[docs.Count];
            for (var i = 0; i < docs.Count; i++) { before[i] = docs[i].MdiBounds; }
            Shell.MoveAll(dx, dy);
            var moved = 0;
            for (var i = 0; i < docs.Count && i < before.Length; i++) { if (!docs[i].MdiBounds.Equals(before[i])) { moved++; } }
            if (moved > 0) { PerfProbe.Current.NoteInput(); } // 실제로 움직인 이동만 입력 사건(commit_raw.inputs_ms — B2 누락 공백 지표)
            if (a.Bool("full")) { return DocsJson(); }
            return new JsonObject { ["moved"] = moved, ["dx"] = dx, ["dy"] = dy, ["docCount"] = docs.Count };
        });
        RegisterMoveLight(r); // chrome.moveLight(끌기 중 가벼운 크롬·K1~K3 진단)는 RemoteWindowsCommands.MoveLight.cs — 300줄 규칙으로 나눔
        // 끌기 단계별 진단(시작 인자 --drag-probe on|full 일 때만 값이 쌓인다) — 측정 직전 reset=true, 이동 뒤 조회
        r.Register("perf.dragProbe", "끌기 단계별 진단 [reset=true 면 0 으로 하고 빈 값 반환] → {mode,moves,place_bytes_per_move,chrome_bytes,layout_bytes_per_move,layout_queued_measure,measure_calls(full),gantt_renders,ui_thread_bytes,other_thread_bytes,comp_render(full)…}", a =>
        {
            if (a.Bool("reset")) { MdiDragProbe.Reset(); }
            return MdiDragProbe.ToJson();
        });
        // layout.save·load·list·rename·delete·template.* 는 RemoteCommands.Layouts.cs(UI-11 개인 레이아웃 포함)
        r.Register("layout.get", "현재 레이아웃 JSON(저장 없이)", _ => JsonNode.Parse(Shell.SerializeLayout()));
        r.Register("screenshot", "PNG 캡처 [window=0..] | [doc] (문서 내용만) [path] [inline=true(base64 포함)] → path", ScreenshotAsync);
        r.Register("perf.get", "PerfProbe 계측 JSON(fps·단계 ms·지표)", _ => JsonNode.Parse(PerfProbe.Current.ToJson(AutomationBridge.Platform)));
        r.Register("log.tail", "앱 로그 패널 최근 n 줄(오래된 것 → 최신) n|lines=50", a =>
        {
            var lines = App.MainViewModel?.LogLines.Take(Math.Max(1, a.Int("n") ?? a.Int("lines") ?? 50)).Reverse()
                ?? Enumerable.Empty<string>();
            return new JsonArray(lines.Select(l => (JsonNode?)JsonValue.Create(l)).ToArray());
        });
        r.Register("app.closeMain", "메인 창 닫기(사용자 X 버튼과 같은 Window.Close 경로 — Closing 의 종료 자동 저장 포함, 응답 송신 후)", _ =>
        {
            if (VisualTreeInspector.Main() is not Window main) { throw new RemoteCommandException("메인 창이 Window 가 아닙니다(브라우저·모바일)."); }
            Dispatcher.UIThread.Post(main.Close, DispatcherPriority.Background);
            return JsonValue.Create(true);
        });
        r.Register("app.shutdown", "앱 종료(응답 송신 후)", _ =>
        {
            Dispatcher.UIThread.Post(App.RequestExit, DispatcherPriority.Background);
            return JsonValue.Create(true);
        });
    }

    private static ShellArrange ToArrange(string mode) => mode.ToLowerInvariant() switch
    {
        "cascade" => ShellArrange.Cascade,
        "tile-h" or "tilehorizontal" => ShellArrange.TileHorizontal,
        "tile" or "tile-v" or "tilevertical" => ShellArrange.TileVertical,
        "tabbed" => ShellArrange.Tabbed,
        _ => throw new RemoteCommandException("mode=cascade|tile-h|tile-v|tabbed"),
    };

    private static JsonArray WindowsJson()
    {
        var arr = new JsonArray();
        var tops = VisualTreeInspector.TopLevels();
        for (var i = 0; i < tops.Count; i++)
        {
            var t = tops[i];
            var o = new JsonObject { ["index"] = i, ["main"] = i == 0, ["type"] = t.GetType().Name };
            if (t is Window w)
            {
                o["title"] = w.Title; o["active"] = w.IsActive; o["state"] = w.WindowState.ToString(); o["topmost"] = w.Topmost;
                o["x"] = w.Position.X; o["y"] = w.Position.Y;
            }

            o["w"] = Math.Round(t.ClientSize.Width, 1); o["h"] = Math.Round(t.ClientSize.Height, 1);
            // 관리형 SDI 창 문서는 메인 TopLevel 안에 그려지지만 아래 ManagedWindow 항목으로 따로 나열한다(중복 방지)
            o["docs"] = new JsonArray(Shell.Documents.Where(d => ReferenceEquals(VisualTreeInspector.TopLevelOf(d), t)
                    && !(IsFloating(d, i) && global::Dock.Settings.DockSettings.UseManagedWindows))
                .Select(d => (JsonNode?)JsonValue.Create(d.Id)).ToArray());
            arr.Add(o);
        }

        // 관리형 창(브라우저·Android SDI): OS 창이 없어 TopLevel 목록에 없으므로 Dock 창(IDockWindow)별로 덧붙인다. index 없음(screenshot 은 doc= 로).
        // 한 창에 합류한 문서는 모두 docs 에 넣는다(D-C2-27: 문서별로 세면 화면에 없는 뒤 탭은 TopLevelOf 가 null 이라 빠져 docs 가 1건이었다)
        var root = RemoteDockAccess.Factory.Root;
        foreach (var g in Shell.Documents.Where(d => IsFloating(d, -1)).GroupBy(d => (object?)(root is null ? null : LayoutSerializer.FindWindowOf(root, d)) ?? d))
        {
            var docs = g.ToList();
            var d = docs.FirstOrDefault(x => VisualTreeInspector.TopLevelOf(x) is not null); // 화면에 그려진 활성 탭(경계·제목 기준)
            if (d is null || !ReferenceEquals(VisualTreeInspector.TopLevelOf(d), tops.FirstOrDefault())) { continue; }
            arr.Add(new JsonObject
            {
                ["index"] = null, ["main"] = false, ["type"] = "ManagedWindow", ["managed"] = true, ["bounds"] = ManagedBounds(d), // 메인 창 DIP
                ["title"] = d.Title, ["active"] = docs.Any(x => ReferenceEquals(x, Shell.ActiveDocument)), ["dockWindow"] = DockWindowJson(d),
                ["docs"] = new JsonArray(docs.Select(x => (JsonNode?)JsonValue.Create(x.Id)).ToArray()),
            });
        }

        return arr;
    }

    private static async Task<JsonNode?> ScreenshotAsync(JsonObject a)
    {
        var tops = VisualTreeInspector.TopLevels();
        TopLevel top;
        Control? part = null;
        var label = "main";
        if (a.ContainsKey("doc"))
        {
            var d = Doc(a);
            part = VisualTreeInspector.DocumentHost(d) ?? throw new RemoteCommandException("문서가 화면에 표시돼 있지 않습니다(최소화·비활성 탭).");
            top = TopLevel.GetTopLevel(part)!;
            label = "doc" + d.Seq + d.Kind;
        }
        else
        {
            var wi = a.Int("window") ?? 0;
            top = wi >= 0 && wi < tops.Count ? tops[wi] : throw new RemoteCommandException($"window 범위 0~{tops.Count - 1}");
            label = "win" + wi;
        }

        await AutomationBridge.WaitFramesAsync(top, 2); // 직전 명령의 레이아웃·렌더 반영
        var png = part is null ? AutomationBridge.Capture(top) : CaptureControl(part, top.RenderScaling);
        var result = new JsonObject { ["bytes"] = png.Length };
        if (!OperatingSystem.IsBrowser())
        {
            // 기본 저장 위치 = 앱 데이터 폴더\shots(D-SHOT1). 호스트가 PdLog.FileDirectory 를 '<데이터 폴더>\logs' 로 넣는다(데스크톱 --data-dir 반영). 없으면 기존 위치
            var dataDir = Path.GetDirectoryName(Pd.Client.Data.Logging.PdLog.FileDirectory ?? "") is { Length: > 0 } d0 ? d0 : Path.GetDirectoryName(RemoteControlServer.InfoFilePath)!;
            var path = a.Str("path") ?? Path.Combine(dataDir, "shots", $"{DateTime.Now:yyyyMMdd_HHmmss_fff}_{label}.png");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            await File.WriteAllBytesAsync(path, png);
            result["path"] = Path.GetFullPath(path);
        }

        if (a.Bool("inline") || OperatingSystem.IsBrowser()) { result["base64"] = Convert.ToBase64String(png); }
        return result;
    }

    /// <summary>컨트롤 1개(문서 내용)만 PNG 로 렌더.</summary>
    private static byte[] CaptureControl(Control c, double scale)
    {
        var size = new Avalonia.PixelSize(Math.Max(1, (int)(c.Bounds.Width * scale)), Math.Max(1, (int)(c.Bounds.Height * scale)));
        using var rtb = new RenderTargetBitmap(size, new Avalonia.Vector(96 * scale, 96 * scale));
        rtb.Render(c);
        using var ms = new MemoryStream();
        rtb.Save(ms, (int?)null);
        return ms.ToArray();
    }
}
#endif
