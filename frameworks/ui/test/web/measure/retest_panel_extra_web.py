#!/usr/bin/env python3
# 웹 패널 위치 메뉴 보충 측정 — 기본 웹 도구가 빼는 같은 가장자리·옮긴 뒤 동작·분할 패널·알림 탭·모서리·문서 분할·저장 복원 항목을 같은 검사 모듈로 잰다
"""
사용: python3 -B retest_panel_extra_web.py --bundle <wwwroot> --out <결과.json> --shots <폴더> [--port 5795]
종료 코드: 0=전부 통과, 1=실패 있음. 입력은 headless Chrome 안 원격 명령이다(사용자 PC 입력과 무관).
"""
import argparse
import json
import os
import sys
import time

from playwright.sync_api import sync_playwright

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(HERE, "..", "..", "desktop", "measure"))
from ui28_web import Web  # noqa: E402
import retest_panel_position_menu as core  # noqa: E402


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--bundle", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--shots", required=True)
    ap.add_argument("--port", type=int, default=5795)
    a = ap.parse_args()
    out = {}
    with sync_playwright() as p:
        w = Web(a.bundle, a.port, a.shots)
        w.open(p)
        k = core.Checks(w.res)
        time.sleep(2)
        k.c("lang.set", code="ko")
        time.sleep(1)
        core.same_edge(k)
        core.moved_behaviour(k)
        w.shot("after_moved")
        core.split_pane_to_edge(k)
        core.notify_as_tab(k)
        core.corner(k)
        core.edge_with_doc_split(k)
        w.shot("after_docsplit")
        core.saved_layout(k)
        w.shot("after_saved")
        out["console_errors"] = w.errors[:20]
        w.close()
    out["items"], out["passed"], out["total"] = k.items, sum(i["pass"] for i in k.items), len(k.items)
    os.makedirs(os.path.dirname(os.path.abspath(a.out)), exist_ok=True)
    with open(a.out, "w", encoding="utf-8") as f:
        json.dump(out, f, ensure_ascii=False, indent=1)
    print("결과 %d/%d 콘솔오류 %d" % (out["passed"], out["total"], len(out["console_errors"])))
    return 0 if out["passed"] == out["total"] else 1


if __name__ == "__main__":
    sys.exit(main())
