# odev-3 원인 확정 측정(Fold5): A·A_empty 각 1회, theme.next 5회 전후로 dock.proportions·grid_measure_lastsize·배율 관련 perf 값을 기록하는 스크립트
"""
사용: python3 -E -u tools/retest_android_dockprop.py --apk <APK(RemoteCommands.Scroll.cs 9bd6b73a 판)> --outdir <폴더>
"""
import argparse
import json
import os
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import retest_android as ra  # noqa: E402
import retest_android_cond2 as c2  # noqa: E402


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--apk", required=True)
    p.add_argument("--serial", default="R3CW70202FP")
    p.add_argument("--outdir", required=True)
    a = p.parse_args()
    os.makedirs(a.outdir, exist_ok=True)
    state, rep = {}, {}
    try:
        info = ra.prepare(a.serial, a.apk, state)
        from retest_remote import Remote
        for cond, args in (("A", "--fresh"), ("A_empty", "--fresh --notify-panel empty")):
            c2.launch(a.serial, args, info)
            time.sleep(3)
            r = Remote()
            r.call("docs.close", all=True)
            for k in ("grid", "gantt", "form"):
                r.call("docs.open", kind=k)
            time.sleep(2)
            perf = r.call("perf.get") or {}
            run = {"args": args, "scale_keys": {k: perf.get(k) for k in perf if any(s in k.lower() for s in ("scal", "dpi", "density", "render"))}, "rounds": []}

            def snap():
                pg = r.call("perf.get") or {}
                return {"dock": r.call("dock.proportions"), "grid_measure_lastsize": pg.get("grid_measure_lastsize"),
                        "grid_measure_sizechg": pg.get("grid_measure_sizechg")}

            for i in range(1, 6):
                before = snap()
                res = r.call("theme.next")
                ms = r.calls[-1]["elapsed_ms"]
                time.sleep(0.5)
                run["rounds"].append({"i": i, "to": res, "ms": ms, "before": before, "after": snap()})
            rep[cond] = run
    finally:
        json.dump(rep, open(os.path.join(a.outdir, "dockprop_result.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=1)
        ra.cleanup(a.serial, state)
        ra.adb(a.serial, "shell", "wm", "size", "reset")


if __name__ == "__main__":
    main()
