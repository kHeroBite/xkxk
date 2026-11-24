// 원격 명령(진행 표시 시험 — busy.begin·end·state·hold·cursorForce·selftest·runAsyncTest·blockTest, spinner.frame, window.load.selftest·state). 진단 빌드 전용
#if PD_DIAGNOSTICS // 진단 빌드 전용(PdDiagnostics=true)
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using xkxk.frameworks.ui.shell.Busy;
using xkxk.frameworks.ui.Controls;
using xkxk.frameworks.ui.Motion;

namespace xkxk.frameworks.ui.shell.Diagnostics.Remote;

/// <summary>스피너 시험 표면. 실제 경로(BusyHooks.Service·BusyOverlay)를 그대로 쓰고 상태만 읽는다. 성능 수치는 재지 않는다.</summary>
internal static partial class RemoteBusyCommands
{
    private const int MaxTestMs = 10000;

    private static readonly Dictionary<int, BusyScope> s_tokens = new();
    private static int s_nextToken;
    private static bool s_registered;

    internal static void Register(RemoteCommandRouter r)
    {
        if (s_registered)
        {
            return;
        }

        s_registered = true;
        r.Register("busy.begin", "스코프 작업 시작 scope=main|문서Id [reason=] → token", a =>
        {
            var token = ++s_nextToken;
            s_tokens[token] = BusyHooks.Service.Begin(a.ReqStr("scope"), a.Str("reason") ?? "remote");
            return new JsonObject { ["token"] = token };
        });
        r.Register("busy.end", "작업 끝 token= 또는 scope=(그 스코프의 가장 최근 토큰) → ended", a =>
        {
            var token = a.Int("token") ?? s_tokens.Where(t => t.Value.Scope == a.ReqStr("scope")).Select(t => t.Key).DefaultIfEmpty(0).Max();
            if (!s_tokens.Remove(token, out var scope))
            {
                throw new RemoteCommandException($"토큰이 없습니다: {token}");
            }

            scope.Dispose();
            return new JsonObject { ["ended"] = token, ["scope"] = scope.Scope };
        });
        r.Register("busy.hold", "창 첫 열림·SDI 전환의 끝 표시를 ms 만큼 미룸(시계열 관찰용, 0=끔) ms= → ms", a =>
        {
            BusyHooks.DiagHoldMs = Math.Max(0, a.Int("ms") ?? 0);
            return new JsonObject { ["ms"] = BusyHooks.DiagHoldMs };
        });
        r.Register("busy.slowWork", "양보 직후 UI 스레드를 ms 만큼 막아 실제 호출 지점의 긴 작업을 흉내 냄(0=끔) ms= → ms", a =>
        {
            BusyHooks.DiagSlowMs = Math.Clamp(a.Int("ms") ?? 0, 0, MaxTestMs);
            return new JsonObject { ["ms"] = BusyHooks.DiagSlowMs };
        });
        r.Register("busy.rapidTest", "한 틱 안에서 테마 전환 2번·SDI 전환 2번을 건다(원격 왕복 지연 없이 연타 재현) doc= → 호출 수", a =>
        {
            var shell = App.Shell ?? throw new RemoteCommandException("셸이 없습니다.");
            var doc = shell.Documents.FirstOrDefault(d => d.Id == a.ReqStr("doc")) ?? throw new RemoteCommandException("문서가 없습니다.");
            xkxk.frameworks.ui.shell.Theming.UiSettings.Current.ToggleTheme();
            xkxk.frameworks.ui.shell.Theming.UiSettings.Current.ToggleTheme();
            BusyHooks.RunAfterFrame(doc.Id, "sdi", () => shell.ToggleSdi(doc));
            BusyHooks.RunAfterFrame(doc.Id, "sdi", () => shell.ToggleSdi(doc));
            return new JsonObject { ["themeToggles"] = 2, ["sdiToggles"] = 2 };
        });
        r.Register("busy.cursorForce", "비트맵 커서 실패 흉내 fail=true|false(Wait 폴백 시험) → cursorMode", a =>
        {
            var cursor = Driver();
            cursor.ForceBitmapFailure = a.Bool("fail");
            cursor.ResetFrames();
            return new JsonObject { [BusyCursorDriver.CursorModeKey] = cursor.Mode, ["forced"] = cursor.ForceBitmapFailure };
        });
        r.Register("busy.state", "진행 표시 상태: 스코프·오버레이 좌표/크기/불투명도/입력 비차단·커서 방식·타이머 수", _ => State());
        r.Register("busy.selftest", "BusyService 가짜 시계 시험(지연·최소 표시·참조 카운트·스코프 독립·사전 걸기·Clear) → cases[{name,pass,detail}]", _ => RemoteBusySelfTest.Run());
        RemoteBackgroundSelfTest.Register(r);
        RegisterGrid(r); // RemoteBusyCommands.Grid.cs
        r.Register("window.load.selftest", "WindowLoadState 시험(요청 결합·완료·예외·취소·이중 호출·닫힘 해제·다른 스레드·지연·보고 계약) → cases[{name,pass,detail,pendingAfter}]", async _ => (JsonNode?)await RemoteLoadSelfTest.Run());
        r.Register("window.load.state", "창 콘텐츠 불러오는 중 상태: 문서별 isLoading·isSlow·수·사유·오류 → docs[]·states[]", _ => LoadState());
        r.Register("busy.runAsyncTest", "BusyService.RunAsync 로 ms 동안 비동기 작업을 건다(즉시 반환, 완료까지 busy.state 로 관찰) scope= ms= → started", a =>
        {
            var ms = TestMs(a);
            _ = BusyHooks.Service.RunAsync(a.ReqStr("scope"), "asyncTest", () => Task.Delay(ms));
            return new JsonObject { ["started"] = true, ["ms"] = ms };
        });
        r.Register("busy.blockTest", "RunLongAsync 경로로 UI 스레드를 ms 동안 막는다(표시를 먼저 건 뒤 막음, 막는 동안 원격 응답 없음) scope= ms= → 막기 직전 스피너 상태", async a =>
        {
            if (OperatingSystem.IsBrowser())
            {
                throw new RemoteCommandException("브라우저는 UI 스레드를 막는 시험을 할 수 없습니다.");
            }

            var scope = a.ReqStr("scope");
            var ms = TestMs(a);
            return await BusyHooks.RunLongAsync<JsonNode?>(scope, "blockTest", () =>
            {
                var before = new JsonObject { ["blockedMs"] = ms, ["overlays"] = new JsonArray(BusyOverlay.OverlaysFor(scope).Select(OverlayJson).ToArray<JsonNode?>()) };
                Thread.Sleep(ms);
                return before;
            });
        });
        r.Register("busy.block", "양보 없이 UI 스레드를 ms 동안 막는다(실제 동기 작업 재현) ms= [beginScope=] [preArm=true] → blockedMs", a =>
        {
            if (OperatingSystem.IsBrowser())
            {
                throw new RemoteCommandException("브라우저는 UI 스레드를 막는 시험을 할 수 없습니다.");
            }

            var ms = TestMs(a);
            var scope = a.Str("beginScope") is { } name ? BusyHooks.Service.Begin(name, "block", a.Bool("preArm")) : null;
            Thread.Sleep(ms);
            scope?.Dispose();
            return new JsonObject { ["blockedMs"] = ms };
        });
        r.Register("spinner.frame", "그리기 진행도 t(0~1) 프레임을 렌더해 막대별 칠해진 화소 범위를 돌려줌 t= [size=96] → bars[{bar,painted,startX,endX,progress}]", a => Frame(a.Num("t") ?? 1, a.Int("size") ?? 96));
    }

