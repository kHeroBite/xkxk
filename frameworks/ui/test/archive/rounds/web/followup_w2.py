# i2000rc 웹 후속 확인 — D-DAD2 4b-1 5회(서빙 wasm md5 포함)와 PW1·PW2 split.dump 단계별 기록(otest-w-2)
# 사용: python3 -u followup_w2.py <A|B|AB>   (A=4b-1 5회, B=PW 진단)
import hashlib
import json
import sys
import time
import urllib.request

sys.path.insert(0, "/mnt/c/DATA/Project/xkxk/frameworks/ui/tools")
sys.path.insert(0, "/mnt/c/DATA/Project/xkxk/frameworks/ui/test/common")  # D-TESTDIR: test/common/_paths.py
import _paths  # noqa: E402
_paths.add()
from playwright.sync_api import sync_playwright
from ui20_lib import WebRemote

BASE = "http://127.0.0.1:5261"
OUT = "/mnt/c/DATA/Project/xkxk/frameworks/ui/logs/otest_w1/i2000rc/followup_w2"
WHICH = sys.argv[1] if len(sys.argv) > 1 else "AB"
res = {"at": time.strftime("%Y-%m-%d %H:%M:%S"), "build": "i2000rc"}


def walk(x, p=()):
    yield x, p
    for c in x.get("children", []) or []:
        yield from walk(c, p + (x,))


def served_md5():
    html = urllib.request.urlopen(BASE + "/_framework/blazor.boot.json", timeout=10).read() if False else None
    out = {}
    import re
    idx = urllib.request.urlopen(BASE + "/index.html", timeout=10).read().decode("utf-8", "ignore")
    m = re.findall(r"Pd\.Poc\.Shell\.[A-Za-z0-9]+\.wasm", idx)
    names = sorted(set(m))
    if not names:
        # index 에 이름이 없으면 main.js 쪽에서 찾는다
        for js in ("/main.js", "/_framework/dotnet.js"):
            try:
                t = urllib.request.urlopen(BASE + js, timeout=10).read().decode("utf-8", "ignore")
                names = sorted(set(re.findall(r"Pd\.Poc\.Shell\.[A-Za-z0-9]+\.wasm", t)))
                if names:
                    break
            except Exception:
                pass
    for n in names:
        data = urllib.request.urlopen(BASE + "/_framework/" + n, timeout=60).read()
        out[n] = hashlib.md5(data).hexdigest()[:8]
    return out


def open_page(pw, w=1280, h=800):
    b = pw.chromium.launch(headless=True, args=WebRemote.ARGS)
    ctx = b.new_context(viewport={"width": w, "height": h}, has_touch=False)
    ctx.add_init_script(WebRemote.ALLOW_DUP_JS)
    page = ctx.new_page()
    cons = []
    page.on("console", lambda m: cons.append(m.text))
    page.goto(BASE + "/?fresh")
    page.wait_for_function("() => window.pdAutomation && window.pdAutomation.invoke", timeout=90000)
    time.sleep(3)

    def inv(cmd, **a):
        t = page.evaluate("async (x) => await window.pdAutomation.invoke(x)", json.dumps({"cmd": cmd, "args": a}))
        d = json.loads(t) if isinstance(t, str) else t
        return d.get("result", {"_error": d.get("error")})
    for _ in range(30):
        pl = inv("panel.list")
        n = next((p for p in pl if p.get("id") == "NotificationPane"), {}) if isinstance(pl, list) else {}
        if "visible" in n and "bounds" in n:
            break
        time.sleep(1)
    return b, page, inv, cons


def snap(inv, tag):
    d = inv("split.dump")
    bd = inv("panel.bounds")
    txt = json.dumps(d, ensure_ascii=False)
    return {"tag": tag, "dump": d, "nan_in_dump": ("NaN" in txt or "nan" in txt.lower()),
            "bounds": {k: v for k, v in bd.items() if k in ("LeftPane", "BottomPane", "NotificationPane", "CenterPane")} if isinstance(bd, dict) else bd}


