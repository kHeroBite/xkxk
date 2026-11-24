# 웹 M4(폰 모드 뷰 재사용) 남은 판정과 SDI 캐시 [sdi] 로그 절차(odev-2r) — SDI 왕복 해시·본문 화소, 테마 10회 뷰 유지, 폰 모드 활성 전환 뒤 MDI 3창, 그리드 왕복 3회·폼 4칸·간트 가로 스크롤/배율 유지
# 사용: PD_URL=http://127.0.0.1:5212/ python3 tools/retest_browser25.py <태그>   (진단판·헤드리스 전용, 관리형 SDI 창은 캔버스 안이라 화면 노출 없음)
import json
import os
import sys
import time
from io import BytesIO

from PIL import Image
from playwright.sync_api import sync_playwright

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import retest_browser2 as t  # noqa: E402
from retest_browser11 import _walk, full_tree  # noqa: E402

TAG = sys.argv[1] if len(sys.argv) > 1 else "m4"
OUT = os.path.join(t.OUT, TAG)
res = []
BODY = {"grid": "grid.dgMain", "gantt": "gantt.lblZoom", "form": "form.txtTitle"}


def rec(item, expect, actual, verdict):
    res.append({"item": item, "expect": expect, "actual": actual, "verdict": verdict})
    print("%-6s %s | %s" % (verdict, item, str(actual)[:500]), flush=True)


def colors(page, b):
    if not b or not b.get("w") or not b.get("h"):
        return None
    vp = page.viewport_size
    x0, y0 = max(0, b["x"]), max(0, b["y"])
    w, h = min(vp["width"] - x0, b["w"]), min(vp["height"] - y0, b["h"])
    if w < 2 or h < 2:
        return 0
    c = Image.open(BytesIO(page.screenshot(clip={"x": x0, "y": y0, "width": w, "height": h}))).convert("RGB").getcolors(maxcolors=8192)
    return len(c) if c else 8192


def count(r, ids, types=()):
    tr = full_tree(r)
    out = {i: 0 for i in ids}
    out.update({ty: 0 for ty in types})
    log_sdi = []
    for n, _ in _walk(tr.get("tree"), []):
        if n.get("id") in out:
            out[n["id"]] += 1
        if n.get("type") in out:
            out[n["type"]] += 1
        if "[sdi]" in str(n.get("text") or ""):
            log_sdi.append(n.get("text"))
    out["truncated"] = tr.get("truncated")
    return out, log_sdi


def gv(r, d):
    v = r.call("grid.visible", id="grid.dgMain", doc=d) or {}
    return {k: v.get(k) for k in ("first", "selectedIndex", "gridHash", "viewHash")}


