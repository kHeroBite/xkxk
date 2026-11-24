# 그리드 빈 칸 회귀 판별(Fold5, 리드 지시): 제품 Grid-1 탭 모드에서 머리글 탭 정렬·세로 스크롤 뒤 행별 셀 텍스트 수를 tree 로 세고, 캡처 → 화면 탭 → 2초 대기 뒤 다시 세어 그리기 누락 여부를 기록하는 스크립트
"""
사용: python3 -E -u test/mobile/measure/retest_android_emptycells.py --apk <APK> --outdir <폴더>
"""
import argparse
import collections
import json
import os
import sys
import time

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "common"))  # D-TESTDIR: test/common/_paths.py
import _paths  # noqa: E402
_paths.add()
import retest_android as ra  # noqa: E402
import retest_android_cond2 as c2  # noqa: E402

S = 360 / 160.0


def walk(t, out, pred):
    if isinstance(t, dict):
        if pred(t):
            out.append({k: t.get(k) for k in ("type", "name", "text", "bounds", "visible") if k in t})
        for v in t.values():
            if isinstance(v, (dict, list)):
                walk(v, out, pred)
    elif isinstance(t, list):
        for v in t:
            walk(v, out, pred)
    return out


def rows_cells(r):
    """그리드 본문 영역의 셀 TextBlock 을 y 로 묶어 행별 보이는 텍스트 수를 센다."""
    tr = r.call("tree.get", named=False, depth=90) or {}
    cells = walk(tr, [], lambda n: "DataGridCell" in str(n.get("type")))
    tbs = walk(tr, [], lambda n: "TextBlock" in str(n.get("type")) and n.get("text") not in (None, "") and (n.get("bounds") or {}).get("w", 0) > 0)
    hdr = walk(tr, [], lambda n: "DataGridColumnHeader" in str(n.get("type")))
    by = collections.Counter()
    for c in cells:
        b = c.get("bounds") or {}
        if b.get("w", 0) > 0:
            by[round(b.get("y", 0))] += 1
    return {"cells_total": len(cells), "cells_visible_by_row_y": sorted(by.items())[:20], "textblocks": len(tbs), "headers": [(h.get("text"), h.get("bounds")) for h in hdr][:12]}


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--apk", required=True)
    p.add_argument("--serial", default="R3CW70202FP")
    p.add_argument("--outdir", required=True)
    a = p.parse_args()
    O, sr = a.outdir, a.serial
    os.makedirs(O, exist_ok=True)
    state, rep = {}, {}

    def cap(name):
        rc, data = ra.adb(sr, "exec-out", "screencap", "-p", binary=True)
        k = data.find(b"\x89PNG") if rc == 0 else -1
        if k >= 0:
            open(os.path.join(O, name + ".png"), "wb").write(data[k:])
            return name + ".png"
        return None

    try:
        info = ra.prepare(sr, a.apk, state)
        from retest_remote import Remote
        c2.launch(sr, "--fresh", info)
        time.sleep(3)
        r = Remote()
        r.call("docs.close", all=True)
        g = (r.call("docs.open", kind="grid") or [{}])[0].get("id")
        r.call("layout.template.apply", id="tpl.tabbed")
        time.sleep(1.5)
        tr = r.call("dock.trace", action="none", frames=1) or {}
        rep["scaling"] = [s.get("scaling") for s in (tr.get("shots") or [])][:1]
        rep["perf_render"] = {k: v for k, v in (r.call("perf.get") or {}).items() if any(s in k for s in ("render", "gpu", "scal"))}
        rep["s0"] = rows_cells(r)
        rep["cap0"] = cap("ec0_loaded")
        # 머리글 '작업명' 탭으로 정렬
        hdrs = rep["s0"]["headers"]
        tgt = next((b for t, b in hdrs if t and "작업명" in str(t)), None) or next((b for t, b in hdrs if b and b.get("w", 0) > 40), None)
        if tgt:
            x, y = (tgt["x"] + tgt["w"] / 2) * S, (tgt["y"] + tgt["h"] / 2) * S
            ra.adb(sr, "shell", "input", "tap", str(int(x)), str(int(y)))
            rep["sort_tap_px"] = [int(x), int(y)]
        time.sleep(1.5)
        r.call("ui.scroll", id="grid.dgMain", doc=g, lines=30)
        time.sleep(0.3)
        rep["s1_immediate"] = rows_cells(r)
        rep["gv1"] = r.call("grid.visible", id="grid.dgMain", doc=g)
        rep["cap1"] = cap("ec1_sorted_scrolled")
        # 그리기 누락 판별: 화면(그리드 밖 여백) 한 번 탭 → 캡처, 2초 대기 → 캡처
        ra.adb(sr, "shell", "input", "tap", "1700", "2100")
        time.sleep(0.5)
        rep["s2_after_tap"] = rows_cells(r)
        rep["cap2"] = cap("ec2_after_tap")
        time.sleep(2)
        rep["s3_after_2s"] = rows_cells(r)
        rep["cap3"] = cap("ec3_after_2s")
    finally:
        json.dump(rep, open(os.path.join(O, "emptycells_result.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=1)
        ra.cleanup(sr, state)
        ra.adb(sr, "shell", "wm", "size", "reset")


if __name__ == "__main__":
    main()
