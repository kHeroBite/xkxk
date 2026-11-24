#!/usr/bin/env python3
# 클라이언트 계층 정적 검사 — 프로젝트 참조·패키지·코드 참조 방향과 신규 층 파일의 줄 수·헤더 규칙을 판정한다
"""
사용: python3 poc/ui/tools/check_layers.py [--root 저장소] [--rules 규칙.json] [--self-test]
규칙: 같은 폴더 check_layers_rules.json. 주석 안의 언급은 위반으로 치지 않는다.
출력: 위반 줄(VIOLATION ...), 요약 줄, 마지막 줄 elapsed_ms=<n>. 종료 코드: 0=통과, 1=위반 또는 검사 불가.
"""
import argparse
import json
import os
import re
import sys
import time
from concurrent.futures import ThreadPoolExecutor

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))  # python -I 는 스크립트 폴더를 path 에 넣지 않는다
from check_layers_parts import check_template_parts, load_dock_blob  # noqa: E402

HANGUL = re.compile(r"[가-힣]")
XML_COMMENT = re.compile(r"<!--.*?-->", re.S)
BLOCK_COMMENT = re.compile(r"/\*.*?\*/", re.S)
LINE_COMMENT = re.compile(r"(?<!:)//[^\n]*")
PROJECT_REF = re.compile(r"<ProjectReference\s+Include=\"([^\"]+)\"")
PACKAGE_REF = re.compile(r"<PackageReference\s+Include=\"([^\"]+)\"(?:\s+Version=\"([^\"]*)\")?")
CODE_EXTS = (".cs", ".axaml")


def blank(match):
    """주석을 지우되 줄 번호가 유지되도록 개행은 남긴다."""
    return re.sub(r"[^\n]", " ", match.group(0))


def strip_comments(rel, text):
    if rel.endswith(".cs"):
        return LINE_COMMENT.sub(blank, BLOCK_COMMENT.sub(blank, text))
    return XML_COMMENT.sub(blank, text)


def line_of(text, pos):
    return text.count("\n", 0, pos) + 1


def layer_of(rel, rules):
    prefix = rules["src_root"].rstrip("/") + "/"
    if not rel.startswith(prefix):
        return None
    name = rel[len(prefix):].split("/", 1)[0]
    return name if name in rules["layers"] else None


def check_csproj(rel, code, layer, rule):
    found = []
    for m in PROJECT_REF.finditer(code):
        target = re.split(r"[\\/]", m.group(1))[-1].removesuffix(".csproj")
        if target in rule.get("forbid_projects", []):
            found.append(f"{rel}:{line_of(code, m.start())} {layer} 는 프로젝트 {target} 를 참조할 수 없다")
    allow = rule.get("allow_packages")
    for m in PACKAGE_REF.finditer(code):
        name, version = m.group(1), m.group(2)
        at = f"{rel}:{line_of(code, m.start())}"
        if any(re.search(p, name) for p in rule.get("forbid_packages", [])):
            found.append(f"{at} {layer} 는 패키지 {name} 를 참조할 수 없다")
        if allow is not None and name not in allow:
            found.append(f"{at} {layer} 허용 패키지가 아니다: {name}")
        elif allow is not None and allow[name] != version:
            found.append(f"{at} {layer} 패키지 {name} 버전은 {allow[name]} 이어야 한다(실제 {version})")
    return found


def check_layer_file(rel, text, layer, rule, patterns):
    if rel.endswith(".csproj"):
        return check_csproj(rel, strip_comments(rel, text), layer, rule)
    if not any(p.search(text) for p in patterns):
        return []  # 원문에 없으면 주석을 지워도 없다 — 대부분의 파일이 여기서 끝난다
    code = strip_comments(rel, text)
    found = []
    for pattern in patterns:
        for m in pattern.finditer(code):
            found.append(f"{rel}:{line_of(code, m.start())} {layer} 금지 참조: {m.group(0)}")
    return found


def has_korean_header(rel, text):
    lines = text.lstrip("﻿").split("\n")
    if rel.endswith(".py") and lines and lines[0].startswith("#!"):
        lines = lines[1:]
    first = lines[0].strip() if lines else ""
    if rel.endswith(".cs"):
        is_comment = first.startswith("//")
    elif rel.endswith(".py"):
        is_comment = first.startswith("#")
    else:
        is_comment = first.startswith("<!--")
    return is_comment and bool(HANGUL.search(first))


def check_new_file(rel, text, cfg):
    found = []
    found += check_line_limit(rel, text, cfg)
    if not has_korean_header(rel, text):
        found.append(f"{rel} 첫 줄에 한국어 헤더 주석이 없다")
    return found


