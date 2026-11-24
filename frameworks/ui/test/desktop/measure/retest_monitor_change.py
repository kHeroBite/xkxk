#!/usr/bin/env python3
# 요건 5 자동 복구 측정 — 가상 모니터를 실행 중 바꾸고 메인 창을 줄여 창 사각형 변화를 JSON 에 저장한다(실제 모니터 분리·연결은 사람 확인 H-5-1)
"""
사용: python3 -B retest_monitor_change.py --out <logs/<에이전트>/<회차>/monitor.json>
앱은 먼저 임시 폴더로 띄운다(가상 모니터 2대 = 18000~21000·21000~24000, 메인은 2번 모니터 위):
  (WSL 환경변수는 WSLENV 에 넣어야 Windows exe 로 넘어간다. 빠지면 가상 모니터가 꺼져 AC-5-03·05 가 거짓 FAIL 이다)
  WSLENV="PD_VIRTUAL_MONITORS:$WSLENV" PD_VIRTUAL_MONITORS=2 run_desktop_bg.sh --conf Work --no-kill -- --remote-control --data-dir <임시> --profile-dir <임시>
      --bg-pos 21500,100 --primary-override 18000,0,6000,1440 --offscreen-guard on
격리되지 않은 앱에 붙으면 rc=3 으로 거부한다. poc/ui/logs/ui28/ 에는 쓰지 않는다(otest 전용).
종료 코드: 0=측정 완료, 2=연결 실패, 3=실행 거부.
"""
import argparse
import json
import os
import sys
import time

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "common"))  # D-TESTDIR
import _paths  # noqa: E402
_paths.add()
from retest_remote import Remote  # noqa: E402
import retest_user_files  # noqa: E402

DEBOUNCE_MS = 400
POLL_S = 0.05
WAIT_LIMIT_S = 3.0
STAY_S = 1.2  # 움직이지 않아야 하는 창을 보는 시간(디바운스보다 충분히 길게)
MON1_X, MON2_X = 18000, 21000
SHRUNK_W, SHRUNK_H = 1100, 700  # 메인 축소 크기(DIP). 더 좁히면 자동 UI 형이 바뀌어 MDI 영역이 사라진다(800 폭 실측 area=null)


def open_doc(r, **kw):
    before = {d["id"] for d in r.call("docs.list") or []}
    r.call("docs.open", kind="grid", allowDuplicate=True, **kw)
    time.sleep(0.3)
    return [d["id"] for d in r.call("docs.list") or [] if d["id"] not in before][0]


def sdi_row(snapshot, doc):
    return next(s for s in snapshot["sdi"] if s.get("doc") == doc)


def doc_row(state, doc):
    return next(d for d in state["docs"] if d["id"] == doc)


def wait_change(read, before):
    """read() 값이 before 에서 바뀌는 데 걸린 ms. 제한 안에 안 바뀌면 None."""
    start = time.time()
    while time.time() - start < WAIT_LIMIT_S:
        if read() != before:
            return round((time.time() - start) * 1000)
        time.sleep(POLL_S)
    return None


def measure_monitors(r):
    """2→1: 메인·SDI(2번 모니터)는 주 모니터로, 1번 모니터의 SDI 와 고정 SDI 처리를 함께 본다."""
    visible = open_doc(r)
    hidden = open_doc(r)
    locked = open_doc(r)
    for doc in (visible, hidden, locked):
        r.call("docs.sdi", doc=doc, on=True)
        time.sleep(0.4)
    r.call("chrome.lock", doc=locked)
    r.call("sdi.setBounds", doc=visible, x=MON1_X + 500, y=300)
    r.call("sdi.setBounds", doc=hidden, x=MON2_X + 500, y=300)
    r.call("sdi.setBounds", doc=locked, x=MON2_X + 900, y=500)
    time.sleep(0.5)
    before = r.call("window.snapshot")
    r.call("monitors.setVirtual", count=1, force=True)
    latency = wait_change(lambda: r.call("window.snapshot")["main"]["x"], before["main"]["x"])
    time.sleep(STAY_S)
    after = r.call("window.snapshot")
    return {
        "before": before, "after": after, "latency_ms": latency, "debounce_ms": DEBOUNCE_MS,
        "primary_work": {"x": MON1_X, "y": 0, "w": MON2_X - MON1_X, "h": 1440},
        "visible": visible, "hidden": hidden, "locked": locked,
        "locked_state": r.call("chrome.state", doc=locked),
        "guard_log": r.call("log.tail", n=40),
    }


