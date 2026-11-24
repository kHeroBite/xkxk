#!/usr/bin/env python3
# 웹 PC 폭 MDI 끌기 경계 측정 — headless Chrome 안의 Playwright 실마우스로 네 방향 한계·다시 잡기·복구를 재서 JSON 에 남긴다(사용자 PC 입력과 무관)
"""
사용: python3 -B retest_mdi_drag_web.py --bundle <wwwroot> --out <결과.json> --shots <폴더> [--port 5791] [--snap off|on|keep]
--snap 은 데스크톱 retest_mdi_drag.py 와 같다(기본 off).
데스크톱과 같은 MagnetMdiLayoutManager 를 쓰므로 같은 공식으로 본다. 브라우저에서는 문서 영역 좌표가 페이지 좌표다.
종료 코드: 0=측정 완료·검사 통과, 1=검사 실패. logs/ui28/ 에는 쓰지 않는다(otest 전용).
"""
import argparse
import json
import math
import os
import sys
import time

from playwright.sync_api import sync_playwright

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from ui28_web import Web  # noqa: E402

MIN_GRAB = 50  # MdiDragLimit.MinGrab 와 같은 값
SETTLE_S = 0.8


def ratio_keep(size, pct):  # MdiDragLimit.RatioKeep 과 같은 식 ceil(크기×비율−1e-9), pct 0~90, 0 이면 0(데스크톱 retest_mdi_drag.py 와 동일)
    p = min(max(pct or 0, 0), 90) / 100.0
    return math.ceil(size * p - 1e-9) if p > 0 else 0


def state(w, doc):
    s = w.res("placement.state")
    return s, next(d for d in s["docs"] if d["id"] == doc)


def press_point(w, doc):
    s, d = state(w, doc)
    a, b, g = s["area"], d["bounds"], d["grab"]
    foldable = g.get("foldable", 0)
    # 왼쪽으로 밀려 기능 버튼 줄이 접힌 상태면 잡을 구간은 RightInset-Foldable 앞까지(MdiDragLimit)
    right_inset = g["rightInset"] - foldable if (foldable > 0 and b["x"] < 0) else g["rightInset"]
    lo, hi = max(b["x"] + g["left"], 0), min(b["x"] + b["w"] - right_inset, a["dipW"])
    return None if hi - lo < 4 else (a["x"] + (lo + hi) / 2, a["y"] + b["y"] + g["titleBottom"] / 2)


def drag(w, doc, dx, dy):
    at = press_point(w, doc)
    if at is None:
        return False
    w.drag(at[0], at[1], at[0] + dx, at[1] + dy)
    time.sleep(SETTLE_S)
    return True


