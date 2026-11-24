# 요건 2·4·6 웹 축 측정(AC-2-09 포인터 추적 스피너·AC-4-05 관리형 창 복구·AC-6-09 예약키 표기) — 판정 없이 raw JSON 만 낸다(판정은 test/web/judge/ui28_web_rules.py)
# 실행: python3 -B ui28_web.py --bundle <wwwroot> --out <raw.json> --shots <폴더> [--only ac409,ac405,ac609] [--port 5701]
# 입력은 Playwright 실마우스(headless Chrome 안) — 사용자 PC 입력과 무관하다. 번들은 127.0.0.1 정적 서버로 연다.
import argparse
import http.server
import io
import json
import os
import socketserver
import threading
import time
from functools import partial

from playwright.sync_api import sync_playwright
from PIL import Image, ImageChops

ARGS = ["--use-angle=swiftshader", "--enable-unsafe-swiftshader", "--ignore-gpu-blocklist", "--enable-webgl"]


class Web:
    def __init__(self, bundle, port, shots, viewport=(1400, 900)):
        http.server.SimpleHTTPRequestHandler.log_message = lambda *a: None
        self.httpd = socketserver.TCPServer(("127.0.0.1", port), partial(http.server.SimpleHTTPRequestHandler, directory=bundle))
        threading.Thread(target=self.httpd.serve_forever, daemon=True).start()
        self.port = port
        self.shots = shots
        self.errors = []
        self.viewport = viewport
        os.makedirs(shots, exist_ok=True)

    def open(self, p):
        self.browser = p.chromium.launch(headless=True, args=ARGS)
        self.page = self.browser.new_page(viewport={"width": self.viewport[0], "height": self.viewport[1]})
        self.page.on("console", lambda m: self.errors.append(m.text[:300]) if m.type == "error" else None)
        self.page.on("pageerror", lambda e: self.errors.append("pageerror: " + str(e)[:300]))
        self.page.goto("http://127.0.0.1:%d/?fresh" % self.port, wait_until="load", timeout=90000)
        self.page.wait_for_function("() => window.pdAutomation && window.pdAutomation.invoke", timeout=120000)
        for _ in range(60):
            if self.call("status").get("ok"):
                break
            time.sleep(1)

    def call(self, cmd, **a):
        body = json.dumps({"cmd": cmd, "args": a}, ensure_ascii=False)
        try:
            return json.loads(self.page.evaluate("b => window.pdAutomation.invoke(b)", body))
        except Exception as e:  # noqa: BLE001
            return {"ok": False, "error": "EXC " + str(e)[:200]}

    def res(self, cmd, **a):
        r = self.call(cmd, **a)
        return r.get("result") if r.get("ok") else None

    def shot(self, name):
        path = os.path.join(self.shots, name + ".png")
        data = self.page.screenshot(path=path)
        return Image.open(io.BytesIO(data)).convert("RGB")

    def drag(self, x0, y0, x1, y1, steps=14):
        m = self.page.mouse
        m.move(x0, y0)
        m.down()
        for i in range(1, steps + 1):
            m.move(x0 + (x1 - x0) * i / steps, y0 + (y1 - y0) * i / steps)
            time.sleep(0.03)
        m.up()
        time.sleep(0.8)

    def center(self, ctl_id, **a):
        r = self.res("ui.find", id=ctl_id, **a)
        b = (r or {}).get("bounds")
        return (b["x"] + b["w"] / 2, b["y"] + b["h"] / 2) if b else None

    def close(self):
        self.browser.close()
        self.httpd.shutdown()


def changed(a, b, box):
    ca, cb = a.crop(box), b.crop(box)
    d = ImageChops.difference(ca, cb).convert("L")
    return sum(1 for v in d.getdata() if v > 2)


def box_at(x, y):
    return (int(x - 12), int(y - 12), int(x + 52), int(y + 52))


