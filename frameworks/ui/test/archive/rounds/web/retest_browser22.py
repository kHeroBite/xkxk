# 웹 그리드 가로 스크롤 빈 칸 결함 원인 가르기(odev-gc 긴급 대조) — 제품 그리드(frozen 0)·벤치 그리드(frozen 2, rows 50)에서 가로·세로 이동 뒤 화소와 셀 텍스트를 덤프
# 사용: PD_URL=http://127.0.0.1:5206/ python3 tools/retest_browser22.py <Builtin|Pro> <product|bench50>   → logs/odevgc/hscroll_web_<impl>_<case>.json/.png
"""
product : 뷰포트 800×700 에서 docs.open kind=grid(제품 GridDocumentView, FrozenColumnCount 미사용) 를 최대화해 가로 스크롤을 만든 뒤
          가로 휠 → Shift+휠 → ui.scroll id=grid.dgMain col=6 → 세로 휠 → ui.scroll lines=20 순서로 진행.
bench50 : 벤치 그리드(frozen 2)를 rows=50 으로 띄워 가로 휠 → 세로 휠(가상화·재활용 경로 영향 확인).
단계마다: 그리드 본문(첫 열 제외) 영역 화소 색 수, 보이는 DataGridRow 앞 3개의 셀 TextBlock 텍스트(값이 빈 것인지 그리기만 안 된 것인지), 캡처.
"""
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

IMPL = sys.argv[1] if len(sys.argv) > 1 else "Builtin"
CASE = sys.argv[2] if len(sys.argv) > 2 else "product"
DPR = float(os.environ.get("HS_DPR", "1"))  # lead 판별 신호 ②: dpr 1 과 2.625
OUTD = os.environ.get("HS_OUTD") or os.path.join(HERE, "..", "logs", "odevgc")
BASE = os.path.join(OUTD, os.environ.get("HS_NAME") or "hscroll_web_%s_%s" % (IMPL, CASE))


def colors(page, clip):
    c = Image.open(BytesIO(page.screenshot(clip=clip))).convert("RGB").getcolors(maxcolors=8192)
    return len(c) if c else 8192


def row_cells(r, grid_id, n=3):
    """보이는 DataGridRow 앞 n 개의 셀 텍스트(TextBlock text·visible·x). 트리 순서 기준."""
    tr = full_tree(r)
    rows = []
    for node, path in _walk(tr.get("tree"), []):
        if node.get("type") == "DataGridRow" and node.get("visible") and any(x.get("id") == grid_id for x in path):
            cells = [(c.get("text"), c.get("visible"), (c.get("bounds") or {}).get("x")) for c, _ in _walk(node.get("children"), []) if c.get("type") == "TextBlock"]
            rows.append({"bounds": node.get("bounds"), "cells": cells[:24]})
            if len(rows) >= n:
                break
    return {"truncated": tr.get("truncated"), "rows": rows}