def measure(w, res, checks):
    def check(name, ok, detail=""):
        checks.append({"name": name, "pass": bool(ok), "detail": str(detail)})
        print("  %s %s %s" % ("PASS" if ok else "FAIL", name, detail), flush=True)

    w.res("docs.open", kind="grid")
    time.sleep(1.5)
    doc = w.res("docs.list")[0]["id"]
    s, d = state(w, doc)
    a, g = s["area"], d["grab"]
    res.update(area=a, grab=g, start=d["bounds"], selftest_failed=[c["name"] for c in w.res("placement.selftest")["cases"] if not c["pass"]])
    check("selftest", not res["selftest_failed"], res["selftest_failed"])
    rows = {}
    for name, dx, dy, back in (("left", -3000, 0, (150, 0)), ("right", 3000, 0, (-150, 0)), ("up", 0, -3000, (0, 80)), ("down", 0, 3000, (0, -80))):
        w.res("placement.markPlaced", doc=doc, x=100, y=100, w=d["bounds"]["w"], h=d["bounds"]["h"])
        time.sleep(SETTLE_S)
        drag(w, doc, dx, dy)
        limit = state(w, doc)[1]["bounds"]
        w.shot("web_limit_" + name)
        drag(w, doc, *back)
        rows[name] = {"limit": limit, "after_regrab": state(w, doc)[1]["bounds"]}
        check("regrab_" + name, rows[name]["after_regrab"] != limit, rows[name])
    res["directions"] = rows
    width = d["bounds"]["w"]
    foldable = g.get("foldable", 0)  # 기능 버튼 줄 폭(0 이면 줄 없음)
    pct = w.res("placement.state").get("minVisiblePercent", 0)  # 잔존 비율 %(키 없는 옛 판본은 0 = 잡을 곳 규칙만)
    keep_left = min(width, max(ratio_keep(width, pct), (g["rightInset"] - foldable) + MIN_GRAB))
    keep_right = min(width, max(ratio_keep(width, pct), g["left"] + MIN_GRAB))
    keep_down = max(g["titleBottom"], min(d["bounds"]["h"], ratio_keep(d["bounds"]["h"], pct)))  # 위는 0 정지(비율 무관)
    res.update(min_visible_percent=pct, expected_keep={"left": keep_left, "right": keep_right, "down": keep_down})
    res["foldable"], res["folded"] = foldable, foldable > 0 and rows["left"]["limit"]["x"] < 0
    check("left_limit", abs(rows["left"]["limit"]["x"] - (keep_left - width)) <= 1, rows["left"]["limit"])
    check("right_limit", abs(rows["right"]["limit"]["x"] - (a["dipW"] - keep_right)) <= 1, rows["right"]["limit"])
    check("up_blocked", rows["up"]["limit"]["y"] == 0, rows["up"]["limit"])
    check("down_title_visible", abs(rows["down"]["limit"]["y"] - (a["dipH"] - keep_down)) <= 1, rows["down"]["limit"])
    # 복구: 밀어 둔 창은 조금 줄여도 그대로, 잡을 곳이 없어지면 기본 위치
    w.res("placement.markPlaced", doc=doc, x=100, y=100, w=width, h=d["bounds"]["h"])
    time.sleep(SETTLE_S)
    drag(w, doc, 3000, 0)
    pushed = state(w, doc)[1]["bounds"]
    moved0 = w.res("placement.state")["mdiMoved"]
    w.page.set_viewport_size({"width": w.viewport[0] - 20, "height": w.viewport[1]})
    time.sleep(2.0)
    small = state(w, doc)[1]["bounds"]
    w.page.set_viewport_size({"width": 1100, "height": w.viewport[1]})
    time.sleep(2.0)
    s2, d2 = state(w, doc)
    res["recovery"] = {"pushed": pushed, "shrink_20": small, "shrink_300": d2["bounds"], "moved": s2["mdiMoved"] - moved0, "area": s2["area"]}
    check("recover_not_on_small_shrink", small == pushed, res["recovery"])
    check("recover_when_ungrabbable", s2["mdiMoved"] > moved0 and d2["bounds"]["x"] < pushed["x"], res["recovery"])
    res["console_errors"] = w.errors
    check("no_console_errors", not w.errors, w.errors[:3])


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--bundle", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--shots", required=True)
    ap.add_argument("--port", type=int, default=5791)
    ap.add_argument("--snap", choices=("keep", "on", "off"), default="off",
                    help="Snap 레이아웃 켬·끔(원격 snapflyout.options snapEnabled). 기본 off(가장자리 칸 배치가 한계 검사를 가리지 않게), keep 은 건드리지 않음")
    a = ap.parse_args()
    if "logs/ui28" in os.path.abspath(a.out).replace("\\", "/"):
        print("logs/ui28/ 는 otest 결과 전용이다")
        return 1
    res, checks = {"bundle": a.bundle}, []
    with sync_playwright() as p:
        w = Web(a.bundle, a.port, a.shots)
        try:
            w.open(p)
            if a.snap != "keep":  # 임시 브라우저 프로필이라 원복할 저장본이 없다
                res["snap_option"] = w.res("snapflyout.options", snapEnabled="true" if a.snap == "on" else "false")
            res["snap"] = a.snap
            measure(w, res, checks)
        finally:
            w.close()
    res["checks"] = checks
    with open(a.out, "w", encoding="utf-8") as f:
        json.dump(res, f, ensure_ascii=False, indent=1)
    failed = [c["name"] for c in checks if not c["pass"]]
    print("검사 %d건, 실패 %d건 %s" % (len(checks), len(failed), failed))
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
