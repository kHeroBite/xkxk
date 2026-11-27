# 공개 API 명세(docs/api/*.md) 생성기 CLI — 쓰기·--check(낡음 검사)·--prune·--self-test, render_all 공개
"""사용: python3 -B tools/gen_api_docs.py [--root R] [--check] [--prune] [--self-test]

- 기본: 바뀐 생성 파일만 쓴다. docs/api/ 의 생성 대상이 아닌 .md 는 '남음' 으로 출력만 하고, --prune 이면 지운다.
- --check: 디스크와 비교해 파일별 '다름|없음|남음' 을 출력한다. 다르면 rc=1.
- rc: 0 PASS · 1 불일치 · 2 입력 오류(PublicAPI txt 없음·미해석 줄).
- 끝 줄: 'api_docs files=<n> types=<n> members=<n> undocumented=<n> result=PASS|FAIL'.
"""
import argparse
import glob
import os
import re
import shutil
import sys
import tempfile

sys.dont_write_bytecode = True
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from gen_api_docs_extra import add_extra  # noqa: E402
from gen_api_docs_parse import load_surface  # noqa: E402
from gen_api_docs_render import OUT_DIR, render, stats_of  # noqa: E402

DEFAULT_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def load_menu_rows(root):
    """셸 기본 메뉴 표(src/*/Navigation/ShellMenuTable.cs) 행을 {ID: (부모|None, 종류, 형제 순서, 표 순번, 명령ID|None, 인자|None)}
    로 읽는다. 없으면 {}."""
    rows, order = {}, {}

    def lit(v):
        return None if v == "null" else v[1:-1] if v.startswith('"') and v.endswith('"') else v

    for path in sorted(glob.glob(os.path.join(root, "src", "*", "Navigation", "ShellMenuTable.cs"))):
        with open(path, encoding="utf-8-sig") as f:
            text = f.read()
        for m in re.finditer(r'new\(\s*"([^"]+)"\s*,\s*(null|"[^"]*")\s*,\s*ShellMenuRowKind\.(\w+)\s*,\s*([^,]+?)\s*,'
                             r'\s*([^,]+?)\s*,', text):
            parent = lit(m.group(2))
            order[parent] = order.get(parent, 0) + 1
            rows.setdefault(m.group(1), (parent, m.group(3), str(order[parent]), len(rows), lit(m.group(4)),
                                         lit(m.group(5))))
    return rows


def _surface(root):
    surface = add_extra(root, load_surface(root))
    surface["menu_rows"] = load_menu_rows(root)
    return surface


def render_all(root):
    """{'docs/api/<파일>.md'(root 상대): 전체 텍스트(LF, 끝 개행 1개)}."""
    return render(_surface(root))


def _read(path):
    try:
        with open(path, "rb") as f:
            return f.read()
    except OSError:
        return None


def _extras(root, expected):
    d = os.path.join(root, OUT_DIR)
    if not os.path.isdir(d):
        return []
    return sorted("%s/%s" % (OUT_DIR, fn) for fn in os.listdir(d)
                  if fn.endswith(".md") and "%s/%s" % (OUT_DIR, fn) not in expected)


def run(root, check=False, prune=False):
    surface = _surface(root)
    st = stats_of(surface)
    rc = 0
    if not surface["txt_files"]:
        print("입력 오류: %s/src/*/PublicAPI.*.txt 가 없다" % root)
        rc = 2
    for u in surface["unresolved"]:
        print("미해석 %s" % u)
        rc = 2
    outputs = render(surface) if rc == 0 else {}
    if rc == 0:
        for rel in sorted(outputs):
            data = outputs[rel].encode("utf-8")
            disk = _read(os.path.join(root, rel))
            if disk == data:
                continue
            if check:
                print("  %s %s" % ("없음" if disk is None else "다름", rel))
                rc = 1
            else:
                path = os.path.join(root, rel)
                os.makedirs(os.path.dirname(path), exist_ok=True)
                with open(path, "wb") as f:
                    f.write(data)
                print("  썼음 %s" % rel)
        for rel in _extras(root, outputs):
            if prune and not check:
                os.remove(os.path.join(root, rel))
                print("  지움 %s" % rel)
            else:
                print("  남음 %s%s" % (rel, "" if check else " (--prune 으로 지운다)"))
                rc = 1 if check else rc
    print("api_docs files=%d types=%d members=%d undocumented=%d result=%s" % (
        len(outputs), st["types"], st["members"], st["undocumented"], "PASS" if rc == 0 else "FAIL"))
    return rc


