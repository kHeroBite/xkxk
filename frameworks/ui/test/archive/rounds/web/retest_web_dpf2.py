# D-PF2 Fold5 Chrome 측정(문서 14개, T·M-top·M-max·M-bot 각 3회 교대 끌기, 주사율·CDP getMetrics 차분 포함)을 dpf2_procedure.md(89e822da)대로 재는 스크립트(otest-m-4)
"""
사용: python3 -E -u tools/retest_web_dpf2.py --url-part 127.0.0.1:5261 --out-dir logs/otest_m3/<판>/dpf2 [--rounds 3] [--oy 297] [--build i1835rc]
읽기만 하는 주사율 기록(SurfaceFlinger activeMode / display renderFrameRate)을 끌기 직전·직후에 남긴다. 기기 설정은 바꾸지 않는다.
"""
import argparse
import json
import os
import subprocess
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from cdp_bridge import Bridge  # noqa: E402
from retest_web_dpf1 import RAF_JS, RAF_STOP, stats, count_nodes  # noqa: E402
from retest_web_dpf1c import display_rate  # noqa: E402
from retest_web_scroll_ratio import PROBE_JS, PD_READ  # noqa: E402

ADB = "/mnt/c/Program Files (x86)/Android/android-sdk/platform-tools/adb.exe"
KEYS = ("TaskDuration", "ScriptDuration", "LayoutDuration", "RecalcStyleDuration", "LayoutCount")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--url-part", required=True)
    ap.add_argument("--out-dir", required=True)
    ap.add_argument("--oy", type=float, default=297.0)
    ap.add_argument("--serial", default="R3CW70202FP")
    ap.add_argument("--rounds", type=int, default=3)
    ap.add_argument("--build", default="i1835rc")
    ap.add_argument("--conds", default="T,M-top,M-max,M-bot")
    ap.add_argument("--load-max", type=float, default=8.0)
    a = ap.parse_args()
    O = a.out_dir
    os.makedirs(O, exist_ok=True)
    b = Bridge(a.url_part)
    sl = time.sleep

    def jd(name, obj):
        obj = dict(obj, _at=time.strftime("%Y-%m-%dT%H:%M:%S"), _host_load=[round(x, 2) for x in os.getloadavg()], _build=a.build)
        with open(os.path.join(O, name), "w", encoding="utf-8") as f:
            json.dump(obj, f, ensure_ascii=False, indent=1)

    def metrics():
        r = b.cdp("Performance.getMetrics")
        if not isinstance(r, dict) or "metrics" not in r:
            return None
        return {m["name"]: m["value"] for m in r["metrics"] if m["name"] in KEYS}

    def mdiff(m0, m1):
        if not m0 or not m1:
            return None
        return {k: round(m1[k] - m0[k], 4) for k in KEYS if k in m0 and k in m1}

    for _ in range(40):
        if b.eval('typeof pdAutomation !== "undefined"') is True:
            break
        sl(1)
    sl(2)
    ds = b.call("docs.list") or []
    head = {"url": b.url, "docs_at_open": len(ds), "docs_at_open_kinds": [d.get("kind") for d in ds],
            "metrics_env": b.eval("({w:innerWidth,h:innerHeight,dpr:devicePixelRatio})")}
    head["frame_pump_at_open"] = (b.call("perf.get") or {}).get("frame_pump")
    cdp_on = b.cdp("Performance.enable")
    head["cdp_performance_enable"] = cdp_on if not (isinstance(cdp_on, dict) and "__error" in cdp_on) else cdp_on
    print("open", head, flush=True)
    if not any(d.get("kind") == "grid" for d in ds):
        b.call("docs.open", kind="grid")
        sl(3)
    if len(b.call("docs.list") or []) < 14:  # 이미 14개면 다시 열지 않는다
        b.call("docs.open", kind="form", count=13)
        sl(3)
    ds = b.call("docs.list") or []
    head["docs_list_count"] = len(ds)
    G = [d.get("id") for d in ds if d.get("kind") == "grid"][0]
    dpr = head["metrics_env"]["dpr"]
    sl(5)
    print("docs", len(ds), "grid", G, flush=True)

    def setup(cond):
        if cond == "T":
            b.call("docs.arrange", preset="Tabbed")
            b.call("docs.activate", doc=G)
        elif cond == "M-top":
            b.call("docs.arrange", preset="MdiCascade")
            b.call("docs.activate", doc=G)
        elif cond == "M-max":
            b.call("docs.arrange", preset="MdiCascade")
            b.call("docs.activate", doc=G)
            b.call("docs.state", state="maximized", doc=G)
        elif cond == "M-bot":
            b.call("docs.arrange", preset="MdiCascade")
        elif cond == "M-bot2":  # 그리드를 실제 맨 아래로: 배치 뒤 그리드가 아닌 문서를 차례로 활성화한다(그리드는 활성화하지 않음)
            b.call("docs.arrange", preset="MdiCascade")
            for d in b.call("docs.list") or []:
                if d.get("id") != G:
                    b.call("docs.activate", doc=d.get("id"))
        sl(5)

    def wait_load():
        waited = 0
        while os.getloadavg()[0] > a.load_max and waited < 120:
            sl(5)
            waited += 5
        return waited

    def baseline(cond):
        m0 = metrics()
        b.eval(RAF_JS)
        sl(5)
        st = stats(json.loads(b.eval(RAF_STOP)), drop_ms=0)
        m1 = metrics()
        return {"cond": cond, "rest_raf": st, "cdp_diff_5s": mdiff(m0, m1), "display": display_rate(a.serial)}

    def drag(cond, rnd):
        waited = wait_load()
        load = [round(x, 2) for x in os.getloadavg()]
        b.call("grid.select", doc=G, index=0, scroll=True)
        sl(1.0)
        zi = [dd.get("ZIndex") for dd in ((b.call("layout.get") or {}).get("Documents") or []) if dd.get("Id") == G]
        gb = (b.call("ui.find", id="grid.dgMain", doc=G) or {}).get("bounds")
        if not gb:
            return {"error": "grid bounds 없음"}
        fp = (b.call("perf.get") or {}).get("frame_pump")
        colx = gb["x"] + (230 if gb["w"] > 300 else gb["w"] * 0.55)
        y0, y1 = gb["y"] + gb["h"] * 0.8, gb["y"] + gb["h"] * 0.2
        v0 = b.call("grid.visible", id="grid.dgMain", doc=G) or {}
        d0 = display_rate(a.serial)
        m0 = metrics()
        b.eval(PROBE_JS)
        b.eval(RAF_JS)
        p0 = json.loads(b.eval(PD_READ))
        act = [str(int(round(colx * dpr))), str(int(round(y0 * dpr + a.oy))), str(int(round(colx * dpr))), str(int(round(y1 * dpr + a.oy))), "1000"]
        subprocess.run([ADB, "-s", a.serial, "shell", "input", "swipe"] + act, capture_output=True, timeout=60)
        ts = json.loads(b.eval(RAF_STOP))
        p1 = json.loads(b.eval(PD_READ))
        d1 = display_rate(a.serial)
        sl(5)
        m1 = metrics()
        v1 = b.call("grid.visible", id="grid.dgMain", doc=G) or {}
        st = stats(ts, drop_ms=0)
        fing = round(y0 - y1, 1)
        dy = round((v1.get("offsetY") or 0) - (v0.get("offsetY") or 0), 1)
        changed = d0.get("sf_vsync") != d1.get("sf_vsync") or d0.get("render_rate") != d1.get("render_rate")
        lay = b.call("layout.get") or {}
        return {"cond": cond, "round": rnd, "host_load": load, "load_wait_s": waited, "load_over": load[0] > a.load_max, "frame_pump": fp,
                "gb": gb, "adb": act, "raf_during": st, "raf_count": len(ts), "handlerMax": round(p1.get("handlerMax") or 0, 1),
                "handlerMs_per_move": round((p1.get("handlerMs") or 0) / p1["move"], 2) if p1.get("move") else None, "move": p1.get("move"), "cancel": p1.get("cancel"),
                "d_offsetY": dy, "finger_css": fing, "scroll_ratio": round(dy / fing, 3) if fing else None,
                "display_before": d0, "display_after": d1, "refresh_changed": changed, "cdp_diff_5s": mdiff(m0, m1),
                "layout_after": {"Mode": lay.get("Mode")}, "grid_zindex_before_drag": zi, "grid_zindex_after_drag": [dd.get("ZIndex") for dd in (lay.get("Documents") or []) if dd.get("Id") == G]}

    for rnd in range(1, a.rounds + 1):
        for cond in a.conds.split(","):
            setup(cond)
            if rnd == 1:
                lay = b.call("layout.get")
                tree = b.call("tree.get", named=False, depth=60) or {}
                nodes = tree.get("nodes") if isinstance(tree, dict) and isinstance(tree.get("nodes"), int) else count_nodes(tree)
                jd("dpf2_layout_%s.json" % cond, {"layout_get": lay, "tree_nodes_depth60": nodes, "docs": len(b.call("docs.list") or [])})
                jd("dpf2_baseline_%s.json" % cond, baseline(cond))
            r = drag(cond, rnd)
            jd("dpf2_%s_%d.json" % (cond, rnd), r)
            print(cond, rnd, json.dumps({k: r.get(k) for k in ("host_load", "frame_pump", "handlerMax", "handlerMs_per_move", "scroll_ratio", "refresh_changed", "d_offsetY")}, ensure_ascii=False), (r.get("raf_during") or {}).get("median_ms"), flush=True)
    jd("dpf2_head_%s.json" % a.conds.replace(",", "_"), head)
    b.close()


if __name__ == "__main__":
    main()
