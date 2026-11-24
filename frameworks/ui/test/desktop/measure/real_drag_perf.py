#!/usr/bin/env python3
# PERF-14·16·02 실끌기 성능 측정 — 자기 격리 인스턴스의 MDI 창 제목줄을 safe_click 실입력으로 왕복해 끌며 perf.get 커밋 프레임을 b2 기준으로 판정한다
"""
사용
  real_drag_perf.py --info <격리 data-dir/remote-control.json> --owner <PD_SLOT_OWNER> --away-state <pc_away.json> \\
      --out <결과.json> --monitor <DISPLAYn> --onscreen-approved '<누가·언제>' --mode busy|shadow|level \\
      [--user-pid N] [--docs 8] [--drags 8] [--rounds 1] [--level medium] [--hold-ms 180000]
  real_drag_perf.py --self-test      # 앱·입력 없이 판정·집계 함수만 시험

앱을 띄우거나 끝내지 않는다. 앱은 tools/run_desktop_bg.sh 로 `--multi-instance on --data-dir <격리>`·화면 안(승인)에 먼저 띄운다.
안전 전제는 drag_shadow_option_input.OwnRig 를 그대로 쓴다: 자기 기동 기록·격리 폴더·다른 Pd 0개(--user-pid 1개만 예외)·pc_away away·
user_away_guard.check·입력 직전 전경 게이트·safe_click ExpectedProcessId=자기 PID. 끝나면 전경·커서를 되돌린다(복원 이동은 주입 기록에 남는다).
모드
  busy   그리드 --docs 개를 계단식으로 열고 맨 위 표에 sample.load+grid.search 로 불러오는 중(스피너)을 걸어 끈다(PERF-14).
         끌기 앞뒤 isLoading 이 모두 true 일 때만 판정하고 아니면 INVALID. sample.load 서버 상한이 60000ms 라 실사용은 --drags 4 --hold-ms 60000.
  shadow style.moveShadow.set hide/keep 을 라운드마다 번갈아 재고 RECORDED(hide_p95·keep_p95)를 낸다(PERF-16).
  level  style.effect.set effectLevel=--level 뒤 라운드마다 잰다(PERF-02 등, 효과 단계별).
판정: perf_effect_common.commit_verdict(b2_commit_frames — 커밋 프레임 p95 ≤ 25ms·누락 0·표본 ≥ 30, 입력 사건 0 = INVALID). 기준 수치는 바꾸지 않는다.
rc 0=PASS·RECORDED 1=FAIL 2=오류 3=전제 거부 4=INVALID 등 5=사용자 복귀·가드 불가(중단)
출처: 2026-11-24 otest-1 이 세션 증거 폴더에서 쓴 실측 스크립트(K10·K13·PERF-02 근거)를 승격. 저장소 perf_* 도구는 합성 이동만 재서 DEFERRED 만 낸다.
"""
import argparse
import json
import os
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "..", "common"))
import _paths  # noqa: E402
_paths.add()
sys.path.insert(0, HERE)

import drag_shadow_option_input as dso  # noqa: E402  OwnRig·Precondition·restore_desktop·fg_tool
import perf_effect_common as pec  # noqa: E402  commit_verdict
import retest_real_input as real  # noqa: E402  SAFE_CLICK·InputRefused
import user_away_guard as away  # noqa: E402

DRAG_DIP = 180
REQUIRED = ("info", "owner", "away_state", "out", "monitor", "onscreen_approved")


def drags(rig, doc, n):
    """doc 창 제목줄을 n 번 왕복 끈다(한 번 약 1초, 25 이동)."""
    pts = []
    for i in range(n):
        title = rig.need("ui.find", id="PART_Grid", doc=doc)
        x, y = rig.point(rig.main, title["bounds"], 0.4, 0.5)
        s = rig.win(rig.main)["dpi"] / 96.0
        sign = 1 if i % 2 == 0 else -1
        to = (int(x + sign * DRAG_DIP * s), y)
        rig.click(x, y, hwnd=rig.main, drag=to)
        pts.append({"from": (x, y), "to": to})
    return pts


