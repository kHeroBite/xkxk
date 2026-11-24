#!/usr/bin/env python3
# RETEST MDI-Z 단계 — 창 5개의 앞뒤·항상 위·활성·위치 고정을 정한 뒤 저장 → 앱 재기동 → 복원하고 Z순서·활성·핀·잠금 복원을 원격 명령으로 대조
"""
사용: python3 test/desktop/measure/retest_remote_z.py [--mode explicit|user] [--out 결과.json] [--skip restart] [--conf Release] [--profile-dir 임시폴더]
  explicit(기본): layout.save → app.shutdown → 재기동(--fresh) → layout.load. --skip restart 는 재기동 없이 닫기→load 만 대조.
  user: 메인 창 닫기(app.closeMain, 종료 자동 저장) → 재기동 → 자동 복원을 layout.load 없이 대조(Program.cs 가 --remote-control 을 인자에서 빼는 빌드 전제).
        --profile-dir <비어 있는 새 임시 폴더 절대경로> 필수, 없으면 rc=3 거부(사용자 layout.json·settings.json 불접촉).
  --isolated: 사용자 폴더 layout·settings 백업·복원을 생략한다. 연결된 앱 PID 명령줄에 사용자 폴더(하위·정션 포함)가 아닌 --data-dir 가
        있을 때만 실행(--profile-dir 만으로는 격리 아님), 확인 못 하면 rc=3 거부. 재기동 때 첫 기동의 --data-dir·--multi-instance 를 넘기고
        새 앱도 같은 조건을 다시 확인한다. 실패하면 이 도구가 띄운 앱(run_desktop_bg 출력 로그 이름이 명령줄에 있는 PID)만 내린다.
  비격리(기본): 사용자 Pd(run_desktop_bg 로 띄우지 않은 Pd)가 떠 있으면 실행 전에 rc=3 거부 — --isolated 를 쓴다.
재기동은 run_desktop_bg.sh --no-kill(같은 이미지 전체 종료 없음), 앱 인자에 항상 --offscreen-guard on. 호출자는 PD_SLOT_OWNER 를 준다.
판정 ① 저장값 ZOrder 순서·ActiveId ② 화면 쌓임(docs.list zIndex·childIndex — 핀 창 맨 위, 활성 창은 핀 그룹 바로 아래) ③ IsLocked·IsPinned 복원.
문서는 제목(Z1~Z5)으로 대조한다(복원 뒤 Id 가 바뀌어도 판정 가능). 종료 코드 0=전부 통과, 1=실패, 2=연결·재기동 실패.
"""
import argparse
import datetime
import json
import os
import shutil
import subprocess
import sys
import time

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "common"))  # D-TESTDIR: test/common/_paths.py
import _paths  # noqa: E402
_paths.add()
TOOLS = _paths.TOOLS  # 옛 위치(tools) — run_desktop_bg.sh·pdctl.py·logs 경로 기준 그대로
sys.path.insert(0, TOOLS)
import pdctl  # noqa: E402
from retest_remote import Remote  # noqa: E402  호출·검사 기록 재사용
from retest_user_files import USER_FILES, backup_user_files, user_md5, wait_app_exit  # noqa: E402  사용자 파일 백업·md5(공용)
from retest_user_files import isolation, launched_by, launched_log, relaunch_args, stop_pid, user_pds  # noqa: E402  --isolated 격리 확인·자기 PID 종료·사용자 Pd 확인(공용)
from retest_remote_z_docs import TITLES, by_title, chrome_flags, docs_with_z, panel_tabs, saved_order, screen_order  # noqa: E402  문서 목록 해석

PIN, ACTIVE, LOCK = "Z2", "Z4", "Z3"  # 항상 위 1개, 활성(핀 없음) 1개, 위치 고정 1개
SHUFFLE = ["Z3", "Z1", "Z5", "Z2", "Z1", "Z3"]  # 활성화 순서로 앞뒤 섞기(마지막에 ACTIVE 활성)


