# W4 웹 제품판 실측기 — Windows Chrome 실창(CDP 입력)으로 창 열기·SDI 열기·탭 전환·문서 1/3/5 끌기를 추적하고 원자료·요약 JSON 을 남긴다
# 사용: python3 w4_web.py <출력폴더(WSL)> <단계: open|sdi|tab|drag|prof|all> [반복=12]
import json, os, subprocess, sys, time
UI = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", ".."))
TOOL = os.path.join(UI, "test/common/measure/pdchrome.mjs")
NODE = "/mnt/c/Program Files/nodejs/node.exe"
URL = os.environ.get("W10_URL", "http://127.0.0.1:5241/")
TAG = os.environ.get("W10_TAG", "run")
CPORT = int(os.environ.get("W10_CPORT", "9373"))
FAST = os.environ.get("W8_FAST", "on")
SETTINGS = os.environ.get("W10_SETTINGS", "")  # pd-poc-settings JSON 문자열(예: {"PrewarmViews":false})
LAYOUT = os.environ.get("W10_LAYOUT", "after")  # before=도구모음 줄 있음, after=도구모음 줄 제거 판  # 문서 전환 가속 켬/끔(localStorage pd-poc-settings.FastDocumentSwitch)
# drag_os CPU 프로파일(기본 끔). on 이면 문서 수마다 시간 측정 회차가 끝난 뒤 같은 SendInput 끌기를 프로파일을 켠 채 따로 돌린다.
# 결과는 rec["prof_dragos_doc{n}"]·prof/*.cpuprofile 에만 남고 판정 원자료 rec["dragos_doc{n}"] 에는 들어가지 않는다(프로파일 부하가 프레임 간격을 흐리므로)
CPU_PROFILE = os.environ.get("W10_CPU_PROFILE", "off").strip().lower() in ("on", "1", "true")
CPU_PROFILE_RUNS = max(1, int(os.environ.get("W10_CPU_PROFILE_RUNS", "2")))
# drag_os 진단 인자(기본 꺼짐 — 미지정이면 공식 W4 경로·출력과 같다). 진단 결과는 판정에 쓰지 않는다
#   W10_DRAG_LAYOUT=tile : 문서를 다 연 뒤 pdAutomation docs.arrange mode=tile-v 로 바둑판 배치(겹침 없음)하고 활성 문서 제목줄을 끈다
#   W10_DOC_KIND=form    : 문서 1/3/5 를 grid·gantt 섞지 않고 모두 form 으로 연다
DRAG_LAYOUT = os.environ.get("W10_DRAG_LAYOUT", "cascade").strip().lower()
DOC_KIND = os.environ.get("W10_DOC_KIND", "mixed").strip().lower()
#   W10_DRAG_KIND=grid|gantt|form : 끄는 맨 위 창 종류. 기존 열기 순서에서 같은 종류를 맨 뒤로 바꿔 놓고, 없으면(문서 1개 등) 마지막만 그 종류로 바꾼다
DRAG_KIND = os.environ.get("W10_DRAG_KIND", "").strip().lower()
#   W10_DRAG_SHOT=on : doc5 시간 측정 뒤 끌기 1회를 더 하며 끌기 중 화면 1장(cap_dragos_doc5_during.png)을 남긴다(판정 원자료 아님)
DRAG_SHOT = os.environ.get("W10_DRAG_SHOT", "off").strip().lower() in ("on", "1", "true")
#   W10_DRAG_PROBE=on : drag_os 회차마다 추적(trace) 밖에서 끌기 전 chrome.moveLight 조회·perf.reset, 끌기 뒤 chrome.moveLight·perf.get(layout_pass_*·frame_*)·창 가시 상태를 rec 의 a["probe"] 에 남긴다
DRAG_PROBE = os.environ.get("W10_DRAG_PROBE", "off").strip().lower() in ("on", "1", "true")
#   W10_DRAG_SHOT_DOCS=1,5 : W10_DRAG_SHOT=on 일 때 끌기 중 캡처를 찍을 문서 수(1·3·5 중, 쉼표 구분). 미지정이면 5 만(기존과 같음)
DRAG_SHOT_DOCS_RAW = os.environ.get("W10_DRAG_SHOT_DOCS", "").strip()
#   W10_MAGNET_PROBE=on : drag_os 끌기마다(추적 밖) perf.ops op=ui.magnet 응답을 a["magnet"] 에, 끌기 중 캡처 뒤 응답을 rec["dragshot_docN"]["magnet"] 에 남긴다
MAGNET_PROBE = os.environ.get("W10_MAGNET_PROBE", "off").strip().lower() in ("on", "1", "true")
#   W10_PRE_CMDS='[{"cmd":"chrome.moveLight","args":{"inactiveDim":"brush"}}, ...]' : drag_os 에서 문서 수마다(새로고침·문서 열기 뒤, 끌기 전) 원격 명령을 순서대로 보내고 응답을 rec["pre_cmds"] 에 남긴다
PRE_CMDS_RAW = os.environ.get("W10_PRE_CMDS", "").strip()
#   W10_STATIC_GAPS=include : drag 판정값 frame_interval_p95_ms 에 무변화 간격도 넣는다(옛 정의). 미지정·exclude 면 빼고 낸다(사용자 결정 D-39, 판별은 drag_static_gaps.py)
#   두 경우 모두 원래 p95(frame_interval_p95_all_ms)·간격 종류(frame_interval_kinds)·뺀 수(excluded_static_gaps)를 함께 기록한다
STATIC_GAPS = "include" if os.environ.get("W10_STATIC_GAPS", "exclude").strip().lower() == "include" else "exclude"


def parse_pre_cmds(raw):
    """W10_PRE_CMDS 풀이: 빈 문자열이면 [], 아니면 {cmd, args?} 객체 배열(단일 객체도 허용). 형식이 틀리면 ValueError."""
    if not raw:
        return []
    v = json.loads(raw)
    if isinstance(v, dict):
        v = [v]
    if not isinstance(v, list) or not all(isinstance(x, dict) and isinstance(x.get("cmd"), str) and isinstance(x.get("args", {}), dict) for x in v):
        raise ValueError("W10_PRE_CMDS 는 {cmd, args} 객체 배열이어야 한다: %r" % raw[:200])
    return [{"cmd": x["cmd"], "args": x.get("args", {})} for x in v]


PRE_CMDS = parse_pre_cmds(PRE_CMDS_RAW)


def parse_shot_docs(raw):
    """W10_DRAG_SHOT_DOCS 풀이: 빈 문자열이면 (5,), 아니면 1·3·5 중 쉼표 구분 목록. 그 밖의 값은 ValueError."""
    if not raw:
        return (5,)
    try:
        v = tuple(sorted({int(s) for s in raw.split(",") if s.strip()}))
    except ValueError:
        raise ValueError("W10_DRAG_SHOT_DOCS 는 1,3,5 중 쉼표 구분이어야 한다: %r" % raw)
    if not v or any(m not in (1, 3, 5) for m in v):
        raise ValueError("W10_DRAG_SHOT_DOCS 는 1,3,5 중 쉼표 구분이어야 한다: %r" % raw)
    return v


DRAG_SHOT_DOCS = parse_shot_docs(DRAG_SHOT_DOCS_RAW)


def open_seq(order, n, kind=None):
    """문서 n 개를 여는 순서. kind 를 주면 그 종류가 마지막(맨 위)에 오도록 순서만 조정한다."""
    seq = list(order[:n])
    kind = DRAG_KIND if kind is None else kind
    if kind and seq[-1] != kind:
        j = max((i for i, v in enumerate(seq[:-1]) if v == kind), default=None)
        if j is None: seq[-1] = kind
        else: seq[j], seq[-1] = seq[-1], seq[j]
    return seq
sys.path.insert(0, os.path.join(UI, "test/desktop/measure"))
import user_away_guard as G  # noqa: E402
import dc_common  # noqa: E402
import drag_static_gaps as DSG  # noqa: E402
import drag_start_gap as DST  # noqa: E402  A29 시작 구간 값(A15 값 계산과 별개)

BTN = {"grid": (100, 162), "gantt": (100, 196), "form": (100, 230), "sdi_grid": (100, 293), "sdi_gantt": (100, 327), "sdi_form": (100, 361)}
if LAYOUT == "after":
    BTN = {k: (x, y - 43) for k, (x, y) in BTN.items()}
CLOSE0 = (828, 111)  # 첫 MDI 창 닫기 단추(문서 0개에서 연 창). 실측 화면 w4_s1·w4_s3
TITLE0 = (400, 111)  # 첫 MDI 창 제목 줄. 다음 창은 +30,+30 계단


if LAYOUT == "after":
    CLOSE0 = (CLOSE0[0], CLOSE0[1] - 43); TITLE0 = (TITLE0[0], TITLE0[1] - 43)
