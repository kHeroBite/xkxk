# Fold5 웹 측정(webperf_fold5.py)의 시작 구간·캐시 경유·탭 머리 먼저·탭 처음/재방문 확장 모듈(새 옵션 인자로만 켜진다)
"""
webperf_fold5.py 가 --startup-http·--tab-header-first·--collect-startup·actions 에 tab_first,tab_revisit 를 줄 때만 부른다. 주지 않으면 기존 동작과 같다.
  - 시험 출처 localStorage 의 'pd-startup-options'(httpCache direct|revalidate)와 'pd-poc-settings'(TabHeaderFirst)를 cold 로 지운 직후·탐색 전에 쓴다.
    시험 출처(127.0.0.1) 저장소만 만진다. 사용자 Chrome 데이터는 건드리지 않는다.
  - window.__pdStartup(done·measures·resources.nativeWasm[0].via)을 행에 남긴다.
  - tab_first = doc 직후 처음 누르는 tab1, tab_revisit = 이미 본 tab1 을 다시(tab_first 뒤에 준다). 둘 다 tab2 로 되돌려 상태를 그대로 두며 펼침 전용.
"""
import hashlib
import json
import math
import os
import re
import statistics
import time

STARTUP_KEY = "pd-startup-options"
SETTINGS_KEY = "pd-poc-settings"
TAB_ACTIONS = ("tab_first", "tab_revisit")


def add_args(p):
    p.add_argument("--startup-http", choices=("direct", "revalidate"), help="cold 직전 pd-startup-options.httpCache 를 이 값으로 쓴다")
    p.add_argument("--tab-header-first", choices=("on", "off"), help="cold 직전 pd-poc-settings.TabHeaderFirst 를 켬/끔으로 쓴다")
    p.add_argument("--collect-startup", action="store_true", help="웹 cold·warm 행에 window.__pdStartup 요약을 남긴다")
    p.add_argument("--tab-cross", action="store_true", help="tab_body 를 시행마다 탭 머리 먼저 켬/끔 순서를 교차해 둘 다 잰다(펼침 웹)")
    p.add_argument("--max-load", type=float, help="시행 시작 전 호스트 load1 이 이 값 미만이 될 때까지(최대 300초) 기다리고 시행에 low_load 를 남긴다")
    p.add_argument("--drag-docs", default="1,3,5", help="drag_mdi 가 문서 수 N 마다 끌기 frame 간격을 잰다(쉼표)")
    p.add_argument("--web-url-query", default="", help="웹 ext 조작 전에 시험 URL 에 붙일 query(예: ui=pc). 기본 비움 — MDI 형 강제 경로는 앱 확인 필요")
    p.add_argument("--web-setting", default="", help="웹 ext 조작 전에 pd-poc-settings 에 병합할 JSON. 기본 비움")
    p.add_argument("--open-ids", default="", help="open_* 메뉴 id 덮어쓰기 'grid=shell.mnuOpenGrid,form=...,gantt=...'")


def prepare(cdp, a):
    """저장소를 지운 직후·탐색 전에 호출한다. 옵션이 하나도 없으면 아무것도 하지 않는다(기존 동작)."""
    if not (getattr(a, "startup_http", None) or getattr(a, "tab_header_first", None)):
        return None
    sets = {}
    if a.startup_http:
        sets[STARTUP_KEY] = {"timing": True, "httpCache": a.startup_http, "integrity": True}
    if a.tab_header_first:
        sets[SETTINGS_KEY] = {"TabHeaderFirst": a.tab_header_first == "on"}
    for k, v in sets.items():
        cdp.eval("localStorage.setItem(%s, %s)" % (json.dumps(k), json.dumps(json.dumps(v))))
    return sets


