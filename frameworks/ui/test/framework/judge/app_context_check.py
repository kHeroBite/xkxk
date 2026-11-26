# 옛 정적 접근자(App.MainViewModel·App.Shell·ViewRegistry.Current·Loc.Instance) 직접 참조와 정의 파일 위임 형태를 확인하는 판정기 — B04 끝 조건
# 사용: app_context_check.py [--root <R>] [--scope all|defs] [--report] [--self-test] [--help]
#   refs : src 아래 *.cs·*.axaml(bin·obj 제외, 주석 //·/* */·<!-- --> 제외)에서 옛 정적 접근자 직접 참조와 using static 을 센다
#   defs : 정의 파일 3개가 위임 접근자만 남겼는지 본다 — App.cs 두 속성 '=> XkxkUi.Current.' 위임·자동 속성 0·App 안 대입 0,
#          ViewRegistry.cs 정적 IViewRegistry 필드 0·XkxkUi.Current.Views/RegisterViews 사용, Loc.cs 'public static Loc Instance =>'
#          (정의 줄 자체는 'App.'·'Loc.' 한정이 없으므로 refs 에 잡히지 않는다 — 위임 접근자 선언은 허용된다)
#   --scope defs : defs 만 본다(파일 수 하한 없음)
#   --report     : 파일별 남은 참조 수(REMAIN <경로> <수>)와 끝 줄만 출력한다, rc 0(훑은 파일 <100 이면 2)
# 출력: REF <상대경로>:<줄> <일치> / DEF FAIL <사유> / 끝 줄 'files=N refs=M defs=ok|fail'
# 종료코드: 0=PASS(refs=0·defs=ok), 1=FAIL, 2=인자 오류·대상 없음·훑은 파일 N<100(판정불가)
import os
import re
import shutil
import sys
import tempfile

SRC = "src"
SKIP_DIRS = {"bin", "obj"}
MIN_FILES = 100
REF = re.compile(r"(?<![\w.])(?:(?:global::)?xkxk\.frameworks\.ui\.shell\.)?App\.(?:MainViewModel|Shell)\b|\bViewRegistry\.Current\b|\bLoc\.Instance\b"
                 r"|\busing\s+static\s+(?:global::)?xkxk\.frameworks\.ui\.shell\.(?:App|ViewRegistry|Localization\.Loc)\s*;")
DEF_APP = "src/xkxk.frameworks.ui.shell/App.cs"
DEF_VIEWS = "src/xkxk.frameworks.ui.shell/ViewRegistry.cs"
DEF_LOC = "src/xkxk.frameworks.ui.shell/Localization/Loc.cs"
USAGE = "사용: app_context_check.py [--root <R>] [--scope all|defs] [--report] [--self-test] [--help]"


def strip_cs(text):
    """C# 주석을 공백으로 바꾸되 줄 수는 유지한다. 문자열·문자 리터럴 안의 // 는 건드리지 않는다."""
    out, i, n = [], 0, len(text)
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
        elif (c == "@" and nx == '"') or (c == "$" and nx == "@") or (c == "@" and nx == "$"):
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


def strip_xml(text):
    """<!-- --> 주석을 공백으로 바꾸되 줄 수는 유지한다."""
    return re.sub(r"<!--.*?-->", lambda m: re.sub(r"[^\n]", " ", m.group(0)), text, flags=re.S)


def find_refs(text, axaml=False):
    """(줄, 일치문자열) 목록."""
    body = strip_xml(text) if axaml else strip_cs(text)
    hits = []
    for no, line in enumerate(body.split("\n"), 1):
        for m in REF.finditer(line):
            hits.append((no, " ".join(m.group(0).split())))
    return hits


def check_defs(app, views, loc):
    """정의 파일 본문 3개(주석 포함 원문) → 실패 사유 목록(빈 목록이면 ok)."""
    bad = []
    a, v, l = strip_cs(app), strip_cs(views), strip_cs(loc)
    if not re.search(r"public\s+static\s+MainViewModel\?\s*MainViewModel\s*=>\s*XkxkUi\.Current\.", a):
        bad.append("App.MainViewModel 이 '=> XkxkUi.Current.' 위임이 아님")
    if not re.search(r"public\s+static\s+IShellCommands\?\s*Shell\s*=>\s*XkxkUi\.Current\.", a):
        bad.append("App.Shell 이 '=> XkxkUi.Current.' 위임이 아님")
    if re.search(r"\b(?:MainViewModel|Shell)\s*\{\s*(?:(?:public|private|internal|protected)\s+)?get\s*;", a):
        bad.append("App 에 MainViewModel·Shell 자동 속성이 남음")
    if re.search(r"(?:(?<![\w.])|\bApp\.)(?:MainViewModel|Shell)\s*(?:=(?![=>])|\?\?=)", a):
        bad.append("App 안에 MainViewModel·Shell 대입이 남음")
    if re.search(r"\bstatic\s+(?:readonly\s+|volatile\s+)*IViewRegistry\??\s+\w+\s*[;=]", v):
        bad.append("ViewRegistry 에 정적 IViewRegistry 필드가 남음")
    if "XkxkUi.Current.Views" not in v or "XkxkUi.Current.RegisterViews" not in v:
        bad.append("ViewRegistry.Current 가 XkxkUi.Current.Views/RegisterViews 위임이 아님")
    if not re.search(r"public\s+static\s+Loc\s+Instance\s*=>", l):
        bad.append("Loc.Instance 가 'public static Loc Instance =>' 위임이 아님")
    if re.search(r"\bInstance\s*\{\s*get\s*;\s*\}\s*=\s*new\b", l):
        bad.append("Loc.Instance 가 '{ get; } = new' 로 남음")
    return bad