def main():
    os.makedirs(OUT, exist_ok=True)
    with sync_playwright() as p:
        b = p.chromium.launch(headless=True, args=t.ARGS)
        ctx = t.allow_dup(b.new_context(viewport={"width": 1280, "height": 800}, has_touch=True))
        ctx, page, r = t.boot(b, "?layout-key=otestw2-m4rest", ctx=ctx)
        con = []
        page.on("console", lambda m: con.append(m.text) if "[sdi]" in m.text else None)
        r.call("ui.mode", set="tablet")
        r.call("docs.close", all=True)
        g = r.call("docs.open", kind="grid", title="M4-grid", allowDuplicate=True)[0]["id"]
        n = r.call("docs.open", kind="gantt", title="M4-gantt", allowDuplicate=True)[0]["id"]
        f = r.call("docs.open", kind="form", title="M4-form", allowDuplicate=True)[0]["id"]
        time.sleep(1.5)
        r.call("layout.template.apply", id="tpl.mdi-cascade")
        r.call("docs.activate", doc=g)
        time.sleep(0.6)
        r.call("grid.select", id="grid.dgMain", index=300, scroll=True, doc=g)
        time.sleep(0.6)

        # ① SDI 왕복([sdi] 로그 절차, odev-2r) + SDI 본문 화소(M4 조건 ②)
        s0 = gv(r, g)
        r.call("docs.sdi", doc=g, on=True)
        time.sleep(0.4)
        s1 = gv(r, g)
        time.sleep(0.6)
        page.screenshot(path=os.path.join(OUT, "sdi_float.png"))
        gb = (r.call("ui.find", id="grid.dgMain", doc=g) or {}).get("bounds")
        c_sdi = colors(page, gb)
        cnt_sdi, _ = count(r, ["grid.dgMain"])
        r.call("docs.sdi", doc=g, on=False)
        time.sleep(0.4)
        s2 = gv(r, g)
        time.sleep(0.6)
        page.screenshot(path=os.path.join(OUT, "sdi_redock.png"))
        cnt_back, log_tree = count(r, ["grid.dgMain"])
        rec("S1 SDI 왕복 해시·[sdi] 로그(기록)", "MDI→SDI→MDI 각 단계 grid.visible(gridHash·viewHash·선택·첫 행)과 [sdi] 줄 원문",
            {"mdi": s0, "sdi_400ms": s1, "redock_400ms": s2, "console_sdi": con[:20], "logpanel_sdi": log_tree[:20]}, "기록")
        rec("S2 SDI(관리형 창) 활성 문서 본문이 그려짐(M4 조건 ②)", "관리형 창 안 그리드 본문 영역 10색 이상, grid.dgMain 1개",
            {"colors": c_sdi, "bounds": gb, "instances_sdi": cnt_sdi, "instances_back": cnt_back},
            "PASS" if (c_sdi or 0) >= 10 and cnt_sdi["grid.dgMain"] == 1 else "FAIL")

        # ② 테마 10회 동안 뷰 유지(M4 조건 ③)
        r.call("docs.activate", doc=g)
        h0 = gv(r, g)
        c0, _ = count(r, ["grid.dgMain"], ("MdiDocumentControl", "MdiDocumentWindow"))
        hs = []
        for i in range(10):
            r.call("theme.select", id="pd-dark" if i % 2 == 0 else "pd-ocean")
            time.sleep(0.4)
            hs.append(gv(r, g).get("viewHash"))
        c1, _ = count(r, ["grid.dgMain"], ("MdiDocumentControl", "MdiDocumentWindow"))
        rec("T1 테마 10회 전환 동안 문서 뷰 재생성 없음", "viewHash 10회 모두 처음과 같음, MdiDocumentControl·grid.dgMain 개수 불변",
            {"viewHash0": h0.get("viewHash"), "viewHashes": hs, "before": c0, "after": c1},
            "PASS" if all(x == h0.get("viewHash") for x in hs) and c0 == c1 else "FAIL")

        # ③ 그리드 왕복 3회, 폼 4칸, 간트 가로 스크롤·배율
        texts = {"form.txtTitle": "제목-가", "form.txtOwner": "담당-나", "form.txtClient": "고객-다", "form.txtPlace": "장소-라"}
        for k, v in texts.items():
            r.call("ui.setText", id=k, doc=f, text=v)
        r.call("docs.activate", doc=n)
        time.sleep(0.4)
        r.call("ui.click", id="gantt.btnZoomIn", doc=n)
        gs = (r.call("ui.find", id="gantt.hScroll", doc=n) or {}).get("bounds")
        if gs:
            page.mouse.move(gs["x"] + gs["w"] / 2, gs["y"] - 60)
            page.mouse.wheel(500, 0)
        time.sleep(0.6)

        def gantt_state():
            return {"zoom": (r.call("ui.find", id="gantt.lblZoom", doc=n) or {}).get("text"), "hScroll": (r.call("ui.find", id="gantt.hScroll", doc=n) or {}).get("value")}

        def form_state():
            return {k: (r.call("ui.find", id=k, doc=f) or {}).get("text") for k in texts}
        g0, fm0 = gantt_state(), form_state()
        r.call("docs.activate", doc=g)
        time.sleep(0.4)
        trips = []
        for i in range(3):
            for mode in ("phone", "tablet"):
                r.call("ui.mode", set=mode)
                time.sleep(0.8)
                st = gv(r, g)
                c, _ = count(r, ["grid.dgMain"])
                trips.append({"trip": i + 1, "mode": mode, "first": st["first"], "selected": st["selectedIndex"], "viewHash": st["viewHash"], "instances": c["grid.dgMain"]})
        rec("R1 그리드 폰↔태블릿 왕복 3회: 인스턴스 1·선택·첫 행 유지", "매 단계 grid.dgMain 1, selectedIndex 300, 첫 행 ±1, viewHash 불변", trips,
            "PASS" if all(x["instances"] == 1 and x["selected"] == 300 and x["first"] is not None and abs(x["first"] - trips[0]["first"]) <= 1 and x["viewHash"] == trips[0]["viewHash"] for x in trips) else "FAIL")
        r.call("ui.mode", set="phone")
        r.call("docs.activate", doc=f)
        time.sleep(0.8)
        fm_p = form_state()
        page.screenshot(path=os.path.join(OUT, "form_phone.png"))
        r.call("docs.activate", doc=n)
        time.sleep(0.8)
        g_p = gantt_state()
        r.call("ui.mode", set="tablet")
        time.sleep(0.8)
        fm_t, g_t = form_state(), gantt_state()
        rec("R2 폼 입력 4칸 내용 유지(폰·태블릿)", "4칸 글자가 폰·태블릿 모두 같음", {"before": fm0, "phone": fm_p, "tablet": fm_t}, "PASS" if fm0 == fm_p == fm_t == texts else "FAIL")
        rec("R3 간트 배율·가로 스크롤 유지(폰·태블릿)", "배율 글자·hScroll 값 유지", {"before": g0, "phone": g_p, "tablet": g_t}, "PASS" if g0 == g_p == g_t and g0.get("zoom") else "FAIL")

        # ④ 폰 모드에서 활성 문서 Grid→Gantt→Form 전환 뒤 태블릿 복귀: MDI 3창 실현(세로 바둑판으로 펼쳐 본문 화소)
        r.call("ui.mode", set="phone")
        for d in (g, n, f):
            r.call("docs.activate", doc=d)
            time.sleep(0.6)
        r.call("ui.mode", set="tablet")
        time.sleep(0.8)
        r.call("layout.template.apply", id="tpl.mdi-tile-v")
        time.sleep(1.0)
        page.screenshot(path=os.path.join(OUT, "mdi3_tile.png"))
        cnt, _ = count(r, list(BODY.values()), ("MdiDocumentWindow",))
        px = {k: colors(page, (r.call("ui.find", id=v, doc={"grid": g, "gantt": n, "form": f}[k]) or {}).get("bounds")) for k, v in BODY.items()}
        rec("P1 폰 모드 활성 전환 뒤 태블릿 복귀: MDI 3창 모두 실현", "MdiDocumentWindow 3, 본문 노드 각 1, 본문 영역 각 10색 이상", {"counts": cnt, "colors": px},
            "PASS" if cnt.get("MdiDocumentWindow") == 3 and all(cnt[v] == 1 for v in BODY.values()) and all((x or 0) >= 10 for x in px.values()) else "FAIL")
        r.call("ui.mode", set="auto")
        with open(os.path.join(OUT, "retest_web25.json"), "w", encoding="utf-8") as fo:
            json.dump(res, fo, ensure_ascii=False, indent=2)
        b.close()


if __name__ == "__main__":
    main()