def measured(rig, doc, n):
    rig.need("docs.activate", doc=doc)
    time.sleep(0.8)
    rig.need("perf.reset")
    pts = drags(rig, doc, n)
    perf = rig.need("perf.get")
    v, r = pec.commit_verdict(perf)
    return {"verdict": v, "commit": r, "drags": pts, "raw_keys": sorted(perf.keys()) if isinstance(perf, dict) else None, "raw": perf}


def fresh(rig, count):
    rig.need("docs.close", all=True)
    time.sleep(0.6)
    rig.need("docs.open", kind="grid", count=count, allowDuplicate=True)
    rig.r.call("wait.for", docCount=count, timeout=30000)
    time.sleep(1.2)
    rig.need("docs.arrange", mode="cascade")
    time.sleep(0.8)
    return rig.need("docs.list")


def busy_verdict(measure_verdict, loading_before, loading_after):
    """스피너가 끌기 앞뒤 모두 떠 있을 때만 측정 판정을 쓴다."""
    return measure_verdict if (loading_before and loading_after) else "INVALID"


def shadow_verdict(verdicts):
    return "INVALID" if "INVALID" in verdicts else "RECORDED"


def level_verdict(verdicts):
    return "PASS" if verdicts and all(v == "PASS" for v in verdicts) else ("FAIL" if "FAIL" in verdicts else "INVALID")


def exit_code(verdict):
    return {"PASS": 0, "FAIL": 1, "RECORDED": 0}.get(verdict, 4)


def mode_busy(rig, a, res):
    docs = fresh(rig, a.docs)
    target = docs[-1]["id"]
    rig.need("docs.activate", doc=target)
    time.sleep(0.5)
    try:
        rig.need("sample.load", delayMs=a.hold_ms, blockMs=0)
        rig.need("grid.search", doc=target, text="")
        time.sleep(1.0)
        st0 = rig.need("window.load.state")
        res["loading_before"] = any(d.get("id") == target and d.get("isLoading") for d in st0.get("docs") or [])
        m = measured(rig, target, a.drags)
        st1 = rig.need("window.load.state")
        res["loading_after"] = any(d.get("id") == target and d.get("isLoading") for d in st1.get("docs") or [])
    finally:
        rig.r.call("sample.load", delayMs=0, blockMs=0)
    res["PERF-14"] = m
    res["verdict"] = busy_verdict(m["verdict"], res["loading_before"], res["loading_after"])


def mode_shadow(rig, a, res):
    docs = fresh(rig, a.docs)
    target = docs[-1]["id"]
    rows = {"hide": [], "keep": []}
    try:
        for i in range(a.rounds):
            for mode in (("hide", "keep") if i % 2 == 0 else ("keep", "hide")):
                r = rig.need("style.moveShadow.set", value=mode)
                if r.get("effective") != mode:
                    raise RuntimeError("그림자 %s 실효값 아님: %s" % (mode, r))
                time.sleep(0.8)
                m = measured(rig, target, a.drags)
                m.pop("raw", None)
                rows[mode].append(m)
    finally:
        rig.r.call("style.moveShadow.set")
    res["rows"] = rows
    res["hide_p95"] = [x["commit"].get("commit_frame_p95_ms") for x in rows["hide"]]
    res["keep_p95"] = [x["commit"].get("commit_frame_p95_ms") for x in rows["keep"]]
    res["verdict"] = shadow_verdict([x["verdict"] for x in rows["hide"] + rows["keep"]])


def mode_level(rig, a, res):
    docs = fresh(rig, a.docs)
    target = docs[-1]["id"]
    rows = []
    res["level_set"] = rig.need("style.effect.set", key="effectLevel", value=a.level)
    time.sleep(0.8)
    for _ in range(a.rounds):
        m = measured(rig, target, a.drags)
        m.pop("raw", None)
        rows.append(m)
    res["rows"] = rows
    res["verdict"] = level_verdict([x["verdict"] for x in rows])


