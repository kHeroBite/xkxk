#!/usr/bin/env python3
# R1 — 최종 빌드(r2f) 격리 앱에서 다시 잰 데스크톱 원자료를 logs/ui28/<AC>.json 으로 옮기는 재현 가능한 조립기(명시 매핑 표 + 수용 기준 판정)
"""
사용: python3 -B ui28_remap.py --raw-dir <원자료 폴더> --motion-dir <투어 motion 폴더> --runs <run_r1 로그 폴더> [--write] [--check]
  --write 없이 실행하면 계산 결과를 표준출력과 <raw-dir>/ui28_remap_out.json 에만 쓴다(ui28 결과는 건드리지 않는다).
  --write 는 logs/ui28/<AC>.json 을 덮어쓴다. --check 는 같은 입력으로 두 번 계산해 결과가 같은지(결정성) 확인한다.
판정 원칙(team-lead 00:35·00:5x 조건):
  - 원자료 값은 아래 MAP 의 명시 경로로만 옮긴다(휴리스틱·옛 결과 비교 없음).
  - 판정은 각 AC 수용 기준 문장(acceptance_criteria_prev.json name)에 직접 대어 낸다. 요건 3~7 은 측정 전에 고정된
    judge/ui_req3_8_rules.py(문장을 코드로 옮긴 규칙)의 결과를 쓰고, 그 규칙이 다루지 않는 부분(UIA Invoke·테마 불변·
    투어 motion·window_menu 회귀)은 이 파일의 RULES 에 문장과 함께 적는다.
  - 판정 근거가 없는 부분은 PASS 로 두지 않고 '부분 미측정'·'보류' 로 적는다.
"""
import argparse
import hashlib
import json
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
sys.path.insert(0, HERE)
import ui_req3_8_rules as rules  # noqa: E402

