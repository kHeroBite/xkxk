# 메뉴 레지스트리 판정기 자체 시험 — 모의 트리 사례 23건(menu_registry_check 분할 모듈)
import json
import os
import re
import shutil
import tempfile

from menu_registry_rules import *  # noqa: F401,F403


# ── 자체 시험: 실산출물 꼴의 작은 트리(기준 4메뉴 + 기준 카탈로그 → 표·섹션·파생 카탈로그) ──
def _ax(root_aid, key, items):
    return ('<MenuItem xmlns="https://github.com/avaloniaui" Header="{loc:Tr %s}" AutomationProperties.AutomationId="%s">\n%s</MenuItem>\n'
            % (key, root_aid, items))


ST_BASE = {
    VW + "Menus/FileMenu.axaml": _ax("shell.mnuFile", "menu.file",
                                     '  <MenuItem Header="{loc:Tr menu.file.openGrid}" AutomationProperties.AutomationId="shell.mnuOpenGrid" />\n'
                                     '  <MenuItem Header="{loc:Tr menu.file.openSdi}" AutomationProperties.AutomationId="shell.mnuOpenSdiGroup">\n'
                                     '    <MenuItem Header="{loc:Tr menu.file.openGridSdi}" AutomationProperties.AutomationId="shell.mnuOpenGridSdi" />\n'
                                     '  </MenuItem>\n  <Separator />\n'
                                     '  <MenuItem Header="{loc:Tr menu.file.exit}" AutomationProperties.AutomationId="shell.mnuExit" />\n'),
    VW + "Menus/WindowMenu.axaml": _ax("shell.mnuWindow", "menu.window",
                                       '  <MenuItem Header="{loc:Tr menu.window.next}" AutomationProperties.AutomationId="shell.mnuNext" />\n'
                                       '  <MenuItem Header="{Binding A}" AutomationProperties.AutomationId="shell.mnuWinList1" />\n'
                                       '  <MenuItem Header="{Binding B}" AutomationProperties.AutomationId="shell.mnuWinList2" />\n'
                                       '  <MenuItem Header="{loc:Tr menu.window.more}" AutomationProperties.AutomationId="shell.mnuWinListMore" />\n'),
    VW + "Menus/ViewMenu.axaml": _ax("shell.mnuView", "menu.view",
                                     '  <MenuItem Header="{loc:Tr menu.view.settings}" AutomationProperties.AutomationId="shell.mnuSettings" />\n'),
    VW + "Menus/SystemMenuItem.axaml": _ax("shell.mnuSys", "menu.sys",
                                           '  <MenuItem Header="{loc:Tr menu.sys.settings}" AutomationProperties.AutomationId="shell.mnuSysSettings" />\n'
                                           '  <MenuItem Header="{loc:Tr menu.sys.help}" AutomationProperties.AutomationId="shell.mnuSysHelp" />\n'),
    F_NAV: """// 기준 카탈로그(모의)
public static class NavMenuCatalog
{
    public const string ReasonPcTablet = "nav.reason.pcTablet";
    private const string IconDocs = "M2,2 H12 V12 H2 Z";
    private const string IconSettings = "M1,3 H17 V5 H1 Z";
    public static IReadOnlyList<NavEntry> Entries { get; } = Build();
    private static List<NavEntry> Build()
    {
        const NavGroup F = NavGroup.File, W = NavGroup.Window, V = NavGroup.View, S = NavGroup.System;
        return new List<NavEntry>
        {
            G(F, 0, "File", "menu.file"),
            I(F, 1, "OpenGrid", "menu.file.openGrid", "Open", "Grid"),
            X(F, 1, "OpenSdiGroup", "menu.file.openSdi", true),
            X(F, 2, "OpenGridSdi", "menu.file.openGridSdi"),
            X(F, 1, "Exit", "menu.file.exit"),
            G(W, 0, "Window", "menu.window"),
            I(W, 1, "Next", "menu.window.next", "Next"),
            Tab(I(W, 1, "WinList1", "nav.tab.docs", "Panel", "docs", "WinList2", "WinListMore"), 1, "nav.tab.docs", IconDocs),
            G(V, 0, "View", "menu.view"),
            Tab(I(V, 1, "Settings", "menu.view.settings", "Panel", "settings", "SysSettings"), 3, "nav.tab.settings", IconSettings),
            G(S, 0, "Sys", "menu.sys"),
            I(S, 1, "SysHelp", "menu.sys.help", "SystemView", "Help"),
        };
    }
}
""",
}
# (Id, Parent, Kind, CommandId, Param, TitleKey, 아이콘 식, AutomationId, Section, 모바일 식) — D=서랍, P=PC·태블릿 전용, H=서랍 밖
ST_ROWS = [
    ("xkxk.file", None, "Submenu", None, None, "menu.file", None, "shell.mnuFile", None, "Drawer"),
    ("xkxk.file.openGrid", "xkxk.file", "Command", "xkxk.demo.open", "Grid", "menu.file.openGrid", "MenuIconCatalog.GridIconKey", "shell.mnuOpenGrid", None, "Drawer"),
    ("xkxk.file.openSdiGroup", "xkxk.file", "Submenu", None, None, "menu.file.openSdi", "CmdIconKeys.OpenSdi", "shell.mnuOpenSdiGroup", None, "PcOnly"),
    ("xkxk.file.openGridSdi", "xkxk.file.openSdiGroup", "Command", "xkxk.demo.openSdi", "Grid", "menu.file.openGridSdi", "MenuIconCatalog.GridIconKey",
     "shell.mnuOpenGridSdi", None, "PcOnly"),
    ("xkxk.file.sep1", "xkxk.file", "Separator", None, None, "", None, None, None, "Hidden"),
    ("xkxk.file.exit", "xkxk.file", "Command", "xkxk.app.exit", None, "menu.file.exit", "CmdIconKeys.Exit", "shell.mnuExit", None, "PcOnly"),
    ("xkxk.window", None, "Submenu", None, None, "menu.window", None, "shell.mnuWindow", None, "Drawer"),
    ("xkxk.window.next", "xkxk.window", "Command", "xkxk.window.next", None, "menu.window.next", "CmdIconKeys.Next", "shell.mnuNext", None, "Drawer"),
    ("xkxk.window.openList", "xkxk.window", "Section", None, None, "", None, None, "xkxk.window.openList",
     'new(true, true, null, "WinList1", "nav.tab.docs", "xkxk.nav.openPanel", "docs", 1, "nav.tab.docs", IconDocs, new[] { "WinList2", "WinListMore" })'),
    ("xkxk.view", None, "Submenu", None, None, "menu.view", None, "shell.mnuView", None, "Drawer"),
    ("xkxk.view.settings", "xkxk.view", "Command", "xkxk.system.settings", None, "menu.view.settings", '"PdShellIconSettings"', "shell.mnuSettings", None,
     'new ShellMenuMobileRow(true, true, null, null, null, "xkxk.nav.openPanel", "settings", 3, "nav.tab.settings", IconSettings, new[] { "SysSettings" })'),
    ("xkxk.system", None, "Submenu", None, None, "menu.sys", None, "shell.mnuSys", None, "Drawer"),
    ("xkxk.system.settings", "xkxk.system", "Command", "xkxk.system.settings", None, "menu.sys.settings", '"PdShellIconSettings"', "shell.mnuSysSettings", None, "Hidden"),
    ("xkxk.system.help", "xkxk.system", "Command", "xkxk.system.help", None, "menu.sys.help", '"PdShellIconHelp"', "shell.mnuSysHelp", None, "Drawer"),
]
ST_HEAD = """// 셸 4메뉴 표(자체 시험 모의) — 행 1줄 = 항목 1개
namespace xkxk.frameworks.ui.data.Navigation;
public enum ShellMenuRowKind { Command, Submenu, Separator, Section }
public sealed record ShellMenuMobileRow(bool InDrawer, bool Available, string? ReasonKey, string? EntryId, string? TitleKey, string? CommandId, string? Param,
    int TabOrder, string? TabKey, string? TabIcon, string[] Covers);
public sealed record ShellMenuRow(string Id, string? Parent, ShellMenuRowKind Kind, string? CommandId, string? Param, string TitleKey, string? Icon,
    string? AutomationId, string? Section, string? VisibleWhen, string? ToolTipWhen, ShellMenuMobileRow Mobile);
public static class ShellMenuTable
{
    private const string IconDocs = "M2,2 H12 V12 H2 Z";
    private const string IconSettings = "M1,3 H17 V5 H1 Z";
    private static readonly ShellMenuMobileRow Drawer = new(true, true, null, null, null, null, null, -1, null, null, Array.Empty<string>());
    private static readonly ShellMenuMobileRow PcOnly = Drawer with { Available = false, ReasonKey = NavMenuCatalog.ReasonPcTablet };
    private static readonly ShellMenuMobileRow Hidden = new(InDrawer: false, Available: true, ReasonKey: null, EntryId: null, TitleKey: null, CommandId: null,
        Param: null, TabOrder: -1, TabKey: null, TabIcon: null, Covers: []);
"""


