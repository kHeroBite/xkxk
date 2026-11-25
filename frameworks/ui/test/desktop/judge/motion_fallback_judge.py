# AC-013 판정기 — 움직임 수준(off·reduced·full)·효과 단계 high·소프트웨어 렌더 조합마다 motion.probe resolved 가 폴백 규칙대로인지 본다
"""
사용
  motion_fallback_judge.py --info <격리>/remote-control.json [--away-state <pc_away.json>] --approved '<누가·언제>' --out <결과.json>
  (기본 remote_only — 원격 명령만 쓰므로 PC 비움 검사를 하지 않는다)
  motion_fallback_judge.py --self-test

기대(a14 AC-013, 해석기 DesignThemeResolver.LowerMove): 움직임 끔·소프트웨어 렌더 → 열기·닫기·전환 모두 none,
움직임 줄이기 → 클립·밀어내기·3D(무거운 효과)는 fade, 효과 단계 high 여도 이 폴백이 앞선다.
무거운 효과가 실제로 낮춰지는지 보려고 열기 wipeLeft(클립)·닫기 pushLeft(밀어내기)·전환 tilt3d(3D)를 덮어쓴 뒤 잰다.
배치는 MdiOverlap·DockTab 두 곳. resolved 는 표본 재생 없이 읽는다(effect 에 표에 없는 이름을 준다).
문서를 열지 않는다(창 수 초과 폴백이 섞이지 않게). 끝나면 움직임 수준·효과 단계·덮어쓰기·소프트웨어 모의를 원래대로 되돌린다.
rc 는 judge_session 과 같다(PASS 0 · FAIL 1 · INVALID 2 …).
"""
import sys
import time

import judge_session as session

KEYS = ("open", "close", "switch")
HEAVY_SET = {"open": "wipeLeft", "close": "pushLeft", "switch": "tilt3d"}  # 클립·밀어내기·3D
OVERRIDE_KEY = {"open": "motion.open", "close": "motion.close", "switch": "motion.switch"}
HEAVY = {"pushLeft", "pushRight", "pushUp", "pushDown", "wipeLeft", "wipeRight", "wipeUp", "wipeDown",
         "flyUp", "flyDown", "split", "circle", "tilt3d"}  # MotionEffects 표의 Heavy(이동·잘라냄·3D) 행
PLACEMENTS = ("MdiOverlap", "DockTab")
NO_SAMPLE = "__resolved_only__"  # 표에 없는 effect → 표본 재생 없이 resolved 만

# (사례, 움직임 수준, 효과 단계, 소프트웨어, 무거운 효과 덮어쓰기, 기대, 기대 폴백 사유)
COMBOS = (
    ("full", "full", "theme", False, True, "configured", None),
    ("off", "off", "theme", False, True, "none", "motionOff"),
    ("reduced", "reduced", "theme", False, True, "fade", "reducedMotion"),
    ("software", "full", "theme", True, True, "none", "software"),
    ("high_off", "off", "high", False, True, "none", "motionOff"),
    ("high_reduced", "reduced", "high", False, True, "fade", "reducedMotion"),
    ("high_software", "full", "high", True, True, "none", "software"),
    ("high_bundle_reduced", "reduced", "high", False, False, "light", "reducedMotion"),
)


def expected_ok(want, key, got):
    if want == "none":
        return got == "none"
    if want == "fade":
        return got == "fade"
    if want == "configured":
        return got == HEAVY_SET[key]
    if want == "light":  # 효과 단계 묶음 값 + 줄이기: 무거운 효과가 남지 않는다
        return bool(got) and got not in HEAVY
    raise ValueError(want)


def judge_combo(resolved, want, reason_have=None, reason_want=None):
    """resolved{open,close,switch} 를 기대와 대조. resolved 를 못 읽으면 INVALID."""
    if not isinstance(resolved, dict) or any(k not in resolved for k in KEYS):
        return {"verdict": "INVALID", "why": "motion.probe resolved 를 읽지 못했다", "resolved": resolved}
    bad = {k: [resolved[k], want] for k in KEYS if not expected_ok(want, k, resolved[k])}
    if reason_want and reason_want not in (reason_have or "").split(","):
        bad["폴백 사유"] = [reason_have, reason_want]
    if not reason_want and reason_have:
        bad["폴백 사유(없어야 함)"] = reason_have
    return {"verdict": "FAIL" if bad else "PASS", "why": "기대 %s" % want, "resolved": resolved,
            "fallbackReason": reason_have, "problems": bad}