def measure_409(w):
    """웹 포인터 추적 스피너: busy 상태값 + 실마우스 이동 + 화소 대조(포인터 자리에 그려지고, 떠난 자리·종료 뒤에는 남지 않고, 같은 자리에서 프레임이 바뀐다)"""
    out = {}
    p1, p2, far = (300, 250), (950, 600), (1300, 860)
    w.page.mouse.move(*far)
    time.sleep(0.4)
    tok = (w.res("busy.begin", scope="main", reason="ui28-409") or {}).get("token")
    time.sleep(1.2)
    out["busy_state"] = w.res("busy.state")
    w.page.mouse.move(*far, steps=3)
    time.sleep(0.5)
    shot_far = w.shot("ac409_busy_pointer_far")
    w.page.mouse.move(*p1, steps=8)
    time.sleep(0.5)
    shot_p1 = w.shot("ac409_busy_pointer_p1")
    w.page.mouse.move(*p2, steps=8)
    time.sleep(0.5)
    shot_p2 = w.shot("ac409_busy_pointer_p2")
    out["after_move"] = w.res("busy.state")
    time.sleep(0.6)
    shot_p2b = w.shot("ac409_busy_pointer_p2_later")
    new_px = changed(shot_p2, shot_far, box_at(*p2))
    old_px = changed(shot_p2, shot_far, box_at(*p1))
    p1_px = changed(shot_p1, shot_far, box_at(*p1))
    anim_px = changed(shot_p2, shot_p2b, box_at(*p2))
    if tok is not None:
        w.call("busy.end", token=tok)
    time.sleep(1.0)
    out["after_end"] = w.res("busy.state")
    shot_end = w.shot("ac409_after_end")
    base_far = None
    out["pixels"] = {"new_changed_px": new_px, "old_changed_px": old_px, "p1_follow_changed_px": p1_px, "anim_changed_px": anim_px,
                     "end_changed_px": changed(shot_end, shot_far, box_at(*p2)), "end_vs_p2_px": changed(shot_end, shot_p2, box_at(*p2)),
                     "boxes": {"p1": box_at(*p1), "p2": box_at(*p2)}, "note": "비교 기준 shot_far = busy 중 포인터를 먼 구석(1300,860)에 둔 화면"}
    del base_far
    return out


def mdi_bounds(w, doc):
    c = w.res("chrome.state", doc=doc) or {}
    return c.get("mdiBounds"), c


