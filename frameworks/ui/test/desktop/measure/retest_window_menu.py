#!/usr/bin/env python3
# Mars 윈도우 메뉴 13항목 원격 시험 실행기 — 데스크톱·Android(HTTP)·웹(Playwright pdAutomation) 공용, 항목별 실행 전후 대조 + 재기동 후 유지 판정(PASS/FAIL/N/A/PENDING)
"""
사용: --target desktop|android|web [--out 결과.json] [--only 4,5] [--no-restart] [--conf Release] [--serial 기기] [--url 웹주소] [--isolated]
  android 는 PD_REMOTE_INFO(포트를 forward 포트로 바꾼 연결 정보), web 은 Playwright 로 window.pdAutomation 을 부른다.
  --isolated(desktop): 사용자 폴더 layout·settings 백업·복원을 생략한다. 연결된 앱 PID 명령줄에 사용자 폴더(하위·정션 포함)가 아닌
    --data-dir 가 있을 때만 실행(--profile-dir 만으로는 격리 아님), 확인 못 하면 rc=3 거부. 재기동한 앱도 같은 조건을 다시 확인하고,
    실패하면 이 도구가 띄운 앱(run_desktop_bg 출력 로그 이름이 명령줄에 있는 PID)만 내린다.
  비격리(desktop 기본): 사용자 Pd(run_desktop_bg 로 띄우지 않은 Pd)가 떠 있으면 실행 전에 rc=3 거부 — --isolated 를 쓴다.
판정: PASS / FAIL / N/A(플랫폼 해당 없음, 사유 기록) / PENDING(원격 명령 미구현). 설정형 항목은 재기동 뒤 값 유지도 본다
(desktop=run_desktop_bg --no-kill 슬롯 필수 — 첫 기동의 --data-dir·--profile-dir·--multi-instance 를 그대로 넘기고, 종료는 시험 중인 앱 PID 하나만.
 사용자가 띄운 Pd 는 종료하지 않는다. android=force-stop 후 재실행, web=새로고침). 끝나면 바꾼 설정을 되돌린다.
항목 정의는 retest_window_menu_items.py(ITEMS). 시험 대상 앱은 진단 빌드(-p:PdDiagnostics=true)여야 한다.
"""
import argparse
import datetime
import json
import os
import subprocess
import sys
import time

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "common"))  # D-TESTDIR: test/common/_paths.py
import _paths  # noqa: E402
_paths.add()
TOOLS = _paths.TOOLS  # 옛 위치(poc/ui/tools) — run_desktop_bg.sh·pdctl.py·logs 경로 기준 그대로
sys.path.insert(0, TOOLS)
import pdctl  # noqa: E402
from retest_remote import Remote  # noqa: E402
from retest_window_menu_items import ITEMS, NotApplicable, Pending  # noqa: E402

ADB = "/mnt/c/Program Files (x86)/Android/android-sdk/platform-tools/adb.exe"
UNKNOWN = "알 수 없는 명령"


class Ctx:
    """시험 문맥: 원격 호출(r)·대상 플랫폼·결과 표·재기동 유지 목록·되돌림 목록."""

    def __init__(self, r, target, args):
        self.r, self.target, self.args = r, target, args
        self.rows, self.restore, self.persist = [], [], []  # persist: (조회 명령, 인자, 기대값) — 재기동 뒤 대조
        self.layout_id = None
        self.host = {}  # desktop: 첫 기동 명령줄의 --data-dir·--profile-dir·--multi-instance(재기동 때 그대로 넘김)

    def call(self, cmd, **kw):
        res = self.r.call(cmd, **kw)
        last = self.r.calls[-1] if self.r.calls else {}
        if res is None and UNKNOWN in (last.get("error") or ""):
            raise Pending(cmd)
        if isinstance(res, dict) and res.get("status") in ("Unavailable", "NotApplicable"):  # lead 지정 이름 NotApplicable(옛 Unavailable 도 받음)
            raise NotApplicable("%s — 플랫폼 %s 에서 실행 불가(메뉴 비활성)" % (cmd, res.get("platform")))
        return res

    def row(self, no, name, verdict, detail):
        self.rows.append({"no": no, "item": name, "verdict": verdict, "detail": detail})
        print("%-7s #%-3d %s | %s" % (verdict, no, name, str(detail)[:300]), flush=True)


