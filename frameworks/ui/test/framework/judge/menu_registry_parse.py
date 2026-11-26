# 메뉴 레지스트리 판정기 ShellMenuTable 판독·기준 커밋 읽기·모바일 목록 파생(menu_registry_check 분할 모듈, 공개 함수 parse_table_files·derive_nav·leaf_aids)
import glob
import os
import re
import subprocess

from app_context_check import strip_cs, strip_xml  # 같은 폴더의 B04 판정기 — 주석 제거 규칙을 한 곳에 둔다
from menu_registry_eval import *  # noqa: F401,F403


def cmd_icon_keys(root):
    t = read(root, NAV + "CmdIconKeys.Generated.cs") or ""
    return dict(re.findall(r'public const string (\w+) = "(PdCmdIcon\w+)";', t))


def parse_table(root, errors):
    """ShellMenuTable*.cs 의 행 목록(등장 순서). 해석 실패는 errors 에 더한다."""
    paths = [os.path.join(root, r) for r in rglob(root, TABLE_GLOB)]
    if not paths:
        errors.append("파일 없음: " + NAV + "ShellMenuTable.cs")
        return []
    return parse_table_files(paths, errors)


def parse_table_files(paths, errors, text_of=None):
    """표 파일 경로 목록(같은 Navigation 폴더) → 행 목록. 상수는 그 폴더 *.cs(표 파일 것이 우선), 아이콘 키는 CmdIconKeys.Generated.cs.
    text_of(절대경로) → 원문|None 을 주면 그것으로 읽는다(검사기 자체 시험의 덮어쓰기용)."""
    if not paths:
        errors.append("표 파일 없음")
        return []
    paths = sorted((os.path.abspath(p) for p in paths), key=lambda p: (os.path.basename(p) != "ShellMenuTable.cs", os.path.basename(p)))
    navdir = os.path.dirname(paths[0])
    text_of = text_of or (lambda p: read(navdir, p))
    names, helpers, per_file = {}, {}, []
    for p in [os.path.abspath(q) for q in sorted(glob.glob(os.path.join(navdir, "*.cs"))) if os.path.abspath(q) not in paths] + paths:
        names.update(defs(tokenize(strip_cs(text_of(p) or ""), os.path.basename(p)))[0])
    for path in paths:
        rel = os.path.basename(path)
        toks = tokenize(strip_cs(text_of(path) or ""), rel)
        try:
            n, h, skip = defs(toks)
        except (EvalError, StopIteration, IndexError) as ex:
            errors.append("표 정의 해석 실패 %s: %s" % (rel, ex))
            continue
        names.update(n)
        helpers.update(h)
        per_file.append((toks, skip))
    ck = text_of(os.path.join(navdir, "CmdIconKeys.Generated.cs")) or ""
    ev = Evaluator(names, helpers, cmd_keys=dict(re.findall(r'public const string (\w+) = "(PdCmdIcon\w+)";', ck)))
    rows = []
    for toks, skip in per_file:
        i = 0
        while i < len(toks):
            hit = next((e for s, e in skip if s <= i < e), None)
            if hit is not None:
                i = hit + 1
                continue
            kind, v = toks[i][0], toks[i][1]
            start = (kind == "id" and v == "new") or (kind == "id" and helpers.get(v, ("",))[0] == "ShellMenuRow"
                                                      and i + 1 < len(toks) and toks[i + 1][1] == "(")
            if not start:
                i += 1
                continue
            p = Parser(toks, i)
            try:
                node = p.expr()
            except EvalError as ex:
                errors.append("표 식 해석 실패: %s" % ex)
                i += 1
                continue
            try:
                val = ev.eval(node, {})
            except EvalError as ex:
                if mentions_row(node, helpers):
                    errors.append("표 행 값 계산 실패: %s" % ex)
                val = None
            for r in (val if isinstance(val, list) else [val]):
                if isinstance(r, dict) and r.get("_t") == "row":
                    rows.append(r)
            i = max(p.i, i + 1)
    return rows


def git_reader(base_root, base):
    def rd(rel):
        try:
            p = subprocess.run(["git", "-C", base_root, "-c", "core.quotepath=off", "show", "%s:./%s" % (base, rel)], capture_output=True,
                               env=dict(os.environ, GIT_OPTIONAL_LOCKS="0"), timeout=60)
        except (OSError, subprocess.TimeoutExpired):
            return None
        return p.stdout.decode("utf-8-sig", "replace") if p.returncode == 0 else None
    return rd


