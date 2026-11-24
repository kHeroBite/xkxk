# Q4 분해 단계 1(odev-3 q4_regression_plan.md): Fold5 에서 A 와 R1(23:10 근사)을 번갈아(ABAB) 기동해 theme.next·ctrlWheel·fontName 12회와 스위치 적용 확인을 기록하는 스크립트
"""
사용: python3 -I -u tools/retest_android_q4r1.py --apk <진단 APK> [--sets 3] [--order A,R1] [--outdir logs/otest_m1/<판>_q4r1]
조건마다 --fresh 로 다시 기동 → 문서 grid·gantt·form → 테마 pd-light 고정 → perf-reset → 12회(theme.next·input.ctrlWheel·settings.set fontName 교대)
적용 확인(perf.get·lang.get, 하나라도 다르면 그 세트 valid=False). 각 세트 시작 시각과 호스트 loadavg 를 남긴다.
"""
import argparse
import datetime
import json
import os
import statistics
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import retest_android as ra  # noqa: E402
import retest_android_cond2 as c2  # noqa: E402  launch 재사용

R1_ARGS = "--fresh --theme-new-tokens off --fluent-override off --chrome-inactive-opacity 1 --notify-panel off --loc static --fluent-palette always"
CONDS = {"A": "--fresh", "R1": R1_ARGS,
         "R1_cf": R1_ARGS + " --cell-font family", "R1_cb": R1_ARGS + " --cell-font both"}  # 단계 3(odev-3 10-29): 단계 1 차이가 남을 때만, CellFontDiag 861e2c 판부터
# 단계 2(odev-3 10-29 리드 확정): R1 에서 한 요인의 인자만 뺀다 = 그 요인만 켬
for _name, _drop in (("R1+C8", ["--loc static"]), ("R1+C1C2", ["--theme-new-tokens off", "--fluent-override off"]),
                     ("R1+C1", ["--theme-new-tokens off"]), ("R1+C6", ["--chrome-inactive-opacity 1"]), ("R1+C7", ["--notify-panel off"])):
    _a = R1_ARGS
    for _d in _drop:
        _a = _a.replace(" " + _d, "")
    CONDS[_name] = _a
CONDS["A-C6"] = "--fresh --chrome-inactive-opacity 1"  # 리드 10-3x(odev-7c 제안): A 에서 비활성 흐림 Opacity 층만 끔
CONDS["A+C6a"] = "--fresh --chrome-inactive-dim brush"  # 디자인 변화 없는 대안(브러시 흐림)
# 단계 2b(odev-3 10-29 리드 승인): 알림 패널 몫 분해
CONDS["A_off"] = "--fresh --notify-panel off"
CONDS["A_empty"] = "--fresh --notify-panel empty"
CONDS["A_legacy"] = "--fresh --notify-layout legacy"


def win_cpu():
    """Windows 전체 CPU 사용률(%) — Windows Update 등 호스트 부하 기록용(실패하면 None)."""
    import subprocess
    try:
        out = subprocess.run(["powershell.exe", "-NoProfile", "-Command", "(Get-CimInstance Win32_Processor | Measure-Object LoadPercentage -Average).Average"],
                             capture_output=True, text=True, timeout=30).stdout.strip()
        return float(out) if out else None
    except Exception:
        return None


def size_snap(r, grid_doc):
    """알림 패널·그리드 크기 스냅샷: panel.bounds NotificationPane, grid.visible(행 수), grid.dgMain bounds(ui.find)."""
    pb = r.call("panel.bounds") or {}
    gv = r.call("grid.visible", id="grid.dgMain", doc=grid_doc) or {}
    gf = r.call("ui.find", id="grid.dgMain", doc=grid_doc) or {}
    if isinstance(gf, list):
        gf = gf[0] if gf else {}
    return {"NotificationPane": pb.get("NotificationPane"), "Documents": pb.get("Documents"),
            "grid_rows_visible": len(gv.get("rows") or []) if isinstance(gv.get("rows"), list) else gv.get("rows"),
            "grid_first_last": [gv.get("first"), gv.get("last")], "grid_bounds": gf.get("bounds") if isinstance(gf, dict) else None}
VARIANT = {"pd-light": "L", "pd-light-prev": "L", "pd-ocean": "L", "pd-forest": "L", "pd-dark": "D", "pd-contrast": "D", "pd-ember": "D"}


def med(rows):
    v = [x["elapsed_ms"] for x in rows if x["ok"] and x["elapsed_ms"] is not None]
    return {"n": len(v), "median": round(statistics.median(v), 1), "min": min(v), "max": max(v)} if v else {"n": 0}


