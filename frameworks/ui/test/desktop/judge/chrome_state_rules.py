# 크롬 버튼 상태 대비 순수 규칙 — 테마 색 사전으로 강조 띠·표면색·메인 타이틀바 위 기능·캡션 버튼과 새로고침 적중률 색의 대비를 계산한다
import importlib.util
import math
import os

HERE = os.path.dirname(os.path.abspath(__file__))
spec = importlib.util.spec_from_file_location("titlebar_contrast", os.path.join(HERE, "main_titlebar_contrast.py"))
tc = importlib.util.module_from_spec(spec)
spec.loader.exec_module(tc)

MIN_STATE, MIN_INACTIVE_ON = 3.0, 2.0
CLOSE_HOVER, CLOSE_PRESSED, CLOSE_PRESSED_GLYPH = "#C42B1C", "#E6C42B1C", "#B3FFFFFF"  # Themes/ChromeResources.axaml 고정값


def over(fg, bg):
    """#AARRGGBB 를 바탕 RGB 위에 합성한다."""
    c = tc.parse_color(fg)
    return tc.blend(c[1:], bg, c[0] / 255.0)


def floor2(x):
    return math.floor(x * 100) / 100


def ratio(a, b):
    return floor2(tc.contrast(a, b))


MIN_GLYPH_DIST, TOGGLE_OFF_OPACITY = 60, 0.85  # 켜짐·꺼짐 기호 RGB 거리 하한(초기 제시값)·꺼진 토글 기호 불투명도(Themes/ChromeResources.axaml PdChromeToggleOffOpacity)


def on_glyph_vs_off_glyph(off_glyph, on_glyph):
    """켜짐 구분은 기호 색으로 한다(2026-11-11 사용자 지시). 켜짐 기호와 꺼짐 기호의 RGB 거리(|dR|+|dG|+|dB|, 0~765)를 잰다. 같은 색이면 0 이다.
    색상이 다른 두 기호(흰색 대 노랑)는 밝기 대비가 작아도 구분되므로 대비율이 아니라 거리로 본다. 켜짐 기호가 바탕에서 보이는지는 on_glyph_vs_bar 가 따로 본다."""
    off = tc.rgb_of(off_glyph) if isinstance(off_glyph, str) else off_glyph
    on = tc.rgb_of(on_glyph) if isinstance(on_glyph, str) else on_glyph
    return float(sum(abs(a - b) for a, b in zip(on, off)))


def inactive_hover_ratio(colors):
    """비활성 창 호버 바탕(PdBrushChromeHoverInactive, 키가 없으면 활성 호버 값 = FillMissing 과 같은 대체) 대 비활성 제목줄 바탕 대비."""
    bar = tc.rgb_of(colors["PdBrushMdiTitleBarInactive"])
    hover = colors.get("PdBrushChromeHoverInactive") or colors["PdBrushMdiTitleButtonHover"]
    # 기본(Opacity 방식)은 PdCaptionStrip 전체가 INACTIVE_OPACITY 로 흐려져 호버 채움도 그만큼 옅어진다(실화면 실측 1.2~2.07 이 불투명 계산 3.2~3.96 과 어긋난 원인, 2026-11-12)
    return ratio(tc.blend(over(hover, bar), bar, tc.INACTIVE_OPACITY), bar)


