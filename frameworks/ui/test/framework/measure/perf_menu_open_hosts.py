# B08 메뉴 열기 측정기의 호스트별 기동부 — 웹(127.0.0.1 정적 서버 + headless Chromium)·데스크톱(run_desktop_bg.sh 화면 밖·임시 데이터 폴더, 자기 PID 만 종료)
import contextlib
import json
import os
import re
import subprocess
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
UI = os.path.normpath(os.path.join(HERE, "..", "..", ".."))
sys.path.insert(0, os.path.join(UI, "test", "common"))
REJECT_PORTS = (5180, 5220, 5242, 5791)


def decode_console(raw):
    """Windows 콘솔 도구(tasklist.exe 등) 바이트 출력을 정확히 디코딩한다 — UTF-8 우선, 실패 시 cp949(한글 Windows 기본). 글자를 버리지 않는다."""
    for enc in ("utf-8", "cp949"):
        try:
            return raw.decode(enc)
        except UnicodeDecodeError:
            continue
    return raw.decode("cp949", errors="strict")


@contextlib.contextmanager
def launch(a, log):
    """(call, meta) 를 내준다. 기동 실패·가드 대기면 call=None 과 meta{status,reason[,carryover]}."""
    with (launch_web if a.host == "web" else launch_desktop)(a, log) as v:
        yield v


@contextlib.contextmanager
def launch_web(a, log):
    import _paths
    _paths.add()
    from playwright.sync_api import sync_playwright
    from ui28_web import Web
    if not os.path.isdir(a.bundle):
        yield None, {"status": "INVALID", "reason": "웹 번들 없음: %s" % a.bundle}
        return
    import socketserver
    socketserver.TCPServer.allow_reuse_address = True  # 이 프로세스만 — 직전 실행의 TIME_WAIT 로 연속 실행이 bind 실패하지 않게
    w, err = None, None
    for _ in range(40):  # WSL 은 재사용 설정이 있어도 TIME_WAIT 동안 bind 를 거부할 수 있어 최대 80초 다시 시도한다
        try:
            w = Web(os.path.abspath(a.bundle), a.port, a.shots)  # 사용자 Chrome 과 무관한 자체 headless 브라우저
            break
        except OSError as e:
            err = e
            time.sleep(2)
    if w is None:
        yield None, {"status": "INVALID", "reason": "정적 서버 포트 %d 사용 불가: %s" % (a.port, err)}
        return
    with sync_playwright() as p:
        try:
            w.open(p)
        except Exception as e:  # noqa: BLE001
            w.httpd.shutdown()
            yield None, {"status": "INVALID", "reason": "웹 기동 실패: %s" % str(e)[:200]}
            return
        log("웹 기동 port=%d bundle=%s" % (a.port, a.bundle))
        meta = {"temp_data_dir": True, "own_pid_only": True, "port": a.port}
        try:
            yield w.call, meta
        finally:
            errs = [e for e in w.errors if e.startswith("pageerror:")]
            meta.update(pageerror=len(errs), console_errors=w.errors[:20])
            w.close()


@contextlib.contextmanager
def launch_desktop(a, log):
    import _paths
    _paths.add()
    import pdctl
    env = dict(os.environ, PD_SLOT_OWNER=a.by)
    # run_desktop_bg.sh 는 --data-dir 지원판이면 임시 --data-dir logs/proddata/<시각> 를 붙이고 "임시 데이터 폴더:" 를 출력한다. 그 줄이 없으면 아래에서 기동 실패(INVALID)로 본다
    pr = subprocess.run(["bash", os.path.join(UI, "tools", "run_desktop_bg.sh"), "--conf", a.conf, "--", "--remote-control"],
                        cwd=UI, env=env, capture_output=True, text=True, timeout=180)
    out = pr.stdout + pr.stderr
    log("run_desktop_bg rc=%d\n%s" % (pr.returncode, out[-1500:]))
    if pr.returncode == 9:
        yield None, {"status": "WAIT", "carryover": "UTODO-0031",
                     "reason": "사용자 파일 가드 rc=9(USER_FILES_CHANGED) — 가드를 풀지 않고 대기. 사용자 확인·복구 뒤 재실행"}
        return
    pid = re.search(r"^PID=(\d+)", out, re.M)
    dd = re.search(r"^임시 데이터 폴더: (.+)$", out, re.M)
    if pr.returncode != 0 or not pid or not dd:
        yield None, {"status": "INVALID", "reason": "데스크톱 기동 실패 rc=%d" % pr.returncode}
        return
    pid, ddw = pid.group(1), dd.group(1).strip()
    ddl = subprocess.run(["wslpath", "-u", ddw], capture_output=True, text=True).stdout.strip()
    meta = {"pid": int(pid), "data_dir": ddw, "own_pid_only": True,
            "temp_data_dir": "appdata\\local\\xkxk" not in ddw.lower() and "proddata" in ddw.lower()}
    info = os.path.join(ddl, "remote-control.json")
    try:
        for _ in range(60):
            if os.path.exists(info):
                break
            time.sleep(1)
        os.environ["PD_REMOTE_INFO"] = info  # 격리 인스턴스에만 붙는다(사용자 폴더 연결 정보 사용 금지)
        ci = pdctl.load_info()
        if int(ci.get("port") or 0) in REJECT_PORTS:
            yield None, dict(meta, status="INVALID", reason="격리 인스턴스 포트가 거부 목록 %s" % ci.get("port"))
            return

        def call(cmd, **args):
            body = json.dumps({"cmd": cmd, "args": args}, ensure_ascii=False)
            try:
                return json.loads(pdctl.curl(ci, "api/cmd", body, timeout=60))
            except ValueError as e:
                return {"ok": False, "error": "JSON 아님: %s" % e}

        for _ in range(60):
            if call("status").get("ok"):
                break
            time.sleep(1)
        yield call, meta
        call("app.shutdown")
    finally:
        time.sleep(3)
        alive = decode_console(subprocess.run(["tasklist.exe", "/FI", "PID eq %s" % pid, "/NH"], capture_output=True).stdout)
        if pid in alive:
            subprocess.run(["taskkill.exe", "/PID", pid, "/F"], capture_output=True)  # 자기 PID 만
            log("자기 PID %s 강제 종료" % pid)
