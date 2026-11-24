# 웹 유휴 rAF 비교(Q9 ⑤, Avalonia.Browser 유휴 rAF 패치 전후) — rAF 래핑으로 호출 수·호출 위치(PD 코드/Avalonia.Browser/기타)를 세고 CDP Performance.getMetrics 유휴 증가량·첫 페인트를 기록
# 사용: python3 tools/retest_browser16.py <라벨=URL> [<라벨=URL> ...] [--runs 3]   예: i1113p=http://127.0.0.1:5196/ p1121p=http://127.0.0.1:5197/
"""
방법(odev-3 web_idle_raf_patch_step2.md ③): 부팅 8초 뒤부터 6초 유휴. requestAnimationFrame 를 페이지 스크립트보다 먼저 래핑해
  호출마다 스택에서 래퍼 다음 프레임(호출 위치)을 뽑아 집계한다. 출처 분류: _framework/avalonia.js → 'Avalonia.Browser',
  main.js·diag.js·index.html(앱 호스트 스크립트) → 'PD 코드', 그 밖 → '기타'.
  CDP Performance.getMetrics 의 TaskDuration·ScriptDuration 을 유휴 구간 시작·끝에서 읽어 증가량(초)을 남긴다.
  첫 페인트는 performance 'first-contentful-paint'(참고값 — 부하 중).
브라우저는 하나만 띄우고 실행마다 새 컨텍스트를 쓴다(odev-4b 벤치와 겹치지 않게).
"""
import json
import os
import re
import sys
import time
from collections import Counter

from playwright.sync_api import sync_playwright

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import retest_browser2 as t  # noqa: E402

OUT = t.OUT
WRAP = """(() => {
  window.__raf = {n: 0, sites: {}, on: false};
  const o = window.requestAnimationFrame.bind(window);
  window.requestAnimationFrame = function (cb) {
    if (window.__raf.on) {
      window.__raf.n++;
      const lines = (new Error().stack || '').split('\\n').slice(2);
      const site = (lines[0] || '?').trim().replace(/^at\\s+/, '');
      window.__raf.sites[site] = (window.__raf.sites[site] || 0) + 1;
    }
    return o(cb);
  };
})();"""


def origin(site):
    s = site.lower()
    if "avalonia.js" in s:
        return "Avalonia.Browser"
    if re.search(r"(main|diag)\.js|index\.html", s):
        return "PD 코드"
    return "기타"


def one(b, url):
    ctx = t.allow_dup(b.new_context(viewport={"width": 1280, "height": 800}))
    ctx.add_init_script(WRAP)
    page = ctx.new_page()
    cdp = ctx.new_cdp_session(page)
    cdp.send("Performance.enable")
    page.goto(url + "?layout-key=otestw2-idleraf")
    page.wait_for_selector("canvas.avalonia-canvas", timeout=60000)
    time.sleep(8.0)

    def metrics():
        return {m["name"]: m["value"] for m in cdp.send("Performance.getMetrics")["metrics"]}
    m0 = metrics()
    page.evaluate("() => { window.__raf.n = 0; window.__raf.sites = {}; window.__raf.on = true; }")
    t0 = time.time()
    time.sleep(6.0)
    st = page.evaluate("() => { window.__raf.on = false; return window.__raf; }")
    secs = time.time() - t0
    m1 = metrics()
    by_origin = Counter()
    for site, n in st["sites"].items():
        by_origin[origin(site)] += n
    fcp = page.evaluate("() => { const e = performance.getEntriesByName('first-contentful-paint')[0]; return e ? Math.round(e.startTime) : null; }")
    ctx.close()
    return {"raf_total": st["n"], "secs": round(secs, 2), "per_sec": round(st["n"] / secs, 1),
            "by_origin": dict(by_origin), "sites_top": sorted(st["sites"].items(), key=lambda x: -x[1])[:5],
            "idle_TaskDuration_s": round(m1.get("TaskDuration", 0) - m0.get("TaskDuration", 0), 4),
            "idle_ScriptDuration_s": round(m1.get("ScriptDuration", 0) - m0.get("ScriptDuration", 0), 4),
            "fcp_ms(참고)": fcp}


def main():
    args = [a for a in sys.argv[1:] if "=" in a and not a.startswith("--")]
    runs = int(sys.argv[sys.argv.index("--runs") + 1]) if "--runs" in sys.argv else 3
    rows = []
    with sync_playwright() as p:
        b = p.chromium.launch(headless=True, args=t.ARGS)
        for spec in args:
            label, url = spec.split("=", 1)
            for i in range(1, runs + 1):
                r = one(b, url)
                r.update(label=label, run=i)
                rows.append(r)
                print(json.dumps({k: r[k] for k in ("label", "run", "per_sec", "by_origin", "idle_TaskDuration_s", "idle_ScriptDuration_s", "fcp_ms(참고)")}, ensure_ascii=False), flush=True)
        b.close()
    with open(os.path.join(OUT, "retest_web16_idleraf.json"), "w", encoding="utf-8") as f:
        json.dump(rows, f, ensure_ascii=False, indent=1)


if __name__ == "__main__":
    main()
