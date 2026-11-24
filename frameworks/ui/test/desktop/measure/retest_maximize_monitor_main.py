#!/usr/bin/env python3
# A01 메인 창 새 판 — --bg-pos 없이(off) 기동해 MainBgPinHook 이 메인 Normal 창 이동을 되돌리지 않게 하고, 메인 창 최대화 소속 모니터 9건을 시험 모니터 우선으로 잰다
"""
사용:
  python3 -B retest_maximize_monitor_main.py --conf <bin/run 복사본> --out-dir <logs/...> --onscreen-approved '<누가·언제>' \
      [--monitor DISPLAY3] [--terminal-monitor DISPLAY2] [--allow-terminal-monitor] [--min-idle 300]
  python3 -B retest_maximize_monitor_main.py --self-test
결과: <out-dir>/maximize_main.json (rows·pending·vacuous·control·foreground·initial_main·userFiles md5).

원본(retest_maximize_monitor.py)·전경 판(retest_maximize_monitor_fg.py)과의 차이(11-09, 두 원본 파일·sha 불변):
- 원본은 --bg-pos <왼쪽 모니터 맨 아래> 로 띄운다. 그러면 MainBgPinHook 이 설치돼 메인 Normal 창 이동(SetWindowPos)을 되돌려 메인 9건을 잴 수 없었다(원인 확정).
  이 판은 --bg-pos off 로 띄워(BgPlacement.Parse("off") = null → Hook 미설치) 이동이 그대로 적용되게 한다.
  run_desktop_bg.sh 의 화면 안 기동 예외(PD_ALLOW_ONSCREEN=1 + 사유 10자 이상, logs/onscreen_runs.log 기록)를 정식 경로로 쓴다. 가드를 우회하지 않는다.
- 메인 창 케이스(각 모니터 안 + 이웃 모니터 걸침 60:40·40:60·50:50)만 잰다. SDI 는 원본·전경 판 몫이다.
- 케이스 순서: 터미널 모니터(--terminal-monitor)와 겹치지 않는 케이스 먼저. 터미널 모니터와 겹치는 케이스는 --allow-terminal-monitor 이고
  그 케이스 직전 user_away_guard 유휴가 --min-idle 초 이상일 때만 잰다. 아니면 재지 않고 pending(사유·시각)으로 남긴다(숨기지 않는다).
- '공허한 PASS' 검출(vacuous): (1) 케이스별 놓기 전 사각형이 요청 좌표로 실제 옮겨졌는가(되돌림 감지), (2) 케이스끼리 놓기 전 사각형이 모두 다른가,
  (3) 케이스들의 다수 겹침 모니터가 2개 이상이면 기대 모니터(MonitorFromRect)도 2개 이상인가. 하나라도 어긋나면 전체 FAIL 이다.
- 전경: 화면 안 기동이라 시험 앱이 전경을 잡을 수 있다. 전경 판처럼 허용 횟수를 기록하고, 기동 직후와 끝에 원래 전경 창·커서를 복원한다.
  기동 직후 메인 창 사각형(initial_main)과 그것이 터미널 모니터와 겹쳤는지를 남긴다.
- 판정식(케이스 row["pass"])은 원본 Session.judge_max 를 그대로 쓴다(완화 없음).
종료 코드: 0=전 케이스 통과·pending 없음, 1=실패 있음(공허 판정 포함), 2=기동·연결 실패, 3=거부(승인 없음·자기 PID 확인 실패), 5=사용자 복귀, 6=통과했으나 pending 남음.
"""
import argparse
import json
import os
import subprocess
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import retest_maximize_monitor as m  # noqa: E402
import retest_maximize_monitor_fg as fg  # noqa: E402

PLACE_TOL = 8  # 요청 좌표와 놓기 전 사각형의 허용 차(px). 되돌림(수백 px)을 잡는 용도라 판정식이 아니다


def dev(device):
    return device.strip("\\.")


def case_rect(x, y, w, h):
    return {"l": x, "t": y, "r": x + w, "b": y + h}


def split_cases(cases, mons, terminal):
    """(먼저 잴 케이스, 터미널 모니터와 겹치는 케이스). 겹침은 overlap_share > 0."""
    first, term = [], []
    for c in cases:
        share = m.overlap_share(case_rect(*c[1:]), mons)
        (term if share.get(terminal, 0) > 0 else first).append(c)
    return first, term


def majority(share):
    best = max(share.items(), key=lambda kv: kv[1]) if share else (None, 0)
    ties = [k for k, v in share.items() if abs(v - best[1]) < 1e-6]
    return None if len(ties) > 1 else best[0]  # 50:50 은 다수 모니터가 없다


