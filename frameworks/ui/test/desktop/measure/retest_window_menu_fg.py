# Mars 창 메뉴 4~7(메인 창 초기화·싱글/멀티 모니터 최대화·SDI 위치 초기화) HOLD 를 풀어 전경 허용으로 측정하는 새 판
"""
원본 retest_window_menu.py·retest_window_menu_items.py·retest_window_menu_sdi.py 는 수정하지 않는다(sha 유지).
원본은 데스크톱에서 4~7 번을 창을 열기 전에 HOLD 로 돌려보낸다(--primary-override 미동작 → 시험 창이 사용자 화면에 나옴).
이 판은 실행 중 메모리에서만 다음 세 가지를 바꾼다. 판정 기준·판정식은 원본 그대로다.
  1) retest_window_menu_items.desktop_hold → 항상 None (4·5·6 의 HOLD 해제)
  2) retest_window_menu_sdi.it_sdi_reset 의 HOLD 분기 한 줄만 `if False:` 로 바꾼 복사본 (7 의 HOLD 해제, 소스 문자열 치환)
  3) restart() 동안만 primary_override 를 비운다(앱이 못 지키는 인자를 재기동 명령줄에 넘기지 않기 위해)
대상 작업 영역: --fg-area x,y,w,h (물리 px, 시험 모니터 DISPLAY3 작업 영역) 를 ⑦ 판정 영역으로 쓴다(원본 --primary-override 의
  _override_arg 거부를 우회하지 않고, 인자 해석 뒤 args 에 직접 넣는다). 생략하면 주 모니터 작업 영역이 영역이 된다.
안전: --onscreen-approved 가 없으면 거부(rc 4) — 시험 창이 화면에 나온다는 승인. 측정 전 전경 창·커서를 읽고 끝나면(실패해도) 복원한다.
A07 보정(11-09): 격리 기동을 이 도구가 직접 한다(--launch). mdi_drag_app.launch 는 --primary-override·PD_VIRTUAL_MONITORS=2 를 강제해
  앱 주 모니터가 가상 영역이 되므로 쓰지 않는다. 대신 retest_maximize_monitor_main.build_cmd(run_desktop_bg.sh --bg-pos off, 화면 안 기동 예외
  PD_ALLOW_ONSCREEN=1 + PD_ONSCREEN_REASON)를 재사용한다(기동기 무수정). 가상 모니터·primary-override 는 쓰지 않는다.
  --fg-area 를 생략하면 --monitor(기본 DISPLAY3) 실제 모니터 작업 영역을 실측해 ⑦ 판정 영역으로 쓴다. 터미널 모니터(기본 DISPLAY2)와 겹치거나
  실제 모니터 안이 아니면 거부(rc 5, 공허한 PASS 방지). 재기동(restart)도 같은 화면 안 경로(--bg-pos off + 화면 안 허용 환경)로 한다.
사용: python3 -B retest_window_menu_fg.py --onscreen-approved --launch --conf <bin/run 복사본> --out-dir <폴더> [--monitor DISPLAY3] [--terminal-monitor DISPLAY2] --isolated [--only 4,5,6,7] [원본 인자...]
      python3 -B retest_window_menu_fg.py --onscreen-approved --fg-area X,Y,W,H --isolated ...   (이미 떠 있는 격리 앱에 연결, 옛 방식)
      python3 -B retest_window_menu_fg.py --self-test   (앱·입력 없음)
A07 #7 보정(odev-12): 기동 직후·재기동 직후 앱 메인 창의 실제 모니터를 확인해 시험 모니터가 아니면 SetWindowPos(NOACTIVATE)로 옮기고, 그래도 아니면 측정 없이 UNDET(rc 2).
  전경 복원: 측정 전 전경이 자기 앱이면 hwnd 복원 대신 커서만 복원(restored|False 오탐 방지).
rc: 원본 규칙(0 통과 / 1 FAIL / 2 통과 아님) + 3 격리 거부 + 4 승인 누락 + 5 인자 오류
"""
import argparse
import hashlib
import inspect
import os
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(HERE, "..", "..", "common"))
import _paths  # noqa: E402

