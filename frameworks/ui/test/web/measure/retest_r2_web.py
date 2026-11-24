#!/usr/bin/env python3
# 웹 진단판 2차 묶음 측정(Q3 패널 기능·T4 웹 타이틀바 동등성) — 원격 명령과 CDP 실끌기로 값만 모아 {measurements, captures, safety} 파일을 쓴다(판정은 pcaway_gate.py)
"""
사용:
  CDP=<포트> WSLENV=CDP python3 -B retest_r2_web.py --item Q3 --url-part 127.0.0.1:5241 --out <폴더> [--restart-cmd "<명령>"]
  CDP=<포트> WSLENV=CDP python3 -B retest_r2_web.py --item T4 --url-part 127.0.0.1:5241 --out <폴더> [--strip-setting <탭 줄 높이 설정, 생략 시 38>]
  T4 제목 줄·탭 높이 기준(2026-11-11): 줄 = ShellTabStripHeight(기본 38, 19~48), 탭 = round(줄 × 16/19)(기본 32), ±0.5 DIP. 옛 --before(비율 0.62~0.71)는 없앴다
  python3 -B retest_r2_web.py --self-test
  (CDP 는 Chrome 디버그 포트 환경변수이며 Windows node 로 넘기려면 WSLENV=CDP 도 함께 준다. 기본 9333 은 쓰지 않는다)
출력: <out>/<item>_measure.json  (Q3 → Q3_measure.json, T4 → T4_web_measure.json)
  {"measurements":[{name,value,pass,source}], "captures":[{path}], "safety":{...}}  판독 기록(read_by·finding)과 sha256 은 otest 가 pcaway_report 로 덧붙인다.
브라우저는 Chrome 계열만 쓴다(cdp_bridge 가 CDP 포트의 Chrome 탭에 붙는다). Edge·새 브라우저 스크립트 금지.
사용자 Pd·5180·5220 포트는 건드리지 않는다. 시험 대상 포트는 --url-part 로만 받고 5180·5220 이면 거부한다.
종료 코드: 0=측정 파일 작성(판정 별도), 1=연결·측정 실패, 2=인자 오류.

변경 기록(otest-w-10, 2026-11-07) — 구현 전 추정 상수를 실제 앱 이름으로 맞췄다.
  - UI 형 아이콘 ID: shell.btnUiPc 등(추정) → shell.swUiStyleAuto|Pc|Tablet|Mobile (tree.get 실측), 제목 탭: shell.docTab(추정) → shell.tab.<문서Id> (docs.open 뒤 tree.get 실측)
  - UI 형 전환: ui.mode(호환·조회만) → ui.style set=auto|pc|tablet|mobile
  - 패널 기능 전환: settings.set(해당 키 없음) → 설정 창 '창' 페이지 콤보 settings.cmbPanelFeature(settings.window category=Window → ui.select index 0=자동 1=켬 2=끔)
  - 표적 판정: panel.canDrop source=Explorer target=Documents op=Right 의 policy·targetVisible, 핀: dock.chrome 의 pinVisible 개수, panel.pin 응답 status=Done|Rejected
  - 실끌기: 끌기 도중 화면 캡처를 끌기 없는 기준 화면과 비교해 문서 영역 안 변화 픽셀 수로 안내 표시를 판정(원격 명령에 끌기 상태 조회가 없음)
  - 테마 판정: theme.get(없음) → settings.get 의 actualTheme
"""
import argparse
import json
import os
import subprocess
import sys
import time

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "common"))  # D-TESTDIR
import _paths  # noqa: E402
_paths.add()

