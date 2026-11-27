# 아이콘·명령 레지스트리 정적 판정기 — 아이콘 키 집합·리터럴 등록·기본 명령(ShellCommandIds 상수 전부)·직접 조회 잔존을 확인한다(B06 끝 조건, B07 D3 보강)
# 사용: icon_command_registry_check.py [--root <R>] [--self-test] [--help]
#   ① views/Themes/MenuIcons.axaml 아이콘 키 집합 = 생성 원천(CmdIconKeys.Generated 상수·MenuIconCatalog.Generated 의
#      MenuIconKeyPrefix+ID·ShellIconNames·특수 3)
#   ② src *.axaml·*.cs 의 아이콘 키 리터럴("Pd…Icon…", 주석·x:Key 정의 제외)이 그 집합 안
#   ③ ShellCommandIds 상수 중복 없음·All 에 전부, Framework/ShellDefaultCommands*.cs 전체의 Add(r, ShellCommandIds.<X>, "<키>", <아이콘>, …)
#      로 상수마다 정확히 1번 등록, 둘째 인자(문구 키 리터럴)가 Localization/*.ko.json 합집합과 *.en.json 합집합 양쪽에 있음,
#      셋째 인자(아이콘)가 CmdIconKeys.<정의된 이름>·"Pd…Icon…" 리터럴(①의 키 집합 안)·MenuIconCatalog.(Grid|Gantt|Form)IconKey 중 하나.
#      명령 수는 ShellCommandIds 상수를 파싱한 개수다(개수 고정 비교 없음)
#   ④ WindowIcon.FindGeometry·MenuIconBitmap 이 UiIconGeometry.Find 경유(TryFindResource(key 직접 조회 잔존 금지)
# 출력: FAIL <사유> 줄들, 끝 줄 'icons=<n> commands=<n> literals=<n> result=PASS|FAIL'
# 종료코드: 0=PASS, 1=FAIL(파일 부재 포함), 2=인자 오류
import glob
import json
import os
import re
import sys

from app_context_check import strip_cs, strip_xml  # 같은 폴더의 B04 판정기 — 주석 제거 규칙을 한 곳에 둔다

NAV = "src/xkxk.frameworks.ui.data/Navigation/"
SH = "src/xkxk.frameworks.ui.shell/"
VW = "src/xkxk.frameworks.ui.views/"
F_AXAML = VW + "Themes/MenuIcons.axaml"
F_CMDKEYS = NAV + "CmdIconKeys.Generated.cs"
F_CATALOG = NAV + "MenuIconCatalog.Generated.cs"
F_IDS = SH + "Framework/ShellCommandIds.cs"
F_DEFS = SH + "Framework/ShellDefaultCommands.cs"
DEFS_GLOB = SH + "Framework/ShellDefaultCommands*.cs"  # 본 파일 + partial(ShellDefaultCommands.Menu.cs 등) 전체
DICTS = ('Localization/*.ko.json', 'Localization/*.en.json')  # 문구 키 사전 = 언어별 합집합(menu.sys.* 는 system 사전에 있다)
F_KO = SH + "Localization/shell.ko.json"
F_EN = SH + "Localization/shell.en.json"
# Add(r, ShellCommandIds.<X>, <문구 키>, <아이콘>, … — 둘째·셋째 인자를 그대로 잡는다
REG = re.compile(r"\bAdd\(\s*\w+\s*,\s*ShellCommandIds\.(\w+)\s*,\s*([^,]*?)\s*,\s*([^,]*?)\s*,")
F_WINICON = VW + "WindowIcon.cs"
F_BITMAP = SH + "Platform/MenuIconBitmap.cs"
USAGE = "사용: icon_command_registry_check.py [--root <R>] [--self-test] [--help]"
TESTBED_OWNED = ("DemoOpen", "DemoOpenSdi", "BenchOpen")  # 시험대 소유 명령 — ShellDefaultCommands*.cs 에 Add 가 없어야 PASS
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


def dict_union(root, bad):
    """(ko 키 합집합, en 키 합집합) — DICTS 의 언어별 사전 전부."""
    out = []
    for pat in DICTS:
        keys, files = set(), sorted(glob.glob(os.path.join(root, SH, pat)))
        if not files:
            bad.append("사전 없음: " + SH + pat)
        for p in files:
            try:
                with open(p, encoding="utf-8-sig") as f:
                    keys |= set(json.load(f))
            except ValueError:
                bad.append("사전 해석 실패: " + os.path.relpath(p, root))
        out.append(keys)
    return out


