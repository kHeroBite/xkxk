# 웹 perf.idle 유휴 rAF 출처 가르기(i1320 진단판, odev-3·lead) — wait_for_selector 유무 2변형 × 4구간(기본 → stop=fps → stop=probe → resume=all), 구간마다 rAF 초당·호출 위치 상위·startAnimationFrames·perf.idle frames 처음/끝
# 사용: PD_URL=http://127.0.0.1:5215/ python3 tools/retest_browser28.py sel nosel   (진단판·헤드리스 전용, dpr 1, 800×700, 제품 그리드 1개)
import json
import os
import sys
import time

from playwright.sync_api import sync_playwright

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import retest_browser as rb  # noqa: E402
import retest_browser2 as t  # noqa: E402
from retest_browser16 import WRAP, origin  # noqa: E402

OUT = t.OUT
REST = 2.0
MEASURE = 8.0


def segment(page, r, name, cmd_args):
    if cmd_args is not None:
        r.call("perf.idle", **cmd_args)
        time.sleep(REST)
    p0 = r.call("perf.idle") or {}
    page.evaluate("() => { window.__raf.n = 0; window.__raf.sites = {}; window.__raf.on = true; }")
    t0 = time.time()
    time.sleep(MEASURE)
    st = page.evaluate("() => { window.__raf.on = false; return window.__raf; }")
    secs = time.time() - t0
    p1 = r.call("perf.idle") or {}
    restarts = sum(n for s, n in st["sites"].items() if "startAnimationFrames" in s)
    by_origin = {}
    for s, n in st["sites"].items():
        by_origin[origin(s)] = by_origin.get(origin(s), 0) + n
    return {"segment": name, "cmd": cmd_args, "per_sec": round(st["n"] / secs, 1), "raf_total": st["n"], "secs": round(secs, 2),
            "startAnimationFrames": restarts, "by_origin": by_origin,
            "sites_top": sorted(st["sites"].items(), key=lambda x: -x[1])[:5],
            "perf_idle_start": p0, "perf_idle_end": p1,
            "frames_delta": (p1.get("frames") - p0.get("frames")) if isinstance(p1.get("frames"), (int, float)) and isinstance(p0.get("frames"), (int, float)) else None}


def run(b, variant):
    ctx = t.allow_dup(b.new_context(viewport={"width": 800, "height": 700}, device_scale_factor=1))
    ctx.add_init_script(WRAP)
    page = ctx.new_page()
    page.goto(t.URL + "?layout-key=otestw2-perfidle-" + variant)
    if variant == "sel":
        page.wait_for_selector("canvas.avalonia-canvas", timeout=60000)
    page.wait_for_function("() => window.pdAutomation && window.pdAutomation.invoke", timeout=60000)
    r = rb.WebRemote(page)
    for _ in range(90):
        if r.call("status") is not None and r.call("docs.list") is not None:
            break
        time.sleep(1)
    r.call("docs.close", all=True)
    r.call("docs.open", kind="grid", title="PI-grid", allowDuplicate=True)
    time.sleep(8.0)
    segs = [segment(page, r, "① 기본", None),
            segment(page, r, "② stop=fps", {"stop": "fps"}),
            segment(page, r, "③ stop=probe", {"stop": "probe"}),
            segment(page, r, "④ resume=all", {"resume": "all"})]
    page.screenshot(path=os.path.join(OUT, "perfidle_%s.png" % variant))
    ctx.close()
    return {"variant": variant, "wait_for_selector": variant == "sel", "segments": segs}


def main():
    variants = sys.argv[1:] or ["sel", "nosel"]
    rows = []
    with sync_playwright() as p:
        b = p.chromium.launch(headless=True, args=t.ARGS)
        for v in variants:
            row = run(b, v)
            rows.append(row)
            for s in row["segments"]:
                print(json.dumps({"variant": v, "segment": s["segment"], "per_sec": s["per_sec"], "startAnimationFrames": s["startAnimationFrames"],
                                  "frames_delta": s["frames_delta"], "probe_end": s["perf_idle_end"].get("probe"), "fpsPaused_end": s["perf_idle_end"].get("fpsPaused")}, ensure_ascii=False), flush=True)
            with open(os.path.join(OUT, "retest_web28_perfidle.json"), "w", encoding="utf-8") as f:
                json.dump(rows, f, ensure_ascii=False, indent=1)
        b.close()


if __name__ == "__main__":
    main()
