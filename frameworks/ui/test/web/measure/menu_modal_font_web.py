# 사이클27 웹 보조 판정(B1·B3·B4) — headless Chrome 에 브라우저 번들을 띄워 메뉴 아이콘·설정 플라이아웃 모달·메인 크롬 Ctrl+휠 글자를 본다
"""
사용
  menu_modal_font_web.py --bundle <wwwroot> --port <자기 포트> --out <c27>/web.json [--results <c27>/RESULTS.json]
  menu_modal_font_web.py --self-test
--port 는 반드시 준다(5791 사용자 서버·5180·5220·5242 거부). 이 도구가 127.0.0.1:<포트> 정적 서버를 직접 띄우고 끝나면 닫는다(serve_browser.sh 불요).
새 프로필·?fresh 라 사용자 데이터를 건드리지 않는다. 입력은 headless Chrome 안의 Playwright 마우스·키보드(사용자 PC 입력과 무관).
B1  문서 1개를 연 채 menu.icons 4메뉴 판정과 열린 창 칸 판정(데스크톱 menu_icons_judge 의 judge_menu·winlist_case 그대로 — 숨은 칸 제외)
    + '파일'·'창' 메뉴를 실클릭으로 펼친 화면 캡처(창 메뉴 캡처는 활성 ✓ 판독용).
B3  메뉴 경로로 설정을 연 뒤 메뉴 줄을 실클릭 → 설정이 닫히지 않고(바깥 누름 닫기 없음) 메뉴도 열리지 않아야 한다. Esc 로만 닫히고 닫은 뒤 메뉴 클릭이 다시 동작.
B4  메뉴 줄 위 Ctrl+실휠 → 메뉴 FontSize 정확히 +1(되돌림 포함), 문서 표 위 Ctrl+실휠 → 문서 값만 변하고 시스템 글자 불변.
결과는 scope=web 이며 RESULTS.json 에는 B1_web·B3_web·B4_web 으로만 넣는다(데스크톱 B1·B3·B4 판정을 덮지 않음).
--part b10  문서 2개를 연 채 창 메뉴 열린 창 칸을 확대 크롭해 나란히(b10_compare.png) → B10_web(판독 대기 RECORDED).
"""
import os
import sys
import time

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "common"))
import _paths  # noqa: E402

_paths.add()
import judge_session as session  # noqa: E402
from c27_b10 import B10_METHOD, active_doc, b10_cases, b10_open, slots_of  # noqa: E402
from c27_judge_common import add_c27_args, run_c27, shown_font, verdict  # noqa: E402
from menu_icons_judge import TOP_MENUS, judge_menu, winlist_case  # noqa: E402

USER_SERVER_PORT = 5791  # 사용자가 띄운 브라우저 서버(INFRA) — 절대 쓰지 않는다
METHOD = {"B1_web": "headless Chrome 웹 번들에서 원격 menu.icons 4메뉴 판정 + 파일 메뉴 실클릭 캡처",
          "B3_web": "headless Chrome 에서 설정 플라이아웃 연 뒤 메뉴 줄 실클릭(바깥 누름) 무반응·Esc 닫기·닫은 뒤 복구 확인",
          "B4_web": "headless Chrome 에서 메뉴 줄·문서 표 위 Ctrl+실휠(Playwright) 전후 메뉴 FontSize·문서 값 비교"}


def port_problem(port):
    if port is None:
        return "필수 인자 없음: --port(자기 포트를 명시)"
    if port == USER_SERVER_PORT or port in session.WEB_REFUSED_PORTS:
        return "쓰면 안 되는 포트 %d" % port
    return None


def menu_open(s):
    return (s.call("ui.find", id="shell.mnuFile", props="IsSubMenuOpen").get("props") or {}).get("IsSubMenuOpen") == "True"


def click(s, cid, **scope):
    pt = s.web.center(cid, **scope)
    if pt is None:
        raise session.Abort("INVALID", "누를 자리를 찾지 못했다: %s" % cid)
    s.web.page.mouse.click(*pt)
    time.sleep(0.8)


def ctrl_wheel(s, cid, notches, **scope):
    """cid 가운데에서 Ctrl+휠. notches>0 = 위로(확대)."""
    pt = s.web.center(cid, **scope)
    if pt is None:
        raise session.Abort("INVALID", "휠 자리를 찾지 못했다: %s" % cid)
    page = s.web.page
    page.mouse.move(*pt)
    page.keyboard.down("Control")
    try:
        for _ in range(abs(notches)):
            page.mouse.wheel(0, -120 if notches > 0 else 120)
            time.sleep(0.35)
    finally:
        page.keyboard.up("Control")
    time.sleep(0.6)


