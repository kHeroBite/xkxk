# B09 창 레지스트리 판정기(T-JUDGE) — src 의 DocumentKind 식별자·enum 선언 0 과 Framework 창 계약 파일의 Avalonia 식별자 0 을 C# 어휘 분석으로 판정한다
"""
사용: python3 -B window_registry_check.py [--root <UI 영역 루트>] [--json]

- 대상: <root>/src/**/*.cs (bin·obj 폴더 제외). 주석·문자열·문자 리터럴은 세지 않고, 보간 문자열의 {…} 안 코드는 센다.
  토큰은 같은 폴더 menu_registry_lex.tokenize 로 나누며 식별자가 정확히 같을 때만 센다(DocumentKindNames 는 다른 식별자다).
- 위반: ① 식별자 DocumentKind(kind=identifier) ② `enum DocumentKind` 선언(kind=enum_declaration)
  ③ src/xkxk.frameworks.ui.shell/Framework/ 의 IUiWindowRegistry.cs·UiWindow*.cs 에 있는 식별자 Avalonia(avalonia_in_framework)
- rc: 0=위반 없음, 1=위반 있음, 2=실행 오류 또는 검사한 .cs 파일 0개(빈 경로를 통과로 세지 않는다)
- --json: {files_checked, violations:[{file,line,kind}], avalonia_in_framework:[{file,line}], rc} 만 stdout 에 낸다.
"""
import argparse
import json
import os
import re
import sys

from menu_registry_lex import tokenize

UI = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", ".."))
TARGET = "DocumentKind"
FW_DIR = "src/xkxk.frameworks.ui.shell/Framework/"
STR_OPEN = re.compile(r'([$@]*)("{3,}|")')
CHAR_LIT = re.compile(r"'(?:\\.[0-9A-Fa-f]*|[^'\\\n])'")
DIRECTIVE_TEXT = re.compile(r"(?m)^[ \t]*#[ \t]*(?:region|endregion|error|warning)\b.*$")


def code_only(text):
    """주석·문자열·문자 리터럴을 공백으로 바꾼 코드. 줄 수는 그대로이고 보간 구멍 안의 코드는 남긴다."""
    text = DIRECTIVE_TEXT.sub("", text)
    out = []
    _scan_code(text, 0, out, in_hole=False)
    return "".join(out)


def _blank(text, a, b, out):
    out.append(re.sub(r"[^\n]", " ", text[a:b]))
    return b


def _run(text, i, ch):
    j = i
    while j < len(text) and text[j] == ch:
        j += 1
    return j - i


def _scan_code(t, i, out, in_hole):
    """코드 구간을 읽는다. in_hole 이면 짝 없는 '}' 위치를 돌려준다."""
    depth, n = 0, len(t)
    while i < n:
        c = t[i]
        if t.startswith("//", i):
            j = t.find("\n", i)
            i = _blank(t, i, n if j < 0 else j, out)
            continue
        if t.startswith("/*", i):
            j = t.find("*/", i + 2)
            i = _blank(t, i, n if j < 0 else j + 2, out)
            continue
        if c == "'":
            m = CHAR_LIT.match(t, i)
            if m:
                i = _blank(t, i, m.end(), out)
                continue
        if c in "$@\"":
            m = STR_OPEN.match(t, i)
            if m:
                i = _scan_string(t, m, out)
                continue
        if in_hole:
            if c == "}" and depth == 0:
                return i
            depth += (c == "{") - (c == "}")
        out.append(c)
        i += 1
    return i


