// 원격 명령 busy.selftest 본문 — BusyService 를 가짜 시계로 돌려 지연·최소 표시·참조 카운트·사전 걸기를 판정한다. 진단 빌드 전용
#if PD_DIAGNOSTICS // 진단 빌드 전용(PdDiagnostics=true)
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Pd.Poc.Shell.Busy;

namespace Pd.Poc.Shell.Diagnostics.Remote;

/// <summary>별도 시험 프로젝트가 없어 앱 안에서 돈다. 실제 타이머·화면을 쓰지 않는다.</summary>
internal static class RemoteBusySelfTest
{
    internal static JsonObject Run()
    {
        var cases = new JsonArray();
        var D = BusyService.CurrentShowDelayMs; // 서비스가 실제로 쓰는 적용값(설정 공급자 반영)
        var M = BusyService.CurrentMinVisibleMs;
        void Case(string name, Func<FakeClock, BusyService, List<string>, string?> run)
        {
            var clock = new FakeClock();
            var service = new BusyService(clock);
            var log = new List<string>();
            service.VisibilityChanged += (scope, visible) => log.Add($"{clock.NowMs}:{scope}:{(visible ? "show" : "hide")}");
            service.Armed += (scope, armed) => log.Add($"{clock.NowMs}:{scope}:{(armed ? "arm" : "disarm")}");
            var failure = run(clock, service, log);
            cases.Add(new JsonObject { ["name"] = name, ["pass"] = failure is null, ["detail"] = failure ?? string.Join(",", log), ["pendingAfter"] = service.PendingTimerCount });
        }

        static string? Expect(List<string> log, params string[] want) =>
            log.SequenceEqual(want) ? null : $"got [{string.Join(",", log)}] want [{string.Join(",", want)}]";

        Case("short_task_not_shown", (c, s, log) =>
        {
            var t = s.Begin("a", "x");
            c.Advance(BusyService.ShowDelayMs - 1);
            t.Dispose();
            c.Advance(1000);
            return Expect(log);
        });
        Case("min_visible_held", (c, s, log) =>
        {
            var t = s.Begin("a", "x");
            c.Advance(D + 50); // 표시 후 50ms 에 끝남 → 표시 시각 + 최소 표시 시간까지 유지(적용값으로 계산 — 상수 300 가정 제거)
            t.Dispose();
            c.Advance(1000);
            var hide = Math.Max(D + 50, D + M);
            return Expect(log, $"{D}:a:show", $"{hide}:a:hide");
        });
        Case("long_task_hides_at_end", (c, s, log) =>
        {
            var t = s.Begin("a", "x");
            c.Advance(1000);
            t.Dispose();
            return Expect(log, "150:a:show", "1000:a:hide");
        });
        Case("ref_count", (c, s, log) =>
        {
            var t1 = s.Begin("a", "x");
            var t2 = s.Begin("a", "y");
            c.Advance(500);
            t1.Dispose();
            t1.Dispose(); // 두 번 불러도 한 번만 줄어든다
            c.Advance(500);
            var mid = s.IsVisible("a") ? null : "first end hid scope";
            t2.Dispose();
            return mid ?? Expect(log, "150:a:show", "1000:a:hide");
        });
        Case("scopes_independent", (c, s, log) =>
        {
            var a = s.Begin("a", "x");
            var b = s.Begin("b", "x");
            c.Advance(500);
            a.Dispose();
            var mid = s.IsVisible("b") && !s.IsVisible("a") ? null : "scopes coupled";
            b.Dispose();
            return mid ?? Expect(log, "150:a:show", "150:b:show", "500:a:hide", "500:b:hide");
        });
        Case("restart_during_linger", (c, s, log) =>
        {
            var t = s.Begin("a", "x");
            c.Advance(D + 10);
            t.Dispose(); // 최소 표시 시간 안에 끝남 → 숨김 예약
            c.Advance(M / 2);
            var again = s.Begin("a", "y"); // 숨김 예약을 취소하고 계속 보인다
            c.Advance(1000);
            again.Dispose();
            return Expect(log, $"{D}:a:show", $"{D + 10 + M / 2 + 1000}:a:hide");
        });
        Case("prearm_short_cancelled", (c, s, log) =>
        {
            var t = s.Begin("a", "x", preArm: true);
            c.Advance(100);
            t.Dispose();
            c.Advance(1000);
            return Expect(log, "0:a:arm", "100:a:disarm");
        });
        Case("prearm_ui_blocked", (c, s, log) =>
        {
            var t = s.Begin("a", "x", preArm: true);
            c.AdvanceBlocked(1000); // UI 스레드가 막혀 예약이 돌지 못한 채 시간만 지남. 렌더 스레드에서는 150ms 에 이미 보였다
            t.Dispose();
            c.Advance(1000);
            return Expect(log, "0:a:arm", "1000:a:show", "1000:a:hide", "1000:a:disarm");
        });
        Case("prearm_min_visible_from_render", (c, s, log) =>
        {
            var t = s.Begin("a", "x", preArm: true);
            c.AdvanceBlocked(D + 50);
            t.Dispose(); // 렌더 스레드 표시 시각(D)부터 최소 표시 시간을 센다
            c.Advance(1000);
            var hide = Math.Max(D + 50, D + M);
            return Expect(log, "0:a:arm", $"{D + 50}:a:show", $"{hide}:a:hide", $"{hide}:a:disarm");
        });
        Case("clear_visible_hides", (c, s, log) =>
        {
            var t1 = s.Begin("a", "x");
            var t2 = s.Begin("a", "y");
            c.Advance(500);
            var cleared = s.Clear("a"); // 문서 닫힘 해제 — 보이던 표시를 즉시 내리고 예약을 남기지 않는다
            t1.Dispose(); // 늦게 끝난 토큰은 아무 일도 하지 않는다
            t2.Dispose();
            c.Advance(1000);
            var mid = cleared && s.CountOf("a") == 0 && !s.IsVisible("a") ? null : "clear left scope";
            return mid ?? Expect(log, "150:a:show", "500:a:hide");
        });
        Case("clear_armed_disarms", (c, s, log) =>
        {
            var t = s.Begin("a", "x", preArm: true);
            c.AdvanceBlocked(50);
            s.Clear("a");
            t.Dispose();
            c.Advance(1000);
            return Expect(log, "0:a:arm", "50:a:disarm");
        });
        Case("clear_pending_no_show", (c, s, log) =>
        {
            s.Begin("a", "x");
            c.Advance(100);
            s.Clear("a"); // 표시 예약 전에 비우면 끝내 보이지 않는다
            c.Advance(1000);
            var again = s.Clear("a") ? "second clear true" : null;
            return again ?? Expect(log);
        });
        return new JsonObject { ["cases"] = cases, ["showDelayMs"] = BusyService.ShowDelayMs, ["minVisibleMs"] = BusyService.MinVisibleMs };
    }

    /// <summary>가짜 시계 — window.load.selftest(RemoteLoadSelfTest)도 함께 쓴다.</summary>
    internal sealed class FakeClock : IBusyClock
    {
        private sealed class Item(long due, Action run) : IDisposable
        {
            public long Due { get; } = due;
            public Action Run { get; } = run;
            public bool Cancelled { get; private set; }
            public void Dispose() => Cancelled = true;
        }

        private readonly List<Item> _items = new();

        public long NowMs { get; private set; }

        public IDisposable Schedule(int delayMs, Action callback)
        {
            var item = new Item(NowMs + delayMs, callback);
            _items.Add(item);
            return item;
        }

        public void Advance(long ms)
        {
            var end = NowMs + ms;
            while (_items.Where(i => !i.Cancelled && i.Due <= end).OrderBy(i => i.Due).FirstOrDefault() is { } next)
            {
                _items.Remove(next);
                NowMs = next.Due;
                next.Run();
            }

            NowMs = end;
        }

        /// <summary>예약을 돌리지 않고 시간만 보낸다(UI 스레드가 막힌 구간).</summary>
        public void AdvanceBlocked(long ms) => NowMs += ms;
    }
}
#endif