def is_new_file(rel, cfg):
    return (rel.endswith(tuple(cfg["exts"])) and rel not in cfg["exempt"]
            and any(rel == p or rel.startswith(p) for p in cfg["prefixes"]))


def count_lines(text):
    return text.count("\n") + (0 if text.endswith("\n") or not text else 1)


def check_line_limit(rel, text, cfg):
    count = count_lines(text)
    return [f"{rel} 줄 수 {count} > {cfg['max_lines']}"] if count > cfg["max_lines"] else []


def is_limited_file(rel, rules):
    cfg = rules.get("line_limit_only")
    return bool(cfg) and rel.endswith(tuple(cfg["exts"])) and any(rel.startswith(p) for p in cfg["prefixes"])


def check_all(files, rules, dock_blob=None):
    """files: {저장소 상대 경로: 내용}. dock_blob: Dock DLL 내용(없으면 검사 불가 위반). 위반 문자열 목록을 돌려준다."""
    compiled = {k: [re.compile(p) for p in v.get("forbid_code", [])] for k, v in rules["layers"].items()}
    cfg = rules["new_files"]
    found = []
    for rel, text in files.items():
        layer = layer_of(rel, rules)
        if layer and rel.endswith(CODE_EXTS + (".csproj",)):
            found += check_layer_file(rel, text, layer, rules["layers"][layer], compiled[layer])
            if rel.endswith(tuple(rules["layers"][layer].get("forbid_exts", ["\0"]))):
                found.append(f"{rel} {layer} 에 둘 수 없는 파일 형식이다(화면 파일은 Pd.Ui.Views)")
        if is_new_file(rel, cfg):
            found += check_new_file(rel, text, cfg)
        elif is_limited_file(rel, rules):
            found += check_line_limit(rel, text, rules["line_limit_only"])
    for name, rule in rules["layers"].items():
        csproj = f"{rules['src_root'].rstrip('/')}/{name}/{name}.csproj"
        if rule.get("required") and csproj not in files:
            found.append(f"{csproj} 필수 프로젝트가 없다")
    return found + check_template_parts(files, rules, dock_blob, strip_comments)


def scan_dir(root, rel):
    files, dirs = [], []
    with os.scandir(os.path.join(root, rel)) as entries:
        for entry in entries:
            (dirs if entry.is_dir(follow_symlinks=False) else files).append(f"{rel}/{entry.name}")
    return files, dirs


def list_files(root, rules, pool):
    """작업 트리를 폴더 단계별로 나눠 훑는다. 미추적 신규 파일도 봐야 하는데 git ls-files --others·순차 os.walk 는 NTFS 에서 2~5초 걸려 시간 상한을 넘긴다."""
    skip = set(rules["skip_dirs"])
    found, level = [], list(rules["scan_paths"])
    while level:
        next_level = []
        for files, dirs in pool.map(lambda rel: scan_dir(root, rel), level):
            found += files
            next_level += [d for d in dirs if d.rsplit("/", 1)[-1] not in skip]
        level = next_level
    return sorted(found)


def wanted(rel, rules):
    if is_new_file(rel, rules["new_files"]) or is_limited_file(rel, rules):
        return True
    return layer_of(rel, rules) is not None and rel.endswith(CODE_EXTS + (".csproj",))


def read_text(root, rel):
    try:
        with open(os.path.join(root, rel), encoding="utf-8", errors="replace", newline="") as f:
            return rel, f.read()
    except FileNotFoundError:
        return rel, None  # 작업 트리에서 지웠고 아직 커밋 전인 파일


