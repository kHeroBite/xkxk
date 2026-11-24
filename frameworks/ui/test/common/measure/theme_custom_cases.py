# 테마 요소별 사용자 지정(REQ-101) 시험 케이스 정의 — 원격 계산값만으로 판정하는 케이스 함수 모음
"""
계약(계획서 §3.7·§4.1, 구현 쪽 RemoteThemeDesignCommands 가 지킬 형식):
  themedesign.get el=<요소>  → {themeId, baseTemplateId, isBuiltin, stored{prop:값}, effective{prop:값},
                               computed{FontSize,FontFamily,FontWeight,CornerRadius,BorderThickness,Padding,Background,Foreground},
                               templateColors{토큰:#RRGGBB}}   computed 는 셸의 실제 컨트롤에서 읽은 값
  themedesign.set el= prop= value= / themedesign.reset el= [prop=] / themedesign.resetall
  themedesign.contrast el= → {ratio, warn, min}
  기존: theme.select id= · theme.set colors=토큰=#RRGGBB,… · theme.export path= [id=] · theme.import path= · settings.get
판정은 측정값 비교만 한다. 결과를 본 뒤 기준을 바꾸지 않는다(기준은 케이스 안에 고정).
"""
import json
import os
import re

TEMPLATE = "pd-ocean"
CONTRAST_TEMPLATE = "pd-contrast"
BUILTINS = ("pd-light", "pd-dark", "pd-contrast")


class Blocked(Exception):
    """시험 대상이 계약 명령·값을 주지 않아 판정 불가 — PASS 로 치지 않는다."""


def num(v):
    m = re.search(r"-?\d+(?:\.\d+)?", str(v))
    if not m:
        raise Blocked("숫자 아님: %r" % (v,))
    return float(m.group())


def nums(v):
    return [float(x) for x in re.findall(r"-?\d+(?:\.\d+)?", str(v))]


def color(v):
    s = str(v).strip().lstrip("#").upper()
    if len(s) == 8:
        s = s[2:]  # ARGB → RGB
    return "#" + s


def get(b, el):
    g = b.call("themedesign.get", el=el)
    if not isinstance(g, dict) or not isinstance(g.get("computed"), dict):
        raise Blocked("themedesign.get el=%s 응답 없음/형식 불일치: %r" % (el, g))
    return g


def comp(b, el, key):
    c = get(b, el)["computed"]
    if key not in c:
        raise Blocked("computed.%s 없음 (el=%s)" % (key, el))
    return c[key]


def setv(b, el, prop, value):
    r = b.call("themedesign.set", el=el, prop=prop, value=str(value))
    if r is None:
        raise Blocked("themedesign.set 실패 el=%s prop=%s value=%s" % (el, prop, value))


def res(ok, actual, expected, template=None):
    d = {"verdict": "PASS" if ok else "FAIL", "actual": actual, "expected": expected}
    if template:
        d["template"] = template
    return d


def fresh_custom(b, template=TEMPLATE):
    """내장 템플릿을 고르고 전체 되돌린 상태에서 시작(사용자 사본이 이미 선택돼 있으면 그대로 초기화)."""
    b.call("theme.select", id=template)
    b.call("themedesign.resetall")


# ---- 케이스 ----
def template_select(b, ctx):
    b.call("theme.select", id=TEMPLATE)
    g = get(b, "app")
    exp = ctx["builtin_colors"].get(TEMPLATE, {}).get("Background") or (g.get("templateColors") or {}).get("Background")
    if not exp:
        raise Blocked("템플릿 Background 기준값 없음(--builtin-colors 또는 templateColors)")
    a = color(g["computed"]["Background"])
    return res(a == color(exp), a, color(exp), TEMPLATE)


def auto_customize(b, ctx):
    b.call("theme.select", id=TEMPLATE)
    setv(b, "button", "cornerRadius", 6)
    g = get(b, "button")
    # 새 사용자 테마(themeId 는 user-… 형태의 가변 값)가 선택되고 base 가 TEMPLATE 이며 계산값이 6
    new_user = str(g.get("themeId") or "").startswith("user-") and g.get("themeId") != TEMPLATE
    ok = (not g.get("isBuiltin") and g.get("baseTemplateId") == TEMPLATE and new_user
          and num(g["computed"]["CornerRadius"]) == 6)
    ctx["custom_id"] = g.get("themeId")
    b.call("theme.select", id=TEMPLATE)
    st = get(b, "button").get("stored") or {}
    ok = ok and not st  # 내장 Design 은 빈 사전
    b.call("theme.select", id=ctx["custom_id"])
    return res(ok, {"themeId": g.get("themeId"), "base": g.get("baseTemplateId"), "builtinStored": st},
               {"themeId": "user-*(새 사용자 테마)", "base": TEMPLATE, "builtinStored": {}}, TEMPLATE)


def color_override(b, ctx):
    fresh_custom(b)
    setv(b, "button", "cornerRadius", 6)  # 사본 확보
    if b.call("theme.set", colors="Button=#336699") is None:
        raise Blocked("theme.set 실패")
    a = color(comp(b, "button", "Background"))
    return res(a == "#336699", a, "#336699", TEMPLATE)


