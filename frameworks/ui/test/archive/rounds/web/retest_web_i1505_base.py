# i1505 웹 기준값(Fold5 Chrome): D-W3 c06 마우스 hover 툴팁·D-W1 c15 마우스 3행 끌기·c09 터치 끌기 스크롤양을 i1705rc 와 같은 조작으로 재는 대조 스크립트
"""
사용: python3 -E -u test/web/measure/retest_web_i1505_base.py --url-part 127.0.0.1:5186 --tag F|U --out <폴더>
"""
import argparse
import json
import math
import os
import sys
import time

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "common"))  # D-TESTDIR: test/common/_paths.py
import _paths  # noqa: E402
_paths.add()
from cdp_bridge import Bridge  # noqa: E402


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--url-part", default="127.0.0.1:5186")
    ap.add_argument("--tag", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--H", type=float, default=36.8)
    a = ap.parse_args()
    b = Bridge(a.url_part)
    sl = time.sleep
    rep = {"tag": a.tag, "url": b.url}
    for _ in range(30):
        if b.eval('typeof pdAutomation !== "undefined"') is True:
            break
        sl(1)
    ds = b.call("docs.list") or []
    gs = [d.get("id") for d in ds if d.get("kind") == "grid"]
    if not gs:
        b.call("docs.open", kind="grid")
        sl(2.5)
        gs = [d.get("id") for d in (b.call("docs.list") or []) if d.get("kind") == "grid"]
    G = gs[0]
    b.call("docs.activate", doc=G)
    sl(1)
    gb = (b.call("ui.find", id="grid.dgMain", doc=G) or {}).get("bounds")
    GV = lambda: b.call("grid.visible", id="grid.dgMain", doc=G) or {}  # noqa: E731
    g = GV()
    rh = g.get("rowHeight") or 28
    colx = gb["x"] + (230 if gb["w"] > 300 else gb["w"] * 0.55)

    # D-GV1: first 는 화면 밖 포커스 행 컨테이너로 오염되므로 좌표·기준 행은 offsetY 로 계산한다
    # 실제 행 간격 P(RowHeight 28 과 다름, 실측 약 31.1): 선택 행을 화면 안에 둔 채 30행으로 스크롤해 (offsetY − topClip) ÷ first
    b.call("grid.select", id="grid.dgMain", doc=G, index=30, scroll=True)
    sl(0.6)
    gp = GV()
    _o, _f, _ff = gp.get("offsetY") or 0, gp.get("first"), gp.get("firstFull")
    # first 오염(D-GV1: first < firstFull−1)이면 firstFull 로 근사한다
    P = (_o - (gp.get("topClip") or 0)) / _f if (_f and _ff is not None and _f >= _ff - 1) else (_o / (_ff - 0.5) if _ff else rh)
    # 검산: 포커스 행 잔류로 first·firstFull 이 함께 오염되면 P 가 크게 틀린다(i1720rc 웹 접힘 실측 120). RowHeight 의 0.9~1.6배 밖이면 RowHeight×1.11(실측 31.1/28)
    if not (0.9 * rh <= P <= 1.6 * rh):
        P = rh * 31.1 / 28
    rep["pitch"] = {"P": round(P, 2), "probe": {k: gp.get(k) for k in ("first", "firstFull", "offsetY", "topClip")}}
    b.call("grid.select", id="grid.dgMain", doc=G, index=0, scroll=True)
    sl(0.6)
    g = GV()

    def top(v):
        """완전히 보이는 첫 행 = ceil(offsetY / 실제 행 간격)."""
        return int(math.ceil(((v.get("offsetY") or 0) - 0.5) / P))

    def rowy(i):
        v = GV()
        return gb["y"] + a.H + i * P - (v.get("offsetY") or 0) + P / 2.0

    rep.update(gb=gb, metrics=b.eval("({w:innerWidth,h:innerHeight,dpr:devicePixelRatio})"), doc=G)
    # c06 hover
    b.mouse("mouseMoved", 5, 5)
    sl(0.5)
    y = rowy(top(g) + 4)
    b.mouse("mouseMoved", colx - 20, y - 5)
    b.mouse("mouseMoved", colx, y)
    sl(1.5)
    rep["c06_shot"] = b.shot(os.path.join(a.out, "web_i1505_%s_w3_c06_shot1.png" % a.tag))
    b.mouse("mouseMoved", 5, 5)
    sl(0.8)
    # c15 마우스 3행 끌기
    g = GV()
    R = top(g) + 3
    ry = rowy(R)
    b.mouse("mouseMoved", colx, ry)
    b.mouse("mousePressed", colx, ry, "left", 1, 1)
    for i in range(1, 7):
        b.sleep_ms(30)
        b.mouse("mouseMoved", colx, ry + rh * 3 * i / 6, "left", 0, 1)
    b.mouse("mouseReleased", colx, ry + rh * 3, "left", 1, 0)
    sl(0.5)
    v = GV()
    rep["c15"] = {"R": R, "selectedIndex": v.get("selectedIndex"), "rel": (v.get("selectedIndex") or 0) - R}
    # c09 터치 세로 끌기
    g = GV()
    y0, y1 = gb["y"] + gb["h"] * 0.8, gb["y"] + gb["h"] * 0.2
    b.touch("touchStart", [{"x": colx, "y": y0, "id": 1}])
    for i in range(1, 13):
        b.sleep_ms(16)
        b.touch("touchMove", [{"x": colx, "y": y0 + (y1 - y0) * i / 12, "id": 1}])
    b.sleep_ms(60)
    b.touch("touchEnd", [])
    sl(0.8)
    v = GV()
    fr = round((y0 - y1) / rh, 2)
    # 스크롤 비율은 offsetY 변화 ÷ 손가락 이동(CSS px)(D-GV1 — first 는 참고로만 남김)
    rep["c09"] = {"sel_before": g.get("selectedIndex"), "sel_after": v.get("selectedIndex"), "offsetY_before": g.get("offsetY"), "offsetY_after": v.get("offsetY"),
                  "finger_px": round(y0 - y1, 1), "rowHeight": rh, "finger_rows": fr,
                  "offset_ratio": round(((v.get("offsetY") or 0) - (g.get("offsetY") or 0)) / (y0 - y1), 3),
                  "first_before_ref": g.get("first"), "first_after_ref": v.get("first")}
    json.dump(rep, open(os.path.join(a.out, "web_i1505_base_%s.json" % a.tag), "w", encoding="utf-8"), ensure_ascii=False, indent=1)
    print(json.dumps(rep, ensure_ascii=False))
    b.close()


if __name__ == "__main__":
    main()