def _q(v):
    return "null" if v is None else '"%s"' % v


def st_table(style):
    """style=positional: new(…) 12 인자, style=helper: 도우미·이름 인자·with."""
    out = [ST_HEAD]
    if style == "helper":
        out.append("    private static ShellMenuRow Sub(string id, string? parent, string title, string? icon, string aid, ShellMenuMobileRow mobile) =>\n"
                   "        new(id, parent, ShellMenuRowKind.Submenu, null, null, title, icon, aid, null, null, null, mobile);\n"
                   "    private static ShellMenuRow Cmd(string id, string parent, string cmd, string title, string? icon, string aid, ShellMenuMobileRow mobile,"
                   " string? param = null)\n    {\n        return new ShellMenuRow(id, parent, ShellMenuRowKind.Command, cmd, param, title, icon, aid, null, null, null, mobile);\n    }\n")
    out.append("    public static IReadOnlyList<ShellMenuRow> Rows { get; } = new ShellMenuRow[]\n    {\n")
    for rid, par, kind, cmd, prm, title, icon, aid, sec, mob in ST_ROWS:
        if style == "helper" and kind == "Submenu":
            out.append('        Sub("%s", %s, "%s", %s, "%s", %s),\n' % (rid, _q(par), title, icon or "null", aid, mob))
        elif style == "helper" and kind == "Command":
            tail = ', param: "%s"' % prm if prm else ""
            w = ' with { VisibleWhen = "bench" }' if rid == "xkxk.file.openGrid" else ""
            out.append('        Cmd("%s", "%s", "%s", "%s", %s, "%s", %s%s)%s,\n' % (rid, par, cmd, title, icon, aid, mob, tail, w))
        else:
            out.append('        new(%s, %s, ShellMenuRowKind.%s, %s, %s, "%s", %s, %s, %s, null, null, %s),\n'
                       % (_q(rid), _q(par), kind, _q(cmd), _q(prm), title, icon or "null", _q(aid), _q(sec), mob))
    out.append("    };\n}\n")
    return "".join(out)


