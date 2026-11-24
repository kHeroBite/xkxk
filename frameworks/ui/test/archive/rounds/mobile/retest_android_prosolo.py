# i1600gp Pro 단독 모드 대조(Fold5): cf_only·frozen_only 를 회차마다 순서를 바꿔 확인(로드 전→G1 10만 행→재확인) 뒤 G3 3000·10000셀/초 각 1회씩, 3회차를 재는 스크립트(odev-gc 절차)
"""
사용: python3 -E -u tools/retest_android_prosolo.py --apk <i1600gp APK> [--rest 30]
회차 순서: r1 cf_only→frozen_only, r2 frozen_only→cf_only, r3 cf_only→frozen_only. 모드를 바꿀 때마다 확인 → G1 → 재확인을 한다.
측정 구간에는 다른 원격 명령을 보내지 않는다(사이에는 perf.get·perf.idle 만). 기대값이 다르면 멈추고 rc=3.
결과: logs/odevgc/remeasure_android_Pro_{모드}_G3_realtime{3000|10000}_r{n}.json, _pro_features_{preload|after_G1}_r{n}.json (사본 logs/otest_m1/i1600gp)
"""
import argparse
import json
import os
import shutil
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import retest_android as ra  # noqa: E402
import retest_android_cond2 as c2  # noqa: E402

ROOT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
G = os.path.join(ROOT, "logs", "odevgc")
M = os.path.join(ROOT, "logs", "otest_m1", "i1600gp")
EXPECT = {"cf_only": {"conditional_formatting": True, "cf_rules": 1, "cf_model": "bench", "frozen_right": 0},
          "frozen_only": {"conditional_formatting": False, "cf_rules": 0, "cf_model": "empty", "frozen_right": 1}}
