#!/usr/bin/env python3
# pcaway_report 의 항목별 원자료 대응표 — 기존 측정 도구가 남긴 값을 판정기 측정 이름으로 그대로 옮긴다. 원자료에 없는 이름은 내지 않아 미측정(FAIL)으로 남는다
"""
규칙
  fn(raws, ctx) 는 {측정 이름: (값, 통과)} 를 돌려준다. 값은 원자료 그대로이며 기준은 원자료 도구·계획서의 기준을 옮긴 것이다.
  원자료 키가 없으면 그 이름을 결과에 넣지 않는다. Result.write 가 {"unmeasured": true} 실패로 채운다.
  MAP[item]["unsourced"] 는 이 저장소의 도구 원자료에 값이 없는 측정 이름이다. otest 가 직접 재서 --measure-file 로 넣거나 없음(FAIL)으로 남는다.
"""
import importlib.util
import json
import os
import re

MS = "test/desktop/measure/"
ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", ".."))
IMMEDIATE = ("창 배율", "활성 전환", "배치 ", "전체 최소화", "제목줄 ", "원격 왕복")  # a9 §4.1 즉시(≤ 100ms) 행. 제목줄 새로고침은 perf_ui 묶음에 없다
OPEN = ("SDI 분리", "레이아웃 ")  # a9 §4.1 창 열기(≤ 500ms) 행
WIDE = ("글자 크기", "Ctrl+휠", "Ctrl+0", "테마 전환", "폰트 변경")  # a9 §4.1 20창 반영(≤ 300ms) 행


def doc_text(rel):
    try:
        with open(os.path.join(ROOT, rel), encoding="utf-8") as f:
            return f.read()
    except OSError:
        return ""


def checks_of(raw):
    """측정 도구의 항목 목록(items·checks·rows)을 찾는다. 모양이 다르면 빈 목록이다."""
    for key in ("items", "checks", "rows"):
        if isinstance((raw or {}).get(key), list):
            return raw[key]
    return []


def passed(row):
    return row.get("pass", row.get("ok", row.get("verdict") == "PASS")) is True


def all_pass(rows):
    return (rows, True) if rows and all(passed(r) for r in rows) else (rows, False)


# 데스크톱 --------------------------------------------------------------------
def a01(raws, ctx):
    raw = raws.get("main")
    if not raw or not raw.get("rows"):
        return {}
    rows = raw["rows"]
    pick = lambda text: [r for r in rows if text in r["case"]]
    out = {"maximize_each_monitor": all_pass(pick("in_")), "straddle_60_40": all_pass(pick("60_40")), "straddle_40_60": all_pass(pick("40_60")), "straddle_50_50": all_pass(pick("50_50"))}
    out["restore_bounds"] = ([{"case": r["case"], "delta": r["restoreDelta"], "zoomed": r["restoreZoomed"]} for r in rows], all(max(r["restoreDelta"]) <= 1 and not r["restoreZoomed"] for r in rows))
    out["dpi_match"] = ([{"case": r["case"], "monitor": r["dpiMonitor"], "window": r["dpiWindow"]} for r in rows], all(r["dpiMonitor"] == r["dpiWindow"] for r in rows))
    control = {c["case"]: c.get("monitor") for c in raw.get("control") or []}
    match = [{"case": r["case"], "kind": r["kind"], "app": r["maxMonitor"], "win32": control.get(r["case"])} for r in rows]
    out["win32_control_match"] = (match, bool(control) and all(m["app"] == m["win32"] for m in match))
    return out


def a02(raws, ctx):
    rows = {r.get("case"): r for r in (raws.get("main") or {}).get("rows") or []}
    out = {}
    for name, case in (("mdi_max_doc_to_sdi_monitor", "MAXIMIZED_MDI_TO_SDI"), ("new_doc_sdi_monitor", "NEW_DOC_SDI")):
        if case in rows:
            out[name] = (rows[case], rows[case].get("pass") is True)
    return out


