# i1515gp Pro 대조(Fold5): Pro 전용 기능 off 확인(로드 전→G1 10만 행→재확인) 뒤 G3 3000·10000셀/초를 off·on 각 3회 재고 logs/odevgc·logs/otest_m1/i1515gp 에 저장하는 스크립트
"""
사용: python3 -E -u tools/retest_android_proctl.py --apk <i1515gp APK> [--rest 30]
측정 구간(bench.grid realtime 호출 중)에는 다른 원격 명령을 보내지 않는다. 사이에는 perf.get·perf.idle 만 쓴다.
off 확인값이 기대(conditional_formatting=false·cf_rules=0·cf_model=empty·frozen_right=0)와 다르면 멈추고 rc=3.
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
M = os.path.join(ROOT, "logs", "otest_m1", "i1515gp")
OFF_EXPECT = {"conditional_formatting": False, "cf_rules": 0, "cf_model": "empty", "frozen_right": 0}


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--apk", required=True)
    p.add_argument("--serial", default="R3CW70202FP")
    p.add_argument("--rest", type=float, default=30)
    p.add_argument("--rounds", type=int, default=3)
    a = p.parse_args()
    os.makedirs(G, exist_ok=True)
    os.makedirs(M, exist_ok=True)
    sr = a.serial
    state, summary, code = {}, {"apk": a.apk, "rest_s": a.rest, "rounds": a.rounds}, 0

    def save(name, obj):
        obj = dict(obj) if isinstance(obj, dict) else {"result": obj}
        obj["_host_load"] = os.getloadavg()
        obj["_at"] = time.strftime("%Y-%m-%dT%H:%M:%S")
        fn = "remeasure_android_%s.json" % name
        json.dump(obj, open(os.path.join(G, fn), "w", encoding="utf-8"), ensure_ascii=False, indent=1)
        shutil.copy(os.path.join(G, fn), os.path.join(M, fn))
        return obj

    def off_ok(pf):
        return isinstance(pf, dict) and all(pf.get(k) == v for k, v in OFF_EXPECT.items())

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

        def temp():
            rc, bat = ra.adb(sr, "shell", "dumpsys", "battery")
            return {k.strip(): v.strip() for k, v in (ln.split(":", 1) for ln in bat.splitlines() if ":" in ln) if k.strip() in ("level", "temperature", "AC powered", "USB powered")}

        # 0. 시작 전 유휴 게이트
        s0, c0, t0 = r.call("perf.idle") or {}, cpu(), time.time()
        time.sleep(10)
        s1, c1, t1 = r.call("perf.idle") or {}, cpu(), time.time()
        pre = {"idle": {"frames_delta": (s1.get("frames") or 0) - (s0.get("frames") or 0), "cpu_pct": round((c1 - c0) / (t1 - t0) * 100, 1)},
               "frame_pump": (r.call("perf.get") or {}).get("frame_pump"), "battery": temp()}
        pre["env"] = (r.call("bench.open", target="grid") or {}).get("env")
        r.call("layout.template.apply", id="tpl.tabbed")
        time.sleep(1.5)
        pre["gate_pass"] = pre["idle"]["cpu_pct"] < 5 and pre["idle"]["frames_delta"] <= 5 and not pre["frame_pump"]
        save("Pro_precheck", pre)
        summary["precheck"] = pre
        print("precheck", pre["idle"], "frame_pump", pre["frame_pump"], "gate", pre["gate_pass"], flush=True)
        if not pre["gate_pass"]:
            print("GATE_FAIL", flush=True)
            code = 2
            return

        # 1. off 확인: 로드 전 → G1 10만 행 → 재확인
        pf0 = (r.call("bench.grid", scenario="pro_features", mode="off", _timeout=120) or {}).get("pro_features")
        save("Pro_check_pro_features_off_preload_r0", {"pro_features": pf0})
        print("off_preload", pf0, flush=True)
        if not off_ok(pf0):
            print("OFF_MISMATCH preload", flush=True)
            code = 3
            return
        time.sleep(a.rest)
        g1 = r.call("bench.grid", scenario="load", rows=100000, _timeout=240)
        save("Pro_off_G1_load100k_r1", {"ok": r.calls[-1]["ok"], "error": r.calls[-1].get("error"), "result": g1})
        print("G1", r.calls[-1]["ok"], (g1 or {}).get("load", {}).get("first_show_ms") if isinstance(g1, dict) else None, flush=True)
        time.sleep(3)
        # 재확인: 먼저 읽기 전용(ui.find props) → 그다음 pro_features off 결과
        ro = r.call("ui.find", id="BenchGrid", props="FrozenColumnCountRight,ConditionalFormattingModel")
        pf1 = (r.call("bench.grid", scenario="pro_features", mode="off", _timeout=120) or {}).get("pro_features")
        save("Pro_off_pro_features_after_G1_r1", {"readonly_find": ro, "pro_features": pf1})
        print("off_after_G1", pf1, "readonly", json.dumps(ro, ensure_ascii=False)[:300], flush=True)
        if not off_ok(pf1):
            print("OFF_MISMATCH after G1", flush=True)
            code = 3
            return
        summary["off_check"] = {"preload": pf0, "after_G1": pf1, "readonly_find": ro}

        # 2. G3 off·on 각 3회
        res = {}
        for mode in ("off", "on"):
            if mode == "on":
                pfo = (r.call("bench.grid", scenario="pro_features", mode="on", _timeout=120) or {}).get("pro_features")
                save("Pro_on_pro_features_r0", {"pro_features": pfo})
                print("on_set", pfo, flush=True)
                summary["on_set"] = pfo
                time.sleep(a.rest)
            for rate in (3000, 10000):
                for n in range(1, a.rounds + 1):
                    idle = r.call("perf.idle")
                    g3 = r.call("bench.grid", scenario="realtime", seconds=5, rate=rate, focus=60, _timeout=240)
                    c = r.calls[-1]
                    pg = r.call("perf.get")
                    o = save("Pro_%s_G3_realtime%d_r%d" % (mode, rate, n), {"ok": c["ok"], "error": c.get("error"), "result": g3, "perf_idle_before": idle, "perf_get_after": pg, "battery": temp()})
                    rt = (g3 or {}).get("realtime", {}) if isinstance(g3, dict) else {}
                    res.setdefault("%s_%d" % (mode, rate), []).append({k: rt.get(k) for k in ("fps", "p50_ms", "p95_ms", "p99_ms", "max_ms", "achieved_cells_per_s", "timed_out")})
                    print(mode, rate, n, c["ok"], rt.get("fps"), rt.get("p95_ms"), rt.get("achieved_cells_per_s"), flush=True)
                    time.sleep(a.rest)
        summary["G3"] = res
        summary["battery_end"] = temp()
        summary["host_load_end"] = os.getloadavg()
    except Exception as e:  # noqa: BLE001
        summary["error"] = repr(e)
        print("ERROR", repr(e), flush=True)
        code = 2
    finally:
        json.dump(summary, open(os.path.join(M, "proctl_summary.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=1)
        ra.cleanup(sr, state)
        ra.adb(sr, "shell", "wm", "size", "reset")
        print("EXIT", code, flush=True)
        sys.exit(code)


if __name__ == "__main__":
    main()
