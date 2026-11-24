# 브라우저(WASM) 에서 ?perf-ui 시나리오를 돌려 PERF_JSON 의 조작별 p50/p95 를 JSON 으로 저장하는 스크립트(소프트웨어 렌더 참고치)
import json, os, sys, time
from playwright.sync_api import sync_playwright
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE)
import retest_browser2 as t
with sync_playwright() as p:
    b = p.chromium.launch(headless=True, args=t.ARGS)
    pg = b.new_page(viewport={"width": 1280, "height": 800})
    pg.goto(t.URL + "?cmd=perf-ui&perf-out=x&frame-pump=on")
    t0 = time.time(); js = None
    while time.time() - t0 < 900:
        time.sleep(5)
        logs = pg.evaluate("() => (window.__pdPoc && window.__pdPoc.logs) || []")
        js = next((l for l in logs if l.startswith("PERF_JSON:")), None)
        if js: break
        print("대기", int(time.time() - t0), "s", len(logs), flush=True)
    if js:
        d = json.loads(js[len("PERF_JSON:"):])
        json.dump(d, open(os.path.join(t.OUT, "perf_ui_web.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=1)
        ops = d.get("operations") or []
        print("ops", len(ops))
        for o in ops:
            print("%-40s n=%s sync p50=%s p95=%s frame p50=%s p95=%s" % (o.get("name"), o.get("count"), o.get("sync_p50_ms"), o.get("sync_p95_ms"), o.get("frame_p50_ms"), o.get("frame_p95_ms")))
    else:
        print("PERF_JSON 없음", [l[:100] for l in logs][-5:])
