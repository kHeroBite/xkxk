# 소비자 문서·공개 API 명세 불일치 검사기 — K1~K10·W1·W2(B07b, UADR-0015). 한계: K6 체인은 변수 ui·XkxkUi.Current 만 푼다
"""docs/ 수기 문서 5개와 docs/api 생성물을 PublicAPI txt(표면 정본)와 대조한다.

사용: python3 -B test/framework/judge/consumer_docs_check.py [--root R] [--strict] [--report J] | --self-test
  K1 생성물 최신(render_all == 디스크, 남는 .md 없음)   K2 수기 문서 5개·한국어 헤더·'상태'·'최종 갱신일'·300줄
  K3 XKUI 코드 실재   K4 'xkxk.' ID 실재(상수 값·네임스페이스·프로젝트, 끝 '*'·'…' 는 접두 표기)
  K5 '<공개형식>.<멤버>' 실재   K6 체인 'ui.A.B'(IUiModuleContext)·'XkxkUi.Current.A.B'(IUiAppContext) 반환 형식 따라 확인
  K7 상대 링크 실재   K8 docs/README.md 가 docs 의 모든 .md(api 는 api/README.md) 링크   K9 AI 블록 마커·80줄
  K10 미해석 PublicAPI 줄 0   W1 설명 없는 공개 멤버(WARN, --strict 면 FAIL)   W2 99_미분류 존재(WARN)
파서·생성기는 tools/gen_api_docs*.py 를 재사용한다(중복 금지). rc 0 PASS·1 FAIL·2 입력 오류, 끝 줄 'consumer_docs … result='.
"""
import argparse
import glob
import json
import os
import re
import sys
import urllib.parse

import consumer_docs_ext as cdx
from consumer_docs_ext import index  # noqa: F401 — 형식→멤버→반환 형식 색인(보조 모듈)

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
TOOLS = os.path.join(REPO, "tools")
HAND_DOCS = ("docs/README.md", "docs/01_시작하기.md", "docs/02_확장_메뉴명령아이콘.md", "docs/03_AI_활용안내.md",
             "docs/04_확장_창.md")
AI_DOC = "docs/03_AI_활용안내.md"
MARK_B, MARK_E = "<!-- xkxk-ui-ai-guide:begin -->", "<!-- xkxk-ui-ai-guide:end -->"
AI_MAX, DOC_MAX = 80, 300
FW_NS = "xkxk.frameworks.ui.shell.Framework"
CHAIN_ROOTS = {"ui": "IUiModuleContext"}  # XkxkUi.Current 은 XkxkUi.Current 반환 형식(IUiAppContext)으로 자연히 풀린다
FILE_EXT = ("cs", "axaml", "md", "txt", "json", "csproj", "dll", "exe", "nupkg", "props", "targets", "xml", "sh", "py")
CODE_LANGS = ("csharp", "cs", "c#")
LIMIT_NOTE = ("한계: K6 체인은 변수 이름 ui(IUiModuleContext)와 XkxkUi.Current(IUiAppContext)만 푼다. "
              "다른 변수 이름·지역 변수·객체 초기화 속성·람다 인자·괄호 뒤 체인은 보지 않고, 예제를 컴파일하지 않는다(식별자 존재만 확인)")
CHAIN_RE = re.compile(r"(?<![\w.])([A-Za-z_]\w*(?:\.[A-Za-z_]\w*)+)")
XK_RE = re.compile(r"(?<![\w/.\-\\])xkxk\.[\w.\-*…]*")
DIAG_RE = re.compile(r"XKUI\d{4}")
TICK_RE = re.compile(r"`([^`\n]+)`")
LINK_RE = re.compile(r"\]\(<?([^)\s>]+)>?(?:\s+\"[^\"]*\")?\)")
HDR_RE = re.compile(r"^<!--.*[가-힣].*-->\s*$")


