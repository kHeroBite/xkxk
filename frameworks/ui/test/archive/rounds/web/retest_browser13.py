# 웹 단일 탭 잠금(UI-17 프로그램 중복모드 기본 끔)과 GPU 모드(?gpu=off) 기능 시험 — 두 번째 탭 '다른 탭에서 사용 중' 화면·[여기서 사용] 인계·첫 탭 저장 후 정지, perf render_kind·gpu_mode
# 사용: PD_URL=http://127.0.0.1:5189/ python3 tools/retest_browser13.py   (헤드리스 전용. 잠금 시험은 AllowDuplicateInstance 를 넣지 않는다)
import json
import os
import sys
import time

from playwright.sync_api import sync_playwright

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import retest_browser as rb  # noqa: E402
import retest_browser2 as t  # noqa: E402

OUT = t.OUT
res = []
KEY = "otestw2-lock"


def rec(item, expect, actual, verdict):
    res.append({"item": item, "expect": expect, "actual": actual, "verdict": verdict})
    print("%-6s %s | %s" % (verdict, item, str(actual)[:600]), flush=True)


def ready(page, timeout=60000):
    page.wait_for_function("() => window.pdAutomation && window.pdAutomation.invoke", timeout=timeout)
    r = rb.WebRemote(page)
    for _ in range(90):
        if r.call("status") is not None and r.call("docs.list") is not None:
            break
        time.sleep(1)
    time.sleep(1.0)
    return r


def overlay(page):
    return page.evaluate("""() => { const b = document.getElementById('pd-instance');
        return b ? {shown: true, text: b.innerText, button: !!document.getElementById('pd-instance-use-here'),
                    buttonText: (document.getElementById('pd-instance-use-here')||{}).textContent || null} : {shown: false}; }""")


def part_lock(b):
    ctx = b.new_context(viewport={"width": 1280, "height": 800})  # allow_dup 를 걸지 않는다(잠금 동작 시험)
    p1 = ctx.new_page()
    p1.goto(t.URL + "?layout-key=" + KEY)
    r1 = ready(p1)
    env = p1.evaluate("() => ({locks: !!(navigator.locks), secure: window.isSecureContext, settings: localStorage.getItem('pd-poc-settings')})")
    rec("잠금 사전: 첫 탭 기동·Web Locks 사용 가능", "첫 탭 자동화 준비, navigator.locks 있음(보안 컨텍스트), AllowDuplicateInstance 미설정", env,
        "PASS" if env.get("locks") and "AllowDuplicateInstance\":true" not in (env.get("settings") or "") else "판정불가(잠금 API 없음 또는 해제 설정 있음)")
    r1.call("docs.close", all=True)
    marker = "LOCK-%d" % int(time.time())
    r1.call("docs.open", kind="form", title=marker, allowDuplicate=True)  # 수동 저장 없이 열어 둔다 → 인계 때 저장되는지 본다
    time.sleep(1.5)
    p2 = ctx.new_page()
    p2.goto(t.URL + "?layout-key=" + KEY)
    try:
        p2.wait_for_selector("#pd-instance", timeout=30000)
    except Exception:
        pass
    time.sleep(1.0)
    o2 = overlay(p2)
    auto2 = p2.evaluate("() => !!(window.pdAutomation && window.pdAutomation.invoke)")
    p2.screenshot(path=os.path.join(OUT, "lock_tab2_busy.png"))
    rec("③-1 두 번째 탭: '다른 탭에서 사용 중' 화면(#pd-instance)과 [여기서 사용] 버튼, 앱 미기동", "#pd-instance 표시·버튼 있음·pdAutomation 없음",
        {"overlay": o2, "tab2_automation": auto2}, "PASS" if o2.get("shown") and o2.get("button") and not auto2 else "FAIL")
    o1_before = overlay(p1)
    p2.click("#pd-instance-use-here")
    try:
        r2 = ready(p2, timeout=90000)
        started = True
    except Exception as e:
        r2, started = None, False
        print("tab2 start failed", e, flush=True)
    try:
        p1.wait_for_selector("#pd-instance", timeout=15000)
    except Exception:
        pass
    time.sleep(1.0)
    o1 = overlay(p1)
    o2b = overlay(p2)
    p1.screenshot(path=os.path.join(OUT, "lock_tab1_stopped.png"))
    p2.screenshot(path=os.path.join(OUT, "lock_tab2_inuse.png"))
    docs2 = [(d.get("title"), d.get("kind")) for d in ((r2.call("docs.list") if r2 else None) or [])]
    raw = p2.evaluate("k => localStorage.getItem(k)", KEY) or ""
    rec("③-2 [여기서 사용]: 두 번째 탭 기동, 첫 탭은 정지 화면(버튼 없음)", "탭2 자동화 준비·#pd-instance 사라짐, 탭1 #pd-instance(정지 문구, 버튼 없음)",
        {"tab2_started": started, "tab2_overlay_after": o2b, "tab1_overlay_before": o1_before, "tab1_overlay_after": o1},
        "PASS" if started and not o2b.get("shown") and o1.get("shown") and not o1.get("button") else "FAIL")
    rec("③-3 첫 탭이 정지 전에 레이아웃을 저장 → 두 번째 탭이 그 문서로 복원", "수동 저장 없이 연 문서(%s)가 저장 슬롯과 탭2 docs.list 에 있음" % marker,
        {"marker": marker, "in_storage": marker in raw, "tab2_docs": docs2}, "PASS" if marker in raw and any(x[0] == marker for x in docs2) else "FAIL")
    ctx.close()


