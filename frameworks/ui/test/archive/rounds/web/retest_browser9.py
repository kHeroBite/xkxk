# 그리드 시드 지연 생성(lazy)과 ?grid-seed=eager 의 화면 동일성을 비교하는 스크립트 — 첫 화면·작업명 정렬 뒤·필터 '검토' 뒤 캡처와 행 수 문구를 대조한다
# 사용: PD_URL=http://127.0.0.1:5185/ python3 tools/retest_browser9.py
import json
import os
import sys
import time

from playwright.sync_api import sync_playwright
from PIL import Image, ImageChops

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import retest_browser2 as t  # noqa: E402

OUT = t.OUT
res = []


def rec(item, expect, actual, verdict):
    res.append({"item": item, "expect": expect, "actual": actual, "verdict": verdict})
    print("%-6s %s | %s" % (verdict, item, str(actual)[:420]), flush=True)


def run(b, mode):
    q = "?fresh" + ("&grid-seed=lazy" if mode == "lazy" else "")
    ctx, page, r = t.boot(b, q)
    did = [x["id"] for x in r.call("docs.open", kind="grid", count=1)][0]
    time.sleep(2.5)
    shots = {}
    info = {}

    def shot(name):
        path = os.path.join(OUT, "seed_%s_%s.png" % (mode, name))
        page.screenshot(path=path)
        shots[name] = path

    count0 = (r.call("ui.find", id="grid.lblCount", doc=did) or {}).get("text")
    shot("1_first")
    # 정렬: '작업명' 머리글 클릭
    hdr = next((n for n in t_nodes(r, "작업명", did)), None)
    if hdr:
        bb = hdr["bounds"]
        page.mouse.click(bb["x"] + bb["w"] / 2, bb["y"] + bb["h"] / 2)
        time.sleep(1.5)
    shot("2_sorted")
    count1 = (r.call("ui.find", id="grid.lblCount", doc=did) or {}).get("text")
    # 필터 '검토'
    r.call("ui.setText", id="grid.txtFilter", doc=did, text="검토")
    time.sleep(1.5)
    shot("3_filtered")
    count2 = (r.call("ui.find", id="grid.lblCount", doc=did) or {}).get("text")
    perf = r.call("perf.get") or {}
    info = {"counts": [count0, count1, count2], "grid_seed_mode": perf.get("grid_seed_mode") or (perf.get("text") or {}).get("grid_seed_mode"), "gc": perf.get("gc"), "sort_header_found": bool(hdr)}
    ctx.close()
    return shots, info


def t_nodes(r, text, did):
    tr = r.call("tree.get", doc=did, named=False, depth=50) or {}
    out = []

    def walk(n):
        if isinstance(n, dict):
            v = n.get("text") or n.get("content") or n.get("header")
            if isinstance(v, str) and v.strip() == text and n.get("bounds") and n.get("visible", True):
                out.append({"bounds": n["bounds"]})
            for c in n.get("children") or []:
                walk(c)
        elif isinstance(n, list):
            for c in n:
                walk(c)

    walk(tr.get("tree", tr))
    return out


def grid_hash(b, mode):
    """?open=grid:1&fresh&perf-out=x&cmd=grid-hash (lazy 는 &grid-seed=lazy) 로 열어 콘솔 PERF_JSON 에서 해시 필드를 읽는다."""
    q = "?open=grid:1&fresh&perf-out=x&cmd=grid-hash" + ("&grid-seed=lazy" if mode == "lazy" else "")
    ctx = b.new_context(viewport={"width": 1280, "height": 800})
    page = ctx.new_page()
    lines = []
    page.on("console", lambda m: lines.append(m.text) if m.text.startswith("PERF_JSON:") else None)
    page.goto(t.URL + q)
    t0 = time.time()
    while time.time() - t0 < 90 and not lines:
        page.wait_for_timeout(2000)
    ctx.close()
    if not lines:
        return None
    d = json.loads(lines[0][len("PERF_JSON:"):])
    pick = lambda k: d.get(k) if k in d else (d.get("text") or {}).get(k)
    return {k: pick(k) for k in ("grid_hash", "grid_hash_equal", "grid_hash_state", "grid_seed_mode")} | {"ops": {o["name"]: (o.get("frame_p50_ms"), o.get("count")) for o in (d.get("operations") or []) if str(o.get("name", "")).startswith(("grid.ctor.seed", "grid.seed.materialize", "doc.open.grid"))}, "gc": d.get("gc")}


def main():
    with sync_playwright() as p:
        b = p.chromium.launch(headless=True, args=t.ARGS)
        hh = {m: grid_hash(b, m) for m in ("eager", "lazy")}
        he, hl = hh["eager"], hh["lazy"]
        ok_hash = bool(he and hl and he["grid_hash"] and he["grid_hash"] == hl["grid_hash"] and he["grid_hash_equal"] in (1, True) and hl["grid_hash_equal"] in (1, True))
        rec("Q grid-hash 동등성(두 모드 해시 동일·grid_hash_equal=1)", "eager==lazy, equal=1", hh, "PASS" if ok_hash else ("미검증(필드 없음: 판 미포함)" if not (he and he.get("grid_hash")) else "FAIL"))
        rec("Q grid_hash_state", "lazy=materialized-by-hash, eager=already-materialized", {"eager": (he or {}).get("grid_hash_state"), "lazy": (hl or {}).get("grid_hash_state")}, "PASS" if (he or {}).get("grid_hash_state") == "already-materialized" and (hl or {}).get("grid_hash_state") == "materialized-by-hash" else "미검증/FAIL")
        s_lazy, i_lazy = run(b, "lazy")
        s_eager, i_eager = run(b, "eager")
        rec("seed 모드 표시(perf text grid_seed_mode)", "lazy / eager", {"lazy": i_lazy["grid_seed_mode"], "eager": i_eager["grid_seed_mode"]}, "PASS" if i_lazy["grid_seed_mode"] != i_eager["grid_seed_mode"] and i_lazy["grid_seed_mode"] and i_eager["grid_seed_mode"] else "미검증(필드 없음 또는 동일)")
        rec("행 수 문구 동일(첫·정렬 뒤·필터 뒤)", "lazy == eager", {"lazy": i_lazy["counts"], "eager": i_eager["counts"]}, "PASS" if i_lazy["counts"] == i_eager["counts"] and all(i_lazy["counts"]) else "FAIL")
        for name in ("1_first", "2_sorted", "3_filtered"):
            a, c = Image.open(s_lazy[name]).convert("L"), Image.open(s_eager[name]).convert("L")
            diff = ImageChops.difference(a, c)
            frac = sum(1 for v in diff.getdata() if v > 24) / (a.size[0] * a.size[1])
            rec("화면 동일성 %s" % name, "차이 비율 0.5%% 미만(FPS·시각 문구 허용)", {"diff_frac": round(frac, 5)}, "PASS" if frac < 0.005 else "FAIL(차이 있음 — 캡처 판독 필요)")
        rec("정렬 머리글 클릭 가능", "작업명 머리글 노드 발견", {"lazy": i_lazy["sort_header_found"], "eager": i_eager["sort_header_found"]}, "PASS" if i_lazy["sort_header_found"] and i_eager["sort_header_found"] else "FAIL")
        with open(os.path.join(OUT, "retest_web9.json"), "w", encoding="utf-8") as f:
            json.dump(res, f, ensure_ascii=False, indent=2)
        b.close()


if __name__ == "__main__":
    main()
