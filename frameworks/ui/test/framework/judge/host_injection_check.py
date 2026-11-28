# 호스트 진입부의 App 정적 속성 대입을 찾는 판정기 — B03 끝 조건(대입 0건)을 다시 확인한다
# 사용: host_injection_check.py [--root <R>] [--self-test]
# 출력: INJECT <상대경로>:<줄> <일치> / 끝 줄 HOST_INJECTION count=N files=M
# 종료코드: 0=0건, 1=대입 있음 또는 훑은 파일 수 M<8(대상 소실 의심), 2=인자 오류·대상 폴더 없음·M=0(판정불가)
import json
import os
import re
import sys
import tempfile
import shutil

# 호스팅 라이브러리 3개(B11-2 신설 층 이름) + 시험대 실행 프로젝트 3개(경로는 tools/host_identity.json 에서만 읽는다)
LIB_TARGETS = ["src/xkxk.frameworks.ui.hosting.desktop", "src/xkxk.frameworks.ui.hosting.browser", "src/xkxk.frameworks.ui.hosting.android"]
IDENT_KEYS = ["desktop.project_dir", "browser.project_dir", "android.project_dir"]
SKIP_DIRS = {"bin", "obj"}
MIN_FILES = 8
PATTERN = re.compile(r"\bApp\.[A-Z]\w*\s*(=(?!=)|\+=|-=|\?\?=)")


def strip_comments(text):
    """주석을 공백으로 바꾸되 줄 수는 유지한다. 문자열·문자 리터럴 안의 // 는 건드리지 않는다."""
    out = []
    i, n = 0, len(text)
    while i < n:
        c = text[i]
        nx = text[i + 1] if i + 1 < n else ""
        if c == "/" and nx == "/":
            while i < n and text[i] != "\n":
                out.append(" ")
                i += 1
        elif c == "/" and nx == "*":
            out.append("  ")
            i += 2
            while i < n and not (text[i] == "*" and i + 1 < n and text[i + 1] == "/"):
                out.append("\n" if text[i] == "\n" else " ")
                i += 1
            out.append("  ")
            i += 2
        elif c == "@" and nx == '"' or (c == "$" and nx == "@") or (c == "@" and nx == "$"):
            # verbatim 문자열(@"…", $@"…") — "" 로만 이스케이프
            j = i
            while j < n and text[j] != '"':
                j += 1
            out.append(text[i:j + 1])
            i = j + 1
            while i < n:
                if text[i] == '"':
                    if i + 1 < n and text[i + 1] == '"':
                        out.append('""')
                        i += 2
                        continue
                    out.append('"')
                    i += 1
                    break
                out.append(text[i])
                i += 1
        elif c == '"' or c == "'":
            q = c
            out.append(c)
            i += 1
            while i < n and text[i] != "\n":
                if text[i] == "\\" and i + 1 < n:
                    out.append(text[i:i + 2])
                    i += 2
                    continue
                if text[i] == q:
                    out.append(q)
                    i += 1
                    break
                out.append(text[i])
                i += 1
        else:
            out.append(c)
            i += 1
    return "".join(out)


def find_hits(text):
    """(줄, 일치문자열) 목록."""
    hits = []
    for no, line in enumerate(strip_comments(text).split("\n"), 1):
        for m in PATTERN.finditer(line):
            hits.append((no, m.group(0).strip()))
    return hits


def targets(root):
    """검사 대상 폴더(영역 루트 기준 상대) 목록. host_identity.json 을 못 읽거나 키가 없으면 None."""
    try:
        with open(os.path.join(root, "tools", "host_identity.json"), encoding="utf-8") as f:
            ident = json.load(f)
        return LIB_TARGETS + [ident[k] for k in IDENT_KEYS]
    except (OSError, ValueError, KeyError) as e:
        print("host_identity.json 을 읽지 못함: %s" % e, file=sys.stderr)
        return None


