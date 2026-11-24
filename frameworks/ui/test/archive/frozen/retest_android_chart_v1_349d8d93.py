# i1853gp Android 차트 벤치(chart_procedure.md 8f1948de)를 Fold5 에서 4개 라이브러리 × 3회 재고 원자료 JSON 을 남기는 스크립트(otest-m-3)
"""
사용: python3 -E -u tools/retest_android_chart.py --apk <i1853gp APK> [--rest 30] [--rounds 1,2,3] [--libs ...]
라이브러리를 바꿀 때마다 앱을 다시 띄운다(retest_android_cond2.launch). 설치는 처음 1회만 한다.
회차 순서: r1 scottplot→oxyplot→procharts→livecharts, r2 livecharts→scottplot→oxyplot→procharts, r3 procharts→livecharts→scottplot→oxyplot
lib 안 순서: 정적 raw 1만·10만·100만 → lttb 1만·10만·100만 → 스트리밍 base(→ anim, scottplot·livecharts) → (scottplot) 정적 raw 10만 anim=proto
생략: 같은 lib·ds 에서 10만 first_show_ms > 5000 이면 100만 생략. 원격 제한 시간 정적 60초·스트리밍 90초.
R3: r1 oxyplot 의 lttb 10만 직후 화면을 chart_android_<tag>_oxyplot_korean_r1.png 로 캡처(두부 판정·json 은 사람이 따로 쓴다).
결과: logs/odevge/chart/chart_android_<tag>_<lib>_<끝부분>_r<n>.json, 요약 logs/otest_m3/<tag>/chart_summary_<tag>.json
"""
import argparse
import json
import os
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import retest_android as ra  # noqa: E402
import retest_android_cond2 as c2  # noqa: E402