_paths.add()
sys.path.insert(0, _paths.TOOLS)

HOLD_LINE = 'if getattr(ctx, "target", None) == "desktop":'  # 원본 sdi.it_sdi_reset 의 HOLD 분기 조건(정확히 한 번 나와야 한다)
ORIGINALS = ("retest_window_menu.py", "retest_window_menu_items.py", "retest_window_menu_sdi.py")


def sha(name):
    with open(os.path.join(HERE, name), "rb") as f:
        return hashlib.sha256(f.read()).hexdigest()


def patch_sdi(sdi):
    """sdi.it_sdi_reset 소스에서 HOLD 분기 조건만 `if False:` 로 바꾼 복사본을 모듈에 올린다. 못 바꾸면 False."""
    src = inspect.getsource(sdi.it_sdi_reset)
    if src.count(HOLD_LINE) != 1:
        return False
    exec(compile(src.replace(HOLD_LINE, "if False:"), sdi.__file__, "exec"), sdi.__dict__)  # 모듈 전역에서 함수 재정의
    return True


def apply_patches():
    import retest_window_menu as menu
    import retest_window_menu_items as items
    import retest_window_menu_sdi as sdi
    items.desktop_hold = lambda ctx: None
    ok = patch_sdi(sdi)
    orig_restart = menu.restart

    class _Sub:  # menu.subprocess 대리 — run_desktop_bg.sh 호출에만 --bg-pos off 를 넣는다(기동기 무수정)
        def __getattr__(self, n):
            return getattr(subprocess, n)

        def run(self, cmd, *a, **kw):
            return subprocess.run(inject_bg_off(cmd), *a, **kw)

    def restart(ctx):  # 앱이 지키지 못하는 --primary-override 를 재기동 명령줄에 넣지 않는다 + 화면 안 기동 허용
        saved, ctx.args.primary_override = ctx.args.primary_override, None
        old_sub, menu.subprocess = menu.subprocess, _Sub()
        env_new = onscreen_env(ctx.args)
        env_old = {k: os.environ.get(k) for k in env_new}
        os.environ.update(env_new)
        try:
            okr = orig_restart(ctx)
            if okr and TARGET:  # 재기동 앱도 기본 위치(주 모니터)로 뜬다 — 시험 모니터에 있는지 확인·이동, 아니면 UNDET
                import retest_maximize_monitor as mm0
                pid = ctx.r.info.get("pid")
                OWN_PIDS.add(pid)
                good, why = ensure_on_monitor(pid, TARGET["monitor"], TARGET["area"], mm0.wp)
                print("재기동 직후 모니터 확인:", why, flush=True)
                if not good:
                    UNDET.append(why)
                    return False
            return okr
        finally:
            menu.subprocess = old_sub
            for k, v in env_old.items():
                if v is None:
                    os.environ.pop(k, None)
                else:
                    os.environ[k] = v
            ctx.args.primary_override = saved
    menu.restart = restart
    return menu, ok


def onscreen_env(args):
    who = getattr(args, "onscreen_who", None) or "A07 창 메뉴 4~7 측정 승인"
    return {"PD_ALLOW_ONSCREEN": "1", "PD_ONSCREEN_REASON": "A07 창 메뉴 4~7 실제 모니터 안 격리 측정(--bg-pos off), 승인: " + who}


def inject_bg_off(cmd):
    """run_desktop_bg.sh 명령줄에 --bg-pos 가 없으면 `--bg-pos off` 를 앱 인자 끝에 넣는다(그 밖의 명령은 그대로)."""
    if isinstance(cmd, (list, tuple)) and any(str(c).endswith("run_desktop_bg.sh") for c in cmd) and "--bg-pos" not in cmd:
        return list(cmd) + ["--bg-pos", "off"]
    return cmd


def dev(device):
    return device.strip("\\.")


