# AC-015·020 판정기 — 제목 탭 3방식, 표시 자리별 아이콘 키, title 방식·정지 조건의 불러오는 중 대체 표시를 판정한다
"""
사용
  title_tab_icons_judge.py --info <격리>/remote-control.json [--away-state <pc_away.json>] --approved '<누가·언제>' --out <결과.json>
  (기본 remote_only — 원격 명령·앱 렌더 캡처만 쓰므로 PC 비움 검사를 하지 않는다)
  title_tab_icons_judge.py --bundle <wwwroot> --port <포트> --out <결과.json>      (웹 결과)
  title_tab_icons_judge.py --self-test

방식 전환은 설정 화면 콤보(settings.cmbTitleTabDisplay)를 거친다. 아이콘 키는 WindowIcon 의 EffectiveIconKey, 표시는 DisplayState 를 읽는다.
자리를 찾을 식별자가 없으면 그 자리는 INVALID 다(보였다고 치지 않는다). NAV 자리는 ui.style set=mobile 로 모바일형을 강제한 뒤
문서 콤보(shell.cmbDocs)의 아이콘(NavDocIcon)을 같은 방식으로 보고 원래 형으로 되돌린다. 결과에 places_pass(PASS 자리 수, 6곳 중)를 남긴다.
깨끗한 격리 인스턴스에서만 돈다(설정을 바꾸고 문서를 연다).
"""
import sys
import time

import judge_session as session

MODES = (("icon_title", 0, True, True), ("icon", 1, True, False), ("title", 2, False, True))  # 이름·콤보 차례·아이콘·제목
PLACES = {  # 자리 → 찾을 이름(데스크톱 OS 독립 창은 DocIcon, 관리형은 PdSdiTitleIcon)
    "title_tab": ("TabIcon",),
    "mdi_header": ("PdDocIcon",),
    "explorer": ("shell.explorer.icon.Grid",),
    "dock_tab": ("HeaderBusy",),
    "sdi_title": ("DocIcon", "PdSdiTitleIcon"),
    "nav": ("NavDocIcon",),  # 모바일형 문서 콤보(MobileNavView shell.cmbDocs)의 WindowIcon
}
PROPS = "EffectiveIconKey,DisplayState,IsBusyShown"


def verdict(problems, why, **data):
    return dict(data, verdict="FAIL" if problems else "PASS", why=why, problems=problems)


def judge_mode(icon, title, want_icon, want_title):
    if icon is None or title is None:
        return {"verdict": "INVALID", "why": "탭 아이콘·제목을 찾지 못했다"}
    bad = {}
    if bool(icon.get("visible")) != want_icon:
        bad["아이콘 보임"] = [icon.get("visible"), want_icon]
    if bool(title.get("visible") and title["bounds"]["w"] > 0) != want_title:
        bad["제목 보임"] = [title.get("visible"), want_title]
    if icon.get("visible") and title.get("visible") and icon["bounds"]["x"] >= title["bounds"]["x"]:
        bad["아이콘이 제목 앞"] = [icon["bounds"]["x"], title["bounds"]["x"]]
    return verdict(bad, "방식대로 보이고 아이콘은 제목 앞")


def judge_place(node, want, need_visible=True):
    if node is None:
        return {"verdict": "INVALID", "why": "자리를 찾을 식별자가 없다"}
    bad, got = {}, (node.get("props") or {}).get("EffectiveIconKey")
    if not want:
        return {"verdict": "INVALID", "why": "문서의 아이콘 키가 비어 있다"}
    if got != want:
        bad["키"] = [got, want]
    if need_visible and not node.get("visible"):
        bad["보임"] = False
    return verdict(bad, "그 창의 메뉴 아이콘", key=got)


def shown_state(nodes):
    """시각 트리의 TabIcon 아래 IconSpinner 로 본 표시(spinner|static). 못 찾으면 None."""
    for node in nodes:
        children = node.get("children", [])
        if node.get("name") == "TabIcon":
            spinner = [c for c in children if c.get("type") == "IconSpinner"]
            return ("spinner" if spinner[0].get("isVisible") else "static") if spinner else None
        hit = shown_state(children)
        if hit:
            return hit
    return None


def judge_busy(node, loading, display, visible=True):
    if not loading:
        return {"verdict": "INVALID", "why": "불러오는 중 상태를 만들지 못했다"}
    if node is None:
        return {"verdict": "INVALID", "why": "표시 자리를 찾지 못했다"}
    bad, props = {}, node.get("props") or {}
    if props.get("DisplayState") != display:
        bad["표시"] = [props.get("DisplayState"), display]
    if str(props.get("IsBusyShown")) != "True":
        bad["IsBusyShown"] = props.get("IsBusyShown")
    if visible and not node.get("visible"):
        bad["보임"] = False
    return verdict(bad, "불러오는 중 표시 = %s" % display)