# 위 좌표는 제목 줄(셸 탭 줄) 높이 19 때 실측이다. 2026-11-11 기본이 38(설정 ShellTabStripHeight 19~48, 공통원칙 §2-1)로 바뀌어 고정값이 19px 어긋났다(otest-w-1).
# 제목 줄 아래에 있는 탐색기 단추·첫 MDI 창은 (실제 줄 높이 − 19)만큼 내려가므로, 첫 fresh() 때 앱에서 줄 높이를 읽어 다시 계산한다(calibrate_strip)
BASE_STRIP_H, DEFAULT_STRIP_H, STRIP_MIN, STRIP_MAX = 19, 38, 19, 48
BASE_BTN, BASE_CLOSE0, BASE_TITLE0 = dict(BTN), CLOSE0, TITLE0
STRIP = {"h": None, "source": None, "dy": None}


def strip_from_settings(raw):
    """W10_SETTINGS(pd-poc-settings JSON 문자열)의 ShellTabStripHeight. 없거나 형식이 틀리면 None. 앱처럼 반올림·19~48 로 자른다."""
    try:
        v = json.loads(raw).get("ShellTabStripHeight") if raw else None
    except (ValueError, AttributeError):
        return None
    if not isinstance(v, (int, float)) or isinstance(v, bool):
        return None
    return float(min(max(round(v), STRIP_MIN), STRIP_MAX))


def apply_strip(h, source):
    """줄 높이 h 로 BTN·CLOSE0·TITLE0 을 기준값에서 다시 계산한다(x 는 그대로, y 만 h − 19)."""
    global BTN, CLOSE0, TITLE0
    dy = round(h - BASE_STRIP_H)
    BTN = {k: (x, y + dy) for k, (x, y) in BASE_BTN.items()}
    CLOSE0, TITLE0 = (BASE_CLOSE0[0], BASE_CLOSE0[1] + dy), (BASE_TITLE0[0], BASE_TITLE0[1] + dy)
    STRIP.update(h=h, source=source, dy=dy)


def calibrate_strip(c):
    """첫 1회: 진단판이면 ui.find shell.titleStrip(높이 PdTitleStripHeight 가 걸린 줄 Grid — 문서 0개여도 줄은 있다)의 실제 높이,
    아니면 W10_SETTINGS 의 설정값, 둘 다 없으면 기본 38. 쓴 값과 출처는 rec["strip"] 에 남는다."""
    if STRIP["source"]:
        return
    r = _unwrap(auto(c, "ui.find", id="shell.titleStrip"))
    h = ((r.get("bounds") or {}) if isinstance(r, dict) else {}).get("h")
    if isinstance(h, (int, float)) and STRIP_MIN - 0.5 <= h <= STRIP_MAX + 0.5:
        return apply_strip(float(h), "ui.find")
    s = strip_from_settings(SETTINGS)
    apply_strip(s if s is not None else float(DEFAULT_STRIP_H), "settings" if s is not None else "default")
    STRIP["ui_find"] = (r.get("error") if isinstance(r, dict) else None) or "높이 없음"


def win(p): return subprocess.check_output(["wslpath", "-w", p], text=True).strip()
def load1(): return float(open("/proc/loadavg").read().split()[0])
def wcpu():
    try:
        o = subprocess.run(["/mnt/c/Windows/System32/WindowsPowerShell/v1.0/powershell.exe", "-NoProfile", "-Command",
                            "(Get-CimInstance Win32_Processor | Measure-Object -Property LoadPercentage -Average).Average"], capture_output=True, text=True, timeout=20)
        return float(o.stdout.strip())
    except Exception:
        return None
def now(): return time.strftime("%Y-%m-%dT%H:%M:%S%z")
def log(*a): print(time.strftime("%H:%M:%S"), *a, flush=True)



def port_listener(port):
    """Windows netstat 로 해당 로컬 포트(서버 쪽)를 쥔 PID 목록(LISTENING·ESTABLISHED, 0 제외). 남의 Chrome 에 붙지 않기 위한 확인."""
    o = subprocess.run(["/mnt/c/Windows/System32/netstat.exe", "-ano", "-p", "tcp"], capture_output=True, text=True, encoding="cp949", errors="replace", timeout=30)
    pids = set()
    for ln in o.stdout.splitlines():
        f = ln.split()
        if len(f) >= 5 and f[0] == "TCP" and f[1].rsplit(":", 1)[-1] == str(port) and f[3].upper() in ("LISTENING", "ESTABLISHED") and f[4].isdigit() and int(f[4]) > 0:
            pids.add(int(f[4]))
    return sorted(pids)


class Chrome:
    def __init__(self, port, prof):
        if port_listener(port): raise SystemExit("포트 %s 에 이미 리스너 — 중단" % port)
        self.p = subprocess.Popen([NODE, win(TOOL), str(port), prof, os.environ.get("W10_WX","-2500"), os.environ.get("W10_WY","40"), "1440", "940"], stdin=subprocess.PIPE, stdout=subprocess.PIPE, text=True, encoding="utf-8", bufsize=1)
        self.prof = prof
        self.hello = json.loads(self.p.stdout.readline())
        if not self.hello.get("ok"): raise RuntimeError(self.hello)
        if self.hello.get("pid") not in port_listener(port): raise SystemExit("포트 리스너가 제 chrome 이 아님 — 중단")
        if SETTINGS: st = "localStorage.setItem('pd-poc-settings',%s)" % json.dumps(SETTINGS)
        elif FAST == "on": st = "localStorage.removeItem('pd-poc-settings')"
        else: st = "localStorage.setItem('pd-poc-settings',JSON.stringify({FastDocumentSwitch:false}))"
        self.q(op="cdp", method="Page.addScriptToEvaluateOnNewDocument", params={"source": "try{localStorage.removeItem('pd-poc-layout');" + st + "}catch(e){}"})
    def q(self, **o):
        self.p.stdin.write(json.dumps(o) + "\n"); self.p.stdin.flush()
        line = self.p.stdout.readline()
        return json.loads(line) if line else {"ok": False, "error": "eof"}
    def close(self):
        try: self.q(op="quit")
        except Exception: pass
        try: self.p.wait(15)
        except Exception: pass
        dc_common.rm_profile(getattr(self, "prof", ""), (getattr(self, "hello", None) or {}).get("pid"))  # 끝난 임시 프로필 정리


def guard():
    if not os.environ.get("PDCHROME_HEADLESS"): G.check()  # 복귀 정황이면 UserReturned 로 즉시 멈춘다(헤드리스는 화면 비사용이라 생략)
    l = load1()
    if l > 50: raise SystemExit("부하 %.1f > 50 — 중단" % l)
    return l


def heap(c):
    r = c.q(op="eval", expr="(()=>{const m=performance.memory;return m?{used:m.usedJSHeapSize,total:m.totalJSHeapSize}:null})()")
    return r.get("value")


def trace_file(out, name): return os.path.join(out, "trace", name + ".json")


def analyze(path, horizon_ms=2400):
    """추적 원자료에서 누름 시각 기준 화면 안정(마지막 DrawAndSwap 끝)·주 스레드 바쁨 끝·GC·상위 이벤트를 뽑는다."""
    ev = json.load(open(path))["traceEvents"]
    mains = {(e["pid"], e["tid"]) for e in ev if e.get("name") == "thread_name" and (e.get("args") or {}).get("name") == "CrRendererMain"}
    press = rel = None
    for e in ev:
        if e.get("name") == "EventLatency" and e.get("ph") == "b":
            t = ((e.get("args") or {}).get("event_latency") or {}).get("event_type")
            if t == "MOUSE_PRESSED" and press is None: press = e["ts"]
            if t == "MOUSE_RELEASED": rel = e["ts"]
    if press is None: return {"error": "no MOUSE_PRESSED"}
    hz = press + horizon_ms * 1000
    sw = sorted((e["ts"], e["ts"] + e.get("dur", 0)) for e in ev if e.get("name") == "Display::DrawAndSwap" and e.get("ph") == "X" and press <= e["ts"] <= hz)
    tasks = [e for e in ev if e.get("ph") == "X" and (e["pid"], e["tid"]) in mains and e.get("name") in ("ThreadControllerImpl::RunTask", "RunTask") and press <= e["ts"] <= hz]
    busy_end = max([e["ts"] + e.get("dur", 0) for e in tasks if e.get("dur", 0) >= 1000] or [press])
    stable = sw[-1][1] if sw else None
    work_end = max([e["ts"] + e.get("dur", 0) for e in tasks if e.get("dur", 0) >= 30000] or [press])
    lo, hi = press, (stable or busy_end)
    gc = [e for e in ev if e.get("ph") == "X" and (e["pid"], e["tid"]) in mains and str(e.get("name", "")).startswith(("MajorGC", "MinorGC", "V8.GC_MC", "V8.GC_SCAVENGER")) and lo <= e["ts"] <= hi]
    by = {}
    for e in ev:
        if e.get("ph") == "X" and (e["pid"], e["tid"]) in mains and lo <= e["ts"] <= hi and e.get("dur", 0) >= 500:
            s = by.setdefault(e["name"], [0, 0.0]); s[0] += 1; s[1] += e["dur"] / 1000
    top = sorted(([k, v[0], round(v[1], 1)] for k, v in by.items()), key=lambda x: -x[2])[:15]
    lt = sorted(round(e.get("dur", 0) / 1000) for e in tasks if e.get("dur", 0) >= 50000)
    return {"click_to_stable_ms": round((stable - press) / 1000, 1) if stable else None, "busy_end_ms": round((busy_end - press) / 1000, 1), "work_end_ms": round((work_end - press) / 1000, 1),
            "frames": len(sw), "frame_times_ms": [round((a - press) / 1000) for a, _ in sw], "press_to_release_ms": round((rel - press) / 1000, 1) if rel else None,
            "gc_ms": round(sum(e.get("dur", 0) for e in gc) / 1000, 1), "gc_n": len(gc), "long_tasks_ms": lt, "main_top": top}