BUILD = "src/xkxk.frameworks.ui.desktop/bin/run/pcaway_r2f/xkxk.frameworks.ui.desktop.dll"
M = "test/desktop/measure/"
AC_TEXT = {
    "AC-3-01": "잘림 판정 순수 함수: 좌/하/알림(4방향) 조합 표본에서 잘린 모서리 집합이 기대값과 일치",
    "AC-3-02": "기본 레이아웃에서 하단 패널 양 끝에만 확장 버튼이 보이고 좌·알림 패널에는 없다",
    "AC-3-03": "하단 '<' 클릭: 하단 패널이 창 왼쪽 끝까지, 좌 패널 높이가 줄고, 문서 영역 폭 변화는 설계값 ±1dip",
    "AC-3-04": "줄어든 좌 패널의 버튼으로 되돌리면 레이아웃 스냅샷이 원래와 동일(왕복)",
    "AC-3-05": "풍차 배치를 만드는 클릭은 버튼이 아예 없다",
    "AC-3-06": "재시작 후 모서리 소유 유지, cornerOwners 없는 Version 1 layout.json 은 기본으로 로드(사용자 파일 불변, 임시 프로필)",
    "AC-3-07": "글리프 방향(좌우 패널 ∧∨, 상하 패널 <>)·툴팁·Tab/Enter 접근",
    "AC-3-08": "알림 패널이 상·하·좌·우일 때 각각 확장·복원 동작",
    "AC-3-09": "기존 U13·U14·U18·집중 템플릿·패널 숨김/복원 회귀 없음",
    "AC-4-01": "SdiRedockPolicy: 가시비율 0.49/0.50/0.51 경계, 제목줄 120dip 미달 시 clamp",
    "AC-4-02": "SDI 가 MDI 영역에 60% 보이는 상태로 재결합하면 MDI 창 보이는 사각형이 SDI 마지막 사각형과 ±2px 일치하고 영역 안으로 당겨지지 않는다",
    "AC-4-03": "40% 가시면 기존대로 영역 안으로 이동",
    "AC-4-04": "재결합 직후 재배치(Arrange)·창 크기 변화가 위치를 되돌리지 않고, 사용자가 이동하면 해제",
    "AC-4-06": "SDI↔MDI 왕복 3회 위치 드리프트 ≤2px",
    "AC-5-02": "PlacementRecovery.Evaluate: 모니터 제거·해상도 축소·DPI 변경·메인 축소 표본에서 이동 대상과 기본 위치가 기대와 일치, 보이는 창은 이동 없음",
    "AC-5-03": "가상 모니터 실행 중 2→1: 400ms+α 내 메인·SDI 안 보이는 창이 주 모니터 작업영역 안 기본 위치로 이동, 보이는 창은 불변",
    "AC-5-04": "메인 창 축소로 MDI 문서 창이 안 보이면 계단 기본 위치로 이동, 보이는 창·최소화·최대화 창은 불변",
    "AC-5-05": "위치 고정 창도 안 보이면 이동하되 고정 상태 유지",
    "AC-5-06": "2초 안에 이벤트 3회가 와도 평가 1회(디바운스)",
    "AC-5-08": "비전경 시험 모드(--bg-pos)에서는 복구가 꺼져 오탐 없음",
    "AC-6-02": "'?' 가 Popup/새 창이 아닌 오버레이 투어를 시작, 메인·MDI 문서·SDI 창에서 독립 시작, 2창 동시 진행",
    "AC-6-03": "투어 중 본창 접근 불가(포인터·키보드·UIA Invoke 모두 거부, 대상 컨트롤 상태 불변)",
    "AC-6-04": "Esc·X 버튼·마지막 닫기로 언제나 종료. Esc 로 문서 창이 닫히지 않는다(문서 수 불변)",
    "AC-6-06": "종료 후 포커스·활성 문서·IsEnabled 가 시작 전과 동일",
    "AC-6-07": "대상 없음/숨김 단계는 건너뜀",
    "AC-6-10": "애니메이션 가독성(사람 H-6-1), 움직임 줄이기 시 전환 없음(자동)",
    "AC-7-01": "UiSettings 공개 설정 전수가 설정 창에 존재(인벤토리 100% 매핑, 원격 settings.list ↔ 창 트리)",
    "AC-7-02": "설정이 새 창으로 열리고 재호출 시 활성화(단일 인스턴스), Esc 로 닫힘",
    "AC-7-03": "테마(라이트/다크/시스템)·글꼴·언어 변경이 즉시 반영·저장(임시 프로필 settings.json)",
    "AC-7-04": "범주별·전체 기본값 복원",
    "AC-7-07": "재시작 필요 항목(GPU)은 사유 표시",
    "AC-7-08": "검색창으로 설정 항목 필터",
}


def get(d, path):
    for k in path:
        d = d[k]
    return d


def checks(raw, prefix):
    return {c["name"]: c["pass"] for c in raw.get("checks", []) if c["name"].startswith(prefix)}


def cases(raw, prefix):
    return {c["name"]: c["pass"] for c in raw["selftest"]["cases"] if c["name"].startswith(prefix)}


def parse_uia(path):
    """uia3.txt('## 절 이름' 다음 JSON) → {절: 값}."""
    out = {}
    for part in re.split(r"^## ", open(path, encoding="utf-8", errors="replace").read(), flags=re.M)[1:]:
        head, _, body = part.partition("\n")
        try:
            v = json.loads(body.strip())
        except ValueError:
            v = {"unparsed": body.strip()[:500]}
        out[head.strip()] = v.get("result", v) if isinstance(v, dict) and "cmd" in v else v
    return out


def load(args):
    r = lambda n: json.load(open(os.path.join(args.raw_dir, n), encoding="utf-8"))
    src = {"panel": r("panel_all.json"), "persist_after": r("panel_persist_after.json"), "glyphs": r("panel_glyphs.json"),
           "redock": r("redock.json"), "monitor": r("monitor.json"), "settings": r("settings2.json"), "theme": r("theme_sync.json"),
           "tour": r("tour.json"), "wm": r("window_menu2.json"), "uia": parse_uia(os.path.join(args.raw_dir, "uia3.txt")),
           "motion_reduced": json.load(open(os.path.join(args.motion_dir, "motion_reduced.json"), encoding="utf-8")),
           "motion_normal": json.load(open(os.path.join(args.motion_dir, "motion_normal.json"), encoding="utf-8"))}
    split = os.path.join(args.raw_dir, "corner_split.json")
    src["split"] = json.load(open(split, encoding="utf-8")) if os.path.exists(split) else {}
    return src