def collect(cdp, wait_s=40):
    """window.__pdStartup 이 done 이 될 때까지 기다린 뒤 요약을 돌려준다. 못 읽으면 error 를 값으로 남긴다."""
    end = time.time() + wait_s
    s = None
    while time.time() < end:
        try:
            s = json.loads(cdp.eval("JSON.stringify(window.__pdStartup||null)") or "null")
        except Exception as e:  # noqa: BLE001 — 읽기 실패는 값으로 남긴다
            s = {"error": repr(e)[:120]}
        if s and s.get("done"):
            break
        time.sleep(1)
    if not s:
        return {"error": "__pdStartup 없음"}
    res = s.get("resources") or {}
    nw = (res.get("nativeWasm") or [{}])[0]
    return {"done": s.get("done"), "options": s.get("options"), "measures": s.get("measures"), "first_frame_source": s.get("firstFrameSource"),
            "native_wasm_via": nw.get("via"), "native_wasm": nw, "navigation_type": res.get("navigationType"),
            "resources": {k: res.get(k) for k in ("total", "cache", "revalidated", "network", "transferBytes")}}


def do_tab(c, act, keep, measure, tap_hold, fg_guard):
    """tab_first / tab_revisit — 펼침 웹만. measure·tap_hold·fg_guard 는 webperf_fold5 의 것을 받는다."""
    if c.a.mode != "open":
        return {"name": act, "skipped": "펼침 전용"}
    if not (c.ids.get("tab1") and c.ids.get("tab2")):
        raise RuntimeError("문서 탭 좌표 없음")
    t1, t2 = c.box(c.ids["tab1"]), c.box(c.ids["tab2"])
    strip = (min(t1[0], t2[0]), min(t1[1], t2[1]) - 6, max(t1[2], t2[2]), max(t1[3], t2[3]) + 6)
    fg_guard(c)
    # doc 직후에는 새 문서(tab2)가 선택돼 있다. 선택 안 된 tab1 을 눌러 잰 뒤 tab2 로 되돌려 상태를 그대로 둔다.
    # tab_first = 로딩 뒤 tab1 을 처음 누름, tab_revisit = 이미 한 번 본 tab1 을 다시 누름(tab_first 를 먼저 줘야 의미가 있다)
    x, y = c.pt(c.ids["tab1"])
    row = measure(c.rec, c.tmp, act, lambda: tap_hold(x, y), strip, (x, y), 2.5, keep)
    row["visit"] = "first" if act == "tab_first" else "revisit"
    tap_hold(*c.pt(c.ids["tab2"]))
    time.sleep(1.5)
    return row


# ---------- R2 확장: 본문 탭·문서 종류별 열기·MDI 끌기·부하 기록·P1~P3 emit ----------
EXT_ACTIONS = ("tab_body", "open_grid", "open_form", "open_gantt", "drag_mdi")
OPEN_DEFAULT = {"open": {"grid": "shell.mnuOpenGrid", "form": "shell.mnuOpenForm", "gantt": "shell.mnuOpenGantt"},  # form·gantt id 는 추정 — 첫 실행에서 확인
                "closed": {"grid": "shell.navDrawer.OpenGrid", "form": "shell.navDrawer.OpenForm", "gantt": "shell.navDrawer.OpenGantt"}}
FRAME_CATS = "devtools.timeline,disabled-by-default-devtools.timeline.frame,benchmark,cc,viz,toplevel"


def run_begin(c, a, run, i):
    """시행 시작 hook — 현재 run 을 기억한다. --max-load 면 load1 이 낮아질 때까지(최대 300초) 기다린다."""
    c.run = run
    if getattr(a, "max_load", None):
        end = time.time() + 300
        while os.getloadavg()[0] >= a.max_load and time.time() < end:
            time.sleep(10)
        run["load1"] = round(os.getloadavg()[0], 1)


def run_end(run, a):
    """시행 끝 hook — --max-load 가 있을 때만 low_load 를 남긴다(시작·끝 load1 모두 기준 미만)."""
    if getattr(a, "max_load", None):
        run["load1_end"] = round(os.getloadavg()[0], 1)
        run["low_load"] = bool(run.get("load1", 99) < a.max_load and run["load1_end"] < a.max_load)


