# 스위치 재판정(Button+pdUiOn 판, Fold5): 실제 탭 같은 칸 5회·다른 칸 즉시 전환(classes pdUiOn), 키보드 SPACE/ENTER(가능 시), pd-light/pd-dark × 폰/태블릿 대비 화소 실측을 기록하는 스크립트
"""
사용: python3 -E -u tools/retest_android_uiswitch_v2.py --apk <스위치 판 APK> --outdir <폴더>
- 앱은 인자 없이 띄운다(--fresh 는 SuppressAutoSave 를 켜 저장 판정을 망친다 — 저장 판정은 반드시 인자 없이).
- 선택 판정: ui.find 의 classes 에 'pdUiOn' 이 있는 칸.
- 대비: 칸 사각형(bounds × 2.25) 화소 중 최빈색 = 배경, 배경과 대비가 가장 큰 상위 3% 화소의 중앙값 = 글자색. WCAG 상대 휘도 대비.
  기준: 선택/비선택 배경 3:1 이상, 선택 칸 글자 4.5:1 이상.
"""
import argparse
import io
import json
import os
import sys
import time
from collections import Counter

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import retest_android as ra  # noqa: E402
import retest_android_cond2 as c2  # noqa: E402
from PIL import Image  # noqa: E402

SC = 2.25
BTN = ("shell.btnUiModePhone", "shell.btnUiModeTablet")


def lum(c):
    def ch(v):
        v = v / 255.0
        return v / 12.92 if v <= 0.03928 else ((v + 0.055) / 1.055) ** 2.4
    return 0.2126 * ch(c[0]) + 0.7152 * ch(c[1]) + 0.0722 * ch(c[2])


