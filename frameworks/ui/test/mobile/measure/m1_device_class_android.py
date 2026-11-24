# Fold5 실기 기기 판별(T7 접힘/펼침·분할/팝업 창 폭)과 패널 기능 끔 끌어 빼기를 값만 모으는 측정 도구 — 판정은 호출자(otest)가 한다
"""
사용: python3 -u -B m1_device_class_android.py --apk <진단 APK> --out <폴더> [--steps t7,mw,panel,selftest]
- 앱 삭제(uninstall)는 하지 않는다. install -r 만 쓴다(서명 불일치면 중단하고 보고).
- 접힘/펼침: cmd device_state state 0 / reset (실제 힌지 상태 오버라이드), 보조로 wm size 904x2316.
- 창 폭: window_mode.sh popup(freeform). 분할 화면 진입 adb 표준 명령이 없어 팝업 창으로 대체한다(multiWindow 값은 그대로 읽는다).
- 끝에서 device_state reset, wm size reset(density 360 은 건드리지 않는다), 앱 설정(패널 기능·compact·UI 형)을 시작값으로 되돌린다.
- 포트는 retest_android_pcaway_lib 의 15960 forward(5180·5220 아님).
"""
import argparse
import hashlib
import json
import os
import re
import subprocess
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import retest_android_pcaway_lib as L  # noqa: E402

ra = L.ra
ROOT = os.path.abspath(os.path.join(HERE, "..", "..", "..", "..", ".."))
WM = os.path.join(ROOT, ".claude", "skills", "otest_mobile_phone", "scripts", "window_mode.sh")
R = {"steps": {}}


def now():
    return time.strftime("%H:%M:%S")


def call(c, cmd, **a):
    try:
        return c.call(cmd, **a)
    except Exception as e:  # noqa: BLE001
        return {"_error": str(e)[:300]}


def classify(c):
    return call(c, "ui.classify")


def style(c):
    return call(c, "ui.style")


def wait_attach(c, n=30):
    """프로세스는 살아 있는 채 원격 서버에 다시 붙는다(재기동 없음)."""
    for _ in range(n):
        r = classify(c)
        if isinstance(r, dict) and "_error" not in r:
            return r
        time.sleep(1)
    return None


def snap(c, label):
    cl = classify(c)
    st = style(c)
    row = {"t": now(), "label": label, "classify": cl, "style": st, "state": L.dev_state(), "wm": L.sh("wm size"), "pid": L.sh("pidof %s" % L.PKG)}
    print(label, json.dumps({"device": (cl or {}).get("device"), "effective": (cl or {}).get("effective"), "traits": (cl or {}).get("hostTraits")}, ensure_ascii=False)[:400], flush=True)
    return row


def settle_style(c, want=None, limit=12):
    t0, cl = time.time(), None
    while time.time() - t0 < limit:
        cl = classify(c)
        if isinstance(cl, dict) and (want is None or cl.get("effective") == want):
            break
        time.sleep(1)
    return cl


def settings_open(c):
    """sys.open view=Settings 로 설정 화면을 연 뒤 '창' 페이지로 간다. 이미 열려 있으면 그대로 쓴다."""
    r = call(c, "settings.window", category="Window")
    if not r or (isinstance(r, dict) and "_error" in r):  # c.call 은 오류 시 None 을 돌려준다
        call(c, "sys.open", view="Settings")
        time.sleep(1.5)
        r = call(c, "settings.window", category="Window")
    time.sleep(0.8)
    return r


def set_combo(c, index):
    settings_open(c)
    call(c, "ui.select", id="settings.cmbPanelFeature", index=index)
    time.sleep(0.8)
    node = call(c, "ui.find", id="settings.cmbPanelFeature") or {}
    sel = node.get("selectedIndex")
    call(c, "settings.esc")
    time.sleep(1.0)
    return sel


