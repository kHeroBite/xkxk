# 브라우저(WASM) 판을 Playwright headless 로 열어 window.pdAutomation.invoke 로 retest_remote 와 같은 시나리오를 돌리고 스크린샷·JSON 을 남기는 웹 축 시험 스크립트
import json
import os
import sys
import time

from playwright.sync_api import sync_playwright

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "common"))  # D-TESTDIR: test/common/_paths.py
import _paths  # noqa: E402
_paths.add()
HERE = _paths.TOOLS  # 옛 위치(Pd 저장소 poc/ui/tools, 이전 전) — OUT 기본값(../logs/otest_w2) 기준 그대로
import retest_remote as rr  # noqa: E402

URL = os.environ.get("PD_URL", "http://127.0.0.1:5181/")
OUT = os.environ.get("PD_OUT", os.path.join(HERE, "..", "logs", "otest_w2"))
os.makedirs(OUT, exist_ok=True)


class WebRemote(rr.Remote):
    def __init__(self, page):
        self.page = page
        self.calls = []
        self.checks = []

    def call(self, cmd, _timeout=60, **args):
        body = json.dumps({"cmd": cmd, "args": {k: v for k, v in args.items() if v is not None}}, ensure_ascii=False)
        t0 = time.time()
        text = self.page.evaluate("b => window.pdAutomation.invoke(b)", body)
        try:
            resp = json.loads(text)
        except Exception:
            resp = {"ok": False, "error": "JSON 아님: " + str(text)[:200]}
        self.calls.append({"cmd": cmd, "args": args, "ok": bool(resp.get("ok")), "ms": resp.get("ms"),
                           "elapsed_ms": resp.get("elapsed_ms"), "ui_frame_ms": resp.get("ui_frame_ms"),
                           "error": resp.get("error"), "wall_ms": round((time.time() - t0) * 1000, 1)})
        if not resp.get("ok"):
            print("  ✗ %s %s → %s" % (cmd, args, (resp.get("error") or "")[:160]))
        return resp.get("result") if resp.get("ok") else None


def main():
    only = set(sys.argv[1:])
    with sync_playwright() as p:
        b = p.chromium.launch(headless=True, args=["--use-angle=swiftshader", "--enable-unsafe-swiftshader", "--ignore-gpu-blocklist", "--enable-webgl"])
        page = b.new_page(viewport={"width": 1280, "height": 800})
        errs = []
        page.on("pageerror", lambda e: errs.append(str(e)))
        t0 = time.time()
        page.goto(URL)
        page.wait_for_function("() => window.pdAutomation && window.pdAutomation.invoke", timeout=60000)
        boot_ms = round((time.time() - t0) * 1000)
        st = page.evaluate("() => ({rt: window.__pdPoc && window.__pdPoc.runtimeReadyMs, ms: window.__pdPoc && window.__pdPoc.mainStartedMs, err: window.__pdPoc && window.__pdPoc.error})")
        print("부팅", boot_ms, "ms", st)
        r = WebRemote(page)
        status = None
        for _ in range(60):
            status = r.call("status")
            if status is not None:
                break
            time.sleep(1)
        shell_ms = round((time.time() - t0) * 1000)
        print("셸 준비까지", shell_ms, "ms", "status", status)
        if status is None:
            return 2
        page.screenshot(path=os.path.join(OUT, "00_boot.png"))
        r.call("perf.reset")
        r.call("docs.close", all=True)
        steps = [("themes", lambda: rr.step_themes(r)), ("fonts", lambda: rr.step_fonts(r)),
                 ("chrome", lambda: rr.step_chrome(r)), ("windows", lambda: rr.step_windows(r, 20))]
        for name, run in steps:
            if only and name not in only:
                continue
            try:
                run()
            except Exception as ex:
                r.check("단계 %s 예외" % name, False, repr(ex))
            page.screenshot(path=os.path.join(OUT, "step_%s.png" % name))
        ops = rr.ops_table(r)
        res = {"url": URL, "boot_ms": boot_ms, "shell_ready_ms": shell_ms, "pdPoc": st, "status": status, "pageerrors": errs,
               "calls": r.calls, "checks": r.checks, "operations": ops}
        with open(os.path.join(OUT, "retest_web.json"), "w", encoding="utf-8") as f:
            json.dump(res, f, ensure_ascii=False, indent=2)
        print("checks", len(r.checks), "failed", [c["name"] for c in r.checks if not c["pass"]],
              "failed_calls", len([c for c in r.calls if not c["ok"]]), "pageerrors", errs[:3])
        b.close()
    return 0


if __name__ == "__main__":
    sys.exit(main())
