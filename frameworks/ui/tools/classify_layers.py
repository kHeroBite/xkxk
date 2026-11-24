#!/usr/bin/env python3
# 클라이언트 소스 계층 분류기 — 파일별 using·형식 참조를 분석해 Data/Control/Ui/Views/Host/Diag 로 나누고 LAYERS.md 를 재생성한다
"""
사용: python3 tools/classify_layers.py [--check] [--json 경로] [--root 저장소]
  기본: LAYERS.md 재생성. --check: 기록된 분류와 다르면 rc=1. --json: 파일별 근거·참조 간선을 JSON 으로도 쓴다.
분류(먼저 걸린 것): Host 호스트 프로젝트 / Ui Pd.Ui / Views axaml·코드비하인드·Views·Themes·Input·Documents 의 Avalonia 코드·벤치·갤러리 /
  Diag 그 밖의 Diagnostics / Data Avalonia·Dock.Avalonia 무참조이고 참조 형식도 전부 Data(닫힘 계산) / Control 나머지.
예외는 같은 폴더 classify_layers_overrides.json 에 사유와 함께 둔다. 소스는 읽기만 한다. 형식 참조는 정규식 근사이고 빌드가 최종 판정이다.
"""
import argparse
import json
import os
import re
import sys
from collections import Counter
from concurrent.futures import ThreadPoolExecutor

SRC = "src"
HOSTS = ("Pd.Poc.Desktop", "Pd.Poc.Browser", "Pd.Poc.Android")
SHELL = "Pd.Poc.Shell"
FIXED = {"Pd.Ui": "Ui", "Pd.Ui.Views": "Views", "Pd.Client.Data": "Data"}
SKIP = {"bin", "obj", ".vs", "node_modules", "__pycache__"}
TOKEN = re.compile(r"(?s)@\"(?:[^\"]|\"\")*\"|\"\"\".*?\"\"\"|\$@?\"|\"(?:\\.|[^\"\\\n])*\"|'(?:\\.|[^'\\\n])+'|//[^\n]*|/\*.*?\*/")
DECL = re.compile(r"\b(class|struct|interface|enum|record(?:\s+(?:struct|class))?|delegate\s+[\w<>\[\],\s?.]+?)\s+([A-Z_]\w*)")
ACCESS = re.compile(r"\b(public|internal|private|protected)\b")
USING = re.compile(r"^\s*(?:global\s+)?using\s+(static\s+)?(?:(\w+)\s*=\s*)?([\w.]+)\s*;", re.M)
NS = re.compile(r"^\s*namespace\s+([\w.]+)", re.M)
IDENT = re.compile(r"[A-Za-z_]\w*")
XMLNS = re.compile(r"xmlns:(\w+)=\"using:([\w.]+)")
XCLASS = re.compile(r"x:Class=\"([\w.]+)\"")
XAML_COMMENT = re.compile(r"(?s)<!--.*?-->")
AVARES = re.compile(r"avares://([\w.]+)/([\w./#-]*)")
LINE_COMMENT = re.compile(r"//[^\n]*|/\*.*?\*/", re.S)
DEP_PATTERNS = {"avalonia": re.compile(r"\bAvalonia\b"), "dock_model": re.compile(r"\bDock\.Model\b"), "dock_avalonia": re.compile(r"\bDock\.(Avalonia|Settings)\b")}


def clean_cs(text):
    """주석과 일반 문자열을 지운다. 보간 문자열은 $" 표지만 지워 안의 식을 참조로 센다."""
    return TOKEN.sub(" ", text)


def scan(root):
    found = []
    stack = [SRC]
    while stack:
        rel = stack.pop()
        with os.scandir(os.path.join(root, rel)) as it:
            for e in it:
                sub = f"{rel}/{e.name}"
                if e.is_dir(follow_symlinks=False):
                    if e.name not in SKIP:
                        stack.append(sub)
                elif e.name.endswith((".cs", ".axaml")):
                    found.append(sub)
    return sorted(found)


def read(root, rel):
    with open(os.path.join(root, rel), encoding="utf-8", errors="replace") as f:
        return rel, f.read()


