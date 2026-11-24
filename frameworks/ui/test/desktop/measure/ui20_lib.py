#!/usr/bin/env python3
# UI-20 판정 공용 모듈 — 원격 전송(데스크톱·Android HTTP, 웹 Playwright)과 split.dump 판정 도우미(ui20_check.py 가 쓴다)
import json
import os
import sys
import time

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "common"))  # D-TESTDIR: test/common/_paths.py
import _paths  # noqa: E402
_paths.add()
import pdctl  # noqa: E402  (같은 폴더의 원격 클라이언트)

SPLIT_OP = {"Left": "Bottom", "Bottom": "Right", "Right": "Bottom"}  # 영역 방향과 맞는 분할 방향(알림이 오른쪽일 때)
CROSS_OP = {"Left": "Right", "Bottom": "Top", "Right": "Left"}  # 가로지르는 방향(거부돼야 함)
HOME = {"Left": "LeftPane", "Bottom": "BottomPane", "Right": "NotificationPane"}
TOOL_HOME = {"Explorer": "LeftPane", "Log": "BottomPane"}  # 도구의 원래 패널
# 알림 영역(Right)도 첫 분할을 문서(Form)로 한다 — D-UI20R 로 알림 VM 을 문서 탭 영역으로 감싸 문서를 옆에 놓을 수 있다.
# 도구(로그)로 알림 옆을 분할하는 경우는 Checker.tool_split_right 에서 따로 본다.


class Undecidable(Exception):
    pass


class Remote:
    """데스크톱·Android 전송: pdctl 과 같은 HTTP 원격 제어(연결 정보 파일)."""

    def __init__(self):
        try:
            self.info = pdctl.load_info()
        except SystemExit as e:
            raise Undecidable("연결 정보 없음: %s" % e)
        self.log = []

    def send(self, body, cmd):
        try:
            return pdctl.curl(self.info, "api/cmd", body, timeout=60)
        except SystemExit:
            raise Undecidable("원격 연결 실패(%s)" % cmd)

    def call(self, cmd, **args):
        body = json.dumps({"cmd": cmd, "args": {k: str(v) for k, v in args.items()}}, ensure_ascii=False)
        for _ in range(20):
            text = self.send(body, cmd)
            try:
                resp = json.loads(text)
            except ValueError:
                raise Undecidable("응답 JSON 아님(%s): %s" % (cmd, text[:200]))
            if resp.get("status") == "NotReady":
                time.sleep(0.5)
                continue
            self.log.append({"cmd": cmd, "args": args, "ok": resp.get("ok"), "error": resp.get("error")})
            return resp
        raise Undecidable("NotReady 지속(%s)" % cmd)

    def result(self, cmd, **args):
        resp = self.call(cmd, **args)
        if not resp.get("ok"):
            raise AssertionError("%s 실패: %s" % (cmd, resp.get("error")))
        return resp.get("result")


class WebRemote(Remote):
    """웹(WASM 진단판) 전송: Playwright 헤드리스 Chromium 의 window.pdAutomation.invoke(diag.js) — 기존 retest_browser*.py 와 같은 방식."""

    ARGS = ["--use-angle=swiftshader", "--enable-unsafe-swiftshader", "--ignore-gpu-blocklist", "--enable-webgl"]
    # 웹 단일 탭 잠금 해제(retest_browser2.ALLOW_DUP_JS 와 같은 값)
    ALLOW_DUP_JS = ("(() => { const k='pd-poc-settings'; let s={}; try { s=JSON.parse(localStorage.getItem(k)||'{}')||{}; } catch (e) {} "
                    "s.AllowDuplicateInstance=true; localStorage.setItem(k, JSON.stringify(s)); })()")

    def __init__(self, pw, url, width, height, touch=False):
        self.log = []
        self.browser = pw.chromium.launch(headless=True, args=self.ARGS)
        ctx = self.browser.new_context(viewport={"width": width, "height": height}, has_touch=touch)
        ctx.add_init_script(self.ALLOW_DUP_JS)
        self.page = ctx.new_page()
        try:
            self.page.goto(url)
            self.page.wait_for_function("() => window.pdAutomation && window.pdAutomation.invoke", timeout=90000)
        except Exception as e:  # noqa: BLE001 — 서빙 안 됨·진단판 아님은 판정불가
            raise Undecidable("웹 진단판 연결 실패(%s): %s" % (url, str(e)[:200]))

    def send(self, body, cmd):
        try:
            text = self.page.evaluate("async (b) => await window.pdAutomation.invoke(b)", body)
        except Exception as e:  # noqa: BLE001
            raise Undecidable("웹 원격 호출 실패(%s): %s" % (cmd, str(e)[:200]))
        return text if isinstance(text, str) else json.dumps(text, ensure_ascii=False)

    def close(self):
        self.browser.close()


def area(dump, name):
    return dump["areas"][name]


def near(a, b, tol):
    return a is not None and b is not None and abs(a - b) <= tol


def size_of(bounds, vertical):
    if not bounds:
        return None
    return bounds.get("h" if vertical else "w") or bounds.get("height" if vertical else "width")


def structural(dump):
    """덤프 하나에 대한 불변식: 빈 ToolDock·빈 분할 패널 0, 분할 영역 비율 합 1±0.02."""
    errs = []
    if dump.get("emptyToolDocks"):
        errs.append("빈 ToolDock 잔존 %s" % dump["emptyToolDocks"])
    if dump.get("emptySplitPanels"):
        errs.append("빈 분할 패널 잔존 %s" % dump["emptySplitPanels"])
    for name, a in dump["areas"].items():
        if a["wrapped"]:
            props = [p["proportion"] for p in a["panes"]]
            if None in props or not near(sum(props), 1.0, 0.02):
                errs.append("%s 영역 비율 합 이상 %s" % (name, props))
            if a["paneCount"] < 2:
                errs.append("%s 영역이 감싸진 채 패널 %d개" % (name, a["paneCount"]))
    return errs
