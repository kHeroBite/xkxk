#!/usr/bin/env python3
# 웹 즐겨찾기 조작(넘침·끌어서 순서·우클릭/길게 눌러 빼기)과 제목 탭 불러오는 중 표시(3방식·지연·최소 표시)를 실입력으로 잰다
"""
사용  motion_web_favorite_ops.py --bundle <wwwroot> --port <포트> --out-dir <고정 폴더> [--only <케이스,…>]
설정은 저장소(pd-poc-settings)에 운영 값을 넣고 다시 띄워서 준다. 케이스마다 빈 브라우저 컨텍스트다.
  overflow        폭 상한이 좁으면 » 로 넘어가고, » 안 항목을 누르면 그 화면이 열린다
  drag_order      끌어서 순서가 바뀌고 새로고침 뒤에도 그대로다
  remove_right    제목 줄 아이콘 우클릭 빼기, 새로고침 뒤에도 빠져 있다
  remove_hold     모바일형 삼선 메뉴 항목 길게 눌러 빼기, 0개면 구역이 숨는다
  busy_icon       아이콘만: 불러오는 동안 탭 그림이 돈다
  busy_inline     제목만+inline: 불러오는 동안만 제목 앞에 아이콘이 끼어든다
  busy_underline  제목만+underline: 불러오는 동안만 아래 줄이 보인다
  busy_delay      show_delay 1500: 처음에는 안 보이고 뒤에 보인다
  busy_min        min_visible 3000: 짧은 불러오기 뒤에도 남았다가 사라진다
rc 0=전부 PASS 1=FAIL 2=실행 오류
"""
import argparse
import json
import os
import socketserver
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from playwright.sync_api import Error as PlaywrightError, sync_playwright  # noqa: E402
import motion_web_check as check  # noqa: E402
from motion_web_regress import click, open_ids, shown  # noqa: E402

need = check.need
SCREENS = ("SCR-PRJ-01", "SCR-SCH-01", "SCR-PRJ-02")
PHONE = dict(viewport={"width": 402, "height": 876}, has_touch=True, is_mobile=True)


def middle(b):
    return b["x"] + b["w"] / 2, b["y"] + b["h"] / 2


def reload(w):
    time.sleep(1)
    w.page.reload(wait_until="load", timeout=90000)
    check.ready(w)
    time.sleep(2)


def seed(w, **values):
    w.page.evaluate("""v => {
      const s = JSON.parse(localStorage.getItem('pd-poc-settings') || '{}');
      localStorage.setItem('pd-poc-settings', JSON.stringify(Object.assign(s, v)));
    }""", values)
    reload(w)


def add(w, count=3):
    for screen in SCREENS[:count]:
        need(w, "favorite.toggle", screen=screen)
    time.sleep(1.5)
    return need(w, "favorite.state")


def overflow(w):
    seed(w, FavoriteBarMaxWidthPct=5)
    state = add(w)
    steps = {"split": bool(state["overflow"]) and len(state["visible"]) + len(state["overflow"]) == 3,
             "button": bool(shown(w, "shell.favoriteOverflow"))}
    need(w, "docs.close", all=True)
    time.sleep(1.5)
    if steps["button"] and state["overflow"]:
        click(w, need, "shell.favoriteOverflow")
        w.shot("overflow")
        item = shown(w, "shell.favorite." + state["overflow"][0])
        steps["popup_item"] = bool(item)
        if item:
            w.page.mouse.click(*middle(item))
            time.sleep(3)
            steps["opens"] = len(open_ids(w, need)) == 1
    return steps


def drag_order(w):
    first = add(w)["items"]
    a, b = [middle(need(w, "ui.find", id="shell.favorite." + s)["bounds"]) for s in (first[0], first[2])]
    docs = open_ids(w, need)
    w.page.mouse.move(*a)
    w.page.mouse.down()
    w.page.mouse.move(*b, steps=12)
    w.page.mouse.up()
    time.sleep(2)
    after = need(w, "favorite.state")["items"]
    steps = {"moved": after == [first[1], first[2], first[0]], "drag_does_not_open": open_ids(w, need) == docs}
    reload(w)
    steps["kept_after_reload"] = need(w, "favorite.state")["items"] == after
    return steps