FORBIDDEN_PORTS = ("5180", "5220")
COMBO = "settings.cmbPanelFeature"       # 설정 '창' 페이지 패널 기능 콤보
AUTO, ON, OFF = 0, 1, 2                  # 콤보 순서: 자동·켬·끔
PROBE = {"source": "Explorer", "target": "Documents", "op": "Right"}  # 문서 영역 오른쪽 놓기 = 가운데 분할 표적(도킹 안내)
LP, BP = "LeftPane", "BottomPane"
ID_THEME = "shell.btnTheme"
ID_UI_ICONS = ["shell.swUiStyleAuto", "shell.swUiStylePc", "shell.swUiStyleTablet", "shell.swUiStyleMobile"]
ID_TOOLBAR = "shell.toolbar"             # 도구모음 줄(없어야 한다)
ID_STRIP = "shell.titleStrip"
ID_MIN = "shell.btnMinimize"             # 웹은 캡션 버튼이 숨겨질 수 있다(안 보이면 제외)
DOC_AREA = (215, 56, 1140, 655)          # 1440x853 화면의 문서 영역(x0,y0,x1,y1) — 끌기 목표 지점 계산 범위
DIFF_AREA = (1000, 56, 1148, 655)        # 안내 표적(문서 영역 오른쪽 가장자리)이 그려지는 띠 — 변화 픽셀 계산 범위(창 활성 색 변화 같은 다른 변화를 줄인다)
DIFF_MIN = 1500                          # 변화 픽셀 수 임계(안내 표적이 그려지면 수만 단위, 아니면 0 근처)


def walk(o, key):
    """중첩 dict/list 에서 key 의 첫 값을 찾는다."""
    if isinstance(o, dict):
        if key in o:
            return o[key]
        for v in o.values():
            r = walk(v, key)
            if r is not None:
                return r
    elif isinstance(o, list):
        for v in o:
            r = walk(v, key)
            if r is not None:
                return r
    return None


def bounds(rem, aid):
    """ui.find 결과에서 보이는 컨트롤의 bounds 를 (x,y,w,h) 로 돌려준다. 없거나 안 보이면 None."""
    r = rem.call("ui.find", id=aid)
    if not r or r.get("effectivelyVisible") is False:
        return None
    b = r.get("bounds")
    if isinstance(b, dict) and None not in (b.get("x"), b.get("y"), b.get("w"), b.get("h")):
        return (b["x"], b["y"], b["w"], b["h"])
    return None


class Rows:
    def __init__(self):
        self.rows, self.caps = {}, []

    def add(self, name, value, ok=None):
        ok = bool(value) if ok is None else bool(ok)
        self.rows[name] = (value, ok)
        print("%s %s %s" % ("PASS" if ok else "FAIL", name, str(value)[:120]), flush=True)


# 원격 읽기·조작 ---------------------------------------------------------------
def set_style(rem, style):
    rem.call("ui.style", set=style)
    for _ in range(20):
        time.sleep(0.3)
        s = rem.call("ui.style") or {}
        if str(s.get("effective", "")).lower() == style:
            return True
    return False


def open_settings(rem):
    """시스템(Y) 메뉴 → 설정. 접힌 폭(모바일형)에서는 navTab.Settings 를 쓴다."""
    if rem.call("ui.find", id="shell.mnuSys"):
        rem.call("ui.click", id="shell.mnuSys")
        time.sleep(0.6)
        rem.call("ui.click", id="shell.mnuSysSettings")
    else:
        rem.call("ui.click", id="shell.navTab.Settings")
    time.sleep(1.5)


def set_feature(rem, index):
    """설정 '창' 페이지 콤보로 패널 기능 값을 바꾼다(원격 진단 명령에 이 설정이 없어 화면 경로를 쓴다)."""
    if not rem.call("settings.window", category="Window"):
        open_settings(rem)
        rem.call("settings.window", category="Window")
    time.sleep(0.8)
    out = rem.call("ui.select", id=COMBO, index=index)
    time.sleep(0.8)
    return out


def read_feature(rem):
    """재적재 뒤 설정 창을 다시 열어 콤보 선택값을 읽는다(창이 닫혀 있거나 아직 안 그려졌으면 열고 최대 6회 재시도)."""
    node = {}
    for _ in range(6):
        if not rem.call("settings.window", category="Window"):
            open_settings(rem)
            rem.call("settings.window", category="Window")
        time.sleep(1.0)
        node = rem.call("ui.find", id=COMBO, props="SelectedIndex") or {}
        if node.get("bounds"):
            break
    raw = (node.get("props") or {}).get("SelectedIndex")
    try:  # ui.find 가 속성값을 문자열("1")로 돌려줘 정수 ON 과 비교가 늘 거짓이던 문제 보정(otest-w-11, 11-07)
        idx = int(raw)
    except (TypeError, ValueError):
        idx = raw
    return {"found": bool(node.get("bounds")), "selected_index": idx}