def analyze_drag(path):
    """끌기 구간(누름~놓음) 안의 DrawAndSwap 간격만 프레임으로 센다. 누름 전·놓음 뒤 유휴 간격은 넣지 않는다."""
    ev = json.load(open(path))["traceEvents"]
    press = rel = None; lat = []; opened = {}
    for e in ev:
        if e.get("name") != "EventLatency": continue
        k = (e["pid"], e.get("id") or (e.get("id2") or {}).get("local") or (e.get("id2") or {}).get("global"))
        if e.get("ph") == "b":
            t = ((e.get("args") or {}).get("event_latency") or {}).get("event_type")
            opened[k] = (e["ts"], t)
            if t == "MOUSE_PRESSED" and press is None: press = e["ts"]
            if t == "MOUSE_RELEASED": rel = e["ts"]
        elif e.get("ph") == "e" and k in opened:
            b, t = opened.pop(k); lat.append((t, (e["ts"] - b) / 1000))
    sw = sorted(e["ts"] for e in ev if e.get("name") == "Display::DrawAndSwap" and e.get("ph") == "X" and press is not None and rel is not None and press <= e["ts"] <= rel + 50000)
    fi = [(b - a) / 1000 for a, b in zip(sw, sw[1:])]
    mv = sorted(ms for t, ms in lat if t and "MOVED" in t)
    mains = {(e["pid"], e["tid"]) for e in ev if e.get("name") == "thread_name" and (e.get("args") or {}).get("name") == "CrRendererMain"}
    grp = {"layout": 0.0, "paint_render": 0.0, "script": 0.0, "gc": 0.0, "other_main": 0.0}
    for e in ev:
        if e.get("ph") != "X" or (e["pid"], e["tid"]) not in mains or press is None or rel is None or not (press <= e["ts"] <= rel): continue
        n = str(e.get("name", "")); d = e.get("dur", 0) / 1000
        if n in ("UpdateLayoutTree", "Layout", "PrePaint", "UpdateLifecycles", "IntersectionObserverController::computeIntersections"): grp["layout"] += d
        elif n in ("Paint", "Commit", "Layerize", "UpdateLayer", "CompositeLayers", "RasterTask", "PaintImage") or n.startswith("ProxyMain::BeginMainFrame"): grp["paint_render"] += d
        elif n in ("FunctionCall", "EvaluateScript", "v8.callFunction", "FireAnimationFrame", "TimerFire", "V8.Execute", "EventDispatch"): grp["script"] += d
        elif n.startswith(("MajorGC", "MinorGC", "V8.GC")): grp["gc"] += d
    nfr = max(1, len(sw))
    def pct(a, p):
        if not a: return None
        a = sorted(a); i = min(len(a) - 1, max(0, int(round(p / 100 * (len(a) - 1))))); return round(a[i], 1)
    kinds = DSG.classify(ev, press, rel, sw)  # D-39: 무변화 간격 판별(원래 간격 목록은 frame_intervals_ms 에 그대로 남긴다)
    fj = fi if STATIC_GAPS == "include" else DSG.judged(fi, kinds)
    return {"frames": len(sw), "frame_interval_median_ms": pct(fi, 50), "frame_interval_p95_ms": pct(fj, 95), "frame_interval_p95_all_ms": pct(fi, 95),
            "excluded_static_gaps": len(fi) - len(fj), "frame_interval_kinds": kinds, "static_gap_rule": STATIC_GAPS, "frame_interval_max_ms": round(max(fi), 1) if fi else None,
            "event_latency_moved_median_ms": pct(mv, 50), "event_latency_moved_p95_ms": pct(mv, 95), "drag_window_ms": round((rel - press) / 1000, 1) if press and rel else None, "main_ms_per_frame": {k: round(v / nfr, 2) for k, v in grp.items()},
            "frame_intervals_ms": [round(x, 1) for x in fi], **DST.start_gap(ev, press, rel, sw)}


def fresh(c, settle=3000):
    # 레이아웃이 localStorage(pd-poc-layout)에 남아 새로고침 때 SDI·문서가 복원된다(06:17 실측). 떠날 때 다시 저장하므로
    # 문서 로드 직전에 키를 지우는 스크립트를 Chrome 기동 때 등록해 두었다(Chrome.__init__). 06:20 실측으로 문서 0 확인
    r = c.q(op="nav", url=URL, timeout=90000)
    st = c.q(op="eval", expr="Object.keys(localStorage).join(',')")
    if r.get("value") is not None: r["value"]["ls_after_load"] = st.get("value")
    c.q(op="sleep", ms=settle)
    calibrate_strip(c)  # 첫 회만 원격 조회(측정 구간 밖). 좌표를 쓰는 클릭은 모두 fresh() 뒤에 온다
    return r.get("value", {})


def timed_click(c, out, name, xy, horizon=2400, wait_ms=2600):
    c.q(op="traceStart"); c.q(op="sleep", ms=300)
    t = time.time()
    c.q(op="click", x=xy[0], y=xy[1]); c.q(op="sleep", ms=wait_ms)
    c.q(op="traceEnd", file=win(trace_file(out, name)))
    a = analyze(trace_file(out, name), horizon); a["name"] = name; a["wall_start"] = t
    return a


def phase_disc(c, out, rep, rec):
    fresh(c); c.q(op="shot", file=win(os.path.join(out, "cap_disc.png")))


def phase_disc2(c, out, rep, rec):
    fresh(c)
    for i, op in enumerate(("grid", "gantt", "form")):
        c.q(op="click", x=BTN[op][0], y=BTN[op][1]); c.q(op="sleep", ms=1500)
        c.q(op="shot", file=win(os.path.join(out, "cap_disc2_%d.png" % (i + 1))))


def phase_open(c, out, rep, rec):
    fresh(c)
    for op in ("grid", "form", "gantt"):
        for i in range(rep):
            f0 = fg(c); time.sleep(0.5); l = guard()  # w-15: 전경 읽기 PowerShell 종료 뒤 0.5초 쉬고 load1 을 읽는다(team-lead 조건)
            a = timed_click(c, out, "open_%s_%02d" % (op, i), BTN[op]); a["load1"] = l; a["fg"] = f0
            rec.setdefault("open_" + op, []).append(a); log("open", op, i, a.get("click_to_stable_ms"), a.get("busy_end_ms"), "load", l)
            if op == "grid" and i == 0: c.q(op="shot", file=win(os.path.join(out, "cap_open_grid.png")))
            c.q(op="click", x=CLOSE0[0], y=CLOSE0[1]); c.q(op="sleep", ms=900)
    c.q(op="shot", file=win(os.path.join(out, "cap_after_open_closed.png")))


def phase_sdi(c, out, rep, rec):
    for i in range(rep):
        fresh(c); l = guard()
        a = timed_click(c, out, "sdi_grid_%02d" % i, BTN["sdi_grid"], 2800, 3000); a["load1"] = l
        rec.setdefault("sdi_grid", []).append(a); log("sdi", i, a.get("click_to_stable_ms"), a.get("busy_end_ms"), a.get("gc_ms"))
        if i == 0: c.q(op="shot", file=win(os.path.join(out, "cap_sdi.png")))


def phase_tab(c, out, rep, rec):
    fresh(c)
    for op in ("grid", "gantt", "form"):
        c.q(op="click", x=BTN[op][0], y=BTN[op][1]); c.q(op="sleep", ms=1500)
    c.q(op="shot", file=win(os.path.join(out, "cap_tabs.png")))
    rec["heap_tab_before"] = heap(c)
    tabs = [(28, 45), (100, 45)]  # 제목 줄 탭 '그리드'·'간트'(w4_s3 실측)
    for i in range(rep):
        l = guard()
        a = timed_click(c, out, "tab_%02d" % i, tabs[i % 2], 1500, 1700); a["load1"] = l
        rec.setdefault("tab_switch", []).append(a); log("tab", i, a.get("click_to_stable_ms"))
    rec["heap_tab_after"] = heap(c)


MODE_DOCK = (451, 16)  # 제목 줄 '태블릿형' 단추(w4_s0.png 실측). '자동' 은 (354,16)
DOCK_COORDS = os.path.join(os.path.dirname(os.path.abspath(__file__)), "w4_dock_coords.json")


