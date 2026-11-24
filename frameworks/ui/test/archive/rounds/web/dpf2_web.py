# D-PF2 웹 headless 몫(절차서 dpf2_procedure.md 6절) — T·M-top·M-max 조건에서 CDP 터치 끌기 처리 시간과 getMetrics 차분을 잰다(otest-w-1)
import json
import os
import statistics as stt
import sys
import time

from playwright.sync_api import sync_playwright

sys.path.insert(0, "/mnt/c/DATA/Project/pd/poc/ui/tools")
import retest_browser2 as t  # noqa: E402

URL = os.environ.get("PD_URL", "http://127.0.0.1:5251/")
t.URL = URL
OUT = "/mnt/c/DATA/Project/pd/poc/ui/logs/otest_w1/dpf2"
os.makedirs(OUT, exist_ok=True)
ARGS = ["--use-angle=swiftshader", "--enable-unsafe-swiftshader", "--ignore-gpu-blocklist", "--enable-webgl"]
PROBE_JS = r"""(() => {
  if (window.__pd) { const s = window.__pd; for (const k of ['down','move','up','cancel','coalesced','handlerMs','handlerMax']) s[k] = 0; return 'reset'; }
  const s = window.__pd = { down: 0, move: 0, up: 0, cancel: 0, coalesced: 0, raf: 0, handlerMs: 0, handlerMax: 0 };
  let t0 = 0;
  const cap = e => { if (e.pointerType !== 'touch') return; if (e.type === 'pointerdown') s.down++; if (e.type === 'pointermove') { s.move++; t0 = performance.now(); } if (e.type === 'pointerup') s.up++; if (e.type === 'pointercancel') s.cancel++; };
  const bub = e => { if (e.pointerType === 'touch' && e.type === 'pointermove' && t0) { const d = performance.now() - t0; s.handlerMs += d; s.handlerMax = Math.max(s.handlerMax, d); t0 = 0; } };
  for (const ty of ['pointerdown', 'pointermove', 'pointerup', 'pointercancel']) { document.addEventListener(ty, cap, { capture: true, passive: true }); window.addEventListener(ty, bub, { capture: false, passive: true }); }
  const f = () => { s.raf++; requestAnimationFrame(f); }; requestAnimationFrame(f);
  return 'ok'; })()"""
RAF_JS = "() => { window.__raf = {t: [], on: true}; const f = ts => { if (!window.__raf.on) return; window.__raf.t.push(ts); requestAnimationFrame(f); }; requestAnimationFrame(f); }"
RAF_STOP = "() => { window.__raf.on = false; return window.__raf.t; }"
KEYS = ("TaskDuration", "ScriptDuration", "LayoutDuration", "RecalcStyleDuration", "LayoutCount", "RecalcStyleCount")
res = {"url": URL, "started": time.strftime("%Y-%m-%d %H:%M:%S"), "rows": []}


def load1():
    return float(open("/proc/loadavg").read().split()[0])


def gate_load():
    for _ in range(40):
        if load1() <= 8:
            return True
        time.sleep(15)
    return False


def node_count(r):
    tr = r.call("tree.get", window=0, named=False, depth=200) or {}
    n = [0]

    def w(x):
        if isinstance(x, list):
            [w(c) for c in x]
        elif isinstance(x, dict):
            if "type" in x:
                n[0] += 1
            [w(v) for v in x.values() if isinstance(v, (list, dict))]
    w(tr)
    return {"nodes": n[0], "truncated": tr.get("truncated")}


def metrics(cdp):
    m = {x["name"]: x["value"] for x in cdp.send("Performance.getMetrics")["metrics"]}
    return {k: m.get(k) for k in KEYS}


def diff(a, b):
    return {k: round((b[k] or 0) - (a[k] or 0), 4) for k in KEYS}


def rstats(ts):
    if len(ts) < 3:
        return {"n": 0}
    iv = sorted(b - a for a, b in zip(ts, ts[1:]))
    q = lambda p: iv[min(len(iv) - 1, int(round(p * (len(iv) - 1))))]
    return {"n": len(iv), "median": round(q(.5), 2), "p95": round(q(.95), 2), "max": round(iv[-1], 2)}


def grid_info(r, G):
    gb = (r.call("ui.find", id="grid.dgMain", doc=G) or {}).get("bounds")
    gv = r.call("grid.visible", id="grid.dgMain", doc=G) or {}
    return gb, gv


def setup(r, cond, G):
    if cond == "T":
        r.call("docs.arrange", preset="Tabbed")
        time.sleep(3)
        r.call("docs.activate", doc=G)
    else:
        r.call("docs.arrange", preset="MdiCascade")
        time.sleep(3)
        r.call("docs.activate", doc=G)
        if cond == "M-max":
            r.call("docs.state", state="maximized", doc=G)
    time.sleep(4)