def _ensure_web_settings(c):
    """--web-setting·--web-url-query 가 있으면 웹에서 한 번 적용하고 다시 연다."""
    a = c.a
    if not c.web or getattr(c, "_ws_applied", False) or not (a.web_setting or a.web_url_query):
        return
    if a.web_setting:
        cur = json.loads(c.cdp.eval("localStorage.getItem(%s)||'{}'" % json.dumps(SETTINGS_KEY)) or "{}")
        cur.update(json.loads(a.web_setting))
        c.cdp.eval("localStorage.setItem(%s, %s)" % (json.dumps(SETTINGS_KEY), json.dumps(json.dumps(cur))))
    base = a.url.split("?")[0].rstrip("/") + "/"
    c.cdp.call("Page.navigate", url=base + ("?" + a.web_url_query + "&" if a.web_url_query else "?") + "x=%d" % time.time())
    time.sleep(25)
    c._ws_applied = True


def _open_id(c, kind):
    ov = dict(kv.split("=", 1) for kv in (c.a.open_ids or "").split(",") if "=" in kv)
    return ov.get(kind) or OPEN_DEFAULT[c.a.mode][kind]


def _doc_count(dump_nodes):
    return len([n for n in dump_nodes() if re.match(r"^[A-Za-z]+-\d+$", n["text"] or "")])


def _open_kind(c, kind, measure, tap, tap_hold, fg_guard, find, dump_nodes, keep, act):
    """메뉴 → kind 항목을 눌러 문서 열기 응답을 잰다(doc 와 같은 방식)."""
    tap(*c.pt(c.ids["opener"]))
    time.sleep(2.5 if c.web else 1.8)
    fg_guard(c)
    b = (c.ids.get("item_" + kind) if c.web else None) or find(dump_nodes(), _open_id(c, kind))  # 웹은 앱 덤프 좌표(c.pt 가 dy 보정)
    if not b:
        tap(*c.pt(c.ids["opener"]))
        raise RuntimeError("메뉴 항목 노드 없음(id 추정 확인 필요): %s" % _open_id(c, kind))
    x, y = c.pt(b)
    import webperf_inject as wi  # noqa: PLC0415 — --trace(웹)일 때만 trace 를 감싼다
    tr = wi.trace_begin(c, act)
    row = wi.trace_end(c, act, measure(c.rec, c.tmp, act, lambda: tap_hold(x, y), c.doc_area(), (x, y), 4.0 if c.web else 3.0, keep), tr)
    row["kind"] = kind
    time.sleep(1)
    return row


def frame_intervals(events):
    """chrome trace 의 DrawFrame 사이 간격(ms) 중 가장 긴 연속 움직임 구간(간격 100ms 이하)을 돌려준다."""
    ts = sorted(e["ts"] for e in events if e.get("name") == "DrawFrame" and "ts" in e)
    iv = [(b - a) / 1000.0 for a, b in zip(ts, ts[1:])]
    best, cur = [], []
    for v in iv:
        if v <= 100:
            cur.append(v)
        else:
            best = cur if len(cur) > len(best) else best
            cur = []
    return cur if len(cur) > len(best) else best


