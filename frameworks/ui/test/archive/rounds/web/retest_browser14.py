# 웹 두 손가락 핀치 확대/축소 시험 — hasTouch 컨텍스트에서 CDP Input.dispatchTouchEvent 로 벌리기·좁히기를 보내 Ctrl+휠과 같은 두 모드(글자 크기·창 배율) 결과를 원격 조회로 판정한다
# 사용: PD_URL=http://127.0.0.1:5189/ python3 tools/retest_browser14.py [--probe]   (헤드리스 전용. --probe = 이벤트가 앱 입력 요소에 닿는지만 보는 짧은 확인)
"""
기준: logs/odev7c/pinch_zoom_draft.md(odev-7c 설계, lead 10:4x 확정)
  누적 비율 s 를 단계로: n = trunc(ln s ÷ ln 1.10), 단계마다 Ctrl+휠 1칸과 같은 함수(HandleCtrlInput ±1).
  모드 ① FontSize : 기준 글자 크기 ±1pt, 범위 10~24.   모드 ② WindowZoom : 대상 문서 zoom ±0.1, 범위 0.5~2.0.
  대상은 제스처 시작 중점 아래 visual — 문서 창 위가 아니면 무반응(IsInDocument).
항목(모드마다):
  P0 도달: 문서 캡처에서 pointerType=touch 이벤트·touchstart 가 Avalonia 입력 요소(canvas/native-host)에 도착
  P1 벌리기 120→150px(s=1.25 → +2단계): 글자 +2 / zoom +0.2
  P2 좁히기 150→120px(s=0.8 → −2단계): P1 전 값으로 복귀
  P3 페이지 자체 확대 없음: #out computed touch-action=none, 앱 pointercancel(touch) 0, visualViewport.scale=1 (canvas·native-host 값은 참고 — touch-action 은 상속 안 됨, odev-3)
  P4 (② 만) 다른 문서 zoom 불변
  P5 문서 창 밖(탐색기 패널) 핀치는 무반응
  P6 범위 끝: 60→480px(s=8 → +21단계) 뒤 글자 24 / zoom 2.0, 480→60px(−21단계) 뒤 글자 10 / zoom 0.5
"""
import json
import os
import sys
import time

from playwright.sync_api import sync_playwright

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import retest_browser2 as t  # noqa: E402

OUT = t.OUT
PROBE = "--probe" in sys.argv
res = []

LISTEN_JS = """() => {
  // 문서 전체에서 캡처로 받고 실제 대상 요소(태그.클래스#id)를 센다 — Avalonia 는 canvas 위 입력 요소(native-host)에서 받는다
  const desc = el => el ? (el.tagName + (el.className && typeof el.className === 'string' ? '.' + el.className.split(' ')[0] : '') + (el.id ? '#' + el.id : '')) : 'null';
  window.__pdTouch = {pointer: {}, touch: {}, targets: {}};
  for (const ev of ['pointerdown', 'pointermove', 'pointerup', 'pointercancel']) {
    document.addEventListener(ev, e => { const k = ev + ':' + e.pointerType; window.__pdTouch.pointer[k] = (window.__pdTouch.pointer[k] || 0) + 1;
      const d = desc(e.target); window.__pdTouch.targets[d] = (window.__pdTouch.targets[d] || 0) + 1; }, {capture: true, passive: true});
  }
  for (const ev of ['touchstart', 'touchmove', 'touchend']) {
    document.addEventListener(ev, e => { window.__pdTouch.touch[ev] = (window.__pdTouch.touch[ev] || 0) + 1; }, {capture: true, passive: true});
  }
  return !!document.querySelector('canvas.avalonia-canvas');
}"""

STYLE_JS = """([x, y]) => { const e = document.elementFromPoint(x, y); const c = document.querySelector('canvas.avalonia-canvas');
  const h = document.querySelector('.avalonia-native-host');
  const ta = el => el ? getComputedStyle(el).touchAction : null;
  // touch-action 은 상속되지 않고 브라우저가 대상~조상 값을 교집합한다(odev-3, MDN) — 조상 사슬 값을 모두 기록
  const chain = []; for (let x = e; x; x = x.parentElement) chain.push((x.id ? '#' + x.id : x.tagName) + '=' + ta(x));
  return {top: e ? e.tagName + '.' + (e.className || '') : null, outTouchAction: ta(document.getElementById('out')),
          canvasTouchAction: ta(c), hostTouchAction: ta(h), chain: chain,
          visualScale: window.visualViewport ? window.visualViewport.scale : null, maxTouchPoints: navigator.maxTouchPoints}; }"""