def self_test(rules):
    """규칙이 실제로 위반을 잡는지(진양성)와 주석·허용 경로를 건너뛰는지(진음성)를 내장 사례로 확인한다."""
    ok = "// 정상 헤더\nusing System;\n"
    src = "src/"
    parts, tpl = rules["template_parts"]["contract"], src + "Pd.Ui.Views/T.axaml"
    part = lambda **kv: "// 헤더\n" + "".join(f'public const string {k} = "{v}";\n' for k, v in kv.items())
    base = {
        src + "Pd.Ui/Pd.Ui.csproj": '<!-- 헤더 -->\n<Project><PackageReference Include="Avalonia" Version="12.1.4" /></Project>\n',
        src + "Pd.Poc.Shell/Pd.Poc.Shell.csproj": '<!-- 헤더 -->\n<Project><ProjectReference Include="..\\Pd.Ui\\Pd.Ui.csproj" /></Project>\n',
        src + "Pd.Ui.Views/Pd.Ui.Views.csproj": '<!-- 헤더 -->\n<Project><ProjectReference Include="..\\Pd.Poc.Shell\\Pd.Poc.Shell.csproj" /></Project>\n',
        src + "Pd.Client.Data/Pd.Client.Data.csproj": '<!-- 헤더 -->\n<Project></Project>\n',
        parts: part(Prefix="PART_", Header="PART_Header", Pin="PART_PinButton"),
        tpl: '<!-- 헤더 -->\n<Border x:Name="PART_Header" />\n',
    }
    dock = "PART_PinButton".encode("utf-16-le") + b"\0\0"
    cases = [
        ("정상 기준", {}, 0),
        ("Ui→Shell using", {src + "Pd.Ui/A.cs": "// 헤더\nusing Pd.Poc.Shell.Views;\n"}, 1),
        ("Ui→Shell 문자열 avares", {src + "Pd.Ui/A.cs": '// 헤더\nvar u = "avares://Pd.Poc.Shell/x";\n'}, 1),
        ("Ui→Shell axaml", {src + "Pd.Ui/A.axaml": '<!-- 헤더 -->\n<A xmlns:v="using:Pd.Poc.Shell.Views" />\n'}, 1),
        ("Ui→Data 코드", {src + "Pd.Ui/A.cs": "// 헤더\nusing Pd.Client.Data;\n"}, 1),
        ("Ui→Views 코드", {src + "Pd.Ui/A.cs": "// 헤더\nusing Pd.Ui.Views;\n"}, 1),
        ("주석 안 언급은 통과", {src + "Pd.Ui/A.cs": "// 헤더 Pd.Poc.Shell\nint a; // Pd.Ui.Views\n/* Pd.Client.Data */\n"}, 0),
        ("Ui→Shell 프로젝트", {src + "Pd.Ui/Pd.Ui.csproj": '<!-- 헤더 -->\n<Project><PackageReference Include="Avalonia" Version="12.1.4" /><ProjectReference Include="..\\Pd.Poc.Shell\\Pd.Poc.Shell.csproj" /></Project>\n'}, 1),
        ("Ui 허용 밖 패키지", {src + "Pd.Ui/Pd.Ui.csproj": '<!-- 헤더 -->\n<Project><PackageReference Include="Avalonia" Version="12.1.4" /><PackageReference Include="Dock.Avalonia" Version="1" /></Project>\n'}, 1),
        ("Ui 패키지 버전 불일치", {src + "Pd.Ui/Pd.Ui.csproj": '<!-- 헤더 -->\n<Project><PackageReference Include="Avalonia" Version="12.0.0" /></Project>\n'}, 1),
        ("Shell→Views 프로젝트", {src + "Pd.Poc.Shell/Pd.Poc.Shell.csproj": '<!-- 헤더 -->\n<Project><ProjectReference Include="../Pd.Ui.Views/Pd.Ui.Views.csproj" /></Project>\n'}, 1),
        ("Shell→Views 코드", {src + "Pd.Poc.Shell/A.cs": "using Pd.Ui.Views;\n"}, 1),
        ("Shell 에 axaml", {src + "Pd.Poc.Shell/A.axaml": "<A />\n"}, 1),
        ("Data→Shell 코드", {src + "Pd.Client.Data/A.cs": "// 헤더\nusing Pd.Poc.Shell.Docking;\n"}, 1),
        ("Views→Shell 허용", {src + "Pd.Ui.Views/Pd.Ui.Views.csproj": '<!-- 헤더 -->\n<Project><ProjectReference Include="..\\Pd.Poc.Shell\\Pd.Poc.Shell.csproj" /></Project>\n', src + "Pd.Ui.Views/A.cs": "// 헤더\nusing Pd.Poc.Shell.Views;\n"}, 0),
        ("Data→Avalonia 패키지", {src + "Pd.Client.Data/Pd.Client.Data.csproj": '<!-- 헤더 -->\n<Project><PackageReference Include="Avalonia" Version="12.1.4" /></Project>\n'}, 1),
        ("Data→Avalonia 코드", {src + "Pd.Client.Data/A.cs": "// 헤더\nusing Avalonia.Controls;\n"}, 1),
        ("Data→Shell 프로젝트", {src + "Pd.Client.Data/Pd.Client.Data.csproj": '<!-- 헤더 -->\n<Project><ProjectReference Include="..\\Pd.Poc.Shell\\Pd.Poc.Shell.csproj" /></Project>\n'}, 1),
        ("신규 파일 301줄", {src + "Pd.Ui/A.cs": ok + "\n" * 299}, 1),
        ("신규 파일 300줄 통과", {src + "Pd.Ui/A.cs": ok + "\n" * 298}, 0),
        ("헤더 없음", {src + "Pd.Ui/A.cs": "using System;\n"}, 1),
        ("영문 헤더", {src + "Pd.Ui/A.cs": "// english only\n"}, 1),
        ("BOM 뒤 헤더 통과", {src + "Pd.Ui/A.cs": "﻿// 정상 헤더\n"}, 0),
        ("Shell 기존 파일은 헤더 면제", {src + "Pd.Poc.Shell/A.cs": "using System;\n"}, 0),
        ("Shell 301줄", {src + "Pd.Poc.Shell/A.cs": "using System;\n" * 301}, 1),
        ("Shell 300줄 통과", {src + "Pd.Poc.Shell/A.cs": "using System;\n" * 300}, 0),
        ("Shell axaml 301줄", {src + "Pd.Poc.Shell/A.axaml": "\n" * 301}, 2),
        ("Shell PART_ 리터럴", {src + "Pd.Poc.Shell/A.cs": 'var n = "PART_Header";\n'}, 1),
        ("Shell 선택자 안 PART_", {src + "Pd.Poc.Shell/A.cs": 'var n = "#PART_Header";\n'}, 1),
        ("Shell 주석 PART_ 통과", {src + "Pd.Poc.Shell/A.cs": "// 템플릿 PART_Header\nint a;\n"}, 0),
        ("Shell 계약 상수 통과", {src + "Pd.Poc.Shell/A.cs": "var n = TemplateParts.Header;\n"}, 0),
        ("계약 파일 없음", {parts: None}, 1),
        ("계약 상수 없음", {parts: part(Prefix="PART_")}, 1),
        ("계약 값이 PART_ 아님", {parts: part(Header="Header")}, 1),
        ("계약 이름이 어디에도 없음", {parts: part(Ghost="PART_Ghost")}, 1),
        ("axaml 주석 안 정의는 불인정", {tpl: '<!-- 헤더 x:Name="PART_Header" -->\n<Border />\n'}, 1),
        ("axaml 선택자만 있으면 불인정", {tpl: '<!-- 헤더 -->\n<Style Selector="Border#PART_Header" />\n'}, 1),
        ("Name= 정의 통과", {tpl: '<!-- 헤더 -->\n<Border Name="PART_Header" />\n'}, 0),
        ("Dock 이름의 앞부분만 일치", {parts: part(Pin="PART_Pin")}, 1),
    ]
    failed = 0
    for name, extra, expected in cases:
        got = len(check_all({k: v for k, v in {**base, **extra}.items() if v is not None}, rules, dock))
        passed = (got > 0) == (expected > 0)
        failed += 0 if passed else 1
        print(f"{'ok  ' if passed else 'FAIL'} {name} 위반={got}")
    missing = check_all({}, rules)
    if len(missing) != 5:
        failed += 1
        print(f"FAIL 필수 프로젝트·계약 누락 위반={len(missing)}")
    no_dock = check_all(base, rules)
    if len(no_dock) != 1 or "Dock 패키지" not in no_dock[0]:
        failed += 1
        print(f"FAIL Dock 패키지 부재는 위반이어야 한다 위반={len(no_dock)}")
    print(f"self_test cases={len(cases) + 2} failed={failed}")
    return 0 if failed == 0 else 1


