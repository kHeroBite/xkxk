#!/usr/bin/env python3
# otest-w-5 E6 웹 부분과 보고용 값(폭별 첫 기동·수식키+휠·타이틀바 색·크롬 버튼 간격) 측정 — web_diag 최종 번들을 headless Chrome 실마우스로 잰다
"""
판정 기준(측정 전 고정, 정본 04 W-18·01 §2-1·title_tab_width_check 규칙)
  web_icon_count: 관리형 창 2개일 때 shell.sdiIcon.* 이 그 2개와 정확히 같고, 제목 탭에는 그 2개가 없고 MDI 2개는 있다.
  web_click_activates: 실마우스 아이콘 클릭으로 활성 문서가 그 창이 되고, 최소화한 창은 클릭으로 복원된다.
  web_tab_width: 표본(탭 1·3·5·10·30 × 뷰포트 900·1100·1600)이 TitleTabWidth 규칙 대조값과 ±TOL(2.5, title_tab_width_check 값) 이내, 자연폭 ≤240, 줄어들면 공통 상한, 72 에서 넘치면 스크롤. 세 구간 모두 표본이 있어야 한다.
extras 는 판정 대상이 아니라 값 보고용이다.
사용: python3 -B e6_web.py --bundle <wwwroot> --out <폴더> --mode e6|extras
"""
import argparse
import json
import os
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
UI = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
sys.path.insert(0, os.path.join(UI, "test", "web", "measure"))
sys.path.insert(0, os.path.join(UI, "test", "desktop", "judge"))
from playwright.sync_api import sync_playwright  # noqa: E402
from ui28_web import ARGS, Web  # noqa: E402
import title_tab_width_check as ttw  # noqa: E402

BUTTONS = ["chrome.btnSdi", "chrome.btnPin", "chrome.btnLock", "chrome.btnSync", "chrome.btnDup", "chrome.btnCache", "chrome.btnHelp"]


def walk(node, out):
    if isinstance(node, list):
        for c in node:
            walk(c, out)
    elif isinstance(node, dict):
        out.append(node)
        walk(node.get("children") or [], out)
    return out


def tree(w):
    return walk((w.res("tree.get", named=True, depth=60) or {}).get("tree"), [])


def visible(w, prefix):
    return [n for n in tree(w) if str(n.get("id", "")).startswith(prefix) and n.get("effectivelyVisible")]


def docs(w):
    r = w.res("docs.list")
    return r if isinstance(r, list) else (r or {}).get("docs", [])


def center(b):
    return b["x"] + b["w"] / 2, b["y"] + b["h"] / 2


class R:
    """title_tab_width_check 함수가 쓰는 r.result 를 웹 호출로 잇는다."""
    def __init__(self, w):
        self.w = w

    def result(self, cmd, **a):
        r = self.w.call(cmd, **a)
        return r.get("result") if r.get("ok") else {}


def float_doc(w, doc):
    """준비 조작이라 진단 명령을 쓴다. 앞선 관리형 창이 MDI 문서의 버튼을 가려 실클릭이 위 창 제목 아이콘으로 가는 것을 두 번의 실행으로 확인했다. 실클릭 분리는 AC-4-05 가 잰다."""
    r = w.call("docs.sdi", doc=doc, on=True)
    time.sleep(1.5)
    return r.get("ok") is True


