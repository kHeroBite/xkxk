#!/usr/bin/env python3
# motion_web_check.py 의 회귀 케이스 — 제목 줄 즐겨찾기(추가·열기·빼기·배치 전환)와 제목 탭 누름·닫기를 실클릭으로 본다
"""
판정
  favorite   별 실클릭으로 추가되고 아이콘이 그려지며, 문서가 없을 때 아이콘 실클릭이 문서를 열고, 탭 문서 배치로 바꿔도 문서 영역이 남고,
             별을 다시 누르면 빠진다. 그동안 콘솔 오류 0
  title_tab  제목 탭 실클릭으로 활성이 바뀌고 닫기 단추 실클릭으로 그 문서만 닫힌다(ReflectionBinding $parent[ListBox] 경로). 콘솔 오류 0
단독 실행은 세로 즐겨찾기 구역(22 §4.4)을 모바일형(402×876 터치)·태블릿형(1200×800) 두 배치로 본다
  motion_web_regress.py --bundle <wwwroot> --port <포트> --out-dir <고정 폴더>
  0개면 숨고, 1개면 삼선 메뉴·탐색 패널 맨 위에 보이며, 누르면 열리고(모바일형은 서랍도 닫힘), 콘솔 오류 0. rc 0=둘 다 PASS 1=FAIL 2=실행 오류
"""
import argparse
import json
import os
import socketserver
import sys
import time

SCREEN = "SCR-PRJ-01"
STAR = "shell.explorer.star.Grid"
ICON = "shell.favorite." + SCREEN


def click(w, need, ctl_id, dx=None):
    b = need(w, "ui.find", id=ctl_id)["bounds"]
    w.page.mouse.click(b["x"] + (b["w"] / 2 if dx is None else dx), b["y"] + b["h"] / 2)
    time.sleep(2.5)


def open_ids(w, need):
    return [d["id"] for d in need(w, "style.state")["windows"]]


def case_favorite(w, need, ready):
    before, steps = len(w.errors), {}
    if SCREEN in need(w, "favorite.state")["items"]:
        return {"verdict": "UNDET", "why": "시작할 때 이미 즐겨찾기가 있다"}
    click(w, need, STAR)
    steps["added"] = SCREEN in need(w, "favorite.state")["items"]
    found = w.call("ui.find", id=ICON)
    steps["icon_drawn"] = bool(found.get("ok") and found["result"]["bounds"]["w"])
    w.shot("favorite_added")
    if steps["icon_drawn"]:
        need(w, "docs.close", all=True)
        time.sleep(2)
        click(w, need, ICON)
        steps["icon_opens"] = len(open_ids(w, need)) == 1
    templates = [t["id"] for t in need(w, "layout.template.list") if "tab" in t["id"].lower()]
    steps["has_tab_template"] = bool(templates)
    if templates:
        need(w, "layout.template.apply", id=templates[0])
        time.sleep(3)
        docs = open_ids(w, need)
        body = w.call("ui.find", id="grid.dgMain", doc=docs[0]) if docs else {}  # 본문 표(PART_Grid 는 MDI 창 머리라 탭 배치에는 없다)
        steps["layout_keeps_document"] = bool(body.get("ok") and body["result"]["bounds"]["w"])
        w.shot("favorite_tabbed")
    w.page.reload(wait_until="load", timeout=90000)  # 1건 저장된 채 다시 띄움(옛 판은 저장 뒤 죽었다)
    ready(w)
    time.sleep(3)
    steps["reload_kept"] = SCREEN in need(w, "favorite.state")["items"]
    found = w.call("ui.find", id=ICON)
    steps["reload_icon_drawn"] = bool(found.get("ok") and found["result"]["bounds"]["w"])
    w.shot("favorite_reloaded")
    if steps["reload_icon_drawn"]:
        need(w, "docs.close", all=True)
        time.sleep(2)
        click(w, need, ICON)
        steps["reload_icon_opens"] = len(open_ids(w, need)) == 1
    click(w, need, STAR)
    steps["removed"] = SCREEN not in need(w, "favorite.state")["items"]
    errors = w.errors[before:]
    ok = not errors and all(steps.values()) and "icon_opens" in steps
    return {"verdict": "PASS" if ok else "FAIL", "why": "추가·열기·배치 전환·빼기, 콘솔 오류 %d" % len(errors), "steps": steps, "errors": errors}


def case_title_tab(w, need):
    before = len(w.errors)
    docs = open_ids(w, need)
    if len(docs) < 2:
        need(w, "docs.open", kind="grid", count=2 - len(docs))
        time.sleep(2.5)
        docs = open_ids(w, need)
    target = next(d for d in docs if d != need(w, "style.state")["active"])
    click(w, need, "shell.tab." + target, dx=25)
    activated = need(w, "style.state")["active"] == target
    click(w, need, "shell.tabClose." + target)
    closed = open_ids(w, need) == [d for d in docs if d != target]
    errors = w.errors[before:]
    ok = activated and closed and not errors
    return {"verdict": "PASS" if ok else "FAIL", "why": "누름 %s·닫기 %s·콘솔 오류 %d" % (activated, closed, len(errors)), "errors": errors}


