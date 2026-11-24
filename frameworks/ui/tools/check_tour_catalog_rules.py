# 도움말 카탈로그 검사 규칙 — 파일 읽기(시험용 덮어쓰기 포함)와 ①~⑥ 규칙 본체. check_tour_catalog.py 가 부른다
import fnmatch
import glob
import json
import os
import re

from check_tour_catalog_format import CATALOG, check_format, parse_catalog

BINDINGS = "src/Pd.Ui.Views/Input/ShortcutBindings.cs"
NAV_MENU = "src/Pd.Client.Data/Navigation/NavMenuCatalog.cs"
WINDOW_MENU = "src/Pd.Ui.Views/WindowTitleMenu.cs"
PANEL_MENU = "src/Pd.Ui.Views/PanelPositionMenu.cs"

UI_KEY = re.compile(r'UiKey \+ "([^"]+)"')
LOC_KEY = re.compile(r'Loc\.Tr\("(sysview\.[^"]+)"\)')
AUTOMATION_AXAML = re.compile(r'AutomationId"?\s*=\s*"([^"{]+)"')
AUTOMATION_SETTER = re.compile(r'\(AutomationProperties\.AutomationId\)" Value="([^"{]+)"')
GESTURE_AXAML = re.compile(r'(?:Gesture|InputGesture)="([^"]+)"')
WKG_TUPLE = re.compile(r'\(((?:Key\.\w+)(?: or Key\.\w+)*),\s*((?:KeyModifiers\.\w+)(?: \| KeyModifiers\.\w+)*)\)')
XML_COMMENT = re.compile(r"<!--.*?-->", re.S)
KEY_NAMES = {"OemMinus": "-", "Subtract": "-", "OemTilde": "`", "Escape": "Esc", "D0": "0", "NumPad0": "0"}
MOD_ORDER = (("Control", "Ctrl"), ("Meta", "Cmd"), ("Alt", "Alt"), ("Shift", "Shift"))
# 제스처 처리가 코드에 흩어진 곳의 고정 표식: (파일, 코드에 있어야 할 표식, 카탈로그에 있어야 할 키)
FIXED_MARKERS = (
    ("src/Pd.Ui.Views/EscCloseInput.cs", "Key.Escape", "Esc"),
    ("src/Pd.Ui.Views/Input/TabFocusScope.cs", "Key.Tab", "Tab"),
    ("src/Pd.Ui.Views/Input/TabFocusScope.cs", "KeyModifiers.Shift", "Shift+Tab"),
    ("src/Pd.Ui.Views/MainTitleBar.axaml.cs", "Key.F10", "F10"),
    ("src/Pd.Poc.Shell/Input/SettingsInput.cs", "Key.D0", "Ctrl+0"),
    ("src/Pd.Poc.Shell/Tour/TourInputGate.cs", "Key.F1", "F1"),
)
# 대상을 찾는 곳. 고정 파일에 없는 대상이 있으면 폴더 전체를 읽으므로 새 화면 axaml 도 자동으로 포함된다
TARGET_FILES = (
    "src/Pd.Ui.Views/MainView.axaml", "src/Pd.Ui.Views/MainTitleBar.axaml", "src/Pd.Ui.Views/ShellStatusBar.axaml",
    "src/Pd.Ui.Views/MobileNavView.axaml", "src/Pd.Ui.Views/SdiTitleBar.axaml", "src/Pd.Ui.Views/ChromeButtons.axaml",
    "src/Pd.Ui.Views/Settings/SettingsView.axaml", "src/Pd.Ui/Controls/SettingsShell.axaml",
)
TARGET_GLOBS = ("src/Pd.Ui.Views/*.axaml", "src/Pd.Ui.Views/*/*.axaml", "src/Pd.Ui/Controls/*.axaml")
GESTURE_AXAMLS = ("src/Pd.Ui.Views/MainView.axaml", "src/Pd.Ui.Views/MainTitleBar.axaml", "src/Pd.Ui.Views/Menus/*.axaml")
TOUR_UI_CS = ("src/Pd.Ui/Controls/TourCard.axaml.cs", "src/Pd.Ui/Controls/ShortcutLayer.axaml.cs", "src/Pd.Ui/Controls/TourOverlay.axaml.cs",
              "src/Pd.Poc.Shell/Tour/TourController.cs", "src/Pd.Poc.Shell/Tour/TourLayerContent.cs")