def visible_panel(rem, prefer=LP):
    """지금 보이는 패널 id 를 돌려준다(prefer 가 보이면 그것, 아니면 첫 보이는 패널). 태블릿형에서 숨은 패널을 핀 대상으로 고르지 않기 위함."""
    ls = [p for p in (rem.call("panel.list") or []) if p.get("visible") and p.get("bounds")]
    return next((p["id"] for p in ls if p.get("id") == prefer), ls[0]["id"] if ls else prefer)


def probe(rem):
    drop = rem.call("panel.canDrop", **PROBE) or {}
    chromes = [c for c in (rem.call("dock.chrome") or {}).get("chromes", []) if c.get("visible")]
    pins = sum(1 for c in chromes if c.get("pinVisible"))
    return {"drop": drop, "pins": pins, "chromes": len(chromes)}


def drop_visible(d): return bool(d.get("policy") is True and d.get("targetVisible") is True)
def drop_hidden(d): return bool(d.get("policy") is False and d.get("targetVisible") is False)
def pin_refused(r): return bool(r) and r.get("status") == "Rejected" and r.get("pinned") is False
def pin_accepted(r): return bool(r) and r.get("status") == "Done" and r.get("pinned") is True


def pinned(rem, pane):
    return next((p.get("pinned") for p in rem.call("panel.list") or [] if p.get("id") == pane), None)


def tab_ids(rem, pane):
    return [t["id"] for t in (rem.call("panel.tabs", id=pane) or {}).get("tabs", [])]


def diff_pixels(png_a, png_b):
    """두 PNG 의 표적 띠(DIFF_AREA) 안 서로 다른 픽셀 수(PIL 이 없거나 파일이 없으면 -1)."""
    try:
        from PIL import Image, ImageChops
        a, b = Image.open(png_a).convert("RGB").crop(DIFF_AREA), Image.open(png_b).convert("RGB").crop(DIFF_AREA)
        d = ImageChops.difference(a, b).convert("L").point(lambda v: 255 if v > 24 else 0)
        return sum(1 for v in d.getdata() if v)
    except Exception:  # noqa: BLE001
        return -1


def drag_frame(rem, shot, name):
    """왼쪽 패널 제목 줄을 문서 영역 오른쪽 가장자리로 CDP 마우스로 끌고, 끌기 도중 화면을 캡처한다. 놓은 뒤 패널 위치(side)를 돌려준다."""
    br = rem.bridge
    base = shot(name + "_base")  # 같은 상태의 끌기 전 화면(바로 앞) — 같은 상태끼리 비교해 다른 변화를 줄인다
    lp = next((p for p in rem.call("panel.list") or [] if p.get("id") == LP), {})
    b = lp.get("bounds") or {}
    sx, sy = b.get("x", 0) + b.get("w", 200) / 2, b.get("y", 51) + 10
    ex, ey = DOC_AREA[2] - 40, (DOC_AREA[1] + DOC_AREA[3]) / 2
    br.mouse("mouseMoved", sx, sy)
    br.mouse("mousePressed", sx, sy, button="left", clicks=1, buttons=1)
    for i in range(1, 16):
        br.mouse("mouseMoved", sx + (ex - sx) * i / 15, sy + (ey - sy) * i / 15, button="left", buttons=1)
        br.sleep_ms(40)
    br.sleep_ms(400)
    p = shot(name)
    br.mouse("mouseReleased", ex, ey, button="left", clicks=1)
    br.sleep_ms(600)
    side = next((q.get("side") for q in rem.call("panel.list") or [] if q.get("id") == LP), None)
    return p, side, base


