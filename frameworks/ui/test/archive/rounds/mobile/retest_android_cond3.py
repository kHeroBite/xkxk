# Fold5 실기기에서 odev-3 측정 지시문(조건 A·A_nogrid·C·N_empty·N_legacy)대로 테마 전환 단계별 census 와 Q4 세트를 3회씩 재는 스크립트
"""
사용: python3 tools/retest_android_cond3.py --apk <진단 APK> [--conds A,A_nogrid,C,N_empty,N_legacy] [--runs 3] [--outdir logs/otest_m1/i0315]
기동: files/pdargs.txt(--fresh + 조건 인자) → 기동 → 적용 확인(notify_view 등, 기대와 다르면 해당 조건 무효 표시)
각 실행: docs.close all → 문서 열기 → cmd.run command=theme:pd-light → command=perf-reset
         → 단계 S0(pd-light-prev) S1(ocean) S2(forest) S3(light) X1(dark) S4(contrast) S5(ember) X2(light)마다 theme.census themeId=<id> + perf.get
           (census2_{조건}_{n}_{단계}.json 저장)
         → perf-reset 후 theme.next·fontName 각 12회(perf2_{조건}_{n}.json)
원복(finally): forward 해제·앱 제거·토큰 파일 삭제·stay_on 원값. 종료 코드 0=완료, 2=준비 오류.
"""
import argparse
import datetime
import json
import os
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import retest_android as ra  # noqa: E402
import retest_android_cond2 as c2  # noqa: E402  launch·summarize 재사용

CONDS = {
    "A": ("--fresh", "grid,gantt,form", {"notify_view": "new", "notify_panel": 1, "notify_content": 1, "notify_layout_legacy": 0}),
    "A_nogrid": ("--fresh", "gantt,form", {"notify_view": "new", "notify_panel": 1, "notify_content": 1, "notify_layout_legacy": 0}),
    "C": ("--fresh --notify-panel off", "grid,gantt,form", {"notify_view": "none", "notify_panel": 0}),
    "N_empty": ("--fresh --notify-panel empty", "grid,gantt,form", {"notify_view": "none", "notify_panel": 1, "notify_content": 0}),
    "N_legacy": ("--fresh --notify-layout legacy", "grid,gantt,form", {"notify_view": "legacy", "notify_layout_legacy": 1}),
    "C_nogrid": ("--fresh --notify-panel off", "gantt,form", {"notify_view": "none", "notify_panel": 0}),  # 알림 패널 몫은 A_nogrid↔C_nogrid 로 가른다(odev-1·odev-3)
    "A_ss": ("--fresh --grid-open staticstroke", "grid,gantt,form", {"grid_open_mode": "staticstroke"}),  # i0352 판 미포함(다음 판)
    "A_lc": ("--fresh --grid-open litecell", "grid,gantt,form", {"grid_open_mode": "litecell"}),  # odev-3 10-29: 다음 판 조건 추가
    "F_on": ("--fresh --fluent-palette on", "grid,gantt,form", {}),  # M3(odev-7c 10-29): 팔레트 교체 몫 분리 — theme_overrides 로 적용 확인
    "F_off": ("--fresh --fluent-palette off", "grid,gantt,form", {}),
}
# M3 변형 구분(BuiltinThemes ThemeBase 기준, otest-1 10-29 전달)
VARIANT = {"pd-light": "L", "pd-light-prev": "L", "pd-ocean": "L", "pd-forest": "L", "pd-dark": "D", "pd-contrast": "D", "pd-ember": "D"}
# 실제 적용 확인 지표(odev-3 10-29): 스위치를 줬는데 값이 0 이면 그 측정은 무효
APPLIED_METRIC = {"staticstroke": ("grid_stable_strokes", lambda v: v == 1), "litecell": ("grid_lite_cells", lambda v: isinstance(v, (int, float)) and v > 0)}
STEPS = [("S0", "pd-light-prev"), ("S1", "pd-ocean"), ("S2", "pd-forest"), ("S3", "pd-light"), ("X1", "pd-dark"), ("S4", "pd-contrast"), ("S5", "pd-ember"), ("X2", "pd-light")]


