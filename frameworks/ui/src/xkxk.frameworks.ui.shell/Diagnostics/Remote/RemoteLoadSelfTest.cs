// 원격 명령 window.load.selftest 본문 — WindowLoadState 를 가짜 시계로 돌려 2단계 시작·완료·예외·취소·이중 호출·닫힘 해제·다른 스레드 완료·지연 표시를 판정한다. 진단 빌드 전용
#if PD_DIAGNOSTICS // 진단 빌드 전용(PdDiagnostics=true)
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using xkxk.frameworks.ui.shell.Busy;
using xkxk.frameworks.ui.shell.Docking;

namespace xkxk.frameworks.ui.shell.Diagnostics.Remote;

/// <summary>별도 시험 프로젝트가 없어 앱 안에서 돈다. 표시 정책(display)은 끈 인스턴스로 판정하고, 닫힘 해제의 오버레이·커서 정리는 실제 BusyHooks 로 본다.</summary>
internal static class RemoteLoadSelfTest
{
    private sealed class FakeReporter : IContentLoadReporter
    {
        public ILoadToken? Open;
        public Func<string, ILoadToken>? Begin;

        public void Attach(ILoadToken openToken, Func<string, ILoadToken> begin)
        {
            Open = openToken;
            Begin = begin;
        }
    }