def monitor_area(mons, name):
    """실측 모니터 목록에서 name(예 DISPLAY3) 의 작업 영역 (x,y,w,h). 없으면 None."""
    for mo in mons:
        if dev(mo["device"]) == name:
            w = mo["work"]
            return (w["l"], w["t"], w["r"] - w["l"], w["b"] - w["t"])
    return None


def area_problem(area, mons, terminal):
    """⑦ 판정 영역이 터미널 모니터와 겹치거나 실제 모니터 안이 아니면 사유, 괜찮으면 None(공허한 PASS 방지)."""
    x, y, w, h = area
    inside = False
    for mo in mons:
        b = mo["bounds"]
        if dev(mo["device"]) == terminal and min(x + w, b["r"]) - max(x, b["l"]) > 0 and min(y + h, b["b"]) - max(y, b["t"]) > 0:
            return "터미널 모니터 %s 와 겹친다" % terminal
        if x >= b["l"] and y >= b["t"] and x + w <= b["r"] and y + h <= b["b"]:
            inside = True
    return None if inside else "실제 모니터 안에 들어가지 않는다(가상 영역 의심)"


OWN_PIDS = set()  # 이 도구가 띄운(또는 재기동한) 앱의 Windows PID — 측정 전 전경이 이 앱이면 끝난 뒤 복원 대상이 사라진다
TARGET = {}  # --launch 일 때 {"monitor": 시험 모니터명, "area": 작업 영역} — 재기동 뒤에도 같은 모니터를 확인한다
UNDET = []  # 앱이 시험 모니터에 놓이지 않아 측정하지 않은 사유(비어 있지 않으면 rc 2)


def main_window(wins):
    """list 결과에서 메인 창 후보(그림자 아님, 폭 100 초과 중 가장 넓은 창). 없으면 None."""
    cand = [w for w in wins if w.get("cls") != "SHADOW" and w["rect"]["r"] - w["rect"]["l"] > 100]
    return max(cand, key=lambda w: (w["rect"]["r"] - w["rect"]["l"]) * (w["rect"]["b"] - w["rect"]["t"])) if cand else None


def move_target(area, rect):
    """시험 모니터 작업 영역 안(왼쪽 위 +40)에 창 크기를 유지해 놓을 위치. 영역보다 크면 영역에 맞춘다."""
    x, y, w, h = area
    cw, ch = min(rect["r"] - rect["l"], w - 80), min(rect["b"] - rect["t"], h - 80)
    return x + 40, y + 40, cw, ch


def ensure_on_monitor(pid, monitor, area, wp, sleep=None):
    """기동 직후 실제 모니터 확인 → 다르면 SetWindowPos(NOACTIVATE)로 시험 모니터에 옮기고 다시 확인.
    돌려주는 값 (ok, 사유). ok 가 False 면 측정하지 않는다(UNDET). wp(pid, ops)·sleep 은 주입(self-test 용)."""
    import time
    sleep = sleep or time.sleep
    for attempt in (0, 1):
        res = wp(pid, ["list"])[0]
        w = main_window(res.get("windows", []))
        if w is None:
            return False, "메인 창을 찾지 못했다(pid %s)" % pid
        mon = dev(str(w.get("monitor", "")))
        if mon == monitor:
            return True, "메인 창 %s 모니터 %s 확인(시도 %d)" % (w["rect"], mon, attempt)
        if attempt == 1:
            return False, "이동 뒤에도 메인 창이 %s 에 있다(기대 %s): %s" % (mon, monitor, w["rect"])
        x, y, cw, ch = move_target(area, w["rect"])
        wp(pid, ["move:%d:%d:%d:%d:%d" % (w["hwnd"], x, y, cw, ch)])
        sleep(1.0)
    return False, "도달 불가"


def pid_of_launch(out):
    """run_desktop_bg.sh 출력의 `PID=<숫자>` (Windows PID). 없으면 None."""
    import re
    m = re.search(r"^PID=(\d+)", out or "", re.M)
    return int(m.group(1)) if m else None


