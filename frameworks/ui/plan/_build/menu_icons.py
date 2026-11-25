# 메뉴 아이콘 생성기 — menu_icon_data.py(조합)·icon_glyphs.py(그림)로 메뉴 ID 별 SVG·Avalonia StreamGeometry·C# 대응표·라이선스 행을 만든다
# 명령 아이콘(CMD, 그림 icon_glyphs_cmd.py)은 StreamGeometry PdCmdIcon<이름> 과 상수 CmdIconKeys.Generated.cs 만 만든다(SVG 없음)
# 실행: python3 -B plan/_build/menu_icons.py [--check | --self-test | --sheet <png 경로>]   (인자 없음 = 생성)
#   --check     누락·중복·없는 이름·폐기 번호·경로 문법·생성물 최신 여부를 보고 문제가 있으면 rc=1
#   --self-test 검사기 결함 주입(누락·중복·없는 이름·폐기 번호·오목 도려내기)과 차집합·래스터·결정성 시험
#   --sheet     16·24·32 px × 밝음·어두움 접촉 시트 PNG(+ 2배 확대본 <이름>_x2.png). 경로는 세션 임시 폴더를 준다
import argparse, os, re, sys
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import icon_glyphs as G
import icon_glyphs_cmd as GC     # 불러오는 순간 명령 글리프·표지가 G 에 합쳐진다
import menu_icon_data as D
from menu_data import MENU_ID, RETIRED_MENU_IDS, SCREENS, REF_SCREENS

ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
SVG_DIR = os.path.join(ROOT, "assets", "icons", "menu")
AXAML = os.path.join(ROOT, "src", "xkxk.frameworks.ui.views", "Themes", "MenuIcons.axaml")
CS = os.path.join(ROOT, "src", "xkxk.frameworks.ui.data", "Navigation", "MenuIconCatalog.Generated.cs")
CMD_CS = os.path.join(ROOT, "src", "xkxk.frameworks.ui.data", "Navigation", "CmdIconKeys.Generated.cs")
LIC = os.path.join(ROOT, "assets", "_LICENSES.csv")
LIC_PREFIX = "icons/menu/"
CORNERS = "M0,0L0.01,0L0,0.01ZM24,24L23.99,24L24,23.99Z"     # 두 모서리 점 — Stretch=Uniform 이 격자 전체 기준으로 맞추게 한다
_N = r"-?\d+(?:\.\d+)?"
PATH_RE = re.compile(rf"^(?:M{_N},{_N}(?:L{_N},{_N})+Z)+$")
SCREEN_NAME = {s[0]: s[2] for s in SCREENS + REF_SCREENS}
SCREEN_OF = {m: s for s, m in MENU_ID.items()}


def validate(icons, menu_ids, retired):
    """조합 데이터 검사 → 오류 문장 목록."""
    errs = []
    ids = set(menu_ids)
    errs += [f"누락: 메뉴 ID {m} 에 아이콘 조합이 없다" for m in sorted(ids - set(icons))]
    errs += [f"초과: 메뉴 ID {m} 는 menu_data.py 에 없다" for m in sorted(set(icons) - ids)]
    errs += [f"폐기 번호: {m}" for m in sorted(set(icons) & set(retired))]
    seen = {}
    for m, (g, b) in sorted(icons.items()):
        if g not in G.GLYPHS:
            errs.append(f"없는 글리프: {m} → {g}")
        if b is not None and b not in G.BADGES:
            errs.append(f"없는 표지: {m} → {b}")
        if (g, b) in seen:
            errs.append(f"중복 조합: {m} 와 {seen[(g, b)]} 가 ({g}, {b})")
        seen.setdefault((g, b), m)
    return errs


def contours_of(spec):
    g, b = spec
    return G.build(None, b, badge_only_k=10.0) if g == "@badge" else G.build(g, b)


def all_paths():
    menu = {m: G.to_path(contours_of(s)) for m, s in sorted(D.ICONS.items())}
    shell = {n: G.to_path(contours_of(s)) for n, s in D.SHELL.items()}
    status = {n: G.to_path(contours_of(s)) for n, s in D.STATUS.items()}
    cmd = {n: G.to_path(contours_of(s)) for n, s in D.CMD.items()}
    return menu, shell, status, cmd


