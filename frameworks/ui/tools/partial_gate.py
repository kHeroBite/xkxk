# 같은 네임스페이스·같은 클래스의 partial 선언이 여러 파일에 있으면 위반으로 보고하는 게이트(partial 쪼개기 금지 원칙)
"""
판정: src/ 아래 .cs 에서 `partial class|struct|interface|record` 선언을 모아, (네임스페이스, 바깥 형식 포함 이름) 이 같은
선언이 서로 다른 파일 2개 이상에 있으면 위반이다. partial 선언이 파일 1개뿐이면(소스 생성기용) 허용한다.
제외: bin·obj·.git·node_modules 폴더(bin_*·obj_* 접두 폴더 포함), 자동 생성물(*.g.cs, *.g.i.cs, *.Designer.cs, *.AssemblyInfo.cs).
rc: 0 위반 없음 / 1 위반 있음 / 2 인자 오류 / 3 src 폴더 없음 또는 대상 파일 0개(부재를 통과로 보지 않는다).
사용: python3 -I partial_gate.py [src 경로] [--with-cand] [--json 출력경로]
  --with-cand: <파일>.cs.cand(반영 대기 후보)를 반영했다고 보고 판정한다(신규 .cand 포함). .alt.cand 등 다른 접미는 무시한다.
한계(추정 검출): 문자열·주석을 걷어낸 뒤 중괄호 깊이로 네임스페이스·바깥 형식을 추정한다. #if 분기는 구분하지 않는다.
"""
import argparse
from concurrent.futures import ThreadPoolExecutor
import json
import os
import re
import sys

DEFAULT_SRC = "/mnt/c/DATA/Project/pd/poc/ui/src"
SKIP_DIRS = {"bin", "obj", ".git", "node_modules"}
SKIP_PREFIXES = ("bin_", "obj_")  # 빌드 출력 폴더(bin_*·obj_*)도 내려가지 않는다
GENERATED = (".g.cs", ".g.i.cs", ".Designer.cs", ".AssemblyInfo.cs", ".GlobalUsings.g.cs")

TOKEN = re.compile(
    r"(?P<ns>\bnamespace\s+(?P<nsname>[\w.]+)\s*(?P<nsend>[;{]))"
    r"|(?P<type>\b(?P<mods>(?:(?:public|private|protected|internal|static|sealed|abstract|unsafe|readonly|ref|file|new|partial)\s+)*)"
    r"(?P<kind>class|struct|interface|record(?:\s+class|\s+struct)?)\s+(?P<name>[A-Za-z_]\w*))"
    r"|(?P<open>\{)|(?P<close>\})"
)


def strip_noise(text):
    """주석·문자열·문자 리터럴을 같은 길이의 공백으로 바꿔 중괄호·키워드 오탐을 줄인다."""
    out, i, n = [], 0, len(text)
    while i < n:
        c, d = text[i], text[i + 1] if i + 1 < n else ""
        if c == "/" and d == "/":
            j = text.find("\n", i)
            j = n if j < 0 else j
            out.append(" " * (j - i)); i = j
        elif c == "/" and d == "*":
            j = text.find("*/", i + 2)
            j = n if j < 0 else j + 2
            out.append(re.sub(r"[^\n]", " ", text[i:j])); i = j
        elif text.startswith('"""', i):
            j = text.find('"""', i + 3)
            j = n if j < 0 else j + 3
            out.append(re.sub(r"[^\n]", " ", text[i:j])); i = j
        elif c == '"' or (c in "@$" and d in '"@$' and text[i:i + 3].replace("@", "").replace("$", "").startswith('"')):
            verbatim = "@" in text[i:i + 3].split('"')[0]
            j = text.index('"', i) + 1
            while j < n:
                if verbatim and text[j] == '"' and text[j + 1:j + 2] == '"':
                    j += 2; continue
                if not verbatim and text[j] == "\\":
                    j += 2; continue
                if text[j] == '"' or (not verbatim and text[j] == "\n"):
                    j += 1; break
                j += 1
            out.append(re.sub(r"[^\n]", " ", text[i:j])); i = j
        elif c == "'" and d != "'":
            j = i + 1
            while j < n and text[j] != "'" and text[j] != "\n":
                j += 2 if text[j] == "\\" else 1
            j = min(j + 1, n)
            out.append(" " * (j - i)); i = j
        else:
            out.append(c); i += 1
    return "".join(out)


_PRE = {}


def prefetch(src):
    """NTFS 열기 지연을 겹치려고 .cs(.cs.cand 포함) 파일을 스레드로 미리 읽는다. 판정 입력은 같고, 못 읽으면 건너뛴다."""
    paths = []
    for root, dirs, files in os.walk(src):
        dirs[:] = [d for d in dirs if d not in SKIP_DIRS and not d.startswith(SKIP_PREFIXES)]
        paths += [os.path.join(root, f) for f in files if f.endswith((".cs", ".cs.cand"))]

    def _one(p):
        try:
            with open(p, "rb") as fh:
                return p, fh.read()
        except OSError:
            return p, None
    with ThreadPoolExecutor(max_workers=32) as ex:
        for p, d in ex.map(_one, paths):
            if d is not None:
                _PRE[p] = d


