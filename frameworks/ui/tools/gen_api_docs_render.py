# load_surface 결과를 docs/api/ 의 Markdown 명세 파일들(README + 그룹 9개, 300줄 분할)로 그리는 렌더러
"""render(surface) -> {'docs/api/<파일>.md': 텍스트}. 날짜·커밋 같은 비결정 값은 넣지 않는다(같은 입력 → 같은 바이트)."""

HEADER = "<!-- 생성물 — tools/gen_api_docs.py 가 만든다. 손으로 고치지 않는다 -->"
LIMIT = 300
OUT_DIR = "docs/api"
UNCLASSIFIED = "99_미분류"
MENU_TABLE = "src/xkxk.frameworks.ui.data/Navigation/ShellMenuTable.cs"  # 08 셸 메뉴 ID 표의 입력(load_menu_rows)

# 그룹 표 — design_contract groups 와 같은 순서. 새 공개 형식은 여기 넣지 않으면 99_미분류 로 간다.
GROUPS = [
    ("01_진입_빌더_모듈", ["XkxkUi", "XkxkUiBuilder", "XkxkUiBuilderOptions", "XkxkUiApplication",
                       "XkxkUiAppBuilderExtensions", "UiHostOptions", "UiHostKinds", "UiModule", "IUiModule",
                       "IUiModuleContext", "UiServiceRegistry", "IUiServiceProvider"]),
    ("02_앱컨텍스트_문구", ["IUiAppContext", "IUiLocalizer", "UiLanguage"]),
    ("03_아이콘", ["IUiIconRegistry", "UiIconProvider", "UiIconDefinition", "UiIconOptions", "UiIconKind"]),
    ("04_명령", ["IUiCommandRegistry", "UiCommandOptions", "UiCommandDefinition", "UiCommandContext"]),
    ("05_메뉴", ["IUiMenuRegistry", "UiMenuItemOptions", "UiMenuItemDefinition", "UiMenuItemKind", "UiMenuItemState",
               "UiMenuMobileOptions", "UiMenuMobileDefinition", "UiMenuPlacementOptions", "UiMenuContext",
               "UiMenuSurface"]),
    ("06_진단_공통", ["UiDiagnostics", "UiDiagnostic", "UiDiagnosticSeverity", "UiDiagnosticIds", "UiErrorPolicy",
                  "UiConfigurationException", "UiRegistryOwners", "UiRegistryChangeKind",
                  "UiRegistryChangedEventArgs"]),
    ("07_셸명령ID", ["ShellCommandIds"]),
    ("08_셸메뉴ID", ["ShellMenuIds"]),
    ("09_창", ["IUiWindowRegistry", "UiWindowOptions", "UiWindowDefinition", "UiWindowPlacement", "UiWindowOpenOptions",
             "UiWindowContentContext"]),
]

# UADR-0009 8원칙 제목 — 출처 /docs/standards/02_공개API_호환원칙.md §3(저장소 루트 기준). 원칙이 바뀌면 함께 고친다.
PRINCIPLES = [
    "확장 입력은 옵션 객체로 받는다",
    "기본값 선택 파라미터를 공개 API 에 두지 않는다",
    "확장점을 미리 연다",
    "소비자가 구현하는 계약은 추상 기반 클래스로 받는다",
    "식별자는 문자열 ID 상수로 둔다",
    "저장 형식에는 버전을 둔다",
    "깨짐은 자동으로 검출한다",
    "폐기는 한 부 버전 이상 예고한다",
]
STANDARD_LINK = "../../../../docs/standards/02_공개API_호환원칙.md"
BANDS = {"00": "모듈", "01": "서비스", "02": "아이콘", "03": "명령", "04": "메뉴"}


def _cell(text):
    return (text or "").replace("\n", " ").replace("|", "\\|").strip()


def _code(text):
    return "`" + _cell(text) + "`"


def _strip_ns(text, namespaces):
    for ns in sorted(namespaces, key=len, reverse=True):
        text = text.replace(ns + ".", "")
    return text


def _title(name):
    num, _, rest = name.partition("_")
    return "%s %s" % (num, rest.replace("_", "·"))


def _signature(member, full, namespaces):
    """txt 원문에서 '<형식>.' 을 떼고 네임스페이스 접두를 줄인 시그니처."""
    raw = member["raw"]
    return _strip_ns(raw.replace(full + ".", "", 1), namespaces)


