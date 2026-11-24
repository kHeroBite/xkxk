#!/usr/bin/env python3
# 태블릿 첫 기동 문서 배치 판정(판정 계층) — uiautomator 덤프에서 제목 탭·MDI 문서 영역이 없는지 봐서 rc 0(합격)·1(불합격)·2(입력 오류)를 돌려준다
"""
사용: python3 -B tablet_first_layout_judge.py --dump <첫 기동 덤프.xml> [--dump ...] [--style <측정 JSON>]
자체 시험(기기 불필요): python3 -B tablet_first_layout_judge.py --self-test
입력: 펼침·태블릿 기기에서 저장 레이아웃 없이(pm clear) 기동하고 docs.arrange·ui.style set 을 부르지 않은 덤프만 넣는다. 조건은 호출자가 지킨다.
기준: 덤프마다 shell.tabStrip 0개, MdiDocumentControl 0개. --style 이 있으면 effective=Tablet 도 본다.
문서 영역(DocumentDockControl)이 없는 덤프는 태블릿 셸 화면이 아니므로 입력 오류다. 숨은 컨트롤은 덤프에 노드가 없다는 전제이며 b3/sdi/fold_Debug_tabs.xml 로 확인했다.
"""
import argparse
import json
import re
import sys


def count_class(xml, cls):
    return len(re.findall(r'<node [^>]*class="%s"' % re.escape(cls), xml))


def count_id(xml, rid):
    return len(re.findall(r'<node [^>]*resource-id="%s"' % re.escape(rid), xml))


def judge_dump(xml):
    """반환 (판정 가능 여부, 값 사전). 합격은 두 값이 모두 0 일 때다."""
    if count_class(xml, "DocumentDockControl") == 0:
        return False, {}
    return True, {"tab_strip": count_id(xml, "shell.tabStrip"), "mdi_area": count_class(xml, "MdiDocumentControl")}


def find_style(data):
    """raw.style 이나 최상위 style, 또는 ui.style 응답 자체에서 형 값을 찾는다."""
    if not isinstance(data, dict):
        return None
    for holder in (data.get("raw"), data):
        if isinstance(holder, dict) and isinstance(holder.get("style"), dict):
            return holder["style"]
    return data if "effective" in data else None


def self_test():
    base = '<node class="DocumentDockControl" bounds="[0,0][1,1]"/>'
    mdi = '<node class="MdiDocumentControl" bounds="[0,0][1,1]"/>'
    strip = '<node text="" resource-id="shell.tabStrip" class="ListBox"/>'
    cases = [
        (base, True, {"tab_strip": 0, "mdi_area": 0}),
        (base + mdi + strip, True, {"tab_strip": 1, "mdi_area": 1}),
        (base + strip, True, {"tab_strip": 1, "mdi_area": 0}),
        ("<node class=\"Button\"/>", False, {}),
    ]
    ok = all(judge_dump(x) == (a, v) for x, a, v in cases)
    ok = ok and find_style({"raw": {"style": {"effective": "Tablet"}}}) == {"effective": "Tablet"}
    ok = ok and find_style({"effective": "Pc"}) == {"effective": "Pc"} and find_style({"raw": {}}) is None
    print("self-test", "PASS" if ok else "FAIL")
    return 0 if ok else 1


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--dump", action="append", default=[])
    p.add_argument("--style")
    p.add_argument("--self-test", action="store_true")
    a = p.parse_args()
    if a.self_test:
        return self_test()
    if not a.dump:
        print("입력 오류: --dump 가 없다")
        return 2
    rows, passed = [], True
    try:
        for path in a.dump:
            with open(path, encoding="utf-8", errors="ignore") as f:
                usable, values = judge_dump(f.read())
            if not usable:
                print("입력 오류: 문서 영역 노드가 없다: %s" % path)
                return 2
            good = values["tab_strip"] == 0 and values["mdi_area"] == 0
            passed = passed and good
            rows.append({"dump": path, "pass": good, **values})
        result = {"dumps": rows}
        if a.style:
            with open(a.style, encoding="utf-8") as f:
                style = find_style(json.load(f))
            if style is None:
                print("입력 오류: 형 값이 없다: %s" % a.style)
                return 2
            good = style.get("effective") == "Tablet"
            passed = passed and good
            result["style"] = {"path": a.style, "effective": style.get("effective"), "pass": good}
    except (OSError, ValueError) as e:
        print("입력 오류: %r" % (e,))
        return 2
    result["pass"] = passed
    print(json.dumps(result, ensure_ascii=False, indent=1))
    return 0 if passed else 1


if __name__ == "__main__":
    sys.exit(main())
