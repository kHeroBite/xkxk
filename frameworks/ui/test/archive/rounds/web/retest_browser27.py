# 웹 F1(SDI 전환 때 같은 뷰 재사용) 판정 — 300행 선택 후 SDI 분리·재결합 3회, 회마다 viewHash·선택·스크롤·[sdi] 원문, 이어서 300 재선택 뒤 R1 폰↔태블릿 왕복 3회
# 사용: PD_URL=http://127.0.0.1:5214/ python3 tools/retest_browser27.py <태그>   (진단판·헤드리스 전용)
import json
import os
import sys
import time

from playwright.sync_api import sync_playwright

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import retest_browser2 as t  # noqa: E402
from retest_browser25 import colors, count, gv  # noqa: E402

TAG = sys.argv[1] if len(sys.argv) > 1 else "f1"
OUT = os.path.join(t.OUT, TAG)
res = []


def rec(item, expect, actual, verdict):
    res.append({"item": item, "expect": expect, "actual": actual, "verdict": verdict})
    print("%-6s %s | %s" % (verdict, item, str(actual)[:600]), flush=True)


def main():
    os.makedirs(OUT, exist_ok=True)
    with sync_playwright() as p:
        b = p.chromium.launch(headless=True, args=t.ARGS)
        ctx = t.allow_dup(b.new_context(viewport={"width": 1280, "height": 800}, has_touch=True))
        ctx, page, r = t.boot(b, "?layout-key=otestw2-f1", ctx=ctx)
        con = []
        page.on("console", lambda m: con.append(m.text) if "[sdi]" in m.text else None)
        r.call("ui.mode", set="tablet")
        r.call("docs.close", all=True)
        g = r.call("docs.open", kind="grid", title="F1-grid", allowDuplicate=True)[0]["id"]
        time.sleep(1.5)
        r.call("docs.activate", doc=g)
        r.call("grid.select", id="grid.dgMain", index=300, scroll=True, doc=g)
        time.sleep(0.8)
        s0 = gv(r, g)
        cycles = []
        for i in range(3):
            n0 = len(con)
            r.call("docs.sdi", doc=g, on=True)
            time.sleep(1.0)
            sf = gv(r, g)
            gb = (r.call("ui.find", id="grid.dgMain", doc=g) or {}).get("bounds")
            cf = colors(page, gb)
            page.screenshot(path=os.path.join(OUT, "f1_float_%d.png" % (i + 1)))
            cnt_f, _ = count(r, ["grid.dgMain"])
            r.call("docs.sdi", doc=g, on=False)
            time.sleep(1.0)
            sd = gv(r, g)
            page.screenshot(path=os.path.join(OUT, "f1_redock_%d.png" % (i + 1)))
            cnt_d, _ = count(r, ["grid.dgMain"])
            lines = con[n0:]
            cycles.append({"cycle": i + 1, "float": sf, "redock": sd, "float_colors": cf,
                           "instances_float": cnt_f["grid.dgMain"], "instances_redock": cnt_d["grid.dgMain"],
                           "sdi_lines": lines,
                           "float_laid_hit": [("hit=True" in x) for x in lines if "float.laid" in x]})
        tail = r.call("log.tail", n=200) or {}
        tail_sdi = [x for x in json.dumps(tail, ensure_ascii=False).split("\\n") if "[sdi]" in x][:60] if not isinstance(tail, list) else [str(x) for x in tail if "[sdi]" in str(x)][:60]

        def same(st):
            return st.get("viewHash") == s0.get("viewHash") and st.get("gridHash") == s0.get("gridHash") \
                and st.get("selectedIndex") == 300 and st.get("first") is not None and s0.get("first") is not None \
                and abs(st["first"] - s0["first"]) <= 1
        ok = all(same(c["float"]) and same(c["redock"]) and c["instances_float"] == 1 and c["instances_redock"] == 1
                 and (c["float_colors"] or 0) >= 10 for c in cycles)
        # float.laid hit=False·cached=null 은 Dock 이 새 창을 붙일 때 캐시를 지우는 기대 동작(odev-2r·lead 13:37) — 판정 제외, redock cached id 동일만 본다
        cached_ids = [x.split("cached=")[1].split("(")[0] for x in tail_sdi if "redock.before" in x and "cached=" in x]
        ok = ok and len(cached_ids) >= 3 and len(set(cached_ids)) == 1
        rec("F1 SDI 분리·재결합 3회: 같은 뷰 재사용", "매 단계 viewHash·gridHash 처음과 같음, 선택 300, 첫 행 ±1, grid.dgMain 1, 분리 창 본문 10색 이상, [sdi] redock.before cached 3회 같은 id(float.laid hit=False 는 기대값)",
            {"mdi0": s0, "cycles": cycles, "log_tail_sdi": tail_sdi}, "PASS" if ok else "FAIL")

        # R1: SDI 단계 뒤 300 재선택하고 시작(lead 지시)
        r.call("docs.activate", doc=g)
        r.call("grid.select", id="grid.dgMain", index=300, scroll=True, doc=g)
        time.sleep(0.8)
        trips = []
        for i in range(3):
            for mode in ("phone", "tablet"):
                r.call("ui.mode", set=mode)
                time.sleep(0.8)
                st = gv(r, g)
                c, _ = count(r, ["grid.dgMain"])
                trips.append({"trip": i + 1, "mode": mode, "first": st["first"], "selected": st["selectedIndex"], "viewHash": st["viewHash"], "instances": c["grid.dgMain"]})
        rec("R1 (300 재선택 후) 그리드 폰↔태블릿 왕복 3회: 인스턴스 1·선택·첫 행 유지", "매 단계 grid.dgMain 1, selectedIndex 300, 첫 행 ±1, viewHash 불변", trips,
            "PASS" if all(x["instances"] == 1 and x["selected"] == 300 and x["first"] is not None and abs(x["first"] - trips[0]["first"]) <= 1 and x["viewHash"] == trips[0]["viewHash"] for x in trips) else "FAIL")
        r.call("ui.mode", set="auto")
        with open(os.path.join(OUT, "retest_web27.json"), "w", encoding="utf-8") as fo:
            json.dump(res, fo, ensure_ascii=False, indent=2)
        b.close()


if __name__ == "__main__":
    main()
