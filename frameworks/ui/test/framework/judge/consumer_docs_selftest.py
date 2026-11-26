# 소비자 문서 불일치 검사기 자체 시험 — 가짜 트리(txt·Framework·docs) 사례 18건으로 K1~K10·W1 탐지와 거짓 FAIL 없음을 확인
"""consumer_docs_check.py --self-test 가 부른다. 임시 트리는 tempfile 로 만들고 finally 로 지운다.

가짜 트리 = <T>/xkxk/frameworks/ui (api README 의 ../../../../docs/standards 링크가 <T>/xkxk/docs 로 풀리게 한다).
생성물 docs/api 는 실제 tools/gen_api_docs.render_all 로 만든다(검사기와 같은 생성기).
"""
import os
import shutil
import tempfile

import consumer_docs_check as cdc

NS = "xkxk.frameworks.ui.shell.Framework"
TXT = [
    "#nullable enable",
    f"abstract {NS}.UiModule.Configure({NS}.IUiModuleContext! context) -> void",
    f"abstract {NS}.UiModule.Id.get -> string!",
    f'const {NS}.ShellMenuIds.FileExit = "xkxk.file.exit" -> string!',
    f'const {NS}.UiDiagnosticIds.MenuIdReserved = "XKUI0401" -> string!',
    f"static {NS}.XkxkUi.Current.get -> {NS}.IUiAppContext!",
    f"{NS}.IUiAppContext",
    f"{NS}.IUiAppContext.Menus.get -> {NS}.IUiMenuRegistry!",
    f"{NS}.IUiMenuRegistry",
    f"{NS}.IUiMenuRegistry.SetHidden(string! id, bool hidden) -> void",
    f"{NS}.IUiModuleContext",
    f"{NS}.IUiModuleContext.Menus.get -> {NS}.IUiMenuRegistry!",
    f"{NS}.ShellMenuIds",
    f"{NS}.UiDiagnosticIds",
    f"{NS}.UiModule",
    f"{NS}.UiModule.UiModule() -> void",
    f"{NS}.XkxkUi",
]
CS = {
    "IUiAppContext": "/// <summary>앱 컨텍스트.</summary>\npublic interface IUiAppContext\n{\n"
                     "    /// <summary>메뉴 레지스트리.</summary>\n    IUiMenuRegistry Menus { get; }\n}",
    "IUiMenuRegistry": "/// <summary>메뉴 레지스트리.</summary>\npublic interface IUiMenuRegistry\n{\n"
                       "    /// <summary>항목을 숨긴다.</summary>\n    void SetHidden(string id, bool hidden);\n}",
    "IUiModuleContext": "/// <summary>모듈 구성 문맥.</summary>\npublic interface IUiModuleContext\n{\n"
                        "    /// <summary>메뉴 레지스트리.</summary>\n    IUiMenuRegistry Menus { get; }\n}",
    "ShellMenuIds": "/// <summary>셸 메뉴 ID.</summary>\npublic static class ShellMenuIds\n{\n"
                    "    /// <summary>끝내기.</summary>\n    public const string FileExit = \"xkxk.file.exit\";\n}",
    "UiDiagnosticIds": "/// <summary>진단 코드.</summary>\npublic static class UiDiagnosticIds\n{\n"
                       "    /// <summary>예약 접두어 위반(오류).</summary>\n    public const string MenuIdReserved = \"XKUI0401\";\n}",
    "UiModule": "/// <summary>모듈 기반 형식.</summary>\npublic abstract class UiModule\n{\n"
                "    /// <summary>생성자.</summary>\n    protected UiModule() { }\n"
                "    /// <summary>모듈 ID.</summary>\n    public abstract string Id { get; }\n"
                "    /// <summary>구성한다.</summary>\n    public abstract void Configure(IUiModuleContext context);\n}",
    "XkxkUi": "/// <summary>진입점.</summary>\npublic static class XkxkUi\n{\n"
              "    /// <summary>현재 앱 컨텍스트.</summary>\n    public static IUiAppContext Current => null!;\n}",
}
HEAD = "상태: 사전판 · 최종 갱신일: 2026-11-26\n"
DOCS = {
    "README.md": "<!-- 별별UI 소비자 문서 진입점(시험용) -->\n# 문서\n" + HEAD +
                 "\n- [시작](01_시작하기.md)\n- [확장](02_확장_메뉴명령아이콘.md)\n- [AI](03_AI_활용안내.md)\n- [API](api/README.md)\n",
    "01_시작하기.md": "<!-- 시작하기(시험용) -->\n# 시작하기\n" + HEAD +
        "\n`XkxkUi.Current.Menus.SetHidden` 로 숨긴다. 진단 `XKUI0401`, ID `xkxk.file.exit`, 접두 `xkxk.*`·`xkxk.file.…`·`xkxk.`.\n"
        "프로젝트 `xkxk.frameworks.ui.shell.csproj`, 파일 `UiModule.cs`, 형식 `xkxk.frameworks.ui.shell.Framework.UiModule`,"
        " 경로 `src/xkxk.frameworks.ui.shell/PublicAPI.Unshipped.txt`, 외부 `System.Console`.\n\n"
        "```csharp\nusing xkxk.frameworks.ui.shell.Framework;\n\npublic sealed class AcmeModule : UiModule\n{\n"
        "    public override string Id => \"acme.tools\";\n    public override void Configure(IUiModuleContext ui)\n    {\n"
        "        ui.Menus.SetHidden(ShellMenuIds.FileExit, true);\n    }\n}\n```\n",
    "02_확장_메뉴명령아이콘.md": "<!-- 확장(시험용) -->\n# 확장\n" + HEAD + "\n[명세](api/README.md), [시작](01_시작하기.md#시작하기)\n",
    "03_AI_활용안내.md": "<!-- AI 안내(시험용) -->\n# AI\n" + HEAD + "\n" + cdc.MARK_B + "\n- 공개 API 만 쓴다\n" + cdc.MARK_E + "\n",
}


