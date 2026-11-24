# Fold5 실기기에서 --profile-dir 로 테마·언어·글꼴·패널 위치·문서가 재기동 뒤 유지되는지(지속성)와 프로필 격리를 판정하는 스크립트
"""
사용: python3 tools/retest_android_profile.py --apk <진단 APK> [--serial R3CW70202FP] [--out 결과.json]
시나리오(pdargs 는 files/pdargs.txt 로 1회용 주입):
  1) 1차 실행 `--profile-dir t1`: 테마 pd-ocean·언어 en·글꼴 Noto Mono·글자 크기 16·알림 패널 Left·문서(grid·form) 상태를 만들고 layout.save → 앱 종료
  2) 2차 실행 같은 인자: 값이 유지되는지(theme·lang·font·baseFontSize·panel side·문서 제목) 비교. 첫 기동 직후 설정은 앱 기본 파일 값일 수 있어
     (odev-2) 설정 유지는 2차 실행에서 판정한다. panel.list 는 NotReady 동안 1초 간격 최대 10초 재시도하고 첫 성공까지 ms 를 기록한다.
  3) 3차 실행 인자 없음(프로필 격리): 1차의 값이 새지 않고 기본값으로 시작하는지 본다.
  logcat 의 `[shell] profile-dir=`·`startup-restore=` 줄을 각 실행마다 기록한다.
원복(finally): forward 해제·앱 제거(앱 데이터·프로필 폴더 삭제)·토큰 파일 삭제·stay_on 원값. 종료 코드 0=전부 PASS, 1=FAIL 있음, 2=준비 오류.
"""
import argparse
import datetime
import json
import os
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import retest_android as ra  # noqa: E402
import retest_android_cond2 as c2  # noqa: E402  launch() 재사용


def ready_list(r, serial):
    """panel.list 가 성공할 때까지 1초 간격 최대 10초 재시도한다. (결과, 첫 성공까지 ms, 재시도 횟수)"""
    t0, retries = time.time(), 0
    while time.time() - t0 < 10:
        res = r.call("panel.list")
        if res is not None and r.calls[-1]["ok"]:
            return res, round((time.time() - t0) * 1000), retries
        retries += 1
        time.sleep(1)
    return None, None, retries


def snapshot(r, serial):
    rc, log = ra.adb(serial, "logcat", "-d", "-v", "brief")
    pl, ms, retries = ready_list(r, serial)
    side = next((p.get("side") for p in (pl or []) if p.get("id") == "NotificationPane"), None)
    st = r.call("settings.get") or {}
    lang = r.call("lang.get") or {}
    docs = r.call("docs.list") or []
    return {"panel_first_success_ms": ms, "panel_retries": retries, "notify_side": side, "themeId": st.get("activeThemeId") or st.get("themeId"),
            "baseFontSize": st.get("baseFontSize"), "fontName": st.get("fontName"), "language": lang.get("language"), "culture": lang.get("culture"),
            "docs": sorted(d.get("title") for d in docs),
            "log_profile_dir": [ln[-120:] for ln in log.splitlines() if "profile-dir=" in ln][-1:],
            "log_startup_restore": [ln[-120:] for ln in log.splitlines() if "startup-restore=" in ln or "StartupRestore" in ln][-2:]}


def run(serial, apk, state):
    from retest_remote import Remote
    info = ra.prepare(serial, apk, state)
    if not info:
        return {"error": "prepare 실패"}, 2
    rep, fails = {"runs": {}}, []
    # 1차: 상태 만들기
    if not c2.launch(serial, "--profile-dir t1", info):
        return {"error": "1차 기동 실패"}, 2
    r = Remote()
    ready_list(r, serial)
    r.call("docs.close", all=True)
    for k in ("grid", "form"):
        r.call("docs.open", kind=k)
    r.call("theme.select", id="pd-ocean")
    r.call("lang.set", code="en")
    r.call("settings.set", baseFontSize=16)
    r.call("settings.set", fontName="monospace")  # Android 글꼴 목록(settings.fonts)에는 Noto Mono 가 없어 적용되지 않는 것을 실측했다 — 목록에 있는 monospace 를 쓴다
    time.sleep(1)
    rep["font_after_set"] = (r.call("settings.get") or {}).get("fontName")
    r.call("panel.dock", id="NotificationPane", side="left")
    r.call("layout.save")
    time.sleep(3)
    rep["runs"]["1_make"] = snapshot(r, serial)
    ra.adb(serial, "shell", "am", "force-stop", ra.PKG)
    time.sleep(2)
    # 2차: 같은 프로필로 재기동
    c2.launch(serial, "--profile-dir t1", info)
    r = Remote()
    time.sleep(3)
    snap2 = snapshot(r, serial)
    rep["runs"]["2_same_profile"] = snap2
    want = {"themeId": "pd-ocean", "language": "en", "fontName": "monospace", "baseFontSize": 16, "notify_side": "Left"}
    for k, v in want.items():
        got = snap2.get(k)
        ok = (str(got).lower() == str(v).lower()) if got is not None else False
        rep.setdefault("checks", {})["2차 " + k] = {"want": v, "got": got, "pass": ok}
        if not ok:
            fails.append("2차 " + k)
    docs_ok = any("Grid" in str(t) or "그리드" in str(t) for t in snap2["docs"]) and any("Form" in str(t) or "입력폼" in str(t) for t in snap2["docs"])
    rep["checks"]["2차 문서 복원(grid·form)"] = {"got": snap2["docs"], "pass": docs_ok}
    if not docs_ok:
        fails.append("2차 문서 복원")
    ra.adb(serial, "shell", "am", "force-stop", ra.PKG)
    time.sleep(2)
    # 3차: 프로필 인자 없음(격리)
    c2.launch(serial, "--notify-panel on", info)
    r = Remote()
    time.sleep(3)
    snap3 = snapshot(r, serial)
    rep["runs"]["3_no_profile"] = snap3
    leaked = [k for k in ("themeId", "fontName", "language") if str(snap3.get(k)).lower() in ("pd-ocean", "monospace", "en")]
    rep["checks"]["3차 프로필 격리(t1 값이 새지 않음)"] = {"leaked": leaked, "got": {k: snap3.get(k) for k in ("themeId", "fontName", "language", "notify_side")}, "pass": not leaked}
    if leaked:
        fails.append("3차 프로필 격리")
    rep["fails"], rep["verdict"] = fails, ("PASS" if not fails else "FAIL")
    return rep, (0 if not fails else 1)


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--apk", required=True)
    p.add_argument("--serial", default="R3CW70202FP")
    p.add_argument("--out", default=os.path.join(os.path.dirname(ra.DEFAULT_OUT), "android_profile.json"))
    a = p.parse_args()
    state, code = {}, 2
    report = {"started": datetime.datetime.now().isoformat(timespec="seconds"), "apk": a.apk}
    try:
        res, code = run(a.serial, a.apk, state)
        report.update(res)
    except Exception as e:  # noqa: BLE001 — 오류도 보고서에 남기고 원복은 finally 가 보장
        report["error"] = repr(e)
    finally:
        ra.cleanup(a.serial, state)
        ra.adb(a.serial, "shell", "wm", "size", "reset")
        os.makedirs(os.path.dirname(a.out), exist_ok=True)
        with open(a.out, "w", encoding="utf-8") as f:
            json.dump(report, f, ensure_ascii=False, indent=1)
        print("결과: " + a.out)
    return code


if __name__ == "__main__":
    sys.exit(main())
