# 웹 축 raw 측정(ui28_web.py)을 판정 규칙(ui28_web_rules.py)으로 판정해 결과 계약 JSON(logs/ui28/<AC-ID>.json)으로 쓴다
# 사용: python3 -B ui28_web_emit.py --raw <raw.json> --run-id <id> --build <wasm 경로> --evidence-dir <폴더> [--only AC-2-09,AC-4-05,AC-6-09]
# AC-2-09 는 '문서 대조'가 합격 조건에 있다 → plan 문서에 웹 포인터 추적·Android·폰 해당 없음·macOS 미검증 서술이 있어야 PASS 다(고정 규칙, 아래 doc_check).
import argparse
import hashlib
import json
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
UI = os.path.abspath(os.path.join(HERE, "..", "..", ".."))  # 영역 루트(frameworks/ui)
REPO = UI  # 정본 문서(plan/)와 증거 상대 경로의 기준 = 영역 루트
sys.path.insert(0, HERE)
import ui28_web_rules as R  # noqa: E402


def sha(p):
    h = hashlib.sha256()
    with open(p, "rb") as f:
        for c in iter(lambda: f.read(1 << 20), b""):
            h.update(c)
    return h.hexdigest()


def rel(p):
    return os.path.relpath(os.path.abspath(p), REPO).replace(os.sep, "/")


