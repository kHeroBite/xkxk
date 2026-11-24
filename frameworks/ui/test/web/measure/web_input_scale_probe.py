#!/usr/bin/env python3
# 웹 기기 배율에서 그리기 배율과 누름 배율이 같은지 — Playwright 배율 흉내(device_scale_factor)와 Chrome 실제 배율(--force-device-scale-factor)을 비교하는 측정 도구
"""
사용
  web_input_scale_probe.py --bundle <wwwroot> --port <포트(5180·5220 제외)> --out-dir <결과 폴더> [--dpr 2.25] [--only base,emul,force]
  web_input_scale_probe.py --self-test

배경(r10·r11 웹 close, 10차 번들): DPR 2.25 흉내에서 앱 TopLevel 은 622.2 DIP(windows.list main.w), 뷰포트 1400 CSS 라
그리기는 CSS = DIP × 2.25 였는데(기준 그림을 screencast 로 맞춘 뒤 occlusion·끝 모습 PASS), 마우스 누름은 DIP 좌표 그대로(배율 1)에서만 ✕ 에 닿았다.
앱 코드는 배율을 적용하지 않는다(변환은 Avalonia.Browser 안). 이 차이가 흉내 특성인지 실제 고배율 화면에서도 생기는지 가린다.

구성(같은 번들·같은 조작)
  base  : 배율 1(대조) — new_context(viewport, device_scale_factor=1)
  emul  : Playwright 흉내 — new_context(viewport, device_scale_factor=dpr)
  force : Chrome 실제 배율 — launch(args + --force-device-scale-factor=dpr, --window-size) + new_context(no_viewport=True, Playwright 가 배율을 덮어쓰지 않게)
시행(구성마다): 겹침 창 2개 → 활성 창 ✕(chrome.btnClose, DIP 사각형) 가운데를
  ① 그리기 배율 k = innerWidth ÷ main.w 로 바꾼 CSS 점, ② 배율 1(DIP 그대로) 점에 각각 새 준비 뒤 1회 누른다(k 가 1 이면 ① 만).
  닿음 = 누른 뒤 그 문서가 docs.list 에서 사라짐. 화면은 구성마다 준비 직후 1장.
판정은 하지 않고 기록만 남긴다. 해석(리드 계획): force 에서 ① 이 닿으면 흉내 특성, force 에서도 ② 만 닿으면 실제 사용자도 빗나감(결함 후보).
"""
import argparse
import json
import os
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
BLOCKED_PORTS = (5180, 5220)  # 사용자 Pd 앱 포트 — 쓰지 않는다


def scales(k):
    """누를 배율 목록: 그리기 배율 k 먼저, 배율 1 다음(같으면 하나). k 가 없으면 배율 1 만."""
    return [s for s in dict.fromkeys([k, 1.0]) if s is not None]


def point(bounds, scale):
    """DIP 사각형 가운데를 scale 배 한 CSS 점."""
    return (bounds["x"] + bounds["w"] / 2) * scale, (bounds["y"] + bounds["h"] / 2) * scale


def self_test():
    ok = {
        "배율 2.25 면 두 점": scales(2.25) == [2.25, 1.0],
        "배율 1 이면 한 점": scales(1.0) == [1.0],
        "배율 없음이면 1 만": scales(None) == [1.0],
        "가운데 점 × 배율": point({"x": 100, "y": 40, "w": 20, "h": 20}, 2.25) == (247.5, 112.5),
        "막는 포트": 5180 in BLOCKED_PORTS and 5220 in BLOCKED_PORTS,
    }
    bad = [k for k, v in ok.items() if not v]
    print("self-test %d/%d %s" % (len(ok) - len(bad), len(ok), bad))
    return 1 if bad else 0