def _scan_string(t, m, out):
    """일반·verbatim(@)·보간($)·원시(\"\"\") 문자열 하나를 건너뛰고 다음 위치를 돌려준다."""
    prefix, quotes = m.group(1), m.group(2)
    dollars, verbatim = prefix.count("$"), "@" in prefix
    raw = 0 if verbatim or len(quotes) < 3 else len(quotes)
    i, n = _blank(t, m.start(), m.start(2) + (raw or 1), out), len(t)
    while i < n:
        c = t[i]
        if c == '"':
            if raw:
                q = _run(t, i, '"')
                i = _blank(t, i, i + q, out)
                if q >= raw:
                    return i
                continue
            if verbatim and t.startswith('""', i):
                i = _blank(t, i, i + 2, out)
                continue
            return _blank(t, i, i + 1, out)
        if c == "\\" and not raw and not verbatim:
            i = _blank(t, i, min(i + 2, n), out)
            continue
        if c == "\n" and not raw and not verbatim and not dollars:
            return i
        if c == "{" and dollars:
            k = _run(t, i, "{")
            hole = k >= dollars if raw else k % 2 == 1
            i = _blank(t, i, i + k, out)
            if hole:
                i = _scan_code(t, i, out, in_hole=True)
                i = _blank(t, i, min(i + (dollars if raw else 1), n), out)
            continue
        i = _blank(t, i, i + 1, out)
    return i


def cs_files(root):
    src = os.path.join(root, "src")
    found = []
    for base, dirs, files in os.walk(src):
        dirs[:] = sorted(d for d in dirs if d.lower() not in ("bin", "obj"))
        found += [os.path.relpath(os.path.join(base, f), root).replace(os.sep, "/") for f in sorted(files) if f.endswith(".cs")]
    return found


def is_framework_window_file(rel):
    if not rel.startswith(FW_DIR):
        return False
    name = rel[len(FW_DIR):]
    return "/" not in name and (name == "IUiWindowRegistry.cs" or name.startswith("UiWindow"))


def check(root):
    files = cs_files(root)
    violations, avalonia, fw_files = [], [], 0
    for rel in files:
        with open(os.path.join(root, rel), encoding="utf-8-sig") as f:
            toks = tokenize(code_only(f.read()), rel)
        framework = is_framework_window_file(rel)
        fw_files += framework
        for i, (kind, value, line, _) in enumerate(toks):
            if kind != "id":
                continue
            if value == TARGET:
                declared = i > 0 and toks[i - 1][:2] == ("id", "enum")
                violations.append({"file": rel, "line": line, "kind": "enum_declaration" if declared else "identifier"})
            if framework and value == "Avalonia":
                avalonia.append({"file": rel, "line": line})
    rc = 2 if not files else 1 if violations or avalonia else 0
    return {"files_checked": len(files), "violations": violations, "avalonia_in_framework": avalonia, "rc": rc}, fw_files


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--root", default=UI, help="UI 영역 루트(기본: 이 저장소의 frameworks/ui)")
    ap.add_argument("--json", action="store_true", help="판정 JSON 만 출력")
    a = ap.parse_args(argv)
    try:
        result, fw_files = check(os.path.abspath(a.root))
    except Exception as e:  # noqa: BLE001 — 판정 불능은 통과도 위반도 아닌 rc 2
        print("실행 오류: %s: %s" % (type(e).__name__, e), file=sys.stderr)
        if a.json:
            print(json.dumps({"files_checked": 0, "violations": [], "avalonia_in_framework": [], "rc": 2}))
        return 2
    if not result["files_checked"]:
        print("검사한 .cs 파일이 없다: %s" % os.path.join(os.path.abspath(a.root), "src"), file=sys.stderr)
    elif not fw_files:
        print("참고: Framework 창 계약 파일(IUiWindowRegistry.cs·UiWindow*.cs)이 없다", file=sys.stderr)
    if a.json:
        print(json.dumps(result, ensure_ascii=False, indent=1))
    else:
        for v in result["violations"]:
            print("위반 %s:%d %s" % (v["file"], v["line"], v["kind"]))
        for v in result["avalonia_in_framework"]:
            print("위반 %s:%d avalonia_in_framework" % (v["file"], v["line"]))
        print("검사 파일 %d(창 계약 %d) · DocumentKind %d · Avalonia %d · rc=%d"
              % (result["files_checked"], fw_files, len(result["violations"]), len(result["avalonia_in_framework"]), result["rc"]))
    return result["rc"]


if __name__ == "__main__":
    sys.exit(main())