def rec(item, expect, actual, verdict):
    res.append({"item": item, "expect": expect, "actual": actual, "verdict": verdict})
    print("%-6s %s | %s" % (verdict, item, str(actual)[:500]), flush=True)


def state(r, docs):
    s = r.call("settings.get") or {}
    z = {d.get("id"): d.get("zoom") for d in (r.call("docs.list") or [])}
    return {"font": s.get("baseFontSize"), "mode": s.get("wheelMode"), "zoom": {k: z.get(k) for k in docs}}


def pinch(cdp, cx, cy, d0, d1, steps=10):
    """가로 두 손가락: 간격 d0 → d1 (CSS px)."""
    def pts(d):
        return [{"x": cx - d / 2, "y": cy, "id": 0, "radiusX": 5, "radiusY": 5, "force": 1},
                {"x": cx + d / 2, "y": cy, "id": 1, "radiusX": 5, "radiusY": 5, "force": 1}]
    cdp.send("Input.dispatchTouchEvent", {"type": "touchStart", "touchPoints": pts(d0)})
    for i in range(1, steps + 1):
        cdp.send("Input.dispatchTouchEvent", {"type": "touchMove", "touchPoints": pts(d0 + (d1 - d0) * i / steps)})
        time.sleep(0.03)
    cdp.send("Input.dispatchTouchEvent", {"type": "touchEnd", "touchPoints": []})
    time.sleep(0.8)


def near(a, b, tol=0.001):
    return a is not None and b is not None and abs(a - b) <= tol


def run_mode(r, page, cdp, mode, g, f, center, outside):
    cx, cy = center
    r.call("settings.set", wheelMode=mode, baseFontSize=14)
    r.call("docs.zoom", doc=g, step="reset")
    r.call("docs.zoom", doc=f, step="reset")
    r.call("docs.activate", doc=g)
    time.sleep(0.6)
    key = "font" if mode == "FontSize" else "zoom"

    def val(s):
        return s["font"] if key == "font" else s["zoom"].get(g)
    unit = 1 if key == "font" else 0.1
    s0 = state(r, [g, f])
    pinch(cdp, cx, cy, 120, 150)
    s1 = state(r, [g, f])
    rec("[%s] P1 벌리기 120→150px(+2단계)" % mode, "%s +%s" % (key, 2 * unit), {"before": s0, "after": s1},
        "PASS" if val(s0) is not None and near(val(s1), val(s0) + 2 * unit) else "FAIL")
    pinch(cdp, cx, cy, 150, 120)
    s2 = state(r, [g, f])
    rec("[%s] P2 좁히기 150→120px(−2단계)" % mode, "%s 가 P1 전 값으로" % key, {"before": s1, "after": s2},
        "PASS" if near(val(s2), val(s0)) else "FAIL")
    if mode == "WindowZoom":
        rec("[%s] P4 다른 문서 zoom 불변" % mode, "폼 문서 zoom 그대로", {"before": s0["zoom"].get(f), "afterP1": s1["zoom"].get(f), "afterP2": s2["zoom"].get(f)},
            "PASS" if s0["zoom"].get(f) == s1["zoom"].get(f) == s2["zoom"].get(f) else "FAIL")
    pinch(cdp, outside[0], outside[1], 120, 150)
    s5 = state(r, [g, f])
    rec("[%s] P5 문서 창 밖(탐색기 패널) 핀치 무반응" % mode, "글자·모든 문서 zoom 그대로", {"at": outside, "before": s2, "after": s5},
        "PASS" if s5["font"] == s2["font"] and s5["zoom"] == s2["zoom"] else "FAIL")
    pinch(cdp, cx, cy, 60, 480, steps=14)
    s6a = state(r, [g, f])
    pinch(cdp, cx, cy, 480, 60, steps=14)
    s6b = state(r, [g, f])
    hi, lo = (24, 10) if key == "font" else (2.0, 0.5)
    rec("[%s] P6 범위 끝(+21단계 → %s, −21단계 → %s)" % (mode, hi, lo), "상한·하한에서 멈춤", {"afterSpread": s6a, "afterSqueeze": s6b},
        "PASS" if near(val(s6a), hi) and near(val(s6b), lo) else "FAIL")