def doc_values(s):
    g = s.call("settings.get")
    return [g.get("baseFontSize"), g.get("activeDocZoom")]


def judge_modal_web(active_after_click, menu_opened, closed_by_esc, menu_after_close):
    bad = {}
    if not active_after_click:
        bad["바깥 누름에 설정이 닫힘"] = True
    if menu_opened:
        bad["모달 중 메뉴가 열림"] = True
    if not closed_by_esc:
        bad["Esc 로 닫히지 않음"] = True
    if not menu_after_close:
        bad["닫은 뒤 메뉴 클릭 무반응"] = True
    return verdict(bad, "설정 플라이아웃 = 앱 모달")


def b1(s, cases):
    s.call("docs.open", kind="grid")  # 데스크톱과 같게 문서 1개를 열어 열린 창 칸이 보이게 한다
    time.sleep(3)
    cases["w.b1.winlist"] = winlist_case(s)
    for top in TOP_MENUS:
        listed = s.call("menu.list", id=top).get("items") or []
        menu = (s.call("menu.icons", id=top).get("menus") or [{}])[0]
        cases["w.b1.menu." + top] = judge_menu(menu, [it["id"] for it in listed if it.get("id") and not it.get("separator")])
    click(s, "shell.mnuFile")
    s.capture("b1_file_menu")
    s.web.page.keyboard.press("Escape")
    time.sleep(0.6)
    click(s, "shell.mnuWindow")  # 열린 창 칸(문서 아이콘 + 활성 ✓) 판독용 화면
    s.capture("b1_window_menu")
    s.web.page.keyboard.press("Escape")
    time.sleep(0.6)
    s.call("docs.close", all=True)
    time.sleep(0.8)


def b3(s, cases):
    s.call("ui.click", id="shell.mnuSys")
    time.sleep(0.6)
    s.call("ui.click", id="shell.mnuSysSettings")
    time.sleep(2)
    if not s.call("modal.state").get("active"):
        cases["w.b3.modal"] = {"verdict": "FAIL", "why": "설정을 열었는데 modal.state.active=false"}
        s.call("settings.esc")
        return
    click(s, "shell.mnuFile")  # 플라이아웃 바깥(메뉴 줄) 실클릭
    active, opened = bool(s.call("modal.state").get("active")), menu_open(s)
    s.capture("b3_modal_click_outside")
    s.web.page.keyboard.press("Escape")
    time.sleep(1.2)
    by_esc = not s.call("modal.state").get("active")
    if not by_esc:
        s.call("settings.esc")  # 다음 사례를 위해 정리(판정은 FAIL 로 남는다)
        time.sleep(1)
    click(s, "shell.mnuFile")
    after = menu_open(s)
    s.web.page.keyboard.press("Escape")
    time.sleep(0.6)
    cases["w.b3.modal"] = judge_modal_web(active, opened, by_esc, after)


def b4(s, cases):
    f0 = shown_font(s)
    ctrl_wheel(s, "shell.mnuFile", 1)
    f1 = shown_font(s)
    s.capture("b4_menu_bar_plus1")
    ctrl_wheel(s, "shell.mnuFile", -1)
    f2 = shown_font(s)
    ok = None not in (f0, f1, f2) and f1 - f0 == 1 and f2 == f0
    cases["w.b4.menu_bar"] = verdict({} if ok else {"값": [f0, f1, f2]}, "메뉴 줄 Ctrl+휠 +1 후 되돌림", values=[f0, f1, f2])
    s.call("docs.open", kind="grid")
    time.sleep(3)
    doc = s.call("window.load.state")["docs"][-1]["id"]
    before, doc0 = shown_font(s), doc_values(s)
    ctrl_wheel(s, "grid.dgMain", 1, doc=doc)
    after, doc1 = shown_font(s), doc_values(s)
    s.capture("b4_doc_wheel")
    ctrl_wheel(s, "grid.dgMain", -1, doc=doc)
    bad = {"시스템 글자 변함": [before, after]} if before != after else {}
    if doc0 == doc1:
        bad["문서 값이 안 변함(휠이 문서에 닿지 않음)"] = doc0
    cases["w.b4.doc_grid"] = verdict(bad, "문서 위 Ctrl+휠 = 문서만 변함", doc=[doc0, doc1])
    s.call("docs.close", all=True)