def parse_cs(rel, text):
    code = clean_cs(text)
    ns = (NS.search(code) or [None, ""])[1]
    usings = {m.group(3) for m in USING.finditer(code) if not m.group(1) and not m.group(2)}
    statics = {m.group(3) for m in USING.finditer(code) if m.group(1)}
    aliases = {m.group(3) for m in USING.finditer(code) if m.group(2)}
    block_ns = bool(re.search(r"^\s*namespace\s+[\w.]+\s*(\r?\n\s*)?\{", code, re.M))
    types = []
    for m in DECL.finditer(code):
        before = code[:m.start()]
        if before.count("{") - before.count("}") - (1 if block_ns else 0) != 0:
            continue
        head = code[before.rfind("\n") + 1:m.start()]
        acc = (ACCESS.search(head) or [None, "internal"])[1]
        types.append({"name": m.group(2), "access": acc, "partial": "partial" in head})
    idents = set(IDENT.findall(code))
    uncommented = LINE_COMMENT.sub(" ", text)  # check_layers 는 문자열 안 Avalonia 도 위반으로 본다
    deps = {name for name, pat in DEP_PATTERNS.items() if pat.search(uncommented)}
    return {"kind": "cs", "ns": ns, "usings": usings, "statics": statics, "aliases": aliases, "types": types,
            "idents": idents, "deps": deps, "code": code, "avares": AVARES.findall(text)}


def parse_axaml(rel, text):
    code = XAML_COMMENT.sub(" ", text)
    prefixes = dict(XMLNS.findall(code))
    refs = {(prefixes[p], t) for p, t in re.findall(r"\b(\w+):([A-Z]\w*)", code) if p in prefixes}
    cls = (XCLASS.search(code) or [None, None])[1]
    return {"kind": "axaml", "ns": cls.rsplit(".", 1)[0] if cls else "", "xclass": cls, "prefixes": prefixes, "refs": refs,
            "deps": {"avalonia"} | ({"dock_avalonia"} if any(v.startswith("Dock.Avalonia") for v in prefixes.values()) else set()),
            "avares": AVARES.findall(code), "types": []}


def build_table(infos):
    """프로젝트 안 최상위 형식 표: 이름 → [(네임스페이스, 파일)]. partial 은 파일이 여럿이다."""
    table = {}
    for rel, info in infos.items():
        if info["kind"] == "cs":
            for t in info["types"]:
                table.setdefault(t["name"], []).append((info["ns"], rel))
    return table


def references(rel, info, table):
    """이 파일이 참조하는 다른 파일 → 형식 이름 집합."""
    out = {}
    if info["kind"] == "axaml":
        for ns, name in info["refs"]:
            for tns, target in table.get(name, []):
                if tns == ns and target != rel:
                    out.setdefault(target, set()).add(name)
        return out
    for name in info["idents"] & table.keys():
        for tns, target in table[name]:
            if target == rel:
                continue
            visible = (info["ns"] == tns or info["ns"].startswith(tns + ".") or tns in info["usings"]
                       or f"{tns}.{name}" in info["statics"] | info["aliases"] or f"{tns}.{name}" in info["code"]
                       or f"{tns.rsplit('.', 1)[-1]}.{name}" in info["code"])
            if visible:
                out.setdefault(target, set()).add(name)
    return out


def project_of(rel):
    return rel[len(SRC) + 1:].split("/", 1)[0]


def initial_layer(rel, info, overrides):
    key = rel[len(SRC) + 1:]
    if key in overrides:
        return overrides[key]["layer"], overrides[key]["reason"]
    project = project_of(rel)
    inner = rel[len(SRC) + 1 + len(project) + 1:]
    if project in HOSTS:
        return "Host", "호스트 프로젝트"
    if project in FIXED:
        return FIXED[project], f"{project} 프로젝트"
    if inner.startswith("Documents/Bench/"):
        return "Views", "벤치 화면(진단 빌드 전용, 화면 코드)"
    if inner.startswith("Diagnostics/Gallery/"):
        return "Views", "컨트롤 갤러리 화면(진단 빌드 전용, 화면 코드)"
    if inner.startswith("Diagnostics/"):
        return "Diag", "진단 전용 폴더"
    if info["kind"] == "axaml":
        return "Views", f"axaml(x:Class={info['xclass']})" if info["xclass"] else "axaml 스타일·리소스"
    if rel.endswith(".axaml.cs"):
        return "Views", "axaml 코드비하인드"
    avalonia = bool(info["deps"] & {"avalonia", "dock_avalonia"})
    if inner.startswith(("Views/", "Themes/", "Input/", "Documents/")) and avalonia:
        return "Views", f"{inner.split('/')[0]} 폴더의 Avalonia 코드"
    if not avalonia:
        return "Data?", "Avalonia·Dock.Avalonia 무참조(닫힘 계산 대상)"
    return "Control", "Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹"


