# D-PF1 Fold5 보충: 문서 14개 상태에서 MdiCascade→Tabbed→MdiCascade 를 두 번 돌며 정지 rAF·tree.get 노드 수·layout.get·MDI 패널 bounds 를 남기는 스크립트(otest-m-3, retest_web_dpf1.py 보충)
"""
사용: python3 -E -u tools/retest_web_dpf1b.py --url-part 127.0.0.1:5193 --out-dir logs/otest_m3/i1835rc/dpf1 [--cycles 2]
1차 실행(retest_web_dpf1.py)의 노드 수는 tree.get 응답 구조를 잘못 읽어 1로 나왔다 — 여기서는 응답의 nodes 값을 쓴다.
"""
import argparse
import json
import os
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from cdp_bridge import Bridge  # noqa: E402
from retest_web_dpf1 import RAF_JS, RAF_STOP, stats  # noqa: E402


def find_types(n, keys, out):
    if isinstance(n, dict):
        t = str(n.get("type", ""))
        if any(k.lower() in t.lower() for k in keys):
            out.append({k: n.get(k) for k in ("type", "id", "name", "bounds", "isVisible", "effectivelyVisible")})
        for c in n.get("children") or []:
            find_types(c, keys, out)
    elif isinstance(n, list):
        for c in n:
            find_types(c, keys, out)
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--url-part", required=True)
    ap.add_argument("--out-dir", required=True)
    ap.add_argument("--cycles", type=int, default=2)
    a = ap.parse_args()
    b = Bridge(a.url_part)
    sl = time.sleep
    ds = b.call("docs.list") or []
    G = [d.get("id") for d in ds if d.get("kind") == "grid"][0]
    out = {"docs": len(ds), "rows": [], "at": time.strftime("%Y-%m-%dT%H:%M:%S")}
    seq = []
    for c in range(1, a.cycles + 1):
        seq += [("c%d_A" % c, "MdiCascade"), ("c%d_B" % c, "Tabbed"), ("c%d_C" % c, "MdiCascade")]
    for lab, preset in seq:
        resp = b.call("docs.arrange", preset=preset)
        if preset == "Tabbed":
            b.call("docs.activate", doc=G)
        sl(5)
        b.eval(RAF_JS)
        sl(10)
        raf = stats(json.loads(b.eval(RAF_STOP)))
        t = b.call("tree.get", named=False, depth=60) or {}
        mdi = find_types(t.get("tree"), ["Mdi", "DocumentControl", "DocumentDock"], [])
        lay = b.call("layout.get") or {}
        docs = [{k: d.get(k) for k in ("Id", "X", "Y", "Width", "Height", "MdiState", "ZIndex", "State")} for d in (lay.get("Documents") or [])]
        pi = b.call("perf.idle") or {}
        row = {"label": lab, "preset": preset, "arrange_resp": resp, "idle_raf": raf, "tree_nodes": t.get("nodes"), "truncated": t.get("truncated"),
               "mode": lay.get("Mode"), "active": lay.get("ActiveId"), "doc_rects": docs, "mdi_like_nodes": mdi[:12], "perf_idle": pi,
               "frame_pump": (b.call("perf.get") or {}).get("frame_pump"), "host_load": [round(x, 2) for x in os.getloadavg()]}
        out["rows"].append(row)
        print(lab, preset, raf.get("median_ms"), raf.get("p95_ms"), "nodes", t.get("nodes"), "mode", lay.get("Mode"), "active", lay.get("ActiveId"), flush=True)
        with open(os.path.join(a.out_dir, "fold5_M2_cycles.json"), "w", encoding="utf-8") as f:
            json.dump(out, f, ensure_ascii=False, indent=1)
    b.close()


if __name__ == "__main__":
    main()
