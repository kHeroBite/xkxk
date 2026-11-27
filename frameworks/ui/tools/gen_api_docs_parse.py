# PublicAPI txt(공개 표면 정본)와 소스 /// 주석을 읽어 공개 API 표면 dict 로 만드는 파서(gen_api_docs·consumer_docs_check 공용)
"""load_surface(root) 하나만 공개한다. 계약은 design_contract interfaces gen_api_docs_parse 이다.

- 표면: <root>/src/*/PublicAPI.Shipped.txt·Unshipped.txt 의 '#' 로 시작하지 않는 모든 줄.
- 형식 줄 = 수식어를 뗀 뒤 ' -> ' 가 없고 점으로 이은 이름 하나뿐인 줄. 멤버 줄은 가장 긴 형식 전체 이름 접두로 형식을 정한다.
- member_kind 규칙: 괄호 = method(이름이 형식과 같으면 ctor), .get/.set/.init = property, ' = 값' 이 const 수식어와
  함께면 const·반환이 그 형식이면 enum, 괄호·접근자·값이 없으면 readonly 수식어일 때 field 그 밖은 event.
- 규칙에 맞지 않는 줄은 'unresolved' 에 '파일:줄: 원문' 으로 모은다(생성기 rc=2, 검사기 K10 FAIL).
- 설명: 같은 프로젝트 폴더의 *.cs(bin·obj 제외)에서 선언 바로 위 /// 의 <summary>·<remarks> 를 태그를 벗겨 얻는다.
"""
import os
import re

MODIFIERS = {"abstract", "const", "override", "static", "virtual", "readonly", "sealed", "new", "extern"}
_TYPE_DECL = re.compile(
    r"^(?:(?:public|internal|private|protected|static|sealed|abstract|partial|readonly|unsafe|new|file)\s+)*"
    r"(class|interface|enum|struct|record(?:\s+struct|\s+class)?)\s+([A-Za-z_]\w*)(<[\w\s,]+>)?")
_TYPE_KIND = re.compile(r"\b((?:(?:static|sealed|abstract|readonly)\s+)*)(?:partial\s+)?(class|interface|enum|struct|record)\b")
_NAMESPACE = re.compile(r"^\s*namespace\s+([\w.]+)")
_IDENT = re.compile(r"[A-Za-z_]\w*")
_ENTITIES = (("&lt;", "<"), ("&gt;", ">"), ("&quot;", '"'), ("&apos;", "'"), ("&amp;", "&"))
SKIP_DIRS = {"bin", "obj", ".git", "node_modules"}


def xml_to_text(xml):
    """/// 본문 조각을 사람이 읽는 글로 바꾼다. 문단은 '\\n' 으로 나눈다."""
    def cref(m):
        name = re.sub(r"^[A-Z]:", "", m.group(1)).replace("{", "<").replace("}", ">")
        return "`" + name + "`"
    s = re.sub(r'<see\s+cref="([^"]+)"\s*>(.*?)</see>', lambda m: m.group(2), xml, flags=re.S)
    s = re.sub(r'<(?:see|seealso)\s+cref="([^"]+)"\s*/>', cref, s)
    s = re.sub(r'<see\s+langword="([^"]+)"\s*/>', r"`\1`", s)
    s = re.sub(r'<(?:paramref|typeparamref)\s+name="([^"]+)"\s*/>', r"`\1`", s)
    s = re.sub(r"<c>(.*?)</c>", r"`\1`", s, flags=re.S)
    s = re.sub(r"<para\s*/?>|</para>|<br\s*/?>", "\n", s)
    s = re.sub(r"<[^>]+>", "", s)
    for ent, ch in _ENTITIES:
        s = s.replace(ent, ch)
    paras = [re.sub(r"\s+", " ", p).strip() for p in s.split("\n")]
    return "\n".join(p for p in paras if p)


INHERIT = "\x00inheritdoc"


def _doc_parts(buf):
    joined = " ".join(buf)
    out = {}
    if "<inheritdoc" in joined and "<summary>" not in joined:
        return {"summary": INHERIT, "remarks": ""}
    for tag in ("summary", "remarks"):
        m = re.search(r"<%s>(.*?)</%s>" % (tag, tag), joined, flags=re.S)
        out[tag] = xml_to_text(m.group(1)) if m else ""
    return out


