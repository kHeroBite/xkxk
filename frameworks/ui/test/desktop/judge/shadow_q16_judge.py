#!/usr/bin/env python3
# Q2 #16 창 그림자 판정기 — 측정기가 남긴 활성/비활성 PNG 에서 바깥 16px 밝기 감소 합·바깥 1px 채널 차를 계산하고 criteria v3.2 A0~A4 로 PASS/FAIL 을 낸다
"""
판정 계층(D-LAYER)이다. 화면은 만지지 않고 PNG 만 읽는다. 캡처는 ../measure/shadow_q16.py 가 한다.
근거 1회성 스크립트: logs/otest/c2/w_fg/q2_16/profile16.py(바깥 16px 합)·analyze16c.py(바깥 1px 채널 차, 바깥 12px 어두운 화소 수).

사용:
  python3 -B shadow_q16_judge.py --dir <측정 폴더> [--label pd] [--measure-json <json>] [--out <judge.json>]
  python3 -B shadow_q16_judge.py --dir <폴더> --label np --origin 360,120 --frame 405,160,895,455 --measure-only   # 기준(메모장) 재계산
  프레임·원점은 <dir>/<label>_measure.json 에서 읽는다. 없으면 --frame L,T,R,B(보이는 경계, 화면 좌표)와 --origin X,Y(캡처 왼쪽 위)를 준다.
기준 인자(기본값 = criteria v3.2 고정 상수, 메모장 np_active.png·np_inactive.png 에서 잰 값; 결과를 본 뒤 바꾸지 않는다):
  --ref-active-left 440.1 --ref-active-bottom 481.4 --ref-inactive-left 239.3 --ref-inactive-bottom 324.2
  --ref-edge-active 50 --ref-edge-inactive 36 --ref-bg-active 132.0 --ref-bg-inactive 134.0
임계값 인자(v3.2): --diff-ratio 0.5 (A1) --abs-ratio 0.7 (A2) --edge-min 8 (A3) --bg-tol 3 (A0) --neg-min -3 --jump-max 3 (A4)
rc: 0=A0~A4 전부 PASS, 1=FAIL 있음, 2=A0 판정 불가(A1~A4 판정 안 함), 3=입력(PNG·프레임) 없음, 4=인자 오류. --measure-only 는 수치만 내고 rc 0.

판정 정의(v3.2)
 A0 조건 동등성: Pd 바탕(30px 바깥 평균 밝기)이 기준 바탕과 ±bg-tol 안(활성은 ref-bg-active, 비활성은 ref-bg-inactive).
 A1 활성−비활성 차이가 (기준 활성−기준 비활성)×diff-ratio 이상(왼쪽·아래 모두).
 A2 활성/비활성 합이 기준×abs-ratio 이상(왼쪽·아래 모두).
 A3 왼쪽 바깥 1px 채널 차(edge_diff)가 활성·비활성 모두 edge-min 이상.
 A4 16칸 프로파일(왼쪽·아래, 활성·비활성)에 neg-min 미만 칸 0개, 앞 칸보다 jump-max 이상 늘어난 칸 0개.
"""
import argparse
import json
import os
import sys

from PIL import Image


def lum(px):
    return sum(px[:3]) / 3.0