def icon_arg_ok(expr, keys, ck):
    """아이콘 인자 허용 식 3종인가."""
    m = re.fullmatch(r"CmdIconKeys\.(\w+)", expr)
    if m:
        return m.group(1) not in ("KeyPrefix", "Count") and bool(re.search(r'public const string %s = "PdCmdIcon' % m.group(1), ck))
    m = re.fullmatch(r'"(Pd\w*Icon\w*)"', expr)
    if m:
        return m.group(1) in keys
    return bool(re.fullmatch(r"MenuIconCatalog\.(?:Grid|Gantt|Form)IconKey", expr))


def check_commands(root, keys, bad):
    """ShellCommandIds 상수 수 — 중복·미등록·중복 등록·문구 키·아이콘 식 위반은 bad."""
    ids = read(root, F_IDS)
    rels = sorted(os.path.relpath(p, root).replace(os.sep, "/") for p in glob.glob(os.path.join(root, DEFS_GLOB)))
    for rel, t in ((F_IDS, ids), (F_CMDKEYS, read(root, F_CMDKEYS))):
        if t is None:
            bad.append("파일 없음: " + rel)
    if not rels:
        bad.append("파일 없음: " + DEFS_GLOB)
    ko, en = dict_union(root, bad)
    if ids is None or not rels or not ko or not en:
        return 0, 0
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
    dbody = "".join(strip_cs(read(root, rel) or "") for rel in rels)
    regs = REG.findall(dbody)
    reg_names = [n for n, _, _ in regs]
    owned = [n for n in TESTBED_OWNED if n in names]  # 모의 트리에 상수가 없으면 이 규칙은 건너뛴다
    shell_reg = [n for n in owned if n in reg_names]
    if shell_reg:
        bad.append("시험대 이관 명령을 셸이 등록: %s" % shell_reg)
    miss_reg = [n for n in names if n not in reg_names and n not in owned]
    if miss_reg:
        bad.append("ShellDefaultCommands*.cs 에 Add(r, ShellCommandIds.<X>, …) 로 등록되지 않은 명령 %d개: %s" % (len(miss_reg), miss_reg[:4]))
    dup_reg = sorted({n for n in reg_names if reg_names.count(n) > 1})
    if dup_reg:
        bad.append("같은 명령을 두 번 이상 등록: %s" % dup_reg[:4])
    ghost = sorted({n for n in reg_names if n not in names})
    if ghost:
        bad.append("ShellCommandIds 에 없는 상수를 등록: %s" % ghost[:4])
    ck = read(root, F_CMDKEYS) or ""
    miss_t, bad_t, bad_i = set(), set(), set()
    for n, title, icon in regs:
        m = re.fullmatch(r'"([^"]+)"', title)
        if not m:
            bad_t.add("%s:%s" % (n, title))
        elif m.group(1) not in ko or m.group(1) not in en:
            miss_t.add("%s:%s" % (n, m.group(1)))
        if not icon_arg_ok(icon, keys, ck):
            bad_i.add("%s:%s" % (n, icon or "(없음)"))
    if bad_t:
        bad.append("문구 키 인자가 문자열 리터럴이 아님: %s" % sorted(bad_t)[:4])
    if miss_t:
        bad.append("문구 키가 Localization ko·en 사전 합집합에 없음: %s" % sorted(miss_t)[:4])
    if bad_i:
        bad.append("아이콘 인자가 허용 식(CmdIconKeys.<정의된 이름>·키 집합 안 \"Pd…Icon…\"·MenuIconCatalog.(Grid|Gantt|Form)IconKey)이 아님: %s"
                   % sorted(bad_i)[:4])
    return len(names) - len(owned), len(names)


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
    cmds, consts = check_commands(root, keys, bad)
    check_direct(root, bad)
    if keys and not cmds:
        bad.append("기본 명령이 0개")
    if not quiet:
        for b in bad:
            print("FAIL " + b)
        print("icons=%d commands=%d/%d literals=%d result=%s" % (len(keys), cmds, consts, lits, "FAIL" if bad else "PASS"))
    return 1 if bad else 0


def self_test():
    """자기시험 본문은 같은 폴더 icon_command_registry_selftest.py(모의 트리·사례 표)에 있다."""
    from icon_command_registry_selftest import self_test as run_cases  # 같은 폴더 보조 모듈(300줄 상한 분할)
    return run_cases(sys.modules[__name__])


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