def do_ext(c, act, keep, measure, tap, tap_hold, swipe, fg_guard, find, dump_nodes, center):
    """EXT_ACTIONS 처리 — 인자는 webperf_fold5 의 함수를 받는다(순환 import 회피)."""
    _ensure_web_settings(c)
    fg_guard(c)
    if act.startswith("open_"):
        return _open_kind(c, act[5:], measure, tap, tap_hold, fg_guard, find, dump_nodes, keep, act)
    if act == "tab_body":
        if c.a.mode != "open":
            return {"name": act, "skipped": "펼침 전용"}

        def one(cond):
            if cond:
                c.cdp.eval("(()=>{const k=%s;const o=JSON.parse(localStorage.getItem(k)||'{}');o.TabHeaderFirst=%s;localStorage.setItem(k,JSON.stringify(o))})()"
                           % (json.dumps(SETTINGS_KEY), "true" if cond == "on" else "false"))
                c.cdp.call("Page.reload")
                time.sleep(22)
                c.open_doc()
                c.open_doc()
                n = dump_nodes()
                c.ids["tab1"] = find(n, "Grid-1", cls="DocumentTabStripItem") or c.ids["tab1"]
                c.ids["tab2"] = find(n, "Grid-2", cls="DocumentTabStripItem") or c.ids["tab2"]
                fg_guard(c)
            x, y = c.pt(c.ids["tab1"])
            row = measure(c.rec, c.tmp, "tab_body_" + cond if cond else act, lambda: tap_hold(x, y), c.doc_area(), (x, y), 2.5, keep)
            tap_hold(*c.pt(c.ids["tab2"]))
            time.sleep(1.5)
            row["tab_header_first"] = cond
            return row
        if not c.a.tab_cross:
            return one(None)
        i = c.run["i"] if getattr(c, "run", None) else 0
        order = ("on", "off") if i % 2 == 0 else ("off", "on")
        first = one(order[0])
        first["order"] = "%s>%s" % order
        c.run["rows"].append(first)
        last = one(order[1])
        last["order"] = "%s>%s" % order
        return last
    if act == "drag_mdi":
        out = []
        for n in [int(v) for v in c.a.drag_docs.split(",") if v.strip()]:
            if c.web:  # R2: Chrome 덤프에는 문서 머리 텍스트가 없어 센 값이 늘 0 이라 무한히 연다 — 시행 안에서 연 횟수를 직접 센다(시행 첫 조작이 warm 이라 문서 0개에서 시작)
                have = c.run.setdefault("_wdocs", 0)
                while have < n:
                    c.open_doc()
                    have += 1
                c.run["_wdocs"] = have
            else:
                while _doc_count(dump_nodes) < n:
                    c.open_doc()
            fg_guard(c)
            tb = [x["b"] for x in dump_nodes() if x["id"] in ("TitleArea", "PART_Title") and x["b"][3] - x["b"][1] < 200]
            tgt = max(tb, key=lambda b: b[2] - b[0]) if tb else c.ids.get("tab1")
            if not tgt:
                out.append({"name": "drag_mdi_%d" % n, "error": "끌 제목 노드 없음(확인 필요)"})
                continue
            x, y = c.pt(tgt)
            x = tgt[0] + 60
            ev = c.cdp.trace(lambda: (time.sleep(0.5), swipe(x, y, x - 250, y + 300, 700), time.sleep(1.0)), FRAME_CATS)
            iv = frame_intervals(ev)
            row = {"name": "drag_mdi_%d" % n, "docs": n, "frame_source": "chrome_trace", "frames": len(iv) + 1 if iv else 0}
            if len(iv) < 5:
                row["error"] = "frame 간격 부족 %d" % len(iv)
            else:
                v = sorted(iv)
                row["frame_p95_ms"] = round(v[max(0, math.ceil(0.95 * len(v)) - 1)], 2)
                row["frame_median_ms"] = round(statistics.median(v), 2)
            out.append(row)
            time.sleep(1.5)
        for r in out[:-1]:
            c.run["rows"].append(r)
        return out[-1] if out else {"name": act, "error": "drag_docs 비어 있음"}
    raise ValueError(act)


# ---------- emit: 원자료 → logs/pcaway/P1~P3.json (pcaway_gate.py 형식) ----------
def p95(v):
    v = sorted(v)
    return round(v[max(0, math.ceil(0.95 * len(v)) - 1)], 1) if v else None


def _rows(doc, name):
    return [(run, r) for run in (doc or {}).get("runs", []) for r in run.get("rows", []) if r.get("name") == name and not r.get("error") and not r.get("skipped")]


def _val(r, metric):
    return r.get("settle_ms") if metric == "settle" and r.get("settle_ms") is not None else r.get("resp_ms")


def _sha(path):
    with open(path, "rb") as f:
        return hashlib.sha256(f.read()).hexdigest()


def _meas(name, value, ok):
    return {"name": name, "value": value, "pass": bool(ok)}


