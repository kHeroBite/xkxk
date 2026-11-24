# 웹 유휴 뒤 다시 그리기 회귀 시험(Avalonia.Browser 유휴 rAF 패치 위험) — 유휴 30초 뒤 클릭·타이핑·SDI 끌기·테마 전환·스크롤·핀치가 바로 화면에 그려지는지 화소 차이로 판정
# 사용: PD_URL=http://127.0.0.1:5198/ python3 tools/retest_browser17.py [--idle 30] [--tag p1121]   (진단판·헤드리스 전용)
"""
판정: 동작 직전 캡처와 동작 0.3초 뒤 캡처를 비교해 그 영역에 바뀐 화소가 있으면 '바로 그려짐'.
  모델 값(원격 조회)도 함께 확인해 '모델은 바뀌었는데 화면은 그대로'(멈춘 화면)를 가린다.
  유휴 구간·각 동작 직후 rAF 호출 수도 기록한다(패치판은 유휴 중 0 근처, 동작 뒤 다시 올라가야 함).
항목:
  R1 클릭: 그리드 행 클릭 → 선택 표시
  R2 타이핑: SDI 폼 창 제목 입력칸에 글자 입력 → 글자 표시
  R3 SDI 창 끌기: 제목줄 끌기 → 창 위치 이동(모델·화면)
  R4 테마 전환: theme.select pd-dark → 화면 색 변화
  R5 주기 갱신 표시: 유휴 중 상태줄(FPS 등) 영역이 갱신되는지(기록 — 패치 전후 비교)
  R6 스크롤: 그리드 위 마우스 휠 → 첫 보이는 행 변화(모델·화면)
  R7 핀치: 그리드 위 두 손가락 120→150px(+2단계) → 창 배율 또는 글자 크기 변화(모델·화면)
"""
import json
import os
import sys
import time
from io import BytesIO

from PIL import Image, ImageChops
from playwright.sync_api import sync_playwright

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import retest_browser2 as t  # noqa: E402

OUT = t.OUT
IDLE = int(sys.argv[sys.argv.index("--idle") + 1]) if "--idle" in sys.argv else 30
TAG = sys.argv[sys.argv.index("--tag") + 1] if "--tag" in sys.argv else "run"
res = []
RAF_JS = """(() => { window.__rafc = 0; const o = window.requestAnimationFrame.bind(window);
  window.requestAnimationFrame = function (cb) { window.__rafc++; return o(cb); }; })();"""


def rec(item, expect, actual, verdict):
    res.append({"item": item, "expect": expect, "actual": actual, "verdict": verdict})
    print("%-6s %s | %s" % (verdict, item, str(actual)[:400]), flush=True)


def shot(page, clip=None):
    png = page.screenshot(clip=clip) if clip else page.screenshot()
    return Image.open(BytesIO(png)).convert("RGB")


def changed(a, b):
    d = ImageChops.difference(a, b).convert("L")
    n = sum(1 for v in d.getdata() if v > 24)
    return n


def raf(page):
    return page.evaluate("() => window.__rafc")


def act(page, name, fn, clip, model=None):
    """유휴 상태에서 동작 하나: 직전 캡처 → 동작 → 0.3초 뒤 캡처. 화소 변화 수·rAF 증가·모델 전후."""
    before = shot(page, clip)
    m0 = model() if model else None
    r0 = raf(page)
    fn()
    time.sleep(0.3)
    after = shot(page, clip)
    r1 = raf(page)
    m1 = model() if model else None
    n = changed(before, after)
    return {"pixels_changed": n, "raf_after_action": r1 - r0, "model_before": m0, "model_after": m1}


def idle(page, secs):
    r0 = raf(page)
    time.sleep(secs)
    return {"idle_secs": secs, "raf_during_idle": raf(page) - r0}