def _member_name(decl, type_short):
    """선언 줄에서 멤버 이름을 뽑는다. 인덱서는 'this[]'."""
    s = re.sub(r"//.*$", "", decl).strip()
    if re.search(r"\bthis\s*\[", s):
        return "this[]"
    cuts = [p for p in (s.find("="), s.find("{"), s.find(";")) if p >= 0]
    cut = min(cuts) if cuts else -1
    paren = s.find("(")
    if paren >= 0 and (cut < 0 or paren < cut):
        head = re.sub(r"<[^()]*>\s*$", "", s[:paren].rstrip())
    else:
        head = s[:cut] if cut >= 0 else s
    names = _IDENT.findall(head)
    return names[-1] if names else None


def _params_of(text):
    """첫 괄호 묶음 안쪽(괄호 균형이 안 맞으면 None)."""
    start = text.find("(")
    depth = 0
    for i in range(max(start, 0), len(text)):
        depth += {"(": 1, ")": -1}.get(text[i], 0)
        if depth == 0 and start >= 0:
            return text[start + 1:i]
    return None


def _param_key(params):
    """매개변수 목록을 오버로드 구분용 형식 열로 정규화한다(소스·txt 공통: 이름·기본값·!?·네임스페이스 제거)."""
    parts, depth, cur = [], 0, ""
    for ch in params or "":
        depth += 1 if ch in "<([" else -1 if ch in ">)]" else 0
        if ch == "," and depth == 0:
            parts.append(cur)
            cur = ""
        else:
            cur += ch
    out = []
    for p in parts + ([cur] if cur.strip() else []):
        p = re.sub(r"^\s*(?:\[[^\]]*\]\s*)+", "", p).split("=")[0].strip()
        p = re.sub(r"^(?:(?:this|params|out|ref|in|scoped)\s+)+", "", p)
        typ = p.rsplit(" ", 1)[0] if " " in p else p
        out.append(re.sub(r"\b(?:[A-Za-z_]\w*\.)+(?=[A-Za-z_])", "", re.sub(r"[!?\s]", "", typ)))
    return ",".join(out)


def _scan_cs(path, docs):
    """한 .cs 파일의 형식·멤버 /// 를 docs[전체형식이름] 에 모은다(먼저 본 값 유지, partial 은 합침)."""
    try:
        with open(path, encoding="utf-8-sig") as f:
            lines = f.read().splitlines()
    except (OSError, UnicodeDecodeError):
        return
    ns, depth, stack, buf, pend = "", 0, [], [], None

    def reg_sig(full, name, summary, text):
        key = "%s(%s)" % (name, _param_key(_params_of(text)))
        docs[full]["sig_docs"].setdefault(key, summary)

    for raw in lines:
        st = raw.strip()
        if pend is not None and not st.startswith("///"):
            pend[3] += " " + st
            if _params_of(pend[3]) is not None:
                reg_sig(*pend)
                pend = None
        if st.startswith("///"):
            buf.append(st[3:])
            continue
        m_ns = _NAMESPACE.match(raw)
        if m_ns:
            ns, buf = m_ns.group(1), []
        elif st.startswith("[") and buf:
            continue
        elif st and not st.startswith("//") and not st.startswith("#"):
            decl = re.sub(r"^(?:\[[^\]]*\]\s*)+", "", st)
            m_t = _TYPE_DECL.match(decl)
            if m_t:
                outer = stack[-1][0] if stack else (ns + "." if ns else "")
                full = (outer + "." if stack else outer) + m_t.group(2) + re.sub(r"\s", "", m_t.group(3) or "")
                ent = docs.setdefault(full, {"summary": "", "remarks": "", "kind": "", "member_docs": {}, "bases": [],
                                             "sig_docs": {}})
                bm = re.search(r"\b%s\s*(?:<[^>]*>)?\s*(?:\([^)]*\))?\s*:\s*([^{]+)" % m_t.group(2), decl)
                if bm:
                    base_txt = re.sub(r"<[^<>]*>", "", bm.group(1).split(" where ")[0])
                    ent["bases"] += [b.strip().rsplit(".", 1)[-1] for b in base_txt.split(",") if b.strip()]
                    ent["ns"] = ns
                if not ent["kind"]:
                    km = _TYPE_KIND.search(decl)
                    ent["kind"] = (km.group(1) + km.group(2)).strip() if km else m_t.group(1)
                if buf:
                    parts = _doc_parts(buf)
                    for tag in ("summary", "remarks"):
                        ent[tag] = ent[tag] or (parts[tag] if parts[tag] != INHERIT else "")
                stack.append([full, depth, False])
            elif buf and stack and depth == stack[-1][1] + 1:
                short = stack[-1][0].rsplit(".", 1)[-1]
                name = _member_name(decl, short)
                if name:
                    summary = _doc_parts(buf)["summary"]
                    docs[stack[-1][0]]["member_docs"].setdefault(name, summary)
                    paren = decl.find("(")
                    if name != "this[]" and paren >= 0 and not re.search(r"[={;]", decl[:paren]):
                        pend = [stack[-1][0], name, summary, decl]
                        if _params_of(decl) is not None:
                            reg_sig(*pend)
                            pend = None
            buf = []
        elif not st:
            buf = []
        code = re.sub(r'"(?:\\.|[^"\\])*"', "", raw.split("//", 1)[0]) if not st.startswith("///") else ""
        depth += code.count("{") - code.count("}")
        if stack and (depth > stack[-1][1] or "{" in code):  # 한 줄 본문 형식({ … })도 열림으로 본다
            stack[-1][2] = True
        while stack and stack[-1][2] and depth <= stack[-1][1]:
            stack.pop()


