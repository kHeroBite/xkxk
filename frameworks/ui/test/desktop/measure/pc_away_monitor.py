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

유령 입력 보조 판정(phantom_input_fallback, 기본 켬, 2026-11-24 D-6)
  어떤 장치가 계속 입력을 내 유휴가 늘 수 ms 일 때만 쓴다. 유휴 기준(1순위)이 away 가 아니면, interval 동안 20ms 간격 연속 표본으로
  커서 정지·눌린 키·마우스 버튼 0·전경 불변(시작 창 또는 run_desktop_bg_pids 의 시험 PID)을 보고, 시험 주입 구간(injections.jsonl)은 뺀다.
  깨끗한 구간이 --phantom-window-min(기본 5분) 이상이면 away(mode=phantom_input_fallback), 한 번이라도 위반이 있으면 즉시 present(mode=phantom_violation).
  상태 파일에 mode 와 phantom{window_min, clean_ms, clean_since_ms, covered_until_ms, samples, cursor, base, last_violation} 를 남긴다.
  끄기: --no-phantom-fallback 또는 환경변수 PD_PHANTOM_FALLBACK=0(옛 판정과 같다). 실행 중인 데몬은 다시 띄워야 적용된다.
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


class PhantomTrack:
    """유령 입력 보조 판정의 연속 감시 상태. 감시 창마다 update 하고, 깨끗한 구간 길이로 away 를 정한다(user_away_guard.phantom_scan 기준)."""

    def __init__(self):
        self.reset(None, None)
        self.last_violation = None

    def reset(self, base, since_ms):
        self.base, self.clean_since_ms, self.samples, self.last_end_ms, self.violated = base, since_ms, 0, None, False

    def update(self, window, records, pids):
        if self.last_end_ms is not None and window["win_start_ms"] - self.last_end_ms > uag.PHANTOM_GAP_MAX_MS:
            self.reset(None, None)  # 감시 공백이 길면 연속성이 끊긴 것이다
        scan = uag.phantom_scan([window], records, pids, self.base)
        last_fg = (window.get("fg") or [[0, None]])[-1][1]
        if scan["violations"]:
            self.last_violation = scan["violations"][-1]
            self.reset({"cursor": scan["cursor"], "fg_hwnd": last_fg}, window["win_end_ms"])  # 위반 뒤 창 끝부터 다시 센다
            self.violated = True
        else:
            if self.clean_since_ms is None:
                self.clean_since_ms = window["win_start_ms"]
            self.base = scan["base"]
            self.samples += scan["samples"]
            self.violated = False
        self.cursor = scan["cursor"]
        self.last_end_ms = window["win_end_ms"]
        return scan

    def clean_ms(self):
        return 0 if self.clean_since_ms is None or self.last_end_ms is None else self.last_end_ms - self.clean_since_ms

    def evidence(self, window_min):
        return {"window_min": window_min, "clean_ms": self.clean_ms(), "clean_since_ms": self.clean_since_ms, "covered_until_ms": self.last_end_ms,
                "samples": self.samples, "cursor": getattr(self, "cursor", None), "base": self.base, "last_violation": self.last_violation}


def decide_phantom(raw, track, window_min, enabled=True):
    """1순위는 유휴 기준(raw). raw 가 away 가 아닐 때만 보조 판정을 본다: 이번 창에 위반이 있으면 present, 깨끗한 구간이 창 길이 이상이면 away."""
    if raw == "away" or not enabled:
        return raw, "idle"
    if track.violated:
        return "present", "phantom_violation"
    if track.clean_ms() >= window_min * 60000:
        return "away", uag.PHANTOM_MODE
    return raw, "idle"


def write_atomic(path, obj):
    tmp = path + ".tmp"
    with open(tmp, "w", encoding="utf-8") as f:
        json.dump(obj, f, ensure_ascii=False)
    os.replace(tmp, path)


def run(args):
    state, since = None, _iso()
    os.makedirs(os.path.dirname(os.path.abspath(args.state)), exist_ok=True)
    fails = 0
    phantom = uag.PHANTOM_ENABLED and not args.no_phantom_fallback
    track, window = PhantomTrack(), None
    while True:
        try:
            if phantom:  # 보조 판정을 켜면 interval 동안 연속 표본을 읽는다(그 자체가 대기라 따로 쉬지 않는다)
                window = uag.read_window(args.interval * 1000, args.watch_step_ms)
                probe = {"win_now_ms": window["win_end_ms"], "idle_ms": window["idle_ms"]}
            else:
                probe = uag.read_probe()
        except (Exception, uag.GuardUnavailable) as e:
            track.reset(None, None)  # 감시가 끊기면 보조 판정의 연속 구간도 처음부터
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
        mode = "idle"
        if phantom:
            track.update(window, records, uag.test_pids())
            new, mode = decide_phantom(new, track, args.phantom_window_min)
        if new != state:
            state, since = new, _iso()
        flag = args.flag or os.path.splitext(args.state)[0] + ".on"
        if flag != "off":
            if state == "away":
                open(flag, "a").close()
            elif os.path.exists(flag):
                os.remove(flag)
        doc = {"state": state, "idle_sec": int(idle), "since": since, "updated": _iso()}
        if phantom:  # 판정 근거: mode=idle|phantom_input_fallback|phantom_violation, 표본 수·창 길이·커서 좌표
            doc.update(mode=mode, phantom=track.evidence(args.phantom_window_min))
        write_atomic(args.state, doc)
        if not phantom:
            time.sleep(args.interval)


