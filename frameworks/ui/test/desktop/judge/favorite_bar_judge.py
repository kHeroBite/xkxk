# AC-016·017·033 판정기 — 제목 줄 즐겨찾기 영역의 자리·순서·누름·빼기·끌어 순서 바꾸기·넘침·재기동 유지를 판정한다
"""
사용
  favorite_bar_judge.py --info <격리>/remote-control.json --away-state <pc_away.json> --approved '<누가·언제>' --out <결과.json>
  favorite_bar_judge.py --bundle <wwwroot> --port <포트> --out <결과.json>      (웹 결과)
  favorite_bar_judge.py --self-test

누름은 favorite.click(명령 경로)이 아니라 ui.pointerClick(버튼 Click 경로)으로 잰다. 앱 안 포인터이며 OS 실입력은 아니다.
재기동 유지는 웹만 다시 읽기로 잰다. 데스크톱은 판정기가 앱을 띄우지 않으므로 DEFERRED 다.
즐겨찾기가 비어 있는 깨끗한 격리 인스턴스에서만 돈다.
"""
import glob
import os
import re
import sys
import time

import judge_session as session

MENU = os.path.join(session.HERE, "..", "..", "..", "..", "..", "plan", "09_*", "메뉴구조.csv")
SCREENS = {"grid": None, "gantt": None, "form": None}  # 여는 종류 → 그 문서의 화면 ID(실행 때 읽는다)
EDGE = 60  # 영역 오른쪽 끝과 창 오른쪽 끝 사이에 남아도 되는 폭(DIP)
TAB_START = 60  # 첫 탭이 시작해도 되는 왼쪽 한계(DIP)


def verdict(problems, why, **data):
    return dict(data, verdict="FAIL" if problems else "PASS", why=why, problems=problems)


def judge_area(xs, bar, width, tabs):
    """xs = 넣은 순서대로 아이콘 x, tabs = 연 순서대로 탭 x."""
    if len(xs) < 2 or len(tabs) < 2:
        return {"verdict": "INVALID", "why": "순서를 잴 만큼 아이콘·탭이 없다"}
    bad = {}
    if any(b >= a for a, b in zip(xs, xs[1:])):
        bad["즐겨찾기 오른쪽→왼쪽"] = xs
    if width - (bar["x"] + bar["w"]) > EDGE:
        bad["영역 오른쪽 끝"] = [bar, width]
    if any(b <= a for a, b in zip(tabs, tabs[1:])) or tabs[0] > TAB_START:
        bad["탭 왼쪽부터"] = tabs
    if tabs[-1] >= min(xs):
        bad["탭과 겹침"] = [tabs[-1], min(xs)]
    return verdict(bad, "오른쪽 끝·넣은 순서대로 오른쪽→왼쪽·탭은 왼쪽부터", icons=xs, tabs=tabs)


def judge_icon_only(texts):
    return verdict({"글자": texts} if texts else {}, "영역 안에 메뉴 이름 글자 0")


def judge_click(before, after, existed, target):
    want = before["openDocs"] + (0 if existed else 1)
    bad = {}
    if after["openDocs"] != want:
        bad["열린 창 수"] = [after["openDocs"], want]
    if existed and after.get("active") != target:
        bad["활성"] = [after.get("active"), target]
    if existed and before.get("active") == target:
        return {"verdict": "INVALID", "why": "이미 활성이라 전환을 잴 수 없다"}
    if not existed and after.get("active") == before.get("active"):
        bad["새 창 활성"] = after.get("active")
    return verdict(bad, "있으면 활성·수 그대로" if existed else "없으면 +1", before=before, after=after)


def judge_remove(before, after, screen):
    want = [i for i in before["items"] if i != screen]
    return verdict({} if after["items"] == want else {"items": [after["items"], want]}, "빼기 뒤 그 1건만 줄어듦")


