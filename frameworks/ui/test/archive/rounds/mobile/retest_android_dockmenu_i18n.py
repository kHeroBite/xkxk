# UI-16 Dock 메뉴 다국어 캡처 판정(Fold5): ko·en 각각 ① 탐색기 머리줄 ▾ 메뉴 ② 탭 모드 문서 탭 길게 누름 메뉴 ③ MDI 문서 창 아이콘 메뉴를 열어 캡처하는 스크립트
"""
사용: python3 -E -u tools/retest_android_dockmenu_i18n.py --apk <APK> --outdir <폴더>
메뉴 문구 판정은 캡처를 직접 연다. dock.menuHeaders 가 있는 판이면 그 응답도 함께 저장한다.
"""
import argparse
import json
import os
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import retest_android as ra  # noqa: E402
import retest_android_cond2 as c2  # noqa: E402

S = 360 / 160.0


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--apk", required=True)
    p.add_argument("--serial", default="R3CW70202FP")
    p.add_argument("--outdir", required=True)
    a = p.parse_args()
    O, sr = a.outdir, a.serial
    os.makedirs(O, exist_ok=True)
    state, rep = {}, {}

    def cap(name):
        rc, data = ra.adb(sr, "exec-out", "screencap", "-p", binary=True)
        k = data.find(b"\x89PNG") if rc == 0 else -1
        if k >= 0:
            open(os.path.join(O, name + ".png"), "wb").write(data[k:])
            return name + ".png"
        return None

    def esc():
        ra.adb(sr, "shell", "input", "keyevent", "111")
        time.sleep(0.8)

    try:
        info = ra.prepare(sr, a.apk, state)
        from retest_remote import Remote
        c2.launch(sr, "--fresh", info)
        time.sleep(3)
        r = Remote()
        r.call("docs.close", all=True)
        ids = [(r.call("docs.open", kind=k) or [{}])[0].get("id") for k in ("grid", "gantt", "form")]
        for lang in ("ko", "en"):
            L = {"lang": r.call("lang.set", code=lang)}
            time.sleep(1)
            # ① 탐색기 머리줄 ▾
            r.call("layout.template.apply", id="tpl.mdi-cascade")
            time.sleep(1.5)
            lb = (r.call("panel.bounds") or {}).get("LeftPane") or {}
            if lb:
                x, y = (lb["x"] + lb["w"] - 37) * S, (lb["y"] + 11) * S
                ra.adb(sr, "shell", "input", "tap", str(int(x)), str(int(y)))
                time.sleep(1.2)
                L["cap_tool_menu"] = cap("%s_1_tool_menu" % lang)
                esc()
            # ③ MDI 문서 창 아이콘 메뉴(맨 앞 창 제목줄 왼쪽 아이콘)
            ra.adb(sr, "shell", "input", "tap", "434", "480")
            time.sleep(1.2)
            L["cap_mdi_menu"] = cap("%s_3_mdi_menu" % lang)
            esc()
            # ② 탭 모드 문서 탭 길게 누름
            r.call("layout.template.apply", id="tpl.tabbed")
            time.sleep(1.5)
            db = (r.call("panel.bounds") or {}).get("Documents") or {}
            if db:
                x, y = (db["x"] + 30) * S, (db["y"] + 10) * S
                ra.adb(sr, "shell", "input", "swipe", str(int(x)), str(int(y)), str(int(x)), str(int(y)), "900")
                time.sleep(1.2)
                L["cap_tab_menu"] = cap("%s_2_tab_menu" % lang)
                esc()
            for tgt, doc in (("tool", None), ("tab", ids[0]), ("mdi", ids[0])):
                L["menuHeaders_" + tgt] = r.call("dock.menuHeaders", target=tgt, doc=doc)
            L["dock_chrome"] = r.call("dock.chrome")  # ▾ 메뉴 대상 값(리드 지시 — 원문 저장)
            rep[lang] = L
        r.call("lang.set", code="ko")
    finally:
        json.dump(rep, open(os.path.join(O, "dockmenu_result.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=1)
        ra.cleanup(sr, state)
        ra.adb(sr, "shell", "wm", "size", "reset")


if __name__ == "__main__":
    main()