def vacuous(rows, requested):
    """공허한 PASS 검출. requested = {case: (x, y)}. 돌려주는 값의 pass 가 False 면 전체 FAIL."""
    placed = []
    for r in rows:
        x, y = requested[r["case"]]
        placed.append({"case": r["case"], "dx": r["before"]["l"] - x, "dy": r["before"]["t"] - y,
                       "ok": abs(r["before"]["l"] - x) <= PLACE_TOL and abs(r["before"]["t"] - y) <= PLACE_TOL})
    rects = [m.rect_tuple(r["before"]) for r in rows]
    majors = {majority(r["share"]) for r in rows} - {None}
    expects = {r["expect"] for r in rows}
    out = {"placed": placed, "placed_all": all(p["ok"] for p in placed), "distinct_before": len(set(rects)) == len(rects),
           "majority_monitors": sorted(majors), "expect_monitors": sorted(dev(e) for e in expects),
           "expect_varies": len(majors) < 2 or len(expects) >= 2, "rows": len(rows)}
    out["pass"] = bool(rows) and out["placed_all"] and out["distinct_before"] and out["expect_varies"]
    return out


def build_cmd(conf, data, profile):
    return ["bash", os.path.join(m.UI, "tools", "run_desktop_bg.sh"), "--conf", conf, "--no-kill", "--", "--multi-instance", "on", "--remote-control",
            "--data-dir", data, "--profile-dir", profile, "--bg-pos", "off"]


def launch_main(conf, out_dir, approved):
    data, profile = os.path.join(out_dir, "data"), os.path.join(out_dir, "profile")
    os.makedirs(data, exist_ok=True)
    os.makedirs(profile, exist_ok=True)
    env = dict(os.environ, PD_ALLOW_ONSCREEN="1", PD_ONSCREEN_REASON="A01 메인 창 최대화 측정(--bg-pos off, MainBgPinHook 미설치), 승인: " + approved)
    res = subprocess.run(build_cmd(conf, data, profile), capture_output=True, text=True, env=env, timeout=120)
    print(res.stdout[-500:], flush=True)
    os.environ["PD_REMOTE_INFO"] = os.path.join(data, "remote-control.json")
    return res.returncode


def idle_ok(away, min_idle):
    try:
        away.check()
        idle = away.read_probe()["idle_ms"] / 1000.0
    except (away.UserReturned, away.GuardUnavailable) as e:
        return False, "가드: %r" % (e,)
    return idle >= min_idle, "유휴 %.0f초(기준 %d초)" % (idle, min_idle)


