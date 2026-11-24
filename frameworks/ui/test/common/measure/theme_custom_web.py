# 테마 요소별 사용자 지정(REQ-101) 웹 시험 실행기 — headless Chromium(ui28_web.Web)에 theme_custom_cases 케이스를 연결해 결과 JSON 을 남긴다
"""
사용:
  python3 -B theme_custom_web.py --bundle <웹 번들 폴더> --port <서빙 포트> --out <결과.json> [--shots <스크린샷 폴더>] [--import-arg json]
안전: 포트는 인자로만 받는다(기본값 없음). 사용자 Pd 앱·포트 5180·5220 을 쓰지 않는다. 격리 번들만 연다.
웹은 파일 경로가 없으므로 export_import 는 inline_json 모드(경로 없는 theme.export → JSON 문자열, theme.import 는 JSON 문자열 인자)로 돈다.
rc: 0=전 케이스 PASS, 1=FAIL/BLOCKED/ERROR 있음. 결과 형식은 theme_custom_check.py 와 같다(fails=정수, fail_cases=목록).
"""
import argparse
import json
import os
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
UI = os.path.abspath(os.path.join(HERE, "..", "..", ".."))  # 영역 루트
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(UI, "test", "web", "measure"))
import theme_custom_cases as C  # noqa: E402
import theme_custom_check as T  # noqa: E402


class Backend:
    """케이스가 기대하는 backend 형식(b.call, restart)을 ui28_web.Web 위에 얹는다. 재시작 = 페이지 다시 읽기."""

    def __init__(self, w):
        self.w = w

    @property
    def b(self):
        return self

    def call(self, cmd, **k):
        k.pop("_timeout", None)
        return self.w.res(cmd, **k)

    def restart(self):
        self.w.page.reload(wait_until="load", timeout=90000)
        self.w.page.wait_for_function("() => window.pdAutomation && window.pdAutomation.invoke", timeout=120000)
        for _ in range(60):
            if self.w.call("status").get("ok"):
                break
            time.sleep(1)
        time.sleep(2)
        self.w.call("docs.open", kind="grid")
        time.sleep(2)


def open_without_fresh(w, p):
    """ui28_web.Web.open 과 같되 URL 에 ?fresh 를 붙이지 않는다."""
    # ?fresh 는 설정 저장소를 메모리로 돌려(App.FreshStart) 다시 읽기 때 저장값이 사라지므로 restart_persist 를 잴 수 없다 — 격리는 새 Chromium 컨텍스트(빈 localStorage)가 맡는다.
    from ui28_web import ARGS
    w.browser = p.chromium.launch(headless=True, args=ARGS)
    w.page = w.browser.new_page(viewport={"width": w.viewport[0], "height": w.viewport[1]})
    w.page.on("console", lambda m: w.errors.append(m.text[:300]) if m.type == "error" else None)
    w.page.on("pageerror", lambda e: w.errors.append("pageerror: " + str(e)[:300]))
    w.page.goto("http://127.0.0.1:%d/" % w.port, wait_until="load", timeout=90000)
    w.page.wait_for_function("() => window.pdAutomation && window.pdAutomation.invoke", timeout=120000)
    for _ in range(60):
        if w.call("status").get("ok"):
            break
        time.sleep(1)


def main(argv=None):
    ap = argparse.ArgumentParser()
    ap.add_argument("--bundle", required=True)
    ap.add_argument("--port", type=int, required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--shots", default=None)
    ap.add_argument("--import-arg", default="path", help="theme.import 가 JSON 문자열을 받는 인자 이름(값이 { 또는 [ 로 시작하면 문자열로 처리)")
    ap.add_argument("--builtin-colors")
    ap.add_argument("--src-root", default=os.path.join(UI, "src"))
    a = ap.parse_args(argv)
    from ui28_web import Web
    from playwright.sync_api import sync_playwright
    with sync_playwright() as p:
        w = Web(a.bundle, a.port, a.shots)
        open_without_fresh(w, p)
        be = Backend(w)
        w.call("docs.open", kind="grid")
        time.sleep(2)
        ns = argparse.Namespace(builtin_colors=a.builtin_colors, exchange_dir=None, legacy_fixture=None,
                                legacy_baseline=None, settings_file=None)
        ctx = T.build_ctx(ns, lambda: be)
        ctx.update(inline_json=True, import_arg=a.import_arg)
        cases, fails = T.run_cases(be, list(C.COMMON), ctx)
        errs = list(w.errors)
        w.close()
    out = {"verdict": "PASS" if not fails else "FAIL", "fails": len(fails), "fail_cases": fails, "target": "web",
           "src_hash": T.src_hash(a.src_root), "evidence": a.out + ".log", "cases": cases, "console_errors": errs[:20]}
    with open(a.out, "w", encoding="utf-8") as f:
        json.dump(out, f, ensure_ascii=False, indent=1)
    print("verdict", out["verdict"], fails)
    return 0 if not fails else 1


if __name__ == "__main__":
    sys.exit(main())