# ---------- 자기시험(T1) ----------
_TXT = """#nullable enable
Ns.Fw.IFoo
Ns.Fw.IFoo.Changed -> System.EventHandler?
Ns.Fw.IFoo.Get<T>(string! key) -> T!
Ns.Fw.IFoo.Get<T>(System.Collections.Generic.IList<int>! keys, out string? note) -> T!
Ns.Fw.IFoo.this[string! key].get -> string!
Ns.Fw.Ids
const Ns.Fw.Ids.A = "xkxk.a" -> string!
Ns.Fw.Kind
Ns.Fw.Kind.One = 1 -> Ns.Fw.Kind
Ns.Fw.Foo
Ns.Fw.Foo.Foo() -> void
override Ns.Fw.Foo.ToString() -> string!
Ns.Fw.Foo.Name.get -> string?
"""
_CS1 = """// 가짜
namespace Ns.Fw;
/// <summary>푸 <see cref="Ids.A"/> 와 <c>x</c> &lt;b&gt;</summary>
/// <remarks>첫<para>둘</para></remarks>
public interface IFoo
{
    /// <summary>바뀜</summary>
    event System.EventHandler? Changed;
    /// <summary>얻기 <paramref name="key"/></summary>
    T Get<T>(string key);
    /// <summary>여럿 얻기</summary>
    T Get<T>(IList<int> keys,
        out string? note);
    /// <summary>인덱서</summary>
    string this[string key] { get; }
}
"""
_CS2 = """namespace Ns.Fw;
/// <summary>상수</summary>
public static class Ids
{
    /// <summary>에이</summary>
    public const string A = "xkxk.a";
}
/// <summary>종류</summary>
public enum Kind
{
    /// <summary>하나</summary>
    One = 1,
}
/// <summary>구현</summary>
public sealed class Foo : IFoo
{
    private sealed class Inner { public void Hidden() { } }
    /// <summary>생성자</summary>
    public Foo() { }
    /// <inheritdoc />
    public override string ToString() => "f";
    /// <summary>이름</summary>
    public string? Name { get; }
}
"""


def _write(path, text):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8", newline="") as f:
        f.write(text)


def _make_tree(tmp, txt=_TXT):
    proj = os.path.join(tmp, "src", "fake.proj")
    _write(os.path.join(proj, "PublicAPI.Shipped.txt"), "#nullable enable\n")
    _write(os.path.join(proj, "PublicAPI.Unshipped.txt"), txt)
    _write(os.path.join(proj, "Fw", "IFoo.cs"), _CS1)
    _write(os.path.join(proj, "Fw", "Ids.cs"), _CS2)
    return tmp


