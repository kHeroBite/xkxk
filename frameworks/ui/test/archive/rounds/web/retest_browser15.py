# 웹 알림 패널 유무별 테마 전환 census 대조(odev-3 요청) — DPR 1·2.625 × 알림 패널 기본/off/empty, 문서 grid·gantt·form, theme.census pd-ocean·pd-dark 의 measure_invalid_visible·who_measure_visible
# 사용: PD_URL=http://127.0.0.1:5189/ python3 tools/retest_browser15.py   (진단판·헤드리스 전용)
import json
import os
import sys
import time

from playwright.sync_api import sync_playwright

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import retest_browser2 as t  # noqa: E402

OUT = t.OUT
DPRS = [1, 2.625]
VARIANTS = [("A(기본)", "", 2), ("notify-panel=off", "&notify-panel=off", 2), ("notify-panel=empty", "&notify-panel=empty", 1)]
THEMES = ["pd-ocean", "pd-dark"]
rows = []


def top_who(c, n=8):
    """who_measure_visible 상위 n 개(항목 = {key: '형식#이름@부모', count}) — 원본 그대로."""
    return (c.get("who_measure_visible") or [])[:n]


def one(b, dpr, label, q, rep):
    ctx = t.allow_dup(b.new_context(viewport={"width": 1280, "height": 800}, device_scale_factor=dpr))
    ctx, page, r = t.boot(b, "?fresh&layout-key=otestw2-census%s" % q, ctx=ctx)
    for k in ("grid", "gantt", "form"):
        r.call("docs.open", kind=k, title="CS-" + k, allowDuplicate=True)
        time.sleep(0.8)
    time.sleep(2.0)
    pf = r.call("perf.get") or {}
    notify = None
    stack = [pf]
    while stack and notify is None:
        x = stack.pop()
        if isinstance(x, dict):
            notify = x.get("notify_panel", notify)
            stack.extend(v for v in x.values() if isinstance(v, (dict, list)))
        elif isinstance(x, list):
            stack.extend(x)
    row = {"dpr_req": dpr, "dpr": page.evaluate("() => window.devicePixelRatio"), "variant": label, "rep": rep, "notify_panel_metric": notify, "themes": {}}
    for th in THEMES:
        c = r.call("theme.census", themeId=th, _timeout=120) or {}
        # 응답은 단계별(baseline·sync·posted·settled) — 단계마다 measure_invalid_visible 과 who_measure_visible 상위를 남긴다(i1023b 실측 구조)
        row["themes"][th] = {"sync_ms": c.get("sync_ms"), "settled_ms": c.get("settled_ms"), "variant": (c.get("variant_before"), c.get("variant_after")),
                             "stages": {st: {"measure_invalid_visible": (c.get(st) or {}).get("measure_invalid_visible"),
                                             "arrange_invalid_visible": (c.get(st) or {}).get("arrange_invalid_visible"),
                                             "who_measure_visible_top": top_who(c.get(st) or {})}
                                        for st in ("baseline", "sync", "posted", "settled")}}
        time.sleep(0.5)
    rows.append(row)
    print(json.dumps({"dpr": row["dpr"], "variant": label, "rep": rep, "notify": notify,
                      "miv": {th: {st: v["measure_invalid_visible"] for st, v in row["themes"][th]["stages"].items()} for th in THEMES},
                      "who_sync": {th: row["themes"][th]["stages"]["sync"]["who_measure_visible_top"][:3] for th in THEMES}}, ensure_ascii=False), flush=True)
    ctx.close()


def main():
    with sync_playwright() as p:
        b = p.chromium.launch(headless=True, args=t.ARGS)
        for dpr in DPRS:
            for label, q, reps in VARIANTS:
                for rep in range(1, reps + 1):
                    one(b, dpr, label, q, rep)
        b.close()
    with open(os.path.join(OUT, "retest_web15_census.json"), "w", encoding="utf-8") as f:
        json.dump(rows, f, ensure_ascii=False, indent=1)


if __name__ == "__main__":
    main()
