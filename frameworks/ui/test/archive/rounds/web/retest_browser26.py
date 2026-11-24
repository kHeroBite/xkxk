# 진단판 유휴 rAF 출처 가르기(odev-3 절차) — 제품 그리드 1개 기본 상태에서 ① 기본 ② 포커스 해제 ③ 로그·알림 패널 닫기(누적) 조건마다 8초 대기 뒤 6초 유휴 rAF 수·호출 위치·startAnimationFrames 횟수·상태바 FPS 글자 변화
# 사용: PD_URL=http://127.0.0.1:5213/ python3 tools/retest_browser26.py <태그>   (진단판·헤드리스, dpr 1, 800×700)
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
from retest_browser16 import WRAP, origin  # noqa: E402
from retest_browser11 import _walk, full_tree  # noqa: E402

TAG = sys.argv[1] if len(sys.argv) > 1 else "x"
OUT = os.path.join(t.OUT, TAG)
SB = {"x": 0, "y": 670, "width": 800, "height": 30}  # 상태바(FPS 글자) 영역


def shot(page, clip=None):
    return Image.open(BytesIO(page.screenshot(clip=clip) if clip else page.screenshot())).convert("RGB")


def measure(page, name):
    time.sleep(8.0)
    a = shot(page, SB)
    page.evaluate("() => { window.__raf.n = 0; window.__raf.sites = {}; window.__raf.on = true; }")
    t0 = time.time()
    time.sleep(6.0)
    st = page.evaluate("() => { window.__raf.on = false; return window.__raf; }")
    secs = time.time() - t0
    bimg = shot(page, SB)
    page.screenshot(path=os.path.join(OUT, "idlesrc_%s.png" % name))
    changed = sum(1 for v in ImageChops.difference(a, bimg).convert("L").getdata() if v > 24)
    restarts = sum(n for s, n in st["sites"].items() if "startAnimationFrames" in s)
    focus = page.evaluate("() => { const e = document.activeElement; return e ? (e.tagName + (e.id ? '#' + e.id : '') + (e.className && typeof e.className === 'string' ? '.' + e.className.split(' ')[0] : '')) : null; }")
    row = {"cond": name, "per_sec": round(st["n"] / secs, 1), "raf": st["n"], "startAnimationFrames": restarts,
           "by_origin": {origin(s): 0 for s in st["sites"]}, "sites_top": sorted(st["sites"].items(), key=lambda x: -x[1])[:4],
           "statusbar_pixels_changed": changed, "activeElement": focus}
    for s, n in st["sites"].items():
        row["by_origin"][origin(s)] += n
    print(json.dumps(row, ensure_ascii=False)[:500], flush=True)
    return row


def main():
    os.makedirs(OUT, exist_ok=True)
    rows = []
    with sync_playwright() as p:
        b = p.chromium.launch(headless=True, args=t.ARGS)
        ctx = t.allow_dup(b.new_context(viewport={"width": 800, "height": 700}))
        ctx.add_init_script(WRAP)
        ctx, page, r = t.boot(b, "?layout-key=otestw2-idlesrc", w=800, h=700, ctx=ctx)
        r.call("docs.close", all=True)
        r.call("docs.open", kind="grid", title="IS-grid", allowDuplicate=True)
        rows.append(measure(page, "1_default"))
        # ② 포커스 해제: 상태바 빈 곳 클릭(텍스트 상자 포커스 없앰)
        page.mouse.click(780, 690)
        page.evaluate("() => { if (document.activeElement && document.activeElement.blur) document.activeElement.blur(); }")
        rows.append(measure(page, "2_unfocus"))
        # ③ 로그·알림 패널 닫기: 머리줄 ✕(PART_CloseButton) 와 알림 패널은 ✕ 가 없어 ▾ 메뉴 대신 dock 숨김이 필요 — 로그 ✕ 클릭 후 알림은 남으면 기록
        tr = full_tree(r)
        closes = []
        for n, path in _walk(tr.get("tree"), []):
            if n.get("type") == "ToolChromeControl":
                title = next((c.get("text") for c, _ in _walk(n.get("children"), []) if c.get("type") == "TextBlock" and c.get("text")), "")
                btn = next((c for c, _ in _walk(n.get("children"), []) if c.get("name") == "PART_CloseButton" and c.get("visible")), None)
                closes.append((title[:20], btn.get("bounds") if btn else None))
        clicked = []
        for title, bb in closes:
            if bb and ("레이아웃" in title or "로그" in title or ":" in title or "알림" in title):  # 로그 패널 첫 글자는 로그 줄 시각
                page.mouse.click(bb["x"] + bb["w"] / 2, bb["y"] + bb["h"] / 2)
                time.sleep(0.6)
                clicked.append(title)
        rows.append(measure(page, "3_panels_closed"))
        rows[-1]["closed"] = clicked
        rows[-1]["chrome_close_buttons"] = closes
        b.close()
    with open(os.path.join(OUT, "retest_web26_idlesrc.json"), "w", encoding="utf-8") as f:
        json.dump(rows, f, ensure_ascii=False, indent=1)


if __name__ == "__main__":
    main()
