# otest-w-15 B3 perf05_web_chrome 측정 — 새 프로필 실창 Chrome 으로 product2 첫 적재를 재서 measure 파일을 쓴다(입력 주입 없음)
# 사용: python3 -B perf05_web.py <출력폴더> <서버 URL> <CDP 포트>
# 판정 규칙(측정 전 고정, team-lead 01:5x 지시 — pcaway_gate.py B3 SPEC 에 expect 없음 확인):
#   유효 시행: 시행 직전 load1 < 8. 무효 시행은 버리고 다시 잰다(최대 15회 시도). 유효 5회를 모은다
#   값: 유효 5회 tPaint(내비게이션 명령→첫 내용 프레임, pdchrome nav) 중앙값, 최댓값도 기록. tCanvas(캔버스 표시·로딩 문구 사라짐)도 함께 기록
#   pass: 중앙값 <= 5000ms(문서 상한). 3000ms 이하 여부는 표시만 하고 판정에 쓰지 않는다
#   한계: 루프백(127.0.0.1) 무압축 전송이라 문서의 '압축 전송' 조건이 아니다(source·note 에 적는다)
import json, os, statistics, subprocess, sys, time
UI = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", ".."))
sys.path.insert(0, os.path.join(UI, "test/web/measure"))
sys.path.insert(0, os.path.join(UI, "test/desktop/measure"))
import dc_common as dc  # noqa: E402
import retest_real_input as ri  # noqa: E402
away = ri.away
PS = "/mnt/c/Windows/System32/WindowsPowerShell/v1.0/powershell.exe"
FG_PS = ("Add-Type 'using System;using System.Runtime.InteropServices;public class W15FG{[DllImport(\"user32.dll\")]public static extern IntPtr GetForegroundWindow();"
         "[DllImport(\"user32.dll\")]public static extern uint GetWindowThreadProcessId(IntPtr h,out uint p);}';"
         "$h=[W15FG]::GetForegroundWindow();$p=0;[void][W15FG]::GetWindowThreadProcessId($h,[ref]$p);\"$([int64]$h) $p\"")
out, url, cport = sys.argv[1], sys.argv[2], int(sys.argv[3])
os.makedirs(out, exist_ok=True)
log = []
def P(*a):
    s = time.strftime("%H:%M:%S") + " " + " ".join(str(x) for x in a); print(s, flush=True); log.append(s)
def fg():
    try:
        o = subprocess.run([PS, "-NoProfile", "-Command", FG_PS], capture_output=True, text=True, timeout=30).stdout.strip().split()
        return int(o[-2]), int(o[-1])
    except Exception:  # noqa: BLE001
        return None, None
def pid_alive(pid):
    o = subprocess.run(["/mnt/c/Windows/System32/tasklist.exe", "/FI", "PID eq %d" % pid, "/NH"], capture_output=True, text=True, errors="replace", timeout=30).stdout
    return str(pid) in o
started = ri.now_iso(); trials, valid = [], []
stamp = time.strftime("%H%M%S")
try:
    for k in range(15):
        if len(valid) >= 5: break
        away.check()
        SMOKE = os.environ.get("PERF05_SMOKE") == "1"  # 점검용: 헤드리스 1회, 결과는 채택하지 않는다
        if SMOKE and k >= 1: break
        c = dc.Chrome(cport, "w15p5_%s_%02d" % (stamp, k), headless=SMOKE)  # 새 프로필 디렉터리(빈 캐시)
        pid = c.hello.get("pid")
        try:
            time.sleep(1.5)
            h, fp = fg(); time.sleep(0.5)
            l = dc.load1()
            t = {"k": k, "pid": pid, "load1": l, "fg_pid": fp, "fg_is_chrome": fp == pid, "at": ri.now_iso()}
            if l >= 8 and not SMOKE:
                t["valid"] = False; t["why"] = "load1>=8"; trials.append(t); P("무효", t); time.sleep(20); continue
            r = c.q(op="nav", url=url, timeout=120000, needAuto=False)
            v = r.get("value") or {}
            t.update(tCanvas=v.get("tCanvas"), tPaint=v.get("tPaint"), tSettle=v.get("tSettle"), load1_after=dc.load1(),
                     perf={x: (v.get("perf") or {}).get(x) for x in ("domContentLoaded", "load", "transfer", "encoded", "decoded", "fromCache", "lastResEnd")})
            t["valid"] = v.get("tPaint") is not None
            if not t["valid"]: t["why"] = "tPaint 없음 %s" % (r.get("error") or "")
            if k == 0 or len(valid) == 0:
                p = os.path.join(out, "perf05_first_%02d.png" % k); c.q(op="shot", file=dc.win(p)); t["shot"] = p
            trials.append(t); P("시행", json.dumps(t, ensure_ascii=False, default=str))
            if t["valid"]: valid.append(t)
        finally:
            c.close(); time.sleep(1.0)
            if pid and pid_alive(pid): P("경고: Chrome %s 아직 살아 있음" % pid)
except (away.UserReturned, away.GuardUnavailable) as e:
    P("STOP guard", repr(e))
fin = ri.now_iso()
vals = [t["tPaint"] for t in valid]
med = statistics.median(vals) if len(vals) >= 5 else None
mx = max(vals) if vals else None
ok = med is not None and med <= 5000
src = ("perf05_web.py(otest-w-15): product2(web_prod) %s 새 프로필 실창 Chrome 첫 적재, 유효(load1<8) %d회 tPaint 중앙값 %s ms·최댓값 %s ms, 기준 중앙값<=5000ms(문서 상한, 3000ms 이하=%s 표시만). "
       "한계: 루프백 무압축 전송이라 '압축 전송' 조건 아님. 시행 원자료 %s" % (url, len(vals), med, mx, (med is not None and med <= 3000), os.path.join(out, "perf05_raw.json")))
doc = {"measurements": [{"name": "perf05_web_chrome", "value": {"tPaint_median_ms": med, "tPaint_max_ms": mx, "valid_n": len(vals), "under_3000": med is not None and med <= 3000,
                                                                "tCanvas_median_ms": statistics.median([t["tCanvas"] for t in valid if t.get("tCanvas") is not None]) if valid else None},
                         "pass": bool(ok), "source": src}],
       "measured_started_at": started, "measured_finished_at": fin,
       "note": "루프백(127.0.0.1) 무압축 전송 — PERF-05 '압축 전송' 조건이 아니다. 값은 내비게이션 명령 시점부터 첫 내용 프레임(pdchrome tPaint)까지"}
json.dump({"trials": trials, "log": log}, open(os.path.join(out, "perf05_raw.json"), "w"), ensure_ascii=False, indent=1, default=str)
json.dump(doc, open(os.path.join(out, "perf05_measure.json"), "w"), ensure_ascii=False, indent=1, default=str)
P("END valid=%d median=%s max=%s pass=%s" % (len(vals), med, mx, ok))
sys.exit(0 if len(vals) >= 5 else 3)
