# 셸 메뉴가 모바일형 하단 탭·삼선 서랍으로 전부 도달 가능한지 정적으로 판정하는 판정기(측정·기록 없음)
"""
사용법: python3 nav_reach_judge.py --menu-dir <Views/Menus 폴더> --dump <nav.menu 진단 JSON> [--exclude 아이디 ...]

입력 ① 데스크톱 셸 메뉴 정본 = --menu-dir 안 *.axaml 의 MenuItem 중 자식 MenuItem 이 없는 잎 항목의
         AutomationProperties.AutomationId (shell.mnu* 형). 분류용 상위 머리글은 제외한다.
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
    p.add_argument("--menu-dir", required=True)
    p.add_argument("--dump", required=True)
    p.add_argument("--exclude", nargs="*", default=[])
    a = p.parse_args(argv)
    if not os.path.isdir(a.menu_dir) or not os.path.isfile(a.dump):
        print("입력 부재(통과 아님): menu-dir 또는 dump 없음")
        return 2
    try:
        exp = menu_leaf_ids(a.menu_dir)
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