    private static int TestMs(JsonObject a)
    {
        var ms = a.Int("ms") ?? 1000;
        return ms is >= 1 and <= MaxTestMs ? ms : throw new RemoteCommandException($"ms 는 1~{MaxTestMs} 입니다: {ms}");
    }

    private static BusyCursorDriver Driver()
    {
        _ = BusyHooks.Service;
        return BusyHooks.Cursor ?? throw new RemoteCommandException("커서 구동기가 없습니다.");
    }

    private static JsonObject State()
    {
        var service = BusyHooks.Service;
        var cursor = Driver();
        var scopes = new JsonArray();
        foreach (var s in service.Snapshot())
        {
            scopes.Add(new JsonObject
            {
                ["scope"] = s.Scope,
                ["count"] = s.Count,
                ["visible"] = s.Visible,
                ["reason"] = s.LastReason,
                ["overlays"] = new JsonArray(BusyOverlay.OverlaysFor(s.Scope).Select(OverlayJson).ToArray<JsonNode?>()),
            });
        }

        return new JsonObject
        {
            ["scopes"] = scopes,
            ["history"] = new JsonArray(BusyHooks.History.Select(h => (JsonNode?)h).ToArray()),
            [BusyCursorDriver.CursorModeKey] = cursor.Mode,
            ["cursorError"] = cursor.FrameError,
            ["cursorFrameIndex"] = cursor.FrameIndex,
            ["cursorFrameAdvances"] = cursor.FrameAdvances,
            ["cursorFrameSetsBuilt"] = cursor.FrameSetsBuilt,
            ["cursorWindows"] = new JsonArray(cursor.AppliedWindows.Select(t => (JsonNode?)new JsonObject
            {
                ["title"] = (t as Window)?.Title,
                ["driven"] = cursor.IsDriven(t),
            }).ToArray()),
            ["timers"] = new JsonObject
            {
                ["spinner"] = GanttSpinner.RunningTimerCount,
                ["spinnerFrames"] = GanttSpinner.RenderThreadFrames,
                ["cursor"] = cursor.IsTimerRunning ? 1 : 0,
                ["service"] = service.PendingTimerCount,
                ["opening"] = BusyHooks.OpeningScopes.Count,
            },
            ["usesFollower"] = BusyCursorDriver.UsesFollower,
            ["followerActive"] = BusyHooks.Follower?.IsActive ?? false,
            ["followerVisible"] = BusyHooks.Follower?.IsFollowing ?? false,
            ["reducedMotion"] = MotionPreference.Reduced,
            ["platform"] = OperatingSystem.IsBrowser() ? "browser" : OperatingSystem.IsAndroid() ? "android" : OperatingSystem.IsMacOS() ? "macos" : OperatingSystem.IsWindows() ? "windows" : "other",
            ["unverifiedPlatform"] = OperatingSystem.IsMacOS(),
        };
    }

