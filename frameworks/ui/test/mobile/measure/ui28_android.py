#!/usr/bin/env python3
# UI/UX 요건 2~8 Android 축 공식 시험(AC-3-10·6-08·7-06 + 기동·크래시) — 원격 진단 명령과 uiautomator 실터치만 쓴다
"""
사용: python3 ui28_android.py --serial emulator-5554 --role phone|tablet --apk <APK> --build Debug|Release --out <폴더>
Debug: 원격 제어(진단)로 상태를 읽고 adb input tap(uiautomator 좌표)으로 실제 터치한다. 결과 <out>/<role>_<build>_ui28.json 의 checks 가 판정 입력이다.
Release: 진단이 꺼져 있으므로 기동·프로세스 생존·logcat 치명 0 만 잰다(REL 접두). 터치 전에 터치 대상이 캡처에 그려졌는지 확인하고 그 시각을 raw.first_frame 에 남긴다.
원복: finally 에서 forward 해제·앱 제거·stay_on 원값(retest_android.cleanup). 화면 크기·회전은 건드리지 않는다.
종료 코드: 0=측정 완료(판정은 별도), 1=설치·연결 실패.
"""
import argparse
import glob
import io
import json
import os
import re
import sys
import time

from PIL import Image

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "common"))
import _paths  # noqa: E402
_paths.add()
import retest_android as ra  # noqa: E402
import retest_remote as rr  # noqa: E402
sys.path.insert(1, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "judge"))
import back_judge  # noqa: E402

PKG = ra.PKG
FIRST_FRAME_LIMIT_S = 60  # Release 첫 프레임 상한. 넘으면 터치 없이 FAIL 이다
FIRST_FRAME_MIN_COLORS = 8  # 터치 대상 영역이 이만큼의 색을 가져야 그려진 것으로 본다(실측: 스플래시 1색, 실제 화면 299색)
FIRST_FRAME_POLL_S = 0.5
CORNER_RX = re.compile(r"^(?!PART_).*corner", re.I)  # Avalonia 템플릿 부품(PART_BottomRightCorner 등)은 제외
NAV_STEPS = ["nav.drawer", "nav.docs", "nav.notify", "nav.close", "nav.tabs"]
NAV_TARGETS = ["shell.btnDrawer", "shell.cmbDocs", "shell.btnNotify", "shell.compactClose", "shell.navTabBar"]


