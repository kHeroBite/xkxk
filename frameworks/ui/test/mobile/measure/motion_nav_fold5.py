#!/usr/bin/env python3
# AC-022 Fold5 전면 화면(NAV) — 폰 효과 부분집합·삼선 메뉴 즐겨찾기·불러오는 중 상태를 실기기에서 잰다
"""
사용
  motion_nav_fold5.py --apk-md5 <설치된 Debug APK 의 md5> --out-dir <고정 결과 폴더> [--only 사례,사례 | --only close] | --self-test

이미 설치된 Debug 판만 잰다(설치·제거 없음). 접지 않고 cmd device_state state 0 으로 전면 화면에 보내고, 끝나면 어떤 경로로든 state reset 한다.
안전 규칙
  - 화면 깨우기·잠금 해제·stayon 변경을 하지 않는다. 꺼져 있거나 잠겨 있거나 판독하지 못하면 손대지 않고 WAIT 로 끝낸다.
  - 시작 때 홈 화면·시험 앱이 아닌 앱이 앞에 있으면 사람이 쓰는 것으로 보고 시작하지 않는다.
  - 조작마다 시험 앱 전면·전면 화면 상태·켜짐·잠금 없음을 다시 보고, 어긋나면 사람 흔적으로 보고 그 자리에서 멈춘다.
  - 측정 중 회전을 세로로 고정하고 끝나면 원래 값으로 되돌린다(motion_nav_fold5_rotation). 캡처가 세로 크기가 아니면 그 사례는 ERROR 다.
한계: 시험 앱 위에서 사람이 만지는 것은 가리지 못한다. 회전은 간격 캡처 3장, 실제 재생은 전환 직후 자리·불투명도 표본으로 본다(못 잡으면 UNDET).
adb 는 ADB 환경변수로 지정한다(예 ADB=adb.exe). 기기는 항상 -s 로 고정하고 서버는 끄지 않는다.
rc 0=전부 PASS 1=FAIL 있음 2=실행 오류 3=대기(미연결·꺼짐·잠김·판 다름) 4=미확인만 남음 5=사람 흔적으로 중단
"""
import argparse
import json
import os
import subprocess
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from motion_nav_fold5_rules import exit_code, expected, may_start, screen_ready, self_test, snapshot  # noqa: E402
import motion_nav_fold5_rotation as R  # noqa: E402  세로 고정·원복·캡처 크기 검사
import retest_android_pcaway_lib as L  # noqa: E402  wake·relaunch·set_closed·set_open 은 화면을 깨우므로 부르지 않는다

NAV_EXPECT = (("wipeRight", "fade"), ("tilt3d", "fade"), ("pushLeft", "pushLeft"), ("fade", "fade"))
SCREEN = "SCR-PRJ-01"
CHANGED_KEYS = ("motion", "motion.open", "motion.switch", "motion.close")
EVIDENCE = {"nav_effects": "probe", "drawer_favorite": "touch", "keys": "key", "loading": "inject", "playback": "touch", "close": "row"}  # row=행마다 따로
EXPECT = {"state": "CLOSED"}  # 안전 확인이 기대하는 화면 상태(닫기 사례의 펼침 구간만 OPENED)


class Wait(Exception):
    """조건이 안 돼 잴 수 없음."""


class Human(Exception):
    """사람 흔적."""


def require_ready(error):
    ok, why = screen_ready(L.sh("dumpsys power | grep mWakefulness="), L.sh("dumpsys window policy"))
    if not ok:
        raise error(why)


def guard():
    require_ready(Human)
    if not L.app_on_top():
        raise Human("시험 앱이 앞에 없다")
    if L.dev_state() != EXPECT["state"]:
        raise Human("화면 상태가 %s 에서 풀렸다" % EXPECT["state"])


def call(c, cmd, **kw):
    guard()
    try:
        result = c.call(cmd, **kw)
    except SystemExit as e:  # pdctl 은 연결 실패 때 프로세스를 끝낸다 — 원복하도록 오류로 바꾼다
        raise RuntimeError("%s 연결 실패(pdctl exit %r)" % (cmd, e.code)) from None
    if result is None:
        raise RuntimeError("%s %s 실패" % (cmd, kw))
    return result


