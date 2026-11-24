# 스위치 판정 보완(Fold5): --fresh 없이 저장·재기동 확인(5), 태블릿 저장 상태의 접힘 메뉴 체크(8), 실제 손가락 탭으로 같은 칸 재클릭 5회·다른 칸 전환(4)을 기록하는 스크립트
"""
사용: python3 -E -u tools/retest_android_uiswitch_fix.py --apk <스위치 판 APK> --outdir <폴더>
- --fresh 로 띄우면 SuppressAutoSave 가 켜져 설정이 저장되지 않는다(App.axaml.cs). 그래서 이 스크립트는 처음부터 인자 없이 띄운다(설치 직후라 설정은 기본값).
- 탭 좌표: ui.find bounds(DIP) × RenderScaling 2.25. 먼저 다른 칸을 탭해 전환되는지로 좌표를 검증한다.
"""
import argparse
import json
import os
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import retest_android as ra  # noqa: E402
import retest_android_cond2 as c2  # noqa: E402

FOLD = "904x2316"
SC = 2.25


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

    try:
        info = ra.prepare(sr, a.apk, state)
        from retest_remote import Remote

        def launch():
            c2.launch(sr, "", info)
            time.sleep(3)
            return Remote()

        r = launch()
        F = lambda i: r.call("ui.find", id=i) or {}
        CK = lambda: {b.split("btnUiMode")[-1]: F(b).get("checked") for b in ("shell.btnUiModePhone", "shell.btnUiModeTablet")}
        MD = lambda: r.call("ui.mode") or {}
        mn = lambda: [(x.get("id"), x.get("enabled"), x.get("checked"), x.get("tip")) for x in ((r.call("menu.list", id="shell.mnuUiMode") or {}).get("items") or [])]
        rep["start"] = {"mode": MD(), "checked": CK()}

        def tap(btn):
            b = F(btn).get("bounds") or {}
            x, y = int((b["x"] + b["w"] / 2) * SC), int((b["y"] + b["h"] / 2) * SC)
            ra.adb(sr, "shell", "input", "tap", str(x), str(y))
            return [x, y]

        # 4) 실제 탭: 좌표 검증(다른 칸 전환) → 같은 칸 5회 → 다른 칸 즉시 전환
        tp = []
        for btn, other in (("shell.btnUiModePhone", "shell.btnUiModeTablet"), ("shell.btnUiModeTablet", "shell.btnUiModePhone")):
            xy = tap(btn)
            time.sleep(1.5)
            tp.append({"tap": btn, "xy": xy, "kind": "switch_to", "mode": MD().get("uiModeEffective"), "checked": CK()})
            for k in range(1, 6):
                tap(btn)
                c_now = CK()
                time.sleep(0.1)
                c_100 = CK()
                tp.append({"tap": btn, "k": k, "now": c_now, "after100ms": c_100, "mode": MD().get("uiModeEffective")})
            tp.append({"tap": other, "kind": "switch_check_after", "xy": tap(other), "now": CK(), "mode": MD().get("uiModeEffective")})
            time.sleep(1.5)
        rep["tap_reclick"] = tp
        rep["cap_after_taps"] = cap("fix_after_taps")
        # 5) 저장·재기동(--fresh 없음)
        sv = {}
        for target in ("Phone", "Tablet"):
            r.call("ui.click", id="shell.btnUiMode" + target) if MD().get("uiMode") != target else None
            time.sleep(2.5)
            sv["before_restart_" + target] = MD()
            r = launch()
            sv["after_restart_" + target] = {"mode": MD(), "checked": CK()}
        rep["save_restart"] = sv
        # 8) 태블릿 저장 상태 → 접힘 → 펼침
        fm = {"before": {"mode": MD(), "menu": mn()}}
        ra.adb(sr, "shell", "wm", "size", FOLD); time.sleep(2.5)
        fm["tablet_folded"] = {"mode": MD(), "menu": mn(), "cap": cap("fix_foldmenu_tablet")}
        ra.adb(sr, "shell", "wm", "size", "reset"); time.sleep(2.5)
        fm["tablet_unfolded"] = {"mode": MD(), "menu": mn(), "checked": CK()}
        rep["fold_menu_tablet"] = fm
        r.call("ui.mode", set="auto")
        time.sleep(1)
    finally:
        json.dump(rep, open(os.path.join(O, "uiswitch_fix_result.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=1)
        ra.cleanup(sr, state)
        ra.adb(sr, "shell", "wm", "size", "reset")


if __name__ == "__main__":
    main()