def measure_405(w):
    out = {"selftest": w.res("placement.selftest")}
    w.call("placement.force", on=True)
    st = {}
    w.call("docs.open", kind="grid", title="W1")
    time.sleep(1.5)
    doc = "Grid-1"
    b0, _ = mdi_bounds(w, doc)
    ts = w.page.evaluate("() => 0")
    del ts
    sdi = w.center("chrome.btnSdi", doc=doc)
    # 제목줄 끌기(MDI 문서 실제 이동): 제목줄 왼쪽 글자 자리를 잡는다
    tb = (sdi[0] - 335 + 120, sdi[1])
    w.drag(tb[0], tb[1], tb[0] + 150, tb[1] + 100)
    b1, _ = mdi_bounds(w, doc)
    st["mdi_bounds_before_drag"], st["mdi_bounds_after_drag"] = b0, b1
    st["mdi_drag_px"] = max(abs(b1["x"] - b0["x"]), abs(b1["y"] - b0["y"])) if b0 and b1 else None
    w.shot("ac405_1_mdi_moved")
    # 실클릭으로 SDI 전환(관리형 창)
    sdi = w.center("chrome.btnSdi", doc=doc)
    w.page.mouse.click(*sdi)
    time.sleep(1.5)
    wl = w.res("windows.list") or []
    managed = [x for x in wl if x.get("managed")]
    ps = w.res("placement.state") or {}
    d0 = (ps.get("docs") or [{}])[0]
    st["floated_managed"] = bool(managed) and d0.get("floating") is True
    st["sdi_frame_null"] = d0.get("sdiFrame") is None
    st["placement_after_float"] = d0
    st["managed_before_drag"] = managed[0] if managed else None
    w.shot("ac405_2_floated")
    # 관리형 창 제목줄을 실제로 끌어 메인 오른쪽 밖으로 내보낸다
    if managed:
        mb = managed[0]["bounds"]
        sdi = w.center("chrome.btnSdi", doc=doc)
        tb = (sdi[0] - 335 + 120, sdi[1]) if sdi else (mb["x"] + 120, mb["y"] + 16)
        w.drag(tb[0], tb[1], tb[0] + 950, tb[1] + 60, steps=20)
        wl2 = w.res("windows.list") or []
        m2 = [x for x in wl2 if x.get("managed")]
        st["managed_after_drag"] = m2[0] if m2 else None
        if m2:
            a, b = mb, m2[0]["bounds"]
            st["managed_drag_px"] = max(abs(b["x"] - a["x"]), abs(b["y"] - a["y"]))
            vw = w.viewport[0]
            visible = max(0, min(b["x"] + b["w"], vw) - max(b["x"], 0))
            st["managed_outside_ratio"] = round(1 - visible / b["w"], 3)
        w.shot("ac405_3_managed_dragged")
    # 재결합(실클릭) — 안 보이는 쪽이면 chrome 버튼 위치를 다시 찾는다
    sdi = w.center("chrome.btnSdi", doc=doc)
    st["btn_sdi_found_after_drag"] = sdi is not None
    if sdi and 0 <= sdi[0] <= w.viewport[0]:
        w.page.mouse.click(*sdi)
    else:
        w.call("docs.redock", doc=doc)
        st["redock_via_command"] = True
    time.sleep(1.5)
    b2, c2 = mdi_bounds(w, doc)
    st["mdi_bounds_after_redock"] = b2
    st["redocked"] = (c2.get("floating") is False)
    st["redock_vs_prefloat_max_delta"] = max(abs(b2[k] - b1[k]) for k in ("x", "y", "w", "h")) if b1 and b2 else None
    w.shot("ac405_4_redocked")
    out["steps"] = st
    # 메인 영역 축소 복구(MdiVisibilityGuard): 문서를 오른쪽으로 끌어 둔 뒤 창을 줄인다
    sdi = w.center("chrome.btnSdi", doc=doc)
    tb = (sdi[0] - 335 + 120, sdi[1])
    w.drag(tb[0], tb[1], tb[0] + 550, tb[1])
    g_probe = (w.res("placement.state") or {}).get("docs", [None])[0]  # 실제 끌기는 제목 줄의 끌 수 있는 구간이 영역에 남는 데까지 나간다(MdiDragLimit) — 관측만 남긴다
    out["real_drag_limit_probe"] = g_probe
    # 복구 대상을 확실히 만들려고 창을 줄인 뒤에도 잡을 곳이 없는 값을 진단 명령으로 놓는다(끌기로 간 자리는 축소 폭에 따라 잡을 곳이 남을 수 있다)
    out["markPlaced"] = w.res("placement.markPlaced", doc=doc, x=700, y=100, w=640, h=420)
    time.sleep(1.0)
    p_before = w.res("placement.state") or {}
    g = {"evaluations_before": p_before.get("mdiEvaluations"), "moved_before": p_before.get("mdiMoved"), "area_before": p_before.get("area"),
         "doc_before": (p_before.get("docs") or [None])[0]}
    w.shot("ac405_5_before_shrink")
    w.page.set_viewport_size({"width": 1000, "height": 900})
    w.viewport = (1000, 900)
    time.sleep(2.0)
    p_after = w.res("placement.state") or {}
    g["evaluations_after"], g["moved_after"] = p_after.get("mdiEvaluations"), p_after.get("mdiMoved")
    g["area_after"] = p_after.get("area")
    dd = (p_after.get("docs") or [None])[0]
    g["doc_after"] = dd
    if dd and p_after.get("area"):
        bx, bw, aw = dd["bounds"]["x"], dd["bounds"]["w"], p_after["area"]["dipW"]
        vis = min(bx + bw, aw) - max(bx, 0)
        g["title_visible_px"] = vis
        g["visible_after"] = vis >= 120
    w.shot("ac405_6_after_shrink")
    out["guard"] = g
    return out


