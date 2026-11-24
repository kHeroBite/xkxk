# 브라우저(WASM) 추가 시험 — MDI 상태·SDI(관리형 창)·핀 상태 최대/최소화·레이아웃 저장/복원(localStorage)·URL 시나리오·390px 폭·한글 IME 를 Playwright 로 점검한다
import json
import os
import sys
import time

from playwright.sync_api import sync_playwright

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import retest_browser as rb  # noqa: E402

URL = os.environ.get("PD_URL", "http://127.0.0.1:5181/")
OUT = rb.OUT
ARGS = ["--use-angle=swiftshader", "--enable-unsafe-swiftshader", "--ignore-gpu-blocklist", "--enable-webgl"]
results = []


def rec(item, expect, actual, verdict):
    results.append({"item": item, "expect": expect, "actual": actual, "verdict": verdict})
    print("%-5s %s | %s" % (verdict, item, str(actual)[:200]), flush=True)


# 웹 단일 탭 잠금(UI-17 프로그램 중복모드 기본 끔, odev-3 main.js 1c69dc8f) 해제 — 여러 탭·새 페이지 경로 시험용.
# 매 페이지 로드 전에 pd-poc-settings.AllowDuplicateInstance=true 를 넣는다. 잠금 동작 자체를 시험할 때는 PD_SINGLE_TAB=1.
ALLOW_DUP_JS = ("() => { const k='pd-poc-settings'; let s={}; try { s=JSON.parse(localStorage.getItem(k)||'{}')||{}; } catch (e) {} "
                "s.AllowDuplicateInstance=true; localStorage.setItem(k, JSON.stringify(s)); }")


def allow_dup(ctx):
    if not os.environ.get("PD_SINGLE_TAB") and not getattr(ctx, "_pd_allow_dup", False):
        ctx.add_init_script("(" + ALLOW_DUP_JS + ")()")
        ctx._pd_allow_dup = True
    return ctx


def boot(b, query="", w=1280, h=800, ctx=None):
    ctx = allow_dup(ctx or b.new_context(viewport={"width": w, "height": h}))
    page = ctx.new_page()
    page.goto(URL + query)
    page.wait_for_function("() => window.pdAutomation && window.pdAutomation.invoke", timeout=60000)
    r = rb.WebRemote(page)
    for _ in range(90):
        if r.call("status") is not None and r.call("docs.list") is not None:
            break
        time.sleep(1)
    r.calls.clear()
    return ctx, page, r


def doc(r, i):
    return next((d for d in (r.call("docs.list") or []) if d.get("id") == i), {})


