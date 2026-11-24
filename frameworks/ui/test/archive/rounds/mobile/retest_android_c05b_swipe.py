# i1705rc Fold5 펼침에서 D-W3 c05 대체 1·2(알림 제목·선택된 스위치 칸 재탭) 툴팁 캡처와 adb swipe 지속시간별 스크롤 비율을 재는 스크립트(otest-m-2)
"""
사용: python3 -E -u tools/retest_android_c05b_swipe.py --apk <i1705rc APK> [--only c05,swipe]
증거: logs/otest_m1/i1705rc/dw/app_apkU_w3_c05b{1,2}_*.png·json, app_apkU_swipe_*.json, swipe_app.json
      c05 케이스는 dw_app_result.json 의 cases 에 추가한다(id w3_c05b1·w3_c05b2).
원복: 앱 제거·forward 해제·stay_on 0·wm size reset(density 는 건드리지 않음).
"""
import argparse
import json
import os
import sys
import threading
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import retest_android as ra  # noqa: E402

ROOT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
O = os.path.join(ROOT, "logs", "otest_m1", "i1705rc", "dw")
S = 360 / 160.0


def walk(node, out, depth=0):
    if isinstance(node, dict):
        out.append(node)
        for k in ("children", "tree"):
            v = node.get(k)
            if isinstance(v, list):
                for x in v:
                    walk(x, out, depth + 1)
    elif isinstance(node, list):
        for x in node:
            walk(x, out, depth + 1)
    return out