def close_data(layers, graph, infos):
    """Data 후보 중 Data 밖을 참조하는 파일과, partial 짝이 Data 가 아닌 파일을 Control 로 내린다(고정점)."""
    partial = {}
    for rel, info in infos.items():
        for t in info["types"]:
            if t["partial"]:
                partial.setdefault((info["ns"], t["name"]), set()).add(rel)
    changed = True
    while changed:
        changed = False
        for rel, (layer, reason) in list(layers.items()):
            if layer != "Data?":
                continue
            outside = sorted(t for t in graph[rel] if layers[t][0] not in ("Data?", "Data"))
            mates = [m for g in partial.values() if rel in g for m in g if layers[m][0] != "Data?"]
            if outside or mates:
                why = outside[0] if outside else mates[0]
                layers[rel] = ("Control", f"Avalonia 무참조지만 Data 밖 참조로 Data 불가({why.rsplit('/', 1)[-1]})")
                changed = True
    for rel, (layer, reason) in list(layers.items()):
        if layer == "Data?":
            layers[rel] = ("Data", "Avalonia 무참조, 참조 형식도 전부 Data")


def classify(root):
    with ThreadPoolExecutor(max_workers=16) as pool:
        texts = dict(pool.map(lambda r: read(root, r), scan(root)))
    over_path = os.path.join(os.path.dirname(os.path.abspath(__file__)), "classify_layers_overrides.json")
    overrides = {k: v for k, v in json.load(open(over_path, encoding="utf-8")).items() if not k.startswith("_")}
    infos = {r: (parse_axaml(r, t) if r.endswith(".axaml") else parse_cs(r, t)) for r, t in texts.items()}
    shell_infos = {r: i for r, i in infos.items() if project_of(r) == SHELL or project_of(r) in FIXED}
    table = build_table(shell_infos)
    graph = {r: references(r, i, table) for r, i in infos.items()}
    layers = {r: initial_layer(r, i, overrides) for r, i in infos.items()}
    close_data(layers, graph, infos)
    targets = {r: target_of(r, layers[r][0], overrides) for r in infos}
    return infos, graph, layers, targets


def target_of(rel, layer, overrides):
    """이동 뒤 경로(영역 루트의 src 기준). 그대로면 None."""
    key = rel[len(SRC) + 1:]
    if overrides.get(key, {}).get("target"):
        return overrides[key]["target"]
    if project_of(rel) != SHELL:
        return None
    inner = key[len(SHELL) + 1:]
    if layer == "Views":
        return "Pd.Ui.Views/" + inner.removeprefix("Views/")
    if layer == "Data":
        return "Pd.Client.Data/" + inner.replace("Docking/", "Layout/").replace("Documents/", "Samples/")
    return None


def partial_splits(infos, layers):
    """한 partial 형식이 두 층에 걸치면 옮길 수 없다. 이동 전에 0 이어야 한다."""
    groups = {}
    for rel, info in infos.items():
        for t in info["types"]:
            if t["partial"]:
                groups.setdefault((project_of(rel), info["ns"], t["name"]), set()).add(rel)
    return sorted(f"{k[1]}.{k[2]}" for k, files in groups.items() if len({layers[f][0] for f in files}) > 1)


def crossings(graph, layers):
    """금지 방향 간선: Control/Diag → Views, Data → 그 밖. 이동 전에 끊어야 하는 목록이다."""
    out = []
    for src, targets in graph.items():
        sl = layers[src][0]
        if project_of(src) in HOSTS:
            continue
        for tgt, names in targets.items():
            tl = layers[tgt][0]
            if (sl in ("Control", "Diag") and tl == "Views") or (sl == "Data" and tl != "Data") or (sl == "Ui" and tl != "Ui"):
                out.append({"from": src, "from_layer": sl, "to": tgt, "to_layer": tl, "types": sorted(names)})
    return sorted(out, key=lambda e: (e["from"], e["to"]))


def stats_block(layers):
    return Counter(layer for layer, _ in layers.values())