def cmd_errors():
    taken = {**{s: f"메뉴 {m}" for m, s in D.ICONS.items()}, **{s: f"셸 {n}" for n, s in D.SHELL.items()}}
    return GC.validate_cmd(D.CMD, taken)


def _esc(s):
    return s.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;").replace("--", "-")


def svg_text(mid, path):
    scr = SCREEN_OF.get(mid, "")
    return (f"<!-- 메뉴 아이콘 m{mid} {scr} {_esc(SCREEN_NAME.get(scr, ''))} — plan/_build/menu_icons.py 생성물, 손으로 고치지 않는다 -->\n"
            f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" width="24" height="24">'
            f'<path fill="currentColor" d="{path}"/></svg>\n')


def axaml_text(menu, shell, status, cmd):
    out = ["<!-- 메뉴·셸·명령 아이콘 StreamGeometry — plan/_build/menu_icons.py 생성물(손으로 고치지 말고 menu_icon_data.py 를 고친 뒤 다시 만든다).",
           "     24 격자 단색, 키 PdMenuIcon<메뉴ID>·PdShellIcon<이름>·PdIcon<이름>·PdCmdIcon<이름>(상수 CmdIconKeys). 두 모서리 점으로 Uniform 배율을 격자 기준으로 맞춘다 -->",
           '<ResourceDictionary xmlns="https://github.com/avaloniaui"',
           '                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">']
    out += [f'  <StreamGeometry x:Key="PdMenuIcon{m}">F1 {CORNERS}{p}</StreamGeometry>' for m, p in menu.items()]
    out += [f'  <StreamGeometry x:Key="PdShellIcon{n}">F1 {CORNERS}{p}</StreamGeometry>' for n, p in shell.items()]
    out += [f'  <StreamGeometry x:Key="PdIcon{n}">F1 {CORNERS}{p}</StreamGeometry>' for n, p in status.items()]
    out += [f'  <StreamGeometry x:Key="{GC.KEY_PREFIX}{n}">F1 {CORNERS}{p}</StreamGeometry>' for n, p in cmd.items()]
    return "\n".join(out + ["</ResourceDictionary>", ""])


def cs_text():
    scr = [f'        ["{s}"] = "{m}",' for s, m in sorted(MENU_ID.items(), key=lambda kv: (kv[1][0].isalpha(), kv[1]))]
    keys = [f'        ["{m}"] = MenuIconKeyPrefix + "{m}",' for m in sorted(D.ICONS, key=lambda m: (m[0].isalpha(), m))]
    shell = ", ".join(f'"{n}"' for n in D.SHELL)
    return "\n".join([
        "// 화면 ID → 메뉴 ID·아이콘 키 대응표 — plan/_build/menu_icons.py 생성물(손으로 고치지 말고 menu_data.py·menu_icon_data.py 를 고친 뒤 다시 만든다)",
        "using System;", "using System.Collections.Generic;", "", "namespace xkxk.frameworks.ui.data.Navigation;", "",
        "/// <summary>메뉴 아이콘 카탈로그의 생성 부분. 아이콘 키는 xkxk.frameworks.ui.views/Themes/MenuIcons.axaml 의 StreamGeometry 키다(UI 비의존 문자열만 둔다).</summary>",
        "public static partial class MenuIconCatalog", "{",
        '    public const string MenuIconKeyPrefix = "PdMenuIcon";', '    public const string ShellIconKeyPrefix = "PdShellIcon";',
        '    public const string BusyStaticKey = "PdIconBusyStatic";', '    public const string BadgeSlowKey = "PdIconBadgeSlow";',
        '    public const string BadgeErrorKey = "PdIconBadgeError";', f"    public const int GeneratedMenuIconCount = {len(D.ICONS)};", "",
        "    /// <summary>화면 ID → 메뉴 4자리 ID(menu_data.py MENU_ID 사본).</summary>",
        "    public static IReadOnlyDictionary<string, string> MenuIdByScreenId { get; } = new Dictionary<string, string>(StringComparer.Ordinal)",
        "    {", *scr, "    };", "",
        "    /// <summary>메뉴 ID → 아이콘 리소스 키.</summary>",
        "    public static IReadOnlyDictionary<string, string> IconKeyByMenuId { get; } = new Dictionary<string, string>(StringComparer.Ordinal)",
        "    {", *keys, "    };", "",
        "    /// <summary>셸 창 아이콘 이름(키 = ShellIconKeyPrefix + 이름).</summary>",
        f"    public static IReadOnlyList<string> ShellIconNames {{ get; }} = new[] {{ {shell} }};", "}", ""])