def case_close(c, saved, apk_md5):
    import motion_nav_fold5_close as close  # 닫기는 표본 문서를 닫으므로 --only close 일 때만, 규칙 파일도 그때 읽는다
    return close.run(c, lambda cmd, **kw: call(c, cmd, **kw), saved, apk_md5, L, EXPECT)


def restore(c, saved, want):
    """바꾼 효과 덮어쓰기·연 문서·활성 문서를 기대값(want — 번호 환산)으로 되돌리고 되돌린 뒤 상태를 준다."""
    for key in CHANGED_KEYS:
        call(c, "style.effect.set", key=key, value=saved["overrides"].get(key, ""))  # 빈 값 = 덮어쓰기 해제
    if want is None:
        raise RuntimeError("다시 연 문서 표가 덜 찼다 — 문서는 닫지 않았다")
    for doc in call(c, "style.state")["windows"]:
        if doc["id"] not in want["docs"]:
            call(c, "docs.close", doc=doc["id"])
    if want["active"]:
        call(c, "docs.activate", doc=want["active"])
    time.sleep(2)  # 자동 저장이 끝난 뒤 종료
    return snapshot(call(c, "style.state"), call(c, "favorite.state"))


def precheck(apk_md5):
    if L.adb("get-state")[1].strip() != "device":
        raise Wait("Fold5(%s) 미연결" % L.SER)
    require_ready(Wait)
    if not may_start(L.top_activity()):
        raise Human("다른 앱이 앞에 있다")
    path = L.sh("pm path %s" % L.PKG).replace("package:", "").splitlines()
    if not path:
        raise Wait("시험 앱이 설치돼 있지 않다")
    have = L.sh("md5sum %s" % path[0]).split(" ")[0]
    if have != apk_md5:
        raise Wait("설치된 판 md5 %s 가 기대 %s 와 다르다" % (have, apk_md5))


def start_on_cover(c):
    L.sh("cmd device_state state 0")
    time.sleep(3)
    require_ready(Wait)  # 전환하며 꺼져도 깨우지 않는다
    name = L.sh("cmd package resolve-activity --brief %s" % L.PKG).splitlines()
    if not name or "/" not in name[-1]:
        raise Wait("시작 액티비티를 찾지 못했다")
    L.sh("am start -S -n '%s'" % name[-1])
    time.sleep(8)  # 새 서버가 연결 정보를 다시 쓰기 전에 읽으면 옛 토큰이다
    info = L.attach(c, restart=False)
    if not info:
        raise Wait("원격 연결 실패(Debug 판이 아니거나 기동 실패)")
    guard()
    return info


def case_effects(c):
    rows = []
    for effect, expect in NAV_EXPECT:
        call(c, "style.effect.set", key="motion.open", value=effect)
        probe = call(c, "motion.probe", effect="fade", placement="Nav")
        rows.append({"set": effect, "expect": expect, "open": probe["resolved"]["open"],
                     "platform": probe.get("platform"), "restored": probe.get("allRestored")})
    ok = all(r["open"] == r["expect"] and r["platform"] == "Android" and r["restored"] for r in rows)
    return {"verdict": "PASS" if ok else "FAIL", "why": "NAV 해석값·platform·제자리 복원", "rows": rows}


def case_favorite(c):
    added = SCREEN not in call(c, "favorite.state")["items"]
    if added:
        call(c, "favorite.toggle", screen=SCREEN)
    call(c, "docs.open", kind="gantt")
    try:
        guard()
        if not c.tap_id("shell.btnDrawer"):
            return {"verdict": "UNDET", "why": "삼선 단추를 찾지 못했다"}
        time.sleep(2)
        guard()
        shot = c.shot("drawer_favorite")
        ids = [i for i in c.ids(c.dump()) if "favorite" in i.lower()]
        guard()
        opened = None
        if any(SCREEN in i for i in ids) and c.tap_id("shell.favoriteList." + SCREEN):  # 화면 좌표 실터치
            time.sleep(2)
            opened = call(c, "style.state")["active"]
        else:
            L.key_back()
        ok = bool(opened) and opened.startswith("Grid")  # 누르기 전 활성은 간트
        return {"verdict": "PASS" if ok else "FAIL", "why": "삼선 메뉴 즐겨찾기 실터치 → 그리드 활성", "ids": ids, "shot": shot, "opened": opened}
    finally:
        if added and L.app_on_top():
            c.call("favorite.toggle", screen=SCREEN)


