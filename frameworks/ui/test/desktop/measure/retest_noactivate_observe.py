#!/usr/bin/env python3
# MainBgPinHook 의 WS_EX_NOACTIVATE 가 켜져 있는데도 시험 앱이 기동 직후 전경을 잡는 원인 가설(Avalonia 덮어쓰기·Show 활성화·SetForegroundWindow)을 로그와 전경 표본으로 가르는 관찰 도구
"""
목적(계획서 conv_179419201221 7h): 판정 기준은 계획서·03 문서에 없다('기준 없음'). 이 도구는 합격/불합격이 아니라 가설 분류만 한다.
  H_OVERWRITE   : 앱 로그 'main bg exstyle <단계> before/after' 에서 NOACTIVATE(0x08000000) 비트가 켜진 뒤 다음 단계 before 에서 사라졌다(덮어쓰기)
  H_NOT_SET     : 어떤 단계 after 에도 비트가 없다(SetWindowLongPtr 가 먹지 않음)
  H_ACTIVATE    : 비트는 모든 단계에서 유지되는데 앱 PID 가 전경으로 관찰됐다(Show 시 활성화·SetForegroundWindow 직접 호출 — exstyle 로 못 막음)
  NOT_REPRODUCED: 비트 유지 + 앱 PID 전경 관찰 0건
  UNDET         : exstyle 로그 줄이 없거나(훅 미설치 — --bg-pos off 로 띄운 앱 등) 전경 표본이 부족하다(공허한 결론 방지)
사용:
  python3 -B retest_noactivate_observe.py --onscreen-approved '<누가·언제>' --conf <bin/run 복사본> --out-dir <폴더> [--seconds 6] [--interval 0.3]
      기동은 run_desktop_bg.sh 기본(--bg-pos 자동 = 훅 설치, 화면 밖). 전경 표본은 powershell 읽기만(입력 없음). 자기 PID 만 app.shutdown. 끝나면 전경·커서 복원.
  python3 -B retest_noactivate_observe.py --analyze-log <앱 로그> [--fg-samples 표본.json --app-pid N]   (앱 기동 없이 기존 로그 분석)
  python3 -B retest_noactivate_observe.py --self-test
  --judge 를 더하면(기동·--analyze-log 모두) 분류 뒤 판정까지 한다.
rc: 0 분류 완료(H_*·NOT_REPRODUCED), 2 UNDET, 3 승인 누락·거부, 5 복귀 감지·인자 오류
--judge rc: 0 PASS, 1 FAIL, 2 UNDET(미측정), 3·5 는 위와 같다
판정(--judge) 기준 — plan/09_화면설계/03_데스크톱_화면설계.md 'NOACTIVATE 판정 기준'(근거 a7 비활성 조작 행 "`--bg` 기동이면 메인 창도 활성화 없이 뜬다")과 같다:
  PASS  = ① 전경 표본 ≥5 ② 앱 PID 전경 획득 0 ③ 모든 단계 after 에 NOACTIVATE 비트, 단계 사이 소실 없음(분류 NOT_REPRODUCED)
  UNDET = 표본 <5·앱 PID 미확인·exstyle 로그 없음(미측정, PASS 아님)
  FAIL  = 그 밖(H_OVERWRITE·H_NOT_SET·H_ACTIVATE, 또는 어느 단계 after 에 비트 없음)
"""
import argparse
import json
import os
import re
import subprocess
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(HERE, "..", "..", "common"))
import _paths  # noqa: E402

_paths.add()
sys.path.insert(0, _paths.TOOLS)

NOACT = 0x08000000
EXSTYLE = re.compile(r"main bg exstyle (\S+) before=0x([0-9A-Fa-f]+) after=0x([0-9A-Fa-f]+) hadNoActivate=(True|False)")
MIN_SAMPLES = 5


def parse_log(lines):
    """로그 줄 → [{stage, before, after, had}]. 줄이 없으면 빈 목록."""
    out = []
    for ln in lines:
        m = EXSTYLE.search(ln)
        if m:
            out.append({"stage": m.group(1), "before": int(m.group(2), 16), "after": int(m.group(3), 16), "had": m.group(4) == "True"})
    return out