def read(path):
    with open(path, encoding="utf-8-sig", errors="replace") as f:
        return f.read()


def scan_refs(root):
    """(hits, files). src 가 없으면 None. hits = [(상대경로, 줄, 일치)]."""
    base = os.path.join(root, SRC)
    if not os.path.isdir(base):
        print("대상 폴더 없음: %s" % SRC, file=sys.stderr)
        return None
    hits, files = [], 0
    for dp, dns, fns in os.walk(base):
        dns[:] = sorted(d for d in dns if d not in SKIP_DIRS)
        for fn in sorted(fns):
            if not fn.endswith((".cs", ".axaml")):
                continue
            path = os.path.join(dp, fn)
            files += 1
            rel = os.path.relpath(path, root).replace(os.sep, "/")
            for no, m in find_refs(read(path), fn.endswith(".axaml")):
                hits.append((rel, no, m))
    return hits, files


def scan_defs(root):
    """실패 사유 목록. 정의 파일이 없으면 None."""
    paths = [os.path.join(root, p) for p in (DEF_APP, DEF_VIEWS, DEF_LOC)]
    for p in paths:
        if not os.path.isfile(p):
            print("정의 파일 없음: %s" % os.path.relpath(p, root), file=sys.stderr)
            return None
    return check_defs(*[read(p) for p in paths])


GOOD_APP = ("public partial class App {\n    public static IShellCommands? Shell => XkxkUi.Current.Shell;\n"
            "    public static MainViewModel? MainViewModel => XkxkUi.Current.MainViewModel;\n"
            "    void Init() { var vm = new MainViewModel(s); UiAppContext.Instance.SetMain(vm); vm.Shell = 1; if (MainViewModel == null) { } }\n}\n")
GOOD_VIEWS = ("public static class ViewRegistry {\n    public static IViewRegistry Current\n    {\n"
              "        get => XkxkUi.Current.Views;\n        set => XkxkUi.Current.RegisterViews(value);\n    }\n}\n")
GOOD_LOC = ("public sealed class Loc : INotifyPropertyChanged, IUiLocalizer {\n"
            "    // 옛 형태: public static Loc Instance { get; } = new();\n"
            "    public static Loc Instance => UiAppContext.Instance.CoreLoc;\n}\n")