PROFILE = None  # user 모드 전용 임시 프로필 폴더(WSL 절대경로)
ISO = None  # --isolated: 첫 앱 격리 확인 상세(args 에 첫 기동 --data-dir 등). None 이면 격리 모드 아님


def launch_args(user):  # 앱 인자 — 항상 --offscreen-guard on, explicit=--fresh, user=임시 --profile-dir
    args = ["--remote-control", "--offscreen-guard", "on"]
    if ISO:  # 격리 모드: 첫 기동 데이터 폴더·단일 인스턴스 우회를 그대로(user 모드의 --profile-dir 는 아래 PROFILE 이 준다)
        args += relaunch_args({k: v for k, v in ISO["args"].items() if not (user and k == "--profile-dir")})
    if user:
        win = subprocess.run(["wslpath", "-w", PROFILE], capture_output=True).stdout.decode().strip()
        return args + ["--profile-dir", win]
    return args + ["--fresh"]


def alive(info):
    """토큰 없이 /api/health 만 확인(실패해도 종료하지 않는다)."""
    curl = pdctl.CURL_EXE if os.path.exists(pdctl.CURL_EXE) else "curl"
    try:
        p = subprocess.run([curl, "-s", "--max-time", "3", "http://localhost:%d/api/health" % info["port"]],
                           capture_output=True, timeout=8)
        return json.loads(p.stdout.decode("utf-8", "replace") or "{}").get("result")
    except (subprocess.TimeoutExpired, ValueError):
        return None


def prepare(r):
    print("[Z1] 창 5개 준비")
    r.call("docs.close", all=True)
    r.call("docs.arrange", preset="MdiCascade")
    for t in TITLES:
        r.call("docs.open", kind="grid", title=t, allowDuplicate=True)
    ids = {t: d["id"] for t, d in by_title(r.call("docs.list")).items()}
    r.call("chrome.pin", doc=ids[PIN])
    r.call("chrome.lock", doc=ids[LOCK])
    for t in SHUFFLE + [ACTIVE]:
        r.call("docs.activate", doc=ids[t])
    docs = docs_with_z(r, "준비")
    expect = {
        "screen": screen_order(docs),
        "flags": chrome_flags(r, docs),
        "panelTabs": panel_tabs(docs),  # 패널 탭 문서 별도 목록(기록 전용)
    }
    layout = r.call("layout.get")
    expect["saved"], expect["active"], expect["savedFlags"] = saved_order(layout)
    r.check("준비: 활성 = %s" % ACTIVE, next((d["title"] for d in docs if d.get("active")), None) == ACTIVE)
    r.check("준비: 핀 창 맨 위", expect["screen"][-1:] == [PIN], expect["screen"])
    r.check("준비: 활성 창은 핀 바로 아래", expect["screen"][-2:-1] == [ACTIVE], expect["screen"])
    print("  기대 화면 쌓임(아래→위) %s / 저장 ZOrder %s / 활성 %s" % (expect["screen"], expect["saved"], expect["active"]))
    return expect


def stop(r, user):
    """user: 메인 창 닫기(사용자 X 와 같은 경로, 종료 자동 저장) / 아니면 명시 저장 뒤 app.shutdown."""
    if user:
        r.call("app.closeMain")
    else:
        r.call("layout.save")
        r.call("app.shutdown")
    for _ in range(30):
        if alive(r.info) is None:
            return True
        time.sleep(1)
    return False