def classify(stages, fg_pids, app_pid):
    """(분류, 근거 dict). fg_pids = 표본별 전경 PID 목록."""
    ev = {"stages": [{"stage": s["stage"], "before": hex(s["before"]), "after": hex(s["after"]), "bit_after": bool(s["after"] & NOACT)} for s in stages],
          "fg_samples": len(fg_pids), "app_pid": app_pid, "app_fg_hits": sum(1 for p in fg_pids if app_pid and p == app_pid)}
    if not stages:
        return "UNDET", dict(ev, reason="exstyle 로그 줄 없음(훅 미설치이거나 로그 경로 오류)")
    if len(fg_pids) < MIN_SAMPLES or not app_pid:
        return "UNDET", dict(ev, reason="전경 표본 %d개(<%d) 또는 앱 PID 미확인" % (len(fg_pids), MIN_SAMPLES))
    if not any(s["after"] & NOACT for s in stages):
        return "H_NOT_SET", ev
    for prev, cur in zip(stages, stages[1:]):
        if prev["after"] & NOACT and not cur["before"] & NOACT:
            return "H_OVERWRITE", dict(ev, lost_between=[prev["stage"], cur["stage"]])
    return ("H_ACTIVATE" if ev["app_fg_hits"] else "NOT_REPRODUCED"), ev


def judge(classification, stages):
    """03 문서 NOACTIVATE 판정 기준 → (PASS|FAIL|UNDET, 사유). 분류는 바꾸지 않고 그 위에 판정만 얹는다."""
    if classification == "UNDET":
        return "UNDET", "미측정(표본 <%d·앱 PID 미확인·exstyle 로그 없음)" % MIN_SAMPLES
    missing = [s["stage"] for s in stages if not s["after"] & NOACT]
    if classification == "NOT_REPRODUCED" and not missing:
        return "PASS", "표본 ≥%d, 앱 전경 0, 모든 단계 NOACTIVATE 유지" % MIN_SAMPLES
    return "FAIL", "분류 %s%s" % (classification, (", 비트 없는 단계 %s" % missing) if missing else "")


def launch_with_hook(conf, out_dir):
    """run_desktop_bg.sh 기본 기동(--bg-pos 미지정 = auto, 훅 설치). (rc, 로그 이름) 을 돌려준다."""
    from retest_user_files import launched_log
    data, profile = os.path.join(out_dir, "data"), os.path.join(out_dir, "profile")
    os.makedirs(data, exist_ok=True)
    os.makedirs(profile, exist_ok=True)
    cmd = ["bash", os.path.join(_paths.TOOLS, "run_desktop_bg.sh"), "--conf", conf, "--no-kill", "--", "--multi-instance", "on", "--remote-control",
           "--data-dir", data, "--profile-dir", profile]
    env = {k: v for k, v in os.environ.items() if k != "PD_VIRTUAL_MONITORS"}
    res = subprocess.run(cmd, capture_output=True, text=True, env=env, timeout=120)
    print(res.stdout[-400:], flush=True)
    os.environ["PD_REMOTE_INFO"] = os.path.join(data, "remote-control.json")
    return res.returncode, launched_log(res.stdout)


def sample_fg(fg, seconds, interval):
    pids, end = [], time.time() + seconds
    while time.time() < end:
        parts = fg.run_ps(["-Action", "read"]).split("|")
        if len(parts) == 4 and parts[1].isdigit():
            pids.append(int(parts[1]))
        time.sleep(interval)
    return pids


