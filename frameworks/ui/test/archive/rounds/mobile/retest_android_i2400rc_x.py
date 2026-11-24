# i2400rc Fold5 MainTitleBar 보강 시험 — AutomationId 110개 전체 대조(tree.get+menu.list 합집합)·mnuNextTheme Click·GPU/로그 라디오 배타·메뉴 열린 캡처 (otest-m-6)
"""
사용: python3 -E -u tools/retest_android_i2400rc_x.py --apk <i2400rc diag APK> [--only autoid,theme,radio,menuopen] [--out logs/otest_m2/i2400rc_x]
원복: 테마(theme.select 원값)·GPU·로그 수준(원래 항목 재클릭)·ui.mode auto·wm size reset(density 360 유지)·앱 제거·forward 해제·stay_on 원값. 원복 결과는 x_restore.json 에 기록.
"""
import argparse
import hashlib
import json
import os
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import retest_android as ra  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, ".."))
BASE = os.path.join(ROOT, "logs", "odev8n", "maintitlebar_autoid_before.txt")
TOP = ["shell.mnuFile", "shell.mnuWinMenu", "shell.mnuView", "shell.mnuWindow", "shell.mnuSys", "shell.mnuMore"]
FOLD = "904x2316"


def collect_ids(node, out):
    """tree.get 결과에서 id/automationId 성격의 문자열을 모두 모은다(키 이름은 실측 후 확정)."""
    if isinstance(node, dict):
        for k, v in node.items():
            if k in ("id", "automationId", "AutomationId", "name") and isinstance(v, str) and v.startswith("shell."):
                out.add(v)
            collect_ids(v, out)
    elif isinstance(node, list):
        for v in node:
            collect_ids(v, out)


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--apk", required=True)
    p.add_argument("--serial", default="R3CW70202FP")
    p.add_argument("--only", default="autoid,theme,radio,menuopen")
    p.add_argument("--expect-md5", default="11920719cb841f0122f5389fdc0e8299")
    p.add_argument("--out", default="logs/otest_m2/i2400rc_x")
    a = p.parse_args()
    O = os.path.join(ROOT, a.out)
    os.makedirs(O, exist_ok=True)
    only = set(a.only.split(","))
    sr = a.serial
    md5 = hashlib.md5(open(a.apk, "rb").read()).hexdigest()
    if md5 != a.expect_md5:
        print("MD5_MISMATCH", md5)
        sys.exit(4)
    base = [ln.strip() for ln in open(BASE, encoding="utf-8") if ln.strip()]
    summ = {"apk_md5": md5, "started": time.strftime("%Y-%m-%d %H:%M:%S"), "baseline_count": len(base), "items": []}
    state, code = {}, 0

    def jd(name, obj):
        json.dump(obj, open(os.path.join(O, "x_%s.json" % name), "w", encoding="utf-8"), ensure_ascii=False, indent=1)

    def sv():
        jd("summary", summ)

    def cap(name):
        rc, data = ra.adb(sr, "exec-out", "screencap", "-p", binary=True)
        k = data.find(b"\x89PNG") if rc == 0 else -1
        if k >= 0:
            open(os.path.join(O, "x_%s.png" % name), "wb").write(data[k:])
            return "x_%s.png" % name
        return None

    def item(cid, verdict, actual, note=None):
        summ["items"].append({"id": cid, "verdict": verdict, "note": note, "at": time.strftime("%Y-%m-%d %H:%M:%S")})
        jd(cid, {"id": cid, "verdict": verdict, "actual": actual, "note": note})
        sv()
        print("ITEM", cid, verdict, json.dumps(actual, ensure_ascii=False)[:400], flush=True)

    try:
        ra.adb(sr, "shell", "wm", "size", "reset")
        if not ra.prepare(sr, a.apk, state):
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

        def flat(ms):
            if isinstance(ms, dict):
                ms = ms.get("items")
            return [x for x in (ms or []) if isinstance(x, dict) and x.get("separator") is None] if isinstance(ms, list) else []

        def ids_now(tag):
            ids = set()
            t = R("tree.get", named=True, depth=60)
            jd("tree_%s" % tag, t if isinstance(t, (dict, list)) else {"raw": str(t)[:2000]})
            collect_ids(t, ids)
            from_tree = set(ids)
            for m in TOP:
                for x in flat(R("menu.list", id=m)):
                    if isinstance(x.get("id"), str):
                        ids.add(x["id"])
                ids.add(m) if R("ui.find", id=m) else None
            return ids, from_tree

        R("lang.set", code="ko")
        R("ui.mode", set="auto")
        summ["settings_start"] = R("settings.get")
        sv()

        if "autoid" in only:
            res = {}
            for tag, setup in (("tablet_wide", lambda: R("ui.mode", set="auto")),
                               ("phone_mode", lambda: R("ui.mode", set="phone")),
                               ("narrow_904", lambda: (R("ui.mode", set="auto"), ra.adb(sr, "shell", "wm", "size", FOLD)))):
                setup()
                time.sleep(4)
                ids, from_tree = ids_now(tag)
                missing = sorted(set(base) - ids)
                extra = sorted(i for i in ids if i not in set(base))
                res[tag] = {"total_found": len(ids), "from_tree": len(from_tree), "baseline": len(base), "missing": missing, "extra": extra, "mode": R("ui.mode")}
                cap("autoid_" + tag)
                ra.adb(sr, "shell", "wm", "size", "reset")
                time.sleep(3)
            R("ui.mode", set="auto")
            w = res["tablet_wide"]
            ok = not w["missing"] and set(w["extra"]) <= {"shell.compactClose"}
            item("autoid_110", "PASS" if ok else "확인 필요(missing/extra 목록 참조)", res, "extra 의 shell.compactClose 는 odev-8r 가 정상으로 안내한 +1")

        if "menuopen" in only:
            out = {}
            for mid in ("shell.mnuFile", "shell.mnuView"):
                R("ui.click", id=mid)
                time.sleep(1.5)
                after = flat(R("menu.list", id=mid))
                out[mid] = {"click_error": err(), "items_after_open": len(after), "sample": [x.get("id") for x in after][:8], "shot": cap("menuopen_" + mid.split(".")[-1])}
                R("ui.click", id=mid)
                time.sleep(1)
            item("menu_open_capture", "캡처 판독 필요", out)

        if "theme" in only:
            s0 = R("settings.get") or {}
            tid0 = s0.get("themeId")
            owner = None
            for m in TOP:
                if any(x.get("id") == "shell.mnuNextTheme" for x in flat(R("menu.list", id=m))):
                    owner = m
                    break
            R("ui.click", id=owner) if owner else None
            time.sleep(1.2)
            res = R("ui.click", id="shell.mnuNextTheme")
            e = err()
            time.sleep(1.5)
            tid1 = (R("settings.get") or {}).get("themeId")
            if owner:
                R("ui.click", id=owner)
            time.sleep(0.8)
            R("theme.select", id=tid0) if tid0 and tid1 != tid0 else None
            time.sleep(1)
            tid2 = (R("settings.get") or {}).get("themeId")
            item("mnuNextTheme_click", "PASS" if (tid1 and tid1 != tid0 and not e) else "FAIL", {"owner_menu": owner, "click_error": e, "theme_before": tid0, "theme_after_click": tid1, "theme_restored": tid2, "restored_ok": tid2 == tid0})

        if "radio" in only:
            out = {}
            for grp, path in (("shell.mnuSysGpu", ["shell.mnuSys", "shell.mnuSysGpu"]), ("shell.mnuSysLogLevel", ["shell.mnuSys", "shell.mnuSysLog", "shell.mnuSysLogLevel"])):
                def openp():  # 메뉴를 ESC 로 정리한 뒤 상위 메뉴 → 하위 메뉴 순으로 연다(항목은 팝업이 열려야 클릭됨)
                    ra.adb(sr, "shell", "input", "keyevent", "111")
                    time.sleep(0.8)
                    for pid in path:
                        R("ui.click", id=pid)
                        time.sleep(1.2)
                openp()
                g0 = [{"id": x.get("id"), "checked": x.get("checked")} for x in flat(R("menu.list", id=grp)) if x.get("id") != grp]
                orig = [x["id"] for x in g0 if x.get("checked") is True]
                tgt = [x["id"] for x in g0 if x.get("checked") is not True and x.get("id")]
                rec = {"before": g0, "orig_checked": orig, "target": tgt[:1]}
                if tgt:
                    R("ui.click", id=tgt[0])
                    rec["click_error"] = err()
                    time.sleep(1.5)
                    g1 = [{"id": x.get("id"), "checked": x.get("checked")} for x in flat(R("menu.list", id=grp)) if x.get("id") != grp]
                    rec["after"] = g1
                    rec["exclusive"] = sum(1 for x in g1 if x.get("checked") is True) == 1 and any(x["id"] == tgt[0] and x["checked"] is True for x in g1)
                    if orig:
                        openp()
                        R("ui.click", id=orig[0])
                        rec["restore_click_error"] = err()
                        time.sleep(1.5)
                    g2 = [{"id": x.get("id"), "checked": x.get("checked")} for x in flat(R("menu.list", id=grp)) if x.get("id") != grp]
                    rec["restored"] = g2
                    rec["restored_ok"] = [x["id"] for x in g2 if x.get("checked") is True] == orig
                out[grp] = rec
                rec["shot"] = cap("radio_" + grp.split(".")[-1])
                ra.adb(sr, "shell", "input", "keyevent", "111")
                time.sleep(0.8)
            allok = all(v.get("exclusive") and v.get("restored_ok") for v in out.values() if v.get("target"))
            item("radio_gpu_log", "PASS" if allok else "확인 필요", out, "클릭한 뒤 원래 항목을 다시 클릭해 원복하고 restored_ok 로 확인")
    except Exception as e:  # noqa: BLE001
        summ["error"] = repr(e)
        print("ERROR", repr(e), flush=True)
        code = 2
    finally:
        ra.adb(sr, "shell", "wm", "size", "reset")
        try:
            summ["settings_end"] = R("settings.get") if "R" in dir() else None
            R("ui.mode", set="auto") if "R" in dir() else None
        except Exception:  # noqa: BLE001
            pass
        sv()
        ra.cleanup(sr, state)
        print("EXIT", code, flush=True)
        sys.exit(code)


if __name__ == "__main__":
    main()