def judge(s):
    start = s.call("style.state")
    orig = {"level": start.get("motionLevel") or "system", "effectLevel": start.get("effectLevel") or "theme",
            "overrides": {k: (start.get("overrides") or {}).get(v, "") for k, v in OVERRIDE_KEY.items()}}
    if start.get("locked"):
        raise session.Abort("INVALID", "회사 잠금이라 효과 덮어쓰기를 못 한다(잠금 없는 격리 폴더로 다시)")
    cases = {}
    try:
        for name, level, effect_level, software, heavy, want, reason in COMBOS:
            s.effect("effectLevel", effect_level)
            for key, oid in OVERRIDE_KEY.items():
                if heavy:
                    s.effect(oid, HEAVY_SET[key])
                else:
                    s.call("style.effect.set", key=oid, value="")
            s.call("settings.set", **{"theme.motion.level": level})
            s.call("gpu.simulateSoftware", on=software)
            time.sleep(0.4)
            state = s.call("style.state")
            for place in PLACEMENTS:
                probe = s.call("motion.probe", effect=NO_SAMPLE, placement=place)
                case = judge_combo(probe.get("resolved"), want, state.get("fallbackReason"), reason)
                case.update(motionLevel=state.get("motionLevel"), effectLevel=state.get("effectLevel"), software=software)
                cases["%s.%s" % (name, place)] = case
    finally:
        s.call("gpu.simulateSoftware", on=False)
        s.call("settings.set", **{"theme.motion.level": orig["level"]})
        for key, oid in OVERRIDE_KEY.items():
            s.call("style.effect.set", key=oid, value=orig["overrides"][key] or "")
        s.call("style.effect.set", key="effectLevel", value=orig["effectLevel"])
    end = s.call("style.state")
    restored = end.get("motionLevel") == orig["level"] and end.get("effectLevel") == orig["effectLevel"] and not (
        "software" in (end.get("fallbackReason") or ""))
    cases["restore"] = {"verdict": "PASS" if restored else "FAIL", "why": "설정 원복",
                        "before": orig, "after": {"motionLevel": end.get("motionLevel"), "effectLevel": end.get("effectLevel"),
                                                   "fallbackReason": end.get("fallbackReason")}}
    return cases


def self_test():
    heavy = dict(HEAVY_SET)
    none = {k: "none" for k in KEYS}
    fade = {k: "fade" for k in KEYS}
    light = {"open": "fadeZoom", "close": "fadeZoomOut", "switch": "fadeZoom"}
    return session.run_cases([
        ("off → none PASS", judge_combo(none, "none", "motionOff", "motionOff")["verdict"] == "PASS"),
        ("off 인데 효과 남음 FAIL", judge_combo(dict(none, close="pushLeft"), "none", "motionOff", "motionOff")["verdict"] == "FAIL"),
        ("reduced → fade PASS", judge_combo(fade, "fade", "reducedMotion", "reducedMotion")["verdict"] == "PASS"),
        ("reduced 인데 3D 남음 FAIL", judge_combo(dict(fade, switch="tilt3d"), "fade", "reducedMotion", "reducedMotion")["verdict"] == "FAIL"),
        ("high+reduced 폴백 사유 없음 FAIL", judge_combo(fade, "fade", None, "reducedMotion")["verdict"] == "FAIL"),
        ("소프트웨어 → none PASS", judge_combo(none, "none", "software", "software")["verdict"] == "PASS"),
        ("소프트웨어 사유 결합 PASS", judge_combo(none, "none", "software,reducedMotion", "software")["verdict"] == "PASS"),
        ("full 설정값 PASS", judge_combo(heavy, "configured", None, None)["verdict"] == "PASS"),
        ("full 인데 폴백 사유 FAIL", judge_combo(heavy, "configured", "software", None)["verdict"] == "FAIL"),
        ("full 인데 낮춰짐 FAIL", judge_combo(fade, "configured", None, None)["verdict"] == "FAIL"),
        ("묶음+줄이기 가벼움 PASS", judge_combo(light, "light", "reducedMotion", "reducedMotion")["verdict"] == "PASS"),
        ("묶음+줄이기 무거움 FAIL", judge_combo(dict(light, open="wipeLeft"), "light", "reducedMotion", "reducedMotion")["verdict"] == "FAIL"),
        ("resolved 없음 INVALID", judge_combo(None, "none")["verdict"] == "INVALID"),
        ("resolved 키 빠짐 INVALID", judge_combo({"open": "none"}, "none")["verdict"] == "INVALID"),
        ("조합 8개·배치 2곳", len(COMBOS) == 8 and len(PLACEMENTS) == 2),
        ("무거운 효과 3개가 HEAVY", all(v in HEAVY for v in HEAVY_SET.values())),
    ])


def main():
    ap = session.parser(__doc__)
    ap.set_defaults(remote_only=True)  # 원격 명령만 쓴다(실입력 없음) — PC 비움 검사 생략
    args = ap.parse_args()
    return self_test() if args.self_test else session.run(args, "motion_fallback_judge", judge)


if __name__ == "__main__":
    sys.exit(main())
