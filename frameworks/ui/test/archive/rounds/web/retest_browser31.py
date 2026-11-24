# 웹 현행 화면 모드 메뉴 실측(odev-2r) — 폭 1024·390 에서 shell.mnuUiMode visible·bounds, 보기 ▶ 화면 모드 하위 항목 캡처, 1024 에서 폰 모드 선택 후 메뉴 잔존·태블릿 모드 복귀, 끝에 자동
# 사용: PD_URL=http://127.0.0.1:5216/ PD_OUT=<폴더> python3 tools/retest_browser31.py   (진단판·헤드리스 전용)
import json
import os
import sys
import time

from playwright.sync_api import sync_playwright

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import retest_browser2 as t  # noqa: E402
from retest_browser11 import _walk, full_tree  # noqa: E402

OUT = t.OUT
res = {}


def texts(r, keys):
    tr = full_tree(r)
    out = []
    for n, path in _walk(tr.get("tree"), []):
        tx = str(n.get("text") or n.get("header") or "")
        if any(k in tx for k in keys) and n.get("bounds"):
            out.append({"type": n.get("type"), "id": n.get("id"), "text": tx, "visible": n.get("visible"), "enabled": n.get("enabled"),
                        "checked": n.get("checked") if "checked" in n else n.get("isChecked"), "bounds": n.get("bounds")})
    return out


def click(page, bb):
    page.mouse.click(bb["x"] + bb["w"] / 2, bb["y"] + bb["h"] / 2)


def open_view_menu(page, r):
    hdr = next((x for x in texts(r, ["보기"]) if x["bounds"]["y"] < 40), None)
    if hdr:
        click(page, hdr["bounds"])
        time.sleep(0.8)
    mode = next((x for x in texts(r, ["화면 모드"]) if x["bounds"]["y"] >= 30), None)
    if mode:
        page.mouse.move(mode["bounds"]["x"] + 20, mode["bounds"]["y"] + mode["bounds"]["h"] / 2)
        time.sleep(0.8)
    return hdr, mode


def main():
    with sync_playwright() as p:
        b = p.chromium.launch(headless=True, args=t.ARGS)
        for w in (1024, 390):
            ctx = t.allow_dup(b.new_context(viewport={"width": w, "height": 760}))
            ctx, page, r = t.boot(b, "?layout-key=otestw2-uimodemenu%d" % w, w=w, h=760, ctx=ctx)
            time.sleep(1.0)
            f = r.call("ui.find", id="shell.mnuUiMode")
            hdr, mode = open_view_menu(page, r)
            page.screenshot(path=os.path.join(OUT, "uimodemenu_%d_open.png" % w))
            items = texts(r, ["자동", "태블릿", "폰"])
            res[str(w)] = {"mnuUiMode": f, "view_header": hdr, "mode_item": mode, "items": items, "status": r.call("status")}
            print(w, json.dumps(res[str(w)], ensure_ascii=False)[:900], flush=True)
            if w == 1024:
                ph = next((x for x in items if "폰" in x["text"] and x["bounds"]["y"] > 30), None)
                if ph:
                    click(page, ph["bounds"])
                    time.sleep(1.2)
                    page.screenshot(path=os.path.join(OUT, "uimodemenu_1024_phone.png"))
                    st_p = r.call("ui.mode") or {}
                    f_p = r.call("ui.find", id="shell.mnuUiMode")
                    hdr2, mode2 = open_view_menu(page, r)
                    page.screenshot(path=os.path.join(OUT, "uimodemenu_1024_phone_menu.png"))
                    items2 = texts(r, ["자동", "태블릿", "폰"])
                    tb = next((x for x in items2 if "태블릿" in x["text"] and x["bounds"]["y"] > 30), None)
                    st_t = None
                    if tb:
                        click(page, tb["bounds"])
                        time.sleep(1.2)
                        st_t = r.call("ui.mode") or {}
                        page.screenshot(path=os.path.join(OUT, "uimodemenu_1024_back_tablet.png"))
                    res["1024_phone"] = {"clicked": ph, "mode_after": st_p, "mnuUiMode_after": f_p, "menu_reopened": bool(mode2), "items": items2, "tablet_clicked": tb, "mode_back": st_t}
                    print("phone", json.dumps(res["1024_phone"], ensure_ascii=False)[:900], flush=True)
            r.call("ui.mode", set="auto")
            res[str(w) + "_final"] = r.call("ui.mode")
            ctx.close()
        with open(os.path.join(OUT, "retest_web31_uimodemenu.json"), "w", encoding="utf-8") as fo:
            json.dump(res, fo, ensure_ascii=False, indent=1)
        b.close()


if __name__ == "__main__":
    main()