class Rep:
    def __init__(self):
        self.checks, self.fails, self.warns = 0, [], []
    def ok(self, cond, kid, where, msg):
        self.checks += 1
        if not cond:
            self.fails.append({"id": kid, "where": where, "msg": msg})
        return cond
    def warn(self, kid, where, msg):
        self.warns.append({"id": kid, "where": where, "msg": msg})


def load_gen():
    if TOOLS not in sys.path:
        sys.path.insert(0, TOOLS)
    import gen_api_docs, gen_api_docs_parse, gen_api_docs_render  # noqa: E401,E402 — tools/ 의 생성기
    return gen_api_docs, gen_api_docs_parse, gen_api_docs_render


def namespaces(root, surface):
    names = {FW_NS}
    names.update(os.path.basename(p) for p in glob.glob(os.path.join(root, "src", "xkxk.*")) if os.path.isdir(p))
    for f in surface.get("txt_files", []):
        p = f if os.path.isabs(f) else os.path.join(root, f)
        try:
            with open(p, encoding="utf-8-sig") as fh:
                names.update(re.findall(r"xkxk(?:\.\w+)+", fh.read()))
        except OSError:
            pass
    names.update(cdx.declared_namespaces(root))  # 소스에 선언된 namespace(views.Windows·testbed 등)
    out = set()
    for n in names:
        parts = n.split(".")
        out.update(".".join(parts[:i]) for i in range(2, len(parts) + 1))
    return out


def segments(text):
    """(줄번호, 조각, 종류) — 종류 code(csharp 블록 줄)·tick(백틱 안)·plain(그 밖 줄, 링크용). 다른 언어 블록은 건너뛴다."""
    fence = None
    for no, line in enumerate(text.split("\n"), 1):
        s = line.strip()
        if s.startswith("```"):
            fence = None if fence is not None else (s[3:].strip().lower() or "text")
            continue
        if fence is not None:
            if fence in CODE_LANGS:
                yield no, line, "code"
            continue
        yield no, line, "plain"
        for t in TICK_RE.findall(line):
            yield no, t, "tick"


def check_chain(rep, where, tok, idx):
    parts = tok.split(".")
    if parts[-1].lower() in FILE_EXT:
        return
    if parts[0] in CHAIN_ROOTS:
        cur, start = CHAIN_ROOTS[parts[0]], 1
    elif parts[0] in idx:
        cur, start = parts[0], 1
    else:
        return
    for i in range(start, len(parts)):
        name = parts[i]
        if cur not in idx:
            return
        kid = "K5" if i == 1 and parts[0] not in CHAIN_ROOTS else "K6"
        if not rep.ok(name in idx[cur], kid, where, f"`{tok}` — 형식 {cur} 에 멤버 {name} 없음"):
            return
        nxt = sorted(r for r in idx[cur][name] if r in idx)
        if not nxt:
            return
        cur = nxt[0]


def xk_ok(tok, vals, nss, idx):
    tok = tok.rstrip(".")
    if tok in ("xkxk", "") or tok in vals or tok in nss:
        return True
    if tok.endswith(("*", "…")):
        pre = tok.rstrip("*…")
        return any(v.startswith(pre) for v in vals | nss)
    head, _, ext = tok.rpartition(".")
    if ext.lower() in FILE_EXT and head in nss:
        return True
    if tok.startswith(FW_NS + "."):
        rest = tok[len(FW_NS) + 1:].split(".")
        return rest[0] in idx and (len(rest) == 1 or rest[1] in idx[rest[0]])
    return False


def check_doc(rep, rel, text, ctx):
    idx, vals, nss, codes = ctx
    for no, seg, kind in segments(text):
        where = f"{rel}:{no}"
        if kind != "tick":
            for code in DIAG_RE.findall(seg):
                rep.ok(code in codes, "K3", where, f"`{code}` — 진단 코드 없음(UiDiagnosticIds)")
        if kind == "plain":
            continue
        for tok in XK_RE.findall(seg):
            rep.ok(xk_ok(tok, vals, nss, idx), "K4", where, f"`{tok}` — 상수 값·네임스페이스·프로젝트 이름에 없는 xkxk. ID")
        for tok in CHAIN_RE.findall(seg):
            check_chain(rep, where, tok, idx)