def main():
    with sync_playwright() as p:
        b = p.chromium.launch(headless=True, args=t.ARGS)
        ctx = t.allow_dup(b.new_context(viewport={"width": 1280, "height": 800}, has_touch=True))
        ctx, page, r = t.boot(b, "?layout-key=otestw2-pinch", ctx=ctx)
        orig = r.call("settings.get") or {}
        r.call("docs.close", all=True)
        g = r.call("docs.open", kind="grid", title="PZ-grid", allowDuplicate=True)[0]["id"]
        f = r.call("docs.open", kind="form", title="PZ-form", allowDuplicate=True)[0]["id"]
        time.sleep(1.0)
        r.call("layout.template.apply", id="tpl.tabbed")
        r.call("docs.activate", doc=g)
        time.sleep(1.0)
        has_canvas = page.evaluate(LISTEN_JS)
        bb = (r.call("ui.find", id="grid.dgMain", doc=g) or {}).get("bounds") or {"x": 400, "y": 300, "w": 400, "h": 300}
        center = (bb["x"] + bb["w"] / 2, bb["y"] + bb["h"] / 2)
        ex = (r.call("ui.find", dock="Explorer") or {}).get("bounds")
        outside = (ex["x"] + ex["w"] / 2, ex["y"] + ex["h"] / 2) if ex else (90, 450)  # 탐색기 패널(문서 창 밖) — 못 찾으면 왼쪽 패널 자리
        cdp = ctx.new_cdp_session(page)
        st0 = page.evaluate(STYLE_JS, list(center))
        s_before = state(r, [g, f])
        pinch(cdp, center[0], center[1], 120, 150)
        ev = page.evaluate("() => window.__pdTouch")
        tgts = (ev or {}).get("targets", {})
        on_app = any(k.startswith("CANVAS") or "avalonia" in k.lower() for k in tgts)
        reached = has_canvas and on_app and any(k.endswith(":touch") for k in (ev or {}).get("pointer", {})) and (ev or {}).get("touch", {}).get("touchstart", 0) > 0
        rec("P0 터치 이벤트가 앱 입력 요소에 도달", "pointerType=touch·touchstart 수신, 대상이 avalonia canvas/native-host", {"events": ev, "center": center}, "PASS" if reached else "FAIL")
        st1 = page.evaluate(STYLE_JS, list(center))
        page.screenshot(path=os.path.join(OUT, "pinch_spread.png"))
        # P3(odev-3 기준 조정): ① #out computed touch-action=none ② 앱 쪽 pointercancel(touch) 없음 ③ visualViewport.scale=1. canvas·native-host 값은 참고 기록.
        ta_ok = st1.get("outTouchAction") == "none"
        cancels = (ev or {}).get("pointer", {}).get("pointercancel:touch", 0)
        pst = r.call("pinch.state")  # odev-7c 진단(있는 판만) — 앱 쪽 제스처·단계 카운터
        rec("P3 페이지 자체 확대 없음(#out touch-action:none·pointercancel 없음·scale 1)", "#out touch-action=none, pointercancel:touch=0, visualViewport.scale=1(canvas·native-host 값은 참고)",
            {"before": st0, "after": st1, "pointercancel_touch": cancels, "pinch.state": pst},
            "PASS" if ta_ok and cancels == 0 and st1.get("visualScale") in (1, 1.0, None) else ("FAIL(#out touch-action 이 none 아님)" if not ta_ok else "FAIL"))
        if PROBE:
            rec("참고(probe): 첫 벌리기 전후 상태", "기록", {"before": s_before, "after": state(r, [g, f])}, "기록")
        else:
            for mode in ("FontSize", "WindowZoom"):
                run_mode(r, page, cdp, mode, g, f, center, outside)
            page.screenshot(path=os.path.join(OUT, "pinch_end.png"))
        # 설정 원복(기준 글자 크기·휠 모드)
        r.call("settings.set", wheelMode=orig.get("wheelMode") or "FontSize", baseFontSize=orig.get("baseFontSize") or 14)
        with open(os.path.join(OUT, "retest_web14.json"), "w", encoding="utf-8") as fo:
            json.dump(res, fo, ensure_ascii=False, indent=2)
        b.close()


if __name__ == "__main__":
    main()
