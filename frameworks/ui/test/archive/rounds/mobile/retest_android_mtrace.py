# Q4 수정안 ① 판정(Fold5, odev-3): 테마 전환 10회 동안 grid_measure_sizechg·stabilized·grid.measure 를 보고, grid.mtrace 원문(테마 전환·접기/펴기)을 저장하고 그리드 오른쪽 끝을 캡처하는 스크립트
"""
사용: python3 -E -u tools/retest_android_mtrace.py --apk <APK(MeasureTrace 1653d11d 판)> --outdir <폴더>
"""
import argparse
import json
import os
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import retest_android as ra  # noqa: E402
import retest_android_cond2 as c2  # noqa: E402

FOLD = "904x2316"
KEYS = ("grid_measure_sizechg", "grid_measure_stabilized", "grid_measure_lastsize")


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--apk", required=True)
    p.add_argument("--serial", default="R3CW70202FP")
    p.add_argument("--outdir", required=True)
    p.add_argument("--layout", default="tpl.tabbed")  # Z1 판정은 tpl.tabbed·tpl.mdi-cascade 두 모드(odev-3)
    a = p.parse_args()
    O, sr = a.outdir, a.serial
    os.makedirs(O, exist_ok=True)
    state, rep = {}, {"host_load": os.getloadavg()}

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
        for k in ("grid", "gantt", "form"):
            r.call("docs.open", kind=k)
        r.call("layout.template.apply", id=a.layout)
        r.call("docs.activate", doc="Grid-1")
        rep["layout"] = a.layout
        time.sleep(2)
        p0 = r.call("perf.get") or {}
        rep["perf_before"] = {k: p0.get(k) for k in KEYS}
        r.call("perf.reset")
        traces = []
        for i in range(10):
            t = r.call("grid.mtrace", action="theme-next", frames=8, limit=400, _timeout=120)
            traces.append(t)
        p1 = r.call("perf.get") or {}
        ops = {o.get("name"): o for o in (r.call("perf.ops") or []) if isinstance(o, dict)}
        rep["perf_after_10_theme"] = {k: p1.get(k) for k in KEYS}
        rep["grid_measure_ops_10_theme"] = ops.get("grid.measure")
        rep["theme_summary"] = [{k: (t or {}).get(k) for k in ("total", "passes", "stabilized", "byWidth")} for t in traces]
        json.dump(traces, open(os.path.join(O, "mtrace_theme10_raw.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=1)
        rep["cap_grid_tabbed"] = cap("mtrace_grid_tabbed_after_theme")
        # 실제 크기 변경: 접기 → 펴기 (흡수되면 안 됨)
        for tag, size in (("fold", FOLD), ("unfold", "reset")):
            r.call("grid.mtrace", action="clear")
            pb = r.call("perf.get") or {}
            ra.adb(sr, "shell", "wm", "size", size)
            time.sleep(2.5)
            t = r.call("grid.mtrace", action="get", limit=400)
            pa = r.call("perf.get") or {}
            rep["size_" + tag] = {"summary": {k: (t or {}).get(k) for k in ("total", "passes", "stabilized", "byWidth")},
                                  "sizechg_delta": (pa.get("grid_measure_sizechg") or 0) - (pb.get("grid_measure_sizechg") or 0),
                                  "stabilized_delta": (pa.get("grid_measure_stabilized") or 0) - (pb.get("grid_measure_stabilized") or 0),
                                  "cap": cap("mtrace_after_" + tag)}
            json.dump(t, open(os.path.join(O, "mtrace_%s_raw.json" % tag), "w", encoding="utf-8"), ensure_ascii=False, indent=1)
    finally:
        json.dump(rep, open(os.path.join(O, "mtrace_result.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=1)
        ra.cleanup(sr, state)
        ra.adb(sr, "shell", "wm", "size", "reset")


if __name__ == "__main__":
    main()
