# 웹 태블릿모드/폰모드 전환 스위치 판정(odev-2r 절차 1~8, i1402) — 표시 폭·겹침(ko/en)·전환 3회 왕복 뷰 유지·체크 상태·저장(새로고침·폭 변경)·용어·좁은 폭 메뉴 체크
# 사용: PD_URL=http://127.0.0.1:5217/ PD_OUT=<폴더> python3 tools/retest_browser32.py   (진단판·헤드리스 전용, 회귀 7은 retest_browser11/20/27 로 따로 돈다)
import json
import os
import sys
import time

from playwright.sync_api import sync_playwright

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import retest_browser2 as t  # noqa: E402
from retest_browser25 import count, gv  # noqa: E402

OUT = t.OUT
H = 760
res = []
SW, BT, BP = "shell.uiModeSwitch", "shell.btnUiModeTablet", "shell.btnUiModePhone"


def rec(item, expect, actual, verdict):
    res.append({"item": item, "expect": expect, "actual": actual, "verdict": verdict})
    print("%-6s %s | %s" % (verdict, item, json.dumps(actual, ensure_ascii=False)[:700]), flush=True)


def f(r, i):
    return r.call("ui.find", id=i) or {}


def sw(r):
    s, tb, ph = f(r, SW), f(r, BT), f(r, BP)
    return {"visible": bool(s.get("effectivelyVisible")), "bounds": s.get("bounds"), "tablet": {"text": tb.get("text"), "checked": tb.get("checked")},
            "phone": {"text": ph.get("text"), "checked": ph.get("checked")}}


def mode(r):
    m = r.call("ui.mode") or {}
    return {k: m.get(k) for k in ("uiMode", "uiModeEffective", "isCompact", "viewWidth", "isNarrow")}


def menu(r):
    m = r.call("menu.list", id="shell.mnuUiMode") or {}
    return [{k: x.get(k) for k in ("id", "header", "effectivelyEnabled", "checked", "tip")} for x in m.get("items", []) if not x.get("separator")]


def checked_label(items):
    return [x["header"] for x in items if x.get("checked")]


def width(page, w):
    page.set_viewport_size({"width": w, "height": H})
    time.sleep(1.2)


def click(page, r, i):
    bb = f(r, i).get("bounds")
    if bb:
        page.mouse.click(bb["x"] + bb["w"] / 2, bb["y"] + bb["h"] / 2)
        time.sleep(1.0)
    return bb


def overlap(a, b):
    if not a or not b:
        return None
    return not (a["x"] + a["w"] <= b["x"] or b["x"] + b["w"] <= a["x"] or a["y"] + a["h"] <= b["y"] or b["y"] + b["h"] <= a["y"])


def open_menu_shot(page, r, name):
    # 보기 ▶ 화면 모드 를 열어 캡처(체크 표시 눈 확인용) 후 Escape 로 닫는다
    from retest_browser31 import open_view_menu
    open_view_menu(page, r)
    page.screenshot(path=os.path.join(OUT, name))
    page.keyboard.press("Escape")
    page.keyboard.press("Escape")
    time.sleep(0.4)


