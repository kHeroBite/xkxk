# 웹 스위치 재판정(i1446~, Button+classes pdUiOn) — 항목 4(같은 칸 5회 재클릭·다른 칸 즉시 전환, 직후·100ms), 4-k(키보드 Space·Enter), 9(라이트·다크 × 폰·태블릿 대비 캡처)
# 사용: PD_URL=http://127.0.0.1:5219/ PD_OUT=<폴더> python3 tools/retest_browser33.py   (진단판·헤드리스 전용. 포커스 확인 원격 명령이 없어 키 입력 결과(모드·pdUiOn)로 판정)
import json
import os
import sys
import time

from playwright.sync_api import sync_playwright

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import retest_browser2 as t  # noqa: E402

OUT = t.OUT
H = 760
BT, BP, SW = "shell.btnUiModeTablet", "shell.btnUiModePhone", "shell.uiModeSwitch"
res = []


def rec(item, expect, actual, verdict):
    res.append({"item": item, "expect": expect, "actual": actual, "verdict": verdict})
    print("%-6s %s | %s" % (verdict, item, json.dumps(actual, ensure_ascii=False)[:600]), flush=True)


def f(r, i):
    return r.call("ui.find", id=i) or {}


def state(r, target, n, when):
    tb, ph = f(r, BT), f(r, BP)
    m = r.call("ui.mode") or {}
    return {"target": target, "n": n, "when": when, "mode": m.get("uiMode"), "isCompact": m.get("isCompact"),
            "tablet_on": "pdUiOn" in (tb.get("classes") or []), "phone_on": "pdUiOn" in (ph.get("classes") or []),
            "tablet_classes": tb.get("classes"), "phone_classes": ph.get("classes")}


def good(x):
    want_phone = x["target"] == "phone"
    on_ok = (x["phone_on"] and not x["tablet_on"]) if want_phone else (x["tablet_on"] and not x["phone_on"])
    return on_ok and str(x["mode"]).lower() == x["target"] and x["isCompact"] is want_phone


def cell_bg(path, cb, sb):
    # 캡처(스위치 x-10 부터 잘라 냄) 안에서 칸 영역의 최빈 색 = 배경색
    from collections import Counter

    from PIL import Image
    if not cb:
        return None
    im = Image.open(path).convert("RGB")
    ox = max(0, sb["x"] - 10)
    box = (int(cb["x"] - ox) + 2, int(cb["y"]) + 3, int(cb["x"] - ox + cb["w"]) - 2, int(cb["y"] + cb["h"]) - 3)
    return list(Counter(im.crop(box).getdata()).most_common(1)[0][0])


def cell_text(path, cb, sb, bg):
    # 글자색 = 칸 안에서 배경과 대비가 가장 큰 화소(글자 획의 가장 진한 화소, 안티앨리어싱 가장자리 제외 효과). 반환: (글자색, 글자 대 배경 대비비)
    from PIL import Image
    if not cb or not bg:
        return None, None
    im = Image.open(path).convert("RGB")
    ox = max(0, sb["x"] - 10)
    box = (int(cb["x"] - ox) + 2, int(cb["y"]) + 3, int(cb["x"] - ox + cb["w"]) - 2, int(cb["y"] + cb["h"]) - 3)
    best = max(set(im.crop(box).getdata()), key=lambda c: contrast(list(c), bg))
    return list(best), contrast(list(best), bg)


def contrast(a, b):
    if not a or not b:
        return None

    def lum(c):
        v = [x / 255 for x in c]
        v = [x / 12.92 if x <= 0.03928 else ((x + 0.055) / 1.055) ** 2.4 for x in v]
        return 0.2126 * v[0] + 0.7152 * v[1] + 0.0722 * v[2]
    la, lb = lum(a), lum(b)
    return round((max(la, lb) + 0.05) / (min(la, lb) + 0.05), 2)


def click(page, r, i):
    bb = f(r, i).get("bounds")
    page.mouse.click(bb["x"] + bb["w"] / 2, bb["y"] + bb["h"] / 2)