def self_test():
    """앱·입력 없이 판정(commit_verdict)과 모드별 집계·종료 코드만 잰다."""
    good, slow, no_input = pec.fake_perf(), pec.fake_perf(dt=40), pec.fake_perf(inputs=False)
    cases = [
        ("commit_verdict 정상 PASS", pec.commit_verdict(good)[0] == "PASS"),
        ("commit_verdict 느린 프레임 FAIL", pec.commit_verdict(slow)[0] == "FAIL"),
        ("commit_verdict 입력 0 INVALID", pec.commit_verdict(no_input)[0] == "INVALID"),
        ("busy 스피너 앞뒤 있음", busy_verdict("PASS", True, True) == "PASS"),
        ("busy 스피너 뒤 없음 INVALID", busy_verdict("PASS", True, False) == "INVALID"),
        ("shadow 기록", shadow_verdict(["PASS", "FAIL"]) == "RECORDED"),
        ("shadow INVALID 포함", shadow_verdict(["PASS", "INVALID"]) == "INVALID"),
        ("level 전부 PASS", level_verdict(["PASS", "PASS", "PASS"]) == "PASS"),
        ("level FAIL 포함", level_verdict(["PASS", "FAIL"]) == "FAIL"),
        ("level 빈 목록 INVALID", level_verdict([]) == "INVALID"),
        ("rc 매핑", [exit_code(v) for v in ("PASS", "RECORDED", "FAIL", "INVALID")] == [0, 0, 1, 4]),
        ("safe_click 사본 존재", os.path.exists(real.SAFE_CLICK)),
    ]
    for name, ok in cases:
        print(("PASS " if ok else "FAIL ") + name)
    passed = sum(ok for _, ok in cases)
    print("self-test %s %d/%d" % ("PASS" if passed == len(cases) else "FAIL", passed, len(cases)))
    return 0 if passed == len(cases) else 1


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    for n in ("--info", "--owner", "--away-state", "--out", "--monitor", "--onscreen-approved"):
        p.add_argument(n)
    p.add_argument("--user-pid", type=int, help="떠 있어도 되는 사용자 앱 PID 1개(입력 대상 아님)")
    p.add_argument("--mode", choices=("busy", "shadow", "level"))
    p.add_argument("--docs", type=int, default=8)
    p.add_argument("--drags", type=int, default=8)
    p.add_argument("--rounds", type=int, default=1)
    p.add_argument("--level", default="medium")
    p.add_argument("--hold-ms", type=int, default=180000, help="busy 모드 조회 대기(스피너 유지 시간) — 끌기 전 구간보다 길어야 한다(서버 상한 60000)")
    p.add_argument("--self-test", action="store_true")
    a = p.parse_args()
    if a.self_test:
        return self_test()
    missing = [n for n in REQUIRED if not getattr(a, n)] + ([] if a.mode else ["mode"])
    if missing:
        print("거부: 필요한 인자 없음 — %s" % ", ".join("--" + n.replace("_", "-") for n in missing))
        return 3
    res, code = {"mode": a.mode, "approved": a.onscreen_approved, "user_pid": a.user_pid, "safe_click": real.SAFE_CLICK, "started": time.strftime("%Y-%m-%dT%H:%M:%S")}, 2
    rig = dso.OwnRig(a, os.path.dirname(os.path.abspath(a.out)))
    saved = None
    try:
        res["window_rect"] = rig.attach(a.info)
        res["pid"] = rig.pid
        away.check()
        saved = dso.fg_tool.run_ps(["-Action", "read"])
        {"busy": mode_busy, "shadow": mode_shadow, "level": mode_level}[a.mode](rig, a, res)
        code = exit_code(res.get("verdict"))
        res["foreground_leaks"] = len(rig.leaks)
    except (away.UserReturned, away.GuardUnavailable) as e:
        res["stopped"], code = repr(e), 5
    except (dso.Precondition, real.InputRefused) as e:
        res["refused"], code = str(e), 3
    except (OSError, ValueError, KeyError, RuntimeError, SystemExit) as e:
        res["error"], code = repr(e), 2
    finally:
        if saved:
            res["restored"] = dso.restore_desktop(saved)  # 커서 복원 주입 기록은 fg_tool.run_ps 가 남긴다(D-9)
    res["rc"], res["ended"] = code, time.strftime("%Y-%m-%dT%H:%M:%S")
    os.makedirs(os.path.dirname(os.path.abspath(a.out)), exist_ok=True)
    with open(a.out, "w", encoding="utf-8") as f:
        json.dump(res, f, ensure_ascii=False, indent=1)
    print(json.dumps({"rc": code, "verdict": res.get("verdict"), "stop": res.get("stopped") or res.get("refused") or res.get("error")}, ensure_ascii=False))
    return code


if __name__ == "__main__":
    sys.exit(main())