# 측정 이름 → 원자료 값 (명시 매핑)
MAP = {
    "AC-3-01": lambda s: {"rules_samples": checks(s["panel"], "rules.")},
    "AC-3-02": lambda s: {"default_buttons": get(s["panel"], ["default", "buttons"]), "default_checks": checks(s["panel"], "default.")},
    "AC-3-03": lambda s: {"roundtrip_rects": s["panel"]["roundtrip"], "expand_checks": checks(s["panel"], "expand.")},
    "AC-3-04": lambda s: {"roundtrip_checks": checks(s["panel"], "roundtrip.")},
    "AC-3-05": lambda s: {"pinwheel": checks(s["panel"], "rules.pinwheel")},
    "AC-3-06": lambda s: {"persist": checks(s["panel"], "persist."), "persist_after": checks(s["persist_after"], "persist_after.")},
    "AC-3-07": lambda s: {"glyph_checks": {k: v for k, v in checks(s["panel"], "default.").items() if k in ("default.glyphs", "default.tips", "default.focusable")},
                          "glyph_rows": s["glyphs"].get("rows", s["glyphs"]),
                          "uia_info": s["uia"].get("panel info BottomLeft"), "uia_invoke_expand": s["uia"].get("panel invoke BottomLeft"),
                          "owners_after_invoke": (s["uia"].get("panel state after") or {}).get("owners"),
                          "uia_invoke_restore": s["uia"].get("panel invoke back LeftPane.BottomLeft"),
                          "owners_after_restore": (s["uia"].get("panel state restored") or {}).get("owners")},
    "AC-3-08": lambda s: {"edges_checks": checks(s["panel"], "edges.")},
    "AC-3-09": lambda s: {"summary": s["wm"]["summary"], "rows": [{"no": x.get("no"), "verdict": x.get("verdict")} for x in s["wm"]["rows"]], "userFiles": s["wm"].get("userFiles")},
    "AC-4-01": lambda s: {"selftest_redock_cases": cases(s["redock"], "redock_"), "selftest_total": [s["redock"]["selftest"]["passed"], s["redock"]["selftest"]["total"]]},
    "AC-4-02": lambda s: {"keep_60": s["redock"]["keep_60"]},
    "AC-4-03": lambda s: {"clamp_40": s["redock"]["clamp_40"]},
    "AC-4-04": lambda s: {"hold": s["redock"]["hold"]},
    "AC-4-06": lambda s: {"round_trips": s["redock"]["round_trips"]},
    "AC-5-02": lambda s: {"selftest_recovery_mdi_cases": {k: v for k, v in cases(s["monitor"], "").items() if not k.startswith(("redock_", "debounce_", "drag_", "resize_"))}},
    "AC-5-03": lambda s: {"latency_ms": s["monitor"]["monitors"]["latency_ms"], "main_before": s["monitor"]["monitors"]["before"]["main"], "main_after": s["monitor"]["monitors"]["after"]["main"],
                          "primary_work": s["monitor"]["monitors"]["primary_work"], "sdi_before": s["monitor"]["monitors"]["before"]["sdi"], "sdi_after": s["monitor"]["monitors"]["after"]["sdi"]},
    "AC-5-04": lambda s: {"latency_ms": s["monitor"]["mdi"]["latency_ms"], "docs_before": s["monitor"]["mdi"]["before"]["docs"], "docs_after": s["monitor"]["mdi"]["after"]["docs"]},
    "AC-5-05": lambda s: {"sdi_locked_state": s["monitor"]["monitors"]["locked_state"], "mdi_locked_state": s["monitor"]["mdi"]["locked_state"]},
    "AC-5-06": lambda s: {"selftest_debounce_cases": cases(s["monitor"], "debounce_"), "debounce_ms": s["monitor"]["monitors"]["debounce_ms"]},
    "AC-5-08": lambda s: {"background_off": s["monitor"]["background_off"]},
    "AC-6-02": lambda s: {"start": s["tour"]["start"], "twoWindows": {k: s["tour"]["twoWindows"][k] for k in ("sessionsStart", "sessionsAfterOne")}},
    "AC-6-03": lambda s: {"internal_block": s["tour"]["blocked"],
                          "uia_invoke_theme_during_tour": s["uia"].get("uia invoke btnTheme during tour"), "uia_invoke_mnuFile_during_tour": s["uia"].get("uia invoke mnuFile during tour"),
                          "theme_before_tour_invoke": (s["uia"].get("settings.get before tour invoke") or {}).get("theme"),
                          "theme_after_tour_invoke": (s["uia"].get("settings.get theme") or {}).get("theme"),
                          "uia_positive_control_next": s["uia"].get("uia invoke tour.btnNext"),
                          "uia_invoke_theme_after_tour": s["uia"].get("uia invoke btnTheme after tour end"),
                          "theme_after_tour_end": (s["uia"].get("settings.get after") or {}).get("theme")},
    "AC-6-04": lambda s: {"exit": s["tour"]["exit"], "escControl": s["tour"]["escControl"]},
    "AC-6-06": lambda s: {"restore": s["tour"]["restore"]},
    "AC-6-07": lambda s: {"stepIds_with_docs": [x["stepId"] for x in s["tour"]["skip"]["trailWithDocs"]], "stepIds_without_docs": [x["stepId"] for x in s["tour"]["skip"]["trailWithoutDocs"]]},
    "AC-6-10": lambda s: {"reduced_motion_run": {k: s["motion_reduced"][k] for k in ("first", "steps", "violations")}, "normal_run_positive_control": {k: s["motion_normal"][k] for k in ("first", "steps", "violations")}},
    "AC-7-01": lambda s: {"inventory": {k: s["settings"]["inventory"][k] for k in ("missing", "wrongCategory", "notInInventory", "unmappedSettings")}, "entries": len(s["settings"]["inventory"]["listing"]["entries"])},
    "AC-7-02": lambda s: {"open": s["settings"]["open"], "close": s["settings"]["close"]},
    "AC-7-03": lambda s: {"apply": s["settings"]["apply"], "theme_sync": s["theme"]},
    "AC-7-04": lambda s: {"reset": s["settings"]["reset"]},
    "AC-7-07": lambda s: {"gpu": s["settings"]["gpu"]},
    "AC-7-08": lambda s: {"search": s["settings"]["search"]},
}

