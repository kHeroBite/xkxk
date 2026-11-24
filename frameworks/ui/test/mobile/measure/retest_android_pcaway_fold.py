#!/usr/bin/env python3
# Fold5 접힘·펼침 전환 측정(C6) — device_state 왕복에서 크기·UI 형·문서·프로세스 유지와 실터치 반응을 잰다
"""
사용: python3 -B retest_android_pcaway_fold.py --apk <Debug APK> --out <폴더> [--cycles 3] [--fresh] [--measure]
끝나면 state reset 상태로 둔다. 앱 제거와 stay_on·density 원복은 호출자 몫이다.
"""
import argparse
import os
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import retest_android_pcaway_lib as L  # noqa: E402


def poll(c, want, limit=20):
    t0, s = time.time(), {}
    while time.time() - t0 < limit:
        try:
            s = c.call("ui.style") or {}
        except Exception as e:  # noqa: BLE001 — 전환 중 원격 응답 실패는 다시 시도한다
            s = {"err": repr(e)}
        if s.get("effective") == want:
            break
        time.sleep(0.3)
    return s, int((time.time() - t0) * 1000)


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--apk", required=True)
    p.add_argument("--out", required=True)
    p.add_argument("--cycles", type=int, default=3)
    p.add_argument("--fresh", action="store_true")
    p.add_argument("--measure", action="store_true")
    a = p.parse_args()
    c = L.Ctx("fold", "Debug", a.out)
    c.raw["apk"] = a.apk
    L.set_open()
    if not L.install(a.apk, fresh=a.fresh) or not L.attach(c):
        print("설치·연결 실패")
        return 1
    L.U.style_wait(c, "Tablet", 40)
    c.call("docs.close", all=True)
    for k in ("grid", "gantt", "form"):
        c.call("docs.open", kind=k)
    time.sleep(2)
    docs0 = sorted(d["id"] for d in c.call("docs.list"))
    pid0 = c.pid()
    caps = [c.shot("open0")]
    closed, opened = [], []
    for i in range(a.cycles):
        L.sh("cmd device_state state 0")
        s, ms = poll(c, "Mobile")
        time.sleep(1.5)
        L.wake()
        r = {"state": L.dev_state(), "wm": L.wm_size(), "effective": s.get("effective"), "ms": ms, "on_top": L.app_on_top(), "pid_same": c.pid() == pid0, "docs_same": sorted(d["id"] for d in c.call("docs.list")) == docs0}
        cap = c.shot("closed%d" % i)
        if i == 0:
            caps.append(cap)
        ok = c.tap_id("shell.btnDrawer")
        time.sleep(1.8)
        r["drawer_nodes"] = len([x for x in c.ids(c.dump()) if x.startswith("shell.navDrawer.")]) if ok else 0
        L.key_back()
        time.sleep(1.2)
        if not L.app_on_top():
            r["drawer_back_left_app"] = True
            L.relaunch()
        closed.append(r)
        print("closed", r, flush=True)
        L.sh("cmd device_state state reset")
        s, ms = poll(c, "Tablet")
        time.sleep(1.5)
        L.wake()
        r = {"state": L.dev_state(), "wm": L.wm_size(), "effective": s.get("effective"), "ms": ms, "on_top": L.app_on_top(), "pid_same": c.pid() == pid0, "docs_same": sorted(d["id"] for d in c.call("docs.list")) == docs0}
        c.shot("opened%d" % i)
        opened.append(r)
        print("opened", r, flush=True)
    c.raw.update(closed=closed, opened=opened, docs=docs0)
    final = L.sh("cmd device_state state")
    n, ex = c.fatal_count()
    c.check("치명 0", n == 0, (n, ex), 0)
    raw = L.save_raw(c, "c6")
    if a.measure:
        rows = {
            "override_closed_ok": ([r["state"] for r in closed], all(r["state"] == "CLOSED" for r in closed)),
            "cover_size_904x2316": ([r["wm"] for r in closed], all(tuple(r["wm"]) == (904, 2316) for r in closed)),
            "nav_on_closed": ([(r["effective"], r["ms"], r["drawer_nodes"]) for r in closed], all(r["effective"] == "Mobile" and r["drawer_nodes"] >= 3 for r in closed)),
            "override_opened_ok": ([(r["state"], r["wm"]) for r in opened], all(r["state"] == "OPENED" and tuple(r["wm"]) == (1812, 2176) for r in opened)),
            "restored_style_doc_state": ([(r["effective"], r["docs_same"], r["pid_same"]) for r in opened], all(r["effective"] == "Tablet" and r["docs_same"] and r["pid_same"] for r in opened)),
            "reset_base_state": (final, "OPENED" in final),
        }
        L.write_measure(a.out, "C6", rows, "retest_android_pcaway_fold.py " + raw, caps)
    return 0


if __name__ == "__main__":
    sys.exit(main())
