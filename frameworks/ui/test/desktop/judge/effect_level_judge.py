# AC-030 판정기 — 효과 단계가 해석 층인지(실효값·덮어쓰기 0·테마 전환·표시·묶음 표 1곳) 원격 값으로 판정한다
"""
사용
  effect_level_judge.py --info <격리>/remote-control.json --away-state <pc_away.json> --approved '<누가·언제>' --out <결과.json>
  effect_level_judge.py --bundle <wwwroot> --port <포트> --out <결과.json>      (웹 결과)
  effect_level_judge.py --self-test

기대값은 plan 21 §5.2 표에서 읽는다(구현 값을 기대값으로 쓰지 않는다). 테마 고유 값은 단계 theme 에서 잰 실효값이다.
덮어쓰기가 남은 인스턴스, 낮춤(fallbackReason)이 걸린 상태는 실효값 = 묶음을 잴 수 없어 INVALID 다.
"""
import glob
import os
import re
import sys
import time

import judge_session as session

ROOT = os.path.join(session.HERE, "..", "..", "..")
DOC = os.path.join(ROOT, "..", "..", "plan", "09_*", "21_*.md")
TABLE = os.path.join(ROOT, "src", "xkxk.frameworks.ui.shell", "Theming", "DesignThemes.Levels.cs")
LEVELS = ("high", "medium", "low")
THEMES = ("ds-cute", "ds-depth")
SHADOW = "ui.mdi.move_light.shadow"
BUNDLE_ROW = re.compile(r'\["(high|medium|low)"\]\s*=')


def bundles(text):
    """21 §5.2 표 → {단계: {키: 값}}. '(묶음 밖…)' 칸은 그 단계 묶음에 없다."""
    body = text.split("### 5.2", 1)[-1].split("### 5.3", 1)[0]
    out = {level: {} for level in LEVELS}
    for line in body.splitlines():
        cell = [c.strip().strip("`") for c in line.strip().strip("|").split("|")]
        if len(cell) != 4 or not re.match(r"^[a-z][a-z_.]*$", cell[0]):
            continue
        for level, value in zip(LEVELS, cell[1:]):
            if not value.startswith("("):
                out[level][cell[0]] = value
    return out


def value_of(state, key):
    return state.get("moveShadow") if key == SHADOW else (state.get("effective") or {}).get(key)


def mismatch(state, expected):
    return {k: [value_of(state, k), v] for k, v in expected.items() if value_of(state, k) != v}


def verdict(problems, why, **data):
    return dict(data, verdict="FAIL" if problems else "PASS", why=why, problems=problems)


def judge_level(before, after, level, bundle):
    if after.get("fallbackReason"):
        return {"verdict": "INVALID", "why": "낮춤이 걸려 묶음과 견줄 수 없다: %s" % after["fallbackReason"]}
    bad = mismatch(after, bundle)
    if after.get("effectLevel") != level:
        bad["effectLevel"] = [after.get("effectLevel"), level]
    if after.get("overrides") != before.get("overrides"):
        bad["overrides"] = [after.get("overrides"), before.get("overrides")]
    if after.get("levelDisplay") != {"level": level, "overrides": 0}:
        bad["levelDisplay"] = [after.get("levelDisplay"), {"level": level, "overrides": 0}]
    return verdict(bad, "%s 실효값 = 묶음·덮어쓰기 증가 0" % level)


def judge_switch(first, second, preset_first, preset_second, bundle):
    """묶음 밖 키는 새 테마 값, 묶음 안 키는 그대로. 두 테마에서 다른 묶음 밖 키가 없으면 바뀜을 잴 수 없다."""
    keys = [k for k in preset_second["effective"] if k not in bundle and k != "depth.open"]  # depth.open 은 motion.open 별칭
    differing = [k for k in keys if preset_first["effective"].get(k) != preset_second["effective"][k]]
    if not differing:
        return {"verdict": "INVALID", "why": "두 테마에서 다른 묶음 밖 키가 없다"}
    bad = mismatch(second, dict({k: preset_second["effective"][k] for k in keys}, **bundle))
    bad.update({"전환 전 " + k: v for k, v in mismatch(first, {k: preset_first["effective"][k] for k in differing}).items()})
    if second.get("overrides"):
        bad["overrides"] = [second["overrides"], {}]
    return verdict(bad, "테마 전환 뒤 묶음 밖 키 = 새 테마 값", changed_keys=differing)


def judge_back(state, preset):
    bad = mismatch(state, dict(preset["effective"], **{SHADOW: preset.get("moveShadow")}))
    if state.get("overrides") or state.get("effectLevel") != "theme":
        bad["state"] = [state.get("overrides"), state.get("effectLevel")]
    return verdict(bad, "theme 복귀 뒤 실효값 = 새 테마 기본값")


def judge_single(state, label, key, value, bundle):
    bad = mismatch(state, dict(bundle, **{key: value}))
    if state.get("overrides") != {key: value}:
        bad["overrides"] = [state.get("overrides"), {key: value}]
    if state.get("levelDisplay") != {"level": "high", "overrides": 1}:
        bad["levelDisplay"] = [state.get("levelDisplay"), {"level": "high", "overrides": 1}]
    if label not in ("높음 + 사용자 지정 1개", "High + 1 custom"):
        bad["label"] = [label, "높음 + 사용자 지정 1개"]
    return verdict(bad, "낱개 1개만 덮어쓰기·'단계 + 사용자 지정 N개' 표시", label=label)