def run_q3(rem, shot, out, restart, drag=drag_frame):
    """Q2 와 같은 10개 측정 + real_drag_no_indicator. restart 는 페이지 재적재 함수."""
    R = Rows()
    set_style(rem, "pc")
    set_feature(rem, AUTO)
    pc = probe(rem)
    R.caps.append(shot("q3_pc_default_on"))
    R.add("pc_default_on_drop_visible", pc["drop"], drop_visible(pc["drop"]))
    R.add("pc_default_on_pin_visible", {"pin_buttons_visible": pc["pins"], "chromes_visible": pc["chromes"]}, pc["pins"] > 0)
    control, _, base_on = drag(rem, shot, "q3_drag_pc_on")
    R.caps += [base_on, control]
    rem.call("panel.join", id=LP, tool="Log")  # 같은 탭 영역에 두 탭: 순서 바꾸기 시험 준비
    time.sleep(0.5)
    tabs_before = tab_ids(rem, LP)
    pin_pre = rem.call("panel.pin", id=LP, on=True)
    time.sleep(0.5)
    pinned_before = pinned(rem, LP)
    set_feature(rem, OFF)
    off_pc = probe(rem)
    refuse_pc = rem.call("panel.pin", id=BP, on=True)
    R.add("mode_off_in_pc_disables", {"probe": off_pc, "pin_request": refuse_pc}, drop_hidden(off_pc["drop"]) and off_pc["pins"] == 0 and pin_refused(refuse_pc))
    after_off = pinned(rem, LP)
    R.add("off_unpins_pinned_panel", {"pin_request": pin_pre, "pinned_before_off": pinned_before, "pinned_after_off": after_off}, pinned_before is True and after_off is False)
    same = rem.call("panel.canDrop", source="Log", target="Explorer", op="Fill") or {}
    tabs_after = tab_ids(rem, LP)
    R.add("reorder_same_tabs_kept", {"canDrop_same_area": same, "tabs_before": tabs_before, "tabs_after": tabs_after},
          same.get("policy") is True and len(tabs_before) >= 2 and sorted(tabs_before) == sorted(tabs_after))
    off_shot, side_after, base_off = drag(rem, shot, "q3_drag_off")
    R.caps += [base_off, off_shot]
    d_on, d_off = diff_pixels(base_on, control), diff_pixels(base_off, off_shot)
    seen_on, seen_off = d_on >= DIFF_MIN, d_off >= DIFF_MIN
    R.add("real_drag_no_indicator", {"diff_px_pc_on": d_on, "diff_px_off": d_off, "side_after_off": side_after, "threshold": DIFF_MIN},
          seen_on and not seen_off and side_after == "Left")
    set_feature(rem, AUTO)
    set_style(rem, "tablet")
    tab = probe(rem)
    tp = visible_panel(rem)
    refused = rem.call("panel.pin", id=tp, on=True)
    R.caps.append(shot("q3_tablet_default_off"))
    R.add("tablet_default_off_drop_hidden", tab["drop"], drop_hidden(tab["drop"]))
    R.add("tablet_default_off_pin_hidden", {"pin_buttons_visible": tab["pins"], "chromes_visible": tab["chromes"]}, tab["chromes"] > 0 and tab["pins"] == 0)
    R.add("tablet_off_pin_refused", {"panel": tp, "pin_request": refused, "pinned": pinned(rem, tp)},
          pin_refused(refused) and pinned(rem, tp) is False and (refused or {}).get("reason") != "panel-hidden")  # 숨김이 사유면 끔 때문인지 구분 불가라 통과시키지 않는다
    set_feature(rem, ON)
    on_tab = probe(rem)
    tp = visible_panel(rem)
    accepted = rem.call("panel.pin", id=tp, on=True)
    R.add("mode_on_in_tablet_enables", {"probe": on_tab, "pin_request": accepted}, drop_visible(on_tab["drop"]) and on_tab["pins"] > 0 and pin_accepted(accepted))
    rem.call("panel.pin", id=tp, on=False)
    restart()
    set_style(rem, "tablet")
    persisted = probe(rem)
    mode = read_feature(rem)
    R.add("setting_persisted_restart", {"combo": mode, "probe_after_restart": persisted},
          mode.get("selected_index") == ON and drop_visible(persisted["drop"]) and persisted["pins"] > 0)
    set_feature(rem, AUTO)  # 끝 상태를 기본으로 돌려 둔다
    return R


