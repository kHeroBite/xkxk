# 아이콘·명령 레지스트리 정적 판정기 — 아이콘 키 집합·리터럴 등록·기본 명령 23개·직접 조회 잔존을 확인한다(B06 끝 조건)
# 사용: icon_command_registry_check.py [--root <R>] [--self-test] [--help]
#   ① views/Themes/MenuIcons.axaml 아이콘 키 집합 = 생성 원천(CmdIconKeys.Generated 상수·MenuIconCatalog.Generated 의
#      MenuIconKeyPrefix+ID·ShellIconNames·특수 3)
#   ② src *.axaml·*.cs 의 아이콘 키 리터럴("Pd…Icon…", 주석·x:Key 정의 제외)이 그 집합 안
#   ③ ShellCommandIds 상수 중복 없음·All 에 전부·ShellDefaultCommands 에 전부 등록·각 TitleKey 가 shell.ko/en.json 에 있음·
#      각 아이콘이 CmdIconKeys 에 정의됨
#   ④ WindowIcon.FindGeometry·MenuIconBitmap 이 UiIconGeometry.Find 경유(TryFindResource(key 직접 조회 잔존 금지)
# 출력: FAIL <사유> 줄들, 끝 줄 'icons=<n> commands=<n> literals=<n> result=PASS|FAIL'
# 종료코드: 0=PASS, 1=FAIL(파일 부재 포함), 2=인자 오류
import json
import os
import re
import shutil
import sys
import tempfile

from app_context_check import strip_cs, strip_xml  # 같은 폴더의 B04 판정기 — 주석 제거 규칙을 한 곳에 둔다

NAV = "src/xkxk.frameworks.ui.data/Navigation/"
SH = "src/xkxk.frameworks.ui.shell/"
VW = "src/xkxk.frameworks.ui.views/"
F_AXAML = VW + "Themes/MenuIcons.axaml"
F_CMDKEYS = NAV + "CmdIconKeys.Generated.cs"
F_CATALOG = NAV + "MenuIconCatalog.Generated.cs"
F_IDS = SH + "Framework/ShellCommandIds.cs"
F_DEFS = SH + "Framework/ShellDefaultCommands.cs"
F_KO = SH + "Localization/shell.ko.json"
F_EN = SH + "Localization/shell.en.json"
F_WINICON = VW + "WindowIcon.cs"
F_BITMAP = SH + "Platform/MenuIconBitmap.cs"
USAGE = "사용: icon_command_registry_check.py [--root <R>] [--self-test] [--help]"
KEY_LIT = re.compile(r'"(Pd(?:CmdIcon|MenuIcon|ShellIcon)\w+|PdIcon(?:BusyStatic|BadgeSlow|BadgeError))"')
XKEY = re.compile(r'x:Key="[^"]*"')


def read(root, rel):
    p = os.path.join(root, rel)
    if not os.path.isfile(p):
        return None
    with open(p, encoding="utf-8-sig", errors="replace") as f:
        return f.read()


def src_files(root):
    """src 아래 *.cs·*.axaml 의 (상대경로, 원문) — bin·obj 제외."""
    out = []
    for dp, dns, fns in os.walk(os.path.join(root, "src")):
        dns[:] = sorted(d for d in dns if d not in ("bin", "obj"))
        for fn in sorted(fns):
            if fn.endswith((".cs", ".axaml")):
                p = os.path.join(dp, fn)
                with open(p, encoding="utf-8-sig", errors="replace") as f:
                    out.append((os.path.relpath(p, root).replace(os.sep, "/"), f.read()))
    return out