def measure_mdi(r):
    """메인 축소: 놓은 값 유지 창(영역 오른쪽)은 계단 기본 위치로, 보이는 창·최소화·최대화 창은 그대로."""
    r.call("docs.close", all=True)
    time.sleep(0.3)
    snap = r.call("window.snapshot")["main"]
    r.call("placement.resizeMain", w=1400, h=900)
    time.sleep(0.8)
    placed = open_doc(r)
    locked = open_doc(r)
    keep = open_doc(r)
    mini = open_doc(r)
    maxi = open_doc(r)
    r.call("docs.state", doc=mini, state="minimized")
    r.call("placement.markPlaced", doc=placed, x=1000, y=100, w=300, h=200)
    r.call("placement.markPlaced", doc=locked, x=1100, y=200, w=300, h=200)
    r.call("placement.markPlaced", doc=keep, x=10, y=10, w=300, h=200)
    r.call("chrome.lock", doc=locked)
    time.sleep(0.8)
    r.call("docs.state", doc=maxi, state="maximized")
    time.sleep(0.5)
    before = r.call("placement.state")
    r.call("placement.resizeMain", w=SHRUNK_W, h=SHRUNK_H)
    latency = wait_change(lambda: doc_row(r.call("placement.state"), placed)["bounds"], doc_row(before, placed)["bounds"])
    time.sleep(STAY_S)
    after = r.call("placement.state")
    r.call("placement.resizeMain", w=snap["w"], h=snap["h"])
    return {
        "before": before, "after": after, "latency_ms": latency,
        "placed": placed, "locked": locked, "keep": keep, "minimized": mini, "maximized": maxi,
        "locked_state": r.call("chrome.state", doc=locked),
    }


def measure_background_off(r):
    """복구를 끈 비전경 상태: 창을 영역 밖에 두고 메인을 줄여도 옮기지 않고 로그만 남긴다."""
    r.call("placement.force", on=False)
    r.call("docs.close", all=True)
    time.sleep(0.3)
    doc = open_doc(r)
    snap = r.call("window.snapshot")["main"]
    r.call("placement.markPlaced", doc=doc, x=snap["w"] + 300, y=100, w=300, h=200)
    time.sleep(0.5)
    before = doc_row(r.call("placement.state"), doc)
    r.call("placement.resizeMain", w=snap["w"] - 100, h=snap["h"])
    time.sleep(STAY_S)
    after = doc_row(r.call("placement.state"), doc)
    log = r.call("log.tail", n=40)
    r.call("placement.resizeMain", w=snap["w"], h=snap["h"])
    return {"before": before, "after": after, "log": log, "suppressed": r.call("placement.state")["suppressed"]}


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--out", required=True)
    args = p.parse_args()
    if os.path.normpath(args.out).replace("\\", "/").find("logs/ui28/") >= 0:
        print("logs/ui28/ 는 otest 결과 전용이다 — 원시 측정은 logs/<에이전트>/<회차>/ 에 쓴다.")
        return 3
    r = Remote()
    status = r.call("status")
    if not status:
        print("앱 연결 실패")
        return 2
    iso = retest_user_files.isolation(status.get("pid"))
    if not (iso and iso[0]):
        print("격리되지 않은 앱이라 거부한다: %s" % (iso,))
        return 3

    raw = {"pid": status.get("pid"), "selftest": r.call("placement.selftest")}
    raw["background_off"] = measure_background_off(r)  # 켜기 전에 먼저 본다
    r.call("placement.force", on=True)
    raw["monitors"] = measure_monitors(r)
    raw["mdi"] = measure_mdi(r)
    r.call("placement.force", on=False)
    os.makedirs(os.path.dirname(os.path.abspath(args.out)), exist_ok=True)
    with open(args.out, "w", encoding="utf-8") as f:
        json.dump(raw, f, ensure_ascii=False, indent=1)
    print(args.out)
    return 0


if __name__ == "__main__":
    sys.exit(main())