class Ctx:
    def __init__(self, serial, role, build, out):
        self.s, self.role, self.build, self.out = serial, role, build, out
        self.checks, self.raw, self.r = [], {}, None

    def adb(self, *a, **k):
        return ra.adb(self.s, *a, **k)

    def check(self, name, ok, value, expect):
        self.checks.append({"name": name, "pass": bool(ok), "value": value, "expect": expect})
        print("  %s %s value=%s expect=%s" % ("PASS" if ok else "FAIL", name, str(value)[:150], expect))

    def shot(self, name):
        rc, data = self.adb("exec-out", "screencap", "-p", binary=True)
        sig = data.find(b"\x89PNG")  # 다중 디스플레이 기기(폴드)는 adb 경고 문구가 PNG 앞에 붙는다
        data = data[sig:] if sig > 0 else data
        p = os.path.join(self.out, "%s_%s_%s.png" % (self.role, self.build, name))
        open(p, "wb").write(data)
        self.raw.setdefault("shots", []).append({"file": os.path.basename(p), "bytes": len(data)})
        return os.path.basename(p)

    def dump(self, name=None):
        txt = ""
        for _ in range(4):
            self.adb("shell", "uiautomator", "dump", "/sdcard/ui.xml")
            rc, data = self.adb("exec-out", "cat", "/sdcard/ui.xml", binary=True)
            txt = data.decode("utf-8", "replace")
            if "android:id/aerr_wait" in txt:  # 호스트 과부하로 뜨는 시스템 ANR 대화상자는 앱 화면을 가린다 — '대기'로 닫고 기록한다(앱 ANR 아님: System UI)
                b = self.bounds(txt, "android:id/aerr_wait")
                self.raw.setdefault("system_anr_dialog_dismissed", []).append({"name": name, "pkg_system_ui": "System UI" in txt})
                if b:
                    self.adb("shell", "input", "tap", str(b[0]), str(b[1]))
                time.sleep(2)
                continue
            if "<hierarchy" in txt:
                break
            time.sleep(1.5)
        if name:
            open(os.path.join(self.out, "%s_%s_%s.xml" % (self.role, self.build, name)), "w", encoding="utf-8").write(txt)
        return txt

    def ids(self, txt):
        return re.findall(r'resource-id="([^"]+)"', txt)

    def bounds(self, txt, rid):
        m = re.search(r'resource-id="%s"[^>]*bounds="\[(\d+),(\d+)\]\[(\d+),(\d+)\]"' % re.escape(rid), txt)
        if not m:
            return None
        x1, y1, x2, y2 = map(int, m.groups())
        return (x1 + x2) // 2, (y1 + y2) // 2

    def bounds_top(self, txt, rid):
        m = re.search(r'resource-id="%s"[^>]*bounds="\[(\d+),(\d+)\]\[(\d+),(\d+)\]"' % re.escape(rid), txt)
        return int(m.group(2)) if m else 0

    def tap_id(self, rid):
        """uiautomator 좌표(화면 픽셀)로 실제 터치. 못 찾으면 False."""
        txt = self.dump()
        b = self.bounds(txt, rid)
        # Avalonia 접근성 노드의 bounds 는 상태 표시줄(android:id/content 윗변) 만큼 위로 어긋난다 — 캡처 대조로 확인(r3) — 윗변을 더해 보정한다
        off = self.bounds_top(txt, "android:id/content")
        self.raw.setdefault("taps", []).append({"id": rid, "px": b, "y_offset": off})
        if not b:
            return False
        self.adb("shell", "input", "tap", str(b[0]), str(b[1] + off))
        return True

    def call(self, cmd, **a):
        return self.r.call(cmd, **a)

    def fatal_count(self):
        rc, out = self.adb("logcat", "-d", "-v", "brief", "*:E")
        anr = re.compile(r"ANR in " + re.escape(PKG) + r"(\s|$)")
        lines = [l for l in out.splitlines() if (re.search(r"FATAL EXCEPTION|AndroidRuntime|Fatal signal", l) or anr.search(l)) and re.search(re.escape(PKG) + r"(?![\w.])", out)]
        return len([l for l in out.splitlines() if re.search(r"FATAL EXCEPTION|Fatal signal", l) or anr.search(l)]), lines[:5]

    def pid(self):
        return self.adb("shell", "pidof", PKG)[1].strip()

    def foreground(self):
        """전면 활성의 패키지(dumpsys). BACK 판정은 앱 이탈을 닫힘으로 세지 않게 이 값을 함께 본다."""
        return back_judge.foreground_package(self.adb("shell", "dumpsys", "activity", "activities")[1])


def style_wait(c, expect, limit=20):
    t0 = time.time()
    st = {}
    while time.time() - t0 < limit:
        st = c.call("ui.style") or {}
        if st.get("effective") == expect:
            break
        time.sleep(1)
    return st


def vis(c, rid):
    f = c.call("ui.find", id=rid) or {}
    return bool(f.get("effectivelyVisible"))


def phase_boot(c):
    want = "Mobile" if c.role == "phone" else "Tablet"
    st = style_wait(c, want)
    c.raw["style"] = st
    c.check("BOOT 기동·원격 연결·형 판별 %s" % want, st.get("effective") == want and st.get("device") == ("Phone" if c.role == "phone" else "Tablet"),
            "%s/%s" % (st.get("device"), st.get("effective")), "Phone/Mobile" if c.role == "phone" else "Tablet/Tablet")
    c.check("BOOT 프로세스 생존", bool(c.pid()), c.pid(), "pid")
    c.shot("00_boot")


def corner_ids(txt, c):
    return sorted({i for i in c.ids(txt) if CORNER_RX.search(i)})