def check_icons(root, bad):
    """(아이콘 키 집합, 원천 집합) — 불일치·부재는 bad 에 사유를 더한다."""
    ax, ck, cat = read(root, F_AXAML), read(root, F_CMDKEYS), read(root, F_CATALOG)
    for rel, t in ((F_AXAML, ax), (F_CMDKEYS, ck), (F_CATALOG, cat)):
        if t is None:
            bad.append("파일 없음: " + rel)
    if None in (ax, ck, cat):
        return set(), set()
    keys = set(re.findall(r'x:Key="(Pd(?:CmdIcon|MenuIcon|ShellIcon)\w*|PdIcon(?:BusyStatic|BadgeSlow|BadgeError))"', ax))
    cmd = set(re.findall(r'public const string \w+ = "(PdCmdIcon\w+)";', ck))
    menu = {"PdMenuIcon" + n for n in re.findall(r'MenuIconKeyPrefix \+ "(\w+)"', cat)}
    sm = re.search(r"ShellIconNames\s*\{\s*get;\s*\}\s*=\s*new\[\]\s*\{([^}]*)\}", cat)
    shell = {"PdShellIcon" + n for n in re.findall(r'"(\w+)"', sm.group(1))} if sm else set()
    if not sm:
        bad.append("ShellIconNames 목록을 찾지 못함")
    special = set(re.findall(r'(?:BusyStaticKey|BadgeSlowKey|BadgeErrorKey)\s*=\s*"(\w+)"', cat))
    prov = cmd | menu | shell | special
    if not keys:
        bad.append("MenuIcons.axaml 에 아이콘 키가 없음")
    if prov != keys:
        bad.append("아이콘 키 집합 불일치: 원천만 %s / axaml 만 %s" % (sorted(prov - keys)[:4], sorted(keys - prov)[:4]))
    return keys, prov


def check_literals(root, keys, bad):
    """아이콘 키 리터럴 수 — 집합 밖 리터럴은 bad."""
    n, unknown = 0, set()
    for rel, t in src_files(root):
        body = strip_xml(t) if rel.endswith(".axaml") else strip_cs(t)
        for lit in KEY_LIT.findall(XKEY.sub("", body)):
            n += 1
            if lit not in keys:
                unknown.add("%s:%s" % (os.path.basename(rel), lit))
    if unknown:
        bad.append("레지스트리에 없는 아이콘 키 리터럴 %d종: %s" % (len(unknown), sorted(unknown)[:4]))
    if n == 0:
        bad.append("아이콘 키 리터럴을 하나도 찾지 못함(판정불가)")
    return n


def check_commands(root, bad):
    """ShellCommandIds 상수 수 — 중복·미등록·문구 키·아이콘 누락은 bad."""
    ids, defs = read(root, F_IDS), read(root, F_DEFS)
    ko, en = read(root, F_KO), read(root, F_EN)
    for rel, t in ((F_IDS, ids), (F_DEFS, defs), (F_KO, ko), (F_EN, en), (F_CMDKEYS, read(root, F_CMDKEYS))):
        if t is None:
            bad.append("파일 없음: " + rel)
    if None in (ids, defs, ko, en):
        return 0
    pairs = re.findall(r'public const string (\w+) = "([\w.]+)";', strip_cs(ids))
    names, vals = [p[0] for p in pairs], [p[1] for p in pairs]
    if not pairs:
        bad.append("ShellCommandIds 상수가 없음")
    for what, seq in (("상수 이름", names), ("명령 ID 값", vals)):
        dup = sorted({x for x in seq if seq.count(x) > 1})
        if dup:
            bad.append("ShellCommandIds %s 중복: %s" % (what, dup[:4]))
    allblk = ids.split("All", 1)[1] if "All" in ids else ""
    miss_all = [n for n in names if not re.search(r"\b%s\b" % n, allblk)]
    if miss_all:
        bad.append("ShellCommandIds.All 에 빠진 상수: %s" % miss_all[:4])
    dbody = strip_cs(defs)
    miss_reg = [n for n in names if not re.search(r"ShellCommandIds\.%s\b" % n, dbody)]
    if miss_reg:
        bad.append("ShellDefaultCommands 에 등록되지 않은 명령: %s" % miss_reg[:4])
    try:
        kod, end = json.loads(ko), json.loads(en)
    except ValueError:
        bad.append("shell.ko/en.json 해석 실패")
        kod, end = {}, {}
    miss_t = sorted({t for t in re.findall(r'"(menu\.[\w.]+)"', dbody) if t not in kod or t not in end})
    if miss_t:
        bad.append("문구 키가 ko·en 사전에 없음: %s" % miss_t[:4])
    ck = read(root, F_CMDKEYS) or ""
    miss_i = sorted({i for i in re.findall(r"CmdIconKeys\.(\w+)", dbody)
                     if i != "All" and not re.search(r"public const string %s = " % i, ck)})
    if miss_i:
        bad.append("CmdIconKeys 에 없는 아이콘: %s" % miss_i[:4])
    return len(names)


