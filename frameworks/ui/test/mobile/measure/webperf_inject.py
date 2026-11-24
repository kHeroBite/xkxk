# Fold5 웹 측정(webperf_fold5.py)에 설정 JSON 주입(--settings-json)과 열기·문서 조작별 CDP trace(--trace)를 더하는 확장 모듈
"""
webperf_fold5.py 가 --settings-json 을 받거나 --trace 가 있을 때만 부른다. 둘 다 없으면 아무것도 하지 않아 기존 동작과 같다.
  - --settings-json '{"MdiMoveLight":false}': 저장소를 지운 직후(cold·fresh)·탐색 전에 시험 출처 localStorage 'pd-poc-settings' 에 병합한다.
    키 이름은 앱 설정 DTO(xkxk.frameworks.ui.shell/Theming/UiSettingsDto.cs)의 속성 이름이다. 시험 출처(127.0.0.1) 저장소만 만진다.
  - --trace(웹): doc·open_* 조작마다 측정 전 Tracing.start, 측정 뒤 Tracing.end 로 모아 trace_<조작>_<mode>_<시행>.json 과 요약(row["trace"])을 남긴다.
    trace 를 켠 시행의 시간 값은 계측 부하가 섞이므로 row["traced"]=True 로 표시한다(공식 값과 섞지 않는다).
"""
import json
import os
import time

SETTINGS_KEY = "pd-poc-settings"
TRACE_ACTS = ("doc",)  # open_* 는 접두어로 따로 판정한다
CATS = ("devtools.timeline,disabled-by-default-devtools.timeline,disabled-by-default-devtools.timeline.frame,"
        "v8,disabled-by-default-v8.cpu_profiler,blink.user_timing,toplevel")


def add_args(p):
    p.add_argument("--settings-json", default="", help="cold·fresh 직후 pd-poc-settings 에 병합할 JSON 객체(예: '{\"MdiMoveLight\":false}'). 기본 비움")
    add_args_prewarm(p)
    p.add_argument("--settings-set", default="", help="fresh 로드 뒤 진단 번들 settings.set 으로 보낼 JSON 객체(예: '{\"gridViewPool\":true}'). 기본 비움. 진단 번들 전용")


def add_args_prewarm(p):
    p.add_argument("--prewarm-probe", default="", choices=("", "log", "wait"), help="웹 doc 열기 직전 prewarm_* 지표 기록(log), 또는 prewarm_grid_attached=1 까지 최대 5초 대기 후 기록(wait). 진단 번들 전용, 기본 비움")


def parse(a):
    """잘못된 JSON 이면 측정 전에 바로 실패시킨다. 값이 없으면 None."""
    if not getattr(a, "settings_json", ""):
        return None
    v = json.loads(a.settings_json)
    if not isinstance(v, dict):
        raise ValueError("--settings-json 은 JSON 객체여야 한다")
    return v


def apply(cdp, a):
    """저장소를 지운 직후·탐색 전에 호출한다. 옵션이 없으면 아무것도 하지 않는다."""
    v = parse(a)
    if not v or cdp is None:
        return None
    cur = json.loads(cdp.eval("localStorage.getItem(%s)||'{}'" % json.dumps(SETTINGS_KEY)) or "{}")
    cur.update(v)
    cdp.eval("localStorage.setItem(%s, %s)" % (json.dumps(SETTINGS_KEY), json.dumps(json.dumps(cur))))
    return cur


def verify(c):
    """fresh 로드 뒤 호출. --settings-set 이 있으면 진단 번들 settings.set 을 보낸 뒤(?fresh 는 localStorage 를 읽지 않아 로드 뒤 원격 설정만 먹는다),
    --settings-set·--settings-json 중 하나라도 있으면 settings.get 응답을 <out>/settings_verify.jsonl 에 남긴다(켬·끔 반영 확인용, 1회성 진단 확장)."""
    ss = getattr(c.a, "settings_set", "")
    if not (ss or getattr(c.a, "settings_json", "")) or not c.web or not c.cdp:
        return None
    sent = _auto(c, "settings.set", **json.loads(ss)) if ss else None
    r = _auto(c, "settings.get")
    with open(os.path.join(c.a.out, "settings_verify.jsonl"), "a", encoding="utf-8") as f:
        f.write(json.dumps({"t": time.strftime("%H:%M:%S"), "set": sent, "resp": r}, ensure_ascii=False) + "\n")
    return r


