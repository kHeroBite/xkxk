# Windows 입력 유휴를 주기적으로 읽어 PC 비움 상태 파일을 갱신하는 읽기 전용 감시기와 확인 명령
"""PC 비움 자동 감시.

사용법
  pc_away_monitor.py run   --state <pc_away.json> [--threshold 300] [--interval 10] [--return-below 5]
  pc_away_monitor.py check --state <pc_away.json> [--max-age 60]   # away 이면 rc=0, 아니면 rc=1, 상태 불명 rc=2
  pc_away_monitor.py --self-test

상태 파일 {"state":"away|present","idle_sec":N,"since":"ISO","updated":"ISO"} 을 원자적으로 갱신한다.
away 이면 플래그 파일(--flag, 기본 <state>.on)을 만들고 present 이면 지운다(test -f 용).
비움 판정은 유휴 >= threshold, 복귀는 유휴 < return-below(기본 5초). 그 사이 값은 직전 상태 유지.
user_away_guard.read_probe(GetLastInputInfo 읽기 전용)를 재사용하며 입력·화면을 건드리지 않는다.
"""
import argparse
import json
import os
import sys
import time
from datetime import datetime, timezone

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import user_away_guard as uag  # noqa: E402


def _iso():
    return datetime.now(timezone.utc).astimezone().isoformat(timespec="seconds")


def decide(prev, idle_sec, threshold, return_below):
    """직전 상태와 유휴 초로 새 상태를 정한다."""
    if idle_sec >= threshold:
        return "away"
    if idle_sec < return_below:
        return "present"
    return prev or "present"


PRE_MAX_MS = 10000  # 끝나지 않은 주입(*_pre 뒤 완료 기록 없음)을 주입 창으로 보는 상한 — 남은 pre 가 복귀 감지를 영원히 막지 않게 한다(D-21)


def injected_input(probe, records):
    """마지막 입력이 시험 도구의 주입(user_away_guard 기록) 창 안이면 True. 사용자 입력이 아니다.
    마지막 기록이 완료 기록이면 그 시각 + GRACE_MS 까지, 마지막 기록이 *_pre(끝나지 않은 주입 — 예: 부하로 3.7초 걸린 끌기, 11-13 02:46 실측)이면
    pre 시각 + PRE_MAX_MS 까지를 주입 창으로 본다."""
    marks = [r for r in records if r.get("kind") != uag.KIND_RETURNED and isinstance(r.get("t_ms"), (int, float))]
    if not marks:
        return False
    last = max(marks, key=lambda r: r["t_ms"])
    last_input_ms = probe["win_now_ms"] - probe["idle_ms"]
    window = PRE_MAX_MS if str(last.get("kind", "")).endswith("_pre") else uag.GRACE_MS
    return last_input_ms <= last["t_ms"] + window


def decide_probe(prev, probe, records, threshold, return_below):
    """주입 직후 입력은 사용자 입력에서 뺀다. 그 경우 present 로 바꾸지 않고 직전 상태를 유지한다(away 판정은 그대로)."""
    idle = probe["idle_ms"] / 1000.0
    if idle < threshold and injected_input(probe, records):
        return prev or "present", idle
    return decide(prev, idle, threshold, return_below), idle


def write_atomic(path, obj):
    tmp = path + ".tmp"
    with open(tmp, "w", encoding="utf-8") as f:
        json.dump(obj, f, ensure_ascii=False)
    os.replace(tmp, path)


def run(args):
    state, since = None, _iso()
    os.makedirs(os.path.dirname(os.path.abspath(args.state)), exist_ok=True)
    fails = 0
    while True:
        try:
            probe = uag.read_probe()
        except (Exception, uag.GuardUnavailable) as e:
            # D-32: GuardUnavailable 은 BaseException 이라 except Exception 으로 잡히지 않아 데몬이 끝났다(11-13 10:12, WSL vsock accept4 failed 110).
            # 끝내지 않고 state=unknown 을 쓴 뒤 1·2·4·8초 간격으로 다시 읽는다. unknown 은 away 가 아니다(check rc=2)
            fails += 1
            wait = (1, 2, 4, 8)[min(fails, 4) - 1]
            print("%s probe 실패 #%d(다음 %ds 뒤 재시도): %r" % (_iso(), fails, wait, e), file=sys.stderr, flush=True)
            if state != "unknown":
                state, since = "unknown", _iso()
            flag = args.flag or os.path.splitext(args.state)[0] + ".on"
            if flag != "off" and os.path.exists(flag):
                os.remove(flag)  # unknown 은 away 가 아니므로 away 표식도 내린다
            write_atomic(args.state, {"state": "unknown", "idle_sec": -1, "since": since, "updated": _iso(), "probe_fails": fails, "reason": repr(e)[-200:]})
            time.sleep(wait)
            continue
        if fails:
            print("%s probe 복구(연속 실패 %d회 뒤)" % (_iso(), fails), file=sys.stderr, flush=True)
            fails = 0
            if state == "unknown":
                state = None  # 복구 뒤에는 처음 판정처럼 idle 로 다시 정한다
        try:
            records = uag.read_records()
        except Exception:  # 주입 기록을 못 읽으면 기존 판정으로
            records = []
        new, idle = decide_probe(state, probe, records, args.threshold, args.return_below)
        if new != state:
            state, since = new, _iso()
        flag = args.flag or os.path.splitext(args.state)[0] + ".on"
        if flag != "off":
            if state == "away":
                open(flag, "a").close()
            elif os.path.exists(flag):
                os.remove(flag)
        write_atomic(args.state, {"state": state, "idle_sec": int(idle), "since": since, "updated": _iso()})
        time.sleep(args.interval)


