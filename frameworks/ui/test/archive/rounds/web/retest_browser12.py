# 웹 관리형 SDI 창 위치·크기 저장/복원 시험(lead 09:5x (a)) — 사용자 경로로 옮기고 크기를 바꾼 SDI 2개를 레이아웃 저장 → 새로고침·재기동 → 복원했을 때 그대로 돌아오는지 본다
# 사용: PD_URL=http://127.0.0.1:5186/ python3 tools/retest_browser12.py   (헤드리스 전용, 시험 전용 layout-key=otestw2-persist)
"""
판정: 저장 직전 각 SDI 창 표시 사각형(windows.list inContainer x,y,w,h) S0 와 복원 뒤 사각형을 ±1 로 비교한다.
  A1 = 자동 저장 슬롯(layout.save 인자 없음) → page.reload() → 기동 자동 복원 결과
  A2 = A1 직후 layout.load(인자 없음, 자동 슬롯 명시 복원)
  B  = 개인 레이아웃(layout.save name=) → 새 페이지(같은 브라우저 문맥 = 같은 localStorage, 재기동 대리) → layout.load name=
저장 파일 내용: localStorage 에서 layout-key 가 들어간 항목을 모두 떠서(persist_storage.json) 창 위치·크기로 보이는 키(X·Y·Width·Height·Bounds 등)를 뽑아 기록한다.
"""
import json
import os
import re
import sys
import time

from playwright.sync_api import sync_playwright

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import retest_browser as rb  # noqa: E402
import retest_browser2 as t  # noqa: E402

OUT = t.OUT
KEY = "otestw2-persist"
NAME = "otestw2-persist-B"
res = []


def rec(item, expect, actual, verdict):
    res.append({"item": item, "expect": expect, "actual": actual, "verdict": verdict})
    print("%-6s %s | %s" % (verdict, item, str(actual)[:600]), flush=True)


def ready(page):
    page.wait_for_function("() => window.pdAutomation && window.pdAutomation.invoke", timeout=60000)
    r = rb.WebRemote(page)
    for _ in range(90):
        if r.call("status") is not None and r.call("docs.list") is not None:
            break
        time.sleep(1)
    time.sleep(1.5)
    return r


def rects(r):
    out = {}
    for w in (r.call("windows.list") or []):
        if w.get("managed"):
            ic = (w.get("bounds") or {}).get("inContainer") or {}
            out[w.get("title")] = (ic.get("x"), ic.get("y"), ic.get("w"), ic.get("h"))
    return out


def models(r):
    """windows.list 관리형 창의 Dock 창 모델 {제목: dockWindow{x,y,w,h,state,host}} — 키가 없는 판(i0944 이하)이면 None."""
    return {w.get("title"): w.get("dockWindow") for w in (r.call("windows.list") or []) if w.get("managed")}


def stored_windows(page, key):
    """localStorage[key] 자동 슬롯 JSON 의 Documents[].Window 를 {제목: (X,Y,W,H)} 로."""
    raw = page.evaluate("k => localStorage.getItem(k)", key)
    try:
        doc = json.loads(raw) if raw else None
    except Exception:
        return {"_raw": str(raw)[:200]}
    out = {}
    for d in (doc or {}).get("Documents", []) if isinstance(doc, dict) else []:
        w = d.get("Window") or {}
        out[d.get("Title")] = (w.get("X"), w.get("Y"), w.get("Width"), w.get("Height")) if w else None
    return out


def same(a, b, tol=1.0):
    return a is not None and b is not None and None not in a and None not in b and all(abs(x - y) <= tol for x, y in zip(a, b))


def compare(s0, s1):
    rows = {k: {"saved": s0[k], "restored": s1.get(k), "same": same(s0[k], s1.get(k))} for k in s0}
    return all(v["same"] for v in rows.values()) and bool(rows), rows


GEOM = re.compile(r"^(x|y|w|h|left|top|width|height|bounds|mdibounds|position|size|windowx|windowy|windowwidth|windowheight)$", re.I)


def geom_entries(node, path="", acc=None):
    """저장 JSON 에서 위치·크기로 보이는 키를 가진 객체만 뽑는다(경로·값)."""
    acc = [] if acc is None else acc
    if isinstance(node, dict):
        hits = {k: v for k, v in node.items() if GEOM.match(k) and not isinstance(v, (list,))}
        if hits:
            ctx = {k: node.get(k) for k in ("Id", "id", "Title", "title", "Kind", "kind", "State", "state", "WindowState", "Context") if k in node}
            acc.append({"path": path, "ctx": ctx, "geom": hits})
        for k, v in node.items():
            geom_entries(v, path + "/" + str(k), acc)
    elif isinstance(node, list):
        for i, v in enumerate(node):
            geom_entries(v, path + "[%d]" % i, acc)
    return acc