def shown(w, ctl_id):
    """보이면 자리, 아니면 None."""
    r = w.call("ui.find", id=ctl_id, props="IsEffectivelyVisible")
    if not r.get("ok") or r["result"]["props"]["IsEffectivelyVisible"] != "True" or not r["result"]["bounds"]["w"]:
        return None
    return r["result"]["bounds"]


def case_list(w, need, style):
    """세로 즐겨찾기 구역 1배치."""
    steps, mobile = {}, style == "mobile"
    press = w.page.touchscreen.tap if mobile else w.page.mouse.click
    if not mobile:
        need(w, "ui.style", set=style)
        time.sleep(3)
    steps["style"] = str(need(w, "ui.style")["effective"]).lower() == style
    need(w, "docs.close", all=True)  # 서랍을 열기 전에 비운다(열린 뒤 명령을 보내면 서랍 상태가 바뀔 수 있다)
    time.sleep(1.5)
    if mobile:
        b = need(w, "ui.find", id="shell.btnDrawer")["bounds"]
        press(b["x"] + b["w"] / 2, b["y"] + b["h"] / 2)
        time.sleep(2)
    steps["hidden_when_empty"] = not shown(w, "shell.favoriteList")
    need(w, "favorite.toggle", screen=SCREEN)
    time.sleep(2)
    w.shot("favorite_list_" + style)
    area, item = shown(w, "shell.favoriteList"), shown(w, "shell.favoriteList." + SCREEN)
    below = shown(w, "shell.navMenuPanel" if mobile else STAR)
    steps["shown"] = bool(area and item)
    if area and below:
        steps["at_top"] = area["y"] - below["y"] < 8 if mobile else area["y"] + area["h"] <= below["y"] + 1
    if item:
        before = w.shot("favorite_list_%s_before_press" % style)
        press(item["x"] + item["w"] / 2, item["y"] + item["h"] / 2)
        time.sleep(3)
        steps["opens"] = len(open_ids(w, need)) == 1
        after = w.shot("favorite_list_%s_opened" % style)
        if mobile and area:  # 닫힌 서랍도 트리에는 보임으로 남으므로 구역 제목 글자가 화면에서 사라졌는지로 본다
            title = (int(area["x"]) + 2, int(area["y"]) + 2, int(area["x"]) + 60, int(area["y"]) + 22)
            steps["drawer_closed"] = before.crop(title).tobytes() != after.crop(title).tobytes()
    ok = all(steps.values()) and {"shown", "at_top", "opens"} <= set(steps) and not w.errors
    return {"verdict": "PASS" if ok else "FAIL", "steps": steps, "errors": list(w.errors)}


def main():
    sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
    import motion_web_check as check  # 준비 대기·명령 호출 재사용(서로 import 하므로 여기서 읽는다)
    from playwright.sync_api import Error as PlaywrightError, sync_playwright

    ap = argparse.ArgumentParser()
    ap.add_argument("--bundle", required=True)
    ap.add_argument("--port", type=int, required=True)
    ap.add_argument("--out-dir", required=True)
    args = ap.parse_args()
    if args.port in (5180, 5220):
        ap.error("5180·5220 은 쓰지 않는다")
    os.makedirs(args.out_dir, exist_ok=True)
    socketserver.TCPServer.allow_reuse_address = True
    w, res = check.Web(os.path.abspath(args.bundle), args.port, args.out_dir), {}
    layouts = (("mobile", dict(viewport={"width": 402, "height": 876}, has_touch=True, is_mobile=True)),
               ("tablet", dict(viewport={"width": 1200, "height": 800})))
    try:
        with sync_playwright() as p:
            w.browser = p.chromium.launch(headless=True, args=check.ARGS)
            for style, options in layouts:
                context = w.browser.new_context(**options)  # 배치마다 빈 저장소
                w.page, w.errors = context.new_page(), []
                w.page.on("pageerror", lambda e: w.errors.append("pageerror: " + str(e)[:300]))
                w.page.on("console", lambda m: w.errors.append(m.text[:300]) if m.type == "error" else None)
                try:
                    w.page.goto("http://127.0.0.1:%d/" % args.port, wait_until="load", timeout=90000)
                    check.ready(w)
                    res[style] = case_list(w, check.need, style)
                except (RuntimeError, KeyError, check.Missing, PlaywrightError) as e:
                    res[style] = {"verdict": "ERROR", "why": repr(e)[:300]}
                print(style, res[style], flush=True)
                context.close()
            w.browser.close()
    finally:
        w.httpd.shutdown()
    with open(os.path.join(args.out_dir, "motion_web_favorite_lists.json"), "w", encoding="utf-8") as f:
        json.dump(res, f, ensure_ascii=False, indent=1)
    verdicts = {c["verdict"] for c in res.values()}
    return 2 if "ERROR" in verdicts else 1 if "FAIL" in verdicts else 0


if __name__ == "__main__":
    sys.exit(main())
