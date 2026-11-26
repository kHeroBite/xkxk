# 공개 API 표면 검사기 — PublicAPI Shipped·Unshipped 줄의 삭제·변경을 git 기준선과 비교해 막고 형식·빌드 로그 RS 진단 0건을 본다(UADR-0009)
"""사용:
  python3 -B api_surface_check.py [--base HEAD] [--allow-major] [--allow-unshipped-change] [--log <빌드 로그>] [--root <영역 루트>]
  python3 -B api_surface_check.py --self-test

판정
  - 대상: <root>/src/*/PublicAPI.Shipped.txt·PublicAPI.Unshipped.txt(작업 트리).
  - 기준선: git show <base>:<경로>. base 에 파일이 없으면 '기준선 없음(첫 도입)' 으로 그 파일 비교는 통과.
  - base 에 있던 줄이 사라지면 FAIL(변경 = 삭제+추가). Shipped 는 --allow-major, Unshipped 는 --allow-unshipped-change 면 WARN.
  - 두 파일 첫 줄 '#nullable enable', 같은 프로젝트 두 파일 사이 중복 줄 0.
  - --log 가 있으면 'warning|error RS0016/17/26/27/36/37' 진단 0건(로그가 없으면 rc=2).
rc 0=PASS, 1=FAIL, 2=인자·입력 오류.
"""
import argparse
import collections
import glob
import os
import re
import subprocess
import sys
import tempfile

DEFAULT_ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))))
KINDS = ("PublicAPI.Shipped.txt", "PublicAPI.Unshipped.txt")
HEADER = "#nullable enable"
RS_RE = re.compile(r"\b(?:warning|error)\s+(RS00(?:16|17|26|27|36|37))\b")


class InputError(Exception):
    pass


def git(root, *args):
    p = subprocess.run(["git", "-c", "core.quotepath=off", "-C", root] + list(args),
                       capture_output=True, text=True, encoding="utf-8", errors="replace")
    return p.returncode, p.stdout, p.stderr


def norm_lines(text):
    if text.startswith("﻿"):
        text = text[1:]
    return [ln.rstrip() for ln in text.splitlines()]


def api_lines(lines):
    return [ln for ln in lines if ln.strip()]


def read_file(path):
    with open(path, encoding="utf-8", errors="replace") as f:
        return norm_lines(f.read())


def check(root, base="HEAD", allow_major=False, allow_unshipped=False, log=None):
    """검사 본체 — (rc, 출력 줄 목록). 입력 오류는 InputError."""
    out = []
    fails = warns = 0
    root = os.path.abspath(root)
    if not os.path.isdir(root):
        raise InputError("영역 루트 없음: %s" % root)
    files = sorted(glob.glob(os.path.join(root, "src", "*", "PublicAPI.*.txt")))
    files = [f for f in files if os.path.basename(f) in KINDS]
    if not files:
        raise InputError("PublicAPI txt 없음: %s/src/*/" % root)
    rc, _, err = git(root, "rev-parse", "--verify", "--quiet", base + "^{commit}")
    if rc != 0:
        raise InputError("git 기준선 ref 를 찾지 못함: %s %s" % (base, err.strip()))
    if log is not None and not os.path.isfile(log):
        raise InputError("빌드 로그 없음: %s" % log)

    by_project = collections.defaultdict(dict)
    for path in files:
        rel = os.path.relpath(path, root).replace(os.sep, "/")
        kind = os.path.basename(path)
        cur = read_file(path)
        by_project[os.path.dirname(rel)][kind] = (rel, cur)
        if not cur or cur[0].strip() != HEADER:
            fails += 1
            out.append("FAIL %s 첫줄이_%s_아님 %s" % (rel, HEADER.replace(" ", "_"), cur[0] if cur else "<빈 파일>"))
        rc, text, _ = git(root, "show", "%s:./%s" % (base, rel))
        if rc != 0:
            out.append("INFO %s 기준선 없음(첫 도입) — 비교 대상 없음으로 통과" % rel)
            continue
        removed = collections.Counter(api_lines(norm_lines(text))) - collections.Counter(api_lines(cur))
        shipped = kind == KINDS[0]
        allowed = allow_major if shipped else allow_unshipped
        flag = "--allow-major" if shipped else "--allow-unshipped-change"
        for line, n in sorted(removed.items()):
            for _ in range(n):
                if allowed:
                    warns += 1
                    out.append("WARN %s 기준선_줄_삭제·변경(%s 허용) %s" % (rel, flag, line))
                else:
                    fails += 1
                    out.append("FAIL %s 기준선_줄_삭제·변경(허용하려면 %s) %s" % (rel, flag, line))

    for proj, pair in sorted(by_project.items()):
        if len(pair) < 2:
            continue
        ship = set(api_lines(pair[KINDS[0]][1])) - {HEADER}
        unship = set(api_lines(pair[KINDS[1]][1])) - {HEADER}
        for line in sorted(ship & unship):
            fails += 1
            out.append("FAIL %s Shipped·Unshipped_중복_줄 %s" % (proj, line))

    if log is not None:
        with open(log, encoding="utf-8", errors="replace") as f:
            hits = collections.Counter(m.group(1) for m in RS_RE.finditer(f.read()))
        for rule, n in sorted(hits.items()):
            fails += 1
            out.append("FAIL %s 빌드_로그_%s_진단 %d건" % (log, rule, n))
        if not hits:
            out.append("INFO 빌드 로그 RS0016/17/26/27/36/37 진단 0건: %s" % log)

    out.append("요약: 파일 %d · FAIL %d · WARN %d · base=%s" % (len(files), fails, warns, base))
    return (1 if fails else 0), out