def judge_reorder(before, after):
    if len(before["items"]) < 2:
        return {"verdict": "INVALID", "why": "2건 미만"}
    bad = {}
    if sorted(after["items"]) != sorted(before["items"]):
        bad["구성 변함"] = [after["items"], before["items"]]
    elif after["items"] == before["items"]:
        bad["순서 그대로"] = after["items"]
    return verdict(bad, "끌기 뒤 구성은 같고 순서만 바뀜", before=before["items"], after=after["items"])


def judge_overflow(state, button_visible):
    if not state.get("overflow"):
        return {"verdict": "INVALID", "why": "넘치게 만들지 못했다", "count": len(state.get("items") or [])}
    bad = {}
    if not button_visible:
        bad["» 단추"] = "안 보임"
    if sorted(state["visible"] + state["overflow"]) != sorted(state["items"]):
        bad["빠진 항목"] = state
    return verdict(bad, "넘친 것은 » 로 모이고 빠진 항목 0")


def catalog():
    with open(glob.glob(MENU)[0], encoding="utf-8-sig") as f:
        found = re.findall(r"^\d{4},(SCR-[A-Z]+-\d+),", f.read(), re.M)
    return list(dict.fromkeys(found))


def texts_under(node):
    own = [node["text"]] if node.get("visible") and node.get("text") and node.get("type") == "TextBlock" else []
    return own + [t for child in node.get("children", []) for t in texts_under(child)]


def node_by_id(nodes, key):
    for node in nodes:
        if node.get("id") == key:
            return node
        hit = node_by_id(node.get("children", []), key)
        if hit:
            return hit
    return None


def judge(s):
    start = s.call("favorite.state")
    if start["items"] or start["openDocs"]:
        raise session.Abort("INVALID", "즐겨찾기·문서가 남은 인스턴스다(깨끗한 격리 폴더로 다시)")
    added, cases = [], {}

    def state():
        time.sleep(1.2)
        return s.call("favorite.state")

    def toggle(screen):
        s.call("favorite.toggle", screen=screen)
        (added.remove if screen in added else added.append)(screen)

    def box(key):
        return s.call("ui.find", id=key)["bounds"]

    try:
        docs = []
        for kind in SCREENS:
            s.call("docs.open", kind=kind)
            time.sleep(2.5)
            new = [d for d in s.call("window.load.state")["docs"] if d["id"] not in [x["id"] for x in docs]]
            if len(new) != 1 or not new[0].get("screenId"):
                raise session.Abort("INVALID", "%s 문서의 화면 ID 를 읽지 못했다" % kind)
            docs.append(new[0])
        for d in docs:
            toggle(d["screenId"])
            time.sleep(0.8)
        full = state()
        icons = [box("shell.favorite." + d["screenId"])["x"] for d in docs]
        tree = s.call("tree.get", named=True, depth=200)["tree"]
        bar = node_by_id(tree, "shell.favoriteBar")
        if not bar or not tree:
            raise session.Abort("INVALID", "shell.favoriteBar 를 찾지 못했다")
        cases["area"] = judge_area(icons, bar["bounds"], tree[0]["bounds"]["w"], [box("shell.tab." + d["id"])["x"] for d in docs])
        cases["icon_only"] = judge_icon_only(texts_under(bar))
        s.capture("favorite_area")

        first = docs[0]
        before = state()
        s.call("ui.pointerClick", id="shell.favorite." + first["screenId"])
        cases["click.existing"] = judge_click(before, state(), True, first["id"])
        s.call("docs.close", doc=first["id"])
        before = state()
        s.call("ui.pointerClick", id="shell.favorite." + first["screenId"])
        cases["click.missing"] = judge_click(before, state(), False, None)

        before, dx = state(), icons[1] - icons[0]
        s.call("ui.pointerDrag", id="shell.favorite." + first["screenId"], dx=dx, dy=0, steps=12)
        moved = state()
        cases["reorder"] = judge_reorder(before, moved)

        if s.scope == "web":
            s.web.page.reload(wait_until="load", timeout=90000)
            s.web.page.wait_for_function("() => window.pdAutomation && window.pdAutomation.invoke", timeout=120000)
            time.sleep(4)
            again = s.call("favorite.state")["items"]
            cases["restart"] = verdict({} if again == moved["items"] else {"items": [again, moved["items"]]}, "다시 읽기 뒤 구성·순서 유지")
        else:
            cases["restart"] = {"verdict": "DEFERRED", "why": "판정기는 앱을 다시 띄우지 않는다", "expected": moved["items"]}

        last = docs[-1]["screenId"]
        before = state()
        toggle(last)
        cases["remove"] = judge_remove(before, state(), last)

        for screen in catalog():
            if screen in full["items"]:
                continue
            toggle(screen)
            now = s.call("favorite.state")
            if now.get("overflow") or len(now["items"]) >= 60:
                break
        now = state()
        visible = bool(now.get("overflow")) and bool(s.call("ui.find", id="shell.favoriteOverflow").get("visible"))
        s.capture("favorite_overflow")
        cases["overflow"] = judge_overflow(now, visible)
    finally:
        for screen in list(added):
            toggle(screen)
        s.call("docs.close", all=True)
    return cases


