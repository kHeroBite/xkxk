# B09 창 레지스트리 판정기 자체 시험(T-JUDGE-SELF) — 임시 트리의 위반·비위반·빈 폴더 표본으로 rc·목록·JSON 키를 확인한다
"""
사용: python3 -B window_registry_selftest.py   (rc 0=전부 통과, 1=실패 있음)
임시 폴더는 사례마다 만들고 바로 지운다. 판정기는 실제 명령줄(하위 프로세스)로 부른다.
"""
import json
import os
import shutil
import subprocess
import sys
import tempfile

CHECK = os.path.join(os.path.dirname(os.path.abspath(__file__)), "window_registry_check.py")
SH = "src/xkxk.frameworks.ui.shell/"
FW = SH + "Framework/"
VIEW = "src/xkxk.frameworks.ui.views/A.cs"
CLEAN = "// 정상 표본\nclass A { }\n"

# 주석·문자열·문자·더 긴 식별자 — 하나도 세지 않아야 한다
NOT_VIOLATION = '''// DocumentKind 설명
/* enum DocumentKind { Grid }
   DocumentKind.Form */
#region DocumentKind 옛 코드
class A
{
    /// <summary>옛 DocumentKind 값</summary>
    string a = "DocumentKind.Grid // 아님";
    string b = @"여러 줄 ""DocumentKind""
DocumentKind";
    string c = $"{{DocumentKind}} {x} \\" DocumentKind";
    string d = $@"{{DocumentKind}} ""DocumentKind"" {x}";
    string e = """
        DocumentKind "따옴표" {DocumentKind}
        """;
    string f = $$"""
        {DocumentKind} {{x}}
        """;
    char g = '"'; string h = "DocumentKind"; char i = '\\''; // DocumentKind
    int DocumentKindNames, OldDocumentKind, documentkind;
}
#endregion
'''

# (이름, {경로: 내용}, 기대 rc, 기대 violations [(파일, 줄, kind)], 기대 avalonia [(파일, 줄)])
CASES = [
    ("clean_comments_strings_longer_ids", {VIEW: NOT_VIOLATION}, 0, [], []),
    ("identifier", {VIEW: "class A\n{\n    object k = DocumentKind.Grid;\n}\n"}, 1, [(VIEW, 3, "identifier")], []),
    ("enum_declaration", {SH + "Docking/K.cs": "public enum DocumentKind\n{\n    Grid,\n}\n"}, 1,
     [(SH + "Docking/K.cs", 1, "enum_declaration")], []),
    ("interpolation_hole", {VIEW: 'class A\n{\n    string s = $"종류 {DocumentKind.Grid} 끝";\n}\n'}, 1, [(VIEW, 3, "identifier")], []),
    ("raw_interpolation_hole", {VIEW: 'class A\n{\n    string s = $$"""\n        {x} {{DocumentKind.Form}}\n        """;\n}\n'}, 1,
     [(VIEW, 4, "identifier")], []),
    ("after_quote_char_literal", {VIEW: "class A { char q = '\"'; object k = DocumentKind.Form; string t = \"x\"; }\n"}, 1,
     [(VIEW, 1, "identifier")], []),
    ("after_multiline_comment_line_number", {VIEW: '/*\n\n*/\nvar a = @"\n\n";\nDocumentKind k;\n'}, 1, [(VIEW, 7, "identifier")], []),
    ("framework_avalonia", {FW + "UiWindowOptions.cs": "// 창 옵션\nusing Avalonia.Controls;\nclass A { }\n",
                            FW + "IUiWindowRegistry.cs": "interface I { global::Avalonia.Controls.Control C(); }\n"}, 1, [],
     [(FW + "IUiWindowRegistry.cs", 1), (FW + "UiWindowOptions.cs", 2)]),
    ("framework_avalonia_comment_ok", {FW + "UiWindowOptions.cs": '/// <c>Avalonia</c>\nclass A { string s = "Avalonia"; }\n'}, 0, [], []),
    ("avalonia_outside_scope_ok", {FW + "XkxkUi.cs": "using Avalonia;\n", VIEW: "using Avalonia;\n"}, 0, [], []),
    ("bin_obj_excluded", {VIEW: CLEAN, "src/p/bin/B.cs": "DocumentKind k;\n", "src/p/obj/Debug/O.cs": "DocumentKind k;\n"}, 0, [], []),
    ("only_bin_obj_is_empty", {"src/p/bin/B.cs": CLEAN, "src/p/obj/O.cs": CLEAN}, 2, [], []),
    ("empty_src", {"src/readme.txt": "x"}, 2, [], []),
    ("no_src", {"other/A.cs": CLEAN}, 2, [], []),
]


def run(root, *extra):
    return subprocess.run([sys.executable, "-B", CHECK, "--root", root, *extra], capture_output=True, text=True, timeout=60)


def build(root, files):
    for rel, content in files.items():
        p = os.path.join(root, rel)
        os.makedirs(os.path.dirname(p), exist_ok=True)
        with open(p, "w", encoding="utf-8") as f:
            f.write(content)


def judge(case):
    name, files, rc, violations, avalonia = case
    root = tempfile.mkdtemp(prefix="b09_winreg_")
    try:
        build(root, files)
        js, plain = run(root, "--json"), run(root)
        d = json.loads(js.stdout)
        got_v = [(v["file"], v["line"], v["kind"]) for v in d["violations"]]
        got_a = [(v["file"], v["line"]) for v in d["avalonia_in_framework"]]
        problems = []
        if (js.returncode, plain.returncode, d["rc"]) != (rc, rc, rc):
            problems.append("rc json=%d text=%d 필드=%s 기대 %d" % (js.returncode, plain.returncode, d["rc"], rc))
        if sorted(d) != ["avalonia_in_framework", "files_checked", "rc", "violations"]:
            problems.append("JSON 키 %s" % sorted(d))
        if got_v != violations or got_a != avalonia:
            problems.append("목록 violations=%s avalonia=%s" % (got_v, got_a))
        if (d["files_checked"] == 0) != (rc == 2):
            problems.append("files_checked=%d" % d["files_checked"])
        if any("%s:%d" % (f, line) not in plain.stdout for f, line, *_ in violations + avalonia):
            problems.append("글 출력에 파일:줄 없음")
        return name, problems
    finally:
        shutil.rmtree(root, ignore_errors=True)


def judge_unreadable():
    """해석할 수 없는 바이트(UTF-8 아님)는 통과가 아니라 rc 2."""
    root = tempfile.mkdtemp(prefix="b09_winreg_")
    try:
        os.makedirs(os.path.join(root, "src"))
        with open(os.path.join(root, "src", "A.cs"), "wb") as f:
            f.write(b"\xff\xfe\x00class")
        r = run(root, "--json")
        ok = r.returncode == 2 and json.loads(r.stdout)["rc"] == 2
        return "unreadable_is_error", [] if ok else ["rc=%d %s" % (r.returncode, r.stdout[:200])]
    finally:
        shutil.rmtree(root, ignore_errors=True)


def main():
    results = [judge(c) for c in CASES] + [judge_unreadable()]
    for name, problems in results:
        print("%s %s %s" % ("PASS" if not problems else "FAIL", name, "; ".join(problems)))
    failed = sum(1 for _, p in results if p)
    print("자체 시험 %d/%d %s" % (len(results) - failed, len(results), "PASS" if not failed else "FAIL"))
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
