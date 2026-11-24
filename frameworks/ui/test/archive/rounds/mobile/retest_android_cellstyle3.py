# i1720rcgp Pro 같은 세션 3모드(off·frozen_only·cell_style) 교대 측정 스크립트(cell_style_procedure.md 0aed4776 §6-1, retest_android_cellstyle.py fec97e0e 기반, otest-m-3)
"""
사용: python3 -E -u tools/retest_android_cellstyle3.py --apk <i1720rcgp APK> [--rest 30] [--tag i1720rcgp]
앱 1회 기동 → 준비 로드 1회(판정 미사용) → 회차별 3모드 교대. 모드마다 preG1(기대값만) → G1 → after_G1(기대값+highlight 멈춤, 그리드 bounds 기록) → G3 3000 → G3 10000.
  r1 off → frozen_only → cell_style / r2 cell_style → off → frozen_only / r3 frozen_only → cell_style → off
측정 구간에는 perf.get·perf.idle 만 보낸다. 사이에는 ui.find(읽기 전용)만 쓴다.
측정을 모두 마친 뒤 근거 보강(판정 측정과 분리): cf_only·cell_style 에서 ui.scroll colEnd=true 로 상태 열을 화면에 넣고 index 0·5000·50000 에서 highlight 를 다시 센다.
결과: logs/odevgc/remeasure_android_Pro_{모드}_{단계}_{tag}_r{n}.json (사본 logs/otest_m3/{tag}/), 요약 logs/otest_m3/{tag}/cellstyle3_summary_{tag}.json
rc: 0 정상, 2 유휴 게이트·예외, 3 멈춤 조건(기대값 불일치·after_G1 highlight.match=false 등)
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
# 기대값(절차서 1절·6-1절). rule_active 는 highlight 안의 값이다
EXPECT = {
    "off": {"conditional_formatting": False, "cf_rules": 0, "cf_model": "empty", "frozen_right": 0, "cell_style": False},
    "frozen_only": {"conditional_formatting": False, "cf_rules": 0, "cf_model": "empty", "frozen_right": 1, "cell_style": False},
    "cell_style": {"conditional_formatting": False, "cf_rules": 0, "cf_model": "empty", "frozen_right": 0, "cell_style": True},
    "cf_only": {"conditional_formatting": True, "cf_rules": 1, "cf_model": "bench", "frozen_right": 0, "cell_style": False},
}
RULE = {"off": False, "frozen_only": False, "cell_style": True, "cf_only": True}
ORDER = {1: ["off", "frozen_only", "cell_style"], 2: ["cell_style", "off", "frozen_only"], 3: ["frozen_only", "cell_style", "off"]}


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--apk", required=True)
    p.add_argument("--serial", default="R3CW70202FP")
    p.add_argument("--rest", type=float, default=30)
    p.add_argument("--tag", default="i1720rcgp")
    p.add_argument("--no-evidence", action="store_true")
    a = p.parse_args()
    M = os.path.join(ROOT, "logs", "otest_m3", a.tag)
    os.makedirs(G, exist_ok=True)
    os.makedirs(M, exist_ok=True)
    sr = a.serial
    state, code = {}, 0
    summary = {"apk": a.apk, "tag": a.tag, "rest_s": a.rest, "order": ORDER, "G1": {}, "G3": {}, "checks": [], "bounds": [], "evidence": []}

    def save(name, obj):
        obj = dict(obj) if isinstance(obj, dict) else {"result": obj}
        obj["_host_load"] = os.getloadavg()
        obj["_at"] = time.strftime("%Y-%m-%dT%H:%M:%S")
        obj["_tag"] = a.tag
        fn = "remeasure_android_Pro_%s.json" % name
        with open(os.path.join(G, fn), "w", encoding="utf-8") as f:
            json.dump(obj, f, ensure_ascii=False, indent=1)
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

        def safe(cmd, **kw):
            try:
                res = r.call(cmd, **kw)
                last = r.calls[-1] if r.calls else {}
                return {"ok": bool(last.get("ok", res is not None)), "error": last.get("error"), "result": res}
            except Exception as e:  # noqa: BLE001
                return {"ok": False, "error": repr(e)}

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
        pre["grid_impl"] = (pre.get("bench_env") or {}).get("grid_impl") if isinstance(pre.get("bench_env"), dict) else None
        pre["host_load"] = os.getloadavg()
        save("cellstyle3_precheck_%s" % a.tag, pre)
        summary["precheck"] = {k: pre[k] for k in ("idle", "frame_pump", "battery", "gate_pass", "grid_impl", "host_load")}
        print("precheck", pre["idle"], "frame_pump", pre["frame_pump"], "gate", pre["gate_pass"], pre["grid_impl"], pre["battery"], flush=True)
        if not pre["gate_pass"]:
            print("GATE_FAIL", flush=True)
            code = 2
            return

        def check(mode, n, when, name=None):
            ro = r.call("ui.find", id="BenchGrid", props="FrozenColumnCountRight") if when == "after_G1" else None
            pf = (r.call("bench.grid", scenario="pro_features", mode=mode, _timeout=120) or {}).get("pro_features")
            ok = isinstance(pf, dict) and all(pf.get(k) == v for k, v in EXPECT[mode].items())
            hl = (pf or {}).get("highlight") if isinstance(pf, dict) else None
            hl = hl if isinstance(hl, dict) else {}
            ok = ok and hl.get("rule_active") is RULE[mode]
            stop = None
            if when == "after_G1":
                if hl.get("match") is not True:
                    stop = "highlight.match=%s" % hl.get("match")
                elif mode == "cf_only" and hl.get("highlighted") == 0 and (hl.get("realized_cells") or 0) > 0:
                    stop = "cf_only highlighted=0 realized_cells=%s" % hl.get("realized_cells")
                elif RULE[mode] and not (hl.get("highlighted") == hl.get("expected_highlight") and (hl.get("expected_highlight") or 0) > 0):
                    stop = "highlighted(%s)!=expected(%s)>0" % (hl.get("highlighted"), hl.get("expected_highlight"))
            fr = ((ro or {}).get("props") or {}).get("FrozenColumnCountRight") if ro else None
            rec = {"pro_features": pf, "readonly_FrozenColumnCountRight": fr, "expect": dict(EXPECT[mode], rule_active=RULE[mode]), "match": ok, "stop": stop}
            if when == "after_G1":
                # 절차서 6-1 추가 기록(읽기 전용): 그리드 bounds 와 상태 열 헤더 bounds
                rec["ui_find_grid"] = ro
                rec["ui_find_status_header"] = safe("ui.find", id="상태")
                summary["bounds"].append({"mode": mode, "n": n, "grid": (ro or {}).get("bounds") if isinstance(ro, dict) else None,
                                          "status_header": rec["ui_find_status_header"]})
            save(name or "%s_pro_features_%s_%s_r%d" % (mode, when, a.tag, n), rec)
            summary["checks"].append({"mode": mode, "n": n, "when": when, "match": ok, "stop": stop, "readonly_frozen": fr, "highlight": hl})
            print("check", mode, n, when, ok, "stop", stop, "highlight", hl, "readonly_frozen", fr, flush=True)
            return ok and not stop

        # 0단계: 준비 로드 1회(판정 미사용)
        wu = r.call("bench.grid", scenario="load", rows=100000, _timeout=240)
        save("warmup_G1_load100k_%s_r1" % a.tag, {"ok": r.calls[-1]["ok"], "error": r.calls[-1].get("error"), "result": wu, "battery": batt(), "note": "v2 준비 로드, 판정 미사용"})
        print("WARMUP", r.calls[-1]["ok"], flush=True)
        time.sleep(a.rest)
        for n in (1, 2, 3):
            for mode in ORDER[n]:
                if not check(mode, n, "preG1"):
                    print("MISMATCH", mode, n, "preG1", flush=True)
                    code = 3
                    return
                time.sleep(a.rest)
                g1 = r.call("bench.grid", scenario="load", rows=100000, _timeout=240)
                save("%s_G1_load100k_%s_r%d" % (mode, a.tag, n), {"ok": r.calls[-1]["ok"], "error": r.calls[-1].get("error"), "result": g1, "battery": batt()})
                fs = ((g1 or {}).get("load") or {}).get("first_show_ms") if isinstance(g1, dict) else None
                summary["G1"].setdefault(mode, []).append({"first_show_ms": fs, "r": n})
                print("G1", mode, n, r.calls[-1]["ok"], fs, flush=True)
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
                    bt = batt()
                    save("%s_G3_realtime%d_%s_r%d" % (mode, rate, a.tag, n), {"ok": c["ok"], "error": c.get("error"), "result": g3, "perf_idle_before": idle, "perf_get_after": pg, "battery": bt})
                    rt = (g3 or {}).get("realtime", {}) if isinstance(g3, dict) else {}
                    summary["G3"].setdefault("%s_%d" % (mode, rate), []).append(dict({k: rt.get(k) for k in ("fps", "p50_ms", "p95_ms", "max_ms", "achieved_cells_per_s", "timed_out")}, r=n, temp=bt.get("temperature"), host_load1=round(os.getloadavg()[0], 2)))
                    print(mode, rate, n, c["ok"], rt.get("fps"), rt.get("p95_ms"), rt.get("achieved_cells_per_s"), rt.get("timed_out"), "temp", bt.get("temperature"), flush=True)
                    time.sleep(a.rest)
        summary["battery_end"] = batt()
        summary["host_load_end"] = os.getloadavg()

        # 근거 보강(판정 측정과 분리, 측정 뒤) — 상태 열을 가로로 화면에 넣고 여러 행 위치에서 highlight 를 다시 센다
        if not a.no_evidence:
            for mode in ("cf_only", "cell_style"):
                pf0 = safe("bench.grid", scenario="pro_features", mode=mode, _timeout=120)
                for idx in (0, 5000, 50000):
                    sc = safe("ui.scroll", id="BenchGrid", colEnd=True, index=idx)
                    time.sleep(1.5)
                    pf = safe("bench.grid", scenario="pro_features", mode=mode, _timeout=120)
                    hl = (((pf.get("result") or {}).get("pro_features") or {}).get("highlight")) if pf.get("ok") else None
                    gv = safe("grid.visible", id="BenchGrid")
                    rec = {"mode": mode, "index": idx, "scroll": sc, "pro_features": pf, "grid_visible": gv, "mode_set": pf0.get("ok")}
                    save("%s_evidence_col_end_idx%d_%s" % (mode, idx, a.tag), rec)
                    summary["evidence"].append({"mode": mode, "index": idx, "scroll_ok": sc.get("ok"), "scroll_error": sc.get("error"),
                                                "afterCols": ((sc.get("result") or {}).get("afterCols") if sc.get("ok") else None), "highlight": hl})
                    print("EVIDENCE", mode, idx, "scroll", sc.get("ok"), sc.get("error"), "highlight", hl, flush=True)
            safe("ui.scroll", id="BenchGrid", col=0, index=0)
    except Exception as e:  # noqa: BLE001
        summary["error"] = repr(e)
        print("ERROR", repr(e), flush=True)
        code = 2
    finally:
        with open(os.path.join(M, "cellstyle3_summary_%s.json" % a.tag), "w", encoding="utf-8") as f:
            json.dump(summary, f, ensure_ascii=False, indent=1)
        ra.cleanup(sr, state)
        ra.adb(sr, "shell", "wm", "size", "reset")
        print("EXIT", code, flush=True)
        sys.exit(code)


if __name__ == "__main__":
    main()
