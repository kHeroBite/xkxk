#!/usr/bin/env python3
# UI-20 패널 영역 분할 판정 스크립트 — 원격 명령(split.*·panel.*·layout.*)으로 탭 합류·1/2 분할·다중 분할·경계 크기 조절·빈 패널 정리·저장 복원을 재현하고 rc 로 판정한다
"""
사용:
  ui20_check.py [--out 결과.json] [--areas Left,Bottom,Right]
    연결은 pdctl.py 와 같다(사용자 데이터 폴더의 remote-control.json 또는 PD_REMOTE_INFO — Android 는 adb forward 뒤 PD_REMOTE_INFO 로 지정).
  ui20_check.py --web http://127.0.0.1:5181/ [--viewport 1280x800,805x1100:touch,390x844:touch]
    웹 진단판(diag.js window.pdAutomation)을 Playwright 헤드리스로 뷰포트마다 판정한다. 폭 600 미만은 폰 모드(rc=2)여야 통과. ':touch' 는 터치 폼팩터(form-factor=touch, D-UX1).
  ui20_check.py --judge 덤프.json
    이미 받아 둔 split.dump 결과 하나로 구조 불변식(빈 패널 0·비율 합·1/2)만 정적으로 본다.

종료 코드: 0=PASS, 1=FAIL, 2=판정불가(연결 실패·NotReady·진단 빌드 아님·폰 모드(바형)).
판정 항목:
  a 탭 합류 — 문서를 패널 탭으로 넣으면 그 패널 탭 목록에 들어간다(기본·분할 패널 모두).
  b 1/2 분할 — 패널 가장자리 놓기 1회로 영역 안 패널 2개, 두 비율 0.5±0.03, 화면 크기 비 0.5±0.1. 가로지르는 방향은 거부.
  c 다중 분할 — 분할 패널을 다시 쪼개 3개 이상, 새로 나뉜 두 패널은 서로 같은 비율.
  d 크기 조절 — 영역 안 분할선과 문서 영역 경계 분할선을 끌면(실제 분할선 코드) 비율·화면 크기가 바뀐다. 모든 분할선 canResize=true.
  e 빈 패널 정리 — 탭이 빈 분할 패널은 사라지고(빈 ToolDock 0) 남은 패널이 자리를 채운다(비율 합 1). 모두 비우면 영역 감싸기가 풀린다.
  f 저장·복원 — 개인 레이아웃 저장 뒤 분할을 바꾸고 다시 불러오면 영역별 패널 수·패널별 탭·비율(±0.02)이 돌아온다.
"""
import argparse
import json
import os
import sys
import time

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "common"))  # D-TESTDIR: test/common/_paths.py
import _paths  # noqa: E402
_paths.add()
from ui20_lib import (  # noqa: E402  (같은 폴더의 공용 모듈 — 원격 전송·판정 도우미)
    CROSS_OP, HOME, SPLIT_OP, TOOL_HOME, Remote, Undecidable, WebRemote, area, near, size_of, structural,
)
from ui20_rules import Checker  # noqa: E402  (판정 규칙 a~f — 줄 수 원칙으로 분리한 모듈)


def run_live(r, areas):
    """원격 1개(데스크톱·Android·웹 뷰포트 1개)에 대해 판정 → (rc, 보고 dict). 폰 모드·연결 실패는 Undecidable 로 올린다."""
    names = r.result("help")
    text = json.dumps(names, ensure_ascii=False)
    if "panel.list" not in text:
        raise Undecidable("진단 빌드 아님(panel.list 명령 없음)")
    if "split.dump" not in text:
        return 1, {"verdict": "FAIL", "reason": "UI-20 미구현 — split.* 명령 없음(분할 덤프 불가)"}
    checker = Checker(r, areas)
    try:
        items = checker.run()
    finally:
        checker.cleanup()
    rc = 0 if all(v["pass"] for v in items.values()) else 1
    return rc, {"items": items, "calls": len(r.log)}


