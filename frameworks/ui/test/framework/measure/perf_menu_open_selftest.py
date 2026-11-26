# B08 메뉴 열기 측정기 자체 시험(앱 없음) — 가짜 원격 응답으로 rc 0~4·nearest-rank·INVALID 조건·WAIT 경로를 덮는다. perf_menu_open.py --self-test 가 부른다
import contextlib
import shutil
import tempfile

import perf_menu_open as pm


class Fake:
    """가짜 앱. ms=시행별 첫 프레임 ms 생성 함수, timeout_at=그 순번부터 timed_out, missing=없는 메뉴, opened=이미 열린 메뉴."""

    def __init__(self, ms=lambda i: 10.0, timeout_at=None, missing=(), already=(), modules=True, refresh=True, build=True):
        self.ms, self.timeout_at, self.missing, self.already = ms, timeout_at, set(missing), set(already)
        self.modules, self.refresh, self.build, self.i, self.calls = modules, refresh, build, 0, []

    def __call__(self, cmd, **a):
        self.calls.append(cmd)
        if cmd == "menu.open":
            if a["id"] in self.missing:
                return {"ok": False, "error": "메뉴를 찾지 못했습니다: %s" % a["id"]}
            if a["id"] in self.already:
                return {"ok": False, "error": "이미 열린 메뉴 — 닫은 뒤 다시 재세요: %s" % a["id"]}
            self.i += 1
            to = self.timeout_at is not None and self.i >= self.timeout_at
            return {"ok": True, "result": {"id": a["id"], "opened": True, "sync_ms": 1.0, "first_frame_ms": None if to else self.ms(self.i),
                                           "timed_out": to, "items": 5, "popup_kind": "overlay"}}
        if cmd == "perf.ops":
            names = ["ui.menu.build"] if a.get("op") == "ui.menu.build" else ["ui.menu.open.firstframe"] + (["ui.menu.refresh"] if self.refresh else [])
            if a.get("op") == "ui.menu.build" and not self.build:
                names = []
            return {"ok": True, "result": [{"name": n, "count": 1, "frame_p95_ms": 3.0} for n in names]}
        if cmd == "perf.modules":
            return {"ok": True, "result": {"recorded": self.modules, "build_ms": 8.0, "total_configure_ms": 6.0, "count": 2 if self.modules else 0, "modules": []}}
        return {"ok": True, "result": {}}


def make_launch(fake, state, meta=None):
    @contextlib.contextmanager
    def launch(a, log):
        state["launched"] = True
        if meta and meta.get("status"):
            yield None, dict(meta)
            return
        m = dict(meta or {})
        yield fake, m
    return launch


def run(argv, fake=None, away=lambda: 0, meta=None, tmp=None):
    st = {"launched": False}
    a = pm.parse(argv + ["--out", "%s/r.json" % tmp])
    res, rc = pm.execute(a, make_launch(fake or Fake(), st, meta), away, log=lambda s: None)
    return res, rc, st["launched"]