def profile(im, frame, origin):
    """바깥 16px 프로파일. frame=(L,T,R,B) 화면 좌표, origin=(OX,OY) 캡처 왼쪽 위. profile16.py·analyze16c.py 와 같은 정의."""
    L, T, R, B = frame
    x0, y0, x1, y1 = L - origin[0], T - origin[1], R - origin[0], B - origin[1]
    W, H = im.size
    need = [x0 - 30 >= 0, y1 + 28 < H, x0 - 16 >= 0, y1 + 15 < H, y0 + 40 < y1 - 40, x0 + 40 < x1 - 40, x1 <= W]
    if not all(need):
        raise ValueError("캡처 범위가 모자란다(왼쪽 30px·아래 28px 바탕 필요): size=%s frame_in_img=%s" % (im.size, (x0, y0, x1, y1)))
    ys = list(range(y0 + 40, y1 - 40, 4))
    xs = list(range(x0 + 40, x1 - 40, 4))

    def ml(x):
        return sum(sum(im.getpixel((x, y))[:3]) / 3.0 for y in ys) / len(ys)

    def mb(y):
        return sum(sum(im.getpixel((x, y))[:3]) / 3.0 for x in xs) / len(xs)

    bg = ml(x0 - 30)
    left = [round(bg - ml(x0 - dx), 1) for dx in range(1, 17)]
    bgb = mb(y1 + 28)
    bottom = [round(bgb - mb(y1 + dy), 1) for dy in range(0, 16)]
    ym = (y0 + y1) // 2
    xm = (x0 + x1) // 2
    far = im.getpixel((x0 - 30, ym))[:3]
    edge = im.getpixel((x0 - 1, ym))[:3]
    farb = im.getpixel((xm, y1 + 28))[:3]
    edgeb = im.getpixel((xm, y1))[:3]
    dark_l = sum(1 for dx in range(1, 13) for yy in range(y0 + 30, y1 - 10, 6) if sum(im.getpixel((x0 - dx, yy))[:3]) < sum(far) - 6)
    dark_b = sum(1 for dy in range(0, 12) for xx in range(x0 + 30, x1 - 30, 6) if sum(im.getpixel((xx, y1 + dy))[:3]) < sum(farb) - 6)
    return {
        "bg": round(bg, 1), "bg_bottom": round(bgb, 1),
        "left_drop_dx1_16": left, "left_sum": round(sum(left), 1),
        "bottom_drop_dy0_15": bottom, "bottom_sum": round(sum(bottom), 1),
        "left_edge1": list(edge), "left_far30": list(far), "left_edge_diff": max(abs(p - q) for p, q in zip(edge, far)),
        "bottom_edge0": list(edgeb), "bottom_far28": list(farb), "bottom_edge_diff": max(abs(p - q) for p, q in zip(edgeb, farb)),
        "left_dark12": dark_l, "bottom_dark12": dark_b,
    }


def a4_bad(prof_cells, neg_min, jump_max):
    neg = [i for i, v in enumerate(prof_cells) if v < neg_min]
    jump = [i for i in range(1, len(prof_cells)) if prof_cells[i] - prof_cells[i - 1] >= jump_max]
    return neg, jump