def drag(page, cdp, r, G, mode):
    gb, gv0 = grid_info(r, G)
    x = gb["x"] + (230 if gb["w"] > 300 else gb["w"] * 0.55)
    y0, y1 = gb["y"] + gb["h"] * 0.8, gb["y"] + gb["h"] * 0.2
    steps, gap = (60, 1000 / 60.0) if mode == "A" else (240, 1000 / 240.0)
    r.call("grid.select", id="grid.dgMain", doc=G, index=0, scroll=True)
    time.sleep(1.0)
    gv0 = r.call("grid.visible", id="grid.dgMain", doc=G) or {}
    page.evaluate(PROBE_JS)
    page.evaluate(RAF_JS)
    m0 = metrics(cdp)
    t0 = time.time()
    cdp.send("Input.dispatchTouchEvent", {"type": "touchStart", "touchPoints": [{"x": x, "y": y0, "id": 1}]})
    for i in range(1, steps + 1):
        tgt = t0 + i * gap / 1000.0
        d = tgt - time.time()
        if d > 0:
            time.sleep(d)
        cdp.send("Input.dispatchTouchEvent", {"type": "touchMove", "touchPoints": [{"x": x, "y": y0 + (y1 - y0) * i / steps, "id": 1}]})
    t1 = time.time()
    time.sleep(0.06)
    cdp.send("Input.dispatchTouchEvent", {"type": "touchEnd", "touchPoints": []})
    raf = page.evaluate(RAF_STOP)
    pd = page.evaluate("() => Object.assign({}, window.__pd)")
    time.sleep(5)
    m1 = metrics(cdp)
    gv1 = r.call("grid.visible", id="grid.dgMain", doc=G) or {}
    mv = pd.get("move") or 0
    # 끌기 구간만의 rAF(끌기 시작~끝 시각 기준 전체가 아닌 수집분 = 끌기 중 + 마지막 touchEnd 직전까지)
    finger = y0 - y1
    d_off = (gv1.get("offsetY") or 0) - (gv0.get("offsetY") or 0)
    return {"mode": mode, "steps": steps, "drag_wall_ms": round((t1 - t0) * 1000), "grid_bounds": gb, "finger_css": round(finger, 1),
            "d_offsetY": round(d_off, 1), "ratio": round(d_off / finger, 3) if finger else None,
            "raf": rstats(raf), "handlerMax": round(pd.get("handlerMax") or 0, 1), "handlerMs_per_move": round(pd["handlerMs"] / mv, 2) if mv else None,
            "move": mv, "cancel": pd.get("cancel"), "metrics_diff_5s": diff(m0, m1)}


def idle_baseline(page, cdp):
    page.evaluate(RAF_JS)
    m0 = metrics(cdp)
    time.sleep(5)
    raf = page.evaluate(RAF_STOP)
    m1 = metrics(cdp)
    return {"raf": rstats(raf), "metrics_diff_5s": diff(m0, m1)}


def main():
    with sync_playwright() as p:
        b = p.chromium.launch(headless=True, args=ARGS)
        ctx = b.new_context(viewport={"width": 1280, "height": 800}, has_touch=True)
        t.allow_dup(ctx)
        page = ctx.new_page()
        page.goto(URL + "?fresh")
        page.wait_for_function("() => window.pdAutomation && window.pdAutomation.invoke", timeout=60000)
        import retest_browser as rb
        r = rb.WebRemote(page)
        for _ in range(90):
            if r.call("status") is not None and r.call("docs.list") is not None:
                break
            time.sleep(1)
        ds = r.call("docs.list") or []
        res["docs_at_open"] = {"count": len(ds), "kinds": [d.get("kind") for d in ds]}
        if not any(d.get("kind") == "grid" for d in ds):
            r.call("docs.open", kind="grid")
            time.sleep(3)
            res["docs_at_open"]["note"] = "그리드를 직접 열었다"
        r.call("docs.open", kind="form", count=13)
        time.sleep(6)
        ds = r.call("docs.list") or []
        res["docs_count_14"] = len(ds)
        G = next(d["id"] for d in ds if d.get("kind") == "grid")
        res["env"] = {"viewport": "1280x800", "dpr": page.evaluate("() => devicePixelRatio"), "frame_pump": (r.call("perf.get") or {}).get("frame_pump"), "load1_start": load1()}
        cdp = ctx.new_cdp_session(page)
        cdp.send("Performance.enable")
        order = ["T", "M-top", "M-max"]
        base = {}
        for lap in range(3):
            conds = order[lap % 3:] + order[:lap % 3]
            for cond in conds:
                setup(r, cond, G)
                if lap == 0:
                    lay = r.call("layout.get")
                    json.dump({"layout": lay, "tree": node_count(r)}, open("%s/dpf2_layout_%s.json" % (OUT, cond), "w"), ensure_ascii=False, indent=1)
                    base[cond] = idle_baseline(page, cdp)
                    json.dump(base[cond], open("%s/dpf2_baseline_%s.json" % (OUT, cond), "w"), ensure_ascii=False, indent=1)
                    print("BASE", cond, json.dumps(base[cond]), flush=True)
                for mode in ("A", "B"):
                    ok = gate_load()
                    ld0 = load1()
                    row = drag(page, cdp, r, G, mode)
                    row.update(cond=cond, lap=lap + 1, load1_before=ld0, load1_gate_ok=ok, frame_pump=(r.call("perf.get") or {}).get("frame_pump"))
                    res["rows"].append(row)
                    json.dump(row, open("%s/dpf2_%s_%s_r%d.json" % (OUT, cond, mode, lap + 1), "w"), ensure_ascii=False, indent=1)
                    print(cond, mode, lap + 1, json.dumps({k: row[k] for k in ("handlerMax", "handlerMs_per_move", "ratio", "raf", "load1_before")}), "script", row["metrics_diff_5s"]["ScriptDuration"], flush=True)
                    time.sleep(2)
                time.sleep(5)
        res["baseline"] = base
        b.close()
    json.dump(res, open(OUT + "/dpf2_web_all.json", "w"), ensure_ascii=False, indent=1)
    open(OUT + "/dpf2.done", "w").write("done\n")


main()