def run_web(url, viewports, areas):
    """웹 뷰포트별 실행. 폭 600 미만(폰 모드 기대)은 rc=2(폰 모드)로 끝나야 통과다. 넓은 폭·태블릿 폭은 PASS 여야 한다."""
    from playwright.sync_api import sync_playwright  # 웹 전송에서만 필요

    per = {}
    with sync_playwright() as pw:
        for vp in viewports:
            size, _, kind = vp.lower().partition(":")
            w, h = (int(x) for x in size.split("x"))
            touch = kind == "touch"
            # 터치 폼팩터(태블릿·모바일 웹)는 form-factor=touch 로 강제한다 — 헤드리스는 주 포인터가 마우스라 PC 웹(데스크톱 폼팩터)으로 판정된다(D-UX1)
            vp_url = url + (("&" if "?" in url else "?") + "form-factor=touch" if touch else "")
            r = None
            try:
                r = WebRemote(pw, vp_url, w, h, touch)
                rc, part = run_live(r, areas)
            except Undecidable as e:
                rc, part = 2, {"verdict": "UNDECIDABLE", "reason": str(e)}
            except AssertionError as e:
                rc, part = 1, {"verdict": "FAIL", "reason": str(e)}
            finally:
                if r is not None:
                    r.close()
            phone = w < 600  # 폭 600 미만은 폼팩터와 무관하게 폰모드(단일 문서)여야 한다
            part.update(rc=rc, viewport=vp, expect="phone-mode rc=2" if phone else "PASS rc=0")
            part["ok"] = (rc == 2 and "폰 모드" in part.get("reason", "")) if phone else rc == 0
            per[vp] = part
    if all(p["ok"] for p in per.values()):
        return 0, per
    bad = [p for p in per.values() if not p["ok"]]
    # 어긋난 뷰포트 중 하나라도 FAIL(rc=1)이거나 폰 폭이 폰 모드가 아니면 FAIL, 나머지(연결 실패 등)는 판정불가
    return (1 if any(p["rc"] == 1 or (p["expect"].startswith("phone") and p["rc"] != 2) for p in bad) else 2), per


def main(argv):
    ap = argparse.ArgumentParser(description="UI-20 패널 영역 분할 판정")
    ap.add_argument("--out")
    ap.add_argument("--areas", default="Left,Bottom,Right")
    ap.add_argument("--judge", help="split.dump 결과 JSON 파일(정적 불변식만)")
    ap.add_argument("--web", metavar="URL", help="웹 진단판 URL(예 http://127.0.0.1:5181/) — Playwright 헤드리스로 pdAutomation 경유")
    ap.add_argument("--viewport", default="1280x800,805x1100:touch,390x844:touch",
                    help="웹 뷰포트 목록(쉼표, WxH[:touch]). 기본 = PC 웹 1280(데스크톱 폼팩터)·태블릿 웹 805(터치)·모바일 웹 390(터치, rc=2 폰모드 기대)")
    ap.add_argument("--web-query", default="", help="웹 URL 에 붙일 쿼리(예 ui-mode=tablet — 저장된 화면 모드와 무관하게 강제할 때)")
    a = ap.parse_args(argv)
    areas = [x for x in a.areas.split(",") if x]
    report = {"ui": "UI-20", "time": time.strftime("%Y-%m-%d %H:%M:%S")}
    rc = 2
    try:
        if a.judge:
            with open(a.judge, encoding="utf-8") as f:
                d = json.load(f)
            d = d.get("result", d)
            errs = structural(d)
            report.update(mode="judge", errors=errs)
            rc = 1 if errs else 0
        elif a.web:
            url = a.web + (("&" if "?" in a.web else "?") + a.web_query if a.web_query else "")
            rc, per = run_web(url, [v for v in a.viewport.split(",") if v], areas)
            report.update(mode="web", url=url, viewports=per)
        else:
            rc, part = run_live(Remote(), areas)
            report.update(mode="live", **part)
    except Undecidable as e:
        report.update(verdict="UNDECIDABLE", reason=str(e))
        rc = 2
    except AssertionError as e:
        report.update(verdict="FAIL", reason=str(e))
        rc = 1
    report.setdefault("verdict", {0: "PASS", 1: "FAIL", 2: "UNDECIDABLE"}[rc])
    report["rc"] = rc
    text = json.dumps(report, ensure_ascii=False, indent=2)
    print(text)
    if a.out:
        with open(a.out, "w", encoding="utf-8") as f:
            f.write(text)
    return rc


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
