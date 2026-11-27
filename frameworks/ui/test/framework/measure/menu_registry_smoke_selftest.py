# B07 메뉴 레지스트리 스모크의 요약 계산부 자체 시험(앱 없음) — menu_registry_smoke.py --self-test 가 부른다
import json

from menu_registry_smoke import SMOKE_AID, SMOKE_ICON, summarize


def self_test():
    base = {"shell.mnuOpenGrid", "shell.mnuExit", "shell.mnuWinList1"}
    nav = ["A", "B"]
    good = {
        "registry_dump": {"ok": True, "result": {"menus": [{"id": "m%d" % i, "owner": "xkxk.shell"} for i in range(70)],
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
    for name, ok in cases:
        print("  %s %s" % ("✓" if ok else "✗", name))
    passed = sum(1 for _, ok in cases if ok)
    print("self-test %d/%d" % (passed, len(cases)))
    return 0 if passed == len(cases) else 1
