# Android 차트 C(주사율 대체 규칙 재측정)·E(U1-a 실사용 실시간)·F(U3 소량 정적)를 Fold5 에서 재는 스크립트(otest-m-5)
"""
사용: python3 -E -u tools/retest_android_chart_ext2.py --apk <APK> [--rounds 1,2,3] [--parts C,E,F] [--rest 30]
C: oxyplot·livecharts × static raw 10k·100k + stream_base, 파일에 _rh. 주사율은 설정을 바꾸지 않고(대체 규칙) 측정 중 표본이 전부 120Hz 인 회차만 valid.
E: 4종 stream window=1000 rate=10 fill seconds=60 animate=false (r1 만) -> _stream_u1a_r1.json
F: 4종 static points=1000 ds=raw -> _static_raw_1k_r<n>.json
회차·lib 순서는 retest_android_chart.py ORDER 와 같다. lib 바뀔 때 앱 재기동, 휴지 --rest 초.
"""
import argparse
import json
import os
import sys
import threading
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import retest_android as ra  # noqa: E402
import retest_android_cond2 as c2  # noqa: E402
from retest_web_dpf1c import display_rate  # noqa: E402

ROOT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
C = os.path.join(ROOT, "logs", "odevge", "chart")
ORDER = {1: ["scottplot", "oxyplot", "procharts", "livecharts"], 2: ["livecharts", "scottplot", "oxyplot", "procharts"], 3: ["procharts", "livecharts", "scottplot", "oxyplot"]}
BASE = {"scottplot": {"anim": "off"}, "livecharts": {"animate": False}, "oxyplot": {"animate": False}, "procharts": {"animate": False}}
U1A = {"scottplot": {"anim": "off"}, "livecharts": {"animate": False}, "oxyplot": {"animate": False}, "procharts": {"animate": False}}


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--apk", required=True)
    p.add_argument("--serial", default="R3CW70202FP")
    p.add_argument("--rest", type=float, default=30)
    p.add_argument("--tag", default="i1853gp")
    p.add_argument("--rounds", default="1,2,3")
    p.add_argument("--parts", default="C,E,F")
    a = p.parse_args()
    parts = a.parts.split(",")
    sr = a.serial
    state, code = {}, 0
    M = os.path.join(ROOT, "logs", "otest_m3", a.tag)
    os.makedirs(M, exist_ok=True)
    summ = {"apk": a.apk, "parts": parts, "runs": [], "anomalies": []}
    sumf = os.path.join(M, "chart_ext2_summary_%s.json" % a.tag)

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

    def refresh_block(fn, every):
        """fn() 실행 전후와 실행 중 every 초 간격 표본의 SurfaceFlinger activeMode vsync Hz(읽기만). 설정은 바꾸지 않는다."""
        b0 = display_rate(sr)
        samples = []
        stop = threading.Event()

        def run():
            while not stop.wait(every):
                samples.append(display_rate(sr))
        th = threading.Thread(target=run, daemon=True)
        th.start()
        timed_out = False
        try:
            out = fn()
        except SystemExit:
            out, timed_out = None, True
        stop.set()
        th.join(timeout=10)
        b1 = display_rate(sr)
        sv = [x.get("sf_vsync") for x in samples]
        allv = [x for x in [b0.get("sf_vsync")] + sv + [b1.get("sf_vsync")] if x is not None]
        valid = bool(sv) and all(x is not None and x >= 119.5 for x in sv)
        return out, {"timeout": timed_out, "before": b0.get("sf_vsync"), "after": b1.get("sf_vsync"), "samples": sv, "min": min(allv) if allv else None,
                     "render_before": b0.get("render_rate"), "render_after": b1.get("render_rate"), "rule": "대체 규칙(설정 미변경, 측정 중 표본 전부 120Hz 만 valid)", "valid": valid}

    try:
        info = ra.prepare(sr, a.apk, state)
        from retest_remote import Remote
        for n in [int(x) for x in a.rounds.split(",")]:
            for lib in ORDER[n]:
                do_c = "C" in parts and lib in ("oxyplot", "livecharts")
                do_e = "E" in parts and n == 1
                do_f = "F" in parts
                if not (do_c or do_e or do_f):
                    continue
                c2.launch(sr, "--fresh", info)
                time.sleep(4)
                H = {"r": Remote()}

                def reboot():
                    c2.launch(sr, "--fresh", info)
                    time.sleep(4)
                    H["r"] = Remote()
                    H["r"].call("bench.open", target="chart", _timeout=120)
                    H["r"].call("layout.template.apply", id="tpl.tabbed")
                    time.sleep(3)
                H["r"].call("bench.open", target="chart", _timeout=120)
                H["r"].call("layout.template.apply", id="tpl.tabbed")
                time.sleep(3)

                def step(name, kind, kw, extra=None):
                    tmo = 60 if kind == "static" else 90
                    res, rhz = refresh_block(lambda: H["r"].call("bench.chart", scenario=kind, lib=lib, _timeout=tmo, **kw), every=(0.7 if kind == "static" else 10.0))
                    if rhz.get("timeout"):
                        save(name, {"refresh_hz": rhz, "ok": False, "error": "원격 제한 시간 초과(앱 무응답) — 재기동 후 계속", "args": kw, "result": None, "battery": batt()})
                        summ["anomalies"].append({"r": n, "lib": lib, "step": name, "anomaly": ["timeout"]})
                        print("TIMEOUT", n, lib, name, flush=True)
                        dump()
                        reboot()
                        return
                    last = H["r"].calls[-1]
                    save(name, {"refresh_hz": rhz, "ok": last.get("ok"), "error": last.get("error"), "args": kw, "result": res, "battery": batt()})
                    sub = (res or {}).get(kind) if isinstance(res, dict) else {}
                    sub = sub if isinstance(sub, dict) else {}
                    row = {"r": n, "lib": lib, "step": name, "ok": last.get("ok"), "refresh_valid": rhz["valid"], "refresh_before": rhz["before"], "refresh_min": rhz["min"], "samples": rhz["samples"]}
                    if kind == "static":
                        row.update({"first_show_ms": sub.get("first_show_ms"), "redraw_p95_ms": (sub.get("redraw") or {}).get("p95_ms"), "bytes_per_point": (sub.get("memory_delta") or {}).get("managed_bytes_per_point"), "drawn": sub.get("drawn_points")})
                    else:
                        row.update({"fps": sub.get("fps"), "p95_ms": sub.get("p95_ms"), "p99_ms": sub.get("p99_ms"), "max_ms": sub.get("max_ms"), "ach": sub.get("achieved_points_per_s"), "tgt": sub.get("target_points_per_s"),
                                    "alloc_kb": (sub.get("alloc") or {}).get("allocated_kb_per_frame"), "timed_out": sub.get("timed_out")})
                    summ["runs"].append(row)
                    print("STEP", row, flush=True)
                    dump()

                if do_c:
                    step("%s_static_raw_10k_rh_r%d" % (lib, n), "static", {"points": 10000, "ds": "raw"})
                    step("%s_static_raw_100k_rh_r%d" % (lib, n), "static", {"points": 100000, "ds": "raw"})
                    step("%s_stream_base_rh_r%d" % (lib, n), "stream", dict(window=10000, rate=1000, fill=True, seconds=60, **BASE[lib]))
                if do_e:
                    step("%s_stream_u1a_r1" % lib, "stream", dict(window=1000, rate=10, fill=True, seconds=60, **U1A[lib]))
                if do_f:
                    step("%s_static_raw_1k_r%d" % (lib, n), "static", {"points": 1000, "ds": "raw"})
                time.sleep(a.rest)
        summ["battery_end"] = batt()
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