def launch(r, conf, user, label):
    """
    재기동(team-lead 결정: Program.cs 가 --remote-control 을 앱 인자에서 빼고, --fresh 가 자동 복원·저장을 끈다).
    user: --remote-control 만 → 사용자 실행과 같다(종료 자동 저장·기동 자동 복원).
    explicit: --remote-control --fresh → 자동 복원·저장 없이 깨끗하게 시작, 명시 save/load 로만 대조.
    전제: Program.cs 의 --remote-control 제거가 빌드에 들어 있어야 user 가 성립한다(미반영이면 자동 저장이 꺼져 ①이 실패한다).
    """
    old_pid = r.info.get("pid")
    env = dict(os.environ)
    app_args = launch_args(user)
    run = subprocess.run(["bash", os.path.join(TOOLS, "run_desktop_bg.sh"), "--conf", conf, "--no-kill", "--"] + app_args,
                         capture_output=True, timeout=80, env=env)
    out = run.stdout.decode("utf-8", "replace")
    print("  " + out.strip().replace("\n", "\n  "))
    for _ in range(60):
        try:
            info = pdctl.load_info()
        except SystemExit:
            info = None
        if info and info.get("pid") != old_pid and alive(info):
            if ISO:  # 재기동한 앱도 격리 조건을 다시 확인 — 아니면 연결하지 않는다
                ok, detail = isolation(info.get("pid"))
                if not ok:
                    r.check("%s 격리 확인" % label, False, detail)
                    log_name = launched_log(out)  # run_desktop_bg 는 Windows PID 를 출력하지 않는다 — 고유 로그 이름으로 이 도구가 띄운 앱인지 대조
                    if launched_by(info.get("pid"), log_name):  # 이 도구가 띄운 앱일 때만 내린다 — 사용자 Pd 는 건드리지 않는다
                        print("  이 도구가 띄운 앱(pid %s, %s) 종료: %s" % (info.get("pid"), log_name, stop_pid(info.get("pid"), timeout=1)))
                    else:
                        print("  종료하지 않음 — pid %s 가 이 도구가 띄운 앱인지 확인 못 함(%s)" % (info.get("pid"), log_name))
                    return False
            r.info = info
            r.check("%s(새 pid %s)" % (label, info.get("pid")), True)
            return True
        time.sleep(1)
    r.check(label, False, out[-300:])
    return False


def verify(r, expect, user):
    print("[Z3] 복원 대조(%s)" % ("자동 복원 — layout.load 없음" if user else "layout.load"))
    if not user:
        loaded = r.call("layout.load")
        r.check("layout.load 복원됨", loaded is True, loaded)
    r.call("wait.for", _timeout=20, docCount=len(TITLES), timeout=10000)
    saved, active, saved_flags = saved_order(r.call("layout.get"))
    r.check("① 저장 ZOrder 순서", saved == expect["saved"], "%s / 기대 %s" % (saved, expect["saved"]))
    r.check("① ActiveId", active == expect["active"] == ACTIVE, "%s / 기대 %s" % (active, expect["active"]))
    docs = docs_with_z(r, "복원 후")
    screen = screen_order(docs)
    r.check("② 화면 쌓임 순서", screen == expect["screen"],
            "%s / 기대 %s / 패널 탭 %s" % (screen, expect["screen"], panel_tabs(docs)))
    r.check("② 핀 맨 위·활성 그 아래", screen[-2:] == [ACTIVE, PIN], screen)
    flags = chrome_flags(r, docs)
    for t in TITLES:
        r.check("③ %s 핀·잠금 복원" % t, flags.get(t) == expect["flags"].get(t),
                "%s / 기대 %s" % (flags.get(t), expect["flags"].get(t)))
    r.check("③ 저장 IsPinned·IsLocked", saved_flags == expect["savedFlags"], saved_flags)


