# 아이콘·명령 레지스트리 판정기(icon_command_registry_check.py)의 자기시험 — 최소 모의 트리를 만들고 한 곳씩 어긋나게 해 판정 결과를 대조한다
# 사용: 직접 실행하지 않는다. icon_command_registry_check.py --self-test 가 self_test(판정기 모듈)를 부른다.
import json
import os
import shutil
import tempfile

C_CONST = 'public const string C = "xkxk.c";\n'
C_REG = '// partial 등록 파일(모의)\nAdd(r, ShellCommandIds.C, "menu.sys.c", %s, null, Sys, c => Run(c), HasShell);\n'


def build_tree(m, root, **mut):
    """판정기 시험용 최소 트리. m 은 판정기 모듈(경로 상수 출처), mut 키로 한 곳만 어긋나게 만든다."""
    cmd_keys = ["PdCmdIconExit", "PdCmdIconSave"]
    files = {
        m.F_AXAML: "".join('<StreamGeometry x:Key="%s">M0,0</StreamGeometry>\n' % k for k in
                           cmd_keys + ["PdMenuIcon1001", "PdShellIconHelp", "PdIconBusyStatic", "PdIconBadgeSlow", "PdIconBadgeError"]
                           + mut.get("extra_axaml", [])),
        m.F_CMDKEYS: "".join('public const string K%d = "%s";\n' % (i, k) for i, k in enumerate(cmd_keys)),
        m.F_CATALOG: 'public const string BusyStaticKey = "PdIconBusyStatic";\npublic const string BadgeSlowKey = "PdIconBadgeSlow";\n'
                     'public const string BadgeErrorKey = "PdIconBadgeError";\n["1001"] = MenuIconKeyPrefix + "1001",\n'
                     'public static IReadOnlyList<string> ShellIconNames { get; } = new[] { "Help" };\n',
        m.F_IDS: 'public const string A = "xkxk.a";\npublic const string B = "%s";\n%sAll = new[] { A, B%s };\n'
                 % (mut.get("b_value", "xkxk.b"), mut.get("ids_extra", ""), mut.get("all_extra", "")),
        m.F_DEFS: 'private static void Add(IUiCommandRegistry r, string id, string titleKey, string icon, string? gesture, string category) { }\n'
                  'Add(r, ShellCommandIds.A, "menu.t.a", CmdIconKeys.K0, null, Window, c => Run(c), HasShell);\n'
                  '%sAdd(r, ShellCommandIds.B, %s, %s, "Ctrl+B", Window,\n    c => Run(c), HasShell);\n'
                  % (mut.get("defs_extra", ""), mut.get("title_b", '"menu.t.b"'), mut.get("icon_b", "CmdIconKeys.K1")),
        m.F_KO: json.dumps({"menu.t.a": "가", "menu.t.b": "나"}),
        m.F_EN: json.dumps({"menu.t.a": "a", "menu.t.b": mut.get("en_b", "b")}) if "en_missing" not in mut else json.dumps({"menu.t.a": "a"}),
        m.SH + "Localization/system.ko.json": json.dumps({"menu.sys.c": "다"}),  # menu.sys.* 는 system 사전에만 있다(D3)
        m.SH + "Localization/system.en.json": json.dumps({"menu.sys.c": "c"}),
        m.F_WINICON: mut.get("winicon", "class W {\n    Geometry? FindGeometry(string? key) => UiIconGeometry.Find(this, null, key);\n}\n"),
        m.F_BITMAP: mut.get("bitmap", "var g = UiIconGeometry.Find(host, theme, key);\n"),
        m.VW + "Menus/M.axaml": mut.get("axaml_use", '<MenuIcon Key="PdMenuIcon1001"/>\n'),
    }
    if "menu_partial" in mut:
        files[m.SH + "Framework/ShellDefaultCommands.Menu.cs"] = mut["menu_partial"]
    if "drop_ids" in mut:
        del files[m.F_IDS]
    if "drop_ko" in mut:
        del files[m.F_KO]
    for rel, text in files.items():
        p = os.path.join(root, rel)
        os.makedirs(os.path.dirname(p), exist_ok=True)
        with open(p, "w", encoding="utf-8") as f:
            f.write(text)


