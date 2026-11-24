# i1720rc Fold5 앱 시험(odev-1r i1720rc_test_plan.md d066771f): UX1-A·Q16-A 01~17·U20-AU/AF(ui20_check)를 원격 명령으로 재고 케이스별 응답 원문과 기대·판정을 남기는 스크립트(otest-m-2)
"""
사용: python3 -E -u tools/retest_android_i1720rc.py --apk <i1720rc diag APK> [--only ux1,q16,q16r,u20]
결과: logs/otest_m2/i1720rc/app_<케이스>.json|png, app_U20-A{U,F}_ui20_check.json, app_i1720rc_summary.json
원복: 패널 side=right·언어 ko·화면 모드 auto 를 되돌린 뒤 앱 제거·forward 해제·stay_on 0·wm size reset(density 360 유지).
"""
import argparse
import hashlib
import json
import os
import subprocess
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import retest_android as ra  # noqa: E402
import retest_android_cond2 as c2  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, ".."))
O = os.path.join(ROOT, "logs", "otest_m2", "i1720rc")
FOLD = "904x2316"


def md5(p):
    return hashlib.md5(open(p, "rb").read()).hexdigest()


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--apk", required=True)
    p.add_argument("--serial", default="R3CW70202FP")
    p.add_argument("--only", default="ux1,q16,q16r,u20")
    a = p.parse_args()
    only = set(a.only.split(","))
    os.makedirs(O, exist_ok=True)
    sr = a.serial
    tools_md5 = {"ui20_check.py": md5(os.path.join(HERE, "ui20_check.py")), "ui20_lib.py": md5(os.path.join(HERE, "ui20_lib.py"))}
    summ = {"build": "i1720rc", "apk": a.apk, "apk_md5": md5(a.apk), "tools_md5": tools_md5, "started": time.strftime("%Y-%m-%d %H:%M:%S"), "cases": []}
    state = {}

    def save_summary():
        json.dump(summ, open(os.path.join(O, "app_i1720rc_summary.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=1)

    def jd(name, obj):
        json.dump(obj, open(os.path.join(O, name), "w", encoding="utf-8"), ensure_ascii=False, indent=1)

    def cap(name):
        rc, data = ra.adb(sr, "exec-out", "screencap", "-p", binary=True)
        k = data.find(b"\x89PNG") if rc == 0 else -1
        if k >= 0:
            open(os.path.join(O, name + ".png"), "wb").write(data[k:])
            return name + ".png"
        return None

    def case(cid, expect, actual, verdict, files=None, note=None):
        c = {"id": cid, "build": "i1720rc", "at": time.strftime("%Y-%m-%d %H:%M:%S"), "tools_md5": tools_md5, "expect": expect, "actual": actual,
             "verdict": verdict, "files": files or [], "note": note}
        jd("app_%s.json" % cid, c)
        summ["cases"].append({k: c[k] for k in ("id", "verdict", "note")})
        save_summary()
        print("CASE", cid, verdict, (note or "")[:120], flush=True)

    try:
        ra.adb(sr, "shell", "wm", "size", "reset")
        info = ra.prepare(sr, a.apk, state)
        if not info:
            raise RuntimeError("앱 연결 실패")
        from retest_remote import Remote
        time.sleep(4)
        r = Remote()
        R = lambda cmd, **k: r.call(cmd, _timeout=120, **k)  # noqa: E731
        R("settings.set", theme="System", baseFontSize=14)
        R("ui.mode", set="auto")
        R("lang.set", code="ko")
        summ["settings_before"] = R("settings.get")
        summ["mode"] = R("ui.mode")
        save_summary()

        def pane(pl, pid="NotificationPane"):
            items = pl if isinstance(pl, list) else (pl or {}).get("panels") or []
            return next((x for x in items if isinstance(x, dict) and x.get("id") == pid), None)

        # ---------- UX1-A ----------
        if "ux1" in only:
            sd = R("split.dump")
            jd("app_UX1-A_dump.json", sd)
            lt = R("log.tail", n=200)
            jd("app_UX1-A_logtail.json", lt)
            tff = (sd or {}).get("touchFormFactor")
            plat = (sd or {}).get("platform")
            lines = lt if isinstance(lt, list) else ((lt or {}).get("lines") if isinstance(lt, dict) else None)
            host = [x for x in (lines or []) if "[host] form-factor" in json.dumps(x, ensure_ascii=False)]
            case("UX1-A", {"touchFormFactor": True, "platform": "android", "host_log": "없음(플랫폼 판정)"},
                 {"touchFormFactor": tff, "platform": plat, "compact": (sd or {}).get("compact"), "host_log_lines": host},
                 "PASS" if tff is True and plat == "android" and not host else "FAIL", ["app_UX1-A_dump.json", "app_UX1-A_logtail.json"])

        # ---------- Q16-A ----------
        if "q16" in only:
            pl0 = R("panel.list")
            np0 = pane(pl0)
            ok01 = bool(np0) and np0.get("alwaysOnTop") is True and np0.get("locked") is True and str(np0.get("side")).lower() == "right" and np0.get("visible") is True and np0.get("title") == "알림"
            case("Q16-A-01", "NotificationPane alwaysOnTop·locked·side=Right·visible·title=알림", np0, "PASS" if ok01 else "FAIL", note=json.dumps(pl0, ensure_ascii=False)[:300])
            t2 = R("panel.tabs", id="NotificationPane")
            tabs2 = (t2 or {}).get("tabs") or []
            ok02 = (t2 or {}).get("active") == "Notifications" and len(tabs2) == 1 and tabs2[0].get("id") == "Notifications"
            case("Q16-A-02", {"active": "Notifications", "tabs": [{"id": "Notifications", "active": True}]}, t2, "PASS" if ok02 else "FAIL")
            t3 = R("panel.tabs", id="NotificationPane.Tabs")
            case("Q16-A-03", "02 와 같은 결과", t3, "PASS" if t3 == t2 and t3 is not None else "FAIL")
            s4 = R("split.do", target="LeftPane", op="Bottom", tool="Notifications")
            np4 = pane(R("panel.list"))
            case("Q16-A-04", {"status": "Rejected", "policy": False, "side": "Right 그대로"}, {"split": s4, "side": (np4 or {}).get("side")},
                 "PASS" if (s4 or {}).get("status") == "Rejected" and (s4 or {}).get("policy") is False and str((np4 or {}).get("side")).lower() == "right" else "FAIL")
            d5 = R("docs.open", kind="form", count=1)
            did = (d5[0] if isinstance(d5, list) and d5 else d5 or {}).get("id") if d5 else None
            j5 = R("panel.join", id="NotificationPane", doc=did)
            t5 = R("panel.tabs", id="NotificationPane")
            case("Q16-A-05", {"status": "Rejected", "tabs": "02 그대로"}, {"doc": did, "join": j5, "tabs": t5}, "PASS" if (j5 or {}).get("status") == "Rejected" and t5 == t2 else "FAIL")
            j6 = R("panel.join", id="NotificationPane", tool="Log")
            tb6 = R("panel.tabs", id="BottomPane")
            ok6 = (j6 or {}).get("status") == "Rejected" and any(x.get("id") == "Log" for x in ((tb6 or {}).get("tabs") or []))
            case("Q16-A-06", {"status": "Rejected", "Log": "BottomPane 그대로"}, {"join": j6, "bottom_tabs": tb6}, "PASS" if ok6 else "FAIL")
            d7 = R("panel.dock", id="NotificationPane", side="center")
            case("Q16-A-07", {"status": "Rejected"}, d7, "PASS" if (d7 or {}).get("status") == "Rejected" else "FAIL")
            d8a = R("panel.dock", id="NotificationPane", side="bottom")
            s8a = (pane(R("panel.list")) or {}).get("side")
            cap("app_Q16-A-08_bottom")
            d8b = R("panel.dock", id="NotificationPane", side="right")
            np8 = pane(R("panel.list"))
            ok8 = (d8a or {}).get("status") == "Docked" and str(s8a).lower() == "bottom" and (d8b or {}).get("status") == "Docked" and np8 == np0
            case("Q16-A-08", {"bottom": "Docked·side=Bottom", "right": "Docked·01 과 같음"}, {"dock_bottom": d8a, "side_after_bottom": s8a, "dock_right": d8b, "pane_after": np8},
                 "PASS" if ok8 else "FAIL", ["app_Q16-A-08_bottom.png"])
            # 09 탭 줄 숨김
            tree = R("tree.get", named=False, depth=80) or {}
            txt = json.dumps(tree, ensure_ascii=False)
            jd("app_Q16-A-09_tree.json", tree)

            def walk(n, path, out):
                if isinstance(n, dict):
                    out.append((n, path))
                    for ch in n.get("children") or n.get("tree") or []:
                        walk(ch, path + [n], out)
                elif isinstance(n, list):
                    for ch in n:
                        walk(ch, path, out)
                return out
            nodes = walk(tree, [], [])
            strips = []
            for n, path in nodes:
                if n.get("id") == "notify.root":
                    for anc in reversed(path):
                        if anc.get("type") in ("DocumentControl", "ToolControl"):
                            for m, _ in walk(anc, [], []):
                                if m.get("name") == "PART_TabStrip" or m.get("type") in ("DocumentTabStrip", "ToolTabStrip"):
                                    strips.append({"type": m.get("type"), "name": m.get("name"), "isVisible": m.get("isVisible"), "visible": m.get("visible"), "bounds": m.get("bounds")})
                            strips.append({"container": anc.get("type")})
                            break
            shot9 = cap("app_Q16-A-09")
            vis = [s for s in strips if s.get("isVisible") or s.get("visible")]
            case("Q16-A-09", "notify.root 를 품은 컨트롤의 탭 줄이 보이지 않음 + 캡처 확인", {"strips": strips, "tree_has_notify_menu": "notify.menu" in txt},
                 "PASS(캡처 확인 필요)" if strips and not vis else ("FAIL" if vis else "판정필요"), [shot9, "app_Q16-A-09_tree.json"])
            # 10 언어
            R("lang.set", code="en")
            time.sleep(1.5)
            pl10e = R("panel.list")
            cap("app_Q16-A-10_en")
            R("lang.set", code="ko")
            time.sleep(1.5)
            pl10k = R("panel.list")
            cap("app_Q16-A-10_ko")
            te, tk = (pane(pl10e) or {}).get("title"), (pane(pl10k) or {}).get("title")
            others = [((pane(pl10e, i) or {}).get("title"), (pane(pl10k, i) or {}).get("title")) for i in ("LeftPane", "BottomPane")]
            case("Q16-A-10", {"en": "Notifications", "ko": "알림", "others_change": True}, {"en": te, "ko": tk, "others(en,ko)": others},
                 "PASS" if te == "Notifications" and tk == "알림" and all(x[0] and x[0] != x[1] for x in others) else "FAIL", ["app_Q16-A-10_en.png", "app_Q16-A-10_ko.png"])
            # 11·16 dock.chrome
            ch = R("dock.chrome")
            jd("app_Q16-A-11_chrome.json", ch)
            rows = ch if isinstance(ch, list) else (ch or {}).get("rows") or (ch or {}).get("chromes") or []
            nrow = next((x for x in rows if "Notification" in json.dumps(x, ensure_ascii=False)), None)
            ad = (nrow or {}).get("activeDockable") or nrow or {}

            def g(k):
                return ad.get(k) if k in ad else (nrow or {}).get(k)
            ok11 = nrow is not None and g("canFloat") is False and g("canPin") is False and g("canClose") is False and g("canDockAsDocument") is False
            case("Q16-A-11", {"canFloat": False, "canPin": False, "canClose": False, "canDockAsDocument": False, "ActiveDockable": "NotificationTabsDock·NotificationPane.Tabs"},
                 nrow, "PASS" if ok11 else "FAIL", ["app_Q16-A-11_chrome.json"])
            case("Q16-A-16", {"canDockAsDocument": False, "panel": "01 그대로"}, {"canDockAsDocument": g("canDockAsDocument"), "pane": pane(R("panel.list"))},
                 "PASS(메뉴 캡처 미실시)" if g("canDockAsDocument") is False else "FAIL", ["app_Q16-A-11_chrome.json"], "▾ 메뉴 열기 캡처는 원격으로 메뉴를 여는 경로가 없어 미실시")
            # 12~14 문서 옆 분할
            d12 = R("docs.open", kind="form", count=1)
            did12 = (d12[0] if isinstance(d12, list) and d12 else d12 or {}).get("id") if d12 else None
            s12 = R("split.do", target="NotificationPane", op="Bottom", doc=did12)
            sd12 = R("split.dump")
            jd("app_Q16-A-12_dump.json", sd12)
            right = ((sd12 or {}).get("areas") or {}).get("Right") if isinstance((sd12 or {}).get("areas"), dict) else next((x for x in ((sd12 or {}).get("areas") or []) if isinstance(x, dict) and x.get("area") == "Right"), None)
            right = right or (sd12 or {}).get("Right")
            panes = (right or {}).get("panes") or []
            props = [round(x.get("proportion") or 0, 3) for x in panes]
            ok12 = (s12 or {}).get("status") == "Split" and (right or {}).get("wrapped") is True and (right or {}).get("paneCount") == 2 and all(abs(x - 0.5) <= 0.03 for x in props)
            case("Q16-A-12", {"status": "Split", "wrapped": True, "paneCount": 2, "비율": "0.5/0.5±0.03"}, {"doc": did12, "split": s12, "Right": right},
                 "PASS" if ok12 else "FAIL", ["app_Q16-A-12_dump.json"])
            s14 = R("split.do", target="NotificationPane", op="Left", doc=did12)
            rd = R("panel.returnDoc", doc=did12)
            sd13 = R("split.dump")
            jd("app_Q16-A-13_dump.json", sd13)
            right13 = ((sd13 or {}).get("areas") or {}).get("Right") if isinstance((sd13 or {}).get("areas"), dict) else next((x for x in ((sd13 or {}).get("areas") or []) if isinstance(x, dict) and x.get("area") == "Right"), None)
            right13 = right13 or (sd13 or {}).get("Right")
            t13 = R("panel.tabs", id="NotificationPane")
            np13 = pane(R("panel.list"))
            ok13 = (right13 or {}).get("wrapped") is False and (right13 or {}).get("paneCount") == 1 and not (sd13 or {}).get("emptyToolDocks") and not (sd13 or {}).get("emptySplitPanels") and t13 == t2 and np13 == np0
            case("Q16-A-13", {"wrapped": False, "paneCount": 1, "empty": "없음", "tabs·list": "02·01 과 같음"}, {"returnDoc": rd, "Right": right13, "tabs": t13, "pane": np13,
                 "emptyToolDocks": (sd13 or {}).get("emptyToolDocks"), "emptySplitPanels": (sd13 or {}).get("emptySplitPanels")}, "PASS" if ok13 else "FAIL", ["app_Q16-A-13_dump.json"])
            case("Q16-A-14", {"status": "Rejected", "Right paneCount": "1(분할 상태에서 시도했으므로 2 유지)"}, {"split_left": s14},
                 "PASS" if (s14 or {}).get("status") == "Rejected" else "FAIL", note="계획서는 알림 단독(오른쪽)일 때 시도. 이 스크립트는 12 분할 상태에서 시도했다")
            # 14 를 계획서 조건(분할 전, 알림 단독)으로 한 번 더
            s14b = R("split.do", target="NotificationPane", op="Left", doc=did12)
            sd14 = R("split.dump")
            r14 = ((sd14 or {}).get("areas") or {}).get("Right") if isinstance((sd14 or {}).get("areas"), dict) else next((x for x in ((sd14 or {}).get("areas") or []) if isinstance(x, dict) and x.get("area") == "Right"), None)
            r14 = r14 or (sd14 or {}).get("Right")
            case("Q16-A-14b", {"status": "Rejected", "Right paneCount": 1}, {"split_left": s14b, "Right_paneCount": (r14 or {}).get("paneCount")},
                 "PASS" if (s14b or {}).get("status") == "Rejected" and (r14 or {}).get("paneCount") == 1 else "FAIL")
            if (s14b or {}).get("status") != "Rejected":
                R("panel.returnDoc", doc=did12)
            # 17 탐색기 문서로 도킹(D-DAD1)
            inv = R("ui.invoke")
            jd("app_Q16-A-17_invoke_list.json", inv)
            cand = [x for x in json.dumps(inv, ensure_ascii=False).replace('"', ' ').split() if "Document" in x and "Dock" in x]
            case("Q16-A-17", "Explorer 가 Documents 탭이 됨(LeftPane 접힘), 원복 panel.join", {"invoke_candidates": cand[:10]}, "미실시",
                 ["app_Q16-A-17_invoke_list.json"], "원격으로 ▾ '문서로 도킹'을 실행할 명령을 찾지 못함(ui.invoke 목록 첨부). 후속에서 adb 탭 경로 검토")
            R("docs.close", all=True)

        # ---------- Q16-A-15 재기동 유지 ----------
        if "q16r" in only:
            d15 = R("panel.dock", id="NotificationPane", side="bottom")
            R("layout.save")
            time.sleep(2)
            info2 = c2.launch(sr, "", state["info_path"])
            time.sleep(5)
            r = Remote()
            R = lambda cmd, **k: r.call(cmd, _timeout=120, **k)  # noqa: E731
            np15 = pane(R("panel.list"))
            back = R("panel.dock", id="NotificationPane", side="right")
            np15b = pane(R("panel.list"))
            case("Q16-A-15", {"after_restart_side": "Bottom", "restore": "Right"}, {"dock_bottom": d15, "relaunch": bool(info2), "side_after_restart": (np15 or {}).get("side"),
                 "restore": back, "side_after_restore": (np15b or {}).get("side")},
                 "PASS" if str((np15 or {}).get("side")).lower() == "bottom" and str((np15b or {}).get("side")).lower() == "right" else "FAIL",
                 note="저장은 시험 앱의 사용자 경로(시험 기기·시험 설치본, 끝에 제거)")
            R("layout.save")

        # ---------- U20-AU / U20-AF ----------
        if "u20" in only:
            for cid, fold in (("U20-AU", False), ("U20-AF", True)):
                ra.adb(sr, "shell", "wm", "size", FOLD if fold else "reset")
                time.sleep(3)
                outp = os.path.join(O, "app_%s_ui20_check.json" % cid)
                t0 = time.time()
                pr = subprocess.run([sys.executable, "-E", "-u", os.path.join(HERE, "ui20_check.py"), "--out", outp], env=dict(os.environ), capture_output=True, text=True, timeout=900)
                open(os.path.join(O, "app_%s_ui20_check.log" % cid), "w", encoding="utf-8").write(pr.stdout + "\n--- stderr ---\n" + pr.stderr)
                shot = cap("app_%s_end" % cid)
                exp = 2 if fold else 0
                reason = ""
                try:
                    reason = json.dumps(json.load(open(outp, encoding="utf-8")), ensure_ascii=False)
                except Exception:  # noqa: BLE001
                    pass
                ok = pr.returncode == exp and (not fold or "폰 모드" in reason or "폰 모드" in pr.stdout)
                case(cid, {"rc": exp, "reason": "폰 모드" if fold else None, "wm": FOLD if fold else "reset"}, {"rc": pr.returncode, "sec": round(time.time() - t0, 1), "tail": pr.stdout[-800:]},
                     "PASS" if ok else "FAIL", ["app_%s_ui20_check.json" % cid, "app_%s_ui20_check.log" % cid, shot], "접힘은 wm size 904x2316 대용(실제 힌지 접기 아님)" if fold else None)
            ra.adb(sr, "shell", "wm", "size", "reset")
            time.sleep(2)
        summ["settings_after"] = R("settings.get")
        R("panel.dock", id="NotificationPane", side="right")
        R("lang.set", code="ko")
        R("ui.mode", set="auto")
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