def a07(raws, ctx):
    rows = {r.get("no"): r for r in (raws.get("main") or {}).get("rows") or []}
    return {"hold%d" % no: (rows[no], rows[no].get("verdict") == "PASS") for no in (4, 5, 6, 7) if no in rows}


def a13(raws, ctx):
    out = {}
    normal, reduced = raws.get("normal"), raws.get("reduced")
    if normal and normal.get("steps"):
        moved = [s for s in normal["steps"] if s.get("moved")]
        out["h61_frames_advance"] = ({"moved": len(moved), "animated": sum(1 for s in moved if s.get("animated")), "violations": normal.get("violations")}, bool(moved) and any(s.get("animated") for s in moved))
    if reduced and reduced.get("steps"):
        moved = [s for s in reduced["steps"] if s.get("moved")]
        out["motion_off_no_transition"] = ({"moved": len(moved), "animated": sum(1 for s in moved if s.get("animated")), "violations": reduced.get("violations")},
                                           bool(moved) and not any(s.get("animated") for s in moved) and not reduced.get("violations"))
    return out


def perf_groups(raw, key):
    return (raw or {}).get(key) or []


def group_p95(group):
    values = [o.get("frame_p95_ms") for o in (group.get("ops") or {}).values() if isinstance(o, dict) and o.get("frame_p95_ms") is not None]
    return max(values) if values else None


def b1(raws, ctx):
    raw = raws.get("main")
    if not raw:
        return {}
    out = {}
    for name, key in (("ops_all_measured_1doc", "docs_1"), ("ops_all_measured_20doc", "docs_20")):
        groups = perf_groups(raw, key)
        if groups:
            out[name] = ([{"group": g["group"], "errors": g["errors"], "p95": group_p95(g)} for g in groups], all(g["errors"] == 0 and group_p95(g) is not None for g in groups))
    counts = [o.get("count") for key in ("docs_1", "docs_20") for g in perf_groups(raw, key) for o in (g.get("ops") or {}).values() if isinstance(o, dict)]
    if counts:
        out["repeat_ge_10"] = ({"min_count": min(counts)}, min(counts) >= 10)
    table = doc_text("plan/04_기술스택/04_판정보고서_부록/a9_성능_웹_모바일_4_5_6.x.md")
    section = table.split("## 4.1", 1)[-1].split("\n## ", 1)[0]
    out["doc_a9_table_updated"] = ({"대기_행": section.count("| 대기 |")}, "## 4.1" in table and "| 대기 |" not in section)
    return out


def limit_rows(raw, keys, labels, limit):
    rows = [{"docs": key, "group": g["group"], "p95": group_p95(g)} for key in keys for g in perf_groups(raw, key) if any(t in g["group"] for t in labels)]
    return rows, bool(rows) and all(r["p95"] is not None and r["p95"] <= limit for r in rows)


def b2(raws, ctx):
    raw = raws.get("main")
    if not raw:
        return {}
    both = ("docs_1", "docs_20")
    return {"immediate_le_100ms": limit_rows(raw, both, IMMEDIATE, 100), "open_le_500ms": limit_rows(raw, both, OPEN, 500), "theme_font_20win_le_300ms": limit_rows(raw, ("docs_20",), WIDE, 300)}


def b4(raws, ctx):
    rc = ctx.get("rc") or {}
    out = {}
    if "plan" in rc:
        out["plan_rc0"] = ({"rc": rc["plan"]}, rc["plan"] == 0)
    if "measure" in rc:
        out["measure_rc_recorded"] = ({"rc": rc["measure"], "data": raws.get("main")}, True)
        section = doc_text("plan/07_구축_배포/05_구성프로파일_최소사양.md").split("### 9.1", 1)[-1].split("\n## ", 1)[0]
        out["doc_05_section9_updated"] = ({"rc": rc["measure"]}, re.search(r"\brc\s*=?\s*%d\b" % rc["measure"], section) is not None)
    return out


