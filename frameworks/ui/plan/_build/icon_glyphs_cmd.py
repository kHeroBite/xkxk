# 명령 아이콘(상단 메뉴 실행 항목 PdCmdIcon<이름>) 전용 글리프·표지 — icon_glyphs.py 의 기본 도형으로 새로 그린다(외부 아이콘 세트 path 복사 없음)
# 불러오면 GLYPHS·BADGES 를 icon_glyphs 의 사전에 합친다(이름이 겹치면 ImportError). 조합 데이터는 menu_icon_data.py 의 CMD 다.
# 끝부분에 명령 조합 검사(validate_cmd)와 CmdIconKeys 상수 본문(cmdkeys_text)도 둔다. icon_glyphs.py·menu_icons.py 를 300줄 아래로 지키려고 나눈 파일이다(22 설계 §2.1).
import math
import re
import icon_glyphs as G
from icon_glyphs import A, C, L, P, PIE, R


def _win(b, x, y, w, h, t=1.6, top=3.0):
    """윤곽 창(제목 띠가 두꺼운 테두리). 안쪽 도려내기는 앞서 그린 것도 자르므로 겹칠 때는 _gap 으로 먼저 비운다."""
    b.f(R(x, y, w, h, 1))
    b.x(R(x + t, y + top, w - 2 * t, h - top - t))


def _gap(b, x, y, w, h, g=1.2):
    b.x(R(x - g, y - g, w + 2 * g, h + 2 * g))


def _turn(b, cx, cy, r, w, head):
    """시계 방향 순환 화살표(45°→315° 호 + 끝 화살촉) — 되돌리기·초기화."""
    b.f(A(cx, cy, r, 45, 315, w))
    a = math.radians(315)
    ex, ey = cx + r * math.cos(a), cy + r * math.sin(a)
    dx, dy = -math.sin(a), math.cos(a)
    nx, ny = math.cos(a), math.sin(a)
    b.f(P((ex + nx * head, ey + ny * head), (ex + dx * head * 1.2, ey + dy * head * 1.2), (ex - nx * head, ey - ny * head)))


def _corners(b, k=7.0, w=2.2):
    for x, y, sx, sy in ((2, 2, 1, 1), (22, 2, -1, 1), (2, 22, 1, -1), (22, 22, -1, -1)):
        b.f(R(min(x, x + sx * k), min(y, y + sy * w), k, w), R(min(x, x + sx * w), min(y, y + sy * k), w, k))


def _cascade(b):
    _win(b, 2, 2, 12, 10)
    _gap(b, 6, 7, 12, 10); _win(b, 6, 7, 12, 10)
    _gap(b, 10, 12, 12, 10); _win(b, 10, 12, 12, 10)


def _restore(b):
    _win(b, 8, 3, 13, 12)
    _gap(b, 3, 8, 13, 13); _win(b, 3, 8, 13, 13)


def _monitors(b):
    b.f(R(8, 2, 14, 10, 1)); b.x(R(9.8, 3.8, 10.4, 6.4))
    _gap(b, 2, 8, 14, 10)
    b.f(R(2, 8, 14, 10, 1)); b.x(R(3.8, 9.8, 10.4, 6.4))
    b.f(R(8, 18, 2, 2), R(5, 20, 8, 2))


def _chip(b):
    b.f(R(6, 6, 12, 12, 1.5)); b.x(R(8.3, 8.3, 7.4, 7.4)); b.f(R(10.2, 10.2, 3.6, 3.6))
    for i in range(3):
        o = 7.7 + 3.4 * i
        b.f(R(o, 2.5, 1.6, 3.5), R(o, 18, 1.6, 3.5), R(2.5, o, 3.5, 1.6), R(18, o, 3.5, 1.6))