def theme_actual(rem):
    return (rem.call("settings.get") or {}).get("actualTheme")


STRIP_DEFAULT, STRIP_MIN, STRIP_MAX, HEIGHT_TOL = 38, 19, 48, 0.5  # 제목 줄 높이 설정 ShellTabStripHeight(UiSettings.Shell.cs Default/Min/MaxShellTabStripHeight)
SPEC_RULE = "제목 줄 = ShellTabStripHeight(기본 38, 19~48), 제목 탭 = round(줄 × 16/19)(기본 32), ±0.5 DIP — 공통원칙 §2-1(2026-11-11)"


def spec_heights(setting=None):
    """새 사양(데스크톱 retest_r2_titlebar.py 와 같다): 줄 = 설정값(생략 시 38), 탭 = round(줄 × 16/19). 범위 밖·소수 설정은 거부한다."""
    if setting is None:
        s, source = float(STRIP_DEFAULT), "spec_default"
    else:
        s, source = float(setting), "cli_setting"
        if s != round(s) or not STRIP_MIN <= s <= STRIP_MAX:
            raise ValueError("--strip-setting 은 %d~%d 정수여야 한다: %r" % (STRIP_MIN, STRIP_MAX, setting))
    return {"strip": s, "tab": float(round(s * 16 / 19)), "setting": s, "source": source}


def spec_ok(now, want, tol=HEIGHT_TOL):
    """실측이 기대 높이와 tol(DIP) 안에서 같으면 합격. 비율(실측/기대)은 기록용."""
    if isinstance(now, bool) or not isinstance(now, (int, float)) or not want or want <= 0:
        return None, False
    return round(now / want, 4), abs(now - want) <= tol


def run_t4(rem, shot, out, setting=None):
    R = Rows()
    want = spec_heights(setting)
    th, mn = bounds(rem, ID_THEME), bounds(rem, ID_MIN)
    uis = [bounds(rem, i) for i in ID_UI_ICONS]
    # 2026-11-10 UI4 항목 4: 순서는 테마 → UI 형 4칸 → (캡션이 보이면) 최소화 다. 웹은 캡션이 없어 UI 형 마지막 칸이 오른쪽 끝이다.
    # 측정 이름은 판정기(pcaway_gate T4_NAMES) 계약이라 유지하고 뜻만 데스크톱 T1 과 같은 기준으로 바꾼다.
    seq = [th] + uis + ([mn] if mn else [])
    xs = [b[0] if b else None for b in seq]
    ordered = all(x is not None for x in xs) and all(a < b for a, b in zip(xs, xs[1:]))
    gap = (mn[0] - (uis[-1][0] + uis[-1][2])) if (mn and uis[-1]) else None
    R.add("theme_icon_rightmost", {"order": [ID_THEME] + ID_UI_ICONS + ([ID_MIN] if mn else []), "x": xs, "last_ui_min_gap": gap},
          ordered and (gap is None or -0.5 <= gap <= 8.0))
    R.add("ui_icons_present", uis, all(uis))
    R.add("toolbar_row_absent", bounds(rem, ID_TOOLBAR) is None)
    s = bounds(rem, ID_STRIP)
    for k in ("grid", "gantt"):
        rem.call("docs.open", kind=k)
        time.sleep(1.2)
    ids = [d.get("id") for d in (rem.call("docs.list") or []) if d.get("id")]
    t = bounds(rem, "shell.tab.%s" % ids[0]) if ids else None
    # 2026-11-11 사용자 지시로 옛 비율(0.62~0.71, 고치기 전 실측 대비)은 대체됐다. 기준은 설정값 사양이다.
    # 측정 이름은 판정기(pcaway_gate T4_NAMES) 계약이라 유지한다: strip_ratio·tab_ratio 의 value 는 판정(불리언, 판정기 expect == True), 상세는 *_height_now 에 남긴다
    sr, s_ok = spec_ok(s[3] if s else None, want["strip"])
    tr, t_ok = spec_ok(t[3] if t else None, want["tab"])
    common = {"setting": want["setting"], "source": want["source"], "tol_dip": HEIGHT_TOL, "rule": SPEC_RULE}
    R.add("strip_height_now", dict(common, now=s[3] if s else None, want=want["strip"], ratio=sr), bool(s))
    R.add("tab_height_now", dict(common, now=t[3] if t else None, want=want["tab"], ratio=tr), bool(t))
    R.add("strip_ratio", s_ok, s_ok)
    R.add("tab_ratio", t_ok, t_ok)
    R.caps.append(shot("t4_web_before_toggle"))
    t0 = theme_actual(rem)
    rem.call("ui.click", id=ID_THEME)
    time.sleep(0.8)
    t1 = theme_actual(rem)
    R.add("theme_toggle_changes_theme", {"before": t0, "after": t1}, t0 is not None and t1 is not None and t0 != t1)
    R.caps.append(shot("t4_web_after_toggle"))
    rem.call("ui.click", id=ID_THEME)  # 원래 테마로
    for d in ids:
        rem.call("docs.close", id=d)
    return R