def b5(raws, ctx):
    raw = raws.get("main")
    if not raw:
        return {}
    out = {}
    restore = (raw.get("long") or {}).get("restore")
    if restore:
        out["restored_profile_measured"] = (restore, restore.get("history_after", 0) > restore.get("history_before", 0))
    timers = (raw.get("idle") or {}).get("timers")
    if timers:
        from ui_req2_8_rules import TIMER_KEYS  # 누적값 spinnerFrames 는 타이머가 아니다. 경로는 pcaway_report 가 앞서 넣는다
        out["timer_zero_after_idle"] = (timers, all(timers.get(k) == 0 for k in TIMER_KEYS))
    return out


# 모바일·웹 --------------------------------------------------------------------
def c1(raws, ctx):
    rows = checks_of(raws.get("main"))
    if not rows:
        return {}
    joined = [r for r in rows if str(r.get("name", "")).startswith("F ")]
    split = [r for r in rows if str(r.get("name", "")).startswith("S ")]
    return {"sdi_join_phone": all_pass(joined), "sdi_split_phone": all_pass(split)}


def c3(raws, ctx):
    raw = raws.get("main")
    if not raw:
        return {}
    cond = raw.get("conditions") or {}
    return {"apk_diag_strings_absent": ({"kind": raw.get("kind"), "verdict": raw.get("verdict"), "conditions": cond}, raw.get("kind") == "apk" and raw.get("verdict") == "PASS" and cond.get("3_command_strings_zero") is True)}


def d4(raws, ctx):
    raw, rc, out = raws.get("main"), ctx.get("rc") or {}, {}
    if "check" in rc:
        out["check_product_web_rc0"] = ({"rc": rc["check"]}, rc["check"] == 0)
    if raw and raw.get("runtime") is not None:
        cond = raw.get("conditions") or {}
        out["product_boot"] = ({"booted": raw["runtime"].get("booted")}, raw["runtime"].get("booted") is True)
        out["diag_absent"] = (cond, all(cond.get(k) is True for k in ("1_diag_file_absent", "2_globals_absent", "3_command_strings_zero")))
    return out


def ui28_row(ac):
    try:
        with open(os.path.join(ROOT, "logs", "ui28", ac + ".json"), encoding="utf-8") as f:
            doc = json.load(f)
    except (OSError, ValueError):
        return None
    return {"ac": ac, "verdict": doc.get("verdict"), "run_id": doc.get("run_id"), "build_sha": (doc.get("build") or {}).get("sha256")}


def d2(raws, ctx):
    out = {}
    for name, ac in (("ac_2_09", "AC-2-09"), ("ac_4_05", "AC-4-05"), ("ac_6_09", "AC-6-09"), ("ac_7_06", "AC-7-06")):
        row = ui28_row(ac)
        if row:
            out[name] = (row, row["verdict"] == "PASS")
    for name, key in (("panel_menu", "panel"), ("sdi_join", "sdi")):
        rows = checks_of(raws.get(key))
        if rows:
            out[name] = ({"total": len(rows), "failed": [r.get("name") for r in rows if not passed(r)]}, all(passed(r) for r in rows))
    return out


def d3(raws, ctx):
    raw, out = raws.get("main"), {}
    if not raw:
        return out
    webkit, firefox = raw.get("webkit") or {}, raw.get("firefox")
    if webkit:
        out["webkit_boot"] = (webkit, webkit.get("booted") is True and not webkit.get("exception"))
    if firefox:
        out["firefox_absent_recorded"] = (firefox, firefox.get("installed") is False)
    return out


# 이월·토폴로지·회귀 -----------------------------------------------------------
def by_name(rows):
    return {r.get("name"): r for r in rows}


def f05(raws, ctx):
    rows = by_name(checks_of(raws.get("main")))
    return {n: (rows[n], rows[n].get("pass") is True) for n in ("hidden_window_not_main", "main_hwnd_correct_with_hidden") if n in rows}