def dump_storage(page):
    items = page.evaluate("() => Object.keys(localStorage).map(k => [k, localStorage.getItem(k)])")
    mine = {k: v for k, v in items if KEY in k}
    parsed = {}
    for k, v in mine.items():
        try:
            parsed[k] = json.loads(v)
        except Exception:
            parsed[k] = v
    with open(os.path.join(OUT, "persist_storage.json"), "w", encoding="utf-8") as f:
        json.dump(parsed, f, ensure_ascii=False, indent=1)
    geo = {k: geom_entries(v) for k, v in parsed.items() if isinstance(v, (dict, list))}
    return {"keys": list(mine), "allKeys": [k for k, _ in items], "geometry": geo}


def drag(page, sx, sy, dx, dy):
    page.mouse.move(sx, sy)
    page.mouse.down()
    for i in range(1, 15):
        page.mouse.move(sx + dx * i / 14, sy + dy * i / 14)
        time.sleep(0.03)
    page.mouse.up()
    time.sleep(1.0)


def main():
    with sync_playwright() as p:
        b = p.chromium.launch(headless=True, args=t.ARGS)
        ctx, page, r = t.boot(b, "?layout-key=" + KEY)
        r.call("docs.close", all=True)
        ids = [r.call("docs.open", kind="form", title="PS-%d" % i, sdi=True, allowDuplicate=True)[0]["id"] for i in (1, 2)]
        time.sleep(1.5)
        # 초기화 포함: ⑦ 로 기본 위치에 놓은 뒤 사용자 경로(마우스)로 1번은 옮기고 2번은 크기를 바꾼다
        r.call("sdi.resetPositions")
        time.sleep(1.0)
        s_reset = rects(r)
        lk = (r.call("ui.find", id="chrome.btnLock", doc=ids[0]) or {}).get("bounds")
        if lk:
            drag(page, lk["x"] - 150, lk["y"] + lk["h"] / 2, 210, 90)  # 제목줄 끌기
        g = (r.call("ui.find", id="chrome.gripResize", doc=ids[1]) or {}).get("bounds")
        if g:
            drag(page, g["x"] + g["w"] - 3, g["y"] + g["h"] - 3, -120, -70)  # 크기 줄이기
        s0 = rects(r)
        changed = len(s0) == 2 and all(not same(s0[k], s_reset.get(k)) for k in s0)
        rec("사전: SDI 2개를 초기화 후 사용자 경로로 위치·크기 변경", "두 창 모두 초기화 위치·크기와 달라짐", {"afterReset": s_reset, "S0": s0}, "PASS" if changed else "판정불가(변경 실패)")
        page.screenshot(path=os.path.join(OUT, "persist_s0.png"))

        # A: 자동 저장 슬롯 → 새로고침(기동 자동 복원) → 명시 복원
        sv = r.call("layout.save")
        st_a = dump_storage(page)
        page.reload()
        r = ready(page)
        s1 = rects(r)
        ok1, rows1 = compare(s0, s1)
        rec("A1 자동 슬롯 저장 → 새로고침 → 기동 자동 복원: 위치·크기 그대로", "두 창 x,y,w,h ±1 일치(표시 managedBounds), dockWindow 기록", {"save": sv, "rows": rows1, "dockWindow": models(r), "docs": [(d.get("title"), d.get("state")) for d in (r.call("docs.list") or [])]}, "PASS" if ok1 else ("FAIL" if s1 else "FAIL(SDI 창이 복원되지 않음)"))
        ld = r.call("layout.load")
        time.sleep(1.5)
        s2 = rects(r)
        ok2, rows2 = compare(s0, s2)
        rec("A2 새로고침 뒤 layout.load(자동 슬롯 명시 복원): 위치·크기 그대로", "두 창 x,y,w,h ±1 일치", {"load": ld, "rows": rows2, "dockWindow": models(r)}, "PASS" if ok2 else "FAIL")
        page.screenshot(path=os.path.join(OUT, "persist_a2.png"))

        # B: 개인 레이아웃 저장 → 재기동 대리(페이지 닫고 같은 문맥에 새 페이지) → 이름으로 복원
        r.call("docs.close", all=True)
        ids = [r.call("docs.open", kind="form", title="PS-%d" % i, sdi=True, allowDuplicate=True)[0]["id"] for i in (1, 2)]
        time.sleep(1.5)
        r.call("sdi.resetPositions")
        time.sleep(1.0)
        lk = (r.call("ui.find", id="chrome.btnLock", doc=ids[1]) or {}).get("bounds")
        if lk:
            drag(page, lk["x"] - 150, lk["y"] + lk["h"] / 2, 160, 120)
        g = (r.call("ui.find", id="chrome.gripResize", doc=ids[0]) or {}).get("bounds")
        if g:
            drag(page, g["x"] + g["w"] - 3, g["y"] + g["h"] - 3, 90, 60)
        s0b = rects(r)
        svb = r.call("layout.save", name=NAME, overwrite=True)
        st_b = dump_storage(page)
        page.close()
        page = ctx.new_page()
        page.goto(t.URL + "?layout-key=" + KEY)
        r = ready(page)
        r.call("docs.close", all=True)
        time.sleep(0.8)
        ldb = r.call("layout.load", name=NAME)
        time.sleep(1.5)
        s3 = rects(r)
        ok3, rows3 = compare(s0b, s3)
        rec("B 개인 레이아웃 저장 → 재기동(새 페이지) → 이름으로 복원: 위치·크기 그대로", "두 창 x,y,w,h ±1 일치", {"save": svb, "load": ldb, "rows": rows3, "dockWindow": models(r)}, "PASS" if ok3 else ("FAIL" if s3 else "FAIL(SDI 창이 복원되지 않음)"))
        page.screenshot(path=os.path.join(OUT, "persist_b.png"))
        rec("저장 파일(localStorage) 안의 창 위치·크기 값", "SDI 창별 저장 사각형이 S0 와 같아야 함(기록)", {"afterAutoSave": st_a, "afterNamedSave": st_b, "S0": s0, "S0b": s0b}, "기록")
        ctx.close()

        # C: 끌기 없이 ⑦ 초기화로만 위치가 바뀐 뒤, 수동 저장 없이 새로고침 — 자동 저장이 그 위치를 남기는지(lead 09:5x 추가)
        keyc = KEY + "-C"
        ctx, page, r = t.boot(b, "?layout-key=" + keyc)
        r.call("docs.close", all=True)
        for i in (1, 2):
            r.call("docs.open", kind="form", title="PC-%d" % i, sdi=True, allowDuplicate=True)
            time.sleep(1.0)
        time.sleep(3.0)  # 창 열림 자동 저장이 끝나도록
        c_open = rects(r)
        st_open = stored_windows(page, keyc)
        r.call("sdi.resetPositions")
        time.sleep(3.0)
        c_reset = rects(r)
        st_reset = stored_windows(page, keyc)
        moved = len(c_reset) == 2 and any(not same(c_open[k], c_reset.get(k)) for k in c_open)
        saved_reset = bool(st_reset) and all(same(st_reset.get(k), c_reset.get(k)) for k in c_reset)
        rec("C1 ⑦ 초기화(끌기 없음) 뒤 자동 저장 슬롯이 초기화 위치를 담음(새로고침 전)", "localStorage Window X,Y,W,H = 초기화 뒤 표시값 ±1",
            {"display_open": c_open, "stored_open": st_open, "display_reset": c_reset, "stored_reset": st_reset},
            "판정불가(초기화로 위치가 안 바뀜)" if not moved else ("PASS" if saved_reset else "FAIL(자동 저장 안 됨 — 창 열림 때 값 그대로)" if st_reset == st_open else "FAIL"))
        page.reload()
        r = ready(page)
        st_after = stored_windows(page, keyc)
        c_after = rects(r)
        okc, rowsc = compare(c_reset, c_after)
        rec("C2 수동 저장 없이 새로고침 → 표시 위치가 초기화 위치", "두 창 x,y,w,h ±1 일치(표시), dockWindow·저장값 기록",
            {"rows": rowsc, "dockWindow": models(r), "stored_after_reload": st_after, "stored_before_reload": st_reset},
            "PASS" if okc else "FAIL")
        page.screenshot(path=os.path.join(OUT, "persist_c.png"))
        with open(os.path.join(OUT, "retest_web12.json"), "w", encoding="utf-8") as f:
            json.dump(res, f, ensure_ascii=False, indent=2)
        ctx.close()
        b.close()


if __name__ == "__main__":
    main()