def main(argv):
    p = argparse.ArgumentParser(description="RETEST MDI-Z 재기동 Z순서")
    p.add_argument("--out", default="retest_remote_z_%s.json" % datetime.datetime.now().strftime("%Y%m%d_%H%M%S"))
    p.add_argument("--skip", default="", help="restart 를 주면 재기동 없이 같은 프로세스에서 복원만")
    p.add_argument("--conf", default="Release")
    p.add_argument("--mode", choices=["explicit", "user"], default="explicit",
                   help="explicit=save/load 명시, user=메인 창 닫기 자동 저장 → 인자 없는 재기동 자동 복원")
    p.add_argument("--profile-dir", default="", help="user 모드 필수: 비어 있는 새 임시 폴더(절대경로). 없으면 rc=3 으로 거부")
    p.add_argument("--isolated", action="store_true",
                   help="사용자 폴더 백업·복원 생략. 앱 명령줄에 사용자 폴더가 아닌 --data-dir 가 있을 때만 실행(--profile-dir 만으로는 격리 아님, 확인 불가면 rc=3)")
    a = p.parse_args(argv)
    user = a.mode == "user"
    global PROFILE, ISO
    if user:  # 사용자 layout.json·settings.json 을 건드리지 않도록 임시 프로필일 때만 허용(team-lead 03:3x)
        if not (a.profile_dir and os.path.isabs(a.profile_dir) and os.path.isdir(a.profile_dir) and not os.listdir(a.profile_dir)):
            print("거부: --mode user 는 비어 있는 새 임시 폴더(절대경로)를 --profile-dir 로 줄 때만 실행한다")
            return 3
        PROFILE = a.profile_dir
    # explicit 의 layout.save 는 자동 슬롯(layout.json)을 덮는다(01:33 실측) → 두 모드 모두 앱을 띄우기 '전' 에 백업, 끝나면(실패해도) 앱을 내리고 복원.
    # 먼저 띄운 앱이 기동 중 layout.json 을 다시 써서 원본이 아닌 값이 백업된 일(01:32)이 있어 백업은 반드시 기동 전이다.
    # --isolated 는 사용자 폴더를 건드리지 않는다(백업·복원 생략) — 대신 아래에서 앱 격리를 확인하고 아니면 거부한다.
    if not a.isolated:  # 비격리 모드는 사용자 폴더를 백업·복원하고 Pd 0개까지 기다린다 — 사용자 Pd 가 떠 있으면 실행 전에 거부
        upd = user_pds()
        if upd:
            print("거부: 사용자 Pd 가 떠 있다 %s — 비격리 모드는 사용자 Pd 0개일 때만 실행한다. 사용자 Pd 를 그대로 두려면 --isolated 를 써라" % [p for p, _ in upd])
            return 3
    backup = None if a.isolated else backup_user_files(None, a.out)
    md5_before = user_md5(backup) if backup else None
    launched = False
    try:
        r = Remote()
    except SystemExit:  # 앱이 꺼져 있으면 사용자 파일을 쓰지 않는 --fresh 로 띄운다(user 모드는 run 의 Z0 에서 사용자 조건으로 다시 띄운다)
        subprocess.run(["bash", os.path.join(TOOLS, "run_desktop_bg.sh"), "--conf", a.conf, "--no-kill", "--"] + launch_args(False),
                       capture_output=True, timeout=80)
        launched = True
        for _ in range(40):
            try:
                r = Remote()
                break
            except SystemExit:
                time.sleep(1)
        else:
            return 2
    for _ in range(30):
        if r.call("status") is not None:
            break
        time.sleep(1)
    else:
        return 2
    if a.isolated:
        ok, detail = isolation(r.info.get("pid"))
        if not ok:
            print("거부: --isolated 는 앱이 사용자 폴더가 아닌 --data-dir/--profile-dir 로 떠 있을 때만 실행한다 — %s" % detail)
            if launched:  # 이 도구가 띄운 앱만 내린다
                r.call("app.shutdown")
                stop_pid(r.info.get("pid"))
            return 3
        ISO = detail
        print("  격리 확인: %s" % detail)
    try:
        return run(a, r, user)
    finally:
        if ISO:  # 사용자 파일 복원 없음 — 격리 확인된 시험 앱(PID 하나)만 내린다
            if alive(r.info) is not None:
                r.call("app.shutdown")
            stop_pid(r.info.get("pid"))
        else:
            restore_user_files(r, backup)
        data = {"mode": a.mode, "calls": r.calls}
        if os.path.exists(a.out):  # run 이 예외로 끝났으면 결과 파일이 없을 수 있다
            with open(a.out, encoding="utf-8") as f:
                data = json.load(f)
        data["checks"] = r.checks  # 복원 검사 결과까지 포함해 다시 쓴다
        if ISO:
            data["userFiles"] = {"skipped": "isolated", "isolation": ISO}
            print("  사용자 파일 백업·복원 생략(--isolated)")
        else:
            md5_after = user_md5(backup)  # lead 01:4x: 시작 전·종료 후 md5 기록
            data["userFiles"] = {"md5_before": md5_before, "md5_after": md5_after, "same": md5_before == md5_after}
            print("  사용자 파일 md5 %s → %s (%s)" % (md5_before, md5_after, "같음" if md5_before == md5_after else "다름"))
        with open(a.out, "w", encoding="utf-8") as f:
            json.dump(data, f, ensure_ascii=False, indent=2)
        print("  %s — 앱은 종료 상태다(슬롯 반납 전 확인)" % ("시험 앱 종료 완료(--isolated)" if ISO else "사용자 파일 복원 완료"))