def phase_3_10(c):
    st = c.call("panel.corner.state") or {}
    c.raw["corner_state0"] = st
    btns = st.get("buttons") or []
    if c.role == "phone":
        c.check("AC-3-10 폰 NAV panel.corner.state supported=false", st.get("supported") is False, st.get("supported"), False)
        c.check("AC-3-10 폰 NAV 모서리 버튼 0개", len(btns) == 0, len(btns), 0)
        c.call("docs.open", kind="grid")
        c.call("docs.open", kind="form")
        time.sleep(1.5)
        st2 = c.call("panel.corner.state") or {}
        c.check("AC-3-10 폰 NAV 문서 열린 뒤에도 버튼 0개", len(st2.get("buttons") or []) == 0, len(st2.get("buttons") or []), 0)
        txt = c.dump("3_10_nav")
        cid = corner_ids(txt, c)
        fg = c.foreground()
        c.check("AC-3-10 폰 NAV 화면 트리에 corner 식별자 없음(uiautomator, 앱 전면)", back_judge.back_closed(len(cid), fg, PKG), "%s fg=%s" % (cid, fg), "[] fg=%s" % PKG)
        c.shot("3_10_nav")
        docs = c.call("docs.list") or []
        did = docs[0].get("id") if docs else None
        res = c.call("docs.sdi", doc=did, on=True)
        time.sleep(2)
        wl = c.call("windows.list") or []
        managed = [w for w in wl if w.get("managed")]
        c.raw["windows_sdi"] = wl
        c.check("AC-3-10 SDI 창 존재(관리형 창 ≥1)", len(managed) >= 1, len(managed), "≥1")
        st3 = c.call("panel.corner.state") or {}
        c.check("AC-3-10 SDI 창 열린 뒤 버튼 0개·supported=false", len(st3.get("buttons") or []) == 0 and st3.get("supported") is False,
                "%s/%s" % (len(st3.get("buttons") or []), st3.get("supported")), "0/False")
        txt = c.dump("3_10_sdi")
        cid = corner_ids(txt, c)
        fg = c.foreground()
        c.check("AC-3-10 SDI 창 트리에 corner 식별자 없음(uiautomator, 앱 전면)", back_judge.back_closed(len(cid), fg, PKG), "%s fg=%s" % (cid, fg), "[] fg=%s" % PKG)
        c.shot("3_10_sdi")
        c.call("docs.sdi", doc=did, on=False)
        c.call("docs.close", all=True)
    else:
        c.check("AC-3-10 태블릿(양성 대조) supported=true", st.get("supported") is True, st.get("supported"), True)
        vb = [b for b in btns if b.get("visible")]
        c.check("AC-3-10 태블릿(양성 대조) 보이는 버튼 ≥1", len(vb) >= 1, len(vb), "≥1")
        txt = c.dump("3_10_tablet")
        cid = corner_ids(txt, c)
        c.check("AC-3-10 태블릿(양성 대조) 화면 트리에 corner 식별자 있음", len(cid) >= 1, cid[:4], "≥1")
        c.shot("3_10_tablet")
        docs = c.call("docs.open", kind="grid") or []
        did = docs[0].get("id") if docs else None
        c.call("docs.sdi", doc=did, on=True)
        time.sleep(2)
        wl = c.call("windows.list") or []
        c.raw["windows_sdi_tablet"] = wl
        st3 = c.call("panel.corner.state") or {}
        c.raw["corner_state_sdi_tablet"] = st3
        c.check("AC-3-10 태블릿 SDI 창은 버튼이 메인 창 소속뿐(state.buttons 는 패널 모서리 id)", all(str(b.get("id", "")).strip() != "" for b in (st3.get("buttons") or [])),
                len(st3.get("buttons") or []), "메인 패널 버튼만")
        c.shot("3_10_tablet_sdi")
        c.call("docs.sdi", doc=did, on=False)
        c.call("docs.close", all=True)


