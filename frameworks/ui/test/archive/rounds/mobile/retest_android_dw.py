# i1705rc 터치 결함 D-W1~3 Android 앱 adb 시험(Fold5, 펼침·접힘)과 UI-20 ui20_check(펼침)를 실행해 케이스별 기대·실제·조작 좌표를 남기는 스크립트(odev-8m dw_test_procedure.md 앱 축)
"""
사용: python3 -E -u tools/retest_android_dw.py --apk <i1705rc APK> [--skip-ui20]
증거: logs/otest_m1/i1705rc/dw/app_{apkU|apkF}_{w1|w2|w3}_{케이스}_{before|after|shot*}.{json|png}, dw_app_result.json
REQ-25: 케이스마다 expect·actual·action(adb 명령·화면 px·논리 좌표)을 기록해 같은 케이스 ID 를 웹(web_ 접두)과 맞댈 수 있게 한다.
앱은 인자 없이 띄운다(--fresh 없음). 끝나면 앱 제거·forward 해제·stay_on 원값·wm size reset.
"""
import argparse
import json
import math
import os
import subprocess
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import retest_android as ra  # noqa: E402

ROOT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
O = os.path.join(ROOT, "logs", "otest_m1", "i1705rc", "dw")
FOLD = "904x2316"
S = 360 / 160.0  # density 360(원값) 기준 논리px→실화면 px
TOP = ["shell.mnuFile", "shell.mnuWindow", "shell.mnuView", "shell.mnuWinMenu", "shell.mnuSys"]


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--apk", required=True)
    p.add_argument("--serial", default="R3CW70202FP")
    p.add_argument("--skip-ui20", action="store_true")
    p.add_argument("--out-dir", default=None, help="증거 폴더(기본 logs/otest_m1/i1705rc/dw)")
    p.add_argument("--only", default="w1,w2,w3", help="실행할 항목(w1,w2,w3 쉼표) — i1720rc §5 r4 는 w1")
    a = p.parse_args()
    global O
    if a.out_dir:
        O = os.path.abspath(a.out_dir)
    only = set(a.only.split(","))
    os.makedirs(O, exist_ok=True)
    sr = a.serial
    state, rep = {}, {"apk": a.apk, "serial": sr, "scale": S, "cases": [], "calib": {}}

    def adb(*args):
        return ra.adb(sr, *args)

    def jdump(name, obj):
        json.dump(obj, open(os.path.join(O, name + ".json"), "w", encoding="utf-8"), ensure_ascii=False, indent=1)

    def cap(name):
        rc, data = ra.adb(sr, "exec-out", "screencap", "-p", binary=True)
        k = data.find(b"\x89PNG") if rc == 0 else -1
        if k >= 0:
            open(os.path.join(O, name + ".png"), "wb").write(data[k:])
            return name + ".png"
        return None

    def case(pf, w, cid, expect, actual, action, ok, files=None, note=None):
        c = {"id": "%s_%s" % (w, cid), "platform": pf, "expect": expect, "actual": actual, "action": action,
             "verdict": "PASS" if ok is True else ("FAIL" if ok is False else str(ok)), "files": files or [], "note": note}
        rep["cases"].append(c)
        print("CASE", pf, w, cid, c["verdict"], json.dumps(actual, ensure_ascii=False)[:200], flush=True)
        jdump("dw_app_result", rep)

    try:
        info = ra.prepare(sr, a.apk, state)
        if not info:
            raise RuntimeError("앱 연결 실패")
        from retest_remote import Remote
        time.sleep(4)
        r = Remote()
        F = lambda i, **k: r.call("ui.find", id=i, **k) or {}  # noqa: E731
        GV = lambda: r.call("grid.visible", id="grid.dgMain", doc="Grid-1") or {}  # noqa: E731

        PITCH = {"P": None}

        def pitch():
            """실제 행 간격 P(RowHeight 28 과 다름, i1705rc 실측 약 31.1). 선택 행을 화면 안에 둔 채 30행으로 스크롤해 (offsetY − topClip) ÷ first 로 잰다(오염 없음)."""
            if PITCH["P"] is None:
                g0 = GV()
                r.call("grid.select", id="grid.dgMain", doc="Grid-1", index=30, scroll=True)
                time.sleep(0.6)
                gp = GV()
                off, f, ff = gp.get("offsetY") or 0, gp.get("first"), gp.get("firstFull")
                if f and ff is not None and f >= ff - 1:
                    PITCH["P"] = (off - (gp.get("topClip") or 0)) / f  # first 오염 없음
                elif ff:
                    PITCH["P"] = off / (ff - 0.5)  # first 오염(D-GV1) — firstFull 근사(오차 < 반 행/ff)
                else:
                    PITCH["P"] = g0.get("rowHeight") or 28
                # 검산: 포커스 행 잔류로 first·firstFull 이 함께 오염되면 P 가 크게 틀린다(i1720rc 웹 접힘 실측 120). RowHeight 의 0.9~1.6배 밖이면 버리고 RowHeight×1.11(실측 31.1/28) 을 쓴다
                rh0 = g0.get("rowHeight") or 28
                if not (0.9 * rh0 <= PITCH["P"] <= 1.6 * rh0):
                    rep.setdefault("pitch_rejected", []).append(round(PITCH["P"], 2))
                    PITCH["P"] = rh0 * 31.1 / 28
                rep["pitch"] = {"P": round(PITCH["P"], 2), "probe": {k: gp.get(k) for k in ("first", "firstFull", "offsetY", "topClip")}}
                r.call("grid.select", id="grid.dgMain", doc="Grid-1", index=0, scroll=True)
                time.sleep(0.6)
            return PITCH["P"]

        def top_row(v):
            """완전히 보이는 첫 행 = ceil(offsetY / 실제 행 간격) — first 대신(D-GV1)."""
            return int(math.ceil(((v.get("offsetY") or 0) - 0.5) / pitch()))
        r.call("settings.set", theme="System", baseFontSize=14)
        r.call("ui.mode", set="auto")
        r.call("docs.close", all=True)
        time.sleep(1)
        r.call("docs.open", kind="grid", _timeout=120)
        time.sleep(2)
        rep["start_docs"] = r.call("docs.list")

        def tap(x, y):
            adb("shell", "input", "tap", str(int(round(x))), str(int(round(y))))

        def calibrate(pf):
            """작업명 열 x 에서 화면 y 를 8px 간격으로 훑어 행 경계를 찾고 C(= 원점/S + 머리글 높이)를 구한다."""
            gb = F("grid.dgMain", doc="Grid-1").get("bounds")
            PITCH["P"] = None  # 폼팩터(접힘·펼침)마다 다시 잰다
            rh = pitch()  # 아래 행 위치 계산은 실제 행 간격으로 한다
            rep.setdefault("pitch_by_pf", {})[pf] = rep.get("pitch")
            g = GV()
            top = g.get("topClip") or 0
            xs = (gb["x"] + min(230, gb["w"] * 0.55)) * S
            pts = []
            # D-GV1: first 는 화면 밖 포커스 행 컨테이너로 오염되므로 보정·좌표는 offsetY 로 계산한다
            for y in range(int((gb["y"] + 110) * S), int((gb["y"] + 175) * S), 8):
                tap(xs, y)
                time.sleep(0.45)
                v = GV()
                pts.append((y, v.get("selectedIndex"), v.get("offsetY") or 0))
            cs = []
            for (y0, r0, o0), (y1, r1, o1) in zip(pts, pts[1:]):
                if r0 is not None and r1 == r0 + 1 and o0 == o1:
                    yb = (y0 + y1) / 2.0  # 경계 추정(±4px)
                    cs.append(yb / S - gb["y"] - (r1 * rh - o1))
            C = sum(cs) / len(cs) if cs else None
            cal = {"gb": gb, "rowHeight": rh, "topClip": top, "x_screen": xs, "sweep": pts, "C_candidates": cs, "C": C}
            rep["calib"][pf] = cal
            jdump("app_%s_calib" % pf, cal)
            print("calib", pf, "C", C, "pts", pts, flush=True)
            if C is None:
                raise RuntimeError("보정 실패 " + pf)

            def row_y(i):
                g2 = GV()
                return S * (gb["y"] + C + i * rh - (g2.get("offsetY") or 0) + rh / 2.0)
            # 검증: 완전히 보이는 첫 행+3 중심 탭 → 그 행 선택
            g2 = GV()
            k = top_row(g2) + 3
            tap(xs, row_y(k))
            time.sleep(0.5)
            cal["verify"] = {"want": k, "got": GV().get("selectedIndex")}
            jdump("app_%s_calib" % pf, cal)
            print("calib verify", cal["verify"], flush=True)
            return gb, xs, row_y

        # ---------------- D-W1 (adb a1~a4) ----------------
        def dw1(pf):
            gb, xs, row_y = calibrate(pf)
            for cid, kind in (("a1", "tap"), ("a2", 11), ("a3", 14), ("a4", "long")):
                g0 = GV()
                Srow, R = top_row(g0) + 2, top_row(g0) + 4
                tap(xs, row_y(Srow))
                time.sleep(0.5)
                before = GV()
                s_sel = before.get("selectedIndex")
                ry = row_y(R)
                if kind == "tap":
                    act = ["input", "tap", int(xs), int(ry)]
                    expect = {"selectedIndex": R}
                elif kind == "long":
                    y0, y1 = (gb["y"] + gb["h"] * 0.8) * S, (gb["y"] + gb["h"] * 0.2) * S
                    act = ["input", "swipe", int(xs), int(y0), int(xs), int(y1), 300]
                    expect = {"selectedIndex": s_sel, "offset_ratio": ">= 0.9"}
                else:
                    act = ["input", "swipe", int(xs), int(ry), int(xs), int(ry) + kind, 150]
                    expect = {"selectedIndex": R if kind == 11 else s_sel}
                adb("shell", *[str(x) for x in act])
                time.sleep(0.8)
                after = GV()
                jdump("app_%s_w1_%s_before" % (pf, cid), before)
                jdump("app_%s_w1_%s_after" % (pf, cid), after)
                files = ["app_%s_w1_%s_before.json" % (pf, cid), "app_%s_w1_%s_after.json" % (pf, cid)]
                if kind == "long":
                    files.append(cap("app_%s_w1_%s_shot" % (pf, cid)))
                    # 스크롤 판정: offsetY 변화 ÷ 손가락 이동(논리 px) ≥ 0.9(D-GV1 — first 사용 안 함)
                    fpx = (act[3] - act[5]) / S
                    d_off = (after.get("offsetY") or 0) - (before.get("offsetY") or 0)
                    o_ratio = round(d_off / fpx, 3) if fpx else None
                    ok = after.get("selectedIndex") == s_sel and (o_ratio or 0) >= 0.9
                else:
                    ok = after.get("selectedIndex") == expect["selectedIndex"]
                    fpx = d_off = o_ratio = None
                logical = {"x": round(xs / S, 1), "y_R": round(ry / S, 1), "dy_logical": (round(kind / S, 2) if isinstance(kind, int) else None)}
                case(pf, "w1", cid, expect, {"S": s_sel, "R": R, "selectedIndex": after.get("selectedIndex"), "offsetY_before": before.get("offsetY"), "offsetY_after": after.get("offsetY"),
                                             "finger_px": round(fpx, 1) if fpx else None, "rowHeight": before.get("rowHeight"), "offset_ratio": o_ratio,
                                             "first_before_ref": before.get("first"), "first_after_ref": after.get("first")},
                     {"adb": " ".join(str(x) for x in act), "screen_px": act[2:], "logical": logical}, ok, files)
            case(pf, "w1", "pinch", "두 손가락 실핀치로 선택 불변·글자 확대", None, "adb 로 두 손가락 입력 불가", "미검증(사람 손 필요)")

        # ---------------- D-W2 ----------------
        def menu_state():
            tb = F("shell.titleBar").get("bounds") or {}
            more = F("shell.mnuMore")
            tops = []
            for i in TOP:
                f = F(i)
                tops.append({"id": i, "text": f.get("text") or f.get("header"), "vis": f.get("effectivelyVisible"), "b": f.get("bounds")})
            ml = r.call("menu.list", id="shell.mnuMore", depth=1)
            right = (tb.get("x") or 0) + (tb.get("w") or 0)
            shown = [t for t in tops if t["vis"]]
            ends = [(t["b"]["x"] + t["b"]["w"]) for t in shown if t["b"]] + ([more["bounds"]["x"] + more["bounds"]["w"]] if more.get("effectivelyVisible") and more.get("bounds") else [])
            return {"titleBar": tb, "more_vis": more.get("effectivelyVisible"), "more_b": more.get("bounds"), "tops": tops, "more_children": ml,
                    "max_right": max(ends) if ends else None, "bar_right": right, "fits": (max(ends) <= right + 0.5) if ends else None,
                    "shown_ids": [t["id"] for t in shown]}

        def children_ids(ms):
            ch = ms.get("more_children")
            items = ch.get("items") if isinstance(ch, dict) else ch
            return [x.get("id") for x in (items or []) if isinstance(x, dict) and (x.get("depth") in (None, 0, 1))]

        def judge_folded(ms):
            shown = ms["shown_ids"]
            prefix_ok = shown == TOP[:len(shown)]
            moved = [i for i in TOP if i not in shown]
            ch = children_ids(ms)
            return bool(ms["more_vis"]) and bool(ms["fits"]) and prefix_ok and len(moved) >= 1, {"moved": moved, "children": ch, "prefix_ok": prefix_ok}

        def judge_unfolded(ms):
            ch = children_ids(ms)
            return (not ms["more_vis"]) and ms["shown_ids"] == TOP and len(ch) == 0, {"children": ch}

        def fold(on):
            adb("shell", "wm", "size", FOLD if on else "reset")
            time.sleep(3)

        def dw2():
            fold(True)
            ms = menu_state()
            ok, ex = judge_folded(ms)
            jdump("app_apkF_w2_c01_after", ms)
            case("apkF", "w2", "c01", "더보기 표시·오른쪽 끝 ≤ 제목줄 끝·남은 메뉴는 앞부분", {"more_vis": ms["more_vis"], "max_right": ms["max_right"], "bar_right": ms["bar_right"], "shown": ms["shown_ids"], **ex},
                 {"remote": "ui.find shell.titleBar/mnuMore/5메뉴", "wm": FOLD}, ok, ["app_apkF_w2_c01_after.json", cap("app_apkF_w2_c01_shot")])
            ch = ex["children"]
            moved = ex["moved"]
            case("apkF", "w2", "c02", "더보기 하위에 옮겨진 메뉴(최소 mnuSys)가 원래 순서로", {"children": ch, "moved": moved},
                 {"remote": "menu.list id=shell.mnuMore depth=1"}, ("shell.mnuSys" in ch) and [c for c in ch if c in TOP] == moved)
            # c03: 터치로 더보기 열기 → 시스템 하위 열기 → 캡처 → ESC
            mb = ms.get("more_b") or {}
            act = None
            if mb:
                cx, cy = None, None
                # 화면 원점: 제목줄은 맨 위. 보정값 C 는 그리드용이므로 세로 원점은 상태바 인셋을 실측하지 않고 ui.click 경로를 함께 쓴다.
                r.call("ui.click", id="shell.mnuMore")
                time.sleep(1.2)
                sysb = F("shell.mnuSys")
                r.call("ui.click", id="shell.mnuSys")
                time.sleep(1.2)
                shot = cap("app_apkF_w2_c03_shot")
                sub = F("shell.mnuSysAbout")
                act = {"remote": "ui.click shell.mnuMore → ui.click shell.mnuSys", "mnuSys_bounds": sysb.get("bounds"), "child_visible": sub.get("effectivelyVisible"), "child_bounds": sub.get("bounds")}
                adb("shell", "input", "keyevent", "111")
                time.sleep(0.5)
                adb("shell", "input", "keyevent", "111")
                time.sleep(0.5)
                case("apkF", "w2", "c03", "하위 메뉴가 화면 안에 열림(캡처 판정)", {"child_visible": sub.get("effectivelyVisible"), "child_bounds": sub.get("bounds")}, act,
                     "캡처판정필요" if sub.get("effectivelyVisible") else False, [shot], "ui.click 은 Invoke 패턴. 터치로 여는 확인은 웹 CDP 담당")
            # c04 영어
            r.call("lang.set", code="en")
            time.sleep(1.5)
            ms_en = menu_state()
            ok_en, ex_en = judge_folded(ms_en)
            jdump("app_apkF_w2_c04_after", ms_en)
            r.call("lang.set", code="ko")
            time.sleep(1.5)
            case("apkF", "w2", "c04", "영어에서도 오른쪽 끝 ≤ 제목줄 끝, 옮긴 메뉴 수 ≥ 한국어", {"fits": ms_en["fits"], "max_right": ms_en["max_right"], "bar_right": ms_en["bar_right"], "moved_en": ex_en["moved"], "moved_ko": moved, "tops_text": [t["text"] for t in ms_en["tops"]]},
                 {"remote": "lang.set code=en → judge → lang.set code=ko"}, bool(ms_en["fits"]) and ms_en["more_vis"] and len(ex_en["moved"]) >= len(moved), ["app_apkF_w2_c04_after.json", cap("app_apkF_w2_c04_shot_ko_back")])
            # c05 펼침
            fold(False)
            ms_u = menu_state()
            ok_u, ex_u = judge_unfolded(ms_u)
            jdump("app_apkU_w2_c05_after", ms_u)
            case("apkU", "w2", "c05", "더보기 숨김·5메뉴 원래 순서·더보기 하위 0", {"more_vis": ms_u["more_vis"], "shown": ms_u["shown_ids"], **ex_u}, {"wm": "reset"}, ok_u, ["app_apkU_w2_c05_after.json", cap("app_apkU_w2_c05_shot")])
            # c06 반복 2회
            res = []
            for i in range(2):
                fold(True)
                a1, e1 = judge_folded(menu_state())
                fold(False)
                a2, e2 = judge_unfolded(menu_state())
                res.append({"folded": a1, "moved": e1["moved"], "unfolded": a2})
            case("apkU", "w2", "c06", "접기·펴기 2회 매번 같은 결과", res, {"wm": "904x2316 ↔ reset ×2"}, all(x["folded"] and x["unfolded"] and x["moved"] == moved for x in res))
            # c08 UI-19 스위치
            out = []
            for m in ("shell.btnUiModePhone", "shell.btnUiModeTablet"):
                r.call("ui.click", id=m)
                time.sleep(1.2)
                ms8 = menu_state()
                okm, exm = judge_unfolded(ms8)
                out.append({"mode": (r.call("ui.mode") or {}).get("uiModeEffective"), "more_vis": ms8["more_vis"], "shown": ms8["shown_ids"], "fits": ms8["fits"], "ok": okm})
            r.call("ui.mode", set="auto")
            case("apkU", "w2", "c08", "스위치 전환 뒤에도 더보기 숨김·5메뉴", out, {"remote": "ui.click shell.btnUiModePhone/Tablet"}, all(x["ok"] for x in out))

        # ---------------- D-W3 ----------------
        def dw3(pf, gb, xs, row_y):
            g = GV()
            k = top_row(g) + 3
            y = row_y(k)
            pre = cap("app_%s_w3_c01_before" % pf)
            if pf == "apkF":
                plan = [("c01", ["input", "tap", int(xs), int(y)], "탭"), ("c02", ["input", "swipe", int(xs), int(y), int(xs), int(y), 1000], "길게 누름"),
                        ("c03", ["input", "swipe", int(xs), int((gb["y"] + gb["h"] * 0.8) * S), int(xs), int((gb["y"] + gb["h"] * 0.2) * S), 300], "세로 끌기")]
            else:
                plan = [("c04", ["input", "tap", int(xs), int(y)], "탭")]
            for cid, act, what in plan:
                adb("shell", *[str(x) for x in act])
                time.sleep(1.5)
                s1 = cap("app_%s_w3_%s_shot1" % (pf, cid))
                time.sleep(1.5)
                s2 = cap("app_%s_w3_%s_shot2" % (pf, cid)) if cid in ("c01", "c04") else None
                case(pf, "w3", cid, "%s 뒤 1.5초%s 캡처에 툴팁 상자 없음" % (what, "·3초" if s2 else ""), "캡처판정필요",
                     {"adb": " ".join(str(x) for x in act), "screen_px": act[2:], "logical": {"x": round(xs / S, 1), "y": round(y / S, 1)}}, "캡처판정필요", [pre, s1, s2])
                time.sleep(0.8)

        def dw3_c05():
            r.call("ui.click", id="shell.mnuView")
            time.sleep(1)
            r.call("ui.click", id="shell.mnuUiMode")
            time.sleep(1)
            it = F("shell.mnuUiModeTablet")
            b = it.get("bounds")
            ml = r.call("menu.list", id="shell.mnuUiMode", depth=1)
            note = None
            if b and it.get("effectivelyVisible"):
                # 메뉴 팝업 항목의 화면 원점은 그리드 보정 C 와 다를 수 있어 그리드 보정의 세로 원점(oy≈C−머리글)을 쓰지 않고, 세로 원점 0·80px 두 후보 중 항목 높이 안에 드는 쪽을 쓴다(캡처로 확인)
                cx = (b["x"] + b["w"] / 2) * S
                cy = (b["y"] + b["h"] / 2) * S + rep["calib"]["apkU"].get("oy_px", 0)
                adb("shell", "input", "tap", str(int(cx)), str(int(cy)))
                act = {"adb": "input tap %d %d" % (cx, cy), "item_bounds": b}
            else:
                act = {"remote": "ui.click mnuView→mnuUiMode", "item_visible": it.get("effectivelyVisible")}
                note = "항목이 보이지 않아 탭 못 함"
            time.sleep(1.5)
            s1 = cap("app_apkU_w3_c05_shot1")
            adb("shell", "input", "keyevent", "111")
            time.sleep(0.5)
            case("apkU", "w3", "c05", "보기→화면 모드→태블릿 항목 터치 뒤 1.5초 캡처에 툴팁 없음", "캡처판정필요", dict(act, menu=ml), "캡처판정필요", [s1], note)

        # ===== 실행 순서: 펼침 → 접힘 =====
        gbU, xsU, rowU = None, None, None
        if "w1" in only:
            dw1("apkU")
        if "w3" in only:
            gbU, xsU, rowU = calibrate("apkU")
            cU = rep["calib"]["apkU"]
            # 세로 원점 추정(메뉴 항목 탭용): C − 머리글은 분리할 수 없으므로 제목줄 shell.mnuFile 중심 탭 실측 대신 0 을 쓴다(캡처로 확인)
            cU["oy_px"] = 0
            dw3("apkU", gbU, xsU, rowU)
            dw3_c05()
        fold(True)
        time.sleep(1)
        if "w1" in only:
            dw1("apkF")
        if "w3" in only:
            gbF, xsF, rowF = calibrate("apkF")
            dw3("apkF", gbF, xsF, rowF)
        fold(False)
        if "w2" in only:
            dw2()
        # ---------------- UI-20 (펼침) ----------------
        if not a.skip_ui20:
            fold(False)
            env = dict(os.environ)
            outp = os.path.join(O, "app_apkU_ui20_check.json")
            t0 = time.time()
            pr = subprocess.run([sys.executable, "-E", "-u", os.path.join(os.path.dirname(os.path.abspath(__file__)), "ui20_check.py"), "--out", outp],
                                env=env, capture_output=True, text=True, timeout=900)
            open(os.path.join(O, "app_apkU_ui20_check.log"), "w", encoding="utf-8").write(pr.stdout + "\n--- stderr ---\n" + pr.stderr)
            rep["ui20"] = {"rc": pr.returncode, "sec": round(time.time() - t0, 1), "out": "app_apkU_ui20_check.json", "log": "app_apkU_ui20_check.log", "tail": pr.stdout[-1500:]}
            cap("app_apkU_ui20_end")
            print("UI20 rc", pr.returncode, flush=True)
        rep["end_settings"] = r.call("settings.get")
    except Exception as e:  # noqa: BLE001
        rep["error"] = repr(e)
        print("ERROR", repr(e), flush=True)
    finally:
        jdump("dw_app_result", rep)
        try:
            ra.adb(sr, "shell", "wm", "size", "reset")
        finally:
            ra.cleanup(sr, state)
        print("EXIT", flush=True)


if __name__ == "__main__":
    main()