def launch(a, out_dir):
    """화면 안 격리 기동(--bg-pos off, 가상 모니터·primary-override 없음). rc 0 이면 PD_REMOTE_INFO 를 설정한다."""
    import retest_maximize_monitor_main as mm
    data, profile = os.path.join(out_dir, "data"), os.path.join(out_dir, "profile")
    os.makedirs(data, exist_ok=True)
    os.makedirs(profile, exist_ok=True)
    env = {k: v for k, v in os.environ.items() if k != "PD_VIRTUAL_MONITORS"}
    env.update(onscreen_env(a))
    res = subprocess.run(mm.build_cmd(a.conf, data, profile), capture_output=True, text=True, env=env, timeout=120)
    print(res.stdout[-500:], flush=True)
    os.environ["PD_REMOTE_INFO"] = os.path.join(data, "remote-control.json")
    pid = pid_of_launch(res.stdout)
    if res.returncode != 0 or pid is None:
        return res.returncode or 1
    OWN_PIDS.add(pid)
    import retest_maximize_monitor as mm0
    area = getattr(a, "area", None)  # 다른 도구(C1·넓은 창)가 launch 를 재사용할 때는 area 가 없다 — 시험 모니터 작업 영역을 실측한다
    if area is None:
        mons = mm0.wp(0, ["monitors"])[0]["monitors"]
        area = monitor_area([mons] if isinstance(mons, dict) else mons, dev(getattr(a, "monitor", "DISPLAY3")))
    if area is None:
        UNDET.append("시험 모니터 작업 영역을 실측하지 못했다")
        return 2
    ok, why = ensure_on_monitor(pid, dev(getattr(a, "monitor", "DISPLAY3")), area, mm0.wp)
    print("기동 직후 모니터 확인:", why, flush=True)
    if not ok:  # 시험 모니터에 놓이지 않으면 측정하지 않는다 — 판정 영역과 앱 위치가 어긋난 채 FAIL 로 오판하지 않기 위해
        UNDET.append(why)
        from retest_user_files import stop_pid
        print("  이 도구가 띄운 앱(pid %s)만 종료: %s" % (pid, stop_pid(pid)), flush=True)
        return 2
    return 0


def restore_foreground(fg, before):
    """측정 전 `hwnd|pid|x|y` 로 전경·커서 복원. 측정 전 전경이 이 도구가 띄운 앱(OWN_PIDS)이었으면 그 창은 측정 중 닫혀
    SetForegroundWindow 가 항상 False 가 된다(11-09 'restored|False' 원인) — 복원 대상이 사라진 것이라 커서만 되돌리고 사유를 남긴다."""
    parts = before.split("|")
    if len(parts) != 4:
        return "전경 읽기 실패(%r) — 복원 생략" % before
    if parts[1].isdigit() and int(parts[1]) in OWN_PIDS:
        res = fg.run_ps(["restore", "-Hwnd", "0", "-X", parts[2], "-Y", parts[3]])  # hwnd 0 = 전경 복원 없이 커서만
        return "전경 복원 해당 없음(측정 전 전경이 자기 앱 pid %s, 종료됨) · 커서만 복원: %s" % (parts[1], res)
    return fg.run_ps(["restore", "-Hwnd", parts[0], "-X", parts[2], "-Y", parts[3]])


def fg_ps():
    import retest_maximize_monitor_fg as fg
    return fg


