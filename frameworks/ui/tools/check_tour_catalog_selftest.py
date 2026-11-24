# 도움말 카탈로그 검사기 자체 시험 — 실제 파일 내용에 결함을 넣어 각 규칙이 잡는지 본다. 저장소 파일은 건드리지 않는다
import json

from check_tour_catalog_rules import CATALOG, Source, check

MENU = "src/Pd.Ui.Views/Menus/FileMenu.axaml"
TITLE = "src/Pd.Ui.Views/MainTitleBar.axaml"
MAIN = "src/Pd.Ui.Views/MainView.axaml"
EN = "src/Pd.Poc.Shell/Localization/sysview.en.json"
WIN_MENU = "src/Pd.Ui.Views/WindowTitleMenu.cs"
NEW_ITEM = '<MenuItem Header="x" AutomationProperties.AutomationId="shell.mnuBrandNew" />\n</MenuItem>'


def catalog_edit(edit):
    def apply(src):
        data = json.loads(src.read(CATALOG))
        edit(data)
        return {CATALOG: json.dumps(data, ensure_ascii=False)}
    return apply


def text_edit(rel, fn):
    return lambda src: {rel: fn(src.read(rel))}


def replace_last(text, old, new):
    head, sep, tail = text.rpartition(old)
    return head + new + tail if sep else text


def first_step(data):
    return data["steps"][0]


def drop_en_key(text):
    data = json.loads(text)
    del data["sysview.tour.main.menu.title"]
    return json.dumps(data, ensure_ascii=False)


def set_field(item_of, field, value):
    return catalog_edit(lambda d: item_of(d).__setitem__(field, value))


# (이름, 변형, 문제 목록에 있어야 할 문구)
CASES = (
    ("없는 대상", set_field(first_step, "target", "shell.gone"), "② 단계 main.menu 의 대상 없음: shell.gone"),
    ("없는 앵커", set_field(first_step, "target", "@nothing"), "② 단계 main.menu 의 대상 없음: @nothing"),
    ("대상 형식", set_field(first_step, "target", "no spaces"), "⑤ 단계 main.menu: target 형식 오류"),
    ("없는 사전 키", set_field(first_step, "text", "missing.key"), "① ko 사전에 키 없음: sysview.tour.missing.key.title"),
    ("en 사전 누락", text_edit(EN, drop_en_key), "① en 사전에 키 없음: sysview.tour.main.menu.title"),
    ("JSON 문법", lambda src: {CATALOG: src.read(CATALOG)[:-5]}, "⑤ 카탈로그 JSON 문법 오류"),
    ("모르는 필드", set_field(first_step, "taget", "x"), "⑤ step main.menu: 모르는 필드 taget"),
    ("필수 필드", catalog_edit(lambda d: first_step(d).pop("scope")), "⑤ step main.menu: 필수 필드 scope 없음"),
    ("scope 값", set_field(first_step, "scope", "Moon"), "⑤ 단계 main.menu: scope 값 'Moon' 없음"),
    ("modes 값", set_field(first_step, "modes", "Mdi|Moon"), "⑤ 단계 main.menu: modes 값 'Moon' 없음"),
    ("조건 이름", set_field(first_step, "requires", "noSuch"), "⑤ 단계 main.menu: 조건 'noSuch' 이 TourConditions 에 없음"),
    ("단계 중복", catalog_edit(lambda d: d["steps"].append(dict(first_step(d)))), "④ 단계 id 중복: main.menu"),
    ("texts 금지", catalog_edit(lambda d: d.__setitem__("texts", {})), "⑤ 저장소 카탈로그에 texts 금지"),
    ("NAV 없음", catalog_edit(lambda d: d.__setitem__("steps", [s for s in d["steps"] if not s["id"].startswith("nav.")])), "④ NAV 단계가 없음"),
    ("새 메뉴", text_edit(MENU, lambda t: replace_last(t, "</MenuItem>", NEW_ITEM)), "⑥ 카탈로그에 설명이 없는 새 항목: shell.mnuBrandNew"),
    ("새 버튼", text_edit(TITLE, lambda t: t.replace("</UserControl>", '<Button AutomationProperties.AutomationId="shell.btnBrandNew" /></UserControl>')),
     "⑥ 카탈로그에 설명이 없는 새 항목: shell.btnBrandNew"),
    ("새 창 메뉴", text_edit(WIN_MENU, lambda t: t.replace('"Restore"),', '"Restore"),\n (WindowMenuAction.Pack, "chrome.winmenu.pack", "Pack"),')),
     "⑥ 카탈로그에 설명이 없는 새 항목: winmenu.Pack"),
    ("없는 항목 제외", catalog_edit(lambda d: d["coverage"]["exclude"].append({"id": "shell.btnGone", "reason": "x"})), "⑥ 제외 목록이 없는 항목을 가리킴: shell.btnGone"),
    ("사유 없는 제외", catalog_edit(lambda d: d["coverage"]["exclude"].append({"id": "shell.btnTheme"})), "⑤ 커버리지 제외 항목에 id·reason 필요"),
    ("없는 covers", set_field(first_step, "covers", ["shell.btnGone"]), "⑥ covers 가 없는 항목을 가리킴: shell.btnGone"),
    ("직접 KeyBinding", text_edit(MAIN, lambda t: t.replace("<DockPanel>", '<UserControl.KeyBindings><KeyBinding Gesture="Ctrl+Q" /></UserControl.KeyBindings><DockPanel>', 1)),
     "③ MainView.axaml 에 KeyBinding 직접 작성 금지"),
    ("코드 키 누락", text_edit(MAIN, lambda t: t.replace("<DockPanel>", '<KeyBinding Gesture="Ctrl+Q" /><DockPanel>', 1)), "③ MainView.axaml 의 실제 키가 카탈로그에 없음: Ctrl+Q"),
    ("없는 binding", catalog_edit(lambda d: next(s for s in d["shortcuts"] if s["id"] == "next").__setitem__("binding", "Nope")), "③ 단축키 next: binding 'Nope' 이 ShortcutBindings 에 없음"),
    ("없는 처리 위치", catalog_edit(lambda d: next(s for s in d["shortcuts"] if s["id"] == "help").__setitem__("handler", "src/x.cs:Key.F1")), "③ 단축키 help: 처리 위치"),
    ("처리 둘 다", catalog_edit(lambda d: next(s for s in d["shortcuts"] if s["id"] == "help").__setitem__("binding", "Next")), "⑤ 단축키 help: binding·handler 중 하나만"),
)


def self_test(root):
    cache = {}
    failed = 0
    for name, mutate, expected in CASES:
        src = Source(root, cache=cache)
        problems, _ = check(Source(root, mutate(src), cache))
        ok = any(expected in p for p in problems)
        failed += 0 if ok else 1
        print("%s %s" % ("PASS" if ok else "FAIL", name))
        if not ok:
            print("  기대: %s\n  실제: %s" % (expected, problems[:6]))
    print("자체 시험 %d건 · 실패 %d" % (len(CASES), failed))
    return 1 if failed else 0
