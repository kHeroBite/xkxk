# 데스크톱 i1853gp 측정 스크립트 공용 도우미(원격 명령 호출·유휴 확인·CPU 측정). 실입력·전경 조작 없음
import json, subprocess, sys, time

sys.path.insert(0, '/mnt/c/DATA/Project/xkxk/frameworks/ui/tools')
import pdctl

info = pdctl.load_info()


def call(cmd, timeout=90, **args):
    """원격 명령 1회. 시간 초과·연결 실패는 {'_error':...} 로 돌려 스크립트가 죽지 않게 한다."""
    body = json.dumps({"cmd": cmd, "args": {k: v for k, v in args.items() if v is not None}}, ensure_ascii=False)
    try:
        r = json.loads(pdctl.curl(info, "api/cmd", body, timeout=timeout))
    except SystemExit:
        return {"_error": f"연결 실패 또는 {timeout}초 시간 초과"}
    except ValueError as e:
        return {"_error": f"JSON 아님: {e}"}
    return r.get("result") if r.get("ok") else {"_error": r.get("error")}


def find(o, k):
    if isinstance(o, dict):
        if k in o:
            return o[k]
        for v in o.values():
            r = find(v, k)
            if r is not None:
                return r
    elif isinstance(o, list):
        for v in o:
            r = find(v, k)
            if r is not None:
                return r
    return None


def load1():
    return open('/proc/loadavg').read().split()[:3]


def cpu(pid):
    o = subprocess.run(['powershell.exe', '-NoProfile', '-Command', f"(Get-Process -Id {pid}).TotalProcessorTime.TotalSeconds"],
                       capture_output=True, timeout=30).stdout.decode().strip().splitlines()
    return float(o[0]) if o else None


def idle_check(pid, secs=10.0):
    """0절 유휴 측정. 호출 전에 연결·입력이 끝나고 2초 넘게 지나 있어야 한다."""
    a = call("perf.get") or {}
    fa, ca, ta = a.get("frame_samples"), cpu(pid), time.time()
    time.sleep(secs)
    cb = cpu(pid)
    b = call("perf.get") or {}
    fb, tb = b.get("frame_samples"), time.time()
    return {"frame_pump": a.get("frame_pump"),
            "frames_delta": (fb - fa) if isinstance(fa, int) and isinstance(fb, int) else None,
            "cpu_s": round(cb - ca, 3), "cpu_pct_one_core": round((cb - ca) / (tb - ta) * 100, 2)}


def open_single_doc(target):
    """벤치 문서 1개를 열고 최대화한다. 문서 목록을 돌려준다."""
    call("bench.open", target=target)
    time.sleep(2.0)
    docs = call("docs.list") or []
    for d in docs:
        call("docs.state", doc=d.get("id"), state="maximized")
    time.sleep(4.0)
    return docs
