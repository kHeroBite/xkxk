#!/usr/bin/env python3
# 요건 2 스피너 측정 — 원격 명령(busy.*·spinner.frame·docs.*)만으로 값을 모아 JSON 에 저장한다(판정은 judge/ui_req2_8_rules.py). 키 주입·성능 측정 없음
"""
사용: python3 -B retest_busy.py --out <logs/<에이전트>/<회차>/busy.json> [--reduced]
앱은 먼저 run_desktop_bg.sh 로 진단 빌드를 임시 --data-dir·--profile-dir 와 --remote-control 로 띄워 둔다.
--reduced: 앱을 --reduced-motion 으로 띄운 회차. reduced 측정만 채우고 그 밖은 생략한다.
격리되지 않은 앱(사용자 폴더 사용)에 붙으면 rc=3 으로 거부한다. logs/ui28/ 에는 쓰지 않는다(otest 전용).
종료 코드: 0=측정 완료, 2=연결 실패, 3=실행 거부. PASS/FAIL 은 판정 도구가 낸다.
"""
import argparse
import json
import os
import sys
import time

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "common"))  # D-TESTDIR: test/common/_paths.py
import _paths  # noqa: E402
_paths.add()
from retest_remote import Remote  # noqa: E402
import retest_user_files  # noqa: E402

SHOW_WAIT_S = 0.25  # 표시 지연 150ms 를 넘겨 기다린다
HOLD_MS = 3000  # 원격 호출 1회가 수백 ms 걸려 문서 열기 뒤 첫 관찰까지 1초 넘게 걸릴 수 있다. 짧으면 표시 구간을 놓친다
POLL_S = 0.05
HIDE_TIMEOUT_S = 3.0
FRAME_TIMES = (0.25, 0.5, 0.75, 1.0)


def scope_row(state, scope):
    for s in (state or {}).get("scopes", []):
        if s.get("scope") == scope:
            return s
    return None


def wait_hidden(r, scope):
    end = time.time() + HIDE_TIMEOUT_S
    while time.time() < end:
        row = scope_row(r.call("busy.state"), scope)
        if row is None or not row.get("visible"):
            return True
        time.sleep(POLL_S)
    return False


def first_overlay(state, scope):
    row = scope_row(state, scope)
    overlays = (row or {}).get("overlays") or []
    return overlays[0] if overlays else None


def open_doc(r, **kw):
    before = {d["id"] for d in r.call("docs.list") or []}
    r.call("docs.open", kind="grid", allowDuplicate=True, **kw)
    time.sleep(SHOW_WAIT_S)
    return [d["id"] for d in r.call("docs.list") or [] if d["id"] not in before][0]


def series_for(r, action, scope_of):
    """busy.hold 를 켠 채 action 을 돌려 스코프의 표시 구간을 시계열로 관찰한다."""
    r.call("busy.hold", ms=HOLD_MS)
    scope = None
    try:
        scope = action()
        samples = []
        overlay = None
        end = time.time() + HOLD_MS / 1000.0 + HIDE_TIMEOUT_S
        key = scope_of(scope)
        while time.time() < end:
            state = r.call("busy.state")
            row = scope_row(state, key)
            samples.append(bool(row and row.get("visible")))
            if samples[-1] and overlay is None:
                overlay = first_overlay(state, key)
            if samples[-1] is False and any(samples):
                break
            time.sleep(POLL_S)
        return scope, {"scope": key, "saw_visible": any(samples), "ended_hidden": samples[-1] is False if samples else False, "samples": len(samples), "overlay": overlay}
    finally:
        r.call("busy.hold", ms=0)


def measure_frames(r):
    return [r.call("spinner.frame", t=t) for t in FRAME_TIMES]


def measure_scopes(r):
    a = open_doc(r)
    b = open_doc(r)
    r.call("busy.begin", scope=a, reason="test")
    r.call("busy.begin", scope=b, reason="test")
    time.sleep(SHOW_WAIT_S)
    both = r.call("busy.state")
    overlays = {a: first_overlay(both, a), b: first_overlay(both, b)}
    r.call("busy.end", scope=a)
    wait_hidden(r, a)
    after = r.call("busy.state")
    r.call("busy.end", scope=b)
    wait_hidden(r, b)
    return {"scope_a": a, "scope_b": b, "both": both, "after_end_one": after}, {k: v for k, v in overlays.items() if v}


def measure_main_overlay(r):
    r.call("busy.begin", scope="main", reason="test")
    time.sleep(SHOW_WAIT_S)
    overlay = first_overlay(r.call("busy.state"), "main")
    r.call("busy.end", scope="main")
    wait_hidden(r, "main")
    return overlay