def self_test():
    ref_cases = [
        ("ref_app_main_detect", "var v = App.MainViewModel;", False, 1),
        ("ref_qualified_shell_detect", "xkxk.frameworks.ui.shell.App.Shell?.Run();", False, 1),
        ("ref_registry_loc_detect", "ViewRegistry.Current.Open(x); var s = Loc.Instance[\"k\"];", False, 2),
        ("ref_using_static_detect", "using static xkxk.frameworks.ui.shell.App;\nusing static global::xkxk.frameworks.ui.shell.Localization.Loc;", False, 2),
        ("ref_lookalike_ignored", "PdApp.MainViewModel; AndroidApp.Shell; UiAppContext.Instance.CoreLoc; XkxkUi.Current.Views;"
                                  " Loc.Tr(\"k\"); App.ShellCommands; CoreLoc.Instance; using static xkxk.frameworks.ui.shell.Theming.X;", False, 0),
        ("ref_instance_member_app_ignored", "var s = c.App.Shell; ctx.App.MainViewModel; x.y.App.Shell?.Run();", False, 0),
        ("ref_global_qualified_detect", "global::xkxk.frameworks.ui.shell.App.MainViewModel.X(); if (App.Shell != null) { }", False, 2),
        ("ref_comment_ignored", "// App.Shell\n/* Loc.Instance\n ViewRegistry.Current */ var u = \"http://x\"; // App.MainViewModel", False, 0),
        ("ref_axaml_detect_comment_ignored", "<!-- {x:Static s:App.MainViewModel}\n -->\n<X Source=\"{x:Static s:ViewRegistry.Current}\"/>", True, 1),
    ]
    bad = 0
    for name, src, axaml, want in ref_cases:
        got = len(find_refs(src, axaml))
        ok = got == want
        bad += 0 if ok else 1
        print("SELFTEST %s %s (기대 %d, 실측 %d)" % ("PASS" if ok else "FAIL", name, want, got))
    def_cases = [
        ("defs_delegate_ok", GOOD_APP, GOOD_VIEWS, GOOD_LOC, 0),
        ("defs_auto_property_fail", GOOD_APP.replace("Shell => XkxkUi.Current.Shell;", "Shell { get; private set; }"), GOOD_VIEWS, GOOD_LOC, 2),
        ("defs_app_assign_fail", GOOD_APP.replace("vm.Shell = 1;", "MainViewModel = vm; Shell = vm.Commands;"), GOOD_VIEWS, GOOD_LOC, 1),
        ("defs_static_field_fail", GOOD_APP, GOOD_VIEWS.replace("{\n", "{\n    private static IViewRegistry? s_current;\n", 1), GOOD_LOC, 1),
        ("defs_loc_new_fail", GOOD_APP, GOOD_VIEWS, GOOD_LOC.replace("Instance => UiAppContext.Instance.CoreLoc;", "Instance { get; } = new();"), 2),
    ]
    for name, a, v, l, want in def_cases:
        got = len(check_defs(a, v, l))
        ok = got == want
        bad += 0 if ok else 1
        print("SELFTEST %s %s (기대 실패 %d, 실측 %d)" % ("PASS" if ok else "FAIL", name, want, got))
    tmp = tempfile.mkdtemp()
    try:
        ok = scan_refs(tmp) is None and scan_defs(tmp) is None
        print("SELFTEST %s missing_target_unjudgeable" % ("PASS" if ok else "FAIL"))
        bad += 0 if ok else 1
        os.makedirs(os.path.join(tmp, SRC, "p", "bin"))
        with open(os.path.join(tmp, SRC, "p", "A.cs"), "w", encoding="utf-8") as f:
            f.write("class A { void M() { var x = App.Shell; } }\n")
        with open(os.path.join(tmp, SRC, "p", "bin", "B.cs"), "w", encoding="utf-8") as f:
            f.write("class B { void M() { var x = Loc.Instance; } }\n")
        r = scan_refs(tmp)
        ok = r is not None and len(r[0]) == 1 and r[1] == 1
        print("SELFTEST %s dir_scan_detect_bin_pruned" % ("PASS" if ok else "FAIL"))
        bad += 0 if ok else 1
        rc = run(tmp, "all", False, quiet=True)
        ok = rc == 2
        print("SELFTEST %s few_files_unjudgeable (기대 rc 2, 실측 %d)" % ("PASS" if ok else "FAIL", rc))
        bad += 0 if ok else 1
    finally:
        shutil.rmtree(tmp, ignore_errors=True)
    return 1 if bad else 0


def run(root, scope, report, quiet=False):
    say = (lambda *a, **k: None) if quiet else print
    defs = scan_defs(root)
    if defs is None:
        return 2
    if scope == "defs":
        for d in defs:
            say("DEF FAIL %s" % d)
        say("files=0 refs=skip defs=%s" % ("fail" if defs else "ok"))
        return 1 if defs else 0
    r = scan_refs(root)
    if r is None:
        return 2
    hits, files = r
    if report:
        per = {}
        for rel, _, _ in hits:
            per[rel] = per.get(rel, 0) + 1
        for rel in sorted(per):
            say("REMAIN %s %d" % (rel, per[rel]))
        say("REMAIN_FILES %d" % len(per))
    else:
        for rel, no, m in hits:
            say("REF %s:%d %s" % (rel, no, m))
        for d in defs:
            say("DEF FAIL %s" % d)
    say("files=%d refs=%d defs=%s" % (files, len(hits), "fail" if defs else "ok"))
    if files < MIN_FILES:
        print("훑은 파일 %d < %d — 대상 소실 의심(판정불가)" % (files, MIN_FILES), file=sys.stderr)
        return 2
    if report:
        return 0
    return 1 if hits or defs else 0


def main(argv):
    root = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", ".."))
    args, scope, report = argv[1:], "all", False
    if args in (["--help"], ["-h"]):
        print(USAGE)
        return 0
    if args == ["--self-test"]:
        return self_test()
    i = 0
    while i < len(args):
        if args[i] == "--root" and i + 1 < len(args):
            root = os.path.abspath(args[i + 1])
            i += 2
        elif args[i] == "--scope" and i + 1 < len(args) and args[i + 1] in ("all", "defs"):
            scope = args[i + 1]
            i += 2
        elif args[i] == "--report":
            report = True
            i += 1
        else:
            print(USAGE, file=sys.stderr)
            return 2
    return run(root, scope, report)


if __name__ == "__main__":
    sys.exit(main(sys.argv))
