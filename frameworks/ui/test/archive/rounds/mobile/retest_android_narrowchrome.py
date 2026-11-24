# 측면 패널 ▾ 숨기기 판정(Fold5, odev-7c): 탐색기 114dp 머리줄 ko/en 캡처·제목 폭, 📌 탭 자동 숨김↔고정·플라이아웃 머리줄, 알림 ▾·MDI 메뉴 ko 를 기록하는 스크립트
"""
사용: python3 -E -u tools/retest_android_narrowchrome.py --apk <APK(NarrowToolChrome d2f2c945 판)> --outdir <폴더>
"""
import argparse
import json
import os
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import retest_android as ra  # noqa: E402
import retest_android_cond2 as c2  # noqa: E402
from retest_android_emptycells import walk  # noqa: E402

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

    def tap(x, y):
        ra.adb(sr, "shell", "input", "tap", str(int(x)), str(int(y)))

    try:
        info = ra.prepare(sr, a.apk, state)
        from retest_remote import Remote
        c2.launch(sr, "--fresh", info)
        time.sleep(3)
        r = Remote()
        r.call("docs.close", all=True)
        for k in ("grid", "gantt", "form"):
            r.call("docs.open", kind=k)
        r.call("layout.template.apply", id="tpl.mdi-cascade")
        time.sleep(1.5)
        for lang in ("ko", "en"):
            r.call("lang.set", code=lang)
            time.sleep(1.2)
            tr = r.call("tree.get", named=False, depth=60) or {}
            rep["titles_" + lang] = walk(tr, [], lambda n: n.get("name") == "PART_Title")[:6]
            rep["cap_" + lang] = cap("nc_header_" + lang)
        r.call("lang.set", code="ko")
        time.sleep(1)
        lb = (r.call("panel.bounds") or {}).get("LeftPane") or {}
        rep["LeftPane"] = lb
        rep["list0"] = [x for x in (r.call("panel.list") or []) if x.get("id") == "LeftPane"]
        if lb:
            hy = (lb["y"] + 11) * S
            right = (lb["x"] + lb["w"]) * S
            pin_x = right - 71  # 로그 패널 기준 위치(✕ 왼쪽 두 번째). 실제 아이콘 위치는 캡처로 확인
            rep["pin_xy"] = [int(pin_x), int(hy)]
            tap(pin_x, hy)
            time.sleep(1.5)
            rep["list_after_pin"] = [x for x in (r.call("panel.list") or []) if x.get("id") == "LeftPane"]
            rep["cap_after_pin"] = cap("nc_after_pin")
            # 자동 숨김 상태면 가장자리 탭으로 플라이아웃을 연다
            tap(20, 900)
            time.sleep(1.5)
            rep["cap_flyout"] = cap("nc_flyout")
            rep["pin_off"] = r.call("panel.pin", id="LeftPane", on=False)
            time.sleep(1.2)
            rep["list_after_pin_off"] = [x for x in (r.call("panel.list") or []) if x.get("id") == "LeftPane"]
            rep["cap_after_pin_off"] = cap("nc_after_pin_off")
        # MDI 창 메뉴 ko(맨 앞 창 아이콘)
        tap(434, 480)
        time.sleep(1.2)
        rep["cap_mdi_menu_ko"] = cap("nc_mdi_menu_ko")
        ra.adb(sr, "shell", "input", "keyevent", "111")
    finally:
        json.dump(rep, open(os.path.join(O, "narrowchrome_result.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=1)
        ra.cleanup(sr, state)
        ra.adb(sr, "shell", "wm", "size", "reset")


if __name__ == "__main__":
    main()