def contrast(a, b):
    la, lb = sorted((lum(a), lum(b)), reverse=True)
    return round((la + 0.05) / (lb + 0.05), 2)


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--apk", required=True)
    p.add_argument("--serial", default="R3CW70202FP")
    p.add_argument("--outdir", required=True)
    a = p.parse_args()
    O, sr = a.outdir, a.serial
    os.makedirs(O, exist_ok=True)
    state, rep = {}, {}

    def shot():
        rc, data = ra.adb(sr, "exec-out", "screencap", "-p", binary=True)
        k = data.find(b"\x89PNG") if rc == 0 else -1
        return Image.open(io.BytesIO(data[k:])).convert("RGB") if k >= 0 else None

    try:
        info = ra.prepare(sr, a.apk, state)
        from retest_remote import Remote
        c2.launch(sr, "", info)
        time.sleep(3)
        r = Remote()
        F = lambda i: r.call("ui.find", id=i) or {}
        ON = lambda: {b.split("btnUiMode")[-1]: ("pdUiOn" in (F(b).get("classes") or [])) for b in BTN}
        MD = lambda: (r.call("ui.mode") or {}).get("uiModeEffective")
        rep["start"] = {"mode": r.call("ui.mode"), "on": ON(), "find_phone": F(BTN[0])}

        def rect(btn):
            b = F(btn).get("bounds") or {}
            return [int(b["x"] * SC), int(b["y"] * SC), int((b["x"] + b["w"]) * SC), int((b["y"] + b["h"]) * SC)]

        def tap(btn):
            x0, y0, x1, y1 = rect(btn)
            xy = [(x0 + x1) // 2, (y0 + y1) // 2]
            ra.adb(sr, "shell", "input", "tap", str(xy[0]), str(xy[1]))
            return xy

        # 4) 실제 탭: 다른 칸으로 전환 → 같은 칸 5회 → 다른 칸 즉시 전환 (양쪽)
        tp = []
        for btn, other in ((BTN[0], BTN[1]), (BTN[1], BTN[0])):
            xy = tap(btn)
            time.sleep(1.5)
            tp.append({"tap": btn, "xy": xy, "kind": "switch_to", "mode": MD(), "on": ON()})
            for k in range(1, 6):
                tap(btn)
                o_now = ON()
                time.sleep(0.1)
                tp.append({"tap": btn, "k": k, "now": o_now, "after100ms": ON(), "mode": MD()})
            t0 = time.time()
            tap(other)
            tp.append({"tap": other, "kind": "switch_immediate", "now": ON(), "mode": MD(), "read_s": round(time.time() - t0, 2)})
            time.sleep(1.5)
        rep["tap_reclick"] = tp
        # 4-k) 키보드: 탭으로 포커스한 칸에 ENTER·SPACE (같은 칸 → 유지), 그 다음 다른 칸 포커스 후 ENTER(전환)
        kb = []
        try:
            tap(BTN[0]); time.sleep(1.2)
            for key in ("KEYCODE_ENTER", "KEYCODE_SPACE"):
                ra.adb(sr, "shell", "input", "keyevent", key)
                time.sleep(0.8)
                kb.append({"focus": BTN[0], "key": key, "on": ON(), "mode": MD(), "classes": F(BTN[0]).get("classes")})
            # 원격 ui.focus 는 없다 — 키보드 TAB 으로 포커스를 옮긴 뒤 ENTER(어느 칸에 갔는지는 isFocused 로 기록)
            for key in ("KEYCODE_TAB", "KEYCODE_ENTER"):
                ra.adb(sr, "shell", "input", "keyevent", key)
                time.sleep(0.8)
                kb.append({"key": key, "on": ON(), "mode": MD(), "classes": {b: F(b).get("classes") for b in BTN}})
        except Exception as e:  # noqa: BLE001
            kb.append({"error": repr(e)})
        rep["keyboard"] = kb
        # 9) 대비: pd-light / pd-dark × Phone / Tablet
        ct = {}
        for th in ("pd-light", "pd-dark"):
            r.call("settings.set", themeId=th)
            time.sleep(1.5)
            for mode in ("Phone", "Tablet"):
                tap("shell.btnUiMode" + mode)
                time.sleep(1.5)
                img = shot()
                sw = F("shell.uiModeSwitch").get("bounds") or {}
                box = [int(sw["x"] * SC) - 6, int(sw["y"] * SC) - 6, int((sw["x"] + sw["w"]) * SC) + 6, int((sw["y"] + sw["h"]) * SC) + 6]
                name = "contrast_%s_%s.png" % (th, mode)
                img.crop(box).resize(((box[2] - box[0]) * 2, (box[3] - box[1]) * 2)).save(os.path.join(O, name))
                cells = {}
                for b in BTN:
                    x0, y0, x1, y1 = rect(b)
                    px = list(img.crop((x0 + 3, y0 + 3, x1 - 3, y1 - 3)).getdata())
                    bg = Counter(px).most_common(1)[0][0]
                    diff = sorted(px, key=lambda c: contrast(c, bg), reverse=True)
                    top = diff[:max(1, len(diff) * 3 // 100)]
                    fg = tuple(sorted(top, key=lum)[len(top) // 2])
                    cells[b.split("btnUiMode")[-1]] = {"bg": bg, "fg": fg, "text_contrast": contrast(fg, bg), "on": "pdUiOn" in (F(b).get("classes") or [])}
                sel = [k for k, v in cells.items() if v["on"]]
                uns = [k for k, v in cells.items() if not v["on"]]
                res = {"cells": cells, "cap": name, "mode": MD()}
                if len(sel) == 1 and len(uns) == 1:
                    s, u = cells[sel[0]], cells[uns[0]]
                    res["bg_sel_vs_unsel"] = contrast(s["bg"], u["bg"])
                    res["sel_text"] = s["text_contrast"]
                    res["pass_bg3"] = res["bg_sel_vs_unsel"] >= 3.0
                    res["pass_text45"] = res["sel_text"] >= 4.5
                ct["%s_%s" % (th, mode)] = res
                print("contrast", th, mode, res.get("bg_sel_vs_unsel"), res.get("sel_text"), flush=True)
        rep["contrast"] = ct
        r.call("ui.mode", set="auto")
        time.sleep(1)
    finally:
        json.dump(rep, open(os.path.join(O, "uiswitch_v2_result.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=1, default=str)
        ra.cleanup(sr, state)
        ra.adb(sr, "shell", "wm", "size", "reset")


if __name__ == "__main__":
    main()