def surface_row(bar, glyph, hover, dim=1.0, on_glyph_color=None, checked_color=None):
    """바탕 하나의 상태 값. dim 은 비활성 요소 투명도다(묶음 투명도라 켜짐 바탕·그림이 각각 바탕 위에 합성된다). 켜짐·누름 그림은 활성 제목줄 색(PdChromeStateOnGlyph)이다."""
    b, g = tc.rgb_of(bar), tc.rgb_of(glyph)
    rest_glyph, on_fill = tc.blend(g, b, dim), tc.blend(g, b, dim)
    hover_fill = tc.blend(over(hover, b), b, dim)
    on_glyph = tc.blend(tc.rgb_of(on_glyph_color or bar), b, dim)
    checked = {}
    if checked_color:  # 실제 켜짐 표현(PdChromeGlyphChecked 색 + 꺼짐은 PdChromeToggleOffOpacity): 켜짐·꺼짐 기호 색 거리와 켜짐 기호 대 바탕 대비
        on_g, off_g = tc.blend(tc.rgb_of(checked_color), b, dim), tc.blend(tc.blend(g, b, TOGGLE_OFF_OPACITY), b, dim)
        checked = {"on_glyph_vs_off_glyph": on_glyph_vs_off_glyph(off_g, on_g), "on_glyph_vs_bar": ratio(on_g, b)}
    return {
        **checked,
        "rest_hover": max(ratio(hover_fill, b), ratio(rest_glyph, b)), "rest_on": ratio(on_fill, b), "on_hover_fill": ratio(on_fill, hover_fill),
        "on_onhover_border": ratio(on_glyph, on_fill), "glyph_on_hover": ratio(rest_glyph, hover_fill), "glyph_on_on": ratio(on_glyph, on_fill),
        "hex": {"bar": bar, "glyph": glyph, "hover_fill": "%02X%02X%02X" % hover_fill, "on_fill": "%02X%02X%02X" % on_fill},
    }


def model(colors):
    """창 스타일별 바탕에서 상태 대비를 계산한다. 강조 띠(기본·클래식)와 표면색(강조 끔·macOS) 양쪽을 본다."""
    text, hover, active = colors["PdBrushMdiTitleText"], colors["PdBrushMdiTitleButtonHover"], colors["PdBrushMdiTitleBar"]
    return {
        "active": surface_row(active, text, hover, checked_color=colors.get("PdBrushChromeGlyphCheckedOnAccent")),
        "pinned": surface_row(colors["PdBrushMdiTitleBarPinned"], text, hover, on_glyph_color=active, checked_color=colors.get("PdBrushChromeGlyphCheckedOnAccent")),
        "surface": surface_row(colors["PdBrushSurface"], colors["PdBrushText"], colors["PdBrushCaptionHover"], checked_color=colors.get("PdBrushChromeGlyphCheckedOnSurface")),
        "inactive": surface_row(colors["PdBrushMdiTitleBarInactive"], text, hover, tc.INACTIVE_OPACITY, on_glyph_color=active, checked_color=colors.get("PdBrushChromeGlyphCheckedOnAccent")),
        "main": surface_row(colors["PdBrushMainTitleBar"], colors["PdBrushMainTitleText"], hover) if "PdBrushMainTitleBar" in colors else None,
    }


def model_fails(rows):
    fails = []
    for name in ("active", "pinned", "surface", "main"):
        row = rows.get(name)
        if not row:
            continue
        for key in ("rest_hover", "rest_on", "on_hover_fill", "on_onhover_border", "glyph_on_hover", "glyph_on_on"):
            if name == "main" and key not in ("rest_hover", "glyph_on_hover"):
                continue  # 메인 타이틀바에는 토글이 없다(캡션 3버튼만)
            if row[key] < MIN_STATE:
                fails.append("%s %s %.2f < %.1f" % (name, key, row[key], MIN_STATE))
    if rows["inactive"]["rest_on"] < MIN_INACTIVE_ON:
        fails.append("inactive rest_on %.2f < %.1f" % (rows["inactive"]["rest_on"], MIN_INACTIVE_ON))
    for name in ("active", "pinned", "surface", "inactive"):
        row = rows[name]
        if "on_glyph_vs_off_glyph" not in row:
            continue  # 켜짐 기호 토큰이 없는 사전은 건너뛴다
        if row["on_glyph_vs_off_glyph"] < MIN_GLYPH_DIST:
            fails.append("%s on_glyph_vs_off_glyph %.0f < %d" % (name, row["on_glyph_vs_off_glyph"], MIN_GLYPH_DIST))
        need = MIN_STATE
        if name != "inactive" and row["on_glyph_vs_bar"] < need:  # 비활성 바탕 위 켜짐 기호 대비는 기준이 정해지지 않아 값만 기록한다(보고 대상)
            fails.append("%s on_glyph_vs_bar %.2f < %.1f" % (name, row["on_glyph_vs_bar"], need))
    return fails


def inactive_hover_fails(colors):
    value = inactive_hover_ratio(colors)
    return ["inactive_hover %.2f < %.1f" % (value, MIN_STATE)] if value < MIN_STATE else []


