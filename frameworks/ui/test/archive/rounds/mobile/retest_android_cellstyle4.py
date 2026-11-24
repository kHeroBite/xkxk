# i1853gp Pro cell_style 보완 측정(cell_style_procedure.md 04f2d639 v4): 6-1 3모드 교대(G1 직후 col=14 고정)→6-2 상태 열 화면 안 세트(col=6)→7 statusPct 를 한 앱 세션에서 재는 스크립트(otest-m-3, retest_android_cellstyle3.py c55b51ee 기반)
"""
사용: python3 -E -u tools/retest_android_cellstyle4.py --apk <i1853gp APK> [--rest 30] [--tag i1853gp] [--parts 61,62,7]
결과: logs/odevgc/remeasure_android_Pro_*_{tag}_*.json (사본 logs/otest_m3/{tag}/), 요약 logs/otest_m3/{tag}/cellstyle4_summary_{tag}.json
측정 구간에는 perf.get·perf.idle 만 보낸다. 사이에는 ui.find·grid.visible(읽기 전용)과 절차서가 정한 ui.scroll 만 쓴다.
rc: 0 정상, 2 유휴 게이트·예외, 3 멈춤 조건
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
EXPECT = {
    "off": {"conditional_formatting": False, "cf_rules": 0, "cf_model": "empty", "frozen_right": 0, "cell_style": False},
    "frozen_only": {"conditional_formatting": False, "cf_rules": 0, "cf_model": "empty", "frozen_right": 1, "cell_style": False},
    "cell_style": {"conditional_formatting": False, "cf_rules": 0, "cf_model": "empty", "frozen_right": 0, "cell_style": True},
    "cf_only": {"conditional_formatting": True, "cf_rules": 1, "cf_model": "bench", "frozen_right": 0, "cell_style": False},
}
RULE = {"off": False, "frozen_only": False, "cell_style": True, "cf_only": True}
ORDER = {1: ["off", "frozen_only", "cell_style"], 2: ["cell_style", "off", "frozen_only"], 3: ["frozen_only", "cell_style", "off"]}
GRID_ID = "bench.grid.main"


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--apk", required=True)
    p.add_argument("--serial", default="R3CW70202FP")
    p.add_argument("--rest", type=float, default=30)
    p.add_argument("--tag", default="i1853gp")
    p.add_argument("--parts", default="61,62,7")
    p.add_argument("--rt-col", type=int, default=16, help="6-1 G1 직후 ui.scroll col(절차서 14, Fold5 에서 3열 모두 보이게 16)")
    a = p.parse_args()
    parts = set(a.parts.split(","))
    M = os.path.join(ROOT, "logs", "otest_m3", a.tag)
    os.makedirs(G, exist_ok=True)
    os.makedirs(M, exist_ok=True)
    sr = a.serial
    state, code = {}, 0
    summary = {"apk": a.apk, "tag": a.tag, "rest_s": a.rest, "order": ORDER, "G1": {}, "G3": {}, "checks": [], "cols": [], "inview": [], "status": []}
    sumf = os.path.join(M, "cellstyle4_summary_%s.json" % a.tag)

    def dump_summary():
        with open(sumf, "w", encoding="utf-8") as f:
            json.dump(summary, f, ensure_ascii=False, indent=1)

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

        def call(cmd, **kw):
            res = r.call(cmd, **kw)
            last = r.calls[-1] if r.calls else {}
            return res, last.get("ok", res is not None), last.get("error")

        def gid():
            return GRID_ID if summary.get("grid_id") is None else summary["grid_id"]

        def scroll(col):
            res, ok, err = call("ui.scroll", id=gid(), col=col, index=0)
            if not ok and gid() == GRID_ID:
                summary["grid_id"] = "BenchGrid"
                res, ok, err = call("ui.scroll", id="BenchGrid", col=col, index=0)
            time.sleep(1.0)
            ac = (res or {}).get("afterCols") if isinstance(res, dict) else None
            return {"col": col, "ok": ok, "error": err, "resp": res, "afterCols": ac, "cols": (ac or {}).get("cols")}

        def cols_now():
            res, ok, err = call("grid.visible", id=gid())
            return ((res or {}).get("columns") or {}).get("cols") if isinstance(res, dict) else None

        # 0. 시작 전 확인
        env = r.call("bench.open", target="grid") or {}
        r.call("layout.template.apply", id="tpl.tabbed")
        time.sleep(3)
        s0, c0, t0 = r.call("perf.idle") or {}, cpu(), time.time()
        time.sleep(10)
        s1, c1, t1 = r.call("perf.idle") or {}, cpu(), time.time()
        pre = {"idle": {"frames_delta": (s1.get("frames") or 0) - (s0.get("frames") or 0), "cpu_pct": round((c1 - c0) / (t1 - t0) * 100, 1)},
               "frame_pump": (r.call("perf.get") or {}).get("frame_pump"), "battery": batt(), "env": env.get("env", env), "bench_env": r.call("bench.env"),
               "wm_size": ra.adb(sr, "shell", "wm", "size")[1].strip()}
        pre["gate_pass"] = pre["idle"]["cpu_pct"] < 5 and pre["idle"]["frames_delta"] <= 5 and not pre["frame_pump"]
        pre["grid_impl"] = (pre.get("bench_env") or {}).get("grid_impl") if isinstance(pre.get("bench_env"), dict) else None
        pre["host_load"] = os.getloadavg()
        save("cellstyle4_precheck_%s" % a.tag, pre)
        summary["precheck"] = {k: pre[k] for k in ("idle", "frame_pump", "battery", "gate_pass", "grid_impl", "host_load", "wm_size")}
        print("precheck", pre["idle"], "frame_pump", pre["frame_pump"], "gate", pre["gate_pass"], pre["grid_impl"], pre["battery"], flush=True)
        dump_summary()
        if not pre["gate_pass"]:
            print("GATE_FAIL", flush=True)
            code = 2
            return

        def pf(mode):
            res, ok, err = call("bench.grid", scenario="pro_features", mode=mode, _timeout=120)
            return (res or {}).get("pro_features") if isinstance(res, dict) else None, ok, err

        def check(mode, n, when, name=None):
            ro = r.call("ui.find", id="BenchGrid", props="FrozenColumnCountRight") if when == "after_G1" else None
            pfr, okc, err = pf(mode)
            ok = isinstance(pfr, dict) and all(pfr.get(k) == v for k, v in EXPECT[mode].items())
            hl = (pfr or {}).get("highlight") if isinstance(pfr, dict) else None
            hl = hl if isinstance(hl, dict) else {}
            ok = ok and hl.get("rule_active") is RULE[mode]
            stop = None
            if when == "after_G1":
                if hl.get("match") is not True:
                    stop = "highlight.match=%s" % hl.get("match")
                elif mode == "cf_only" and hl.get("highlighted") == 0 and (hl.get("realized_cells") or 0) > 0:
                    stop = "cf_only highlighted=0 realized_cells=%s" % hl.get("realized_cells")
                elif RULE[mode] and (hl.get("expected_highlight") or 0) > 0 and hl.get("highlighted") != hl.get("expected_highlight"):
                    stop = "highlighted(%s)!=expected(%s)" % (hl.get("highlighted"), hl.get("expected_highlight"))
            fr = ((ro or {}).get("props") or {}).get("FrozenColumnCountRight") if ro else None
            rec = {"pro_features": pfr, "error": err, "readonly_FrozenColumnCountRight": fr, "expect": dict(EXPECT[mode], rule_active=RULE[mode]), "match": ok, "stop": stop}
            if when == "after_G1":
                rec["ui_find_grid"] = ro
            save(name or "%s_pro_features_%s_%s_r%d" % (mode, when, a.tag, n), rec)
            summary["checks"].append({"mode": mode, "n": n, "when": when, "match": ok, "stop": stop, "readonly_frozen": fr, "highlight": hl})
            print("check", mode, n, when, ok, "stop", stop, "highlight", hl, flush=True)
            return ok and not stop, hl

        def g3(mode, rate, name, **extra):
            idle = r.call("perf.idle")
            res, ok, err = call("bench.grid", scenario="realtime", seconds=5, rate=rate, focus=60, _timeout=240, **extra)
            pg = r.call("perf.get")
            bt = batt()
            cn = cols_now()
            save(name, {"ok": ok, "error": err, "result": res, "perf_idle_before": idle, "perf_get_after": pg, "battery": bt, "cols_after": cn})
            rt = (res or {}).get("realtime", {}) if isinstance(res, dict) else {}
            row = dict({k: rt.get(k) for k in ("fps", "p50_ms", "p95_ms", "max_ms", "achieved_cells_per_s", "timed_out", "status_flips", "method")},
                       highlight=rt.get("highlight"), temp=bt.get("temperature"), host_load1=round(os.getloadavg()[0], 2), cols_after=cn)
            print(name, ok, rt.get("fps"), rt.get("p95_ms"), rt.get("achieved_cells_per_s"), rt.get("timed_out"), "flips", rt.get("status_flips"), "cols", cn, flush=True)
            return row

        # 준비 로드
        wu = r.call("bench.grid", scenario="load", rows=100000, _timeout=240)
        save("warmup_G1_load100k_%s_r1" % a.tag, {"ok": r.calls[-1]["ok"], "error": r.calls[-1].get("error"), "result": wu, "battery": batt(), "note": "v2 준비 로드, 판정 미사용"})
        print("WARMUP", r.calls[-1]["ok"], flush=True)
        time.sleep(a.rest)

        # ---- 6-1(v4): 3모드 교대, G1 직후 col=14 고정 ----
        if "61" in parts:
            for n in (1, 2, 3):
                for mode in ORDER[n]:
                    ok, _ = check(mode, n, "preG1")
                    if not ok:
                        print("MISMATCH", mode, n, "preG1", flush=True)
                        code = 3
                        return
                    time.sleep(a.rest)
                    g1 = r.call("bench.grid", scenario="load", rows=100000, _timeout=240)
                    save("%s_G1_load100k_%s_r%d" % (mode, a.tag, n), {"ok": r.calls[-1]["ok"], "error": r.calls[-1].get("error"), "result": g1, "battery": batt()})
                    fs = ((g1 or {}).get("load") or {}).get("first_show_ms") if isinstance(g1, dict) else None
                    summary["G1"].setdefault(mode, []).append({"first_show_ms": fs, "r": n})
                    # 절차서 v4 는 col=14 이지만 Fold5 펼침에서 col=14 는 14 를 오른쪽 끝에만 넣어 [0,1,9~14](실시간 열 1개)가 됐다(20:00 실측, 중단분).
                    # 실시간 열 3개(14·15·16)를 모두 화면에 넣으려고 col=16 을 쓴다 — 보고에 편차로 적는다
                    sc = scroll(a.rt_col)
                    save("%s_scroll_col%d_%s_r%d" % (mode, a.rt_col, a.tag, n), sc)
                    rtcols = [c for c in (sc.get("cols") or []) if c in (14, 15, 16)]
                    summary["cols"].append({"mode": mode, "n": n, "afterCols": sc.get("cols"), "realtime_cols_visible": len(rtcols), "ok": sc.get("ok"), "error": sc.get("error")})
                    print("G1", mode, n, fs, "col14", sc.get("ok"), sc.get("cols"), flush=True)
                    dump_summary()
                    time.sleep(3)
                    ok, _ = check(mode, n, "after_G1")
                    if not ok:
                        print("MISMATCH", mode, n, "after_G1", flush=True)
                        code = 3
                        return
                    time.sleep(a.rest)
                    for rate in (3000, 10000):
                        row = g3(mode, rate, "%s_G3_realtime%d_%s_r%d" % (mode, rate, a.tag, n))
                        summary["G3"].setdefault("%s_%d" % (mode, rate), []).append(dict(row, r=n))
                        dump_summary()
                        time.sleep(a.rest)

        # ---- 6-2: 상태 열 화면 안(col=6), off → cell_style → cf_only ----
        status_col = 6
        if "62" in parts:
            for mode in ("off", "cell_style", "cf_only"):
                pf(mode)
                g1 = r.call("bench.grid", scenario="load", rows=100000, _timeout=240)
                sc = scroll(status_col)
                if 6 not in (sc.get("cols") or []) and status_col == 6:
                    sc2 = scroll(7)
                    sc = dict(sc, retry_col7=sc2)
                save("%s_scroll_inview_%s_v1" % (mode, a.tag), dict(sc, G1=g1))
                time.sleep(2)
                pfr, okc, err = pf(mode)
                hl = (pfr or {}).get("highlight") or {}
                exp_ok = isinstance(pfr, dict) and all(pfr.get(k) == v for k, v in EXPECT[mode].items())
                save("%s_pro_features_inview_%s_v1" % (mode, a.tag), {"pro_features": pfr, "error": err, "expect_match": exp_ok, "cols": cols_now()})
                print("INVIEW", mode, "cols", sc.get("cols"), "highlight", hl, flush=True)
                rec = {"mode": mode, "afterCols": sc.get("cols"), "status_in_view": 6 in (sc.get("cols") or []), "highlight": hl, "expect_match": exp_ok}
                if (hl.get("realized_cells") or 0) <= 1 or (RULE[mode] and hl.get("match") is not True):
                    rec["stop"] = "realized_cells<=1 또는 match false"
                    summary["inview"].append(rec)
                    dump_summary()
                    print("INVIEW_STOP", rec, flush=True)
                    code = 3
                    return
                time.sleep(a.rest)
                for rate in (3000, 10000):
                    row = g3(mode, rate, "%s_G3_realtime%d_inview_%s_v1" % (mode, rate, a.tag))
                    rec["G3_%d" % rate] = row
                    time.sleep(a.rest)
                fd = r.call("ui.find", id="BenchGrid")
                rec["cols_end"] = cols_now()
                save("%s_scroll_inview_end_%s_v1" % (mode, a.tag), {"ui_find_grid": fd, "cols": rec["cols_end"]})
                summary["inview"].append(rec)
                dump_summary()

        # ---- 7: statusPct(세션 맨 끝) ----
        if "7" in parts:
            SORD = {1: ["cf_only", "cell_style"], 2: ["cell_style", "cf_only"], 3: ["cf_only", "cell_style"]}
            for s in (1, 2, 3):
                for mode in SORD[s]:
                    p0, _, _ = pf(mode)
                    save("%s_status_pre_%s_s%d" % (mode, a.tag, s), {"pro_features": p0})
                    g1 = r.call("bench.grid", scenario="load", rows=100000, _timeout=240)
                    save("%s_status_G1_%s_s%d" % (mode, a.tag, s), {"result": g1})
                    sc = scroll(status_col)
                    time.sleep(2)
                    ok, hl = check(mode, s, "after_G1", name="%s_status_after_G1_%s_s%d" % (mode, a.tag, s))
                    time.sleep(a.rest)
                    row = g3(mode, 10000, "%s_G3_status1pct_%s_s%d" % (mode, a.tag, s), statusPct=1)
                    summary["status"].append(dict(row, mode=mode, s=s, after_G1_ok=ok, after_G1_highlight=hl, cols=sc.get("cols")))
                    dump_summary()
                    time.sleep(a.rest)
        summary["battery_end"] = batt()
        summary["host_load_end"] = os.getloadavg()
    except Exception as e:  # noqa: BLE001
        summary["error"] = repr(e)
        print("ERROR", repr(e), flush=True)
        code = 2
    finally:
        dump_summary()
        ra.cleanup(sr, state)
        print("EXIT", code, flush=True)
        sys.exit(code)


if __name__ == "__main__":
    main()