def set_compact(c, on):
    settings_open(c)
    r = call(c, "ui.toggle", id="settings.chkCompactWindowKeepsTablet", on=str(bool(on)).lower())
    time.sleep(0.8)
    node = call(c, "ui.find", id="settings.chkCompactWindowKeepsTablet") or {}
    chk = node.get("checked")
    call(c, "settings.esc")
    time.sleep(1.0)
    return {"toggle": r, "IsChecked": chk}


def read_initial(c):
    settings_open(c)
    n1 = call(c, "ui.find", id="settings.cmbPanelFeature") or {}
    n2 = call(c, "ui.find", id="settings.chkCompactWindowKeepsTablet") or {}
    call(c, "settings.esc")
    time.sleep(1.0)
    return {"panelFeatureIndex": n1.get("selectedIndex"), "compact": n2.get("checked"), "style": style(c)}


def wm_script(*args):
    act = L.sh("cmd package resolve-activity --brief %s" % L.PKG).splitlines()[-1].split("/")[-1].strip()  # 빌드마다 해시가 바뀌므로 매번 조회
    env = dict(os.environ, ADB=ra.ADB, SERIAL=L.SER, PKG=L.PKG, ACT=act)
    p = subprocess.run(["bash", WM] + list(args), capture_output=True, text=True, timeout=80, env=env)
    return p.returncode, (p.stdout + p.stderr).strip()[-600:]


def topact():
    return L.sh("dumpsys activity activities | grep -a 'ResumedActivity' | head -2")


def step_t7(c):
    out = {}
    out["open_initial"] = snap(c, "T7 펼침(초기)")
    out["cap_open"] = c.shot("t7_open")
    L.set_closed()
    time.sleep(5)
    c.call("ui.classify") if False else None
    wait_attach(c)
    settle_style(c, "Mobile", 12)
    out["closed_device_state"] = snap(c, "T7 접힘(device_state)")
    out["closed_top"] = topact()
    out["cap_closed"] = c.shot("t7_closed")
    L.set_open()
    time.sleep(5)
    wait_attach(c)
    settle_style(c, "Tablet", 12)
    out["reopen"] = snap(c, "T7 다시 펼침")
    out["cap_reopen"] = c.shot("t7_reopen")
    # 보조 수단: wm size (실제 접힘이 아니라 창 크기만 바꾼다) — density 360 은 건드리지 않는다
    L.sh("wm size 904x2316")
    time.sleep(5)
    wait_attach(c)
    settle_style(c, None, 6)
    out["wm_size_substitute"] = snap(c, "T7 보조 wm size 904x2316")
    L.sh("wm size reset")
    time.sleep(4)
    wait_attach(c)
    out["after_wm_reset"] = snap(c, "T7 wm reset 뒤")
    R["steps"]["t7"] = out


def popup_measure(c, tag, rect):
    rc, txt = wm_script("popup", *[str(x) for x in rect])
    time.sleep(3)
    cl = wait_attach(c) or {}
    time.sleep(2)
    row = snap(c, "MW %s" % tag)
    row["window_mode_rc"] = rc
    row["window_mode_out"] = txt
    row["bounds"] = wm_script("bounds")[1]
    row["cap"] = c.shot("mw_%s" % tag.replace(" ", "_"))
    return row


def step_mw(c):
    out = {"initial": read_initial(c)}
    comp0 = out["initial"].get("compact")
    for compact in (False, True):
        out["set_compact_%s" % compact] = set_compact(c, compact)
        key = "compact_%s" % ("on" if compact else "off")
        out[key] = {"full": snap(c, "MW 전체 화면 %s" % key)}
        out[key]["popup_narrow"] = popup_measure(c, "%s_narrow" % key, (50, 150, 910, 2050))
        out[key]["popup_wide"] = popup_measure(c, "%s_wide" % key, (50, 150, 1700, 2050))
        rc, t = wm_script("full")
        out[key]["full_rc"] = (rc, t)
        time.sleep(3)
        L.attach(c, restart=True, token_from_logcat=True, wait=60)
        wait_attach(c)
        out[key]["after_full"] = snap(c, "MW full 복귀 %s" % key)
    out["restore_compact"] = set_compact(c, comp0 is True)
    R["steps"]["mw"] = out


