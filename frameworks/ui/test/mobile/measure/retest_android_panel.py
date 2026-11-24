# Fold5 실기기에서 알림 패널의 위치 고정 없음·가장자리 도킹·가운데 거부·재기동 유지를 원격 명령으로 판정하는 스크립트
"""
사용: python3 test/mobile/measure/retest_android_panel.py [--apk <APK>] [--serial R3CW70202FP] [--out 결과.json]
검사(Q16, UI-15):
  1) panel.list 의 세 패널이 모두 locked=false 다.
  2) panel.dock side=left|right|top|bottom 이 각각 Docked 이고 panel.list 의 side 가 그 값으로 바뀐다.
  3) panel.dock side=center 는 Rejected(center-not-allowed)이고 side 가 그대로다. 다른 패널 dock 도 같은 규칙으로 Docked.
  4) panel.canDrop(source=NotificationPane, target=Documents|LeftPane, op=Fill|Left|Right|Top|Bottom|Window[, inner])에 허용 항목이 있다(모든 패널 같은 규칙).
  6) 재기동 유지: dock side=left → layout.save → 앱 재시작 → panel.list 의 NotificationPane.side 가 left. 끝에 right 로 되돌린다.
  7) 접힘(wm size 904x2316) 에서 panel.list 의 visible 값을 기록한다(단일 문서 모드에서 알림 패널 처리).
원복(finally): 알림 패널 side=right·wm size reset·forward 해제·앱 제거·토큰 파일 삭제·stay_on 원값.
종료 코드: 0=전부 PASS, 1=FAIL 있음, 2=준비 오류.
"""
import argparse
import datetime
import json
import os
import sys
import time

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "common"))  # D-TESTDIR: test/common/_paths.py
import _paths  # noqa: E402
_paths.add()
import retest_android as ra  # noqa: E402
import retest_android_lang as rl  # noqa: E402  restart() 재사용

SIDES = ("left", "right", "top", "bottom", "center")
OPS = ("Fill", "Left", "Right", "Top", "Bottom", "Window")


def panel(plist, pid):
    for p in plist or []:
        if p.get("id") == pid:
            return p
    return {}