# ---------------- 자체 시험 ----------------
SHIP_BASE = [HEADER, "S.Kept() -> void", "S.Gone() -> void"]
UNSHIP_BASE = [HEADER, "U.A() -> void", "U.B() -> void"]


def _write(path, lines):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(lines) + "\n")


def _repo(tmp, with_api=True):
    """임시 저장소(하위 폴더 frameworks/ui 를 영역 루트로) — 기준 커밋을 만들고 영역 루트를 돌려준다."""
    repo = os.path.join(tmp, "repo")
    area = os.path.join(repo, "frameworks", "ui")
    os.makedirs(area)
    subprocess.run(["git", "init", "-q", repo], check=True)
    _write(os.path.join(area, "README.md"), ["시험"])
    if with_api:
        _write(os.path.join(area, "src", "p", KINDS[0]), SHIP_BASE)
        _write(os.path.join(area, "src", "p", KINDS[1]), UNSHIP_BASE)
    subprocess.run(["git", "-C", repo, "add", "-A"], check=True)
    subprocess.run(["git", "-C", repo, "-c", "user.name=t", "-c", "user.email=t@t", "-c", "commit.gpgsign=false",
                    "commit", "-q", "-m", "base"], check=True)
    if not with_api:
        _write(os.path.join(area, "src", "p", KINDS[0]), SHIP_BASE)
        _write(os.path.join(area, "src", "p", KINDS[1]), UNSHIP_BASE)
    return area


def _case(name, expect_rc, mutate=None, with_api=True, log_text=None, expect_text=None, **kw):
    with tempfile.TemporaryDirectory(prefix="api_surface_selftest_") as tmp:
        area = _repo(tmp, with_api)
        p = os.path.join(area, "src", "p")
        if mutate:
            mutate(p)
        log = None
        if log_text is not None:
            log = os.path.join(tmp, "build.log")
            _write(log, [log_text])
        try:
            rc, out = check(area, log=log, **kw)
        except InputError as e:
            rc, out = 2, ["INPUT %s" % e]
        good = rc == expect_rc and (expect_text is None or any(expect_text in x for x in out))
        print("SELFTEST %s %s (rc=%d 기대 %d)" % ("PASS" if good else "FAIL", name, rc, expect_rc))
        if not good:
            for x in out:
                print("    " + x)
        return good


def _drop_unshipped_b(p):
    _write(os.path.join(p, KINDS[1]), [HEADER, "U.A() -> void"])


def self_test():
    cases = [
        ("unshipped_delete_fail", 1, _drop_unshipped_b, {}),
        ("unshipped_change_fail", 1, lambda p: _write(os.path.join(p, KINDS[1]), [HEADER, "U.A() -> void", "U.B(int x) -> void"]), {}),
        ("unshipped_delete_allowed", 0, _drop_unshipped_b, {"allow_unshipped": True}),
        ("shipped_delete_fail", 1, lambda p: _write(os.path.join(p, KINDS[0]), [HEADER, "S.Kept() -> void"]), {}),
        ("shipped_delete_allow_major", 0, lambda p: _write(os.path.join(p, KINDS[0]), [HEADER, "S.Kept() -> void"]), {"allow_major": True}),
        ("add_only_pass", 0, lambda p: _write(os.path.join(p, KINDS[1]), UNSHIP_BASE + ["U.C() -> void"]), {}),
        ("nullable_header_fail", 1, lambda p: _write(os.path.join(p, KINDS[1]), UNSHIP_BASE[1:] + [HEADER]), {}),
        ("duplicate_line_fail", 1, lambda p: _write(os.path.join(p, KINDS[1]), UNSHIP_BASE + ["S.Kept() -> void"]), {}),
    ]
    results = [_case(n, rc, m, **kw) for n, rc, m, kw in cases]
    results.append(_case("base_missing_file", 0, with_api=False, expect_text="기준선 없음"))
    results.append(_case("log_rs0017_fail", 1,
                         log_text="X.cs(3,5): error RS0017: Symbol 'Foo' is part of the declared API, but is either not public or could not be found"))
    results.append(_case("log_clean_pass", 0, log_text="빌드했습니다. 경고 0개 오류 0개"))
    results.append(_case("bad_base_ref_rc2", 2, base="no-such-ref"))
    ok = all(results)
    print("SELFTEST 요약: %d/%d PASS" % (sum(results), len(results)))
    return 0 if ok else 1


def main(argv):
    ap = argparse.ArgumentParser(description="PublicAPI 표면 검사(삭제·변경 FAIL, 형식, 빌드 로그 RS 진단)")
    ap.add_argument("--base", default="HEAD", help="git 기준선 ref(기본 HEAD)")
    ap.add_argument("--allow-major", action="store_true", help="Shipped 줄 삭제·변경을 WARN 으로(주 버전 변경)")
    ap.add_argument("--allow-unshipped-change", action="store_true", help="Unshipped 줄 삭제·변경을 WARN 으로")
    ap.add_argument("--log", help="빌드 로그 — RS0016/17/26/27/36/37 진단 0건 확인")
    ap.add_argument("--root", default=DEFAULT_ROOT, help="영역 루트(기본: 이 스크립트 기준 frameworks/ui)")
    ap.add_argument("--self-test", action="store_true", help="임시 git 저장소로 판정 사례를 시험")
    try:
        a = ap.parse_args(argv)
    except SystemExit as e:
        return 0 if e.code == 0 else 2
    if a.self_test:
        return self_test()
    try:
        rc, out = check(a.root, a.base, a.allow_major, a.allow_unshipped_change, a.log)
    except InputError as e:
        print("ERROR %s" % e)
        return 2
    print("\n".join(out))
    return rc


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
