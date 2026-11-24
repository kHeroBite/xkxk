#!/usr/bin/env python3
# drag_shadow_option_input 부속 — 실입력 끌기 동안 기존 screen_grab_client.ps1 로 자기 창 영역의 OS 합성 화면을 남긴다
"""
screen_grab_client.ps1 은 전경이 대상 PID 일 때만(아니면 REFUSE_FG) 그 PID 창의 클라이언트 영역만 찍는다. 그 게이트를 그대로 쓴다.
PowerShell 기동이 끌기(약 1.3초)만큼 걸리므로 간격을 두고 여러 번 겹쳐 띄우고, 끄는 동안 저장이 끝난 것만 증거로 인정한다.
그림자 유무는 여기서 판정하지 않는다 — 캡처는 사람(시험 담당)이 판독한다.
"""
import os
import subprocess
import threading
import time

import mdi_drag_app as app

HERE = os.path.dirname(os.path.abspath(__file__))
GRAB = os.path.join(HERE, "..", "..", "common", "measure", "screen_grab_client.ps1")
INTERVAL_S = 0.3
MAX_SHOTS = 10
SAVE_MARGIN_S = 0.3  # 파일 시각은 저장이 끝난 때라 찍은 때보다 늦다 — 끌기 시작 쪽에만 여유를 둔다


def during_drag(saved_at, moving_times):
    """저장 시각이 '움직이는 중' 표본 구간 안이면 True."""
    return bool(moving_times) and min(moving_times) + SAVE_MARGIN_S <= saved_at <= max(moving_times)


class OsCapture:
    """with 블록 동안 겹쳐 찍는다. 파일 이름은 고정(회차마다 덮어씀)이고 이번 시작 뒤에 저장된 것만 쓴다."""

    def __init__(self, pid, out_dir, tag):
        self.pid, self.out_dir, self.tag = pid, out_dir, tag
        self.procs, self.stop = [], threading.Event()
        self.thread = threading.Thread(target=self._loop, daemon=True)

    def _loop(self):
        while not self.stop.is_set() and len(self.procs) < MAX_SHOTS:
            args = [app.PS, "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", app.win(os.path.abspath(GRAB)),
                    "-OutDir", app.win(self.out_dir), "-Tag", "%s%d" % (self.tag, len(self.procs)), "-ProcessId", str(self.pid)]
            self.procs.append(subprocess.Popen(args, stdin=subprocess.DEVNULL, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL))
            self.stop.wait(INTERVAL_S)

    def __enter__(self):
        self.started = time.time()
        self.thread.start()
        return self

    def __exit__(self, *exc):
        self.stop.set()
        self.thread.join(5)
        for proc in self.procs:
            try:
                proc.communicate(timeout=20)
            except subprocess.TimeoutExpired:
                proc.kill()
        return False

    def evidence(self, moving_times):
        """끄는 동안 저장된 메인 창(순번 0 포함 전 창) 캡처 경로. 없으면 빈 목록(OS 합성 화면 미확인)."""
        found = []
        for number in range(len(self.procs)):
            prefix = "cgrab_%s%d_" % (self.tag, number)
            for name in sorted(os.listdir(self.out_dir)):
                path = os.path.join(self.out_dir, name)
                if name.startswith(prefix) and os.path.getmtime(path) >= self.started and during_drag(os.path.getmtime(path), moving_times):
                    found.append(path)
        return found