def nav_case(s, find, key):
    """모바일형(NAV)을 강제해 문서 콤보 아이콘 키를 보고 원래 형으로 되돌린다. 기기가 모바일형을 허용하지 않으면 INVALID."""
    before = s.call("ui.style")
    if "mobile" not in [str(a).lower() for a in before.get("allowed") or []]:
        return {"verdict": "INVALID", "why": "이 기기(%s)는 모바일형을 허용하지 않는다" % before.get("device"), "allowed": before.get("allowed")}
    try:
        s.call("ui.style", set="mobile")
        for _ in range(20):  # 자동 전환은 디바운스 뒤 반영
            if str(s.call("ui.style").get("effective")).lower() == "mobile":
                break
            time.sleep(0.5)
        time.sleep(1.5)
        result = judge_place(find(*PLACES["nav"]), key)
        s.capture("place_nav")
        return dict(result, effective=s.call("ui.style").get("effective"))
    finally:
        s.call("ui.style", set=str(before.get("setting") or "auto").lower())
        time.sleep(2)


def places_pass(cases):
    return sum(1 for name, c in cases.items() if name.startswith("place.") and c.get("verdict") == "PASS")


def add_places_pass(path):
    """결과 파일에 places_pass(PASS 자리 수)를 더한다. 결과가 없으면 그대로 둔다."""
    import json

    try:
        with open(path, encoding="utf-8") as f:
            out = json.load(f)
    except (OSError, ValueError):
        return
    out["places_pass"] = places_pass(out.get("cases") or {})
    out["places_total"] = len(PLACES)
    with open(path, "w", encoding="utf-8") as f:
        json.dump(out, f, ensure_ascii=False, indent=1)


def judge(s):
    if s.call("window.load.state").get("docs"):
        raise session.Abort("INVALID", "문서가 열려 있다(깨끗한 격리 폴더로 다시)")

    def find(*names, **kw):
        found = None
        for name in names:
            try:
                node = s.call("ui.find", id=name, props=PROPS, **kw)
            except session.Abort as e:
                if e.args and "찾지 못" in str(e):
                    continue
                raise
            if node.get("visible"):
                return node
            found = found or node
        return found

    def select(control, index):
        s.call("ui.click", id="shell.mnuSys")  # 하위 항목은 메뉴를 펼쳐야 시각 트리에 생긴다
        time.sleep(0.6)
        s.call("ui.click", id="shell.mnuSysSettings")
        time.sleep(1.5)
        s.call("settings.window", category="Window")
        time.sleep(0.8)
        s.call("ui.select", id=control, index=index)
        time.sleep(0.8)
        s.call("settings.esc")
        time.sleep(1.2)

    def loading_case(display):
        delay = s.call("sample.load")["delayMs"]
        s.call("sample.load", delayMs=3000)
        try:
            s.call("grid.refresh", doc=doc["id"])
            time.sleep(0.7)
            on = any(d["id"] == doc["id"] and d["isLoading"] for d in s.call("window.load.state")["docs"])
            node = find("TabIcon")
            if node and node["props"].get("DisplayState") in (None, "(속성 없음)"):
                # 웹 Release 는 쓰이지 않는 DisplayState 를 잘라낸다 — 자식 IconSpinner 의 보임으로 같은 값을 만든다
                node["props"]["DisplayState"] = shown_state(s.call("tree.get", named=False, depth=400)["tree"])
            s.capture("busy_" + display)
            return judge_busy(node, on, display)
        finally:
            s.call("sample.load", delayMs=delay)
            time.sleep(3.5)

    cases = {}
    level = s.call("style.state").get("motionLevel") or "system"
    try:
        s.call("docs.open", kind="grid")
        time.sleep(3)
        doc = s.call("window.load.state")["docs"][0]
        key = doc.get("iconKey")
        for name, index, want_icon, want_title in MODES:
            select("settings.cmbTitleTabDisplay", index)
            cases["tab." + name] = judge_mode(find("TabIcon"), find("shell.tabTitle." + doc["id"]), want_icon, want_title)
            s.capture("tab_" + name)
        # 지금 title 방식이다
        cases["busy.title_inline"] = loading_case("spinner")
        select("settings.cmbTitleTabDisplay", 0)
        s.call("settings.set", **{"theme.motion.level": "reduced"})
        cases["busy.reduced_static"] = loading_case("static")
        s.call("settings.set", **{"theme.motion.level": level})
        cases["busy.accessible_name"] = {"verdict": "RECORDED", "why": "접근성 이름은 원격으로 읽을 수단이 없어 육안·접근성 도구 기록 몫"}

        for place in ("title_tab", "mdi_header", "explorer"):
            cases["place." + place] = judge_place(find(*PLACES[place]), key)
        s.call("docs.arrange", mode="tabbed")
        time.sleep(2)
        # DOCK 머리의 HeaderBusy 는 같은 문서의 키만 읽는 자리(평소 숨김)
        cases["place.dock_tab"] = judge_place(find(*PLACES["dock_tab"]), key, need_visible=False)
        s.capture("place_dock")
        s.call("docs.sdi", doc=doc["id"], on=True)
        time.sleep(3)
        cases["place.sdi_title"] = judge_place(find(*PLACES["sdi_title"]), key)
        s.capture("place_sdi")
        s.call("docs.sdi", doc=doc["id"], on=False)
        time.sleep(2)
        cases["place.nav"] = nav_case(s, find, key)
    finally:
        s.call("settings.set", **{"theme.motion.level": level})
        s.call("docs.close", all=True)
    return cases