def live(a):
    import user_away_guard as away
    import retest_maximize_monitor_fg as fg
    from retest_remote import Remote
    try:
        away.check()
    except (away.UserReturned, away.GuardUnavailable) as e:
        print("복귀 감지 또는 가드 불가 — 시작하지 않는다:", e)
        return 5
    out_dir = os.path.abspath(a.out_dir)
    os.makedirs(out_dir, exist_ok=True)
    before = fg.run_ps(["-Action", "read"])
    rc, log_name = launch_with_hook(a.conf, out_dir)
    if rc != 0 or not log_name:
        print("기동 실패 rc=%s log=%s" % (rc, log_name))
        return 5
    remote, pids, app_pid = None, [], None
    try:
        remote = Remote()
        app_pid = (remote.call("status") or {}).get("pid")
        pids = sample_fg(fg, a.seconds, a.interval)
    finally:
        if remote is not None:
            remote.call("app.shutdown")  # 자기 PID(방금 기동·연결한 앱)만
        time.sleep(2)
        parts = before.split("|")
        if len(parts) == 4:
            print("전경·커서 복원:", fg.run_ps(["-Action", "restore", "-Hwnd", parts[0], "-X", parts[2], "-Y", parts[3]]), flush=True)
    return report(os.path.join(_paths.TOOLS, "..", "logs", log_name), pids, app_pid, out_dir, a.judge)


JUDGE_CRITERIA = ("plan/09_화면설계/03_데스크톱_화면설계.md NOACTIVATE 판정 기준(근거 a7 비활성 조작 행): "
                  "전경 표본 ≥5, 앱 PID 전경 0, 모든 단계 NOACTIVATE 비트 유지(NOT_REPRODUCED). 표본 <5 는 미측정")


def report(log_path, pids, app_pid, out_dir, do_judge=False):
    with open(log_path, encoding="utf-8", errors="replace") as f:
        stages = parse_log(f)
    verdict, ev = classify(stages, pids, app_pid)
    res = {"tool": "retest_noactivate_observe", "criteria": "기준 없음(계획서 7h: 결론만 기록, 가설 분류)", "classification": verdict, "evidence": ev, "log": log_path}
    if do_judge:
        res["judge"], res["judge_reason"] = judge(verdict, stages)
        res["criteria"] = JUDGE_CRITERIA
    if out_dir:
        with open(os.path.join(out_dir, "noactivate.json"), "w", encoding="utf-8") as f:
            json.dump(res, f, ensure_ascii=False, indent=1)
    print(json.dumps(res, ensure_ascii=False)[:800])
    if do_judge:
        return {"PASS": 0, "FAIL": 1}.get(res["judge"], 2)
    return 2 if verdict == "UNDET" else 0


def _cls(lines, pids, app_pid):
    """self-test 도우미: 로그 줄 → (분류, 단계 목록)."""
    stages = parse_log(lines)
    return classify(stages, pids, app_pid)[0], stages