def build_result(item, raws, metric="settle"):
    """raws: {'open':doc,'closed':doc}. 반환 (measurements, detail, 원자료 목록)."""
    m = []
    docs = [d for d in raws.values() if d]
    det = {"load1_per_run": [r.get("load1") for d in docs for r in d.get("runs", [])]}
    if item == "P1":
        o, cl = raws.get("open"), raws.get("closed")
        for nm, d in (("fold5_open_doc_p95_ms_open", o), ("fold5_open_doc_p95_ms_closed", cl)):
            v = p95([_val(r, metric) for _, r in _rows(d, "doc") if _val(r, metric) is not None]) if d else None
            m.append(_meas(nm, v, v is not None and v <= 500))
        for kind in ("grid", "form", "gantt"):
            vals = [p95([_val(r, metric) for _, r in _rows(d, "open_" + kind) if _val(r, metric) is not None]) for d in (o, cl) if d]
            vals = [v for v in vals if v is not None]
            v = max(vals) if vals else None
            m.append(_meas("fold5_open_%s_p95_ms" % kind, v, v is not None and v <= 500))
        src_ok = True
        for n in (1, 3, 5):
            rr = _rows(o, "drag_mdi_%d" % n)
            fr = [r["frame_p95_ms"] for _, r in rr if r.get("frame_p95_ms") is not None]
            src_ok = src_ok and bool(fr) and all(r.get("frame_source") == "chrome_trace" for _, r in rr)
            v = p95(fr) if fr else None
            m.append(_meas("fold5_drag_frame_p95_ms_doc%d" % n, v, v is not None and v <= 25))
        low = min([sum(1 for r in d.get("runs", []) if r.get("low_load")) for d in docs] or [0])
        m.append(_meas("fold5_runs_low_load", low, low >= 10))
        m.append(_meas("frame_source_chrome_trace", bool(src_ok), src_ok))
    elif item == "P2":
        d = raws.get("open") or {}
        on = [_val(r, metric) for _, r in _rows(d, "tab_body_on") if _val(r, metric) is not None]
        off = [_val(r, metric) for _, r in _rows(d, "tab_body_off") if _val(r, metric) is not None]
        po = p95(on)
        m.append(_meas("fold5_tab_body_p95_ms_on", po, po is not None and po <= 500))
        le = bool(on and off and po <= p95(off))
        m.append(_meas("tab_body_on_le_off", le, le))
        orders = {r.get("order") for _, r in _rows(d, "tab_body_on") + _rows(d, "tab_body_off")}
        crossed = {"on>off", "off>on"} <= orders
        m.append(_meas("tab_order_crossed", crossed, crossed))
        each = min(len(on), len(off))
        m.append(_meas("tab_runs_each", each, each >= 10))
        det["tab_body_off_p95"] = p95(off)
    elif item == "P3":
        d = raws.get("closed") or {}
        lowruns = {run["i"] for run in d.get("runs", []) if run.get("low_load")}
        for nm, act, lim in (("closed_menu_median_ms", "menu", 118), ("closed_tab_median_ms", "tab", 86)):
            vals = [r["resp_ms"] for run, r in _rows(d, act) if run["i"] in lowruns and r.get("resp_ms") is not None]
            v = statistics.median(vals) if vals else None
            m.append(_meas(nm, v, v is not None and v <= lim))
        only = bool(lowruns) and len(lowruns) == len(d.get("runs", []))
        m.append(_meas("closed_low_load_only", only, only))
        det.update(low_load_runs=len(lowruns), total_runs=len(d.get("runs", [])))
    return m, det, docs


def emit(a):
    raws = {k: json.load(open(p, encoding="utf-8")) for k, p in (("open", a.raw_open), ("closed", a.raw_closed)) if p}
    m, det, docs = build_result(a.item, raws, a.metric)
    me = os.path.abspath(__file__)
    root = os.path.abspath(a.root)
    start = min((d.get("started_at") for d in docs if d.get("started_at")), default=None)
    fin = max((d.get("finished_at") for d in docs if d.get("finished_at")), default=None)
    bp = os.path.join(root, a.build_path)
    res = {"ac_id": a.item, "verdict": "PASS" if m and all(x["pass"] for x in m) else "FAIL", "writer": a.writer, "run_id": a.run_id,
           "started_at": start, "finished_at": time.strftime("%Y-%m-%dT%H:%M:%S+09:00"),
           "measured_started_at": start, "measured_finished_at": fin,
           "script": {"path": os.path.relpath(me, root), "sha256": _sha(me)},
           "build": {"path": a.build_path, "sha256": _sha(bp) if os.path.isfile(bp) else a.build_sha},
           "measurements": m, "detail": det, "metric": a.metric}
    if a.state:  # R2: 판정기 safety 블록(pcaway_report safety-begin/end 상태의 safety)
        st_doc = json.load(open(a.state, encoding="utf-8"))
        if "safety" not in st_doc:
            print("거부: safety-end 전 상태 파일이다")
            return 3
        res["safety"] = st_doc["safety"]
    if a.capture:  # R2: 캡처 목록(판독 read_by·finding 은 retest_real_input --annotate 로 넣는다)
        res["captures"] = [{"path": os.path.relpath(os.path.abspath(c), root), "sha256": _sha(os.path.abspath(c)), "read_by": "", "finding": ""} for c in a.capture]
    os.makedirs(os.path.join(root, a.out_dir), exist_ok=True)
    path = os.path.join(root, a.out_dir, a.item + ".json")
    with open(path, "w", encoding="utf-8") as f:
        json.dump(res, f, ensure_ascii=False, indent=1, default=str)
    print(res["verdict"], path)
    return 0 if res["verdict"] == "PASS" else 1