class _Pager:
    """줄 수 상한(LIMIT)을 지키며 쪽을 나눈다. 표 중간에서 넘치면 '(계속)' 머리와 표 머리를 다시 단다."""

    def __init__(self):
        self.pages, self.cur = [], None
        self._new()

    def _new(self):
        self.cur = []
        self.pages.append(self.cur)

    def fits(self, n):
        return len(self.cur) + n <= LIMIT - 6  # 쪽 머리 6줄 자리

    def block(self, lines):
        if self.cur and not self.fits(len(lines)):
            self._new()
        self.cur.extend(lines)

    def table(self, type_name, head, rows):
        if not self.fits(len(head) + 1):
            self._new()
            self.cur.extend(["## %s (계속)" % type_name, ""])
        self.cur.extend(head)
        for row in rows:
            if not self.fits(1):
                self._new()
                self.cur.extend(["## %s (계속)" % type_name, ""] + head)
            self.cur.append(row)
        self.cur.append("")


def _type_section(name, t, namespaces, pager, menu_rows=None):
    head = ["## %s" % name, "", "- 종류: %s · 전체 이름: %s" % (_code(t["kind"]), _code(t["full"])), ""]
    if t["summary"]:
        head += t["summary"].split("\n") + [""]
    if t["remarks"]:
        head += ["> " + p for p in t["remarks"].split("\n")] + [""]
    consts = [m for m in t["members"] if m["member_kind"] == "const"]
    others = [m for m in t["members"] if m["member_kind"] != "const"]
    if not t["members"]:
        head += ["공개 멤버 없음.", ""]
    pager.block(head)
    def desc(m):
        return _cell(m.get("summary") or t["member_docs"].get(m["name"]) or "(설명 없음)")
    if consts and menu_rows and name == "ShellMenuIds":
        rows = []  # 메뉴 표 순서(트리 깊이 우선)로 싣고, 표에 없는 값(섹션 이름)은 끝에 둔다
        same = {}
        for c in consts:
            same.setdefault(c["const_value"], []).append(c["name"])
        for m in sorted(consts, key=lambda c: menu_rows.get(c["const_value"], (0, 0, 0, len(menu_rows)))[3]):
            parent, kind, order, _, cmd, arg = menu_rows.get(m["const_value"], (None,) * 6)
            where = ("(최상위)" if parent is None else _code(parent)) if kind else "—"
            run = (_code(cmd) + (" 인자 %s" % _code(arg) if arg else "")) if cmd else "—"
            twins = [n for n in same[m["const_value"]] if n != m["name"]]
            note = "(같은 값: %s — 같은 항목을 가리키는 별칭) " % "·".join(_code(n) for n in twins) if twins else ""
            rows.append("| %s | %s | %s | %s | %s | %s | %s |" % (_code(m["name"]), _code(m["const_value"]), where,
                                                               kind or "섹션 이름", order or "—", run, note + desc(m)))
        pager.table(name, ["| 상수 | 값 | 부모 | 종류 | 순서 | 명령 ID | 설명 |", "|---|---|---|---|---|---|---|"], rows)
    elif consts:
        rows = ["| %s | %s | %s |" % (_code(m["name"]), _code(m["const_value"]), desc(m)) for m in consts]
        pager.table(name, ["| 상수 | 값 | 설명 |", "|---|---|---|"], rows)
    if others:
        rows = ["| %s | %s | %s |" % (_code(m["name"]), _code(_signature(m, t["full"], namespaces)), desc(m))
                for m in others]
        pager.table(name, ["| 멤버 | 시그니처 | 설명 |", "|---|---|---|"], rows)


def _finish(name, pages):
    """쪽마다 머리(생성물 경고·제목·이동 줄)를 붙이고 {파일명: 텍스트} 를 만든다."""
    names = [name + ".md"] + ["%s_%d.md" % (name, i) for i in range(2, len(pages) + 1)]
    out = {}
    for i, body in enumerate(pages):
        title = "# %s — 공개 API 명세" % _title(name)
        if len(pages) > 1:
            title += " (%d/%d)" % (i + 1, len(pages))
        nav = "[명세 색인](README.md)"
        if len(pages) > 1:
            nav += " · 쪽: " + " · ".join("[%d](%s)" % (j + 1, n) for j, n in enumerate(names))
        lines = [HEADER, "", title, "", nav, ""] + body
        while lines and lines[-1] == "":
            lines.pop()
        out["%s/%s" % (OUT_DIR, names[i])] = "\n".join(lines) + "\n"
    return out


