#!/usr/bin/env python3
# 시험 앱 창의 화면 노출·전경 획득 감시 공용 함수 — 측정 도구가 with 로 감싸면 겹침 즉시 창을 되돌리고 다음 명령 전에 GuardBreach 로 측정을 멈춘다
"""
사용:
    from offscreen_guard import OffscreenGuard, GuardBreach
    with OffscreenGuard(pid) as guard:
        guard.check()        # 조작 앞마다 부른다. 겹침·전경이 있었으면 GuardBreach
        ...
    print(guard.summary())   # {samples, maxGapMs, breaches[], virtual, stoppedCleanly}

pid 는 시험 앱 자기 PID 여야 한다(remote-control.json 의 pid). 감시 스크립트는 그 PID 의 창만 옮기고, 활성화·실입력을 하지 않는다.
겹침을 잡기 전까지의 노출은 막지 못한다. 최대 노출 시간은 summary 의 maxGapMs 로 본다(앱 쪽 고정은 MainBgPinHook).
GuardBreach 를 받으면 호출자가 자기 앱을 종료하고 보고한다. 전경은 되돌리지 않는다(사용자 클릭과 구분되지 않는다).
"""
import os
import subprocess
import tempfile
import threading
import time

PS1 = os.path.join(os.path.dirname(os.path.abspath(__file__)), "offscreen_guard.ps1")
READY_TIMEOUT_S = 60  # 시스템이 밀리면 PowerShell 기동이 수십 초 걸린다
STOP_TIMEOUT_S = 15


class GuardBreach(BaseException):
    """측정 도구의 '실패도 기록하고 계속' 처리에 삼켜지지 않게 BaseException 에서 파생한다."""


def _win_path(path):
    return subprocess.check_output(["wslpath", "-w", path], text=True).strip()


class OffscreenGuard:
    def __init__(self, pid, interval_ms=100):
        if not pid:
            raise ValueError("감시할 시험 앱 PID 가 없다")
        self.pid = int(pid)
        self.interval_ms = interval_ms
        self.breaches = []
        self.samples = None
        self.max_gap_ms = None
        self.virtual = None
        self.stopped_cleanly = False
        self._proc = None
        self._thread = None
        self._ready = threading.Event()
        self._done = threading.Event()
        # 창 쪽(powershell.exe)에서도 보이는 폴더에 중지 표식을 둔다
        self._stop_dir = tempfile.mkdtemp(prefix="pd_offguard_", dir=os.path.dirname(PS1))
        self._stop_file = os.path.join(self._stop_dir, "stop")

    def __enter__(self):
        self.start()
        return self

    def __exit__(self, exc_type, exc, tb):
        self.stop()
        return False

    def start(self):
        cmd = ["powershell.exe", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", _win_path(PS1),
               "-TargetPid", str(self.pid), "-StopFile", _win_path(self._stop_dir) + "\\stop", "-IntervalMs", str(self.interval_ms)]
        self._proc = subprocess.Popen(cmd, stdin=subprocess.DEVNULL, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True, errors="replace")
        self._thread = threading.Thread(target=self._read, daemon=True)
        self._thread.start()
        if not self._ready.wait(READY_TIMEOUT_S):
            self.stop()
            raise RuntimeError("창 감시가 %d초 안에 시작되지 않았다 — 측정하지 않는다" % READY_TIMEOUT_S)

    def _read(self):
        for raw in self._proc.stdout:
            line = raw.strip()
            if line.startswith("READY"):
                self.virtual = line.split("virtual=")[-1]
                self._ready.set()
            elif line.startswith(("OVERLAP", "FOREGROUND")):
                self.breaches.append({"at": time.strftime("%H:%M:%S"), "line": line})
            elif line.startswith("DONE"):
                fields = dict(p.split("=", 1) for p in line.split()[1:])
                self.samples = int(fields.get("samples", 0))
                self.max_gap_ms = int(fields.get("maxGapMs", 0))
                self.stopped_cleanly = True
        self._done.set()

    def check(self):
        if self.breaches:
            raise GuardBreach("시험 창 노출·전경 %d건, 첫 건: %s" % (len(self.breaches), self.breaches[0]["line"]))

    def stop(self):
        try:
            with open(self._stop_file, "w", encoding="utf-8"):
                pass
            self._done.wait(STOP_TIMEOUT_S)
            if self._proc and self._proc.poll() is None:
                self._proc.terminate()
        finally:
            try:
                os.remove(self._stop_file)
            except OSError:
                pass
            try:
                os.rmdir(self._stop_dir)
            except OSError:
                pass

    def summary(self):
        return {"pid": self.pid, "virtual": self.virtual, "samples": self.samples, "maxGapMs": self.max_gap_ms,
                "breaches": self.breaches, "stoppedCleanly": self.stopped_cleanly}
