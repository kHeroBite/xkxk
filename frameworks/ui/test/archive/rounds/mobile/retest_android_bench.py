# bp1146a 벤치 기능 확인(Fold5): 그리드 20열 폭·한글 머리글 잘림·이름 정렬 앞 10개·차트 3종 예외·한글 축/범례 캡처·원격 라이브러리 선택의 콤보 반영을 기록하는 스크립트
"""
사용: python3 -E -u tools/retest_android_bench.py --apk <APK> --outdir <폴더>
시간 수치는 참고값(부하 기록 함께). 판정자가 캡처를 직접 연다.
"""
import argparse
import json
import os
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import retest_android as ra  # noqa: E402
import retest_android_cond2 as c2  # noqa: E402


def walk(t, pred, out):
    if isinstance(t, dict):
        if pred(t):
            out.append({k: t.get(k) for k in ("type", "name", "id", "text", "bounds", "visible") if k in t})
        for v in t.values():
            if isinstance(v, (dict, list)):
                walk(v, pred, out)
    elif isinstance(t, list):
        for v in t:
            walk(v, pred, out)
    return out


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--apk", required=True)
    p.add_argument("--serial", default="R3CW70202FP")
    p.add_argument("--outdir", required=True)
    a = p.parse_args()
    O, sr = a.outdir, a.serial
    os.makedirs(O, exist_ok=True)
    state, rep = {}, {"load_start": os.getloadavg()}

    def cap(name):
        rc, data = ra.adb(sr, "exec-out", "screencap", "-p", binary=True)
        k = data.find(b"\x89PNG") if rc == 0 else -1
        if k >= 0:
            open(os.path.join(O, name + ".png"), "wb").write(data[k:])
            return name + ".png"
        return None

    try:
        info = ra.prepare(sr, a.apk, state)
        from retest_remote import Remote
        c2.launch(sr, "--fresh", info)
        time.sleep(3)
        r = Remote()
        ra.adb(sr, "logcat", "-c")
        rep["env"] = r.call("bench.open", target="grid")
        time.sleep(1.5)
        # ① 그리드 로드 + 열 머리글 폭
        rep["grid_load"] = r.call("bench.grid", scenario="load", rows=100000, _timeout=180)
        time.sleep(1.5)
        tr = r.call("tree.get", named=False, depth=80) or {}
        hdr = walk(tr, lambda n: "DataGridColumnHeader" in str(n.get("type")), [])
        rep["headers"] = hdr
        rep["narrow_le20"] = [h for h in hdr if (h.get("bounds") or {}).get("w", 99) <= 20]
        rep["header_texts"] = walk(tr, lambda n: "TextBlock" in str(n.get("type")) and n.get("text") and any("가" <= ch <= "힣" for ch in str(n.get("text"))) and (n.get("bounds") or {}).get("y", 9999) < 400, [])[:40]
        rep["cap_grid"] = cap("bench_grid_loaded")
        # ② 이름 정렬 오름차순
        rep["grid_sort"] = r.call("bench.grid", scenario="sort", property="Name", culture="ko-KR", _timeout=180)
        time.sleep(1.5)
        rep["grid_visible_after_sort"] = r.call("grid.visible", id="bench.grid.main")
        tr2 = r.call("tree.get", named=False, depth=80) or {}
        rep["cells_after_sort"] = walk(tr2, lambda n: "TextBlock" in str(n.get("type")) and n.get("text") and (n.get("bounds") or {}).get("y", 0) > 300, [])[:120]
        rep["cap_sort"] = cap("bench_grid_sorted")
        # odev-gc 추가(team-lead 지시): app 정렬·영문 섞인 정렬·₩ 글리프. 결과는 logs/odevgc/bench_android_<impl>_<시나리오>.json 에도 남긴다
        G = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "logs", "odevgc"))
        os.makedirs(G, exist_ok=True)
        impl = "Pro" if "Pro" in json.dumps(rep.get("env"), ensure_ascii=False) and "Builtin" not in json.dumps(rep.get("env"), ensure_ascii=False) else "Builtin"
        env2 = r.call("bench.env") or {}
        rep["env_full"] = env2
        json.dump(env2, open(os.path.join(G, "bench_android_%s_env.json" % impl), "w", encoding="utf-8"), ensure_ascii=False, indent=1)
        gc = {}
        gc["load10k"] = r.call("bench.grid", scenario="load", rows=10000, _timeout=180)
        gc["sort_koKR"] = r.call("bench.grid", scenario="sort", property="Name", culture="ko-KR", _timeout=180)
        gc["sort_app"] = r.call("bench.grid", scenario="sort", property="Name", culture="app", _timeout=180)
        gc["sort_ordinal"] = r.call("bench.grid", scenario="sort", property="Name", culture="ordinal", _timeout=180)  # i1222ba 부터(리드 ①)
        gc["filter_latin"] = r.call("bench.grid", scenario="filter", latin=True, _timeout=180)
        gc["latin_sort_koKR"] = r.call("bench.grid", scenario="sort", property="Name", culture="ko-KR", _timeout=180)
        gc["latin_sort_app"] = r.call("bench.grid", scenario="sort", property="Name", culture="app", _timeout=180)
        rep["gc"] = gc
        for k, v in gc.items():
            json.dump(v, open(os.path.join(G, "bench_android_%s_%s.json" % (impl, k)), "w", encoding="utf-8"), ensure_ascii=False, indent=1)
        gc_load = r.call("bench.grid", scenario="load", rows=10000, _timeout=180)  # 필터 해제 겸 다시 채움
        bench_doc = ((r.call("docs.list") or [{}])[0]).get("id")
        r.call("layout.template.apply", id="tpl.tabbed")  # 문서를 넓게(₩ 열 확인용, odev-gc 제안)
        time.sleep(1.5)
        rep["won_scroll"] = r.call("ui.scroll", id="BenchGrid", col=12, doc=bench_doc)
        time.sleep(1)
        rep["cap_won"] = cap("bench_grid_won_col12")
        # 빈 칸 그리기 누락 판별(리드 10-29): 빈 곳 한 번 탭 → 캡처, 2초 뒤 → 캡처
        ra.adb(sr, "shell", "input", "tap", "1700", "2100")
        time.sleep(0.5)
        rep["cap_after_tap"] = cap("bench_grid_after_tap")
        time.sleep(2)
        rep["cap_after_2s"] = cap("bench_grid_after_2s")
        rep["gv_after"] = r.call("grid.visible", id="BenchGrid", doc=bench_doc)
        # ③④ 차트 3종
        rep["charts"] = {}
        for lib, pts in (("livecharts", 10000), ("livecharts", 100000), ("scottplot", 100000), ("procharts", 100000)):
            lib_key = lib if not (lib == "livecharts" and pts == 10000) else "livecharts_10k"
            res = r.call("bench.chart", scenario="static", lib=lib, points=pts, _timeout=180)
            c = r.calls[-1]
            time.sleep(2)
            combo = r.call("ui.find", id="bench.chart.lib")
            rc, lc = ra.adb(sr, "logcat", "-d", "-v", "brief")
            exc = [ln for ln in lc.splitlines() if any(s in ln for s in ("MissingMethod", "Exception", "FATAL"))]
            if exc:
                open(os.path.join(O, "logcat_%s.txt" % lib_key), "w", encoding="utf-8").write(lc)
            ra.adb(sr, "logcat", "-c")
            exc = [ln for ln in exc if "Finsky" not in ln]  # 시스템 Play 스토어 잡음 제외
            rep["charts"][lib_key] = {"ok": c["ok"], "error": c.get("error"), "ms": c["elapsed_ms"], "points": pts, "result": res, "combo": combo, "exceptions": exc[:20], "cap": cap("bench_chart_" + lib_key)}
        rep["load_end"] = os.getloadavg()
    finally:
        json.dump(rep, open(os.path.join(O, "bench_result.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=1)
        ra.cleanup(sr, state)
        ra.adb(sr, "shell", "wm", "size", "reset")


if __name__ == "__main__":
    main()
