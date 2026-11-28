#!/usr/bin/env python3
# 다국어(Q17) 정적 검사 — ① 언어별 키 집합 일치 ② 화면에 보이는 하드코딩 한글(axaml·cs) ③ 사용된 키가 사전에 있는지
"""
사용: python3 tools/check_i18n.py [--root .] [--report-only] [--json 결과.json] [--remote]
--remote: 실행 중 앱(진단 빌드, PD_REMOTE_INFO 또는 기본 연결 정보)의 lang.get keys·languages 와 i18n.missing 을 소스 사전과 대조(사전 실제 로드 확인).
사전: src/*/Localization/<영역>.<언어>.json (평면 {"키":"값"}, 기본 언어 ko).
① 영역마다 ko 키 집합 = 다른 언어 키 집합, 빈 값 없음, 키 접두(첫 마디)가 영역 이름과 같은지(소유 구분, 경고) — 위반 시 실패.
② 하드코딩 한글: axaml 의 속성 값·요소 본문, cs 의 문자열 리터럴 중 한글이 든 것.
   제외: 주석·XML 주석, AutomationId·x:Name·Name·Classes·x:Key, Diagnostics/·Localization/ 폴더, bin·obj,
   개발자 전용 출력(Log·Trace·Warn·Console.Write·Debug.·Exception·RecordStep·SetText·TimeOperation·Op() — AddLog·_log 는 화면 로그 패널이라 검사 대상,
   호스트 정적 문서(wwwroot html·js, Android Resources·Manifest)의 한글은 '호스트' 로 따로 센다,
   줄 끝 `// i18n-ok` 표시(의도적 예외 — 사유를 함께 적는다). --report-only 면 ②는 보고만 하고 실패로 치지 않는다(이관 중).
③ axaml {loc:Tr 키}·{loc:Tr Key=키}, cs Loc.Tr("키" / Loc.Instance["키"] 의 키가 ko 사전에 있어야 한다 — 없으면 실패.
   점으로 끝나는 키(코드에서 값을 이어 붙이는 동적 접두, 예 "notify.cat." + 분류)는 그 접두로 시작하는 키가 있으면 통과.
종료 코드: 0=통과, 1=실패.
"""
import argparse
import glob
import json
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))  # python -I(격리 실행)에서도 같은 폴더 모듈을 찾게
from check_i18n_rules import ITEMTPL_ALLOW, check_host_copies, scan_item_templates  # noqa: E402  ④·⑤ 규칙(분리 모듈)
import host_identity  # noqa: E402  호스트 경로 단일 출처
HANGUL = re.compile(r"[가-힣ᄀ-ᇿ㄰-㆏]")
SKIP_DIRS = {"bin", "obj", "Diagnostics", "Localization", "node_modules", "wwwroot"}
SKIP_ATTRS = {"AutomationProperties.AutomationId", "x:Name", "Name", "Classes", "x:Key", "x:Class", "xmlns"}
# 개발자 전용 출력만 제외한다. AddLog·_log 는 화면 '로그' 패널에 그대로 보이므로 제외하지 않는다
# (lead 01:4x: MainViewModel.Compact.cs 의 AddLog 한글 2건을 이 목록이 통째로 건너뛰어 '0건'으로 오판 — WindowCommands._log 도 AddLog 위임).
LOG_CALL = re.compile(r"\b(Log|HostLog\.Write|Trace|Warn|WarnOnce|Console\.Write\w*|Debug\.\w+|RecordStep|SetText|SetMetric|TimeOperation|Op)\s*\(|Exception\s*\(|nameof\(")
# 사용자에게 보이는 호스트 정적 문서(웹 첫 화면·Android 리소스) — 셸 밖이라 사전을 못 쓰는 곳이 있어 '호스트' 로 따로 센다
# 위치는 host_identity 가 정본이다(UADR-0018 로 실행 프로젝트가 testbed 로 옮겨졌다). 경로 값은 영역 루트 기준 상대 경로라 root 아래에 이어 쓴다.
HOST_GLOBS = (
    host_identity.get("browser.wwwroot_src") + "/*.html",
    host_identity.get("browser.wwwroot_src") + "/*.js",
    host_identity.get("android.resources_dir") + "/values*/*.xml",
    host_identity.get("android.project_dir") + "/Properties/AndroidManifest.xml",
)
# 영역 이름과 다른 접두를 허용하는 영역(소유 구분 경고 제외). system 은 시스템 메뉴 머리(menu.sys.*)와 공용 문구(sys.*)를 함께 가진다(UI-17 §3.7)
AREA_PREFIX_ALIASES = {"system": ("menu", "sys")}
KEY_AXAML = re.compile(r"\{loc:Tr\s+(?:Key=)?([A-Za-z0-9_.\-]+)\s*\}")
KEY_CS = re.compile(r"""Loc\.(?:Tr\(\s*"([^"]+)"|Instance\[\s*"([^"]+)"\s*\])""")
ATTR = re.compile(r'([\w:.]+)\s*=\s*"([^"]*)"')
CS_STR = re.compile(r'\$?@?"((?:[^"\\\n]|\\.)*)"')


