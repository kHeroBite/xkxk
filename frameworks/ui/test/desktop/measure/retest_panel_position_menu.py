#!/usr/bin/env python3
# 패널 '위치' 메뉴 회귀 측정 — 알림 제목 건수, 콤보 제거, 패널·탭별 메뉴 항목, 메뉴 항목 실행 뒤 위치, 현재 위치 비활성, 저장·복원을 값으로 남긴다
"""
사용: PD_REMOTE_INFO=<임시 data>/remote-control.json python3 -B retest_panel_position_menu.py --stage all|after --out logs/<이름>/<회차>/panel_menu.json
앱은 먼저 임시 폴더·화면 밖으로 띄운다:
  run_desktop_bg.sh --conf run/<복사본> --no-kill -- --remote-control --multi-instance on --data-dir <임시> --profile-dir <임시>
      --bg-pos 21500,100 --primary-override 18000,0,6000,1440 --offscreen-guard on
단계: all = 전 항목(끝에 저장 배치를 남기고 app.closeMain 은 호출자가 --close 로 고른다). after = 같은 data 로 재기동한 뒤 배치가 복원됐는지.
메뉴는 팝업을 열지 않는다. panelmenu.read 는 실제 메뉴 인스턴스의 항목을 읽고, panelmenu.click 은 항목 Click 을 일으켜 화면 메뉴와 같은 경로를 탄다. 실제 우클릭과 팝업 표시는 사람 확인 항목이다(웹은 retest_panel_position_menu_web.py 가 실마우스로 잰다).
격리되지 않은 앱은 rc=3 으로 거부하고, 겹침·전경은 GuardBreach 로 즉시 멈춘다.
종료 코드: 0=전부 통과, 1=실패 있음, 2=연결 실패, 3=실행 거부, 4=가드 중단.
"""
import argparse
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "..", "common"))  # D-TESTDIR
import _paths  # noqa: E402
_paths.add()
sys.path.insert(0, HERE)

from panel_position_checks import *  # noqa: E402,F401,F403 — 웹 구동기도 이 모듈 이름으로 항목을 부른다


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--stage", choices=["all", "after"], required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--close", action="store_true", help="끝에 app.closeMain 으로 닫아 종료 저장을 태운다")
    args = ap.parse_args()
    if not os.environ.get("PD_REMOTE_INFO"):
        print("PD_REMOTE_INFO 가 없다 — 다른 앱에 붙지 않도록 임시 data 의 remote-control.json 을 지정한다")
        return 3
    from retest_remote import Remote  # 웹 구동기가 이 파일을 import 할 때 데스크톱 의존을 끌어오지 않게 여기서 읽는다
    from offscreen_guard import OffscreenGuard, GuardBreach
    with open(os.environ["PD_REMOTE_INFO"], encoding="utf-8-sig") as f:
        pid = json.load(f).get("pid")
    r = Remote()
    if not r.call("status"):
        return 2
    out = {"stage": args.stage, "pid": pid}
    rc = 0
    k = Checks(None)
    try:
        with OffscreenGuard(pid) as guard:
            def c(cmd, **kw):
                guard.check()
                return r.call(cmd, **kw)
            k.c = c
            (run_all if args.stage == "all" else after_restart)(k)
            guard.check()
            if args.close:
                c("app.closeMain")
        out["guard"] = guard.summary()
    except GuardBreach as e:
        out["breach"] = str(e)
        rc = 4
    out["items"] = k.items
    out["passed"], out["total"] = sum(i["pass"] for i in k.items), len(k.items)
    os.makedirs(os.path.dirname(os.path.abspath(args.out)), exist_ok=True)
    with open(args.out, "w", encoding="utf-8") as f:
        json.dump(out, f, ensure_ascii=False, indent=1)
    print("결과 %d/%d" % (out["passed"], out["total"]))
    return rc or (0 if out["passed"] == out["total"] else 1)


if __name__ == "__main__":
    sys.exit(main())