def inside(b, box):
    return b and box and box["x"] - 1 <= b["x"] and box["y"] - 1 <= b["y"] and b["x"] + b["w"] <= box["x"] + box["w"] + 1 and b["y"] + b["h"] <= box["y"] + box["h"] + 1


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--apk", required=True)
    p.add_argument("--serial", default="R3CW70202FP")
    p.add_argument("--only", default="c05,swipe")
    p.add_argument("--out-name", default="swipe_app.json")
    a = p.parse_args()
    only = set(a.only.split(","))
    sr = a.serial
    state = {}
    rep = {"apk": a.apk, "serial": sr, "scale": S, "started": time.strftime("%Y-%m-%d %H:%M:%S"), "c05": {}, "swipe": []}
    resp = os.path.join(O, a.out_name)

    def save():
        json.dump(rep, open(resp, "w", encoding="utf-8"), ensure_ascii=False, indent=1)

    def adb(*args):
        return ra.adb(sr, *args)

    def cap(name):
        rc, data = ra.adb(sr, "exec-out", "screencap", "-p", binary=True)
        k = data.find(b"\x89PNG") if rc == 0 else -1
        if k >= 0:
            open(os.path.join(O, name + ".png"), "wb").write(data[k:])
            return name + ".png"
        return None

    def tap(x, y):
        adb("shell", "input", "tap", str(int(round(x))), str(int(round(y))))

    new_cases = []
    try:
        adb("shell", "wm", "size", "reset")
        info = ra.prepare(sr, a.apk, state)
        if not info:
            raise RuntimeError("앱 연결 실패")
        from retest_remote import Remote
        time.sleep(4)
        r = Remote()
        F = lambda i, **k: r.call("ui.find", id=i, **k) or {}  # noqa: E731
        GV = lambda: r.call("grid.visible", id="grid.dgMain", doc="Grid-1") or {}  # noqa: E731
        r.call("settings.set", theme="System", baseFontSize=14)
        r.call("ui.mode", set="auto")
        r.call("docs.close", all=True)
        time.sleep(1)
        r.call("docs.open", kind="grid", _timeout=120)
        time.sleep(2)
        rep["docs"] = r.call("docs.list")
        rep["mode"] = r.call("ui.mode")
        rep["panels"] = r.call("panel.list")
        save()

        # ---------- 세로 원점 보정: 그리드 행 탭으로 oy(px) 결정 ----------
        gb = F("grid.dgMain", doc="Grid-1").get("bounds")
        g = GV()
        rh = g.get("rowHeight") or 28
        xs = (gb["x"] + min(230, gb["w"] * 0.55)) * S
        r.call("grid.select", id="grid.dgMain", doc="Grid-1", index=0, scroll=True)
        time.sleep(0.6)
        # 머리글 높이 H 는 모른다. 여러 후보 y 에서 탭해 선택 행으로 (원점+머리글) C 를 역산한다(retest_android_dw.calibrate 방식)
        pts = []
        for y in range(int((gb["y"] + 110) * S), int((gb["y"] + 175) * S), 8):
            tap(xs, y)
            time.sleep(0.45)
            v = GV()
            pts.append((y, v.get("selectedIndex"), v.get("offsetY") or 0))
        cs = []
        # D-GV1: first 대신 offsetY 로 행 위치를 계산한다
        for (y0, r0, o0), (y1, r1, o1) in zip(pts, pts[1:]):
            if r0 is not None and r1 == r0 + 1 and o0 == o1:
                cs.append(((y0 + y1) / 2.0) / S - gb["y"] - (r1 * rh - o1))
        C = sum(cs) / len(cs) if cs else None
        rep["calib"] = {"gb": gb, "rowHeight": rh, "x_screen": xs, "sweep": pts, "C_candidates": cs, "C": C}
        save()
        print("calib C", C, cs, flush=True)

        # ---------- c05 대체 1·2 ----------
        if "c05" in only:
            # 알림 패널 목록 첫 항목의 굵은 제목 TextBlock 찾기
            lst = F("notify.list")
            tree = r.call("tree.get", named=False, depth=80) or {}
            nodes = walk(tree, [])
            lb = lst.get("bounds")
            tbs = [n for n in nodes if n.get("type") == "TextBlock" and n.get("visible") and inside(n.get("bounds"), lb) and n.get("text")]
            items = [n for n in nodes if n.get("type") == "ListBoxItem" and n.get("visible") and inside(n.get("bounds"), lb)]
            items.sort(key=lambda n: n["bounds"]["y"])
            target = None
            pick_item = None
            for it in items[:3]:
                ib = it["bounds"]
                kids = [t for t in tbs if inside(t["bounds"], ib)]
                # 왼쪽 열(분류 → 굵은 제목 → 본문) 중 두 번째가 제목이다. 항목 경계 안에 다른 컨트롤 글자가 겹쳐 잡힐 수 있어 왼쪽 끝 x 로 거른다
                left = min((t["bounds"]["x"] for t in kids), default=None)
                col = sorted([t for t in kids if abs(t["bounds"]["x"] - left) < 1], key=lambda t: t["bounds"]["y"]) if kids else []
                if len(col) >= 3:
                    target, pick_item = col[1], it
                    break
            rep["c05"]["notify"] = {"list": lst, "items": items[:5], "target": target, "textblocks_in_list": tbs[:20]}
            save()
            # 세로 원점: 화면 y(px) = (논리 y + oy_l) * S 로 두고, 그리드 보정 C 에는 머리글이 섞여 있어 쓰지 않는다.
            # 알림 항목 탭으로 oy_l 를 후보 0·10·20·30·40 중 ListBox 선택이 맞는 값으로 정한다(항목 높이 안).
            if target:
                tb = target["bounds"]
                idx = items.index(pick_item)
                oyl = None
                # 다른 항목을 먼저 선택해 두고 목표 항목 탭 — 선택 변화로 좌표 검증
                tries = []
                for cand in (0, 12, 24, 36):
                    other = items[(idx + 1) % len(items)]["bounds"]
                    tap((other["x"] + 20) * S, (other["y"] + other["h"] / 2 + cand) * S)
                    time.sleep(0.6)
                    s0 = F("notify.list").get("selectedIndex")
                    tries.append({"cand": cand, "other_sel": s0})
                    if s0 == (idx + 1) % len(items):
                        oyl = cand
                        break
                rep["c05"]["oy_tries"] = tries
                rep["c05"]["oy_logical"] = oyl
                if oyl is None:
                    oyl = 0
                time.sleep(3.5)  # 위 탭으로 혹시 뜬 것이 있으면 사라지게
                cx, cy = (tb["x"] + min(tb["w"], 120) / 2) * S, (tb["y"] + tb["h"] / 2 + oyl) * S
                pre = cap("app_apkU_w3_c05b1_before")
                tap(cx, cy)
                time.sleep(1.5)
                s1 = cap("app_apkU_w3_c05b1_shot1")
                time.sleep(1.5)
                s2 = cap("app_apkU_w3_c05b1_shot2")
                sel = F("notify.list").get("selectedIndex")
                act = {"adb": "input tap %d %d" % (cx, cy), "screen_px": [int(cx), int(cy)], "logical": {"x": round(cx / S, 1), "y": round(tb["y"] + tb["h"] / 2, 1)},
                       "target_text": target.get("text"), "target_bounds": tb, "oy_logical": oyl, "selected_after": sel, "selected_expect": idx}
                json.dump(act, open(os.path.join(O, "app_apkU_w3_c05b1_action.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=1)
                new_cases.append({"id": "w3_c05b1", "platform": "apkU", "expect": "알림 패널 목록 항목의 굵은 제목(ToolTip.Tip=제목) 탭 뒤 1.5초·3초 캡처에 툴팁 상자 없음(odev-8m 대체 1)",
                                  "actual": "캡처판정필요", "action": act, "verdict": "캡처판정필요", "files": [pre, s1, s2, "app_apkU_w3_c05b1_action.json"],
                                  "note": "선택 검증 %s(기대 %s)" % (sel, idx)})
            else:
                new_cases.append({"id": "w3_c05b1", "platform": "apkU", "expect": "알림 제목 탭 뒤 툴팁 없음", "actual": None, "action": None, "verdict": "미실행", "files": [],
                                  "note": "알림 목록 제목 TextBlock 을 찾지 못함"})
            rep["c05"]["cases"] = new_cases
            save()
            time.sleep(3.5)
            # 대체 2: 선택된 스위치 칸 재탭
            on = []
            for m in ("shell.btnUiModePhone", "shell.btnUiModeTablet"):
                f = F(m)
                if "pdUiOn" in (f.get("classes") or []):
                    on.append((m, f))
            rep["c05"]["switch_on"] = on
            if on:
                m, f = on[0]
                bb = f["bounds"]
                oyl = rep["c05"].get("oy_logical") or 0
                cx, cy = (bb["x"] + bb["w"] / 2) * S, (bb["y"] + bb["h"] / 2 + oyl) * S
                mode0 = (r.call("ui.mode") or {})
                pre = cap("app_apkU_w3_c05b2_before")
                tap(cx, cy)
                time.sleep(1.5)
                s1 = cap("app_apkU_w3_c05b2_shot1")
                time.sleep(1.5)
                s2 = cap("app_apkU_w3_c05b2_shot2")
                mode1 = (r.call("ui.mode") or {})
                f1 = F(m)
                act = {"adb": "input tap %d %d" % (cx, cy), "screen_px": [int(cx), int(cy)], "logical": {"x": round(bb["x"] + bb["w"] / 2, 1), "y": round(bb["y"] + bb["h"] / 2, 1)},
                       "target": m, "target_bounds": bb, "oy_logical": oyl, "mode_before": mode0, "mode_after": mode1, "classes_after": f1.get("classes")}
                json.dump(act, open(os.path.join(O, "app_apkU_w3_c05b2_action.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=1)
                new_cases.append({"id": "w3_c05b2", "platform": "apkU", "expect": "제목줄 화면 모드 스위치의 선택된 칸(pdUiOn) 재탭 뒤 1.5초·3초 캡처에 툴팁 상자 없음(odev-8m 대체 2)",
                                  "actual": "캡처판정필요", "action": act, "verdict": "캡처판정필요", "files": [pre, s1, s2, "app_apkU_w3_c05b2_action.json"], "note": None})
            else:
                new_cases.append({"id": "w3_c05b2", "platform": "apkU", "expect": "선택된 스위치 칸 재탭 뒤 툴팁 없음", "actual": None, "action": None, "verdict": "미실행", "files": [],
                                  "note": "pdUiOn 칸을 찾지 못함"})
            rep["c05"]["cases"] = new_cases
            save()
            r.call("ui.mode", set="auto")
            time.sleep(1)

        # ---------- adb swipe 지속시간별 스크롤 비율 ----------
        if "swipe" in only:
            gb = F("grid.dgMain", doc="Grid-1").get("bounds")
            rep["swipe_gb"] = gb
            y0l = gb["y"] + gb["h"] * 0.8
            plan = [(7, 300), (7, 1000), (7, 2000), (2, 1000)]
            for rows_n, dur in plan:
                for k in range(3):
                    r.call("grid.select", id="grid.dgMain", doc="Grid-1", index=0, scroll=True)
                    time.sleep(1.0)
                    before = GV()
                    dl = rows_n * rh
                    ya, yb = y0l * S, (y0l - dl) * S
                    xx = xs
                    t0 = time.time()
                    adb("shell", "input", "swipe", str(int(xx)), str(int(ya)), str(int(xx)), str(int(yb)), str(dur))
                    t1 = time.time()
                    end = GV()
                    time.sleep(2.5)
                    after = GV()
                    move_px = int(ya) - int(yb)
                    move_l = move_px / S
                    d_end = (end.get("offsetY") or 0) - (before.get("offsetY") or 0)
                    d_set = (after.get("offsetY") or 0) - (before.get("offsetY") or 0)
                    row = {"rows": rows_n, "dur_ms": dur, "rep": k + 1, "adb": "input swipe %d %d %d %d %d" % (xx, ya, xx, yb, dur),
                           "move_px": move_px, "move_logical": round(move_l, 1), "adb_wall_ms": round((t1 - t0) * 1000),
                           "before": {kk: before.get(kk) for kk in ("first", "offsetY", "topClip", "selectedIndex")},
                           "end": {kk: end.get(kk) for kk in ("first", "offsetY", "topClip")},
                           "after": {kk: after.get(kk) for kk in ("first", "offsetY", "topClip", "selectedIndex")},
                           "d_offset_end": round(d_end, 1), "d_offset_settled": round(d_set, 1),
                           "ratio_end": round(d_end / move_l, 3) if move_l else None, "ratio_settled": round(d_set / move_l, 3) if move_l else None,
                           "rows_scrolled": round(d_set / rh, 2)}
                    rep["swipe"].append(row)
                    print("SWIPE", rows_n, dur, k + 1, row["d_offset_settled"], row["ratio_settled"], flush=True)
                    save()
        rep["end_settings"] = r.call("settings.get")
    except Exception as e:  # noqa: BLE001
        rep["error"] = repr(e)
        print("ERROR", repr(e), flush=True)
    finally:
        save()
        # dw_app_result.json 에 c05 대체 케이스 추가(같은 id 는 교체)
        if new_cases:
            pth = os.path.join(O, "dw_app_result.json")
            d = json.load(open(pth, encoding="utf-8"))
            ids = {c["id"] + "|" + c["platform"] for c in new_cases}
            d["cases"] = [c for c in d.get("cases", []) if c["id"] + "|" + c["platform"] not in ids] + new_cases
            d.setdefault("addenda", []).append({"by": "otest-m-2", "at": time.strftime("%Y-%m-%d %H:%M:%S"), "what": "w3_c05 대체 1·2 재시험", "apk": a.apk})
            json.dump(d, open(pth, "w", encoding="utf-8"), ensure_ascii=False, indent=1)
        try:
            ra.adb(sr, "shell", "wm", "size", "reset")
        finally:
            ra.cleanup(sr, state)
            ra.adb(sr, "shell", "settings", "put", "global", "stay_on_while_plugged_in", "0")
        print("EXIT", flush=True)


if __name__ == "__main__":
    main()