def base_aids(base_text, bad):
    ids = set()
    for m in OLD_MENUS:
        t = base_text(VW + "Menus/%s.axaml" % m)
        if t is None:
            bad.append("기준 커밋에서 %s.axaml 을 읽지 못함(판정불가)" % m)
            continue
        ids |= set(re.findall(r'AutomationId="(shell\.[A-Za-z0-9]+)"', strip_xml(t)))
    return ids


def base_nav(base_text, bad):
    """기준 커밋 NavMenuCatalog Build() 행 → 비교용 dict 목록."""
    t = base_text(F_NAV)
    if t is None:
        bad.append("기준 커밋에서 NavMenuCatalog.cs 를 읽지 못함(판정불가)")
        return []
    toks = tokenize(strip_cs(t), "base:NavMenuCatalog.cs")
    names = defs(toks)[0]
    names.update({k: ("lit", v) for k, v in (("F", "File"), ("W", "Window"), ("V", "View"), ("S", "System"))})

    def ids(i, extra):
        return [s if s.startswith("shell.") else "shell.mnu" + s for s in [i] + list(extra)]

    def ent(g, d, i, key, grp, app, extra):
        return {"id": i, "textKey": key, "group": g, "depth": d, "isGroup": grp, "applicable": app, "reason": None if app else "nav.reason.pcTablet",
                "tabOrder": -1, "tabKey": None, "icon": None, "desktopIds": ids(i, extra)}

    def x(g, d, i, key, *rest):
        grp = rest[0] if rest and isinstance(rest[0], bool) else False
        return ent(g, d, i, key, grp, False, rest[1:] if rest and isinstance(rest[0], bool) else rest)

    natives = {"G": lambda g, d, i, key, *e: ent(g, d, i, key, True, True, e),
               "I": lambda g, d, i, key, cmd, param=None, *e: ent(g, d, i, key, False, True, e),
               "X": x, "Tab": lambda e, o, k, ic: dict(e, tabOrder=o, tabKey=k, icon=ic)}
    try:
        b = next(k for k in range(len(toks) - 3) if toks[k][1] == "Build" and toks[k + 1][1] == "(" and toks[k + 3][1] == "{")
        r = next(k for k in range(b, len(toks)) if toks[k][1] == "return")
        out = Evaluator(names, natives=natives).eval(Parser(toks, r + 1).expr(), {})
    except (StopIteration, EvalError, TypeError) as ex:
        bad.append("기준 NavMenuCatalog 행 해석 실패(판정불가): %s" % ex)
        return []
    return out


def derive_nav(rows):
    kids = {}
    for r in rows:
        kids.setdefault(r["Parent"], []).append(r)
    out = []

    def walk(r, depth, root):
        m, aid = r["Mobile"], r["AutomationId"] or ""
        if isinstance(m, dict) and m.get("InDrawer") and (r["Kind"] in ("Command", "Submenu") or m.get("EntryId")):
            eid = m.get("EntryId") or (aid[len("shell.mnu"):] if aid.startswith("shell.mnu") else r["Id"])
            ds = []
            for d in [aid or "shell.mnu" + eid] + [c if c.startswith("shell.") else "shell.mnu" + c for c in m.get("Covers") or []]:
                if d not in ds:
                    ds.append(d)
            out.append({"id": eid, "textKey": m.get("TitleKey") or r["TitleKey"], "group": ROOT_GROUP.get(root, "Custom"), "depth": depth,
                        "isGroup": r["Kind"] == "Submenu", "applicable": bool(m.get("Available")),
                        "reason": None if m.get("Available") else m.get("ReasonKey"), "tabOrder": m.get("TabOrder"),
                        "tabKey": m.get("TabKey"), "icon": m.get("TabIcon"), "desktopIds": ds})
        for c in kids.get(r["Id"], []):
            walk(c, depth + 1, root)

    for r in kids.get(None, []):
        walk(r, 0, r["Id"])
    return out


def leaf_aids(rows):
    """자식 행이 없는 셸 메뉴 항목의 AutomationId(구분선 제외, shell.mnu 형) — nav_reach_judge --menu-table 이 쓴다."""
    parents = {r["Parent"] for r in rows}
    return sorted({r["AutomationId"] for r in rows
                   if r["Kind"] != "Separator" and r["Id"] not in parents and (r["AutomationId"] or "").startswith("shell.mnu")})