def main():
    ap = argparse.ArgumentParser(description="Q2 #16 그림자 판정(criteria v3.2 A0~A4)")
    ap.add_argument("--dir", required=True)
    ap.add_argument("--label", default="pd")
    ap.add_argument("--measure-json")
    ap.add_argument("--frame")
    ap.add_argument("--origin")
    ap.add_argument("--out")
    ap.add_argument("--measure-only", action="store_true")
    for k, v in (("ref-active-left", 440.1), ("ref-active-bottom", 481.4), ("ref-inactive-left", 239.3), ("ref-inactive-bottom", 324.2),
                 ("ref-edge-active", 50), ("ref-edge-inactive", 36), ("ref-bg-active", 132.0), ("ref-bg-inactive", 134.0),
                 ("diff-ratio", 0.5), ("abs-ratio", 0.7), ("edge-min", 8), ("bg-tol", 3.0), ("neg-min", -3.0), ("jump-max", 3.0)):
        ap.add_argument("--" + k, type=float, default=float(v))
    a = ap.parse_args()

    pa = os.path.join(a.dir, a.label + "_active.png")
    pi = os.path.join(a.dir, a.label + "_inactive.png")
    if not (os.path.exists(pa) and os.path.exists(pi)):
        print("입력 없음: %s / %s" % (pa, pi), file=sys.stderr)
        return 3
    frame = origin = None
    mj = a.measure_json or os.path.join(a.dir, a.label + "_measure.json")
    if a.frame:
        try:
            frame = [int(x) for x in a.frame.split(",")]
            origin = [int(x) for x in (a.origin or "").split(",")]
            assert len(frame) == 4 and len(origin) == 2
        except Exception:
            print("인자 오류: --frame L,T,R,B 와 --origin X,Y 를 함께 정수로 준다", file=sys.stderr)
            return 4
    elif os.path.exists(mj):
        m = json.load(open(mj, encoding="utf-8"))
        frame, origin = m.get("frame"), m.get("origin")
    if not frame or not origin:
        print("프레임·원점 없음: measure.json 또는 --frame/--origin 필요", file=sys.stderr)
        return 3
    try:
        P = {"active": profile(Image.open(pa).convert("RGB"), frame, origin),
             "inactive": profile(Image.open(pi).convert("RGB"), frame, origin)}
    except ValueError as e:
        print("입력 오류: %s" % e, file=sys.stderr)
        return 3
    res = {"label": a.label, "frame": frame, "origin": origin, "profile": P, "criteria": "v3.2", "checks": {}}

    if a.measure_only:
        res["verdict"] = "MEASURE_ONLY"
        out = a.out or os.path.join(a.dir, a.label + "_judge.json")
        json.dump(res, open(out, "w", encoding="utf-8"), ensure_ascii=False, indent=1)
        for st in ("active", "inactive"):
            print(a.label, st, "bg", P[st]["bg"], "left_sum", P[st]["left_sum"], "bottom_sum", P[st]["bottom_sum"], "edge_diff", P[st]["left_edge_diff"])
        return 0

    ck = res["checks"]
    ba = abs(P["active"]["bg"] - a.ref_bg_active)
    bi = abs(P["inactive"]["bg"] - a.ref_bg_inactive)
    ck["A0"] = {"pass": ba <= a.bg_tol and bi <= a.bg_tol, "bg_diff_active": round(ba, 1), "bg_diff_inactive": round(bi, 1), "tol": a.bg_tol}
    if not ck["A0"]["pass"]:
        res["verdict"] = "UNDECIDABLE"
        res["note"] = "A0 조건 동등성 불충족 — A1~A4 판정 안 함(FAIL 아님)"
    else:
        need_l = (a.ref_active_left - a.ref_inactive_left) * a.diff_ratio
        need_b = (a.ref_active_bottom - a.ref_inactive_bottom) * a.diff_ratio
        dl = P["active"]["left_sum"] - P["inactive"]["left_sum"]
        db = P["active"]["bottom_sum"] - P["inactive"]["bottom_sum"]
        ck["A1"] = {"pass": dl >= need_l and db >= need_b, "diff_left": round(dl, 1), "need_left": round(need_l, 1),
                    "diff_bottom": round(db, 1), "need_bottom": round(need_b, 1)}
        n = {"al": a.ref_active_left * a.abs_ratio, "ab": a.ref_active_bottom * a.abs_ratio,
             "il": a.ref_inactive_left * a.abs_ratio, "ib": a.ref_inactive_bottom * a.abs_ratio}
        ck["A2"] = {"pass": P["active"]["left_sum"] >= n["al"] and P["active"]["bottom_sum"] >= n["ab"]
                    and P["inactive"]["left_sum"] >= n["il"] and P["inactive"]["bottom_sum"] >= n["ib"],
                    "need": {k: round(v, 1) for k, v in n.items()},
                    "got": {"al": P["active"]["left_sum"], "ab": P["active"]["bottom_sum"],
                            "il": P["inactive"]["left_sum"], "ib": P["inactive"]["bottom_sum"]}}
        ck["A3"] = {"pass": P["active"]["left_edge_diff"] >= a.edge_min and P["inactive"]["left_edge_diff"] >= a.edge_min,
                    "edge_active": P["active"]["left_edge_diff"], "edge_inactive": P["inactive"]["left_edge_diff"], "min": a.edge_min}
        bad = {}
        for st in ("active", "inactive"):
            for side, key in (("left", "left_drop_dx1_16"), ("bottom", "bottom_drop_dy0_15")):
                neg, jump = a4_bad(P[st][key], a.neg_min, a.jump_max)
                if neg or jump:
                    bad["%s_%s" % (st, side)] = {"neg_cells": neg, "jump_cells": jump}
        ck["A4"] = {"pass": not bad, "bad": bad}
        res["verdict"] = "PASS" if all(ck[k]["pass"] for k in ("A0", "A1", "A2", "A3", "A4")) else "FAIL"
        res["reference_edge"] = {"active": a.ref_edge_active, "inactive": a.ref_edge_inactive}

    out = a.out or os.path.join(a.dir, a.label + "_judge.json")
    json.dump(res, open(out, "w", encoding="utf-8"), ensure_ascii=False, indent=1)
    for k, v in ck.items():
        print(k, "PASS" if v["pass"] else "FAIL", {x: y for x, y in v.items() if x != "pass"})
    print("verdict", res["verdict"])
    return {"PASS": 0, "FAIL": 1, "UNDECIDABLE": 2}[res["verdict"]]


if __name__ == "__main__":
    sys.exit(main())