# 커버리지 대상: 메뉴 항목, 그리고 셸 표면의 버튼·선택 칸·상태 표시
MENU_GLOB = "src/Pd.Ui.Views/Menus/*.axaml"
SURFACE_FILES = ("src/Pd.Ui.Views/MainTitleBar.axaml", "src/Pd.Ui.Views/MainView.axaml", "src/Pd.Ui.Views/ShellStatusBar.axaml",
                 "src/Pd.Ui.Views/ChromeButtons.axaml", "src/Pd.Ui.Views/SdiTitleBar.axaml", "src/Pd.Ui.Views/MobileNavView.axaml")
SURFACE_ID = re.compile(r"^(shell\.(btn|sw|cmb|status|ctx|nav|mnuMore)|chrome\.btn)")


class Source:
    """파일 읽기. overrides 로 파일 내용을 바꿔 넣어 자체 시험이 저장소를 건드리지 않게 한다."""

    def __init__(self, root, overrides=None, cache=None):
        self.root = root
        self.overrides = overrides or {}
        self.cache = cache if cache is not None else {}

    def read(self, rel):
        if rel in self.overrides:
            return self.overrides[rel]
        if rel not in self.cache:
            try:
                with open(os.path.join(self.root, rel), encoding="utf-8-sig") as f:
                    self.cache[rel] = f.read()
            except FileNotFoundError:
                self.cache[rel] = None
        return self.cache[rel]

    def glob(self, pattern):
        key = "glob:" + pattern
        if key not in self.cache:
            found = glob.glob(os.path.join(self.root, pattern), recursive=True)
            self.cache[key] = sorted(os.path.relpath(p, self.root).replace(os.sep, "/") for p in found)
        return self.cache[key]


def normalize_gesture(text):
    """Ctrl+D0 → Ctrl+0 처럼 코드 표기를 카탈로그 표기로 맞춘다."""
    parts = text.split("+")
    return "+".join(parts[:-1] + [KEY_NAMES.get(parts[-1], parts[-1])])


def tuple_gestures(source):
    """WindowKeyGestures 의 (Key.X or Key.Y, KeyModifiers.A | KeyModifiers.B) 패턴을 표기로 푼다."""
    found = set()
    for keys, mods in WKG_TUPLE.findall(source):
        names = {m.split(".")[1] for m in mods.split(" | ")}
        prefix = [label for name, label in MOD_ORDER if name in names]
        for key in keys.split(" or "):
            bare = key.split(".")[1]
            found.add("+".join(prefix + [KEY_NAMES.get(bare, bare)]))
    return found


def code_gestures(src, problems):
    found = {}
    for pattern in GESTURE_AXAMLS:
        for rel in src.glob(pattern):
            for gesture in GESTURE_AXAML.findall(XML_COMMENT.sub("", src.read(rel) or "")):
                found.setdefault(normalize_gesture(gesture), os.path.basename(rel))
    rel = "src/Pd.Ui.Views/Input/WindowKeyGestures.cs"
    for gesture in tuple_gestures(src.read(rel) or ""):
        found.setdefault(gesture, os.path.basename(rel))
    for rel, marker, gesture in FIXED_MARKERS:
        if marker not in (src.read(rel) or ""):
            problems.append("③ %s 에 %s 가 없어 '%s' 를 대조할 수 없음(검사기 표식을 갱신)" % (rel, marker, gesture))
        found.setdefault(gesture, os.path.basename(rel))
    return found


