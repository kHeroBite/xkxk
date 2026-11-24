# 웹 태블릿/폰 모드(UI-18, ui.mode) 시험 — 좁은 뷰포트(400, hasTouch)는 폰 고정·태블릿 불가, 넓은 뷰포트(1024, hasTouch)는 태블릿↔폰 전환과 레이아웃 변화·문서 상태 유지를 원격 조회와 화소 차이로 판정
# 사용: PD_URL=http://127.0.0.1:5200/ python3 tools/retest_browser20.py   (진단판·헤드리스 전용, Android B3 와 같은 기준 — lead 12:0x)
"""
기준(MainViewModel.Compact.cs): CompactWidth=600. 폭<600 이면 IsNarrow=true → 항상 폰 방식(isCompact=true), 태블릿 모드 선택 불가.
  ui.mode → {uiMode, uiModeOverride, uiModeEffective, isCompact, viewWidth, uiModeAvailable, isNarrow}, set=auto|tablet|phone 은 실시간 전환.
항목:
  M1 좁은(400×800): uiModeAvailable=true(웹), isNarrow=true, isCompact=true, set=tablet 해도 isCompact=true 유지(태블릿 비활성)
  M2 넓은(1024×768) auto 기본 상태 기록
  M3 넓은: set=tablet → isCompact=false, set=phone → isCompact=true — 전환마다 화면 화소가 크게 바뀜(레이아웃 변화)
  M4 전환 뒤 문서 상태 유지: 문서 목록·활성 문서 같고, 그리드 선택 행(grid.select 300)·첫 보이는 행이 유지
"""
import json
import os
import sys
import time
from io import BytesIO

from PIL import Image, ImageChops
from playwright.sync_api import sync_playwright

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import retest_browser2 as t  # noqa: E402

OUT = t.OUT
res = []


def rec(item, expect, actual, verdict):
    res.append({"item": item, "expect": expect, "actual": actual, "verdict": verdict})
    print("%-6s %s | %s" % (verdict, item, str(actual)[:500]), flush=True)


def img(page, name):
    png = page.screenshot(path=os.path.join(OUT, "uimode_%s.png" % name))
    return Image.open(BytesIO(png)).convert("RGB")


def diff_frac(a, b):
    d = ImageChops.difference(a, b).convert("L")
    return round(sum(1 for v in d.getdata() if v > 24) / (a.size[0] * a.size[1]), 3)


def mode(r):
    time.sleep(0.6)  # 한 프레임 이상 뒤 다시 읽기(RemoteCommands.UiMode 주석)
    return r.call("ui.mode") or {}


def grid_instances(r):
    """grid.dgMain 뷰 인스턴스 수·보이는 수(p1137 실측: 폰 모드에서 두 번째 인스턴스가 생기고 원래 것은 숨음 — grid.visible 은 첫 인스턴스(숨은 것)를 읽는다)."""
    from retest_browser11 import _walk, full_tree
    tr = full_tree(r)
    ns = [n for n, _ in _walk(tr.get("tree"), []) if n.get("id") == "grid.dgMain"]
    return {"count": len(ns), "visible": sum(1 for n in ns if n.get("visible")), "truncated": tr.get("truncated")}


def doc_state(r, g):
    docs = r.call("docs.list") or []
    gv = r.call("grid.visible", id="grid.dgMain", doc=g) or {}
    return {"docs": [(d.get("id"), d.get("title")) for d in docs], "active": next((d.get("id") for d in docs if d.get("active")), None),
            "grid_selected": gv.get("selectedIndex"), "grid_first": gv.get("first"), "grid_instances": grid_instances(r)}


def open_docs(r):
    r.call("docs.close", all=True)
    g = r.call("docs.open", kind="grid", title="UM-grid", allowDuplicate=True)[0]["id"]
    r.call("docs.open", kind="form", title="UM-form", allowDuplicate=True)
    time.sleep(1.0)
    r.call("docs.activate", doc=g)
    time.sleep(0.6)
    r.call("grid.select", id="grid.dgMain", index=300, scroll=True, doc=g)
    time.sleep(0.6)
    return g