def main():
    with sync_playwright() as p:
        b = p.chromium.launch(headless=True, args=t.ARGS)
        ctx = t.allow_dup(b.new_context(viewport={"width": 1024, "height": H}))
        ctx, page, r = t.boot(b, "?layout-key=otestw2-switch2", w=1024, h=H, ctx=ctx)
        r.call("ui.mode", set="auto")
        r.call("docs.close", all=True)
        r.call("docs.open", kind="grid", title="SW2-grid", allowDuplicate=True)
        time.sleep(1.5)
        print("fields", json.dumps(f(r, BT), ensure_ascii=False)[:400], flush=True)

        # 4. 다른 칸 클릭 즉시 전환 → 같은 칸 5회 재클릭(직후·100ms)
        reads = []
        for btn, nm in ((BP, "phone"), (BT, "tablet")):
            for n in range(6):
                click(page, r, btn)
                reads.append(state(r, nm, n, "직후"))
                time.sleep(0.1)
                reads.append(state(r, nm, n, "100ms"))
                time.sleep(0.5)
        bad = [x for x in reads if not good(x)]
        page.screenshot(path=os.path.join(OUT, "sw4b_tablet.png"), clip={"x": 420, "y": 0, "width": 170, "height": 32})
        rec("4 pdUiOn: 다른 칸 클릭 직후 전환(ui.mode·isCompact·pdUiOn), 같은 칸 5회 재클릭 동안 그 칸만 pdUiOn(직후·100ms)", "24회 읽기 모두 대상 칸에만 pdUiOn·모드 일치",
            {"bad": bad, "reads": reads}, "PASS" if not bad else "FAIL")

        # 4-k. 키보드: 선택된 태블릿 칸을 눌러 포커스 확보(모드 불변) → Tab → Space(폰 기대) → Space 재입력(폰 유지) → Shift+Tab → Enter(태블릿 기대)
        kb = []
        click(page, r, BT)
        time.sleep(0.6)
        kb.append({"step": "태블릿 칸 클릭(포커스 확보)", **state(r, "tablet", 0, "후")})
        kb.append({"step": "클릭 뒤 포커스 표시", "tablet_pseudo": [c for c in (f(r, BT).get("classes") or []) if c.startswith(":focus")],
                   "phone_pseudo": [c for c in (f(r, BP).get("classes") or []) if c.startswith(":focus")], "record_only": True})
        page.keyboard.press("Tab")
        time.sleep(0.3)
        # odev-2r 요청: Space 전에 포커스 표시(classes 의 :focus·:focus-visible 의사 클래스) 기록
        kb.append({"step": "Tab 뒤 포커스 표시", "tablet_pseudo": [c for c in (f(r, BT).get("classes") or []) if c.startswith(":focus")],
                   "phone_pseudo": [c for c in (f(r, BP).get("classes") or []) if c.startswith(":focus")], "record_only": True})
        page.keyboard.press("Space")
        time.sleep(0.6)
        kb.append({"step": "Tab → Space", **state(r, "phone", 1, "후")})
        page.keyboard.press("Space")
        time.sleep(0.6)
        kb.append({"step": "Space 재입력(같은 칸)", **state(r, "phone", 2, "후")})
        page.keyboard.press("Shift+Tab")
        time.sleep(0.3)
        page.keyboard.press("Enter")
        time.sleep(0.6)
        kb.append({"step": "Shift+Tab → Enter", **state(r, "tablet", 3, "후")})
        page.screenshot(path=os.path.join(OUT, "sw4k_after.png"), clip={"x": 420, "y": 0, "width": 170, "height": 32})
        rec("4-k 키보드: Space·Enter 로 누른 칸 전환·그 칸만 pdUiOn, 같은 칸 Space 재입력 유지", "4단계 모두 대상 칸만 pdUiOn·모드 일치(포커스 이동은 Tab/Shift+Tab 가정)",
            kb, "PASS" if all(good(x) for x in kb if not x.get("record_only")) else "FAIL")

        # 9. 라이트·다크 × 폰·태블릿 대비 캡처(판정은 캡처 눈 확인)
        shots = []
        for theme in ("pd-light", "pd-dark", "pd-contrast", "pd-ember", "pd-forest"):
            r.call("theme.select", id=theme)
            time.sleep(1.0)
            for btn, nm in ((BP, "phone"), (BT, "tablet")):
                click(page, r, btn)
                page.mouse.move(900, 400)
                time.sleep(0.8)
                sb = f(r, SW).get("bounds") or {"x": 420, "y": 4, "w": 170, "h": 24}
                pth = os.path.join(OUT, "sw9_%s_%s.png" % (theme, nm))
                page.screenshot(path=pth, clip={"x": max(0, sb["x"] - 10), "y": 0, "width": sb["w"] + 20, "height": 32})
                st = state(r, nm, 0, "후")
                cells = {k: cell_bg(pth, (f(r, i).get("bounds") or {}), sb) for k, i in (("tablet", BT), ("phone", BP))}
                sel, uns = cells[nm], cells["tablet" if nm == "phone" else "phone"]
                txt, txt_cr = cell_text(pth, (f(r, BP if nm == "phone" else BT).get("bounds") or {}), sb, sel)
                utxt, utxt_cr = cell_text(pth, (f(r, BT if nm == "phone" else BP).get("bounds") or {}), sb, uns)
                shots.append({"theme": theme, "mode": nm, "path": pth, "ok_state": good(st), "selected_bg": sel, "unselected_bg": uns,
                              "selected_text": txt, "selected_text_hex": "#%02X%02X%02X" % tuple(txt) if txt else None,
                              "text_contrast": txt_cr, "text_contrast_ok_4_5": (txt_cr or 0) >= 4.5,
                              "unselected_text": utxt, "unselected_text_contrast": utxt_cr,
                              "selected_hex": "#%02X%02X%02X" % tuple(sel) if sel else None, "contrast_sel_vs_unsel": contrast(sel, uns)})
        r.call("theme.select", id="pd-light")
        rec("9 대비: 라이트·다크·고대비·ocean × 폰·태블릿 스위치 캡처(선택 칸 배경색 실측, 구분·가독은 눈 확인)",
            "① 선택 칸 배경 대 비선택 칸 배경 3:1 이상 ② 선택 칸 글자 대 배경 4.5:1 이상 ③ 비선택 칸 글자 읽힘(대비 기록·눈 확인) — odev-2r d4614f26 절차", shots,
            "PASS" if all(s["ok_state"] and s["text_contrast_ok_4_5"] and (s["contrast_sel_vs_unsel"] or 0) >= 3 for s in shots) else "FAIL")

        r.call("ui.mode", set="auto")
        with open(os.path.join(OUT, "retest_web33_switch2.json"), "w", encoding="utf-8") as fo:
            json.dump(res, fo, ensure_ascii=False, indent=1)
        b.close()


if __name__ == "__main__":
    main()