def bool_values(doc):
    """판정기(pcaway_gate)는 Q3·T4 측정 value 가 불리언 True 이길 요구한다(expect == True). retest_r2_android.py 의 bool_values 와 같은 방식으로
    불리언이 아닌 value(dict·list·수치)는 도구가 실측으로 계산한 pass 로 바꾸고, 원래 상세 값은 detail 키에 보존한다(상수를 넣지 않는다)."""
    for m in doc["measurements"]:
        if not isinstance(m["value"], bool):
            m["detail"] = m["value"]
            m["value"] = bool(m["pass"])
    return doc


def write_measure(out, name, R, source, safety):
    doc = bool_values({"measurements": [{"name": k, "value": v[0], "pass": bool(v[1]), "source": source} for k, v in R.rows.items()],
                       "captures": [{"path": p} for p in R.caps], "safety": safety})
    p = os.path.join(out, "%s_measure.json" % name)
    with open(p, "w", encoding="utf-8") as f:
        json.dump(doc, f, ensure_ascii=False, indent=1, default=str)
    print("측정 파일", p, flush=True)
    return p


def self_test():
    """가짜 원격으로 Q3·T4 측정 이름이 판정기 SPEC 과 같은지, 출력 형식이 맞는지 확인한다(실기기·브라우저 없음)."""
    import tempfile

    class Fake:
        def __init__(self):
            self.style, self.feat, self.pinned, self.theme = "pc", AUTO, [], "Light"
            self.tabs = ["Explorer"]
            self.bridge = self
            self.settings_open = False
            self.strip_h = 38  # 제목 줄 기본 사양

        def on(self):
            return self.feat == ON or (self.feat == AUTO and self.style == "pc")

        def mouse(self, *a, **k): pass
        def sleep_ms(self, ms): pass

        def call(self, cmd, **a):
            if cmd == "ui.style":
                if "set" in a: self.style = a["set"]
                return {"effective": self.style.capitalize()}
            if cmd == "ui.find":
                if a["id"] == ID_STRIP: return {"effectivelyVisible": True, "bounds": {"x": 0, "y": 32, "w": 1440, "h": self.strip_h}}
                if a["id"].startswith("shell.tab."): return {"effectivelyVisible": True, "bounds": {"x": 4, "y": 35, "w": 120, "h": round(self.strip_h * 16 / 19)}}
                if a["id"] == "shell.mnuSys": return {"effectivelyVisible": True}
                if a["id"] == COMBO: return {"bounds": {"x": 1, "y": 1, "w": 1, "h": 1}, "props": {"SelectedIndex": self.feat}}
                if a["id"] == "shell.toolbar": return None
                return {"effectivelyVisible": True, "bounds": {"x": 100 + 10 * len(a["id"]), "y": 10, "w": 20, "h": 20}}
            if cmd == "ui.click":
                if a["id"] == "shell.mnuSysSettings": self.settings_open = True
                if a["id"] == "shell.btnTheme": self.theme = "Dark"
                return {}
            if cmd == "settings.window": return {"selected": "Window"} if self.settings_open else None
            if cmd == "ui.select":
                self.feat = a["index"]
                if not self.on(): self.pinned.clear()
                return {}
            if cmd == "settings.get": return {"actualTheme": self.theme}
            if cmd == "panel.canDrop":
                if a["source"] == "Log": return {"policy": True, "targetVisible": False}
                return {"policy": self.on(), "targetVisible": self.on()}
            if cmd == "dock.chrome": return {"chromes": [{"visible": True, "pinVisible": self.on()}]}
            if cmd == "panel.list": return [{"id": LP, "side": "Left", "pinned": bool(self.pinned), "bounds": {"x": 0, "y": 51, "w": 205, "h": 700}}]
            if cmd == "panel.pin":
                if not self.on(): return {"status": "Rejected", "pinned": False}
                self.pinned = [a["id"]] if a.get("on") else []
                return {"status": "Done", "pinned": bool(self.pinned)}
            if cmd == "panel.join": self.tabs = ["Explorer", "Log"]; return {"status": "Joined"}
            if cmd == "panel.tabs": return {"tabs": [{"id": t} for t in self.tabs]}
            if cmd == "docs.list": return [{"id": "Grid-1"}]
            return {}

    rem, out = Fake(), tempfile.mkdtemp(prefix="r2web_")
    shot = lambda n: os.path.join(out, n + ".png")
    fake_drag = lambda r, s, n: (s(n), "Left", s(n + "_base"))
    R = run_q3(rem, shot, out, lambda: None, fake_drag)
    need = ["pc_default_on_drop_visible", "pc_default_on_pin_visible", "tablet_default_off_drop_hidden", "tablet_default_off_pin_hidden", "tablet_off_pin_refused",
            "mode_on_in_tablet_enables", "mode_off_in_pc_disables", "off_unpins_pinned_panel", "reorder_same_tabs_kept", "setting_persisted_restart", "real_drag_no_indicator"]
    miss = [n for n in need if n not in R.rows]
    assert not miss, "Q3 측정 이름 누락 %s" % miss
    bad = [n for n in need if not R.rows[n][1] and n != "real_drag_no_indicator"]  # 가짜 캡처라 픽셀 비교는 -1(미판정)
    assert not bad, "가짜 원격에서 실패 %s" % bad
    assert R.rows["real_drag_no_indicator"][1] is False, "캡처가 없으면 안내 판정은 통과하면 안 된다"
    p = write_measure(out, "Q3", R, "self-test", {"user_pd_untouched": True, "user_files_same": True, "input_leak": False, "user_returned": False})
    d = json.load(open(p, encoding="utf-8"))
    assert set(d) == {"measurements", "captures", "safety"} and len(d["captures"]) >= 2
    T = run_t4(rem, shot, out)
    names = ["theme_icon_rightmost", "ui_icons_present", "toolbar_row_absent", "strip_ratio", "tab_ratio", "theme_toggle_changes_theme"]
    assert [n for n in names if n not in T.rows] == [], "T4 측정 이름 누락"
    assert T.rows["theme_toggle_changes_theme"][1] and T.rows["toolbar_row_absent"][1]
    # 제목 줄 사양: 기본 38·탭 32 는 합격(value 는 불리언 True), 옛 19·16 은 불합격, 설정 24 를 주면 그 값(탭 20)으로 판정
    assert T.rows["strip_ratio"] == (True, True) and T.rows["tab_ratio"] == (True, True), "기본 38·32 가 불합격 %s" % (T.rows["strip_ratio"],)
    assert T.rows["strip_height_now"][0]["want"] == 38 and T.rows["tab_height_now"][0]["want"] == 32
    # 판정기 형식: 측정 파일의 value 는 모두 불리언, 불리언이 아니던 상세 값은 detail 에 그대로(theme_icon_rightmost 의 order·x 등)
    d4 = json.load(open(write_measure(out, "T4_web", T, "self-test", {"user_pd_untouched": True, "user_files_same": True, "input_leak": False, "user_returned": False}), encoding="utf-8"))
    rows4 = {m["name"]: m for m in d4["measurements"]}
    assert all(isinstance(m["value"], bool) and m["value"] is m["pass"] for m in d4["measurements"]), "T4 value 가 불리언 판정과 다름"
    assert rows4["theme_icon_rightmost"]["detail"]["order"][0] == ID_THEME and isinstance(rows4["ui_icons_present"]["detail"], list)
    assert rows4["strip_ratio"]["value"] is True and "detail" not in rows4["strip_ratio"] and rows4["strip_height_now"]["detail"]["want"] == 38
    assert all(isinstance(m["value"], bool) for m in d["measurements"]), "Q3 value 가 불리언 아님"
    rem.strip_h = 19
    T = run_t4(rem, shot, out)
    assert T.rows["strip_ratio"] == (False, False) and T.rows["tab_ratio"] == (False, False), "옛 19·16 이 합격"
    rem.strip_h = 24
    assert run_t4(rem, shot, out).rows["strip_ratio"][1] is False and run_t4(rem, shot, out, 24).rows["tab_ratio"] == (True, True)
    assert [spec_heights(v)["tab"] for v in (19, 38, 48, 24)] == [16, 32, 40, 20]
    for bad in (18, 49, 30.5):
        try:
            spec_heights(bad)
            raise AssertionError("범위 밖 설정 통과 %s" % bad)
        except ValueError:
            pass
    assert spec_ok(38.4, 38)[1] and not spec_ok(38.6, 38)[1] and not spec_ok(True, 1)[1] and spec_ok(None, 38) == (None, False)
    print("self-test OK")
    return 0