def measure_e6(w, out):
    res = {}
    for k in range(4):
        w.call("docs.open", kind="form", title="E6 문서 %d" % k, allowDuplicate="true")
        time.sleep(0.6)
    ids = [d["id"] for d in docs(w)]
    a, b, c, d = ids[-4:]
    res["doc_ids"] = ids
    float_c, float_d = float_doc(w, c), float_doc(w, d)
    time.sleep(0.5)
    managed = [x for x in w.res("windows.list") or [] if x.get("managed")]
    icons = [n["id"] for n in visible(w, "shell.sdiIcon.")]
    doc_ids = set(ids)
    tabs = [n["id"] for n in visible(w, "shell.tab.") if n["id"][len("shell.tab."):] in doc_ids]
    res["web_icon_count"] = {"value": {"icons": icons, "tabs": tabs, "managed": [m.get("docs") for m in managed], "floated_clicks": [float_c, float_d]},
                             "pass": sorted(icons) == sorted("shell.sdiIcon." + x for x in (c, d)) and len(managed) == 2 and "shell.tab." + c not in tabs and "shell.tab." + d not in tabs and "shell.tab." + a in tabs and "shell.tab." + b in tabs}
    w.shot("e6_mix_icons")
    w.call("docs.activate", doc=a)
    time.sleep(0.5)
    active0 = next((x["id"] for x in docs(w) if x.get("active")), None)
    node = next((n for n in tree(w) if n.get("id") == "shell.sdiIcon." + c), None)
    w.page.mouse.move(*center(node["bounds"]))
    time.sleep(1.8)
    w.shot("e6_icon_hover_tooltip")
    w.page.mouse.click(*center(node["bounds"]))
    time.sleep(1.0)
    active1 = next((x["id"] for x in docs(w) if x.get("active")), None)
    w.shot("e6_after_icon_click")
    w.call("docs.state", doc=d, state="minimized")
    time.sleep(1.0)
    row_min = next((x for x in docs(w) if x["id"] == d), {})
    placement_min = w.res("chrome.state", doc=d)
    w.shot("e6_minimized")
    node = next((n for n in tree(w) if n.get("id") == "shell.sdiIcon." + d), None)
    minimized_icon_visible = bool(node and node.get("effectivelyVisible"))
    if node:
        w.page.mouse.click(*center(node["bounds"]))
        time.sleep(1.0)
    row_after = next((x for x in docs(w) if x["id"] == d), {})
    placement_after = w.res("chrome.state", doc=d)
    w.shot("e6_after_restore")
    was_min = str(row_min.get("state", "")).lower().startswith("min") or (placement_min or {}).get("state") in ("Minimized", "minimized") or row_min.get("minimized") is True or str((row_min.get("dockWindow") or {}).get("state", "")).lower().startswith("min")
    now_min = str(row_after.get("state", "")).lower().startswith("min") or (placement_after or {}).get("state") in ("Minimized", "minimized") or row_after.get("minimized") is True or str((row_after.get("dockWindow") or {}).get("state", "")).lower().startswith("min")
    active2 = next((x["id"] for x in docs(w) if x.get("active")), None)
    res["web_click_activates"] = {"value": {"active_before": active0, "target": c, "active_after": active1, "minimized_row": row_min, "minimized_chrome": placement_min, "after_row": row_after, "after_chrome": placement_after,
                                            "minimized_icon_visible": minimized_icon_visible, "active_after_restore": active2},
                                  "pass": active0 == a and active1 == c and was_min and not now_min and minimized_icon_visible}
    # 아이콘 우클릭 메뉴(값 보고)
    node = next((n for n in tree(w) if n.get("id") == "shell.sdiIcon." + c), None)
    if node:
        w.page.mouse.click(*center(node["bounds"]), button="right")
        time.sleep(0.8)
        w.shot("e6_icon_menu")
        res["icon_menu_shot"] = True
        w.page.keyboard.press("Escape")
        time.sleep(0.5)
    w.call("docs.close", all="true")
    time.sleep(1.0)
    # 탭 폭: title_tab_width_check 와 같은 표본·대조값
    r = R(w)

    def set_width(_r, width):
        w.page.set_viewport_size({"width": int(width), "height": 900})
        w.viewport = (int(width), 900)
        time.sleep(0.8)
    ttw.set_width = set_width
    widths = (900, 1100, 1600)  # 첫 실행에서 700 은 자동 전환으로 제목 탭 0 폭이었다(600~839 형은 extras first_start 에 값으로 남긴다). 폭 규칙 표본은 탭이 있는 PC 폭에서 잰다
    nat = ttw.natural_of(r, max(widths) + 400)
    meas = {k: {"pass": True, "samples": []} for k in ("natural", "shrink", "scroll")}
    for width in widths:
        for n in ttw.COUNTS:
            set_width(r, width)
            ids2 = ttw.open_n(r, n)
            time.sleep(0.6)
            rows, sx, sw = ttw.tab_widths(r, ids2)
            got = [x[1] for x in rows]
            exp, ov = ttw.compute([nat[k % 3] for k in range(n)], sw - ttw.GAP * max(0, n - 1))
            ok = len(got) == len(exp) and all(abs(p - q) <= ttw.TOL for p, q in zip(got, exp))
            last_right = rows[-1][0] + rows[-1][1] if rows else 0
            # 새 탭을 활성으로 열면 줄이 그 탭까지 스크롤되므로 마지막 탭 오른쪽만 보면 넘침을 놓친다. 첫 탭이 왼쪽 밖에 있거나 마지막 탭이 오른쪽 밖에 있으면 스크롤된 것이다
            scrolled = last_right > sx + sw + 0.5 or (bool(rows) and rows[0][0] < sx - 0.5)
            s = {"viewport": width, "n": n, "strip_w": sw, "got": [round(g, 1) for g in got], "exp": [round(e, 1) for e in exp], "overflow_exp": ov, "last_right": round(last_right, 1), "first_x": round(rows[0][0], 1) if rows else None, "strip_x": sx, "scrolled": scrolled}
            fits = sum(ttw.compute([nat[k % 3] for k in range(n)], 1e9)[0]) + ttw.GAP * max(0, n - 1) <= sw
            if fits:
                m = meas["natural"]
                m["pass"] &= ok and all(ttw.MIN - ttw.TOL <= g <= ttw.MAX + ttw.TOL for g in got)
            elif not ov:
                m = meas["shrink"]
                m["pass"] &= ok and all(g >= ttw.MIN - ttw.TOL for g in got) and max(got) - max(exp) <= ttw.TOL
            else:
                m = meas["scroll"]
                m["pass"] &= ok and all(abs(g - ttw.MIN) <= ttw.TOL for g in got) and scrolled
            s["ok"] = ok and (scrolled if not fits and ov else True)
            m["samples"].append(s)
            if width == 1100 and n in (3, 30):
                try:
                    w.page.screenshot(path=os.path.join(w.shots, "e6_tabs_%d.png" % n), timeout=180000)
                except Exception as e:  # noqa: BLE001  캡처 시간 초과는 값 측정을 막지 않는다. 사실만 남긴다
                    res.setdefault("shot_errors", []).append("e6_tabs_%d %s" % (n, repr(e)[:120]))
    for m in meas.values():
        if not m["samples"]:
            m["pass"] = False
    res["web_tab_width"] = {"value": {"natural": [round(x, 1) for x in nat], "gap": ttw.GAP, "tol": ttw.TOL, "buckets": {k: {"pass": v["pass"], "count": len(v["samples"]), "failed": [s for s in v["samples"] if not s["ok"]]} for k, v in meas.items()}, "samples": meas},
                            "pass": all(m["pass"] for m in meas.values())}
    return res


