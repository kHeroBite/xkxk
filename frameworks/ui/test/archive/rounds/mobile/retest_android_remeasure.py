# odev-gc remeasure_plan_idlefix.md 실행(Fold5, 유휴 수정 벤치 APK): 시작 전 확인(유휴 CPU·frame_pump·env·기기 상태·호스트 load) 뒤 G1~G5·C1~C3 를 3회(라이브러리 순서 교대, 측정 사이 30초) 재고 결과를 logs/odevgc 에 저장하는 스크립트
"""
사용: python3 -E -u tools/retest_android_remeasure.py --apk <벤치 APK> [--impl Builtin|Pro] [--rounds 3] [--rest 30] [--grid-only]
"""
import argparse
import json
import os
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import retest_android as ra  # noqa: E402
import retest_android_cond2 as c2  # noqa: E402

G = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "logs", "odevgc"))
ORDERS = [["scottplot", "procharts", "livecharts"], ["livecharts", "procharts", "scottplot"], ["procharts", "livecharts", "scottplot"]]  # odev-gc 확정 순서


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--apk", required=True)
    p.add_argument("--serial", default="R3CW70202FP")
    p.add_argument("--impl", default="Builtin")
    p.add_argument("--rounds", type=int, default=3)
    p.add_argument("--rest", type=float, default=30)
    p.add_argument("--grid-only", action="store_true")  # Pro 판은 G1~G3 만
    a = p.parse_args()
    os.makedirs(G, exist_ok=True)
    sr = a.serial
    state, pre = {}, {}

    def save(scen, n, obj):
        obj = dict(obj or {}) if isinstance(obj, dict) else {"result": obj}
        obj["_host_load"] = os.getloadavg()
        obj["_at"] = time.strftime("%Y-%m-%dT%H:%M:%S")
        json.dump(obj, open(os.path.join(G, "remeasure_android_%s_%s_r%d.json" % (a.impl, scen, n)), "w", encoding="utf-8"), ensure_ascii=False, indent=1)

    try:
        info = ra.prepare(sr, a.apk, state)
        from retest_remote import Remote
        c2.launch(sr, "--fresh", info)
        time.sleep(4)
        r = Remote()
        # 0. 시작 전 확인
        rc, pid = ra.adb(sr, "shell", "pidof", ra.PKG)
        pid = pid.strip().split()[0] if pid.strip() else None

        def cpu():
            rc, st = ra.adb(sr, "shell", "cat", "/proc/%s/stat" % pid)
            f = st.split(")")[-1].split()
            return (int(f[11]) + int(f[12])) / 100 if len(f) > 12 else None

        s0, c0, t0 = r.call("perf.idle") or {}, cpu(), time.time()
        time.sleep(10)
        s1, c1, t1 = r.call("perf.idle") or {}, cpu(), time.time()
        pre["idle"] = {"frames_delta": (s1.get("frames") or 0) - (s0.get("frames") or 0), "cpu_pct": round((c1 - c0) / (t1 - t0) * 100, 1)}
        pre["frame_pump"] = (r.call("perf.get") or {}).get("frame_pump")
        rc, bat = ra.adb(sr, "shell", "dumpsys", "battery")
        pre["battery"] = {k.strip(): v.strip() for k, v in (ln.split(":", 1) for ln in bat.splitlines() if ":" in ln) if k.strip() in ("level", "temperature", "AC powered", "USB powered", "status")}
        rc, br = ra.adb(sr, "shell", "settings", "get", "system", "screen_brightness")
        pre["brightness"] = br.strip()
        pre["env"] = (r.call("bench.open", target="grid") or {}).get("env")
        pre["host_load"] = os.getloadavg()
        r.call("layout.template.apply", id="tpl.tabbed")  # 벤치 문서 1개, 넓게
        time.sleep(1.5)
        json.dump(pre, open(os.path.join(G, "remeasure_android_%s_precheck.json" % a.impl), "w", encoding="utf-8"), ensure_ascii=False, indent=1)
        print("precheck", pre["idle"], "frame_pump", pre["frame_pump"], flush=True)
        # 필수 게이트(odev-gc): 유휴 CPU < 5%·frames_delta ≈ 0·frame_pump=0 이 아니면 벤치를 건너뛴다
        gate = pre["idle"]["cpu_pct"] < 5 and pre["idle"]["frames_delta"] <= 5 and not pre["frame_pump"]
        pre["gate_pass"] = gate
        json.dump(pre, open(os.path.join(G, "remeasure_android_%s_precheck.json" % a.impl), "w", encoding="utf-8"), ensure_ascii=False, indent=1)
        if not gate:
            print("GATE_FAIL — 벤치 건너뜀", flush=True)
            return

        def run(scen, n, cmd, **args):
            res = r.call(cmd, _timeout=240, **args)
            c = r.calls[-1]
            save(scen, n, {"ok": c["ok"], "error": c.get("error"), "args": args, "result": res})
            print(n, scen, "ok" if c["ok"] else c.get("error"), flush=True)
            time.sleep(a.rest)

        for n in range(1, a.rounds + 1):
            run("G1_load10k", n, "bench.grid", scenario="load", rows=10000)
            run("G1_load100k", n, "bench.grid", scenario="load", rows=100000)
            run("G2_scroll", n, "bench.grid", scenario="scroll", seconds=5, step=3)
            run("G3_realtime3000", n, "bench.grid", scenario="realtime", seconds=5, rate=3000, focus=60)
            if n == 1:
                run("G3_realtime10000", n, "bench.grid", scenario="realtime", seconds=5, rate=10000, focus=60)
            if a.grid_only:
                continue
            run("G4_sort_app", n, "bench.grid", scenario="sort", property="Name", culture="app")
            run("G5_filter_kim", n, "bench.grid", scenario="filter", text="김")
            r.call("bench.grid", scenario="filter", text="")  # 필터 해제
            for lib in ORDERS[(n - 1) % 3]:
                run("C1_static10k_" + lib, n, "bench.chart", scenario="static", lib=lib, points=10000, redrawSeconds=3)
                run("C2_static100k_" + lib, n, "bench.chart", scenario="static", lib=lib, points=100000, redrawSeconds=3)
                if lib == "livecharts":
                    run("C3_stream_livecharts_anim", n, "bench.chart", scenario="stream", lib=lib, window=10000, rate=1000, seconds=5, animate=True)
                    run("C3_stream_livecharts_noanim", n, "bench.chart", scenario="stream", lib=lib, window=10000, rate=1000, seconds=5, animate=False)
                else:
                    run("C3_stream_" + lib, n, "bench.chart", scenario="stream", lib=lib, window=10000, rate=1000, seconds=5)
    finally:
        ra.cleanup(sr, state)
        ra.adb(sr, "shell", "wm", "size", "reset")


if __name__ == "__main__":
    main()
