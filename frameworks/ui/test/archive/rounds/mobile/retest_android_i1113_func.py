# i1113 기능 판정(Fold5): 패널 단일 탭 숨김(A1~A5)·화면 모드(B1~B3)·핀치(C1, 모의+실 다중터치 시도)·좁은 패널 머리줄(ko/en, 길게 누르기)을 원격 명령·screencap 으로 기록하는 스크립트
"""
사용: python3 -E -u tools/retest_android_i1113_func.py --apk <APK> --outdir <폴더>
캡처는 판정자가 직접 열어 본다(화소 통계만으로 끝내지 않는다). 사람 손 조작 없음.
"""
import argparse
import json
import os
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import retest_android as ra  # noqa: E402
import retest_android_cond2 as c2  # noqa: E402

FOLD = "904x2316"  # retest_android_notify.py 와 같은 접힘 재현 크기
S = 360 / 160.0  # DIP → 화면 px


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--apk", required=True)
    p.add_argument("--serial", default="R3CW70202FP")
    p.add_argument("--outdir", required=True)
    a = p.parse_args()
    O = a.outdir
    os.makedirs(O, exist_ok=True)
    sr = a.serial
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

        def start(args="--fresh"):
            c2.launch(sr, args, info)
            time.sleep(3)
            return Remote()

        # ---- A. 패널 단일 탭 숨김 + 머리줄 제목(펼침, ko) ----
        r = start()
        r.call("docs.close", all=True)
        ids = [(r.call("docs.open", kind=k) or [{}])[0].get("id") for k in ("grid", "gantt", "form")]
        r.call("layout.template.apply", id="tpl.mdi-cascade")
        time.sleep(1.5)
        A = {"ids": ids, "lang": (r.call("lang.get") or {}).get("language"), "panel_list": r.call("panel.list"),
             "tabs_left": r.call("panel.tabs", id="LeftPane"), "tabs_bottom": r.call("panel.tabs", id="BottomPane"),
             "bounds": r.call("panel.bounds"), "cap_A1_ko": cap("A1_single_tab_ko")}
        A["A2_join"] = r.call("panel.join", id="BottomPane", doc=ids[1])
        time.sleep(1.5)
        A["A2_tabs_after_join"] = r.call("panel.tabs", id="BottomPane")
        A["cap_A2_two_tabs"] = cap("A2_two_tabs")
        A["A2_return"] = r.call("panel.returnDoc", doc=ids[1])
        time.sleep(1.5)
        A["A2_tabs_after_return"] = r.call("panel.tabs", id="BottomPane")
        A["cap_A2_back_single"] = cap("A2_back_single")
        A["A3_canDrop"] = r.call("panel.canDrop", source=ids[2], target="LeftPane", op="Fill")
        A["A3_join"] = r.call("panel.join", id="LeftPane", doc=ids[2])
        time.sleep(1.5)
        A["A3_tabs"] = r.call("panel.tabs", id="LeftPane")
        A["cap_A3_joined"] = cap("A3_joined_left")
        A["A3_return"] = r.call("panel.returnDoc", doc=ids[2])
        A["A4_pin"] = r.call("panel.pin", id="BottomPane")
        time.sleep(1)
        A["A4_list_after_pin"] = r.call("panel.list")
        A["cap_A4_after_pin"] = cap("A4_after_pin")
        A["A4_unpin"] = r.call("panel.pin", id="BottomPane")
        # 머리줄 길게 누르기(odev-7c 추가 확인 1): 탐색기 머리줄 제목 위치를 800ms 누른다
        lb = (A["bounds"] or {}).get("LeftPane") or {}
        if lb:
            x, y = int((lb["x"] + 20) * S), int((lb["y"] + 10) * S)
            A["longpress_xy_px"] = [x, y]
            ra.adb(sr, "shell", "input", "swipe", str(x), str(y), str(x), str(y), "800")
            time.sleep(1.2)
            A["cap_longpress"] = cap("H_longpress_explorer_header")
            ra.adb(sr, "shell", "input", "keyevent", "111")  # ESC 로 메뉴 닫기
            time.sleep(0.5)
        # A5: 탭 모드 중앙 문서 1개
        r.call("docs.close", all=True)
        r.call("docs.open", kind="form")
        r.call("layout.template.apply", id="tpl.tabbed")
        time.sleep(1.5)
        A["cap_A5_tabbed_one_doc"] = cap("A5_tabbed_one_doc")
        # 머리줄 제목 en
        A["lang_set_en"] = r.call("lang.set", code="en")
        r.call("layout.template.apply", id="tpl.mdi-cascade")
        time.sleep(1.5)
        A["cap_header_en"] = cap("H_header_en")
        r.call("lang.set", code="ko")
        rep["A"] = A

        # ---- B. 화면 모드 ----
        B = {"unfolded_read": r.call("ui.mode"), "bounds_before": r.call("panel.bounds")}
        B["set_phone"] = r.call("ui.mode", set="phone")
        time.sleep(1.5)
        B["phone_read"] = r.call("ui.mode")
        B["cap_phone_unfolded"] = cap("B2_phone_unfolded")
        B["set_tablet"] = r.call("ui.mode", set="tablet")
        time.sleep(1.5)
        B["tablet_read"] = r.call("ui.mode")
        B["bounds_after"] = r.call("panel.bounds")
        B["cap_tablet_back"] = cap("B2_tablet_back")
        B["set_auto"] = r.call("ui.mode", set="auto")
        ra.adb(sr, "shell", "wm", "size", FOLD)
        time.sleep(2.5)
        B["folded_read"] = r.call("ui.mode")
        B["cap_folded"] = cap("B1_folded")
        B["folded_set_tablet"] = r.call("ui.mode", set="tablet")
        time.sleep(1)
        B["folded_after_set_tablet"] = r.call("ui.mode")
        r.call("ui.mode", set="auto")
        ra.adb(sr, "shell", "wm", "size", "reset")
        time.sleep(2)
        B["unfolded_again"] = r.call("ui.mode")
        # B3: --ui-mode phone 은 저장되지 않아야 한다
        r = start("--ui-mode phone")
        B["arg_phone_read"] = r.call("ui.mode")
        r = start("")
        B["after_restart_read"] = r.call("ui.mode")
        rep["B"] = B

        # ---- C. 핀치 ----
        r = start()
        r.call("docs.close", all=True)
        r.call("docs.open", kind="form")
        time.sleep(1.5)
        C = {"state0": r.call("pinch.state")}
        C["sim_up3"] = r.call("pinch.sim", scales="1.1,1.21,1.33", end=True)
        C["state1"] = r.call("pinch.state")
        C["sim_down2"] = r.call("pinch.sim", scales="0.9,0.82", end=True)
        C["state2"] = r.call("pinch.state")
        C["sim_outside_menu"] = r.call("pinch.sim", scales="1.1,1.21", x=20, y=20, end=True)
        C["state3"] = r.call("pinch.state")
        C["wheel_regress"] = r.call("input.ctrlWheel", delta=1, doc=((r.call("docs.list") or [{}])[0]).get("id"))
        C["wheel_ms"] = r.calls[-1]["elapsed_ms"]
        C["state4"] = r.call("pinch.state")
        C["perf_ops_pinch"] = [o for o in (r.call("perf.ops") or []) if isinstance(o, dict) and "pinch" in str(o.get("name"))]
        # 실 다중 터치: 장치 목록과 sendevent 권한 확인(일반 셸 권한으로 /dev/input 쓰기 가능 여부)
        rc, ev = ra.adb(sr, "shell", "getevent", "-pl")
        open(os.path.join(O, "getevent_pl.txt"), "w", encoding="utf-8").write(ev)
        rc2, ls = ra.adb(sr, "shell", "ls", "-l", "/dev/input/")
        C["dev_input_ls"] = ls
        rc3, se = ra.adb(sr, "shell", "sendevent /dev/input/event0 0 0 0; echo rc=$?")
        C["sendevent_try"] = se.strip()
        rc4, ui = ra.adb(sr, "shell", "pm list instrumentation")
        C["instrumentation"] = ui.strip()
        rep["C"] = C
    finally:
        json.dump(rep, open(os.path.join(O, "func_result.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=1)
        ra.cleanup(sr, state)
        ra.adb(sr, "shell", "wm", "size", "reset")


if __name__ == "__main__":
    main()
