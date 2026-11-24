# odev-perf 웹 cap 효과 측정(규칙 work/odev-perf_measure_rules_w5.md §1) — headless Chromium 에서 저장→새 페이지 load-layout 을 cap/off 로 재고 결과를 stdout JSON 줄로만 낸다(파일 쓰기 없음)
# 사용: python3 -I web_cap.py <URL> <회차 시작> <회차 끝> [shots]   예) python3 -I web_cap.py http://127.0.0.1:5197/ 1 2
import json
import os
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from pngdiff_lib import decode, diff_stats  # noqa: E402
from playwright.sync_api import sync_playwright  # noqa: E402

URL = sys.argv[1]
K0, K1 = int(sys.argv[2]), int(sys.argv[3])
SHOTS = len(sys.argv) > 4 and sys.argv[4] == "shots"
ARGS = ["--use-angle=swiftshader", "--enable-unsafe-swiftshader", "--ignore-gpu-blocklist", "--enable-webgl"]
OPEN = "open=grid:7,gantt:7,form:6&layout=cascade&save-layout"


def load1():
    return float(open("/proc/loadavg").read().split()[0])


def perf_json(page, timeout=120):
    logs = []
    page.on("console", lambda m: logs.append(m.text))
    t0 = time.time()
    while time.time() - t0 < timeout:
        js = next((x for x in logs if x.startswith("PERF_JSON:")), None)
        if js:
            return json.loads(js[len("PERF_JSON:"):])
        page.wait_for_timeout(250)
    return None


def op(d, name):
    return next((o for o in d.get("operations", []) if o["name"] == name), None)


def run(b, cond, tag):
    key = f"odevperf_{tag}_{int(time.time() * 1000)}"
    ctx = b.new_context(viewport={"width": 1400, "height": 900})
    p1 = ctx.new_page()
    p1.goto(f"{URL}?layout-key={key}&{OPEN}&perf-out=x")
    saved = perf_json(p1)
    p1.close()
    for _ in range(int(os.environ.get("PD_WAIT_S", "60"))):  # 절차(규칙 아님): 저장 페이지가 올린 부하가 가라앉을 때까지 대기
        if load1() < float(os.environ.get("PD_WAIT_LOAD", "1.5")):
            break
        time.sleep(1)
    l0 = load1()
    p2 = ctx.new_page()
    extra = "&grid-first-measure=off" if cond == "off" else ""
    p2.goto(f"{URL}?layout-key={key}&load-layout&perf-out=x{extra}")
    d = perf_json(p2)
    l1 = load1()
    shot = None
    if SHOTS and d:
        p2.wait_for_timeout(2000)
        shot = p2.screenshot()
        # 규칙 b: 마스크용 패널 사각형(메인 창 DIP)과 devicePixelRatio 를 스크린샷 직후 같은 페이지에서 받는다
        body = json.dumps({"cmd": "panel.bounds", "args": {}})
        try:
            resp = json.loads(p2.evaluate("b => window.pdAutomation.invoke(b)", body))
            res = resp.get("result") or {}
            mask = {k: res.get(k) for k in ("BottomPane", "NotificationPane")}
        except Exception as ex:  # noqa: BLE001 — 마스크 실패는 결과에 남기고 판정에서 '원인 미확인' 처리
            mask = {"error": str(ex)[:200]}
        dpr = p2.evaluate("() => window.devicePixelRatio")
    ctx.close()
    rec = {"tag": tag, "cond": cond, "load_start": l0, "load_end": l1, "saved": saved is not None, "ok": d is not None}
    if shot is not None:
        rec.update({"mask": mask, "dpr": dpr})
    if d:
        m1 = op(d, "grid.open.measure1.next")
        lo = op(d, "ui.layout.load")
        rec.update({
            "m1_next_p50": m1 and m1["sync_p50_ms"], "m1_next_n": m1 and m1["count"],
            "step_load": next((s["ms"] for s in d.get("steps", []) if s.get("step") == "load-layout"), None),
            "load_sync": lo and lo["sync_p50_ms"], "load_frame": lo and lo["frame_p50_ms"],
            "gfm": d.get("grid_first_measure"), "capped": d.get("zoom_first_measure_capped"),
            "lastsize": d.get("grid_measure_lastsize"), "loaded": d.get("layout_loaded"), "docs": d.get("doc_count"),
        })
    return rec, shot


with sync_playwright() as pw:
    b = pw.chromium.launch(headless=True, args=ARGS)
    shots = {}
    for k in range(K0, K1 + 1):
        order = ["off", "cap"] if k % 2 else ["cap", "off"]
        if SHOTS:
            order = ["off", "off2", "cap"]
        elif len(sys.argv) > 4 and sys.argv[4].startswith("shot:"):
            # 스크린샷 1장 모드(90초 상한 때문에 1장씩): 브라우저가 PNG 를 s_web_<이름>.png 로 저장(앱 --screenshot 과 같은 시험 산출물)
            name = sys.argv[4][5:]
            SHOTS = True
            rec, shot = run(b, "off" if name.startswith("off") else "cap", f"{name}_{k}")
            print(json.dumps(rec, ensure_ascii=False), flush=True)
            if shot:
                open(os.path.join(os.path.dirname(os.path.abspath(__file__)), f"s_web_{name}_{k}.png" if k >= 20 else f"s_web_{name}.png"), "wb").write(shot)  # 규칙 b 회차는 k>=20 으로 부른다
            order = []
            SHOTS = False
        elif len(sys.argv) > 4:
            order = [sys.argv[4]]  # 한 조건만(bash_exec 90초 상한 때문에 1회 1조건)
        for c in order:
            rec, shot = run(b, "off" if c == "off2" else c, f"{c}_{k}")
            print(json.dumps(rec, ensure_ascii=False), flush=True)
            if shot:
                shots[c] = decode(shot)
    b.close()
    if SHOTS and len(shots) == 3:
        n_oo, t_oo = diff_stats(shots["off"], shots["off2"])
        n_oc, t_oc = diff_stats(shots["off"], shots["cap"])
        print(json.dumps({"shots": True, "diff_off_off2": n_oo, "diff_off_cap": n_oc,
                          "hash_tiles_cap_not_in_noise": sorted(map(list, (t_oc or set()) - (t_oo or set())))}), flush=True)