def remove_right(w):
    items = add(w, 2)["items"]
    w.page.mouse.click(*middle(need(w, "ui.find", id="shell.favorite." + items[0])["bounds"]), button="right")
    time.sleep(1.5)
    w.shot("remove_right")
    steps = {"menu": bool(shown(w, "shell.ctxFavoriteRemove"))}
    if steps["menu"]:
        click(w, need, "shell.ctxFavoriteRemove")
        steps["removed"] = need(w, "favorite.state")["items"] == items[1:]
        reload(w)
        steps["kept_after_reload"] = need(w, "favorite.state")["items"] == items[1:]
    return steps


def remove_hold(w):
    items = add(w, 1)["items"]
    reload(w)
    steps = {"kept_after_reload": need(w, "favorite.state")["items"] == items}
    w.page.touchscreen.tap(*middle(need(w, "ui.find", id="shell.btnDrawer")["bounds"]))
    time.sleep(2)
    x, y = middle(need(w, "ui.find", id="shell.favoriteList." + items[0])["bounds"])
    cdp = w.page.context.new_cdp_session(w.page)
    cdp.send("Input.dispatchTouchEvent", {"type": "touchStart", "touchPoints": [{"x": x, "y": y}]})
    time.sleep(1.5)
    cdp.send("Input.dispatchTouchEvent", {"type": "touchEnd", "touchPoints": []})
    time.sleep(1.5)
    w.shot("remove_hold")
    menu = shown(w, "shell.ctxFavoriteListRemove")
    steps["menu"] = bool(menu)
    if menu:
        w.page.touchscreen.tap(*middle(menu))
        time.sleep(2)
        steps["removed"] = need(w, "favorite.state")["items"] == []
        steps["hidden_at_zero"] = not shown(w, "shell.favoriteList")
    return steps


def loading(w, delay_ms):
    """불러오기가 걸린 새 문서를 연다."""
    need(w, "sample.load", delayMs=delay_ms)
    known = set(open_ids(w, need))
    need(w, "docs.open", kind="grid", allowDuplicate=True)
    started = time.time()
    return next(d for d in open_ids(w, need) if d not in known), started


def wait_loaded(w, doc):
    until = time.time() + 20
    while time.time() < until:
        if not any(d["id"] == doc and d["isLoading"] for d in need(w, "window.load.state")["docs"]):
            return
        time.sleep(0.2)
    raise RuntimeError("불러오기가 끝나지 않았다")


def busy_icon(w):
    seed(w, TitleTabDisplay="icon")
    doc, _ = loading(w, 7000)
    time.sleep(1)
    b = need(w, "ui.find", id="shell.tab." + doc)["bounds"]
    box = (int(b["x"]), int(b["y"]), int(b["x"] + b["w"]), int(b["y"] + b["h"]))
    moving = set()
    for i in range(3):
        moving.add(w.shot("busy_icon_%d" % i).crop(box).tobytes())
        time.sleep(0.23)
    steps = {"spins": len(moving) >= 2, "title_hidden": not shown(w, "shell.tabTitle." + doc)}
    wait_loaded(w, doc)
    time.sleep(1.5)
    still = w.shot("busy_icon_end").crop(box).tobytes()
    time.sleep(0.4)
    steps["stops"] = still == w.shot("busy_icon_end2").crop(box).tobytes() and still not in moving
    return steps


def busy_inline(w):
    seed(w, TitleTabDisplay="title")  # title_only 기본 = inline
    doc, _ = loading(w, 5000)
    time.sleep(1)
    busy = need(w, "ui.find", id="shell.tabTitle." + doc)["bounds"]["x"]
    w.shot("busy_inline")
    wait_loaded(w, doc)
    time.sleep(1.5)
    idle = need(w, "ui.find", id="shell.tabTitle." + doc)["bounds"]["x"]
    return {"icon_inserted_while_busy": busy - idle >= 12, "no_underline": not shown(w, "shell.tabBusyLine." + doc)}