def check_direct(root, bad):
    """아이콘 직접 조회 잔존 — WindowIcon.FindGeometry·MenuIconBitmap 은 UiIconGeometry.Find 만 쓴다."""
    wi, mb = read(root, F_WINICON), read(root, F_BITMAP)
    for rel, t in ((F_WINICON, wi), (F_BITMAP, mb)):
        if t is None:
            bad.append("파일 없음: " + rel)
    if wi is not None:
        fg = re.search(r"Geometry\?\s+FindGeometry\(string\?\s+key\)\s*(=>[^;]*;|\{.*?\n    \})", strip_cs(wi), re.S)
        if not fg:
            bad.append("WindowIcon.FindGeometry 를 찾지 못함")
        elif "UiIconGeometry.Find(" not in fg.group(1) or "TryFindResource" in fg.group(1):
            bad.append("WindowIcon.FindGeometry 가 UiIconGeometry.Find 를 쓰지 않거나 TryFindResource 직접 조회가 남음")
    if mb is not None:
        b = strip_cs(mb)
        if "UiIconGeometry.Find(" not in b or re.search(r"TryFindResource\(\s*key\b", b):
            bad.append("MenuIconBitmap 이 UiIconGeometry.Find 를 쓰지 않거나 TryFindResource(key 직접 조회가 남음")


def run(root, quiet=False):
    bad = []
    keys, _ = check_icons(root, bad)
    lits = check_literals(root, keys, bad) if keys else 0
    cmds = check_commands(root, bad)
    check_direct(root, bad)
    if keys and not cmds:
        bad.append("기본 명령이 0개")
    if not quiet:
        for b in bad:
            print("FAIL " + b)
        print("icons=%d commands=%d literals=%d result=%s" % (len(keys), cmds, lits, "FAIL" if bad else "PASS"))
    return 1 if bad else 0


def build_tree(root, **mut):
    """판정기 시험용 최소 트리. mut 키로 한 곳만 어긋나게 만든다."""
    cmd_keys = ["PdCmdIconExit", "PdCmdIconSave"]
    files = {
        F_AXAML: "".join('<StreamGeometry x:Key="%s">M0,0</StreamGeometry>\n' % k for k in
                         cmd_keys + ["PdMenuIcon1001", "PdShellIconHelp", "PdIconBusyStatic", "PdIconBadgeSlow", "PdIconBadgeError"]
                         + mut.get("extra_axaml", [])),
        F_CMDKEYS: "".join('public const string K%d = "%s";\n' % (i, k) for i, k in enumerate(cmd_keys)),
        F_CATALOG: 'public const string BusyStaticKey = "PdIconBusyStatic";\npublic const string BadgeSlowKey = "PdIconBadgeSlow";\n'
                   'public const string BadgeErrorKey = "PdIconBadgeError";\n["1001"] = MenuIconKeyPrefix + "1001",\n'
                   'public static IReadOnlyList<string> ShellIconNames { get; } = new[] { "Help" };\n',
        F_IDS: 'public const string A = "xkxk.a";\npublic const string B = "%s";\n%sAll = new[] { A, B };\n' % (mut.get("b_value", "xkxk.b"), mut.get("ids_extra", "")),
        F_DEFS: 'Add(ShellCommandIds.A, "menu.t.a", CmdIconKeys.K0);\n%sAdd(ShellCommandIds.B, "%s", CmdIconKeys.%s);\n'
                % (mut.get("defs_extra", ""), mut.get("title_b", "menu.t.b"), mut.get("icon_b", "K1")),
        F_KO: json.dumps({"menu.t.a": "가", "menu.t.b": "나"}),
        F_EN: json.dumps({"menu.t.a": "a", "menu.t.b": mut.get("en_b", "b")}) if "en_missing" not in mut else json.dumps({"menu.t.a": "a"}),
        F_WINICON: mut.get("winicon", "class W {\n    Geometry? FindGeometry(string? key) => UiIconGeometry.Find(this, null, key);\n}\n"),
        F_BITMAP: mut.get("bitmap", "var g = UiIconGeometry.Find(host, theme, key);\n"),
        VW + "Menus/M.axaml": mut.get("axaml_use", '<MenuIcon Key="PdMenuIcon1001"/>\n'),
    }
    if "drop_ids" in mut:
        del files[F_IDS]
    if "drop_ko" in mut:
        del files[F_KO]
    for rel, text in files.items():
        p = os.path.join(root, rel)
        os.makedirs(os.path.dirname(p), exist_ok=True)
        with open(p, "w", encoding="utf-8") as f:
            f.write(text)