def main():
    os.makedirs(OUTD, exist_ok=True)
    out = {"impl": IMPL, "case": CASE, "url": t.URL, "steps": []}
    with sync_playwright() as p:
        # 고DPR 은 odev-3 방법으로만: 실행 인자 --force-device-scale-factor 와 컨텍스트 device_scale_factor 를 함께(하나만 주면 devicePixelContentBoxSize 가 어긋나 앱 폭이 800/dpr 로 잘못 잡힘)
        launch_args = list(t.ARGS) + (["--force-device-scale-factor=%s" % DPR] if DPR != 1 else [])
        b = p.chromium.launch(headless=True, args=launch_args)
        if CASE == "product":
            ctx = t.allow_dup(b.new_context(viewport={"width": 800, "height": 700}, device_scale_factor=DPR))
            ctx, page, r = t.boot(b, "?layout-key=otestw2-hscroll-p", w=800, h=700, ctx=ctx)
            r.call("docs.close", all=True)
            d = r.call("docs.open", kind="grid", title="HS-grid", allowDuplicate=True)[0]["id"]
            gid = "grid.dgMain"
        else:
            ctx, page, r = t.boot(b, "?layout-key=otestw2-hscroll-b")
            r.call("docs.close", all=True)
            r.call("bench.open", target="grid")
            time.sleep(1.5)
            d = next((x["id"] for x in (r.call("docs.list") or []) if x.get("active")), None)
            gid = "bench.grid.main"
        time.sleep(1.0)
        r.call("docs.state", doc=d, state="maximized")
        time.sleep(0.8)
        if CASE == "bench50":
            r.call("bench.grid", scenario="load", rows=50, _timeout=120)
            time.sleep(1.5)
        gb = (r.call("ui.find", id=gid, doc=d) or {}).get("bounds") or {"x": 200, "y": 300, "w": 500, "h": 300}
        clip = {"x": gb["x"] + 180, "y": gb["y"] + 40, "width": max(50, min(gb["w"] - 200, 600)), "height": max(50, min(gb["h"] - 60, 220))}
        cx, cy = gb["x"] + gb["w"] / 2, gb["y"] + gb["h"] / 2

        def step(name, fn=None):
            res = fn() if fn else None
            time.sleep(1.2)
            page.screenshot(path="%s_%d_%s.png" % (BASE, len(out["steps"]), name))
            s = {"step": name, "colors": colors(page, clip), "cells": row_cells(r, gid), "resp": res,
                 "visible": r.call("grid.visible", id=gid, doc=d)}
            out["steps"].append(s)
            print(name, s["colors"], json.dumps([x["cells"][:6] for x in s["cells"]["rows"]], ensure_ascii=False)[:300], flush=True)

        page.mouse.move(cx, cy)
        out["env"] = {"devicePixelRatio": page.evaluate("() => window.devicePixelRatio"), "innerWidth": page.evaluate("() => innerWidth"),
                      "uiMode": r.call("ui.mode")}
        out["mtrace_clear"] = r.call("grid.mtrace", action="clear") is not None  # 진단판에 없으면 False
        step("before")
        step("wheelX", lambda: page.mouse.wheel(400, 0))
        if CASE == "product":
            def shift_wheel():
                page.keyboard.down("Shift")
                page.mouse.wheel(0, 400)
                page.keyboard.up("Shift")
            step("shiftWheel", shift_wheel)
            step("uiScrollCol6", lambda: r.call("ui.scroll", id=gid, doc=d, col=6))
        step("vWheel", lambda: page.mouse.wheel(0, 300))
        if CASE == "product":
            step("uiScrollLines20", lambda: r.call("ui.scroll", id=gid, doc=d, lines=20))
        # lead 판별 신호 ①: 빈 칸이 생긴 뒤 아무 입력 1회(마우스 1px) → 바로 채워지면 '그리기 누락', 그대로면 '레이아웃·행 재활용'. 2초 뒤 한 번 더.
        step("nudge1px", lambda: page.mouse.move(cx + 1, cy))
        time.sleep(2.0)
        step("after2s")
        # odev-3 판별 D: 절차 동안의 그리드 측정 기록(total·byWidth) + 위 5행 DataGridRow·셀 bounds(위 1~4행 어긋남 확인용)
        mt = r.call("grid.mtrace", action="get", limit=400)
        out["mtrace"] = {k: (mt or {}).get(k) for k in ("total", "passes", "stabilized", "byWidth")} if mt is not None else None
        out["mtrace_raw_head"] = ((mt or {}).get("calls") or [])[:5]
        tr = full_tree(r)
        top = []
        for node, path in _walk(tr.get("tree"), []):
            if node.get("type") == "DataGridRow" and node.get("visible") and any(x.get("id") == gid for x in path):
                top.append({"row": node.get("bounds"), "cells": [(c.get("text"), (c.get("bounds") or {}).get("x"), (c.get("bounds") or {}).get("w"), c.get("visible"))
                                                                 for c, _ in _walk(node.get("children"), []) if c.get("type") == "TextBlock"][:8]})
                if len(top) >= 5:
                    break
        out["top5_rows"] = top
        b.close()
    out["dpr"] = DPR
    c0 = out["steps"][0]["colors"]
    body = [s for s in out["steps"][1:] if s["step"] not in ("nudge1px", "after2s")]
    out["min_colors_after"] = min(s["colors"] for s in body)
    out["blank_suspect"] = out["min_colors_after"] < max(10, c0 * 0.1)
    nud = {s["step"]: s["colors"] for s in out["steps"] if s["step"] in ("nudge1px", "after2s")}
    last = body[-1]["colors"]
    # 빈 상태에서 입력 1회로 채워졌는가(색 수가 처음의 50% 이상 회복)
    out["refilled_by_input"] = bool(out["blank_suspect"]) and nud.get("nudge1px", 0) >= c0 * 0.5
    out["refilled_after_2s"] = bool(out["blank_suspect"]) and nud.get("after2s", 0) >= c0 * 0.5
    out["last_body_colors"] = last
    with open(BASE + ".json", "w", encoding="utf-8") as f:
        json.dump(out, f, ensure_ascii=False, indent=1)
    print("RESULT", IMPL, CASE, "dpr", DPR, "before", c0, "min_after", out["min_colors_after"], "blank_suspect", out["blank_suspect"],
          "nudge", nud, "refilled_by_input", out["refilled_by_input"], "refilled_after_2s", out["refilled_after_2s"], flush=True)


if __name__ == "__main__":
    main()
