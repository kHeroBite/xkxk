# 메뉴 레지스트리 판정기 공용 상수·파일 읽기·C# 토큰화·식 파서(menu_registry_check 분할 모듈)
import glob
import os
import re

DEFAULT_BASE = "e8950ea"
NAV = "src/xkxk.frameworks.ui.data/Navigation/"
SH = "src/xkxk.frameworks.ui.shell/"
VW = "src/xkxk.frameworks.ui.views/"
OLD_MENUS = ("FileMenu", "WindowMenu", "ViewMenu", "SystemMenuItem")
F_IDS = SH + "Framework/ShellCommandIds.cs"
F_NAV = NAV + "NavMenuCatalog.cs"
F_TITLE = VW + "MainTitleBar.axaml"
TABLE_GLOB = NAV + "ShellMenuTable*.cs"
SECTIONS_GLOB = VW + "Menus/ShellMenuSections*.cs"
TESTBED_MENUS_GLOB = "testbed/xkxk.frameworks.ui.testbed/TestbedMenus*.cs"  # B11-4: 데모·벤치 8행의 등록 위치(표에서 이전)
DICTS = ("Localization/*.ko.json", "Localization/*.en.json")
ROW_F = ("Id", "Parent", "Kind", "CommandId", "Param", "TitleKey", "Icon", "AutomationId", "Section", "VisibleWhen", "ToolTipWhen", "Mobile")
MOB_F = ("InDrawer", "Available", "ReasonKey", "EntryId", "TitleKey", "CommandId", "Param", "TabOrder", "TabKey", "TabIcon", "Covers")
ICON_EXPR = {"MenuIconCatalog.GridIconKey", "MenuIconCatalog.GanttIconKey", "MenuIconCatalog.FormIconKey"}
ROOT_GROUP = {"xkxk.file": "File", "xkxk.window": "Window", "xkxk.view": "View", "xkxk.system": "System"}
NAV_FIELDS = ("id", "textKey", "group", "depth", "isGroup", "applicable", "reason", "tabOrder", "tabKey", "icon", "desktopIds")
USAGE = "사용: menu_registry_check.py [--root <R>] [--base <커밋>] [--base-root <git 작업 폴더>] [--self-test] [--help]"
TOK = re.compile(r'\s+|(?P<vs>@"(?:[^"]|"")*")|(?P<s>"(?:[^"\\\n]|\\.)*")|(?P<n>\d+)|(?P<id>[A-Za-z_]\w*)|(?P<op>=>|\S)')


class EvalError(Exception):
    pass


class Unresolved(str):
    """CmdIconKeys 에 정의되지 않은 아이콘 이름처럼 값으로 풀지 못한 이름 — ③ 이 사유를 낸다."""


class IconRef(str):
    """MenuIconCatalog.(Grid|Gantt|Form)IconKey — 실행 때 계산되는 허용 아이콘 식."""


def read(root, rel):
    p = os.path.join(root, rel)
    if not os.path.isfile(p):
        return None
    with open(p, encoding="utf-8-sig", errors="replace") as f:
        return f.read()


def rglob(root, pattern):
    return sorted(os.path.relpath(p, root).replace(os.sep, "/") for p in glob.glob(os.path.join(root, pattern), recursive=True))


def tokenize(text, src="?"):
    out, line = [], 1
    for m in TOK.finditer(text):
        g = m.group(0)
        if m.lastgroup == "vs":
            out.append(("s", g[2:-1].replace('""', '"'), line, src))
        elif m.lastgroup == "s":
            out.append(("s", re.sub(r"\\(.)", lambda e: {"n": "\n", "t": "\t"}.get(e.group(1), e.group(1)), g[1:-1]), line, src))
        elif m.lastgroup:
            out.append((m.lastgroup, g, line, src))
        line += g.count("\n")
    return out


class Parser:
    """C# 식의 작은 부분집합(리터럴·이름·호출·new·컬렉션·with·+)을 노드 튜플로 바꾼다."""

    def __init__(self, toks, i=0):
        self.t, self.i = toks, i

    def peek(self, k=0):
        j = self.i + k
        return self.t[j] if j < len(self.t) else ("eof", "", 0, "")

    def is_(self, v, k=0):
        return self.peek(k)[1] == v and self.peek(k)[0] in ("op", "id")

    def take(self, v=None):
        tok = self.peek()
        if tok[0] == "eof" or (v is not None and tok[1] != v):
            raise EvalError("'%s' 기대, '%s' 있음(%s:%s)" % (v, tok[1], tok[3], tok[2]))
        self.i += 1
        return tok

    def expr(self):
        node = self.postfix()
        while self.is_("+"):
            self.take()
            node = ("cat", node, self.postfix())
        return node

    def postfix(self):
        node = self.primary()
        while True:
            if self.is_("with") and self.is_("{", 1):
                self.take(), self.take("{")
                sets = {}
                while not self.is_("}"):
                    name = self.take()[1]
                    self.take("=")
                    sets[name] = self.expr()
                    if self.is_(","):
                        self.take()
                self.take("}")
                node = ("with", node, sets)
            elif self.is_(".") and self.peek(1)[0] == "id" and self.is_("(", 2):
                self.take(), self.take(), self.args("(", ")")  # .ToArray() 같은 꼬리 호출은 값 그대로
            else:
                return node

    def args(self, open_, close):
        self.take(open_)
        out = []
        while not self.is_(close):
            name = None
            if self.peek()[0] == "id" and self.is_(":", 1):
                name = self.take()[1]
                self.take(":")
            out.append((name, self.expr()))
            if self.is_(","):
                self.take()
        self.take(close)
        return out

    def type_name(self):
        name = self.take()[1]
        while self.is_(".") and self.peek(1)[0] == "id":
            self.take()
            name += "." + self.take()[1]
        if self.is_("<"):
            depth = 0
            while True:
                v = self.take()[1]
                depth += (v == "<") - (v == ">")
                if depth == 0:
                    break
        if self.is_("?"):
            self.take()
        return name

    def primary(self):
        kind, v, line, src = self.peek()
        if kind == "s":
            self.take()
            return ("lit", v)
        if kind == "n":
            self.take()
            return ("lit", int(v))
        if v == "-" and self.peek(1)[0] == "n":
            self.take()
            return ("lit", -int(self.take()[1]))
        if v in ("null", "true", "false") and kind == "id":
            self.take()
            return ("lit", {"null": None, "true": True, "false": False}[v])
        if v == "new" and kind == "id":
            self.take()
            if self.is_("("):
                return ("ctor", None, self.args("(", ")"), line, src)
            if self.is_("["):
                self.take("["), self.take("]")
                return ("list", [e for _, e in self.args("{", "}")])
            tname = self.type_name()
            if self.is_("["):
                self.take("[")
                empty = self.peek()[0] == "n"
                while not self.is_("]"):
                    self.take()
                self.take("]")
                return ("list", [] if empty else [e for _, e in self.args("{", "}")])
            if self.is_("("):
                a = self.args("(", ")")
                if self.is_("{"):
                    return ("list", [e for _, e in self.args("{", "}")])
                return ("ctor", tname, a, line, src)
            return ("list", [e for _, e in self.args("{", "}")])
        if v == "[" and kind == "op":
            return ("list", [e for _, e in self.args("[", "]")])
        if v == "(" and kind == "op":
            self.take()
            node = self.expr()
            self.take(")")
            return node
        if kind == "id":
            name = self.type_name()
            if self.is_("("):
                return ("call", name, self.args("(", ")"), line, src)
            return ("name", name, line, src)
        raise EvalError("해석 못한 토큰 '%s'(%s:%s)" % (v, src, line))