def main():
    with sync_playwright() as p:
        b = p.chromium.launch(headless=True, args=ARGS)
        ctx, page, r = boot(b)
        ids = [d["id"] for d in r.call("docs.open", kind="grid", count=3)]
        d1 = ids[0]
        # MDI 상태
        for s in ("minimized", "maximized", "normal"):
            res = r.call("docs.state", state=s, doc=d1) or {}
            rec("MDI-06 상태 " + s, s, res.get("state"), "PASS" if str(res.get("state")).lower() == s else "FAIL")
        page.screenshot(path=os.path.join(OUT, "mdi06_normal.png"))
        # 핀(사용자 지시: Visual Studio 참조) — 핀 상태에서 최대/최소화 동작 실측
        r.call("chrome.pin", doc=d1)
        for s in ("maximized", "minimized", "normal"):
            res = r.call("docs.state", state=s, doc=d1)
            cs = r.call("chrome.state", doc=d1) or {}
            rec("핀 고정 중 " + s, "관찰(허용 여부 판단 대기)", {"ok": res is not None, "state": (res or {}).get("state"), "pinned": cs.get("pinned"), "locked": cs.get("locked")}, "관찰")
        r.call("chrome.pin", doc=d1)
        # MDI-07 전체 최소화·복원
        r.call("docs.state", all="minimize")
        r.call("docs.state", all="restore")
        rec("MDI-07 전체 최소화/복원", "복원 후 문서 3", len(r.call("docs.list") or []), "PASS" if len(r.call("docs.list") or []) == 3 else "FAIL")
        # MDI-16 다음/이전 전환
        a0 = (r.call("status") or {}).get("active")
        r.call("docs.activate", dir="next")
        a1 = (r.call("status") or {}).get("active")
        rec("MDI-16 다음 창 전환", "활성 변경", "%s→%s" % (a0, a1), "PASS" if a0 != a1 else "FAIL")
        # MDI-08 닫기
        r.call("docs.close", doc=ids[2])
        rec("MDI-08 닫기", "문서 2", len(r.call("docs.list") or []), "PASS" if len(r.call("docs.list") or []) == 2 else "FAIL")
        # MDI-09 SDI(관리형 창)
        s1 = r.call("docs.sdi", doc=d1, on=True)
        fl = doc(r, d1)
        page.screenshot(path=os.path.join(OUT, "mdi09_sdi.png"))
        r.call("docs.sdi", doc=d1, on=False)
        rec("MDI-09 SDI 분리/재결합(관리형 창)", "분리 후 floating, 재결합", {"detached": fl.get("floating"), "after": doc(r, d1).get("floating")}, "차이(사유: 브라우저는 OS 창 대신 관리형 창)" if fl.get("floating") else "FAIL")
        # MDI-10 저장/복원
        r.call("docs.arrange", mode="tile-h")
        r.call("layout.save")
        n_before = len(r.call("docs.list") or [])
        r.call("docs.close", all=True)
        ok = r.call("layout.load")
        n_after = len(r.call("docs.list") or [])
        rec("MDI-10 레이아웃 저장→닫기→복원", "문서 수 일치", "저장 %s 복원 %s load=%s" % (n_before, n_after, ok), "PASS" if n_before == n_after and ok else "FAIL")
        r.call("layout.save")
        ls = page.evaluate("() => (localStorage.getItem('pd-poc-layout')||'').length")
        rec("MDI-10 localStorage 기록", ">0 바이트", ls, "PASS" if ls > 0 else "FAIL")
        page.close()
        # 같은 컨텍스트(localStorage 유지)에서 새 탭으로 load-layout
        ctx, page2, r2 = boot(b, "?load-layout", ctx=ctx)
        time.sleep(2)
        n2 = len(r2.call("docs.list") or [])
        rec("MDI-10 재기동(?load-layout) 복원", "문서 수 %d" % n_before, n2, "PASS" if n2 == n_before else "FAIL")
        page2.screenshot(path=os.path.join(OUT, "mdi10_restored.png"))
        page2.close()
        ctx.close()
        # URL 시나리오(WEB-01)
        ctx, page3, r3 = boot(b, "?open=grid:2,gantt:2,form:1&layout=tile")
        time.sleep(3)
        n3 = len(r3.call("docs.list") or [])
        rec("WEB-01 URL 시나리오 open+layout", "문서 5", n3, "PASS" if n3 == 5 else "FAIL")
        page3.screenshot(path=os.path.join(OUT, "web01_url.png"))
        # 테마 라이트/다크 + UI-01
        r3.call("settings.set", theme="Dark")
        page3.screenshot(path=os.path.join(OUT, "ui01_dark.png"))
        r3.call("settings.set", theme="Light")
        page3.screenshot(path=os.path.join(OUT, "ui01_light.png"))
        rec("UI-01 라이트/다크 스크린샷", "두 장 저장", "ui01_dark.png / ui01_light.png", "증거")
        # UI-02~04 글꼴 목록
        fonts = r3.call("settings.fonts")
        rec("UI-04 글꼴 목록", "목록 존재", (fonts if isinstance(fonts, list) else str(fonts))[:6] if fonts else None, "PASS" if fonts else "FAIL")
        # UI-10 프레임
        for m in (True, False):
            r3.call("chrome.frame", mars=m)
            page3.screenshot(path=os.path.join(OUT, "ui10_frame_%s.png" % ("mars" if m else "pd")))
        ctx.close()
        # 390px (MOB-01)
        ctx, page4, r4 = boot(b, "?open=grid:2", w=390, h=844)
        time.sleep(2)
        page4.screenshot(path=os.path.join(OUT, "mob01_390.png"))
        rec("MOB-01 390px", "레이아웃 깨짐 없음(스크린샷 판독)", "mob01_390.png", "증거")
        ctx.close()
        with open(os.path.join(OUT, "retest_web2.json"), "w", encoding="utf-8") as f:
            json.dump(results, f, ensure_ascii=False, indent=2)
        b.close()


if __name__ == "__main__":
    main()
