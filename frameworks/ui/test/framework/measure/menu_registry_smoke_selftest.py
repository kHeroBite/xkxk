# B07 메뉴 레지스트리 스모크의 요약 계산부 자체 시험(앱 없음) — menu_registry_smoke.py --self-test 가 부른다
import json

from menu_registry_smoke import MOVED, OWNER_SHELL, OWNER_TB, SMOKE_AID, SMOKE_ICON, diff_registry, summarize


def self_test():
    base = {"shell.mnuOpenGrid", "shell.mnuExit", "shell.mnuWinList1"}
    nav = ["A", "B"]
    good = {
        "registry_dump": {"ok": True, "result": {"menus": [{"id": "m%d" % i, "owner": "xkxk.shell"} for i in range(70)] + [{"id": i, "owner": OWNER_TB} for i in MOVED],
                                                  "commands": [{"owner": "xkxk.shell"}] * 58 + [{"owner": "xkxk.frameworks.ui.testbed"}] * 3, "icons": {"explicit": []}}},
        "menu_list": [{"root": "shell.mnuFile", "response": {"ok": True, "result": {"id": "shell.mnuFile", "count": 2,
                                                                                     "items": [{"id": "shell.mnuOpenGrid"}, {"id": "shell.mnuExit"}]}}}],
        "menu_add": {"ok": True, "result": {"ok": True}}, "menu_remove": {"ok": True, "result": {"ok": True}},
        "icon_add": {"ok": True, "result": {"ok": True}}, "icon_remove": {"ok": True, "result": {"ok": True}},
        "menu_list_after_add": {"ok": True, "result": {"id": "shell.mnuFile", "items": [{"id": SMOKE_AID}]}},
        "menu_list_after_remove": {"ok": True, "result": {"id": "shell.mnuFile", "items": []}},
        "menu_icons_after_icon_add": {"ok": True, "result": {"menus": [{"items": [{"id": SMOKE_AID, "iconKey": SMOKE_ICON, "geometryFound": True}]}]}},
        "menu_icons_after_icon_remove": {"ok": True, "result": {"menus": [{"items": [{"id": SMOKE_AID, "iconKey": SMOKE_ICON, "geometryFound": False}]}]}},
        "command_run": {"ok": True, "result": {"id": "xkxk.view.showExplorer", "executed": True}},
        "nav_menu": {"ok": True, "result": {"tabs": ["A"], "drawer": [{"id": "A"}, {"id": "B"}]}},
    }
    cases = []
    c, miss, allowed = summarize(good, base, nav, "web")
    cases.append(("정상 PASS", all(x["pass"] for x in c) and miss == ["shell.mnuWinList1"] and allowed[0]["reason"]))
    bad = json.loads(json.dumps(good))
    bad["menu_list"][0]["response"]["result"]["items"] = [{"id": "shell.mnuExit"}]
    c, _, _ = summarize(bad, base, nav, "web")
    cases.append(("조건부 아닌 ID 누락 FAIL", not all(x["pass"] for x in c)))
    # 루트 AutomationId: ok 응답 + items 있음이면 찾은 것으로 센다(odev-12 실측), 실패 응답·요청 묶음 키만으로는 세지 않는다
    c, miss, _ = summarize(good, base | {"shell.mnuFile"}, nav, "web")
    cases.append(("루트 ok+items 면 셈 PASS", "shell.mnuFile" not in miss and all(x["pass"] for x in c)))
    echo = json.loads(json.dumps(good))
    echo["menu_list"] = [{"id": "shell.mnuFile", "root": "shell.mnuFile", "response": {"ok": False, "error": "메뉴를 찾지 못했습니다"}},
                         {"root": "shell.mnuView", "response": {"ok": True, "result": {"id": "shell.mnuView", "items":
                                                                good["menu_list"][0]["response"]["result"]["items"]}}}]
    c, miss, _ = summarize(echo, base | {"shell.mnuFile"}, nav, "web")
    cases.append(("루트 실패 응답·요청 키만이면 누락 FAIL", "shell.mnuFile" in miss and not all(x["pass"] for x in c)))
    ei = json.loads(json.dumps(good))
    ei["menu_list"][0]["response"]["result"]["items"] = []
    c, miss, _ = summarize(ei, {"shell.mnuFile"}, nav, "web")
    cases.append(("루트 items 비면 누락 FAIL", "shell.mnuFile" in miss and not all(x["pass"] for x in c)))
    ae = json.loads(json.dumps(good))
    ae["menu_list_after_add"] = {"ok": True, "result": {"id": SMOKE_AID, "items": []}}
    c, _, _ = summarize(ae, base, nav, "web")
    cases.append(("추가 확인도 items 만 셈 FAIL", not all(x["pass"] for x in c)))
    mig = json.loads(json.dumps(good))  # 셸 61개 = 시험대 이관 미반영 → 판정이 FAIL 이어야 한다
    mig["registry_dump"]["result"]["commands"] = [{"owner": "xkxk.shell"}] * 61
    c, _, _ = summarize(mig, base, nav, "web")
    cases.append(("셸 61개(이관 미반영) FAIL", not all(x["pass"] for x in c)))
    for key, mut in [("menu_list_after_remove", {"ok": True, "result": {"id": "shell.mnuFile", "items": [{"id": SMOKE_AID}]}}),
                     ("menu_icons_after_icon_add", {"ok": True, "result": {"menus": [{"items": [{"id": SMOKE_AID, "iconKey": SMOKE_ICON, "geometryFound": False}]}]}}),
                     ("menu_icons_after_icon_remove", {"ok": True, "result": {"menus": [{"items": [{"id": SMOKE_AID, "iconKey": SMOKE_ICON, "geometryFound": True}]}]}}),
                     ("command_run", {"ok": True, "result": {"id": "xkxk.view.showExplorer", "executed": False}}),
                     ("nav_menu", {"ok": True, "result": {"tabs": ["A"], "drawer": [{"id": "B"}, {"id": "A"}]}}),
                     ("registry_dump", {"ok": False, "error": "x"})]:
        b = dict(good, **{key: mut})
        c, _, _ = summarize(b, base, nav, "web")
        cases.append(("%s 이상 FAIL" % key, not all(x["pass"] for x in c)))
    b = dict(good, nav_menu={"ok": False})
    c, _, _ = summarize(b, base, nav, "desktop")
    cases.append(("데스크톱은 nav 순서 판정 제외", all(x["pass"] for x in c)))
    # 이전 8행 owner 기대값(셸→시험대)
    mv = json.loads(json.dumps(good))
    mv["registry_dump"]["result"]["menus"][-1]["owner"] = OWNER_SHELL
    c, _, _ = summarize(mv, base, nav, "web")
    cases.append(("이전 행 owner 가 셸에 남으면 FAIL", not all(x["pass"] for x in c)))
    nb = json.loads(json.dumps(good))
    nb["registry_dump"]["result"]["menus"] = [m for m in nb["registry_dump"]["result"]["menus"] if m["id"] != MOVED[-1]]
    c, _, _ = summarize(nb, base, nav, "web")
    cases.append(("벤치 미포함 빌드(7행) PASS", all(x["pass"] for x in c)))
    # 이전 전후 비교 — owner 이전만 허용
    def pair():
        bf = json.loads(json.dumps(good))
        for m in bf["registry_dump"]["result"]["menus"]:
            m.update(parent=None, kind="Command", automationId=m["id"], visible=True, hidden=False, enabled=True, section=None)
            if m["id"] in MOVED:
                m["owner"] = OWNER_SHELL
        bf["nav_menu"]["result"]["drawer"] = [{"id": "A", "group": False, "enabled": True, "reason": None, "desktopIds": []},
                                              {"id": "OpenGrid", "group": False, "enabled": False, "reason": "R", "desktopIds": ["x"]}]
        bf["menu_list"] = [{"root": "r", "response": {"ok": True, "result": {"id": "r", "items": [{"id": "i1", "parent": "r", "enabled": True, "visible": True, "depth": 1, "hasChildren": False}]}}}]
        return bf, json.loads(json.dumps(bf))
    def to_tb(af):
        for m in af["registry_dump"]["result"]["menus"]:
            if m["id"] in MOVED:
                m["owner"] = OWNER_TB
        return af
    bf, af = pair()
    cases.append(("diff: owner 이전만이면 PASS", diff_registry(bf, to_tb(af)) == []))
    bf, af = pair()
    to_tb(af)
    af["registry_dump"]["result"]["menus"][0]["owner"] = OWNER_TB
    cases.append(("diff: 다른 행 owner 변경 FAIL", bool(diff_registry(bf, af))))
    for label, mut in [("parent", lambda a: a["registry_dump"]["result"]["menus"][3].update(parent="x")),
                       ("automationId", lambda a: a["registry_dump"]["result"]["menus"][3].update(automationId="y")),
                       ("순서", lambda a: a["registry_dump"]["result"]["menus"].reverse()),
                       ("visible", lambda a: a["registry_dump"]["result"]["menus"][3].update(visible=False)),
                       ("drawer Available", lambda a: a["nav_menu"]["result"]["drawer"][1].update(enabled=True)),
                       ("drawer ReasonKey", lambda a: a["nav_menu"]["result"]["drawer"][1].update(reason="Z")),
                       ("drawer InDrawer", lambda a: a["nav_menu"]["result"]["drawer"].pop()),
                       ("menu.list 가시", lambda a: a["menu_list"][0]["response"]["result"]["items"][0].update(visible=False))]:
        bf, af = pair()
        to_tb(af)
        mut(af)
        cases.append(("diff: %s 변경 FAIL" % label, bool(diff_registry(bf, af))))
    bf, af = pair()
    to_tb(af)
    af["registry_dump"]["result"]["menus"] = [m for m in af["registry_dump"]["result"]["menus"] if m["id"] != MOVED[-1]]
    cases.append(("diff: 벤치 행 빠짐은 --allow-missing 없으면 FAIL, 있으면 PASS",
                  bool(diff_registry(bf, af)) and diff_registry(bf, af, [MOVED[-1]]) == []))
    cases.append(("diff: 빈 덤프는 판정불가 FAIL", bool(diff_registry({}, {}))))
    for name, ok in cases:
        print("  %s %s" % ("✓" if ok else "✗", name))
    passed = sum(1 for _, ok in cases if ok)
    print("self-test %d/%d" % (passed, len(cases)))
    return 0 if passed == len(cases) else 1
