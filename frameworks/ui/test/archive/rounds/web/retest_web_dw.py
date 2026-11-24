# i1705rc 터치 결함 D-W1~3 Fold5 Chrome 웹 시험(CDP 터치·마우스 + adb OS 실터치, 접힘·펼침)으로 케이스별 기대·실제·조작 좌표를 web_ 접두로 남기는 스크립트(dw_test_procedure.md 웹 축, REQ-25 짝 비교용)
"""
사용: python3 -E -u tools/retest_web_dw.py --tag U|F [--url-part 127.0.0.1:5185] [--out logs/otest_m1/i1705rc/dw] [--only w1,w2,w3]
전제: 웹 서빙·adb reverse·adb forward tcp:9333 이 되어 있고 Fold5 Chrome 에 PD 탭이 열려 있다. 폼팩터(wm size)는 호출하는 쪽이 맞춘다.
좌표: CDP 는 CSS px(= 논리px). adb 는 화면 px = oy + CSS × dpr(oy 는 adb 탭 훑기로 보정).
결과: <out>/web_{webU|webF}_{w1|w2|w3}_{케이스}_{before|after|shot*}.{json|png}, web_dw_{tag}_result.json
"""
import argparse
import json
import math
import os
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import retest_android as ra  # noqa: E402
from cdp_bridge import Bridge  # noqa: E402

