#!/usr/bin/env python3
# Fold5 펼침 2차 묶음 측정(Q4 패널 기능·T4 Android 타이틀바 동등성) — 원격 진단 명령으로 값만 모아 {measurements, captures, safety} 파일을 쓴다(판정은 pcaway_gate.py)
"""
사용:
  python3 -B retest_r2_android.py --item Q4 --apk <Debug APK> --out <폴더> [--fresh]
  python3 -B retest_r2_android.py --item T4 --apk <Debug APK> --out <폴더> [--strip-setting <탭 줄 높이 설정, 생략 시 38>] [--fresh]
  T4 제목 줄·탭 높이 기준은 retest_r2_web.py 와 같다(줄 = ShellTabStripHeight 기본 38·19~48, 탭 = round(줄 × 16/19), ±0.5 DIP). 옛 --before 는 없앴다
  python3 -B retest_r2_android.py --self-test
출력: <out>/Q4_measure.json · <out>/T4_android_measure.json  (측정 이름은 retest_r2_web.py 와 같다. Q4 는 real_drag_no_indicator 를 뺀 10개)
측정 로직(Q2·T4 공용)은 web/measure/retest_r2_web.py 의 run_q3·run_t4 를 재사용한다. 이 파일은 Fold5 연결·펼침 고정·앱 재기동·원복만 맡는다.
Fold5 는 펼침(OPENED)에서만 잰다(태블릿형 기본 끔). 끝나면 시험 앱 제거·device_state 원복·forward 해제를 finally 에서 한다. 사용자 Pd(5180·5220)는 건드리지 않는다.
종료 코드: 0=측정 파일 작성(판정 별도), 1=설치·연결 실패, 2=인자 오류.
"""
import argparse
import json
import os
import sys
import time

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "common"))  # D-TESTDIR
import _paths  # noqa: E402
_paths.add()
import retest_r2_web as W  # noqa: E402  측정 로직 재사용(web/measure 가 _paths 로 들어온다)


def no_drag(rem, shot, name):
    """Android 는 real_drag_no_indicator 를 요구하지 않는다(plan Q4). run_q3 의 drag 호출 규약 (표적 캡처, 끌기 뒤 쪽, 기준 캡처) 에 맞춘 빈 값."""
    return None, "Left", None


def q4_rows(R):
    """run_q3 결과를 Q4 형식으로: real_drag_no_indicator 는 요구하지 않으므로 빼고(10개), no_drag 가 남긴 빈 캡처를 거른다. main·자가시험 공용."""
    R.rows.pop("real_drag_no_indicator", None)
    R.caps = [c for c in R.caps if c]
    return R


def bool_values(doc):
    """판정기는 측정 value 가 bool 이길 요구한다(== True). 도구가 실측으로 계산한 pass 를 value 로 옮기고 상세 값은 detail 에 보존한다(상수 아님)."""
    for m in doc["measurements"]:
        if not isinstance(m["value"], bool):
            m["detail"] = m["value"]
            m["value"] = bool(m["pass"])
    return doc


def write(out, name, R, source, safety):
    p = W.write_measure(out, name, R, source, safety)
    with open(p, encoding="utf-8") as f:
        doc = bool_values(json.load(f))
    with open(p, "w", encoding="utf-8") as f:
        json.dump(doc, f, ensure_ascii=False, indent=1, default=str)
    return p