    internal static async Task<JsonObject> Run()
    {
        var cases = new JsonArray();
        void Add(string name, string? failure, int pending) =>
            cases.Add(new JsonObject { ["name"] = name, ["pass"] = failure is null && pending == 0, ["detail"] = failure ?? "ok", ["pendingAfter"] = pending });

        void Case(string name, Func<RemoteBusySelfTest.FakeClock, WindowLoadState, List<string>, string?> run)
        {
            var clock = new RemoteBusySelfTest.FakeClock();
            var state = new WindowLoadState(clock, () => true, a => a(), display: false);
            var log = new List<string>();
            state.Changed += (_, key) => log.Add(key);
            string? failure;
            try
            {
                failure = run(clock, state, log);
            }
            catch (Exception ex)
            {
                failure = ex.GetType().Name + ": " + ex.Message;
            }

            Add(name, failure, state.PendingTimerCount);
        }

        Case("request_bind_complete", (_, s, log) =>
        {
            var t = s.BeginRequest("k1", "open");
            var counted = s.RequestCount("k1") == 1;
            var d = Doc("D-1");
            s.Bind("k1", d);
            var bound = s.IsLoading(d) && d.IsLoading && ReferenceEquals(d.LoadToken, t) && s.RequestCount("k1") == 0;
            t.Complete();
            return Check((counted, "request not counted"), (bound, "bind failed"), (!s.IsLoading(d) && !d.IsLoading && t.IsEnded, "not completed"), (log.Count >= 3, "changed not raised"));
        });
        Case("request_fail_no_doc", (_, s, _) =>
        {
            var t = s.BeginRequest("k2", "open");
            t.Fail(new InvalidOperationException("open failed"));
            return Check((s.RequestCount("k2") == 0 && s.Snapshot().Count == 0, "request left"));
        });
        Case("complete_fail_cancel", (_, s, _) =>
        {
            var d = Doc("D-2");
            var a = s.Begin(d, "search");
            var b = s.Begin(d, "refresh");
            var c = s.Begin(d, "x");
            var three = s.CountOf(d.Id) == 3;
            a.Complete();
            var error = new InvalidOperationException("boom");
            b.Fail(error);
            var afterFail = s.CountOf(d.Id) == 1 && ReferenceEquals(d.LastLoadError, error);
            c.Cancel();
            return Check((three, "overlap not summed"), (afterFail, "fail not applied"), (!s.IsLoading(d) && a.IsEnded && b.IsEnded && c.IsEnded, "not ended"));
        });
        Case("double_call", (_, s, _) =>
        {
            var d = Doc("D-3");
            var t1 = s.Begin(d, "x");
            var t2 = s.Begin(d, "y");
            t1.Complete();
            t1.Complete();
            t1.Fail();
            t1.Cancel();
            t1.Dispose();
            var once = s.CountOf(d.Id) == 1 && d.LastLoadError is null;
            t2.Dispose();
            return Check((once, "decremented more than once"), (s.CountOf(d.Id) == 0, "not zero"));
        });
        Case("close_release", (_, s, log) =>
        {
            var d = Doc("D-4");
            var open = s.BeginOpen(d);
            var search = s.Begin(d, "search");
            s.Release(d);
            var released = !s.IsLoading(d) && !d.IsLoading && d.LoadToken is null && open.IsEnded && search.IsEnded;
            var events = log.Count;
            open.Complete(); // 닫힌 뒤 늦게 끝남 — 무시
            search.Fail(new InvalidOperationException("late"));
            var ignored = log.Count == events && s.CountOf(d.Id) == 0 && d.LastLoadError is null;
            var again = s.Begin(d, "x");
            var reusable = s.CountOf(d.Id) == 1;
            again.Complete();
            return Check((released, "release left state"), (ignored, "late completion applied"), (reusable, "state not reusable"));
        });
        Case("slow_flag", (c, s, _) =>
        {
            var d = Doc("D-5");
            var t = s.Begin(d, "x");
            var limit = WindowLoadState.CurrentSlowSeconds * 1000L;
            c.Advance(limit - 1);
            var notYet = !s.IsSlow(d) && !d.IsLoadSlow;
            c.Advance(2);
            var slow = s.IsSlow(d) && d.IsLoadSlow && s.IsLoading(d); // 시간 초과에도 스피너는 내리지 않는다
            t.Complete();
            return Check((notYet, "slow too early"), (slow, "slow not raised"), (!s.IsSlow(d) && !d.IsLoadSlow, "slow left after end"));
        });
        Case("reporter_claims_open", (_, s, _) =>
        {
            var d = Doc("D-6");
            var open = s.BeginOpen(d);
            var reporter = new FakeReporter();
            var attached = s.AttachContent(d, reporter) && ReferenceEquals(reporter.Open, open);
            s.ViewRealized(d.Id); // 보고 계약 콘텐츠는 뷰 부착이 완료가 아니다
            var held = s.IsLoading(d);
            var search = reporter.Begin!("search");
            var two = s.CountOf(d.Id) == 2;
            open.Complete();
            search.Complete();
            var plain = Doc("D-7");
            s.BeginOpen(plain);
            var notReporter = !s.AttachContent(plain, new object());
            s.ViewRealized(plain.Id);
            return Check((attached, "attach failed"), (held, "view realize completed reporter open"), (two, "begin not summed"),
                (!s.IsLoading(d), "not completed"), (notReporter && !s.IsLoading(plain), "plain content not completed by view"));
        });

        Add("activation_origin_no_misattribution", OriginCase(), 0);
        Add("run_async", await RunAsyncCase(), 0);
        var (threadFailure, threadPending) = await OtherThreadCase();
        Add("other_thread", threadFailure, threadPending);
        var (releaseFailure, releasePending) = ReleaseScopeCase();
        Add("release_scope_hooks", releaseFailure, releasePending);
        return new JsonObject { ["cases"] = cases, ["slowSeconds"] = WindowLoadState.CurrentSlowSeconds };
    }

    private static PocDocument Doc(string id) => new() { Id = id, WindowId = LegacyWindowKinds.Grid, Seq = 1, Title = id };

    private static string? Check(params (bool Ok, string Message)[] checks) => checks.FirstOrDefault(c => !c.Ok).Message;

    /// <summary>출처 표식 오귀속 방지: 표식 뒤 다른 창 열림(Clear) → 출처 없음, 유효 시간 안 전환 → 출처, 시간 초과·두 번째 소비·변경 없음 → Program.</summary>
    private static string? OriginCase()
    {
        long now = 0;
        var t = new ActivationOriginTracker(() => now);
        t.Mark(ActivationOrigin.TitleTab);
        t.Clear(); // 창 열기(AddDocument)·닫기 경로
        var afterOpen = t.Take(changed: true);
        t.Mark(ActivationOrigin.Keyboard);
        var unchanged = t.Take(changed: false); // 활성이 그대로면 쓰지 않고 남긴다
        now = 300;
        var taken = t.Take(changed: true);
        var second = t.Take(changed: true);
        t.Mark(ActivationOrigin.BodyClick);
        now = 1000;
        var expired = t.Take(changed: true);
        return Check((afterOpen == ActivationOrigin.Program, "open took origin"), (unchanged == ActivationOrigin.Program, "unchanged took origin"),
            (taken == ActivationOrigin.Keyboard, "switch lost origin"), (second == ActivationOrigin.Program, "origin used twice"),
            (expired == ActivationOrigin.Program, "expired origin used"));
    }