def f08(raws, ctx):
    # 판정기 F08 req 에서 doc_03_records_55pct_rule 이 제거돼 이 대응표가 내보낼 이름이 없다(요구 3개는 retest_pcaway_extra2.py·--measure-file 이 채운다)
    return {}


def g1(raws, ctx):
    rows, result, out = by_name(checks_of(raws.get("selftest"))), raws.get("main"), {}
    # 자가시험의 watchdog_restores_after_main_kill_dry 는 건식(실제 적용 없음)이라 watchdog_restores_after_main_kill 값으로 옮기지 않는다
    for name in ("validate_only_rc0", "no_save_to_database"):
        if name in rows:
            out[name] = (rows[name], rows[name].get("pass") is True)
    if result and "topology_equal_after_restore" in result:
        out["topology_equal_after_restore"] = (result["topology_equal_after_restore"], result["topology_equal_after_restore"] is True)
    return out


def g2(raws, ctx):
    result, out = raws.get("main"), {}
    if not result:
        return out
    out["detach_right_monitor"] = ({"applied": result.get("applied"), "dry_run": result.get("dry_run"), "target": result.get("target"), "apply_rc": result.get("apply_rc")}, result.get("applied") is True and result.get("dry_run") is False)
    if "topology_equal_after_restore" in result:
        equal = result["topology_equal_after_restore"]
        out["topology_equal_after_restore"] = (equal, equal is True)
        out["reattach_ok"] = (result.get("restore"), equal is True and result.get("applied") is True)
    if "windows_changed_count" in result:
        out["user_windows_compared"] = ({"changed": result["windows_changed_count"], "still_different": result.get("user_windows_still_different")}, True)
    if "user_pd_windows_unmoved" in result:
        out["user_pd_windows_unmoved"] = (result.get("user_pd_windows_moved"), result["user_pd_windows_unmoved"] is True)
    return out


def r1(raws, ctx):
    folder = os.path.join(ROOT, "logs", "ui28")
    try:
        names = [n for n in os.listdir(folder) if n.endswith(".json")]
    except OSError:
        return {}
    bad = [n for n in names if (ui28_row(n[:-5]) or {}).get("verdict") != "PASS"]
    out = {"ui28_gate_all_pass": ({"total": len(names), "not_pass": bad}, len(names) >= 59 and not bad)}
    if ctx.get("manifest") is not None:
        out["final_build_all_axes"] = (ctx["manifest"], ctx["manifest"].get("complete") is True)
    return out


REPLAY_JUDGE = os.path.join(ROOT, "test", "mobile", "judge", "replay_target_judge.py")
REPLAY_ROLES = ("android_prod", "android_release")
REPLAY_MIN_SENT, REPLAY_DOCS = 20, 5  # K7 기준(sent 20 이상, 기본 문서 5개 복원 상태)