def top5(wm):
    if isinstance(wm, list):
        return wm[:5]
    if isinstance(wm, dict):
        return sorted(wm.items(), key=lambda kv: -(kv[1] if isinstance(kv[1], (int, float)) else 0))[:5]
    return wm


def rect_measure(sync):
    for t in (sync or {}).get("by_type", []):
        if t.get("type") == "Rectangle":
            return t.get("measure")
    return None


def one_run(Remote, serial, cond, n, outdir):
    args, docs, expect = CONDS[cond]
    host = {"start": time.strftime("%Y-%m-%dT%H:%M:%S"), "loadavg": os.getloadavg()}  # 리드 10-29: 조건 시작 시각의 호스트 load 기록
    print(cond, n, "start", host["start"], "load", "%.2f %.2f %.2f" % host["loadavg"], flush=True)
    r = Remote()
    chk = {}
    perf0 = r.call("perf.get") or {}
    for k, v in expect.items():
        chk[k] = {"got": perf0.get(k), "expect": v, "ok": perf0.get(k) == v}
    if cond.startswith("F_"):  # --fluent-palette 적용 확인: perf.get theme_overrides 에 fluent_palette=on|off
        want = "fluent_palette=" + cond[2:]
        chk["theme_overrides"] = {"got": perf0.get("theme_overrides"), "ok": want in str(perf0.get("theme_overrides"))}
    chk["docs_at_start"] = len(r.call("docs.list") or [])
    if cond.startswith("C"):  # C 계열은 알림 패널이 실제로 없어야 한다: panel.bounds NotificationPane=null 과 생성 추적(notifyCreated·[notify] view created 로그)
        pb = r.call("panel.bounds") or {}
        chk["panel_bounds_NotificationPane_null"] = {"got": pb.get("NotificationPane"), "ok": pb.get("NotificationPane") is None}
        jp = str(r.call("panel.joinProbe", x=0, y=0))
        chk["notifyCreated_probe"] = jp[jp.find("notifyCreated"):jp.find("notifyCreated") + 24] if "notifyCreated" in jp else None
        rc_l, log_l = ra.adb(serial, "logcat", "-d", "-v", "brief")
        chk["notify_created_logs"] = [ln[-200:] for ln in log_l.splitlines() if "[notify] view created" in ln][:4]
    chk["valid"] = all(x["ok"] for x in chk.values() if isinstance(x, dict))
    r.call("docs.close", all=True)
    for k in docs.split(","):
        r.call("docs.open", kind=k)
    if "grid_open_mode" in expect:  # grid_open_mode 는 그리드를 연 뒤에야 perf 에 나타나므로 문서를 연 뒤 다시 읽어 적용 확인한다
        time.sleep(1.5)  # grid_lite_cells 는 셀이 실제로 그려진 뒤(문서 열기 + 2프레임 이상) 늘어난다(odev-3 10-29)
        perf1 = r.call("perf.get") or {}
        chk["grid_open_mode"] = {"got": perf1.get("grid_open_mode"), "expect": expect["grid_open_mode"], "ok": perf1.get("grid_open_mode") == expect["grid_open_mode"]}
        mk, mok = APPLIED_METRIC.get(expect["grid_open_mode"], (None, None))
        if mk:
            chk[mk] = {"got": perf1.get(mk), "ok": bool(mok(perf1.get(mk)))}
        chk["valid"] = all(x["ok"] for x in chk.values() if isinstance(x, dict))
    pc = None
    import pdctl  # cmd.run 의 인자 이름이 command 로 바뀌었지만(별칭 cmd) Remote.call 과 충돌하지 않게 직접 요청
    for command in ("theme:pd-light", "perf-reset"):
        pc = pdctl.curl(r.info, "api/cmd", json.dumps({"cmd": "cmd.run", "args": {"command": command}}), timeout=60)
    steps = []
    for tag, tid in STEPS:
        c = r.call("theme.census", themeId=tid) or {}
        perf = r.call("perf.get") or {}
        ops = {o["name"]: o for o in (r.call("perf.ops") or [])}
        sync = c.get("sync") or {}
        row = {"step": tag, "theme": tid, "variant": (c.get("variant_before"), c.get("variant_after")), "sync_ms": c.get("sync_ms"), "settled_ms": c.get("settled_ms"),
               "measure_invalid": sync.get("measure_invalid"), "arrange_invalid": sync.get("arrange_invalid"), "rect_measure": rect_measure(sync),
               "who_measure_top5": top5(sync.get("who_measure")),
               "measure_invalid_visible": sync.get("measure_invalid_visible"), "arrange_invalid_visible": sync.get("arrange_invalid_visible"),
               "who_measure_visible_top5": top5(sync.get("who_measure_visible")), "notifications_last": perf.get("theme.apply.notifications_last"),
               "batched": {k: ops.get("theme.apply.batched", {}).get(k) for k in ("count", "frame_p50_ms", "frame_max_ms")}}
        steps.append(row)
        json.dump({"cond": cond, "n": n, "step": tag, "census": c, "perf": perf}, open(os.path.join(outdir, "census2_%s_%d_%s.json" % (cond, n, tag)), "w", encoding="utf-8"), ensure_ascii=False)
    r.call("perf.reset")
    theme, font = [], []
    prev = ((r.call("settings.get") or {}).get("themeId"))  # 회차마다 전·후 테마 id 를 기록해 변형(L→L·D→D·L↔D)을 분류한다(M3)
    for _ in range(12):
        res = r.call("theme.next")
        ok, ms = r.calls[-1]["ok"], r.calls[-1]["elapsed_ms"]
        cur = str(res).split(":", 1)[-1].strip() if res else None
        theme.append({"ok": ok, "elapsed_ms": ms, "from": prev, "to": cur, "kind": "%s→%s" % (VARIANT.get(prev, "?"), VARIANT.get(cur, "?"))})
        prev = cur
    fonts = r.call("settings.fonts") or ["Noto Sans KR", "monospace"]  # Android 글꼴 목록에 없는 이름(Noto Mono)을 쓰지 않는다
    for i in range(12):
        r.call("settings.set", fontName=fonts[i % len(fonts)])
        font.append({"ok": r.calls[-1]["ok"], "elapsed_ms": r.calls[-1]["elapsed_ms"]})
    rep = {"cond": cond, "n": n, "args": args, "host": host, "checks": chk, "steps": steps, "theme_next": c2.summarize(theme),
           "theme_next_by_kind": {k: c2.summarize([t for t in theme if t["kind"] == k]) for k in sorted({t["kind"] for t in theme})}, "font_family": c2.summarize(font)}
    json.dump(rep, open(os.path.join(outdir, "perf2_%s_%d.json" % (cond, n)), "w", encoding="utf-8"), ensure_ascii=False, indent=1)
    return rep


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--apk", required=True)
    p.add_argument("--serial", default="R3CW70202FP")
    p.add_argument("--conds", default="A,A_nogrid,C,N_empty,N_legacy")
    p.add_argument("--runs", type=int, default=3)
    p.add_argument("--outdir", default=os.path.join(os.path.dirname(ra.DEFAULT_OUT), "i0315"))
    a = p.parse_args()
    os.makedirs(a.outdir, exist_ok=True)
    state, summary, code = {}, [], 2
    try:
        info = ra.prepare(a.serial, a.apk, state)
        if info:
            from retest_remote import Remote
            for cond in a.conds.split(","):
                for n in range(1, a.runs + 1):
                    if not c2.launch(a.serial, CONDS[cond][0], info):
                        print("기동 실패", cond, n)
                        continue
                    time.sleep(2)
                    rep = one_run(Remote, a.serial, cond, n, a.outdir)
                    summary.append({k: rep[k] for k in ("cond", "n", "args", "host", "checks", "theme_next", "font_family")})
                    print(cond, n, rep["theme_next"]["median"], rep["font_family"]["median"], "valid=%s" % rep["checks"]["valid"], flush=True)
            code = 0
    finally:
        ra.cleanup(a.serial, state)
        ra.adb(a.serial, "shell", "wm", "size", "reset")
        json.dump({"finished": datetime.datetime.now().isoformat(timespec="seconds"), "apk": a.apk, "summary": summary}, open(os.path.join(a.outdir, "cond2_summary.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=1)
    return code


if __name__ == "__main__":
    sys.exit(main())
