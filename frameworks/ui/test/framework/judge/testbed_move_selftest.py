# B10 시험대 이동 판정기 자체 시험 — 가짜 트리의 정상·위반 표본으로 rc 와 규칙 번호를 확인한다
"""
사용: python3 -B testbed_move_selftest.py   (rc 0=전부 통과, 1=실패 있음)
임시 폴더는 사례마다 만들고 바로 지운다. 판정기는 하위 프로세스로 부른다.
"""
import os
import shutil
import subprocess
import sys
import tempfile

CHECK = os.path.join(os.path.dirname(os.path.abspath(__file__)), "testbed_move_check.py")
HOSTS = {
    "src/xkxk.frameworks.ui.desktop/DesktopUiHost.cs": "class A { void M(){ b.AddModule<TestbedModule>(); } }\n",
    "src/xkxk.frameworks.ui.browser/BrowserUiHost.cs": "class A { void M(){ b.AddModule<TestbedModule>(); } }\n",
    "src/xkxk.frameworks.ui.android/AndroidUiHost.cs": "class A { void M(){ b.AddModule<TestbedModule>(); } }\n",
}
GOOD = dict(HOSTS)
GOOD.update({
    "src/xkxk.frameworks.ui.views/V.cs": "// ClosedXML 설명 주석\nclass V { }\n",
    "src/xkxk.frameworks.ui.shell/Framework/ShellCommandIds.cs": "class C { const string DemoOpen = \"x\"; }\n",
    "src/xkxk.frameworks.ui.shell/Framework/Other.cs": "class O { }\n",
    "testbed/xkxk.frameworks.ui.testbed/T.csproj": "<PackageReference Include=\"ClosedXML\" />\n",
    "testbed/xkxk.frameworks.ui.testbed/T.cs": "class T { string[] Aliases = { \"doc.grid\" }; }\n",
})

# (이름, 덮어쓸 {경로: 내용 | None=삭제}, 기대 rc, 기대 규칙 집합)
CASES = [
    ("clean", {}, 0, set()),
    ("r1_views_chart", {"src/xkxk.frameworks.ui.views/V.cs": "using LiveChartsCore;\nclass V { }\n"}, 1, {"R1"}),
    ("r1_views_csproj", {"src/xkxk.frameworks.ui.views/v.csproj": "<PackageReference Include=\"ScottPlot.Avalonia\" />\n"}, 1, {"R1"}),
    ("r1_bin_ignored", {"src/xkxk.frameworks.ui.views/bin/X.cs": "using ClosedXML;\n"}, 0, set()),
    ("r2_no_package", {"testbed/xkxk.frameworks.ui.testbed/T.csproj": "<Project />\n"}, 1, {"R2"}),
    ("r3_host_missing", {"src/xkxk.frameworks.ui.android/AndroidUiHost.cs": "class A { }\n"}, 1, {"R3"}),
    ("r4_default_windows", {"src/xkxk.frameworks.ui.shell/Framework/ShellDefaultWindows.cs": "class D { }\n"}, 1, {"R4"}),
    ("r5_shell_register", {"src/xkxk.frameworks.ui.shell/Framework/Other.cs": "class O { void M(){ Reg(ShellCommandIds.BenchOpen); } }\n"}, 1, {"R5"}),
    ("r5_comment_ok", {"src/xkxk.frameworks.ui.shell/Framework/Other.cs": "// DemoOpen 상수 설명\nclass O { }\n"}, 0, set()),
    ("r6_internal", {"testbed/xkxk.frameworks.ui.testbed/T.cs": "using xkxk.frameworks.ui.shell.Internal;\nclass T { }\n"}, 1, {"R6"}),
    ("r6_nonpublic", {"testbed/xkxk.frameworks.ui.testbed/T.cs": "class T { var f = BindingFlags.NonPublic; }\n"}, 1, {"R6"}),
    ("r7b_src_array", {"src/xkxk.frameworks.ui.shell/Framework/Other.cs": 'class O { static readonly string[] K = { "xkxk.demo.grid" }; }\n'}, 1, {"R7"}),
    ("r7b_src_dict", {"src/xkxk.frameworks.ui.shell/Framework/Other.cs": 'class O { var d = new Dictionary<string,string>{ ["xkxk.bench.open"] = "x" }; }\n'}, 1, {"R7"}),
    ("r7b_const_ok", {"src/xkxk.frameworks.ui.shell/Framework/Other.cs": 'class O { public const string G = "xkxk.demo.grid"; }\n'}, 0, set()),
    ("r7b_comment_ok", {"src/xkxk.frameworks.ui.shell/Framework/Other.cs": '// { "xkxk.demo.grid" } 설명\nclass O { }\n'}, 0, set()),
    ("r7_old_alias", {"testbed/xkxk.frameworks.ui.testbed/T.cs": "class T { string[] Aliases = { \"Grid\" }; }\n"}, 1, {"R7"}),
]


def build(root, over):
    files = dict(GOOD)
    files.update(over)
    for rel, content in files.items():
        if content is None:
            continue
        p = os.path.join(root, rel)
        os.makedirs(os.path.dirname(p), exist_ok=True)
        with open(p, "w", encoding="utf-8") as fh:
            fh.write(content)


def run(args):
    r = subprocess.run([sys.executable, "-B", CHECK] + args, capture_output=True, text=True)
    return r.returncode, r.stdout


def main():
    fail = 0
    for name, over, rc_want, rules_want in CASES:
        root = tempfile.mkdtemp(prefix="tbmove_")
        try:
            build(root, over)
            rc, out = run(["--root", root])
            got = {ln.split()[1] for ln in out.splitlines() if ln.startswith("VIOLATION")}
            ok = rc == rc_want and got == rules_want
            print("%s %s rc=%d rules=%s" % ("PASS" if ok else "FAIL", name, rc, ",".join(sorted(got)) or "-"))
            fail += 0 if ok else 1
        finally:
            shutil.rmtree(root, ignore_errors=True)
    root = tempfile.mkdtemp(prefix="tbmove_")
    try:
        rc, _ = run(["--root", root])  # src 없는 빈 트리는 rc 2
        ok = rc == 2
        print("%s empty_tree rc=%d" % ("PASS" if ok else "FAIL", rc))
        fail += 0 if ok else 1
        build(root, {})
        rc, out = run(["--root", root, "--batch", "2"])
        ok = rc == 5 and "NOT_IMPLEMENTED" in out
        print("%s batch2_not_implemented rc=%d" % ("PASS" if ok else "FAIL", rc))
        fail += 0 if ok else 1
    finally:
        shutil.rmtree(root, ignore_errors=True)
    print("testbed_move_selftest failed=%d result=%s" % (fail, "PASS" if fail == 0 else "FAIL"))
    return 1 if fail else 0


if __name__ == "__main__":
    sys.exit(main())