def case_loading(c):
    before = call(c, "sample.load")
    call(c, "sample.load", delayMs=9000)
    try:
        call(c, "docs.open", kind="grid")
        seen, left, shots = False, None, []
        until = time.time() + 30
        while time.time() < until:
            left = [d["id"] for d in call(c, "window.load.state")["docs"] if d["isLoading"]]
            if left and not seen:
                seen = True
                for i in range(3):
                    shots.append(c.shot("loading_%d" % i))
                    time.sleep(0.35)
                if not any(d["isLoading"] for d in call(c, "window.load.state")["docs"]):
                    shots = []  # 찍는 사이 끝났으면 회전 근거가 못 된다
            if seen and not left:
                break
            time.sleep(0.3)
    finally:
        if L.app_on_top():
            c.call("sample.load", delayMs=before["delayMs"])
    if not seen:
        return {"verdict": "UNDET", "why": "불러오는 중을 관측하지 못했다"}
    if left:
        return {"verdict": "FAIL", "why": "끝나지 않았다", "left": left}
    if not shots:
        return {"verdict": "UNDET", "why": "상태는 켜졌다 꺼짐. 회전 캡처 3장을 못 얻었다"}
    n = R.distinct_spinner_frames(shots)  # 세로 기준 크기가 아니면 RotationError → ERROR
    return {"verdict": "PASS" if n == 3 else "FAIL", "why": "상태 켜졌다 꺼짐·스피너 칸 서로 다른 그림 %d/3(회전)" % n, "shots": shots}


def case_playback(c):
    import motion_nav_fold5_touch as touch  # 실재생은 실제 터치 경로로만 잰다
    return touch.run(c, lambda cmd, **kw: call(c, cmd, **kw), L)


def case_keys(c):
    """Ctrl+F6 주입이 안 먹으면 주입 문제인지 앱 문제인지 가른다(F1=주입 도달 대조, 초점 전·뒤)."""
    def press(keys):
        guard()
        L.sh("input %s" % keys)
        time.sleep(2.5)

    def switched():
        before = call(c, "style.state")["active"]
        press("keycombination 113 136")
        return call(c, "style.state")["active"] != before

    out = {"ctrl_f6_cold": switched()}
    press("tap 450 1100")  # 문서 본문에 초점
    out["ctrl_f6_focused"] = switched()
    press("keyevent 131")
    out["f1_reaches_app"] = bool(call(c, "tour.state"))
    c.call("tour.end")
    if out["ctrl_f6_focused"]:
        return {"verdict": "PASS", "why": "초점 뒤 Ctrl+F6 전환됨", "steps": out}
    return {"verdict": "UNDET", "why": "전환 안 됨. F1 도달=%s(아니면 주입·초점, 맞으면 조합 키·앱 쪽)" % out["f1_reaches_app"], "steps": out}


