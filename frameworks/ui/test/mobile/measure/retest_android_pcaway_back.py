#!/usr/bin/env python3
# Fold5 BACK 측정(C4) — 펼침·접힘에서 메뉴·서랍·Flyout·시트·패널 메뉴를 실터치로 열고 키·제스처 BACK 뒤 앱 전면 유지와 메뉴 닫힘을 잰다
"""
사용: python3 -B retest_android_pcaway_back.py --apk <APK> --out <폴더> --mode open|closed [--inputs key,gesture] [--diag-release] [--fresh]
      [--reproduced <수정 전 원자료.json>] [--measure]
값만 모은다. --measure 는 pcaway_report 측정 파일(C4_measure.json)을 쓴다. 두 mode 결과를 모아 쓰려면 --merge <원자료.json> 을 더 준다.
--reproduced 는 수정 전 판본에서 잰 원자료다(발견 회차 c4_debug_open.json 등). 앱 이탈 시도가 있어야 back_reproduced_before_fix 가 참이다.
기기 설정 원복은 호출자가 phone_session.sh 와 cmd device_state state reset 으로 한다.
"""
import argparse
import json
import os
import re
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import retest_android_pcaway_lib as L  # noqa: E402


def trial(c, a, name, opener, probe, input_name):
    """opener 로 띄우고 BACK 1회. probe(xml)=메뉴 표식 수. 앱이 빠지면 메뉴 판정은 None 이다(launcher 노드 0개를 닫힘으로 세지 않는다)."""
    L.ensure("CLOSED" if a.mode == "closed" else "OPENED")
    if not L.app_on_top():
        L.relaunch()
    opened = opener()
    time.sleep(1.8)
    before = probe(c.dump("%s_%s_open" % (name, input_name)))
    c.shot("%s_%s_open" % (name, input_name))
    pid0 = c.pid()
    L.adb("logcat", "-c")
    (L.key_back if input_name == "key" else L.gesture_back)()
    time.sleep(1.8)
    host_back = [ln.strip()[:160] for ln in L.adb("logcat", "-d")[1].splitlines() if "[host] back" in ln]  # MainActivity 콜백 경로 판별
    on_top = L.app_on_top()
    top = L.top_activity()
    after = probe(c.dump("%s_%s_after" % (name, input_name))) if on_top else None
    c.shot("%s_%s_after" % (name, input_name))
    rec = {"name": name, "input": input_name, "opener": opened, "menu_before": before, "app_on_top_after": on_top, "menu_after": after, "pid_before": pid0, "pid_after": c.pid(), "top": top[:220], "state": L.dev_state(), "host_back": host_back}
    c.raw.setdefault("trials", []).append(rec)
    print(("앱이탈" if not on_top else "유지") + " %s %s before=%s after=%s host=%s" % (name, input_name, before, after, [h.split("[host] ")[-1] for h in host_back]), flush=True)
    c.check("%s[%s] 메뉴 열림(전제)" % (name, input_name), bool(before), before, "열림")
    c.check("%s[%s] BACK: 앱 전면 유지" % (name, input_name), on_top, top[:120], "앱")
    c.check("%s[%s] BACK: 메뉴 닫힘" % (name, input_name), on_top and not after, after, "닫힘")
    via_key = sum(1 for h in host_back if "via=key" in h)
    via_cb = sum(1 for h in host_back if "via=callback" in h)
    rec["via_key"], rec["via_callback"] = via_key, via_cb
    c.check("%s[%s] logcat via=key 0·callback 1" % (name, input_name), via_key == 0 and via_cb == 1, [h.split("[host] ")[-1] for h in host_back], "key 0, callback 1")
    if not on_top:
        L.relaunch()
    elif after:
        L.key_back()
        time.sleep(1)
        if not L.app_on_top():
            L.relaunch()
    time.sleep(1)


def trial_nothing_open(c, a):
    """열린 것 없을 때 키 BACK 은 앱이 뒤로 가야 정상이다(메뉴 닫기 수정이 뒤로 가기를 삼키지 않는지 본다)."""
    L.ensure("CLOSED" if a.mode == "closed" else "OPENED")
    if not L.app_on_top():
        L.relaunch()
        time.sleep(2)
    L.adb("logcat", "-c")
    L.key_back()
    time.sleep(1.8)
    host_back = [ln.strip()[:160] for ln in L.adb("logcat", "-d")[1].splitlines() if "[host] back" in ln]
    on_top = L.app_on_top()
    cap = c.shot("nothing_open_after")
    rec = {"app_on_top_after": on_top, "top": L.top_activity()[:160], "host_back": host_back, "cap": cap}
    c.raw["nothing_open"] = rec
    print("nothing_open 전면=%s host=%s" % (on_top, [h.split("[host] ")[-1] for h in host_back]), flush=True)
    c.check("열린 것 없을 때 BACK: 앱 이탈", not on_top, rec["top"], "launcher")
    if not on_top:
        L.relaunch()


def ids_prefix(c, prefix):
    return lambda txt: len([i for i in c.ids(txt) if i.startswith(prefix)])


def run_closed(c, a, inputs):
    drawer = ids_prefix(c, "shell.navDrawer.")

    def drawer_sub():
        c.tap_id("shell.btnDrawer")
        time.sleep(1.5)
        ids = [i for i in c.ids(c.dump()) if i.startswith("shell.navDrawer.")]
        return c.tap_id(ids[0]) if ids else False

    for inp in inputs:
        trial(c, a, "drawer", lambda: c.tap_id("shell.btnDrawer"), drawer, inp)
        trial(c, a, "settings_sheet", lambda: c.tap_id("shell.navTab.Settings"), ids_prefix(c, "settings."), inp)
        trial(c, a, "more_tab", lambda: c.tap_id("shell.navTab.more"), drawer, inp)
        trial(c, a, "drawer_sub", drawer_sub, drawer, inp)


