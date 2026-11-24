#!/usr/bin/env python3
# 화면 방식(자동·PC형·태블릿형·모바일형) 전환 측정 — 제목 줄 스위치와 설정 창에서 클릭 한 번 전환·선택 표시·저장값을 원격 명령으로 모아 JSON 에 저장하고 요약 표를 찍는다
"""
사용: PD_REMOTE_INFO=<임시 data>/remote-control.json python3 -B retest_view_mode.py --out <logs/<에이전트>/<회차>/view_mode.json> --settings-file <임시 profile>/settings.json [--after-restart <수동값>] [--touch]
앱은 먼저 run_desktop_bg.sh 로 진단 빌드를 임시 --data-dir·--profile-dir, --remote-control, 화면 밖 --bg-pos 로 띄워 둔다(저장 파일 settings.json 은 profile 쪽에 생긴다).
사용자 Pd 가 떠 있으면 --no-kill --multi-instance on 이 필요하다(lead 승인). 기동 결과의 PID= 와 remote-control.json 의 pid 가 같은지 본다.
--after-restart <값>: 재시작 뒤 저장값(pc|tablet|mobile|auto)이 유지됐는지만 읽는다.
--touch: 앱을 --form-factor touch 로 띄웠을 때의 기기 규칙(태블릿 자동=태블릿형, 폰=모바일형만·스위치 숨김·수동 거부)만 잰다.
--l2: 자동에서 칸을 비전경 창 메시지(post_click.ps1)로 눌러 포인터 경로를 잰다. 활성화·커서 이동은 없고, 물리 입력은 아니다.
ui.click 은 비활성 컨트롤을 거부하므로 '자동에서 수동 칸 클릭'의 비활성 결함을 잡는다. 실제 포인터 입력은 아니다.
측정 내내 offscreen_guard 로 창 겹침·전경을 감시하고, 걸리면 앱을 종료하고 rc=4 로 멈춘다.
제약: 최대화·최소화 항목은 넣지 않는다. MainBgPinHook 이 기동 구간 뒤 최대화·최소화 창의 이동을 고정하지 않아 사용자 화면에 노출될 수 있다(team-lead 16:4x).
격리되지 않은 앱에 붙으면 rc=3 으로 거부한다. logs/ui28/ 에는 쓰지 않는다. 키 주입은 하지 않는다.
종료 코드: 0=전 항목 통과, 1=실패 있음, 2=연결 실패, 3=실행 거부, 4=화면 노출·전경 감지로 중단.
"""
import argparse
import json
import os
import subprocess
import sys
import time

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "common"))  # D-TESTDIR: test/common/_paths.py
import _paths  # noqa: E402
_paths.add()
from retest_remote import Remote  # noqa: E402
import retest_user_files  # noqa: E402
from offscreen_guard import GuardBreach, OffscreenGuard  # noqa: E402
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "judge"))
from view_mode_rules import judge, judge_l2, judge_touch  # noqa: E402

GUARD = None  # main 이 넣는다. 원격 명령마다 앞에서 확인한다
SETTLE_S = 0.8  # 페이드(약 300ms)와 디바운스(200ms) 뒤 값을 읽는다
SAVE_WAIT_S = 1.5  # 설정 저장은 비동기다
CELLS = {"auto": "shell.swUiStyleAuto", "pc": "shell.swUiStylePc", "tablet": "shell.swUiStyleTablet", "mobile": "shell.swUiStyleMobile"}
COMBO_ID = "settings.cmbUiStyle"
COMBO_ORDER = ("auto", "pc", "tablet", "mobile")
WIDE_W, NARROW_W, PHONE_W, H = 1400, 700, 500, 900


def safe(r, cmd, **kw):
    """명령 실패도 측정값이다 — 예외 대신 {"error": 문구} 로 남긴다. 화면 노출은 측정값이 아니라 중단 사유다."""
    if GUARD:
        GUARD.check()
    try:
        res = r.call(cmd, **kw)
    except Exception as ex:  # noqa: BLE001 — 측정 도구는 멈추지 않고 실패를 기록한다
        return {"error": str(ex)}
    if res is None and r.calls and r.calls[-1].get("error"):
        return {"error": r.calls[-1]["error"]}
    return res


def lower(v):
    return str(v).lower() if v is not None else None


def state(r):
    """저장값·적용 형과 칸별 활성 여부."""
    style = safe(r, "ui.style") or {}
    cells = {}
    for key, cid in CELLS.items():
        found = safe(r, "ui.find", id=cid) or {}
        cells[key] = {"enabled": found.get("enabled"), "visible": found.get("effectivelyVisible"), "classes": found.get("classes"), "error": found.get("error")}
    return {"setting": lower(style.get("setting")), "effective": lower(style.get("effective")), "device": style.get("device"), "width": style.get("width"), "cells": cells}


def to_auto(r):
    safe(r, "ui.style", set="auto")
    time.sleep(SETTLE_S)