def restart(ctx):
    """플랫폼별 재기동 후 원격 연결을 다시 잡는다. 성공하면 True."""
    t, a = ctx.target, ctx.args
    if t == "web":
        ctx.r.page.reload()
        ctx.r.page.wait_for_function("() => window.pdAutomation && window.pdAutomation.invoke", timeout=60000)
    elif t == "desktop":
        from retest_user_files import isolation, launched_by, launched_log, relaunch_args, stop_pid
        old = ctx.r.info.get("pid")
        ctx.r.call("app.closeMain")  # 사용자 경로(종료 자동 저장)
        if not stop_pid(old):  # 시험 중인 앱 PID 하나만 종료 대기·강제 종료(--kill 이미지 전체 종료는 사용자 Pd 까지 죽인다)
            return False
        extra = ["--primary-override", str(a.primary_override)] if a.primary_override is not None else []  # 사용자 화면 대신 지정 모니터(⑦)
        extra += relaunch_args(ctx.host)  # 첫 기동의 데이터 폴더를 그대로 — 기동마다 새 임시 폴더면 '저장 → 재기동 → 유지' 대조가 어긋난다
        run_out = subprocess.run(["bash", os.path.join(TOOLS, "run_desktop_bg.sh"), "--conf", a.conf, "--no-kill", "--", "--remote-control"] + extra,
                                 capture_output=True, timeout=80).stdout.decode("utf-8", "replace")
        log_name = launched_log(run_out)  # 이 도구가 띄운 앱 식별(run_desktop_bg 는 Windows PID 를 출력하지 않는다 — 고유 로그 이름으로 대조)
        for _ in range(40):
            try:
                info = pdctl.load_info()
                if info.get("pid") != old:
                    if a.isolated:  # 재기동한 앱도 격리 조건을 다시 확인 — 아니면 연결하지 않는다
                        ok, detail = isolation(info.get("pid"))
                        if not ok:
                            print("재기동 앱 격리 확인 실패: %s" % detail, flush=True)
                            if launched_by(info.get("pid"), log_name):  # 이 도구가 띄운 앱일 때만 내린다 — 사용자 Pd 는 건드리지 않는다
                                print("  이 도구가 띄운 앱(pid %s, %s) 종료: %s" % (info.get("pid"), log_name, stop_pid(info.get("pid"), timeout=1)), flush=True)
                            else:
                                print("  종료하지 않음 — pid %s 가 이 도구가 띄운 앱인지 확인 못 함(%s)" % (info.get("pid"), log_name), flush=True)
                            return False
                    ctx.r.info = info
                    break
            except SystemExit:
                pass
            time.sleep(1)
    else:  # android: 강제 종료 → 재실행 → 토큰 다시 읽기(포트는 기존 forward 유지)
        s = ["-s", a.serial] if a.serial else []
        subprocess.run([ADB] + s + ["shell", "am", "force-stop", "com.pd.poc"], capture_output=True, timeout=20)
        subprocess.run([ADB] + s + ["shell", "monkey", "-p", "com.pd.poc", "-c", "android.intent.category.LAUNCHER", "1"],
                       capture_output=True, timeout=20)
        time.sleep(6)
        raw = subprocess.run([ADB] + s + ["exec-out", "run-as", "com.pd.poc", "cat", "files/PdPoc/remote-control.json"],
                             capture_output=True, timeout=20).stdout
        info = json.loads(raw.decode("utf-8"))
        info["port"] = ctx.r.info["port"]
        ctx.r.info = info
    for _ in range(30):
        if ctx.r.call("status") is not None:
            return True
        time.sleep(1)
    return False


def check_persist(ctx):
    for cmd, kw, expect in ctx.persist:
        got = ctx.r.call(cmd, **(kw or {}))
        if isinstance(expect, tuple) and expect[0] == "contains":
            ok = any(x.get("id") == expect[1] for x in (got or []))
        elif isinstance(expect, tuple) and expect[0] == "field":
            ok = (got or {}).get(expect[1]) == expect[2]
        elif isinstance(expect, tuple) and expect[0] == "titles":  # 불러온 뒤 문서 제목 집합이 같은가(① 데이터 보존)
            titles = sorted(d.get("title") for d in ctx.r.call("docs.list") or [])
            ok, got = got is True and titles == sorted(expect[1]), {"loaded": got, "titles": titles}
        else:
            ok = got == expect
        ctx.row(0, "재기동 유지: " + cmd, "PASS" if ok else "FAIL", {"got": got, "expect": expect})


