# 헤드리스 Chrome 을 자기 포트로 띄워 CDP 한 줄 JSON 명령을 주고받는 공용 도우미(끌기 원인 분리 프로파일용). 남의 Chrome 에 붙지 않도록 포트 리스너 PID 를 확인한다
import json, os, shutil, subprocess, time

UI = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", ".."))
TOOL = os.path.join(UI, "test/common/measure/pdchrome.mjs")
NODE = "/mnt/c/Program Files/nodejs/node.exe"
PS = "/mnt/c/Windows/System32/WindowsPowerShell/v1.0/powershell.exe"


def win(p): return subprocess.check_output(["wslpath", "-w", p], text=True).strip()
def load1(): return float(open("/proc/loadavg").read().split()[0])
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


def rm_profile(winpath, pid=None):
    """측정이 끝난 뒤 자기가 만든 임시 프로필 폴더를 지운다. pdwebperf 아래 폴더만 대상이며, 자기 Chrome 이 파일을 놓을 때까지 재시도하고 실패해도 측정에 영향을 주지 않는다."""
    try:
        if "\\pdwebperf\\" not in winpath: return False
        if pid:  # 자기가 띄운 Chrome 의 PID 만 종료(이미 끝났으면 무시)
            subprocess.run(["/mnt/c/Windows/System32/taskkill.exe", "/PID", str(pid), "/T", "/F"], capture_output=True, timeout=20)
        d = subprocess.check_output(["wslpath", "-u", winpath], text=True).strip()
        for _ in range(10):
            shutil.rmtree(d, ignore_errors=True)
            if not os.path.exists(d): return True
            time.sleep(1)
    except Exception: pass
    return False


class Chrome:
    def __init__(self, port, tag, headless=True):
        if port_listener(port): raise SystemExit("포트 %s 에 이미 리스너 — 중단" % port)
        env = dict(os.environ, WSLENV="PDCHROME_HEADLESS")
        if headless: env["PDCHROME_HEADLESS"] = "1"
        else: env.pop("PDCHROME_HEADLESS", None)
        self.profile = r"C:\Users\rioky\AppData\Local\Temp\pdwebperf\dc_" + tag
        self.p = subprocess.Popen([NODE, win(TOOL), str(port), self.profile, "40", "40", "1440", "940"],
                                  stdin=subprocess.PIPE, stdout=subprocess.PIPE, text=True, encoding="utf-8", bufsize=1, env=env)
        self.hello = json.loads(self.p.stdout.readline())
        if not self.hello.get("ok"): raise RuntimeError(self.hello)
        lst = port_listener(port)
        if self.hello.get("pid") not in lst:
            time.sleep(2); lst = port_listener(port)  # 바인드 직후 조회가 비는 경우 1회 재확인
        if self.hello.get("pid") not in lst: raise SystemExit("포트 %s 리스너 %s 가 제 chrome PID %s 와 다름 — 중단" % (port, lst, self.hello.get("pid")))
        self.port = port

    def q(self, **o):
        self.p.stdin.write(json.dumps(o) + "\n"); self.p.stdin.flush()
        line = self.p.stdout.readline()
        return json.loads(line) if line else {"ok": False, "error": "eof"}

    def call(self, cmd, **a):
        """pdAutomation 원격 명령 호출 → result 또는 None"""
        body = json.dumps({"cmd": cmd, "args": a}, ensure_ascii=False)
        r = self.q(op="eval", expr="(async()=>await pdAutomation.invoke(%s))()" % json.dumps(body))
        try: j = json.loads(r.get("value"))
        except Exception: return None
        return j.get("result") if j.get("ok") else None

    def fresh(self, url, settle=3000):
        self.q(op="cdp", method="Page.addScriptToEvaluateOnNewDocument", params={"source": "try{localStorage.removeItem('pd-poc-layout');localStorage.removeItem('pd-poc-settings')}catch(e){}"})
        r = self.q(op="nav", url=url, timeout=90000)
        self.q(op="sleep", ms=settle)
        return r

    def close(self):
        try: self.q(op="quit")
        except Exception: pass
        try: self.p.wait(15)
        except Exception: pass
        rm_profile(getattr(self, "profile", ""), (getattr(self, "hello", None) or {}).get("pid"))  # 끝난 임시 프로필 정리