    /// <summary>RunAsync: 예외는 다시 던지고 실패로 끝내며, 취소는 오류 표지 없이 끝낸다.</summary>
    private static async Task<string?> RunAsyncCase()
    {
        var s = new WindowLoadState(new RemoteBusySelfTest.FakeClock(), () => true, a => a(), display: false);
        var d = Doc("D-8");
        var rethrown = false;
        try
        {
            await s.RunAsync(d, "search", _ => Task.FromException(new InvalidOperationException("work")), CancellationToken.None);
        }
        catch (InvalidOperationException)
        {
            rethrown = true;
        }

        var failed = !s.IsLoading(d) && d.LastLoadError is InvalidOperationException;
        d.LastLoadError = null;
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var cancelled = false;
        try
        {
            await s.RunAsync(d, "refresh", ct => Task.FromCanceled(ct), cts.Token);
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }

        await s.RunAsync(d, "ok", _ => Task.CompletedTask, CancellationToken.None);
        return Check((rethrown, "exception swallowed"), (failed, "fail not ended"), (cancelled && d.LastLoadError is null, "cancel marked as error"),
            (!s.IsLoading(d) && s.PendingTimerCount == 0, "run left state"));
    }

    /// <summary>다른 스레드 완료: 상태 변경은 UI 스레드로 넘어가고, 두 스레드가 같은 토큰을 끝내도 한 번만 줄어든다.</summary>
    private static async Task<(string?, int)> OtherThreadCase()
    {
        var s = new WindowLoadState(new DispatcherBusyClock(), Dispatcher.UIThread.CheckAccess, a => Dispatcher.UIThread.Post(a), display: false);
        var d = Doc("D-9");
        var offUi = 0;
        s.Changed += (_, _) =>
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                offUi++;
            }
        };
        var t1 = s.Begin(d, "x");
        var t2 = s.Begin(d, "y");
        var t3 = s.Begin(d, "z");
        await Task.Run(t1.Complete);
        await Task.WhenAll(Task.Run(t2.Complete), Task.Run(t2.Complete));
        for (var i = 0; i < 40 && s.CountOf(d.Id) != 1; i++)
        {
            await Task.Delay(50);
        }

        var one = s.CountOf(d.Id) == 1;
        await Task.Run(() => t3.Fail(new InvalidOperationException("thread")));
        for (var i = 0; i < 40 && s.IsLoading(d); i++)
        {
            await Task.Delay(50);
        }

        return (Check((one, "cross-thread count wrong"), (!s.IsLoading(d), "not completed on UI"), (offUi == 0, "changed raised off UI thread")), s.PendingTimerCount);
    }

    /// <summary>닫힘 해제의 하위 표시 정리: 실제 BusyHooks 의 첫 열림 타이머·스코프가 남지 않는다(오버레이·커서는 Clear 이벤트로 꺼진다 — busy.selftest clear_*).</summary>
    private static (string?, int) ReleaseScopeCase()
    {
        const string id = "selftest-load-release";
        BusyHooks.TrackOpen(id, BusyHooks.Service);
        var opened = BusyHooks.OpeningScopes.Contains(id) && BusyHooks.Service.CountOf(id) == 1;
        BusyHooks.ReleaseScope(id);
        var left = BusyHooks.Service.Snapshot().Any(x => x.Scope == id) ? 1 : 0;
        return (Check((opened, "track open failed"), (!BusyHooks.OpeningScopes.Contains(id), "opening timer left"),
            (BusyHooks.Service.CountOf(id) == 0 && !BusyHooks.Service.IsVisible(id), "busy scope left")), left);
    }
}
#endif
