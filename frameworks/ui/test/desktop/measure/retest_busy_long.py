#!/usr/bin/env python3
# 요건 2 스피너 측정(긴 작업) — UI 스레드가 막힌 동안 렌더 스레드 프레임이 늘어나는지와 실제 호출 지점을 잰다. retest_busy.py 가 부른다. 판정은 judge/ui_req2_8_rules.py
import time

from retest_busy import HIDE_TIMEOUT_S, HOLD_MS, POLL_S, SHOW_WAIT_S, first_overlay, open_doc, scope_row, wait_hidden

SLOW_MS = 1500


def frames_of(r):
    return ((r.call("busy.state") or {}).get("timers") or {}).get("spinnerFrames")


def _delta(before, after):
    return {"frames_before": before, "frames_after": after, "delta": None if before is None or after is None else after - before}


def blocked_run(r, cmd, **kw):
    """UI 스레드를 막는 명령 전후의 렌더 스레드 프레임 수 차이. 막는 동안 원격 응답이 없어 전후만 볼 수 있다."""
    before = frames_of(r)
    r.call(cmd, _timeout=30, **kw)
    after = frames_of(r)
    wait_hidden(r, "main")
    return _delta(before, after)


def measure_async_path(r, ms=2500):
    """비동기 작업은 UI 스레드가 놀아 끝까지 관찰할 수 있다. 표시 시작 후 작업이 끝날 때까지 켜져 있는지 본다."""
    r.call("busy.runAsyncTest", scope="main", ms=ms)
    started = time.time()
    samples = []
    while time.time() - started < ms / 1000.0 + HIDE_TIMEOUT_S:
        row = scope_row(r.call("busy.state"), "main")
        samples.append({"t": round(time.time() - started, 2), "visible": bool(row and row.get("visible")), "count": (row or {}).get("count", 0)})
        if samples[-1]["count"] == 0 and time.time() - started > ms / 1000.0:
            break
        time.sleep(POLL_S)
    during = [s for s in samples if s["count"] > 0 and s["t"] > 0.5]
    return {"ms": ms, "samples": samples, "visible_while_running": bool(during) and all(s["visible"] for s in during), "ended_hidden": wait_hidden(r, "main")}


def measure_float(r, main_title):
    """문서를 먼저 연 뒤 SDI 로 분리한다. 스피너가 새 창 안에 나타나는지 본다(busy.hold 로 관찰 구간 확보)."""
    doc = open_doc(r)
    wait_hidden(r, doc)
    r.call("busy.hold", ms=HOLD_MS)
    try:
        r.call("docs.sdi", doc=doc, on=True)
        overlay = None
        end = time.time() + HOLD_MS / 1000.0
        while overlay is None and time.time() < end:
            overlay = first_overlay(r.call("busy.state"), doc)
            time.sleep(POLL_S)
        hidden = wait_hidden(r, doc) or wait_hidden(r, doc)
        return {"doc": doc, "main_title": main_title, "overlay": overlay, "in_new_window": bool(overlay and overlay.get("window") != main_title), "ended_hidden": hidden}
    finally:
        r.call("busy.hold", ms=0)


def measure_restore(r):
    """자동 저장 슬롯 복원(LoadLayoutAsync). RunLongAsync 를 지나 main:layout 기록이 늘고 끝나면 꺼지는지 본다."""
    before = ((r.call("busy.state") or {}).get("history") or []).count("main:layout")
    r.call("layout.save")
    r.call("layout.load", _timeout=60)
    time.sleep(SHOW_WAIT_S)
    after = ((r.call("busy.state") or {}).get("history") or []).count("main:layout")
    return {"history_before": before, "history_after": after, "ended_hidden": wait_hidden(r, "main")}


def slow_path(r, action, wait_scope):
    """실제 호출 지점을 busy.slowWork 로 오래 걸리게 만들고 렌더 스레드 프레임 증가를 본다. 막는 동안 다음 원격 호출이 대기한다."""
    before = frames_of(r)
    action()
    time.sleep(0.3)  # 프레임 양보 뒤 막기가 시작되기를 기다린다. 다음 호출은 막기가 끝난 뒤 처리된다
    after = frames_of(r)
    wait_hidden(r, wait_scope)
    return _delta(before, after)


def _floating(r, doc):
    return any(d.get("id") == doc and d.get("floating") for d in r.call("docs.list") or [])


def measure_real_paths(r):
    """사용자가 쓰는 경로 그대로 판다: 테마 버튼, 문서 첫 열림, 창 전환 명령(분리·재결합)."""
    doc = open_doc(r)
    wait_hidden(r, doc)
    r.call("busy.slowWork", ms=SLOW_MS)
    try:
        result = {
            "theme": slow_path(r, lambda: r.call("ui.click", id="shell.btnTheme"), "main"),
            "open": slow_path(r, lambda: r.call("docs.open", kind="grid", allowDuplicate=True), "main"),
            "float": slow_path(r, lambda: r.call("ui.invoke", command="ToggleSdiCommand", doc=doc), doc),
        }
        result["float_floating"] = _floating(r, doc)
        result["redock"] = slow_path(r, lambda: r.call("ui.invoke", command="ToggleSdiCommand", doc=doc), doc)
        result["redock_floating"] = _floating(r, doc)
        r.call("ui.click", id="shell.btnTheme")  # 테마 되돌리기
        time.sleep(SLOW_MS / 1000.0)
        wait_hidden(r, "main")
        return result
    finally:
        r.call("busy.slowWork", ms=0)


def measure_rapid(r):
    """한 틱 안의 연타(busy.rapidTest): 테마 2번은 원래 값으로 돌아오고, SDI 전환 2번은 1번만 돌며, 끝나면 스코프·타이머가 남지 않아야 한다."""
    def theme_look():  # 설정값과 실제로 적용된 색을 함께 본다. 토글은 System 으로 돌아오지 않으므로 먼저 한 번 눌러 Light/Dark 에서 시작한다
        time.sleep(SHOW_WAIT_S)
        wait_hidden(r, "main")
        found = r.call("ui.find", id="shell.tabStrip", props="Background") or {}
        return [(r.call("status") or {}).get("theme"), found.get("props")]

    r.call("ui.click", id="shell.btnTheme")
    theme_before = theme_look()
    doc = open_doc(r)
    wait_hidden(r, doc)
    was_floating = _floating(r, doc)
    r.call("busy.rapidTest", doc=doc)
    theme_after = theme_look()
    wait_hidden(r, doc)
    time.sleep(SHOW_WAIT_S)
    state = r.call("busy.state") or {}
    return {
        "theme_before": theme_before, "theme_after": theme_after,
        "doc": doc, "was_floating": was_floating, "floating_after": _floating(r, doc),
        "doc_still_open": any(d.get("id") == doc for d in r.call("docs.list") or []),
        "scopes_left": state.get("scopes"), "timers": state.get("timers"),
    }


def measure_long(r, main_title):
    return {
        "rapid": measure_rapid(r),
        "real_paths": measure_real_paths(r),
        "yielded_block": blocked_run(r, "busy.blockTest", scope="main", ms=1500),
        "unyielded_block": blocked_run(r, "busy.block", beginScope="main", ms=1500),
        "unyielded_prearm_block": blocked_run(r, "busy.block", beginScope="main", preArm=True, ms=1500),
        "async_path": measure_async_path(r),
        "float": measure_float(r, main_title),
        "restore": measure_restore(r),
    }
