# 데스크톱 시험 공용 — 사용자 상태 파일(%LOCALAPPDATA%\PdPoc 의 layout·settings) 백업·md5(앱이 꺼져 있어도 폴더를 찾는다)
import glob
import hashlib
import os
import shutil

USER_FILES = ("layout.json", "settings.json")  # 종료 자동 저장·명시 layout.save 가 덮어쓰는 사용자 상태 파일


def user_dir():
    """사용자 설정 폴더. pdctl.info_path 는 연결 정보 파일이 없으면 sys.exit 하므로 쓰지 않는다."""
    found = sorted(glob.glob("/mnt/c/Users/*/AppData/Local/PdPoc"), key=os.path.getmtime)
    if not found:
        raise RuntimeError("사용자 설정 폴더(PdPoc) 없음")
    return found[-1]


def backup_user_files(r, out):
    """시험 시작 전(앱을 띄우기 전): layout·settings 를 <out>.backup/ 에 복사. 없던 파일은 표시. r 은 쓰지 않는다(호환용 인자)."""
    src = user_dir()
    dst = os.path.abspath(out) + ".backup"
    os.makedirs(dst, exist_ok=True)
    saved = {}
    for name in USER_FILES:
        p = os.path.join(src, name)
        saved[name] = os.path.exists(p)
        if saved[name]:
            shutil.copy2(p, os.path.join(dst, name))
    print("  백업 %s → %s %s" % (src, dst, saved))
    return src, dst, saved


def wait_app_exit(timeout=30):
    """
    데스크톱 앱 프로세스가 0개가 될 때까지 기다린다(True=종료 확인). 복원은 반드시 이 뒤에 한다:
    app.shutdown 뒤 고정 대기(4초)로 복원했더니, 아직 끝나지 않은 앱이 종료 중 layout.json 을 다시 써서 복원본을 덮었다
    (02:15:48 i0156 실측 — md5_after 는 640aee92 로 찍혔는데 실제 파일은 8fac1ea9).
    """
    import subprocess
    import time
    for _ in range(timeout):
        out = subprocess.run(["tasklist.exe", "/FI", "IMAGENAME eq Pd.Poc.Desktop.exe", "/NH"], capture_output=True).stdout
        if out.count(b"Pd.Poc.Desktop") == 0:
            return True
        time.sleep(1)
    return False


HOST_KEEP = ("--data-dir", "--profile-dir", "--multi-instance")  # 재기동 때 첫 기동 값을 그대로 넘길 호스트 인자(격리 폴더·단일 인스턴스 우회)


def proc_cmdline(pid):
    """Windows 프로세스 명령줄(Win32_Process.CommandLine). 읽지 못하면 None."""
    import subprocess
    try:
        p = subprocess.run(["powershell.exe", "-NoProfile", "-Command",
                            "(Get-CimInstance Win32_Process -Filter 'ProcessId=%d').CommandLine" % int(pid)],
                           capture_output=True, timeout=30)
    except (subprocess.TimeoutExpired, OSError, ValueError, TypeError):
        return None
    return p.stdout.decode("utf-8", "replace").strip() or None


def host_args(cmdline):
    """명령줄에서 HOST_KEEP 인자 값을 꺼낸다('--키 값'·'--키=값', 따옴표 값 허용)."""
    import re
    found = {}
    for key in HOST_KEEP:
        m = re.search(r'(?:^|\s)%s(?:=|\s+)("([^"]*)"|(\S+))' % re.escape(key), cmdline or "")
        if m:
            found[key] = m.group(2) if m.group(2) is not None else m.group(3)
    return found


def in_user_dir(win_path):
    """
    그 Windows 경로가 사용자 폴더(%LOCALAPPDATA%\\PdPoc)와 같거나 그 아래인가(하위 폴더·정션 포함). 판정 못 하면 True(비격리로 본다).
    문자열 검사(…\\AppData\\Local\\PdPoc[\\…]) 뒤, wslpath 로 바꿔 realpath(정션·심볼릭 링크 해석)를 사용자 폴더 realpath 와 대조한다.
    """
    import re
    import subprocess
    s = (win_path or "").replace("/", "\\").lower()
    if not s or re.search(r"\\appdata\\local\\pdpoc(\\|$)", s):
        return True
    try:
        wsl = subprocess.run(["wslpath", "-u", win_path], capture_output=True, timeout=10).stdout.decode().strip()
    except (subprocess.TimeoutExpired, OSError):
        return True
    if not wsl:
        return True
    real = os.path.realpath(wsl).lower()
    users = [os.path.realpath(p).lower() for p in glob.glob("/mnt/c/Users/*/AppData/Local/PdPoc")]
    return any(real == u or real.startswith(u.rstrip("/") + "/") for u in users)