def main():
    with sync_playwright() as p:
        b = p.chromium.launch(headless=True, args=t.ARGS)
        ctx = t.allow_dup(b.new_context(viewport={"width": 1024, "height": H}))
        ctx, page, r = t.boot(b, "?layout-key=otestw2-switch", w=1024, h=H, ctx=ctx)
        r.call("ui.mode", set="auto")
        r.call("docs.close", all=True)
        g = r.call("docs.open", kind="grid", title="SW-grid", allowDuplicate=True)[0]["id"]
        r.call("docs.open", kind="gantt", title="SW-gantt", allowDuplicate=True)
        r.call("docs.open", kind="form", title="SW-form", allowDuplicate=True)
        time.sleep(1.5)

        # 1. 표시 폭
        vis = {}
        for w in (1024, 800, 700, 600, 599, 390):
            width(page, w)
            vis[w] = sw(r)["visible"]
            page.screenshot(path=os.path.join(OUT, "sw1_w%d.png" % w))
        width(page, 1024)
        click(page, r, BP)
        vis_phone = sw(r)["visible"]
        page.screenshot(path=os.path.join(OUT, "sw1_1024_phone.png"))
        click(page, r, BT)
        ok1 = all(vis[w] for w in (1024, 800, 700, 600)) and not vis[599] and not vis[390] and vis_phone
        rec("1 표시: 1024·800·700·600 보임, 599·390 숨김, 1024 폰모드 뒤에도 보임", "visible 표", {"by_width": vis, "1024_phone": vis_phone}, "PASS" if ok1 else "FAIL")

        # 2. 겹침(ko/en, 600~800)
        ov = {}
        for lang, widths in (("ko", (600, 700, 800)), ("en", (600, 650, 700, 800))):
            r.call("lang.set", code=lang)
            time.sleep(1.0)
            for w in widths:
                width(page, w)
                mb, sb, db = f(r, "shell.menu").get("bounds"), f(r, SW).get("bounds"), f(r, "shell.titleDragArea").get("bounds")
                page.screenshot(path=os.path.join(OUT, "sw2_%s_w%d.png" % (lang, w)), clip={"x": 0, "y": 0, "width": w, "height": 40})
                tb, ph = f(r, BT), f(r, BP)
                ov["%s_%d" % (lang, w)] = {"menu": mb, "switch": sb, "drag": db, "menu_x_switch": overlap(mb, sb), "switch_x_drag": overlap(sb, db),
                                           "switch_in_view": bool(sb) and sb["x"] >= 0 and sb["x"] + sb["w"] <= w,
                                           "btn_texts": [tb.get("text"), ph.get("text")], "btn_w": [(tb.get("bounds") or {}).get("w"), (ph.get("bounds") or {}).get("w")]}
        r.call("lang.set", code="ko")
        width(page, 1024)
        ok2 = all(v["menu_x_switch"] is False and v["switch_x_drag"] is False and v["switch_in_view"] for v in ov.values())
        rec("2 겹침·잘림 없음(ko 600·700·800, en 600·650·700·800, 글자 잘림은 캡처로 확인)", "menu·switch·drag bounds 서로 겹치지 않음, 스위치가 화면 안", ov, "PASS" if ok2 else "FAIL")

        # 3. 전환 3회 왕복(1024, Grid 선택 300)
        r.call("docs.activate", doc=g)
        r.call("grid.select", id="grid.dgMain", index=300, scroll=True, doc=g)
        time.sleep(0.8)
        s0 = gv(r, g)
        trips = []
        for i in range(3):
            for btn, nm in ((BP, "phone"), (BT, "tablet")):
                click(page, r, btn)
                time.sleep(0.4)
                st = gv(r, g)
                c, _ = count(r, ["grid.dgMain", "gantt.lblZoom", "form.txtTitle"])
                trips.append({"trip": i + 1, "to": nm, "mode": mode(r)["uiMode"], **st, "inst": {k: c[k] for k in ("grid.dgMain", "gantt.lblZoom", "form.txtTitle")}})
        # 기준(odev-2r 정정): 폰 단계 = 활성 문서(grid) 1·비활성(gantt·form) 0, 태블릿 복귀 = 각 1·viewHash 처음과 같음
        def inst_ok(x):
            if x["to"] == "phone":
                return x["inst"]["grid.dgMain"] == 1 and x["inst"]["gantt.lblZoom"] == 0 and x["inst"]["form.txtTitle"] == 0
            return all(v == 1 for v in x["inst"].values())
        ok3 = all(x["gridHash"] == s0["gridHash"] and x["viewHash"] == s0["viewHash"] and x["selectedIndex"] == 300 and x["first"] is not None
                  and abs(x["first"] - s0["first"]) <= 1 and inst_ok(x) and x["mode"].lower() == x["to"] for x in trips)
        rec("3 스위치 전환 3회 왕복: gridHash·viewHash·선택 300·첫 행 유지, 폰 활성 1·비활성 0 / 태블릿 각 1", "매 단계 처음과 같음", {"start": s0, "trips": trips}, "PASS" if ok3 else "FAIL")

        # 4. 표시 상태(현재 모드 칸만 checked, 같은 칸 다시 눌러도 유지)
        # 재판정 절차: 다른 칸 클릭(즉시 전환) → 같은 칸 5회 연속 재클릭, 클릭 직후·100ms 뒤 두 번 읽기(폰·태블릿 각각)
        def read4(nm, n, when):
            s = sw(r)
            m = mode(r)
            return {"target": nm, "n": n, "when": when, "mode": m["uiMode"], "isCompact": m["isCompact"], "tablet_checked": s["tablet"]["checked"], "phone_checked": s["phone"]["checked"]}

        def quick_click(btn):
            bb = f(r, btn).get("bounds")
            page.mouse.click(bb["x"] + bb["w"] / 2, bb["y"] + bb["h"] / 2)

        st4 = []
        for btn, nm in ((BP, "phone"), (BT, "tablet")):
            for n in range(6):  # n=0 은 다른 칸에서 전환, 1~5 는 같은 칸 재클릭
                quick_click(btn)
                st4.append(read4(nm, n, "직후"))
                time.sleep(0.1)
                st4.append(read4(nm, n, "100ms"))
                time.sleep(0.5)

        def good(x):
            want_c = x["target"] == "phone"
            single = (x["phone_checked"] is True and x["tablet_checked"] is False) if want_c else (x["tablet_checked"] is True and x["phone_checked"] is False)
            return single and x["mode"].lower() == x["target"] and x["isCompact"] is want_c
        ok4 = all(good(x) for x in st4)
        rec("4 체크 상태: 다른 칸 클릭 즉시 전환(ui.mode·isCompact·체크), 같은 칸 5회 재클릭 동안 체크 유지·동시 해제 없음(직후·100ms)", "24회 읽기 모두 대상 칸만 checked·모드 일치",
            {"bad": [x for x in st4 if not good(x)], "reads": st4}, "PASS" if ok4 else "FAIL")

        # 5. 저장(새로고침·폭 390→1024)
        st5 = {}
        for btn, nm in ((BP, "phone"), (BT, "tablet")):
            click(page, r, btn)
            time.sleep(1.5)
            page.reload()
            page.wait_for_function("() => window.pdAutomation && window.pdAutomation.invoke", timeout=60000)
            for _ in range(60):
                if r.call("status") is not None:
                    break
                time.sleep(1)
            time.sleep(1.5)
            after_reload = mode(r)
            width(page, 390)
            narrow = mode(r)
            width(page, 1024)
            back = mode(r)
            s = sw(r)
            st5[nm] = {"after_reload": after_reload, "narrow390": narrow, "back1024": back, "switch": {"tablet": s["tablet"]["checked"], "phone": s["phone"]["checked"]}}
        ok5 = all(st5[n]["after_reload"]["uiMode"].lower() == n and st5[n]["back1024"]["uiMode"].lower() == n and st5[n]["narrow390"]["isCompact"] for n in ("phone", "tablet"))
        rec("5 저장: 새로고침 뒤 유지, 390→1024 뒤 저장값 복귀(폰·태블릿)", "after_reload·back1024 uiMode = 저장값", st5, "PASS" if ok5 else "FAIL")

        # 6. 용어(ko/en, 스위치·메뉴)
        terms = {}
        for lang in ("ko", "en"):
            r.call("lang.set", code=lang)
            time.sleep(1.0)
            s = sw(r)
            tips = [(r.call("ui.find", id=i) or {}).get("tip") or (r.call("ui.find", id=i) or {}).get("toolTip") for i in (BT, BP)]
            terms[lang] = {"switch": [s["tablet"]["text"], s["phone"]["text"]], "switch_tip": tips, "menu": [x["header"] for x in menu(r)]}
            open_menu_shot(page, r, "sw6_menu_%s.png" % lang)
        r.call("lang.set", code="ko")
        ok6 = terms["ko"]["switch"] == ["태블릿모드", "폰모드"] and "태블릿모드" in terms["ko"]["menu"] and "폰모드" in terms["ko"]["menu"] \
            and terms["en"]["switch"] == ["Tablet", "Phone"] and "Tablet mode" in terms["en"]["menu"] and "Phone mode" in terms["en"]["menu"]
        # en 스위치 툴팁 'Tablet mode/Phone mode' 는 ui.find 가 툴팁을 돌려줄 때만 판정(값 없으면 기록)
        rec("6 용어: ko 스위치·메뉴 태블릿모드/폰모드, en 스위치 Tablet/Phone·메뉴(툴팁) Tablet mode/Phone mode", "글자 일치", terms, "PASS" if ok6 else "FAIL")

        # 8. 좁은 폭 메뉴 체크(태블릿 저장 → 390 → 1024, 자동 저장 → 390 → 1024)
        st8 = {}
        for saved in ("tablet", "auto"):
            if saved == "tablet":
                click(page, r, BT)
            else:
                r.call("ui.mode", set="auto")
                time.sleep(1.0)
            width(page, 390)
            mn = menu(r)
            open_menu_shot(page, r, "sw8_%s_390.png" % saved)
            narrow = {"mode": mode(r), "checked": checked_label(mn), "items": mn}
            width(page, 1024)
            mw = menu(r)
            open_menu_shot(page, r, "sw8_%s_1024.png" % saved)
            wide = {"mode": mode(r), "checked": checked_label(mw), "switch": sw(r)}
            st8[saved] = {"narrow": narrow, "wide": wide}
        tn, tw, an, aw = st8["tablet"]["narrow"], st8["tablet"]["wide"], st8["auto"]["narrow"], st8["auto"]["wide"]
        tab_item = next((x for x in tn["items"] if x["header"] == "태블릿모드"), {})
        ph_item = next((x for x in tn["items"] if x["header"] == "폰모드"), {})
        ok8 = tn["checked"] == ["폰모드"] and tab_item.get("effectivelyEnabled") is False and str(ph_item.get("tip") or "").startswith("좁은 화면이라 폰모드 적용 중") \
            and tw["checked"] == ["태블릿모드"] and tw["mode"]["isCompact"] is False \
            and an["checked"] == ["폰모드"] and aw["checked"] == ["자동"]
        rec("8 좁은 폭 메뉴 체크: 태블릿 저장 390→체크 폰모드(태블릿 비활성·툴팁)·1024→태블릿모드, 자동 저장 390→폰모드·1024→자동", "체크·활성·툴팁", st8, "PASS" if ok8 else "FAIL")

        r.call("ui.mode", set="auto")
        res.append({"item": "마무리", "actual": mode(r), "verdict": "기록"})
        with open(os.path.join(OUT, "retest_web32_switch.json"), "w", encoding="utf-8") as fo:
            json.dump(res, fo, ensure_ascii=False, indent=1)
        b.close()


if __name__ == "__main__":
    main()
