# B10 시험대 이동 정적 판정기 — views 의 차트·엑셀·벤치 참조 0, 셸 기본 창·이관 명령 등록 0, 시험대 모듈 연결·별칭 0 을 판정한다
"""
사용: python3 -B testbed_move_check.py [--root <영역 루트>] [--batch 1|2] [--json <경로>]
종료코드: 0=전부 통과, 1=위반 있음, 2=인자·트리 오류(검사한 파일이 0개 등)
규칙(batch 1)
  R1 views(bin·obj 제외)의 .cs·.axaml·.csproj 에 LiveCharts|ClosedXML|ScottPlot|OxyPlot|ProCharts|TheOne|Documents.Bench 식별자 0
  R2 testbed csproj 에 위 패키지(ClosedXML·ScottPlot·ProCharts·LiveCharts·OxyPlot·TheOne) 참조가 존재
  R3 3호스트 어댑터(Desktop·Browser·Android UiHost)에 AddModule<TestbedModule>
  R4 셸 Framework/ShellDefaultWindows.cs 부재
  R5 셸 소스에서 DemoOpen·DemoOpenSdi·BenchOpen 참조는 ShellCommandIds.cs(상수 정의)만 허용(등록 호출 0)
  R6 testbed 소스에 Internal 네임스페이스·BindingFlags.NonPublic 0
  R7 testbed 소스의 Aliases 대입에 옛 이름 별칭 Grid·Gantt·Form 0
  R7b src 의 상수 배열·사전·컬렉션 초기화에 옛 창·명령 ID 문자열 별칭(\"xkxk.demo.*\"·\"xkxk.bench.*\"·\"xkxk.grid|gantt|form\") 0
      (주석은 제외한다. 상수·switch 로 정의한 변환표는 배열·사전이 아니라 대상이 아니다)
규칙(batch 2, 데모 문서 3종·간트 캔버스 이동)
  R8 views(아래 풀·예열 파일 제외)의 .cs·.axaml 에 GridDocumentView|GanttDocumentView|FormDocumentView|GanttCanvas 식별자 0(.cs 는 주석 제외)
  R9 DocumentPrewarm.cs·GridViewPool.cs·WindowViewPool*.cs 에 위 식별자와 GridScrollProbe|GridRouteProbe 참조 0(주석 제외)
  R10 이동 11파일이 testbed/xkxk.frameworks.ui.testbed/Documents 에 존재하고 views/Documents 에는 없음
끝 줄: testbed_move batch=N rules=R result=PASS|FAIL
"""
import argparse
import json
import os
import re
import sys

SKIP_DIRS = {"bin", "obj", ".git", "node_modules", "__pycache__"}
VIEWS = "src/xkxk.frameworks.ui.views"
SHELL = "src/xkxk.frameworks.ui.shell"
TESTBED = "testbed/xkxk.frameworks.ui.testbed"
HOSTS = ["src/xkxk.frameworks.ui.desktop/DesktopUiHost.cs",
         "src/xkxk.frameworks.ui.browser/BrowserUiHost.cs",
         "src/xkxk.frameworks.ui.android/AndroidUiHost.cs"]
BANNED_IN_VIEWS = re.compile(r"LiveCharts|ClosedXML|ScottPlot|OxyPlot|ProCharts|TheOne|Documents\.Bench")
TESTBED_PKG = re.compile(r"ClosedXML|ScottPlot|ProCharts|LiveCharts|OxyPlot|TheOne")
MOVED_CMD = re.compile(r"\b(DemoOpenSdi|DemoOpen|BenchOpen)\b")
INTERNAL_NS = re.compile(r"\bxkxk\.frameworks\.ui(\.[A-Za-z0-9_]+)*\.Internal\b|\bBindingFlags\.NonPublic\b")
ALIAS_LINE = re.compile(r"Aliases")
COLLECTION_CTX = re.compile(r"\[\]|Dictionary<|HashSet<|List<|new\s*\[|=\s*\{|\{\s*\"|\[\s*\"")
OLD_ID_STR = re.compile(r"\"xkxk\.(?:demo|bench)\.[A-Za-z0-9_.]*\"|\"xkxk\.(?:grid|gantt|form)\"")
_DEMO = ["GridDocumentView", "GanttDocumentView", "FormDocumentView", "GanttCanvas"]
DEMO_TYPES = re.compile(r"\b(" + "|".join(_DEMO) + r")\b")
DEMO_REFS = re.compile(r"\b(" + "|".join(_DEMO + ["GridScrollProbe", "GridRouteProbe"]) + r")\b")
POOL_FILES = re.compile(r"^(DocumentPrewarm|GridViewPool|WindowViewPool(\..+)?)\.cs$")
MOVED = ["GridDocumentView.axaml", "GridDocumentView.axaml.cs", "GridScrollProbe.cs", "GridRouteProbe.cs",
         "GanttDocumentView.axaml", "GanttDocumentView.axaml.cs", "GanttCanvas.cs", "GanttCanvas.Dense.cs",
         "GanttCanvas.Render.cs", "FormDocumentView.axaml", "FormDocumentView.axaml.cs"]
OLD_ALIAS = re.compile(r"\"(grid|gantt|form)\"", re.IGNORECASE)


def strip_comments(text):
    text = re.sub(r"/\*.*?\*/", lambda m: "\n" * m.group(0).count("\n"), text, flags=re.S)
    return re.sub(r"//[^\n]*", "", text)