ROOT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
C = os.path.join(ROOT, "logs", "odevge", "chart")
ORDER = {1: ["scottplot", "oxyplot", "procharts", "livecharts"], 2: ["livecharts", "scottplot", "oxyplot", "procharts"], 3: ["procharts", "livecharts", "scottplot", "oxyplot"]}
BASE = {"scottplot": {"anim": "off"}, "livecharts": {"animate": False}, "oxyplot": {"animate": False}, "procharts": {"animate": False}}
ANIM = {"scottplot": {"anim": "proto"}, "livecharts": {"animate": True}}


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--apk", required=True)
    p.add_argument("--serial", default="R3CW70202FP")
    p.add_argument("--rest", type=float, default=30)
    p.add_argument("--tag", default="i1853gp")
    p.add_argument("--rounds", default="1,2,3")
    a = p.parse_args()
    M = os.path.join(ROOT, "logs", "otest_m3", a.tag)
    os.makedirs(C, exist_ok=True)
    os.makedirs(M, exist_ok=True)
    sr = a.serial
    state, code = {}, 0
    summ = {"apk": a.apk, "tag": a.tag, "rest_s": a.rest, "order": ORDER, "runs": [], "skips": [], "anomalies": [], "idle": []}
    sumf = os.path.join(M, "chart_summary_%s.json" % a.tag)

    def dump():
        with open(sumf, "w", encoding="utf-8") as f:
            json.dump(summ, f, ensure_ascii=False, indent=1)

    def save(name, obj):
        obj = dict(obj)
        obj["_host_load"] = os.getloadavg()
        obj["_at"] = time.strftime("%Y-%m-%dT%H:%M:%S")
        with open(os.path.join(C, "chart_android_%s_%s.json" % (a.tag, name)), "w", encoding="utf-8") as f:
            json.dump(obj, f, ensure_ascii=False, indent=1)

    def batt():
        rc, bat = ra.adb(sr, "shell", "dumpsys", "battery")
        return {k.strip(): v.strip() for k, v in (ln.split(":", 1) for ln in bat.splitlines() if ":" in ln) if k.strip() in ("level", "temperature", "AC powered", "USB powered", "status")}

    try:
        info = ra.prepare(sr, a.apk, state)
        from retest_remote import Remote
        first = True
        for n in [int(x) for x in a.rounds.split(",")]:
            for lib in ORDER[n]:
                c2.launch(sr, "--fresh", info)
                time.sleep(4)
                r = Remote()
                rc, pid = ra.adb(sr, "shell", "pidof", ra.PKG)
                pid = pid.strip().split()[0] if pid.strip() else None

                def cpu():
                    rc, st = ra.adb(sr, "shell", "cat", "/proc/%s/stat" % pid)
                    f = st.split(")")[-1].split()
                    return (int(f[11]) + int(f[12])) / 100 if len(f) > 12 else None
                op = r.call("bench.open", target="chart", _timeout=120)
                r.call("layout.template.apply", id="tpl.tabbed")
                time.sleep(3)
                s0, c0, t0 = r.call("perf.idle") or {}, cpu(), time.time()
                time.sleep(10)
                s1, c1, t1 = r.call("perf.idle") or {}, cpu(), time.time()
                idle = {"r": n, "lib": lib, "frames_delta": (s1.get("frames") or 0) - (s0.get("frames") or 0), "cpu_pct": round((c1 - c0) / (t1 - t0) * 100, 1) if c0 is not None and c1 is not None else None,
                        "frame_pump": (r.call("perf.get") or {}).get("frame_pump"), "battery": batt(), "host_load": [round(x, 2) for x in os.getloadavg()]}
                idle["gate_pass"] = (idle["cpu_pct"] or 99) < 5 and idle["frames_delta"] <= 5 and not idle["frame_pump"]
                summ["idle"].append(idle)
                if first:
                    env = r.call("bench.env")
                    save("env", {"bench_env": env, "open": op, "idle": idle})
                    libs = json.dumps(env, ensure_ascii=False).lower()
                    missing = [x for x in ("scottplot", "procharts", "livecharts", "oxyplot") if x not in libs]
                    summ["chart_libs_missing"] = missing
                    first = False
                    if missing or not idle["gate_pass"]:
                        print("GATE/LIBS FAIL", missing, idle, flush=True)
                        code = 2
                        return
                print("LIB", n, lib, "idle", idle, flush=True)
                dump()
                big = {}
                steps = []
                for ds in ("raw", "lttb"):
                    for pts, lab in ((10000, "10k"), (100000, "100k"), (1000000, "1m")):
                        steps.append(("static", ds, pts, lab))
                for kind, ds, pts, lab in steps:
                    if pts == 1000000 and big.get(ds) and big[ds] > 5000:
                        summ["skips"].append({"r": n, "lib": lib, "step": "static_%s_1m" % ds, "reason": "10만 first_show_ms %s > 5000(2-1절)" % big[ds]})
                        print("SKIP", lib, ds, "1m", big[ds], flush=True)
                        continue
                    res = r.call("bench.chart", scenario="static", lib=lib, points=pts, ds=ds, _timeout=60)
                    last = r.calls[-1]
                    st = (res or {}).get("static") if isinstance(res, dict) else None
                    st = st if isinstance(st, dict) else {}
                    save("%s_static_%s_%s_r%d" % (lib, ds, lab, n), {"ok": last.get("ok"), "error": last.get("error"), "result": res, "battery": batt()})
                    fs = st.get("first_show_ms")
                    if pts == 100000:
                        big[ds] = fs or 0
                    exp_dp = pts if ds == "raw" else 2000
                    an = []
                    if st.get("adapter") not in (None, lib) and str(st.get("adapter")).lower() != lib:
                        an.append("adapter=%s" % st.get("adapter"))
                    if st.get("drawn_points") is not None and st.get("drawn_points") != exp_dp:
                        an.append("drawn_points=%s(기대 %s)" % (st.get("drawn_points"), exp_dp))
                    if st.get("max_points") not in (None, 0):
                        an.append("max_points=%s" % st.get("max_points"))
                    if not last.get("ok"):
                        an.append("error=%s" % last.get("error"))
                    rd = st.get("redraw") or {}
                    md = st.get("memory_delta") or {}
                    summ["runs"].append({"r": n, "lib": lib, "step": "static_%s_%s" % (ds, lab), "first_show_ms": fs, "redraw_p95_ms": rd.get("p95_ms"), "redraw_timed_out": rd.get("timed_out"),
                                         "bytes_per_point": md.get("managed_bytes_per_point"), "font": st.get("font"), "anomaly": an})
                    print("STATIC", n, lib, ds, lab, last.get("ok"), fs, rd.get("p95_ms"), md.get("managed_bytes_per_point"), an, flush=True)
                    dump()
                    if an:
                        summ["anomalies"].append({"r": n, "lib": lib, "step": "static_%s_%s" % (ds, lab), "anomaly": an})
                        if any(x.startswith(("adapter", "drawn_points", "max_points")) for x in an):
                            print("STOP anomaly", an, flush=True)
                            code = 3
                            return
                    if n == 1 and lib == "oxyplot" and ds == "lttb" and pts == 100000:
                        rc, data = ra.adb(sr, "exec-out", "screencap", "-p", binary=True)
                        k = data.find(b"\x89PNG") if rc == 0 else -1
                        if k >= 0:
                            with open(os.path.join(C, "chart_android_%s_oxyplot_korean_r1.png" % a.tag), "wb") as f:
                                f.write(data[k:])
                            summ["korean_png"] = "chart_android_%s_oxyplot_korean_r1.png" % a.tag
                streams = [("base", BASE[lib])] + ([("anim", ANIM[lib])] if lib in ANIM else [])
                for lab, kw in streams:
                    res = r.call("bench.chart", scenario="stream", lib=lib, window=10000, rate=1000, fill=True, seconds=60, _timeout=90, **kw)
                    last = r.calls[-1]
                    sm = (res or {}).get("stream") if isinstance(res, dict) else None
                    sm = sm if isinstance(sm, dict) else {}
                    save("%s_stream_%s_r%d" % (lib, lab, n), {"ok": last.get("ok"), "error": last.get("error"), "args": kw, "result": res, "battery": batt()})
                    an = []
                    if sm.get("fill") is False or (sm.get("prefilled_points") not in (None, 10000)):
                        an.append("fill=%s prefilled=%s" % (sm.get("fill"), sm.get("prefilled_points")))
                    if not last.get("ok"):
                        an.append("error=%s" % last.get("error"))
                    tgt = sm.get("target_points_per_s")
                    ach = sm.get("achieved_points_per_s")
                    summ["runs"].append({"r": n, "lib": lib, "step": "stream_%s" % lab, "fps": sm.get("fps"), "p95_ms": sm.get("p95_ms"), "reflect": round(ach / tgt, 3) if ach and tgt else None,
                                         "managed_mb": (sm.get("memory_delta") or {}).get("managed_mb"), "alloc_kb_per_frame": (sm.get("alloc") or {}).get("allocated_kb_per_frame"),
                                         "timed_out": sm.get("timed_out"), "anomaly": an})
                    print("STREAM", n, lib, lab, last.get("ok"), sm.get("fps"), sm.get("p95_ms"), ach, tgt, an, flush=True)
                    if an:
                        summ["anomalies"].append({"r": n, "lib": lib, "step": "stream_%s" % lab, "anomaly": an})
                    dump()
                if lib == "scottplot":
                    res = r.call("bench.chart", scenario="static", lib="scottplot", points=100000, ds="raw", anim="proto", _timeout=60)
                    last = r.calls[-1]
                    st = (res or {}).get("static") if isinstance(res, dict) else {}
                    save("scottplot_static_raw_100k_proto_r%d" % n, {"ok": last.get("ok"), "error": last.get("error"), "result": res, "battery": batt()})
                    summ["runs"].append({"r": n, "lib": lib, "step": "static_raw_100k_proto", "first_show_ms": (st or {}).get("first_show_ms"), "redraw_p95_ms": ((st or {}).get("redraw") or {}).get("p95_ms")})
                    dump()
                time.sleep(a.rest)
        summ["battery_end"] = batt()
        summ["host_load_end"] = os.getloadavg()
    except Exception as e:  # noqa: BLE001
        summ["error"] = repr(e)
        print("ERROR", repr(e), flush=True)
        code = 2
    finally:
        dump()
        ra.cleanup(sr, state)
        print("EXIT", code, flush=True)
        sys.exit(code)


if __name__ == "__main__":
    main()
