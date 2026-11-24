# 디자인 테마 축(REQ-104) 시험 케이스 정의 — 원격 계산값만으로 판정하는 케이스 함수 모음
"""
계약(계획서 §5 odev-8 행·§7.2, 구현 쪽 RemoteDesignThemeCommands 가 지킬 형식):
  style.set id=<ds-*>                 → 디자인 테마 선택(회사 잠금이면 무시)
  style.get / style.state             → {id, locked, motionLevel, fallbackReason, suggestedColor, glassOpacity,
                                         effective{shadow,surface,decor,motion,depth.open,depth.overview,depth.page,chrome},
                                         overrides{키:값}, resources{PdStyleShadow,PdStyleSurface,PdStyleDecor,PdStyleMotionMs},
                                         windows[{id}], active(창 id), overview{open,mode,cards[{windowId,x,y}]}}
  style.effect.set key= value=        → 효과 덮어쓰기(value 가 빈 문자열이면 해제, 허용 목록 밖이면 무시)
  overview.open / overview.select index=|x=,y= / overview.state
  depth.probe kind=open|page          → {samples[{angleY}], restored(끝 뒤 RenderTransform 이 전 값과 같음)}
  gpu.simulateSoftware on=true|false  → 진단 전용 소프트웨어 렌더 모의
  기존: theme.select id= · settings.get/set · themedesign.get el= · themedesign.contrast el= · docs.open kind=
판정은 측정값 비교만 한다. 결과를 본 뒤 기준을 바꾸지 않는다(기준은 케이스 안에 고정).
"""
import json
import time

PRESETS = {  # 설계 정본 18 §4.2 표(시험 기준값 — 제품 코드에는 없다)
    "ds-classic": {"shadow": "inherit", "surface": "flat", "decor": "none", "motion": "inherit", "depth.open": "none", "depth.overview": "flat", "depth.page": "inherit", "chrome": "standard"},
    "ds-cute": {"shadow": "soft", "surface": "flat", "decor": "dots", "motion": "bouncy", "depth.open": "none", "depth.overview": "flat", "depth.page": "inherit", "chrome": "pill"},
    "ds-simple": {"shadow": "none", "surface": "flat", "decor": "none", "motion": "subtle", "depth.open": "none", "depth.overview": "flat", "depth.page": "inherit", "chrome": "minimal"},
    "ds-rich": {"shadow": "float", "surface": "glass", "decor": "aurora", "motion": "cinematic", "depth.open": "none", "depth.overview": "flat", "depth.page": "inherit", "chrome": "standard"},
    "ds-depth": {"shadow": "float", "surface": "gradient", "decor": "waves", "motion": "cinematic", "depth.open": "tilt", "depth.overview": "carousel3d", "depth.page": "flip3d", "chrome": "standard"},
}
SUGGEST = {"ds-classic": "", "ds-cute": "pd-pastel", "ds-simple": "pd-light", "ds-rich": "pd-aurora", "ds-depth": ""}
OFF = ("", "none", "inherit", None)
COMMON = ["style_select_each", "classic_identical", "orthogonal_color", "user_override_wins", "effect_override", "restart_persist",
          "depth_tilt_transition", "overview3d", "hit_test_3d", "reduce_motion"]
DESKTOP_ONLY = ["company_lock", "legacy_profile", "overview_flat", "fallback_software", "contrast_ok", "suggest_color"]
WEB_ONLY = ["prefers_reduced_motion"]
ANDROID = ["style_select_each", "orthogonal_color", "restart_persist", "page_flip", "overview_nav_flat", "reduce_motion"]


class Blocked(Exception):
    """시험 대상이 계약 명령·값을 주지 않아 판정 불가 — PASS 로 치지 않는다."""


def res(ok, actual, expected):
    return {"verdict": "PASS" if ok else "FAIL", "actual": actual, "expected": expected}


def state(b):
    s = b.call("style.state")
    if not isinstance(s, dict) or not isinstance(s.get("effective"), dict):
        raise Blocked("style.state 응답 없음/형식 불일치: %r" % (s,))
    return s


def select(b, sid):
    if b.call("style.set", id=sid) is None:
        raise Blocked("style.set id=%s 실패" % sid)
    return state(b)


