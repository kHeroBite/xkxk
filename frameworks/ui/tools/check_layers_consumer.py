# 프레임워크 소비 경계 규칙(InternalsVisibleTo 금지·소비자 시험의 Internal/NonPublic 접근 금지)을 검사한다
"""
check_layers.py 가 불러 쓴다. 규칙은 check_layers_rules.json 의 consumer_rules 키다.
- framework_paths 아래 .cs/.csproj 에서 framework_forbid_code 를 찾는다(주석 제외).
- consumers 중 active=true 인 경로 아래 파일에서 consumer_forbid_code 를 찾는다. 경로가 없으면 건너뛴다.
단독 실행: python3 tools/check_layers_consumer.py [--root 저장소] [--self-test]
"""
import json
import os
import re
import sys


def _strip(rel, text):
    from check_layers import strip_comments  # 순환 import 를 피하려 호출 시점에 가져온다
    return strip_comments(rel, text)


def scan_text(rel, text, patterns, label):
    """주석을 지운 코드에서 금지 패턴을 찾아 위반 문자열 목록을 돌려준다."""
    if not any(p.search(text) for p in patterns):
        return []
    code = _strip(rel, text)
    return [f"{rel}:{code.count(chr(10), 0, m.start()) + 1} {label}: {m.group(0)}"
            for p in patterns for m in p.finditer(code)]


def check_files(files, cr):
    """files: {저장소 상대 경로: 내용}. consumer_rules 에 따라 위반 목록을 돌려준다."""
    exts = tuple(cr["exts"])
    fw = [re.compile(p) for p in cr["framework_forbid_code"]]
    cons = [re.compile(p) for p in cr["consumer_forbid_code"]]
    roots = [c["path"].rstrip("/") + "/" for c in cr["consumers"] if c.get("active")]
    fw_roots = [p.rstrip("/") + "/" for p in cr["framework_paths"]]
    found = []
    for rel, text in files.items():
        if not rel.endswith(exts):
            continue
        if any(rel.startswith(r) for r in fw_roots):
            found += scan_text(rel, text, fw, "프레임워크 금지 참조")
        if any(rel.startswith(r) for r in roots):
            found += scan_text(rel, text, cons, "소비자 금지 접근")
    return found


def check_consumers(root, rules):
    """작업 트리에서 대상 파일을 읽어 위반 목록을 돌려준다. consumer_rules 가 없으면 빈 목록이다."""
    cr = rules.get("consumer_rules")
    if not cr:
        return []
    skip = set(rules.get("skip_dirs", []))
    exts = tuple(cr["exts"])
    bases = list(cr["framework_paths"]) + [c["path"] for c in cr["consumers"] if c.get("active")]
    files = {}
    for base in bases:
        top = os.path.join(root, base)
        if not os.path.isdir(top):
            continue
        for cur, dirs, names in os.walk(top):
            dirs[:] = [d for d in dirs if d not in skip]
            for name in names:
                if name.endswith(exts):
                    path = os.path.join(cur, name)
                    rel = os.path.relpath(path, root).replace(os.sep, "/")
                    with open(path, encoding="utf-8", errors="replace", newline="") as f:
                        files[rel] = f.read()
    return check_files(files, cr)


def self_test_cases():
    """(이름, 파일 맵, 기대 위반 수 — 0 이면 없음, 양수면 1건 이상) 목록."""
    fw, tf = "src/xkxk.frameworks.ui.shell/", "test/framework/"
    return [
        ("IVT 특성 금지", {fw + "A.cs": "// 헤더\n[assembly: InternalsVisibleTo(\"x\")]\n"}, 1),
        ("IVT csproj 금지", {fw + "A.csproj": "<Project><InternalsVisibleTo Include=\"x\" /></Project>\n"}, 1),
        ("IVT 주석은 통과", {fw + "A.cs": "// InternalsVisibleTo 금지\nint a;\n"}, 0),
        ("프레임워크 정상", {fw + "A.cs": "// 헤더\nint a;\n"}, 0),
        ("소비자 Internal 네임스페이스", {tf + "T.cs": "using xkxk.frameworks.ui.shell.Framework.Internal;\n"}, 1),
        ("소비자 NonPublic 리플렉션", {tf + "T.cs": "var f = t.GetField(\"x\", BindingFlags.NonPublic);\n"}, 1),
        ("소비자 정상", {tf + "T.cs": "using xkxk.frameworks.ui.shell.Framework;\n"}, 0),
        ("활성 testbed 위반은 잡힌다", {"testbed/T.cs": "var f = BindingFlags.NonPublic;\n"}, 1),
        ("소비자 주석은 통과", {tf + "T.cs": "// BindingFlags.NonPublic 금지\n"}, 0),
    ]


def run_self_test(rules):
    """내장 사례를 돌려 실패 수를 돌려준다(check_layers --self-test 가 합산한다)."""
    cr = rules.get("consumer_rules")
    failed = 0
    for name, files, expected in self_test_cases() if cr else []:
        got = len(check_files(files, cr))
        passed = (got > 0) == (expected > 0)
        failed += 0 if passed else 1
        print(f"{'ok  ' if passed else 'FAIL'} consumer {name} 위반={got}")
    return failed


def main():
    here = os.path.dirname(os.path.abspath(__file__))
    root = os.path.abspath(os.path.join(here, ".."))
    sys.path.insert(0, here)
    with open(os.path.join(here, "check_layers_rules.json"), encoding="utf-8") as f:
        rules = json.load(f)
    if "--self-test" in sys.argv:
        return 1 if run_self_test(rules) else 0
    found = check_consumers(root, rules)
    for v in found:
        print(f"VIOLATION {v}")
    print(f"consumer violations={len(found)}")
    return 1 if found else 0


if __name__ == "__main__":
    sys.exit(main())