def click_cell(r, key):
    """스위치 칸 클릭 1번 → 경과 뒤 상태. 칸을 찾지 못하거나 비활성이면 click 에 오류가 남는다."""
    res = safe(r, "ui.click", id=CELLS[key])
    click_error = res.get("error") if isinstance(res, dict) else None
    time.sleep(SETTLE_S)
    return {"clickError": click_error, "after": state(r)}


def measure_switch(r, out):
    out["initial"] = state(r)
    out["autoToManual"] = {}
    for key in ("pc", "tablet", "mobile"):
        to_auto(r)
        before = state(r)
        out["autoToManual"][key] = {"before": before, **click_cell(r, key)}
    out["manualToManual"] = {}
    to_auto(r)
    safe(r, "ui.style", set="pc")
    time.sleep(SETTLE_S)
    for src, dst in (("pc", "tablet"), ("tablet", "mobile"), ("mobile", "pc")):
        safe(r, "ui.style", set=src)
        time.sleep(SETTLE_S)
        out["manualToManual"]["%s>%s" % (src, dst)] = click_cell(r, dst)
    out["manualToAuto"] = {}
    for src in ("pc", "tablet"):
        safe(r, "ui.style", set=src)
        time.sleep(SETTLE_S)
        out["manualToAuto"][src] = click_cell(r, "auto")
    out["autoCellAgain"] = click_cell(r, "auto")  # 자동에서 [자동] 재클릭 = 현재 판정 형을 수동으로 남긴다(화면 불변)


def measure_rules(r, out):
    """기기 규칙: 자동이면 크기를 따르고, 수동은 크기와 무관하다. 크기는 끝에 되돌린다."""
    rows = {}
    to_auto(r)
    for name, width in (("wide", WIDE_W), ("narrow", NARROW_W), ("phoneWidth", PHONE_W), ("wideBack", WIDE_W)):
        safe(r, "window.resize", w=width, h=H)
        time.sleep(SETTLE_S)
        rows["auto_" + name] = state(r)
    safe(r, "ui.style", set="tablet")
    time.sleep(SETTLE_S)
    for name, width in (("phoneWidth", PHONE_W), ("wideBack", WIDE_W)):
        safe(r, "window.resize", w=width, h=H)
        time.sleep(SETTLE_S)
        rows["tablet_" + name] = state(r)
    out["rules"] = rows