# 경로 단위 제외(root 기준 상대 경로, '/' 구분). Documents/Bench 는 진단+벤치 빌드(PdBench=true)에서만 컴파일되는 시험 도구이고,
# 남은 한글은 한글 렌더링·정렬·IME 검증용 시험 데이터다(odev-gc 요청·team-lead 지시, csproj 가 제품판에서 Documents\Bench\** 를 제거).
SKIP_PATHS = ("xkxk.frameworks.ui.testbed/Bench",)


def _skip_path(d, name):
    """d/name 의 끝부분이 SKIP_PATHS 중 하나와 같으면 제외(walk 의 root 가 영역 루트든 src 든 같게 동작)."""
    p = (d.replace(os.sep, "/").rstrip("/") + "/" + name)
    return any(p == s or p.endswith("/" + s) for s in SKIP_PATHS)


def walk(root, exts):
    for d, ds, fs in os.walk(root):
        ds[:] = [x for x in ds if x not in SKIP_DIRS and not _skip_path(d, x)]
        for f in fs:
            if os.path.splitext(f)[1] in exts:
                yield os.path.join(d, f)


def load_dicts(root):
    """{area: {lang: {key: value}}}"""
    out = {}
    for p in glob.glob(os.path.join(root, "src", "*", "Localization", "*.json")):
        parts = os.path.basename(p).split(".")
        if len(parts) != 3:
            continue
        area, lang = parts[0], parts[1].lower()
        with open(p, encoding="utf-8-sig") as f:
            out.setdefault(area, {})[lang] = json.load(f)
    return out


def check_dicts(d, problems, warns):
    langs = sorted({lang for a in d.values() for lang in a})
    for area, by_lang in sorted(d.items()):
        ko = by_lang.get("ko")
        if ko is None:
            problems.append("① 영역 %s: ko 사전 없음" % area)
            continue
        for lang in langs:
            if lang == "ko":
                continue
            other = by_lang.get(lang, {})
            miss, extra = sorted(set(ko) - set(other)), sorted(set(other) - set(ko))
            if miss:
                problems.append("① %s.%s: 번역 누락 %d개 %s" % (area, lang, len(miss), miss[:10]))
            if extra:
                problems.append("① %s.%s: ko 에 없는 키 %d개 %s" % (area, lang, len(extra), extra[:10]))
        for lang, kv in by_lang.items():
            empty = [k for k, v in kv.items() if not str(v).strip()]
            if empty:
                problems.append("① %s.%s: 빈 값 %s" % (area, lang, empty[:10]))
            allowed = (area, "lang") + AREA_PREFIX_ALIASES.get(area, ())
            foreign = [k for k in kv if k.split(".")[0] not in allowed and area != "common"]
            if foreign:
                warns.append("① %s.%s: 영역과 다른 접두 키 %s(소유 구분 확인)" % (area, lang, foreign[:5]))
    # 영역 간 중복 키: Loc 는 모든 영역을 한 사전으로 합치므로 같은 키가 두 영역에 있으면 한쪽이 조용히 덮인다(UI-17 system·sysview 추가 때 도입)
    owners = {}
    for area, by_lang in d.items():
        for k in by_lang.get("ko", {}):
            owners.setdefault(k, []).append(area)
    dup = sorted((k, a) for k, a in owners.items() if len(a) > 1)
    if dup:
        problems.append("① 영역 간 중복 키 %d개 %s" % (len(dup), dup[:10]))
    return set().union(*[set(a.get("ko", {})) for a in d.values()]) if d else set()