def run(args, res):
    precheck(args.apk_md5)
    res["state_before"] = L.dev_state()
    c = L.Ctx("fold5", "Debug", args.out_dir)
    info, saved, rot = None, {}, {}
    try:
        rot = res["rotation_before"] = R.lock_portrait(L.sh)  # 기기를 가로로 둬도 세로 기준 좌표·칸으로 잰다
        info = start_on_cover(c)
        style = call(c, "ui.style")
        res["style"] = style
        if str(style.get("effective")).lower() != "mobile":
            raise RuntimeError("전면 화면인데 모바일형이 아니다: %s" % style.get("effective"))
        saved = snapshot(call(c, "style.state"), call(c, "favorite.state"))
        res["app_before"] = dict(saved)
        call(c, "style.effect.set", key="motion", value="cinematic")
        cases = (("nav_effects", case_effects), ("drawer_favorite", case_favorite), ("keys", case_keys),
                 ("loading", case_loading), ("playback", case_playback), ("close", lambda x: case_close(x, saved, args.apk_md5)))
        for name, fn in [x for x in cases if (x[0] in args.only.split(",") if args.only else x[0] != "close")]:
            try:
                at = {"ui_style": call(c, "ui.style").get("effective"), "capture": R.check_capture(c.shot("case_" + name)), "evidence": EVIDENCE[name]}
                if str(at["ui_style"]).lower() != "mobile":
                    res["cases"][name] = dict(at, verdict="UNDET", why="INVALID — 기대 배치(모바일형)가 아니다")
                else:
                    res["cases"][name] = dict(at, **{k: v for k, v in fn(c).items() if k not in at})
                    if at["evidence"] in ("probe", "inject") and res["cases"][name]["verdict"] == "PASS":
                        res["cases"][name]["real_path_pass"] = False  # 실경로 PASS 로 세지 않는다
            except (RuntimeError, KeyError) as e:
                res["cases"][name] = {"verdict": "ERROR", "why": repr(e)[:300]}
            print(name, res["cases"][name]["verdict"], res["cases"][name]["why"], flush=True)
    finally:
        try:
            if c.r and saved:
                guard()  # 사람 흔적이면 원복 조작도 하지 않는다(결과에 app_after 가 없으면 원복 미확인)
                res["app_restored"], res["remap"] = False, saved.get("remap")  # remap 이 있으면 같은 종류 복원이고 번호는 바뀐다
                try:
                    want = expected(saved)
                    res["app_after"] = restore(c, saved, want)
                    res["app_restored"] = res["app_after"] == want
                except (RuntimeError, KeyError) as e:
                    res["restore_error"] = repr(e)[:300]
                L.sh("am force-stop %s" % L.PKG)  # 받은 대로 앱 종료 상태
        finally:
            L.sh("cmd device_state state reset")
            time.sleep(2)
            res["state_after"] = L.dev_state()
            L.adb("forward", "--remove", "tcp:%d" % L.ra.HOST_PORT)
            if info:
                os.remove(info)  # attach 가 만든 토큰 임시 파일
            res["rotation_restored"] = R.restore(L.sh, rot)  # 받은 회전 설정으로 되돌린다(고정 전 실패면 rot 가 비어 아무것도 안 함)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--self-test", action="store_true")
    for name in ("--apk-md5", "--out-dir", "--only"):  # --only 케이스,케이스
        ap.add_argument(name)
    args = ap.parse_args()
    if args.self_test:
        import motion_nav_fold5_frames as frames  # 화소 판정
        return self_test() or frames.self_test() or R.self_test()
    if not (args.apk_md5 and args.out_dir):
        ap.error("--apk-md5 --out-dir 가 필요하다")
    os.makedirs(args.out_dir, exist_ok=True)
    res = {"tool": os.path.basename(__file__), "apk_md5": args.apk_md5, "cases": {}}
    try:
        run(args, res)
        rc = 2 if "restore_error" in res else exit_code([c["verdict"] for c in res["cases"].values()])
    except Wait as e:
        rc, res["wait"] = 3, str(e)
    except Human as e:
        rc, res["interrupted"] = 5, str(e)
    except (OSError, RuntimeError, KeyError, SystemExit, subprocess.SubprocessError) as e:
        rc, res["error"] = 2, repr(e)[:400]
    res["rc"], res["finished"] = rc, time.strftime("%Y-%m-%d %H:%M:%S")
    out = os.path.join(args.out_dir, "motion_nav_fold5.json")
    with open(out, "w", encoding="utf-8") as f:
        json.dump(res, f, ensure_ascii=False, indent=1, default=str)
    print("RESULT rc=%d %s %s" % (rc, res.get("wait") or res.get("interrupted") or res.get("error") or "", out), flush=True)
    return rc


if __name__ == "__main__":
    sys.exit(main())