# 명령 글리프 — 이름: 그리기 함수(b). 격자 24, 선 굵기 약 2. 창을 다루는 명령은 윤곽 창(_win) 조합으로 그린다.
GLYPHS = {
    "cascade": _cascade,
    "tileh": lambda b: (_win(b, 2, 2, 20, 9), _win(b, 2, 13, 20, 9)),
    "tilev": lambda b: (_win(b, 2, 2, 9, 20), _win(b, 13, 2, 9, 20)),
    "tabbed": lambda b: (b.f(R(2, 2, 8, 5, 1), R(11, 3, 7, 4, 1), R(2, 6, 20, 16, 1)), b.x(R(3.8, 7.8, 16.4, 12.4))),
    "snap": lambda b: (b.f(R(2, 3, 20, 18, 1.5)), b.x(R(3.8, 4.8, 7.3, 6.5), R(12.9, 4.8, 7.3, 6.5), R(3.8, 13.1, 7.3, 6.1), R(12.9, 13.1, 7.3, 6.1)),
                       b.f(R(4.8, 5.8, 5.3, 4.5))),
    "minimize": lambda b: (_win(b, 5, 3, 14, 11), b.f(R(5, 17.5, 14, 2.6))),
    "maximize": lambda b: (_corners(b), _win(b, 7.5, 7.5, 9, 9, 1.5, 2.5)),
    "restore": _restore,
    "pin": lambda b: b.f(R(8, 2, 8, 3, 1), P((9, 5), (15, 5), (16, 11), (19, 14), (5, 14), (8, 11)), L(12, 14, 12, 22, 1.8)),
    "rename": lambda b: (b.f(R(2, 7, 13, 10, 1)), b.x(R(3.8, 8.8, 9.4, 6.4)), b.f(R(5.5, 11, 6, 2), R(17.4, 3, 2, 18), R(15, 3, 6.8, 1.8), R(15, 19.2, 6.8, 1.8))),
    "reset": lambda b: _turn(b, 12, 12, 7.5, 2.6, 4.2),
    "next": lambda b: b.f(R(3, 10.8, 12, 2.4), P((13, 5), (21, 12), (13, 19))),
    "prev": lambda b: b.f(R(9, 10.8, 12, 2.4), P((11, 5), (3, 12), (11, 19))),
    "popout": lambda b: (_win(b, 2, 6, 15, 16), b.x(R(10, 1, 14, 10)), b.f(L(11, 13, 19.5, 4.5, 2.2), P((13.5, 2.5), (21.5, 2.5), (21.5, 10.5)))),
    "dockin": lambda b: (_win(b, 2, 2, 20, 20, 1.6, 3.5), b.f(L(18, 7.5, 10.5, 15, 2.2), P((6.5, 10), (6.5, 18.5), (15, 18.5)))),
    "sdi": lambda b: (_win(b, 1.5, 2.5, 11, 9.5), _win(b, 11.5, 12, 11, 9.5)),
    "monitor": lambda b: (b.f(R(2, 3, 20, 13, 1.5)), b.x(R(4, 5, 16, 9)), b.f(R(11, 16, 2, 3), R(7, 19, 10, 2))),
    "monitors": _monitors,
    "overview": lambda b: [_win(b, x, y, 9, 9, 1.5, 2.6) for x, y in ((2, 2), (13, 2), (2, 13), (13, 13))],
    "close": lambda b: b.f(L(5, 5, 19, 19, 3), L(19, 5, 5, 19, 3)),
    "flash": lambda b: b.f(P((14, 2), (5, 13.5), (11, 13.5), (9, 22), (19, 9.5), (13, 9.5), (16.5, 2))),
    "more": lambda b: b.f(C(5, 12, 2.2), C(12, 12, 2.2), C(19, 12, 2.2)),
    "floppy": lambda b: (b.f(P((3, 3), (17, 3), (21, 7), (21, 21), (3, 21))), b.x(R(6.5, 5, 9, 4.2), R(6, 12.5, 12, 6.8)), b.f(R(8, 14.5, 8, 1.6))),
    "folderopen": lambda b: b.f(P((2, 4.5), (9, 4.5), (11, 7), (19, 7), (19, 9.4), (2, 9.4)), R(2, 9.4, 2, 10.6), P((5.2, 11), (22.5, 11), (18.8, 20), (2, 20))),
    "exit": lambda b: (b.f(R(3, 2, 11, 20, 1)), b.x(R(5, 4, 7, 16)), b.f(L(9, 12, 18.5, 12, 2.4), P((17, 7.5), (22, 12), (17, 16.5)))),
    "font": lambda b: b.f(L(5, 21, 12, 3, 2.6), L(19, 21, 12, 3, 2.6), R(8, 14, 8, 2.2)),
    "contrast": lambda b: (b.f(C(12, 12, 10)), b.x(C(12, 12, 8)), b.f(PIE(12, 12, 8.2, 90, 270))),
    "chip": _chip,
    "pulse": lambda b: b.f(L(2, 13, 7, 13), L(7, 13, 10, 5), L(10, 5, 14, 20), L(14, 20, 17, 11), L(17, 11, 22, 11)),
    "cloud": lambda b: b.f(C(8, 14, 4.5), C(14, 10.5, 6), C(18, 15, 4), R(8, 14, 10, 5)),
    "info": lambda b: (b.f(C(12, 12, 10)), b.x(C(12, 12, 7.8)), b.f(C(12, 7.5, 1.5), R(10.9, 10.5, 2.2, 7.5))),
}

