# 웹 추가 확인 — 폰 폭(모바일형 NAV)에서 도움말 투어가 Nav 범위로 시작되고(단계 6), 마지막 단축키 층에 예약키 '웹에서는 사용 불가' 표기가 같이 나오는지.
# 기대값(측정 전 고정): scope=Nav, count=6, 단축키 층 도달, 예약키 6개(next·prev·close·windowMenu·exit·macClose)에 ko 문구 '웹에서는 사용 불가', 콘솔 오류 0.
# 실행: python3 -B ui28_web_tournav.py --bundle <wwwroot> --out <json> --shots <폴더> [--port 5821]
import argparse
import http.server
import json
import os
import socketserver
import sys
import threading
import time
from functools import partial

from playwright.sync_api import sync_playwright

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from ui28_web706 import W  # noqa: E402

FLAGGED = ["next", "prev", "close", "windowMenu", "exit", "macClose"]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--bundle", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--shots", required=True)
    ap.add_argument("--port", type=int, default=5821)
    a = ap.parse_args()
    os.makedirs(a.shots, exist_ok=True)
    http.server.SimpleHTTPRequestHandler.log_message = lambda *x: None
    httpd = socketserver.TCPServer(("127.0.0.1", a.port), partial(http.server.SimpleHTTPRequestHandler, directory=a.bundle))
    threading.Thread(target=httpd.serve_forever, daemon=True).start()
    errors, res = [], {"started_at": time.strftime("%Y-%m-%dT%H:%M:%S%z")}
    t0 = time.time()
    try:
        with sync_playwright() as p:
            w = W(p, a.port, a.bundle, (390, 844), a.shots, errors)
            res["effective"] = (w.res("ui.style") or {}).get("effective")
            st = w.call("tour.start")
            res["start"] = st.get("result") if st.get("ok") else st
            time.sleep(1.0)
            steps = []
            for _ in range(12):
                s = (w.res("tour.state") or [{}])[0]
                steps.append({"stepId": s.get("stepId"), "index": s.get("index"), "count": s.get("count"), "scope": s.get("scope"), "onShortcutLayer": s.get("onShortcutLayer")})
                if s.get("onShortcutLayer"):
                    break
                w.call("tour.next")
                time.sleep(0.6)
            res["steps"] = steps
            w.shot("tournav_shortcuts")
            notes = {}
            for k in FLAGGED:
                r = w.res("ui.find", id="tour.keyNote." + k)
                notes[k] = {"text": r.get("text"), "visible": r.get("effectivelyVisible")} if r else None
            res["notes"] = notes
            res["tour_end"] = w.res("tour.end")
            w.b.close()
    except Exception as e:  # noqa: BLE001
        res["exception"] = repr(e)[:500]
    last = (res.get("steps") or [{}])[-1]
    why = []
    if last.get("scope") != "Nav":
        why.append("scope %r != Nav" % last.get("scope"))
    if last.get("count") != 6:
        why.append("count %r != 6" % last.get("count"))
    if not last.get("onShortcutLayer"):
        why.append("단축키 층에 도달하지 못함")
    for k in FLAGGED:
        n = (res.get("notes") or {}).get(k)
        if not n or n.get("text") != "웹에서는 사용 불가" or n.get("visible") is not True:
            why.append("%s 표기 %r" % (k, n))
    if errors:
        why.append("콘솔 오류 %d건" % len(errors))
    res["console_errors"] = errors
    res["verdict"] = "PASS" if not why and "exception" not in res else "FAIL"
    res["reasons"] = why
    res["elapsed_ms"] = round((time.time() - t0) * 1000)
    with open(a.out, "w", encoding="utf-8") as f:
        json.dump(res, f, ensure_ascii=False, indent=1)
    httpd.shutdown()
    print("saved", a.out, res["verdict"])


if __name__ == "__main__":
    main()
