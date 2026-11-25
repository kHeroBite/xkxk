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
MAX_SHOTS = 10  # 파일 이름 순번 수(순번을 돌려 쓴다 — 끌기가 몇 초 뒤에 시작해도 with 구간 내내 찍고 디스크는 이 장수로 묶는다)
MAX_RUNNING = 4  # 동시에 도는 PowerShell 수 상한(기동이 무겁다)
SAVE_MARGIN_S = 0.3  # 파일 시각은 저장이 끝난 때라 찍은 때보다 늦다 — 끌기 시작 쪽에만 여유를 둔다


def during_drag(saved_at, moving_times):
    """저장 시각이 '움직이는 중' 표본 구간 안이면 True."""
    return bool(moving_times) and min(moving_times) + SAVE_MARGIN_S <= saved_at <= max(moving_times)


class OsCapture:
    """with 블록 동안 겹쳐 찍는다. 파일 이름은 고정(회차마다 덮어씀)이고 이번 시작 뒤에 저장된 것만 쓴다."""

    def __init__(self, pid, out_dir, tag):
        self.pid, self.out_dir, self.tag = pid, out_dir, tag
        self.procs, self.stop, self.launched = [], threading.Event(), 0
        self.interval = INTERVAL_S
        self.thread = threading.Thread(target=self._loop, daemon=True)

    def _launch(self, number):
        args = [app.PS, "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", app.win(os.path.abspath(GRAB)),
                "-OutDir", app.win(self.out_dir), "-Tag", "%s%d" % (self.tag, number), "-ProcessId", str(self.pid)]
        return subprocess.Popen(args, stdin=subprocess.DEVNULL, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL)

    def _loop(self):
        # with 구간 내내 interval 마다 띄운다(끌기 시작 신호가 없어 시작 시각을 모른다 — 6초 뒤 끌기 사례). 순번은 MAX_SHOTS 로 돌린다
        while not self.stop.is_set():
            if sum(1 for q in self.procs if q.poll() is None) < MAX_RUNNING:
                number = self.launched % MAX_SHOTS
                self.launched += 1
                if number < len(self.procs):  # 같은 순번을 다시 쓰면 앞 프로세스가 끝난 뒤다(동시 상한 < 순번 수)
                    self.procs[number].communicate()
                    self.procs[number] = self._launch(number)
                else:
                    self.procs.append(self._launch(number))
            self.stop.wait(self.interval)

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


def self_test():
    """앱·PowerShell 없이 시간 기반 단위 시험 — 긴 with 구간 뒤쪽까지 계속 찍고 순번·동시 수 상한을 지키는지."""
    class Fake:
        def __init__(self, n):
            self.n, self.t0 = n, time.time()
        def poll(self):
            return 0 if time.time() - self.t0 > 0.05 else None
        def communicate(self, timeout=None):
            return b"", b""
        def kill(self):
            pass

    cap = OsCapture(1, ".", "t")
    cap.interval, log, peak = 0.01, [], [0]

    def fake(number):
        log.append((time.time(), number))
        peak[0] = max(peak[0], sum(1 for q in cap.procs if q.poll() is None) + 1)
        return Fake(number)

    cap._launch = fake
    with cap:
        time.sleep(1.0)
    late = [t for t, _ in log if t - cap.started > 0.8]
    cases = [
        len(log) > MAX_SHOTS * 2,  # 총 장수 상한이 구간을 막지 않는다(옛 결함)
        bool(late),  # 구간 끝 가까이에서도 찍는다
        all(n < MAX_SHOTS for _, n in log),
        peak[0] <= MAX_RUNNING,
        during_drag(5.0, [4.0, 5.5]) and not during_drag(3.0, [4.0, 5.5]),
    ]
    print("self-test %s %d/%d" % ("PASS" if all(cases) else "FAIL", sum(cases), len(cases)))
    return 0 if all(cases) else 1


if __name__ == "__main__":
    import sys
    sys.exit(self_test() if "--self-test" in sys.argv else 2)