def measure_first_open(r):
    """메인 창 첫 열림은 기동 시점이라 원격으로 다시 만들 수 없어 같은 경로의 main 스코프 begin/end 로 표시·해제만 본다."""
    r.call("busy.begin", scope="main", reason="first-open-sim")
    time.sleep(SHOW_WAIT_S)
    shown = bool((scope_row(r.call("busy.state"), "main") or {}).get("visible"))
    r.call("busy.end", scope="main")
    hidden = wait_hidden(r, "main")
    history = (r.call("busy.state") or {}).get("history") or []
    result = {"main": {"scope": "main", "saw_visible": shown, "ended_hidden": hidden, "simulated": True, "startup_open_ended": "main:open" in history}}
    _, result["mdi"] = series_for(r, lambda: open_doc(r), lambda d: d)
    _, result["sdi"] = series_for(r, lambda: open_doc(r, sdi=True), lambda d: d)
    return result


def measure_cursor(r):
    doc = open_doc(r)
    before = r.call("busy.state")
    r.call("busy.begin", scope=doc, reason="test")
    time.sleep(SHOW_WAIT_S)
    during = r.call("busy.state")
    time.sleep(0.6)
    later = r.call("busy.state")
    r.call("busy.end", scope=doc)
    wait_hidden(r, doc)
    after = r.call("busy.state")
    r.call("busy.begin", scope=doc, reason="test2")
    time.sleep(SHOW_WAIT_S)
    r.call("busy.end", scope=doc)
    wait_hidden(r, doc)
    second = r.call("busy.state")
    r.call("busy.cursorForce", fail=True)
    r.call("busy.begin", scope=doc, reason="fallback")
    time.sleep(SHOW_WAIT_S)
    fallback = r.call("busy.state")
    r.call("busy.end", scope=doc)
    wait_hidden(r, doc)
    fallback_after = r.call("busy.state")
    r.call("busy.cursorForce", fail=False)
    return {
        "mode": during.get("cursorMode"), "error": during.get("cursorError"),
        "advanced": later.get("cursorFrameAdvances", 0) > during.get("cursorFrameAdvances", 0),
        "driven_during": any(w.get("driven") for w in during.get("cursorWindows", [])),
        "windows_after": len(after.get("cursorWindows", [])),
        "sets_built_before": after.get("cursorFrameSetsBuilt"), "sets_built_after": second.get("cursorFrameSetsBuilt"),
        "fallback_mode": fallback.get("cursorMode"), "fallback_windows_after": len(fallback_after.get("cursorWindows", [])),
        "before_mode": before.get("cursorMode"),
    }


def measure_reduced(r):
    r.call("busy.begin", scope="main", reason="test")
    time.sleep(SHOW_WAIT_S)
    state = r.call("busy.state")
    overlay = first_overlay(state, "main") or {}
    result = {"reducedMotion": state.get("reducedMotion"), "spinnerStatic": overlay.get("spinnerStatic"), "spinnerRunning": overlay.get("spinnerRunning"), "timers": state.get("timers")}
    r.call("busy.end", scope="main")
    wait_hidden(r, "main")
    return result


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--out", required=True)
    p.add_argument("--reduced", action="store_true")
    args = p.parse_args()
    if os.path.normpath(args.out).replace("\\", "/").find("logs/ui28/") >= 0:
        print("logs/ui28/ 는 otest 결과 전용이다 — 원시 측정은 logs/<에이전트>/<회차>/ 에 쓴다.")
        return 3
    r = Remote()
    status = r.call("status")
    if not status:
        print("앱 연결 실패")
        return 2
    pid = status.get("pid")
    iso = retest_user_files.isolation(pid)
    if not (iso and iso[0]):
        print("격리되지 않은 앱이라 거부한다: %s" % (iso,))
        return 3
    raw = {"pid": pid}
    if args.reduced:
        raw["reduced"] = measure_reduced(r)
    else:
        raw["frames"] = measure_frames(r)
        raw["independent"], raw["overlays"] = measure_scopes(r)
        raw["overlays"]["main"] = measure_main_overlay(r)
        raw["first_open"] = measure_first_open(r)
        if raw["first_open"]["sdi"].get("overlay"):
            raw["overlays"]["sdi"] = raw["first_open"]["sdi"]["overlay"]  # SDI 창 안 스피너도 같은 기하 규칙으로 본다
        raw["selftest"] = r.call("busy.selftest")
        raw["cursor"] = measure_cursor(r)
        import retest_busy_long  # 이 파일을 다시 불러오므로 main 안에서 불러온다
        raw["long"] = retest_busy_long.measure_long(r, (raw["overlays"].get("main") or {}).get("window"))
        time.sleep(0.5)
        raw["idle"] = r.call("busy.state")
    raw["calls_failed"] = [c for c in r.calls if not c.get("ok")]
    os.makedirs(os.path.dirname(os.path.abspath(args.out)), exist_ok=True)
    with open(args.out, "w", encoding="utf-8") as f:
        json.dump(raw, f, ensure_ascii=False, indent=1)
    print("저장: %s (실패 호출 %d건)" % (args.out, len(raw["calls_failed"])))
    return 0


if __name__ == "__main__":
    sys.exit(main())