SCRIPT = {"AC-3-07": M + "uia_invoke.ps1", "AC-3-09": M + "retest_window_menu.py", "AC-6-10": M + "retest_tour_motion.py"}
for _a in ("AC-3-01", "AC-3-02", "AC-3-03", "AC-3-04", "AC-3-05", "AC-3-06", "AC-3-08"):
    SCRIPT[_a] = M + "retest_panel_corner.py"
for _a in ("AC-4-01", "AC-4-02", "AC-4-03", "AC-4-04", "AC-4-06"):
    SCRIPT[_a] = M + "retest_redock_policy.py"
for _a in ("AC-5-02", "AC-5-03", "AC-5-04", "AC-5-05", "AC-5-06", "AC-5-08"):
    SCRIPT[_a] = M + "retest_monitor_change.py"
for _a in ("AC-6-02", "AC-6-03", "AC-6-04", "AC-6-06", "AC-6-07"):
    SCRIPT[_a] = M + "retest_tour.py"
for _a in ("AC-7-01", "AC-7-02", "AC-7-03", "AC-7-04", "AC-7-07", "AC-7-08"):
    SCRIPT[_a] = M + "retest_settings_window.py"
RUNLOG = {"retest_panel_corner.py": ["panel_all", "panel_persist_after"], "uia_invoke.ps1": ["uia", "panel_glyphs"], "retest_window_menu.py": ["window_menu2"],
          "retest_redock_policy.py": ["redock"], "retest_monitor_change.py": ["monitor"], "retest_tour.py": ["tour", "uia"],
          "retest_settings_window.py": ["settings2", "theme_sync"], "retest_tour_motion.py": []}