def run_open(c, a, inputs):
    txt = c.dump("base")
    tops = sorted(set(re.findall(r'resource-id="(shell\.mnu[A-Za-z]+)"', txt)))
    base = len(re.findall(r'resource-id="(shell\.mnu[^"]*)"', txt))
    menu_probe = lambda t: max(0, len(re.findall(r'resource-id="(shell\.mnu[^"]*)"', t)) - base)
    panel_base = {}

    def panel_menu():
        t = c.dump()
        off = c.bounds_top(t, "android:id/content")
        bl = [tuple(map(int, m.groups())) for m in re.finditer(r'resource-id="PART_MenuButton"[^>]*bounds="\[(\d+),(\d+)\]\[(\d+),(\d+)\]"', t)]
        if not bl:
            return False
        L.sh("input tap %d %d" % ((bl[0][0] + bl[0][2]) // 2, (bl[0][1] + bl[0][3]) // 2 + off))
        return True

    def panel_probe(t):
        return max(0, len(re.findall(r"<node ", t)) - panel_base.get("n", 10 ** 6))

    for inp in inputs:
        trial(c, a, "top_menu", lambda: c.tap_id(tops[0]) if tops else False, menu_probe, inp)
        trial(c, a, "settings_flyout", lambda: c.tap_id("shell.mnuSys") and (time.sleep(1.2) or True) and c.tap_id("shell.mnuSysSettings"), ids_prefix(c, "settings."), inp)
        panel_base["n"] = len(re.findall(r"<node ", c.dump()))
        trial(c, a, "panel_menu", panel_menu, panel_probe, inp)


def summarize(trials, reproduced):
    menu = [t for t in trials if t["name"] in ("top_menu", "panel_menu", "drawer", "more_tab", "drawer_sub")]
    fly = [t for t in trials if t["name"] in ("settings_flyout", "settings_sheet")]
    good = lambda t: t["app_on_top_after"] and not t["menu_after"] and t["menu_before"]
    table = [(t["name"], t["input"], t["app_on_top_after"], t["menu_after"]) for t in trials]
    gest = [t for t in trials if t["input"] == "gesture"]
    return {
        "back_reproduced_before_fix": ({"file": reproduced["file"], "exits": reproduced["exits"]}, reproduced["exits"] > 0),
        "back_closes_menu": (table, bool(menu) and all(good(t) for t in menu)),
        "back_closes_flyout": (table, bool(fly) and all(good(t) for t in fly)),
        "back_no_app_exit": ([t[:3] for t in table], bool(trials) and all(t["app_on_top_after"] for t in trials)),
        "gesture_back": ([t[:4] for t in table if t[1] == "gesture"], bool(gest) and all(good(t) for t in gest)),
    }


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--apk", required=True)
    p.add_argument("--out", required=True)
    p.add_argument("--mode", required=True, choices=("open", "closed"))
    p.add_argument("--inputs", default="key,gesture")
    p.add_argument("--diag-release", action="store_true")
    p.add_argument("--no-remote", action="store_true")
    p.add_argument("--fresh", action="store_true")
    p.add_argument("--reproduced")
    p.add_argument("--merge", action="append", default=[])
    p.add_argument("--measure", action="store_true")
    a = p.parse_args()
    c = L.Ctx(a.mode, "pcaway", a.out)
    c.raw["apk"] = a.apk
    (L.set_closed if a.mode == "closed" else L.set_open)()
    if not L.install(a.apk, fresh=a.fresh):
        print("설치 실패")
        return 1
    want_id = "shell.navTab.Settings" if a.mode == "closed" else "shell.mnuFile"
    if a.no_remote:  # 진단 없는 빌드: 원격 없이 실터치·uiautomator·logcat 만 쓴다. 형은 트리의 대상 노드로 확인한다
        L.sh("am force-stop %s" % L.PKG)
        L.adb("logcat", "-c")
        L.relaunch()
        for _i in range(60):
            if want_id in c.ids(c.dump()):
                break
            time.sleep(1)
        else:
            print("대상 노드 없음", want_id)
            return 1
        c.raw["style"] = {"no_remote": True, "node": want_id}
    else:
        if not L.attach(c, token_from_logcat=a.diag_release):
            print("연결 실패")
            return 1
        c.raw["style"] = L.U.style_wait(c, "Mobile" if a.mode == "closed" else "Tablet", 60)
    inputs = a.inputs.split(",")
    (run_closed if a.mode == "closed" else run_open)(c, a, inputs)
    trial_nothing_open(c, a)
    n, ex = c.fatal_count()
    c.check("치명 0", n == 0, (n, ex), 0)
    raw = L.save_raw(c, "c4_%s" % a.mode)
    if a.measure:
        trials = list(c.raw.get("trials", []))
        for m in a.merge:
            with open(m, encoding="utf-8") as f:
                trials += json.load(f)["raw"].get("trials", [])
        rep = {"file": a.reproduced, "exits": 0}
        if a.reproduced:
            with open(a.reproduced, encoding="utf-8") as f:
                rep["exits"] = sum(1 for t in json.load(f)["raw"].get("trials", []) if not t["app_on_top_after"])
        L.write_measure(a.out, "C4", summarize(trials, rep), "retest_android_pcaway_back.py " + raw)
    return 0


if __name__ == "__main__":
    sys.exit(main())
