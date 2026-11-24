# otest-12 웹 Q15 보조 시험 — ④-F 원인 확인(끌기 중 드롭 지시자 bounds 측정)·④-F 재측정(지시자 중심)·①합류·②탭 클릭·⑤새로고침 유지(결과는 표준출력 JSON, 캡처는 인자 폴더)
"""
사용: python3 -B web_q15b.py <URL> <캡처 폴더> probe|refx:<x>,<y>|j125
판정 규칙: rules_otest12_web_w3.md '추가(18:0x)' 절.
"""
import json
import os
import sys
import time

# 영역 루트 = 이 파일 위치(test/archive/rounds/web) 기준 네 단계 위(UTODO-0012)
UI_ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "..", ".."))
sys.path.insert(0, os.path.join(UI_ROOT, "tools"))
from playwright.sync_api import sync_playwright  # noqa: E402
import retest_browser as rb  # noqa: E402

URL, OUT, MODE = sys.argv[1], sys.argv[2].rstrip("/"), sys.argv[3]
walk = lambda n: [n] + [y for c in (n.get("children") or []) for y in walk(c)]
KEYS = ("indicator", "docktarget", "selector", "adorner")


def emit(o):
    print("RESULT_JSON " + json.dumps(o, ensure_ascii=False, default=str), flush=True)