def main():
    ap = argparse.ArgumentParser(description="웹 기기 배율에서 그리기 배율과 누름 배율을 비교한다(흉내 배율 대 Chrome 실제 배율).")
    ap.add_argument("--bundle", help="Pd.Poc.Browser publish 의 wwwroot 경로(index.html 이 있는 폴더)")
    ap.add_argument("--port", type=int, default=0, help="정적 서버 포트(필수, 5180·5220 금지 — 사용자 Pd 앱 포트)")
    ap.add_argument("--out-dir", help="결과 폴더(web_input_scale_probe.json 을 쓴다)")
    ap.add_argument("--dpr", type=float, default=2.25, help="기기 배율(기본 2.25)")
    ap.add_argument("--only", default="base,emul,force", help="잴 방식 쉼표 목록 base(배율 1)·emul(흉내)·force(실제 배율), 기본 셋 다")
    ap.add_argument("--self-test", action="store_true", help="판정 규칙 자체 시험만 돌린다(브라우저 없이)")
    a = ap.parse_args()
    if a.self_test:
        return self_test()
    if not (a.bundle and a.out_dir and a.port) or a.port in BLOCKED_PORTS:
        ap.error("--bundle·--out-dir·--port(5180·5220 제외) 가 필요하다")
    sys.path.insert(0, os.environ.get("PD_WEB_MEASURE", HERE))  # motion_web_check·ui28_web 위치(test/web/measure). 후보 폴더에서 돌리면 환경변수로 준다
    import motion_web_check as check
    from playwright.sync_api import sync_playwright
    from ui28_web import ARGS, Web

    view = (1400, 900)
    w, out = Web(a.bundle, a.port, a.out_dir, viewport=view), {"dpr": a.dpr, "configs": {}}
    try:
        with sync_playwright() as p:
            for name in [n for n in a.only.split(",") if n]:
                row = out["configs"][name] = {}
                try:
                    row.update(run_config(p, w, check, ARGS, name, a.dpr, view))
                except Exception as e:  # noqa: BLE001 — 한 구성의 실패가 다른 구성을 가리지 않게
                    row["error"] = repr(e)[:300]
                print(name, json.dumps(row, ensure_ascii=False)[:400], flush=True)
    finally:
        w.httpd.shutdown()
        w.httpd.server_close()
        with open(os.path.join(a.out_dir, "web_input_scale_probe.json"), "w", encoding="utf-8") as f:
            json.dump(out, f, ensure_ascii=False, indent=1)
    return 0


def run_config(p, w, check, args, name, dpr, view):
    if name == "force":
        browser = p.chromium.launch(headless=True, args=args + ["--force-device-scale-factor=%s" % dpr, "--window-size=%d,%d" % view])
        context = browser.new_context(no_viewport=True)
    else:
        browser = p.chromium.launch(headless=True, args=args)
        context = browser.new_context(viewport={"width": view[0], "height": view[1]}, device_scale_factor=dpr if name == "emul" else 1)
    try:
        w.page = context.new_page()
        w.page.set_default_timeout(90000)
        w.page.goto("http://127.0.0.1:%d/" % w.port, wait_until="load", timeout=90000)
        check.ready(w)
        env = w.page.evaluate("() => ({dpr: window.devicePixelRatio, innerWidth: innerWidth, innerHeight: innerHeight})")
        main = next((x for x in check.need(w, "windows.list") if x.get("main")), {})
        k = env["innerWidth"] / main["w"] if main.get("w") else None
        res = {"env": env, "mainW": main.get("w"), "mainH": main.get("h"), "k": k, "trials": []}
        for i, scale in enumerate(scales(k)):
            doc = prepare(w, check)
            if i == 0:
                w.shot("%s.prepared" % name)
            b = check.need(w, "ui.find", id="chrome.btnClose", doc=doc)["bounds"]
            x, y = point(b, scale)
            w.page.mouse.click(x, y)
            time.sleep(1.5)
            closed = doc not in [d["id"] for d in check.need(w, "docs.list")]
            res["trials"].append({"scale": scale, "dip": [b["x"], b["y"], b["w"], b["h"]], "css_point": [round(x, 1), round(y, 1)], "closed": closed})
        return res
    finally:
        context.close()
        browser.close()


def prepare(w, check):
    """겹침 창 2개(활성 = 위 창)를 만들고 활성 문서 Id 를 돌려준다."""
    until = time.time() + 90
    while not w.call("docs.close", all=True).get("ok") and time.time() < until:
        time.sleep(2)
    check.need(w, "ui.style", set="pc")
    time.sleep(1.5)
    check.need(w, "docs.open", kind="grid", count=2, allowDuplicate=True)
    time.sleep(3)
    check.need(w, "docs.arrange", mode="cascade")
    time.sleep(1.5)
    return check.active(w)


if __name__ == "__main__":
    sys.exit(main())