def checks(r, cond):
    perf = r.call("perf.get") or {}
    lang = r.call("lang.get") or {}
    ov, cd = str(perf.get("theme_overrides")), str(perf.get("chrome_diag"))
    c = {"theme_overrides": perf.get("theme_overrides"), "chrome_diag": perf.get("chrome_diag"), "render_kind": perf.get("render_kind"),
         "notify_panel": perf.get("notify_panel"), "notify_view_instances": perf.get("notify_view_instances"),
         "notify_view_attached": perf.get("notify_view_attached"), "loc_staticMode": lang.get("staticMode"), "loc_staticCount": lang.get("staticCount")}
    c["cell_font"] = perf.get("cell_font")
    c["notify_content"], c["notify_layout_legacy"], c["notify_view"] = perf.get("notify_content"), perf.get("notify_layout_legacy"), perf.get("notify_view")
    a = CONDS[cond]  # 인자에 든 스위치마다 적용값을 확인한다(빠진 스위치는 기본값이어야 한다)
    need = {"newTokens=" + ("off" if "--theme-new-tokens off" in a else "on"): ("newTokens=off" if "--theme-new-tokens off" in a else "newTokens=on") in ov,
            "fluent=" + ("off" if "--fluent-override off" in a else "on"): ("fluent=off" if "--fluent-override off" in a else "fluent=on") in ov,
            "inactiveOpacity=" + ("1" if "--chrome-inactive-opacity 1" in a else "default"): ("inactiveOpacity=1" if "--chrome-inactive-opacity 1" in a else "inactiveOpacity=default") in cd,
            "notify_panel=" + ("0" if "--notify-panel off" in a else "1"): perf.get("notify_panel") == (0 if "--notify-panel off" in a else 1),
            "loc " + ("static" if "--loc static" in a else "바인딩"): (lang.get("staticMode") is True and (lang.get("staticCount") or 0) > 0) if "--loc static" in a else (lang.get("staticMode") in (False, None))}
    need["dim=" + ("brush" if "--chrome-inactive-dim brush" in a else "opacity")] = ("dim=brush" if "--chrome-inactive-dim brush" in a else "dim=opacity") in cd
    need["render_kind 있음"] = bool(perf.get("render_kind"))  # i1023b 부터 기록(odev-3)
    if "--notify-panel empty" in a:  # Dock 틀만 있고 내용 없음
        need["notify_content=0"] = perf.get("notify_content") == 0
    if "--notify-layout legacy" in a:
        need["notify_layout_legacy=1"] = perf.get("notify_layout_legacy") == 1
    if "--fluent-palette always" in a:
        need["fluent_palette=always"] = "fluent_palette=always" in ov
    if "--cell-font" in a:  # cell_font=family|both 이고 replaced=0 이 붙으면 무효
        want, cf = ("family" if "--cell-font family" in a else "both"), str(perf.get("cell_font"))
        need["cell_font=" + want] = cf.startswith(want) and "replaced=0" not in cf
    c["need"] = need
    c["valid"] = all(need.values())
    return c, perf


