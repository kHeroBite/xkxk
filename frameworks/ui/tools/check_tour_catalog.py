#!/usr/bin/env python3
# 도움말 투어 카탈로그 정적 검사(요건 6) — tour_catalog.json 의 형식·사전 키·대상 AutomationId, 코드의 실제 키, 새 메뉴·버튼·명령의 커버리지를 빌드 전에 대조한다
"""
사용: python3 -I tools/check_tour_catalog.py [--root .] [--self-test]
① 카탈로그와 xkxk.frameworks.ui 투어 컨트롤이 쓰는 사전 키가 ko·en 사전에 모두 있다.
② 단계의 대상 AutomationId 가 axaml 에 실재한다('@' 앵커는 코드에 정의돼야 한다).
③ MainView·메뉴 InputGesture·WindowKeyGestures·EscCloseInput·TabFocusScope·MainTitleBar·SettingsInput·TourInputGate 의 실제 키가 카탈로그에 전부 있다.
  반대로 카탈로그의 키는 처리 위치가 실재한다. MainView 에 KeyBinding 을 직접 쓰지 않는다.
④ 단계 id·단축키 Keys 중복 없음, NAV 단계 1개 이상, 웹 사용 불가 표기 1개 이상.
⑤ 카탈로그 형식(JSON 문법, 필수·허용 필드, 열거값, 조건 이름, 제외 사유, texts 금지).
⑥ 메뉴·버튼·창 메뉴·패널 메뉴 항목이 카탈로그나 메뉴 카탈로그에 설명되거나 제외 목록에 사유와 함께 있다. 없어진 항목을 가리키는 표기도 실패다.
실행 시간을 마지막 줄 elapsed_ms= 로 출력한다. 종료 코드: 0=통과, 1=실패.
"""
import argparse
import os
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))  # python -I 는 스크립트 폴더를 path 에 넣지 않는다
from check_tour_catalog_rules import Source, check  # noqa: E402
from check_tour_catalog_selftest import self_test  # noqa: E402


def main():
    started = time.perf_counter()
    default_root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", default=default_root)
    parser.add_argument("--self-test", action="store_true")
    args = parser.parse_args()
    if args.self_test:
        rc = self_test(args.root)
        print("elapsed_ms=%d" % ((time.perf_counter() - started) * 1000))
        return rc
    problems, summary = check(Source(args.root))
    for problem in problems:
        print(problem)
    print("단계 %d · 단축키 %d · 기능 %d · 코드 키 %d · 사전 키 %d · 커버리지 %d/%d · 문제 %d" % (
        summary["steps"], summary["shortcuts"], summary["features"], summary["gestures"], summary["keys"],
        summary["covered"], summary["coverable"], len(problems)))
    print("결과: %s" % ("통과" if not problems else "실패"))
    print("elapsed_ms=%d" % ((time.perf_counter() - started) * 1000))
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())
