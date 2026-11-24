#!/usr/bin/env python3
# 리뷰 M-5 + 복원 경로 측정 — (1) 문서를 열자마자 닫기를 반복해도 스피너 스코프·타이머가 남지 않고 오류 로그가 없는지, (2) 레이아웃 복원으로 열린 문서의 내용이 실현되어 있는지
"""
사용: PD_REMOTE_INFO=<격리 앱 연결 정보> python3 -B retest_m5_restore.py --out <결과.json> [--mode quickclose|restored] [--rounds 15]
규칙(결과 보기 전에 고정): quickclose — 매 회 docs.open 직후 docs.close 가 성공하고, 2초 뒤 busy.state.scopes 가 비고 timers(spinner·cursor·service·opening)가 전부 0 이며 log.tail 에 Exception 이 없다.
                      restored — docs.list 의 모든 문서가 visible 이고 grid 문서는 ui.find grid.dgMain 이 찾아지며 busy.state.scopes 가 빈다.
종료 코드: 0=규칙 참, 1=위반, 2=연결 실패. logs/ui28/ 에는 쓰지 않는다.
"""
import argparse
import json
import os
import sys
import time

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "common"))
import _paths  # noqa: E402
_paths.add()
from retest_remote import Remote  # noqa: E402

TIMERS = ("spinner", "cursor", "service", "opening")


def safe(r, cmd, **kw):
    try:
        return r.call(cmd, **kw)
    except Exception as ex:  # noqa: BLE001
        return {"error": str(ex)}


def idle_state(r):
    time.sleep(2.0)
    st = safe(r, "busy.state") or {}
    return {"scopes": [s for s in st.get("scopes", []) if s.get("visible") or s.get("count")], "timers": st.get("timers", {})}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", required=True)
    ap.add_argument("--mode", choices=["quickclose", "restored"], default="quickclose")
    ap.add_argument("--rounds", type=int, default=15)
    a = ap.parse_args()
    if os.path.abspath(a.out).replace("\\", "/").find("/logs/ui28/") >= 0 or not os.environ.get("PD_REMOTE_INFO"):
        sys.exit("logs/ui28 쓰기 금지 / PD_REMOTE_INFO 필수")
    r = Remote()
    if r.call("status") is None:
        return 2
    out, bad = {"mode": a.mode}, []
    if a.mode == "quickclose":
        rows = []
        for i in range(a.rounds):
            before = {d["id"] for d in safe(r, "docs.list") or []}
            kind = ("grid", "gantt", "form")[i % 3]
            t0 = time.time()
            safe(r, "docs.open", kind=kind, allowDuplicate=True)
            now = [d["id"] for d in safe(r, "docs.list") or [] if d["id"] not in before]
            closed = safe(r, "docs.close", doc=now[0]) if now else {"error": "열린 문서 없음"}
            rows.append({"i": i, "kind": kind, "opened": now, "close": closed, "open_to_close_ms": int((time.time() - t0) * 1000)})
            if not now or (isinstance(closed, dict) and closed.get("error")):
                bad.append("회차 %d 열기/닫기 실패: %s %s" % (i, now, closed))
        out["rounds"] = rows
        out["idle"] = idle_state(r)
        if out["idle"]["scopes"]:
            bad.append("스코프가 남음: %s" % out["idle"]["scopes"])
        bad += ["타이머 %s=%s" % (k, out["idle"]["timers"].get(k)) for k in TIMERS if out["idle"]["timers"].get(k) != 0]
        log = safe(r, "log.tail", n=200)
        text = json.dumps(log, ensure_ascii=False)
        out["log_exception_count"] = text.count("Exception")
        if out["log_exception_count"]:
            bad.append("log.tail 에 Exception %d회" % out["log_exception_count"])
    else:
        time.sleep(1.5)
        docs = safe(r, "docs.list") or []
        rows = []
        for d in docs:
            found = None
            if d.get("kind") == "grid":
                found = bool((safe(r, "ui.find", id="grid.dgMain", doc=d["id"]) or {}).get("found", True)) if not (safe(r, "ui.find", id="grid.dgMain", doc=d["id"]) or {}).get("error") else False
            rows.append({"id": d["id"], "kind": d.get("kind"), "visible": d.get("visible"), "grid_content": found})
            if not d.get("visible"):
                bad.append("%s 가 visible 아님" % d["id"])
            if d.get("kind") == "grid" and found is False:
                bad.append("%s 그리드 내용이 실현되지 않음" % d["id"])
        out["docs"] = rows
        out["idle"] = idle_state(r)
        if not docs:
            bad.append("복원된 문서가 없음")
        if out["idle"]["scopes"]:
            bad.append("스코프가 남음: %s" % out["idle"]["scopes"])
    out["violations"] = bad
    os.makedirs(os.path.dirname(os.path.abspath(a.out)), exist_ok=True)
    with open(a.out, "w", encoding="utf-8") as f:
        json.dump(out, f, ensure_ascii=False, indent=1)
    print("mode=%s 위반 %d건 %s" % (a.mode, len(bad), bad))
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