def one_set(Remote, serial, cond, n, outdir):
    host = {"start": time.strftime("%Y-%m-%dT%H:%M:%S"), "loadavg": os.getloadavg(), "win_cpu_pct": win_cpu()}
    print(cond, n, "start", host["start"], "load", "%.2f %.2f %.2f" % host["loadavg"], "winCPU", host["win_cpu_pct"], flush=True)
    r = Remote()
    r.call("docs.close", all=True)
    docs = [(r.call("docs.open", kind=k) or [{}])[0].get("id") for k in ("grid", "gantt", "form")]
    time.sleep(1.5)
    chk, perf0 = checks(r, cond)
    import pdctl
    for command in ("theme:pd-light", "perf-reset"):
        pdctl.curl(r.info, "api/cmd", json.dumps({"cmd": "cmd.run", "args": {"command": command}}), timeout=60)
    fonts = r.call("settings.fonts") or ["Noto Sans KR", "monospace"]
    prev = (r.call("settings.get") or {}).get("themeId")
    r.call("perf.reset")
    theme, wheel, font = [], [], []
    sizes = {}
    for i in range(12):
        if i == 0:
            sizes["before_theme"] = size_snap(r, docs[0])  # 전환 전후로 패널·그리드 폭이 바뀌는지(odev-3 2b) — 첫 회만, 시간 측정 밖
        res = r.call("theme.next")
        cur = str(res).split(":", 1)[-1].strip() if res else None
        theme.append({"ok": r.calls[-1]["ok"], "elapsed_ms": r.calls[-1]["elapsed_ms"], "from": prev, "to": cur, "kind": "%s→%s" % (VARIANT.get(prev, "?"), VARIANT.get(cur, "?"))})
        prev = cur
        if i == 0:
            sizes["after_theme"] = size_snap(r, docs[0])
        r.call("input.ctrlWheel", delta=1 if i % 2 == 0 else -1, doc=docs[0])
        wheel.append({"ok": r.calls[-1]["ok"], "elapsed_ms": r.calls[-1]["elapsed_ms"]})
        if i == 0:
            sizes["before_font"] = size_snap(r, docs[0])
        r.call("settings.set", fontName=fonts[i % len(fonts)])
        font.append({"ok": r.calls[-1]["ok"], "elapsed_ms": r.calls[-1]["elapsed_ms"]})
        if i == 0:
            sizes["after_font"] = size_snap(r, docs[0])
    # 전환 1회당 패널·그리드 측정·배치 계측(odev-3 2b, 75c9cc11·a50e282b 판부터): 직전 perf.reset(조작 기록만 지움) → 실행 → perf.get·perf.ops
    OPK = ("notify.measure", "notify.arrange", "grid.measure", "grid.arrange")
    MK = ("notify_measure_sizechg", "notify_arrange_sizechg", "grid_measure_sizechg", "grid_arrange_sizechg",
          "notify_bounds_reports", "notify_header_flips")  # 뒤 둘은 odev-3 추가(994f2963·4c993ad6 판부터)
    per_tr = {}
    for tag, cmd, args in (("theme", "theme.next", {}), ("font", "settings.set", {"fontName": fonts[1 % len(fonts)]})):
        before = r.call("perf.get") or {}
        r.call("perf.reset")
        r.call(cmd, **args)
        ms = r.calls[-1]["elapsed_ms"]
        time.sleep(0.5)
        after = r.call("perf.get") or {}
        ops = {o.get("name"): o for o in (r.call("perf.ops") or []) if isinstance(o, dict)}
        per_tr[tag] = {"elapsed_ms": ms, "ops": {k: ops.get(k) for k in OPK},
                       "sizechg_delta": {k: (after.get(k) - before.get(k)) if isinstance(after.get(k), (int, float)) and isinstance(before.get(k), (int, float)) else [before.get(k), after.get(k)] for k in MK},
                       "lastsize": {k: after.get(k) for k in after if k.endswith("_lastsize")},  # grid_*_lastsize 는 소수 셋째 자리(1px 미만 흔들림)
                       "notify_bounds_last": after.get("notify_bounds_last"), "notify_header_tworows": after.get("notify_header_tworows")}
    perf_end = r.call("perf.get") or {}
    late = {"notify_view_instances": perf_end.get("notify_view_instances"), "notify_view_attached": perf_end.get("notify_view_attached")}  # 늦게 생성되는지 확인용
    ops = r.call("perf.ops")
    census = []
    for tag, tid in (("S1", "pd-ocean"), ("X1", "pd-dark")):  # visible 열만 비교에 쓴다(odev-3)
        c = r.call("theme.census", themeId=tid) or {}
        s = c.get("sync") or {}
        census.append({"step": tag, "theme": tid, "sync_ms": c.get("sync_ms"), "settled_ms": c.get("settled_ms"),
                       "measure_invalid_visible": s.get("measure_invalid_visible"), "arrange_invalid_visible": s.get("arrange_invalid_visible"),
                       "who_measure_visible": s.get("who_measure_visible")})  # 전체 보관(A_empty 에서 DataGridCellsPresenter 유무 확인)
    rep = {"cond": cond, "n": n, "args": CONDS[cond], "host": host, "checks": chk,
           "theme_next": med(theme), "theme_next_by_kind": {k: med([t for t in theme if t["kind"] == k]) for k in sorted({t["kind"] for t in theme})},
           "input_ctrlWheel": med(wheel), "font_family": med(font), "census_visible": census, "sizes": sizes, "notify_late": late, "per_transition": per_tr,
           "raw": {"theme": theme, "wheel": wheel, "font": font}, "perf_get_before": perf0, "perf_ops": ops}
    json.dump(rep, open(os.path.join(outdir, "q4r1_%s_%d.json" % (cond, n)), "w", encoding="utf-8"), ensure_ascii=False, indent=1)
    return rep


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--apk", required=True)
    p.add_argument("--serial", default="R3CW70202FP")
    p.add_argument("--sets", type=int, default=3)
    p.add_argument("--order", default="A,R1")
    p.add_argument("--outdir", required=True)
    a = p.parse_args()
    os.makedirs(a.outdir, exist_ok=True)
    state, summary = {}, []
    try:
        info = ra.prepare(a.serial, a.apk, state)
        if info:
            from retest_remote import Remote
            seen = {}  # 순서에 같은 조건(A 끼워 넣기)이 여러 번 나와도 파일이 겹치지 않게 조건별 회차를 센다
            for _ in range(a.sets):
                for cond in a.order.split(","):
                    seen[cond] = n = seen.get(cond, 0) + 1
                    if not c2.launch(a.serial, CONDS[cond], info):
                        print("기동 실패", cond, n, flush=True)
                        continue
                    time.sleep(3)
                    rep = one_set(Remote, a.serial, cond, n, a.outdir)
                    summary.append({k: rep[k] for k in ("cond", "n", "host", "checks", "theme_next", "theme_next_by_kind", "input_ctrlWheel", "font_family", "census_visible", "sizes", "notify_late", "per_transition")})
                    print(cond, n, "theme", rep["theme_next"].get("median"), "wheel", rep["input_ctrlWheel"].get("median"), "font", rep["font_family"].get("median"),
                          "valid=%s" % rep["checks"]["valid"], flush=True)
    finally:
        ra.cleanup(a.serial, state)
        ra.adb(a.serial, "shell", "wm", "size", "reset")
        json.dump({"finished": datetime.datetime.now().isoformat(timespec="seconds"), "apk": a.apk, "summary": summary},
                  open(os.path.join(a.outdir, "q4r1_summary.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=1)


if __name__ == "__main__":
    main()