def phase_tab_dock(c, out, rep, rec):
    """PC 웹을 태블릿형(DOCK) 으로 바꿔 탭 전환을 잰다(Fold5 504ms 분석 (b) — 모드 효과와 기기 CPU 효과 분리, team-lead 채택).
    DOCK 배치의 탐색기·탭 좌표는 미실측이라 2단계로 한다: w4_dock_coords.json 이 없으면 배치 캡처만 남기고 끝낸다(발견 모드).
    좌표 파일 형식: {"open": [[x,y]x3 — grid·gantt·form 여는 단추], "tabs": [[x,y],[x,y]] — 전환할 두 탭}"""
    fresh(c)
    c.q(op="click", x=MODE_DOCK[0], y=MODE_DOCK[1]); c.q(op="sleep", ms=2000)
    c.q(op="shot", file=win(os.path.join(out, "cap_dock_mode.png")))
    if not os.path.exists(DOCK_COORDS):
        rec["tab_dock_discovery"] = "좌표 파일 없음 — cap_dock_mode.png 를 판독해 %s 를 만든 뒤 다시 실행" % DOCK_COORDS
        log("tab_dock 발견 모드: 캡처만 남김")
        return
    cd = json.load(open(DOCK_COORDS)); rec["tab_dock_coords"] = cd; rec["heap_dock_start"] = heap(c)
    for x, y in cd["open"]:
        c.q(op="click", x=x, y=y); c.q(op="sleep", ms=1500)
    c.q(op="sleep", ms=1000)
    c.q(op="shot", file=win(os.path.join(out, "cap_dock_tabs.png")))
    tabs = cd["tabs"]
    for i in range(rep):
        l = guard()
        a = timed_click(c, out, "tabdock_%02d" % i, tabs[i % 2], 1500, 1700); a["load1"] = l
        rec.setdefault("tab_switch_dock", []).append(a); log("tabdock", i, a.get("click_to_stable_ms"), a.get("long_tasks_ms"))
    rec["heap_dock_after"] = heap(c)
    c.q(op="shot", file=win(os.path.join(out, "cap_dock_tabs_after.png")))


def phase_drag(c, out, runs, rec):
    order = ("grid", "gantt", "form", "grid", "gantt")
    for n in (1, 3, 5):
        fresh(c)
        for k in range(n):
            c.q(op="click", x=BTN[order[k]][0], y=BTN[order[k]][1]); c.q(op="sleep", ms=1500)
        c.q(op="sleep", ms=1500)
        c.q(op="shot", file=win(os.path.join(out, "cap_drag_doc%d_before.png" % n)))
        x, y = TITLE0[0] + 30 * (n - 1), TITLE0[1] + 30 * (n - 1)
        for i in range(runs):
            l = guard()
            dx = 250 if i % 2 == 0 else -250
            nm = "drag_doc%d_%02d" % (n, i)
            c.q(op="traceStart"); c.q(op="sleep", ms=300)
            r = c.q(op="drag", x=x, y=y, dx=dx, dy=0, steps=60, interval=16)
            c.q(op="sleep", ms=600); c.q(op="traceEnd", file=win(trace_file(out, nm)))
            a = analyze_drag(trace_file(out, nm)); a["name"] = nm; a["load1"] = l; a["dispatch_wall_ms"] = round((r.get("value") or {}).get("wall", 0))
            rec.setdefault("drag_doc%d" % n, []).append(a); log("drag", n, i, a["frame_interval_median_ms"], a["frame_interval_p95_ms"], a["event_latency_moved_p95_ms"])
            x += dx
        c.q(op="shot", file=win(os.path.join(out, "cap_drag_doc%d_after.png" % n)))


PS = "/mnt/c/Windows/System32/WindowsPowerShell/v1.0/powershell.exe"
APP_PS = subprocess.run(["wslpath", "-w", os.path.join(UI, "test/common/measure/w4_app.ps1")], capture_output=True, text=True).stdout.strip()


def chrome_hwnd(c):
    o = subprocess.run([PS, "-NoProfile", "-Command", "(Get-Process -Id %d).MainWindowHandle" % c.hello["pid"]], capture_output=True, text=True, timeout=30).stdout.strip()
    return int(o.split()[-1])


_FG_PS = ("Add-Type 'using System;using System.Runtime.InteropServices;public class W15FG{[DllImport(\"user32.dll\")]public static extern IntPtr GetForegroundWindow();"
          "[DllImport(\"user32.dll\")]public static extern uint GetWindowThreadProcessId(IntPtr h,out uint p);}';"
          "$h=[W15FG]::GetForegroundWindow();$p=0;[void][W15FG]::GetWindowThreadProcessId($h,[ref]$p);\"$([int64]$h) $p\"")


def fg(c):
    """w-15 보강: 시행 직전 전경 창(hwnd·pid)을 읽어 Chrome 실창이 맨 위인지 기록한다(읽기 전용, 입력 없음, 측정 구간 밖)."""
    try:
        o = subprocess.run([PS, "-NoProfile", "-Command", _FG_PS], capture_output=True, text=True, timeout=30).stdout.strip().split()
        h, p = int(o[-2]), int(o[-1])
        return {"hwnd": h, "pid": p, "is_chrome": p == c.hello.get("pid"), "t": now()}
    except Exception as e:  # noqa: BLE001
        return {"error": repr(e)[:120], "is_chrome": None, "t": now()}


