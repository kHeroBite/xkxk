# D-PF2 2회차(절차서 v2) Fold5 Chrome 측정 — 끌기마다 앱 재로드, R1(zoom_*)·R5(grid_probe_move_*) 계수 차분 기록(otest-m-6)
"""
사용: python3 -E -u tools/retest_web_dpf2r2.py --url-part 127.0.0.1:<포트> --out-dir logs/otest_m3/i2350rc/dpf2r2 [--rounds 3] [--round-start 1] [--conds T,M-top,M-max,M-bot2]
기기 설정은 바꾸지 않는다(주사율은 읽기만). 끌기마다 Page.navigate 로 앱을 다시 띄우고 문서 14개·조건을 다시 만든다.
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
CNT = ("zoom_measure_calls", "zoom_measure_size_changed", "zoom_arrange_calls")
PRB = ("grid_probe_move_first_ms_max", "grid_probe_move_rest_ms_max")
EXPECT = {"M-top": (13, "Normal"), "M-max": (13, "Maximized"), "M-bot2": (0, "Normal")}


def flat(o, want, out=None):
    out = {} if out is None else out
    if isinstance(o, dict):
        for k, v in o.items():
            if k in want:
                out[k] = v
            flat(v, want, out)
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--url-part", required=True)
    ap.add_argument("--out-dir", required=True)
    ap.add_argument("--oy", type=float, default=297.0)
    ap.add_argument("--serial", default="R3CW70202FP")
    ap.add_argument("--rounds", type=int, default=3)
    ap.add_argument("--round-start", type=int, default=1)
    ap.add_argument("--conds", default="T,M-top,M-max,M-bot2")
    ap.add_argument("--load-max", type=float, default=8.0)
    ap.add_argument("--build", default="i2350rc")
    a = ap.parse_args()
    O = a.out_dir
    os.makedirs(O, exist_ok=True)
    b = Bridge(a.url_part)
    URL = b.url
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

    def counters():
        return flat(b.call("perf.get") or {}, CNT + PRB)

    def reload():
        b.cdp("Page.navigate", url=URL)
        sl(4)
        for _ in range(60):
            if b.eval('typeof pdAutomation !== "undefined"') is True:
                break
            sl(1)
        sl(3)
        b.cdp("Performance.enable")
        ds = b.call("docs.list") or []
        n0 = len(ds)
        if not any(d.get("kind") == "grid" for d in ds):
            b.call("docs.open", kind="grid")
            sl(3)
        n1 = len(b.call("docs.list") or [])
        if n1 < 14:
            b.call("docs.open", kind="form", count=14 - n1)
            sl(4)
        ds = b.call("docs.list") or []
        gid = [d.get("id") for d in ds if d.get("kind") == "grid"][0]
        return n0, len(ds), gid

    def setup(cond, G):
        if cond == "T":
            b.call("docs.arrange", preset="Tabbed")
            b.call("docs.activate", doc=G)
        elif cond in ("M-top", "M-max"):
            b.call("docs.arrange", preset="MdiCascade")
            b.call("docs.activate", doc=G)
            if cond == "M-max":
                b.call("docs.state", state="maximized", doc=G)
        elif cond == "M-bot2":
            b.call("docs.arrange", preset="MdiCascade")
            for d in b.call("docs.list") or []:
                if d.get("id") != G:
                    b.call("docs.activate", doc=d.get("id"))
        sl(5)

    def gstate(G):
        lay = b.call("layout.get") or {}
        docs = lay.get("Documents") or []
        g = [d for d in docs if d.get("Id") == G]
        return lay, docs, (g[0] if g else {})

    def uncovered_x(docs, g):
        """M-bot2: 그리드 왼쪽 가장자리 안쪽(8~16px, 컨테이너 좌표 g.X+~10)이 다른 창에 덮이지 않았는가(근사 — 판정은 d_offsetY 로 최종 확인)."""
        cx = (g.get("X") or 0) + 10
        for d in docs:
            if d.get("Id") == g.get("Id"):
                continue
            if (d.get("X") or 0) <= cx <= (d.get("X") or 0) + (d.get("Width") or 0):
                return False
        return True

    def wait_load():
        w = 0
        while os.getloadavg()[0] > a.load_max and w < 120:
            sl(5)
            w += 5
        return w

    def baseline(cond):
        m0 = metrics()
        b.eval(RAF_JS)
        sl(5)
        st = stats(json.loads(b.eval(RAF_STOP)), drop_ms=0)
        return {"cond": cond, "rest_raf": st, "cdp_diff_5s": mdiff(m0, metrics()), "display": display_rate(a.serial)}

    def drag(cond, rnd, G, reload_info):
        waited = wait_load()
        load = [round(x, 2) for x in os.getloadavg()]
        b.call("grid.select", doc=G, index=0, scroll=True)
        sl(1.0)
        lay, docs, g = gstate(G)
        exp = EXPECT.get(cond)
        state_ok = True if exp is None else (g.get("ZIndex") == exp[0] and g.get("MdiState") == exp[1])
        gb = (b.call("ui.find", id="grid.dgMain", doc=G) or {}).get("bounds")
        if not gb:
            return {"cond": cond, "round": rnd, "error": "grid bounds 없음", "state_ok": state_ok, "grid_layout": g}
        dpr = b.eval("devicePixelRatio")
        unc = uncovered_x(docs, g) if cond == "M-bot2" else None
        strip = None
        if cond == "M-bot2":
            # 그리드 오른쪽에서 다른 창에 덮이지 않은 띠(컨테이너 좌표 → CSS 는 gb.x - g.X 로 환산, 안쪽 여백은 0 가정)의 중앙
            others = [d for d in docs if d.get("Id") != g.get("Id")]
            max_r = max([(d.get("X") or 0) + (d.get("Width") or 0) for d in others] or [0])
            off = gb["x"] - (g.get("X") or 0)
            lo, hi = max_r + off, min(gb["x"] + gb["w"], (g.get("X") or 0) + (g.get("Width") or 0) + off)
            strip = {"css_lo": round(lo, 1), "css_hi": round(hi, 1), "width": round(hi - lo, 1)}
            colx = (lo + hi) / 2 if hi - lo >= 6 else gb["x"] + 8
        if cond != "M-bot2":
            colx = gb["x"] + (230 if gb["w"] > 300 else gb["w"] * 0.55)
        y0, y1 = gb["y"] + gb["h"] * 0.8, gb["y"] + gb["h"] * 0.2
        fp = (b.call("perf.get") or {}).get("frame_pump")
        v0 = b.call("grid.visible", id="grid.dgMain", doc=G) or {}
        d0 = display_rate(a.serial)
        m0 = metrics()
        c0 = counters()
        b.eval(PROBE_JS)
        b.eval(RAF_JS)
        p0 = json.loads(b.eval(PD_READ))
        act = [str(int(round(colx * dpr))), str(int(round(y0 * dpr + a.oy))), str(int(round(colx * dpr))), str(int(round(y1 * dpr + a.oy))), "1000"]
        subprocess.run([ADB, "-s", a.serial, "shell", "input", "swipe"] + act, capture_output=True, timeout=60)
        ts = json.loads(b.eval(RAF_STOP))
        p1 = json.loads(b.eval(PD_READ))
        c1 = counters()
        d1 = display_rate(a.serial)
        sl(5)
        m1 = metrics()
        v1 = b.call("grid.visible", id="grid.dgMain", doc=G) or {}
        st = stats(ts, drop_ms=0)
        fing = round(y0 - y1, 1)
        dy = round((v1.get("offsetY") or 0) - (v0.get("offsetY") or 0), 1)
        chg = d0.get("sf_vsync") != d1.get("sf_vsync") or d0.get("render_rate") != d1.get("render_rate")
        diff = {k: (c1[k] - c0[k]) if (k in c0 and k in c1 and c0[k] is not None and c1[k] is not None) else None for k in CNT}
        return {"cond": cond, "round": rnd, "host_load": load, "load_wait_s": waited, "load_over": load[0] > a.load_max, "frame_pump": fp,
                "docs_at_reload_before_fill": reload_info[0], "docs_list_count": reload_info[1],
                "gb": gb, "adb": act, "raf_during": st, "raf_count": len(ts), "handlerMax": round(p1.get("handlerMax") or 0, 1),
                "handlerMs_per_move": round((p1.get("handlerMs") or 0) / p1["move"], 2) if p1.get("move") else None, "move": p1.get("move"), "cancel": p1.get("cancel"),
                "d_offsetY": dy, "finger_css": fing, "scroll_ratio": round(dy / fing, 3) if fing else None,
                "display_before": d0, "display_after": d1, "refresh_changed": chg, "cdp_diff_5s": mdiff(m0, m1),
                "r1_counters_before": {k: c0.get(k) for k in CNT}, "r1_counters_after": {k: c1.get(k) for k in CNT}, "r1_diff": diff,
                "r5_after_drag": {k: c1.get(k) for k in PRB},
                "grid_layout_before_drag": g, "state_ok": state_ok, "expected": exp, "m_bot2_uncovered_strip_est": unc, "m_bot2_right_strip_css": strip, "swipe_x_css": round(colx, 1),
                "valid": bool(state_ok and dy != 0), "invalid_reason": None if (state_ok and dy != 0) else ("state_mismatch" if not state_ok else "d_offsetY=0")}

    for rnd in range(a.round_start, a.round_start + a.rounds):
        for cond in a.conds.split(","):
            info = reload()
            G = info[2]
            setup(cond, G)
            if rnd == a.round_start and not os.path.exists(os.path.join(O, "dpf2r2_layout_%s.json" % cond)):
                lay = b.call("layout.get")
                tree = b.call("tree.get", named=False, depth=60) or {}
                nodes = tree.get("nodes") if isinstance(tree, dict) and isinstance(tree.get("nodes"), int) else count_nodes(tree)
                jd("dpf2r2_layout_%s.json" % cond, {"layout_get": lay, "tree_nodes_depth60": nodes, "docs": len(b.call("docs.list") or []),
                                                     "env": b.eval("({w:innerWidth,h:innerHeight,dpr:devicePixelRatio})")})
                jd("dpf2r2_baseline_%s.json" % cond, baseline(cond))
            r = drag(cond, rnd, G, info)
            jd("dpf2r2_%s_%d.json" % (cond, rnd), r)
            print(cond, rnd, json.dumps({k: r.get(k) for k in ("host_load", "handlerMax", "d_offsetY", "valid", "invalid_reason", "r1_diff", "r5_after_drag", "m_bot2_uncovered_strip_est")}, ensure_ascii=False), flush=True)
    b.close()


if __name__ == "__main__":
    main()