def doc_check():
    """AC-2-09 문서 대조: plan/09_화면설계·plan/04_기술스택·plan/05_아키텍처 의 md 에서 세 가지 서술을 찾는다(줄 단위)."""
    want = {"web_follower": re.compile(r"(스피너|커서).*(포인터 추적)|(포인터 추적).*(스피너|커서)"),
            "android_phone_na": re.compile(r"(스피너|커서)"),
            "macos_unverified": re.compile(r"(스피너|커서)")}
    found = {k: [] for k in want}
    for sub in ("plan/09_화면설계", "plan/04_기술스택", "plan/05_아키텍처", "plan/03_개발범위"):
        base = os.path.join(REPO, sub)
        for root, _d, files in os.walk(base):
            for fn in files:
                if not fn.endswith(".md"):
                    continue
                path = os.path.join(root, fn)
                try:
                    lines = open(path, encoding="utf-8").read().splitlines()
                except OSError:
                    continue
                for i, ln in enumerate(lines, 1):
                    if want["web_follower"].search(ln):
                        found["web_follower"].append("%s:%d" % (rel(path), i))
                    if want["android_phone_na"].search(ln) and re.search(r"Android|안드로이드|스마트폰|폰", ln) and "해당 없음" in ln:
                        found["android_phone_na"].append("%s:%d" % (rel(path), i))
                    if want["macos_unverified"].search(ln) and re.search(r"macOS|맥", ln) and re.search(r"미검증|미실측|미확인", ln):
                        found["macos_unverified"].append("%s:%d" % (rel(path), i))
    return found


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--raw", required=True)
    ap.add_argument("--run-id", required=True)
    ap.add_argument("--build", required=True)
    ap.add_argument("--evidence-dir", required=True)
    ap.add_argument("--only", default="AC-2-09,AC-4-05,AC-6-09")
    ap.add_argument("--writer", default="otest-w-1")
    ap.add_argument("--measure-script", default=os.path.join(UI, "test", "web", "measure", "ui28_web.py"))
    a = ap.parse_args()
    raw = json.load(open(a.raw, encoding="utf-8"))
    out_dir = os.path.join(UI, "logs", "ui28")
    os.makedirs(out_dir, exist_ok=True)
    rules = {"AC-2-09": ("ac409", R.rule_ac409), "AC-4-05": ("ac405", R.rule_ac405), "AC-6-09": ("ac609", R.rule_ac609)}
    ev_dir = rel(a.evidence_dir)
    shots = sorted(f for f in os.listdir(os.path.join(a.evidence_dir, "shots")))
    summary = {}
    for ac in a.only.split(","):
        key, fn = rules[ac]
        sec = raw[key]
        v = fn(sec)
        reasons = list(v["reasons"])
        meas = []
        if ac == "AC-2-09":
            px, bs = sec.get("pixels") or {}, sec.get("busy_state") or {}
            meas += [{"name": "cursorMode", "value": bs.get("cursorMode"), "expected": "follower"},
                     {"name": "usesFollower", "value": bs.get("usesFollower")},
                     {"name": "followerVisible_after_real_mouse_move", "value": (sec.get("after_move") or {}).get("followerVisible")},
                     {"name": "followerVisible_after_busy_end", "value": (sec.get("after_end") or {}).get("followerVisible")},
                     {"name": "pixels_changed_at_pointer", "value": px.get("new_changed_px"), "expected": ">=%d" % R.FOLLOW_MIN_CHANGED_PX},
                     {"name": "pixels_left_at_old_pointer", "value": px.get("old_changed_px"), "expected": "<=%d" % R.FOLLOW_OLD_MAX_CHANGED_PX},
                     {"name": "pixels_animation_same_spot", "value": px.get("anim_changed_px"), "expected": ">0"},
                     {"name": "pixels_left_after_end", "value": px.get("end_changed_px"), "expected": "<=%d" % R.FOLLOW_OLD_MAX_CHANGED_PX}]
            dc = doc_check()
            meas.append({"name": "doc_check", "value": {k: len(v2) for k, v2 in dc.items()}, "locations": dc})
            for k, label in (("web_follower", "웹 포인터 추적 스피너"), ("android_phone_na", "Android·스마트폰 커서 스피너 해당 없음"), ("macos_unverified", "macOS 커서 스피너 미검증")):
                if not dc[k]:
                    reasons.append("plan 문서 대조: '%s' 서술이 없음(plan/09·04·05·03 md 줄 검색)" % label)
        elif ac == "AC-4-05":
            s, g = sec.get("steps") or {}, sec.get("guard") or {}
            meas += [{"name": "selftest_passed", "value": "%s/%s" % ((sec.get("selftest") or {}).get("passed"), (sec.get("selftest") or {}).get("total"))},
                     {"name": "mdi_real_drag_px", "value": s.get("mdi_drag_px")}, {"name": "floated_managed", "value": s.get("floated_managed")},
                     {"name": "sdiFrame_null_on_managed_window", "value": s.get("sdi_frame_null")}, {"name": "managed_real_drag_px", "value": s.get("managed_drag_px")},
                     {"name": "redocked", "value": s.get("redocked")}, {"name": "redock_vs_prefloat_max_delta_px", "value": s.get("redock_vs_prefloat_max_delta"), "expected": "<=%d" % R.BOUNDS_TOL},
                     {"name": "mdi_guard_evaluations", "value": [g.get("evaluations_before"), g.get("evaluations_after")]},
                     {"name": "mdi_guard_moved", "value": [g.get("moved_before"), g.get("moved_after")]}, {"name": "title_visible_px_after_recover", "value": g.get("title_visible_px"), "expected": ">=120"}]
        else:
            for run in sec.get("runs") or []:
                meas.append({"name": "notes_%s_%s" % (run["lang"], run["theme"]), "value": {k: n["text"] for k, n in run["notes"].items() if n and n.get("text")}, "active_theme": run.get("active_theme_id")})
        meas.append({"name": "console_errors", "value": len(sec.get("console_errors") or []), "expected": 0})
        meas.append({"name": "elapsed_ms", "value": sec.get("elapsed_ms")})
        res = {"verdict": "PASS" if not reasons else "FAIL", "ac_id": ac, "writer": a.writer, "run_id": a.run_id,
               "started_at": raw.get("started_at"), "finished_at": raw.get("finished_at"),
               "script": {"path": rel(a.measure_script), "sha256": sha(a.measure_script)},
               "build": {"path": rel(a.build), "sha256": sha(a.build)},
               "measurements": meas, "reasons": reasons,
               "evidence": [ev_dir + "/" + os.path.basename(a.raw)] + [ev_dir + "/shots/" + f for f in shots if f.startswith(("ac409", "ac405", "ac609")[("AC-2-09", "AC-4-05", "AC-6-09").index(ac)])],
               "rules": rel(os.path.join(HERE, "ui28_web_rules.py")), "rules_sha256": sha(os.path.join(HERE, "ui28_web_rules.py"))}
        with open(os.path.join(out_dir, ac + ".json"), "w", encoding="utf-8") as f:
            json.dump(res, f, ensure_ascii=False, indent=1)
        summary[ac] = {"verdict": res["verdict"], "reasons": reasons}
    print(json.dumps(summary, ensure_ascii=False, indent=1))
    return 0 if all(v["verdict"] == "PASS" for v in summary.values()) else 1


if __name__ == "__main__":
    sys.exit(main())