def _find_prewarm(o, out):
    if isinstance(o, dict):
        for k, v in o.items():
            if isinstance(k, str) and k.startswith("prewarm_"):
                out[k] = v
            else:
                _find_prewarm(v, out)
    elif isinstance(o, list):
        for v in o:
            _find_prewarm(v, out)
    return out


def pre_open(c, act):
    """--prewarm-probe log|wait 일 때만, 웹 doc 열기 직전(메뉴를 연 뒤·측정 시작 전)에 perf.get 의 prewarm_* 지표와 페이지 시작 뒤 경과(performance.now)를
    <out>/prewarm_probe.jsonl 에 남긴다. wait 면 prewarm_grid_attached==1 이 될 때까지 최대 5초 기다린 뒤 기록한다(예열 경합 가설 진단용 1회성 확장, 옵션 없으면 무동작)."""
    mode = getattr(c.a, "prewarm_probe", "")
    if not mode or act != "doc" or not c.web or not c.cdp:
        return None
    t0 = time.time()
    pw = _find_prewarm(_auto(c, "perf.get"), {})
    waited = 0.0
    if mode == "wait":
        while pw.get("prewarm_grid_attached") != 1 and time.time() - t0 < 5:
            time.sleep(0.1)
            pw = _find_prewarm(_auto(c, "perf.get"), {})
        waited = round(time.time() - t0, 2)
    try:
        now_ms = c.cdp.eval("performance.now()")
    except Exception as e:  # noqa: BLE001 — 진단 수집 실패가 측정을 멈추지 않게 한다
        now_ms = "EXC " + str(e)[:80]
    with open(os.path.join(c.a.out, "prewarm_probe.jsonl"), "a", encoding="utf-8") as f:
        f.write(json.dumps({"t": time.strftime("%H:%M:%S"), "run": (getattr(c, "run", None) or {}).get("i"), "mode": mode, "page_ms_at_open": now_ms,
                            "waited_s": waited, "prewarm": pw, "grid_attached": pw.get("prewarm_grid_attached") == 1}, ensure_ascii=False) + "\n")
    return pw


def wants_trace(c, act):
    return bool(getattr(c.a, "trace", False) and c.web and c.cdp and (act in TRACE_ACTS or act.startswith("open_")))


def trace_begin(c, act):
    """측정 직전 호출. 대상 조작이 아니면 False."""
    if not wants_trace(c, act):
        return False
    _auto(c, "perf.reset")  # 진단 번들이면 앱 단계 계측을 이번 조작만 남게 비운다(제품 번들은 pdAutomation 이 없어 무시). trace 시작 전에 불러 trace 메시지와 섞이지 않는다
    c.cdp.call("Tracing.start", categories=CATS, transferMode="ReportEvents")
    return True


def _auto(c, cmd, **args):
    """진단 번들의 window.pdAutomation 원격 명령 1건. 없거나 실패하면 {"error": ...}(측정은 계속한다)."""
    body = json.dumps({"cmd": cmd, "args": args}, ensure_ascii=False)
    try:
        v = c.cdp.eval("(async b => window.pdAutomation ? await window.pdAutomation.invoke(b) : JSON.stringify({error: 'no pdAutomation'}))(%s)" % json.dumps(body))
        return json.loads(v) if isinstance(v, str) else {"error": "응답 없음"}
    except Exception as e:  # noqa: BLE001 — 진단 수집 실패가 측정을 멈추지 않게 한다
        return {"error": "EXC " + str(e)[:200]}


def trace_end(c, act, row, on):
    """측정 직후 호출. on 이 False 면 row 를 그대로 돌려준다."""
    if not on:
        return row
    c.cdp.mid += 1
    c.cdp.ws.send(json.dumps({"id": c.cdp.mid, "method": "Tracing.end", "params": {}}))
    ev = []
    while True:
        m = json.loads(c.cdp.ws.recv())
        if m.get("method") == "Tracing.dataCollected":
            ev += m["params"]["value"]
        if m.get("method") == "Tracing.tracingComplete":
            break
    i = (getattr(c, "run", None) or {}).get("i", 0)
    path = os.path.join(c.a.out, "trace_%s_%s_%d.json" % (act, c.a.mode, i))
    with open(path, "w", encoding="utf-8") as f:
        json.dump(ev, f)
    summ = getattr(c, "trace_summarizer", None)
    row["trace"] = {"file": path, "events": len(ev), **(summ(ev) if summ else {})}
    row["traced"] = True
    row["perf_ops"] = _auto(c, "perf.ops")  # 진단 번들: 이번 조작의 앱 단계 시간(grid.ctor.*·grid.open.*·doc.open.grid 등). trace 종료 뒤라 메시지가 섞이지 않는다
    return row