def license_rows():
    src = f"AI 생성({D.GEN_TOOL} · {D.GEN_MODEL} · {D.GEN_DATE})"
    return [f"{LIC_PREFIX}m{m}.svg,{src},프로젝트 자체 제작(제한 없음),{D.GEN_DATE},{D.GEN_MODEL}({D.GEN_TOOL}),메뉴·창 아이콘 {SCREEN_OF.get(m, '')}"
            for m in sorted(D.ICONS, key=lambda m: (m[0].isalpha(), m))]


def merge_rows(text, prefix, rows):
    """prefix 로 시작하는 행을 rows 로 바꾼다. 처음 나온 자리에 넣고, 없으면 끝에 붙인다(다른 생성기 행 순서와 무관하게 멱등)."""
    lines = text.splitlines()
    pos = next((i for i, ln in enumerate(lines) if ln.startswith(prefix)), None)
    keep = [ln for ln in lines if not ln.startswith(prefix)]
    if pos is None:
        while keep and not keep[-1].strip():
            keep.pop()
        pos = len(keep)
    else:
        pos = sum(1 for ln in lines[:pos] if not ln.startswith(prefix))
    return "\n".join(keep[:pos] + rows + keep[pos:]) + "\n"


def expected():
    menu, shell, status, cmd = all_paths()
    files = {os.path.join(SVG_DIR, f"m{m}.svg"): svg_text(m, p) for m, p in menu.items()}
    files[AXAML] = axaml_text(menu, shell, status, cmd)
    files[CS] = cs_text()
    files[CMD_CS] = GC.cmdkeys_text(D.CMD)
    return files, menu, shell, status, cmd


def _read(p):
    try:
        with open(p, encoding="utf-8", newline="") as f:
            return f.read()
    except FileNotFoundError:
        return None


def _write(p, text):
    os.makedirs(os.path.dirname(p), exist_ok=True)
    with open(p, "w", encoding="utf-8", newline="\n") as f:
        f.write(text)


def stale_svgs(files):
    if not os.path.isdir(SVG_DIR):
        return []
    return sorted(os.path.join(SVG_DIR, n) for n in os.listdir(SVG_DIR) if n.endswith(".svg") and os.path.join(SVG_DIR, n) not in files)


def generate():
    errs = validate(D.ICONS, MENU_ID.values(), RETIRED_MENU_IDS) + cmd_errors()
    if errs:
        print("\n".join(errs)); return 1
    files, *_ = expected()
    changed = 0
    for p, t in files.items():
        if _read(p) != t:
            _write(p, t); changed += 1
    for p in stale_svgs(files):
        os.remove(p); changed += 1
    lic = _read(LIC) or "file,source,license,date,author,purpose\n"
    new = merge_rows(lic, LIC_PREFIX, license_rows())
    if new != lic:
        _write(LIC, new); changed += 1
    print(f"생성: 메뉴 아이콘 {len(D.ICONS)}개 · 셸 {len(D.SHELL)} · 상태 {len(D.STATUS)} · 명령 {len(D.CMD)} · 바뀐 파일 {changed}개")
    return 0