def _compare(el, prop, value, key, conv, expect, cmpf=lambda a, e: a == e):
    def case(b, ctx):
        fresh_custom(b)
        base = conv(comp(b, el, key)) if conv else None
        setv(b, el, prop, value)
        a = conv(comp(b, el, key)) if conv else comp(b, el, key)
        e = expect(base) if callable(expect) else expect
        return res(cmpf(a, e), a, e, TEMPLATE)
    return case


def pad_h_v(v):
    n = nums(v)
    return (n[0], n[1], n[2], n[3]) if len(n) == 4 else (n[0], n[0], n[0], n[0]) if len(n) == 1 else (n[0], n[1], n[0], n[1])


font_family_override = _compare("menuBar", "fontFamily", "Consolas", "FontFamily", None, "Consolas",
                                lambda a, e: e.lower() in str(a).lower())
font_size_override = _compare("statusBar", "fontSize", 3, "FontSize", num, lambda base: min(24.0, max(10.0, base + 3)))
font_weight_override = _compare("gridHeader", "fontWeight", "Bold", "FontWeight", None, "Bold",
                                lambda a, e: str(a).lower() in ("bold", "700"))
corner_override = _compare("input", "cornerRadius", 8, "CornerRadius", num, 8.0)
border_override = _compare("button", "borderThickness", 2, "BorderThickness", num, 2.0)
padding_override = _compare("gridCell", "padding", "10,6", "Padding", pad_h_v, (10.0, 6.0, 10.0, 6.0))

OVERRIDES = [("menuBar", "FontFamily"), ("statusBar", "FontSize"), ("gridHeader", "FontWeight"),
             ("input", "CornerRadius"), ("button", "BorderThickness"), ("gridCell", "Padding")]


def live_preview(b, ctx):
    fresh_custom(b)
    setv(b, "button", "cornerRadius", 2)
    a1 = num(comp(b, "button", "CornerRadius"))
    setv(b, "button", "cornerRadius", 11)
    a2 = num(comp(b, "button", "CornerRadius"))  # 재시작·재조회 없이 같은 세션 즉시 값
    return res((a1, a2) == (2.0, 11.0), [a1, a2], [2.0, 11.0], TEMPLATE)


def _snapshot(b):
    return {"%s.%s" % k: str(comp(b, k[0], k[1])) for k in OVERRIDES}


def restart_persist(b, ctx):
    fresh_custom(b)
    for el, prop, v in (("menuBar", "fontFamily", "Consolas"), ("statusBar", "fontSize", 3), ("gridHeader", "fontWeight", "Bold"),
                        ("input", "cornerRadius", 8), ("button", "borderThickness", 2), ("gridCell", "padding", "10,6")):
        setv(b, el, prop, v)
    before = _snapshot(b)
    ctx["backend"].restart()
    b = ctx["backend"].b
    after = _snapshot(b)
    return res(before == after, after, before, TEMPLATE)


def reset_prop(b, ctx):
    fresh_custom(b)
    base = num(comp(b, "button", "CornerRadius"))
    setv(b, "button", "cornerRadius", 12)
    setv(b, "button", "borderThickness", 3)
    b.call("themedesign.reset", el="button", prop="cornerRadius")
    a = (num(comp(b, "button", "CornerRadius")), num(comp(b, "button", "BorderThickness")))
    return res(a == (base, 3.0), a, (base, 3.0), TEMPLATE)


def reset_element(b, ctx):
    fresh_custom(b)
    base = (num(comp(b, "button", "CornerRadius")), num(comp(b, "button", "BorderThickness")))
    setv(b, "button", "cornerRadius", 12)
    setv(b, "button", "borderThickness", 3)
    setv(b, "input", "cornerRadius", 9)
    b.call("themedesign.reset", el="button")
    a = (num(comp(b, "button", "CornerRadius")), num(comp(b, "button", "BorderThickness")), num(comp(b, "input", "CornerRadius")))
    return res(a == (base[0], base[1], 9.0), a, (base[0], base[1], 9.0), TEMPLATE)


def reset_all(b, ctx):
    fresh_custom(b)
    g0 = get(b, "button")
    base = (num(g0["computed"]["CornerRadius"]), color(g0["computed"]["Background"]))
    setv(b, "button", "cornerRadius", 12)
    b.call("theme.set", colors="Button=#336699")
    b.call("themedesign.resetall")
    g = get(b, "button")
    a = (num(g["computed"]["CornerRadius"]), color(g["computed"]["Background"]))
    return res(a == base and not g.get("stored"), a, base, TEMPLATE)


