# AC-7-06 웹 측정 — 설정 화면이 PC 폭에서 Flyout(창이 아님)로, 폰 폭(모바일형 NAV)에서 ☰ 시트·하단 탭으로 열리는지 Playwright 실입력으로 잰다. 판정 없이 raw JSON 만 낸다
# 실행: python3 -B ui28_web706.py --bundle <wwwroot> --out <raw.json> --shots <폴더> [--port 5801]. 판정은 test/web/judge/ui28_web706_rules.py
import argparse
import http.server
import json
import os
import socketserver
import threading
import time
from functools import partial

from playwright.sync_api import sync_playwright

ARGS = ["--use-angle=swiftshader", "--enable-unsafe-swiftshader", "--ignore-gpu-blocklist", "--enable-webgl"]


class W:
    def __init__(self, p, port, bundle, viewport, shots, errors):
        self.b = p.chromium.launch(headless=True, args=ARGS)
        self.vp = viewport
        self.page = self.b.new_page(viewport={"width": viewport[0], "height": viewport[1]})
        self.shots = shots
        self.page.on("console", lambda m: errors.append(m.text[:300]) if m.type == "error" else None)
        self.page.on("pageerror", lambda e: errors.append("pageerror: " + str(e)[:300]))
        self.page.goto("http://127.0.0.1:%d/?fresh" % port, wait_until="load", timeout=90000)
        self.page.wait_for_function("() => window.pdAutomation && window.pdAutomation.invoke", timeout=120000)
        for _ in range(60):
            if self.call("status").get("ok"):
                break
            time.sleep(1)
        time.sleep(1.5)

    def call(self, cmd, **a):
        try:
            return json.loads(self.page.evaluate("b => window.pdAutomation.invoke(b)", json.dumps({"cmd": cmd, "args": a}, ensure_ascii=False)))
        except Exception as e:  # noqa: BLE001
            return {"ok": False, "error": "EXC " + str(e)[:200]}

    def res(self, cmd, **a):
        r = self.call(cmd, **a)
        return r.get("result") if r.get("ok") else None

    def find(self, cid):
        return self.res("ui.find", id=cid)

    def click_id(self, cid):
        f = self.find(cid)
        b = (f or {}).get("bounds")
        if not b:
            return False
        self.page.mouse.click(b["x"] + b["w"] / 2, b["y"] + b["h"] / 2)
        return True

    def shot(self, name):
        self.page.screenshot(path=os.path.join(self.shots, name + ".png"))

    def n_windows(self):
        return len(self.res("windows.list") or [])

    def settings_view(self):
        """열린 설정 화면: settings.window 결과(범주)와 설정 관련 컨트롤 중 가장 큰 것의 좌표"""
        sw = self.call("settings.window")
        cats = []
        for c in ((sw.get("result") or {}).get("categories") or []):
            cats.append(c if isinstance(c, str) else (c.get("id") or c.get("name") or c.get("category") or str(c)))
        best = None
        t = self.res("tree.get", named=True, depth=60)
        stack = [t]
        while stack:
            n = stack.pop()
            if isinstance(n, dict):
                i = str(n.get("id") or "")
                bd = n.get("bounds")
                if "settings" in i.lower() and "navTab" not in i and "navDrawer" not in i and "mnuSys" not in i and bd and bd.get("w") and bd.get("h"):
                    if best is None or bd["w"] * bd["h"] > best[1]["w"] * best[1]["h"]:
                        best = (i, bd)
                stack.extend(n.values())
            elif isinstance(n, list):
                stack.extend(n)
        inside = bool(best) and best[1]["x"] >= -1 and best[1]["y"] >= -1 and best[1]["x"] + best[1]["w"] <= self.vp[0] + 1 and best[1]["y"] + best[1]["h"] <= self.vp[1] + 1
        return {"open_ok": bool(sw.get("ok")), "categories": cats, "raw_error": sw.get("error"), "view_id": best[0] if best else None,
                "view_bounds": best[1] if best else None, "inside_page": inside, "settings_window_raw": json.dumps(sw.get("result"), ensure_ascii=False)[:500]}

    def close_by_escape(self):
        self.page.keyboard.press("Escape")
        time.sleep(1.2)
        if not self.call("settings.window").get("ok"):
            return "real-escape"
        self.call("settings.esc")
        time.sleep(1.0)
        return "synthetic-esc" if not self.call("settings.window").get("ok") else None