def _override_arg(v):
    """--primary-override 'x,y,w,h' 검사(형식 틀리면 실행 전에 거부 — 앱은 틀린 값을 조용히 무시하므로)."""
    from retest_window_menu_sdi import parse_override
    # odev-4b q6_i0451 실측: override 를 주어도 ⑤·두 번째 ④ 가 X=0 으로 잘려 사용자 화면에 나온다 → 원인 수정 전까지 거부(lead 06:1x 화면 안 경로 차단)
    if not os.environ.get("PD_ALLOW_PRIMARY_OVERRIDE"):
        raise argparse.ArgumentTypeError("--primary-override 는 현재 사용 불가(override 미동작 — 창이 사용자 화면에 나옴). 수정 통지 뒤 PD_ALLOW_PRIMARY_OVERRIDE=1 로만 허용")
    if parse_override(v) is None:
        raise argparse.ArgumentTypeError("x,y,w,h 정수 4개(w·h>0)가 필요합니다: %r" % v)
    return v


def main(argv):
    p = argparse.ArgumentParser(description="Mars 윈도우 메뉴 13항목 원격 시험")
    p.add_argument("--target", choices=["desktop", "android", "web"], required=True)
    p.add_argument("--out", default=None)
    p.add_argument("--no-restart", action="store_true")
    p.add_argument("--conf", default="Release")
    p.add_argument("--serial", default=os.environ.get("PD_ADB_SERIAL"))
    p.add_argument("--url", default=os.environ.get("PD_URL", "http://127.0.0.1:5181/?fresh"))
    p.add_argument("--only", default="", help="항목 번호 쉼표 목록(예 4,5,13)")
    p.add_argument("--primary-override", type=_override_arg, default=None, dest="primary_override",
                   help="④⑤⑥⑦ 기준 사각형 x,y,w,h(물리 px, odev-4b Program.cs 진단 전용과 같은 형식) — ⑦ 판정 대상 작업 영역으로 쓰고 재기동 때 앱 인자로도 그대로 넘긴다")
    p.add_argument("--known-fail", default="", help="번호=사유;… 기대 FAIL 항목(판정 EXPECTED_FAIL — gate 근거 아님, 종료 코드 2). "
                   "lead 00:5x: 2차 동결 진단판의 125 한 항목에만 쓴다. SetSidePanelsHidden 반영 판본부터는 쓰지 않는다")
    p.add_argument("--isolated", action="store_true",
                   help="desktop: 사용자 폴더 백업·복원 생략. 앱 명령줄에 사용자 폴더가 아닌 --data-dir 가 있을 때만 실행(--profile-dir 만으로는 격리 아님, 확인 불가면 rc=3)")
    a = p.parse_args(argv)
    out = a.out or os.path.join(TOOLS, "..", "logs", "odev6r", "window_menu_%s_%s.json" % (a.target, datetime.datetime.now().strftime("%H%M%S")))
    only = {int(x) for x in a.only.split(",") if x.strip()}
    if a.target == "web":
        from playwright.sync_api import sync_playwright  # 웹 축만 필요
        import retest_browser as rb
        with sync_playwright() as pw:
            b = pw.chromium.launch(headless=True, args=["--use-angle=swiftshader", "--enable-unsafe-swiftshader"])
            page = b.new_page(viewport={"width": 1280, "height": 800})
            page.goto(a.url)
            page.wait_for_function("() => window.pdAutomation && window.pdAutomation.invoke", timeout=60000)
            return run(Ctx(rb.WebRemote(page), "web", a), only, out, a)
    if a.target != "desktop":
        return run(Ctx(Remote(), a.target, a), only, out, a)
    from retest_user_files import host_args, isolation, pid_alive, proc_cmdline, stop_pid, user_pds
    if not a.isolated:  # 비격리 모드는 사용자 폴더를 백업·복원하고 Pd 0개까지 기다린다 — 사용자 Pd 가 떠 있으면 실행 전에 거부
        upd = user_pds()
        if upd:
            print("거부: 사용자 Pd 가 떠 있다 %s — 비격리 모드는 사용자 Pd 0개일 때만 실행한다. 사용자 Pd 를 그대로 두려면 --isolated 를 써라" % [p for p, _ in upd])
            return 3
    if a.isolated:  # 격리 모드: 사용자 폴더를 쓰지 않는 앱일 때만 — 백업·복원(사용자 폴더 쓰기) 없이 실행
        r = Remote()
        ok, detail = isolation(r.info.get("pid"))
        if not ok:
            print("거부: --isolated 는 앱이 사용자 폴더가 아닌 --data-dir/--profile-dir 로 떠 있을 때만 실행한다 — %s" % detail)
            return 3
        print("격리 확인: %s" % detail)
        ctx = Ctx(r, a.target, a)
        ctx.host = detail["args"]
        try:
            return run(ctx, only, out, a)
        finally:
            # 기존 흐름처럼 끝나면 시험 앱을 내린다 — 대상은 격리 확인된 PID 하나(첫 앱 또는 재기동 후 재확인된 앱)뿐, 사용자 Pd 는 건드리지 않는다
            if ctx.r.info.get("pid") and pid_alive(ctx.r.info.get("pid")):
                ctx.r.call("app.shutdown")
                stop_pid(ctx.r.info.get("pid"))
            if os.path.exists(out):
                with open(out, encoding="utf-8") as f:
                    data = json.load(f)
                data["userFiles"] = {"skipped": "isolated", "isolation": detail}
                with open(out, "w", encoding="utf-8") as f:
                    json.dump(data, f, ensure_ascii=False, indent=2)
    # 데스크톱 재기동 대조는 사용자 조건(메인 창 닫기 → 종료 자동 저장)이라 사용자 layout·settings 를 덮어쓴다(lead 01:4x 재발 방지)
    # → 시작 전 백업·끝나면(실패해도) 복원, 시작 전·종료 후 md5 를 결과 JSON 에 기록
    import glob
    import hashlib
    import shutil
    ud = sorted(glob.glob("/mnt/c/Users/*/AppData/Local/PdPoc"), key=os.path.getmtime)[-1]
    md5 = lambda n: (hashlib.md5(open(os.path.join(ud, n), "rb").read()).hexdigest()[:8] if os.path.exists(os.path.join(ud, n)) else None)
    names = ("layout.json", "settings.json")
    bdir = os.path.abspath(out) + ".backup"
    os.makedirs(bdir, exist_ok=True)
    before = {n: md5(n) for n in names}
    had_layouts = os.path.isdir(os.path.join(ud, "layouts"))  # 개인 레이아웃 폴더 — 시험(#12·#126)이 처음 만들면 끝나고 지운다
    for n in names:
        if before[n]:
            shutil.copy2(os.path.join(ud, n), os.path.join(bdir, n))
    try:
        ctx = Ctx(Remote(), a.target, a)
        pid0 = ctx.r.info.get("pid")
        ctx.host = host_args(proc_cmdline(pid0) if pid0 else None)  # 재기동 때 첫 기동 데이터 폴더 승계(못 읽으면 빈 값 — 이전과 같은 인자)
        return run(ctx, only, out, a)
    finally:
        try:  # 앱이 이미 꺼져 있으면 연결 정보가 없어 SystemExit — 복원은 그대로 진행
            Remote().call("app.shutdown")  # 자동 저장 없이 내린 뒤 복원
        except (SystemExit, Exception):
            pass
        from retest_user_files import wait_app_exit
        wait_app_exit(30)  # 프로세스 0개 확인 뒤 복원(종료 중 재기록이 복원본을 덮은 02:15 사고)
        for n in names:
            if before[n]:
                shutil.copy2(os.path.join(bdir, n), os.path.join(ud, n))
            elif os.path.exists(os.path.join(ud, n)):
                os.remove(os.path.join(ud, n))
        ld = os.path.join(ud, "layouts")
        # 시험 전 없던 폴더이고 빈 목록 파일(index.json·#123 이 만드는 backups.json, Items=[])만 있을 때만 지운다 — 그 밖의 파일이 있으면 남긴다.
        # (예전 조건은 index.json 하나만 허용 → #123 의 backups.json 때문에 폴더가 남았다: otest-1 02:35 rt3 실행)
        if not had_layouts and os.path.isdir(ld) and set(os.listdir(ld)) <= {"index.json", "backups.json"}:
            try:
                names = os.listdir(ld)
                if all(not json.load(open(os.path.join(ld, n), encoding="utf-8")).get("Items") for n in names):
                    for n in names:
                        os.remove(os.path.join(ld, n))
                    os.rmdir(ld)
            except (OSError, ValueError):
                pass
        after = {n: md5(n) for n in names}
        if os.path.exists(out):
            with open(out, encoding="utf-8") as f:
                data = json.load(f)
            data["userFiles"] = {"md5_before": before, "md5_after": after, "restored": before == after, "backup": bdir}
            with open(out, "w", encoding="utf-8") as f:
                json.dump(data, f, ensure_ascii=False, indent=2)
        print("사용자 파일 복원:", "OK" if before == after else "불일치 %s → %s" % (before, after))