def scan(root):
    """(hits, files). 대상 폴더가 없으면 None."""
    hits, files = [], 0
    tg = targets(root)
    if tg is None:
        return None
    for rel in tg:
        base = os.path.join(root, rel)
        if not os.path.isdir(base):
            print("대상 폴더 없음: %s" % rel, file=sys.stderr)
            return None
        for dp, dns, fns in os.walk(base):
            dns[:] = [d for d in dns if d not in SKIP_DIRS]
            for fn in sorted(fns):
                if not fn.endswith(".cs"):
                    continue
                path = os.path.join(dp, fn)
                files += 1
                with open(path, encoding="utf-8-sig", errors="replace") as f:
                    for no, m in find_hits(f.read()):
                        hits.append((os.path.relpath(path, root).replace(os.sep, "/"), no, m))
    return hits, files


def self_test():
    cases = [
        ("assign_detect", "App.LayoutStore = x;", 1),
        ("qualified_detect", "xkxk.frameworks.ui.shell.App.LayoutStore = x;", 1),
        ("compound_detect", "App.Args += y;\nApp.Count -= 1;\nApp.Name ??= z;", 3),
        ("equality_ignored", "if (App.Mode == 1 && App.Flag != 2) { }", 0),
        ("comment_ignored", "// App.A = 1;\n/* App.B = 2;\n App.C = 3; */\nvar u = \"http://x\"; // App.D = 4;", 0),
        ("read_ignored", "var s = App.LayoutStore;\nAndroidApp.ArgsFromFile = true;", 0),
    ]
    bad = 0
    for name, src, want in cases:
        got = len(find_hits(src))
        ok = got == want
        bad += 0 if ok else 1
        print("SELFTEST %s %s (기대 %d, 실측 %d)" % ("PASS" if ok else "FAIL", name, want, got))
    # 디렉터리 수준: 대상 없음 → 판정불가, 양성 폴더 → 1건
    tmp = tempfile.mkdtemp()
    try:
        if scan(tmp) is not None:  # tools/host_identity.json 도 없는 빈 트리
            print("SELFTEST FAIL missing_target_unjudgeable")
            bad += 1
        else:
            print("SELFTEST PASS missing_target_unjudgeable")
        fake = {k: "testbed/t_%d" % i for i, k in enumerate(IDENT_KEYS)}
        os.makedirs(os.path.join(tmp, "tools"))
        with open(os.path.join(tmp, "tools", "host_identity.json"), "w", encoding="utf-8") as f:
            json.dump(fake, f)
        for rel in LIB_TARGETS + list(fake.values()):
            os.makedirs(os.path.join(tmp, rel))
        with open(os.path.join(tmp, LIB_TARGETS[0], "P.cs"), "w", encoding="utf-8") as f:
            f.write("class P { void M() { App.LayoutStore = x; } }\n")
        r = scan(tmp)
        ok = r is not None and len(r[0]) == 1 and r[1] == 1
        print("SELFTEST %s dir_scan_detect" % ("PASS" if ok else "FAIL"))
        bad += 0 if ok else 1
    finally:
        shutil.rmtree(tmp, ignore_errors=True)
    return 1 if bad else 0


def main(argv):
    root = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", ".."))
    args = argv[1:]
    if args == ["--self-test"]:
        return self_test()
    i = 0
    while i < len(args):
        if args[i] == "--root" and i + 1 < len(args):
            root = os.path.abspath(args[i + 1])
            i += 2
        else:
            print("사용: host_injection_check.py [--root <R>] [--self-test]", file=sys.stderr)
            return 2
    r = scan(root)
    if r is None:
        return 2
    hits, files = r
    for rel, no, m in hits:
        print("INJECT %s:%d %s" % (rel, no, m))
    print("HOST_INJECTION count=%d files=%d" % (len(hits), files))
    if files == 0:
        print("검사한 파일 0건 — 판정불가", file=sys.stderr)
        return 2
    if files < MIN_FILES:
        print("검사한 파일 %d < %d — 대상 소실 의심(FAIL)" % (files, MIN_FILES), file=sys.stderr)
        return 1
    return 1 if hits else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
