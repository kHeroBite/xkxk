# i1835rc Fold5 앱 시험(odev-1s i1835rc_test_plan.md e46d7a9f): UX1-A·RH1-A·GV1-A·UIM1-U1/U2/F·Q16-A 축약·DAD1-A·U20-AU/AF 를 원격 명령·adb 로 재고 케이스별 원문·기대·판정을 남기는 스크립트(otest-m-3, retest_android_i1720rc.py f4a6f73a 기반)
"""
사용: python3 -E -u tools/retest_android_i1835rc.py --apk <i1835rc diag APK> [--only ux1,rh1,gv1,uim1,q16,u20,dad1]
결과: logs/otest_m2/i1835rc/app_<케이스>.json|png, app_i1835rc_summary.json
좌표: 화면 px = 논리 px × 2.25(density 360). 앱은 화면 원점부터 그린다(retest_android_q16_17.py 와 같음).
DAD1: 바깥 패널 Id(LeftPane·BottomPane)는 dock.asDocument 에 주지 않는다(D-DAD2).
원복: 패널 오른쪽·언어 ko·화면 모드 auto 를 되돌린 뒤 앱 제거·forward 해제·stay_on 0·wm size reset(density 360 유지).
"""
import argparse
import hashlib
import json
import math
import os
import subprocess
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import retest_android as ra  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, ".."))
O = os.path.join(ROOT, "logs", "otest_m2", "i1835rc")
FOLD = "904x2316"
S = 360 / 160.0
MANIFEST = "60bf6753361a"


