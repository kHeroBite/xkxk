# D-PF1 Fold5 Chrome 최소 측정(M2 배치 모드·M3 정지 출처, 문서 14개)을 dpf1_fold5_min.md(b5c4807e)대로 재는 스크립트(otest-m-3)
"""
사용: python3 -E -u test/web/measure/retest_web_dpf1.py --url-part 127.0.0.1:5193 --out-dir logs/otest_m3/i1835rc/dpf1 [--oy 297] [--only m2,m3]
정지 rAF: 10초 동안 rAF 시각을 모으고 첫 3초를 버린 뒤 간격 중앙값·p95(ms).
M2 각 조건: 정지 rAF → tree.get(named=false, depth=60) 노드 수 → Grid 활성화 뒤 adb swipe 1000ms(80%→20%) 동안 rAF·handlerMax.
  A 에서만 layout.get 과 MDI 패널 bounds 를 fold5_M2A_layout.json 으로 저장한다.
M3: MDI 14개 상태에서 기준 → perf.idle stop=probe → stop=fps → stop=all → resume=all 마다 정지 rAF 와 perf.idle(읽기) frames.
"""
import argparse
import json
import os
import subprocess
import sys
import time

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "common"))  # D-TESTDIR: test/common/_paths.py
import _paths  # noqa: E402
_paths.add()
from cdp_bridge import Bridge  # noqa: E402
from retest_web_scroll_ratio import PROBE_JS, PD_READ  # noqa: E402

ADB = "/mnt/c/Program Files (x86)/Android/android-sdk/platform-tools/adb.exe"
RAF_JS = r"""(() => { const s = window.__raf = {t: [], on: true};
  (function f(ts) { if (!s.on) return; s.t.push(ts); requestAnimationFrame(f); })(performance.now()); return 'ok'; })()"""
RAF_STOP = "(() => { const s = window.__raf; s.on = false; return JSON.stringify(s.t); })()"


def stats(ts, drop_ms=3000.0):
    if len(ts) < 3:
        return {"n": 0}
    t0 = ts[0]
    kept = [t for t in ts if t - t0 >= drop_ms]
    iv = sorted(b - a for a, b in zip(kept, kept[1:]))
    if not iv:
        return {"n": 0}
    p = lambda q: iv[min(len(iv) - 1, int(round(q * (len(iv) - 1))))]  # noqa: E731
    return {"n": len(iv), "median_ms": round(p(0.5), 2), "p95_ms": round(p(0.95), 2), "max_ms": round(iv[-1], 2), "mean_ms": round(sum(iv) / len(iv), 2)}


def count_nodes(n):
    if isinstance(n, dict):
        return 1 + sum(count_nodes(c) for c in (n.get("children") or []))
    if isinstance(n, list):
        return sum(count_nodes(c) for c in n)
    return 0


