# PublicAPI txt 밖이지만 소비자에게 공개하기로 한 형식(명시 허용 목록)을 소스 .cs 에서 읽어 공개 표면 dict 에 더하는 모듈
"""add_extra(root, surface) 하나만 공개한다. 허용 목록 EXTRA_TYPES 의 정적 클래스만 읽고, 목록 밖 형식은 끌어오지 않는다.

- 대상: 소비자가 호출하는 확장 메서드 클래스(예 views 의 UiWindowViewRegistration). 공개 정적 메서드(public static)만 싣는다.
- 설명은 선언 바로 위 /// 의 <summary>·<remarks>(gen_api_docs_parse.xml_to_text 재사용), 제네릭 제약(where ...)은 설명 끝에 붙인다.
- 형식 이름이 PublicAPI 형식과 겹치면 PublicAPI 쪽을 우선하고 더하지 않는다.
"""
import os
import re

from gen_api_docs_parse import xml_to_text

# (소스 상대 경로, 형식 이름) — 새 형식은 여기 넣어야 명세에 실린다(views 전체를 끌어오지 않는다).
EXTRA_TYPES = [
    ("src/xkxk.frameworks.ui.views/Windows/UiWindowViewRegistration.cs", "UiWindowViewRegistration"),
]

_NAMESPACE = re.compile(r"^\s*namespace\s+([\w.]+)\s*;", re.M)
_METHOD = re.compile(
    r"((?:[ \t]*///[^\n]*\n)+)[ \t]*public\s+static\s+([\w.<>?,\[\] ]+?)\s+(\w+)(<[^()>]*>)?\(([^)]*)\)"
    r"((?:\s*where\s+[^\n]+)*)\s*(?==>|\{)")
_TYPE = re.compile(r"((?:[ \t]*///[^\n]*\n)+)[ \t]*public\s+((?:static\s+|sealed\s+|abstract\s+)*)class\s+(\w+)")


def _doc(block):
    joined = " ".join(line.strip()[3:] for line in block.splitlines())
    out = {}
    for tag in ("summary", "remarks"):
        m = re.search(r"<%s>(.*?)</%s>" % (tag, tag), joined, flags=re.S)
        out[tag] = xml_to_text(m.group(1)) if m else ""
    return out


def _params(text):
    return re.sub(r"\s+", " ", text).strip()


def add_extra(root, surface):
    """surface['types'] 에 허용 목록 형식을 더하고 surface['namespaces'] 를 갱신한다(없는 파일·형식은 건너뜀)."""
    types = surface["types"]
    for rel, name in EXTRA_TYPES:
        try:
            with open(os.path.join(root, rel), encoding="utf-8-sig") as f:
                text = f.read()
        except OSError:
            continue
        mt = next((m for m in _TYPE.finditer(text) if m.group(3) == name), None)
        mn = _NAMESPACE.search(text)
        if name in types or not mt or not mn:
            continue
        full = mn.group(1) + "." + name
        d = _doc(mt.group(1))
        members = []
        for m in _METHOD.finditer(text):
            doc = _doc(m.group(1))
            where = re.sub(r"\s+", " ", m.group(6)).strip()
            summary = doc["summary"] + (" 제약: %s." % ", ".join("`%s`" % c.strip() for c in re.split(r"\bwhere\s+", where) if c.strip()) if where else "")
            sig = "%s%s(%s) -> %s" % (m.group(3), m.group(4) or "", _params(m.group(5)), m.group(2).strip())
            members.append({"name": m.group(3), "raw": full + "." + sig, "member_kind": "method", "returns": m.group(2).strip(),
                            "const_value": None, "sig": None, "summary": summary})
        kind = (mt.group(2) + "class").strip().replace("  ", " ")
        types[name] = {"full": full, "kind": kind, "members": members, "summary": d["summary"], "remarks": d["remarks"],
                       "member_docs": {}}
    surface["namespaces"] = sorted({t["full"].rsplit(".", 1)[0] for t in types.values()})
    return surface