def _inherited(docs, full, name, sig=None, seen=None):
    """<inheritdoc/> 멤버 설명을 기반 형식(같은 네임스페이스 우선)에서 찾는다. sig 가 있으면 오버로드 우선. 못 찾으면 ''."""
    seen = seen or set()
    ent = docs.get(full)
    if not ent or full in seen:
        return ""
    seen.add(full)
    for base in ent.get("bases", []):
        cands = [ent.get("ns", "") + "." + base] + sorted(k for k in docs if k.endswith("." + base))
        for cand in cands:
            if cand in docs:
                got = docs[cand]["sig_docs"].get(sig) or docs[cand]["member_docs"].get(name, "")
                got = _inherited(docs, cand, name, sig, seen) if got == INHERIT else got
                if got:
                    return got
                break
    return ""


def _strip_mods(line):
    mods, rest = [], line
    while True:
        m = re.match(r"(\w+) ", rest)
        if not m or m.group(1) not in MODIFIERS:
            return mods, rest
        mods.append(m.group(1))
        rest = rest[m.end():]


def _classify(mods, short, full, sig):
    """(name, member_kind, returns, const_value) 또는 None(미해석)."""
    body, _, returns = sig.rpartition(" -> ")
    if not body:
        return None
    if re.fullmatch(r"this\[.+\]\.(?:get|set|init)", body):
        return "this[]", "property", returns, None
    m = re.match(r"([A-Za-z_]\w*)(.*)$", body)
    if not m:
        return None
    name, tail = m.group(1), m.group(2)
    if re.fullmatch(r"(?:<[^()]*>)?\(.*\)", tail):
        return name, ("ctor" if name == short else "method"), returns, None
    if tail in (".get", ".set", ".init"):
        return name, "property", returns, None
    if tail.startswith(" = "):
        value = tail[3:]
        if "const" in mods:
            return name, "const", returns, value[1:-1] if value.startswith('"') and value.endswith('"') else value
        if returns == full:
            return name, "enum", returns, value
        return None
    if tail == "":
        return name, ("field" if "readonly" in mods else "event"), returns, None
    return None