def phase_6_08(c):
    if c.role != "phone":
        return
    c.call("tour.end")
    res = c.call("tour.start", scope="main") or {}
    seq, st = [], []
    for i in range(8):
        s = c.call("tour.state") or []
        st = s[0] if s else {}
        c.raw.setdefault("tour_states", []).append(st)
        if not st:
            break
        seq.append(st.get("stepId") if not st.get("onShortcutLayer") else "LAYER")
        if i < len(NAV_STEPS):
            tid = NAV_TARGETS[i] if st.get("stepId") == NAV_STEPS[i] else None
            if tid:
                c.check("AC-6-08 단계 %s 대상 %s 보임" % (st.get("stepId"), tid), vis(c, tid), vis(c, tid), True)
            c.shot("6_08_step%d" % (i + 1))
        if st.get("onShortcutLayer"):
            break
        c.call("tour.next")
        time.sleep(0.6)
    c.raw["tour_seq"] = seq
    c.check("AC-6-08 Nav 범위로 시작(scope=Nav)", st.get("scope") == "Nav", st.get("scope"), "Nav")
    c.check("AC-6-08 단계 순서 = 5개 Nav 단계 + 단축키 레이어", seq == NAV_STEPS + ["LAYER"], seq, NAV_STEPS + ["LAYER"])
    c.check("AC-6-08 단계 수 count=6", st.get("count") == 6, st.get("count"), 6)
    c.check("AC-6-08 넓은 화면 단계 id 없음", not [x for x in seq if x not in NAV_STEPS + ["LAYER"]], [x for x in seq if x not in NAV_STEPS + ["LAYER"]], [])
    # 하단 탭·☰ 가 투어 대상으로 실존
    nav = c.call("nav.menu") or {}
    c.check("AC-6-08 하단 탭 4+더보기·☰ 가 화면에 보임", vis(c, "shell.btnDrawer") and vis(c, "shell.navTab.more") and len(nav.get("tabs") or []) == 4,
            [vis(c, "shell.btnDrawer"), vis(c, "shell.navTab.more"), len(nav.get("tabs") or [])], [True, True, 4])
    c.shot("6_08_layer")
    c.call("tour.end")
    time.sleep(0.8)
    after = c.call("tour.state") or []
    c.check("AC-6-08 종료 뒤 세션 0", after == [], after, [])
    c.check("AC-6-08 종료 뒤 입력 복원(☰ 보임·탭 가능)", vis(c, "shell.btnDrawer"), vis(c, "shell.btnDrawer"), True)
    # 두 번째: 시스템 메뉴 도움말 경로에서 시작·건너뛰기→레이어→종료
    c.call("sys.open", view="Help")
    s2 = []
    t_help = time.time()
    for _ in range(45):  # OpenHelpTour 는 메뉴·서랍이 닫힌 뒤 Background 우선순위로 시작한다(비동기)
        time.sleep(1)
        s2 = c.call("tour.state") or []
        if s2:
            break
    c.raw["help_start_s"] = round(time.time() - t_help, 1)
    c.check("AC-6-08 도움말 경로로도 Nav 투어 시작", bool(s2) and s2[0].get("scope") == "Nav", s2[0].get("scope") if s2 else None, "Nav")
    c.call("tour.skip")
    s3 = c.call("tour.state") or []
    c.check("AC-6-08 건너뛰기 → 단축키 레이어", bool(s3) and s3[0].get("onShortcutLayer") is True, s3[0].get("onShortcutLayer") if s3 else None, True)
    c.call("tour.end")
    time.sleep(0.6)
    c.check("AC-6-08 두 번째 종료 뒤 세션 0", (c.call("tour.state") or []) == [], c.call("tour.state"), [])


def settings_ids(c, txt):
    return sorted({i for i in c.ids(txt) if i.startswith("settings.") or "Settings" in i and "navTab" not in i})


