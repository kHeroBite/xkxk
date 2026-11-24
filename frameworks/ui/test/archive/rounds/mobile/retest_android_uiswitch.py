# 태블릿모드/폰모드 전환 스위치 판정(Fold5, odev-2r 절차): 표시·겹침(가로/세로)·3회 왕복 hash 유지·checked·저장/접기펴기/재기동·용어(ko/en)·M4/F1 회귀를 기록하는 스크립트
"""
★저장 판정 절차: 앱은 반드시 인자 없이 띄운다. --fresh 는 SuppressAutoSave 를 켜(App.axaml.cs) 화면 모드가 저장되지 않는다
  (i1432 1차 실측 — 재기동 뒤 Auto 로 돌아감). 이 스크립트의 5번(재기동)은 --fresh 로 시작하므로 저장 판정에 쓰지 않고,
  retest_android_uiswitch_fix.py(인자 없이 기동)의 save_restart 로 판정한다.
사용: python3 -E -u tools/retest_android_uiswitch.py --apk <MainTitleBar 05c4b94a 판> --outdir <폴더>
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

FOLD = "904x2316"


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

    rc, rot0 = ra.adb(sr, "shell", "settings", "get", "system", "accelerometer_rotation")
    rc, urot0 = ra.adb(sr, "shell", "settings", "get", "system", "user_rotation")
    rep["rotation_before"] = [rot0.strip(), urot0.strip()]
    try:
        info = ra.prepare(sr, a.apk, state)
        from retest_remote import Remote

        def launch(args="--fresh"):
            c2.launch(sr, args, info)
            time.sleep(3)
            return Remote()

        r = launch()
        F = lambda i: r.call("ui.find", id=i) or {}
        B = lambda i: {k: F(i).get(k) for k in ("visible", "isVisible", "effectivelyVisible", "bounds", "checked", "isChecked", "text")}

        def inst():
            tr = r.call("tree.get", named=False, depth=90) or {}
            cnt = lambda key: len(walk(tr, [], lambda n: key in [v for v in n.values() if isinstance(v, str)]))
            return {"grid": cnt("grid.dgMain"), "gantt": cnt("gantt.lblZoom"), "form": cnt("form.txtTitle")}

        r.call("docs.close", all=True)
        ids = {k: (r.call("docs.open", kind=k) or [{}])[0].get("id") for k in ("grid", "gantt", "form")}
        r.call("layout.template.apply", id="tpl.mdi-cascade")
        r.call("docs.activate", doc=ids["grid"])
        time.sleep(1.5)
        gv = lambda: {k: (r.call("grid.visible", id="grid.dgMain", doc=ids["grid"]) or {}).get(k) for k in ("gridHash", "viewHash", "selectedIndex", "first")}
        # 1·2 표시·겹침: 펼침 세로(기본) → 펼침 가로
        lay = {}
        for tag, rot in (("unfold_portrait", None), ("unfold_landscape", "1")):
            if rot:
                ra.adb(sr, "shell", "settings", "put", "system", "accelerometer_rotation", "0")
                ra.adb(sr, "shell", "settings", "put", "system", "user_rotation", rot)
                time.sleep(3)
            lay[tag] = {i: B(i) for i in ("shell.menu", "shell.uiModeSwitch", "shell.titleDragArea", "shell.btnUiModeTablet", "shell.btnUiModePhone")}
            lay[tag]["cap"] = cap("sw_" + tag)
        ra.adb(sr, "shell", "settings", "put", "system", "user_rotation", "0")
        time.sleep(3)
        ra.adb(sr, "shell", "wm", "size", FOLD)
        time.sleep(2.5)
        lay["folded"] = {"shell.uiModeSwitch": B("shell.uiModeSwitch"), "cap": cap("sw_folded")}
        ra.adb(sr, "shell", "wm", "size", "reset")
        time.sleep(2.5)
        rep["layout"] = lay
        # 3·4 전환 3회 왕복 + checked + 같은 칸 다시 누르기
        r.call("grid.select", id="grid.dgMain", doc=ids["grid"], index=300, scroll=True)
        time.sleep(1)
        rnd = [{"step": "start", "gv": gv(), "inst": inst(), "mode": r.call("ui.mode")}]
        for i in range(1, 4):
            for btn in ("shell.btnUiModePhone", "shell.btnUiModeTablet"):
                r.call("ui.click", id=btn)
                time.sleep(1.5)
                rnd.append({"step": "%d_%s" % (i, btn.split("btnUiMode")[-1]), "gv": gv(), "inst": inst(), "mode": (r.call("ui.mode") or {}).get("uiModeEffective"),
                            "checked": {b: B(b).get("checked") for b in ("shell.btnUiModePhone", "shell.btnUiModeTablet")}, "switch_visible": B("shell.uiModeSwitch").get("effectivelyVisible")})
            if i == 1:
                rnd[-1]["cap"] = cap("sw_round1_tablet")
        r.call("ui.click", id="shell.btnUiModeTablet")  # 이미 선택된 칸 다시 누르기
        time.sleep(1)
        rnd.append({"step": "tablet_again", "mode": (r.call("ui.mode") or {}).get("uiModeEffective"), "checked": {b: B(b).get("checked") for b in ("shell.btnUiModePhone", "shell.btnUiModeTablet")}})
        rep["roundtrip"] = rnd
        # 4 추가: 같은 칸 5회 연속 재클릭(직후·100ms 뒤 checked) + 다른 칸 즉시 전환
        CK = lambda: {b.split("btnUiMode")[-1]: B(b).get("checked") for b in ("shell.btnUiModePhone", "shell.btnUiModeTablet")}
        rc_ = []
        for btn, other in (("shell.btnUiModePhone", "shell.btnUiModeTablet"), ("shell.btnUiModeTablet", "shell.btnUiModePhone")):
            r.call("ui.click", id=btn)
            time.sleep(1.2)
            for k in range(1, 6):
                r.call("ui.click", id=btn)
                c_now = CK()
                time.sleep(0.1)
                c_100 = CK()
                rc_.append({"btn": btn, "k": k, "now": c_now, "after100ms": c_100, "mode": (r.call("ui.mode") or {}).get("uiModeEffective")})
            t0 = time.time()
            r.call("ui.click", id=other)
            rc_.append({"btn": other, "switch": True, "now": CK(), "mode": (r.call("ui.mode") or {}).get("uiModeEffective"), "read_s": round(time.time() - t0, 2)})
        rep["reclick"] = rc_
        # 5 저장·접기펴기·재기동
        sv = {}
        r.call("ui.click", id="shell.btnUiModePhone")
        time.sleep(1.2)
        ra.adb(sr, "shell", "wm", "size", FOLD); time.sleep(2.5)
        ra.adb(sr, "shell", "wm", "size", "reset"); time.sleep(2.5)
        sv["phone_fold_unfold"] = r.call("ui.mode")
        r.call("ui.click", id="shell.btnUiModeTablet")
        time.sleep(1.2)
        ra.adb(sr, "shell", "wm", "size", FOLD); time.sleep(2.5)
        sv["tablet_folded"] = r.call("ui.mode")
        ra.adb(sr, "shell", "wm", "size", "reset"); time.sleep(2.5)
        sv["tablet_fold_unfold"] = r.call("ui.mode")
        r.call("ui.click", id="shell.btnUiModePhone")
        time.sleep(1.2)
        r = launch("")  # 재기동(--fresh 없이)
        sv["after_restart"] = r.call("ui.mode")
        rep["save"] = sv
        # 8 접힘 메뉴 체크: 태블릿 선택→접힘(체크=폰모드·태블릿 비활성·툴팁)→펼침(태블릿 복귀), 자동 저장 상태 반복
        mn = lambda: (r.call("menu.list", id="shell.mnuUiMode") or {}).get("items")
        fm = {}
        for pick in ("tablet", "auto"):
            if pick == "tablet":
                r.call("ui.click", id="shell.btnUiModeTablet")  # 사용자 경로(펼침에서 태블릿모드 선택)
            else:
                r.call("ui.mode", set="auto")
            time.sleep(1.2)
            ra.adb(sr, "shell", "wm", "size", FOLD); time.sleep(2.5)
            fm[pick + "_folded"] = {"mode": r.call("ui.mode"), "menu": mn(), "cap": cap("sw_foldmenu_" + pick)}
            ra.adb(sr, "shell", "wm", "size", "reset"); time.sleep(2.5)
            fm[pick + "_unfolded"] = {"mode": r.call("ui.mode"), "menu": mn()}
        rep["fold_menu"] = fm
        r.call("ui.mode", set="phone")
        time.sleep(1)
        # 6 용어 ko/en
        tm = {}
        for lang in ("ko", "en"):
            r.call("lang.set", code=lang)
            time.sleep(1)
            tm[lang] = {"tablet_btn": B("shell.btnUiModeTablet").get("text"), "phone_btn": B("shell.btnUiModePhone").get("text"),
                        "menu": [(x.get("id"), x.get("header"), x.get("tip")) for x in ((r.call("menu.list", id="shell.mnuUiMode") or {}).get("items") or [])],
                        "btn_tips": {b: (r.call("ui.find", id=b, props="ToolTip.Tip") or {}).get("props") for b in ("shell.btnUiModeTablet", "shell.btnUiModePhone")},
                        "bounds": {i: B(i).get("bounds") for i in ("shell.menu", "shell.uiModeSwitch", "shell.titleDragArea")},
                        "mode": r.call("ui.mode"),
                        "cap": cap("sw_terms_" + lang)}
        r.call("lang.set", code="ko")
        rep["terms"] = tm
        # 마무리: 자동
        rep["restore_auto"] = r.call("ui.mode", set="auto")
    finally:
        json.dump(rep, open(os.path.join(O, "uiswitch_result.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=1)
        ra.adb(sr, "shell", "settings", "put", "system", "user_rotation", (urot0.strip() or "0"))
        ra.adb(sr, "shell", "settings", "put", "system", "accelerometer_rotation", (rot0.strip() or "0"))
        ra.cleanup(sr, state)
        ra.adb(sr, "shell", "wm", "size", "reset")


if __name__ == "__main__":
    main()