    private static JsonObject LoadState()
    {
        var load = xkxk.frameworks.ui.shell.Docking.WindowLoadState.Default;
        var docs = new JsonArray((App.Shell?.Documents ?? Array.Empty<xkxk.frameworks.ui.shell.Docking.PocDocument>()).Select(d => (JsonNode?)new JsonObject
        {
            ["id"] = d.Id,
            ["isLoading"] = d.IsLoading,
            ["isSlow"] = d.IsLoadSlow,
            ["count"] = load.CountOf(d.Id),
            ["hasToken"] = d.LoadToken is { IsEnded: false },
            ["error"] = d.LastLoadError?.Message,
            ["iconKey"] = d.IconKey,
            ["screenId"] = d.ScreenId,
            ["menuId"] = d.MenuId,
        }).ToArray());
        var states = new JsonArray(load.Snapshot().Select(s => (JsonNode?)new JsonObject
        {
            ["key"] = s.Key, ["doc"] = s.DocumentId, ["count"] = s.Count, ["slow"] = s.Slow, ["reasons"] = s.Reasons,
        }).ToArray());
        return new JsonObject { ["docs"] = docs, ["states"] = states, ["pendingTimers"] = load.PendingTimerCount, ["slowSeconds"] = xkxk.frameworks.ui.shell.Docking.WindowLoadState.CurrentSlowSeconds };
    }

    private static JsonObject OverlayJson(BusyOverlay overlay)
    {
        var top = TopLevel.GetTopLevel(overlay);
        var spinner = overlay.Spinner;
        var regionCenter = overlay.TranslatePoint(new Point(overlay.Bounds.Width / 2, overlay.Bounds.Height / 2), top!);
        var spinnerCenter = spinner?.TranslatePoint(new Point(spinner.Bounds.Width / 2, spinner.Bounds.Height / 2), top!);
        return new JsonObject
        {
            ["window"] = (top as Window)?.Title,
            ["overlayVisible"] = overlay.IsEffectivelyVisible,
            ["hitTestVisible"] = overlay.IsHitTestVisible,
            ["regionWidth"] = overlay.Bounds.Width,
            ["regionHeight"] = overlay.Bounds.Height,
            ["regionCenterX"] = regionCenter?.X,
            ["regionCenterY"] = regionCenter?.Y,
            ["spinnerCenterX"] = spinnerCenter?.X,
            ["spinnerCenterY"] = spinnerCenter?.Y,
            ["spinnerWidth"] = spinner?.Bounds.Width,
            ["spinnerHeight"] = spinner?.Bounds.Height,
            ["spinnerOpacity"] = spinner?.Opacity,
            ["spinnerHitTest"] = spinner?.IsHitTestVisible,
            ["spinnerRunning"] = spinner?.IsRunning,
            ["spinnerStatic"] = spinner?.IsStaticFrame,
        };
    }

    private static JsonObject Frame(double t, int size)
    {
        if (size is < 8 or > 512)
        {
            throw new RemoteCommandException($"size 는 8~512 입니다: {size}");
        }

        var brush = Application.Current is { } app && app.TryGetResource("PdBrushAccent", app.ActualThemeVariant, out var value) && value is IBrush found
            ? found
            : throw new RemoteCommandException("PdBrushAccent 리소스가 없습니다.");
        using var bitmap = GanttSpinner.RenderFrame(t, size, brush);
        var stride = size * 4;
        var buffer = Marshal.AllocHGlobal(stride * size);
        try
        {
            bitmap.CopyPixels(new PixelRect(0, 0, size, size), buffer, stride * size, stride);
            var bars = new JsonArray();
            for (var bar = 0; bar < GanttSpinner.BarCount; bar++)
            {
                var y = (int)((bar + 0.5) * size / GanttSpinner.BarCount);
                int first = -1, last = -1, painted = 0;
                for (var x = 0; x < size; x++)
                {
                    if (Marshal.ReadByte(buffer, y * stride + x * 4 + 3) > 32) // 알파 채널은 BGRA·RGBA 모두 4번째 바이트다
                    {
                        first = first < 0 ? x : first;
                        last = x;
                        painted++;
                    }
                }

                bars.Add(new JsonObject { ["bar"] = bar, ["painted"] = painted, ["startX"] = first, ["endX"] = last, ["progress"] = GanttSpinner.BarProgress(bar, t) });
            }

            return new JsonObject { ["t"] = t, ["size"] = size, ["bars"] = bars };
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
#endif
