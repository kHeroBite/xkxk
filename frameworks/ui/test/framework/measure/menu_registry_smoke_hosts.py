# B07 메뉴 레지스트리 스모크의 호스트별 실행부 — 웹(headless Chromium)·데스크톱(run_desktop_bg.sh 격리 기동, 자기 PID 만 종료)
import json
import os
import re
import subprocess
import time

from menu_registry_smoke import UI, scenario  # 본 모듈의 시나리오·경로 재사용


def run_web(a, log):
    import _paths
    _paths.add()
    from playwright.sync_api import sync_playwright
    from ui28_web import Web
    w = Web(os.path.abspath(a.bundle), a.port, a.shots)
    with sync_playwright() as p:
        w.open(p)
        try:
            raw = scenario(w.call, a.run_command, log)
        finally:
            w.close()
    errs = [e for e in w.errors if e.startswith("pageerror:")]
    return raw, {"pageerror": len(errs), "console_errors": w.errors[:20], "temp_data_dir": True, "own_pid_only": True}


def run_desktop(a, log):
    import _paths
    _paths.add()
    import pdctl
    env = dict(os.environ, PD_SLOT_OWNER=a.by)
    pr = subprocess.run(["bash", os.path.join(UI, "tools", "run_desktop_bg.sh"), "--conf", a.conf, "--", "--remote-control"],
                        cwd=UI, env=env, capture_output=True, text=True, timeout=180)
    out = pr.stdout + pr.stderr
    log("run_desktop_bg rc=%d\n%s" % (pr.returncode, out[-2000:]))
    if pr.returncode == 9:
        return None, {"result": "WAIT", "carryover": "UTODO-0031",
                      "reason": "사용자 파일 가드 rc=9(USER_FILES_CHANGED) — 가드를 풀지 않고 대기. 사용자 확인·복구 뒤 재실행"}
    pid = re.search(r"^PID=(\d+)", out, re.M)
    dd = re.search(r"^임시 데이터 폴더: (.+)$", out, re.M)
    if pr.returncode != 0 or not pid or not dd:
        return None, {"result": "FAIL", "reason": "데스크톱 기동 실패 rc=%d" % pr.returncode}
    pid, ddw = pid.group(1), dd.group(1).strip()
    ddl = subprocess.run(["wslpath", "-u", ddw], capture_output=True, text=True).stdout.strip()
    meta = {"pid": int(pid), "data_dir": ddw, "own_pid_only": True,
            "temp_data_dir": "appdata\\local\\xkxk" not in ddw.lower() and "proddata" in ddw.lower()}
    info = os.path.join(ddl, "remote-control.json")
    for _ in range(60):
        if os.path.exists(info):
            break
        time.sleep(1)
    os.environ["PD_REMOTE_INFO"] = info  # 격리 인스턴스에만 붙는다(사용자 폴더 연결 정보 사용 금지)
    try:
        ci = pdctl.load_info()
        if int(ci.get("port") or 0) in (5180, 5220):
            raise SystemExit(3)

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
        raw = scenario(call, a.run_command, log)
        call("app.shutdown")
    finally:
        time.sleep(3)
        alive = subprocess.run(["tasklist.exe", "/FI", "PID eq %s" % pid, "/NH"], capture_output=True, text=True).stdout
        if pid in alive:
            subprocess.run(["taskkill.exe", "/PID", pid, "/F"], capture_output=True)  # 자기 PID 만
            log("자기 PID %s 강제 종료" % pid)
    return raw, meta