def self_test():
    bar = {"x": 1100, "y": 40, "w": 170, "h": 24}
    two = {"items": ["A", "B"], "visible": ["A"], "overflow": ["B"], "openDocs": 2, "active": "d2"}
    return session.run_cases([
        ("자리 PASS", judge_area([1240, 1210, 1180], bar, 1280, [4, 120, 240])["verdict"] == "PASS"),
        ("거꾸로 FAIL", judge_area([1180, 1210], bar, 1280, [4, 120])["verdict"] == "FAIL"),
        ("끝 아님 FAIL", judge_area([700, 670], dict(bar, x=500), 1280, [4, 120])["verdict"] == "FAIL"),
        ("탭 오른쪽부터 FAIL", judge_area([1240, 1210], bar, 1280, [300, 120])["verdict"] == "FAIL"),
        ("1개 INVALID", judge_area([1240], bar, 1280, [4])["verdict"] == "INVALID"),
        ("글자 FAIL", judge_icon_only(["그리드"])["verdict"] == "FAIL"),
        ("아이콘만 PASS", judge_icon_only([])["verdict"] == "PASS"),
        ("있음 PASS", judge_click(two, dict(two, active="d1"), True, "d1")["verdict"] == "PASS"),
        ("있는데 +1 FAIL", judge_click(two, dict(two, openDocs=3, active="d1"), True, "d1")["verdict"] == "FAIL"),
        ("이미 활성 INVALID", judge_click(two, two, True, "d2")["verdict"] == "INVALID"),
        ("없음 +1 PASS", judge_click(two, dict(two, openDocs=3, active="d3"), False, None)["verdict"] == "PASS"),
        ("안 열림 FAIL", judge_click(two, two, False, None)["verdict"] == "FAIL"),
        ("빼기 PASS", judge_remove(two, dict(two, items=["A"]), "B")["verdict"] == "PASS"),
        ("안 빠짐 FAIL", judge_remove(two, two, "B")["verdict"] == "FAIL"),
        ("순서 PASS", judge_reorder(two, dict(two, items=["B", "A"]))["verdict"] == "PASS"),
        ("순서 그대로 FAIL", judge_reorder(two, two)["verdict"] == "FAIL"),
        ("넘침 PASS", judge_overflow(two, True)["verdict"] == "PASS"),
        ("» 없음 FAIL", judge_overflow(two, False)["verdict"] == "FAIL"),
        ("안 넘침 INVALID", judge_overflow(dict(two, overflow=[]), False)["verdict"] == "INVALID"),
        ("메뉴표 읽기", len(catalog()) > 20),
    ])


def main():
    args = session.parser(__doc__).parse_args()
    # 웹은 새 프로필에 무해한 값을 심어 ?fresh 없이 띄운다(다시 읽기 뒤 저장 유지 판정)
    return self_test() if args.self_test else session.run(args, "favorite_bar_judge", judge, {"MenuIconVisible": True})


if __name__ == "__main__":
    sys.exit(main())