def measure(bundle, port, shots):
    out, errors = {}, []
    with sync_playwright() as p:
        w = W(p, port, bundle, (1400, 900), shots, errors)
        pc = {"windows_before": w.n_windows()}
        pc["btn_found"] = w.click_id("shell.mnuSys")  # 시스템(Y) 메뉴 → 설정 두 번 클릭
        time.sleep(1.0)
        pc["btn_found"] = bool(pc["btn_found"]) and bool(w.click_id("shell.mnuSysSettings"))
        time.sleep(1.8)
        pc.update(w.settings_view())
        pc["windows_after"] = w.n_windows()
        w.shot("ac706_pc_flyout")
        how = w.close_by_escape()
        pc["closed_by"] = how
        pc["closed_by_real_escape"] = how == "real-escape"
        w.shot("ac706_pc_closed")
        out["pc"] = pc
        w.b.close()
        w = W(p, port, bundle, (390, 844), shots, errors)
        m = {"effective": (w.res("ui.style") or {}).get("effective")}
        m["hamburger_clicked"] = w.click_id("shell.btnDrawer")
        time.sleep(1.5)
        pnl = w.find("shell.navMenuPanel") or {}
        m["drawer_opened_by_hamburger"] = bool(pnl.get("effectivelyVisible")) and bool((pnl.get("bounds") or {}).get("h", 0) > 50)
        w.shot("ac706_mobile_sheet")
        # ☰ 시트 안의 설정 항목 — 시트를 휠로 스크롤해 화면 안에 들어오면 실클릭
        d = {"found": False}
        for _ in range(40):
            f = w.find("shell.navDrawer.Settings")
            bd = (f or {}).get("bounds")
            if bd and 120 < bd["y"] + bd["h"] / 2 < 740 and f.get("effectivelyVisible"):
                d["found"] = True
                w.page.mouse.click(bd["x"] + bd["w"] / 2, bd["y"] + bd["h"] / 2)
                break
            w.page.mouse.move(150, 400)
            w.page.mouse.wheel(0, 300)
            time.sleep(0.25)
        time.sleep(1.8)
        d.update(w.settings_view())
        w.shot("ac706_mobile_drawer_settings")
        d["closed_by"] = w.close_by_escape()
        d["closed"] = d["closed_by"] is not None
        m["drawer"] = d
        # 하단 탭 설정
        t = {"btn_found": w.click_id("shell.navTab.Settings")}
        time.sleep(1.8)
        t.update(w.settings_view())
        w.shot("ac706_mobile_tab_settings")
        t["closed_by"] = w.close_by_escape()
        t["closed"] = t["closed_by"] is not None
        m["tab"] = t
        out["mobile"] = m
        w.b.close()
    out["console_errors"] = errors
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--bundle", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--shots", required=True)
    ap.add_argument("--port", type=int, default=5801)
    a = ap.parse_args()
    os.makedirs(a.shots, exist_ok=True)
    http.server.SimpleHTTPRequestHandler.log_message = lambda *x: None
    httpd = socketserver.TCPServer(("127.0.0.1", a.port), partial(http.server.SimpleHTTPRequestHandler, directory=a.bundle))
    threading.Thread(target=httpd.serve_forever, daemon=True).start()
    t0 = time.time()
    res = {"started_at": time.strftime("%Y-%m-%dT%H:%M:%S%z"), "bundle": a.bundle}
    try:
        res.update(measure(a.bundle, a.port, a.shots))
    except Exception as e:  # noqa: BLE001
        res["exception"] = repr(e)[:600]
    res["finished_at"] = time.strftime("%Y-%m-%dT%H:%M:%S%z")
    res["elapsed_ms"] = round((time.time() - t0) * 1000)
    with open(a.out, "w", encoding="utf-8") as f:
        json.dump(res, f, ensure_ascii=False, indent=1)
    httpd.shutdown()
    print("saved", a.out)


if __name__ == "__main__":
    main()