def isolation(pid):
    """
    (격리 여부, 상세). 격리 = 그 PID 명령줄에 사용자 폴더(%LOCALAPPDATA%\\PdPoc, 하위 폴더·정션 포함)가 아닌 --data-dir 가 있다.
    --profile-dir 만으로는 격리로 보지 않는다(앱은 로그·캡처 폴더를 --data-dir 로 정한다). --profile-dir 가 있으면 그것도 사용자 폴더가 아니어야 한다.
    명령줄을 읽지 못하거나 PID 가 없으면 격리 아님(확인 불가 = 거부). --isolated 모드의 실행 전제다.
    """
    cmd = proc_cmdline(pid) if pid else None
    if cmd is None:
        return False, {"pid": pid, "reason": "명령줄 조회 실패 — 격리 확인 불가"}
    args = host_args(cmd)
    dirs = {k: v for k, v in args.items() if k in ("--data-dir", "--profile-dir")}
    bad = [k for k, v in dirs.items() if in_user_dir(v)]
    ok = "--data-dir" in dirs and not bad
    return ok, {"pid": pid, "args": args, "reason": "격리" if ok else ("사용자 폴더 지정 %s" % bad if bad else "--data-dir 없음")}


def launched_log(run_stdout):
    """run_desktop_bg.sh 출력의 '로그=<…/desktop_<시각>.log>' 에서 로그 파일 이름을 꺼낸다(없으면 None). 이 도구가 띄운 앱 식별용."""
    import re
    m = re.search(r"로그=(\S+)", run_stdout or "")
    return os.path.basename(m.group(1)) if m else None


def launched_by(pid, log_name):
    """그 PID 명령줄에 이 도구의 run_desktop_bg 출력 로그 이름이 있는가 — 있을 때만 '도구가 띄운 앱'으로 본다(못 읽으면 False)."""
    cmd = proc_cmdline(pid) if (pid and log_name) else None
    return bool(cmd) and log_name.lower() in cmd.lower()


def user_pds():
    """
    사용자 Pd 목록 [(pid, 명령줄|None)]. 사용자 Pd = run_desktop_bg 로 띄우지 않은 Pd.Poc.Desktop 프로세스
    (명령줄에 --remote-control 과 --log …desktop_*.log 가 함께 없음). 명령줄을 못 읽으면 사용자 Pd 로 본다(확인 불가 = 거부).
    """
    import re
    import subprocess
    out = subprocess.run(["tasklist.exe", "/FI", "IMAGENAME eq Pd.Poc.Desktop.exe", "/NH", "/FO", "CSV"], capture_output=True).stdout
    found = []
    for line in out.decode("utf-8", "replace").splitlines():
        cols = [c.strip('"') for c in line.strip().split('","')]
        if len(cols) > 1 and cols[0].lower().startswith("pd.poc.desktop") and cols[1].isdigit():
            cmd = proc_cmdline(int(cols[1]))
            tool = cmd and "--remote-control" in cmd and re.search(r"--log\s+\"?[^\"]*desktop_[0-9_]+\.log", cmd)
            if not tool:
                found.append((int(cols[1]), cmd))
    return found


def relaunch_args(args):
    """host_args 결과를 재기동 앱 인자 목록으로(HOST_KEEP 순서)."""
    out = []
    for k in HOST_KEEP:
        if k in args:
            out += [k, args[k]]
    return out


def pid_alive(pid):
    """그 PID 의 Pd.Poc.Desktop 프로세스가 살아 있는가(PID 재사용 오인 방지로 이미지 이름도 본다)."""
    import subprocess
    out = subprocess.run(["tasklist.exe", "/FI", "PID eq %d" % int(pid), "/NH", "/FO", "CSV"], capture_output=True).stdout
    return ('"%d"' % int(pid)).encode() in out and b"Pd.Poc.Desktop" in out


def stop_pid(pid, timeout=20):
    """
    그 PID 하나만 종료를 기다리고, 시간 안에 끝나지 않으면 그 PID 만 강제 종료한다(True=종료 확인).
    이미지 전체 종료(run_desktop_bg --kill·taskkill /IM)는 사용자가 띄운 Pd 까지 죽이므로 쓰지 않는다.
    """
    import subprocess
    import time
    if not pid:
        return False
    for _ in range(timeout):
        if not pid_alive(pid):
            return True
        time.sleep(1)
    subprocess.run(["taskkill.exe", "/PID", str(int(pid)), "/FI", "IMAGENAME eq Pd.Poc.Desktop.exe", "/F"], capture_output=True)  # PID 재사용 시 다른 프로그램 오종료 방지
    for _ in range(10):
        if not pid_alive(pid):
            return True
        time.sleep(1)
    return False


def user_md5(backup):
    """백업 대상 폴더의 현재 파일 md5(앞 8자리). 없으면 None."""
    src, _, _ = backup
    return {n: (hashlib.md5(open(os.path.join(src, n), "rb").read()).hexdigest()[:8] if os.path.exists(os.path.join(src, n)) else None)
            for n in USER_FILES}