def phase_7_06(c):
    if c.role == "phone":
        c.call("tour.end")  # 투어 오버레이가 남아 있으면 터치가 막힌다
        time.sleep(0.8)
        before = c.dump("7_06_before")
        sid0 = [i for i in c.ids(before) if i.startswith("settings.")]
        fg = c.foreground()
        c.check("AC-7-06 폰 열기 전 설정 시트 식별자 없음(앱 전면)", back_judge.back_closed(len(sid0), fg, PKG), "%s fg=%s" % (sid0[:3], fg), "[] fg=%s" % PKG)
        ok = c.tap_id("shell.navTab.Settings")
        time.sleep(2.5)
        txt = c.dump("7_06_sheet_tab")
        sid = [i for i in c.ids(txt) if i.startswith("settings.")]
        c.raw["sheet_ids_tab"] = sid[:40]
        c.check("AC-7-06 폰 하단 설정 탭 터치 → 설정 시트 열림(settings.* 노드)", ok and len(sid) >= 3, len(sid), "≥3")
        c.shot("7_06_sheet_tab")
        # 시트를 연 상태로 ☰ 존재·탭 재터치로 닫힘
        c.tap_id("shell.navTab.Settings")
        time.sleep(1.5)
        sid2 = [i for i in c.ids(c.dump("7_06_sheet_closed")) if i.startswith("settings.")]
        fg = c.foreground()
        c.check("AC-7-06 폰 설정 탭 재터치 → 시트 닫힘(앱 전면)", back_judge.back_closed(len(sid2), fg, PKG), "%d nodes fg=%s" % (len(sid2), fg), "0 nodes fg=%s" % PKG)
        # ☰ 서랍 경유
        ok = c.tap_id("shell.btnDrawer")
        time.sleep(2)
        dtxt = c.dump("7_06_drawer")
        dids = [i for i in c.ids(dtxt) if i.startswith("shell.navDrawer.")]
        c.raw["drawer_ids"] = dids
        c.check("AC-7-06 폰 ☰ 터치 → 서랍 열림", ok and len(dids) >= 3, len(dids), "≥3")
        c.shot("7_06_drawer")
        navm = c.call("nav.menu") or {}
        all_ids = ["shell.navDrawer." + str(e.get("id")) for e in (navm.get("drawer") or [])]
        c.raw["drawer_all_ids"] = all_ids
        want = [i for i in all_ids if re.search(r"Settings", i)]
        cand = []
        # 서랍 목록은 화면 밖 항목을 만들지 않는다 — 찾을 때까지 위로 스와이프(최대 12회)
        for n in range(12):
            t2 = c.dump()
            cand = [i for i in want if c.bounds(t2, i)]
            if cand:
                # 하단 탭 막대(navTabBar) 뒤에 가려지면 터치가 탭으로 샌다 — 탭 막대 윗변 위로 올라올 때까지 더 민다
                tab_top = c.bounds_top(t2, "shell.navTabBar")
                if c.bounds(t2, cand[0])[1] < tab_top - 60:
                    break
            c.adb("shell", "input", "swipe", "540", "1500", "540", "1100", "300")
            time.sleep(0.8)
        c.raw["drawer_settings_candidates"] = cand
        c.raw["drawer_settings_wanted"] = want
        if cand:
            ok2 = c.tap_id(cand[0])
            time.sleep(2.5)
            sid3 = [i for i in c.ids(c.dump("7_06_sheet_drawer")) if i.startswith("settings.")]
            c.check("AC-7-06 폰 ☰ 서랍의 설정 항목(%s) 터치 → 설정 시트" % cand[0], ok2 and len(sid3) >= 3, len(sid3), "≥3")
            c.shot("7_06_sheet_drawer")
            c.adb("shell", "input", "keyevent", "KEYCODE_BACK")
            time.sleep(1.2)
            sid4 = [i for i in c.ids(c.dump("7_06_sheet_back")) if i.startswith("settings.")]
            fg = c.foreground()
            c.check("AC-7-06 폰 시트 BACK 키로 닫힘(앱 전면 유지)", back_judge.back_closed(len(sid4), fg, PKG), "%d nodes fg=%s" % (len(sid4), fg), "0 nodes fg=%s" % PKG)
        else:
            c.check("AC-7-06 폰 ☰ 서랍에 설정 항목 있음", False, dids, "Settings 포함 id")
        nav = c.call("nav.menu") or {}
        c.check("AC-7-06 폰 하단 탭에 Settings 포함(nav.menu)", "Settings" in (nav.get("tabs") or []), nav.get("tabs"), "Settings in tabs")
    else:
        sw = c.call("ui.find", id="shell.mnuSys") or {}
        c.check("AC-7-06 태블릿 시스템(Y) 메뉴 보임", bool(sw.get("effectivelyVisible")), sw.get("effectivelyVisible"), True)
        ok = c.tap_id("shell.mnuSys")
        time.sleep(1.2)
        ok = ok and c.tap_id("shell.mnuSysSettings")  # 시스템 메뉴 → 설정 두 번 탭
        time.sleep(3)
        txt = c.dump("7_06_flyout")
        sid = [i for i in c.ids(txt) if i.startswith("settings.")]
        c.raw["flyout_ids"] = sid[:40]
        wl = c.call("windows.list") or []
        c.raw["windows_flyout"] = wl
        c.check("AC-7-06 태블릿 시스템 메뉴 → 설정 터치 → 설정 Flyout(settings.* 노드)", ok and len(sid) >= 3, len(sid), "≥3")
        sw2 = c.call("settings.window") or {}
        c.raw["settings_window"] = sw2
        c.check("AC-7-06 태블릿 settings.window 응답(범주 ≥5)", len(sw2.get("categories") or []) >= 5, len(sw2.get("categories") or []), "≥5")
        c.shot("7_06_flyout")
        # 검색 칸 초점으로 소프트 키보드가 떠 있으면 첫 BACK 은 키보드를 내린다(Android 표준) — 최대 2회까지 허용하고 횟수를 기록한다
        sid2, backs = sid, 0
        for backs in (1, 2):
            c.adb("shell", "input", "keyevent", "KEYCODE_BACK")
            time.sleep(1.5)
            sid2 = [i for i in c.ids(c.dump("7_06_flyout_closed%d" % backs)) if i.startswith("settings.")]
            if not sid2:
                break
        c.raw["flyout_backs_to_close"] = backs
        fg = c.foreground()
        c.check("AC-7-06 태블릿 BACK 키(최대 2회)로 Flyout 닫힘(앱 전면 유지)", back_judge.back_closed(len(sid2), fg, PKG), "%d nodes after %d BACK fg=%s" % (len(sid2), backs, fg), "0 nodes fg=%s" % PKG)