def self_test():
    results = []

    def ok(name, cond):
        results.append(cond)
        print("  %s %s" % ("PASS" if cond else "FAIL", name))

    tmp = tempfile.mkdtemp(prefix="gen_api_docs_")
    try:
        root = _make_tree(os.path.join(tmp, "a"))
        s = load_surface(root)
        t = s["types"]
        kinds = {(k, m["name"]): m["member_kind"] for k, v in t.items() for m in v["members"]}
        ok("형식 4·멤버 9·줄 13", len(t) == 4 and sum(len(v["members"]) for v in t.values()) == 9
           and s["line_count"] == 13 and not s["unresolved"] and not s["unparsed"])
        gets = [m["summary"] for m in t["IFoo"]["members"] if m["name"] == "Get"]
        ok("오버로드별 설명(여러 줄 선언 포함)", gets == ["얻기 `key`", "여럿 얻기"])
        ok("member_kind 규칙(event·method·indexer·const·enum·ctor·override)", kinds == {
            ("IFoo", "Changed"): "event", ("IFoo", "Get"): "method", ("IFoo", "this[]"): "property",
            ("Ids", "A"): "const", ("Kind", "One"): "enum", ("Foo", "Foo"): "ctor", ("Foo", "ToString"): "method",
            ("Foo", "Name"): "property"})
        ok("const 값·const_values", t["Ids"]["members"][0]["const_value"] == "xkxk.a" and s["const_values"] == {"xkxk.a"})
        ok("주석 태그 제거(cref·c·엔티티·para)", t["IFoo"]["summary"] == "푸 `Ids.A` 와 `x` <b>"
           and t["IFoo"]["remarks"] == "첫\n둘" and t["IFoo"]["member_docs"].get("Get") == "얻기 `key`")
        ok("종류·중첩 private 형식 무시", t["Foo"]["kind"] == "sealed class" and t["Kind"]["kind"] == "enum"
           and "Hidden" not in t["Foo"]["member_docs"] and t["Foo"]["member_docs"].get("Name") == "이름")
        out1, out2 = render(s), render(load_surface(root))
        ok("결정성(2회 동일)", out1 == out2 and all(v.endswith("\n") and not v.endswith("\n\n") for v in out1.values()))
        ok("생성물 머리·미분류 99", all(v.startswith("<!-- 생성물") for v in out1.values())
           and "docs/api/99_미분류.md" in out1)
        gen_out = render({"types": {"G<T>": {"full": "Ns.G<T>", "kind": "class", "members": [], "summary": "a <b> `c<d>`",
                                              "remarks": "", "member_docs": {}}}, "namespaces": ["Ns"]})
        ok("제네릭 제목·본문 꺾쇠 이스케이프(코드 스팬 안은 그대로)", "## G\\<T\\>" in gen_out["docs/api/99_미분류.md"]
           and "a \\<b\\> `c<d>`" in gen_out["docs/api/99_미분류.md"])
        ok("쓰기 후 --check rc=0", run(root) == 0 and run(root, check=True) == 0)
        _write(os.path.join(root, "docs", "api", "README.md"), "x\n")
        _write(os.path.join(root, "docs", "api", "zz.md"), "x\n")
        ok("낡음·남음 --check rc=1", run(root, check=True) == 1)
        ok("--prune 뒤 rc=0", run(root, prune=True) == 0 and run(root, check=True) == 0)
        many = _TXT + "".join('const Ns.Fw.Ids.C%03d = "xkxk.c%03d" -> string!\n' % (i, i) for i in range(400))
        big = render(load_surface(_make_tree(os.path.join(tmp, "b"), many)))
        ok("300줄 분할", "docs/api/99_미분류_2.md" in big and all(v.count("\n") <= 300 for v in big.values())
           and sum(v.count("xkxk.c") for v in big.values()) >= 400)
        bad = _make_tree(os.path.join(tmp, "c"), _TXT + "Ns.Fw.Foo.Weird stuff -> int\nNo.Such.Type.X.get -> int\n")
        ok("미해석 줄 rc=2", len(load_surface(bad)["unresolved"]) == 2 and run(bad, check=True) == 2)
        ok("txt 없음 rc=2", run(os.path.join(tmp, "none"), check=True) == 2)
    finally:
        shutil.rmtree(tmp, ignore_errors=True)
    passed = sum(results)
    print("api_docs_selftest passed=%d total=%d result=%s" % (passed, len(results),
                                                             "PASS" if passed == len(results) else "FAIL"))
    return 0 if passed == len(results) else 1


def main(argv=None):
    ap = argparse.ArgumentParser(description="공개 API 명세(docs/api) 생성기")
    ap.add_argument("--root", default=DEFAULT_ROOT, help="영역 루트(기본: 이 파일의 상위 폴더)")
    ap.add_argument("--check", action="store_true", help="쓰지 않고 디스크와 비교(다르면 rc=1)")
    ap.add_argument("--prune", action="store_true", help="생성 대상이 아닌 docs/api/*.md 를 지운다")
    ap.add_argument("--self-test", action="store_true", help="임시 트리로 자기시험")
    a = ap.parse_args(argv)
    if a.self_test:
        return self_test()
    return run(os.path.abspath(a.root), check=a.check, prune=a.prune)


if __name__ == "__main__":
    sys.exit(main())