def self_test():
    def node(x=4, shown=True, **props):
        return {"visible": shown, "bounds": {"x": x, "w": 14}, "props": props}

    icon, title = node(4), node(24)
    busy = node(DisplayState="spinner", IsBusyShown="True")
    return session.run_cases([
        ("icon_title PASS", judge_mode(icon, title, True, True)["verdict"] == "PASS"),
        ("icon PASS", judge_mode(icon, node(24, False), True, False)["verdict"] == "PASS"),
        ("title PASS", judge_mode(node(4, False), title, False, True)["verdict"] == "PASS"),
        ("title 인데 아이콘 FAIL", judge_mode(icon, title, False, True)["verdict"] == "FAIL"),
        ("아이콘이 뒤 FAIL", judge_mode(node(90), title, True, True)["verdict"] == "FAIL"),
        ("못 찾음 INVALID", judge_mode(None, title, True, True)["verdict"] == "INVALID"),
        ("키 PASS", judge_place(node(EffectiveIconKey="PdMenuIcon1"), "PdMenuIcon1")["verdict"] == "PASS"),
        ("키 다름 FAIL", judge_place(node(EffectiveIconKey="X"), "PdMenuIcon1")["verdict"] == "FAIL"),
        ("숨음 FAIL", judge_place(node(shown=False, EffectiveIconKey="K"), "K")["verdict"] == "FAIL"),
        ("식별자 없음 INVALID", judge_place(None, "K")["verdict"] == "INVALID"),
        ("스피너 PASS", judge_busy(busy, True, "spinner")["verdict"] == "PASS"),
        ("정지여야 FAIL", judge_busy(busy, True, "static")["verdict"] == "FAIL"),
        ("inline 숨음 FAIL", judge_busy(node(shown=False, DisplayState="spinner", IsBusyShown="True"), True, "spinner")["verdict"] == "FAIL"),
        ("트리 정지", shown_state([{"name": "TabIcon", "children": [{"type": "IconSpinner", "isVisible": False}]}]) == "static"),
        ("트리 없음", shown_state([{"name": "x", "children": []}]) is None),
        ("로딩 없음 INVALID", judge_busy(busy, False, "spinner")["verdict"] == "INVALID"),
        ("자리 6곳(NAV 포함)", len(PLACES) == 6 and "nav" in PLACES),
        ("places_pass 셈", places_pass({"place.a": {"verdict": "PASS"}, "place.b": {"verdict": "INVALID"},
                                        "tab.x": {"verdict": "PASS"}}) == 1),
        ("NAV 모바일형 불허 INVALID", nav_case(_FakeStyle(["Pc", "Tablet"]), None, "K")["verdict"] == "INVALID"),
    ])


class _FakeStyle:
    """자기시험용 — ui.style 만 답한다(모바일형 불허 경로)."""

    def __init__(self, allowed):
        self.allowed = allowed

    def call(self, cmd, **kw):
        return {"device": "Pc", "allowed": self.allowed, "setting": "Auto", "effective": "Pc"}


def main():
    ap = session.parser(__doc__)
    ap.set_defaults(remote_only=True)  # 원격 명령·앱 렌더 캡처만 쓴다(실입력 없음) — PC 비움 검사 생략
    args = ap.parse_args()
    if args.self_test:
        return self_test()
    rc = session.run(args, "title_tab_icons_judge", judge)
    if args.out:
        add_places_pass(args.out)
    return rc


if __name__ == "__main__":
    sys.exit(main())
