#!/usr/bin/env python3
# F15 조건부 재판정 측정 — AC-2-06 실제 유휴, AC-5-06 실제 표시 구성 변경, AC-6-10 OS 움직임 끄기를 잰다. retest_pcaway_extra.py 가 부른다
"""
AC-5-06·AC-6-10 은 시스템 상태를 바꾸므로 승인 인자가 없으면 재지 않고 미측정 실패로 남긴다.
OS 애니메이션 설정은 SPIF_SENDCHANGE 만 써서 저장하지 않으며 finally 에서 원래 값으로 되돌린다. 되돌림 값도 결과에 남긴다.
"""
import json
import os
import time

import retest_real_input as ri
from retest_real_input import away

HERE = os.path.dirname(os.path.abspath(__file__))
TOPOLOGY = os.path.join(HERE, "display_topology.ps1")
IDLE_WAIT_S = 10
TOPO_HOLD_S = 12
TOPO_WAIT_S = 90
TOPO_EVENTS = 2  # 분리 1건 + 재연결 1건. 유지 시간이 디바운스보다 길어 합쳐지지 않는다
MOTION_STEPS = 6
SPI_PS = r"""
Add-Type -Namespace Pd -Name S -MemberDefinition '[DllImport("user32.dll", SetLastError=true)] public static extern bool SystemParametersInfo(uint a, uint b, ref int v, uint f); [DllImport("user32.dll", EntryPoint="SystemParametersInfo")] public static extern bool SetSpi(uint a, uint b, IntPtr v, uint f);'
%s
"""


def real_idle(rig, res):
    """AC-2-06: busy 를 한 번 돌린 뒤 실제 입력 없이 기다린다. 입력 없음은 OS 값으로 확인하고 타이머는 전부 0 이어야 한다."""
    started = rig.r.call("busy.begin", scope="main", reason="f15")
    time.sleep(0.8)
    rig.r.call("busy.end", token=(started or {}).get("token"))
    time.sleep(IDLE_WAIT_S)
    probe = away.read_probe()
    timers = (rig.r.call("busy.state") or {}).get("timers")
    rig.grab(res, "f15_idle")
    return {"idle_ms": probe["idle_ms"], "wait_s": IDLE_WAIT_S, "timers": timers}, probe["idle_ms"] >= IDLE_WAIT_S * 1000 - 500 and ri.timers_zero(timers)


def one_eval_per_event(start, end, result):
    """표시 구성 변경이 실제로 적용·복원됐고 판정 횟수 증가가 변경 건수와 같은가."""
    delta = None if start is None or end is None else end - start
    return delta, result.get("applied") is True and result.get("topology_equal_after_restore") is True and delta == TOPO_EVENTS


def real_events(rig, res):
    """AC-5-06: 실제 표시 구성 변경. 판정 횟수는 placement.state 의 mdiEvaluations 다."""
    if len(res.args.allow_topology or "") < 10:
        return {"skipped": "--allow-topology 없음"}, False
    away.check()
    start = (rig.r.call("placement.state") or {}).get("mdiEvaluations")
    done = ri.run_ps_file(TOPOLOGY, "-Action", "apply", "-TestPids", rig.pid, "-UserPdPid", res.args.user_pid, "-HoldSec", TOPO_HOLD_S)
    run_id = (json.loads(done.stdout) if done.stdout.strip().startswith("{") else {}).get("run_id")
    if not run_id:
        return {"apply": done.stdout[-300:], "stderr": done.stderr[-200:]}, False
    deadline, status = time.time() + TOPO_WAIT_S, {}
    while time.time() < deadline:
        away.check()  # 복귀면 예외로 멈춘다. 원복은 독립 감시 프로세스가 맡는다
        text = ri.run_ps_file(TOPOLOGY, "-Action", "status", "-RunId", run_id).stdout
        status = json.loads(text) if text.strip().startswith("{") else {}
        if (status.get("result") or {}).get("ended_at"):
            break
        time.sleep(3)
    time.sleep(2)
    end = (rig.r.call("placement.state") or {}).get("mdiEvaluations")
    result = status.get("result") or {}
    delta, ok = one_eval_per_event(start, end, result)
    return {"run_id": run_id, "before": start, "after": end, "delta": delta, "want": TOPO_EVENTS, "applied": result.get("applied"), "restored": result.get("topology_equal_after_restore")}, ok


def spi_motion(value=None):
    """클라이언트 영역 애니메이션 읽기(0x1042)·쓰기(0x1043, SPIF_SENDCHANGE 만 — 저장하지 않는다)."""
    if value is None:
        body = "$v = 0; [void][Pd.S]::SystemParametersInfo(0x1042, 0, [ref]$v, 0); $v"
    else:
        body = "[void][Pd.S]::SetSpi(0x1043, 0, [IntPtr]%d, 2)" % value
    out = ri.ps_encoded(SPI_PS % body)
    return int(out.split()[-1]) if value is None and out else None


def motion_verdict(steps):
    moved = [s for s in steps if s["before"] != s["later"]]
    return len(moved), len([s for s in moved if s["right_after"] != s["later"]])


def real_motion(rig, res):
    """AC-6-10: OS 설정을 끈 채 새로 띄운 앱에서 투어 단계 이동이 즉시(전환 없음)여야 한다."""
    from retest_tour_motion import key, spot
    if len(res.args.allow_motion_toggle or "") < 10:
        return {"skipped": "--allow-motion-toggle 없음"}, False
    original = spi_motion()
    steps, state = [], {}
    try:
        spi_motion(0)
        rig.stop()
        rig.start()
        state = rig.r.call("busy.state") or {}
        ri.open_docs(rig)
        rig.r.call("tour.start")
        time.sleep(0.8)
        for _ in range(MOTION_STEPS):
            before = key(spot(rig.r))
            rig.r.call("tour.next")
            right_after = key(spot(rig.r))
            time.sleep(0.5)
            steps.append({"before": before, "right_after": right_after, "later": key(spot(rig.r))})
        rig.grab(res, "f15_motion")
        rig.r.call("tour.esc")
    finally:
        spi_motion(original)
    restored = spi_motion()
    moved, animated = motion_verdict(steps)
    return {"osOriginal": original, "osRestored": restored, "reducedMotion": state.get("reducedMotion"), "moved": moved, "animated": animated}, state.get("reducedMotion") is True and moved > 0 and animated == 0 and restored == original


def f15(rig, res):
    res.measure("ac_2_06_real_idle_no_timer", lambda: real_idle(rig, res))
    res.measure("ac_5_06_real_events_one_eval", lambda: real_events(rig, res))
    res.measure("ac_6_10_real_motion_off", lambda: real_motion(rig, res))


def self_test():
    fails = []
    good = {"applied": True, "topology_equal_after_restore": True}
    if one_eval_per_event(3, 5, good)[1] is not True:
        fails.append("판정 2회가 통과하지 않음")
    for start, end, res in ((3, 6, good), (3, 4, good), (None, 5, good), (3, 5, {"applied": False, "topology_equal_after_restore": True}), (3, 5, {"applied": True})):
        if one_eval_per_event(start, end, res)[1]:
            fails.append("판정 오류: %s %s %s" % (start, end, res))
    if motion_verdict([{"before": 1, "right_after": 2, "later": 2}, {"before": 2, "right_after": 2, "later": 2}]) != (1, 0):
        fails.append("움직임 끄기 판정 오류")
    if motion_verdict([{"before": 1, "right_after": 1, "later": 2}]) != (1, 1):
        fails.append("전환 감지 오류")
    return fails