def rules_self_test():
    """on_glyph_vs_off_glyph·inactive_hover_ratio 순수 규칙 시험(진양성 포함)."""
    assert on_glyph_vs_off_glyph("#FFFFFF", "#FFE066") >= MIN_GLYPH_DIST, "흰 꺼짐 기호 대 노랑 켜짐 기호는 구분돼야 한다"
    assert on_glyph_vs_off_glyph("#FFFFFF", "#F8F8F8") < MIN_GLYPH_DIST, "진양성: 켜짐·꺼짐 기호 색이 거의 같으면 잡아야 한다"
    assert on_glyph_vs_off_glyph((255, 255, 255), (255, 255, 255)) == 0.0
    rows = model({"PdBrushMdiTitleText": "#FFFFFF", "PdBrushMdiTitleButtonHover": "#33FFFFFF", "PdBrushMdiTitleBar": "#40568D", "PdBrushMdiTitleBarPinned": "#800000", "PdBrushSurface": "#FFFFFF", "PdBrushText": "#000000",
                  "PdBrushCaptionHover": "#1A000000", "PdBrushMdiTitleBarInactive": "#808080", "PdBrushChromeGlyphCheckedOnAccent": "#FFE066", "PdBrushChromeGlyphCheckedOnSurface": "#B00020"})
    assert rows["active"]["on_glyph_vs_off_glyph"] >= MIN_GLYPH_DIST and rows["active"]["on_glyph_vs_bar"] >= MIN_STATE, rows["active"]
    same = model({"PdBrushMdiTitleText": "#FFFFFF", "PdBrushMdiTitleButtonHover": "#33FFFFFF", "PdBrushMdiTitleBar": "#40568D", "PdBrushMdiTitleBarPinned": "#800000", "PdBrushSurface": "#FFFFFF", "PdBrushText": "#000000",
                  "PdBrushCaptionHover": "#1A000000", "PdBrushMdiTitleBarInactive": "#808080", "PdBrushChromeGlyphCheckedOnAccent": "#FAFAFA", "PdBrushChromeGlyphCheckedOnSurface": "#B00020"})
    assert any("on_glyph_vs_off_glyph" in f for f in model_fails(same)), "진양성: 켜짐 기호가 꺼짐 기호와 같은 색이면 모델이 잡아야 한다"
    base = {"PdBrushMdiTitleBarInactive": "#A0A0A0", "PdBrushMdiTitleButtonHover": "#33FFFFFF"}
    assert inactive_hover_fails(base), "진양성: 토큰 없이 옅은 활성 호버를 쓰면 회색 비활성 바탕에서 3:1 미달이어야 한다"
    assert not inactive_hover_fails(dict(base, PdBrushChromeHoverInactive="#B3000000")), "충분히 진한 전용 토큰은 통과해야 한다"
    assert inactive_hover_ratio(base) == inactive_hover_ratio(dict(base, PdBrushChromeHoverInactive="#33FFFFFF")), "키 없음 = 활성 호버로 대체"
    return True


def cache_row(colors):
    """새로고침 아이콘 적중률 색(0%·낮음·높음·100%) 대 활성 제목줄 대비. ChromeButtons.CacheBrush 는 이 네 토큰과 낮음~높음 사이 혼합만 쓴다."""
    bar = tc.rgb_of(colors["PdBrushMdiTitleBar"])
    return {name: ratio(tc.rgb_of(colors[key]), bar) for name, key in (("zero", "PdBrushChromeGlyph"), ("low", "PdBrushCacheLow"), ("high", "PdBrushCacheHigh"), ("full", "PdBrushCacheFull"))}


def cache_surface_row(colors):
    """표면색 제목줄(강조 끔·macOS 스타일) 위 적중률 색. 표면용 토큰이 없는 판본은 강조 띠용 토큰을 그대로 쓰므로 그 값으로 잰다."""
    surface, inactive = tc.rgb_of(colors["PdBrushSurface"]), tc.rgb_of(colors["PdBrushBackground"])
    row = {}
    for name, new, old in (("zero", "PdBrushCacheZeroOnSurface", "PdBrushChromeGlyph"), ("low", "PdBrushCacheLowOnSurface", "PdBrushCacheLow"),
                           ("high", "PdBrushCacheHighOnSurface", "PdBrushCacheHigh"), ("full", "PdBrushCacheFullOnSurface", "PdBrushCacheFull")):
        c = tc.rgb_of(colors.get(new) or colors[old])
        row[name] = ratio(c, surface)
        row[name + "_inactive"] = ratio(tc.blend(c, inactive, tc.INACTIVE_OPACITY), inactive)
    return row


