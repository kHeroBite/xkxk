# i1720rc Fold5 앱 Q16-A-17(D-DAD1): 탐색기를 📌 자동 숨김으로 접은 뒤 가장자리 항목의 앱 내 메뉴에서 '문서로 도킹'을 adb 터치로 시도하고 결과(Explorer→Documents)를 기록하는 스크립트(otest-m-2, lead 결정 (나))
"""
사용: python3 -E -u test/mobile/measure/retest_android_q16_17.py --apk <i1720rc diag APK>
결과: logs/otest_m2/i1720rc/app_Q16-A-17*.json|png. 경로가 없으면 verdict 'N/A(사용자 노출 경로 없음, 결정 (나))'.
원복: panel.join id=LeftPane tool=Explorer, panel.pin on=false, 앱 제거·forward 해제·stay_on 0·wm size reset.
"""
import argparse
import json
import os
import sys
import time

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "common"))  # D-TESTDIR: test/common/_paths.py
import _paths  # noqa: E402
_paths.add()
import retest_android as ra  # noqa: E402

ROOT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
O = os.path.join(ROOT, "logs", "otest_m2", "i1720rc")
S = 360 / 160.0
DOCK_WORDS = ("문서로 도킹", "Dock as Document", "Dock as document")


def walk(n, out):
    if isinstance(n, dict):
        out.append(n)
        for c in n.get("children") or n.get("tree") or []:
            walk(c, out)
    elif isinstance(n, list):
        for c in n:
            walk(c, out)
    return out


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--apk", required=True)
    p.add_argument("--serial", default="R3CW70202FP")
    a = p.parse_args()
    sr = a.serial
    state, rep = {}, {"id": "Q16-A-17", "build": "i1720rc", "steps": []}

    def jd(name, obj):
        json.dump(obj, open(os.path.join(O, name), "w", encoding="utf-8"), ensure_ascii=False, indent=1)

    def cap(name):
        rc, data = ra.adb(sr, "exec-out", "screencap", "-p", binary=True)
        k = data.find(b"\x89PNG") if rc == 0 else -1
        if k >= 0:
            open(os.path.join(O, name + ".png"), "wb").write(data[k:])
            return name + ".png"
        return None

    def step(name, **kw):
        rep["steps"].append(dict(kw, step=name, at=time.strftime("%H:%M:%S")))
        jd("app_Q16-A-17.json", rep)
        print("STEP", name, json.dumps(kw, ensure_ascii=False)[:300], flush=True)

    try:
        ra.adb(sr, "shell", "wm", "size", "reset")
        if not ra.prepare(sr, a.apk, state):
            raise RuntimeError("앱 연결 실패")
        from retest_remote import Remote
        time.sleep(4)
        r = Remote()
        R = lambda cmd, **k: r.call(cmd, _timeout=120, **k)  # noqa: E731
        R("ui.mode", set="auto")
        R("lang.set", code="ko")
        step("before", tabs_left=R("panel.tabs", id="LeftPane"), docs=R("docs.list"))
        pin = R("panel.pin", id="LeftPane", on=True)
        time.sleep(1.5)
        step("pin", result=pin, panels=R("panel.list"))
        cap("app_Q16-A-17_pinned")
        nodes = walk(R("tree.get", named=False, depth=80) or {}, [])
        items = [n for n in nodes if n.get("type") in ("ToolPinItemControl", "ToolPinnedControl") or ("PinItem" in str(n.get("type")))]
        exp = [n for n in nodes if n.get("visible") and n.get("text") in ("탐색기", "Explorer") and n.get("bounds")]
        step("pin_items", pin_items=[{k: n.get(k) for k in ("type", "text", "visible", "bounds")} for n in items][:10],
             explorer_text_nodes=[{k: n.get(k) for k in ("type", "text", "bounds")} for n in exp][:10])
        target = None
        # 띠 전체(ToolPinnedControl)가 아니라 항목(ToolPinItemControl)을 누른다
        items.sort(key=lambda n: 0 if n.get("type") == "ToolPinItemControl" else 1)
        for n in items:
            if n.get("visible") and n.get("bounds"):
                target = n
                break
        if target is None and exp:
            target = exp[0]
        found = None
        if target:
            b = target["bounds"]
            cx, cy = (b["x"] + b["w"] / 2) * S, (b["y"] + b["h"] / 2) * S
            for how, args in (("long_press", ["input", "swipe", str(int(cx)), str(int(cy)), str(int(cx)), str(int(cy)), "1000"]), ("tap", ["input", "tap", str(int(cx)), str(int(cy))])):
                ra.adb(sr, "shell", *args)
                time.sleep(1.5)
                shot = cap("app_Q16-A-17_%s" % how)
                nodes2 = walk(R("tree.get", named=False, depth=80) or {}, [])
                menu = [n for n in nodes2 if n.get("visible") and n.get("type") in ("MenuItem", "ContextMenu", "MenuFlyoutPresenter")]
                hit = [n for n in nodes2 if n.get("visible") and any(w in str(n.get("text")) for w in DOCK_WORDS)]
                step(how, adb=" ".join(args), target={k: target.get(k) for k in ("type", "text", "bounds")}, shot=shot,
                     menu_items=[{k: n.get(k) for k in ("type", "text", "enabled", "bounds")} for n in menu][:15],
                     dock_hits=[{k: n.get(k) for k in ("type", "text", "enabled", "bounds")} for n in hit])
                if hit:
                    found = hit[0]
                    break
                ra.adb(sr, "shell", "input", "keyevent", "111")
                time.sleep(0.8)
        if found and found.get("enabled", True):
            fb = found["bounds"]
            ra.adb(sr, "shell", "input", "tap", str(int((fb["x"] + fb["w"] / 2) * S)), str(int((fb["y"] + fb["h"] / 2) * S)))
            time.sleep(1.5)
            cap("app_Q16-A-17_after")
            lt, dl, sd = R("panel.tabs", id="LeftPane"), R("docs.list"), R("split.dump")
            jd("app_Q16-A-17_dump.json", sd)
            txt = json.dumps(dl, ensure_ascii=False)
            ok = "Explorer" in txt or "탐색기" in txt
            step("after", tabs_left=lt, docs=dl)
            rep["verdict"] = "PASS" if ok else "FAIL"
            rep["note"] = "탐색기 '문서로 도킹' 실행 뒤 docs.list 에 Explorer %s" % ("있음" if ok else "없음")
            R("panel.join", id="LeftPane", tool="Explorer")
        else:
            rep["verdict"] = "N/A(사용자 노출 경로 없음, 결정 (나))"
            rep["note"] = "📌 자동 숨김 뒤 가장자리 항목 길게 누름·탭에서 '문서로 도킹' 메뉴를 찾지 못함" if target else "자동 숨김 뒤 가장자리 항목을 트리에서 찾지 못함"
        R("panel.pin", id="LeftPane", on=False)
        time.sleep(1)
        rep["restore"] = {"tabs_left": R("panel.tabs", id="LeftPane"), "panels": R("panel.list")}
        cap("app_Q16-A-17_restored")
    except Exception as e:  # noqa: BLE001
        rep["error"] = repr(e)
        print("ERROR", repr(e), flush=True)
    finally:
        jd("app_Q16-A-17.json", rep)
        try:
            ra.adb(sr, "shell", "wm", "size", "reset")
        finally:
            ra.cleanup(sr, state)
            ra.adb(sr, "shell", "settings", "put", "global", "stay_on_while_plugged_in", "0")
        print("EXIT", rep.get("verdict"), flush=True)


if __name__ == "__main__":
    main()