def find_types(n, key, out):
    if isinstance(n, dict):
        if key in str(n.get("type", "")):
            out.append({k: n.get(k) for k in ("type", "id", "name", "bounds", "isVisible")})
        for c in n.get("children") or []:
            find_types(c, key, out)
    elif isinstance(n, list):
        for c in n:
            find_types(c, key, out)
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--url-part", required=True)
    ap.add_argument("--out-dir", required=True)
    ap.add_argument("--oy", type=float, default=297.0)
    ap.add_argument("--serial", default="R3CW70202FP")
    ap.add_argument("--only", default="m2,m3")
    a = ap.parse_args()
    only = set(a.only.split(","))
    O = a.out_dir
    os.makedirs(O, exist_ok=True)
    b = Bridge(a.url_part)
    sl = time.sleep

    def jd(name, obj):
        obj = dict(obj, _at=time.strftime("%Y-%m-%dT%H:%M:%S"), _host_load=[round(x, 2) for x in os.getloadavg()], _build="i1835rc")
        with open(os.path.join(O, name), "w", encoding="utf-8") as f:
            json.dump(obj, f, ensure_ascii=False, indent=1)

    def idle_raf(sec=10.0):
        b.eval(RAF_JS)
        sl(sec)
        ts = json.loads(b.eval(RAF_STOP))
        return stats(ts), len(ts)

    summ = {"url": b.url, "rows": []}
    for _ in range(40):
        if b.eval('typeof pdAutomation !== "undefined"') is True:
            break
        sl(1)
    sl(2)
    ds = b.call("docs.list") or []
    summ["docs_at_open"] = {"count": len(ds), "kinds": [d.get("kind") for d in ds]}
    pg = b.call("perf.get") or {}
    summ["frame_pump_at_open"] = pg.get("frame_pump")
    summ["metrics"] = b.eval("({w:innerWidth,h:innerHeight,dpr:devicePixelRatio})")
    print("open", summ["docs_at_open"], "frame_pump", summ["frame_pump_at_open"], flush=True)
    # 0-3: Grid 1 + Form 13
    if not any(d.get("kind") == "grid" for d in ds):
        b.call("docs.open", kind="grid")
        sl(3)
    opened = b.call("docs.open", kind="form", count=13)
    sl(3)
    ds = b.call("docs.list") or []
    summ["docs_after_open"] = {"count": len(ds), "kinds": [d.get("kind") for d in ds], "open_resp_n": len(opened) if isinstance(opened, list) else opened}
    G = [d.get("id") for d in ds if d.get("kind") == "grid"][0]
    sl(5)
    print("docs", summ["docs_after_open"]["count"], flush=True)
    dpr = summ["metrics"]["dpr"]

    def drag():
        b.call("docs.activate", doc=G)
        sl(1.0)
        gb = (b.call("ui.find", id="grid.dgMain", doc=G) or {}).get("bounds")
        if not gb:
            return {"error": "grid bounds 없음"}
        colx = gb["x"] + (230 if gb["w"] > 300 else gb["w"] * 0.55)
        y0, y1 = gb["y"] + gb["h"] * 0.8, gb["y"] + gb["h"] * 0.2
        v0 = b.call("grid.visible", id="grid.dgMain", doc=G) or {}
        b.eval(PROBE_JS)
        b.eval(RAF_JS)
        p0 = json.loads(b.eval(PD_READ))
        act = [str(int(round(colx * dpr))), str(int(round(y0 * dpr + a.oy))), str(int(round(colx * dpr))), str(int(round(y1 * dpr + a.oy))), "1000"]
        subprocess.run([ADB, "-s", a.serial, "shell", "input", "swipe"] + act, capture_output=True, timeout=60)
        ts = json.loads(b.eval(RAF_STOP))
        p1 = json.loads(b.eval(PD_READ))
        sl(1.5)
        v1 = b.call("grid.visible", id="grid.dgMain", doc=G) or {}
        st = stats(ts, drop_ms=0)
        return {"gb": gb, "adb": act, "raf_during": st, "raf_count": len(ts), "handlerMax": round(p1.get("handlerMax") or 0, 1),
                "handlerMs_per_move": round((p1.get("handlerMs") or 0) / p1["move"], 2) if p1.get("move") else None, "move": p1.get("move"), "cancel": p1.get("cancel"),
                "d_offsetY": round((v1.get("offsetY") or 0) - (v0.get("offsetY") or 0), 1), "finger_css": round(y0 - y1, 1)}

    if "m2" in only:
        for cond, preset in (("M2A", "MdiCascade"), ("M2B", "Tabbed"), ("M2C", "MdiCascade")):
            resp = b.call("docs.arrange", preset=preset)
            if preset == "Tabbed":
                b.call("docs.activate", doc=G)
            sl(5)
            fp = (b.call("perf.get") or {}).get("frame_pump")
            raf, nraf = idle_raf()
            tree = b.call("tree.get", named=False, depth=60) or {}
            nodes = count_nodes(tree)
            mdi = find_types(tree, "Mdi", [])
            extra = {}
            if cond == "M2A":
                lay = b.call("layout.get")
                extra = {"layout_get": lay, "mdi_panels": mdi}
                jd("fold5_M2A_layout.json", {"layout_get": lay, "mdi_bounds_candidates": mdi, "metrics": summ["metrics"]})
            dr = drag()
            row = {"cond": cond, "preset": preset, "arrange_resp": resp, "frame_pump": fp, "idle_raf": raf, "raf_samples": nraf, "tree_nodes_depth60": nodes,
                   "mdi_nodes": len(mdi), "drag": dr, "docs": len(b.call("docs.list") or [])}
            jd("fold5_%s.json" % cond, dict(row, **({"layout_file": "fold5_M2A_layout.json"} if extra else {})))
            summ["rows"].append({k: row[k] for k in ("cond", "frame_pump", "idle_raf", "tree_nodes_depth60", "docs")} | {"drag_raf_median": (dr.get("raf_during") or {}).get("median_ms"), "drag_handlerMax": dr.get("handlerMax")})
            print(cond, json.dumps(summ["rows"][-1], ensure_ascii=False), flush=True)
    if "m3" in only:
        b.call("docs.arrange", preset="MdiCascade")
        sl(5)
        for i, (lab, kw) in enumerate((("base", {}), ("stop_probe", {"stop": "probe"}), ("stop_fps", {"stop": "fps"}), ("stop_all", {"stop": "all"}), ("resume_all", {"resume": "all"})), 1):
            resp = b.call("perf.idle", **kw) if kw else None
            sl(2)
            fr0 = (b.call("perf.idle") or {}).get("frames")
            raf, nraf = idle_raf()
            pi = b.call("perf.idle") or {}
            row = {"step": i, "label": lab, "cmd": kw, "resp": resp, "idle_raf": raf, "raf_samples": nraf, "frames_before": fr0, "frames_after": pi.get("frames"),
                   "frames_delta": (pi.get("frames") or 0) - (fr0 or 0) if fr0 is not None else None, "perf_idle": pi, "frame_pump": (b.call("perf.get") or {}).get("frame_pump")}
            jd("fold5_M3_%d.json" % i, row)
            summ["rows"].append({"cond": "M3_%d_%s" % (i, lab), "idle_raf": raf, "frames_delta": row["frames_delta"], "frame_pump": row["frame_pump"]})
            print("M3", i, lab, json.dumps(summ["rows"][-1], ensure_ascii=False), flush=True)
        b.call("perf.idle", resume="all")
    jd("fold5_dpf1_summary.json", summ)
    b.close()


if __name__ == "__main__":
    main()