CASES = [  # (이름, 변형, 기대)
    ("tree_consistent_passes", {}, "pass"),
    ("literal_in_comment_ignored", {"axaml_use": '<!-- <MenuIcon Key="PdMenuIcon9999"/> -->\n<MenuIcon Key="PdShellIconHelp"/>\n'}, "pass"),
    ("axaml_key_not_in_sources_fails", {"extra_axaml": ["PdCmdIconGhost"]}, "fail"),
    ("literal_unregistered_fails", {"axaml_use": '<MenuIcon Key="PdMenuIcon9999"/>\n'}, "fail"),
    ("command_id_duplicate_fails", {"b_value": "xkxk.a"}, "fail"),
    ("command_not_registered_fails", {"ids_extra": C_CONST, "all_extra": ", C"}, "fail"),
    ("title_key_missing_in_dict_fails", {"title_b": '"menu.t.nope"'}, "fail"),
    ("title_key_missing_in_en_fails", {"en_missing": 1}, "fail"),
    ("icon_not_in_cmdiconkeys_fails", {"icon_b": "CmdIconKeys.Ghost"}, "fail"),
    ("system_dict_key_in_menu_partial_passes", {"ids_extra": C_CONST, "all_extra": ", C", "menu_partial": C_REG % '"PdShellIconHelp"'}, "pass"),
    ("menu_partial_unregistered_fails", {"ids_extra": C_CONST, "all_extra": ", C", "menu_partial": "// 등록 줄 없음\n"}, "fail"),
    ("menu_partial_unknown_const_fails", {"menu_partial": C_REG.replace("ShellCommandIds.C", "ShellCommandIds.Z") % "CmdIconKeys.K1"}, "fail"),
    ("duplicate_registration_fails", {"defs_extra": 'Add(r, ShellCommandIds.A, "menu.t.a", CmdIconKeys.K0, null, Window, c => Run(c), HasShell);\n'}, "fail"),
    ("title_arg_not_literal_fails", {"title_b": "Keys.B"}, "fail"),
    ("icon_arg_missing_fails", {"icon_b": "null"}, "fail"),
    ("icon_arg_other_expr_fails", {"icon_b": 'MenuIconCatalog.ShellIconKey("Help")'}, "fail"),
    ("icon_literal_outside_keys_fails", {"icon_b": '"PdShellIconGhost"'}, "fail"),
    ("icon_cmdkeys_count_is_not_icon_fails", {"icon_b": "CmdIconKeys.Count"}, "fail"),
    ("icon_menu_catalog_expr_passes", {"icon_b": "MenuIconCatalog.GridIconKey"}, "pass"),
    ("icon_shell_literal_passes", {"icon_b": '"PdShellIconHelp"'}, "pass"),
    ("direct_lookup_in_windowicon_fails", {"winicon": "class W {\n    Geometry? FindGeometry(string? key) => TryFindResource(key, out var o) ? o as Geometry : null;\n}\n"}, "fail"),
    ("direct_lookup_in_bitmap_fails", {"bitmap": "host.TryFindResource(key, theme, out var g);\n"}, "fail"),
    ("commands_file_missing_fails", {"drop_ids": 1}, "fail"),
    ("dictionary_file_missing_fails", {"drop_ko": 1}, "fail"),
]


def self_test(m):
    """m 은 판정기 모듈(run·경로 상수). 사례마다 모의 트리를 만들어 기대와 대조한다. 0=전부 통과, 1=하나라도 어긋남."""
    bad = 0
    for name, mut, want in CASES:
        tmp = tempfile.mkdtemp()
        try:
            build_tree(m, tmp, **mut)
            got = "pass" if m.run(tmp, quiet=True) == 0 else "fail"
        finally:
            shutil.rmtree(tmp, ignore_errors=True)
        bad += got != want
        print("SELFTEST %s %s expect=%s got=%s" % ("PASS" if got == want else "FAIL", name, want, got))
    tmp = tempfile.mkdtemp()
    try:
        got = "pass" if m.run(tmp, quiet=True) == 0 else "fail"  # 빈 트리는 통과하면 안 된다
    finally:
        shutil.rmtree(tmp, ignore_errors=True)
    bad += got != "fail"
    print("SELFTEST %s empty_tree_fails expect=fail got=%s" % ("PASS" if got == "fail" else "FAIL", got))
    return 1 if bad else 0