with sync_playwright() as pw:
    if "A" in WHICH:
        a = {"served_md5": served_md5(), "runs": []}
        for i in range(1, 6):
            b, page, inv, cons = open_page(pw)
            time.sleep(7)
            r = inv("dock.asDocument", id="LeftPane")
            a["runs"].append({"n": i, "at": time.strftime("%H:%M:%S"), "resp": r, "docs": len(inv("docs.list") or []),
                              "served_md5_now": served_md5()})
            print("4b-1", i, json.dumps(r, ensure_ascii=False)[:200], flush=True)
            b.close()
            time.sleep(2)
        a["verdict_each"] = ["Rejected" if x["resp"].get("status") == "Rejected" else str(x["resp"].get("status")) for x in a["runs"]]
        res["DAD2_4b1"] = a
    if "B" in WHICH:
        # PW2: 조작 전 → 접기(pin) 직전 → 메뉴 도킹·join·unpin 뒤
        b, page, inv, cons = open_page(pw)
        pw2 = {"s0_before_any": snap(inv, "조작 전")}
        pw2["s1_before_pin"] = snap(inv, "LeftPane 접기 직전")
        pw2["pin"] = inv("panel.pin", id="LeftPane", on="true")
        time.sleep(0.5)
        pw2["s2_after_pin"] = snap(inv, "접은 직후")
        t = inv("tree.get", named="false")
        pi = [x["bounds"] for top in t["tree"] for x, _ in walk(top) if x.get("type") == "ToolPinItemControl"]
        if pi:
            bx = pi[0]
            page.mouse.click(bx["x"] + bx["w"] / 2, bx["y"] + bx["h"] / 2, button="right")
            time.sleep(0.8)
            t = inv("tree.get", named="false")
            menu = [(x, p) for top in t["tree"] for x, p in walk(top) if x.get("type") == "MenuItem" and any(q.get("type") == "ContextMenu" for q in p)]
            pw2["menuItems"] = len(menu)
            if len(menu) >= 3:
                m = menu[2][0]["bounds"]
                page.mouse.click(m["x"] + m["w"] / 2, m["y"] + m["h"] / 2)
                time.sleep(1)
        pw2["join"] = inv("panel.join", id="LeftPane", tool="Explorer")
        pw2["unpin"] = inv("panel.pin", id="LeftPane", on="false")
        time.sleep(1.5)
        pw2["s3_end"] = snap(inv, "되살린 뒤")
        pw2["split_log"] = [c for c in cons if "[split]" in c]
        b.close()
        res["PW2"] = pw2
        # PW1-2: 끌지 않은 처음 상태에서 bottom → right
        b, page, inv, cons = open_page(pw)
        p12 = {"s0": snap(inv, "조작 전")}
        p12["dock_bottom"] = inv("panel.dock", id="NotificationPane", side="bottom")
        p12["s1_after_bottom"] = snap(inv, "bottom 직후")
        p12["dock_right"] = inv("panel.dock", id="NotificationPane", side="right")
        p12["s2_after_right"] = snap(inv, "right 직후")
        p12["split_log"] = [c for c in cons if "[split]" in c]
        b.close()
        res["PW1-2"] = p12
        # PW1-1: 알림 폭을 끌어 바꾼 직후 slot 비율 → bottom → right
        b, page, inv, cons = open_page(pw)
        p11 = {"s0": snap(inv, "조작 전")}
        p11["drag"] = inv("split.drag", area="Right", at="outer", delta=-60)
        p11["s1_after_drag"] = snap(inv, "끈 직후")
        p11["dock_bottom"] = inv("panel.dock", id="NotificationPane", side="bottom")
        p11["s2_after_bottom"] = snap(inv, "bottom 직후")
        p11["dock_right"] = inv("panel.dock", id="NotificationPane", side="right")
        p11["s3_after_right"] = snap(inv, "right 직후")
        p11["split_log"] = [c for c in cons if "[split]" in c]
        b.close()
        res["PW1-1"] = p11
json.dump(res, open(OUT + "_%s.json" % WHICH, "w"), ensure_ascii=False, indent=1)
print("saved", OUT + "_%s.json" % WHICH)