def doc_ids(c):
    return [d.get("id") for d in (call(c, "docs.list") or []) if isinstance(d, dict)]


def panel_matrix(c, tag):
    out = {"style": style(c)}
    for d in doc_ids(c):
        call(c, "docs.close", id=d)
    call(c, "docs.close", all=True)
    time.sleep(1)
    o1 = call(c, "docs.open", kind="grid", title="m1grid")
    o2 = call(c, "docs.open", kind="gantt", title="m1gantt")
    time.sleep(2)
    ids = doc_ids(c)
    out["docs"] = ids
    for mode_name, idx in (("Auto", 0), ("Off", 2), ("On", 1)):
        m = {"selected": set_combo(c, idx)}
        time.sleep(1)
        m["style"] = style(c)
        ids = doc_ids(c)
        if not ids:
            m["error"] = "문서 없음"
            out[mode_name] = m
            continue
        d = ids[0]
        m["pin"] = call(c, "panel.pin", id="LeftPane", on="true")
        call(c, "panel.pin", id="LeftPane", on="false")
        m["join"] = call(c, "panel.join", id="LeftPane", doc=d)
        time.sleep(1.5)
        m["canDrop_panelDoc"] = {op: call(c, "panel.canDrop", source=d, target="Documents", op=op) for op in ("Fill", "Window", "Left", "Right", "Top", "Bottom")}
        m["canDrop_toPanel_for_other_doc"] = call(c, "panel.canDrop", source=ids[-1], target="LeftPane", op="Fill", inner="true")
        m["docs_after_join"] = call(c, "docs.list")
        m["panels_after_join"] = call(c, "panel.list")
        m["returnDoc"] = call(c, "panel.returnDoc", doc=d)
        time.sleep(1.5)
        m["docs_after_return"] = call(c, "docs.list")
        m["panels_after_return"] = call(c, "panel.list")
        out[mode_name] = m
        print(tag, mode_name, "join", (m["join"] or {}).get("status") if isinstance(m["join"], dict) else m["join"], "return", m["returnDoc"], flush=True)
        call(c, "docs.close", all=True)
        call(c, "docs.open", kind="grid", title="m1grid")
        call(c, "docs.open", kind="gantt", title="m1gantt")
        time.sleep(1.5)
    out["cap"] = c.shot("panel_%s" % tag)
    call(c, "docs.close", all=True)
    return out


def step_panel(c):
    out = {"initial": read_initial(c)}
    init_idx = out["initial"].get("panelFeatureIndex")
    out["tablet_auto_style"] = settle_style(c, "Tablet", 8)
    out["tablet"] = panel_matrix(c, "tablet")
    sel = call(c, "ui.style", set="pc")
    out["set_pc"] = sel
    time.sleep(3)
    out["pc_style"] = settle_style(c, "Pc", 10)
    out["pc"] = panel_matrix(c, "pc")
    out["restore_style"] = call(c, "ui.style", set="auto")
    time.sleep(2)
    out["restore_combo"] = set_combo(c, int(init_idx) if isinstance(init_idx, int) else 0)
    R["steps"]["panel"] = out


def step_persist(c):
    """옵션 저장·재기동 유지 — compact 켬 + 패널 기능 끔으로 저장한 뒤 앱을 재기동해 값을 다시 읽고, 시작값으로 되돌린다."""
    out = {"initial": read_initial(c)}
    comp0, idx0 = out["initial"].get("compact"), out["initial"].get("panelFeatureIndex")
    out["set"] = {"compact": set_compact(c, True), "combo": set_combo(c, 2)}
    call(c, "layout.save")
    time.sleep(1)
    L.attach(c, restart=True, token_from_logcat=True, wait=60)
    wait_attach(c, 40)
    time.sleep(3)
    out["after_restart"] = read_initial(c)
    out["classify_after_restart"] = classify(c)
    out["restore"] = {"compact": set_compact(c, comp0 is True), "combo": set_combo(c, int(idx0) if isinstance(idx0, int) else 0)}
    out["restored"] = read_initial(c)
    R["steps"]["persist"] = out


