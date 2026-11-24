# Q10 독립 측정 — R2R 판을 연속 새 기동하며 회차마다 첫 그리드 열기·ctor.seed·직전 CPU 사용률을 기록하고 경합 회차(seed > 12ms)를 표시하는 스크립트
"""
사용(슬롯 소유 상태에서만): PD_SLOT_OWNER=otest-1 python3 q10_measure.py <conf> <횟수> <태그> [--env PD_GRID_TEXTCOLUMNS=1]
  - 시작 전 5분 이상 유휴(빌드·headless 종료)를 호출자가 확인한다. 이 스크립트는 회차 직전 CPU 사용률을 typeperf 로 3초 평균 기록한다.
  - 인자: --open grid:1 --fresh --bg-pos auto --exit-after 2 --perf-out <json> (앱은 회차마다 새로 기동, 종료 확인 후 다음 회차)
  - --bg-pos 는 화면 밖 auto(가상 화면 오른쪽 바깥, run_desktop_bg.sh 기본과 같음)만 쓴다. 화면 안 좌표는 기동 전에 거부한다(odev-7c 14:1x, 이전 20,20 은 화면 안).
  - 판정: 경합(ctor.seed > 12ms) 표시 없는 회차의 grid open p50 ≤ 700ms. 경합 회차는 함께 보고하고, 경합 제외 후 5회 미만이면 더 측정한다.
"""
import json, os, statistics, subprocess, sys, time

conf, n, tag = sys.argv[1], int(sys.argv[2]), sys.argv[3]
extra_env = {}
if "--env" in sys.argv:
    k, v = sys.argv[sys.argv.index("--env") + 1].split("=", 1)
    extra_env[k] = v
UI = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "..")) + "/"
OUT = UI + "logs/otest/desktop/rt3/q10_%s/" % tag
os.makedirs(OUT, exist_ok=True)
env = dict(os.environ, **extra_env)
BG_POS = os.environ.get("PD_Q10_BG_POS", "auto")  # 화면 밖만 허용 — auto(기본) 또는 모든 모니터 영역과 겹치지 않는 x,y
DRY = "--dry" in sys.argv  # 앱을 띄우지 않고 인자·화면 밖 판정만 확인(otest-1 14:2x)


def monitors():
    # 모든 모니터 사각형(Bounds) — 조회 실패 시 None(안전을 위해 거부)
    ps = ("Add-Type -AssemblyName System.Windows.Forms; [System.Windows.Forms.Screen]::AllScreens | "
          "ForEach-Object { \"$($_.Bounds.X) $($_.Bounds.Y) $($_.Bounds.Width) $($_.Bounds.Height)\" }")
    try:
        out = subprocess.run(["powershell.exe", "-NoProfile", "-Command", ps], capture_output=True, timeout=30).stdout.decode("utf-8", "replace")
        rs = [tuple(int(v) for v in ln.split()) for ln in out.splitlines() if len(ln.split()) == 4]
        return rs or None
    except Exception:
        return None


def require_offscreen(pos, w=1500, h=1000):
    # 화면 안 기동 거부 가드(lead 14:2x): x,y 기준 창(1500x1000 가정 — run_desktop_bg.sh 와 같음)이 모니터 하나라도 겹치면 거부
    if pos == "auto":
        return "auto(가상 화면 오른쪽 바깥)"
    try:
        x, y = (int(v) for v in pos.split(","))
    except ValueError:
        sys.exit("q10_measure: --bg-pos 형식 오류 '%s' — auto 또는 x,y" % pos)
    ms = monitors()
    if ms is None:
        sys.exit("q10_measure: 모니터 조회 실패 — 안전을 위해 거부(--bg-pos %s)" % pos)
    hit = [m for m in ms if x + w > m[0] and x < m[0] + m[2] and y + h > m[1] and y < m[1] + m[3]]
    if hit:
        sys.exit("q10_measure: 화면 안 좌표 거부 --bg-pos %s — 모니터 %s 와 겹침. 화면 밖 auto 를 쓰라" % (pos, hit))
    return "x,y=%d,%d 모니터 %d개와 겹치지 않음" % (x, y, len(ms))


