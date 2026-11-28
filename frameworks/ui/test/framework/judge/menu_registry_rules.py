# 메뉴 레지스트리 판정기 ①~⑤ 검사 규칙과 본 검사 실행(menu_registry_check 분할 모듈)
import json
import os
import re

from menu_registry_parse import *  # noqa: F401,F403


def dict_union(root, bad):
    out = []
    for pat in DICTS:
        keys, files = set(), rglob(root, SH + pat)
        for rel in files:
            try:
                keys |= set(json.loads(read(root, rel)))
            except ValueError:
                bad.append("사전 해석 실패: " + rel)
        if not files:
            bad.append("사전 없음: " + SH + pat)
        out.append(keys)
    return out


def icon_keys(root):
    cat = read(root, NAV + "MenuIconCatalog.Generated.cs") or ""
    keys = set(cmd_icon_keys(root).values()) | {"PdMenuIcon" + n for n in re.findall(r'MenuIconKeyPrefix \+ "(\w+)"', cat)}
    sm = re.search(r"ShellIconNames\s*\{\s*get;\s*\}\s*=\s*new\[\]\s*\{([^}]*)\}", cat)
    keys |= {"PdShellIcon" + n for n in re.findall(r'"(\w+)"', sm.group(1))} if sm else set()
    return keys | set(re.findall(r'(?:BusyStaticKey|BadgeSlowKey|BadgeErrorKey)\s*=\s*"(\w+)"', cat))


def check_views(root, bad):
    """①"""
    for m in OLD_MENUS:
        for ext in (".axaml", ".axaml.cs"):
            if os.path.exists(os.path.join(root, VW + "Menus/" + m + ext)):
                bad.append("① 옛 메뉴 파일이 남음: %sMenus/%s%s" % (VW, m, ext))
    for rel in rglob(root, VW + "Menus/*.axaml"):
        if "<MenuItem" in strip_xml(read(root, rel)):
            bad.append("① views/Menus axaml 에 MenuItem 고정 항목이 남음: " + rel)
    for rel in rglob(root, VW + "**/*.axaml"):
        left = [a for a in re.findall(r'AutomationId="(shell\.mnu[A-Za-z0-9]*)"', strip_xml(read(root, rel))) if a != "shell.mnuMore"]
        if left:
            bad.append("① views axaml 에 셸 메뉴 고정 AutomationId 가 남음: %s %s" % (rel, left[:3]))
    tb = read(root, F_TITLE)
    if tb is None:
        bad.append("① 파일 없음: " + F_TITLE)
    else:
        if re.search(r"<menus:(FileMenu|WindowMenu|ViewMenu|SystemMenuItem)\b", strip_xml(tb)):
            bad.append("① MainTitleBar.axaml 에 옛 메뉴 요소가 남음")
        if 'AutomationId="shell.mnuMore"' not in strip_xml(tb):
            bad.append("① MainTitleBar.axaml 의 더보기(shell.mnuMore)가 사라짐")


def check_aids(root, rows, base, bad):
    """②"""
    pool = "".join(strip_cs(read(root, r)) for r in rglob(root, TABLE_GLOB) + rglob(root, SECTIONS_GLOB) + rglob(root, TESTBED_MENUS_GLOB))
    lits = set(re.findall(r'"(shell\.mnu[A-Za-z0-9]*)"', pool))
    miss = []
    for i in sorted(base):
        m = re.fullmatch(r"(shell\.mnu[A-Za-z]+?)(\d+)", i)
        if i not in lits and not (m and m.group(1) in lits):
            miss.append(i)
    if miss:
        bad.append("② 보존되지 않은 AutomationId %d개: %s" % (len(miss), miss[:6]))
    aids = [r["AutomationId"] for r in rows if r["AutomationId"]]
    dup = sorted({a for a in aids if aids.count(a) > 1})
    if dup:
        bad.append("② 표에 같은 AutomationId 가 두 번 이상 있음: %s" % dup[:4])