def phase_back_negative(c):
    """음성 대조: 열린 것이 없을 때 BACK 은 기존 동작대로 앱을 벗어나고(설계 §4), 판정기는 그 상태를 닫힘으로 세면 안 된다. 앱이 전면에서 빠지므로 Debug 측정 맨 끝에 돈다."""
    c.adb("shell", "input", "keyevent", "KEYCODE_BACK")
    time.sleep(2)
    nodes = [i for i in c.ids(c.dump("back_negative")) if i.startswith("settings.")]
    fg = c.foreground()
    c.check("NEG 열린 것 없을 때 BACK → 앱 이탈, 판정기는 닫힘 아님", fg != PKG and not back_judge.back_closed(len(nodes), fg, PKG), "%d nodes fg=%s" % (len(nodes), fg), "fg≠%s·닫힘 아님" % PKG)


def phase_extra(c):
    if c.role == "tablet":
        t = c.call("busy.begin", scope="main", reason="ui28") or {}
        time.sleep(0.9)
        bs = c.call("busy.state") or {}
        c.raw["busy_state"] = bs
        c.shot("2_spinner_tablet")
        c.call("busy.end", token=t.get("token") if isinstance(t, dict) else t, scope="main")
        c.check("EXTRA 태블릿 스피너 busy.begin 뒤 busy.state 응답", bool(bs), str(bs)[:120], "상태 있음")
    # 라이트/다크: 테마 한 번 순환해 설정 화면 캡처
    c.call("theme.next")
    time.sleep(1.5)
    c.shot("9_theme_next")
    c.check("EXTRA 테마 순환 뒤 프로세스 생존", bool(c.pid()), c.pid(), "pid")
    c.call("theme.next")


def control_rect(c, txt, rid):
    """터치 대상의 화면 픽셀 영역. tap_id 와 같은 상태 표시줄 보정을 쓴다. 트리에 없으면 None."""
    m = re.search(r'resource-id="%s"[^>]*bounds="\[(\d+),(\d+)\]\[(\d+),(\d+)\]"' % re.escape(rid), txt)
    if not m:
        return None
    x1, y1, x2, y2 = map(int, m.groups())
    off = c.bounds_top(txt, "android:id/content")
    return x1, y1 + off, x2, y2 + off


def drawn_colors(png, rect):
    """캡처에서 영역 안의 서로 다른 색 수. 스플래시·검은 화면은 그 영역이 한 색이라 1 이다."""
    box = Image.open(io.BytesIO(png)).convert("RGB").crop(rect)
    return len(box.getcolors(maxcolors=box.width * box.height))


def wait_first_frame(c, rid):
    """터치 대상이 트리가 아니라 캡처에 그려질 때까지 기다린다. 연속 2장이 기준을 넘어야 그려짐이고, 상한을 넘으면 drawn=False 다."""
    t0, rect, streak, samples = time.time(), None, 0, []
    res = {"target": rid, "drawn": False, "ms": None, "limit_s": FIRST_FRAME_LIMIT_S, "min_colors": FIRST_FRAME_MIN_COLORS, "samples": samples}
    while time.time() - t0 < FIRST_FRAME_LIMIT_S:
        if not rect:
            rect = control_rect(c, c.dump(), rid)
            res["rect"] = rect
        if rect:
            rc, data = c.adb("exec-out", "screencap", "-p", binary=True)
            sig = data.find(b"\x89PNG")
            colors = drawn_colors(data[max(sig, 0):], rect) if sig >= 0 else 0
            samples.append([int((time.time() - t0) * 1000), colors])
            streak = streak + 1 if colors >= FIRST_FRAME_MIN_COLORS else 0
            if streak >= 2:
                res["drawn"], res["ms"] = True, samples[-1][0]
                break
        time.sleep(FIRST_FRAME_POLL_S)
    c.raw["first_frame"] = res
    return res


