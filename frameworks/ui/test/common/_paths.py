# 시험 도구 import 경로 도우미 — test/ 의 계층 폴더(measure·judge·record)와 tools/ 를 sys.path 에 넣는다(D-TESTDIR, 2026-10-31)
"""
옮긴 재사용 도구는 맨 위에서 다음 세 줄로 이 모듈을 부른다(스크립트 위치: test/<축>/<계층>/).
    sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "common"))
    import _paths
    _paths.add()
archive/ 회차본은 내용을 바꾸지 않으므로 이 모듈을 부르지 않는다. 실행법은 test/README.md 를 본다.
"""
import os
import sys

TEST = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
UI = os.path.dirname(TEST)
TOOLS = os.path.join(UI, "tools")  # 공용 4개(run_desktop_bg.sh·pdctl.py·serve_browser.sh·slot_handoff.sh)와 static 검사기
DIRS = [
    os.path.join(TEST, "common", "measure"),
    os.path.join(TEST, "desktop", "measure"),
    os.path.join(TEST, "desktop", "judge"),
    os.path.join(TEST, "web", "measure"),
    os.path.join(TEST, "mobile", "measure"),
    os.path.join(TEST, "perf", "measure"),
    os.path.join(TEST, "perf", "record"),
    TOOLS,
]


if TOOLS not in sys.path:  # host_identity(정체성 값 단일 출처)를 읽어 재노출한다 — 값은 tools/host_identity.json
    sys.path.insert(1, TOOLS)
from host_identity import *  # noqa: E402,F401,F403  load·get·path·bin_dir·apk_path·data_dir_under


def add():
    """DIRS 를 sys.path[1:] 앞쪽에 넣는다. sys.path[0](실행한 스크립트의 폴더)은 그대로 첫 자리에 둔다."""
    for d in reversed(DIRS):
        if d not in sys.path:
            sys.path.insert(1, d)