def load_surface(root):
    """design_contract gen_api_docs_parse 의 dict. 추가 키: unresolved(['파일:줄: 원문'])·unparsed([{file,line,text}],
    예외 없음)·undocumented(int)·txt_line_counts·namespaces, 멤버 추가 키 sig·summary(오버로드별 설명).
    멤버 name 은 정규화 이름(속성 .get/.set 없음, 제네릭 <..> 없음, 인덱서 'this[]', 생성자 = 형식 이름)이며 txt 줄마다 1항목."""
    src = os.path.join(root, "src")
    txt_files, entries, counts = [], [], {}
    for proj in sorted(os.listdir(src)) if os.path.isdir(src) else []:
        for fn in ("PublicAPI.Shipped.txt", "PublicAPI.Unshipped.txt"):
            p = os.path.join(src, proj, fn)
            if not os.path.isfile(p):
                continue
            rel = os.path.relpath(p, root).replace(os.sep, "/")
            txt_files.append(rel)
            with open(p, encoding="utf-8-sig") as f:
                lines = f.read().splitlines()
            counts[rel] = 0
            for no, line in enumerate(lines, 1):
                if line.startswith("#"):
                    continue
                counts[rel] += 1
                entries.append((proj, rel, no, line.rstrip()))
    types, unresolved, unparsed, projects = {}, [], [], {}
    for proj, rel, no, line in entries:
        mods, rest = _strip_mods(line)
        if rest and " -> " not in rest and re.fullmatch(r"[A-Za-z_][\w.]*(?:<[\w\s,]+>)?", rest):
            short = rest.rsplit(".", 1)[-1]
            types[short] = {"full": rest, "kind": "", "members": [], "summary": "", "remarks": "", "member_docs": {}}
            projects.setdefault(proj, set()).add(rest)
    by_full = sorted(((t["full"], k) for k, t in types.items()), key=lambda x: -len(x[0]))
    for proj, rel, no, line in entries:
        mods, rest = _strip_mods(line)
        if rest in (t["full"] for t in types.values()) and " -> " not in rest:
            continue
        owner = next((k for full, k in by_full if rest.startswith(full + ".")), None)
        got = _classify(mods, re.sub(r"<.*>$", "", owner) if owner else owner, types[owner]["full"], rest[len(types[owner]["full"]) + 1:]) if owner else None
        if not got:
            unresolved.append("%s:%d: %s" % (rel, no, line))
            unparsed.append({"file": rel, "line": no, "text": line})
            continue
        name, kind, returns, value = got
        sig = "%s(%s)" % (name, _param_key(_params_of(rest))) if kind in ("method", "ctor") else None
        types[owner]["members"].append({"name": name, "raw": line, "member_kind": kind,
                                        "returns": returns, "const_value": value, "sig": sig})
    docs = {}
    for proj in sorted(projects):
        for dirpath, dirnames, filenames in os.walk(os.path.join(src, proj)):
            dirnames[:] = sorted(d for d in dirnames if d not in SKIP_DIRS)
            for fn in sorted(filenames):
                if fn.endswith(".cs"):
                    _scan_cs(os.path.join(dirpath, fn), docs)
    for t in types.values():
        d = docs.get(t["full"])
        if d:
            mdocs = {k: (_inherited(docs, t["full"], k) if v == INHERIT else v) for k, v in d["member_docs"].items()}
            t.update(kind=d["kind"], summary=d["summary"], remarks=d["remarks"],
                     member_docs={k: v for k, v in mdocs.items() if v})
        t["kind"] = t["kind"] or "unknown"
        for m in t["members"]:  # 오버로드는 매개변수 형식 열로 맞춘 설명, 없으면 이름 기준 첫 설명
            s = d["sig_docs"].get(m["sig"]) if d and m["sig"] else None
            s = _inherited(docs, t["full"], m["name"], m["sig"]) if s == INHERIT else s
            m["summary"] = s or t["member_docs"].get(m["name"], "")
    const_values = {m["const_value"] for t in types.values() for m in t["members"]
                    if m["member_kind"] == "const" and m["raw"].rstrip().endswith("string!")}
    diag = types.get("UiDiagnosticIds", {"members": [], "member_docs": {}})
    diagnostics = [{"name": m["name"], "code": m["const_value"], "summary": diag["member_docs"].get(m["name"], "")}
                   for m in diag["members"] if m["member_kind"] == "const"]
    return {"types": types, "line_count": len(entries), "diagnostics": diagnostics, "const_values": const_values,
            "txt_files": txt_files, "unresolved": unresolved, "unparsed": unparsed, "txt_line_counts": counts,
            "undocumented": sum(1 for t in types.values() for m in t["members"] if not m["summary"]),
            "namespaces": sorted({t["full"].rsplit(".", 1)[0] for t in types.values()})}