def run(ctx, only, out, a):
    for _ in range(30):
        if ctx.r.call("status") is not None:
            break
        time.sleep(1)
    ctx.r.call("perf.reset")
    for no, name, fn in ITEMS:
        if only and no not in only:
            continue
        try:
            ok, detail = fn(ctx)
            verdict = ok if isinstance(ok, str) else ("PASS" if ok else "FAIL" if ok is not None else "N/A")
        except Pending as e:
            verdict, detail = "PENDING", "원격 명령 미구현: %s" % e
        except NotApplicable as e:
            verdict, detail = "N/A", str(e)
        except Exception as e:  # 한 항목 예외가 나머지를 막지 않게
            verdict, detail = "FAIL", repr(e)
        known = dict(kv.split("=", 1) for kv in (a.known_fail or "").split(";") if "=" in kv)
        if verdict == "FAIL" and str(no) in known:  # 알려진 미반영 판본 — 결과는 남기되 gate 근거로 쓰지 않는다(lead 00:5x)
            verdict, detail = "EXPECTED_FAIL", {"note": "%s, 기대 FAIL — gate 근거 아님" % known[str(no)], "detail": detail}
        ctx.row(no, name, verdict, detail)
    if ctx.persist and not a.no_restart:
        if restart(ctx):
            check_persist(ctx)
        else:
            ctx.row(0, "재기동", "FAIL", "원격 재연결 실패")
    for cmd, kw in reversed(ctx.restore):  # 시작 값으로 되돌림
        ctx.r.call(cmd, **{k: v for k, v in kw.items() if v is not None})
    summary = {}
    for row in ctx.rows:
        summary[row["verdict"]] = summary.get(row["verdict"], 0) + 1
    os.makedirs(os.path.dirname(os.path.abspath(out)), exist_ok=True)
    with open(out, "w", encoding="utf-8") as f:
        json.dump({"target": ctx.target, "rows": ctx.rows, "summary": summary, "calls": ctx.r.calls,
                   "ops": ctx.r.call("perf.ops", op="remote.")}, f, ensure_ascii=False, indent=2)
    print("\n%s → %s" % (summary, os.path.abspath(out)))
    # 종료 코드: 0 = 전부 통과(FAIL·EXPECTED_FAIL·UNDET·PENDING 0, N/A 는 플랫폼 해당 없음이라 허용) / 1 = FAIL 있음 / 2 = FAIL 없고 통과 아닌 판정만 있음.
    # EXPECTED_FAIL 은 gate 근거가 아니므로 0(통과)으로 내보내지 않는다(lead 00:5x — PASS 로 세는 경로 차단)
    if summary.get("FAIL"):
        return 1
    # 통과가 아닌 판정(종료 코드 2): EXPECTED_FAIL(gate 근거 아님)·UNDET(판정 불능 — 필요한 원격 명령 없음 등)·PENDING(원격 명령 미구현으로 미실행).
    # PENDING 만 있어도 예전에는 0(통과)으로 나갔다 — UNDET 와 같은 구멍이라 함께 막는다(lead 03:3x '다른 판정 불능 항목도 같은 방식인지').
    # PASS(조건부): ⑦ 공식은 맞으나 기본 위치(시작점) 미검증 — PASS 로 세지 않는다(otest-1 05:3x)
    undet = {k: summary.get(k) for k in ("EXPECTED_FAIL", "UNDET", "PENDING", "PASS(조건부)", "HOLD") if summary.get(k)}
    if undet:
        print("통과 아님(gate 근거 아님): %s — 종료 코드 2" % undet)
        return 2
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