def doc_links(root, rel, text):
    out = []
    for no, seg, kind in segments(text):
        if kind != "plain":
            continue
        for tgt in LINK_RE.findall(seg):
            if re.match(r"^[a-zA-Z][\w+.-]*:", tgt) or tgt.startswith("#"):
                continue
            path = urllib.parse.unquote(tgt.split("#", 1)[0])
            full = os.path.normpath(os.path.join(root, os.path.dirname(rel), path))
            out.append((no, tgt, full))
    return out


def read(path):
    with open(path, encoding="utf-8-sig", newline="") as fh:
        return fh.read()


def run(root, strict=False):
    rep = Rep()
    try:
        gen, parse, rend = load_gen()
        surface = parse.load_surface(root)
    except Exception as e:  # 생성기 모듈 없음·txt 읽기 실패 = 입력 오류
        rep.fails.append({"id": "INPUT", "where": root, "msg": f"생성기 모듈·load_surface 실패: {e}"})
        return 2, rep, {}
    for u in surface.get("unresolved") or []:  # 'rel:no: 줄' 문자열(gen_api_docs_parse)
        where, _, text = str(u).partition(": ")
        rep.ok(False, "K10", where, f"미해석 PublicAPI 줄 `{text}`")
    rep.ok(bool(surface.get("types")), "K10", "PublicAPI", "공개 형식 0개 — txt 를 읽지 못함")
    idx = index(surface)
    cdx.add_extensions(idx, root)  # 공개 확장 메서드(ui.Windows.Register<…>)를 수신 형식 멤버로
    codes = {d.get("code") for d in surface.get("diagnostics", [])}
    ctx = (idx, set(surface.get("const_values", [])), namespaces(root, surface), codes)
    # K1 생성물 최신
    try:
        rendered = gen.render_all(root)
    except Exception as e:
        rep.ok(False, "K1", "docs/api", f"render_all 실패: {e}")
        rendered = {}
    for rel in sorted(rendered):
        p = os.path.join(root, rel)
        st = ("같음" if read(p) == rendered[rel] else "다름") if os.path.isfile(p) else "없음"
        rep.ok(st == "같음", "K1", rel, f"{st} — 생성물이 낡음(python3 -B tools/gen_api_docs.py 로 재생성, 손 편집 금지)")
    for p in sorted(glob.glob(os.path.join(root, "docs", "api", "*.md"))):
        rel = os.path.relpath(p, root).replace(os.sep, "/")
        if rendered:
            rep.ok(rel in rendered, "K1", rel, "남음 — 생성 대상 아닌 파일(--prune 로 정리)")
        if os.path.basename(p).startswith("99_미분류"):
            rep.warn("W2", rel, "그룹 표에 없는 형식이 99_미분류 에 모였다(gen_api_docs_render GROUPS 갱신 필요)")
    # K2·K9 수기 문서 형식
    for rel in HAND_DOCS:
        p = os.path.join(root, rel)
        if not rep.ok(os.path.isfile(p), "K2", rel, "수기 문서 없음"):
            continue
        lines = read(p).split("\n")
        n = len(lines) - (1 if lines and lines[-1] == "" else 0)
        rep.ok(bool(HDR_RE.match(lines[0])), "K2", f"{rel}:1", "첫 줄 한국어 <!-- --> 헤더 없음")
        rep.ok(any("상태" in x for x in lines), "K2", rel, "'상태' 줄 없음")
        rep.ok(any("최종 갱신일" in x for x in lines), "K2", rel, "'최종 갱신일' 줄 없음")
        rep.ok(n <= DOC_MAX, "K2", rel, f"{n}줄 — {DOC_MAX}줄 초과")
        if rel == AI_DOC:
            b = [i for i, x in enumerate(lines) if x.strip() == MARK_B]
            e = [i for i, x in enumerate(lines) if x.strip() == MARK_E]
            if rep.ok(len(b) == 1 and len(e) == 1 and b[0] < e[0], "K9", rel, f"마커 begin {len(b)}개·end {len(e)}개(각 1개, begin 먼저)"):
                inner = e[0] - b[0] - 1
                rep.ok(inner <= AI_MAX, "K9", f"{rel}:{b[0] + 1}", f"AI 복사 블록 {inner}줄 — {AI_MAX}줄 초과")
    # K3~K8 문서 내용·링크
    docs_dir = os.path.join(root, "docs")
    all_md = sorted(os.path.relpath(p, root).replace(os.sep, "/")
                    for p in glob.glob(os.path.join(docs_dir, "**", "*.md"), recursive=True))
    readme_targets = set()
    for rel in all_md:
        text = read(os.path.join(root, rel))
        if not rel.startswith("docs/api/"):
            check_doc(rep, rel, text, ctx)
        for no, tgt, full in doc_links(root, rel, text):
            rep.ok(os.path.exists(full), "K7", f"{rel}:{no}", f"깨진 링크 `{tgt}`")
            if rel == "docs/README.md":
                readme_targets.add(os.path.relpath(full, root).replace(os.sep, "/"))
    if os.path.isfile(os.path.join(docs_dir, "README.md")):
        for rel in all_md:
            if rel == "docs/README.md" or (rel.startswith("docs/api/") and rel != "docs/api/README.md"):
                continue
            rep.ok(rel in readme_targets, "K8", "docs/README.md", f"{rel} 를 링크하지 않음")
    # W1 설명 없는 공개 멤버
    und = rend.stats_of(surface)["undocumented"]  # 생성기 README 통계와 같은 집계
    if und:
        msg = f"설명(///) 없는 공개 멤버 {und}개 (UTODO-0034)"
        if strict:
            rep.ok(False, "W1", "PublicAPI", msg + " — --strict")
        else:
            rep.warn("W1", "PublicAPI", msg)
    stats = {"types": len(idx), "members": sum(len(i.get("members", [])) for i in surface.get("types", {}).values()),
             "undocumented": und, "docs_scanned": len(all_md), "generated": len(rendered)}
    return (1 if rep.fails else 0), rep, stats