def run_main_cases(s, cases, mons, a, out, away):
    main = max(s.app_windows(), key=lambda w: (w["rect"]["r"] - w["rect"]["l"]) * (w["rect"]["b"] - w["rect"]["t"]))
    s.main = main["hwnd"]
    out["initial_main"] = {"rect": main["rect"], "monitor": main.get("monitor"), "share": m.overlap_share(main["rect"], mons)}
    out["initial_main"]["on_terminal"] = out["initial_main"]["share"].get(a.terminal_monitor, 0) > 0
    s.hide_all()
    out["cloak"] = "applied"
    first, term = split_cases(cases, mons, a.terminal_monitor)
    out["order"] = {"first": [c[0] for c in first], "terminal": [c[0] for c in term]}
    pending = []
    for name, x, y, w, h in first:
        away.check()
        s.judge_max(name, "main", None, s.main, x, y, w, h)
    for name, x, y, w, h in term:
        ok, why = idle_ok(away, a.min_idle) if a.allow_terminal_monitor else (False, "--allow-terminal-monitor 없음")
        if not ok:
            pending.append({"case": name, "reason": why, "at": time.strftime("%Y-%m-%dT%H:%M:%S")})
            print("PENDING %-22s %s" % (name, why), flush=True)
            continue
        s.judge_max(name, "main", None, s.main, x, y, w, h)
    out["pending"] = pending


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--conf")
    ap.add_argument("--out-dir")
    ap.add_argument("--onscreen-approved", help="실제 모니터 사용 승인 근거(누가·언제, 10자 이상). 없으면 어떤 창도 띄우지 않고 rc=3")
    ap.add_argument("--monitor", default="DISPLAY3", help="시험 모니터(먼저 잴 케이스의 기준, 기록용)")
    ap.add_argument("--terminal-monitor", default="DISPLAY2", help="터미널이 있는 모니터. 겹치는 케이스는 허용·유휴 게이트 뒤에만 잰다")
    ap.add_argument("--allow-terminal-monitor", action="store_true", help="터미널 모니터와 겹치는 케이스를 유휴 게이트 통과 시 잰다")
    ap.add_argument("--min-idle", type=int, default=300, help="터미널 모니터 케이스 직전 필요한 사용자 유휴(초)")
    ap.add_argument("--self-test", action="store_true")
    a = ap.parse_args(argv)
    if a.self_test:
        return self_test()
    if not a.onscreen_approved or len(a.onscreen_approved) < 10:
        print("거부: 시험 창이 사용자 모니터를 차지한다. 리드 승인을 받아 --onscreen-approved '<누가·언제>'(10자 이상)를 준다.")
        return 3
    if not a.conf or not a.out_dir:
        print("거부: --conf·--out-dir 가 필요하다")
        return 3
    a.terminal_monitor = dev(a.terminal_monitor)
    import user_away_guard as away
    if away.user_returned():
        print("거부: 복귀 플래그가 있다")
        return 5
    try:
        away.check()
    except (away.UserReturned, away.GuardUnavailable) as e:
        print("복귀 감지 또는 가드 불가 — 시작하지 않는다:", e)
        return 5
    out_dir = os.path.abspath(a.out_dir)
    os.makedirs(out_dir, exist_ok=True)
    mons = m.wp(0, ["monitors"])[0]["monitors"]
    if isinstance(mons, dict):
        mons = [mons]
    out = {"tool": "retest_maximize_monitor_main", "monitors": mons, "control": [], "userFilesBefore": m.user_files_md5(),
           "monitor": a.monitor, "terminal_monitor": a.terminal_monitor, "allow_terminal_monitor": a.allow_terminal_monitor}
    _, cases = m.positions(mons)
    requested = {c[0]: (c[1], c[2]) for c in cases}
    for name, x, y, w, h in cases:
        out["control"].append({"case": name, **m.wp(0, ["control:%d:%d:%d:%d" % (x, y, w, h)])[0]})
    fg_before = fg.run_ps(["-Action", "read"])
    out["foreground_before"] = fg_before
    rc = launch_main(a.conf, out_dir, a.onscreen_approved)
    if rc != 0:
        print("기동 실패 rc=%d" % rc)
        return 2
    from retest_remote import Remote
    remote = Remote()
    pid = (remote.call("status") or {}).get("pid")
    if not pid:
        print("status 실패", remote.calls[-1:])
        return 2
    exe = subprocess.run(["powershell.exe", "-NoProfile", "-Command", "(Get-Process -Id %d).Path" % pid], capture_output=True, text=True).stdout.strip()
    out["pid"], out["exe"] = pid, exe
    if a.conf.replace("/", "\\") not in exe:
        print("자기 복사본이 아님 — 조작 거부:", pid, exe)
        return 3
    s = m.Session(pid, remote, mons)
    s.allow_foreground = True
    parts = fg_before.split("|")
    restore = lambda: fg.run_ps(["-Action", "restore", "-Hwnd", parts[0], "-X", parts[2], "-Y", parts[3]]) if len(parts) == 4 else "저장값 없음"  # noqa: E731
    code = 0
    try:
        out["restore_after_launch"] = restore()
        run_main_cases(s, cases, mons, a, out, away)
    except (away.UserReturned, away.GuardUnavailable) as ex:
        out["stopped"] = repr(ex)
        print("중단:", ex)
        code = 5
    except m.ForegroundViolation as ex:  # allow_foreground=True 라 나오지 않아야 하지만 원본 경로 그대로 남긴다
        out["foregroundViolation"] = str(ex)
        code = 4
    finally:
        remote.call("app.shutdown")  # 자기 PID(명령줄 확인 끝난 연결)만 끈다
        time.sleep(2)
        out["restore_end"] = restore()
        out["foreground_allowed_hits"] = len(s.foreground_hits)
        out["rows"], out["userFilesAfter"] = s.rows, m.user_files_md5()
        out["vacuous"] = vacuous(s.rows, requested)
        with open(os.path.join(out_dir, "maximize_main.json"), "w", encoding="utf-8") as f:
            json.dump(out, f, ensure_ascii=False, indent=1)
    failed = [r["case"] for r in s.rows if not r["pass"]]
    print("공허 판정:", out["vacuous"]["pass"], " 실패:", failed or "없음", " pending:", [p["case"] for p in out.get("pending", [])] or "없음",
          " 사용자 파일 불변:", out["userFilesBefore"] == out["userFilesAfter"])
    if code:
        return code
    if failed or not out["vacuous"]["pass"] or out["userFilesBefore"] != out["userFilesAfter"]:
        return 1
    return 6 if out.get("pending") else 0