def busy_underline(w):
    seed(w, TitleTabDisplay="title", BusyIconTitleOnly="underline")
    doc, _ = loading(w, 5000)
    time.sleep(1)
    steps = {"line_while_busy": bool(shown(w, "shell.tabBusyLine." + doc))}
    w.shot("busy_underline")
    wait_loaded(w, doc)
    time.sleep(1.5)
    steps["line_gone"] = not shown(w, "shell.tabBusyLine." + doc)
    return steps


def busy_delay(w):
    seed(w, TitleTabDisplay="title", BusyIconTitleOnly="underline", BusyIconShowDelayMs=1500)
    doc, started = loading(w, 6000)
    early = bool(shown(w, "shell.tabBusyLine." + doc))
    if time.time() - started > 1.3:
        raise RuntimeError("지연 안에 표본을 못 잡았다")
    time.sleep(max(0.0, 3 - (time.time() - started)))
    return {"hidden_before_delay": not early, "shown_after_delay": bool(shown(w, "shell.tabBusyLine." + doc))}


def busy_min(w):
    seed(w, TitleTabDisplay="title", BusyIconTitleOnly="underline", BusyIconMinVisibleMs=3000)
    doc, started = loading(w, 300)
    wait_loaded(w, doc)
    if time.time() - started > 2.5:
        raise RuntimeError("최소 표시 안에 표본을 못 잡았다")
    steps = {"kept_after_load": bool(shown(w, "shell.tabBusyLine." + doc))}
    time.sleep(4)
    steps["gone_after_min"] = not shown(w, "shell.tabBusyLine." + doc)
    return steps


CASES = ((overflow, None), (drag_order, None), (remove_right, None), (remove_hold, PHONE),
         (busy_icon, None), (busy_inline, None), (busy_underline, None), (busy_delay, None), (busy_min, None))


def main():
    ap = argparse.ArgumentParser()
    for name in ("--bundle", "--out-dir"):
        ap.add_argument(name, required=True)
    ap.add_argument("--port", type=int, required=True)
    ap.add_argument("--only", default="")
    args = ap.parse_args()
    if args.port in (5180, 5220):
        ap.error("5180·5220 은 쓰지 않는다")
    os.makedirs(args.out_dir, exist_ok=True)
    socketserver.TCPServer.allow_reuse_address = True
    w, res = check.Web(os.path.abspath(args.bundle), args.port, args.out_dir), {}
    only = set(filter(None, args.only.split(",")))
    try:
        with sync_playwright() as p:
            w.browser = p.chromium.launch(headless=True, args=check.ARGS)
            for fn, options in CASES:
                if only and fn.__name__ not in only:
                    continue
                context = w.browser.new_context(**(options or {"viewport": {"width": 1200, "height": 800}}))
                w.page, w.errors = context.new_page(), []
                w.page.on("pageerror", lambda e: w.errors.append("pageerror: " + str(e)[:300]))
                w.page.on("console", lambda m: w.errors.append(m.text[:300]) if m.type == "error" else None)
                try:
                    w.page.goto("http://127.0.0.1:%d/" % args.port, wait_until="load", timeout=90000)
                    check.ready(w)
                    steps = fn(w)
                    ok = all(steps.values()) and not w.errors
                    res[fn.__name__] = {"verdict": "PASS" if ok else "FAIL", "steps": steps, "errors": list(w.errors)}
                except (RuntimeError, KeyError, StopIteration, check.Missing, PlaywrightError) as e:
                    res[fn.__name__] = {"verdict": "ERROR", "why": repr(e)[:300], "errors": list(w.errors)}
                print(fn.__name__, res[fn.__name__], flush=True)
                context.close()
            w.browser.close()
    finally:
        w.httpd.shutdown()
    with open(os.path.join(args.out_dir, "motion_web_favorite_ops.json"), "w", encoding="utf-8") as f:
        json.dump(res, f, ensure_ascii=False, indent=1)
    verdicts = {c["verdict"] for c in res.values()}
    rc = 2 if "ERROR" in verdicts else 1 if "FAIL" in verdicts else 0
    print("RESULT rc=%d" % rc, flush=True)
    return rc


if __name__ == "__main__":
    sys.exit(main())