def st_files(style="positional"):
    ko = {k: "가" for k in ("menu.file", "menu.file.openGrid", "menu.file.openSdi", "menu.file.openGridSdi", "menu.file.exit", "menu.window",
                            "menu.window.next", "menu.view", "menu.view.settings", "nav.tab.docs", "nav.tab.settings", "nav.reason.pcTablet")}
    sysd = {k: "가" for k in ("menu.sys", "menu.sys.settings", "menu.sys.help")}  # D3: menu.sys.* 는 system 사전에만 있다
    ids = ["DemoOpen = \"xkxk.demo.open\"", "DemoOpenSdi = \"xkxk.demo.openSdi\"", "AppExit = \"xkxk.app.exit\"", "WindowNext = \"xkxk.window.next\"",
           "SystemSettings = \"xkxk.system.settings\"", "SystemHelp = \"xkxk.system.help\"", "NavOpenPanel = \"xkxk.nav.openPanel\""]
    return {
        NAV + "ShellMenuTable.cs": st_table(style),
        NAV + "NavMenuCatalog.cs": "// 파생 카탈로그(모의)\npublic static class NavMenuCatalog\n{\n    public const string ReasonPcTablet = \"nav.reason.pcTablet\";\n"
                                   "    public static IReadOnlyList<NavEntry> Entries { get; } = Derive(ShellMenuTable.Rows);\n}\n",
        NAV + "CmdIconKeys.Generated.cs": "".join('    public const string %s = "PdCmdIcon%s";\n' % (n, n) for n in ("OpenSdi", "Exit", "Next")),
        NAV + "MenuIconCatalog.Generated.cs": '[ "1001" ] = MenuIconKeyPrefix + "1001",\n'
                                              'public static IReadOnlyList<string> ShellIconNames { get; } = new[] { "Settings", "Help" };\n',
        SH + "Framework/ShellCommandIds.cs": "".join("    public const string %s;\n" % i for i in ids),
        SH + "Localization/shell.ko.json": json.dumps(ko), SH + "Localization/shell.en.json": json.dumps(ko),
        SH + "Localization/system.ko.json": json.dumps(sysd), SH + "Localization/system.en.json": json.dumps(sysd),
        VW + "Menus/ShellMenuSections.cs": '// 섹션 채우기(모의)\nstatic class ShellMenuSections { const string List = "shell.mnuWinList"; const string More = "shell.mnuWinListMore"; }\n',
        VW + "Menus/MenuIcon.cs": "// 메뉴 아이콘 부착 속성(모의)\n",
        F_TITLE: '<UserControl><Menu AutomationProperties.AutomationId="shell.menu">\n'
                 '  <MenuItem x:Name="MoreMenu" AutomationProperties.AutomationId="shell.mnuMore" /></Menu></UserControl>\n',
    }