def md5(p):
    return hashlib.md5(open(p, "rb").read()).hexdigest()


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--apk", required=True)
    p.add_argument("--serial", default="R3CW70202FP")
    p.add_argument("--only", default="ux1,rh1,gv1,uim1,q16,u20,dad1")
    p.add_argument("--expect-md5", default="1c32cb3a38f395eab26bc456229d73d0")
    a = p.parse_args()
    only = set(a.only.split(","))
    os.makedirs(O, exist_ok=True)
    sr = a.serial
    apk_md5 = md5(a.apk)
    tools_md5 = {"ui20_check.py": md5(os.path.join(HERE, "ui20_check.py")), "ui20_lib.py": md5(os.path.join(HERE, "ui20_lib.py")),
                 "retest_android_i1835rc.py": md5(os.path.abspath(__file__))}
    summ = {"build": "i1835rc", "manifest": MANIFEST, "apk": a.apk, "apk_md5": apk_md5, "tools_md5": tools_md5,
            "started": time.strftime("%Y-%m-%d %H:%M:%S"), "cases": []}
    if apk_md5 != a.expect_md5:
        summ["error"] = "APK md5 불일치 — 시험하지 않음"
        json.dump(summ, open(os.path.join(O, "app_i1835rc_summary.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=1)
        print("MD5_MISMATCH", apk_md5, flush=True)
        sys.exit(4)
    state = {}

    def save_summary():
        with open(os.path.join(O, "app_i1835rc_summary.json"), "w", encoding="utf-8") as f:
            json.dump(summ, f, ensure_ascii=False, indent=1)

    def jd(name, obj):
        with open(os.path.join(O, name), "w", encoding="utf-8") as f:
            json.dump(obj, f, ensure_ascii=False, indent=1)

    def cap(name):
        rc, data = ra.adb(sr, "exec-out", "screencap", "-p", binary=True)
        k = data.find(b"\x89PNG") if rc == 0 else -1
        if k >= 0:
            with open(os.path.join(O, name + ".png"), "wb") as f:
                f.write(data[k:])
            return name + ".png"
        return None

    def case(cid, expect, actual, verdict, files=None, note=None):
        c = {"id": cid, "build": "i1835rc", "manifest": MANIFEST, "apk_md5": apk_md5, "at": time.strftime("%Y-%m-%d %H:%M:%S"), "tools_md5": tools_md5,
             "expect": expect, "actual": actual, "verdict": verdict, "files": [f for f in (files or []) if f], "note": note}
        jd("app_%s.json" % cid, c)
        summ["cases"].append({k: c[k] for k in ("id", "verdict", "note")})
        save_summary()
        print("CASE", cid, verdict, json.dumps(actual, ensure_ascii=False)[:240], flush=True)

    def tap(x, y):
        ra.adb(sr, "shell", "input", "tap", str(int(round(x))), str(int(round(y))))

    def dev_time():
        rc, out = ra.adb(sr, "shell", "date", "+%m-%d %H:%M:%S.000")
        return out.strip()

    def split_lines(since, name):
        """앱 콘솔(logcat)의 [split] collapse|restore 진단 줄을 since 이후로 모아 저장한다(D-PW 관찰)."""
        rc, out = ra.adb(sr, "logcat", "-d", "-T", since)
        lines = [ln for ln in out.splitlines() if "[split]" in ln]
        with open(os.path.join(O, name), "w", encoding="utf-8") as f:
            f.write("\n".join(lines) + "\n")
        return len(lines)

    try:
        ra.adb(sr, "shell", "wm", "size", "reset")
        info = ra.prepare(sr, a.apk, state)
        if not info:
            raise RuntimeError("앱 연결 실패")
        from retest_remote import Remote
        time.sleep(4)
        r = Remote()

        def R(cmd, **k):
            return r.call(cmd, _timeout=120, **k)

        def last_err():
            return (r.calls[-1] if r.calls else {}).get("error")
        R("settings.set", theme="System", baseFontSize=14)
        R("ui.mode", set="auto")
        R("lang.set", code="ko")
        summ["settings_before"] = R("settings.get")
        summ["mode_before"] = R("ui.mode")
        summ["panels_before"] = R("panel.list")
        save_summary()

        def grid_doc():
            ds = R("docs.list") or []
            gs = [d.get("id") for d in ds if d.get("kind") == "grid"]
            if not gs:
                r.call("docs.open", kind="grid", _timeout=120)
                time.sleep(2)
                gs = [d.get("id") for d in (R("docs.list") or []) if d.get("kind") == "grid"]
            R("docs.activate", doc=gs[0])
            time.sleep(0.8)
            return gs[0]

        def pane(pl, pid="NotificationPane"):
            items = pl if isinstance(pl, list) else (pl or {}).get("panels") or []
            return next((x for x in items if isinstance(x, dict) and x.get("id") == pid), None)

        def walk(n, out):
            if isinstance(n, dict):
                out.append(n)
                for ch in n.get("children") or n.get("tree") or []:
                    walk(ch, out)
            elif isinstance(n, list):
                for ch in n:
                    walk(ch, out)
            return out

        # 문서는 그리드 1개로 시작(깨끗한 조건)
        R("docs.close", all=True)
        time.sleep(1)
        r.call("docs.open", kind="grid", _timeout=120)
        time.sleep(2)
        G = grid_doc()
        summ["docs_at_start"] = R("docs.list")
        save_summary()
        GV = lambda: R("grid.visible", id="grid.dgMain", doc=G) or {}  # noqa: E731

        # ---------- UX1-A ----------
        if "ux1" in only:
            sd = R("split.dump")
            jd("app_UX1-A_dump.json", sd)
            lt = R("log.tail", n=200)
            jd("app_UX1-A_logtail.json", lt)
            tff, plat = (sd or {}).get("touchFormFactor"), (sd or {}).get("platform")
            host = [x for x in (lt or []) if "[host] form-factor" in json.dumps(x, ensure_ascii=False)]
            case("UX1-A", {"touchFormFactor": True, "platform": "android", "host_log": "없음(플랫폼 판정)"},
                 {"touchFormFactor": tff, "platform": plat, "compact": (sd or {}).get("compact"), "host_log_lines": host},
                 "PASS" if tff is True and plat == "android" and not host else "FAIL", ["app_UX1-A_dump.json", "app_UX1-A_logtail.json"], "i1720rc 와 같아야 함")

        # ---------- RH1-A (펼침·접힘) — 판정 밖, 실측만 ----------
        def rh1(pf):
            R("grid.select", id="grid.dgMain", doc=G, index=0, scroll=True)
            time.sleep(0.8)
            tree = R("tree.get", doc=G, named=False, depth=90) or {}
            nodes = walk(tree, [])
            rows = [n for n in nodes if n.get("type") == "DataGridRow" and isinstance(n.get("bounds"), dict) and (n.get("bounds") or {}).get("h", 0) > 0]
            cells = [n for n in nodes if n.get("type") == "DataGridCell" and isinstance(n.get("bounds"), dict) and (n.get("bounds") or {}).get("h", 0) > 0]
            rb = sorted([(n["bounds"].get("y"), n["bounds"].get("h")) for n in rows if n.get("isVisible", n.get("visible", True)) is not False])
            hs = [h for _, h in rb]
            ys = [y for y, _ in rb]
            diffs = [round(y1 - y0, 2) for y0, y1 in zip(ys, ys[1:])]
            ch = sorted(set(round((n.get("bounds") or {}).get("h", 0), 2) for n in cells))
            # 스크롤 기반 검산: 30행으로 스크롤해 (offsetY − topClip) ÷ first
            R("grid.select", id="grid.dgMain", doc=G, index=30, scroll=True)
            time.sleep(0.8)
            g30 = GV()
            P_scroll = round(((g30.get("offsetY") or 0) - (g30.get("topClip") or 0)) / g30["first"], 3) if g30.get("first") else None
            R("grid.select", id="grid.dgMain", doc=G, index=0, scroll=True)
            time.sleep(0.5)
            med = sorted(hs)[len(hs) // 2] if hs else None
            jd("app_RH1-A%s_tree.json" % pf, tree)
            shot = cap("app_RH1-A%s" % pf)
            case("RH1-A%s" % pf, "실측만(판정 밖). i1720rc 앱 31.11(펼침)·31.02(접힘), RowHeight 28",
                 {"row_bounds_h": hs[:20], "row_h_median": med, "row_y_diffs": diffs[:20], "cell_bounds_h_set": ch[:10], "rowHeight_setting": g30.get("rowHeight"),
                  "P_scroll": P_scroll, "scroll30": {k: g30.get(k) for k in ("first", "firstFull", "offsetY", "topClip", "staleRows")}, "rows_found": len(rows)},
                 "기록", ["app_RH1-A%s_tree.json" % pf, shot], "접힘은 wm size 904x2316 대용" if pf == "F" else None)
            return med or P_scroll or 31.1

        P = 31.1
        if "rh1" in only:
            P = rh1("U")
            ra.adb(sr, "shell", "wm", "size", FOLD)
            time.sleep(3)
            G = grid_doc()
            rh1("F")
            ra.adb(sr, "shell", "wm", "size", "reset")
            time.sleep(3)
            G = grid_doc()

        # ---------- GV1-A (펼침) ----------
        if "gv1" in only:
            gb = (R("ui.find", id="grid.dgMain", doc=G) or {}).get("bounds")
            xs = (gb["x"] + min(230, gb["w"] * 0.55)) * S
            H = 32.25  # 머리글 높이 근사 — 탭 결과(selectedIndex)로 보정한다
            tries = []

            def tap_row(i):
                nonlocal H
                for _ in range(4):
                    g = GV()
                    y = (gb["y"] + H + (i + 0.5) * P - (g.get("offsetY") or 0)) * S
                    tap(xs, y)
                    time.sleep(0.6)
                    got = GV().get("selectedIndex")
                    tries.append({"want": i, "y_screen": round(y), "got": got, "H": H})
                    if got == i:
                        return True
                    if got is None or got < 0:
                        return False
                    H += (i - got) * P * 0.9
                return False
            # 탭 없는 A2 를 먼저 한다 — 앱은 다시 읽기가 없어 A 의 행 2 탭(포커스 행)이 A2 에 남는다(19:3x 1차 실행 무효 원인)
            for cid, pre_tap in (("GV1-A2", False), ("GV1-A", True)):
                R("grid.select", id="grid.dgMain", doc=G, index=0, scroll=True)
                time.sleep(0.8)
                tapped = tap_row(2) if pre_tap else None
                b0 = GV()
                y0, y1 = (gb["y"] + gb["h"] * 0.8) * S, (gb["y"] + gb["h"] * 0.2) * S
                act = ["input", "swipe", int(xs), int(y0), int(xs), int(y1), 300]
                ra.adb(sr, "shell", *[str(x) for x in act])
                time.sleep(2.0)
                mid = GV()
                sel = None
                if pre_tap:
                    sel = R("grid.select", id="grid.dgMain", doc=G, index=30, scroll=True)
                    time.sleep(1.0)
                after = GV()
                jd("app_%s_before.json" % cid, b0)
                jd("app_%s_after.json" % cid, after)
                shot = cap("app_%s_after" % cid)
                off = after.get("offsetY") or 0
                want = math.floor(off / 31.05)
                st = after.get("staleRows") or []
                ok_rows = after.get("first") is not None and abs(after["first"] - want) <= 1 and abs((after.get("firstFull") or -9) - want) <= 1
                if pre_tap:
                    exp = {"staleRows": "포커스 행 2 포함(2 가 화면 밖일 때)", "first≈firstFull≈floor(offsetY/31.05)": want}
                    ok = ok_rows and (2 in st or (after.get("first") or 0) <= 2)
                else:
                    exp = {"staleRows": [], "first≈firstFull≈floor(offsetY/31.05)": want}
                    ok = ok_rows and st == []
                case(cid, exp, {"tapped_row2": tapped, "after_swipe": {k: mid.get(k) for k in ("first", "firstFull", "offsetY", "staleRows", "selectedIndex")},
                                "select30": sel, "after": {k: after.get(k) for k in ("first", "firstFull", "lastFull", "offsetY", "topClip", "staleRows", "selectedIndex")},
                                "floor_offset_P": want, "swipe_ratio": round(((mid.get("offsetY") or 0) - (b0.get("offsetY") or 0)) / ((act[3] - act[5]) / S), 3)},
                     "PASS(캡처 확인 필요)" if ok else "FAIL", ["app_%s_before.json" % cid, "app_%s_after.json" % cid, shot],
                     "GV1-A2 는 계획서 GV1-W2 의 앱 짝(추가). 탭 보정 기록=%s" % json.dumps(tries[-4:], ensure_ascii=False))
            R("grid.select", id="grid.dgMain", doc=G, index=0, scroll=True)

        # ---------- UIM1 ----------
        if "uim1" in only:
            def btn(which):
                f = R("ui.find", id="shell.btnUiMode%s" % which) or {}
                return f
            def press(which, label):
                f = btn(which)
                b = f.get("bounds")
                vw = (R("ui.mode") or {}).get("viewWidth") or 9999
                # 19:3x 1차 실행: 접힘에서 bounds 가 남아 있었지만 x(508.9)가 viewWidth(401.8) 밖이었다 — effectivelyVisible·화면 안 여부까지 본다
                if not b or f.get("isVisible") is False or f.get("effectivelyVisible") is False or not (b.get("w") and b.get("h")) or b["x"] + b["w"] / 2 > vw:
                    return {"found": False, "find": f, "viewWidth": vw, "err": last_err()}
                x, y = (b["x"] + b["w"] / 2) * S, (b["y"] + b["h"] / 2) * S
                tap(x, y)
                time.sleep(1.5)
                return {"found": True, "screen": [round(x), round(y)], "bounds": b, "shot": cap("app_UIM1_%s" % label)}
            R("ui.mode", set="auto")
            time.sleep(1)
            m0 = R("ui.mode")
            p1 = press("Tablet", "U1_tap_tablet")
            m1 = R("ui.mode")
            case("UIM1-U1", {"uiMode": "Auto", "uiModeEffective": "Tablet"}, {"start": m0, "tap": p1, "after": m1},
                 "PASS" if (m1 or {}).get("uiMode") == "Auto" and (m1 or {}).get("uiModeEffective") == "Tablet" and p1.get("found") else "FAIL", [p1.get("shot")])
            p2a = press("Phone", "U2_tap_phone")
            m2a = R("ui.mode")
            p2b = press("Tablet", "U2_tap_tablet")
            m2b = R("ui.mode")
            case("UIM1-U2", {"after_phone": "Phone", "after_tablet": "Tablet"}, {"tap_phone": p2a, "mode_phone": m2a, "tap_tablet": p2b, "mode_tablet": m2b},
                 "PASS" if (m2a or {}).get("uiMode") == "Phone" and (m2b or {}).get("uiMode") == "Tablet" else "FAIL", [p2a.get("shot"), p2b.get("shot")])
            R("ui.mode", set="auto")
            time.sleep(1)
            ra.adb(sr, "shell", "wm", "size", FOLD)
            time.sleep(3)
            mf0 = R("ui.mode")
            pf_ = press("Phone", "F_tap_phone")
            mf1 = R("ui.mode")
            ra.adb(sr, "shell", "wm", "size", "reset")
            time.sleep(3)
            mf2 = R("ui.mode")
            shotf = cap("app_UIM1_F_unfolded")
            case("UIM1-F", {"after_tap": "uiMode Auto 유지", "after_unfold": "uiModeEffective Tablet"}, {"folded_start": mf0, "tap": pf_, "after_tap": mf1, "after_unfold": mf2},
                 "PASS" if (mf1 or {}).get("uiMode") == "Auto" and (mf2 or {}).get("uiMode") == "Auto" and (mf2 or {}).get("uiModeEffective") == "Tablet" and pf_.get("found") else
                 ("FAIL" if pf_.get("found") else "판정필요(접힘에서 폰 칸 미노출)"), [pf_.get("shot"), shotf], "접힘은 wm size 904x2316 대용(실제 힌지 접기 아님)")
            R("ui.mode", set="auto")
            G = grid_doc()

        # ---------- Q16-A 축약(01·02·04·08·11·12·13·16) ----------
        if "q16" in only:
            pl0 = R("panel.list")
            np0 = pane(pl0)
            ok01 = bool(np0) and np0.get("alwaysOnTop") is True and np0.get("locked") is True and str(np0.get("side")).lower() == "right" and np0.get("visible") is True and np0.get("title") == "알림"
            case("Q16-A-01", "NotificationPane alwaysOnTop·locked·side=Right·visible·title=알림", np0, "PASS" if ok01 else "FAIL")
            t2 = R("panel.tabs", id="NotificationPane")
            tabs2 = (t2 or {}).get("tabs") or []
            ok02 = (t2 or {}).get("active") == "Notifications" and len(tabs2) == 1 and tabs2[0].get("id") == "Notifications"
            case("Q16-A-02", {"active": "Notifications", "tabs": [{"id": "Notifications", "active": True}]}, t2, "PASS" if ok02 else "FAIL")
            s4 = R("split.do", target="LeftPane", op="Bottom", tool="Notifications")
            np4 = pane(R("panel.list"))
            case("Q16-A-04", {"status": "Rejected", "policy": False, "side": "Right 그대로"}, {"split": s4, "side": (np4 or {}).get("side")},
                 "PASS" if (s4 or {}).get("status") == "Rejected" and (s4 or {}).get("policy") is False and str((np4 or {}).get("side")).lower() == "right" else "FAIL")
            since8 = dev_time()
            w8_0 = (np0 or {}).get("bounds")
            d8a = R("panel.dock", id="NotificationPane", side="bottom")
            s8a = (pane(R("panel.list")) or {}).get("side")
            cap("app_Q16-A-08_bottom")
            d8b = R("panel.dock", id="NotificationPane", side="right")
            time.sleep(1)
            np8 = pane(R("panel.list"))
            n8 = split_lines(since8, "app_Q16-A-08_PW_split.log")
            strip = lambda x: {k: v for k, v in (x or {}).items() if k != "bounds"}  # noqa: E731
            ok8 = (d8a or {}).get("status") == "Docked" and str(s8a).lower() == "bottom" and (d8b or {}).get("status") == "Docked" and strip(np8) == strip(np0)
            case("Q16-A-08", {"bottom": "Docked·side=Bottom", "right": "Docked·01 과 같음"}, {"dock_bottom": d8a, "side_after_bottom": s8a, "dock_right": d8b, "pane_after": np8,
                 "width_obs": {"before": w8_0, "after": (np8 or {}).get("bounds")}, "split_log_lines": n8},
                 "PASS" if ok8 else "FAIL", ["app_Q16-A-08_bottom.png", "app_Q16-A-08_PW_split.log"], "폭 유지는 판정 밖 관찰(D-PW1). 분할선 끌기 없이 시작 폭 그대로 비교")
            ch = R("dock.chrome")
            jd("app_Q16-A-11_chrome.json", ch)
            rows = ch if isinstance(ch, list) else (ch or {}).get("rows") or (ch or {}).get("chromes") or []
            nrow = next((x for x in rows if "Notification" in json.dumps(x, ensure_ascii=False)), None)
            ad = (nrow or {}).get("activeDockable") or nrow or {}

            def g(k):
                return ad.get(k) if k in ad else (nrow or {}).get(k)
            ok11 = nrow is not None and g("canFloat") is False and g("canPin") is False and g("canClose") is False and g("canDockAsDocument") is False
            case("Q16-A-11", {"canFloat": False, "canPin": False, "canClose": False, "canDockAsDocument": False}, nrow, "PASS" if ok11 else "FAIL", ["app_Q16-A-11_chrome.json"])
            case("Q16-A-16", {"canDockAsDocument": False, "panel": "01 그대로"}, {"canDockAsDocument": g("canDockAsDocument"), "pane": pane(R("panel.list"))},
                 "PASS(메뉴 캡처 미실시)" if g("canDockAsDocument") is False else "FAIL", ["app_Q16-A-11_chrome.json"])

            def right_of(sd):
                ar = (sd or {}).get("areas")
                rr = ar.get("Right") if isinstance(ar, dict) else next((x for x in (ar or []) if isinstance(x, dict) and x.get("area") == "Right"), None)
                return rr or (sd or {}).get("Right")
            d12 = R("docs.open", kind="form", count=1)
            did12 = (d12[0] if isinstance(d12, list) and d12 else d12 or {}).get("id") if d12 else None
            s12 = R("split.do", target="NotificationPane", op="Bottom", doc=did12)
            sd12 = R("split.dump")
            jd("app_Q16-A-12_dump.json", sd12)
            right = right_of(sd12)
            props = [round(x.get("proportion") or 0, 3) for x in ((right or {}).get("panes") or [])]
            ok12 = (s12 or {}).get("status") == "Split" and (right or {}).get("wrapped") is True and (right or {}).get("paneCount") == 2 and all(abs(x - 0.5) <= 0.03 for x in props)
            case("Q16-A-12", {"status": "Split", "wrapped": True, "paneCount": 2, "비율": "0.5/0.5±0.03"}, {"doc": did12, "split": s12, "Right": right},
                 "PASS" if ok12 else "FAIL", ["app_Q16-A-12_dump.json"])
            rd = R("panel.returnDoc", doc=did12)
            sd13 = R("split.dump")
            jd("app_Q16-A-13_dump.json", sd13)
            right13 = right_of(sd13)
            t13 = R("panel.tabs", id="NotificationPane")
            np13 = pane(R("panel.list"))
            ok13 = (right13 or {}).get("wrapped") is False and (right13 or {}).get("paneCount") == 1 and not (sd13 or {}).get("emptyToolDocks") and not (sd13 or {}).get("emptySplitPanels") and t13 == t2 and strip(np13) == strip(np0)
            case("Q16-A-13", {"wrapped": False, "paneCount": 1, "empty": "없음", "tabs·list": "02·01 과 같음"}, {"returnDoc": rd, "Right": right13, "tabs": t13, "pane": np13,
                 "emptyToolDocks": (sd13 or {}).get("emptyToolDocks"), "emptySplitPanels": (sd13 or {}).get("emptySplitPanels")}, "PASS" if ok13 else "FAIL", ["app_Q16-A-13_dump.json"])
            R("docs.close", doc=did12)
            G = grid_doc()

        # ---------- U20-AU / U20-AF ----------
        if "u20" in only:
            for cid, fold in (("U20-AU", False), ("U20-AF", True)):
                ra.adb(sr, "shell", "wm", "size", FOLD if fold else "reset")
                time.sleep(3)
                outp = os.path.join(O, "app_%s_ui20_check.json" % cid)
                t0 = time.time()
                pr = subprocess.run([sys.executable, "-E", "-u", os.path.join(HERE, "ui20_check.py"), "--out", outp], env=dict(os.environ), capture_output=True, text=True, timeout=900)
                with open(os.path.join(O, "app_%s_ui20_check.log" % cid), "w", encoding="utf-8") as f:
                    f.write(pr.stdout + "\n--- stderr ---\n" + pr.stderr)
                shot = cap("app_%s_end" % cid)
                ll = R("layout.list")
                left = [x for x in json.dumps(ll, ensure_ascii=False).split('"') if x.startswith("ui20_check_")]
                exp = 2 if fold else 0
                reason = ""
                try:
                    reason = json.dumps(json.load(open(outp, encoding="utf-8")), ensure_ascii=False)
                except Exception:  # noqa: BLE001
                    pass
                ok = pr.returncode == exp and (not fold or "폰 모드" in reason or "폰 모드" in pr.stdout) and not left
                case(cid, {"rc": exp, "reason": "폰 모드" if fold else None, "layout_left": [], "wm": FOLD if fold else "reset"},
                     {"rc": pr.returncode, "sec": round(time.time() - t0, 1), "layout_left": left, "layout_list": ll, "tail": pr.stdout[-800:]},
                     "PASS" if ok else "FAIL", ["app_%s_ui20_check.json" % cid, "app_%s_ui20_check.log" % cid, shot], "접힘은 wm size 904x2316 대용" if fold else None)
            ra.adb(sr, "shell", "wm", "size", "reset")
            time.sleep(3)
            G = grid_doc()

        # ---------- DAD1-A (펼침) ----------
        if "dad1" in only:
            since = dev_time()
            pl0 = R("panel.list")
            jd("app_DAD1-A_panels_before.json", pl0)
            lw0 = (pane(pl0, "LeftPane") or {}).get("bounds")
            res = {}
            for k, did in (("1", "Explorer"), ("2", "Log"), ("3", "Notifications"), ("4", "NotificationPane.Tabs")):
                res[k] = {"resp": R("dock.asDocument", id=did), "err": last_err(), "panels": R("panel.list")}
                time.sleep(0.8)
            cap("app_DAD1-A_after_1to4")
            j5 = R("panel.join", id="LeftPane", tool="Explorer")
            time.sleep(0.8)
            res["5"] = {"rejoin_explorer": j5, "resp": R("dock.asDocument", id="LeftPane.Tabs"), "err": last_err()}
            time.sleep(0.8)
            j6a = R("panel.join", id="LeftPane", tool="Explorer")
            j6b = R("panel.join", id="BottomPane", tool="Log")
            time.sleep(1.2)
            pl6 = R("panel.list")
            jd("app_DAD1-A_panels_after.json", pl6)
            res["6"] = {"join_explorer": j6a, "join_log": j6b}
            nsplit = split_lines(since, "app_DAD1-A_PW_split.log")
            shot6 = cap("app_DAD1-A_restored")

            def st(x):
                return (x or {}).get("status")
            v = {
                "1": st(res["1"]["resp"]) == "Moved" and (res["1"]["resp"] or {}).get("toCenter") is True,
                "2": st(res["2"]["resp"]) == "Moved" and (res["2"]["resp"] or {}).get("toCenter") is True,
                "3": st(res["3"]["resp"]) == "Unchanged",
                "4": st(res["4"]["resp"]) == "Unchanged",
                "5": st(res["5"]["resp"]) == "Moved" and (res["5"]["resp"] or {}).get("toCenter") is True and (res["5"]["resp"] or {}).get("id") == "Explorer",
            }
            norm = lambda pl: [{k: x.get(k) for k in ("id", "side", "visible", "tabs", "title")} for x in (pl if isinstance(pl, list) else [])]  # noqa: E731
            v["6"] = norm(pl6) == norm(pl0)
            lw6 = (pane(pl6, "LeftPane") or {}).get("bounds")
            jd("app_DAD1-A_steps.json", res)
            case("DAD1-A", {"1": "Moved·toCenter·after=Documents", "2": "Moved·toCenter", "3": "Unchanged", "4": "Unchanged", "5": "LeftPane.Tabs → Explorer Moved", "6": "panel.list 시작과 같음",
                            "4b": "바깥 패널 Id 미시험(D-DAD2)"},
                 {"steps": {k: {"status": st(x.get("resp")), "resp": x.get("resp"), "err": x.get("err")} for k, x in res.items() if k != "6"}, "step_verdict": v,
                  "restore": res["6"], "leftpane_width_obs": {"before": lw0, "after": lw6}, "split_log_lines": nsplit},
                 "PASS" if all(v.values()) else "FAIL", ["app_DAD1-A_steps.json", "app_DAD1-A_panels_before.json", "app_DAD1-A_panels_after.json", "app_DAD1-A_after_1to4.png", shot6, "app_DAD1-A_PW_split.log"],
                 "4b(LeftPane·BottomPane 바깥 Id)는 D-DAD2 로 시험하지 않음. 폭 관찰은 판정 밖")

        summ["settings_after"] = R("settings.get")
        R("panel.dock", id="NotificationPane", side="right")
        R("lang.set", code="ko")
        R("ui.mode", set="auto")
        summ["mode_after"] = R("ui.mode")
    except Exception as e:  # noqa: BLE001
        summ["error"] = repr(e)
        print("ERROR", repr(e), flush=True)
    finally:
        save_summary()
        try:
            ra.adb(sr, "shell", "wm", "size", "reset")
        finally:
            ra.cleanup(sr, state)
            ra.adb(sr, "shell", "settings", "put", "global", "stay_on_while_plugged_in", "0")
        print("EXIT", flush=True)


if __name__ == "__main__":
    main()