def check(args):
    try:
        with open(args.state, encoding="utf-8") as f:
            d = json.load(f)
        age = time.time() - datetime.fromisoformat(d["updated"]).timestamp()
    except Exception as e:
        print("UNKNOWN %r" % (e,))
        return 2
    if age > args.max_age:
        print("UNKNOWN stale %ds" % age)
        return 2
    print("%s idle=%ss since=%s" % (d["state"], d["idle_sec"], d["since"]))
    if d["state"] == "unknown":  # D-32: 입력 상태를 못 읽는 중 — away 로 보지 않고 UNKNOWN 과 같은 rc=2
        return 2
    return 0 if d["state"] == "away" else 1


def self_test():
    ok = [decide(None, 400, 300, 5) == "away", decide("away", 3, 300, 5) == "present",
          decide("away", 50, 300, 5) == "away", decide("present", 50, 300, 5) == "present",
          decide(None, 50, 300, 5) == "present"]
    pr = lambda idle_ms: {"win_now_ms": 1000000, "idle_ms": idle_ms}
    rec = [{"t_ms": 999000, "kind": "click"}]
    ok += [
        decide_probe("away", pr(1000), rec, 300, 5)[0] == "away",      # 마지막 입력=주입 시각 → 사용자 아님
        decide_probe("away", pr(1000), [], 300, 5)[0] == "present",    # 기록 없으면 기존 판정
        decide_probe("away", pr(1000 + uag.GRACE_MS + 5000), rec, 300, 5)[0] == "away",  # 주입보다 이전 입력도 사용자 아님
        decide_probe("away", pr(0), [{"t_ms": 990000, "kind": "click"}], 300, 5)[0] == "present",  # 주입 뒤 입력은 사용자
        decide_probe("present", pr(400000), rec, 300, 5)[0] == "away", # 임계 이상은 away
        decide_probe(None, pr(1000), rec, 300, 5)[0] == "present",
    ]
    # D-21: 끝나지 않은 주입(*_pre) 창. now=1000000, 입력은 지금(idle 0)
    pre = lambda dt_ms: [{"t_ms": 1000000 - dt_ms, "kind": "drag_pre"}]
    done = lambda dt_ms: [{"t_ms": 1000000 - dt_ms - 3700, "kind": "drag_pre"}, {"t_ms": 1000000 - dt_ms, "kind": "drag"}]
    ok += [
        injected_input(pr(0), pre(3700)) is True,     # pre 만 있고 3.7초 지남 → 주입
        injected_input(pr(0), pre(12000)) is False,   # pre 뒤 12초(상한 10초 넘음) → 사용자
        injected_input(pr(0), done(2000)) is False,   # 완료 뒤 2초(GRACE 1.5초 넘음) → 사용자
        decide_probe("away", pr(0), pre(3700), 300, 5)[0] == "away",  # 끌기 중 표본은 away 유지
    ]
    print("self-test", "PASS" if all(ok) else "FAIL", ok)
    return 0 if all(ok) else 1


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("cmd", nargs="?", choices=["run", "check"])
    p.add_argument("--state")
    p.add_argument("--flag", help="away 이면 만들고 present 이면 지우는 플래그 파일(기본 상태 파일의 .on, off 이면 미사용)")
    p.add_argument("--threshold", type=float, default=300)
    p.add_argument("--interval", type=float, default=10)
    p.add_argument("--return-below", type=float, default=5)
    p.add_argument("--max-age", type=float, default=60)
    p.add_argument("--self-test", action="store_true")
    a = p.parse_args()
    if a.self_test:
        return self_test()
    if not a.cmd or not a.state:
        p.error("cmd(run|check) 와 --state 필요")
    return run(a) if a.cmd == "run" else check(a)


if __name__ == "__main__":
    sys.exit(main())