def run(serial, apk, state):
    from retest_remote import Remote
    info = ra.prepare(serial, apk, state)
    if not info:
        return {"error": "prepare 실패"}, 2
    r = Remote()
    for k in ("grid", "form"):
        r.call("docs.open", kind=k)
    rep, fails = {"checks": {}}, []

    def check(name, ok, detail=None):
        rep["checks"][name] = {"pass": bool(ok), "detail": detail}
        if not ok:
            fails.append(name)

    plist = r.call("panel.list") or []
    rep["panel_list_initial"] = plist
    n = panel(plist, "NotificationPane")
    check("1 패널 위치 고정 없음", not any(panel(plist, i).get("locked") for i in ("NotificationPane", "LeftPane", "BottomPane")), n)
    original_side = n.get("side")
    rep["original_side"] = original_side
    try:
        docks = {}
        for s in SIDES:
            res = r.call("panel.dock", id="NotificationPane", side=s) or {}
            after = panel(r.call("panel.list") or [], "NotificationPane").get("side")
            docks[s] = {"resp": res, "side_after": after}
            if s != "center":
                check("2 dock %s" % s, res.get("status") == "Docked" and str(after).lower() == s, docks[s])  # 응답 side 는 대문자 시작(Left)
            else:
                check("3 dock center 거부", res.get("status") == "Rejected" and res.get("reason") == "center-not-allowed" and after != "center", docks[s])
        rep["docks"] = docks
        other = r.call("panel.dock", id="LeftPane", side="right") or {}
        check("3 다른 패널도 같은 규칙으로 이동", other.get("status") == "Docked", other)
        r.call("panel.dock", id="LeftPane", side="left")
        drops = {}
        for tgt in ("Documents", "LeftPane"):
            for op in OPS:
                for inner in (False, True):
                    kw = dict(source="NotificationPane", target=tgt, op=op)
                    if inner:
                        kw["inner"] = True
                    res = r.call("panel.canDrop", **kw)
                    drops["%s/%s/%s" % (tgt, op, "inner" if inner else "edge")] = res
        rep["canDrop"] = drops
        allowed = {k: v for k, v in drops.items() if isinstance(v, dict) and v.get("policy") is True}
        check("4 알림 패널 끌어 놓기 판정 허용", bool(allowed), list(allowed)[:6])
        # 재기동 유지
        r.call("panel.dock", id="NotificationPane", side="left")
        r.call("layout.save")
        if not rl.restart(serial, info):
            check("6 재기동 후 연결", False, "연결 실패")
        else:
            r2 = Remote()
            # 재기동 직후 panel.list 가 '표시된 창이 없습니다'(화면 준비 전) 오류로 실패할 수 있어 성공 응답이 나올 때까지 1초 간격 최대 10초 재시도하고,
            # 재기동(연결 정보 확보)부터 첫 성공 응답까지 걸린 시간(ms)과 재시도 횟수를 기록한다. 10초 안에 준비되지 않으면 FAIL.
            t0, retries, first = time.time(), 0, None
            while time.time() - t0 < 10:
                res = r2.call("panel.list")
                if res is not None and r2.calls[-1]["ok"] and panel(res, "NotificationPane"):
                    first = res
                    break
                retries += 1
                rep.setdefault("not_ready_errors", []).append((r2.calls[-1].get("error") or "")[:80])
                time.sleep(1)
            rc_l, log_l = ra.adb(serial, "logcat", "-d", "-v", "brief")  # i0315 부터 odev-2 가 남기는 기동 복원 결과(restored / skipped:… / failed:…)를 같은 회차에 기록한다
            rep["startup_restore_log"] = [ln[-140:] for ln in log_l.splitlines() if "startup-restore=" in ln][-2:]
            rep["profile_dir_log"] = [ln[-140:] for ln in log_l.splitlines() if "profile-dir=" in ln][-1:]
            rep["panel_list_first_success_ms"] = round((time.time() - t0) * 1000) if first else None
            rep["panel_list_retries"] = retries
            side = panel(first or [], "NotificationPane").get("side")
            rep["side_immediately_after_restart"] = side
            check("6 재기동 후 10초 안에 panel.list 준비", first is not None, {"first_success_ms": rep["panel_list_first_success_ms"], "retries": retries})
            rep["side_after_restart_no_load"] = side
            if str(side).lower() != "left":  # 자동 복원이 없으면 layout.load 로 불러와 비교하고 둘 다 기록한다
                r2.call("layout.load")
                rep["side_after_layout_load"] = panel(r2.call("panel.list") or [], "NotificationPane").get("side")
            check("6 재기동 후 side 유지(left)", str(side).lower() == "left", {"after_restart": side, "after_layout_load": rep.get("side_after_layout_load")})
            r2.call("panel.dock", id="NotificationPane", side="right")
            r = r2
        # 접힘에서 visible 기록
        ra.adb(serial, "shell", "wm", "size", "904x2316")
        time.sleep(3)
        rep["folded_panel_list"] = r.call("panel.list")
    finally:
        try:
            r.call("panel.dock", id="NotificationPane", side=original_side or "right")
        except Exception as e:  # noqa: BLE001
            print("side 원복 실패 %s" % e)
    rep["fails"], rep["verdict"] = fails, ("PASS" if not fails else "FAIL")
    return rep, (0 if not fails else 1)


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--apk", default=ra.DEFAULT_APK)
    p.add_argument("--serial", default="R3CW70202FP")
    p.add_argument("--out", default=os.path.join(os.path.dirname(ra.DEFAULT_OUT), "android_panel.json"))
    a = p.parse_args()
    state, code = {}, 2
    report = {"started": datetime.datetime.now().isoformat(timespec="seconds"), "apk": a.apk}
    try:
        res, code = run(a.serial, a.apk, state)
        report.update(res)
    except Exception as e:  # noqa: BLE001 — 오류도 보고서에 남기고 원복은 finally 가 보장
        report["error"] = repr(e)
    finally:
        try:
            ra.adb(a.serial, "shell", "wm", "size", "reset")
        except Exception as e:  # noqa: BLE001
            print("wm size 원복 실패 %s" % e)
        ra.cleanup(a.serial, state)
        os.makedirs(os.path.dirname(a.out), exist_ok=True)
        with open(a.out, "w", encoding="utf-8") as f:
            json.dump(report, f, ensure_ascii=False, indent=1)
        print("결과: " + a.out)
    return code


if __name__ == "__main__":
    sys.exit(main())