def parse_own(argv):
    p = argparse.ArgumentParser(description="Mars 창 메뉴 4~7 전경 허용 판(원본 무수정)", add_help=True)
    p.add_argument("--onscreen-approved", action="store_true", help="시험 창이 화면에 나오는 것을 승인(필수)")
    p.add_argument("--fg-area", default=None, help="⑦ 판정 대상 작업 영역 x,y,w,h(물리 px, 시험 모니터 DISPLAY3)")
    p.add_argument("--launch", action="store_true", help="화면 안 격리 기동(--bg-pos off)을 이 도구가 한다(--conf·--out-dir 필요, --isolated 와 함께)")
    p.add_argument("--out-dir", default=None, help="--launch 때 data·profile 폴더를 만들 곳")
    p.add_argument("--monitor", default="DISPLAY3", help="--fg-area 생략 시 작업 영역을 실측할 시험 모니터")
    p.add_argument("--terminal-monitor", default="DISPLAY2", help="겹치면 거부할 터미널 모니터")
    p.add_argument("--onscreen-who", default=None, help="화면 안 기동 승인 근거(누가·언제, 사유 기록용)")
    p.add_argument("--self-test", action="store_true", help="앱·입력 없이 패치 적용·인자 해석·원본 sha 만 확인")
    return p.parse_known_args(argv)


def _opt(argv, name):
    """원본 인자 목록에서 `name 값` 의 값(없으면 None)."""
    return argv[argv.index(name) + 1] if name in argv and argv.index(name) + 1 < len(argv) else None


def parse_area(v):
    from retest_window_menu_sdi import parse_override
    return parse_override(v)