def main():
    started = time.perf_counter()
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8")
    here = os.path.dirname(os.path.abspath(__file__))
    parser = argparse.ArgumentParser(description="클라이언트 계층 정적 검사")
    parser.add_argument("--root", default=os.path.abspath(os.path.join(here, "..")))
    parser.add_argument("--rules", default=os.path.join(here, "check_layers_rules.json"))
    parser.add_argument("--self-test", action="store_true")
    args = parser.parse_args()
    with open(args.rules, encoding="utf-8") as f:
        rules = json.load(f)

    if args.self_test:
        rc = self_test(rules)
    else:
        try:
            # NTFS 입출력은 대기가 대부분이라 스레드로 나눠 처리한다
            with ThreadPoolExecutor(max_workers=24) as pool:
                targets = [rel for rel in list_files(args.root, rules, pool) if wanted(rel, rules)]
                files = {rel: text for rel, text in pool.map(lambda r: read_text(args.root, r), targets) if text is not None}
            versions = {m.group(1): m.group(2) for m in PACKAGE_REF.finditer(files.get(rules["template_parts"]["version_from"], ""))}
            violations = check_all(files, rules, load_dock_blob(versions, rules))
            for v in violations:
                print(f"VIOLATION {v}")
            print(f"layers={len(rules['layers'])} files={len(files)} violations={len(violations)}")
            rc = 1 if violations else 0
        except OSError as e:
            print(f"VIOLATION 검사 대상을 읽지 못했다: {e}")
            rc = 1
    print(f"elapsed_ms={int((time.perf_counter() - started) * 1000)}")
    return rc


if __name__ == "__main__":
    sys.exit(main())