def measure_609(w):
    runs = []
    tg = w.res("themes.get") or {}
    ids = list(tg.get("builtinThemes") or []) if isinstance(tg, dict) else []
    light = next((i for i in ids if i and "light" in str(i).lower()), None)
    dark = next((i for i in ids if i and "dark" in str(i).lower()), None)
    keys = ["help", "next", "prev", "nextF6", "prevF6", "close", "escClose", "saveLayout", "fontReset", "wheelZoom", "focusNext", "focusPrev", "menu", "windowMenu", "docMenu", "exit", "macClose"]
    for lang in ("ko", "en"):
        for theme_name, tid in (("light", light), ("dark", dark)):
            w.call("lang.set", code=lang)
            if tid:
                w.call("theme.select", id=tid)
            time.sleep(0.8)
            st = w.res("status") or {}
            st["activeThemeId"] = (w.res("themes.get") or {}).get("activeThemeId")
            w.call("tour.start", scope="main")
            time.sleep(0.8)
            w.call("tour.skip")
            time.sleep(1.0)
            notes = {}
            for k in keys:
                r = w.res("ui.find", id="tour.keyNote." + k)
                row = w.res("ui.find", id="tour.key." + k)
                notes[k] = {"text": r.get("text"), "visible": r.get("effectivelyVisible")} if r else None
                notes[k + "__row"] = bool(row)
            w.shot("ac609_%s_%s" % (lang, theme_name))
            tstate = w.res("tour.state")
            w.call("tour.end")
            time.sleep(0.5)
            runs.append({"lang": lang, "theme": theme_name, "theme_id": tid, "platform": st.get("platform"), "theme_status": st.get("theme"), "active_theme_id": st.get("activeThemeId"),
                         "tour_state": tstate, "notes": {k: v for k, v in notes.items() if not k.endswith("__row") and v},
                         "rows_present": {k: v for k, v in notes.items() if k.endswith("__row")}})
    return {"runs": runs, "themes_seen": ids}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--bundle", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--shots", required=True)
    ap.add_argument("--only", default="ac409,ac405,ac609")
    ap.add_argument("--port", type=int, default=5701)
    a = ap.parse_args()
    t0 = time.time()
    res = {"started_at": time.strftime("%Y-%m-%dT%H:%M:%S%z"), "bundle": a.bundle}
    fns = {"ac409": measure_409, "ac405": measure_405, "ac609": measure_609}
    from ui28_web_vis import FNS; fns.update(FNS)   # 보조 모듈 단계 max·sqcorner·cell(--only 로 고른 때만 돈다)
    port = a.port
    with sync_playwright() as p:
        for key in a.only.split(","):
            w = Web(a.bundle, port, a.shots)
            port += 1
            try:
                w.open(p)
                t1 = time.time()
                res[key] = fns[key](w)
                res[key]["elapsed_ms"] = round((time.time() - t1) * 1000)
                res[key]["console_errors"] = list(w.errors)
            except Exception as e:  # noqa: BLE001
                res[key] = {"exception": repr(e)[:500], "console_errors": list(w.errors)}
            finally:
                try:
                    w.close()
                except Exception:  # noqa: BLE001
                    pass
    res["finished_at"] = time.strftime("%Y-%m-%dT%H:%M:%S%z")
    res["total_ms"] = round((time.time() - t0) * 1000)
    with open(a.out, "w", encoding="utf-8") as f:
        json.dump(res, f, ensure_ascii=False, indent=1)
    print("saved", a.out)


if __name__ == "__main__":
    main()