def self_test():
    fails = []

    def expect(name, ok):
        print("OK  " if ok else "FAIL", name)
        if not ok:
            fails.append(name)
    line = "2026-11-09 10:00:00.000 [host] main bg exstyle %s before=0x%X after=0x%X hadNoActivate=%s showInTaskbar=False"
    good = [line % ("opened", 0x100, NOACT | 0x100, False), line % ("pin-end", NOACT | 0x100, NOACT | 0x100, True)]
    lost = [line % ("opened", 0x100, NOACT | 0x100, False), line % ("pin-end", 0x100, NOACT | 0x100, False)]
    unset = [line % ("opened", 0x100, 0x100, False), line % ("pin-end", 0x100, 0x100, False)]
    st = parse_log(good + ["noise"])
    expect("로그 줄 파싱 2건", len(st) == 2 and st[0]["stage"] == "opened" and st[1]["had"] is True)
    many = [1] * 3 + [7] * 3
    expect("비트 유지 + 앱 PID 전경 → H_ACTIVATE", classify(parse_log(good), many, 7)[0] == "H_ACTIVATE")
    expect("비트 유지 + 앱 전경 0 → NOT_REPRODUCED", classify(parse_log(good), [1] * 6, 7)[0] == "NOT_REPRODUCED")
    expect("단계 사이 비트 소실 → H_OVERWRITE", classify(parse_log(lost), many, 7)[0] == "H_OVERWRITE")
    expect("어느 단계도 비트 없음 → H_NOT_SET", classify(parse_log(unset), many, 7)[0] == "H_NOT_SET")
    expect("공허: 로그 줄 없으면 UNDET", classify([], many, 7)[0] == "UNDET")
    expect("공허: 표본 부족 UNDET", classify(parse_log(good), [7], 7)[0] == "UNDET")
    expect("공허: 앱 PID 미확인 UNDET", classify(parse_log(good), many, None)[0] == "UNDET")
    keep = [line % ("install", 0x100, NOACT | 0x100, False),
            "2026-11-09 10:00:00.100 [host] main bg stylechanging keep NOACTIVATE old=0x8000100 new=0x100 kept=0x8000100",
            line % ("opened", NOACT | 0x100, NOACT | 0x100, True), line % ("pin-end", NOACT | 0x100, NOACT | 0x100, True)]
    expect("판정: 유지 로그 + 표본 6 + 앱 전경 0 → PASS", judge(*_cls(keep, [1] * 6, 7))[0] == "PASS")
    expect("판정: 표본 5개 경계 → PASS", judge(*_cls(keep, [1] * 5, 7))[0] == "PASS")
    expect("판정: 표본 4개 → UNDET(미측정)", judge(*_cls(keep, [1] * 4, 7))[0] == "UNDET")
    expect("판정: 앱 전경 1회 → FAIL", judge(*_cls(keep, [1] * 5 + [7], 7))[0] == "FAIL")
    expect("판정: 덮어쓰기 → FAIL", judge(*_cls(lost, [1] * 6, 7))[0] == "FAIL")
    expect("판정: 비트 미설정 → FAIL", judge(*_cls(unset, [1] * 6, 7))[0] == "FAIL")
    expect("판정: 로그 없음 → UNDET", judge(*_cls([], [1] * 6, 7))[0] == "UNDET")
    tail_lost = keep + [line % ("late", NOACT | 0x100, 0x100, True)]
    expect("판정: 마지막 단계 after 비트 없음 → FAIL", judge(*_cls(tail_lost, [1] * 6, 7))[0] == "FAIL")
    expect("분류 유지: keep 로그는 NOT_REPRODUCED", _cls(keep, [1] * 6, 7)[0] == "NOT_REPRODUCED")
    expect("승인 없으면 rc 3", main(["--conf", "x", "--out-dir", "/nonexistent_never_created"]) == 3 and not os.path.exists("/nonexistent_never_created"))
    print("self-test %s, 실패 %d건" % ("PASS" if not fails else "FAIL", len(fails)))
    return 1 if fails else 0


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--onscreen-approved", help="전경 점유를 사용자가 감수한다는 승인 근거(누가·언제, 10자 이상). 없으면 앱을 띄우지 않고 rc=3")
    ap.add_argument("--conf")
    ap.add_argument("--out-dir")
    ap.add_argument("--seconds", type=float, default=6.0)
    ap.add_argument("--interval", type=float, default=0.3)
    ap.add_argument("--analyze-log")
    ap.add_argument("--fg-samples", help="전경 PID 표본 JSON 배열(--analyze-log 용)")
    ap.add_argument("--app-pid", type=int)
    ap.add_argument("--judge", action="store_true", help="분류 뒤 03 문서 기준으로 PASS/FAIL/UNDET 판정(rc 0/1/2)")
    ap.add_argument("--self-test", action="store_true")
    a = ap.parse_args(argv)
    if a.self_test:
        return self_test()
    if a.analyze_log:  # 앱을 띄우지 않는 분석
        pids = json.load(open(a.fg_samples, encoding="utf-8")) if a.fg_samples else []
        return report(a.analyze_log, pids, a.app_pid, None, a.judge)
    if not a.onscreen_approved or len(a.onscreen_approved) < 10:
        print("거부: 시험 앱 기동이 사용자 전경을 가져갈 수 있다. --onscreen-approved '<누가·언제>'(10자 이상)가 필요하다.")
        return 3
    if not a.conf or not a.out_dir:
        print("거부: --conf·--out-dir 가 필요하다")
        return 3
    return live(a)


if __name__ == "__main__":
    sys.exit(main())