why = require_offscreen(BG_POS)
if DRY:
    # run_desktop_bg.sh 점검 모드(PD_RUN_DRYRUN=1 — 앱 기동 없음)로 최종 인자·이중 판정 확인
    d = subprocess.run(["bash", UI + "tools/run_desktop_bg.sh", "--conf", conf, "--", "--open", "grid:1", "--fresh", "--bg-pos", BG_POS,
                        "--exit-after", "2", "--perf-out", OUT + "run01.json"], capture_output=True, env=dict(env, PD_RUN_DRYRUN="1"), timeout=60)
    print("q10_measure dry: bg-pos", BG_POS, "→", why)
    print(d.stdout.decode("utf-8", "replace").strip().splitlines()[-1] if d.stdout else "", "rc=%d" % d.returncode)
    sys.exit(d.returncode)


def procs():
    r = subprocess.run(["tasklist.exe", "/FI", "IMAGENAME eq xkxk.frameworks.ui.desktop.exe", "/NH", "/FO", "CSV"], capture_output=True)
    return r.stdout.decode("cp949", "replace").count("xkxk.frameworks.ui.desktop")


def cpu_pct():
    try:
        r = subprocess.run(["typeperf.exe", r"\Processor(_Total)\% Processor Time", "-sc", "3"], capture_output=True, timeout=30)
        vals = []
        for line in r.stdout.decode("cp949", "replace").splitlines():
            parts = line.replace('"', "").split(",")
            if len(parts) == 2:
                try:
                    vals.append(float(parts[1]))
                except ValueError:
                    pass
        return round(sum(vals) / len(vals), 1) if vals else None
    except Exception:
        return None


def mars_cpu():
    # 사용자 Mars 프로세스 CPU(코어 1개 기준 %, 2초 평균) — 읽기만 한다(프로세스를 건드리지 않음)
    try:
        r = subprocess.run(["typeperf.exe", r"\Process(Mars)\% Processor Time", "-sc", "2"], capture_output=True, timeout=30)
        vals = []
        for line in r.stdout.decode("cp949", "replace").splitlines():
            parts = line.replace('"', "").split(",")
            if len(parts) == 2:
                try:
                    vals.append(float(parts[1]))
                except ValueError:
                    pass
        return round(sum(vals) / len(vals), 1) if vals else None
    except Exception:
        return None


def op(d, name, key="sync_p50_ms"):
    for o in d.get("operations") or []:
        if o.get("name") == name:
            return o.get(key)
    return None


rows = []
for i in range(1, n + 1):
    while procs():
        time.sleep(1)
    cpu = cpu_pct()
    mcpu = mars_cpu()
    pj = OUT + "run%02d.json" % i
    t0 = time.time()
    p = subprocess.run(["bash", UI + "tools/run_desktop_bg.sh", "--conf", conf, "--", "--open", "grid:1", "--fresh", "--bg-pos", BG_POS,
                        "--exit-after", "2", "--perf-out", pj], capture_output=True, env=env, timeout=120)
    for _ in range(40):
        if not procs() and os.path.exists(pj):
            break
        time.sleep(1)
    row = {"run": i, "cpu_before_pct": cpu, "mars_cpu_before_pct": mcpu, "launcher_rc": p.returncode}
    try:
        d = json.load(open(pj))
        row.update({"grid_open_ms": (d.get("opens") or [{}])[0].get("ms"), "ctor_seed_ms": op(d, "grid.ctor.seed") or op(d, "grid.ctor.seed.cached"),
                    "startup_ms": d.get("startup_ms"), "host_main_opened_ms": d.get("host_main_opened_ms"), "notify_layout": d.get("notify_layout_legacy")})
        row["contended"] = (row["ctor_seed_ms"] or 0) > 12
    except Exception as e:
        row["error"] = repr(e)
    rows.append(row); print(json.dumps(row, ensure_ascii=False), flush=True)
    time.sleep(2)


def med(xs):
    return round(statistics.median(xs), 1) if xs else None


ok = [r["grid_open_ms"] for r in rows if r.get("grid_open_ms") is not None and not r.get("contended")]
allv = [r["grid_open_ms"] for r in rows if r.get("grid_open_ms") is not None]
res = {"conf": conf, "tag": tag, "env": extra_env, "rows": rows, "median_all": med(allv), "median_uncontended": med(ok), "n_uncontended": len(ok),
       "pass_uncontended": (med(ok) is not None and len(ok) >= 5 and med(ok) <= 700)}
json.dump(res, open(OUT + "result.json", "w", encoding="utf-8"), ensure_ascii=False, indent=1)
print("median_all", res["median_all"], "median_uncontended", res["median_uncontended"], "n_uncontended", len(ok), "PASS" if res["pass_uncontended"] else "FAIL/보충 필요")