def step_selftest(c, info_path):
    out = {"style.selftest": call(c, "style.selftest")}
    j = os.path.join(HERE, "..", "..", "desktop", "judge", "device_class_table.py")
    p = subprocess.run([sys.executable, "-I", j, "--info", info_path], capture_output=True, text=True, timeout=80)
    out["device_class_table_rc"] = p.returncode
    out["device_class_table_stdout"] = p.stdout[-3000:]
    out["device_class_table_stderr"] = p.stderr[-600:]
    R["steps"]["selftest"] = out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--apk", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--steps", default="selftest,t7,mw,panel")
    a = ap.parse_args()
    os.makedirs(a.out, exist_ok=True)
    c = L.Ctx("m1", "Release", a.out)
    R["apk"] = a.apk
    R["apk_md5"] = hashlib.md5(open(a.apk, "rb").read()).hexdigest()
    R["device_before"] = {"wm": L.sh("wm size"), "density": L.sh("wm density"), "stay_on": L.sh("settings get global stay_on_while_plugged_in"), "state": L.dev_state(), "rotation": L.sh("settings get system accelerometer_rotation")}
    L.sh("svc power stayon usb")
    try:
        L.set_open()
        ok = L.install(a.apk, fresh=False)
        R["install_ok"] = ok
        if not ok:
            print("설치 실패(서명 불일치 가능) — 중단")
            return 3
        path = L.sh("pm path %s" % L.PKG)
        R["installed_path"] = path
        R["installed_md5"] = L.sh("md5sum %s | cut -d' ' -f1" % path.replace("package:", "").strip())
        R["dumpsys_pkg"] = L.sh("dumpsys package %s | grep -E 'versionName|versionCode|lastUpdateTime'" % L.PKG)
        info = L.attach(c, restart=True, token_from_logcat=True, wait=90)
        if not info:
            print("원격 연결 실패")
            return 2
        wait_attach(c, 40)
        R["boot"] = snap(c, "기동 직후")
        for s in a.steps.split(","):
            s = s.strip()
            try:
                if s == "selftest":
                    step_selftest(c, info)
                elif s == "t7":
                    step_t7(c)
                elif s == "mw":
                    step_mw(c)
                elif s == "panel":
                    step_panel(c)
                elif s == "persist":
                    step_persist(c)
            except Exception as e:  # noqa: BLE001
                R["steps"][s] = {"EXCEPTION": repr(e)[:500]}
                print("단계 예외", s, repr(e), flush=True)
            with open(os.path.join(a.out, "m1_measure.json"), "w", encoding="utf-8") as f:
                json.dump(R, f, ensure_ascii=False, indent=1, default=str)
        R["fatal"] = c.fatal_count()
    finally:
        L.sh("cmd device_state state reset")
        L.sh("wm size reset")
        wm_script("full")
        L.sh("input keyevent KEYCODE_BACK") if False else None
        L.sh("svc power stayon %s" % ("false" if R["device_before"]["stay_on"] == "0" else "usb"))
        R["device_after"] = {"wm": L.sh("wm size"), "density": L.sh("wm density"), "stay_on": L.sh("settings get global stay_on_while_plugged_in"), "state": L.dev_state(), "rotation": L.sh("settings get system accelerometer_rotation")}
        adb_fw = ra.adb(L.SER, "forward", "--remove", "tcp:%d" % ra.HOST_PORT)
        R["forward_removed"] = adb_fw[0]
        with open(os.path.join(a.out, "m1_measure.json"), "w", encoding="utf-8") as f:
            json.dump(R, f, ensure_ascii=False, indent=1, default=str)
        print("완료", os.path.join(a.out, "m1_measure.json"), flush=True)
    return 0


if __name__ == "__main__":
    sys.exit(main())
