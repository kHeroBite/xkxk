# 유휴 렌더 측정(Fold5, perf.idle 판): 한 실행에서 기본 → stop=fps → stop=probe → resume=all 순서로 10초씩 perf.idle frames 증가량과 프로세스 CPU 시간(/proc/pid/stat utime+stime)을 기록하는 스크립트
"""
사용: python3 -E -u tools/retest_android_idle.py --apk <APK(i1320 이후)> --outdir <폴더> [--seconds 10]
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
    p.add_argument("--seconds", type=float, default=10)
    a = p.parse_args()
    os.makedirs(a.outdir, exist_ok=True)
    sr = a.serial
    state, rep = {}, {"segments": []}
    try:
        info = ra.prepare(sr, a.apk, state)
        from retest_remote import Remote
        c2.launch(sr, "--fresh", info)
        time.sleep(3)
        r = Remote()
        r.call("docs.close", all=True)
        for k in ("grid", "gantt", "form"):
            r.call("docs.open", kind=k)
        r.call("layout.template.apply", id="tpl.mdi-cascade")
        time.sleep(3)
        rc, pid = ra.adb(sr, "shell", "pidof", ra.PKG)
        pid = pid.strip().split()[0] if pid.strip() else None
        rc, tck = ra.adb(sr, "shell", "getconf", "CLK_TCK")
        hz = int(tck.strip() or 100) if tck.strip().isdigit() else 100
        rep["pid"], rep["clk_tck"] = pid, hz

        def cpu():
            rc, st = ra.adb(sr, "shell", "cat", "/proc/%s/stat" % pid)
            f = st.split(")")[-1].split()
            return (int(f[11]) + int(f[12])) / hz if len(f) > 12 else None  # utime(14)+stime(15), ')' 뒤 기준 12·13번째

        for tag, arg, rest in (("base", {}, 0), ("stop_fps", {"stop": "fps"}, 0), ("stop_probe", {"stop": "probe"}, 1.5), ("resume_all", {"resume": "all"}, 0)):
            ctl = r.call("perf.idle", **arg) if arg else None
            time.sleep(rest)
            s0 = r.call("perf.idle") or {}
            c0, t0 = cpu(), time.time()
            time.sleep(a.seconds)
            s1 = r.call("perf.idle") or {}
            c1, t1 = cpu(), time.time()
            seg = {"tag": tag, "control": ctl, "before": s0, "after": s1, "elapsed_s": round(t1 - t0, 2),
                   "frames_delta": (s1.get("frames") - s0.get("frames")) if isinstance(s1.get("frames"), (int, float)) and isinstance(s0.get("frames"), (int, float)) else None,
                   "cpu_s": round(c1 - c0, 3) if c0 is not None and c1 is not None else None}
            seg["cpu_pct"] = round(seg["cpu_s"] / seg["elapsed_s"] * 100, 1) if seg["cpu_s"] is not None else None
            rep["segments"].append(seg)
            print(tag, "frames", seg["frames_delta"], "cpu_s", seg["cpu_s"], "cpu%", seg["cpu_pct"], "state", {k: s1.get(k) for k in ("probe", "fpsPaused")}, flush=True)
    finally:
        rep["host_load"] = os.getloadavg()
        json.dump(rep, open(os.path.join(a.outdir, "idle_result.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=1)
        ra.cleanup(sr, state)
        ra.adb(sr, "shell", "wm", "size", "reset")


if __name__ == "__main__":
    main()