def need_no_fallback(s):
    if s.get("fallbackReason"):
        raise Blocked("폴백 중이라 효과 실효값을 낮췄다(%s) — 하드웨어 렌더 환경 필요" % s["fallbackReason"])


def set_setting(b, key, value):
    """settings.set 은 설정 이름을 인자 이름으로 받는다(theme.motion.level=reduced). key=/value= 로 보내면 앱이 모르는 인자로 무시한다
    (2026-11-16 K9 reduce_motion FAIL 원인 — 값이 바뀌지 않았다. RemoteSettingsCommands settings.set 도움말 형식에 맞춘 구동기 수정)."""
    return b.call("settings.set", **{key: value})


def reset_all(b):
    b.call("style.set", id="ds-classic")
    for k in PRESETS["ds-classic"]:
        b.call("style.effect.set", key=k, value="")
    set_setting(b, "theme.motion.level", "system")


def res_off(v):
    return str(v or "") in ("", "none", "None", "null")


def style_select_each(b, ctx):
    bad = {}
    for sid, exp in PRESETS.items():
        s = select(b, sid)
        need_no_fallback(s)
        if ctx.get("nav"):  # NAV(폰)은 전체 보기가 평면 목록(설계 18 §4.6)
            exp = dict(exp, **{"depth.overview": "flat"})
        got = {k: s["effective"].get(k) for k in exp}
        r = s.get("resources") or {}
        if got != exp or s.get("id") != sid:
            bad[sid] = got
        elif res_off(r.get("PdStyleDecor")) != (exp["decor"] == "none") or res_off(r.get("PdStyleShadow")) != (exp["shadow"] in ("none", "inherit")):
            bad[sid + ".resources"] = r
    b.call("style.set", id="ds-classic")
    return res(not bad, bad, "5종 효과 = 프리셋 표, 리소스(PdStyleShadow·PdStyleDecor) 유무 일치")


def classic_identical(b, ctx):
    path = ctx.get("classic_baseline")
    if not path:
        raise Blocked("--classic-baseline 필요(직전 theme3 동결판 ds-classic 이전 계산값 표)")
    base = json.load(open(path, encoding="utf-8"))  # {요소:{computed 키:값}}, 선택 "_themeId": 기준표를 뜬 색 테마
    # 글자색 등 계산값은 색 테마 토큰(PdBrushText)을 따르므로 기준표와 같은 색 테마에서 비교해야 한다
    # (2026-11-16 K8: user-legacy01(글자 #1B1B1B)에서 뜬 표를 pd-light(#000000)에서 비교해 Foreground 만 달랐다)
    prev_theme = (b.call("settings.get") or {}).get("themeId")
    if base.get("_themeId"):
        b.call("theme.select", id=base["_themeId"])
    select(b, "ds-classic")
    diff = {}
    for el, vals in base.items():
        if el.startswith("_"):
            continue
        g = b.call("themedesign.get", el=el)
        c = (g or {}).get("computed") or {}
        for k, v in vals.items():
            if str(c.get(k)) != str(v):
                diff["%s.%s" % (el, k)] = [c.get(k), v]
    if base.get("_themeId") and prev_theme:
        b.call("theme.select", id=prev_theme)  # 다음 케이스에 색 테마를 남기지 않는다
    return res(not diff, diff, {})


def bg(b):
    g = b.call("themedesign.get", el="app")
    if not isinstance(g, dict) or "Background" not in (g.get("computed") or {}):
        raise Blocked("themedesign.get el=app computed.Background 없음")
    return str(g["computed"]["Background"]).upper()


def orthogonal_color(b, ctx):
    reset_all(b)
    b.call("theme.select", id="pd-dark")
    base = bg(b)
    seen = {}
    for sid in ("ds-cute", "ds-depth", "ds-classic"):
        s = select(b, sid)
        seen[sid] = [(b.call("settings.get") or {}).get("themeId"), bg(b)]
    ok = all(v == ["pd-dark", base] for v in seen.values())
    b.call("theme.select", id="pd-light")  # 반대 방향 — 색을 바꿔도 디자인 테마 유지
    s = state(b)
    ok = ok and s.get("id") == "ds-classic" and bg(b) != base
    select(b, "ds-cute")
    b.call("theme.select", id="pd-ocean")
    kept = state(b).get("id") == "ds-cute"
    b.call("theme.select", id="pd-light")
    reset_all(b)
    return res(ok and kept, {"seen": seen, "keep_after_color_change": kept}, "디자인 테마를 바꿔도 색 불변, 색을 바꿔도 디자인 테마 유지")