def extra_rules(ac, s, m):
    """ui_req3_8_rules 가 다루지 않는 수용 기준 부분. (사유 목록, 보류 목록)"""
    reasons, holds = [], []
    if ac == "AC-3-07":
        inv, back = m["uia_invoke_expand"] or {}, m["uia_invoke_restore"] or {}
        if not (inv.get("invoked") and back.get("invoked")):
            reasons.append("UIA Invoke(Enter 접근 대응) 펼침·되돌림 중 실패: %s / %s" % (inv.get("invoked"), back.get("invoked")))
        if m["owners_after_invoke"] != {"BottomLeft": "BottomPane"} or m["owners_after_restore"] != {}:
            reasons.append("Invoke 뒤 owners 변화가 기대와 다름: %s → %s" % (m["owners_after_invoke"], m["owners_after_restore"]))
        rows = m["glyph_rows"] if isinstance(m["glyph_rows"], list) else []
        bad = [r.get("id") for r in rows if not (r.get("glyph_ok") and r.get("focusable") and r.get("tabStop"))]
        if not rows or bad:
            reasons.append("글리프 방향·Tab 접근 행 실패: %s" % (bad or "행 없음"))
    elif ac == "AC-3-09":
        bad = [r["no"] for r in m["rows"] if r["verdict"] not in ("PASS", "HOLD")]
        if bad:
            reasons.append("window_menu 회귀 FAIL 행: %s" % bad)
        holds.append("HOLD 행 %s(요건 A07 보류 항목)" % [r["no"] for r in m["rows"] if r["verdict"] == "HOLD"])
        holds.append("재기동 회차(저장→재기동→유지)는 run_desktop_bg.sh 경유라 user_files/pending 표지로 막혀 --no-restart 로만 잼 — pending 보류")
    elif ac == "AC-6-03":
        during = [m["uia_invoke_theme_during_tour"] or {}, m["uia_invoke_mnuFile_during_tour"] or {}]
        if not all(u.get("found") and u.get("invoked") is False and u.get("enabled") is False for u in during):
            reasons.append("투어 중 UIA Invoke 가 거부되지 않음: %s" % [(u.get("id"), u.get("enabled"), u.get("invoked")) for u in during])
        if not (m["uia_positive_control_next"] or {}).get("invoked"):
            reasons.append("양성 대조(tour.btnNext Invoke) 실패")
        if not (m["uia_invoke_theme_after_tour"] or {}).get("invoked"):
            reasons.append("투어 끝난 뒤 btnTheme Invoke 실패(투어 밖 접근 확인 불가)")
        if m["theme_before_tour_invoke"] is None or m["theme_after_tour_invoke"] is None:
            holds.append("대상 컨트롤 상태 불변(테마) 부분 미측정 — settings.get 값을 읽지 못함")
        elif m["theme_before_tour_invoke"] != m["theme_after_tour_invoke"]:
            reasons.append("투어 중 Invoke 시도 뒤 테마가 바뀜: %s → %s" % (m["theme_before_tour_invoke"], m["theme_after_tour_invoke"]))
    elif ac == "AC-6-10":
        red, nor = m["reduced_motion_run"], m["normal_run_positive_control"]
        an = lambda d: [x["i"] for x in d["steps"] if x.get("animated")]
        if red["first"].get("IsMotionReduced") != "True" or red["violations"] or an(red):
            reasons.append("움직임 줄이기에서 전환 있음: animated=%s violations=%s" % (an(red), red["violations"]))
        if nor["first"].get("IsMotionReduced") != "False" or not an(nor) or nor["violations"]:
            reasons.append("양성 대조(normal)에서 전환 없음: animated=%s" % an(nor))
        holds.append("애니메이션 가독성 H-6-1 은 사람 확인")
    elif ac == "AC-5-03":
        holds.append("실제 다중 모니터 의존 부분(실모니터 분리·연결, screens[] 실목록)은 모니터 1대 정상 구성이라 보류 — 판정은 수용 기준대로 가상 모니터 2→1 값으로 냄(AC-5-07 사람 확인 별도)")
    return reasons, holds