ORDER = {1: ["cf_only", "frozen_only"], 2: ["frozen_only", "cf_only"], 3: ["cf_only", "frozen_only"]}


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--apk", required=True)
    p.add_argument("--serial", default="R3CW70202FP")
    p.add_argument("--rest", type=float, default=30)
    a = p.parse_args()
    os.makedirs(G, exist_ok=True)
    os.makedirs(M, exist_ok=True)
    sr = a.serial
    state, summary, code = {}, {"apk": a.apk, "rest_s": a.rest, "order": ORDER, "G3": {}, "checks": []}, 0

    def save(name, obj):
        obj = dict(obj) if isinstance(obj, dict) else {"result": obj}
        obj["_host_load"] = os.getloadavg()
        obj["_at"] = time.strftime("%Y-%m-%dT%H:%M:%S")
        fn = "remeasure_android_Pro_%s.json" % name
        json.dump(obj, open(os.path.join(G, fn), "w", encoding="utf-8"), ensure_ascii=False, indent=1)
        shutil.copy(os.path.join(G, fn), os.path.join(M, fn))

    try:
        info = ra.prepare(sr, a.apk, state)
        from retest_remote import Remote
        c2.launch(sr, "--fresh", info)
        time.sleep(4)
        r = Remote()
        rc, pid = ra.adb(sr, "shell", "pidof", ra.PKG)
        pid = pid.strip().split()[0] if pid.strip() else None

        def cpu():
            rc, st = ra.adb(sr, "shell", "cat", "/proc/%s/stat" % pid)
            f = st.split(")")[-1].split()
            return (int(f[11]) + int(f[12])) / 100 if len(f) > 12 else None

        def batt():
            rc, bat = ra.adb(sr, "shell", "dumpsys", "battery")
            return {k.strip(): v.strip() for k, v in (ln.split(":", 1) for ln in bat.splitlines() if ":" in ln) if k.strip() in ("level", "temperature", "AC powered", "USB powered", "status")}

        # 0. 시작 전 확인(연결·입력 뒤 2초+ 경과 후 유휴 10초)
        env = r.call("bench.open", target="grid") or {}
        r.call("layout.template.apply", id="tpl.tabbed")
        time.sleep(3)
        s0, c0, t0 = r.call("perf.idle") or {}, cpu(), time.time()
        time.sleep(10)
        s1, c1, t1 = r.call("perf.idle") or {}, cpu(), time.time()
        pre = {"idle": {"frames_delta": (s1.get("frames") or 0) - (s0.get("frames") or 0), "cpu_pct": round((c1 - c0) / (t1 - t0) * 100, 1)},
               "frame_pump": (r.call("perf.get") or {}).get("frame_pump"), "battery": batt(), "env": env.get("env", env), "bench_env": r.call("bench.env")}
        pre["gate_pass"] = pre["idle"]["cpu_pct"] < 5 and pre["idle"]["frames_delta"] <= 5 and not pre["frame_pump"]
        save("solo_precheck", pre)
        summary["precheck"] = {k: pre[k] for k in ("idle", "frame_pump", "battery", "gate_pass")}
        print("precheck", pre["idle"], "frame_pump", pre["frame_pump"], "gate", pre["gate_pass"], pre["battery"], flush=True)
        if not pre["gate_pass"]:
            print("GATE_FAIL", flush=True)
            code = 2
            return

        def check(mode, n, when):
            ro = r.call("ui.find", id="BenchGrid", props="FrozenColumnCountRight") if when == "after_G1" else None
            pf = (r.call("bench.grid", scenario="pro_features", mode=mode, _timeout=120) or {}).get("pro_features")
            ok = isinstance(pf, dict) and all(pf.get(k) == v for k, v in EXPECT[mode].items())
            fr = ((ro or {}).get("props") or {}).get("FrozenColumnCountRight") if ro else None
            save("%s_pro_features_%s_r%d" % (mode, when, n), {"pro_features": pf, "readonly_FrozenColumnCountRight": fr, "expect": EXPECT[mode], "match": ok})
            summary["checks"].append({"mode": mode, "n": n, "when": when, "match": ok, "readonly_frozen": fr, "pf": pf})
            print("check", mode, n, when, ok, pf, "readonly_frozen", fr, flush=True)
            return ok

        for n in (1, 2, 3):
            for mode in ORDER[n]:
                if not check(mode, n, "preload"):
                    print("MISMATCH", mode, n, "preload", flush=True)
                    code = 3
                    return
                time.sleep(a.rest)
                g1 = r.call("bench.grid", scenario="load", rows=100000, _timeout=240)
                print("G1", mode, n, r.calls[-1]["ok"], ((g1 or {}).get("load") or {}).get("first_show_ms") if isinstance(g1, dict) else None, flush=True)
                time.sleep(3)
                if not check(mode, n, "after_G1"):
                    print("MISMATCH", mode, n, "after_G1", flush=True)
                    code = 3
                    return
                time.sleep(a.rest)
                for rate in (3000, 10000):
                    idle = r.call("perf.idle")
                    g3 = r.call("bench.grid", scenario="realtime", seconds=5, rate=rate, focus=60, _timeout=240)
                    c = r.calls[-1]
                    pg = r.call("perf.get")
                    save("%s_G3_realtime%d_r%d" % (mode, rate, n), {"ok": c["ok"], "error": c.get("error"), "result": g3, "perf_idle_before": idle, "perf_get_after": pg, "battery": batt()})
                    rt = (g3 or {}).get("realtime", {}) if isinstance(g3, dict) else {}
                    summary["G3"].setdefault("%s_%d" % (mode, rate), []).append(dict({k: rt.get(k) for k in ("fps", "p50_ms", "p95_ms", "max_ms", "achieved_cells_per_s", "timed_out")}, r=n))
                    print(mode, rate, n, c["ok"], rt.get("fps"), rt.get("p95_ms"), rt.get("achieved_cells_per_s"), rt.get("timed_out"), flush=True)
                    time.sleep(a.rest)
        summary["battery_end"] = batt()
        summary["host_load_end"] = os.getloadavg()
    except Exception as e:  # noqa: BLE001
        summary["error"] = repr(e)
        print("ERROR", repr(e), flush=True)
        code = 2
    finally:
        json.dump(summary, open(os.path.join(M, "prosolo_summary.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=1)
        ra.cleanup(sr, state)
        ra.adb(sr, "shell", "wm", "size", "reset")
        print("EXIT", code, flush=True)
        sys.exit(code)


if __name__ == "__main__":
    main()
