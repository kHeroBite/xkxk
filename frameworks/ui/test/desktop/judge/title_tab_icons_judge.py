# AC-015·020 판정기 — 제목 탭 3방식, 표시 자리별 아이콘 키, title 방식·정지 조건의 불러오는 중 대체 표시를 판정한다
"""
사용
  title_tab_icons_judge.py --info <격리>/remote-control.json --away-state <pc_away.json> --approved '<누가·언제>' --out <결과.json>
  title_tab_icons_judge.py --bundle <wwwroot> --port <포트> --out <결과.json>      (웹 결과)
  title_tab_icons_judge.py --self-test

방식 전환은 설정 화면 콤보(settings.cmbTitleTabDisplay)를 거친다. 아이콘 키는 WindowIcon 의 EffectiveIconKey, 표시는 DisplayState 를 읽는다.
자리를 찾을 식별자가 없으면 그 자리는 INVALID 다(보였다고 치지 않는다). NAV 자리는 모바일형 판정기 몫이라 DEFERRED 다.
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
        cases["place.nav"] = {"verdict": "DEFERRED", "why": "NAV 는 모바일형 판정기 몫"}
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
    ])


def main():
    args = session.parser(__doc__).parse_args()
    return self_test() if args.self_test else session.run(args, "title_tab_icons_judge", judge)


if __name__ == "__main__":
    sys.exit(main())