def _readme(surface, group_files, stats):
    counts = surface.get("txt_line_counts", {})
    shipped = sum(v for k, v in counts.items() if k.endswith("Shipped.txt") and not k.endswith("Unshipped.txt"))
    unshipped = sum(v for k, v in counts.items() if k.endswith("Unshipped.txt"))
    ns_list = "·".join(_code(n) for n in surface.get("namespaces", []))
    inputs = "- 입력: " + "·".join(_code(f) for f in surface.get("txt_files", [])) + "."
    if surface.get("menu_rows"):
        inputs += " 셸 메뉴 ID 표의 부모·종류·순서·명령 ID 열은 %s 에서 읽는다." % _code(MENU_TABLE)
    lines = [HEADER, "", "# 별별UI 공개 API 명세", "",
             "이 폴더는 `tools/gen_api_docs.py` 가 PublicAPI txt(공개 표면 정본)와 소스의 `///` 주석으로 만든다.",
             "손으로 고치지 않는다. 공개 API 가 바뀌면 `python3 -B tools/gen_api_docs.py` 로 다시 만들고 `--check` 로 확인한다.", "",
             "## 범위", "",
             "- 공개 계약은 PublicAPI txt 에 실린 형식뿐이다. 현재 네임스페이스: %s." % ns_list,
             "- 그 밖의 public 형식(셸 내부 public 등)은 공개 계약이 아니며 예고 없이 바뀔 수 있다. 공개 범위는 B16 에서 다른 라이브러리로 넓힌다.",
             "- 시그니처는 PublicAPI 분석기 표기다. `!` 는 null 아님, `?` 는 null 허용, `-> T` 는 반환·속성 형식이다. 위 네임스페이스 접두는 줄였다.",
             inputs, "",
             "## Shipped 와 Unshipped", "",
             "- Shipped 는 출시된 판에 실린 API 다. 지우거나 서명을 바꾸면 호환이 깨진다.",
             "- Unshipped 는 다음 판에 실릴 API 다. 사전판(0.x 전) 동안은 전부 Unshipped 이며 바뀔 수 있으니 고정 계약으로 가정하지 않는다.",
             "- 현재 줄 수: Shipped %d · Unshipped %d." % (shipped, unshipped), "",
             "## 호환 규칙 (UADR-0009 8원칙)", ""]
    lines += ["%d. %s" % (i, p) for i, p in enumerate(PRINCIPLES, 1)]
    lines += ["", "상세와 자동 검출 방법은 [공개 API 호환 원칙](%s) 에 있다." % STANDARD_LINK, "",
              "## 진단 코드", "",
              "대역: " + " · ".join("XKUI%sxx %s" % (k, v) for k, v in BANDS.items())
              + ". 한 번 쓴 번호는 재사용하지 않는다.", "",
              "진단 모음은 둘이며 서로 합치지 않는다. 빌드 중 진단은 `XkxkUiApplication.Diagnostics`(빌더 `Build` 결과),"
              " 빌드 뒤 실행 중 진단은 `IUiAppContext.Diagnostics`(`XkxkUi.Current.Diagnostics`)에 쌓인다.", "",
              "| 코드 | 상수 | 대역 | 의미 |", "|---|---|---|---|"]
    for d in sorted(surface.get("diagnostics", []), key=lambda x: x["code"] or ""):
        band = BANDS.get((d["code"] or "")[4:6], "기타")
        lines.append("| %s | %s | %s | %s |" % (_code(d["code"]), _code("UiDiagnosticIds." + d["name"]), band,
                                               _cell(d["summary"] or "(설명 없음)")))
    lines += ["", "## 그룹 파일", "", "| 파일 | 형식 |", "|---|---|"]
    for name, files, members in group_files:
        links = " · ".join("[%s](%s)" % (_title(name) if i == 0 else str(i + 1), f.rsplit("/", 1)[-1])
                           for i, f in enumerate(files))
        lines.append("| %s | %s |" % (links, "·".join(_code(m) for m in members)))
    lines += ["", "## 통계", "",
              "- PublicAPI 줄(`#` 줄 제외) %d · 형식 %d · 멤버 %d · 설명 없는 멤버 %d." % (
                  surface.get("line_count", 0), stats["types"], stats["members"], stats["undocumented"])]
    if surface.get("unresolved"):
        lines.append("- 미해석 줄 %d — 생성기를 고쳐야 한다." % len(surface["unresolved"]))
    return "\n".join(lines) + "\n"


def stats_of(surface):
    types = surface["types"]
    members = sum(len(t["members"]) for t in types.values())
    undoc = sum(1 for t in types.values() for m in t["members"]
                if not (m.get("summary") or t["member_docs"].get(m["name"])))
    return {"types": len(types), "members": members, "undocumented": undoc}


def render(surface):
    types, namespaces = surface["types"], surface.get("namespaces", [])
    grouped = set()
    plan = []
    for name, members in GROUPS:
        present = [m for m in members if m in types]
        grouped.update(present)
        if present:
            plan.append((name, present))
    rest = sorted(k for k in types if k not in grouped)
    if rest:
        plan.append((UNCLASSIFIED, rest))
    out, group_files = {}, []
    for name, members in plan:
        pager = _Pager()
        for m in members:
            _type_section(m, types[m], namespaces, pager, surface.get("menu_rows"))
        files = _finish(name, [p for p in pager.pages if p] or [[]])
        out.update(files)
        group_files.append((name, list(files), members))
    readme = {OUT_DIR + "/README.md": _readme(surface, group_files, stats_of(surface))}
    readme.update(out)
    return readme