def self_test():
    tmp = tempfile.mkdtemp(prefix="perf_menu_open_st_")
    cases = []
    try:
        def case(name, ok):
            cases.append((name, bool(ok)))

        case("nearest_rank_p95", pm.nearest_rank(list(range(1, 21)), 95) == 19 and pm.nearest_rank(list(range(1, 101)), 95) == 95
             and pm.nearest_rank([5], 95) == 5 and pm.nearest_rank([], 95) is None and pm.nearest_rank(list(range(1, 21)), 50) == 10)
        r, rc, _ = run(["--menus", "a,b"], Fake(ms=lambda i: float(i)), tmp=tmp)
        case("pooled_stats", rc == 0 and r["status"] == "RECORDED" and r["pooled"]["n"] == 40 and r["pooled"]["max"] == 44.0
             and r["pooled"]["p95"] == pm.nearest_rank(r["menus"]["a"]["samples"] + r["menus"]["b"]["samples"], 95)
             and r["menus"]["a"]["samples"][0] == 3.0)  # warmup 2회는 버림
        case("recorded_ops_modules", r["within_target"] is True and r["ops"]["ui.menu.build"]["note"].startswith("perf.reset")
             and "ui.menu.refresh" in r["ops"] and r["modules_within_target"] is True)
        f = Fake()
        r, rc, launched = run([], f, away=lambda: 1, tmp=tmp)
        case("wait_no_launch", rc == 3 and r["status"] == "WAIT" and not launched and not f.calls)
        r, rc, _ = run(["--n", "5"], tmp=tmp)
        case("invalid_too_few", rc == 2 and r["status"] == "INVALID" and "n=5<20" in r["reason"])
        r, rc, _ = run([], Fake(timeout_at=30), tmp=tmp)
        case("invalid_timeout", rc == 2 and r["status"] == "INVALID" and "timed_out" in r["reason"])
        r, rc, _ = run([], Fake(ms=lambda i: 80.0), tmp=tmp)
        case("over_target_flag", rc == 0 and r["status"] == "RECORDED" and r["within_target"] is False)
        rejected = []
        for p in pm.REJECT_PORTS:
            r, rc, launched = run(["--port", str(p)], tmp=tmp)
            rejected.append(rc == 4 and not launched)
        r, rc, _ = run(["--port", "5833"], tmp=tmp)
        case("port_reject", all(rejected) and rc == 0)
        r, rc, launched = run(["--functional"], away=lambda: 1, tmp=tmp)
        case("functional_skips_away", rc == 0 and r["status"] == "FUNCTIONAL_PASS" and launched and r["menus"]["shell.mnuFile"]["n"] == 3)
        r, rc, _ = run(["--host", "desktop"], meta={"status": "WAIT", "carryover": "UTODO-0031", "reason": "사용자 파일 가드 rc=9"}, tmp=tmp)
        case("guard_rc9_wait", rc == 3 and r["status"] == "WAIT" and r["carryover"] == "UTODO-0031")
        r, rc, _ = run(["--functional", "--menus", "shell.mnuNotExist"], Fake(missing={"shell.mnuNotExist"}), tmp=tmp)
        case("canary_missing_menu_fail", rc == 1 and r["status"] == "FAIL")
        r, rc, _ = run([], Fake(already={"shell.mnuView"}), tmp=tmp)
        case("menu_already_open_error", rc == 2 and r["status"] == "INVALID" and "이미 열린" in r["reason"])
        seq = iter([0, 0, 1])
        r, rc, _ = run([], away=lambda: next(seq, 1), tmp=tmp)
        case("returned_midway_wait", rc == 3 and r["status"] == "WAIT" and "복귀" in r["reason"])
        r, rc, _ = run(["--functional"], Fake(modules=False), tmp=tmp)
        case("functional_modules_missing_fail", rc == 1 and "perf.modules" in r["reason"])
        f0 = Fake()
        zero = lambda cmd, **a: ({"ok": True, "result": {"recorded": True, "build_ms": 15.8, "total_configure_ms": 0, "count": 0, "modules": []}}
                                 if cmd == "perf.modules" else f0(cmd, **a))  # 실측과 같은 응답: Build 는 했고 모듈 0
        r, rc, _ = run(["--functional"], zero, tmp=tmp)
        case("functional_modules_count0_note", rc == 0 and r["status"] == "FUNCTIONAL_PASS" and "모듈을 등록하지 않음" in r.get("modules_note", "")
             and r["modules"]["count"] == 0 and r["modules_within_target"] is True)
        r, rc, _ = run(["--functional"], Fake(build=False), tmp=tmp)
        case("functional_build_missing_fail", rc == 1 and "ui.menu.build" in r["reason"])
        r, rc, _ = run(["--functional"], meta={"pageerror": 1}, tmp=tmp)
        case("functional_pageerror_fail", rc == 1 and "pageerror" in r["reason"])
        r, rc, _ = run([], meta={"status": "INVALID", "reason": "기동 실패"}, tmp=tmp)
        case("launch_fail_invalid", rc == 2 and r["status"] == "INVALID")
    except Exception as e:  # noqa: BLE001
        cases.append(("예외 %r" % (e,), False))
    finally:
        shutil.rmtree(tmp, ignore_errors=True)
    for name, ok in cases:
        print("SELFTEST %s %s" % ("PASS" if ok else "FAIL", name))
    passed = sum(1 for _, ok in cases if ok)
    print("self-test %d/%d" % (passed, len(cases)))
    return 0 if passed == len(cases) else 1