def user_override_wins(b, ctx):
    el = ctx.get("probe_el", "button")
    reset_all(b)
    select(b, "ds-cute")
    under = (b.call("themedesign.get", el=el) or {}).get("computed", {}).get("CornerRadius")
    if b.call("themedesign.set", el=el, prop="cornerRadius", value="7") is None:
        raise Blocked("themedesign.set 실패")
    over = b.call("themedesign.get", el=el)["computed"]["CornerRadius"]
    b.call("themedesign.reset", el=el, prop="cornerRadius")
    back = b.call("themedesign.get", el=el)["computed"]["CornerRadius"]
    reset_all(b)
    ok = str(over).split(",")[0].strip() in ("7", "7.0") and str(back) == str(under) and str(over) != str(under)
    return res(ok, {"디자인 테마 값": under, "사용자 덮어쓰기": over, "해제 뒤": back}, "사용자 값(7)이 디자인 테마 값을 이기고 해제하면 디자인 테마 값으로 복귀")


def company_lock(b, ctx):
    reset_all(b)
    select(b, "ds-simple")
    # 회사 잠금은 회사 값만 쓴다(18 §4) — 잠근 디자인 테마 ID(회사 값 theme.style.id)를 함께 준다. 없으면 회사 값이 없어 기본 ds-classic 이 된다
    # (2026-11-16 K8: 잠금만 켜서 ds-classic 이 됨. settings.set 도움말 형식 theme.style.locked=true theme.style.id=)
    b.call("settings.set", **{"theme.style.locked": "true", "theme.style.id": "ds-simple"})
    b.call("style.set", id="ds-rich")
    s = state(b)
    kept = s.get("id") == "ds-simple" and bool(s.get("locked"))
    b.call("style.effect.set", key="shadow", value="float")
    kept = kept and state(b)["effective"].get("shadow") == "none"
    set_setting(b, "theme.style.locked", "false")
    after = select(b, "ds-rich").get("id")
    reset_all(b)
    return res(kept and after == "ds-rich", {"잠금 중 id": s.get("id"), "locked": s.get("locked"), "해제 뒤 id": after}, "잠금 중 선택·덮어쓰기 무시, 해제하면 선택 가능")


def effect_override(b, ctx):
    reset_all(b)
    select(b, "ds-classic")
    b.call("style.effect.set", key="shadow", value="float")
    s = state(b)
    ok = s["effective"].get("shadow") == "float" and not res_off((s.get("resources") or {}).get("PdStyleShadow"))
    b.call("style.effect.set", key="shadow", value="zzz-허용밖")
    ign = state(b)["effective"].get("shadow") == "float"  # 허용 목록 밖 값은 무시(저장값 보존)
    b.call("style.effect.set", key="shadow", value="")
    back = state(b)["effective"].get("shadow") == "inherit"
    reset_all(b)
    return res(ok and ign and back, {"덮어쓰기": ok, "허용밖 무시": ign, "해제 복귀": back}, "효과 덮어쓰기 반영·허용 밖 무시·해제 시 디자인 테마 값")


def restart_persist(b, ctx):
    reset_all(b)
    select(b, "ds-cute")
    b.call("style.effect.set", key="decor", value="waves")
    ctx["backend"].restart()
    s = state(ctx["backend"].b)
    ok = s.get("id") == "ds-cute" and s["effective"].get("decor") == "waves"
    reset_all(ctx["backend"].b)
    return res(ok, {"id": s.get("id"), "decor": s["effective"].get("decor")}, {"id": "ds-cute", "decor": "waves"})


