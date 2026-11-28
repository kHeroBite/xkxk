#!/usr/bin/env python3
# Fold5 제품판 SDI 실터치(C2)·H-S1 터치(C8)·기기 원복(C7) 측정 — 실터치만 쓰고 uiautomator 노드로 결과를 읽는다
"""
사용:
  retest_android_pcaway_etc.py c2 --apk <제품 APK> --out <폴더> [--fresh] [--measure]    펼침에서 파일 > 새 독립 창 > 그리드 실터치, 창 닫기 버튼 실터치
  retest_android_pcaway_etc.py c8 --apk <Debug APK> --out <폴더> [--fresh] [--measure]   접힘 ☰ 서랍 도움말 또는 진단 시작으로 투어, 다음 버튼 실터치, 설정 시트 실터치
  retest_android_pcaway_etc.py c7 --out <폴더> [--measure]                               원복 값 읽기(기기를 바꾸지 않는다)
사람 확인 잔여: C8 은 터치 감각을 기계로 재지 못한다.
"""
import argparse
import os
import re
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import retest_android_pcaway_lib as L  # noqa: E402

BASE_DENSITY = "360"
BASE_STAY_ON = "0"


def ids_with(c, xml, pat):
    return sorted({i for i in c.ids(xml) if re.search(pat, i)})


def window_count(xml):
    """관리형 창 수. 제품판은 원격 명령이 없어 트리의 창 틀 노드로 센다."""
    return len(re.findall(r'class="[^"]*MdiDocumentWindow"', xml))  # ManagedWindowLayer 는 창이 없어도 항상 1개라 세지 않는다


def c2(a):
    c = L.Ctx("tablet", "Release", a.out)
    L.set_open()
    installed = L.install(a.apk, fresh=a.fresh)
    L.sh("am force-stop %s" % L.PKG)
    L.adb("logcat", "-c")
    L.relaunch()
    time.sleep(8)
    xml0 = c.dump("c2_base")
    n0 = window_count(xml0)
    cap0 = c.shot("c2_base")
    opened = c.tap_id("shell.mnuFile")
    time.sleep(1.5)
    group = c.tap_id("shell.mnuOpenSdiGroup")
    time.sleep(1.8)
    leaf = c.tap_id("shell.mnuOpenGridSdi")
    time.sleep(3)
    xml1 = c.dump("c2_opened")
    n1 = window_count(xml1)
    cap1 = c.shot("c2_opened")
    close_ids = ids_with(c, xml1, r"^chrome\.btnClose$")  # 창 제목줄 닫기. PART_CloseButton 은 패널 닫기라 쓰지 않는다
    c.raw["open"] = {"tap": (opened, group, leaf), "before": n0, "after": n1, "close_ids": close_ids}
    closed = c.tap_id(close_ids[0]) if close_ids else False
    time.sleep(2.5)
    xml2 = c.dump("c2_closed")
    n2 = window_count(xml2)
    cap2 = c.shot("c2_closed")
    c.raw["close"] = {"tap": closed, "after": n2, "on_top": L.app_on_top()}
    n, ex = c.fatal_count()
    raw = L.save_raw(c, "c2")
    if a.measure:
        L.write_measure(a.out, "C2", {
            "product_apk_installed": (a.apk, installed),
            "sdi_real_touch_open": (c.raw["open"], n1 > n0),
            "sdi_real_touch_close": (c.raw["close"], closed and n2 < n1 and L.app_on_top()),
            "no_fatal": ((n, ex), n == 0),
        }, "retest_android_pcaway_etc.py c2 " + raw, (cap1, cap2))
    return cap0


def c8(a):
    c = L.Ctx("phone", "Debug", a.out)
    L.set_closed()
    if not L.install(a.apk, fresh=a.fresh) or not L.attach(c):
        return None
    L.U.style_wait(c, "Mobile", 40)
    c.call("tour.end")
    c.call("tour.start")
    time.sleep(1.5)
    steps = []
    for _ in range(3):
        before = (c.call("tour.state") or [{}])[0].get("stepId")
        ok = c.tap_id("tour.btnNext")
        time.sleep(1.2)
        after = (c.call("tour.state") or [{}])[0].get("stepId")
        steps.append({"tap": ok, "before": before, "after": after})
    cap0 = c.shot("c8_tour")
    closed = c.tap_id("tour.btnClose")
    time.sleep(1)
    c.call("tour.end")
    time.sleep(1)
    c.raw["tour"] = steps
    c.raw["tour_close"] = closed
    ok = c.tap_id("shell.navTab.Settings")
    time.sleep(2.5)
    nodes = ids_with(c, c.dump("c8_sheet"), r"^settings\.")
    cap1 = c.shot("c8_sheet")
    c.raw["sheet"] = {"tap": ok, "nodes": len(nodes)}
    c.tap_id("shell.navTab.Settings")
    time.sleep(1.5)
    raw = L.save_raw(c, "c8")
    if a.measure:
        L.write_measure(a.out, "C8", {
            "hs1_tour_touch": (steps, bool(steps) and all(s["tap"] and s["before"] != s["after"] for s in steps)),
            "hs1_settings_sheet_touch": (c.raw["sheet"], ok and len(nodes) >= 3),
        }, "retest_android_pcaway_etc.py c8 " + raw, (cap0, cap1))
    return cap1


def c7(a):
    os.makedirs(a.out, exist_ok=True)
    density = L.sh("wm density")
    stay = L.sh("settings get global stay_on_while_plugged_in")
    # 'package:<이름>' 줄 단위 정확 비교 — 본 앱(PKG 의 상위 이름)과 시험대(PKG) 줄은 제외하고 그 아래 접두의 시험용 별도 패키지만 남긴다
    base = L.PKG.rsplit(".", 1)[0]
    keep = {"package:" + base, "package:" + L.PKG}
    pkgs = "\n".join(ln for ln in L.sh("pm list packages " + base + ".").splitlines()
                     if ln.strip().startswith("package:" + base + ".") and ln.strip() not in keep)
    st = L.sh("cmd device_state state")
    print(density, stay, pkgs, st, flush=True)
    if a.measure:
        L.write_measure(a.out, "C7", {
            "density_restored": (density, "Override density: %s" % BASE_DENSITY in density),
            "stay_on_restored": (stay, stay == BASE_STAY_ON),
            "no_test_package_left": (pkgs, pkgs == ""),
            "device_state_override_empty": (st, "OPENED" in st and "Base" not in st.replace("Committed", "")),
        }, "retest_android_pcaway_etc.py c7 adb 실측")


def main():
    p = argparse.ArgumentParser()
    p.add_argument("cmd", choices=("c2", "c7", "c8"))
    p.add_argument("--apk")
    p.add_argument("--out", required=True)
    p.add_argument("--fresh", action="store_true")
    p.add_argument("--measure", action="store_true")
    a = p.parse_args()
    {"c2": c2, "c7": c7, "c8": c8}[a.cmd](a)
    return 0


if __name__ == "__main__":
    sys.exit(main())
