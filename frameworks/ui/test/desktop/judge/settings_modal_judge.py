# B3 판정기 — 설정 창이 열린 동안 메인·MDI·SDI 가 앱 관문·OS(EnableWindow) 양쪽에서 막히고, 닫으면 전부 풀리는지 판정한다
"""
사용
  settings_modal_judge.py --info <격리>/remote-control.json --approved '<누가·언제>' --out <c27>/b3.json [--results <c27>/RESULTS.json]
  settings_modal_judge.py --self-test
기본 remote_only(원격 명령·앱 내부 포인터·휠 이벤트만, 실입력 없음 → PC 비움 불요). 깨끗한 격리 인스턴스(문서 0개)에서 돈다.
순서  MDI·SDI 문서를 연다 → 기준선(차단 없음 + 탐침이 실제로 반응하는지) → 메뉴 경로(shell.mnuSys → shell.mnuSysSettings)로 설정을 연다
      → modal.state·win_enabled.ps1(Win32 IsWindowEnabled) → 앱 내부 입력(input.wheel Ctrl·ui.pointerClick)이 메인·MDI·SDI 에서 효과 0
      → 모달 중 새 SDI 도 막힘 → settings.esc 로 닫기 → 전부 복구(관문 해제·OS 활성·SDI 누름이 다시 활성화).
기준선에서 탐침이 반응하지 않으면(예: Ctrl+휠 메인 글자 미구현) 그 차단 사례는 INVALID 다(막혔다고 치지 않는다).
"""
import base64
import json
import os
import subprocess
import sys
import time

import judge_session as session
from c27_judge_common import add_c27_args, run_c27, shown_font, verdict, win_path

PS1 = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "measure", "win_enabled.ps1"))
METHOD = ("메뉴 경로로 설정을 연 뒤 원격 modal.state(관문·osEnabled)·win_enabled.ps1(Win32 IsWindowEnabled)·앱 내부 Ctrl+휠·포인터 누름이 "
          "메인·MDI·SDI 에서 효과 0, 모달 중 새 SDI 차단, Esc 로 닫은 뒤 전부 복구 — 격리 진단 Release·remote_only")