def _self_test():
    def row(name, v, **kw):
        return dict({"name": name, "resp_ms": v, "settle_ms": v}, **kw)
    runs = [{"i": i, "load1": 3.0, "low_load": True, "rows": [row("doc", 400), row("open_grid", 300), row("open_form", 300), row("open_gantt", 300),
            row("tab_body_on", 200, order="on>off"), row("tab_body_off", 250, order="off>on"), row("menu", 100), row("tab", 80)]
            + [{"name": "drag_mdi_%d" % n, "frame_p95_ms": 20, "frame_source": "chrome_trace"} for n in (1, 3, 5)]} for i in range(10)]
    d = {"runs": runs, "started_at": "2026-11-07T01:00:00+09:00", "finished_at": "2026-11-07T02:00:00+09:00"}
    for it, raws in (("P1", {"open": d, "closed": d}), ("P2", {"open": d}), ("P3", {"closed": d})):
        m, det, _ = build_result(it, raws)
        assert m and all(x["pass"] for x in m), (it, m)
        assert det["load1_per_run"] and all(v < 8 for v in det["load1_per_run"])
    runs[0]["low_load"] = False
    m, _, _ = build_result("P3", {"closed": d})
    assert not [x for x in m if x["name"] == "closed_low_load_only"][0]["pass"]
    fe = [{"name": "DrawFrame", "ts": t * 16000} for t in range(20)] + [{"name": "DrawFrame", "ts": 2000000}]
    assert len(frame_intervals(fe)) == 19
    print("self-test OK")
    return 0


def main():
    import argparse as ap
    p = ap.ArgumentParser(description="webperf_startup 보조 명령 — emit(P1~P3.json 생성)·--self-test")
    p.add_argument("--self-test", action="store_true")
    sub = p.add_subparsers(dest="cmd")
    e = sub.add_parser("emit", help="webperf_fold5 원자료 JSON 에서 logs/pcaway/P1~P3.json 을 만든다")
    e.add_argument("--item", required=True, choices=("P1", "P2", "P3"))
    e.add_argument("--raw-open")
    e.add_argument("--raw-closed")
    e.add_argument("--writer", required=True, help="otest 로 시작해야 한다")
    e.add_argument("--run-id", required=True)
    e.add_argument("--build-path", required=True, help="저장소 루트 기준 최종 빌드 산출물 경로")
    e.add_argument("--build-sha", default="")
    e.add_argument("--root", default=".")
    e.add_argument("--out-dir", default="logs/pcaway")
    e.add_argument("--state", help="pcaway_report safety-end 로 만든 상태 파일(결과 safety 블록 원천)")
    e.add_argument("--capture", action="append", help="결과에 넣을 캡처 PNG(반복 지정)")
    e.add_argument("--metric", choices=("settle", "resp"), default="settle", help="열기·탭 본문 시간 기준(settle=화면 안정, resp=첫 반응)")
    a = p.parse_args()
    if a.self_test:
        return _self_test()
    if a.cmd == "emit":
        return emit(a)
    p.print_help()
    return 0


if __name__ == "__main__":
    import sys as _s
    _s.exit(main())