ROOT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
TOP = ["shell.mnuFile", "shell.mnuWindow", "shell.mnuView", "shell.mnuWinMenu", "shell.mnuSys"]
SR = "R3CW70202FP"


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--tag", required=True, choices=["U", "F"])
    ap.add_argument("--url-part", default="127.0.0.1:5185")
    ap.add_argument("--out", default=os.path.join(ROOT, "logs", "otest_m1", "i1705rc", "dw"))
    ap.add_argument("--only", default="w1,w2,w3")
    a = ap.parse_args()
    O = a.out
    os.makedirs(O, exist_ok=True)
    pf = "web" + a.tag
    b = Bridge(a.url_part)
    rep = {"platform": pf, "url": b.url, "cases": []}
    cur = {"G": "Grid-1"}
    sl = time.sleep

    def jd(name, obj):
        json.dump(obj, open(os.path.join(O, name + ".json"), "w", encoding="utf-8"), ensure_ascii=False, indent=1)

    def shot(name):
        return b.shot(os.path.join(O, name + ".png"))

    def case(w, cid, expect, actual, action, ok, files=None, note=None):
        c = {"id": "%s_%s" % (w, cid), "platform": pf, "expect": expect, "actual": actual, "action": action,
             "verdict": "PASS" if ok is True else ("FAIL" if ok is False else str(ok)), "files": [f for f in (files or []) if f], "note": note}
        rep["cases"].append(c)
        jd("web_dw_%s_result" % a.tag, rep)
        print("CASE", pf, c["id"], c["verdict"], json.dumps(actual, ensure_ascii=False)[:220], flush=True)

    F = lambda i, **k: b.call("ui.find", id=i, **k) or {}  # noqa: E731
    GV = lambda: b.call("grid.visible", id="grid.dgMain", doc=cur["G"]) or {}  # noqa: E731

    def pick_grid():
        ds = b.call("docs.list") or []
        gs = [d.get("id") for d in ds if d.get("kind") == "grid"]
        if not gs:
            b.call("docs.open", kind="grid")
            sl(2.5)
            ds = b.call("docs.list") or []
            gs = [d.get("id") for d in ds if d.get("kind") == "grid"]
        cur["G"] = gs[0]
        b.call("docs.activate", doc=cur["G"])
        sl(1.0)
        return cur["G"]

    def T(typ, pts):
        return b.touch(typ, pts)

    def touch_path(x, y, path, hold=60):
        T("touchStart", [{"x": x, "y": y, "id": 1}])
        for dx, dy in path:
            b.sleep_ms(16)
            T("touchMove", [{"x": x + dx, "y": y + dy, "id": 1}])
        b.sleep_ms(hold)
        T("touchEnd", [])
        sl(0.4)

    try:
        rep["metrics"] = b.eval("({w:innerWidth,h:innerHeight,dpr:devicePixelRatio,ua:navigator.userAgent,sh:screen.height,touch:navigator.maxTouchPoints})")
        rep["settings_before"] = b.call("settings.get")
        rep["form_factor"] = b.call("ui.mode")
        b.call("settings.set", theme="System", baseFontSize=14)
        b.call("ui.mode", set="auto")
        pick_grid()
        rep["grid_doc"] = cur["G"]
        dpr = rep["metrics"]["dpr"]
        gb = F("grid.dgMain", doc=cur["G"]).get("bounds")
        g0 = GV()
        rh = g0.get("rowHeight") or 28
        colx = gb["x"] + (230 if gb["w"] > 300 else gb["w"] * 0.55)
        rep["grid"] = {"gb": gb, "rowHeight": rh}

        # --- 머리글 높이 H 보정(CDP 터치 탭 훑기, 2px) ---
        # D-GV1: grid.visible first 는 화면 밖에 남은 포커스 행 컨테이너로 오염되므로 좌표·기준 행은 offsetY(세로 스크롤바 값)로 계산한다
        # 실제 행 간격 P 는 RowHeight(28)와 다르다(i1705rc 실측 약 31.1). 선택 행을 화면 안에 둔 채(오염 없음) 30행으로 스크롤해 (offsetY − topClip) ÷ first 로 잰다
        b.call("grid.select", id="grid.dgMain", doc=cur["G"], index=30, scroll=True)
        sl(0.6)
        gp = GV()
        _o, _f, _ff = gp.get("offsetY") or 0, gp.get("first"), gp.get("firstFull")
        # first 오염(D-GV1: first < firstFull−1)이면 firstFull 로 근사한다
        P = (_o - (gp.get("topClip") or 0)) / _f if (_f and _ff is not None and _f >= _ff - 1) else (_o / (_ff - 0.5) if _ff else rh)
        rep["grid"]["pitch"] = {"P": round(P, 2), "probe": {k: gp.get(k) for k in ("first", "firstFull", "offsetY", "topClip")}}
        b.call("grid.select", id="grid.dgMain", doc=cur["G"], index=0, scroll=True)
        sl(0.6)

        def top(v):
            """완전히 보이는 첫 행 = ceil(offsetY / 실제 행 간격)."""
            return int(math.ceil(((v.get("offsetY") or 0) - 0.5) / P))

        def rowy(i, H):
            g = GV()
            return gb["y"] + H + i * P - (g.get("offsetY") or 0) + P / 2.0

        pts = []
        for yy in range(int(gb["y"] + 40), int(gb["y"] + 120), 2):
            touch_path(colx, yy, [])
            v = GV()
            pts.append((yy, v.get("selectedIndex"), v.get("offsetY") or 0))
        # 검산: 훑기에서 찾은 행 경계 간격으로 P 를 따로 구한다. 측정값이 15% 넘게 다르면(포커스 행 잔류로 first·firstFull 이 함께 오염된 경우 — i1720rc 접힘 실측) 훑기 값을 쓴다
        bnd = [(y0 + y1) / 2.0 for (y0, r0, o0), (y1, r1, o1) in zip(pts, pts[1:]) if r0 is not None and r1 == r0 + 1 and o0 == o1]
        P_sweep = (bnd[-1] - bnd[0]) / (len(bnd) - 1) if len(bnd) >= 2 else None
        rep["grid"]["pitch"]["P_sweep"] = round(P_sweep, 2) if P_sweep else None
        if P_sweep and abs(P - P_sweep) / P_sweep > 0.15:
            rep["grid"]["pitch"]["rejected_probe_P"] = round(P, 2)
            P = P_sweep
            rep["grid"]["pitch"]["P"] = round(P, 2)
        print("pitch", rep["grid"]["pitch"], flush=True)
        Hs = [((y0 + y1) / 2.0 - gb["y"] - (r1 * P - o1)) for (y0, r0, o0), (y1, r1, o1) in zip(pts, pts[1:]) if r0 is not None and r1 == r0 + 1 and o0 == o1]
        H = sum(Hs) / len(Hs) if Hs else 33.0
        rep["calib_H"] = {"sweep": pts, "H_candidates": Hs, "H": H}
        k = top(GV()) + 3
        touch_path(colx, rowy(k, H), [])
        rep["calib_H"]["verify"] = {"want": k, "got": GV().get("selectedIndex")}
        print("H", H, rep["calib_H"]["verify"], flush=True)

        # --- adb 세로 원점 oy 보정: 화면 px = oy + CSS*dpr ---
        def adb(*x):
            return ra.adb(SR, *[str(v) for v in x])

        rc, ws = ra.adb(SR, "shell", "wm", "size")
        hscr = int(ws.strip().splitlines()[-1].split(":")[-1].strip().split("x")[1])
        guess = hscr - 34 - rep["metrics"]["h"] * dpr
        k = top(GV()) + 4
        target = rowy(k, H)
        tries = []
        oy = None
        for d in range(-90, 91, 12):
            yy = int(guess + d + target * dpr)
            adb("shell", "input", "tap", int(colx * dpr), yy)
            sl(0.5)
            got = GV().get("selectedIndex")
            tries.append((d, yy, got))
            if got == k and oy is None:
                oy = guess + d
        ok_ds = [t[0] for t in tries if t[2] == k]
        if ok_ds:
            oy = guess + (min(ok_ds) + max(ok_ds)) / 2.0
        rep["calib_oy"] = {"screen_h": hscr, "guess": guess, "tries": tries, "oy": oy}
        print("oy", oy, flush=True)

        def sy(css_y):
            return int(round(oy + css_y * dpr))

        def sx(css_x):
            return int(round(css_x * dpr))

        # ================= D-W1 =================
        if "w1" in a.only:
            paths = {"c01": ([], "R"), "c02": ([(1.5, 0), (3, 0)], "R"), "c03": ([(2.5, 0), (5, 0)], "R"), "c04": ([(0, 2.5), (0, 5)], "R"),
                     "c05": ([(3, 0), (6, 0)], "S"), "c06": ([(0, 3), (0, 6)], "S"), "c07": ([(4, 4), (8, 8)], "S"),
                     "c08": ([(5, 0), (10, 0), (5, 0), (0, 0)], "S")}
            for cid, (path, want) in paths.items():
                g = GV()
                Srow, R = top(g) + 2, top(g) + 5
                touch_path(colx, rowy(Srow, H), [])
                before = GV()
                rx, ry = colx, rowy(R, H)
                touch_path(rx, ry, path)
                after = GV()
                jd("web_%s_w1_%s_before" % (pf, cid), before)
                jd("web_%s_w1_%s_after" % (pf, cid), after)
                exp = R if want == "R" else before.get("selectedIndex")
                case("w1", cid, {"selectedIndex": want, "value": exp}, {"S": before.get("selectedIndex"), "R": R, "selectedIndex": after.get("selectedIndex")},
                     {"cdp": "touchStart(%.1f,%.1f) → %s → touchEnd" % (rx, ry, path), "css": [round(rx, 1), round(ry, 1)]}, after.get("selectedIndex") == exp,
                     ["web_%s_w1_%s_before.json" % (pf, cid), "web_%s_w1_%s_after.json" % (pf, cid)])
            # c09 세로 끌기
            g = GV()
            touch_path(colx, rowy(top(g) + 2, H), [])
            before = GV()
            y0, y1 = gb["y"] + gb["h"] * 0.8, gb["y"] + gb["h"] * 0.2
            T("touchStart", [{"x": colx, "y": y0, "id": 1}])
            for i in range(1, 13):
                b.sleep_ms(16)
                T("touchMove", [{"x": colx, "y": y0 + (y1 - y0) * i / 12, "id": 1}])
            b.sleep_ms(60)
            T("touchEnd", [])
            sl(0.8)
            after = GV()
            jd("web_%s_w1_c09_before" % pf, before)
            jd("web_%s_w1_c09_after" % pf, after)
            finger_rows = round((y0 - y1) / rh, 2)
            # 스크롤 판정은 offsetY 변화 ÷ 손가락 이동(CSS px) ≥ 0.9(D-GV1 — first 사용 안 함, odev-8m §5)
            d_off = (after.get("offsetY") or 0) - (before.get("offsetY") or 0)
            ratio = round(d_off / (y0 - y1), 3) if y0 != y1 else None
            case("w1", "c09", {"selectedIndex": before.get("selectedIndex"), "offset_ratio": ">=0.9"},
                 {"selectedIndex": after.get("selectedIndex"), "offsetY_before": before.get("offsetY"), "offsetY_after": after.get("offsetY"), "finger_px": round(y0 - y1, 1), "rowHeight": rh,
                  "finger_rows": finger_rows, "offset_ratio": ratio, "first_before_ref": before.get("first"), "first_after_ref": after.get("first")},
                 {"cdp": "touch drag (%.1f,%.1f)→(%.1f,%.1f) 12단계 16ms" % (colx, y0, colx, y1)}, after.get("selectedIndex") == before.get("selectedIndex") and (ratio or 0) >= 0.9,
                 ["web_%s_w1_c09_before.json" % pf, "web_%s_w1_c09_after.json" % pf, shot("web_%s_w1_c09_shot" % pf)])
            # c10~c12 핀치
            cx, cy = gb["x"] + gb["w"] / 2, gb["y"] + gb["h"] / 2

            def pinch(d0, d1, lift_second_first=False):
                T("touchStart", [{"x": cx - d0, "y": cy, "id": 1}, {"x": cx + d0, "y": cy, "id": 2}])
                for i in range(1, 13):
                    d = d0 + (d1 - d0) * i / 12
                    b.sleep_ms(16)
                    T("touchMove", [{"x": cx - d, "y": cy, "id": 1}, {"x": cx + d, "y": cy, "id": 2}])
                if lift_second_first:
                    b.sleep_ms(30)
                    T("touchMove", [{"x": cx - d1, "y": cy, "id": 1}])
                    b.sleep_ms(300)
                T("touchEnd", [])
                sl(1.0)

            for cid, args in (("c10", (40, 90, False)), ("c11", (90, 40, False)), ("c12", (40, 60, True))):
                s0 = GV().get("selectedIndex")
                p0 = b.call("pinch.state")
                pinch(*args)
                s1 = GV().get("selectedIndex")
                p1 = b.call("pinch.state")
                jd("web_%s_w1_%s_after" % (pf, cid), {"sel_before": s0, "sel_after": s1, "pinch_before": p0, "pinch_after": p1})
                fs0, fs1 = (p0 or {}).get("fontSize"), (p1 or {}).get("fontSize")
                exp_font = {"c10": "증가", "c11": "감소", "c12": "변화 무관"}[cid]
                okf = (fs1 > fs0) if cid == "c10" else ((fs1 < fs0) if cid == "c11" else True)
                case("w1", cid, {"selectedIndex": s0, "fontSize": exp_font}, {"selectedIndex": s1, "fontSize": [fs0, fs1], "steps": [(p0 or {}).get("steps"), (p1 or {}).get("steps")]},
                     {"cdp": "2손가락 %s→%s px%s @(%.1f,%.1f)" % (args[0], args[1], ", 두 번째 손가락 먼저 뗌 300ms" if args[2] else "", cx, cy)}, s1 == s0 and bool(okf),
                     ["web_%s_w1_%s_after.json" % (pf, cid)])
            b.call("settings.set", baseFontSize=14)
            b.call("docs.zoom", doc=cur["G"], step="reset")
            sl(0.8)
            # c13 머리글(작업명) 탭 → 정렬, 원복
            # 정렬 판정: 선택 항목의 보이는 순서(selectedIndex)가 바뀌면 행 순서가 바뀐 것이다(viewHash 는 정렬에 안 바뀜 — i1705rc 실측)
            g = GV()
            hy = gb["y"] + H / 2
            touch_path(colx, hy, [])
            sl(0.8)
            g1 = GV()
            changed = g1.get("selectedIndex") != g.get("selectedIndex")
            s13 = shot("web_%s_w1_c13_shot" % pf)
            # 원복: DataGrid 머리글은 오름↔내림만 돌므로 문서를 닫고 다시 연다
            b.call("docs.close", doc=cur["G"])
            sl(0.8)
            pick_grid()
            g2 = GV()
            jd("web_%s_w1_c13_after" % pf, {"before": g, "after_tap": g1, "after_reopen": g2})
            case("w1", "c13", "머리글 탭 → 정렬(행 순서 변경), 이후 원복", {"selectedIndex_before": g.get("selectedIndex"), "selectedIndex_after": g1.get("selectedIndex"), "sorted": changed, "restored_by": "docs.close+docs.open", "first_after_reopen": g2.get("first")},
                 {"cdp": "touch tap 머리글(%.1f,%.1f)" % (colx, hy)}, changed, ["web_%s_w1_c13_after.json" % pf, s13])
            gb = F("grid.dgMain", doc=cur["G"]).get("bounds") or gb
            # c14 마우스 클릭
            g = GV()
            touch_path(colx, rowy(top(g) + 2, H), [])
            s = GV().get("selectedIndex")
            R = top(g) + 5
            ry = rowy(R, H)
            b.mouse("mouseMoved", colx, ry)
            b.mouse("mousePressed", colx, ry, "left", 1, 1)
            sl(0.2)
            mid = GV().get("selectedIndex")
            b.mouse("mouseReleased", colx, ry, "left", 1, 0)
            sl(0.4)
            fin = GV().get("selectedIndex")
            case("w1", "c14", {"selectedIndex_on_press": R}, {"S": s, "R": R, "on_press": mid, "after_release": fin},
                 {"cdp": "mouse press/release (%.1f,%.1f)" % (colx, ry)}, mid == R and fin == R)
            # c15 마우스 끌기 3행
            g = GV()
            R = top(g) + 3
            ry = rowy(R, H)
            b.mouse("mouseMoved", colx, ry)
            b.mouse("mousePressed", colx, ry, "left", 1, 1)
            for i in range(1, 7):
                b.sleep_ms(30)
                b.mouse("mouseMoved", colx, ry + rh * 3 * i / 6, "left", 0, 1)
            b.mouse("mouseReleased", colx, ry + rh * 3, "left", 1, 0)
            sl(0.5)
            v = GV()
            jd("web_%s_w1_c15_after" % pf, v)
            case("w1", "c15", "i1505 같은 조작과 같은 selectedIndex(대조는 web_i1505_c15 결과)", {"R": R, "selectedIndex": v.get("selectedIndex"), "first": v.get("first"), "rel": (v.get("selectedIndex") or 0) - R},
                 {"cdp": "mouse press (%.1f,%.1f) → 3행 아래 끌기 → release" % (colx, ry)}, "대조필요", ["web_%s_w1_c15_after.json" % pf])
            # adb a1~a4 (OS 실터치)
            for cid, kind in (("a1", "tap"), ("a2", 11), ("a3", 14), ("a4", "long")):
                g = GV()
                Srow, R = top(g) + 2, top(g) + 4
                touch_path(colx, rowy(Srow, H), [])
                before = GV()
                ry = sy(rowy(R, H))
                if kind == "tap":
                    act = ["input", "tap", sx(colx), ry]
                elif kind == "long":
                    act = ["input", "swipe", sx(colx), sy(gb["y"] + gb["h"] * 0.8), sx(colx), sy(gb["y"] + gb["h"] * 0.2), 300]
                else:
                    act = ["input", "swipe", sx(colx), ry, sx(colx), ry + kind, 150]
                adb("shell", *act)
                sl(0.8)
                after = GV()
                jd("web_%s_w1_%s_before" % (pf, cid), before)
                jd("web_%s_w1_%s_after" % (pf, cid), after)
                if kind == "long":
                    fr = round(gb["h"] * 0.6 / rh, 2)
                    fpx = (act[3] - act[5]) / dpr  # 손가락 이동(CSS px) = 화면 px ÷ dpr
                    d_off = (after.get("offsetY") or 0) - (before.get("offsetY") or 0)
                    ok = after.get("selectedIndex") == before.get("selectedIndex") and d_off > 0
                    actual = {"S": before.get("selectedIndex"), "selectedIndex": after.get("selectedIndex"), "offsetY_before": before.get("offsetY"), "offsetY_after": after.get("offsetY"),
                              "finger_px": round(fpx, 1), "rowHeight": rh, "finger_rows": fr, "offset_ratio": round(d_off / fpx, 3) if fpx else None,
                              "first_before_ref": before.get("first"), "first_after_ref": after.get("first")}
                    files = [shot("web_%s_w1_a4_shot" % pf)]
                else:
                    exp = R if kind in ("tap", 11) else before.get("selectedIndex")
                    ok = after.get("selectedIndex") == exp
                    actual = {"S": before.get("selectedIndex"), "R": R, "selectedIndex": after.get("selectedIndex")}
                    files = []
                case("w1", cid, {"a1": "R", "a2": "R", "a3": "S", "a4": "S·first 증가"}[cid], actual,
                     {"adb": " ".join(str(x) for x in act), "screen_px": act[2:], "css_x": round(colx, 1), "oy": oy, "dpr": dpr}, ok,
                     files + ["web_%s_w1_%s_before.json" % (pf, cid), "web_%s_w1_%s_after.json" % (pf, cid)])

        # ================= D-W2 =================
        if "w2" in a.only:
            def menu_state():
                tb = F("shell.titleBar").get("bounds") or {}
                more = F("shell.mnuMore")
                tops = [{"id": i, "text": F(i).get("text"), "vis": F(i).get("effectivelyVisible"), "b": F(i).get("bounds")} for i in TOP]
                ml = b.call("menu.list", id="shell.mnuMore", depth=1)
                shown = [t for t in tops if t["vis"]]
                ends = [(t["b"]["x"] + t["b"]["w"]) for t in shown if t["b"]] + ([more["bounds"]["x"] + more["bounds"]["w"]] if more.get("effectivelyVisible") and more.get("bounds") else [])
                items = ml.get("items") if isinstance(ml, dict) else ml
                ch = [x.get("id") for x in (items or []) if isinstance(x, dict)]
                right = (tb.get("x") or 0) + (tb.get("w") or 0)
                return {"titleBar": tb, "more_vis": more.get("effectivelyVisible"), "more_b": more.get("bounds"), "tops": tops, "children": ch,
                        "max_right": max(ends) if ends else None, "bar_right": right, "fits": (max(ends) <= right + 0.5) if ends else None, "shown": [t["id"] for t in shown]}

            ms = menu_state()
            jd("web_%s_w2_state" % pf, ms)
            moved = [i for i in TOP if i not in ms["shown"]]
            if a.tag == "F":
                ok = bool(ms["more_vis"]) and bool(ms["fits"]) and ms["shown"] == TOP[:len(ms["shown"])] and len(moved) >= 1
                case("w2", "c01", "더보기 표시·오른쪽 끝 ≤ 제목줄 끝·앞부분 유지", {"more_vis": ms["more_vis"], "max_right": ms["max_right"], "bar_right": ms["bar_right"], "shown": ms["shown"], "moved": moved},
                     {"remote": "ui.find titleBar/mnuMore/5메뉴"}, ok, ["web_%s_w2_state.json" % pf, shot("web_%s_w2_c01_shot" % pf)])
                case("w2", "c02", "더보기 하위 = 옮긴 메뉴(원래 순서)", {"children": ms["children"], "moved": moved}, {"remote": "menu.list shell.mnuMore depth=1"},
                     [c for c in ms["children"] if c in TOP] == moved and "shell.mnuSys" in ms["children"])
                # c03 터치로 더보기 열기
                mb = ms["more_b"] or {}
                tx, ty = mb.get("x", 0) + mb.get("w", 0) / 2, mb.get("y", 0) + mb.get("h", 0) / 2
                touch_path(tx, ty, [])
                sl(1.0)
                opened1 = F("shell.mnuSys").get("effectivelyVisible")
                sb = F("shell.mnuSys").get("bounds") or {}
                if sb:
                    touch_path(sb["x"] + sb["w"] / 2, sb["y"] + sb["h"] / 2, [])
                    sl(1.0)
                sub = F("shell.mnuSysAbout")
                s3 = shot("web_%s_w2_c03_shot" % pf)
                for _ in range(2):
                    b.cdp("Input.dispatchKeyEvent", type="keyDown", key="Escape", code="Escape", windowsVirtualKeyCode=27)
                    b.cdp("Input.dispatchKeyEvent", type="keyUp", key="Escape", code="Escape", windowsVirtualKeyCode=27)
                    sl(0.3)
                case("w2", "c03", "터치로 더보기→시스템 하위가 화면 안에 열림", {"mnuSys_visible_after_more_tap": opened1, "child_visible": sub.get("effectivelyVisible"), "child_bounds": sub.get("bounds"), "viewport_h": rep["metrics"]["h"]},
                     {"cdp": "touch tap 더보기(%.1f,%.1f) → 시스템" % (tx, ty)}, "캡처판정필요" if sub.get("effectivelyVisible") else False, [s3])
                # c04 영어
                b.call("lang.set", code="en")
                sl(1.5)
                ms_en = menu_state()
                jd("web_%s_w2_c04_after" % pf, ms_en)
                s4 = shot("web_%s_w2_c04_shot" % pf)
                b.call("lang.set", code="ko")
                sl(1.5)
                moved_en = [i for i in TOP if i not in ms_en["shown"]]
                case("w2", "c04", "영어에서도 넘침 없음, 옮긴 수 ≥ 한국어", {"fits": ms_en["fits"], "max_right": ms_en["max_right"], "bar_right": ms_en["bar_right"], "moved_en": moved_en, "moved_ko": moved, "texts": [t["text"] for t in ms_en["tops"]]},
                     {"remote": "lang.set code=en"}, bool(ms_en["fits"]) and bool(ms_en["more_vis"]) and len(moved_en) >= len(moved), ["web_%s_w2_c04_after.json" % pf, s4])
            else:
                ok = (not ms["more_vis"]) and ms["shown"] == TOP and len(ms["children"]) == 0
                case("w2", "c05", "더보기 숨김·5메뉴·하위 0", {"more_vis": ms["more_vis"], "shown": ms["shown"], "children": ms["children"]}, {"remote": "ui.find·menu.list"}, ok,
                     ["web_%s_w2_state.json" % pf, shot("web_%s_w2_c05_shot" % pf)])
                out = []
                for m in ("shell.btnUiModePhone", "shell.btnUiModeTablet"):
                    bb = F(m).get("bounds")
                    if bb:
                        touch_path(bb["x"] + bb["w"] / 2, bb["y"] + bb["h"] / 2, [])
                    sl(1.0)
                    m8 = menu_state()
                    out.append({"mode": (b.call("ui.mode") or {}).get("uiModeEffective"), "more_vis": m8["more_vis"], "shown": m8["shown"], "fits": m8["fits"],
                                "ok": (not m8["more_vis"]) and m8["shown"] == TOP})
                b.call("ui.mode", set="auto")
                case("w2", "c08", "UI-19 전환 뒤 더보기 숨김·5메뉴", out, {"cdp": "touch tap 스위치 두 칸"}, all(x["ok"] for x in out))

        # ================= D-W3 =================
        if "w3" in a.only:
            def w3(cid, what, fn, two=True):
                pre = shot("web_%s_w3_%s_before" % (pf, cid))
                fn()
                sl(1.5)
                s1 = shot("web_%s_w3_%s_shot1" % (pf, cid))
                s2 = None
                if two:
                    sl(1.5)
                    s2 = shot("web_%s_w3_%s_shot2" % (pf, cid))
                return [pre, s1, s2]

            g = GV()
            ry = rowy(top(g) + 3, H)
            if a.tag == "F":
                case("w3", "c01", "탭 뒤 1.5·3초 툴팁 없음", "캡처판정필요", {"cdp": "touch tap 작업명(%.1f,%.1f)" % (colx, ry)}, "캡처판정필요",
                     w3("c01", "tap", lambda: touch_path(colx, ry, [])))
                case("w3", "c02", "길게 누름 뒤 툴팁 없음", "캡처판정필요", {"cdp": "touchStart→1000ms→touchEnd (%.1f,%.1f)" % (colx, ry)}, "캡처판정필요",
                     w3("c02", "long", lambda: touch_path(colx, ry, [], hold=1000), False))

                def drag():
                    y0, y1 = gb["y"] + gb["h"] * 0.8, gb["y"] + gb["h"] * 0.2
                    T("touchStart", [{"x": colx, "y": y0, "id": 1}])
                    for i in range(1, 13):
                        b.sleep_ms(16)
                        T("touchMove", [{"x": colx, "y": y0 + (y1 - y0) * i / 12, "id": 1}])
                    b.sleep_ms(60)
                    T("touchEnd", [])
                case("w3", "c03", "세로 끌기 뒤 툴팁 없음", "캡처판정필요", {"cdp": "touch drag 작업명 열"}, "캡처판정필요", w3("c03", "drag", drag, False))
                # adb 실터치 tap 도 1건
                case("w3", "c01a", "adb 탭 뒤 1.5·3초 툴팁 없음", "캡처판정필요", {"adb": "input tap %d %d" % (sx(colx), sy(ry))}, "캡처판정필요",
                     w3("c01a", "adbtap", lambda: adb("shell", "input", "tap", sx(colx), sy(ry))))
                # 마우스 회귀(접힘 폭에서도)
                ry2 = rowy(top(g) + 4, H)
                b.mouse("mouseMoved", colx - 20, ry2 - 5)
                case("w3", "c06", "마우스 hover 1.5초 뒤 툴팁 뜸(회귀)", "캡처판정필요", {"cdp": "mouseMoved (%.1f,%.1f)" % (colx, ry2)}, "캡처판정필요",
                     w3("c06", "hover", lambda: b.mouse("mouseMoved", colx, ry2), False))
                b.mouse("mouseMoved", 5, 5)
                sl(0.8)
                ry3 = rowy(top(g) + 6, H)

                def c07f():
                    touch_path(colx, ry, [])
                    sl(0.3)
                    b.mouse("mouseMoved", colx - 10, ry3)
                    b.sleep_ms(50)
                    b.mouse("mouseMoved", colx, ry3)
                case("w3", "c07", "터치 뒤 마우스 이동하면 툴팁 뜸", "캡처판정필요", {"cdp": "touch tap → mouseMoved (%.1f,%.1f)" % (colx, ry3)}, "캡처판정필요", w3("c07", "mix", c07f, False))
                b.mouse("mouseMoved", 5, 5)
            else:
                case("w3", "c04", "탭 뒤 1.5·3초 툴팁 없음", "캡처판정필요", {"cdp": "touch tap 작업명(%.1f,%.1f)" % (colx, ry)}, "캡처판정필요",
                     w3("c04", "tap", lambda: touch_path(colx, ry, [])))
                # c05 대체 2: 선택된 스위치 칸 다시 탭(titlebar.uiMode.tip)
                on = [m for m in ("shell.btnUiModePhone", "shell.btnUiModeTablet") if "pdUiOn" in (F(m).get("classes") or [])]
                bb = F(on[0]).get("bounds") if on else None
                if bb:
                    case("w3", "c05", "선택된 스위치 칸 다시 탭 뒤 툴팁 없음(odev-8m 대체 2)", "캡처판정필요", {"cdp": "touch tap %s (%.1f,%.1f)" % (on[0], bb["x"] + bb["w"] / 2, bb["y"] + bb["h"] / 2)},
                         "캡처판정필요", w3("c05", "switch", lambda: touch_path(bb["x"] + bb["w"] / 2, bb["y"] + bb["h"] / 2, [])))
                # c06 마우스 hover → 툴팁 뜸
                ry2 = rowy(top(g) + 4, H)
                b.mouse("mouseMoved", colx - 20, ry2 - 5)
                case("w3", "c06", "마우스 hover 1.5초 뒤 툴팁 뜸(회귀)", "캡처판정필요", {"cdp": "mouseMoved (%.1f,%.1f)" % (colx, ry2)}, "캡처판정필요",
                     w3("c06", "hover", lambda: b.mouse("mouseMoved", colx, ry2), False))
                b.mouse("mouseMoved", 5, 5)
                sl(0.8)
                # c07 터치 탭 직후 마우스 이동 → 툴팁 뜸
                ry3 = rowy(top(g) + 6, H)

                def c07():
                    touch_path(colx, ry, [])
                    sl(0.3)
                    b.mouse("mouseMoved", colx - 10, ry3)
                    b.sleep_ms(50)
                    b.mouse("mouseMoved", colx, ry3)
                case("w3", "c07", "터치 뒤 마우스 이동하면 툴팁 뜸", "캡처판정필요", {"cdp": "touch tap → mouseMoved (%.1f,%.1f)" % (colx, ry3)}, "캡처판정필요", w3("c07", "mix", c07, False))
                b.mouse("mouseMoved", 5, 5)
        rep["settings_end"] = b.call("settings.get")
    except Exception as e:  # noqa: BLE001
        import traceback
        rep["error"] = traceback.format_exc()
        print("ERROR", repr(e), flush=True)
    finally:
        try:
            b.call("settings.set", theme="System", baseFontSize=14)
            b.call("ui.mode", set="auto")
            b.call("docs.zoom", doc=cur["G"], step="reset")
        except Exception:  # noqa: BLE001
            pass
        jd("web_dw_%s_result" % a.tag, rep)
        b.close()
        print("EXIT", flush=True)


if __name__ == "__main__":
    main()