def win_enabled(pid):
    """자기 PID 최상위 창의 [{hwnd,title,class,enabled,visible,owner}]."""
    out = subprocess.run(["powershell.exe", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", win_path(PS1), "-TargetPid", str(pid)],
                         capture_output=True, timeout=60).stdout.decode("ascii", "replace").strip()
    rows = json.loads(base64.b64decode(out.splitlines()[-1]).decode("utf-8")) if out else []
    return rows if isinstance(rows, list) else [rows]


def judge_modal_state(m, modal_on):
    """modal.state 판정. modal_on=True 면 메인·SDI 전부 관문 차단 + OS 비활성, 모달 창 자신은 열림. False 면 전부 풀림."""
    rows = m.get("windows") or []
    if not rows:
        return {"verdict": "INVALID", "why": "modal.state 창 목록 없음"}
    bad = {}
    if bool(m.get("active")) != modal_on:
        bad["active"] = m.get("active")
    for i, r in enumerate(rows):
        tag = "%s#%d" % (r.get("kind"), i)
        if modal_on and r.get("kind") in ("main", "sdi"):
            if r.get("gateBlocked") is not True:
                bad[tag + ".gateBlocked"] = r.get("gateBlocked")
            if r.get("osEnabled") is not False:
                bad[tag + ".osEnabled"] = r.get("osEnabled")  # None = OS 훅 없음(데스크톱 Windows 는 필수)
        if modal_on and r.get("isModal") and (r.get("gateBlocked") or r.get("osEnabled") is False):
            bad[tag + ".모달 창이 막힘"] = [r.get("gateBlocked"), r.get("osEnabled")]
        if not modal_on and (r.get("gateBlocked") or r.get("osEnabled") is False):
            bad[tag + ".복구 안 됨"] = [r.get("gateBlocked"), r.get("osEnabled")]
    if modal_on and not any(r.get("kind") == "sdi" for r in rows):
        bad["SDI 창 없음"] = True
    return verdict(bad, "모달 중 메인·SDI 차단" if modal_on else "닫은 뒤 전부 복구", name=m.get("name"))


def judge_win32(rows, blocked_titles, open_titles):
    """win_enabled 행에서 막혀야 할 제목은 enabled=false, 열려야 할 제목은 enabled=true."""
    bad = {}
    for t in blocked_titles:
        hit = [r for r in rows if r.get("title") == t]
        if not hit or any(r.get("enabled") for r in hit):
            bad["막혀야 함: " + t] = [r.get("enabled") for r in hit] or "창 없음"
    for t in open_titles:
        hit = [r for r in rows if r.get("title") == t]
        if not hit or not all(r.get("enabled") for r in hit):
            bad["열려야 함: " + t] = [r.get("enabled") for r in hit] or "창 없음"
    return verdict(bad, "Win32 IsWindowEnabled", rows=len(rows))


def judge_no_effect(baseline_moved, before, after, what):
    """모달 중 입력의 효과 0. 기준선에서 같은 탐침이 움직이지 않았으면 판정 불가(INVALID)."""
    if not baseline_moved:
        return {"verdict": "INVALID", "why": "%s: 기준선에서 탐침이 반응하지 않아 차단을 증명할 수 없다" % what, "before": before, "after": after}
    return verdict({} if before == after else {"값 변함": [before, after]}, what + " 효과 0", before=before, after=after)


def active_doc(s):
    docs = s.call("docs.list")
    docs = docs if isinstance(docs, list) else docs.get("docs", [])
    return next((d.get("id") for d in docs if d.get("active")), None)


def doc_state(s):
    g = s.call("settings.get")
    return [g.get("baseFontSize"), g.get("activeDocZoom")]


def wheel(s, dy, **target):
    try:
        return s.call("input.wheel", ctrl="true", dy=dy, **target).get("handled")
    except session.Abort as e:  # 가림 등 — 값 비교로 판정
        return "error: %s" % e


def press(s, **target):
    """앱 내부 포인터 누름. 막힌 창이 히트 테스트를 받지 않아 '가립니다' 로 거부되면 'hit-blocked'(막힘 증거)를 돌려준다."""
    try:
        s.call("ui.pointerClick", **target)
        return "sent"
    except session.Abort as e:
        if "가립니다" in str(e) or "가리" in str(e):
            return "hit-blocked"
        raise


def judge_press_blocked(baseline_ok, how, before, after, what):
    """모달 중 누름: 기준선에서 같은 누름이 반응했어야 하고, 이번엔 거부(hit-blocked)됐거나 보내져도 효과 0 이어야 한다."""
    out = judge_no_effect(baseline_ok, before, after, what)
    return dict(out, press=how)


def judge(s):
    if s.call("window.load.state").get("docs"):
        raise session.Abort("INVALID", "문서가 열려 있다(깨끗한 격리 폴더로 다시)")
    cases = {}
    modal = False
    try:
        s.call("docs.open", kind="grid")
        time.sleep(2.5)
        s.call("docs.open", kind="grid", sdi="true")
        time.sleep(3)
        docs = s.call("docs.list")
        docs = docs if isinstance(docs, list) else docs.get("docs", [])
        mdi, sdi = docs[0]["id"], docs[-1]["id"]
        cases["b3.baseline.state"] = judge_modal_state(s.call("modal.state"), False)
        # 기준선 — 탐침이 실제로 반응하는지(진양성)
        f0 = shown_font(s)
        wheel(s, 1, id="shell.mnuFile")
        f1 = shown_font(s)
        wheel(s, -1, id="shell.mnuFile")
        s.call("docs.activate", doc=mdi)
        time.sleep(0.5)
        base_how = press(s, id="grid.dgMain", doc=sdi)  # 기준선(모달 없음) — 이 누름이 SDI 를 활성화해야 탐침이 산다
        time.sleep(0.8)
        sdi_click_activates = base_how == "sent" and active_doc(s) == sdi
        cases["b3.baseline.sdi_pointer"] = verdict({} if sdi_click_activates else {"기준선 누름": [base_how, active_doc(s)]},
                                                   "기준선: SDI 누름이 활성화(탐침 대조)")
        d0 = doc_state(s)
        wheel(s, 1, id="grid.dgMain", doc=mdi)
        doc_moved = doc_state(s) != d0
        wheel(s, -1, id="grid.dgMain", doc=mdi)
        s.call("docs.activate", doc=mdi)
        time.sleep(0.8)
        titles = [w.get("title") for w in s.call("windows.list") if not w.get("managed")]
        # 설정 열기(메뉴 경로)
        s.call("ui.click", id="shell.mnuSys")
        time.sleep(0.6)
        s.call("ui.click", id="shell.mnuSysSettings")
        time.sleep(2)
        modal = True
        m = s.call("modal.state")
        cases["b3.modal.state"] = judge_modal_state(m, True)
        own = [r.get("title") for r in m.get("windows") or [] if r.get("isModal")]
        cases["b3.modal.win32"] = judge_win32(win_enabled(s.pid), titles, own)
        s.capture("modal_main")
        before = shown_font(s)
        wheel(s, 1, id="shell.mnuFile")
        cases["b3.input.main_ctrl_wheel"] = judge_no_effect(f1 != f0, before, shown_font(s), "메인 크롬 Ctrl+휠")
        for name, doc in (("mdi", mdi), ("sdi", sdi)):
            before = doc_state(s)
            wheel(s, 1, id="grid.dgMain", doc=doc)
            cases["b3.input.%s_ctrl_wheel" % name] = judge_no_effect(doc_moved, before, doc_state(s), name.upper() + " 문서 Ctrl+휠")
        how = press(s, id="grid.dgMain", doc=sdi)
        time.sleep(0.8)
        cases["b3.input.sdi_pointer"] = judge_press_blocked(sdi_click_activates, how, mdi, active_doc(s), "SDI 포인터 누름(활성 문서)")
        how = press(s, id="shell.mnuFile")
        time.sleep(0.6)
        opened = (s.call("ui.find", id="shell.mnuFile", props="IsSubMenuOpen").get("props") or {}).get("IsSubMenuOpen")
        cases["b3.input.main_menu_pointer"] = verdict({} if opened == "False" else {"메뉴 열림": opened}, "메인 메뉴 포인터 누름 무반응", press=how)
        s.call("docs.open", kind="form", sdi="true")  # 모달 중 새로 뜬 SDI 도 막혀야 한다
        time.sleep(3)
        cases["b3.modal.new_sdi"] = judge_modal_state(s.call("modal.state"), True)
        cases["b3.modal.still_open"] = verdict({} if s.call("modal.state").get("active") else {"모달 풀림": True}, "입력 뒤에도 설정 유지")
        # 닫기 → 복구
        s.call("settings.esc")
        time.sleep(1.5)
        modal = False
        after = s.call("modal.state")
        cases["b3.closed.state"] = judge_modal_state(after, False)
        titles = [w.get("title") for w in s.call("windows.list") if not w.get("managed")]
        cases["b3.closed.win32"] = judge_win32(win_enabled(s.pid), [], titles)
        how = press(s, id="grid.dgMain", doc=sdi)  # 해제 뒤 같은 누름이 다시 반응해야 한다
        time.sleep(0.8)
        cases["b3.closed.sdi_pointer"] = (verdict({} if how == "sent" and active_doc(s) == sdi else {"해제 뒤 누름": [how, active_doc(s)]},
                                                  "닫은 뒤 SDI 누름이 다시 활성화")
                                          if sdi_click_activates else {"verdict": "INVALID", "why": "기준선에서 SDI 누름이 활성화하지 않았다"})
    finally:
        if modal:
            s.call("settings.esc")
            time.sleep(1)
        s.call("docs.close", all=True)
    return cases


def self_test():
    row = lambda kind, gate, os_, modal=False: {"kind": kind, "gateBlocked": gate, "osEnabled": os_, "isModal": modal}  # noqa: E731
    on = {"active": True, "name": "settings", "windows": [row("main", True, False), row("sdi", True, False), row("system", False, True, True)]}
    off = {"active": False, "windows": [row("main", False, True), row("sdi", False, True)]}
    rows = [{"title": "A", "enabled": False}, {"title": "S", "enabled": True}]
    return session.run_cases([
        ("모달 PASS", judge_modal_state(on, True)["verdict"] == "PASS"),
        ("SDI 관문 안 막힘 FAIL", judge_modal_state(dict(on, windows=[row("main", True, False), row("sdi", False, False)]), True)["verdict"] == "FAIL"),
        ("OS 훅 없음(None) FAIL", judge_modal_state(dict(on, windows=[row("main", True, None), row("sdi", True, False)]), True)["verdict"] == "FAIL"),
        ("모달 창 자신이 막힘 FAIL", judge_modal_state(dict(on, windows=[row("main", True, False), row("sdi", True, False), row("system", True, True, True)]), True)["verdict"] == "FAIL"),
        ("SDI 없음 FAIL", judge_modal_state(dict(on, windows=[row("main", True, False)]), True)["verdict"] == "FAIL"),
        ("active 꺼짐 FAIL", judge_modal_state(dict(on, active=False), True)["verdict"] == "FAIL"),
        ("복구 PASS", judge_modal_state(off, False)["verdict"] == "PASS"),
        ("복구 안 됨 FAIL", judge_modal_state(dict(off, windows=[row("main", False, False)]), False)["verdict"] == "FAIL"),
        ("빈 목록 INVALID", judge_modal_state({"windows": []}, True)["verdict"] == "INVALID"),
        ("Win32 PASS", judge_win32(rows, ["A"], ["S"])["verdict"] == "PASS"),
        ("Win32 막혀야 하는데 열림 FAIL", judge_win32(rows, ["S"], [])["verdict"] == "FAIL"),
        ("Win32 창 없음 FAIL", judge_win32(rows, ["Z"], [])["verdict"] == "FAIL"),
        ("효과 0 PASS", judge_no_effect(True, 14, 14, "w")["verdict"] == "PASS"),
        ("효과 있음 FAIL", judge_no_effect(True, 14, 15, "w")["verdict"] == "FAIL"),
        ("기준선 무반응 INVALID", judge_no_effect(False, 14, 14, "w")["verdict"] == "INVALID"),
        ("탐침 스크립트 존재", os.path.isfile(PS1)),
        ("모달 중 누름 거부(hit-blocked) + 효과 0 → PASS", judge_press_blocked(True, "hit-blocked", "M", "M", "w")["verdict"] == "PASS"),
        ("모달 중 누름이 활성화 → FAIL", judge_press_blocked(True, "sent", "M", "S", "w")["verdict"] == "FAIL"),
        ("기준선 누름 무반응 → INVALID(거짓 PASS 방지)", judge_press_blocked(False, "hit-blocked", "M", "M", "w")["verdict"] == "INVALID"),
        ("press: 가림 오류 → hit-blocked", press(_Blocked(), id="x") == "hit-blocked"),
    ])


class _Blocked:
    """자기시험용 — ui.pointerClick 이 가림 오류를 낸다."""

    def call(self, cmd, **kw):
        raise session.Abort("INVALID", "ui.pointerClick 실패: 대상 가운데를 다른 요소가 가립니다: 없음")


def main():
    ap = add_c27_args(session.parser(__doc__))
    ap.set_defaults(remote_only=True)
    args = ap.parse_args()
    if args.self_test:
        return self_test()
    return run_c27(args, "settings_modal_judge", judge, {"B3": ("b3.", METHOD)})


if __name__ == "__main__":
    sys.exit(main())
