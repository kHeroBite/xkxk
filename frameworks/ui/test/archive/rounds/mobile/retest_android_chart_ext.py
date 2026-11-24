# Android 차트 추가 측정(한글 캡처 A·확장 제목 B·ScottPlot R2 base D)을 Fold5 에서 수행하는 스크립트(otest-m-5)
"""
사용: python3 -E -u tools/retest_android_chart_ext.py --apk <APK> [--phases A,B,D] [--load-max 2.0]
A: 정적 lttb 10만 점 화면 캡처(scottplot·livecharts·procharts) -> chart_android_<tag>_<lib>_korean_r1.png
B: A 와 같되 title="프로젝트별 매출 및 손익 현황" (4종) -> chart_android_<tag>_<lib>_korean_title_r1.png
D: scottplot 스트리밍 base 1회(load1<max 대기) -> chart_android_<tag>_scottplot_stream_base_q_r1.json
"""
import argparse
import json
import os
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import retest_android as ra  # noqa: E402
import retest_android_cond2 as c2  # noqa: E402
from retest_web_dpf1c import display_rate  # noqa: E402

ROOT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
C = os.path.join(ROOT, "logs", "odevge", "chart")
TITLE = "프로젝트별 매출 및 손익 현황"


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--apk", required=True)
    p.add_argument("--serial", default="R3CW70202FP")
    p.add_argument("--tag", default="i1853gp")
    p.add_argument("--phases", default="A,B,D")
    p.add_argument("--load-max", type=float, default=2.0)
    p.add_argument("--load-wait", type=int, default=600)
    a = p.parse_args()
    sr = a.serial
    state, code = {}, 0
    log = {"phases": a.phases, "events": []}
    os.makedirs(C, exist_ok=True)

    def batt():
        rc, bat = ra.adb(sr, "shell", "dumpsys", "battery")
        return {k.strip(): v.strip() for k, v in (ln.split(":", 1) for ln in bat.splitlines() if ":" in ln) if k.strip() in ("level", "temperature", "AC powered", "USB powered", "status")}

    def shot(name):
        rc, data = ra.adb(sr, "exec-out", "screencap", "-p", binary=True)
        k = data.find(b"\x89PNG") if rc == 0 else -1
        if k >= 0:
            with open(os.path.join(C, name), "wb") as f:
                f.write(data[k:])
            return True
        return False

    def open_lib(info):
        c2.launch(sr, "--fresh", info)
        time.sleep(4)
        from retest_remote import Remote
        r = Remote()
        r.call("bench.open", target="chart", _timeout=120)
        r.call("layout.template.apply", id="tpl.tabbed")
        time.sleep(3)
        return r

    try:
        info = ra.prepare(sr, a.apk, state)
        for ph, libs, extra, suffix in (("A", ["scottplot", "livecharts", "procharts"], {}, "korean_r1.png"), ("B", ["scottplot", "livecharts", "procharts", "oxyplot"], {"title": TITLE}, "korean_title_r1.png")):
            if ph not in a.phases.split(","):
                continue
            for lib in libs:
                r = open_lib(info)
                res = r.call("bench.chart", scenario="static", lib=lib, points=100000, ds="lttb", _timeout=60, **extra)
                last = r.calls[-1]
                time.sleep(2)
                ok = shot("chart_android_%s_%s_%s" % (a.tag, lib, suffix))
                st = (res or {}).get("static") if isinstance(res, dict) else {}
                ev = {"phase": ph, "lib": lib, "ok": last.get("ok"), "error": last.get("error"), "png": ok, "font": (st or {}).get("font"), "adapter": (st or {}).get("adapter"), "drawn_points": (st or {}).get("drawn_points"), "battery": batt()}
                log["events"].append(ev)
                print("CAP", ev, flush=True)
        if "D" in a.phases.split(","):
            t0 = time.time()
            while os.getloadavg()[0] >= a.load_max and time.time() - t0 < a.load_wait:
                time.sleep(5)
            load_before = [round(x, 2) for x in os.getloadavg()]
            r = open_lib(info)
            b0 = display_rate(sr)
            try:
                res = r.call("bench.chart", scenario="stream", lib="scottplot", window=10000, rate=1000, fill=True, seconds=60, anim="off", _timeout=90)
                to = False
            except SystemExit:
                res, to = None, True
            last = r.calls[-1]
            b1 = display_rate(sr)
            obj = {"args": {"window": 10000, "rate": 1000, "fill": True, "seconds": 60, "anim": "off"}, "ok": last.get("ok"), "error": last.get("error"), "timeout": to, "result": res,
                   "host_load_before": load_before, "host_load_after": [round(x, 2) for x in os.getloadavg()], "load_gate_pass": load_before[0] < a.load_max, "waited_s": round(time.time() - t0),
                   "refresh_hz": {"before": b0.get("sf_vsync"), "after": b1.get("sf_vsync"), "render_before": b0.get("render_rate"), "render_after": b1.get("render_rate")}, "battery": batt(), "_at": time.strftime("%Y-%m-%dT%H:%M:%S")}
            with open(os.path.join(C, "chart_android_%s_scottplot_stream_base_q_r1.json" % a.tag), "w", encoding="utf-8") as f:
                json.dump(obj, f, ensure_ascii=False, indent=1)
            sm = (res or {}).get("stream") if isinstance(res, dict) else {}
            print("D", (sm or {}).get("fps"), (sm or {}).get("p95_ms"), obj["host_load_before"], obj["refresh_hz"], flush=True)
        log["battery_end"] = batt()
    except Exception as e:  # noqa: BLE001
        log["error"] = repr(e)
        print("ERROR", repr(e), flush=True)
        code = 2
    finally:
        with open(os.path.join(ROOT, "logs", "otest_m3", a.tag, "chart_ext_log_%s.json" % time.strftime("%H%M%S")), "w", encoding="utf-8") as f:
            json.dump(log, f, ensure_ascii=False, indent=1)
        ra.cleanup(sr, state)
        print("EXIT", code, flush=True)
        sys.exit(code)


if __name__ == "__main__":
    main()
