# 소비자 문서 검사기 보조 — 소스에 선언된 네임스페이스·공개 확장 메서드 수집(K4·K6 보완)
"""consumer_docs_check.py 가 쓴다. src·testbed 의 .cs 를 읽어 (1) 실제 선언된 namespace 와 그 접두,
(2) public static class 안 `this <형식>` 공개 확장 메서드를 수신 형식 멤버로 수집한다."""
import os
import re

NS_RE = re.compile(r"^\s*namespace\s+(xkxk(?:\.\w+)*)", re.M)
EXT_RE = re.compile(r"public\s+static\s+[^;{=(]*?\b(\w+)\s*(?:<[^>(]*>)?\s*\(\s*this\s+([\w.]+)", re.S)
SCAN_DIRS = ("src", "testbed")


def cs_files(root):
    for d in SCAN_DIRS:  # os.walk 로 obj·bin 을 내려가지 않는다(NTFS 에서 glob ** 은 매우 느리다)
        for dp, dns, fns in os.walk(os.path.join(root, d)):
            dns[:] = [x for x in dns if x not in ("obj", "bin", "node_modules", ".git")]
            for f in fns:
                if f.endswith(".cs"):
                    yield os.path.join(dp, f)


def read_cs(path):
    try:
        with open(path, encoding="utf-8-sig") as fh:
            return fh.read()
    except OSError:
        return ""


def declared_namespaces(root):
    """소스에 선언된 xkxk 네임스페이스와 모든 접두(xkxk.a, xkxk.a.b …)."""
    out = set()
    for p in cs_files(root):
        for n in NS_RE.findall(read_cs(p)):
            parts = n.split(".")
            out.update(".".join(parts[:i]) for i in range(2, len(parts) + 1))
    return out


def extension_members(root):
    """{수신 형식 짧은 이름: {확장 메서드 이름}} — 공개 정적 클래스의 공개 확장 메서드."""
    out = {}
    for p in cs_files(root):
        for name, recv in EXT_RE.findall(read_cs(p)):
            out.setdefault(recv.rsplit(".", 1)[-1], set()).add(name)
    return out


def add_extensions(idx, root):
    """idx(형식→멤버→반환 형식 집합)에 확장 메서드를 멤버로 더한다(반환 형식은 모르므로 None)."""
    for recv, names in extension_members(root).items():
        if recv in idx:
            for n in names:
                idx[recv].setdefault(n, set()).add(None)


def base(name):
    n = re.sub(r"<[^>]*>", "", name.split("(")[0]).split(" =")[0].strip()
    n = re.sub(r"\.(get|set|init|add|remove)$", "", n)
    return n.rsplit(".", 1)[-1]


def short_type(t):
    t = re.sub(r"<.*", "", t or "").strip().rstrip("!?[]").strip()
    return t.rsplit(".", 1)[-1] if t else None


def index(surface):
    idx = {}
    for t, info in surface.get("types", {}).items():
        m = idx.setdefault(t, {})
        for mem in info.get("members", []):
            m.setdefault(base(mem.get("name", "")), set()).add(short_type(mem.get("returns")))
    return idx