def export_import(b, ctx):
    """내보내기→가져오기. 경로 모드(데스크톱·안드로이드)는 교환 폴더 파일, inline_json 모드(웹)는 JSON 문자열을 주고받는다."""
    inline = bool(ctx.get("inline_json"))
    d = ctx.get("exchange_dir")
    if not inline and not d:
        raise Blocked("--exchange-dir(앱이 읽고 쓸 수 있는 폴더) 필요")
    fresh_custom(b)
    setv(b, "input", "cornerRadius", 8)
    setv(b, "button", "borderThickness", 2)
    src = get(b, "input")
    if inline:
        payload = b.call("theme.export", id=src.get("themeId"))
        if isinstance(payload, dict):
            payload = payload.get("json", payload.get("data"))
        if not isinstance(payload, str) or not payload.strip().startswith(("{", "[")):
            raise Blocked("theme.export(경로 없음) 가 JSON 문자열을 주지 않음: %r" % (str(payload)[:80],))
        if b.call("theme.import", **{ctx.get("import_arg", "path"): payload}) is None:
            raise Blocked("theme.import(JSON 문자열) 실패")
    else:
        path = os.path.join(d, "theme_custom_export.json")
        if b.call("theme.export", path=ctx["to_app_path"](path), id=src.get("themeId")) is None:
            raise Blocked("theme.export 실패")
        if b.call("theme.import", path=ctx["to_app_path"](path)) is None:
            raise Blocked("theme.import 실패")
    g = get(b, "input")
    gb = get(b, "button")
    ok = (g.get("themeId") != src.get("themeId") and num(g["computed"]["CornerRadius"]) == 8
          and num(gb["computed"]["BorderThickness"]) == 2 and g.get("effective") == src.get("effective"))
    return res(ok, {"id": g.get("themeId"), "radius": g["computed"]["CornerRadius"]}, {"newTheme": True, "radius": 8}, TEMPLATE)


def contrast_warning(b, ctx):
    b.call("theme.select", id=CONTRAST_TEMPLATE)
    b.call("themedesign.resetall")
    setv(b, "button", "cornerRadius", 4)  # 대비 테마 사본
    ok_base = (get(b, "button").get("baseTemplateId") == CONTRAST_TEMPLATE)
    bg = color(comp(b, "button", "Background"))
    b.call("theme.set", colors="Text=%s" % bg)
    c = b.call("themedesign.contrast", el="button")
    if not isinstance(c, dict) or "warn" not in c:
        raise Blocked("themedesign.contrast 응답 형식 불일치: %r" % (c,))
    ok = ok_base and c["warn"] is True and num(c.get("min", 0)) == 7
    return res(ok, c, {"warn": True, "min": 7}, CONTRAST_TEMPLATE)


def legacy_profile(b, ctx):
    """구판(55f5fda) 저장본으로 기동한 앱에 연결된 상태에서 호출(--legacy 단계). 기준 계산값은 --legacy-baseline 파일."""
    fx, bl = ctx.get("legacy_fixture"), ctx.get("legacy_baseline")
    if not (fx and bl):
        raise Blocked("--legacy-fixture 와 --legacy-baseline 필요(구판 계산값 기록)")
    fixture = json.load(open(fx, encoding="utf-8"))
    base = json.load(open(bl, encoding="utf-8"))  # {el:{FontSize:…,…}}
    exp_id = fixture.get("ThemeId") or fixture.get("themeId")
    s = b.call("settings.get") or {}
    bad = {}
    for el, vals in base.items():
        g = get(b, el)
        if g.get("stored"):
            bad[el + ".stored"] = g["stored"]
        for k, v in vals.items():
            if str(g["computed"].get(k)) != str(v):
                bad["%s.%s" % (el, k)] = [g["computed"].get(k), v]
    ok = s.get("themeId") == exp_id and not bad
    return res(ok, {"themeId": s.get("themeId"), "diff": bad}, {"themeId": exp_id, "diff": {}})


def sparse_save(b, ctx):
    fresh_custom(b)
    setv(b, "button", "cornerRadius", 5)
    setv(b, "statusBar", "fontSize", 2)
    b.call("theme.save")
    g = get(b, "button")
    saved = ctx["read_saved_design"](g.get("themeId"))
    exp = {"button.cornerRadius", "statusBar.fontSize"}
    return res(set(saved) == exp, sorted(saved), sorted(exp), TEMPLATE)


def builtin_unchanged(b, ctx):
    bad = {}
    for tid in BUILTINS:
        b.call("theme.select", id=tid)
        g = get(b, "app")
        exp = ctx["builtin_colors"].get(tid, {}).get("Background") or (g.get("templateColors") or {}).get("Background")
        if not exp:
            raise Blocked("%s 기준 Background 없음" % tid)
        if g.get("stored") or color(g["computed"]["Background"]) != color(exp):
            bad[tid] = [g["computed"]["Background"], exp, g.get("stored")]
    return res(not bad, bad, {})


COMMON = ["template_select", "auto_customize", "color_override", "font_family_override", "font_size_override",
          "font_weight_override", "corner_override", "border_override", "padding_override", "live_preview",
          "restart_persist", "reset_prop", "reset_element", "reset_all", "export_import", "contrast_warning",
          "sparse_save", "builtin_unchanged"]
DESKTOP_ONLY = ["legacy_profile"]