def check():
    errs = validate(D.ICONS, MENU_ID.values(), RETIRED_MENU_IDS) + cmd_errors()
    files, menu, shell, status, cmd = expected()
    for k, p in list(menu.items()) + list(shell.items()) + list(status.items()) + list(cmd.items()):
        if not PATH_RE.match(p):
            errs.append(f"경로 문법 오류: {k}")
    errs += [f"생성물 낡음(다시 생성 필요): {os.path.relpath(p, ROOT)}" for p, t in files.items() if _read(p) != t]
    errs += [f"남은 옛 SVG: {os.path.relpath(p, ROOT)}" for p in stale_svgs(files)]
    lic = [ln for ln in (_read(LIC) or "").splitlines() if ln.startswith(LIC_PREFIX)]
    if lic != license_rows():
        errs.append("라이선스 행 불일치: assets/_LICENSES.csv 의 icons/menu/ 행을 다시 생성해야 한다")
    used = {g for g, _ in D.ICONS.values()}
    print(f"메뉴 ID {len(set(MENU_ID.values()))} · 아이콘 {len(D.ICONS)} · 누락 {sum(e.startswith('누락') for e in errs)} · "
          f"중복 {sum(e.startswith('중복') for e in errs)} · 글리프 {len(used)}/{len(G.GLYPHS)} · 표지 {len(G.BADGES)} · 명령 {len(D.CMD)} · 오류 {len(errs)}")
    if errs:
        print("\n".join(errs))
    return 1 if errs else 0


def raster(contours, px, ss=4):
    from PIL import Image, ImageDraw
    big = Image.new("L", (px * ss, px * ss), 0)
    d, k = ImageDraw.Draw(big), px * ss / G.GRID
    for c in contours:
        d.polygon([(x * k, y * k) for x, y in c], fill=255)
    return big.resize((px, px), Image.LANCZOS)


def items_for_sheet():
    return ([(m, contours_of(s)) for m, s in sorted(D.ICONS.items(), key=lambda kv: (kv[0][0].isalpha(), kv[0]))]
            + [(n[:8], contours_of(s)) for n, s in list(D.SHELL.items()) + list(D.STATUS.items())]
            + [(n, contours_of(s)) for n, s in D.CMD.items()])