def main():
    with sync_playwright() as p:
        b = p.chromium.launch(headless=True, args=t.ARGS)
        ctx = t.allow_dup(b.new_context(viewport={"width": 1280, "height": 800}, has_touch=True))
        ctx.add_init_script(RAF_JS)
        ctx, page, r = t.boot(b, "?layout-key=otestw2-idlefx", ctx=ctx)
        r.call("settings.set", wheelMode="WindowZoom", baseFontSize=14)
        r.call("theme.select", id="pd-ocean")
        r.call("docs.close", all=True)
        g = r.call("docs.open", kind="grid", title="IF-grid", allowDuplicate=True)[0]["id"]
        f = r.call("docs.open", kind="form", title="IF-form", sdi=True, allowDuplicate=True)[0]["id"]
        time.sleep(1.0)
        r.call("sdi.resetPositions")
        r.call("docs.activate", doc=g)
        time.sleep(1.5)
        gb = (r.call("ui.find", id="grid.dgMain", doc=g) or {}).get("bounds")
        cdp = ctx.new_cdp_session(page)
        full = None  # 전체 화면 비교

        # R5: 유휴 중 상태줄 주기 갱신 — 유휴 시작·끝 캡처 비교(기록)
        sb = {"x": 0, "y": 765, "width": 1280, "height": 35}
        s_a = shot(page, sb)
        idl = idle(page, IDLE)
        s_b = shot(page, sb)
        rec("R0 유휴 %d초 동안 rAF 호출 수(참고: 원본 ~60/s, 패치 0 근처)" % IDLE, "기록", idl, "기록")
        rec("R5 유휴 중 상태줄 영역 갱신 여부(주기 갱신 표시)", "기록 — 패치 전후 같아야 함", {"status_bar_pixels_changed": changed(s_a, s_b)}, "기록")

        # R1 클릭: 그리드 5번째 행 근처 클릭
        def click_row():
            page.mouse.click(gb["x"] + 120, gb["y"] + 120)
        a1 = act(page, "click", click_row, None)
        rec("R1 유휴 뒤 클릭 → 선택 표시가 바로 그려짐", "0.3초 안에 화소 변화 > 0", a1, "PASS" if a1["pixels_changed"] > 0 else "FAIL(화면 멈춤 의심)")
        idle(page, 5)

        # R2 타이핑: SDI 폼 제목 입력칸
        tb = (r.call("ui.find", id="form.txtTitle", doc=f) or {}).get("bounds")
        page.mouse.click(tb["x"] + 20, tb["y"] + tb["h"] / 2) if tb else None
        idle(page, 5)
        clip2 = {"x": tb["x"], "y": tb["y"], "width": tb["w"], "height": tb["h"]} if tb else None
        a2 = act(page, "type", lambda: page.keyboard.type("abc"), clip2,
                 model=lambda: ((r.call("ui.find", id="form.txtTitle", doc=f) or {}).get("text")))
        rec("R2 유휴 뒤 타이핑 → 글자가 바로 그려짐", "모델 text 변화 + 0.3초 안에 입력칸 화소 변화", a2,
            "PASS" if a2["pixels_changed"] > 0 and a2["model_before"] != a2["model_after"] else "FAIL")
        idle(page, 5)

        # R3 SDI 창 끌기
        lk = (r.call("ui.find", id="chrome.btnLock", doc=f) or {}).get("bounds")

        def drag():
            sx, sy = lk["x"] - 150, lk["y"] + lk["h"] / 2
            page.mouse.move(sx, sy)
            page.mouse.down()
            for i in range(1, 11):
                page.mouse.move(sx + 120 * i / 10, sy + 60 * i / 10)
                time.sleep(0.02)
            page.mouse.up()

        def pos():
            d = next((x for x in (r.call("docs.list") or []) if x.get("id") == f), {})
            ic = (d.get("managedBounds") or {}).get("inContainer") or {}
            return (ic.get("x"), ic.get("y"))
        a3 = act(page, "drag", drag, None, model=pos) if lk else {"error": "btnLock 없음"}
        rec("R3 유휴 뒤 SDI 창 끌기 → 이동이 바로 그려짐", "모델 위치 변화 + 화소 변화", a3,
            "PASS" if lk and a3["pixels_changed"] > 0 and a3["model_before"] != a3["model_after"] else "FAIL")
        idle(page, 5)

        # R4 테마 전환
        a4 = act(page, "theme", lambda: r.call("theme.select", id="pd-dark"), None,
                 model=lambda: (r.call("settings.get") or {}).get("activeThemeId"))
        rec("R4 유휴 뒤 테마 전환 → 색이 바로 바뀜", "activeThemeId 변화 + 화소 변화", a4,
            "PASS" if a4["pixels_changed"] > 0 and a4["model_before"] != a4["model_after"] else "FAIL")
        idle(page, 5)

        # R6 스크롤 — SDI 폼 창이 그리드 가운데를 덮으므로(R3 뒤 위치) 최소화해 휠·핀치가 그리드에 닿게 한다(p1121 첫 실행 교훈)
        r.call("docs.state", doc=f, state="minimized")
        r.call("docs.activate", doc=g)
        time.sleep(0.5)
        gb = (r.call("ui.find", id="grid.dgMain", doc=g) or {}).get("bounds") or gb
        idle(page, 5)

        def wheel():
            page.mouse.move(gb["x"] + gb["w"] / 2, gb["y"] + gb["h"] / 2)
            page.mouse.wheel(0, 600)
        clip6 = {"x": gb["x"], "y": gb["y"], "width": gb["w"], "height": gb["h"]}
        a6 = act(page, "scroll", wheel, clip6,
                 model=lambda: ((r.call("grid.visible", id="grid.dgMain", doc=g) or {}).get("first")))
        rec("R6 유휴 뒤 휠 스크롤 → 행이 바로 다시 그려짐", "첫 보이는 행 변화 + 화소 변화", a6,
            "PASS" if a6["pixels_changed"] > 0 and a6["model_before"] != a6["model_after"] else "FAIL")
        idle(page, 5)

        # R7 핀치(창 배율 +2단계)
        cx, cy = gb["x"] + gb["w"] / 2, gb["y"] + gb["h"] / 2

        def pinch():
            def pts(d):
                return [{"x": cx - d / 2, "y": cy, "id": 0}, {"x": cx + d / 2, "y": cy, "id": 1}]
            cdp.send("Input.dispatchTouchEvent", {"type": "touchStart", "touchPoints": pts(120)})
            for i in range(1, 11):
                cdp.send("Input.dispatchTouchEvent", {"type": "touchMove", "touchPoints": pts(120 + 30 * i / 10)})
                time.sleep(0.03)
            cdp.send("Input.dispatchTouchEvent", {"type": "touchEnd", "touchPoints": []})
        a7 = act(page, "pinch", pinch, clip6,
                 model=lambda: next((x.get("zoom") for x in (r.call("docs.list") or []) if x.get("id") == g), None))
        rec("R7 유휴 뒤 핀치 → 배율 변화가 바로 그려짐", "zoom 변화 + 화소 변화", a7,
            "PASS" if a7["pixels_changed"] > 0 and a7["model_before"] != a7["model_after"] else "FAIL")
        page.screenshot(path=os.path.join(OUT, "idlefx_%s_end.png" % TAG))
        r.call("theme.select", id="pd-ocean")
        r.call("settings.set", wheelMode="FontSize", baseFontSize=14)
        with open(os.path.join(OUT, "retest_web17_%s.json" % TAG), "w", encoding="utf-8") as fo:
            json.dump(res, fo, ensure_ascii=False, indent=2)
        b.close()


if __name__ == "__main__":
    main()