def legacy_profile(b, ctx):
    fx = ctx.get("legacy_fixture")
    if not fx:
        raise Blocked("--legacy-fixture(dtheme_legacy_settings.json) 필요 — 이 저장본으로 기동한 앱에 연결해 호출")
    fixture = json.load(open(fx, encoding="utf-8"))
    exp_theme = fixture.get("ThemeId") or fixture.get("themeId")
    s = state(b)
    st = b.call("settings.get") or {}
    ok = s.get("id") == "ds-classic" and not (s.get("overrides") or {}) and st.get("themeId") == exp_theme
    return res(ok, {"id": s.get("id"), "overrides": s.get("overrides"), "themeId": st.get("themeId")}, {"id": "ds-classic", "overrides": {}, "themeId": exp_theme})


def depth_tilt_transition(b, ctx):
    reset_all(b)
    s = select(b, "ds-depth")
    need_no_fallback(s)
    p = b.call("depth.probe", kind="open")
    if not isinstance(p, dict) or "samples" not in p:
        raise Blocked("depth.probe 응답 없음: %r" % (p,))
    tilted = any(abs(float(x.get("angleY", 0))) > 0 for x in p["samples"])
    ok = tilted and p.get("restored") is True
    reset_all(b)
    return res(ok, {"표본 수": len(p["samples"]), "기울기 관측": tilted, "끝 뒤 복원": p.get("restored")}, "전환 중 AngleY≠0, 끝 뒤 RenderTransform 이 전 값과 같음")


def fallback_software(b, ctx):
    reset_all(b)
    select(b, "ds-depth")
    b.call("gpu.simulateSoftware", on=True)
    s = state(b)
    e = s["effective"]
    low = bool(s.get("fallbackReason")) and e.get("depth.open") == "none" and e.get("depth.overview") == "flat" and e.get("depth.page") == "inherit" and e.get("motion") in ("none", "inherit")
    b.call("gpu.simulateSoftware", on=False)
    back = state(b)["effective"].get("depth.open") == "tilt"
    reset_all(b)
    return res(low and back, {"사유": s.get("fallbackReason"), "낮춘 실효값": {k: e.get(k) for k in ("depth.open", "depth.overview", "depth.page", "motion")}, "해제 뒤 복원": back}, "소프트웨어 렌더면 depth.* → none/flat/inherit·motion → none, 해제하면 복원")


def reduce_motion(b, ctx):
    reset_all(b)
    select(b, "ds-depth")
    set_setting(b, "theme.motion.level", "reduced")
    e = state(b)["effective"]
    ok = e.get("depth.open") == "none" and e.get("depth.overview") == "flat" and e.get("motion") in ("subtle", "none", "inherit")
    set_setting(b, "theme.motion.level", "off")
    e2 = state(b)["effective"]
    ok = ok and e2.get("motion") in ("none", "inherit") and e2.get("depth.open") == "none"
    set_setting(b, "theme.motion.level", "full")
    back = state(b)["effective"].get("depth.open") == "tilt"
    reset_all(b)
    return res(ok and back, {"reduced": {k: e.get(k) for k in ("depth.open", "depth.overview", "motion")}, "off.motion": e2.get("motion"), "full 복원": back}, "reduced/off 에서 depth 끔·motion 낮춤, full 이면 복원")


def contrast_ok(b, ctx):
    bad = {}
    for sid in ("ds-rich", "ds-depth"):
        s = select(b, sid)
        c = b.call("themedesign.contrast", el="app")
        if not isinstance(c, dict):
            raise Blocked("themedesign.contrast 응답 없음")
        g = s.get("glassOpacity")
        if c.get("warn") or float(c.get("ratio", 0)) < float(c.get("min", 4.5)) or (sid == "ds-rich" and float(g or 0) < 0.85):
            bad[sid] = {"contrast": c, "glassOpacity": g}
    reset_all(b)
    return res(not bad, bad, "surface=gradient/glass 글자 대비 ≥ 기준, glass 불투명도 ≥ 0.85")


def suggest_color(b, ctx):
    got = {sid: (select(b, sid).get("suggestedColor") or "") for sid in SUGGEST}
    before = (b.call("settings.get") or {}).get("themeId")
    reset_all(b)
    return res(got == SUGGEST and before is not None, got, SUGGEST)


from design_theme_cases_view import *  # noqa: E402,F401,F403  전체 보기·페이지 전환 케이스(300줄 규칙으로 분리)