def check_remote(dicts, keys, problems):
    """실행 중 앱의 사전 로드 확인: lang.get keys == 소스 ko 키 합계, 언어 목록 == 소스 언어, 언어마다 i18n.missing count == 0."""
    sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
    import pdctl
    info = pdctl.load_info()

    def call(cmd, **args):
        resp = json.loads(pdctl.curl(info, "api/cmd", json.dumps({"cmd": cmd, "args": args})))
        return resp.get("result") if resp.get("ok") else {"error": resp.get("error")}

    got = call("lang.get") or {}
    langs = sorted({lang for a in dicts.values() for lang in a})
    loaded = sorted(x.get("code") for x in got.get("languages") or [])
    print("원격 lang.get: language=%s keys=%s languages=%s (소스 ko 키 %d, 언어 %s)" % (
        got.get("language"), got.get("keys"), loaded, len(keys), langs))
    if got.get("keys") != len(keys):
        problems.append("원격 사전 키 수 %s ≠ 소스 %d (사전 로드 실패·빌드가 소스보다 오래됨)" % (got.get("keys"), len(keys)))
    if loaded != langs:
        problems.append("원격 언어 %s ≠ 소스 %s" % (loaded, langs))
    for lang in langs:
        if lang == "ko":
            continue
        miss = call("i18n.missing", lang=lang) or {}
        print("원격 i18n.missing lang=%s: count=%s" % (lang, miss.get("count")))
        if miss.get("count") != 0:
            problems.append("원격 i18n.missing lang=%s count=%s 예 %s" % (lang, miss.get("count"), (miss.get("keys") or [])[:5]))


def scan_axaml(path, hard, used, tpl_hits=None):
    text = re.sub(r"<!--.*?-->", "", open(path, encoding="utf-8-sig").read(), flags=re.S)
    if tpl_hits is not None:
        scan_item_templates(path, text, tpl_hits)
    for m in KEY_AXAML.finditer(text):
        used.append((path, text[:m.start()].count("\n") + 1, m.group(1)))
    for m in ATTR.finditer(text):
        if m.group(1) not in SKIP_ATTRS and HANGUL.search(m.group(2)) and "{loc:Tr" not in m.group(2):
            hard.append((path, text[:m.start()].count("\n") + 1, "%s=\"%s\"" % (m.group(1), m.group(2)[:60])))
    for m in re.finditer(r">([^<>]+)<", text):
        if HANGUL.search(m.group(1)):
            hard.append((path, text[:m.start()].count("\n") + 1, "본문 " + m.group(1).strip()[:60]))