def post_click(pid, x, y):
    """비전경 창 메시지 클릭(L2) — 활성화·커서 이동 없음. 좌표는 ui.find bounds 중심(배율 1 가정)."""
    ps1 = os.path.join(os.path.dirname(os.path.abspath(__file__)), "post_click.ps1")
    win = subprocess.check_output(["wslpath", "-w", ps1], text=True).strip()
    run = subprocess.run(["powershell.exe", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", win, "-TargetPid", str(pid), "-X", str(x), "-Y", str(y)],
                         capture_output=True, text=True, errors="replace", timeout=60)
    return run.stdout.strip() or run.stderr.strip()[:200]


def measure_l2(r, out, pid):
    """자동에서 칸을 창 메시지로 누른다. ui.click 과 달리 히트 테스트·IsEnabled·Command 를 실제 포인터 경로로 탄다."""
    rows = {}
    for key in ("tablet", "mobile", "pc"):
        to_auto(r)
        found = safe(r, "ui.find", id=CELLS[key]) or {}
        b = found.get("bounds") or {}
        sent = post_click(pid, int(b.get("x", 0) + b.get("w", 0) / 2), int(b.get("y", 0) + b.get("h", 0) / 2))
        time.sleep(SETTLE_S)
        rows[key] = {"bounds": b, "enabledBefore": found.get("enabled"), "sent": sent, "after": state(r)}
    to_auto(r)
    out["l2"] = rows


def measure_touch(r, out):
    """--form-factor touch: 큰 창=태블릿(자동=태블릿형, 수동 칸 눌림), 짧은 변 600 미만=폰(모바일형만, 스위치 숨김, 수동 거부)."""
    rows = {}
    safe(r, "window.resize", w=WIDE_W, h=H)
    time.sleep(SETTLE_S)
    to_auto(r)
    rows["tablet_auto"] = state(r)
    rows["tablet_click_pc"] = click_cell(r, "pc")
    to_auto(r)
    safe(r, "window.resize", w=PHONE_W, h=PHONE_W)
    time.sleep(SETTLE_S)
    rows["phone"] = state(r)
    rows["phone_click_pc"] = click_cell(r, "pc")
    rows["phone_set_pc"] = safe(r, "ui.style", set="pc")
    time.sleep(SETTLE_S)
    rows["phone_after_set"] = state(r)
    safe(r, "window.resize", w=WIDE_W, h=H)
    time.sleep(SETTLE_S)
    rows["tablet_back"] = state(r)
    out["touch"] = rows


def combo_value(r):
    found = safe(r, "ui.find", id=COMBO_ID, props="SelectedIndex") or {}
    props = found.get("props") or {}
    try:
        return COMBO_ORDER[int(props.get("SelectedIndex"))]
    except (TypeError, ValueError, IndexError):
        return {"error": found.get("error"), "raw": found}


def measure_settings(r, out):
    """설정 창: 스위치 값과 동기화되는지, 콤보로 고르면 자동에서도 바로 전환되는지."""
    safe(r, "sys.open", view="Settings")
    time.sleep(0.5)
    safe(r, "settings.window", category="Window")
    time.sleep(0.5)
    rows = {}
    to_auto(r)
    rows["comboWhenAuto"] = combo_value(r)
    for key in ("pc", "tablet", "mobile"):
        to_auto(r)
        res = safe(r, "ui.select", id=COMBO_ID, index=COMBO_ORDER.index(key))
        time.sleep(SETTLE_S)
        rows["combo_auto>" + key] = {"selectError": res.get("error") if isinstance(res, dict) else None, "after": state(r), "combo": combo_value(r)}
    to_auto(r)
    rows["switch>tablet"] = click_cell(r, "tablet")
    rows["switch>tablet"]["combo"] = combo_value(r)
    out["settings"] = rows
    safe(r, "ui.click", id="settings.btnClose")
    time.sleep(0.3)


def read_saved(path):
    if not path:
        return None
    time.sleep(SAVE_WAIT_S)
    try:
        with open(path, encoding="utf-8") as f:
            return (json.load(f) or {}).get("UiStyle")
    except (OSError, ValueError) as ex:
        return {"error": str(ex)}


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--out", required=True)
    p.add_argument("--settings-file")
    p.add_argument("--after-restart")
    p.add_argument("--touch", action="store_true")
    p.add_argument("--l2", action="store_true")
    args = p.parse_args()
    if os.path.normpath(args.out).replace("\\", "/").find("logs/ui28/") >= 0:
        print("logs/ui28/ 는 otest 결과 전용이다 — 원시 측정은 logs/<에이전트>/<회차>/ 에 쓴다")
        return 3
    if not os.environ.get("PD_REMOTE_INFO"):
        print("PD_REMOTE_INFO 가 없다 — 다른 앱에 붙지 않도록 임시 data 의 remote-control.json 을 지정한다")
        return 3
    r = Remote()
    status = r.call("status")
    if not status:
        print("앱 연결 실패")
        return 2
    pid = status.get("pid")
    iso = retest_user_files.isolation(pid)
    if not (iso and iso[0]):
        print("격리되지 않은 앱이라 거부한다: %s" % (iso,))
        return 3
    out = {"pid": pid}
    global GUARD
    table = []
    breach = None
    try:
        with OffscreenGuard(pid) as GUARD:
            try:
                if args.after_restart:
                    out["afterRestart"] = state(r)
                    want = args.after_restart
                    table = [{"item": "재시작 뒤 유지", "pass": out["afterRestart"]["setting"] == want and (want == "auto" or out["afterRestart"]["effective"] == want),
                              "detail": "기대=%s setting=%s effective=%s" % (want, out["afterRestart"]["setting"], out["afterRestart"]["effective"])}]
                elif args.touch:
                    measure_touch(r, out)
                    table = judge_touch(out)
                elif args.l2:
                    measure_l2(r, out, pid)
                    table = judge_l2(out)
                else:
                    measure_switch(r, out)
                    measure_rules(r, out)
                    measure_settings(r, out)  # 끝 상태: 수동 태블릿형(스위치→콤보 동기화 항목)
                    out["saved"] = read_saved(args.settings_file)
                    table = judge(out)
                GUARD.check()
            except GuardBreach as ex:
                breach = str(ex)
                print("중단: %s — 앱을 종료한다(pid=%s)" % (breach, pid))
                r.call("app.shutdown")
        out["guard"] = GUARD.summary()
        if out["guard"]["breaches"] and not breach:
            breach = "종료 직전 감지: %s" % out["guard"]["breaches"][0]["line"]
            r.call("app.shutdown")
    finally:
        GUARD = None
    out["table"] = table
    out["breach"] = breach
    os.makedirs(os.path.dirname(os.path.abspath(args.out)), exist_ok=True)
    with open(args.out, "w", encoding="utf-8") as f:
        json.dump(out, f, ensure_ascii=False, indent=1)
    for row in table:
        print("%s\t%s\t%s" % ("PASS" if row["pass"] else "FAIL", row["item"], row["detail"]))
    print("감시: 표본=%s 최대간격=%sms 노출·전경=%d건" % (out["guard"]["samples"], out["guard"]["maxGapMs"], len(out["guard"]["breaches"])))
    print("저장: %s" % args.out)
    if breach:
        return 4
    return 0 if all(row["pass"] for row in table) else 1


if __name__ == "__main__":
    sys.exit(main())
