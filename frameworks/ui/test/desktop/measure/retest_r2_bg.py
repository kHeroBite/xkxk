#!/usr/bin/env python3
# 사용자 복귀 중에도 실입력·전경 없이 T1·T3·Q2 를 재는 백그라운드 구동기(앱 내장 원격 명령과 앱 자체 스크린샷만 사용)
"""사용:
  python3 -B retest_r2_bg.py --tool titlebar|panel --items T3 --run-dir <bin/run/<복사본>> --phase discovery|final --writer otest-8
시험 앱은 --bg-pos auto 로 모든 모니터 밖에 띄우고 창을 옮기지 않으며 전경을 가져오지 않는다. 클릭·키·끌기·호버는 InputRefused('foreground_pending') 로 막혀
그 측정 이름이 결과의 foreground_pending 목록에 남고 판정은 FAIL 그대로다(값을 꾸미지 않는다). 복귀 표지가 있어도 실행하지만 데스크톱 역할 판정기는 복귀 표지 뒤 결과를 인정하지 않는다.
"""
import argparse
import os
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import retest_real_input as ri  # noqa: E402
import retest_maximize_monitor as base  # noqa: E402
import mdi_drag_app as app  # noqa: E402

PENDING = "foreground_pending"
_REAL_WP = base.wp


def _wp_tolerant(pid, ops):
    """창 목록 출력에 cp949 제목이 섞여 디코딩이 깨지는 것을 막는다(좌표만 쓰므로 제목 글자는 대체 문자여도 된다)."""
    import json
    import subprocess
    out = subprocess.run(["powershell.exe", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", base.to_win(base.PS), "-OwnPid", str(pid), "-Ops", ";".join(ops)],
                         capture_output=True, timeout=90)
    text = out.stdout.decode("utf-8", errors="replace")
    res = [json.loads(line) for line in text.splitlines() if line.strip().startswith("{")]
    if len(res) != len(ops):
        raise RuntimeError("win_place 결과 %d/%d: %s" % (len(res), len(ops), out.stderr[:400]))
    return res


base.wp = _wp_tolerant


class BgRig(ri.Rig):
    l0_click = True  # click_ctl 을 쓰는 도구는 앱 내장 ui.click 으로 대신한다(결과 notes 에 L0 한계 명시)

    def start(self, extra=(), reuse=False):
        real = _wp_tolerant

        def no_move(pid, ops):
            keep = [o for o in ops if not str(o).startswith("move:")]
            return real(pid, keep) if keep else []
        base.wp = no_move
        try:
            super().start(extra=("--bg-pos", "auto") + tuple(extra), reuse=reuse)
        finally:
            base.wp = real

    def acquire(self, hwnd):
        return self.foreground()

    def gate(self, hwnd=None):
        raise ri.InputRefused(PENDING)

    def click(self, *a, **k):
        raise ri.InputRefused(PENDING)

    def key(self, *a, **k):
        raise ri.InputRefused(PENDING)

    def grab(self, res, tag):
        os.makedirs(res.shots, exist_ok=True)
        path = os.path.join(res.shots, tag + ".png")
        self.r.call("screenshot", window=0, path=app.win(path))
        if os.path.exists(path):
            res.capture(path)


class BgResult(ri.Result):
    def write(self, build_required=True):
        pend = [m["name"] for m in self.measurements if PENDING in str(m["value"])]
        self.notes.append({"mode": "bg", "method": "앱 내장 원격 명령(L0, 클릭은 ui.click 이라 포인터 경로 미검증)+앱 자체 스크린샷, --bg-pos auto, 실입력·전경 없음", "foreground_pending": pend})
        return super().write(build_required)


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ri.add_common_args(p)
    p.add_argument("--tool", choices=("titlebar", "panel", "strip", "extra2"), required=True)
    p.add_argument("--before-strip", type=float, default=None)
    p.add_argument("--before-tab", type=float, default=None)
    args = p.parse_args()
    if args.tool == "titlebar":
        import retest_r2_titlebar as mod
    elif args.tool == "panel":
        import retest_panel_feature as mod
    elif args.tool == "strip":
        import retest_title_strip as mod
    else:
        import retest_pcaway_extra2 as mod
    if not args.writer.startswith("otest") or not args.run_dir or not args.items:
        print("거부: --writer(otest*) --run-dir --items 필요")
        return 3
    if not ri.resolve_user_pid(args):
        args.user_pid = 0  # 사용자 Pd 가 실행 중이 아니다. 실입력을 안 보내므로 보호 대상이 없어 계속하되 결과 notes 에 남긴다
        print("사용자 Pd 미실행 — 실입력 없는 bg 측정이라 계속한다")
    final = args.phase == "final"
    build = ri.build_info(args.run_dir, "desktop_work", args.session, final=final)
    info = ri.script_info(os.path.join(HERE, mod.__name__ + ".py"))
    code = 0
    for name in [n.strip() for n in args.items.split(",") if n.strip()]:
        res = BgResult(name, mod.REQ[name], args, info, build)
        res.notes.append({"user_pd_running": bool(args.user_pid)})
        res.notes.append({"driver": os.path.basename(__file__), "driver_sha256": ri.sha256(os.path.abspath(__file__))})
        work = os.path.join(res.dir, "work", res.run_id)
        os.makedirs(work, exist_ok=True)
        before = ri.snapshot_user(args.user_pid)
        rig = BgRig(args, work)
        try:
            rig.start()
            res.notes.append({"test_window": {"pid": rig.pid, "rect": (rig.win(rig.main) or {}).get("rect")}})
            mod.ITEMS[name](rig, res)
        except ri.InputRefused as e:
            res.notes.append({"item_stopped_by_refusal": str(e)})
            print("항목 중단(실입력 필요):", e, flush=True)
            code = code or 1
        except SystemExit as e:
            res.notes.append({"launch_failed": str(e)})
            print("기동 실패:", e, flush=True)
            code = code or 2
        finally:
            rig.stop()
            ri.fill_safety(res, before, ri.snapshot_user(args.user_pid), rig)
            if not res.write():
                code = code or 1
    return code


if __name__ == "__main__":
    sys.exit(main())