def self_test():
    before = {n: sha(n) for n in ORIGINALS}
    menu, ok = apply_patches()
    import retest_window_menu_items as items
    import retest_window_menu_sdi as sdi
    checks = {
        "sdi HOLD 분기 치환": ok,
        "items.desktop_hold 해제": items.desktop_hold(type("C", (), {"target": "desktop"})()) is None,
        "fg-area 해석": parse_area("1920,0,1920,1040") == (1920, 0, 1920, 1040) and parse_area("x") is None,
        "원본 sha 불변": all(sha(n) == before[n] for n in ORIGINALS),
        "항목 4~7 존재": {4, 5, 6, 7} <= {no for no, _, _ in items.ITEMS},
    }
    # A07 화면 안 격리 기동·공허한 PASS 검출
    mons = [{"device": "\\\\.\\DISPLAY1", "bounds": {"l": -1920, "t": 0, "r": 0, "b": 1080}, "work": {"l": -1920, "t": 0, "r": 0, "b": 1040}},
            {"device": "\\\\.\\DISPLAY2", "bounds": {"l": 0, "t": 0, "r": 2560, "b": 1440}, "work": {"l": 0, "t": 0, "r": 2560, "b": 1392}},
            {"device": "\\\\.\\DISPLAY3", "bounds": {"l": 2560, "t": 0, "r": 6400, "b": 2160}, "work": {"l": 2560, "t": 0, "r": 6400, "b": 2120}}]
    d3 = monitor_area(mons, "DISPLAY3")
    cmd = ["bash", "x/run_desktop_bg.sh", "--conf", "c", "--no-kill", "--", "--remote-control"]
    import retest_maximize_monitor_main as mm
    lc = mm.build_cmd("run/x", "d", "p")
    checks.update({
        "실측 DISPLAY3 작업 영역": d3 == (2560, 0, 3840, 2120) and monitor_area(mons, "DISPLAY9") is None,
        "정상 영역은 문제 없음": area_problem(d3, mons, "DISPLAY2") is None,
        "터미널 모니터 겹침 거부": area_problem((0, 0, 2560, 1392), mons, "DISPLAY2") is not None,
        "가상 영역(18000,0) 거부 — 공허한 PASS 방지": area_problem((18000, 0, 6000, 1440), mons, "DISPLAY2") is not None,
        "기동 명령은 --bg-pos off, primary-override 없음": lc[lc.index("--bg-pos") + 1] == "off" and "--primary-override" not in lc,
        "재기동 명령에 --bg-pos off 주입": inject_bg_off(cmd)[-2:] == ["--bg-pos", "off"] and inject_bg_off(cmd + ["--bg-pos", "x"]).count("--bg-pos") == 1,
        "다른 명령은 주입 안 함": inject_bg_off(["ls"]) == ["ls"],
        "화면 안 허용 환경(승인 사유 10자 이상)": onscreen_env(type("A", (), {})())["PD_ALLOW_ONSCREEN"] == "1" and len(onscreen_env(type("A", (), {})())["PD_ONSCREEN_REASON"]) >= 10,
        "승인 없으면 rc 4(--launch 포함)": main(["--launch"]) == 4,
    })
    # 기동 직후 실제 모니터 확인(가짜 wp — 앱·입력 없음)
    def fake(start_mon, moves_ok=True):
        st = {"mon": start_mon, "ops": []}

        def wp(pid, ops):
            st["ops"].append(ops[0])
            if ops[0].startswith("move:") and moves_ok:
                st["mon"] = "DISPLAY3"
            return [{"windows": [{"hwnd": 7, "cls": "Avalonia", "monitor": "\\\\.\\" + st["mon"], "rect": {"l": 234, "t": 234, "r": 1650, "b": 1142}},
                                 {"hwnd": 8, "cls": "SHADOW", "monitor": "\\\\.\\DISPLAY1", "rect": {"l": 0, "t": 0, "r": 3000, "b": 3000}}]}]
        return wp, st
    area3 = (2560, 236, 1920, 1152)
    wpa, sta = fake("DISPLAY3")
    wpb, stb = fake("DISPLAY1")
    wpc, stc = fake("DISPLAY1", moves_ok=False)
    ra = ensure_on_monitor(1, "DISPLAY3", area3, wpa, sleep=lambda s: None)
    rb = ensure_on_monitor(1, "DISPLAY3", area3, wpb, sleep=lambda s: None)
    rc_ = ensure_on_monitor(1, "DISPLAY3", area3, wpc, sleep=lambda s: None)
    mv = [o for o in stb["ops"] if o.startswith("move:")]
    checks.update({
        "이미 DISPLAY3 면 이동 없음": ra[0] and not any(o.startswith("move:") for o in sta["ops"]),
        "다른 모니터면 이동 뒤 확인(목표 영역 안 +40)": rb[0] and len(mv) == 1 and mv[0].startswith("move:7:2600:276:"),
        "이동 불가면 UNDET(측정 안 함)": (not rc_[0]) and "DISPLAY1" in rc_[1],
        "그림자 창은 메인 후보 제외": main_window(wpa(1, ["list"])[0]["windows"])["hwnd"] == 7,
        "창 없으면 UNDET": ensure_on_monitor(1, "DISPLAY3", area3, lambda p, o: [{"windows": []}], sleep=lambda s: None)[0] is False,
        "기동 로그 PID 추출": pid_of_launch("x\nPID=10704\nRESULT=RUNNING\n") == 10704 and pid_of_launch("RESULT=FAILED") is None,
    })
    # 전경 복원: 측정 전 전경이 자기 앱이면 hwnd 복원 없이 커서만(restored|False 오탐 방지)
    calls = []
    fgf = type("F", (), {"run_ps": staticmethod(lambda args: calls.append(args) or "restored|False")})
    OWN_PIDS.add(10704)
    msg = restore_foreground(fgf, "3871062|10704|1274|438")
    msg2 = restore_foreground(fgf, "55|999|1|2")
    OWN_PIDS.discard(10704)
    checks.update({
        "전경이 자기 앱이면 hwnd 0(커서만) 복원": calls[0][calls[0].index("-Hwnd") + 1] == "0" and "해당 없음" in msg,
        "남의 전경 창은 hwnd 그대로 복원": calls[1][calls[1].index("-Hwnd") + 1] == "55" and msg2 == "restored|False",
        "전경 읽기 실패면 복원 생략": "생략" in restore_foreground(fgf, "x"),
    })
    # HOLD 해제 동작 확인: 앱 호출이 없는 가짜 ctx 로 sdi.it_sdi_reset 이 HOLD 를 돌려주지 않고 open_sdi 까지 가는지
    class Stop(Exception):
        pass

    def open_sdi(ctx):
        raise Stop()
    ctx = type("C", (), {"target": "desktop"})()
    try:
        r = sdi.it_sdi_reset(ctx, open_sdi)
        checks["HOLD 미반환"] = not (isinstance(r, tuple) and r[0] == sdi.HOLD)
    except Stop:
        checks["HOLD 미반환"] = True
    for k, v in checks.items():
        print("%-4s %s" % ("PASS" if v else "FAIL", k))
    print("self-test:", "OK" if all(checks.values()) else "FAIL")
    return 0 if all(checks.values()) else 1


