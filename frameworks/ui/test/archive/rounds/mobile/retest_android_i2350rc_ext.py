# i2350rc Fold5 추가 시험(트레이 무동작·메뉴 분할·라디오 묶음·더보기 재배치·창 크롬 P3/P4·레이아웃 관리/테마 편집기 화면·바인딩 오류)을 재는 스크립트(otest-m-5)
"""
사용: python3 -E -u tools/retest_android_i2350rc_ext.py --apk <i2350rc diag APK> [--only tray,menu,radio,more,chrome,screens,bind]
결과: logs/otest_m2/i2350rc_ext/ext_<항목>.json|png, ext_summary.json
원복: wm size reset(density 360 유지)·ui.mode Auto·앱 제거·forward 해제·stay_on 원값(ra.cleanup 포함). 설정 변경은 ui.mode 만(시험 후 Auto 로 되돌림).
"""
import argparse
import hashlib
import json
import os
import re
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import retest_android as ra  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, ".."))
O = os.path.join(ROOT, "logs", "otest_m2", "i2350rc_ext")  # --out 으로 바꿀 수 있다
FOLD = "904x2316"
TOP = ["shell.mnuFile", "shell.mnuWinMenu", "shell.mnuView", "shell.mnuWindow", "shell.mnuSys"]


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--apk", required=True)
    p.add_argument("--serial", default="R3CW70202FP")
    p.add_argument("--only", default="tray,menu,radio,more,chrome,screens,bind")
    p.add_argument("--expect-md5", default="d8274b16d1828ba7b3718e3e0e9575af")
    p.add_argument("--out", default=None)
    a = p.parse_args()
    global O
    if a.out:
        O = os.path.join(ROOT, a.out)
    only = set(a.only.split(","))
    os.makedirs(O, exist_ok=True)
    sr = a.serial
    apk_md5 = hashlib.md5(open(a.apk, "rb").read()).hexdigest()
    summ = {"apk": a.apk, "apk_md5": apk_md5, "started": time.strftime("%Y-%m-%d %H:%M:%S"), "items": []}
    if apk_md5 != a.expect_md5:
        print("MD5_MISMATCH", apk_md5)
        sys.exit(4)
    state = {}

    def sv():
        json.dump(summ, open(os.path.join(O, "ext_summary.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=1)

    def jd(name, obj):
        json.dump(obj, open(os.path.join(O, "ext_%s.json" % name), "w", encoding="utf-8"), ensure_ascii=False, indent=1)

    def cap(name):
        rc, data = ra.adb(sr, "exec-out", "screencap", "-p", binary=True)
        k = data.find(b"\x89PNG") if rc == 0 else -1
        if k >= 0:
            open(os.path.join(O, "ext_%s.png" % name), "wb").write(data[k:])
            return "ext_%s.png" % name
        return None

    def item(cid, expect, actual, verdict, note=None, files=None):
        c = {"id": cid, "at": time.strftime("%Y-%m-%d %H:%M:%S"), "apk_md5": apk_md5, "expect": expect, "actual": actual, "verdict": verdict, "note": note, "files": [f for f in (files or []) if f]}
        jd(cid, c)
        summ["items"].append({"id": cid, "verdict": verdict, "note": note})
        sv()
        print("ITEM", cid, verdict, json.dumps(actual, ensure_ascii=False)[:300], flush=True)

    code = 0
    try:
        ra.adb(sr, "shell", "wm", "size", "reset")
        info = ra.prepare(sr, a.apk, state)
        if not info:
            raise RuntimeError("앱 연결 실패")
        from retest_remote import Remote
        time.sleep(4)
        r = Remote()

        def R(cmd, **k):
            try:
                return r.call(cmd, _timeout=120, **k)
            except SystemExit:
                return {"_timeout": True}

        def err():
            return (r.calls[-1] if r.calls else {}).get("error")
        ra.adb(sr, "logcat", "-c")
        R("lang.set", code="ko")
        R("ui.mode", set="auto")
        summ["mode_start"] = R("ui.mode")
        summ["settings_start"] = R("settings.get")
        sv()

        def flat(ms):
            if isinstance(ms, dict):
                ms = ms.get("items")
            return [x for x in (ms or []) if isinstance(x, dict) and x.get("separator") is None] if isinstance(ms, list) else []

        if "probe" in only:
            jd("probe_invoke_list", {"invoke": R("ui.invoke"), "tree_menu": R("tree.get", named=True, depth=12)})

        if "menuopen" in only:
            out = {}
            for mid in ("shell.mnuFile", "shell.mnuView", "shell.mnuSys"):
                before = flat(R("menu.list", id=mid))
                res = R("ui.click", id=mid)
                e = err()
                time.sleep(1.5)
                after = flat(R("menu.list", id=mid))
                shot = cap("menuopen_" + mid.split(".")[-1])
                out[mid] = {"items_before_open": len(before), "click_error": e, "items_after_open": len(after), "sample_after": [x.get("id") for x in after][:6], "shot": shot}
                R("ui.click", id=mid)
                time.sleep(1)
            item("menu_open", {"메뉴 열기": "열면 하위 항목이 보이고 menu.list 에도 잡힘"}, out, "캡처 판독 필요", None, [v["shot"] for v in out.values()])

        if "tray" in only:
            ts = R("tray.state")
            e1 = err()
            ml = flat(R("menu.list", id="shell.mnuWinMenu")) + flat(R("menu.list", id="shell.mnuWindow"))
            tray_item = [x for x in ml if x.get("id") == "shell.mnuTray"]
            ts_d = ts if isinstance(ts, dict) else {}
            ok = isinstance(ts, dict) and ts_d.get("implPresent") in (False, None) and not ts_d.get("visible") and not ts_d.get("declared") and not e1  # Android 는 implPresent=null(미선언)도 무동작으로 본다
            ok_menu = (not tray_item) or all(not x.get("visible") for x in tray_item)
            item("tray_noop", {"tray.state": "implPresent=false, visible=false, 예외 없음", "shell.mnuTray": "visible=false"},
                 {"state": ts_d, "error": e1, "mnuTray": tray_item}, "PASS" if (ok and ok_menu) else "FAIL")

        ids_found = {}
        if "menu" in only:
            for t in TOP:
                res = R("menu.list", id=t)
                lst = flat(res)
                ids_found[t] = [x.get("id") for x in lst]
                jd("menu_%s" % t.split(".")[-1], {"id": t, "error": err(), "items": lst})
            base = [ln.strip() for ln in open(os.path.join(ROOT, "logs", "odev8n", "maintitlebar_autoid_before.txt"), encoding="utf-8") if ln.strip().startswith("shell.mnu")]
            allids = set(i for v in ids_found.values() for i in v if i) | set(TOP) | {"shell.mnuMore"}
            missing = sorted(set(base) - allids)
            item("menu5_ids", {"5개 상위 메뉴": "분할 전 AutomationId(shell.mnu*) 전부 존재(menu.list 순회)"},
                 {"counts": {k: len(v) for k, v in ids_found.items()}, "baseline_mnu": len(base), "found_union": len(allids), "missing_from_baseline": missing},
                 "PASS" if not missing else "확인 필요(누락 %d — 더보기·팝업 전용 항목일 수 있음)" % len(missing))

        if "radio" in only:
            def grp(gid):
                return [{"id": x.get("id"), "checked": x.get("checked"), "visible": x.get("visible"), "enabled": x.get("enabled")} for x in flat(R("menu.list", id=gid)) if x.get("id") != gid]
            g_ui, g_gpu, g_log = grp("shell.mnuUiMode"), grp("shell.mnuSysGpu"), grp("shell.mnuSysLogLevel")
            n1 = lambda g: sum(1 for x in g if x.get("checked") is True)
            # 화면 모드 묶음 상호배타: Phone 을 켜면 Auto 가 꺼지는지, 이어서 Auto 로 원복
            R("ui.invoke", command="UiModeSwitchCommand", param="Phone")
            time.sleep(1.5)
            g_ui2 = grp("shell.mnuUiMode")
            mode_phone = R("ui.mode")
            R("ui.invoke", command="UiModeSwitchCommand", param="Auto")
            time.sleep(1.5)
            g_ui3 = grp("shell.mnuUiMode")
            R("ui.mode", set="auto")
            ok = n1(g_ui) == 1 and n1(g_ui2) == 1 and n1(g_ui3) == 1 and n1(g_gpu) <= 1 and n1(g_log) <= 1
            item("radio_groups", {"pdUiMode·pdSysGpu·pdSysLog": "묶음마다 체크 1개 이하, UiMode 는 Phone 선택 시 Auto 해제(배타)"},
                 {"uiMode_start": g_ui, "uiMode_phone": g_ui2, "mode_phone": mode_phone, "uiMode_auto_back": g_ui3, "gpu": g_gpu, "logLevel": g_log, "checked_counts": {"ui": n1(g_ui), "ui_phone": n1(g_ui2), "ui_back": n1(g_ui3), "gpu": n1(g_gpu), "log": n1(g_log)}},
                 "PASS" if ok else "FAIL", "GPU·로그 수준은 영속 설정이라 클릭하지 않고 읽기만(배타 조작 미실시)")

        if "more" in only:
            def more_state(tag):
                f = {}
                for t in TOP + ["shell.mnuMore"]:
                    f[t] = R("ui.find", id=t)
                    if not isinstance(f[t], dict):
                        f[t] = None
                ml = flat(R("menu.list", id="shell.mnuMore"))
                return {"tag": tag, "bounds": {k: (v or {}).get("bounds") for k, v in f.items()}, "visible": {k: (v or {}).get("visible") for k, v in f.items()},
                        "more_items": [x.get("id") for x in ml], "view_width": (R("ui.mode") or {}).get("viewWidth"), "shot": cap("more_" + tag)}
            wide = more_state("wide")
            ra.adb(sr, "shell", "wm", "size", FOLD)
            time.sleep(4)
            narrow = more_state("narrow")
            ra.adb(sr, "shell", "wm", "size", "reset")
            time.sleep(4)
            back = more_state("restored")
            vis_n = narrow["visible"]
            moved = [t for t in TOP if vis_n.get(t) is False or vis_n.get(t) is None]
            item("more_reflow", {"좁은 폭": "상위 메뉴 일부가 shell.mnuMore 하위로 이동(shell.mnuMore visible, 항목 있음), 넓힌 뒤 원복"},
                 {"wide": wide, "narrow": narrow, "restored": back, "moved_to_more": moved},
                 "PASS" if (vis_n.get("shell.mnuMore") and narrow["more_items"] and not back["more_items"] or (back["visible"].get("shell.mnuMore") in (False, None))) and moved else "확인 필요(캡처 판독)",
                 "wm size 904x2316 으로 폭만 줄임(접힘 대용, 실제 접힘 아님)", [wide["shot"], narrow["shot"], back["shot"]])

        if "chrome" in only:
            R("docs.close", all=True)
            d1 = (R("docs.open", kind="grid") or [{}])[0].get("id") if isinstance(R("docs.list"), list) else None
            ds = R("docs.list") or []
            d1 = d1 or (ds[0].get("id") if ds else None)
            R("docs.open", kind="form")
            time.sleep(2)
            ds = R("docs.list") or []
            tune = R("chrome.tune")
            def caps(doc):
                o = {}
                for k in ("chrome.btnMin", "chrome.btnMax", "chrome.btnClose", "chrome.logo"):
                    v = R("ui.find", id=k, doc=doc)
                    o[k] = {"bounds": (v or {}).get("bounds"), "visible": (v or {}).get("visible")} if isinstance(v, dict) else None
                return o
            c_tab = caps(d1)
            shot_tab = cap("chrome_P3_tablet_unfolded")
            R("ui.mode", set="phone")
            time.sleep(2)
            c_phone = caps(d1)
            mode_p = R("ui.mode")
            shot_ph = cap("chrome_P_phonemode_unfolded")
            ra.adb(sr, "shell", "wm", "size", FOLD)
            time.sleep(4)
            R("ui.mode", set="auto")
            time.sleep(2)
            c_fold = caps(d1)
            mode_f = R("ui.mode")
            shot_fold = cap("chrome_P2_folded")
            ra.adb(sr, "shell", "wm", "size", "reset")
            time.sleep(3)
            R("ui.mode", set="auto")
            def sz(c, k):
                b = ((c or {}).get(k) or {}).get("bounds") or {}
                return (b.get("w"), b.get("h"))
            vis = lambda c, k: bool(((c or {}).get(k) or {}).get("visible"))
            ok_tab = vis(c_tab, "chrome.btnMin") and vis(c_tab, "chrome.btnMax") and vis(c_tab, "chrome.btnClose")
            ok_ph = (not vis(c_phone, "chrome.btnMin")) and (not vis(c_phone, "chrome.btnMax"))
            ok_fd = (not vis(c_fold, "chrome.btnMin")) and (not vis(c_fold, "chrome.btnMax")) and vis(c_fold, "chrome.btnClose")
            item("chrome_P3_P4", {"P3 펼침(태블릿)": "Windows 스타일 캡션 최소·최대·닫기 보임, 46x32", "폰모드/접힘": "최소·최대 없음, 닫기만"},
                 {"tune": tune, "tablet_unfolded": c_tab, "tablet_sizes": {k: sz(c_tab, k) for k in c_tab}, "phone_mode_unfolded": c_phone, "mode_phone": mode_p, "folded_auto": c_fold, "mode_folded": mode_f},
                 "PASS" if (ok_tab and ok_ph and ok_fd) else "확인 필요(캡처 판독)", "접힘은 wm size 904x2316 대용", [shot_tab, shot_ph, shot_fold])

        if "screens" in only:
            R("docs.close", all=True)
            out = {}
            for nm, cmd in (("layout_manager", "ManageLayoutsCommand"), ("settings", "ToggleSettingsCommand")):
                res = R("ui.invoke", command=cmd)
                e = err()
                time.sleep(2.5)
                out[nm] = {"cmd": cmd, "resp": res if isinstance(res, (dict, list)) else str(res)[:200], "error": e, "shot": cap("screen_" + nm)}
                R("ui.invoke", command=cmd) if nm == "settings" else None  # 설정 패널은 같은 명령으로 닫는다
                time.sleep(1.5)
            te = R("theme.openEditor")
            time.sleep(2)
            out["theme_editor_remote"] = {"resp": te if isinstance(te, dict) else str(te)[:200], "error": err(), "shot": cap("screen_theme_editor_remote")}
            item("screens_S4", {"레이아웃 관리·테마 편집기·설정": "열리고 바인딩 오류 없이 내용이 표시됨(캡처 판독)"}, out, "캡처 판독 필요", None, [v.get("shot") for v in out.values() if isinstance(v, dict)])

        if "bind" in only:
            rc, lg = ra.adb(sr, "logcat", "-d")
            lines = [ln for ln in lg.splitlines() if re.search(r"binding", ln, re.I) and re.search(r"error|fail|exception|warn", ln, re.I)]
            open(os.path.join(O, "ext_bind_lines.log"), "w", encoding="utf-8").write("\n".join(lines) + "\n")
            sg = R("sys.get", tail=500)
            blines = [json.dumps(x, ensure_ascii=False) for x in ((sg or {}).get("logTail") or [])] if isinstance(sg, dict) else []
            bl2 = [x for x in blines if re.search(r"binding", x, re.I)]
            summ["applog_total"] = (sg or {}).get("logTotal") if isinstance(sg, dict) else None
            item("binding_errors", {"앱 로그": "Binding 오류 0건"}, {"logcat_binding_error_lines": len(lines), "applog_binding_lines": len(bl2), "sample": (lines + bl2)[:5], "logcat_total_lines": len(lg.splitlines())},
                 "PASS" if not lines and not bl2 else "FAIL", "logcat -c 후 시험 전 구간(메뉴·화면 전환 포함) 수집")
    except Exception as e:  # noqa: BLE001
        summ["error"] = repr(e)
        print("ERROR", repr(e), flush=True)
        code = 2
    finally:
        try:
            if "r" in dir():
                pass
        except Exception:
            pass
        ra.adb(sr, "shell", "wm", "size", "reset")
        sv()
        ra.cleanup(sr, state)
        print("EXIT", code, flush=True)
        sys.exit(code)


if __name__ == "__main__":
    main()
