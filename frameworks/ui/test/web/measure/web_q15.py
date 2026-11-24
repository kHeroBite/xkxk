# otest-12 웹 축 Q15 ③④ 실제 끌기 시험 — headless Chromium 안 page.mouse 로 패널 탭을 끌어 놓고 panel.tabs·docs.list 로 판정(결과는 표준출력 JSON 한 줄, 캡처는 인자 폴더)
"""
사용: python3 -B web_q15.py <URL> <캡처 폴더(WSL 경로)>
판정 규칙: rules_otest12_web_w3.md. 탭은 panel.tabs 순서와 LeftPane DocumentTabStripItem bounds 순서를 맞춰 문서 Id 로 찾는다.
"""
import json
import sys
import time

sys.path.insert(0, "/mnt/c/DATA/Project/pd/poc/ui/tools")
from playwright.sync_api import sync_playwright  # noqa: E402
import retest_browser as rb  # noqa: E402

URL, OUT = sys.argv[1], sys.argv[2].rstrip("/")
# 선택 인자: <항목 키 쉼표 목록>[:x,y] — 예 '④-F:600,400' 은 ④-F 만, 끝점을 client (600,400) 으로(보조 확인)
ONLY = sys.argv[3] if len(sys.argv) > 3 else ""
ONLY_KEYS = set(ONLY.split(":")[0].split(",")) if ONLY else set()
ONLY_TO = tuple(float(v) for v in ONLY.split(":")[1].split(",")) if ":" in ONLY else None
walk = lambda n: [n] + [y for c in (n.get("children") or []) for y in walk(c)]


def emit(o):
    print("RESULT_JSON " + json.dumps(o, ensure_ascii=False, default=str), flush=True)


def main():
    res = {"url": URL, "items": {}}
    with sync_playwright() as p:
        b = p.chromium.launch(headless=True, args=["--use-angle=swiftshader", "--enable-unsafe-swiftshader", "--ignore-gpu-blocklist", "--enable-webgl"])
        page = b.new_page(viewport={"width": 1400, "height": 900})
        page.goto(URL)
        page.wait_for_function("() => window.pdAutomation && window.pdAutomation.invoke", timeout=90000)
        r = rb.WebRemote(page)
        c = r.call
        canvas = page.evaluate("() => { const c = document.querySelector('canvas'); if (!c) return null; const q = c.getBoundingClientRect(); return [q.x, q.y, q.width, q.height]; }")
        res["canvas"] = canvas
        ox, oy = (canvas[0], canvas[1]) if canvas else (0, 0)

        def tabs():
            t = c("panel.tabs", id="LeftPane") or {}
            return [(x.get("id"), x.get("active")) for x in t.get("tabs") or []]

        def docs():
            return [(d.get("id"), d.get("title"), d.get("floating"), d.get("zIndex"), d.get("zReason")) for d in c("docs.list") or []]

        def tab_center(doc_id):
            order = [x for x, _ in tabs()]
            nodes = [n for top in (c("tree.get", named="false") or {}).get("tree", []) for n in walk(top)]
            # LeftPane 탭 머리줄 항목: LeftPane.Tabs 아래 DocumentTabStripItem — bounds x 가 LeftPane 폭(≤ 패널 오른쪽) 안
            lp = ((c("panel.bounds") or {}).get("LeftPane") or {})
            items = [n["bounds"] for n in nodes if str(n.get("type")) == "DocumentTabStripItem" and (n.get("bounds") or {}).get("w", 0) > 0
                     and n["bounds"]["x"] < lp.get("x", 0) + lp.get("w", 0) and n["bounds"]["y"] < lp.get("y", 0) + 60]
            items.sort(key=lambda q: q["x"])
            if doc_id not in order or len(items) != len(order):
                return None, {"order": order, "items": items}
            q = items[order.index(doc_id)]
            return (q["x"] + q["w"] / 2, q["y"] + q["h"] / 2), {"order": order, "items": items}

        def drag(x0, y0, x1, y1):
            page.mouse.move(ox + x0, oy + y0)
            page.mouse.down()
            for s in range(1, 26):
                page.mouse.move(ox + x0 + (x1 - x0) * s / 25, oy + y0 + (y1 - y0) * s / 25)
                time.sleep(0.04)
            page.mouse.up()
            time.sleep(1.0)

        c("docs.close", all=True)
        ids = [((c("docs.open", kind="grid", title=t, allowDuplicate=True) or [{}])[0]).get("id") for t in ("Q15-A", "Q15-B")]
        c("docs.arrange", mode="tile-v")
        A = ids[0]
        res["ids"] = ids

        def reset():
            c("panel.returnDoc", doc=A)
            time.sleep(0.4)
            c("panel.join", id="LeftPane", doc=A)
            time.sleep(0.6)
            return tabs() == [(A, True), ("Explorer", False)]

        docs_area = (c("panel.bounds") or {}).get("Documents") or {}
        dc = (docs_area.get("x", 0) + docs_area.get("w", 0) / 2, docs_area.get("y", 0) + docs_area.get("h", 0) / 2)
        left_q = (docs_area.get("x", 0) + docs_area.get("w", 0) / 4, dc[1])
        res["docs_area"] = docs_area

        def item(key, target_fn, kind="drag"):
            if ONLY_KEYS and key not in ONLY_KEYS:
                return
            if ONLY_TO and kind == "drag":
                target_fn = lambda info: ONLY_TO  # noqa: E731
                key = key + "_at_%d_%d" % ONLY_TO
            if not reset():
                res["items"][key] = {"verdict": "미실행(사전 조건 불충족)", "tabs": tabs()}
                return
            ctr, info = tab_center(A)
            if ctr is None:
                res["items"][key] = {"verdict": "미실행(탭 위치 못 찾음)", "info": info}
                return
            before = {"tabs": tabs(), "dock": [d.get("dockId") for d in (c("panel.bounds") or {}).get("docDocks", [])]}
            if kind == "dbl":
                page.mouse.dblclick(ox + ctr[0], oy + ctr[1])
                time.sleep(1.0)
                to = None
            else:
                to = target_fn(info)
                drag(ctr[0], ctr[1], to[0], to[1])
            after = {"tabs": tabs(), "docs": docs(), "dock": [d.get("dockId") for d in (c("panel.bounds") or {}).get("docDocks", [])]}
            page.screenshot(path="%s/web_w3_%s.png" % (OUT, key.replace("-", "_")))
            res["items"][key] = {"from": ctr, "to": to, "before": before, "after": after}

        last_item = lambda info: (info["items"][-1]["x"] + info["items"][-1]["w"] + 17, info["items"][-1]["y"] + info["items"][-1]["h"] / 2)
        explorer_item = lambda info: (lambda q: (q["x"] + q["w"] / 2, q["y"] + q["h"] / 2))(info["items"][info["order"].index("Explorer")])
        item("③-1", last_item)
        item("③-2", explorer_item)
        item("③-3", lambda info: dc)
        lp = (c("panel.bounds") or {}).get("LeftPane") or {}
        item("③-4", lambda info: (lp.get("x", 0) + lp.get("w", 0) / 2, lp.get("y", 0) + 286))
        item("③-5", None, kind="dbl")
        item("④-F", lambda info: left_q)
        b.close()
    emit(res)


if __name__ == "__main__":
    main()