def perf_render(b, query):
    ctx = t.allow_dup(b.new_context(viewport={"width": 1280, "height": 800}))
    page = ctx.new_page()
    con = []  # odev-3 요청: 콘솔 [render] 줄(첫 렌더 뒤 AutomationBridge.RunAsync 에서 1회) — WebGL 초기화가 늦을 수 있어 최대 10초 기다린다
    page.on("console", lambda m: con.append((round(time.time() - t_go, 2), m.text)) if ("[render]" in m.text or "[host]" in m.text) else None)
    t_go = time.time()
    page.goto(t.URL + query)
    r = ready(page)
    for _ in range(20):
        if any("[render]" in x[1] for x in con):
            break
        time.sleep(0.5)
    pf = r.call("perf.get") or {}
    flat = json.dumps(pf, ensure_ascii=False)

    def find(key):
        stack = [pf]
        while stack:
            x = stack.pop()
            if isinstance(x, dict):
                if key in x and not isinstance(x[key], (dict, list)):
                    return x[key]
                stack.extend(x.values())
            elif isinstance(x, list):
                stack.extend(x)
        return None
    out = {"query": query, "render_kind": find("render_kind"), "gpu_mode": find("gpu_mode"), "render_graphics": find("render_graphics"),
           "webgl": page.evaluate("() => { const c = document.createElement('canvas'); const g = c.getContext('webgl2') || c.getContext('webgl'); "
                                  "if (!g) return null; const d = g.getExtension('WEBGL_debug_renderer_info'); return d ? g.getParameter(d.UNMASKED_RENDERER_WEBGL) : 'webgl(정보 없음)'; }"),
           "perf_has_keys": [k for k in ("render_kind", "gpu_mode") if k in flat],
           "console": con, "renderLineSeen": any("[render]" in x[1] for x in con), "waitedSec": round(time.time() - t_go, 1),
           # 실제 렌더링 경로: 앱 canvas(avalonia-canvas)가 쥔 컨텍스트 종류 — 이미 만든 종류만 non-null(webgl2→webgl→2d 순으로 확인)
           "canvas": page.evaluate("""() => [...document.querySelectorAll('canvas.avalonia-canvas')].map(c => {
               let kind = null; for (const k of ['webgl2', 'webgl', '2d']) { try { if (c.getContext(k)) { kind = k; break; } } catch (e) {} }
               return kind; })""")}
    page.screenshot(path=os.path.join(OUT, "gpu_%s.png" % (query.replace("?", "").replace("=", "-") or "default")))
    ctx.close()
    return out


def part_gpu(b):
    off = perf_render(b, "?gpu=off&layout-key=otestw2-gpu")
    dflt = perf_render(b, "?layout-key=otestw2-gpu")
    rec("④-1 ?gpu=off: render_kind=software, gpu_mode=Off, 앱 canvas 가 2d", "render_kind=software, gpu_mode 가 Off 로 시작, avalonia-canvas 컨텍스트=2d", off,
        "PASS" if off.get("render_kind") == "software" and str(off.get("gpu_mode") or "").startswith("Off") and off.get("canvas") == ["2d"] else "FAIL")
    rec("④-2a 기본 기동: 실제 렌더링이 WebGL(기능)", "avalonia-canvas 컨텍스트=webgl2 또는 webgl, gpu_mode=Auto(헤드리스 Chromium 은 swiftshader WebGL)", dflt,
        "PASS" if dflt.get("canvas") and dflt["canvas"][0] in ("webgl2", "webgl") and str(dflt.get("gpu_mode") or "").startswith("Auto") else "FAIL")
    rec("④-2b 기본 기동: perf render_kind=gpu-webgl(보고값)", "render_kind=gpu-webgl — 실제 canvas 가 WebGL 이면 보고값도 일치해야 함", {"render_kind": dflt.get("render_kind"), "render_graphics": dflt.get("render_graphics"), "canvas": dflt.get("canvas")},
        "PASS" if dflt.get("render_kind") == "gpu-webgl" else "FAIL(보고값 불일치 — 실제 WebGL 인데 software 로 보고)" if dflt.get("canvas") and dflt["canvas"][0].startswith("webgl") else "FAIL")


def main():
    with sync_playwright() as p:
        b = p.chromium.launch(headless=True, args=t.ARGS)
        part_lock(b)
        part_gpu(b)
        with open(os.path.join(OUT, "retest_web13.json"), "w", encoding="utf-8") as f:
            json.dump(res, f, ensure_ascii=False, indent=2)
        b.close()


if __name__ == "__main__":
    main()