def main(argv):
    own, rest = parse_own(argv)
    if own.self_test:
        return self_test()
    if "--target" not in rest:
        rest = ["--target", "desktop"] + rest
    if not own.onscreen_approved:
        print("거부: --onscreen-approved 필요 — 이 판은 시험 창이 화면(DISPLAY3)에 나온다. 사용자 Pd·5180·5220 은 건드리지 않는다")
        return 4
    area = None
    if own.fg_area is not None:
        area = parse_area(own.fg_area)
        if area is None:
            print("--fg-area 는 x,y,w,h 정수 4개(w·h>0)여야 한다: %r" % own.fg_area)
            return 5
    if own.launch:
        import retest_maximize_monitor as mm0
        conf = _opt(rest, "--conf")
        if not conf or not own.out_dir:
            print("--launch 는 --conf <bin/run 복사본> 과 --out-dir 이 필요하다")
            return 5
        mons = mm0.wp(0, ["monitors"])[0]["monitors"]
        mons = [mons] if isinstance(mons, dict) else mons
        if area is None:
            area = monitor_area(mons, dev(own.monitor))
            if area is None:
                print("시험 모니터 %s 를 실측 목록에서 찾지 못했다: %s" % (own.monitor, [dev(m_["device"]) for m_ in mons]))
                return 5
        bad = area_problem(area, mons, dev(own.terminal_monitor))
        if bad:
            print("거부: ⑦ 판정 영역 %s — %s" % (area, bad))
            return 5
        own.conf, own.area = conf, area
        TARGET.update(monitor=dev(own.monitor), area=area)
        rc = launch(own, os.path.abspath(own.out_dir))
        if UNDET:
            print("UNDET: 앱이 시험 모니터 %s 에 놓이지 않아 측정하지 않았다 — %s" % (own.monitor, UNDET[0]))
            return 2
        if rc != 0:
            print("화면 안 격리 기동 실패 rc=%d" % rc)
            return 5
        print("⑦ 판정 영역(실측 %s 작업 영역): %s" % (own.monitor, area), flush=True)
    if "--only" not in rest:
        rest += ["--only", "4,5,6,7"]
    menu, ok = apply_patches()
    if not ok:
        print("원본 sdi.it_sdi_reset 의 HOLD 분기를 찾지 못했다 — 원본이 바뀐 듯하다. 실행하지 않는다")
        return 5
    if area is not None:  # 원본 파서를 거치되(_override_arg 거부 우회 없음) 판정용 영역은 인자 해석 뒤 직접 주입
        orig_parse = argparse.ArgumentParser.parse_args

        def parse_args(self, args=None, namespace=None):
            ns = orig_parse(self, args, namespace)
            ns.primary_override = "%d,%d,%d,%d" % area
            return ns
        argparse.ArgumentParser.parse_args = parse_args
    fg = fg_ps()
    before = fg.run_ps(["read"])  # hwnd|pid|cursorX|cursorY
    print("측정 전 전경·커서:", before, flush=True)
    try:
        rc = menu.main(rest)
        if UNDET:  # 재기동 앱이 시험 모니터에 놓이지 않았다 — 그 뒤 측정값은 믿지 않는다
            print("UNDET: 재기동 앱이 시험 모니터에 놓이지 않았다 — %s" % UNDET[0])
            return 2
        return rc
    finally:
        try:
            print("복원:", restore_foreground(fg, before), flush=True)
        except Exception as e:  # 복원 실패는 결과를 바꾸지 않고 알린다
            print("전경·커서 복원 실패:", repr(e), flush=True)


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