def main():
    res = {"url": URL, "mode": MODE}
    with sync_playwright() as p:
        b = p.chromium.launch(headless=True, args=["--use-angle=swiftshader", "--enable-unsafe-swiftshader", "--ignore-gpu-blocklist", "--enable-webgl"])
        page = b.new_page(viewport={"width": 1400, "height": 900})
        page.goto(URL)
        page.wait_for_function("() => window.pdAutomation && window.pdAutomation.invoke", timeout=90000)
        r = rb.WebRemote(page)
        c = r.call

        def tabs():
            t = c("panel.tabs", id="LeftPane") or {}
            return [(x.get("id"), x.get("active")) for x in t.get("tabs") or []]

        def nodes():
            return [n for top in (c("tree.get", named="false") or {}).get("tree", []) for n in walk(top)]

        def tab_items():
            lp = (c("panel.bounds") or {}).get("LeftPane") or {}
            it = [n["bounds"] for n in nodes() if str(n.get("type")) == "DocumentTabStripItem" and (n.get("bounds") or {}).get("w", 0) > 0
                  and n.get("isVisible") is not False
                  and n["bounds"]["x"] < lp.get("x", 0) + lp.get("w", 0) and n["bounds"]["y"] < lp.get("y", 0) + 60]
            return sorted(it, key=lambda q: q["x"])

        def center_of(doc_id):
            order, it = [x for x, _ in tabs()], tab_items()
            if doc_id not in order or len(it) != len(order):
                return None
            q = it[order.index(doc_id)]
            return (q["x"] + q["w"] / 2, q["y"] + q["h"] / 2)

        def move_steps(x0, y0, x1, y1):
            for s in range(1, 26):
                page.mouse.move(x0 + (x1 - x0) * s / 25, y0 + (y1 - y0) * s / 25)
                time.sleep(0.04)

        def drag(x0, y0, x1, y1):
            page.mouse.move(x0, y0)
            page.mouse.down()
            move_steps(x0, y0, x1, y1)
            page.mouse.up()
            time.sleep(1.0)

        def docs():
            return [(d.get("id"), d.get("title"), d.get("floating"), d.get("zIndex"), d.get("zReason")) for d in c("docs.list") or []]

        c("docs.close", all=True)
        ids = [((c("docs.open", kind="grid", title=t, allowDuplicate=True) or [{}])[0]).get("id") for t in ("Q15-A", "Q15-B")]
        c("docs.arrange", mode="tile-v")
        A = ids[0]
        da = (c("panel.bounds") or {}).get("Documents") or {}
        dc = (da["x"] + da["w"] / 2, da["y"] + da["h"] / 2)
        res.update({"ids": ids, "docs_area": da, "quarter_point": (da["x"] + da["w"] / 4, dc[1])})

        def reset():
            c("panel.returnDoc", doc=A)
            time.sleep(0.4)
            c("panel.join", id="LeftPane", doc=A)
            time.sleep(0.6)
            return tabs() == [(A, True), ("Explorer", False)]

        if MODE == "probe":
            res["reset"] = reset()
            ctr = center_of(A)
            page.mouse.move(*ctr)
            page.mouse.down()
            move_steps(ctr[0], ctr[1], dc[0], dc[1])
            time.sleep(0.8)
            ns = nodes()
            found = [{"type": n.get("type"), "name": n.get("name") or n.get("id"), "bounds": n.get("bounds"), "visible": n.get("isVisible")}
                     for n in ns if any(k in (str(n.get("type")) + " " + str(n.get("name") or n.get("id") or "")).lower() for k in KEYS)]
            res["indicators"] = found
            page.screenshot(path=OUT + "/web_w3_probe_mid_drag.png")
            page.keyboard.press("Escape")
            time.sleep(0.3)
            page.mouse.up()
            time.sleep(1.0)
            res["after_release"] = {"tabs": tabs(), "docs": docs()}
        elif MODE.startswith("refx:"):
            to = tuple(float(v) for v in MODE[5:].split(","))
            res["reset"] = reset()
            ctr = center_of(A)
            drag(ctr[0], ctr[1], to[0], to[1])
            res["after"] = {"tabs": tabs(), "docs": docs(), "dock": [d.get("dockId") for d in (c("panel.bounds") or {}).get("docDocks", [])]}
            page.screenshot(path=OUT + "/web_w3_④_F_rerun.png")
        elif MODE == "j125":
            # ① 합류: Q15-A 관리형 창 제목줄 → LeftPane 탭 머리줄 마지막 탭 오른쪽
            logo = c("ui.find", id="chrome.logo", doc=A) or {}
            sdi = c("ui.find", id="chrome.btnSdi", doc=A) or {}
            lb, sb = logo.get("bounds") or {}, sdi.get("bounds") or {}
            grab = ((lb.get("x", 0) + lb.get("w", 0) + sb.get("x", 0)) / 2, lb.get("y", 0) + lb.get("h", 0) / 2)
            it = tab_items()
            if it:
                to = (it[-1]["x"] + it[-1]["w"] + 17, it[-1]["y"] + it[-1]["h"] / 2)
                end_kind = "tab"
            else:
                # w8 부터 1탭 패널은 탭 띠가 숨는다 → 끝점은 LeftPane 제목 칸(PART_Title) 중심(T9, rules_otest12_w8.md 변경 기록)
                lp = (c("panel.bounds") or {}).get("LeftPane") or {}
                title = [n["bounds"] for n in nodes() if n.get("name") == "PART_Title" and n.get("isVisible") is not False
                         and (n.get("bounds") or {}).get("w", 0) > 0
                         and lp.get("x", 0) <= n["bounds"]["x"] < lp.get("x", 0) + lp.get("w", 0)]
                q = min(title, key=lambda b: b["y"]) if title else None
                to = (q["x"] + q["w"] / 2, q["y"] + q["h"] / 2) if q else None
                end_kind = "title"
            res["j1_input"] = {"grab": grab, "to": to, "end": end_kind, "logo": lb, "btnSdi": sb}
            if to is None:
                res["①"] = {"verdict": "FAIL", "reason": "끝점(탭·제목 칸)을 트리에서 찾지 못함"}
                b.close()
                emit(res)
                return
            drag(grab[0], grab[1], to[0], to[1])
            t1 = tabs()
            res["①"] = {"verdict": "PASS" if any(x == A for x, _ in t1) else "FAIL", "tabs": t1, "docs": docs()}
            page.screenshot(path=OUT + "/web_w3_①_join.png")
            # ② 탭 클릭
            seq = []
            for target in ("Explorer", A):
                q = center_of(target)
                page.mouse.click(q[0], q[1])
                time.sleep(0.8)
                seq.append({"click": target, "tabs": tabs()})
            ok2 = seq[0]["tabs"] and dict(seq[0]["tabs"]).get("Explorer") is True and dict(seq[1]["tabs"]).get(A) is True
            res["②"] = {"verdict": "PASS" if ok2 else "FAIL", "seq": seq}
            # ⑤ 새로고침 유지(사용자 경로)
            before = tabs()
            page.reload()
            page.wait_for_function("() => window.pdAutomation && window.pdAutomation.invoke", timeout=90000)
            time.sleep(1.5)
            after = tabs()
            res["⑤"] = {"verdict": "PASS" if before == after else "FAIL", "before": before, "after": after, "docs_after": docs()}
            if before != after:
                c("panel.join", id="LeftPane", doc=A)  # 보조: 명시 저장 뒤 새로고침(기록만)
                time.sleep(0.6)
                b2 = tabs()
                c("layout.save")
                page.reload()
                page.wait_for_function("() => window.pdAutomation && window.pdAutomation.invoke", timeout=90000)
                time.sleep(1.5)
                res["⑤_supplement_layout_save"] = {"before": b2, "after": tabs()}
            page.screenshot(path=OUT + "/web_w3_⑤_after_reload.png")
        b.close()
    emit(res)


if __name__ == "__main__":
    main()