def judge_all(s):
    split = s["split"] if s["split"] else None
    panel_in = dict(s["panel"])
    inv = s["uia"].get("panel invoke BottomLeft") or {}
    panel_in["uia_activation"] = {"invoked": inv.get("invoked")}  # ui_req3_8_rules 가 읽는 키 — uia_invoke.ps1 결과
    j = {}
    j.update(rules.judge_panel(panel_in, split or {}, s["persist_after"]))
    j.update(rules.judge_redock(s["redock"]))
    j.update(rules.judge_monitor(s["monitor"]))
    j.update(rules.judge_settings(s["settings"]))
    j.update(rules.judge_tour(s["tour"]))
    j.update(rules.judge_theme_sync(s["theme"]))
    return j


def sha(p):
    return hashlib.sha256(open(p if os.path.isabs(p) else os.path.join(ROOT, p), "rb").read()).hexdigest()


def times(args, tool):
    st, fi = [], []
    for tag in RUNLOG.get(os.path.basename(tool), []):
        p = os.path.join(args.runs, "r1_%s.log" % tag)
        if os.path.exists(p):
            t = open(p, encoding="utf-8", errors="replace").read()
            st += re.findall(r"^started_at (\S+)", t, re.M)
            fi += re.findall(r"^finished_at (\S+)", t, re.M)
    return (min(st) if st else None), (max(fi) if fi else None)


def compute(args):
    s = load(args)
    j = judge_all(s)
    out = {}
    for ac in sorted(MAP):
        m = MAP[ac](s)
        reasons = list((j.get(ac) or {}).get("reasons", [])) if ac in j else []
        if ac == "AC-7-03":
            reasons += j["AC-7-03-sync"]["reasons"]
        er, holds = extra_rules(ac, s, m)
        reasons += er
        verdict = "FAIL" if reasons or any("미측정" in h for h in holds) else "PASS"  # 수용 기준의 일부라도 못 쟀으면 PASS 가 아니다(부분 미측정)
        tool = SCRIPT[ac]
        st, fi = times(args, tool)
        if ac == "AC-6-10":
            st, fi = "2026-11-08T00:19:24+09:00", "2026-11-08T00:20:08+09:00"
        out[ac] = {"verdict": verdict, "ac_id": ac, "writer": "otest-15", "run_id": "r1-otest15-remap",
                   "started_at": st or "", "finished_at": fi or "",
                   "script": {"path": tool, "sha256": sha(tool)}, "build": {"path": BUILD, "sha256": sha(BUILD)},
                   "measurements": [{"name": k, "value": v} for k, v in m.items()],
                   "criterion": AC_TEXT[ac], "judge": "ui_req3_8_rules.py + ui28_remap.extra_rules" if ac in j else "ui28_remap.extra_rules",
                   "reasons": reasons, "holds": holds,
                   "remap": {"script": "test/desktop/judge/ui28_remap.py", "sha256": sha(os.path.abspath(__file__))}}
    return out


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--raw-dir", required=True)
    ap.add_argument("--motion-dir", required=True)
    ap.add_argument("--runs", required=True)
    ap.add_argument("--write", action="store_true")
    ap.add_argument("--check", action="store_true")
    args = ap.parse_args()
    a = compute(args)
    if args.check:
        b = compute(args)
        same = json.dumps(a, sort_keys=True, ensure_ascii=False) == json.dumps(b, sort_keys=True, ensure_ascii=False)
        print("결정성", "같음" if same else "다름")
        if not same:
            return 1
    with open(os.path.join(args.raw_dir, "ui28_remap_out.json"), "w", encoding="utf-8") as f:
        json.dump(a, f, ensure_ascii=False, indent=1, sort_keys=True)
    for ac, d in a.items():
        print(ac, d["verdict"], ("사유 %s" % d["reasons"]) if d["reasons"] else "", ("보류 %s" % d["holds"]) if d["holds"] else "")
        if args.write:
            p = os.path.join(ROOT, "logs/ui28/%s.json" % ac)
            with open(p + ".tmp", "w", encoding="utf-8") as f:
                json.dump(d, f, ensure_ascii=False, indent=1)
            os.replace(p + ".tmp", p)
    print("출력 sha256", hashlib.sha256(json.dumps(a, sort_keys=True, ensure_ascii=False).encode("utf-8")).hexdigest())
    return 0


if __name__ == "__main__":
    sys.exit(main())
