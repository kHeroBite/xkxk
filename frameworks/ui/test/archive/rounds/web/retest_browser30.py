# 웹 유휴 수정 판(i1350~) 회귀 — sel·nosel 각 8초 유휴(rAF·startAnimationFrames·perf.idle frames 증가 0), 휠 스크롤 중 rAF 동작·상태바 FPS 칸 채워짐, 멈춘 뒤 4초 안에 rAF 0·FPS 글자 고정(1초 창, 상태바 외부 캡처)
# 사용: PD_URL=http://127.0.0.1:5216/ PD_OUT=<폴더> python3 tools/retest_browser30.py sel nosel   (진단판·헤드리스 전용, 유휴 구간 안에서는 perf.idle 만 호출·마우스 무입력)
import json
import os
import sys
import time

from playwright.sync_api import sync_playwright

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import retest_browser as rb  # noqa: E402
import retest_browser2 as t  # noqa: E402
from retest_browser16 import WRAP  # noqa: E402

OUT = t.OUT
res = []
# 상태바 'FPS' 글자 찾기: i1350 sel 캡처(fps_sel_scroll_6.png)에서 'FPS' 글리프(x 124~146, y 7~22)를 본으로 잘라 상태바 전체를 미끄러뜨리며 대조.
# 칸 위치 고정 판정은 빈 칸일 때 옆 글자가 당겨져 들어오므로 쓰지 않는다(i1350 nosel 캡처에서 확인). 고정 판정은 상태바 왼쪽 330px 해시가 이후 모두 같은지로 본다.
TEMPLATE_PNG = os.path.join(HERE, "..", "logs", "otest_w2", "i1350", "fps_sel_scroll_6.png")
TEMPLATE_BOX = (124, 7, 147, 23)
MATCH_MAX = 20.0  # 평균 밝기 차 이하이면 'FPS' 글자 있음
FILLED_DARK = 1   # 결과 표기 호환용(filled 는 match 로 판정)


def _template():
    import numpy as np
    from PIL import Image
    if not os.path.exists(TEMPLATE_PNG):
        return None
    return np.asarray(Image.open(TEMPLATE_PNG).convert("L").crop(TEMPLATE_BOX), dtype=np.float32)


TPL = _template()


def fps_slot(page, clip, path):
    import hashlib
    from io import BytesIO

    import numpy as np
    from PIL import Image
    png = page.screenshot(path=path, clip=clip)
    im = np.asarray(Image.open(BytesIO(png)).convert("L"), dtype=np.float32)
    best = None
    if TPL is not None:
        th, tw = TPL.shape
        for y in range(0, im.shape[0] - th + 1):
            for x in range(0, min(im.shape[1], 400) - tw + 1):
                d = float(np.abs(im[y:y + th, x:x + tw] - TPL).mean())
                if best is None or d < best[0]:
                    best = (d, x, y)
    found = best is not None and best[0] <= MATCH_MAX
    return {"dark": 1 if found else 0, "match": round(best[0], 1) if best else None, "at": best[1:] if best else None,
            "hash": hashlib.md5(im[:, :330].tobytes()).hexdigest()[:10]}


def raf_on(page):
    page.evaluate("() => { window.__raf.n = 0; window.__raf.sites = {}; window.__raf.on = true; }")


def raf_off(page):
    st = page.evaluate("() => { window.__raf.on = false; return window.__raf; }")
    return st["n"], sum(n for s, n in st["sites"].items() if "startAnimationFrames" in s), sorted(st["sites"].items(), key=lambda x: -x[1])[:3]


def frames(r):
    return (r.call("perf.idle") or {}).get("frames")