def shot_drag_os(c, out, n, hw, ox, oy, x, y, rec):
    """W10_DRAG_SHOT=on: 시간 측정 회차가 끝난 뒤 같은 SendInput 끌기를 1회 더 하면서 누름 뒤 약 300ms(끌기 중간)에 화면을 1장 캡처한다.
    캡처 부하가 프레임을 흐리므로 이 끌기는 판정 원자료(dragos_doc{n})에 넣지 않고 rec["dragshot_doc{n}"] 에만 남긴다."""
    dx = 250 if (x - TITLE0[0] - 30 * (n - 1)) <= 0 else -250
    sx, sy = ox + x, oy + y
    f = os.path.join(out, "cap_dragos_doc%d_during.png" % n)
    G.record_injection("w4_web_dragshot_pre")
    p = subprocess.Popen([PS, "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", APP_PS, "-Mode", "drag", "-Hwnd", str(hw), "-X", str(sx), "-Y", str(sy),
                          "-Dx", str(dx), "-Steps", "75", "-Interval", "8", "-RX", str(sx), "-RY", str(sy - 40), "-RW", "8", "-RH", "8", "-Reps", "1"],
                         stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
    # PowerShell 기동 지연이 일정하지 않아 활성 창 mdiBounds.x 가 처음 바뀐 때(끌기 시작)를 원격으로 기다린 뒤 약 150ms 더 두고 찍는다
    b_start = _bounds(auto(c, "chrome.state")); t0 = time.time(); seen = False
    while time.time() - t0 < 10 and p.poll() is None:
        b = _bounds(auto(c, "chrome.state"))
        if b and b_start and abs(b["x"] - b_start["x"]) >= 20: seen = True; break
    if seen: time.sleep(0.15)
    t_shot = time.time() - t0
    b_shot = b if seen else None; w_shot0 = now()
    c.q(op="shot", file=win(f))
    t_shot_end = time.time() - t0; ended_at_shot_end = p.poll() is not None
    b_after = _bounds(auto(c, "chrome.state"))  # 캡처 직후 위치 — 감지 때와 다르면 캡처 동안에도 이동 중이었다
    p.wait(60); G.record_injection("w4_web_dragshot", sx + dx, sy)
    rec["dragshot_doc%d" % n] = {"file": f, "dx": dx, "shot_after_start_s": round(t_shot, 2), "moving_seen": seen, "drag_ended_before_shot": p.returncode is not None and not seen, "note": "판정 원자료 아님",
                                 "shot_wall_start": w_shot0, "shot_end_after_start_s": round(t_shot_end, 2), "drag_proc_ended_at_shot_end": ended_at_shot_end,
                                 "bounds_start": b_start, "bounds_at_detect": b_shot, "bounds_after_shot": b_after}
    c.q(op="sleep", ms=600)
    if MAGNET_PROBE:
        rec["dragshot_doc%d" % n]["magnet"] = {"t": now(), "resp": auto(c, "perf.ops", op="ui.magnet")}
    return x + dx


def auto(c, cmd, **args):
    """진단 빌드의 window.pdAutomation 원격 명령 1건(drag_os 진단 인자 전용). 없거나 실패하면 {"error": ...}."""
    expr = ("(async()=>{try{if(!globalThis.pdAutomation)return {error:'pdAutomation 없음'};"
            "const r=await globalThis.pdAutomation.invoke(%s);try{return typeof r==='string'?JSON.parse(r):r}catch(e){return {raw:String(r)}}}"
            "catch(e){return {error:String(e)}}})()") % json.dumps(json.dumps({"cmd": cmd, "args": args}))
    v = c.q(op="eval", expr=expr).get("value")
    return v if isinstance(v, dict) else {"error": "응답 형식 %r" % (v,)}


def _bounds(r):
    """원격 응답에서 mdiBounds(x,y,w,h)를 찾는다(응답이 {ok,result} 로 감싸였을 수도 있다)."""
    for o in (r, r.get("result") if isinstance(r.get("result"), dict) else None, r.get("value") if isinstance(r.get("value"), dict) else None):
        if isinstance(o, dict) and isinstance(o.get("mdiBounds"), dict):
            return o["mdiBounds"]
    return None


def _unwrap(r):
    return r["result"] if isinstance(r, dict) and isinstance(r.get("result"), dict) else r


def drag_probe(c, before):
    """W10_DRAG_PROBE=on 전용(추적 밖): chrome.moveLight 조회(인자 없음 = 설정 안 바꿈)로 staticCacheAppliedTotal/Active 를 읽고,
    끌기 전(before)이면 perf.reset 으로 앱 프레임 표본을 비우고, 끌기 뒤면 perf.get 의 layout_pass_*·frame_* 를 읽는다. 창 가시 상태도 함께 남긴다."""
    ml = _unwrap(auto(c, "chrome.moveLight"))
    keep = ("enabled", "staticCache", "staticCacheAppliedTotal", "staticCacheActive", "renderDirtyRegions", "bitmapCache", "siblings", "inactiveDim", "error")
    d = {"t": now(), "moveLight": {k: ml.get(k) for k in keep if k in ml}}
    d["vis"] = c.q(op="eval", expr="({vis:document.visibilityState,focus:document.hasFocus()})").get("value")
    if before:
        d["perf_reset"] = auto(c, "perf.reset").get("error") or "ok"
    else:
        pg = _unwrap(auto(c, "perf.get"))
        d["perf"] = {k: v for k, v in pg.items() if k.startswith("layout_pass") or k.startswith("frame_") or k == "error"}
    return d


def tile_grab(c, b0, rec, n):
    """W10_DRAG_LAYOUT=tile: 바둑판(tile-v) 배치 후 활성 문서 제목줄의 페이지 좌표를 돌려준다. 실패면 None.
    MDI 영역 원점은 계단 첫 창으로 구한다 — y 는 TITLE0.y−첫창 y, x 는 (CLOSE0.x+12)−첫창 폭−첫창 x(닫기 단추 오른쪽 여백 12 추정).
    잡는 점은 활성 창 왼쪽에서 폭의 40%, 제목줄 높이는 계단 때와 같다."""
    arr = auto(c, "docs.arrange", mode="tile-v"); c.q(op="sleep", ms=1500)
    st = auto(c, "chrome.state")
    info = {"b0": b0, "arrange_error": arr.get("error"), "active": st}
    rec.setdefault("drag_os_tile", {})["doc%d" % n] = info
    p0, p = _bounds(b0 or {}), _bounds(st)
    if not p0 or not p or arr.get("error"):
        info["skip"] = "배치 또는 위치 조회 실패"; return None
    x, y = _tile_xy(p0, p)
    info["grab_page_xy"] = [x, y]
    return x, y


def _tile_xy(p0, p):
    ax = CLOSE0[0] + 12 - p0["w"] - p0["x"]
    return round(ax + p["x"] + p["w"] * 0.4), round(TITLE0[1] - p0["y"] + p["y"])


def tile_regrab(c, b0, rec, n, i, x, y):
    """tile 모드 회차마다: 좁은 창은 ±250px 끌기 뒤 가장자리·흡착에 걸려 dx 만큼 따라오지 않으므로 활성 창 위치를 다시 읽어 잡는 점을 정한다."""
    st = auto(c, "chrome.state"); p0, p = _bounds(b0 or {}), _bounds(st)
    xy = _tile_xy(p0, p) if p0 and p else (x, y)
    rec.setdefault("drag_os_tile", {}).setdefault("doc%d" % n, {}).setdefault("regrab", []).append({"run": i, "bounds": p, "grab_page_xy": list(xy)})
    return xy


def phase_drag_os(c, out, runs, rec):
    """OS 실입력 끌기: SendInput 으로 Chrome 실창에 마우스를 8ms 주기(125Hz)로 움직이고 프레임은 Chrome 추적으로 잰다.
    CDP 끌기는 입력 1건마다 렌더러 처리 응답을 기다려 입력 주기가 약 27ms 로 묶이므로(06:25 실측) 프레임 간격이 입력 주기 아래로 내려갈 수 없다."""
    order = ("grid", "gantt", "form", "grid", "gantt")
    if DOC_KIND == "form":
        order = ("form",) * 5
    hw = chrome_hwnd(c)
    rec["drag_os_method"] = {"input": "SendInput absolute move, 75단계·8ms, 250px 왕복", "hwnd": hw}
    if DRAG_LAYOUT != "cascade" or DOC_KIND != "mixed" or DRAG_KIND:  # 진단 인자를 준 회차만 기록(미지정이면 출력 불변)
        rec["drag_os_options"] = {"layout": DRAG_LAYOUT, "doc_kind": DOC_KIND, "drag_kind": DRAG_KIND or None,
                                  "open_order": {"doc%d" % m: open_seq(order, m) for m in (1, 3, 5)}, "diagnostic_only": True}
    if DRAG_PROBE:
        rec.setdefault("drag_os_options", {"diagnostic_only": True})["probe"] = True
    if PRE_CMDS:
        rec.setdefault("drag_os_options", {"diagnostic_only": True})["pre_cmds"] = PRE_CMDS
    if DRAG_SHOT_DOCS_RAW:
        rec.setdefault("drag_os_options", {"diagnostic_only": True})["drag_shot_docs"] = list(DRAG_SHOT_DOCS)
    if MAGNET_PROBE:
        rec.setdefault("drag_os_options", {"diagnostic_only": True})["magnet_probe"] = True
    for n in (1, 3, 5):
        fresh(c)
        b0 = None
        seq = open_seq(order, n)
        for k in range(n):
            c.q(op="click", x=BTN[seq[k]][0], y=BTN[seq[k]][1]); c.q(op="sleep", ms=1500)
            if DRAG_LAYOUT == "tile" and k == 0:
                b0 = auto(c, "chrome.state")  # 계단 첫 창 MDI 위치 — TITLE0(페이지 좌표)와의 차로 MDI 영역 원점을 구한다
        c.q(op="sleep", ms=1500)
        g = c.q(op="eval", expr="({sx:screenX,sy:screenY,ow:outerWidth,oh:outerHeight,iw:innerWidth,ih:innerHeight,dpr:devicePixelRatio})")["value"]
        bx = (g["ow"] - g["iw"]) // 2
        ox, oy = g["sx"] + bx, g["sy"] + g["oh"] - g["ih"] - bx
        rec.setdefault("drag_os_geom", {})["doc%d" % n] = dict(g, ox=ox, oy=oy)
        x, y = TITLE0[0] + 30 * (n - 1), TITLE0[1] + 30 * (n - 1)
        if DRAG_LAYOUT == "tile":
            xy = tile_grab(c, b0, rec, n)
            if xy is None:
                log("drag_os tile 배치 실패 — doc%d 건너뜀" % n); continue
            x, y = xy
        if PRE_CMDS:  # 새로고침마다 실행 상태가 초기화되므로 문서 수마다 다시 보낸다(끌기 전, 추적 밖)
            rec.setdefault("pre_cmds", {})["doc%d" % n] = [{"cmd": pc["cmd"], "args": pc["args"], "resp": auto(c, pc["cmd"], **pc["args"]), "t": now()} for pc in PRE_CMDS]
            c.q(op="sleep", ms=500)
            if os.environ.get("W10_PRE_STRICT", "off").strip().lower() in ("on", "1", "true"):  # 진단 명령이 하나라도 실패하면 끌기 전에 중단(적용 실패 패스를 판정에 섞지 않는다)
                bad = [x for x in rec["pre_cmds"]["doc%d" % n] if not (x["resp"] or {}).get("ok")]
                if bad: raise SystemExit("PRE_CMDS 적용 실패 doc%d: %s" % (n, [(x["cmd"], (x["resp"] or {}).get("error")) for x in bad]))
        c.q(op="shot", file=win(os.path.join(out, "cap_dragos_doc%d_before.png" % n)))
        for i in range(runs):
            if DRAG_LAYOUT == "tile" and i > 0:
                x, y = tile_regrab(c, b0, rec, n, i, x, y)
            pb = drag_probe(c, True) if DRAG_PROBE else None
            f0 = fg(c); time.sleep(0.5); l = guard()  # w-15: 전경 읽기 PowerShell 종료 뒤 0.5초 쉬고 load1 을 읽는다(team-lead 조건)
            dx = 250 if i % 2 == 0 else -250
            nm = "dragos_doc%d_%02d" % (n, i)
            c.q(op="traceStart"); c.q(op="sleep", ms=300)
            sx, sy = ox + x, oy + y
            G.record_injection("w4_web_dragos_pre")
            o = subprocess.run([PS, "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", APP_PS, "-Mode", "drag", "-Hwnd", str(hw), "-X", str(sx), "-Y", str(sy),
                                "-Dx", str(dx), "-Steps", "75", "-Interval", "8", "-RX", str(sx), "-RY", str(sy - 40), "-RW", "8", "-RH", "8", "-Reps", "1"],
                               capture_output=True, text=True, timeout=60)
            G.record_injection("w4_web_dragos", sx + dx, sy)
            c.q(op="sleep", ms=600); c.q(op="traceEnd", file=win(trace_file(out, nm)))
            a = analyze_drag(trace_file(out, nm)); a["name"] = nm; a["load1"] = l; a["fg"] = f0
            if DRAG_PROBE: a["probe"] = {"before": pb, "after": drag_probe(c, False)}
            if MAGNET_PROBE: a["magnet"] = {"t": now(), "resp": auto(c, "perf.ops", op="ui.magnet")}
            try: a["ps"] = json.loads(o.stdout.strip().splitlines()[-1])["results"][0]; a["ps"].pop("changes", None)
            except Exception: a["ps_err"] = (o.stdout[-200:], o.stderr[-200:])
            rec.setdefault("dragos_doc%d" % n, []).append(a); log("dragos", n, i, a["frame_interval_median_ms"], a["frame_interval_p95_ms"], a["event_latency_moved_p95_ms"], a["frames"])
            x += dx
        if CPU_PROFILE:
            x = prof_drag_os(c, out, n, hw, ox, oy, x, y, rec)
        if DRAG_SHOT and n in DRAG_SHOT_DOCS:
            x = shot_drag_os(c, out, n, hw, ox, oy, x, y, rec)
        c.q(op="shot", file=win(os.path.join(out, "cap_dragos_doc%d_after.png" % n)))


def summarize_profile(prof, top_n=30):
    """CDP Profiler 결과에서 함수별 자기 시간(ms) 상위와 갈래별 합(wasm·js·gc·program·idle)을 뽑는다. 표본 시간은 다음 표본까지의 간격으로 본다(phase_prof 와 같은 셈)."""
    nodes = {n["id"]: n for n in prof.get("nodes", [])}
    dt = prof.get("timeDeltas", []); smp = prof.get("samples", [])
    self_t, kinds, urls = {}, {"wasm": 0.0, "js": 0.0, "gc": 0.0, "program": 0.0, "idle": 0.0}, {}
    for s, d in zip(smp, dt[1:] + [0]):
        cf = (nodes.get(s) or {}).get("callFrame", {})
        fn = cf.get("functionName") or "(anon)"; url = cf.get("url", "").split("/")[-1]
        ms = d / 1000
        k = fn + " @" + url; self_t[k] = self_t.get(k, 0) + ms
        if fn == "(garbage collector)": kind = "gc"
        elif fn == "(program)": kind = "program"
        elif fn == "(idle)": kind = "idle"
        elif url.endswith(".wasm") or fn.startswith(("$", "wasm-function")): kind = "wasm"
        else: kind = "js"
        kinds[kind] += ms
        if url: urls[url] = urls.get(url, 0) + ms
    top = sorted(self_t.items(), key=lambda x: -x[1])[:top_n]
    busy = sum(v for k, v in kinds.items() if k != "idle")
    return {"top_self_ms": [[k, round(v, 1)] for k, v in top], "total_ms": round(sum(self_t.values()), 1), "busy_ms": round(busy, 1),
            "by_kind_ms": {k: round(v, 1) for k, v in kinds.items()}, "by_url_ms": sorted(([u, round(v, 1)] for u, v in urls.items()), key=lambda x: -x[1])[:10]}


def prof_drag_os(c, out, n, hw, ox, oy, x, y, rec):
    """W10_CPU_PROFILE=on 일 때만 부른다. 시간 측정 회차와 같은 SendInput 끌기(75단계·8ms·250px 왕복)를 CPU 프로파일을 켠 채 CPU_PROFILE_RUNS 회 돌린다.
    추적(trace)은 켜지 않는다. 결과는 rec["prof_dragos_doc{n}"] 에만 넣는다. 끝난 x 를 돌려준다."""
    for j in range(CPU_PROFILE_RUNS):
        l = guard()
        dx = 250 if j % 2 == 0 else -250
        sx, sy = ox + x, oy + y
        c.q(op="cdp", method="Profiler.enable"); c.q(op="cdp", method="Profiler.setSamplingInterval", params={"interval": 200})
        c.q(op="cdp", method="Profiler.start"); c.q(op="sleep", ms=300)
        G.record_injection("w4_web_dragos_prof_pre")
        o = subprocess.run([PS, "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", APP_PS, "-Mode", "drag", "-Hwnd", str(hw), "-X", str(sx), "-Y", str(sy),
                            "-Dx", str(dx), "-Steps", "75", "-Interval", "8", "-RX", str(sx), "-RY", str(sy - 40), "-RW", "8", "-RH", "8", "-Reps", "1"],
                           capture_output=True, text=True, timeout=60)
        G.record_injection("w4_web_dragos_prof", sx + dx, sy)
        c.q(op="sleep", ms=300)
        prof = (c.q(op="cdp", method="Profiler.stop").get("value") or {}).get("profile") or {}
        path = os.path.join(out, "prof", "dragos_doc%d_%02d.cpuprofile" % (n, j))
        json.dump(prof, open(path, "w"))
        s = summarize_profile(prof); s.update(file=path, dx=dx, load1=l, ps_rc=o.returncode)
        rec.setdefault("prof_dragos_doc%d" % n, []).append(s); log("prof_dragos", n, j, s["by_kind_ms"], s["top_self_ms"][:3])
        x += dx
    return x


def phase_prof(c, out, rep, rec):
    """SDI·grid 열기 CPU 프로파일(시간 측정 회차와 분리). wasm 함수 이름이 남아 있으면 상위 자기 시간을 뽑는다."""
    for kind, btn in (("sdi_grid", BTN["sdi_grid"]), ("open_grid", BTN["grid"])):
        for i in range(2):
            fresh(c); guard()
            c.q(op="cdp", method="Profiler.enable"); c.q(op="cdp", method="Profiler.setSamplingInterval", params={"interval": 200})
            c.q(op="cdp", method="Profiler.start"); c.q(op="click", x=btn[0], y=btn[1]); c.q(op="sleep", ms=2500)
            r = c.q(op="cdp", method="Profiler.stop")
            prof = (r.get("value") or {}).get("profile") or {}
            json.dump(prof, open(os.path.join(out, "prof", "%s_%d.cpuprofile" % (kind, i)), "w"))
            nodes = {n["id"]: n for n in prof.get("nodes", [])}
            dt = prof.get("timeDeltas", []); smp = prof.get("samples", [])
            self_t = {}
            for s, d in zip(smp, dt[1:] + [0]):
                n = nodes.get(s); fn = (n or {}).get("callFrame", {}).get("functionName") or "(anon)"
                url = (n or {}).get("callFrame", {}).get("url", "").split("/")[-1]
                k = fn + " @" + url; self_t[k] = self_t.get(k, 0) + d / 1000
            top = sorted(self_t.items(), key=lambda x: -x[1])[:30]
            rec.setdefault("prof_" + kind, []).append({"top_self_ms": [[k, round(v, 1)] for k, v in top], "total_ms": round(sum(self_t.values()), 1)})
            log("prof", kind, i, top[:5])


def phase_startup(c, out, rep, rec):
    """시작 구간: pd-startup-options httpCache direct/revalidate 각 rep회 새로고침, window.__pdStartup marks·measures 기록"""
    expr = "(()=>{const s=window.__pdStartup;return s?{done:s.done,marks:s.marks,measures:s.measures}:null})()"
    for mode in ("direct", "revalidate"):
        c.q(op="cdp", method="Page.addScriptToEvaluateOnNewDocument", params={"source": "try{localStorage.setItem('pd-startup-options',JSON.stringify({timing:true,httpCache:'%s',integrity:true}))}catch(e){}" % mode})
        for i in range(rep + 1):
            l = guard(); fresh(c, 6000)
            v = c.q(op="eval", expr=expr).get("value")
            rec.setdefault("startup_" + mode, []).append({"i": i, "warmup": i == 0, "load1": l, "startup": v}); log("startup", mode, i, (v or {}).get("done"))
    c.q(op="cdp", method="Page.addScriptToEvaluateOnNewDocument", params={"source": "try{localStorage.removeItem('pd-startup-options')}catch(e){}"})


def phase_heap(c, out, rep, rec):
    """문서를 3·6·9개로 늘리며 JS 힙 기록"""
    fresh(c); rec["heap_docs_0"] = heap(c)
    order = ("grid", "gantt", "form")
    for n in range(1, 10):
        guard(); b = BTN[order[(n - 1) % 3]]
        c.q(op="click", x=b[0], y=b[1]); c.q(op="sleep", ms=1500)
        if n in (3, 6, 9):
            c.q(op="sleep", ms=1500); rec["heap_docs_%d" % n] = heap(c); log("heap", n, rec["heap_docs_%d" % n])
    c.q(op="shot", file=win(os.path.join(out, "cap_heap_docs9.png")))


def main():
    out, ph = sys.argv[1], sys.argv[2]
    rep = int(sys.argv[3]) if len(sys.argv) > 3 else 12
    for d in ("trace", "prof"): os.makedirs(os.path.join(out, d), exist_ok=True)
    rf = os.path.join(out, "web_%s.json" % ph)
    rec = {"phase": ph, "url": URL, "fast_document_switch": FAST, "started_at": now(), "load_start": load1(), "wcpu_start": wcpu()}
    c = Chrome(CPORT, r"C:\Users\rioky\AppData\Local\Temp\pdwebperf\w10_" + TAG)
    rec["chrome"] = c.hello
    try:
        # all: 판정용 끌기는 drag_os(SendInput 125Hz, team-lead 결정), CDP drag 는 참고. tab_dock 은 좌표 파일이 있어야 측정(없으면 캡처만)
        phases = ["disc", "open", "sdi", "tab", "tab_dock", "drag_os", "drag", "prof"] if ph == "all" else ph.split(",")
        for p in phases:
            {"open": phase_open, "sdi": phase_sdi, "tab": phase_tab, "disc": phase_disc, "disc2": phase_disc2, "drag": phase_drag, "drag_os": phase_drag_os, "tab_dock": phase_tab_dock, "prof": phase_prof, "startup": phase_startup, "heap": phase_heap}[p](c, out, rep if p not in ("drag_os",) else max(3, rep // 3), rec)
            rec["errors_" + p] = (c.q(op="errors").get("value") or [])[:20]
            rec["strip"] = dict(STRIP, base_h=BASE_STRIP_H, btn=BTN, title0=TITLE0, close0=CLOSE0)
            json.dump(rec, open(rf, "w"), ensure_ascii=False, indent=1)
    except (G.UserReturned, G.GuardUnavailable) as e:
        rec["stopped"] = "guard: %r" % (e,); log("STOP", e)
    except SystemExit as e:
        rec["stopped"] = str(e); log("STOP", e)
    finally:
        rec["finished_at"] = now(); rec["load_end"] = load1(); rec["wcpu_end"] = wcpu()
        json.dump(rec, open(rf, "w"), ensure_ascii=False, indent=1)
        c.close(); log("END", rf)


def self_test():
    """앱·Chrome 없이 확인: 프로파일 옵션 기본 끔, 자기 시간·갈래 합 셈, 판정 원자료 키와 분리."""
    global BTN, CLOSE0, TITLE0  # 줄 높이 보정 시험 뒤 원래 값으로 되돌린다
    fails = []
    if os.environ.get("W10_CPU_PROFILE") is None and CPU_PROFILE:
        fails.append("W10_CPU_PROFILE 미지정인데 켜짐")
    prof = {"nodes": [{"id": 1, "callFrame": {"functionName": "(idle)", "url": ""}},
                      {"id": 2, "callFrame": {"functionName": "$mono_interp_exec", "url": "http://x/_framework/dotnet.native.wasm"}},
                      {"id": 3, "callFrame": {"functionName": "drawElements", "url": "http://x/_framework/dotnet.js"}},
                      {"id": 4, "callFrame": {"functionName": "(garbage collector)", "url": ""}}],
            "samples": [1, 2, 2, 3, 4, 2], "timeDeltas": [0, 1000, 2000, 500, 300, 700]}
    s = summarize_profile(prof)
    want = {"idle": 1.0, "wasm": 2.0 + 0.5 + 0.0, "js": 0.3, "gc": 0.7, "program": 0.0}
    if s["by_kind_ms"] != {k: round(v, 1) for k, v in want.items()}:
        fails.append("갈래 합 %s != %s" % (s["by_kind_ms"], want))
    if s["busy_ms"] != 3.5 or s["total_ms"] != 4.5:
        fails.append("busy/total %s/%s" % (s["busy_ms"], s["total_ms"]))
    if s["top_self_ms"][0] != ["$mono_interp_exec @dotnet.native.wasm", 2.5]:
        fails.append("상위 함수 %s" % s["top_self_ms"][:1])
    if summarize_profile({})["total_ms"] != 0:
        fails.append("빈 프로파일 처리")
    if os.environ.get("W10_DRAG_LAYOUT") is None and DRAG_LAYOUT != "cascade":
        fails.append("W10_DRAG_LAYOUT 미지정인데 %s" % DRAG_LAYOUT)
    if os.environ.get("W10_DOC_KIND") is None and DOC_KIND != "mixed":
        fails.append("W10_DOC_KIND 미지정인데 %s" % DOC_KIND)
    mb = {"x": 1, "y": 2, "w": 3, "h": 4}
    if _bounds({"ok": True, "result": {"mdiBounds": mb}}) != mb or _bounds({"ok": False, "error": "x"}) is not None:
        fails.append("mdiBounds 풀이")
    if os.environ.get("W10_DRAG_SHOT") is None and DRAG_SHOT:
        fails.append("W10_DRAG_SHOT 미지정인데 켜짐")
    if os.environ.get("W10_DRAG_PROBE") is None and DRAG_PROBE:
        fails.append("W10_DRAG_PROBE 미지정인데 켜짐")
    if _unwrap({"ok": True, "result": {"a": 1}}) != {"a": 1} or _unwrap({"a": 2}) != {"a": 2}:
        fails.append("_unwrap 풀이")
    if os.environ.get("W10_PRE_CMDS") is None and PRE_CMDS:
        fails.append("W10_PRE_CMDS 미지정인데 명령 있음")
    want_pc = [{"cmd": "chrome.moveLight", "args": {"inactiveDim": "brush"}}, {"cmd": "render.overlay", "args": {}}]
    if parse_pre_cmds('[{"cmd":"chrome.moveLight","args":{"inactiveDim":"brush"}},{"cmd":"render.overlay"}]') != want_pc \
            or parse_pre_cmds('{"cmd":"render.overlay"}') != want_pc[1:] or parse_pre_cmds("") != []:
        fails.append("parse_pre_cmds 풀이")
    for bad in ('[{"args":{}}]', '"x"', '[{"cmd":"a","args":1}]'):
        try:
            parse_pre_cmds(bad); fails.append("parse_pre_cmds 잘못된 형식 통과 %s" % bad)
        except ValueError:
            pass
    if os.environ.get("W10_DRAG_SHOT_DOCS") is None and DRAG_SHOT_DOCS != (5,):
        fails.append("W10_DRAG_SHOT_DOCS 미지정인데 %s" % (DRAG_SHOT_DOCS,))
    if parse_shot_docs("") != (5,) or parse_shot_docs("1") != (1,) or parse_shot_docs("5,1") != (1, 5):
        fails.append("parse_shot_docs 풀이")
    for bad in ("2", "x", ","):
        try:
            parse_shot_docs(bad); fails.append("parse_shot_docs 잘못된 형식 통과 %s" % bad)
        except ValueError:
            pass
    if os.environ.get("W10_MAGNET_PROBE") is None and MAGNET_PROBE:
        fails.append("W10_MAGNET_PROBE 미지정인데 켜짐")
    o5 = ("grid", "gantt", "form", "grid", "gantt")
    if os.environ.get("W10_DRAG_KIND") is None and (DRAG_KIND or open_seq(o5, 5) != list(o5) or open_seq(o5, 1) != ["grid"]):
        fails.append("W10_DRAG_KIND 미지정인데 순서 바뀜")
    want_seq = {("grid", 5): ["grid", "gantt", "form", "gantt", "grid"], ("form", 5): ["grid", "gantt", "gantt", "grid", "form"],
                ("gantt", 1): ["gantt"], ("gantt", 3): ["grid", "form", "gantt"], ("gantt", 5): list(o5)}
    for (k, m), w in want_seq.items():
        if open_seq(o5, m, k) != w: fails.append("open_seq %s %d → %s" % (k, m, open_seq(o5, m, k)))
    # 제목 줄 높이 보정: 19 는 옛 고정값 그대로, 38 은 +19, 설정 풀이는 반올림·19~48 자르기
    if strip_from_settings('{"ShellTabStripHeight":24}') != 24 or strip_from_settings('{"ShellTabStripHeight":60}') != 48 \
            or strip_from_settings("") is not None or strip_from_settings('{"PrewarmViews":false}') is not None or strip_from_settings("x") is not None \
            or strip_from_settings('{"ShellTabStripHeight":true}') is not None:
        fails.append("strip_from_settings 풀이")
    saved = (dict(BTN), CLOSE0, TITLE0, dict(STRIP))
    apply_strip(19, "t")
    if BTN != BASE_BTN or TITLE0 != BASE_TITLE0 or CLOSE0 != BASE_CLOSE0:
        fails.append("줄 19 인데 좌표가 바뀜")
    apply_strip(38, "t")
    dy = 38 - BASE_STRIP_H
    if BTN["grid"] != (BASE_BTN["grid"][0], BASE_BTN["grid"][1] + dy) or TITLE0 != (BASE_TITLE0[0], BASE_TITLE0[1] + dy) or CLOSE0[1] != BASE_CLOSE0[1] + dy or STRIP["dy"] != 19:
        fails.append("줄 38 보정 %s %s" % (BTN["grid"], TITLE0))
    if LAYOUT == "after" and (BASE_BTN["grid"] != (100, 119) or BASE_TITLE0 != (400, 68)):
        fails.append("기준 좌표(after) %s %s" % (BASE_BTN["grid"], BASE_TITLE0))
    BTN, CLOSE0, TITLE0 = saved[0], saved[1], saved[2]; STRIP.clear(); STRIP.update(saved[3])
    # D-39: 미지정이면 무변화 간격 제외 판정, 원래 p95·뺀 수 병기(합성 trace 간격 17 ok·60 무변화·40 느림·30 놓기)
    if os.environ.get("W10_STATIC_GAPS") is None and STATIC_GAPS != "exclude":
        fails.append("W10_STATIC_GAPS 미지정인데 %s" % STATIC_GAPS)
    if DSG.self_test() != 0:
        fails.append("drag_static_gaps self-test")
    import tempfile
    with tempfile.NamedTemporaryFile("w", suffix=".json", delete=False) as tf:
        json.dump({"traceEvents": DSG._ev_fixture()}, tf)
    try:
        ad = analyze_drag(tf.name)
    finally:
        os.unlink(tf.name)
    want_ad = (40.0 if STATIC_GAPS == "exclude" else 60.0, 60.0, 1 if STATIC_GAPS == "exclude" else 0, ["ok", "nochange", "slow", "release"])
    if (ad["frame_interval_p95_ms"], ad["frame_interval_p95_all_ms"], ad["excluded_static_gaps"], ad["frame_interval_kinds"]) != want_ad \
            or ad["frame_intervals_ms"] != [17.0, 60.0, 40.0, 30.0]:
        fails.append("analyze_drag D-39 %s" % {k: ad.get(k) for k in ("frame_interval_p95_ms", "frame_interval_p95_all_ms", "excluded_static_gaps", "frame_interval_kinds")})
    print("self-test", "PASS" if not fails else "FAIL %s" % fails)
    return 1 if fails else 0


def slow_frames(out_json, groups, slow=25.0, long_=100.0, norm=20.0):
    """오프라인 분석(실입력·Chrome 불필요): 이미 저장된 끌기 추적(trace/dragos_doc*.json)에서 프레임 간격을 정상(≤norm)·느림(>slow)·긴(>long_)으로 나누고,
    구간마다 렌더러 메인 스레드가 얼마나 바빴는지(RunTask 합집합 겹침 비율)와 단계별 시간(스크립트·입력·레이아웃·페인트·GC)을 평균낸다.
    groups: [(라벨, [추적 파일...])]. 결과는 out_json 에 쓴다. 사용: python3 -B w10_web.py --slow-frames <out.json> 라벨=추적폴더 [라벨=추적폴더 ...]"""
    import glob
    ST = {"script": ("FunctionCall", "EvaluateScript", "v8.callFunction"), "anim_frame": ("FireAnimationFrame",), "timer": ("TimerFire",), "input": ("EventDispatch", "WebFrameWidgetImpl::HandleInputEvent"),
          "layout": ("UpdateLayoutTree", "Layout", "PrePaint", "UpdateLifecycles"), "paint": ("Paint", "Layerize", "Commit", "RasterTask", "PaintImage")}
    def clip(a, b, s, d): return max(0.0, min(b, s + d) - max(a, s))
    res = {}
    for label, files in groups:
        buckets = {"normal": [], "slow": [], "long": []}
        gc_events = 0; thread_hist = {}
        for path in files:
            ev = json.load(open(path))["traceEvents"]
            press = rel = None; opened = {}
            for e in ev:
                if e.get("name") != "EventLatency": continue
                k = (e["pid"], e.get("id") or (e.get("id2") or {}).get("local") or (e.get("id2") or {}).get("global"))
                if e.get("ph") == "b":
                    t = ((e.get("args") or {}).get("event_latency") or {}).get("event_type")
                    opened[k] = t
                    if t == "MOUSE_PRESSED" and press is None: press = e["ts"]
                    if t == "MOUSE_RELEASED": rel = e["ts"]
            if press is None or rel is None: continue
            sw = sorted(e["ts"] for e in ev if e.get("name") == "Display::DrawAndSwap" and e.get("ph") == "X" and press <= e["ts"] <= rel + 50000)
            mains = {(e["pid"], e["tid"]) for e in ev if e.get("name") == "thread_name" and (e.get("args") or {}).get("name") == "CrRendererMain"}
            tasks = [e for e in ev if e.get("ph") == "X" and (e["pid"], e["tid"]) in mains]
            gcs = [e for e in ev if e.get("ph") == "X" and str(e.get("name", "")).startswith(("MajorGC", "MinorGC", "V8.GC"))]   # 모든 스레드의 GC
            gc_events += len(gcs)
            runtask = sorted((e["ts"], e["ts"] + e.get("dur", 0)) for e in tasks if e.get("name") == "ThreadControllerImpl::RunTask")
            for a, b in zip(sw, sw[1:]):
                ms = (b - a) / 1000
                kind = "long" if ms > long_ else "slow" if ms > slow else "normal" if ms <= norm else None
                if kind is None: continue
                busy = 0.0; last = a   # RunTask 가 겹치므로 합집합으로 센다
                for s, t in runtask:
                    if t <= a or s >= b: continue
                    s2, t2 = max(s, last), min(t, b)
                    if t2 > s2: busy += t2 - s2; last = t2
                row = {"interval_ms": ms, "main_busy_ratio": busy / (b - a) if b > a else 0.0, "start_off_ms": (a - press) / 1000, "end_off_ms": (rel - b) / 1000,
                       "gc": sum(clip(a, b, e["ts"], e.get("dur", 0)) for e in gcs) / 1000}
                for st, names in ST.items():
                    row[st] = sum(clip(a, b, e["ts"], e.get("dur", 0)) for e in tasks if e.get("name") in names) / 1000
                big = max((e for e in tasks if e.get("name") in sum(ST.values(), ()) + ("ThreadControllerImpl::RunTask",)), key=lambda e: clip(a, b, e["ts"], e.get("dur", 0)), default=None)
                if big and kind != "normal": thread_hist[big["name"]] = thread_hist.get(big["name"], 0) + 1
                buckets[kind].append(row)
        def avg(rows, k): return round(sum(r[k] for r in rows) / len(rows), 2) if rows else None
        res[label] = {"files": len(files), "gc_events": gc_events, "largest_event_in_slow_intervals": thread_hist}
        for kind, rows in buckets.items():
            res[label][kind] = {"n": len(rows), "mean_interval_ms": avg(rows, "interval_ms"), "main_busy_ratio": avg(rows, "main_busy_ratio"), "gc": avg(rows, "gc"), **{st: avg(rows, st) for st in ST}}
            if kind == "long": res[label][kind]["start_off_ms"] = sorted(round(r["start_off_ms"]) for r in rows); res[label][kind]["end_off_ms"] = sorted(round(r["end_off_ms"]) for r in rows)
    json.dump(res, open(out_json, "w"), ensure_ascii=False, indent=1)
    return res


def prof_timeline(out_json, files, gap_ms=40.0):
    """오프라인 분석(실입력 불필요): 저장된 .cpuprofile 의 시간선에서 유휴가 gap_ms 이상 끊긴 구간과 그 앞뒤 첫 비유휴 함수, JS 쪽 WebGL·GC 호출 시각, wasm 함수 번호별 자기 시간 상위를 뽑는다.
    wasm 함수 이름이 없는 빌드(이름 구간 없음)면 'wasm-function[번호]' 로만 보이므로 번호별 합으로 비교한다. 사용: python3 -B w10_web.py --prof-timeline <out.json> <cpuprofile...>"""
    res = {}
    for path in files:
        prof = json.load(open(path)); nodes = {n["id"]: n for n in prof["nodes"]}
        t = prof["startTime"]; tl = []
        for s, d in zip(prof["samples"], prof["timeDeltas"]):
            t += d; cf = nodes[s]["callFrame"]; tl.append((t, cf.get("functionName") or "(anon)", cf.get("url", "").split("/")[-1]))
        t0 = tl[0][0]; idle = lambda f: f in ("(idle)",)
        gaps = []; i = 0
        while i < len(tl):
            if idle(tl[i][1]):
                j = i
                while j < len(tl) and idle(tl[j][1]): j += 1
                dur = ((tl[j][0] if j < len(tl) else tl[-1][0]) - tl[i][0]) / 1000
                if dur >= gap_ms: gaps.append({"start_ms": round((tl[i][0] - t0) / 1000), "idle_ms": round(dur), "before": tl[i - 1][1:] if i else None, "after": tl[j][1:] if j < len(tl) else None})
                i = j
            else: i += 1
        webgl = [(round((x[0] - t0) / 1000), x[1]) for x in tl if x[1] in ("getProgramParameter", "linkProgram", "compileShader", "getShaderParameter", "texImage2D", "texSubImage2D", "(garbage collector)", "createProgram", "useProgram")]
        selft = {}
        for a, b in zip(tl, tl[1:]):
            if a[1] not in ("(idle)", "(program)"): selft[a[1] + "@" + a[2][:12]] = selft.get(a[1] + "@" + a[2][:12], 0) + (b[0] - a[0]) / 1000
        res[os.path.basename(path)] = {"gaps": gaps, "webgl_gc_calls": webgl[:40], "top_self": sorted(([k, round(v, 1)] for k, v in selft.items()), key=lambda x: -x[1])[:15]}
    json.dump(res, open(out_json, "w"), ensure_ascii=False, indent=1)
    return res


if len(sys.argv) > 1 and sys.argv[1] == "--self-test":
    sys.exit(self_test())
if len(sys.argv) > 3 and sys.argv[1] == "--prof-timeline":
    import glob
    print(len(prof_timeline(sys.argv[2], sorted(sum((glob.glob(a) for a in sys.argv[3:]), [])))), "개 프로파일 분석 →", sys.argv[2]); sys.exit(0)
if len(sys.argv) > 3 and sys.argv[1] == "--slow-frames":
    import glob
    gm = {}   # 같은 라벨을 여러 번 주면 파일을 합친다(예: b2=패스1 b2=패스4)
    for a in sys.argv[3:]: gm.setdefault(a.split("=", 1)[0], []).extend(sorted(glob.glob(os.path.join(a.split("=", 1)[1], "dragos_doc5_*.json"))))
    print(json.dumps(slow_frames(sys.argv[2], list(gm.items())), ensure_ascii=False, indent=1)); sys.exit(0)
main()
