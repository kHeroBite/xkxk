# 웹 벤치 그리드 가로 스크롤(frozen 2 기본 → left=0 → left=2)과 정렬 app·ordinal 확인(odev-gc i1239bw 묶음) — 판정은 단계별 캡처를 눈으로, 수치는 기록
# 사용: PD_URL=http://127.0.0.1:5211/ python3 tools/retest_browser24.py <태그 예 i1239bw>   → logs/odevgc/hscroll_web_<태그>_*.png·json, bench_web_<impl>_<태그>_sort.json
import json
import os
import sys
import time

from playwright.sync_api import sync_playwright

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import retest_browser2 as t  # noqa: E402

TAG = sys.argv[1] if len(sys.argv) > 1 else "bench"
OUTD = os.path.join(HERE, "..", "logs", "odevgc")


def raw(page, cmd, args):
    txt = page.evaluate("b => window.pdAutomation.invoke(b)", json.dumps({"cmd": cmd, "args": args}, ensure_ascii=False))
    try:
        return json.loads(txt)
    except ValueError:
        return {"ok": False, "error": str(txt)[:300]}


def main():
    out = {"tag": TAG, "url": t.URL, "hscroll": [], "sort": {}}
    with sync_playwright() as p:
        b = p.chromium.launch(headless=True, args=t.ARGS)
        ctx, page, r = t.boot(b, "?layout-key=otestw2-b24")
        r.call("docs.close", all=True)
        r.call("bench.open", target="grid")
        time.sleep(1.5)
        d = next((x["id"] for x in (r.call("docs.list") or []) if x.get("active")), None)
        r.call("docs.state", doc=d, state="maximized")
        time.sleep(0.8)
        env = raw(page, "bench.env", {})
        out["env_collation"] = ((env.get("result") or {}).get("collation"))
        ld = raw(page, "bench.grid", {"scenario": "load", "rows": 10000})
        out["impl"] = (ld.get("result") or {}).get("impl")
        time.sleep(1.5)
        gb = (r.call("ui.find", id="bench.grid.main", doc=d) or {}).get("bounds") or {"x": 600, "y": 450, "w": 10, "h": 10}
        cx, cy = gb["x"] + gb["w"] / 2, gb["y"] + gb["h"] / 2

        def run(label, frozen_args):
            if frozen_args is not None:
                fz = raw(page, "bench.grid", dict(scenario="frozen", **frozen_args))
                out["hscroll"].append({"set": label, "frozen_resp": {k: fz.get(k) for k in ("ok", "error")} | {"result": fz.get("result")}})
                time.sleep(1.0)
            raw(page, "ui.scroll", {"id": "bench.grid.main", "doc": d, "index": 0})  # 맨 위로
            time.sleep(0.5)
            shots = []
            for i, (name, fn) in enumerate((("before", None), ("wheelX", lambda: page.mouse.wheel(400, 0)),
                                            ("shiftWheel", lambda: (page.keyboard.down("Shift"), page.mouse.wheel(0, 400), page.keyboard.up("Shift"))),
                                            ("vWheel", lambda: page.mouse.wheel(0, 300)), ("nudge1px", lambda: page.mouse.move(cx + 1, cy)))):
                page.mouse.move(cx, cy) if name == "before" else None
                if fn:
                    fn()
                time.sleep(1.2 if name != "nudge1px" else 2.0)
                path = os.path.join(OUTD, "hscroll_web_%s_%s_%d_%s.png" % (TAG, label, i, name))
                page.screenshot(path=path)
                gv = raw(page, "grid.visible", {"id": "bench.grid.main", "doc": d}).get("result")
                shots.append({"step": name, "png": os.path.basename(path), "visible": gv})
            out["hscroll"].append({"set": label, "steps": shots})
            print(label, [(s["step"], (s["visible"] or {}).get("first")) for s in shots], flush=True)

        run("default", None)
        run("left0", {"left": 0, "right": 0})
        run("left2", {"left": 2, "right": 0})

        # 정렬(③): 영문 이름만 남긴 뒤 culture=app·ordinal·ko-KR 의 head·head_distinct
        raw(page, "bench.grid", {"scenario": "filter", "latin": True})
        for cul in ("app", "ordinal", "ko-KR"):
            s = raw(page, "bench.grid", {"scenario": "sort", "property": "Name", "culture": cul})
            out["sort"]["latin_" + cul] = (s.get("result") or {}).get("sort") if s.get("ok") else {"error": s.get("error")}
        raw(page, "bench.grid", {"scenario": "filter", "text": ""})
        for cul in ("app", "ordinal", "ko-KR"):
            s = raw(page, "bench.grid", {"scenario": "sort", "property": "Name", "culture": cul})
            out["sort"]["all_" + cul] = (s.get("result") or {}).get("sort") if s.get("ok") else {"error": s.get("error")}
        b.close()
    impl = "Pro" if "Pro" in (out.get("impl") or "") else "Builtin"
    with open(os.path.join(OUTD, "hscroll_web_%s.json" % TAG), "w", encoding="utf-8") as f:
        json.dump({k: v for k, v in out.items() if k != "sort"}, f, ensure_ascii=False, indent=1)
    with open(os.path.join(OUTD, "bench_web_%s_%s_sort.json" % (impl, TAG)), "w", encoding="utf-8") as f:
        json.dump({"env_collation": out.get("env_collation"), "sort": out["sort"]}, f, ensure_ascii=False, indent=1)
    print("ENV", json.dumps(out.get("env_collation"), ensure_ascii=False)[:600], flush=True)
    for k, v in out["sort"].items():
        print("SORT", k, json.dumps({kk: (v or {}).get(kk) for kk in ("culture", "head", "head_distinct", "error")}, ensure_ascii=False)[:400], flush=True)


if __name__ == "__main__":
    main()
