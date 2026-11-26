# 셸 메뉴가 모바일형 하단 탭·삼선 서랍으로 전부 도달 가능한지 정적으로 판정하는 판정기(측정·기록 없음)
"""
사용법: python3 nav_reach_judge.py (--menu-table <ShellMenuTable.cs> | --menu-dir <Views/Menus 폴더>) --dump <nav.menu 진단 JSON> [--exclude 아이디 ...]

입력 ① 데스크톱 셸 메뉴 정본(둘 중 하나, 둘 다 주면 --menu-table 우선)
         --menu-table = B07 이후 정본 표 ShellMenuTable.cs(같은 폴더 ShellMenuTable*.cs 포함)의 행 중 자식 행이 없는 잎 항목의
                        AutomationId(shell.mnu* 형, 구분선 제외). 표 판독은 test/framework/judge/menu_registry_check.py 와 같다.
                        섹션이 실행 때 만드는 칸(창 배치 칸·열린 창 칸·모니터 목록)은 표에 없으므로 정본에 들어가지 않는다.
         --menu-dir   = 옛 정본(e8950ea 까지) — 폴더 안 *.axaml 의 MenuItem 중 자식 MenuItem 이 없는 잎 항목의
                        AutomationProperties.AutomationId (shell.mnu* 형). 분류용 상위 머리글은 제외한다.
                        폴더에 axaml 이 없으면 ../../xkxk.frameworks.ui.data/Navigation/ShellMenuTable.cs 가 있을 때 그 표로 대신 읽는다.
입력 ② 모바일형 탐색 덤프 = 진단 nav.menu 결과 JSON(측정 단계가 저장한 파일). 형식은 둘 다 받는다.
         {"tabs":[{id,desktopAutomationId?}], "drawer":[{id,enabled,reason,desktopAutomationId}]}
         (진단 응답이 {"data":{...}} 로 감싸져 있어도 풀어서 읽는다)
도달 정의 = 덤프 항목의 desktopAutomationId(없으면 id)가 정본 아이디와 같고, 활성이거나 비활성+사유(reason)가 있음.
rc: 0=도달률 100%, 1=누락 또는 사유 없는 비활성 있음, 2=입력 부재·형식 오류(통과 아님).
"""
import argparse
import glob
import json
import os
import sys
import xml.etree.ElementTree as ET

_FRAMEWORK_JUDGE = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "framework", "judge")


def table_leaf_ids(table):
    """ShellMenuTable 잎 항목 AutomationId 목록. 표가 없거나 읽지 못하면 ValueError."""
    if _FRAMEWORK_JUDGE not in sys.path:
        sys.path.insert(0, _FRAMEWORK_JUDGE)
    import menu_registry_check as mrc  # 표 판독 규칙을 한 곳(메뉴 레지스트리 판정기)에 둔다
    errors = []
    rows = mrc.parse_table_files(sorted(glob.glob(os.path.join(os.path.dirname(os.path.abspath(table)), "ShellMenuTable*.cs"))), errors)
    if errors:
        raise ValueError("표 판독 실패: %s" % errors[:2])
    return mrc.leaf_aids(rows)


def sibling_table(menu_dir):
    p = os.path.normpath(os.path.join(menu_dir, "..", "..", "xkxk.frameworks.ui.data", "Navigation", "ShellMenuTable.cs"))
    return p if os.path.isfile(p) else None


def _local(tag):
    return tag.rsplit("}", 1)[-1]


def _aid(el):
    for k, v in el.attrib.items():
        if k.rsplit("}", 1)[-1].endswith("AutomationProperties.AutomationId") or k.endswith("AutomationId"):
            return v
    return None


def menu_leaf_ids(menu_dir):
    """MenuItem 중 자식 MenuItem 이 없는 항목의 AutomationId 목록. 읽을 파일이 없으면 None."""
    files = sorted(glob.glob(os.path.join(menu_dir, "*.axaml")))
    if not files:
        return None
    ids = []
    for f in files:
        root = ET.parse(f).getroot()
        for el in root.iter():
            if _local(el.tag) != "MenuItem":
                continue
            if any(_local(c.tag) == "MenuItem" for c in el.iter() if c is not el):
                continue
            a = _aid(el)
            if a and a.startswith("shell.mnu"):
                ids.append(a)
    return sorted(set(ids))


def load_dump(path):
    with open(path, encoding="utf-8-sig") as fh:
        d = json.load(fh)
    if isinstance(d, dict) and isinstance(d.get("data"), dict):
        d = d["data"]
    if not isinstance(d, dict) or not isinstance(d.get("drawer"), list):
        raise ValueError("drawer 배열이 없는 덤프")
    return d


def judge(expected, dump):
    """반환: (도달 수, 누락 목록, 사유 없는 비활성 목록)"""
    entries = list(dump.get("drawer", [])) + list(dump.get("tabs", []))
    by = {}
    for e in entries:
        if not isinstance(e, dict):
            continue
        keys = list(e.get("desktopIds") or [])  # NavMenuCatalog.ToJson 형식(항목 하나가 여러 데스크톱 아이디를 덮음)
        keys.append(e.get("desktopAutomationId") or e.get("id"))
        for key in keys:
            if key:
                by.setdefault(key, []).append(e)
    missing, noreason, ok = [], [], 0
    for i in expected:
        es = by.get(i)
        if not es:
            missing.append(i)
            continue
        if any(e.get("enabled", True) for e in es) or any(str(e.get("reason") or "").strip() for e in es):
            ok += 1
        else:
            noreason.append(i)
    return ok, missing, noreason


def main(argv=None):
    p = argparse.ArgumentParser(description="모바일형 메뉴 도달률 판정기")
    p.add_argument("--menu-table")
    p.add_argument("--menu-dir")
    p.add_argument("--dump", required=True)
    p.add_argument("--exclude", nargs="*", default=[])
    a = p.parse_args(argv)
    if not (a.menu_table or a.menu_dir):
        print("입력 부재(통과 아님): --menu-table 또는 --menu-dir 필요")
        return 2
    table = a.menu_table
    if not table and os.path.isdir(a.menu_dir) and not glob.glob(os.path.join(a.menu_dir, "*.axaml")):
        table = sibling_table(a.menu_dir)
        if table:
            print("정본: %s 에 axaml 이 없어 표 %s 로 읽음" % (a.menu_dir, table))
    if (table and not os.path.isfile(table)) or (not table and not os.path.isdir(a.menu_dir)) or not os.path.isfile(a.dump):
        print("입력 부재(통과 아님): menu-table·menu-dir 또는 dump 없음")
        return 2
    try:
        exp = table_leaf_ids(table) if table else menu_leaf_ids(a.menu_dir)
        dump = load_dump(a.dump)
    except (ET.ParseError, ValueError, OSError) as ex:
        print("입력 형식 오류(통과 아님): %s" % ex)
        return 2
    if not exp:
        print("입력 부재(통과 아님): 정본 메뉴 항목 0건")
        return 2
    exp = [i for i in exp if i not in set(a.exclude)]
    ok, missing, noreason = judge(exp, dump)
    total = len(exp)
    print("도달 %d/%d (%.1f%%)" % (ok, total, 100.0 * ok / total if total else 0.0))
    for i in missing:
        print("누락: %s" % i)
    for i in noreason:
        print("사유 없는 비활성: %s" % i)
    return 0 if not missing and not noreason else 1


if __name__ == "__main__":
    sys.exit(main())