def check_values(root, rows, bad):
    """③ — 반환: ShellCommandIds 상수 수."""
    ids = read(root, F_IDS)
    if ids is None:
        bad.append("③ 파일 없음: " + F_IDS)
        ids = ""
    pairs = re.findall(r'public const string (\w+) = "([\w.]+)";', strip_cs(ids))
    cmds, keys, (ko, en) = {v for _, v in pairs}, icon_keys(root), dict_union(root, bad)
    seen, ids_, found = set(), {r["Id"] for r in rows}, {}

    def add(what, item):
        found.setdefault(what, []).append(item)

    for r in rows:
        rid, m = r["Id"], r["Mobile"]
        if rid in seen:
            add("메뉴 ID 중복", rid)
        seen.add(rid)
        if r["Parent"] is not None and r["Parent"] not in ids_:
            add("없는 Parent", "%s→%s" % (rid, r["Parent"]))
        if r["Kind"] not in ("Command", "Submenu", "Separator", "Section"):
            add("모르는 Kind", "%s:%s" % (rid, r["Kind"]))
        if r["Kind"] == "Command" and not r["CommandId"]:
            add("Command 행에 CommandId 없음", rid)
        if not isinstance(m, dict):
            add("Mobile 값이 ShellMenuMobileRow 가 아님", rid)
            m = {}
        for c in (r["CommandId"], m.get("CommandId")):
            if c is not None and c not in cmds:
                add("ShellCommandIds 에 없는 명령", "%s→%s" % (rid, c))
        ic = r["Icon"]
        if ic is not None and not isinstance(ic, IconRef) and (isinstance(ic, Unresolved) or ic not in keys):
            add("생성 키 집합에 없는 아이콘", "%s→%s" % (rid, ic))
        for k in (r["TitleKey"] if r["Kind"] != "Separator" else None, m.get("TitleKey"), m.get("ReasonKey"), m.get("TabKey")):
            if k and (k not in ko or k not in en):
                add("문구 키가 Localization ko·en 사전 합집합에 없음", "%s→%s(%s 없음)" % (rid, k, "/".join(n for n, d in (("ko", ko), ("en", en)) if k not in d)))
        if r["Kind"] in ("Command", "Submenu") and not r["TitleKey"]:
            add("TitleKey 없음", rid)
    for what, items in found.items():
        uniq = sorted(set(items))
        bad.append("③ %s %d건: %s" % (what, len(uniq), uniq[:5]))
    if not pairs:
        bad.append("③ ShellCommandIds 상수가 없음")
    return len(pairs)


def check_nav(rows, want, bad):
    """④ — 반환: 순서·값이 같은 행 수."""
    got = derive_nav(rows)
    same = sum(1 for a, b in zip(got, want) if a == b)
    if len(got) != len(want) or same != len(want):
        diffs = []
        for k, (a, b) in enumerate(zip(got, want)):
            if a != b:
                diffs.append("%d:%s/%s %s" % (k, a["id"], b["id"], [f for f in NAV_FIELDS if a.get(f) != b.get(f)]))
        bad.append("④ 모바일 목록이 기준 NavMenuCatalog 와 다름(파생 %d행·기준 %d행·일치 %d): 빠짐 %s 더함 %s 다름 %s" % (
            len(got), len(want), same, sorted({b["id"] for b in want} - {a["id"] for a in got})[:4],
            sorted({a["id"] for a in got} - {b["id"] for b in want})[:4], diffs[:3]))
    return same


def check_catalog(root, bad):
    """⑤"""
    t = read(root, F_NAV)
    if t is None:
        bad.append("⑤ 파일 없음: " + F_NAV)
        return
    s = strip_cs(t)
    own = re.findall(r"^\s*(?:Tab\()?[GIX]\(\s*[FWVS]\s*,", s, re.M)
    if own:
        bad.append("⑤ NavMenuCatalog 에 자체 행 정의 %d개가 남음(ShellMenuTable 파생이어야 함)" % len(own))
    if "ShellMenuTable" not in s:
        bad.append("⑤ NavMenuCatalog 가 ShellMenuTable 을 참조하지 않음")


def run(root, base_text, quiet=False):
    bad, errors = [], []
    check_views(root, bad)
    aids = base_aids(base_text, bad)
    want = base_nav(base_text, bad)
    rows = parse_table(root, errors)
    bad += errors
    if not rows and not errors:
        bad.append("표 행을 하나도 찾지 못함(판정불가)")
    check_aids(root, rows, aids, bad)
    cmds = check_values(root, rows, bad)
    same = check_nav(rows, want, bad) if want else 0
    check_catalog(root, bad)
    if not aids:
        bad.append("기준 AutomationId 0개(판정불가)")
    if not quiet:
        for b in bad:
            print("FAIL " + b)
        print("menus=%d automation_ids=%d commands=%d nav_parity=%d result=%s" % (len(rows), len(aids), cmds, same, "FAIL" if bad else "PASS"))
    return 1 if bad else 0