def w(path, text):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8", newline="\n") as fh:
        fh.write(text)


def regen(root):
    gen = cdc.load_gen()[0]
    for rel, text in gen.render_all(root).items():
        w(os.path.join(root, rel), text)


def build(root):
    shell = os.path.join(root, "src", "xkxk.frameworks.ui.shell")
    w(os.path.join(shell, "PublicAPI.Shipped.txt"), "#nullable enable\n")
    w(os.path.join(shell, "PublicAPI.Unshipped.txt"), "\n".join(TXT) + "\n")
    for name, body in CS.items():
        w(os.path.join(shell, "Framework", name + ".cs"), f"// 시험용 {name}\nnamespace {NS};\n\n{body}\n")
    for rel, text in DOCS.items():
        w(os.path.join(root, "docs", rel), text)
    std = os.path.join(root, "..", "..", "docs", "standards", "02_공개API_호환원칙.md")
    w(os.path.normpath(std), "<!-- 시험용 -->\n# 호환 원칙\n")
    regen(root)


def edit(root, rel, old, new):
    p = os.path.join(root, rel)
    text = cdc.read(p)
    assert old in text, (rel, old)
    w(p, text.replace(old, new, 1))


def append(root, rel, text):
    w(os.path.join(root, rel), cdc.read(os.path.join(root, rel)) + text)


TXT_REL = "src/xkxk.frameworks.ui.shell/PublicAPI.Unshipped.txt"
D01, D03 = "docs/01_시작하기.md", "docs/03_AI_활용안내.md"


def add_undocumented(root):
    append(root, TXT_REL, f"{NS}.IUiMenuRegistry.Remove(string! id) -> bool\n")
    edit(root, "src/xkxk.frameworks.ui.shell/Framework/IUiMenuRegistry.cs", "hidden);\n", "hidden);\n    bool Remove(string id);\n")
    regen(root)