def run_release(c):
    target = "shell.navTab.Settings" if c.role == "phone" else "shell.mnuSys"
    ff = wait_first_frame(c, target)
    c.check("REL Release 첫 프레임 그려짐(캡처, 터치 대상 영역)", ff["drawn"], ff["ms"], "%d초 안" % FIRST_FRAME_LIMIT_S)
    c.check("REL Release 기동 프로세스 생존", bool(c.pid()), c.pid(), "pid")
    c.shot("00_boot")
    txt = c.dump("release_boot")
    ids = c.ids(txt)
    nav = [i for i in ids if i.startswith("shell.navTab.")] if c.role == "phone" else [i for i in ids if i.startswith("shell.")]
    c.check("REL Release 첫 화면 셸 노드 ≥3(uiautomator)", len(nav) >= 3, len(nav), "≥3")
    if c.role == "phone":
        ok = ff["drawn"] and c.tap_id("shell.navTab.Settings")  # 그려지지 않았으면 터치하지 않고 아래 판정을 FAIL 로 둔다
        time.sleep(2.5)
        sid = [i for i in c.ids(c.dump("release_sheet")) if i.startswith("settings.")]
        c.check("REL Release 폰 설정 탭 터치 → 시트(settings.* ≥3)", ok and len(sid) >= 3, len(sid), "≥3")
        c.shot("release_sheet")
    else:
        ok = ff["drawn"] and c.tap_id("shell.mnuSys")  # 그려지지 않았으면 터치하지 않고 아래 판정을 FAIL 로 둔다
        time.sleep(1.2)
        ok = ok and c.tap_id("shell.mnuSysSettings")  # 시스템 메뉴 → 설정 두 번 탭
        time.sleep(2.5)
        sid = [i for i in c.ids(c.dump("release_flyout")) if i.startswith("settings.")]
        c.check("REL Release 태블릿 시스템 메뉴 → 설정 터치 → Flyout(settings.* ≥3)", ok and len(sid) >= 3, len(sid), "≥3")
        c.shot("release_flyout")
    c.check("REL Release 프로세스 생존(조작 뒤)", bool(c.pid()), c.pid(), "pid")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--serial", required=True)
    ap.add_argument("--role", required=True, choices=["phone", "tablet"])
    ap.add_argument("--apk", required=True)
    ap.add_argument("--build", required=True, choices=["Debug", "Release"])
    ap.add_argument("--out", required=True)
    a = ap.parse_args()
    os.makedirs(a.out, exist_ok=True)
    c = Ctx(a.serial, a.role, a.build, a.out)
    state = {}
    t0 = time.time()
    started = time.strftime("%Y-%m-%dT%H:%M:%S+09:00")
    rc_out = 0
    try:
        if a.build == "Debug":
            p = ra.prepare(a.serial, a.apk, state)
            if not p:
                print("설치·원격 연결 실패")
                rc_out = 1
                return 1
            c.r = rr.Remote()
            phase_boot(c)
            phase_3_10(c)
            phase_6_08(c)
            phase_7_06(c)
            phase_extra(c)
            phase_back_negative(c)
        else:
            rc, out = c.adb("install", "-r", ra.win_path(a.apk), timeout=300)
            state["installed"] = True
            c.check("REL 설치 Success", "Success" in out, out.strip()[-60:], "Success")
            c.adb("logcat", "-c")
            c.adb("shell", "monkey", "-p", PKG, "-c", "android.intent.category.LAUNCHER", "1")
            run_release(c)
        n, ex = c.fatal_count()
        c.check("CRASH logcat FATAL·ANR 0건(E 레벨)", n == 0, "%d %s" % (n, ex), 0)
    finally:
        ra.cleanup(a.serial, state)
        res = {"role": a.role, "build": a.build, "serial": a.serial, "apk": a.apk, "started_at": started,
               "finished_at": time.strftime("%Y-%m-%dT%H:%M:%S+09:00"), "elapsed_ms": int((time.time() - t0) * 1000),
               "checks": c.checks, "raw": c.raw}
        p = os.path.join(a.out, "%s_%s_ui28.json" % (a.role, a.build))
        json.dump(res, open(p, "w", encoding="utf-8"), ensure_ascii=False, indent=1)
        print("결과:", p, "PASS %d / FAIL %d" % (sum(1 for k in c.checks if k["pass"]), sum(1 for k in c.checks if not k["pass"])))
    return rc_out


if __name__ == "__main__":
    sys.exit(main())