def table_places():
    found = []
    for folder, dirs, names in os.walk(os.path.join(ROOT, "src")):
        dirs[:] = [d for d in dirs if d not in ("bin", "obj", "node_modules")]
        for name in names:
            if name.endswith(".cs"):
                with open(os.path.join(folder, name), encoding="utf-8-sig") as f:
                    if BUNDLE_ROW.search(f.read()):
                        found.append(os.path.abspath(os.path.join(folder, name)))
    return found


def judge_table(places):
    bad = [] if places == [os.path.abspath(TABLE)] else places or ["묶음 표를 찾지 못함"]
    return verdict(bad, "묶음 표는 DesignThemes.Levels 1곳", places=places)


def judge(s):
    with open(glob.glob(DOC)[0], encoding="utf-8-sig") as f:
        want = bundles(f.read())
    if not all(len(want[level]) >= 8 for level in LEVELS):
        raise session.Abort("INVALID", "21 §5.2 표를 읽지 못했다")
    start = s.call("style.state")
    if start.get("overrides") or (start.get("levelDisplay") or {}).get("overrides"):
        raise session.Abort("INVALID", "덮어쓰기가 남은 인스턴스다(깨끗한 격리 폴더로 다시)")

    def put(key, value):
        r = s.call("style.effect.set", key=key, value=value)
        if not r.get("applied"):
            raise session.Abort("INVALID", "%s=%s 적용 안 됨: %s" % (key, value, r))
        time.sleep(0.4)
        return s.call("style.state")

    def theme(name):
        if not s.call("style.set", id=name).get("applied"):
            raise session.Abort("INVALID", "%s 테마를 고르지 못했다" % name)
        time.sleep(0.8)
        return s.call("style.state")

    cases = {}
    try:
        put("effectLevel", "theme")
        preset = {name: theme(name) for name in THEMES}
        theme(THEMES[0])
        for level in LEVELS:
            before = put("effectLevel", "theme")
            first = put("effectLevel", level)
            cases["select." + level] = judge_level(before, first, level, want[level])
            second = theme(THEMES[1])
            cases["switch." + level] = judge_switch(first, second, preset[THEMES[0]], preset[THEMES[1]], want[level])
            cases["back." + level] = judge_back(put("effectLevel", "theme"), preset[THEMES[1]])
            theme(THEMES[0])
        put("effectLevel", "high")
        changed = put("shadow", "none")
        s.call("theme.openEditor")
        time.sleep(1.5)
        open_design_tab(s)
        label = s.call("ui.find", id="designTheme.lblLevel").get("text")
        s.capture("level_label")
        cases["single"] = judge_single(changed, label, "shadow", "none", want["high"])
    finally:
        s.call("style.effect.set", key="shadow", value="")
        s.call("style.effect.set", key="effectLevel", value=start.get("effectLevel") or "theme")
        s.call("style.set", id=start["id"])
    cases["table"] = judge_table(table_places())
    return cases


def open_design_tab(s):
    s.call("ui.click", id="theme.tabDesignTheme")
    time.sleep(1.5)


def self_test():
    sample = bundles("### 5.2 x\n| 키 | 높음 | 중간 | 낮음 |\n|---|---|---|---|\n| shadow | float | soft | none |\n"
                     "| decor | aurora | (묶음 밖) | none |\n| `ui.mdi.move_light.shadow` | keep | hide | hide |\n### 5.3 y")

    def state(level="high", over=None, count=0, **change):
        eff = dict({"shadow": "float", "decor": "aurora", "element": "round"}, **change)
        return {"effective": eff, "moveShadow": "keep", "effectLevel": level, "overrides": over or {},
                "levelDisplay": {"level": level, "overrides": count}}

    p1 = state("theme", shadow="soft", decor="dots", element="round")
    p2 = state("theme", shadow="deep", decor="grid", element="sharp")
    high = sample["high"]
    single = state(over={"shadow": "none"}, count=1, shadow="none")
    return session.run_cases([
        ("표 읽기", high == {"shadow": "float", "decor": "aurora", SHADOW: "keep"}),
        ("묶음 밖 칸 제외", "decor" not in sample["medium"]),
        ("단계 PASS", judge_level(p1, state(), "high", high)["verdict"] == "PASS"),
        ("실효값 다름 FAIL", judge_level(p1, state(shadow="soft"), "high", high)["verdict"] == "FAIL"),
        ("덮어쓰기 기록 FAIL", judge_level(p1, state(over={"shadow": "float"}), "high", high)["verdict"] == "FAIL"),
        ("낮춤 INVALID", judge_level(p1, dict(state(), fallbackReason="software"), "high", high)["verdict"] == "INVALID"),
        ("전환 PASS", judge_switch(state(), state(element="sharp"), p1, p2, high)["verdict"] == "PASS"),
        ("옛 테마 값 남음 FAIL", judge_switch(state(), state(), p1, p2, high)["verdict"] == "FAIL"),
        ("차이 없는 테마 INVALID", judge_switch(state(), state(), p1, p1, high)["verdict"] == "INVALID"),
        ("복귀 PASS", judge_back(p2, p2)["verdict"] == "PASS"),
        ("복귀 값 남음 FAIL", judge_back(state("theme"), p2)["verdict"] == "FAIL"),
        ("낱개 PASS", judge_single(single, "높음 + 사용자 지정 1개", "shadow", "none", high)["verdict"] == "PASS"),
        ("표시 다름 FAIL", judge_single(single, "높음", "shadow", "none", high)["verdict"] == "FAIL"),
        ("표 2곳 FAIL", judge_table([os.path.abspath(TABLE), "/x.cs"])["verdict"] == "FAIL"),
    ])


def main():
    args = session.parser(__doc__).parse_args()
    return self_test() if args.self_test else session.run(args, "effect_level_judge", judge)


if __name__ == "__main__":
    sys.exit(main())
