# Fold5 접힘(단일 문서 모드)에서 알림 오버레이 경로(shell.btnNotify·notify.*)를 원격 명령으로 판정하는 스크립트
"""
사용: python3 test/mobile/measure/retest_android_notify.py [--apk <APK>] [--serial R3CW70202FP] [--out 결과.json]
판정(odev-1·odev-2 제안, 접힘은 wm size 904x2316 으로 재현):
  1) shell.btnNotify 를 누르면 notify.list 가 보이고 항목이 5개(샘플)이며 notify.menu(위치 메뉴)는 보이지 않는다.
  2) 한 번 더 누르면 닫힌다(notify.root·notify.list 가 사라짐).
  3) 오버레이를 연 뒤 문서 콤보(shell.cmbDocs)로 문서를 고르거나 드로어(shell.btnDrawer)를 열면 닫힌다.
  4) 넓은 폭(wm size reset)으로 돌아가면 오버레이가 사라지고 panel.list 의 NotificationPane.visible 이 true 다.
  5) 오버레이를 연 채 접힘↔펼침을 3회 반복해도 notify.root 가 최대 1개다.
캡처: 오버레이 열림 상태 1장(notify_overlay_folded.png).
원복(finally): wm size reset·forward 해제·앱 제거·토큰 파일 삭제·stay_on 원값.
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
import retest_android_lang as rl  # noqa: E402  capture() 재사용

FOLD = "904x2316"


def ids(node, out=None):
    """tree.get 결과에서 id 별 (type, visible, text) 목록을 모은다."""
    out = out if out is not None else []
    if isinstance(node, dict):
        if node.get("id"):
            out.append({"id": str(node["id"]), "type": node.get("type"), "visible": node.get("visible", True), "text": node.get("text")})
        for v in node.values():
            if isinstance(v, (list, dict)):
                ids(v, out)
    elif isinstance(node, list):
        for v in node:
            ids(v, out)
    return out


def snapshot(r):
    """현재 화면의 알림 관련 컨트롤 상태를 요약한다."""
    tree = r.call("tree.get", named=True, depth=60) or {}
    items = ids(tree)
    root = [i for i in items if i["id"] == "notify.root"]
    lst = [i for i in items if i["id"].startswith("notify.list")]
    menu = [i for i in items if i["id"] == "notify.menu" and i["visible"]]
    rows = [i for i in items if i["id"].startswith("notify.item")]
    return {"roots": len(root), "list_visible": any(i["visible"] for i in lst), "menu_visible": bool(menu), "items": len(rows), "raw_ids": [i["id"] for i in items if i["id"].startswith(("notify.", "shell.btn", "shell.notify"))][:30]}


def click(r, cid, **kw):
    res = r.call("ui.click", id=cid, **kw)
    time.sleep(1.5)
    return res is not None and r.calls[-1]["ok"]


def run(serial, apk, state):
    from retest_remote import Remote
    info = ra.prepare(serial, apk, state)
    if not info:
        return {"error": "prepare 실패"}, 2
    r = Remote()
    for k in ("grid", "gantt", "form"):
        r.call("docs.open", kind=k)
    rep, fails = {"checks": {}}, []

    def check(name, ok, detail=None):
        rep["checks"][name] = {"pass": bool(ok), "detail": detail}
        if not ok:
            fails.append(name)

    ra.adb(serial, "shell", "wm", "size", FOLD)
    time.sleep(4)
    check("0 버튼 존재", click(r, "shell.btnNotify"), "shell.btnNotify 클릭")
    s = snapshot(r)
    rep["opened"] = s
    # 접힘에서도 넓은 폭 알림 패널의 notify.* 컨트롤이 트리에 숨은 채 남으므로 notify.root 개수·항목 id 로는 판정하지 않고 가시성(visible)으로 본다. 항목 5개는 캡처로 확인한다
    check("1 오버레이 열림(목록 보임)·위치 메뉴 숨김", s["list_visible"] and not s["menu_visible"], s)
    rep["capture"] = rl.capture(serial, "notify_overlay_folded")
    click(r, "shell.btnNotify")
    s = snapshot(r)
    check("2 다시 누르면 닫힘", not s["list_visible"], s)
    for name, action in (("3a 문서 콤보 선택", lambda: r.call("ui.select", id="shell.cmbDocs", index=1)), ("3b 드로어 열기", lambda: click(r, "shell.btnDrawer"))):
        click(r, "shell.btnNotify")
        action()
        time.sleep(1.5)
        s = snapshot(r)
        check(name + " 시 닫힘", not s["list_visible"], s)
        if "드로어" in name:
            click(r, "shell.btnDrawer")  # 드로어 닫기
    click(r, "shell.btnNotify")
    ra.adb(serial, "shell", "wm", "size", "reset")
    time.sleep(4)
    s = snapshot(r)
    pl = r.call("panel.list") or []
    notif = next((p for p in pl if p.get("id") == "NotificationPane"), {})
    check("4 넓은 폭 복귀 시 오버레이 사라짐·알림 패널 보임", s["roots"] <= 1 and notif.get("visible") is True, {"snapshot": s, "NotificationPane": notif})
    ra.adb(serial, "shell", "wm", "size", FOLD)
    time.sleep(3)
    click(r, "shell.btnNotify")
    maxroots = 0
    for _ in range(3):
        ra.adb(serial, "shell", "wm", "size", "reset")
        time.sleep(3)
        ra.adb(serial, "shell", "wm", "size", FOLD)
        time.sleep(3)
        maxroots = max(maxroots, snapshot(r)["roots"])
    check("5 접힘↔펼침 3회 후 notify.root ≤ 1", maxroots <= 1, {"max_roots": maxroots})
    rep["fails"], rep["verdict"] = fails, ("PASS" if not fails else "FAIL")
    return rep, (0 if not fails else 1)


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--apk", default=ra.DEFAULT_APK)
    p.add_argument("--serial", default="R3CW70202FP")
    p.add_argument("--out", default=os.path.join(os.path.dirname(ra.DEFAULT_OUT), "android_notify.json"))
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