def _edit(rel, old, new):
    def f(files):
        assert old in files[rel], "자체 시험 변형 대상 없음: %s" % old
        files[rel] = files[rel].replace(old, new, 1)
    return f


def _put(rel, text):
    return lambda files: files.__setitem__(rel, text)


def _drop(rel):
    return lambda files: files.pop(rel)


T = NAV + "ShellMenuTable.cs"
TB_MENUS = "testbed/xkxk.frameworks.ui.testbed/TestbedMenus.cs"
TB_MENUS_SRC = ("// 시험대 메뉴 등록(모의) — 표에서 이전한 openGrid 1행\nstatic class TestbedMenus\n{\n    static void Register(IUiModuleContext context)\n    {\n        var m = context.Menus;\n"
                "        AddCommand(m, ShellMenuIds.FileOpenGrid, ShellMenuIds.File, ShellCommandIds.DemoOpen, \"Grid\", \"menu.file.openGrid\",\n"
                "            MenuIconCatalog.GridIconKey, \"shell.mnuOpenGrid\", before: ShellMenuIds.FileOpenSdiGroup, after: null, pcOnly: false);\n    }\n}\n")
TB_IDS = SH + "Framework/ShellMenuIds.cs"
TB_IDS_SRC = ('public static class ShellMenuIds\n{\n    public const string File = "xkxk.file";\n    public const string FileOpenGrid = "xkxk.file.openGrid";\n'
              '    public const string FileOpenSdiGroup = "xkxk.file.openSdiGroup";\n}\n')


def _row_out(files):  # 표에서 openGrid 행 1줄을 뺀다(이전 뒤 상태)
    t = files[NAV + "ShellMenuTable.cs"]
    files[NAV + "ShellMenuTable.cs"] = re.sub(r'[^\n]*new\("xkxk\.file\.openGrid"[^\n]*\n', "", t, count=1)
    assert files[NAV + "ShellMenuTable.cs"] != t, "자체 시험 변형 대상 없음: openGrid 행"