def open_ctx(p, port, width, height, touch, errors):
    b = p.chromium.launch(headless=True, args=ARGS)
    ctx = b.new_context(viewport={"width": width, "height": height}, has_touch=touch)
    page = ctx.new_page()
    page.on("console", lambda m: errors.append(m.text[:200]) if m.type == "error" else None)
    if touch:
        page.add_init_script("Object.defineProperty(window,'__pdTouchCtx',{value:true})")
    page.goto("http://127.0.0.1:%d/?fresh" % port, wait_until="load", timeout=90000)
    page.wait_for_function("() => window.pdAutomation && window.pdAutomation.invoke", timeout=120000)
    return b, page


def extras_first_start(w0, p, out):
    rows = []
    for width in (599, 600, 700, 839, 840, 1024):
        for touch in (False, True):
            errors = []
            b, page = open_ctx(p, w0.port, width, 900, touch, errors)
            w0.page = page
            for _ in range(60):
                if w0.call("status").get("ok"):
                    break
                time.sleep(1)
            time.sleep(1.5)
            st = w0.res("ui.style")
            coarse = page.evaluate("() => matchMedia('(pointer: coarse)').matches")
            w0.call("docs.open", kind="form", title="폭 시험")
            time.sleep(1.2)
            nodes = tree(w0)
            seen = {"tabStrip_visible": any(n.get("id") == "shell.tabStrip" and n.get("effectivelyVisible") for n in nodes),
                    "MdiDocumentControl": any(n.get("type") == "MdiDocumentControl" and n.get("effectivelyVisible") for n in nodes),
                    "DocumentTabStrip": any(n.get("type") == "DocumentTabStrip" and n.get("effectivelyVisible") for n in nodes)}
            path = os.path.join(out, "shots_e", "first_%d_%s.png" % (width, "touch" if touch else "mouse"))
            os.makedirs(os.path.dirname(path), exist_ok=True)
            page.screenshot(path=path)
            rows.append({"width": width, "touch": touch, "pointer_coarse": coarse, "ui_style": st, "seen": seen, "console_errors": len(errors)})
            print(json.dumps(rows[-1], ensure_ascii=False)[:400], flush=True)
            b.close()
    return rows


WHEEL_PROBE = """() => { window.__wheelLog = []; window.addEventListener('wheel', e => { window.__wheelLog.push({ctrl:e.ctrlKey, alt:e.altKey, shift:e.shiftKey, dy:e.deltaY, dx:e.deltaX, prevented:e.defaultPrevented, target:(e.target||{}).tagName}); }, {passive:true}); }"""
ZOOM_JS = "() => ({dpr: devicePixelRatio, vv: visualViewport.scale, inner: innerWidth})"