# (이름, 변형, strict, 기대 rc 0 여부, 기대 FAIL id)
CASES = [
    ("기준 트리 PASS(거짓 FAIL 없음)", None, False, True, None),
    ("기준 트리 --strict PASS(설명 누락 0)", None, True, True, None),
    ("K1 txt 줄 추가 → 생성물 낡음", lambda r: append(r, TXT_REL, f"{NS}.IUiMenuRegistry.Count.get -> int\n"), False, False, "K1"),
    ("K1 생성 대상 아닌 api 파일 남음", lambda r: w(os.path.join(r, "docs/api/zz_옛파일.md"), "# 옛\n"), False, False, "K1"),
    ("K2 첫 줄 헤더 없음", lambda r: edit(r, "docs/02_확장_메뉴명령아이콘.md", "<!-- 확장(시험용) -->", "# 머리 없음"), False, False, "K2"),
    ("K2 300줄 초과", lambda r: append(r, "docs/02_확장_메뉴명령아이콘.md", "줄\n" * 300), False, False, "K2"),
    ("K3 없는 진단 코드", lambda r: append(r, D01, "진단 `XKUI9999` 확인.\n"), False, False, "K3"),
    ("K4 없는 xkxk. ID", lambda r: append(r, D01, "ID `xkxk.nosuch.id` 를 쓴다.\n"), False, False, "K4"),
    ("K5 없는 멤버", lambda r: append(r, D01, "`IUiMenuRegistry.NoSuch` 를 부른다.\n"), False, False, "K5"),
    ("K6 csharp ui.Menus.없는멤버", lambda r: edit(r, D01, "ui.Menus.SetHidden(", "ui.Menus.NoSuchMember("), False, False, "K6"),
    ("K6 XkxkUi.Current.Menus.없는멤버", lambda r: append(r, D01, "`XkxkUi.Current.Menus.Nope` 는 없다.\n"), False, False, "K6"),
    ("K7 깨진 상대 링크", lambda r: append(r, D01, "[없음](없는파일.md)\n"), False, False, "K7"),
    ("K8 README 미링크 문서", lambda r: w(os.path.join(r, "docs/04_추가.md"), "<!-- 추가 -->\n# 추가\n"), False, False, "K8"),
    ("K9 AI 마커 없음", lambda r: edit(r, D03, cdc.MARK_E, "끝"), False, False, "K9"),
    ("K9 AI 블록 81줄", lambda r: edit(r, D03, "- 공개 API 만 쓴다\n", "- 규칙\n" * 81), False, False, "K9"),
    ("K10 미해석 PublicAPI 줄", lambda r: append(r, TXT_REL, "this is not an api line\n"), False, False, "K10"),
    ("W1 --strict 설명 없는 멤버 FAIL", add_undocumented, True, False, "W1"),
    ("W1 기본 모드는 WARN 만(rc 0)", add_undocumented, False, True, None),
]


def main():
    tmp = tempfile.mkdtemp(prefix="consumer_docs_st_")
    passed = failed = 0
    try:
        tpl = os.path.join(tmp, "tpl", "xkxk", "frameworks", "ui")
        build(tpl)
        for i, (name, mut, strict, want_ok, want_id) in enumerate(CASES, 1):
            case = os.path.join(tmp, f"c{i:02d}")
            shutil.copytree(os.path.join(tmp, "tpl"), case)
            root = os.path.join(case, "xkxk", "frameworks", "ui")
            try:
                if mut:
                    mut(root)
                rc, rep, _ = cdc.run(root, strict)
                ids = sorted({f["id"] for f in rep.fails})
                good = (rc == 0 and not ids) if want_ok else (rc != 0 and want_id in ids)
                if name.startswith("W1 기본"):
                    good = good and any(x["id"] == "W1" for x in rep.warns)
                detail = f"rc={rc} fail_ids={ids} " + "; ".join(f"{f['id']} {f['where']} {f['msg']}" for f in rep.fails[:3])
            except Exception as e:  # 사례 준비·실행 오류도 실패로 센다
                good, detail = False, f"예외 {type(e).__name__}: {e}"
            if good:
                passed += 1
                print(f"PASS {i:02d} {name}")
            else:
                failed += 1
                print(f"FAIL {i:02d} {name} — {detail}")
    finally:
        shutil.rmtree(tmp, ignore_errors=True)
    total = passed + failed
    print(f"consumer_docs_selftest cases={total} pass={passed} fail={failed} result={'PASS' if failed == 0 and total >= 12 else 'FAIL'}")
    return 0 if failed == 0 and total >= 12 else 1


if __name__ == "__main__":
    import sys
    sys.exit(main())