def run(b, variant):
    ctx = t.allow_dup(b.new_context(viewport={"width": 1280, "height": 800}))
    ctx.add_init_script(WRAP)
    page = ctx.new_page()
    page.goto(t.URL + "?layout-key=otestw2-idle1350-" + variant)
    if variant == "sel":
        page.wait_for_selector("canvas.avalonia-canvas", timeout=60000)
    page.wait_for_function("() => window.pdAutomation && window.pdAutomation.invoke", timeout=60000)
    r = rb.WebRemote(page)
    for _ in range(90):
        if r.call("status") is not None and r.call("docs.list") is not None:
            break
        time.sleep(1)
    r.call("docs.close", all=True)
    g = r.call("docs.open", kind="grid", title="IR-grid", allowDuplicate=True)[0]["id"]
    time.sleep(1.5)
    gb = (r.call("ui.find", id="grid.dgMain", doc=g) or {}).get("bounds")
    # 마지막 측정 대상 호출 뒤 2초 이상 무입력·무호출
    time.sleep(4.0)
    f0 = frames(r)
    raf_on(page)
    time.sleep(8.0)
    n, rs, top = raf_off(page)
    f1 = frames(r)
    idle = {"per_sec": round(n / 8.0, 2), "raf_total": n, "startAnimationFrames": rs, "sites_top": top, "frames_start": f0, "frames_end": f1,
            "frames_delta": (f1 - f0) if isinstance(f0, (int, float)) and isinstance(f1, (int, float)) else None}
    ok = n == 0 and rs == 0 and idle["frames_delta"] == 0
    res.append({"item": "I-%s 8초 유휴 rAF 0·재시작 0·frames 증가 0" % variant, "actual": idle, "verdict": "PASS" if ok else "FAIL"})
    print(res[-1]["verdict"], res[-1]["item"], json.dumps(idle, ensure_ascii=False)[:400], flush=True)

    # 휠 스크롤 2초 동안 상태바 FPS 글자 캡처, 멈춘 뒤 1초 창 6개
    sb = {"x": 0, "y": 760, "width": 640, "height": 40}
    shots = []
    if gb:
        page.mouse.move(gb["x"] + gb["w"] / 2, gb["y"] + gb["h"] / 2)
        raf_on(page)
        t0 = time.time()
        k = 0
        scroll_slots = []
        while time.time() - t0 < 2.0:
            page.mouse.wheel(0, 120)
            k += 1
            time.sleep(0.1)
            if k in (6, 12, 18):
                p = os.path.join(OUT, "fps_%s_scroll_%d.png" % (variant, k))
                scroll_slots.append(fps_slot(page, sb, p))
                shots.append(p)
        n_s, rs_s, _ = raf_off(page)
        after = []
        for i in range(6):
            raf_on(page)
            time.sleep(1.0)
            n_a, rs_a, _ = raf_off(page)
            p = os.path.join(OUT, "fps_%s_after%ds.png" % (variant, i + 1))
            slot = fps_slot(page, sb, p)  # 외부 캡처(입력·원격 호출 아님)
            after.append({"sec": i + 1, "raf": n_a, "restarts": rs_a, "slot_dark": slot["dark"], "slot_hash": slot["hash"]})
        shots.append(p)
        zero_at = next((a["sec"] for a in after if a["raf"] == 0 and all(x["raf"] == 0 for x in after[after.index(a):])), None)
        fixed_at = next((a["sec"] for a in after if all(x["slot_hash"] == a["slot_hash"] for x in after[after.index(a):])), None)
        filled = [s["dark"] >= FILLED_DARK for s in scroll_slots]  # dark=1 ⇔ 'FPS' 글자 대조 성공
        sc = {"scroll_raf": n_s, "scroll_restarts": rs_s, "wheel_events": k, "scroll_fps_slot": scroll_slots, "fps_filled_in_scroll": filled,
              "after_1s_windows": after, "zero_from_sec": zero_at, "fps_fixed_from_sec": fixed_at, "shots": shots}
        # 기준(lead 결정): 마지막 입력 뒤 4초 안 — 깨움 2초 + FPS 타이머 1틱(최대 1초) + 커밋 뒤 몇 프레임
        okS = n_s > 0 and zero_at is not None and zero_at <= 4
        res.append({"item": "S-%s 휠 스크롤 중 rAF 동작, 멈춘 뒤 4초 안에 rAF 0 복귀" % variant, "actual": sc, "verdict": "PASS" if okS else "FAIL"})
        print(res[-1]["verdict"], res[-1]["item"], json.dumps(sc, ensure_ascii=False)[:600], flush=True)
        okF = any(filled) and fixed_at is not None and fixed_at <= 4
        res.append({"item": "F-%s 스크롤 중 상태바 FPS 칸 채워짐, 멈춘 뒤 4초 안에 FPS 글자 고정(상태바 외부 캡처 — 판정 후 캡처 눈 확인)" % variant,
                    "actual": {"filled": filled, "match": [(s["match"], s["at"]) for s in scroll_slots], "fixed_from_sec": fixed_at, "match_max": MATCH_MAX},
                    "verdict": "PASS" if okF else "FAIL"})
        print(res[-1]["verdict"], res[-1]["item"], json.dumps(res[-1]["actual"], ensure_ascii=False), flush=True)
    ctx.close()


def main():
    with sync_playwright() as p:
        b = p.chromium.launch(headless=True, args=t.ARGS)
        for v in sys.argv[1:] or ["sel", "nosel"]:
            run(b, v)
            with open(os.path.join(OUT, "retest_web30_idle.json"), "w", encoding="utf-8") as f:
                json.dump(res, f, ensure_ascii=False, indent=1)
        b.close()


if __name__ == "__main__":
    main()