def main():
    with sync_playwright() as p:
        b = p.chromium.launch(headless=True, args=t.ARGS)
        # 좁은 뷰포트
        ctx = t.allow_dup(b.new_context(viewport={"width": 400, "height": 800}, has_touch=True))
        ctx, page, r = t.boot(b, "?layout-key=otestw2-uimode-n", w=400, h=800, ctx=ctx)
        r.call("ui.mode", set="auto")
        g = open_docs(r)
        m0 = mode(r)
        i0 = img(page, "narrow_auto")
        s = r.call("ui.mode", set="tablet") or {}
        m1 = mode(r)
        i1 = img(page, "narrow_set_tablet")
        rec("M1 좁은 화면(400): 폰 고정·태블릿 비활성", "uiModeAvailable=true, isNarrow=true, isCompact=true(auto·tablet 설정 모두), 화면 변화 거의 없음",
            {"auto": m0, "after_set_tablet": m1, "set_resp": s, "pixel_diff_frac": diff_frac(i0, i1)},
            "PASS" if m0.get("uiModeAvailable") and m0.get("isNarrow") and m0.get("isCompact") and m1.get("isCompact") and m1.get("isNarrow") else "FAIL")
        r.call("ui.mode", set="auto")
        ctx.close()

        # 넓은 뷰포트
        ctx = t.allow_dup(b.new_context(viewport={"width": 1024, "height": 768}, has_touch=True))
        ctx, page, r = t.boot(b, "?layout-key=otestw2-uimode-w", w=1024, h=768, ctx=ctx)
        r.call("ui.mode", set="auto")
        g = open_docs(r)
        ma = mode(r)
        ia = img(page, "wide_auto")
        st0 = doc_state(r, g)
        rec("M2 넓은 화면(1024) auto 기본 상태", "기록", {"mode": ma, "docState": st0}, "기록")
        r.call("ui.mode", set="tablet")
        mt = mode(r)
        it = img(page, "wide_tablet")
        st_t = doc_state(r, g)
        r.call("ui.mode", set="phone")
        mp = mode(r)
        ip = img(page, "wide_phone")
        st_p = doc_state(r, g)
        r.call("ui.mode", set="tablet")
        mt2 = mode(r)
        it2 = img(page, "wide_tablet_again")
        st_t2 = doc_state(r, g)
        rec("M3 넓은 화면 태블릿↔폰 전환과 레이아웃 변화", "tablet→isCompact=false, phone→isCompact=true, 다시 tablet→false, 전환마다 화면 화소 변화 큼(>10%)",
            {"tablet": mt, "phone": mp, "tablet_again": mt2, "diff_tablet_phone": diff_frac(it, ip), "diff_phone_tablet2": diff_frac(ip, it2), "diff_auto_tablet": diff_frac(ia, it)},
            "PASS" if mt.get("isCompact") is False and mp.get("isCompact") is True and mt2.get("isCompact") is False and diff_frac(it, ip) > 0.1 and diff_frac(ip, it2) > 0.1 else "FAIL")

        def same(a, c):
            return a["docs"] == c["docs"] and a["active"] == c["active"] and a["grid_selected"] == c["grid_selected"]
        # grid.visible 은 첫 인스턴스를 읽으므로, 폰 모드에서 인스턴스가 둘이고 그중 보이는 것이 다른 인스턴스면 '선택·스크롤이 화면에 유지되는지'를 원격으로 판정할 수 없다 → 캡처(uimode_wide_phone.png)로 판정
        new_view = st_p["grid_instances"]["count"] > st0["grid_instances"]["count"] and st_p["grid_first"] is None
        rec("M4 전환 뒤 문서 상태 유지", "문서 목록·활성 문서 같음 + 보이는 그리드에서 선택 행(300)·스크롤 위치 유지(폰 모드 포함)",
            {"auto": st0, "tablet": st_t, "phone": st_p, "tablet_again": st_t2, "phone_new_view_instance": new_view},
            ("FAIL(폰 모드는 새 그리드 뷰 인스턴스 — 원래 뷰는 숨고 선택·스크롤 미유지, 태블릿 복귀 때 원래 뷰로 돌아옴)" if new_view else
             ("PASS" if st0["grid_selected"] == 300 and same(st0, st_t) and same(st0, st_p) and same(st0, st_t2) else "FAIL")))
        # M5 간트·폼 문서도 폰 전환 뒤 뷰 인스턴스가 늘어나는지(lead 12:1x) — 문서마다 단독으로 열고 본문 대표 id 노드 수를 태블릿·폰에서 센다
        from retest_browser11 import _walk, full_tree

        def count_id(node_id):
            tr = full_tree(r)
            ns = [n for n, _ in _walk(tr.get("tree"), []) if n.get("id") == node_id]
            return {"count": len(ns), "visible": sum(1 for n in ns if n.get("visible")), "truncated": tr.get("truncated")}
        m5 = {}
        for kind, body in (("grid", "grid.dgMain"), ("gantt", "gantt.lblZoom"), ("form", "form.txtTitle")):
            r.call("ui.mode", set="tablet")
            r.call("docs.close", all=True)
            r.call("docs.open", kind=kind, title="UM5-" + kind, allowDuplicate=True)
            time.sleep(1.0)
            ct = count_id(body)
            r.call("ui.mode", set="phone")
            time.sleep(0.8)
            cp = count_id(body)
            m5[kind] = {"body_id": body, "tablet": ct, "phone": cp}
        rec("M5 문서 종류별 폰 전환 뒤 뷰 인스턴스 수(grid·gantt·form)", "폰 전환 뒤에도 본문 노드 1개(새 뷰 인스턴스가 생기지 않음)", m5,
            "PASS" if all(v["phone"]["count"] == 1 for v in m5.values()) else "FAIL(폰 모드에서 뷰 인스턴스 증가: %s)" % ",".join(k for k, v in m5.items() if v["phone"]["count"] > 1))
        r.call("ui.mode", set="auto")
        with open(os.path.join(OUT, "retest_web20_uimode.json"), "w", encoding="utf-8") as f:
            json.dump(res, f, ensure_ascii=False, indent=2)
        b.close()


if __name__ == "__main__":
    main()