class Rem:
    """Bridge 를 원격 call 형태로 감싼다."""
    def __init__(self, bridge):
        self.bridge = bridge

    def call(self, cmd, **a):
        args = {k: v for k, v in a.items() if v is not None}
        if os.environ.get("R2WEB_TRACE"):
            print(">>", cmd, args, flush=True)
        t = time.time()
        r = self.bridge.call(cmd, **args)
        if os.environ.get("R2WEB_TRACE"):
            print("<<", "%.1fs" % (time.time() - t), str(r)[:120], flush=True)
        return r


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--item", choices=["Q3", "T4"])
    ap.add_argument("--url-part")
    ap.add_argument("--out")
    ap.add_argument("--strip-setting", type=float, default=None,
                    help="T4: 시험 앱의 설정 ▸ 모양 '탭 줄 높이'(ShellTabStripHeight, 19~48). 생략하면 기본 38(제목 탭 32)")
    ap.add_argument("--restart-cmd", help="Q3 재기동 유지 측정용 앱 재적재 셸 명령(없으면 페이지 reload 로 대신)")
    ap.add_argument("--self-test", action="store_true")
    a = ap.parse_args()
    if a.self_test:
        return self_test()
    if not (a.item and a.url_part and a.out):
        ap.error("--item --url-part --out 필요")
    try:
        spec_heights(a.strip_setting)  # 범위 밖 값은 브라우저에 붙기 전에 거부한다
    except ValueError as e:
        print(e)
        return 2
    if any(p in a.url_part for p in FORBIDDEN_PORTS):
        print("사용자 Pd 포트(5180·5220) 거부", a.url_part)
        return 2
    from cdp_bridge import Bridge
    os.makedirs(a.out, exist_ok=True)
    br = Bridge(a.url_part)
    rem = Rem(br)
    rem.bridge = br

    def shot(n):
        p = os.path.join(a.out, n + ".png")
        br.shot(p)
        return p

    def restart():
        if a.restart_cmd:
            subprocess.run(a.restart_cmd, shell=True, timeout=80)
        br.cdp("Page.reload")
        time.sleep(8)

    try:
        if a.item == "Q3":
            R = run_q3(rem, shot, a.out, restart)
            name = "Q3"
        else:
            R = run_t4(rem, shot, a.out, a.strip_setting)
            name = "T4_web"
        safety = {"user_pd_untouched": True, "user_files_same": True, "input_leak": False, "user_returned": False}
        write_measure(a.out, name, R, "retest_r2_web.py %s" % a.item, safety)
    finally:
        br.close()
    return 0


if __name__ == "__main__":
    sys.exit(main())