def extras_wheel(w, out):
    w.page.evaluate(WHEEL_PROBE)
    rows = []
    for kind, cid in (("gantt", "gantt.canvas"), ("grid", "grid.dgMain")):
        w.call("docs.close", all="true")
        w.call("docs.open", kind=kind, title="휠 " + kind)
        time.sleep(2.5)
        b = (w.res("ui.find", id=cid) or {}).get("bounds")
        if not b:
            rows.append({"kind": kind, "error": "대상 없음"})
            continue
        at = center(b)
        w.page.mouse.move(*at)
        time.sleep(0.3)
        for op, keys in (("plain", []), ("ctrl", ["Control"]), ("alt", ["Alt"]), ("shift", ["Shift"]), ("alt_shift", ["Alt", "Shift"])):
            before = {"dense": w.res("dense.state", id=cid), "font": (w.res("settings.get") or {}).get("baseFontSize"), "zoom": (w.res("settings.get") or {}).get("activeDocZoom"), "page": w.page.evaluate(ZOOM_JS)}
            for k in keys:
                w.page.keyboard.down(k)
            w.page.mouse.wheel(0, -120)
            time.sleep(0.5)
            for k in reversed(keys):
                w.page.keyboard.up(k)
            time.sleep(0.4)
            after = {"dense": w.res("dense.state", id=cid), "font": (w.res("settings.get") or {}).get("baseFontSize"), "zoom": (w.res("settings.get") or {}).get("activeDocZoom"), "page": w.page.evaluate(ZOOM_JS)}
            log = w.page.evaluate("() => { const l = window.__wheelLog; window.__wheelLog = []; return l; }")
            pick = ("rowLevel", "rowHeight", "rowAt", "dayWidth", "offsetX", "columnSum", "hOffset")
            rows.append({"kind": kind, "op": op, "before": {k: (before["dense"] or {}).get(k) for k in pick}, "after": {k: (after["dense"] or {}).get(k) for k in pick},
                         "font": [before["font"], after["font"]], "zoom": [before["zoom"], after["zoom"]], "page_before": before["page"], "page_after": after["page"], "wheel_events": log[-2:]})
            print(json.dumps(rows[-1], ensure_ascii=False)[:500], flush=True)
            if op == "ctrl":
                w.call("input.ctrlWheel", delta=0)
                time.sleep(0.3)
    return rows


def lum(c):
    def f(v):
        v /= 255
        return v / 12.92 if v <= 0.03928 else ((v + 0.055) / 1.055) ** 2.4
    return 0.2126 * f(c[0]) + 0.7152 * f(c[1]) + 0.0722 * f(c[2])


def ratio(a, b):
    la, lb = lum(a), lum(b)
    return round((max(la, lb) + 0.05) / (min(la, lb) + 0.05), 2)


def ink_runs(row, bg, tol=40):
    width = len(row[0])
    cols = [any(sum(abs(p[i] - bg[i]) for i in range(3)) > tol for p in (line[x] for line in row)) for x in range(width)]
    runs, start = [], None
    for x, on in enumerate(cols + [False]):
        if on and start is None:
            start = x
        elif not on and start is not None:
            runs.append((start, x - 1))
            start = None
    return runs


