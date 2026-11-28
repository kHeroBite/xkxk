#!/usr/bin/env python3
# motion_nav_fold5.py 의 안전 판정(켜짐·잠금·전면 앱)·원복 기대값·DOCK ✕ 자리와 종료 코드, 그 자체 시험 — 기기 없이 돈다
import os
import re
import sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "common"))  # D-TESTDIR: test/common/_paths.py
import _paths  # noqa: E402

PKG = _paths.get("android.package")
BOX = re.compile(r'bounds="\[(\d+),(\d+)\]\[(\d+),(\d+)\]"')


def snapshot(style, favorites):
    """시험 전후에 같아야 하는 앱 상태."""
    return {"overrides": style["overrides"], "docs": sorted(d["id"] for d in style["windows"]),
            "active": style["active"], "favorites": favorites["items"]}


def expected(saved):
    """원복 기대값. 닫기 사례가 문서를 다시 열면 번호가 바뀌므로 remap(옛 id → 새 id)으로 환산한다. 표가 덜 차면 None(원복 불가)."""
    base = {k: v for k, v in saved.items() if k != "remap"}
    if "remap" not in saved:
        return base
    remap = saved["remap"]
    if set(saved["docs"]) - set(remap):
        return None
    return dict(base, docs=sorted(remap[d] for d in saved["docs"]), active=remap.get(saved["active"], saved["active"]))


def close_point(xml, title):
    """DOCK 문서 탭 ✕(PART_ClosePresenter)의 가운데 (x, y). 제목 노드 중 같은 줄·가로 범위 안에 ✕ 가 있는 맨 위 것만 탭이다. 없으면 None."""
    found = []
    nodes = [(n, tuple(map(int, m.groups()))) for n in re.findall(r"<node [^>]*>", xml) for m in [BOX.search(n)] if m]
    for head_node, head in nodes:
        if 'text="%s"' % title not in head_node and 'content-desc="%s"' % title not in head_node:
            continue
        for node, b in nodes:
            x, y = (b[0] + b[2]) // 2, (b[1] + b[3]) // 2
            if 'resource-id="PART_ClosePresenter"' in node and head[0] <= x < head[2] and head[1] <= y <= head[3]:
                found.append((head[1], head[2] - head[0], b[0], (x, y)))
    return min(found)[3] if found else None


def screen_ready(power, policy):
    """(준비됨, 사유). 판독하지 못하면 준비 안 됨으로 본다."""
    if "mWakefulness=Awake" not in power:
        return False, "화면이 켜져 있지 않다"
    shown = re.findall(r"\bshowing=(true|false)", policy)
    if not shown:
        return False, "잠금 상태를 읽지 못했다"
    return (False, "잠금 화면이다") if "true" in shown else (True, "")


def may_start(top):
    return bool(top) and (PKG + "/" in top or "launcher" in top.lower())


def exit_code(verdicts):
    if not verdicts or "ERROR" in verdicts:
        return 2
    if "FAIL" in verdicts:
        return 1
    return 0 if set(verdicts) == {"PASS"} else 4


def self_test():
    tab = '<node content-desc="%s" bounds="[%d,152][%d,206]">'
    cross = '<node resource-id="PART_ClosePresenter" bounds="[%d,163][%d,194]">'
    body = '<node text="그리드 6" bounds="[139,2066][253,2104]" />'
    dump = body + tab % ("그리드 5", 484, 702) + cross % (657, 689) + tab % ("그리드 6", 702, 920) + cross % (874, 907)
    old = {"docs": ["Form-2", "Grid-1"], "active": "Grid-1", "overrides": {}}
    on, unlocked = "mWakefulness=Awake", "KeyguardServiceDelegate\n  showing=false"
    checks = {
        "준비됨": screen_ready(on, unlocked)[0],
        "꺼짐": not screen_ready("mWakefulness=Dozing", unlocked)[0],
        "잠김": not screen_ready(on, "showing=true")[0],
        "잠금 판독 실패": not screen_ready(on, "")[0],
        "다른 이름은 안 봄": not screen_ready(on, "isShowing=false")[0],
        "홈에서 시작": may_start("topResumedActivity=com.sec.android.app.launcher/.Home"),
        "시험 앱에서 시작": may_start("topResumedActivity=%s/.MainActivity" % PKG),
        "다른 앱이면 안 함": not may_start("topResumedActivity=com.android.chrome/.Main"),
        "전면 판독 실패": not may_start(""),
        "DOCK ✕ 자리": close_point(dump, "그리드 6") == (890, 178) and close_point(dump, "그리드 5") == (673, 178),
        "✕ 없으면 안 누름": close_point(body, "그리드 6") is None,
        "다시 안 열었으면 그대로": expected(old) == old,
        "새 번호로 환산": expected(dict(old, remap={"Grid-1": "Grid-9", "Form-2": "Form-7"}))
        == dict(old, docs=["Form-7", "Grid-9"], active="Grid-9"),
        "표가 덜 차면 원복 안 함": expected(dict(old, remap={"Grid-1": "Grid-9"})) is None,
        "종료 코드": [exit_code(v) for v in (["PASS"], ["FAIL", "PASS"], ["UNDET"], [], ["ERROR"])] == [0, 1, 4, 2, 2],
    }
    for name, ok in checks.items():
        print("%s %s" % ("PASS" if ok else "FAIL", name))
    print("self-test %d/%d" % (sum(checks.values()), len(checks)))
    return 0 if all(checks.values()) else 1