def self_test():
    bad = 0
    cases = [  # (이름, 변형, 기대)
        ("tree_consistent_passes", {}, "pass"),
        ("literal_in_comment_ignored", {"axaml_use": '<!-- <MenuIcon Key="PdMenuIcon9999"/> -->\n<MenuIcon Key="PdShellIconHelp"/>\n'}, "pass"),
        ("axaml_key_not_in_sources_fails", {"extra_axaml": ["PdCmdIconGhost"]}, "fail"),
        ("literal_unregistered_fails", {"axaml_use": '<MenuIcon Key="PdMenuIcon9999"/>\n'}, "fail"),
        ("command_id_duplicate_fails", {"b_value": "xkxk.a"}, "fail"),
        ("command_not_registered_fails", {"ids_extra": 'public const string C = "xkxk.c";\n'}, "fail"),
        ("title_key_missing_in_dict_fails", {"title_b": "menu.t.nope"}, "fail"),
        ("title_key_missing_in_en_fails", {"en_missing": 1}, "fail"),
        ("icon_not_in_cmdiconkeys_fails", {"icon_b": "Ghost"}, "fail"),
        ("direct_lookup_in_windowicon_fails", {"winicon": "class W {\n    Geometry? FindGeometry(string? key) => TryFindResource(key, out var o) ? o as Geometry : null;\n}\n"}, "fail"),
        ("direct_lookup_in_bitmap_fails", {"bitmap": "host.TryFindResource(key, theme, out var g);\n"}, "fail"),
        ("commands_file_missing_fails", {"drop_ids": 1}, "fail"),
        ("dictionary_file_missing_fails", {"drop_ko": 1}, "fail"),
    ]
    for name, mut, want in cases:
        tmp = tempfile.mkdtemp()
        try:
            build_tree(tmp, **mut)
            if name == "command_not_registered_fails":  # 상수는 3개인데 기본 명령에는 2개만 등록한다
                p = os.path.join(tmp, F_IDS)
                with open(p, "a", encoding="utf-8") as f:
                    f.write('public const string C = "xkxk.c";\nAll2 = C;\n')
            got = "pass" if run(tmp, quiet=True) == 0 else "fail"
        finally:
            shutil.rmtree(tmp, ignore_errors=True)
        bad += got != want
        print("SELFTEST %s %s expect=%s got=%s" % ("PASS" if got == want else "FAIL", name, want, got))
    tmp = tempfile.mkdtemp()
    try:
        got = "pass" if run(tmp, quiet=True) == 0 else "fail"  # 빈 트리는 통과하면 안 된다
    finally:
        shutil.rmtree(tmp, ignore_errors=True)
    bad += got != "fail"
    print("SELFTEST %s empty_tree_fails expect=fail got=%s" % ("PASS" if got == "fail" else "FAIL", got))
    return 1 if bad else 0


def main(argv):
    root = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", ".."))
    args = argv[1:]
    if args in (["--help"], ["-h"]):
        print(USAGE)
        return 0
    if args == ["--self-test"]:
        return self_test()
    if args and args[0] == "--root" and len(args) == 2:
        return run(os.path.abspath(args[1]))
    if args:
        print(USAGE, file=sys.stderr)
        return 2
    return run(root)


if __name__ == "__main__":
    sys.exit(main(sys.argv))