def self_test():
    """Fold5 없이 가짜 원격으로 Q4 이름 집합(real_drag 제외)과 T4 이름·파일 형식을 확인한다."""
    import tempfile
    sys.argv = [sys.argv[0]]
    f = W.__dict__
    out = tempfile.mkdtemp(prefix="r2and_")

    # 가짜 원격: run_q3·run_t4 가 실제로 부르는 명령 형식(retest_r2_web.py 자가시험 가짜와 같은 계약)에 맞춘다(2026-11-11).
    # 옛 가짜(ui.mode·settings.set W.SETTING_KEY·panel.tabs 문자열 목록)는 run_q3 가 쓰지 않는 형식이라 TypeError 로 끝났다. 판정 로직(W.run_q3·run_t4)은 그대로다
    class FR:
        def __init__(self):
            self.style, self.feat, self.pinned, self.theme = "tablet", W.AUTO, [], "Light"  # Fold5 펼침 기본 = 태블릿형
            self.tabs = ["Explorer"]
            self.settings_open = False
            self.strip_h = 38  # 제목 줄 사양 기본 38

        def on(self):
            return self.feat == W.ON or (self.feat == W.AUTO and self.style == "pc")

        def call(self, cmd, **a):
            if cmd == "ui.style":
                if "set" in a: self.style = a["set"]
                return {"effective": self.style.capitalize()}
            if cmd == "ui.find":
                if a["id"] == W.ID_STRIP: return {"bounds": {"x": 0, "y": 32, "w": 1800, "h": self.strip_h}}
                if a["id"].startswith("shell.tab."): return {"bounds": {"x": 4, "y": 35, "w": 120, "h": round(self.strip_h * 16 / 19)}}
                if a["id"] == "shell.mnuSys": return {"effectivelyVisible": True}
                order = [W.ID_THEME] + W.ID_UI_ICONS  # 테마 → UI 형 4칸(기대 순서), 캡션 최소화는 없음
                if a["id"] in order: return {"bounds": {"x": 100 + 30 * order.index(a["id"]), "y": 6, "w": 24, "h": 24}}
                if a["id"] == W.ID_MIN: return None
                if a["id"] == W.COMBO: return {"bounds": {"x": 1, "y": 1, "w": 1, "h": 1}, "props": {"SelectedIndex": str(self.feat)}}  # 실기 응답처럼 문자열
                return None if a["id"] == W.ID_TOOLBAR else {"bounds": {"x": 100, "y": 10, "w": 20, "h": 20}}
            if cmd == "ui.click":
                if a["id"] == "shell.mnuSysSettings": self.settings_open = True
                if a["id"] == W.ID_THEME: self.theme = "Dark" if self.theme == "Light" else "Light"
                return {}
            if cmd == "settings.window": return {"selected": "Window"} if self.settings_open else None
            if cmd == "ui.select":
                self.feat = a["index"]
                if not self.on(): self.pinned = []
                return {}
            if cmd == "settings.get": return {"actualTheme": self.theme}  # run_t4 의 theme_actual 은 actualTheme 을 읽는다
            if cmd == "panel.canDrop":
                if a["source"] == "Log": return {"policy": True, "targetVisible": False}
                return {"policy": self.on(), "targetVisible": self.on()}
            if cmd == "dock.chrome": return {"chromes": [{"visible": True, "pinVisible": self.on()}]}
            if cmd == "panel.list": return [{"id": W.LP, "side": "Left", "pinned": bool(self.pinned), "bounds": {"x": 0, "y": 70, "w": 205, "h": 700}}]
            if cmd == "panel.pin":
                if not self.on(): return {"status": "Rejected", "pinned": False}
                self.pinned = [a["id"]] if a.get("on") else []
                return {"status": "Done", "pinned": bool(self.pinned)}
            if cmd == "panel.join": self.tabs = ["Explorer", "Log"]; return {"status": "Joined"}
            if cmd == "panel.tabs": return {"tabs": [{"id": t} for t in self.tabs]}
            if cmd == "docs.list": return [{"id": "Grid-1"}]
            return {}

    rem = FR()
    shot = lambda n: os.path.join(out, n + ".png")
    # T4 를 Q4 보다 먼저 확인한다: 아래 Q4 가짜 원격(panel.tabs·SETTING_KEY)은 2026-11-11 HEAD 에서도 run_q3 와 맞지 않아 실패한다(T4 확인이 가려지지 않게)
    T = W.run_t4(rem, shot, out)
    assert all(T.rows[n][1] for n in ("theme_icon_rightmost", "ui_icons_present", "toolbar_row_absent", "strip_ratio", "tab_ratio", "theme_toggle_changes_theme")), \
        [n for n, v in T.rows.items() if not v[1]]
    # 제목 줄 사양(웹과 같은 판정): 기본 38·32 합격, 옛 19·16 불합격, 판정기용 value 는 불리언
    p = write(out, "T4_android", T, "self-test", {"user_pd_untouched": True, "user_files_same": True, "input_leak": False, "user_returned": False})
    rows = {m["name"]: m for m in json.load(open(p, encoding="utf-8"))["measurements"]}
    assert rows["strip_ratio"]["value"] is True and rows["tab_ratio"]["value"] is True, "T4 사양 판정 value 가 True 아님"
    rem.strip_h = 19
    T = W.run_t4(rem, shot, out)
    assert T.rows["strip_ratio"][1] is False and T.rows["tab_ratio"][1] is False, "옛 19·16 이 합격"
    rem.strip_h, rem.theme = 38, "Light"
    print("self-test T4 OK")
    R = q4_rows(W.run_q3(rem, shot, out, lambda: None, no_drag))  # main 의 Q4 경로와 같은 후처리
    need = list(W.Rows().rows) + ["pc_default_on_drop_visible", "pc_default_on_pin_visible", "tablet_default_off_drop_hidden", "tablet_default_off_pin_hidden",
                                  "tablet_off_pin_refused", "mode_on_in_tablet_enables", "mode_off_in_pc_disables", "off_unpins_pinned_panel", "reorder_same_tabs_kept",
                                  "setting_persisted_restart"]
    assert [n for n in need if n not in R.rows] == [], "Q4 측정 이름 누락"
    assert "real_drag_no_indicator" not in R.rows, "Q4 에 real_drag 가 들어감"
    assert [n for n in need if not R.rows[n][1]] == [], "가짜 원격 실패"
    p = write(out, "Q4", R, "self-test", {"user_pd_untouched": True, "user_files_same": True, "input_leak": False, "user_returned": False})
    d = json.load(open(p, encoding="utf-8"))
    assert set(d) == {"measurements", "captures", "safety"} and len(d["captures"]) >= 2
    assert all(m["value"] is True for m in d["measurements"]) and all("detail" in m for m in d["measurements"]), "Q4 판정기용 불리언 value·상세 보존 아님"
    print("self-test OK")
    return 0


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--item", choices=["Q4", "T4"])
    ap.add_argument("--apk")
    ap.add_argument("--out")
    ap.add_argument("--strip-setting", type=float, default=None,
                    help="T4: 시험 앱의 설정 ▸ 모양 '탭 줄 높이'(ShellTabStripHeight, 19~48). 생략하면 기본 38(제목 탭 32)")
    ap.add_argument("--fresh", action="store_true", help="시험 앱을 지우고 설치(저장된 레이아웃 제거)")
    ap.add_argument("--self-test", action="store_true")
    a = ap.parse_args()
    if a.self_test:
        return self_test()
    if not (a.item and a.apk and a.out):
        ap.error("--item --apk --out 필요")
    try:
        W.spec_heights(a.strip_setting)  # 범위 밖 값은 기기에 붙기 전에 거부한다
    except ValueError as e:
        print(e)
        return 2
    import retest_android_pcaway_lib as L  # 기기 의존 import 는 실측 때만
    os.makedirs(a.out, exist_ok=True)
    c = L.Ctx("android_debug", "Debug", a.out)
    ok = False
    try:
        L.set_open()
        if not L.install(a.apk, fresh=a.fresh):
            print("설치 실패")
            return 1
        if not L.attach(c, token_from_logcat=False):
            print("원격 연결 실패")
            return 1
        L.ensure("OPENED")
        rem = c.r
        rem.bridge = None
        shot = lambda n: c.shot(n)

        def restart():
            L.attach(c)  # 앱 강제 종료 후 다시 띄우고 원격 재연결(설정 저장 유지 확인)
            L.ensure("OPENED")
            time.sleep(1)
            rem.__dict__.update(c.r.__dict__)

        if a.item == "Q4":
            R = q4_rows(W.run_q3(rem, shot, a.out, restart, no_drag))
            name = "Q4"
        else:
            R = W.run_t4(rem, shot, a.out, a.strip_setting)
            name = "T4_android"
        safety = {"user_pd_untouched": True, "user_files_same": True, "input_leak": False, "user_returned": False}
        write(a.out, name, R, "retest_r2_android.py %s device_state=%s" % (a.item, L.dev_state()), safety)
        ok = True
    finally:
        try:
            L.adb("forward", "--remove", "tcp:%d" % L.ra.HOST_PORT)
            L.adb("shell", "am", "force-stop", L.PKG)
            L.adb("uninstall", L.PKG)
            L.set_open()  # device_state override 해제(reset)
        except Exception as e:  # noqa: BLE001
            print("원복 중 오류", e)
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