def self_test():
    fails = []

    def expect(name, ok):
        print("OK  " if ok else "FAIL", name)
        if not ok:
            fails.append(name)

    mons = [{"device": "\\\\.\\DISPLAY1", "bounds": {"l": -1920, "t": 0, "r": 0, "b": 1080}, "work": {"l": -1920, "t": 0, "r": 0, "b": 1040}},
            {"device": "\\\\.\\DISPLAY2", "bounds": {"l": 0, "t": 0, "r": 2560, "b": 1440}, "work": {"l": 0, "t": 0, "r": 2560, "b": 1400}},
            {"device": "\\\\.\\DISPLAY3", "bounds": {"l": 2560, "t": 0, "r": 6400, "b": 2160}, "work": {"l": 2560, "t": 0, "r": 6400, "b": 2120}}]
    _, cases = m.positions(mons)
    expect("케이스 9건(모니터 3 + 걸침 2×3)", len(cases) == 9)
    first, term = split_cases(cases, mons, "DISPLAY2")
    expect("터미널 모니터와 겹치지 않는 케이스는 DISPLAY1·DISPLAY3 안 2건", sorted(c[0] for c in first) == ["in_0_DISPLAY1", "in_2_DISPLAY3"])
    expect("DISPLAY2 와 겹치는 케이스 7건은 뒤로", len(term) == 7 and all(c[0].startswith(("in_1", "span")) for c in term))
    cmd = build_cmd("run/x", "d", "p")
    expect("--bg-pos off 로 기동(auto·좌표 아님)", cmd[cmd.index("--bg-pos") + 1] == "off" and cmd.count("--bg-pos") == 1)
    expect("다중 인스턴스·원격 제어 인자 유지", "--multi-instance" in cmd and "--remote-control" in cmd and "--no-kill" in cmd)

    def row(case, l, t, expect_dev, share):
        return {"case": case, "before": {"l": l, "t": t, "r": l + 1400, "b": t + 800}, "expect": "\\\\.\\" + expect_dev, "share": share}
    req = {"a": (100, 100), "b": (3000, 150), "c": (2000, 150)}
    good = [row("a", 100, 100, "DISPLAY2", {"DISPLAY2": 1.0}), row("b", 3000, 150, "DISPLAY3", {"DISPLAY3": 1.0}), row("c", 2000, 150, "DISPLAY2", {"DISPLAY2": 0.6, "DISPLAY3": 0.4})]
    expect("정상 케이스는 공허 판정 통과", vacuous(good, req)["pass"] is True)
    pinned = [row("a", -32000, 2000, "DISPLAY1", {"DISPLAY1": 0.0}), row("b", -32000, 2000, "DISPLAY1", {}), row("c", -32000, 2000, "DISPLAY1", {})]
    v = vacuous(pinned, req)
    expect("되돌림(같은 사각형·요청 좌표 아님)은 FAIL", v["pass"] is False and not v["placed_all"] and not v["distinct_before"])
    same_expect = [row("a", 100, 100, "DISPLAY2", {"DISPLAY2": 1.0}), row("b", 3000, 150, "DISPLAY2", {"DISPLAY3": 1.0}), row("c", 2000, 150, "DISPLAY2", {"DISPLAY2": 0.6, "DISPLAY3": 0.4})]
    expect("다수 모니터가 2개인데 기대 모니터가 1개면 FAIL", vacuous(same_expect, req)["pass"] is False)
    expect("행 0건은 공허 판정 FAIL", vacuous([], req)["pass"] is False)
    expect("50:50 은 다수 모니터 없음", majority({"DISPLAY2": 0.5, "DISPLAY3": 0.5}) is None)
    expect("승인 없으면 rc=3(창 안 띄움)", main(["--conf", "run/x", "--out-dir", "/nonexistent_never_created"]) == 3 and not os.path.exists("/nonexistent_never_created"))
    expect("판정식은 원본 Session.judge_max 그대로", m.Session.judge_max.__module__ == m.__name__ and not hasattr(sys.modules[__name__], "judge_max"))
    print("self-test %s, 실패 %d건" % ("PASS" if not fails else "FAIL", len(fails)))
    return 1 if fails else 0


if __name__ == "__main__":
    sys.exit(main())