def check(args):
    uag.PHANTOM_STATE = args.state  # 조작 직전 확인(user_away_guard.check)이 같은 상태 파일로 보조 판정 근거를 읽게 한다
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
    ok += phantom_self_test()
    print("self-test", "PASS" if all(ok) else "FAIL", ok)
    return 0 if all(ok) else 1


def phantom_self_test():
    """유령 입력 보조 판정 가짜 표본 시험. 10초 창을 이어 붙이고 유휴는 늘 5ms(유령 입력)다."""
    T0, STEP, PID_TEST, FG, BASE = 2000000, 10000, 4242, 111, (100, 100)

    def win(i, cursor=None, keys=None, fg=None, gap=0):
        s = T0 + i * STEP + gap
        return {"win_start_ms": s, "win_end_ms": s + STEP - 50, "samples": 500, "idle_ms": 5,
                "cursor": [[s, BASE[0], BASE[1]]] + (cursor or []), "keys": keys or [], "fg": [[s, FG, 999]] + (fg or [])}

    def run_track(extra=None, n=31, records=(), gap_at=None):
        tr, out = PhantomTrack(), None
        for i in range(n):
            w = (extra or {}).get(i) or win(i)
            if gap_at is not None and i >= gap_at:
                w = dict(w, win_start_ms=w["win_start_ms"] + 20000, win_end_ms=w["win_end_ms"] + 20000)
            tr.update(w, list(records), {PID_TEST})
            out = decide_phantom("present", tr, 5)
        return tr, out

    t_mid = lambda i: T0 + i * STEP + 3000
    rec_inj = [{"t_ms": t_mid(30) - 100, "kind": "click_pre"}, {"t_ms": t_mid(30) + 200, "kind": "click", "x": 300, "y": 300}]
    still_tr, still = run_track()
    ok = [
        still == ("away", uag.PHANTOM_MODE) and still_tr.evidence(5)["clean_ms"] >= 300000 and still_tr.evidence(5)["cursor"] == [100, 100],  # 유령 입력+정지 → away
        run_track(n=29)[1] == ("present", "idle"),                                                                 # 5분 미만은 아직 present
        run_track({30: win(30, cursor=[[t_mid(30), 101, 100]])})[1] == ("present", "phantom_violation"),           # 커서 1px 이동 → present
        run_track({30: win(30, keys=[[t_mid(30), [65]]])})[1] == ("present", "phantom_violation"),                 # 키 1개 → present
        run_track({30: win(30, fg=[[t_mid(30), 222, 777]])})[1] == ("present", "phantom_violation"),               # 전경이 다른 창 → present
        run_track({30: win(30, fg=[[t_mid(30), 333, PID_TEST], [t_mid(30) + 500, FG, 999]])})[1] == ("away", uag.PHANTOM_MODE),  # 시험 창 전경·원래 창 복귀는 허용
        run_track({30: win(30, cursor=[[t_mid(30), 300, 300]], keys=[[t_mid(30), [1]]])}, records=rec_inj)[1] == ("away", uag.PHANTOM_MODE),  # 주입 구간 커서·버튼은 허용
        run_track({30: win(30, cursor=[[t_mid(30), 300, 300], [t_mid(30) + 4000, 100, 100]])}, records=rec_inj)[1] == ("away", uag.PHANTOM_MODE),  # 시작 위치로 복원은 허용
        run_track({30: win(30, cursor=[[t_mid(30) + 4000, 400, 400]])}, records=rec_inj)[1] == ("present", "phantom_violation"),  # 주입 끝난 뒤 다른 위치 → present
        run_track(gap_at=20)[1] == ("present", "idle"),                                                             # 감시 공백 20초 → 연속 구간 처음부터
        decide_phantom("away", still_tr, 5) == ("away", "idle"),                                                    # 유휴 기준이 1순위
        decide_phantom("present", still_tr, 5, enabled=False) == ("present", "idle"),                               # 끄면 옛 판정
    ]
    names = ["유령+정지 away", "5분 미만 present", "커서 1px present", "키 1개 present", "전경 변경 present", "시험 전경 허용",
             "주입 구간 허용", "복원 허용", "주입 뒤 이동 present", "공백 초기화", "유휴 1순위", "끄기"]
    for name, passed in zip(names, ok):
        print(("PASS " if passed else "FAIL ") + "phantom " + name)
    return ok


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("cmd", nargs="?", choices=["run", "check"])
    p.add_argument("--state")
    p.add_argument("--flag", help="away 이면 만들고 present 이면 지우는 플래그 파일(기본 상태 파일의 .on, off 이면 미사용)")
    p.add_argument("--threshold", type=float, default=300)
    p.add_argument("--interval", type=float, default=10)
    p.add_argument("--return-below", type=float, default=5)
    p.add_argument("--max-age", type=float, default=60)
    p.add_argument("--phantom-window-min", type=float, default=5, help="유령 입력 보조 판정: 커서 정지·눌린 키 0·전경 불변이 이 분 이상 이어지면 away(기본 5)")
    p.add_argument("--watch-step-ms", type=int, default=20, help="보조 판정 표본 간격 ms(기본 20)")
    p.add_argument("--no-phantom-fallback", action="store_true", help="보조 판정을 끈다(옛 판정과 같다). 환경변수 PD_PHANTOM_FALLBACK=0 도 같다")
    p.add_argument("--self-test", action="store_true")
    a = p.parse_args()
    if a.self_test:
        return self_test()
    if not a.cmd or not a.state:
        p.error("cmd(run|check) 와 --state 필요")
    return run(a) if a.cmd == "run" else check(a)


if __name__ == "__main__":
    sys.exit(main())