def caption_row(colors, bar, glyph, hover):
    """캡션 3버튼. 최소·최대 호버 = 옅은 채움+그림색 테두리, 누름 = 그림색 채움+바탕색 그림, 닫기 호버 = 빨강 채움+그림색 테두리+밝은 기호, 닫기 누름 = 진한 빨강."""
    b, g = tc.rgb_of(bar), tc.rgb_of(glyph)
    fill, red, red_press = over(hover, b), tc.rgb_of(CLOSE_HOVER), over(CLOSE_PRESSED, b)
    text = tc.rgb_of(colors.get("PdBrushMainTitleText") or "#FFFFFF")
    return {
        "hover_border": ratio(g, b), "glyph_on_hover": ratio(g, fill), "pressed_fill": ratio(g, b), "glyph_on_pressed": ratio(b, g),
        "close_hover_border": ratio(g, b), "close_glyph": ratio(text, red), "close_pressed_glyph": ratio(over(CLOSE_PRESSED_GLYPH, red_press), red_press),
    }


def extended_rows(colors):
    """표면색 제목줄과 캡션 버튼까지 넓힌 판정 값."""
    return {
        "cache_surface": cache_surface_row(colors),
        "caption_accent": caption_row(colors, colors["PdBrushMdiTitleBar"], colors["PdBrushMdiTitleText"], colors["PdBrushMdiTitleButtonHover"]),
        "caption_surface": caption_row(colors, colors["PdBrushSurface"], colors["PdBrushText"], colors["PdBrushCaptionHover"]),
        "caption_main": caption_row(colors, colors["PdBrushMainTitleBar"], colors["PdBrushMainTitleText"], colors["PdBrushMdiTitleButtonHover"]) if "PdBrushMainTitleBar" in colors else None,
        "surface_inactive": surface_row(colors["PdBrushBackground"], colors["PdBrushText"], colors["PdBrushCaptionHover"], tc.INACTIVE_OPACITY, on_glyph_color=colors["PdBrushSurface"]),
    }


def extended_fails(colors):
    rows, fails = extended_rows(colors), []
    for key, value in rows["cache_surface"].items():
        need = MIN_INACTIVE_ON if key.endswith("_inactive") else MIN_STATE
        if value < need:
            fails.append("cache_surface %s %.2f < %.1f" % (key, value, need))
    for name in ("caption_accent", "caption_surface", "caption_main"):
        for key, value in (rows[name] or {}).items():
            if value < MIN_STATE:
                fails.append("%s %s %.2f < %.1f" % (name, key, value, MIN_STATE))
    if rows["surface_inactive"]["rest_on"] < MIN_INACTIVE_ON:
        fails.append("surface_inactive rest_on %.2f < %.1f" % (rows["surface_inactive"]["rest_on"], MIN_INACTIVE_ON))
    return fails


def cache_fails(colors):
    return ["cache %s %.2f < %.1f" % (name, value, MIN_STATE) for name, value in cache_row(colors).items() if value < MIN_STATE] + extended_fails(colors)


def old_weakness(colors):
    """수정 전 표현(바탕 없이 그림 색만)의 대비. 약함의 근거로 기록한다."""
    rest = tc.rgb_of(colors["PdBrushMdiTitleText"])
    return {"hover_vs_rest_glyph": ratio(tc.rgb_of(colors["PdBrushChromeGlyphHoverOnAccent"]), rest), "on_vs_rest_glyph": ratio(tc.rgb_of(colors["PdBrushChromeGlyphCheckedOnAccent"]), rest)}


if __name__ == "__main__":
    import sys
    if "--self-test" in sys.argv:
        rules_self_test()
        print("chrome_state_rules self-test ok")