def extras_chrome(w, out):
    tg = w.res("themes.get") or {}
    themes = tg.get("builtinThemes") or []
    w.call("docs.open", kind="form", title="크롬 시험")
    time.sleep(1.5)
    doc = docs(w)[-1]["id"]
    rows = []
    for theme in themes:
        w.call("theme.select", id=theme)
        time.sleep(1.0)
        w.page.mouse.move(700, 880)
        time.sleep(0.3)
        img = w.shot("chrome_" + theme).convert("RGB")
        row = {"theme": theme}
        bar, strip, drag = (w.res("ui.find", id=i, props="Background") or {} for i in ("shell.titleBar", "shell.titleStrip", "shell.titleDragArea"))
        row["bar_prop"], row["strip_prop"] = bar.get("props"), strip.get("props")
        menu = w.res("ui.find", id="shell.mnuFile", props="Foreground") or {}
        row["menu_prop"] = menu.get("props")
        try:
            db = drag.get("bounds") or bar["bounds"]
            sb = strip["bounds"]
            pb = img.getpixel((int(db["x"] + db["w"] / 2), int(db["y"] + db["h"] / 2)))
            ps = img.getpixel((int(sb["x"] + sb["w"] - 8), int(sb["y"] + sb["h"] / 2)))
            row["px_bar"], row["px_strip"], row["bar_strip_ratio"] = pb, ps, ratio(pb, ps)
        except (KeyError, TypeError, IndexError, ValueError) as e:
            row["px_error"] = repr(e)
        nodes = [w.res("ui.find", id=c, doc=doc) or {} for c in BUTTONS]
        bounds = [n.get("bounds") for n in nodes]
        row["button_visible"] = {c: bool(n.get("bounds")) and n.get("effectivelyVisible") is not False for c, n in zip(BUTTONS, nodes)}
        have = [b for b in bounds if b]
        if len(have) >= 2:
            row["pitch"] = [round(have[i + 1]["x"] - have[i]["x"], 2) for i in range(len(have) - 1)]
            x0, y0, x1, y1 = int(have[0]["x"]), int(have[0]["y"]), int(have[-1]["x"] + have[-1]["w"]), int(have[0]["y"] + have[0]["h"])
            bg = img.getpixel((int(have[4]["x"] + have[4]["w"] * 0.12), int(have[4]["y"] + have[4]["h"] * 0.5))) if len(have) > 4 else img.getpixel((x0, y0))
            lines = [[img.getpixel((x, y)) for x in range(x0, x1)] for y in range(y0, y1)]
            runs = ink_runs(lines, bg)
            gaps = [runs[i + 1][0] - runs[i][1] - 1 for i in range(len(runs) - 1)]
            row["ink_runs"], row["gap_ink"], row["bg"] = len(runs), gaps, bg
        rows.append(row)
        print(json.dumps(row, ensure_ascii=False)[:500], flush=True)
    return rows


def measure_shift(w):
    """Shift+휠 가로 스크롤 값. extras 는 맨 왼쪽에서 위로 돌려 움직일 수 없었으므로 아래 방향으로 다시 잰다."""
    rows = []
    for kind, cid, key in (("gantt", "gantt.canvas", "offsetX"), ("grid", "grid.dgMain", "hOffset")):
        w.call("docs.close", all="true")
        w.call("docs.open", kind=kind, title="휠 " + kind)
        time.sleep(2.5)
        at = center(w.res("ui.find", id=cid)["bounds"])
        w.page.mouse.move(*at)
        if kind == "grid":
            w.page.keyboard.down("Alt")
            for _ in range(4):
                w.page.mouse.wheel(0, -120)
                time.sleep(0.3)
            w.page.keyboard.up("Alt")
        b = w.res("dense.state", id=cid)
        w.page.keyboard.down("Shift")
        w.page.mouse.wheel(0, 120)
        time.sleep(0.6)
        w.page.keyboard.up("Shift")
        time.sleep(0.4)
        a = w.res("dense.state", id=cid)
        rows.append({"kind": kind, "before": b.get(key), "after": a.get(key), "column_or_day": [b.get("columnSum", b.get("dayWidth")), a.get("columnSum", a.get("dayWidth"))]})
        print(json.dumps(rows[-1], ensure_ascii=False), flush=True)
    return rows


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--bundle", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--mode", choices=("e6", "extras", "shift"), required=True)
    ap.add_argument("--port", type=int, default=5961)
    a = ap.parse_args()
    shots = os.path.join(a.out, "shots_e")
    doc = {"started_at": time.strftime("%Y-%m-%dT%H:%M:%S%z"), "bundle": a.bundle}
    with sync_playwright() as p:
        if a.mode == "shift":
            w = Web(a.bundle, a.port, shots)
            w.open(p)
            time.sleep(1.5)
            doc["shift"] = measure_shift(w)
            w.close()
        elif a.mode == "e6":
            w = Web(a.bundle, a.port, shots)
            w.open(p)
            time.sleep(1.5)
            doc.update(measure_e6(w, a.out))
            doc["console_errors"] = list(w.errors)
            w.close()
        else:
            w = Web(a.bundle, a.port, shots)
            doc["first_start"] = extras_first_start(w, p, a.out)
            w.httpd.shutdown()
            w = Web(a.bundle, a.port + 1, shots)
            w.open(p)
            time.sleep(1.5)
            doc["wheel"] = extras_wheel(w, a.out)
            w.close()
            w = Web(a.bundle, a.port + 2, shots)
            w.open(p)
            time.sleep(1.5)
            doc["chrome"] = extras_chrome(w, a.out)
            doc["console_errors"] = list(w.errors)
            w.close()
    doc["finished_at"] = time.strftime("%Y-%m-%dT%H:%M:%S%z")
    path = os.path.join(a.out, "%s_web_raw.json" % a.mode)
    with open(path, "w", encoding="utf-8") as f:
        json.dump(doc, f, ensure_ascii=False, indent=1)
    print("saved", path)


if __name__ == "__main__":
    main()