def load_dict(src, lang):
    """투어 키는 전부 sysview 영역이다."""
    return set(json.loads(src.read("src/Pd.Poc.Shell/Localization/sysview.%s.json" % lang)).keys())


def automation_ids(src, wanted):
    """대상이 있는 파일을 먼저 읽고, 못 찾은 대상이 남을 때만 폴더 전체를 읽는다(NTFS 3초 상한)."""
    ids = set()

    def scan(rel):
        text = XML_COMMENT.sub("", src.read(rel) or "")
        ids.update(AUTOMATION_AXAML.findall(text))
        ids.update(AUTOMATION_SETTER.findall(text))

    for rel in TARGET_FILES:
        scan(rel)
    if wanted - ids:
        for pattern in TARGET_GLOBS:
            for rel in src.glob(pattern):
                scan(rel)
    return ids


def required_keys(steps, entries, features, src):
    keys = set()
    for step in steps:
        text = step.get("text", step["id"])
        keys.update(("sysview.tour.%s.title" % text, "sysview.tour.%s.body" % text))
    keys.update("sysview.tour.key.%s" % e.get("text", e["id"]) for e in entries)
    for feature in features:
        keys.update(("sysview.tour.feature.%s.title" % feature["id"], "sysview.tour.feature.%s.body" % feature["id"]))
    for rel in TOUR_UI_CS:
        text = src.read(rel) or ""
        keys.update("sysview.tour.ui." + key for key in UI_KEY.findall(text))
        keys.update(LOC_KEY.findall(text))
    return keys


def check_shortcuts(entries, src, problems):
    bindings = set(re.findall(r'"(\w+)" =>', src.read(BINDINGS) or ""))
    for entry in entries:
        label = "단축키 %s" % entry["id"]
        if bool(entry.get("binding")) == bool(entry.get("handler")):
            problems.append("⑤ %s: binding·handler 중 하나만 있어야 함" % label)
        elif entry.get("binding"):
            if entry["binding"] not in bindings:
                problems.append("③ %s: binding '%s' 이 ShortcutBindings 에 없음" % (label, entry["binding"]))
        else:
            rel, _, marker = entry["handler"].partition(":")
            if not marker or marker not in (src.read(rel) or ""):
                problems.append("③ %s: 처리 위치 '%s' 에 표식이 없음" % (label, entry["handler"]))
    if "<KeyBinding" in XML_COMMENT.sub("", src.read("src/Pd.Ui.Views/MainView.axaml") or ""):
        problems.append("③ MainView.axaml 에 KeyBinding 직접 작성 금지 — 카탈로그 shortcuts 의 binding 으로 등록")


def coverable_ids(src):
    ids = set()
    for rel in src.glob(MENU_GLOB):
        ids.update(i for i in AUTOMATION_AXAML.findall(XML_COMMENT.sub("", src.read(rel) or "")) if i.startswith("shell.mnu"))
    for rel in SURFACE_FILES:
        ids.update(i for i in AUTOMATION_AXAML.findall(XML_COMMENT.sub("", src.read(rel) or "")) if SURFACE_ID.match(i))
    ids.update("winmenu." + m for m in re.findall(r'\(\w+\.\w+, "[\w.]+", "(\w+)"\)', src.read(WINDOW_MENU) or ""))
    ids.update(re.findall(r'SetAutomationId\(\w+, "([\w.]+)"\)', src.read(PANEL_MENU) or ""))
    return ids


def nav_menu_ids(src):
    """메뉴 카탈로그 항목은 기능 레이어의 메뉴 줄로 자동 설명된다."""
    ids = set()
    for args in re.findall(r"\b[GIX]\([FWVS], \d, ([^\n]*?)\)[,)]", src.read(NAV_MENU) or ""):
        ids.update("shell.mnu" + name for name in re.findall(r'"(\w+)"', args))
    return ids