def run(a, r, user):
    if user:
        # 지금 앱이 --remote-control 인자로 떠 있으면 자동 저장이 꺼져 있다 → 사용자와 같은 조건으로 먼저 다시 띄운다
        print("[Z0] 사용자 조건(인자 없음)으로 기동")
        r.call("app.shutdown")
        if not (stop_wait(r) and launch(r, a.conf, True, "사용자 조건 기동")):
            return save_fail(a, {}, r)
        r.call("wait.for", _timeout=20, control="shell.dock", timeout=10000)
    expect = prepare(r)
    if "restart" in a.skip and not user:
        r.call("layout.save")
        r.call("docs.close", all=True)
    elif not (stop(r, user) and launch(r, a.conf, user, "재기동")):
        return save_fail(a, expect, r)
    verify(r, expect, user)
    failed = [c for c in r.checks if not c["pass"]] + [c for c in r.calls if not c["ok"]]
    with open(a.out, "w", encoding="utf-8") as f:
        json.dump({"mode": a.mode, "expect": expect, "calls": r.calls, "checks": r.checks,
                   "summary": {"checks": len(r.checks), "failed": len(failed)}}, f, ensure_ascii=False, indent=2)
    print("\n검사 %d · 실패 %d → %s" % (len(r.checks), len(failed), os.path.abspath(a.out)))
    return 0 if not failed else 1


def restore_user_files(r, backup):
    """user 모드 끝: 앱을 app.shutdown(자동 저장 없음)으로 내린 뒤 백업으로 되돌린다. 원래 없던 파일은 지운다."""
    src, dst, saved = backup
    if alive(r.info) is not None:
        r.call("app.shutdown")
        stop_wait(r)
    wait_app_exit(30)  # 다른 pid(재기동분)까지 Pd.Poc.Desktop 0개 확인 뒤 복원(02:15 사고)
    for name, existed in saved.items():
        p = os.path.join(src, name)
        if existed:
            shutil.copy2(os.path.join(dst, name), p)
        elif os.path.exists(p):
            os.remove(p)
    r.check("사용자 파일 복원(%s)" % ", ".join(USER_FILES), alive(r.info) is None, "%s ← %s" % (src, dst))


def stop_wait(r):
    for _ in range(30):
        if alive(r.info) is None:
            return True
        time.sleep(1)
    return False


def save_fail(a, expect, r):
    with open(a.out, "w", encoding="utf-8") as f:
        json.dump({"mode": a.mode, "expect": expect, "calls": r.calls, "checks": r.checks}, f,
                  ensure_ascii=False, indent=2)
    print("연결·재기동 실패 → %s" % os.path.abspath(a.out))
    return 2


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