# 명령 표지 — 이름: 그리기 함수(b, cx, cy, k). k 는 표지 반지름.
BADGES = {
    "right": lambda b, x, y, k: b.f(R(x - k, y - 0.22 * k, 1.1 * k, 0.44 * k), P((x, y - 0.8 * k), (x + k, y), (x, y + 0.8 * k))),
    "cross": lambda b, x, y, k: b.f(L(x - 0.75 * k, y - 0.75 * k, x + 0.75 * k, y + 0.75 * k, 0.42 * k), L(x + 0.75 * k, y - 0.75 * k, x - 0.75 * k, y + 0.75 * k, 0.42 * k)),
    "reset": lambda b, x, y, k: _turn(b, x, y, 0.55 * k, 0.42 * k, 0.68 * k),
}

_dup = (set(GLYPHS) & set(G.GLYPHS)) | (set(BADGES) & set(G.BADGES))
if _dup:
    raise ImportError(f"명령 글리프·표지 이름이 icon_glyphs.py 와 겹친다: {sorted(_dup)}")
G.GLYPHS.update(GLYPHS)
G.BADGES.update(BADGES)

# ── 명령 아이콘 검사·상수 생성 보조(menu_icons.py 가 부른다 — 생성기 본체 300줄 상한 보호) ──
KEY_PREFIX = "PdCmdIcon"
RESERVED = {"KeyPrefix", "Count", "All"}   # CmdIconKeys 의 고정 멤버 이름과 겹치면 안 된다
_NAME_RE = re.compile(r"[A-Z][A-Za-z0-9]*")


def validate_cmd(cmd, taken):
    """명령 조합 검사 → 오류 문장 목록. taken = 메뉴·셸이 이미 쓴 {(글리프, 표지): 이름}(교차 중복도 잡는다)."""
    errs, seen = [], dict(taken)
    for n, (g, b) in cmd.items():
        if not _NAME_RE.fullmatch(n) or n in RESERVED:
            errs.append(f"명령 이름 형식 오류(파스칼 표기·예약어 금지): {n}")
        if g not in G.GLYPHS:
            errs.append(f"없는 글리프: 명령 {n} → {g}")
        if b is not None and b not in G.BADGES:
            errs.append(f"없는 표지: 명령 {n} → {b}")
        if (g, b) in seen:
            errs.append(f"중복 조합: 명령 {n} 와 {seen[(g, b)]} 가 ({g}, {b})")
        seen.setdefault((g, b), f"명령 {n}")
    return errs


def cmdkeys_text(cmd):
    """CmdIconKeys.Generated.cs 본문. 상수 값은 리소스 키 문자열 그대로라 x:Static 으로 쓸 수 있다."""
    return "\n".join([
        "// 명령 아이콘 키 상수(상단 메뉴 실행 항목과 그 메뉴로 연 창이 같이 쓴다) — plan/_build/menu_icons.py 생성물(손으로 고치지 말고 menu_icon_data.py CMD 를 고친 뒤 다시 만든다)",
        "using System.Collections.Generic;", "", "namespace xkxk.frameworks.ui.data.Navigation;", "",
        "/// <summary>명령 아이콘 리소스 키 = xkxk.frameworks.ui.views/Themes/MenuIcons.axaml 의 StreamGeometry 키 PdCmdIcon&lt;이름&gt;. UI 비의존 문자열만 둔다(22 설계 §2.1).</summary>",
        "public static class CmdIconKeys", "{",
        f'    public const string KeyPrefix = "{KEY_PREFIX}";', f"    public const int Count = {len(cmd)};", "",
        *[f'    public const string {n} = "{KEY_PREFIX}{n}";' for n in cmd], "",
        "    /// <summary>모든 명령 아이콘 키(데이터 순서).</summary>",
        "    public static IReadOnlyList<string> All { get; } = new[]", "    {",
        *[f"        {n}," for n in cmd], "    };", "}", ""])