def b10(s, cases):
    """B10(D-5): 문서 2개를 연 채 창 메뉴를 실클릭으로 펼쳐 메뉴 전체를 찍고 열린 창 칸을 ui.find 경계대로 잘라 나란히 둔다.
    표 문서를 활성화해 다시 펼쳐 찍고 재조회한다(✓ 이동). 판정 규칙은 데스크톱과 같은 c27_b10.judge_b10_field."""
    slots, docs, active = b10_open(s)
    click(s, "shell.mnuWindow")
    img, _ = s.capture("b10_window_menu")
    crops = []
    for it in (x for x in slots if x.get("visible")):
        b = s.call("ui.find", id=it["id"])["bounds"]
        crops.append(img.crop((b["x"], b["y"], b["x"] + min(b["w"], 200), b["y"] + b["h"])))
    s.web.page.keyboard.press("Escape")
    time.sleep(0.6)
    s.call("docs.activate", doc=docs[0]["id"])
    time.sleep(1)
    click(s, "shell.mnuWindow")
    s.capture("b10_window_menu_moved")
    s.web.page.keyboard.press("Escape")
    time.sleep(0.6)
    cases.update(b10_cases(s, slots, docs, active, crops if len(crops) >= 2 else [], slots_of(s), active_doc(s), prefix="w.b10."))
    if len(crops) < 2 and "w.b10.read" in cases:
        cases["w.b10.read"] = {"verdict": "INVALID", "why": "보이는 열린 창 칸 %d개 — 2칸 이상이 필요하다" % len(crops)}
    s.call("docs.close", all=True)


def tidy(s):
    """축이 중간에 멈췄을 때 다음 축이 열린 설정·메뉴에 가리지 않게 정리한다(설정이 없으면 settings.esc 가 실패해도 무시)."""
    s.web.page.keyboard.press("Escape")
    time.sleep(0.4)
    try:
        if s.call("settings.esc").get("viewOpen"):
            time.sleep(1)
    except session.Abort:
        pass
    s.call("docs.close", all=True)
    time.sleep(0.8)


def judge(s, parts=None):
    cases = {}
    for part in parts or (b1, b3, b4):
        try:
            part(s, cases)
        except session.Abort as e:  # 한 축이 막혀도 나머지 축은 잰다
            cases["w.%s.error" % part.__name__] = {"verdict": e.verdict, "why": str(e)}
            tidy(s)
    return cases


def self_test():
    return session.run_cases([
        ("포트 없음 거부", port_problem(None) is not None),
        ("5791 거부", port_problem(5791) is not None),
        ("5180 거부", port_problem(5180) is not None),
        ("자기 포트 허용", port_problem(5793) is None),
        ("모달 PASS", judge_modal_web(True, False, True, True)["verdict"] == "PASS"),
        ("바깥 누름 닫힘 FAIL", judge_modal_web(False, False, True, True)["verdict"] == "FAIL"),
        ("모달 중 메뉴 열림 FAIL", judge_modal_web(True, True, True, True)["verdict"] == "FAIL"),
        ("Esc 안 닫힘 FAIL", judge_modal_web(True, False, False, True)["verdict"] == "FAIL"),
        ("닫은 뒤 무반응 FAIL", judge_modal_web(True, False, True, False)["verdict"] == "FAIL"),
        ("메뉴 판정 공유", judge_menu({"items": []}, [])["verdict"] == "INVALID"),
    ])


def main():
    ap = add_c27_args(session.parser(__doc__))
    ap.add_argument("--part", choices=("all", "b10"), default="all", help="b10 = 열린 창 칸 활성 표시 판독 캡처(RESULTS B10_web)")
    ap.set_defaults(port=None)
    args = ap.parse_args()
    if args.self_test:
        return self_test()
    bad = port_problem(args.port) or (None if args.bundle else "필수 인자 없음: --bundle")
    if bad:
        print('{"tool": "menu_modal_font_web", "verdict": "DEFERRED", "reason": "%s"}' % bad)
        return session.common.RC["DEFERRED"]
    if args.part == "b10":  # 판독용(B10) — 기본 실행(B1·B3·B4)과 따로
        return run_c27(args, "menu_modal_font_web", lambda s: judge(s, (b10,)), {"B10_web": ("w.b10.", B10_METHOD)})
    items = {k: ("w.%s." % k[:2].lower(), METHOD[k]) for k in METHOD}
    return run_c27(args, "menu_modal_font_web", judge, items)


if __name__ == "__main__":
    sys.exit(main())