def read_text(path):
    """open(..., encoding='utf-8-sig', errors='replace') 텍스트 모드와 같은 결과(개행 통일, BOM 제거)."""
    if path in _PRE:
        return _PRE.pop(path).decode("utf-8-sig", errors="replace").replace("\r\n", "\n").replace("\r", "\n")
    with open(path, encoding="utf-8-sig", errors="replace") as f:
        return f.read()


def partial_decls(path):
    """파일 안 partial 선언을 (네임스페이스, 바깥 포함 이름, 종류) 목록으로 돌려준다."""
    raw = read_text(path)
    if "partial" not in raw:  # 키워드가 없으면 선언도 없다(주석 제거는 비싼 문자 단위 순회라 생략)
        return []
    text = strip_noise(raw)
    result, stack, file_ns, pending = [], [], "", None  # stack: ("ns"|"type", 이름)
    for m in TOKEN.finditer(text):
        if m.group("ns"):
            if m.group("nsend") == ";":
                file_ns = m.group("nsname")
            else:
                pending = ("ns", m.group("nsname"))
        elif m.group("type"):
            is_partial = re.search(r"\bpartial\b", m.group("mods") or "") is not None
            ns = ".".join([file_ns] * bool(file_ns) + [n for k, n in stack if k == "ns"])
            outer = ".".join(n for k, n in stack if k == "type")
            full = (outer + "." if outer else "") + m.group("name")
            if is_partial:
                result.append((ns, full, m.group("kind").split()[0]))
            pending = ("type", m.group("name"))
        elif m.group("open"):
            stack.append(pending or ("block", ""))
            pending = None
        elif m.group("close") and stack:
            stack.pop()
    return result


def collect(src, with_cand):
    groups = {}  # (ns, name) -> {rel 경로: 선언 수}
    count = 0
    prefetch(src)
    for root, dirs, files in os.walk(src):
        dirs[:] = [d for d in dirs if d not in SKIP_DIRS and not d.startswith(SKIP_PREFIXES)]
        names = {f for f in files if f.endswith(".cs")}
        if with_cand:
            names |= {f[:-5] for f in files if f.endswith(".cs.cand")}
        for fn in sorted(names):
            if fn.endswith(GENERATED):
                continue
            p = os.path.join(root, fn)
            use = p + ".cand" if with_cand and (fn + ".cand") in files else p
            if not os.path.isfile(use):
                continue
            count += 1
            rel = os.path.relpath(p, src).replace(os.sep, "/") + (" (.cand 반영)" if use != p else "")
            for ns, name, _ in partial_decls(use):
                groups.setdefault((ns, name), {}).setdefault(rel, 0)
                groups[(ns, name)][rel] += 1
    return groups, count


def judge(groups):
    """판정: 같은 (네임스페이스, 클래스) partial 선언이 2파일 이상이면 위반. (위반 묶음, 단일 파일 partial 수)."""
    bad = {k: v for k, v in groups.items() if len(v) >= 2}
    only_one = sum(1 for v in groups.values() if len(v) == 1)
    return bad, only_one


def print_report(groups, count, bad, only_one):
    """기록: 콘솔 보고."""
    for (ns, name), files in sorted(bad.items(), key=lambda kv: (-len(kv[1]), kv[0])):
        print("위반 %s.%s — 파일 %d개" % (ns or "(전역)", name, len(files)))
        for rel in sorted(files):
            print("    " + rel)
    print("PARTIAL_GATE 대상 %d파일 · partial 클래스 %d개 · 위반 클래스 %d개 · 단일 파일 partial(허용) %d개 · rc=%d"
          % (count, len(groups), len(bad), only_one, 1 if bad else 0))


def write_json(path, count, bad):
    """기록: 결과 JSON."""
    with open(path, "w", encoding="utf-8") as f:
        json.dump({"files": count, "violations": [{"namespace": k[0], "class": k[1], "files": sorted(v)} for k, v in sorted(bad.items())]},
                  f, ensure_ascii=False, indent=2)


def main():
    """조립: 인자 → 수집(collect) → 판정(judge) → 기록(print_report·write_json) 순서만 정한다."""
    ap = argparse.ArgumentParser(description="partial 쪼개기 금지 게이트")
    ap.add_argument("src", nargs="?", default=DEFAULT_SRC)
    ap.add_argument("--with-cand", action="store_true")
    ap.add_argument("--json")
    a = ap.parse_args()
    if not os.path.isdir(a.src):
        print("src 폴더가 없다: " + a.src); return 3
    groups, count = collect(a.src, a.with_cand)
    if count == 0:
        print("대상 .cs 파일이 0개다"); return 3
    bad, only_one = judge(groups)
    print_report(groups, count, bad, only_one)
    if a.json:
        write_json(a.json, count, bad)
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