def replay_judge():
    spec = importlib.util.spec_from_file_location("replay_target_judge", REPLAY_JUDGE)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def k7(raws, ctx):
    """--raw android_prod_open·android_prod_closed·android_release_open·android_release_closed = replay_<mode>.json. rc 는 입력받지 않고 replay_target_judge 의 판정 함수를 원자료에 그대로 적용한다. 원자료가 하나라도 없으면 값을 내지 않는다."""
    keys = ["%s_%s" % (role, mode) for role in REPLAY_ROLES for mode in ("open", "closed")]
    if any(not isinstance(raws.get(k), dict) for k in keys):
        return {}
    judge, by, ok_all = replay_judge(), {}, True
    for role in REPLAY_ROLES:
        opened, closed = raws[role + "_open"], raws[role + "_closed"]
        res = {"open": judge.judge_doc(opened), "closed": judge.judge_doc(closed)}
        rc = judge.verdict(res, REPLAY_MIN_SENT)
        same_apk = bool(opened.get("apk_sha256")) and opened.get("apk_sha256") == closed.get("apk_sha256") and opened.get("apk") == closed.get("apk")
        modes_ok = opened.get("mode") == "open" and closed.get("mode") == "closed"
        docs = [opened.get("docs_opened"), closed.get("docs_opened")]
        ok_all = ok_all and rc == 0 and same_apk and modes_ok and docs == [REPLAY_DOCS, REPLAY_DOCS]
        by[role] = {"path": opened.get("apk"), "sha256": opened.get("apk_sha256"), "replay_target_rc": rc, "replay_sent_open": res["open"]["sent"], "replay_sent_closed": res["closed"]["sent"],
                    "replay_bad": res["open"]["bad"] + res["closed"]["bad"], "replay_fatal": res["open"]["fatal"] + res["closed"]["fatal"], "same_apk": same_apk, "modes_ok": modes_ok, "docs_opened": docs,
                    "bad_rows": res["open"]["bad_rows"] + res["closed"]["bad_rows"]}
    rows = by.values()
    rcs = [e["replay_target_rc"] for e in rows]
    rc = 1 if 1 in rcs else max(rcs)  # 판정기와 같이 위반을 표본 부족보다 앞세운다
    sent_open, sent_closed = min(e["replay_sent_open"] for e in rows), min(e["replay_sent_closed"] for e in rows)
    bad, fatal = sum(e["replay_bad"] for e in rows), sum(e["replay_fatal"] for e in rows)
    return {"replay_target_rc": (rc, rc == 0), "replay_sent_open": (sent_open, sent_open >= REPLAY_MIN_SENT), "replay_sent_closed": (sent_closed, sent_closed >= REPLAY_MIN_SENT),
            "replay_bad": (bad, bad == 0), "replay_fatal": (fatal, fatal == 0), "replay_by_build": (by, ok_all)}


MAP = {
    "A01": {"script": MS + "retest_maximize_monitor.py", "fn": a01, "unsourced": []},
    "A02": {"script": MS + "retest_maximize_spawn.py", "fn": a02, "unsourced": []},
    "A07": {"script": MS + "retest_window_menu.py", "fn": a07, "unsourced": []},
    "A13": {"script": MS + "retest_tour_motion.py", "fn": a13, "unsourced": [], "extra2": True},
    "B1": {"script": MS + "perf_ui.py", "fn": b1, "unsourced": [], "extra2": True},
    "B2": {"script": MS + "perf_ui.py", "fn": b2, "unsourced": [], "extra2": True},
    "B4": {"script": "test/min_spec/measure_stack.py", "fn": b4, "unsourced": []},
    "B5": {"script": MS + "retest_busy.py", "fn": b5, "unsourced": [], "extra2": True},
    "C1": {"script": "test/mobile/measure/retest_sdi_join_android.py", "fn": c1, "unsourced": ["cover_state_closed", "tool_error_absent"]},
    "C3": {"script": "tools/check_product_web.py", "fn": c3, "unsourced": ["apk_channel_prod"]},
    "D2": {"script": "test/web/measure/ui28_web.py", "fn": d2, "unsourced": []},
    "D3": {"script": None, "fn": d3, "unsourced": ["edge_excluded_recorded"]},
    "D4": {"script": "tools/check_product_web.py", "fn": d4, "unsourced": []},
    "F05": {"script": MS + "win_owner_z.ps1", "fn": f05, "unsourced": []},
    "F08": {"script": None, "fn": f08, "unsourced": ["user_report_listed"], "extra2": True},
    "G1": {"script": MS + "display_topology.ps1", "fn": g1, "unsourced": ["watchdog_restores_after_main_kill"]},
    "G2": {"script": MS + "display_topology.ps1", "fn": g2, "unsourced": ["main_moved_visible", "sdi_moved_visible", "visible_windows_unmoved", "debounce_one_eval"]},
    "R1": {"script": None, "fn": r1, "unsourced": []},
    "K7": {"script": "test/mobile/measure/retest_android_pcaway_replay.py", "fn": k7, "unsourced": []},
}