def strip_cs_comments(text):
    """C# 주석(// · /// · /* */)을 공백으로 지운다(줄바꿈 유지 → 줄 번호 보존). 문자열·문자 리터럴 안의 // 는 건드리지 않는다."""
    out, i, n = [], 0, len(text)
    while i < n:
        c, nx = text[i], text[i + 1:i + 2]
        if c == "/" and nx == "/":
            j = text.find("\n", i)
            j = n if j < 0 else j
            out.append(" " * (j - i)); i = j
        elif c == "/" and nx == "*":
            j = text.find("*/", i + 2)
            j = n if j < 0 else j + 2
            out.append(re.sub(r"[^\n]", " ", text[i:j])); i = j
        elif c == '"' or (c in "@$" and nx in '"@$'):
            j = i
            while j < n and text[j] in "@$":
                j += 1
            verbatim = "@" in text[i:j]
            j += 1  # 여는 따옴표
            while j < n:
                if verbatim and text[j] == '"' and text[j + 1:j + 2] == '"':
                    j += 2
                elif not verbatim and text[j] == "\\":
                    j += 2
                elif text[j] == '"' or (not verbatim and text[j] == "\n"):
                    j += 1
                    break
                else:
                    j += 1
            out.append(text[i:j]); i = j
        elif c == "'":
            m = re.match(r"'(?:\\.|[^'\\\n])*'", text[i:])
            j = i + (m.end() if m else 1)
            out.append(text[i:j]); i = j
        else:
            out.append(c); i += 1
    return "".join(out)


def scan_cs(path, hard, used):
    raw = open(path, encoding="utf-8-sig").read()
    # ③ 사용 키는 주석을 지운 코드에서만 찾는다 — 문서 주석 예시(TrExtension.cs 의 {loc:Tr chrome.tip.pin} 등) 오탐 방지(리드 00:4x)
    for i, cline in enumerate(strip_cs_comments(raw).split("\n"), 1):
        for m in KEY_CS.finditer(cline):
            used.append((path, i, m.group(1) or m.group(2)))
        for m in KEY_AXAML.finditer(cline):  # C# 문자열 안 마크업 확장 표기도 사용 키로 친다
            used.append((path, i, m.group(1)))
    for i, line in enumerate(raw.split("\n"), 1):
        code = line.split("//")[0] if '"' not in line.split("//")[0][-1:] else line
        s = line.strip()
        if s.startswith(("//", "///", "*", "/*")) or "i18n-ok" in line or not HANGUL.search(code):
            continue
        if LOG_CALL.search(code) or "AutomationId" in code:
            continue
        for m in CS_STR.finditer(code):
            if HANGUL.search(m.group(1)):
                hard.append((path, i, m.group(1)[:60]))