ST_CASES = [  # (이름, 표 형식, 변형, 기대)
    ("tree_positional_passes", "positional", None, "pass"),
    ("tree_helper_with_named_passes", "helper", None, "pass"),
    ("old_menu_axaml_left_fails", "positional", _put(VW + "Menus/FileMenu.axaml", ST_BASE[VW + "Menus/FileMenu.axaml"]), "fail"),
    ("views_axaml_fixed_mnu_id_fails", "positional", _edit(F_TITLE, "<MenuItem x:Name", '<MenuItem AutomationProperties.AutomationId="shell.mnuFile" />\n  <MenuItem x:Name'), "fail"),
    ("title_more_removed_fails", "positional", _edit(F_TITLE, "shell.mnuMore", "shell.more"), "fail"),
    ("automation_id_dropped_fails", "positional", _edit(VW + "Menus/ShellMenuSections.cs", '"shell.mnuWinListMore"', '"shell.listMore"'), "fail"),
    ("numbered_id_prefix_missing_fails", "positional", _edit(VW + "Menus/ShellMenuSections.cs", '"shell.mnuWinList"', '"shell.list"'), "fail"),
    ("automation_id_duplicate_fails", "positional", _edit(T, '"shell.mnuSysHelp"', '"shell.mnuSysSettings"'), "fail"),
    ("command_id_unknown_fails", "positional", _edit(T, '"xkxk.app.exit"', '"xkxk.app.quit"'), "fail"),
    ("mobile_command_unknown_fails", "positional", _edit(T, '"xkxk.nav.openPanel", "docs"', '"xkxk.nav.nope", "docs"'), "fail"),
    ("icon_cmdkey_undefined_fails", "positional", _edit(T, "CmdIconKeys.Exit", "CmdIconKeys.Ghost"), "fail"),
    ("icon_literal_unknown_fails", "positional", _edit(T, '"PdShellIconHelp"', '"PdShellIconGhost"'), "fail"),
    ("title_key_missing_in_en_fails", "positional", _edit(SH + "Localization/shell.en.json", '"menu.file.exit": "\\uac00", ', ""), "fail"),
    ("title_key_only_in_system_dict_passes", "helper", _edit(SH + "Localization/shell.ko.json", '{', '{"menu.unused": "x", '), "pass"),
    ("nav_text_key_differs_fails", "positional", _edit(T, 'new ShellMenuMobileRow(true, true, null, null, null,', 'new ShellMenuMobileRow(true, true, null, null, "nav.tab.settings",'), "fail"),
    ("nav_desktop_ids_differs_fails", "positional", _edit(T, 'new[] { "SysSettings" }', "new string[0]"), "fail"),
    ("nav_row_missing_fails", "positional", _edit(T, '"shell.mnuExit", null, null, null, PcOnly', '"shell.mnuExit", null, null, null, Hidden'), "fail"),
    ("nav_tab_order_differs_fails", "positional", _edit(T, '"docs", 1, "nav.tab.docs"', '"docs", 4, "nav.tab.docs"'), "fail"),
    ("catalog_own_rows_left_fails", "positional", _edit(F_NAV, "{\n    public const", '{\n            G(F, 0, "File", "menu.file"),\n    public const'), "fail"),
    ("row_value_unparsable_fails", "positional", _edit(T, '"xkxk.window.next", null, "menu.window.next"', 'Unknown.Thing, null, "menu.window.next"'), "fail"),
    ("parent_missing_fails", "positional", _edit(T, '"xkxk.system.help", "xkxk.system"', '"xkxk.system.help", "xkxk.gone"'), "fail"),
    ("table_missing_fails", "positional", _drop(T), "fail"),
    # B11-4: 데모·벤치 행이 표에서 시험대 메뉴 등록(TestbedMenus.cs)으로 이전 — 같은 AutomationId 가 거기 있으면 보존, 어디에도 없으면 실패
    ("aid_moved_to_testbed_passes", "positional", lambda f: (_row_out(f), _put(TB_MENUS, TB_MENUS_SRC)(f), _put(TB_IDS, TB_IDS_SRC)(f)), "pass"),
    ("aid_moved_out_nowhere_fails", "positional", _row_out, "fail"),
]


def _write(root, files):
    for rel, text in files.items():
        p = os.path.join(root, rel)
        os.makedirs(os.path.dirname(p), exist_ok=True)
        with open(p, "w", encoding="utf-8") as f:
            f.write(text)


def self_test():
    bad, base = 0, ST_BASE.get
    for name, style, mut, want in ST_CASES:
        files = st_files(style)
        if mut:
            mut(files)
        tmp = tempfile.mkdtemp()
        try:
            _write(tmp, files)
            got = "pass" if run(tmp, base, quiet=True) == 0 else "fail"
        finally:
            shutil.rmtree(tmp, ignore_errors=True)
        bad += got != want
        print("SELFTEST %s %s expect=%s got=%s" % ("PASS" if got == want else "FAIL", name, want, got))
    tmp = tempfile.mkdtemp()
    try:
        got = "pass" if run(tmp, git_reader(tmp, DEFAULT_BASE), quiet=True) == 0 else "fail"  # 빈 트리·기준 없음은 통과하면 안 된다
    finally:
        shutil.rmtree(tmp, ignore_errors=True)
    bad += got != "fail"
    print("SELFTEST %s empty_tree_fails expect=fail got=%s" % ("PASS" if got == "fail" else "FAIL", got))
    return 1 if bad else 0
