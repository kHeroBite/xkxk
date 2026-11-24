# 기기 Chrome CDP 다리(cdp_bridge.mjs)를 Windows node.exe 로 띄우고 invoke·eval·CDP 명령을 한 줄 JSON 으로 주고받는 Python 클라이언트(ui20_check 원격 전송 겸 웹 시험 도구)
import base64
import json
import os
import subprocess
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

NODE = "/mnt/c/Program Files/nodejs/node.exe"
HERE = os.path.dirname(os.path.abspath(__file__))


def winpath(p):
    return subprocess.run(["wslpath", "-w", p], capture_output=True, text=True).stdout.strip()


class Bridge:
    def __init__(self, url_part="127.0.0.1:5185"):
        self.p = subprocess.Popen([NODE, winpath(os.path.join(HERE, "cdp_bridge.mjs")), url_part],
                                  stdin=subprocess.PIPE, stdout=subprocess.PIPE, text=True, encoding="utf-8", bufsize=1)
        first = json.loads(self.p.stdout.readline())
        if not first.get("ok"):
            raise RuntimeError("CDP 다리 연결 실패: %s" % first)
        self.url = first.get("ready")

    def _req(self, q):
        self.p.stdin.write(json.dumps(q, ensure_ascii=False) + "\n")
        self.p.stdin.flush()
        line = self.p.stdout.readline()
        if not line:
            raise RuntimeError("CDP 다리 종료됨")
        return json.loads(line)

    def invoke_raw(self, body):
        r = self._req({"op": "invoke", "body": body})
        if not r.get("ok"):
            raise RuntimeError(r.get("error"))
        v = r.get("value")
        return v if isinstance(v, str) else json.dumps(v, ensure_ascii=False)

    def call(self, cmd, **args):
        """retest_remote.Remote.call 과 같은 형태: 성공이면 result, 실패면 None(오류는 last_error)."""
        args.pop("_timeout", None)
        body = json.dumps({"cmd": cmd, "args": args}, ensure_ascii=False)
        j = json.loads(self.invoke_raw(body))
        self.last = j
        return j.get("result") if j.get("ok") else None

    def eval(self, expr):
        r = self._req({"op": "eval", "expr": expr})
        return r.get("value") if r.get("ok") else {"__error": r.get("error")}

    def cdp(self, method, **params):
        r = self._req({"op": "cdp", "method": method, "params": params})
        return r.get("value") if r.get("ok") else {"__error": r.get("error")}

    def sleep_ms(self, ms):
        self._req({"op": "sleep", "ms": ms})

    def touch(self, typ, pts):
        return self.cdp("Input.dispatchTouchEvent", type=typ, touchPoints=pts)

    def mouse(self, typ, x, y, button="none", clicks=0, buttons=0):
        return self.cdp("Input.dispatchMouseEvent", type=typ, x=x, y=y, button=button, clickCount=clicks, buttons=buttons, pointerType="mouse")

    def shot(self, path):
        v = self.cdp("Page.captureScreenshot", format="png")
        if isinstance(v, dict) and v.get("data"):
            open(path, "wb").write(base64.b64decode(v["data"]))
            return os.path.basename(path)
        return None

    def close(self):
        try:
            self._req({"op": "quit"})
        except Exception:  # noqa: BLE001
            pass
        self.p.wait(timeout=10)