def main(argv):
    p = argparse.ArgumentParser(description="다국어 정적 검사(Q17)")
    p.add_argument("--root", default=os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
    p.add_argument("--report-only", action="store_true", help="② 하드코딩 한글은 보고만(실패로 치지 않음)")
    p.add_argument("--json", default=None)
    p.add_argument("--remote", action="store_true",
                   help="실행 중 앱(진단 빌드)에 lang.get·i18n.missing 을 물어 사전이 실제로 로드됐는지 대조(PD_REMOTE_INFO 사용)")
    a = p.parse_args(argv)
    root = os.path.abspath(a.root)
    problems, warns, hard, used, tpl_hits = [], [], [], [], []
    dicts = load_dicts(root)
    keys = check_dicts(dicts, problems, warns)
    check_host_copies(root, dicts, problems)
    if a.remote:
        check_remote(dicts, keys, problems)
    for sub in ("src", "testbed"):
        for f in walk(os.path.join(root, sub), {".axaml"}):
            scan_axaml(f, hard, used, tpl_hits)
    judge_item_templates(root, tpl_hits, problems, warns)
    for sub in ("src", "testbed"):
        for f in walk(os.path.join(root, sub), {".cs"}):
            scan_cs(f, hard, used)
    scan_hosts(root, hard)
    judge_unknown_keys(root, used, keys, problems)
    by_file = group_by_file(root, hard)
    fail = bool(problems) or (bool(hard) and not a.report_only)
    print_report(keys, used, hard, by_file, warns, problems)
    if a.json:
        write_json(a.json, keys, used, problems, warns, by_file, a.report_only, hard, fail)
    print("결과:", "실패" if fail else "통과")
    return 1 if fail else 0


# ── main 이 조립하는 단위: 측정(scan_*) / 판정(judge_*) / 기록(print_report·write_json) ──

def scan_hosts(root, hard):
    """측정: 호스트 정적 문서에서 주석(<!-- --> · //) 밖의 한글 줄을 모은다."""
    for pat in HOST_GLOBS:
        for f in glob.glob(os.path.join(root, pat)):
            text = re.sub(r"<!--.*?-->", "", open(f, encoding="utf-8-sig").read(), flags=re.S)
            if f.endswith(".js"):
                text = strip_cs_comments(text)  # JS 주석(// · /* */)도 C# 과 같은 규칙 — 줄바꿈 보존
            for i, line in enumerate(text.split("\n"), 1):
                code = line
                if re.search(r"\bconsole\.\w+\s*\(", code):  # 브라우저 개발자 콘솔 — 사용자 화면 아님
                    continue
                if HANGUL.search(code) and "i18n-ok" not in line:
                    hard.append((f, i, "호스트 " + code.strip()[:60]))


def judge_item_templates(root, tpl_hits, problems, warns):
    """판정 ④: ItemTemplate 없는 표시 경로는 허용 목록 밖이면 실패(lead 02:0x — 새로 생기는 경고는 계속 실패)."""
    for f, n, tag, src in tpl_hits:
        rel = os.path.relpath(f, root).replace(os.sep, "/")
        why = ITEMTPL_ALLOW.get((rel, src))
        line = "④ 표시 경로: %s:%d <%s ItemsSource=%s> ItemTemplate 없음 — 항목 ToString 이 지역화 문자열이면 언어 전환 미갱신" % (rel, n, tag, src)
        if why:
            warns.append(line + " [허용: %s]" % why)
        else:
            problems.append(line)


def judge_unknown_keys(root, used, keys, problems):
    """판정 ③: 사전에 없는 키. 점으로 끝나는 키는 동적 접두(예 "notify.cat." + 분류) — 그 접두로 시작하는 키가 하나라도 있으면 통과."""
    unknown = [(f, n, k) for f, n, k in used
               if k not in keys and not (k.endswith(".") and any(x.startswith(k) for x in keys))]
    problems += ["③ 사전에 없는 키 %s (%s:%d)" % (k, os.path.relpath(f, root), n) for f, n, k in unknown]


def group_by_file(root, hard):
    """하드코딩 한글을 파일별로 묶는다."""
    by_file = {}
    for f, n, t in hard:
        by_file.setdefault(os.path.relpath(f, root), []).append((n, t))
    return by_file


def print_report(keys, used, hard, by_file, warns, problems):
    """기록: 콘솔 보고(결과 줄은 main 이 JSON 뒤에 찍는다)."""
    print("사전 키 %d · 사용 %d · 하드코딩 한글 %d건(%d파일)" % (len(keys), len(used), len(hard), len(by_file)))
    for f, items in sorted(by_file.items(), key=lambda x: -len(x[1])):
        print("  ② %-60s %3d건  예: %s:%s" % (f, len(items), items[0][0], items[0][1]))
    for w in warns:
        print("  경고 " + w)
    for pr in problems:
        print("  실패 " + pr)


def write_json(path, keys, used, problems, warns, by_file, report_only, hard, fail):
    """기록: 결과 JSON."""
    with open(path, "w", encoding="utf-8") as f:
        json.dump({"keys": len(keys), "used": len(used), "problems": problems, "warnings": warns,
                   "hardcoded": [{"file": k, "items": v} for k, v in by_file.items()],
                   # gate 근거용(lead 02:4x): 실행 조건·종료 코드를 결과에 같이 남긴다
                   "reportOnly": report_only, "hardcodedCount": len(hard), "rc": 1 if fail else 0,
                   "result": "실패" if fail else "통과"}, f, ensure_ascii=False, indent=2)


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
