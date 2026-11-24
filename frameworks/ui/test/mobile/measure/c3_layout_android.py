#!/usr/bin/env python3
# c3 UI 형 규칙(L2·L3·L4) 모바일 에뮬레이터 측정 — ui.style·nav.menu·menu.list 원격 값과 adb 화면 캡처만 모으고 판정 파일을 함께 남긴다
"""
사용: python3 c3_layout_android.py --serial emulator-5554 --role phone|tablet --apk <APK> --out <결과 폴더>
측정: 원자료(진단 응답·캡처·log.tail)를 <out>/<role>_c3.json 에 모은다. 판정은 같은 파일의 checks 목록(이름·값·기대)이다.
원복: finally 에서 wm size/density reset, 회전·애니메이터 배율 원값, 앱 제거, forward 해제.
"""
import argparse
import json
import os
import sys
import time

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "common"))  # test/common/_paths.py
import _paths  # noqa: E402
_paths.add()
import retest_android as ra  # noqa: E402
import retest_remote as rr  # noqa: E402

SETTINGS = [("global", "animator_duration_scale"), ("global", "window_animation_scale"),
            ("global", "transition_animation_scale"), ("system", "accelerometer_rotation"), ("system", "user_rotation")]


class Ctx:
    def __init__(self, serial, out, role):
        self.s, self.out, self.role = serial, out, role
        self.checks, self.raw, self.orig = [], {}, {}
        self.r = None

    def adb(self, *a, **k):
        return ra.adb(self.s, *a, **k)

    def sh(self, cmd):
        return self.adb("shell", cmd)[1].strip()

    def shot(self, name):
        rc, data = self.adb("exec-out", "screencap", "-p", binary=True)
        p = os.path.join(self.out, "%s_%s.png" % (self.role, name))
        with open(p, "wb") as f:
            f.write(data)
        return os.path.basename(p)

    def content_top(self):
        """android:id/content 윗변(화면 픽셀) = 상태 표시줄 높이. Avalonia 접근성·ui.find bounds 는 이만큼 위로 어긋나므로 실터치 y 에 더한다(ui28_android.tap_id 와 같은 방법)."""
        import re as _re
        self.adb("shell", "uiautomator", "dump", "/sdcard/ui_top.xml")
        rc, data = self.adb("exec-out", "cat", "/sdcard/ui_top.xml", binary=True)
        m = _re.search(r'resource-id="android:id/content"[^>]*bounds="\[(\d+),(\d+)\]\[(\d+),(\d+)\]"', data.decode("utf-8", "replace"))
        return int(m.group(2)) if m else 0

    def dump(self, name):
        self.adb("shell", "uiautomator", "dump", "/sdcard/ui.xml")
        rc, data = self.adb("exec-out", "cat", "/sdcard/ui.xml", binary=True)
        for _ in range(3):  # 호스트 과부하로 뜨는 시스템 ANR 대화상자("isn't responding")는 앱 화면을 가리므로 '대기'를 눌러 닫는다
            txt = data.decode("utf-8", "replace")
            import re as _re
            m = _re.search(r'resource-id="android:id/aerr_wait"[^>]*bounds="\[(\d+),(\d+)\]\[(\d+),(\d+)\]"', txt)
            if not m:
                break
            x1, y1, x2, y2 = map(int, m.groups())
            self.adb("shell", "input", "tap", str((x1 + x2) // 2), str((y1 + y2) // 2))
            self.raw.setdefault("system_anr_dialog_dismissed", []).append(name)
            time.sleep(2)
            self.adb("shell", "uiautomator", "dump", "/sdcard/ui.xml")
            rc, data = self.adb("exec-out", "cat", "/sdcard/ui.xml", binary=True)
        p = os.path.join(self.out, "%s_%s.xml" % (self.role, name))
        with open(p, "wb") as f:
            f.write(data)
        return data.decode("utf-8", "replace")

    def check(self, name, ok, value, expect, proof=""):
        expect = sorted(expect) if isinstance(expect, (set, frozenset)) else expect
        self.checks.append({"name": name, "pass": bool(ok), "value": value, "expect": expect, "proof": proof})
        print("  %s %s value=%s expect=%s" % ("PASS" if ok else "FAIL", name, str(value)[:160], expect))

    def style(self, wait=0.0, expect=None, limit=25.0):
        """ui.style 를 읽는다. expect(effective) 에 닿거나 값이 연속 2회 같을 때까지(최대 limit초) 1초 간격으로 다시 읽고
        _settle_ms(최초 읽기부터 안정까지)를 기록한다 — 호스트 부하로 디바운스·애니메이션이 늦어지는 경우를 구분하기 위해서다."""
        if wait:
            time.sleep(wait)
        t0, prev, st, same = time.time(), None, {}, 0
        while True:
            st = self.r.call("ui.style") or {}
            key = (st.get("effective"), st.get("device"), st.get("width"))
            same = same + 1 if key == prev else 0
            prev = key
            if (expect is None and same >= 1) or (expect is not None and st.get("effective") == expect and same >= 1):
                break
            if time.time() - t0 > limit:
                break
            time.sleep(1.0)
        st["_settle_ms"] = int((time.time() - t0) * 1000)
        return st

    def logs(self, n=40):
        res = self.r.call("log.tail", n=n) or {}
        lines = res.get("lines") if isinstance(res, dict) else res
        return [l for l in (lines or []) if "ui.style" in str(l)]


def device_info(c):
    c.raw["wm_size"] = c.sh("wm size")
    c.raw["wm_density"] = c.sh("wm density")
    c.raw["mem_total"] = c.sh("cat /proc/meminfo | head -1")
    c.raw["avd"] = c.adb("emu", "avd", "name")[1].strip().splitlines()[:1]
    c.raw["apk_md5"] = None


def dp_size(c):
    import re
    m = re.search(r"Physical size: (\d+)x(\d+)", c.raw["wm_size"])
    d = re.search(r"Physical density: (\d+)", c.raw["wm_density"])
    return int(m.group(1)), int(m.group(2)), int(d.group(1)) / 160.0


def phase_basic(c):
    st = c.style(1.0, expect="Mobile" if c.role == "phone" else "Tablet")
    c.raw["style_initial"] = st
    want_dev = "Phone" if c.role == "phone" else "Tablet"
    c.check("L2 기기 판별", st.get("device") == want_dev, st.get("device"), want_dev)
    want_eff = "Mobile" if c.role == "phone" else "Tablet"
    c.check("L2 자동 기본형", st.get("effective") == want_eff and st.get("setting") == "Auto",
            "%s/%s" % (st.get("setting"), st.get("effective")), "Auto/" + want_eff)
    want_allowed = ["Mobile"] if c.role == "phone" else {"Auto", "Pc", "Tablet", "Mobile"}  # allowed 에는 자동 항목 포함(계획 §3-2: 자동·PC형·태블릿형·모바일형)
    got = st.get("allowed") or []
    c.check("L2 선택 가능 집합", (got == want_allowed) if c.role == "phone" else (set(got) == want_allowed),
            got, want_allowed)
    c.check("L2 선택기 표시", st.get("selectorVisible") is (c.role != "phone"), st.get("selectorVisible"),
            c.role != "phone")
    # 보기 메뉴의 화면 방식 항목은 요건 7 에서 설정 창으로 옮겼다: 메뉴에 남아 있지 않아야 한다
    ml = c.r.call("menu.list", id="shell.mnuView") or {}
    c.raw["menu_view"] = ml
    items = {i.get("id"): i for i in (ml.get("items") if isinstance(ml, dict) else ml) or []}
    left = [k for k in items if str(k).startswith("shell.mnuUiStyle")]
    c.check("L2 보기메뉴 화면 방식 항목 없음", not left, left, "[]")
    # 제목줄 스위치
    for key in ("Pc", "Tablet", "Mobile"):
        f = c.r.call("ui.find", id="shell.swUiStyle" + key) or {}
        c.raw["sw_" + key] = f
        want_vis = c.role != "phone"
        c.check("L2 제목줄 스위치 %s %s" % (key, "표시" if want_vis else "숨김"),
                bool(f) and f.get("effectivelyVisible") is want_vis, f.get("effectivelyVisible"), want_vis)
    c.shot("00_initial")


def phase_nav(c):
    nav = c.r.call("nav.menu") or {}
    p = os.path.join(c.out, "%s_nav_menu.json" % c.role)
    with open(p, "w", encoding="utf-8") as f:
        json.dump(nav, f, ensure_ascii=False, indent=1)
    c.raw["nav_menu_file"] = os.path.basename(p)
    # 모바일형 화면이어야 탭·서랍이 보이므로 phone 은 기본, tablet 은 set=mobile 로 전환 뒤 확인
    if c.role == "tablet":
        c.r.call("ui.style", set="mobile")
        c.style(1.5, expect="Mobile")
        time.sleep(2)  # 전환 애니메이션·레이아웃 완료 대기
    c.shot("05_nav_mobile")
    # 탭·☰·서랍은 앱 원격 ui.find(effectivelyVisible·bounds)로 확인한다 — 호스트 과부하로 시스템 ANR 대화상자가 계속 떠도 앱 화면 판정에 영향이 없다.
    tab_ids = ["shell.navTab." + str(t) for t in (nav.get("tabs") or [])]
    seen = {}
    for tid in tab_ids + ["shell.navTab.more"]:
        for _ in range(8):  # 레이아웃 지연 대비 최대 약 16초
            f = c.r.call("ui.find", id=tid) or {}
            if f.get("effectivelyVisible"):
                break
            time.sleep(2)
        seen[tid] = {k: f.get(k) for k in ("effectivelyVisible", "bounds", "enabled")}
    c.raw["tabs_seen"] = seen
    vis = [t for t, v in seen.items() if v.get("effectivelyVisible")]
    c.check("L4 하단 탭 4+더보기", len(tab_ids) == 4 and len(vis) == 5 and "shell.navTab.more" in vis,
            sorted(vis), "탭 4 + shell.navTab.more 모두 보임")
    d = c.r.call("ui.find", id="shell.btnDrawer") or {}
    c.raw["btnDrawer"] = d
    c.check("L4 왼쪽 ☰ 버튼", bool(d) and d.get("effectivelyVisible") is True, d.get("effectivelyVisible"), True)
    bd = d.get("bounds") or {}
    if bd:
        w, h, dens = dp_size(c)
        cx_dp = bd.get("x", 0) + bd.get("w", 0) / 2.0
        c.check("L4 ☰ 왼쪽 배치", cx_dp < (w / dens) / 2, "중심 x=%.0fdp / 폭 %.0fdp" % (cx_dp, w / dens), "왼쪽 절반")
        # 실터치(adb input tap)로 먼저 열어 보고, 안 열리면 원격 클릭으로 연다
        px, py = int((bd.get("x", 0) + bd.get("w", 0) / 2.0) * dens), int((bd.get("y", 0) + bd.get("h", 0) / 2.0) * dens)
        top = c.content_top()  # 상태 표시줄 오프셋(px)
        py += top
        c.raw["btnDrawer_tap_px"] = [px, py]
        c.raw["btnDrawer_tap_y_offset"] = top
        drawer_ids = ["shell.navDrawer." + str(e.get("id")) for e in (nav.get("drawer") or [])]
        c.adb("shell", "input", "tap", str(px), str(py))
        time.sleep(2)
        probe = c.r.call("ui.find", id=drawer_ids[0]) or {} if drawer_ids else {}
        c.raw["drawer_open_by"] = "adb input tap"
        if not probe.get("effectivelyVisible"):
            c.r.call("ui.click", id="shell.btnDrawer")
            c.raw["drawer_open_by"] = "remote ui.click (adb tap 후 미열림)"
            time.sleep(2)
        c.shot("10_drawer_open")
        shown = []
        for did in drawer_ids:
            f = c.r.call("ui.find", id=did) or {}
            if f.get("effectivelyVisible"):
                shown.append(did)
        c.raw["drawer_ids_seen_visible"] = shown
        c.check("L4 서랍 열림(항목 노출)", len(shown) > 0, "%d/%d" % (len(shown), len(drawer_ids)), ">0 (스크롤 밖 항목은 제외)")
        c.adb("shell", "input", "keyevent", "KEYCODE_BACK")
        time.sleep(0.8)
    # 판정기(정적)
    import subprocess
    ui_root = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", ".."))  # 이 체크아웃의 영역 루트(frameworks/ui)
    judge = os.path.join(ui_root, "test", "common", "judge", "nav_reach_judge.py")
    menu_dir = os.path.join(ui_root, "src", "Pd.Ui.Views", "Menus")
    pr = subprocess.run([sys.executable, "-B", judge, "--menu-dir", menu_dir, "--dump", p], capture_output=True, text=True)
    c.raw["nav_reach_judge"] = {"rc": pr.returncode, "out": pr.stdout[-1500:], "err": pr.stderr[-500:]}
    c.check("L4 전체 메뉴 도달 100%(nav_reach_judge)", pr.returncode == 0, "rc=%d" % pr.returncode, "rc=0", pr.stdout[-200:].strip())
    if c.role == "tablet":
        c.r.call("ui.style", set="auto")
        c.style(1.5)


def phase_rotation(c):
    c.adb("shell", "settings", "put", "system", "accelerometer_rotation", "0")
    c.adb("shell", "settings", "put", "system", "user_rotation", "1")
    time.sleep(3)
    st = c.style(1.0)
    c.raw["style_landscape"] = st
    want = "Mobile" if c.role == "phone" else "Tablet"
    c.check("L2 회전(가로) 짧은 변 기준 유지", st.get("effective") == want and st.get("device") == ("Phone" if c.role == "phone" else "Tablet"),
            "%s/%s w=%s" % (st.get("device"), st.get("effective"), st.get("width")), "device 유지 + " + want)
    c.shot("20_landscape")
    c.adb("shell", "settings", "put", "system", "user_rotation", "0")
    time.sleep(2.5)
    st = c.style(1.0)
    c.check("L2 회전 복귀", st.get("effective") == want, st.get("effective"), want)


def set_width_dp(c, w_dp, h_dp):
    w, h, dens = dp_size(c)
    c.adb("shell", "wm", "size", "%dx%d" % (int(w_dp * dens), int(h_dp * dens)))


def phase_resize(c):
    """창 크기 변화(wm size) — 자동일 때 짧은 변 기준 판정, 수동 선택 유지."""
    w, h, dens = dp_size(c)
    base_w, base_h = int(w / dens), int(h / dens)
    rows = []
    # 폰 800x1200dp = 짧은 변 800 ≥600 → 기기 판정 태블릿 → 태블릿형(plan 3-2·결정 6)
    plan = ([(450, 900, "Mobile"), (800, 1200, "Tablet"), (393, 851, "Mobile")] if c.role == "phone" else
            [(500, 900, "Mobile"), (800, 1280, "Tablet"), (620, 1000, None), (900, 1280, "Tablet"), (base_w, base_h, "Tablet")])
    for wd, hd, want in plan:
        set_width_dp(c, wd, hd)
        st = c.style(1.0, expect=want)
        rows.append({"dp": [wd, hd], "style": st})
        if want:
            c.check("L2/L3 자동 크기 %dx%ddp" % (wd, hd), st.get("effective") == want,
                    "%s/%s w=%s" % (st.get("device"), st.get("effective"), st.get("width")), want)
        c.shot("30_resize_%dx%d" % (wd, hd))
    c.raw["resize_rows"] = rows
    c.adb("shell", "wm", "size", "reset")
    time.sleep(2)
    if c.role == "tablet":
        # 수동 선택 유지: PC형 선택 뒤 폭을 줄여도(폰 판정 아님, 폭 700dp) 유지
        res = c.r.call("ui.style", set="pc")
        st = c.style(1.5)
        c.check("L2 수동 PC형 선택", st.get("effective") == "Pc" and st.get("setting") == "Pc", "%s/%s" % (st.get("setting"), st.get("effective")), "Pc/Pc")
        c.shot("31_manual_pc")
        set_width_dp(c, 700, 1100)
        st = c.style(2.0)
        c.check("L3 수동 선택은 폭과 무관하게 유지", st.get("effective") == "Pc", "%s/%s" % (st.get("setting"), st.get("effective")), "Pc 유지")
        c.adb("shell", "wm", "size", "reset")
        time.sleep(1.5)
        c.r.call("ui.style", set="auto")
        c.style(1.5)
    if c.role == "phone":
        for bad in ("pc", "tablet"):
            ok = c.r.call("ui.style", set=bad) is None  # 허용 밖은 오류 → 결과 null
            c.check("L2 폰 %s형 선택 거부" % bad, ok, "거부" if ok else "수락됨", "거부")


def phase_transition(c):
    """L3 전환 애니메이션: 기본 배율(중간 프레임 캡처)과 애니메이터 0(즉시 전환)."""
    if c.role != "tablet":
        return
    # 기본 배율에서 전환 시작 직후 연속 캡처
    c.r.call("ui.style", set="mobile")
    frames = []
    t0 = time.time()
    for i in range(3):
        frames.append({"t_ms": int((time.time() - t0) * 1000), "file": c.shot("40_anim_frame%d" % i)})
    c.raw["anim_frames"] = frames
    c.style(1.5)
    lg = c.logs()
    c.raw["transition_log_default"] = lg
    c.check("L3 전환 로그 animated=true(기본 배율)", any("transition" in str(l) and "animated=True" in str(l).replace("true", "True") for l in lg),
            [str(l)[-120:] for l in lg[-3:]], "animated=true ms≈300")
    # 애니메이터 배율 0
    for ns, key in SETTINGS[:3]:
        c.adb("shell", "settings", "put", ns, key, "0")
    time.sleep(1)
    c.r.call("ui.style", set="tablet")
    c.style(1.5)
    lg0 = c.logs()
    c.raw["transition_log_scale0"] = lg0
    last = [str(l) for l in lg0 if "transition" in str(l)][-1:] or [""]
    c.check("L3 애니메이터 배율 0 즉시 전환(ms=0·animated=false)",
            ("animated=false" in last[0].lower() or "animated=False" in last[0]) and ("ms=0" in last[0]),
            last[0][-160:], "animated=false ms=0")
    c.r.call("ui.style", set="auto")
    c.style(1.0)


def phase_preserve(c):
    """L4 형 전환 때 열린 문서·입력 보존."""
    if c.role != "tablet":
        return
    c.r.call("docs.open", kind="grid", title="c3_keep_g")
    c.r.call("docs.open", kind="form", title="c3_keep_f")
    def titles(x):
        arr = x.get("docs") if isinstance(x, dict) else x
        return sorted(str(d.get("title")) for d in (arr or []))
    before = titles(c.r.call("docs.list"))
    c.r.call("ui.style", set="mobile")
    c.style(1.8)
    c.shot("50_preserve_mobile")
    mid = c.r.call("docs.list")
    c.r.call("ui.style", set="tablet")
    c.style(1.8)
    c.shot("51_preserve_back_tablet")
    after = c.r.call("docs.list")
    c.raw["preserve"] = {"before": before, "mid": titles(mid), "after": titles(after)}
    c.check("L4 형 전환 열린 문서 보존(손실 0)", before == titles(mid) == titles(after) and len(before) >= 2,
            c.raw["preserve"], "전환 전·중·후 동일, 2건 이상")
    c.r.call("ui.style", set="auto")
    c.style(1.0)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--serial", required=True)
    ap.add_argument("--role", required=True, choices=["phone", "tablet"])
    ap.add_argument("--apk", required=True)
    ap.add_argument("--out", required=True)
    a = ap.parse_args()
    os.makedirs(a.out, exist_ok=True)
    c = Ctx(a.serial, a.out, a.role)
    state = {}
    for ns, key in SETTINGS:
        c.orig[key] = c.adb("shell", "settings", "get", ns, key)[1].strip()
    try:
        c.adb("uninstall", ra.PKG)  # 이전 회차 remote-control.json(옛 토큰) 잔존으로 인한 토큰 불일치 방지
        info = ra.prepare(a.serial, a.apk, state)
        if not info:
            print("prepare 실패")
            return 2
        c.r = rr.Remote()
        device_info(c)
        for ph in (phase_basic, phase_nav, phase_rotation, phase_resize, phase_transition, phase_preserve):
            print("== " + ph.__name__)
            try:
                ph(c)
            except (Exception, SystemExit) as e:  # noqa: BLE001 — 한 단계 실패(pdctl 연결 실패 SystemExit 포함)가 다음 단계를 막지 않게 한다
                c.check(ph.__name__ + " 예외", False, repr(e), "예외 없음")
                c.raw.setdefault("phase_errors", {})[ph.__name__] = {
                    "err": repr(e), "pid_alive": c.sh("pidof com.pd.poc"),
                    "logcat_tail": [l for l in c.adb("logcat", "-d", "-v", "time", "-t", "300")[1].splitlines()
                                    if "pd.poc" in l.lower() or "FATAL" in l or "ANR" in l][-25:]}
        c.raw["remote_calls"] = c.r.calls
        c.raw["logcat_fatal"] = [l for l in c.adb("logcat", "-d", "-v", "time")[1].splitlines()
                                 if "FATAL" in l or "ANR in" in l][:10]
        c.check("FATAL/ANR 0", not c.raw["logcat_fatal"], len(c.raw["logcat_fatal"]), 0)
    finally:
        c.adb("shell", "wm", "size", "reset")
        c.adb("shell", "wm", "density", "reset")
        for ns, key in SETTINGS:
            v = c.orig.get(key)
            if v in (None, "", "null"):
                c.adb("shell", "settings", "delete", ns, key)
            else:
                c.adb("shell", "settings", "put", ns, key, v)
        ra.cleanup(a.serial, state)
        c.raw["after"] = {"wm_size": c.sh("wm size"), "wm_density": c.sh("wm density"),
                          "settings": {k: c.sh("settings get %s %s" % (ns, k)) for ns, k in SETTINGS},
                          "pkg_count": c.sh("pm list packages | grep -c com.pd.poc")}
        res = {"role": a.role, "serial": a.serial, "apk": a.apk, "orig_settings": c.orig, "checks": c.checks, "raw": c.raw,
               "pass": all(x["pass"] for x in c.checks), "n_fail": sum(1 for x in c.checks if not x["pass"])}
        with open(os.path.join(a.out, "%s_c3.json" % a.role), "w", encoding="utf-8") as f:
            json.dump(res, f, ensure_ascii=False, indent=1)
        print("RESULT role=%s pass=%s fail=%d" % (a.role, res["pass"], res["n_fail"]))
    return 0 if res["pass"] else 1


if __name__ == "__main__":
    sys.exit(main())