def emit(rc, rep, stats, report=None):
    for kind, items in (("FAIL", rep.fails), ("WARN", rep.warns)):
        for f in items:
            print(f"{kind} {f['id']} {f['where']}: {f['msg']}")
    print(LIMIT_NOTE)
    result = "PASS" if rc == 0 else "FAIL"
    if report:
        os.makedirs(os.path.dirname(os.path.abspath(report)), exist_ok=True)
        with open(report, "w", encoding="utf-8", newline="\n") as fh:
            json.dump({"result": result, "rc": rc, "checks": rep.checks, "fail": len(rep.fails), "warn": len(rep.warns),
                       "failures": rep.fails, "warnings": rep.warns, "stats": stats, "limits": LIMIT_NOTE},
                      fh, ensure_ascii=False, indent=2)
            fh.write("\n")
    print(f"consumer_docs checks={rep.checks} fail={len(rep.fails)} warn={len(rep.warns)} result={result}")


def main(argv=None):
    ap = argparse.ArgumentParser(description="소비자 문서·공개 API 명세 불일치 검사기(K1~K10·W1·W2)")
    ap.add_argument("--root", default=REPO)
    ap.add_argument("--strict", action="store_true", help="W1(설명 없는 공개 멤버)을 FAIL 로 본다")
    ap.add_argument("--report", default=None, help="보고 JSON(기본 <root>/logs/consumer_docs_check.json)")
    ap.add_argument("--self-test", action="store_true")
    a = ap.parse_args(argv)
    if a.self_test:
        sys.path.insert(0, HERE)
        import consumer_docs_selftest  # noqa: E402
        return consumer_docs_selftest.main()
    root = os.path.abspath(a.root)
    rc, rep, stats = run(root, a.strict)
    emit(rc, rep, stats, a.report or os.path.join(root, "logs", "consumer_docs_check.json"))
    return rc


if __name__ == "__main__":
    sys.exit(main())