def sheet(path):
    from PIL import Image, ImageDraw
    items, cols, cw, ch = items_for_sheet(), 10, 180, 52
    rows = (len(items) + cols - 1) // cols
    img = Image.new("RGB", (cols * cw, rows * ch), (255, 255, 255))
    d = ImageDraw.Draw(img)
    for i, (label, cs) in enumerate(items):
        x0, y0 = (i % cols) * cw, (i // cols) * ch
        d.rectangle([x0 + 90, y0, x0 + cw - 1, y0 + ch - 1], fill=(30, 30, 30))
        d.text((x0 + 4, y0 + 1), label, fill=(120, 0, 0))
        for half, fg in ((0, (31, 41, 55)), (90, (229, 231, 235))):
            for px, ox in ((16, 4), (24, 24), (32, 52)):
                m = raster(cs, px)
                img.paste(Image.new("RGB", (px, px), fg), (x0 + half + ox, y0 + 16), m)
        d.line([x0, y0 + ch - 1, x0 + cw, y0 + ch - 1], fill=(200, 200, 200))
    os.makedirs(os.path.dirname(os.path.abspath(path)), exist_ok=True)
    img.save(path)
    stem, ext = os.path.splitext(path)
    img.resize((img.width * 2, img.height * 2), Image.NEAREST).save(stem + "_x2" + ext)
    print(f"접촉 시트: {path} ({len(items)}개)")
    return 0


def self_test():
    fails = []
    ok = lambda cond, name: None if cond else fails.append(name)
    good = dict(D.ICONS)
    ids = list(MENU_ID.values())
    ok(not validate(good, ids, RETIRED_MENU_IDS), "정상 데이터 통과")
    bad = dict(good); bad.pop(ids[0]); ok(any(e.startswith("누락") for e in validate(bad, ids, set())), "누락 검출")
    bad = dict(good); bad[ids[1]] = bad[ids[2]]; ok(any(e.startswith("중복") for e in validate(bad, ids, set())), "중복 검출")
    bad = dict(good); bad[ids[0]] = ("없는글리프", None); ok(any("없는 글리프" in e for e in validate(bad, ids, set())), "없는 글리프 검출")
    bad = dict(good); bad[ids[0]] = ("home", "없는표지"); ok(any("없는 표지" in e for e in validate(bad, ids, set())), "없는 표지 검출")
    ok(any(e.startswith("폐기") for e in validate(good, ids, {ids[0]})), "폐기 번호 검출")
    ok(any(e.startswith("초과") for e in validate(dict(good, zz9=("home", "plus")), ids, set())), "초과 검출")
    try:
        b = G.Builder(); b.f(G.R(0, 0, 24, 24)); b.x(G.P((2, 2), (20, 2), (11, 8), (20, 20), (2, 20))); fails.append("오목 도려내기 거부")
    except ValueError:
        pass
    b = G.Builder(); b.f(G.R(0, 0, 24, 24)); b.x(G.C(12, 12, 6))
    m = raster(b.c, 48, 4)
    cov = sum(m.tobytes()) / 255 / (48 * 48)
    ok(m.getpixel((24, 24)) < 10 and m.getpixel((2, 2)) > 245, "차집합 구멍·바깥")
    ok(abs(cov - (1 - 3.14159 * 36 / 576)) < 0.02, f"차집합 면적({cov:.3f})")
    ok(all(G.area(c) > 0 for c in b.c), "윤곽 방향 일치")
    p1, p2 = all_paths(), all_paths()
    ok(p1 == p2, "결정성")
    taken, n0, n1 = {D.ICONS["1001"]: "메뉴 1001"}, *list(D.CMD)[:2]
    ok(not cmd_errors(), "명령 정상 데이터 통과(교차 중복 0)")
    ok(any(e.startswith("중복") for e in GC.validate_cmd(dict(D.CMD, **{n1: D.CMD[n0]}), {})), "명령 중복 검출")
    ok(any(e.startswith("중복") for e in GC.validate_cmd({"Zz": D.ICONS["1001"]}, taken)), "명령·메뉴 교차 중복 검출")
    ok(any("없는 글리프" in e for e in GC.validate_cmd({"Zz": ("없는글리프", None)}, {})), "명령 없는 글리프 검출")
    ok(any("이름 형식" in e for e in GC.validate_cmd({"Count": ("close", "plus")}, {})), "명령 예약 이름 검출")
    ok(all(f'{n} = "{GC.KEY_PREFIX}{n}";' in GC.cmdkeys_text(D.CMD) for n in D.CMD), "CmdIconKeys 상수 전부")
    for k, s in list(D.ICONS.items()) + list(D.SHELL.items()) + list(D.STATUS.items()) + list(D.CMD.items()):
        cs = contours_of(s)
        c = sum(raster(cs, 32).tobytes()) / 255 / 1024
        ok(0.05 < c < 0.85, f"{k} 채움 비율 {c:.2f}")
        ok(bool(PATH_RE.match(G.to_path(cs))), f"{k} 경로 문법")
    ok(merge_rows("h\na,1\nicons/menu/x\nb,2\n", LIC_PREFIX, ["icons/menu/y"]) == "h\na,1\nicons/menu/y\nb,2\n", "라이선스 행 제자리 교체")
    ok(merge_rows("h\na,1\n", LIC_PREFIX, ["icons/menu/y"]) == "h\na,1\nicons/menu/y\n", "라이선스 행 끝에 붙임")
    total = 20 + 2 * (len(D.ICONS) + len(D.SHELL) + len(D.STATUS) + len(D.CMD))
    print(f"자체 시험: {total - len(fails)}/{total} 통과")
    if fails:
        print("\n".join("실패: " + f for f in fails))
    return 1 if fails else 0


def main():
    ap = argparse.ArgumentParser(description="메뉴 아이콘 생성기")
    g = ap.add_mutually_exclusive_group()
    g.add_argument("--check", action="store_true")
    g.add_argument("--self-test", action="store_true")
    g.add_argument("--sheet", metavar="PNG")
    a = ap.parse_args()
    if a.check:
        return check()
    if a.self_test:
        return self_test()
    if a.sheet:
        return sheet(a.sheet)
    return generate()


if __name__ == "__main__":
    sys.exit(main())