def check_coverage(data, steps, features, src, problems):
    coverable = coverable_ids(src)
    described = {s["target"] for s in steps}
    for item in steps + features:
        described.update(item.get("covers") or [])
    menu = nav_menu_ids(src) - {"shell.mnu" + s for s in (data.get("menuRows") or {}).get("skip") or []}
    excludes = [e for e in (data.get("coverage") or {}).get("exclude") or [] if isinstance(e, dict) and e.get("id")]
    for covered in sorted({c for item in steps + features for c in item.get("covers") or []} - coverable):
        problems.append("⑥ covers 가 없는 항목을 가리킴: %s" % covered)
    for exclude in excludes:
        if not any(fnmatch.fnmatchcase(i, exclude["id"]) for i in coverable):
            problems.append("⑥ 제외 목록이 없는 항목을 가리킴: %s" % exclude["id"])
    missing = 0
    for item in sorted(coverable):
        if item in described or item in menu or any(fnmatch.fnmatchcase(item, e["id"]) for e in excludes):
            continue
        missing += 1
        problems.append("⑥ 카탈로그에 설명이 없는 새 항목: %s — 단계·기능 covers 나 메뉴 카탈로그에 넣거나 coverage.exclude 에 사유와 함께 적는다" % item)
    return len(coverable) - missing, len(coverable)


def check(src):
    problems = []
    summary = dict(steps=0, shortcuts=0, features=0, gestures=0, keys=0, covered=0, coverable=0)
    data = parse_catalog(src, problems)
    if data is None:
        return problems, summary
    steps, entries, features = check_format(data, src, problems)
    if not steps or not entries or not features:
        problems.append("④ 카탈로그를 읽지 못함(단계 %d·단축키 %d·기능 %d)" % (len(steps), len(entries), len(features)))

    keys = required_keys(steps, entries, features, src)
    for lang in ("ko", "en"):
        for key in sorted(keys - load_dict(src, lang)):
            problems.append("① %s 사전에 키 없음: %s" % (lang, key))

    ids = automation_ids(src, {s["target"] for s in steps if not s["target"].startswith("@")})
    anchors = set(re.findall(r'const string \w+ = "(@\w+)"', src.read("src/Pd.Poc.Shell/Tour/TourCatalog.cs") or ""))
    for step in steps:
        target = step["target"]
        if (target.startswith("@") and target not in anchors) or (not target.startswith("@") and target not in ids):
            problems.append("② 단계 %s 의 대상 없음: %s" % (step["id"], target))

    catalog_keys = [e["keys"] for e in entries]
    gestures = code_gestures(src, problems)
    for gesture, origin in sorted(gestures.items()):
        if gesture not in catalog_keys:
            problems.append("③ %s 의 실제 키가 카탈로그에 없음: %s" % (origin, gesture))
    check_shortcuts(entries, src, problems)

    step_ids = [s["id"] for s in steps]
    for dup in sorted({i for i in step_ids if step_ids.count(i) > 1}):
        problems.append("④ 단계 id 중복: %s" % dup)
    for dup in sorted({k for k in catalog_keys if catalog_keys.count(k) > 1}):
        problems.append("④ 단축키 Keys 중복: %s" % dup)
    entry_ids = [e["id"] for e in entries]
    for dup in sorted({i for i in entry_ids if entry_ids.count(i) > 1}):
        problems.append("④ 단축키 id 중복: %s" % dup)
    feature_ids = [f["id"] for f in features]
    for dup in sorted({i for i in feature_ids if feature_ids.count(i) > 1}):
        problems.append("④ 기능 id 중복: %s" % dup)
    if not any(s["id"].startswith("nav.") for s in steps):
        problems.append("④ NAV 단계가 없음")
    if not any(e.get("webUnavailable") for e in entries):
        problems.append("④ 웹 사용 불가 표기가 없음")

    covered, coverable = check_coverage(data, steps, features, src, problems)
    summary.update(steps=len(steps), shortcuts=len(entries), features=len(features), gestures=len(gestures), keys=len(keys), covered=covered, coverable=coverable)
    return problems, summary