def render(infos, layers, cross, targets):
    counts = stats_block(layers)
    splits = partial_splits(infos, layers)
    lines = ["# frameworks/ui 소스 계층 분류표", "",
             "> 자동 생성물 — 고치지 말고 `python3 -B tools/classify_layers.py`(영역 루트 `frameworks/ui` 기준) 로 재생성한다. 예외는 `tools/classify_layers_overrides.json`(사유 필수).",
             "> 층 정의와 참조 규칙의 정본은 `plan/04_기술스택/02_소스구조_분할규칙.md`, 강제는 `tools/check_layers.py` 다. 형식 참조는 정규식 근사이고 빌드가 최종 판정이다.", "",
             "## 분류별 파일 수", "", "| 층 | 파일 수 |", "|---|---:|"]
    lines += [f"| {k} | {counts[k]} |" for k in sorted(counts)]
    lines += ["", f"합계 {len(layers)}개(.cs·.axaml).", "", "## 끊어야 하는 참조(현재 기준 금지 방향)", "",
              f"{len(cross)}건. Shell 이 화면을 직접 만드는 지점과 Data 후보가 밖을 보는 지점이다.", "",
              "| 참조하는 파일 | 층 | 참조되는 파일 | 층 | 형식 |", "|---|---|---|---|---|"]
    strip = lambda p: p[len(SRC) + 1:]
    lines += [f"| `{strip(e['from'])}` | {e['from_layer']} | `{strip(e['to'])}` | {e['to_layer']} | {', '.join(e['types'][:5])} |" for e in cross]
    lines += ["", f"## 층에 걸친 partial 형식", "", f"{len(splits)}건. {', '.join(splits) or '없음'}"]
    with open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "classify_layers_overrides.json"), encoding="utf-8") as f:
        lines += ["", "## 옮기지 않기로 한 판단", ""] + [f"- {d}" for d in json.load(f)["_decisions"]]
    lines += ["", "## 파일별 분류", "", "| 파일 | 층 | 이동 뒤 경로 | 근거 | Avalonia | Dock.Model | 내부 형식 |", "|---|---|---|---|:-:|:-:|---|"]
    for rel in sorted(layers):
        info = infos[rel]
        internal = ", ".join(t["name"] for t in info["types"] if t["access"] == "internal")
        lines.append(f"| `{strip(rel)}` | {layers[rel][0]} | {targets[rel] or ''} | {layers[rel][1]} | {'O' if info['deps'] & {'avalonia', 'dock_avalonia'} else ''} | {'O' if 'dock_model' in info['deps'] else ''} | {internal} |")
    return "\n".join(lines) + "\n"


def to_json(infos, graph, layers, cross, targets):
    files = {}
    for rel, info in infos.items():
        files[rel] = {"layer": layers[rel][0], "reason": layers[rel][1], "target": targets[rel], "ns": info["ns"], "deps": sorted(info["deps"]),
                      "types": info["types"], "avares": sorted(set(f"{a}/{b}" for a, b in info["avares"])),
                      "refs": {t: sorted(n) for t, n in graph[rel].items()}}
    return {"files": files, "crossings": cross, "partial_splits": partial_splits(infos, layers)}


def main():
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8")
    here = os.path.dirname(os.path.abspath(__file__))
    ap = argparse.ArgumentParser(description="클라이언트 소스 계층 분류")
    ap.add_argument("--root", default=os.path.abspath(os.path.join(here, "..")))
    ap.add_argument("--json")
    ap.add_argument("--check", action="store_true")
    args = ap.parse_args()
    infos, graph, layers, targets = classify(args.root)
    cross = crossings(graph, layers)
    text = render(infos, layers, cross, targets)
    out = os.path.join(args.root, "LAYERS.md")
    if args.json:
        with open(args.json, "w", encoding="utf-8") as f:
            json.dump(to_json(infos, graph, layers, cross, targets), f, ensure_ascii=False, indent=1)
    if args.check:
        same = os.path.exists(out) and open(out, encoding="utf-8").read() == text
        print("LAYERS.md 최신" if same else "LAYERS.md 가 소스와 다르다 — 재생성 필요")
        return 0 if same else 1
    with open(out, "w", encoding="utf-8", newline="\n") as f:
        f.write(text)
    print(" ".join(f"{k}={v}" for k, v in sorted(stats_block(layers).items())), f"crossings={len(cross)} partial_splits={len(partial_splits(infos, layers))}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