def walk(root, rel, exts):
    base = os.path.join(root, rel)
    for d, dirs, files in os.walk(base):
        dirs[:] = [x for x in dirs if x not in SKIP_DIRS]
        for f in files:
            if f.endswith(exts):
                yield os.path.join(d, f)


def read(path):
    with open(path, encoding="utf-8-sig", errors="replace") as fh:
        return fh.read()


def lines_hit(text, rx, strip=True):
    src = strip_comments(text) if strip else text
    return [i + 1 for i, ln in enumerate(src.split("\n")) if rx.search(ln)]


def check_batch1(root):
    v, scanned = [], 0
    rel = lambda p: os.path.relpath(p, root).replace(os.sep, "/")
    for p in walk(root, VIEWS, (".cs", ".axaml", ".csproj")):
        scanned += 1
        t = read(p)
        for n in lines_hit(t, BANNED_IN_VIEWS, strip=not p.endswith(".csproj")):
            v.append({"rule": "R1", "file": rel(p), "line": n})
    tb_csprojs = [p for p in walk(root, TESTBED, (".csproj",))]
    if not any(TESTBED_PKG.search(read(p)) for p in tb_csprojs):
        v.append({"rule": "R2", "file": TESTBED, "line": 0})
    for h in HOSTS:
        p = os.path.join(root, h)
        if not os.path.isfile(p) or "AddModule<TestbedModule>" not in strip_comments(read(p)):
            v.append({"rule": "R3", "file": h, "line": 0})
    if os.path.exists(os.path.join(root, SHELL, "Framework/ShellDefaultWindows.cs")):
        v.append({"rule": "R4", "file": SHELL + "/Framework/ShellDefaultWindows.cs", "line": 0})
    for p in walk(root, SHELL, (".cs",)):
        scanned += 1
        if os.path.basename(p) == "ShellCommandIds.cs":
            continue
        for n in lines_hit(read(p), MOVED_CMD):
            v.append({"rule": "R5", "file": rel(p), "line": n})
    for p in walk(root, TESTBED, (".cs",)):
        scanned += 1
        t = strip_comments(read(p))
        for n in lines_hit(t, INTERNAL_NS, strip=False):
            v.append({"rule": "R6", "file": rel(p), "line": n})
        for i, ln in enumerate(t.split("\n")):
            if ALIAS_LINE.search(ln) and OLD_ALIAS.search(ln):
                v.append({"rule": "R7", "file": rel(p), "line": i + 1})
    for p in walk(root, "src", (".cs",)):
        scanned += 1
        for i, ln in enumerate(strip_comments(read(p)).split("\n")):
            if COLLECTION_CTX.search(ln) and OLD_ID_STR.search(ln):
                v.append({"rule": "R7", "file": rel(p), "line": i + 1})
    return v, scanned


def check_batch2(root):
    v, scanned = [], 0
    rel = lambda p: os.path.relpath(p, root).replace(os.sep, "/")
    for p in walk(root, VIEWS, (".cs", ".axaml")):
        scanned += 1
        pool = bool(POOL_FILES.match(os.path.basename(p)))
        rx, rule = (DEMO_REFS, "R9") if pool else (DEMO_TYPES, "R8")
        text = read(p)
        if p.endswith(".axaml"):  # XML 주석은 줄 수를 유지하며 비운다
            text = re.sub(r"<!--.*?-->", lambda m: "\n" * m.group(0).count("\n"), text, flags=re.S)
        for n in lines_hit(text, rx, strip=p.endswith(".cs")):
            v.append({"rule": rule, "file": rel(p), "line": n})
    for f in MOVED:
        if not os.path.isfile(os.path.join(root, TESTBED, "Documents", f)):
            v.append({"rule": "R10", "file": TESTBED + "/Documents/" + f, "line": 0})
        if os.path.exists(os.path.join(root, VIEWS, "Documents", f)):
            v.append({"rule": "R10", "file": VIEWS + "/Documents/" + f, "line": 0})
    return v, scanned


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--root", default=os.path.abspath(os.path.join(os.path.dirname(__file__), "../../..")))
    ap.add_argument("--batch", type=int, default=1, choices=[1, 2])
    ap.add_argument("--json")
    a = ap.parse_args()
    if not os.path.isdir(os.path.join(a.root, "src")):
        print("src 폴더가 없습니다: " + a.root, file=sys.stderr)
        return 2
    v, scanned = (check_batch1 if a.batch == 1 else check_batch2)(a.root)
    if scanned == 0:
        print("검사한 파일이 0개입니다", file=sys.stderr)
        return 2
    for x in v:
        print("VIOLATION %s %s:%d" % (x["rule"], x["file"], x["line"]))
    rules = sorted({x["rule"] for x in v})
    res = "PASS" if not v else "FAIL"
    print("testbed_move batch=%d scanned=%d violations=%d rules=%s result=%s"
          % (a.batch, scanned, len(v), ",".join(rules) or "-", res))
    if a.json:
        with open(a.json, "w", encoding="utf-8") as fh:
            json.dump({"batch": a.batch, "scanned": scanned, "violations": v, "result": res}, fh, ensure_ascii=False)
    return 0 if not v else 1


if __name__ == "__main__":
    sys.exit(main())
