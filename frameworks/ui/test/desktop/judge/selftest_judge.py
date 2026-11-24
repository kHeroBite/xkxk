# AC-007·008 판정기 — 진단 빌드의 window.load·busy·background 자기시험 응답을 기계 판정한다
"""
사용
  selftest_judge.py --info <격리>/remote-control.json --away-state <pc_away.json> --approved '<누가·언제>' --out <결과.json>
  selftest_judge.py --bundle <wwwroot> --port <포트> --out <결과.json>      (웹 결과)
  selftest_judge.py --self-test

help 로 진단 빌드(자기시험 명령 3개)를 먼저 확인하고, 없으면 DEFERRED 다.
AC-007 = 두 자기시험의 모든 사례 pass=true·pendingAfter=0 이고 필수 사례가 빠지지 않음.
AC-008 = 거부 4종(크기·형식·매직 바이트·가로세로)·이름 규칙·잘못된 이름 거부가 pass 이고, 받은 이름이 경로 구분자 없는 해시 이름.
"""
import re
import sys

import judge_session as session

COMMANDS = ("window.load.selftest", "busy.selftest", "background.selftest")
LOAD = ("request_bind_complete", "request_fail_no_doc", "complete_fail_cancel", "double_call", "close_release", "slow_flag",
        "reporter_claims_open", "activation_origin_no_misattribution", "run_async", "other_thread", "release_scope_hooks")
BUSY = ("short_task_not_shown", "ref_count", "clear_visible_hides", "clear_armed_disarms", "clear_pending_no_show")
REJECT = ("reject_too_large", "reject_format", "reject_magic", "reject_dimensions")
ACCEPT = ("accept_png", "accept_jpg", "accept_jpeg", "accept_bmp")
BACKGROUND = REJECT + ACCEPT + ("invalid_names_refused", "same_image_same_name")
NAME = re.compile(r"^[0-9a-f]{16}\.(png|jpg|jpeg|bmp)$")


def missing_commands(help_result):
    text = str(help_result)
    return [c for c in COMMANDS if c not in text]


def judge_cases(result, required, pending):
    """pending=True 면 pendingAfter 가 0 인 것까지 요구한다(값이 없으면 0 으로 세지 않는다)."""
    cases = (result or {}).get("cases") or []
    if not cases:
        return {"verdict": "INVALID", "why": "사례 0건"}
    missing = [n for n in required if n not in {c.get("name") for c in cases}]
    bad = [c for c in cases if c.get("pass") is not True or (pending and c.get("pendingAfter") != 0)]
    return {"verdict": "FAIL" if missing or bad else "PASS", "why": "%d건, 실패 %d, 누락 %d" % (len(cases), len(bad), len(missing)),
            "missing": missing, "failed": bad, "cases": cases}


def judge_background(result):
    out = judge_cases(result, BACKGROUND, False)
    if out["verdict"] == "INVALID":
        return out
    names = []
    for c in out["cases"]:
        if c.get("name") in ACCEPT:
            part = (c.get("detail") or "").split()
            names.append(part[1] if len(part) == 3 else "")
    out["stored_names"] = names
    bad = [n for n in names if not NAME.match(n)]
    if bad or result.get("pass") is not True:
        out.update(verdict="FAIL", why=out["why"] + ", 이름 규칙 위반 %d, 전체 pass=%s" % (len(bad), result.get("pass")))
    return out


def judge(s):
    missing = missing_commands(s.call("help"))
    if missing:
        raise session.Abort("DEFERRED", "진단 빌드가 아니거나 옛 판이다(명령 없음: %s)" % ", ".join(missing))
    return {"AC-007.window.load": judge_cases(s.call(COMMANDS[0]), LOAD, True),
            "AC-007.busy": judge_cases(s.call(COMMANDS[1]), BUSY, True),
            "AC-008.background": judge_background(s.call(COMMANDS[2]))}


def self_test():
    def load(**change):
        cases = [{"name": n, "pass": True, "pendingAfter": 0} for n in LOAD]
        cases[0].update(change)
        return {"cases": cases}

    def background(name="0123456789abcdef.png", ok=True):
        cases = [{"name": n, "pass": True, "detail": "None %s 32x32" % name} for n in BACKGROUND]
        cases[0]["pass"] = ok
        return {"cases": cases, "pass": ok}

    no_pending = load()
    del no_pending["cases"][0]["pendingAfter"]
    return session.run_cases([
        ("전부 통과 PASS", judge_cases(load(), LOAD, True)["verdict"] == "PASS"),
        ("pass=false FAIL", judge_cases(load(**{"pass": False}), LOAD, True)["verdict"] == "FAIL"),
        ("남은 예약 1 FAIL", judge_cases(load(pendingAfter=1), LOAD, True)["verdict"] == "FAIL"),
        ("pendingAfter 없음 FAIL", judge_cases(no_pending, LOAD, True)["verdict"] == "FAIL"),
        ("필수 사례 누락 FAIL", judge_cases({"cases": load()["cases"][1:]}, LOAD, True)["verdict"] == "FAIL"),
        ("사례 0 INVALID", judge_cases({}, LOAD, True)["verdict"] == "INVALID"),
        ("배경 통과 PASS", judge_background(background())["verdict"] == "PASS"),
        ("배경 거부 실패 FAIL", judge_background(background(ok=False))["verdict"] == "FAIL"),
        ("경로 구분자 이름 FAIL", judge_background(background("a/0123456789abcdef.png"))["verdict"] == "FAIL"),
        ("배경 사례 0 INVALID", judge_background({})["verdict"] == "INVALID"),
        ("진단 명령 없음 탐지", missing_commands([{"cmd": "status"}]) == list(COMMANDS)),
    ])


def main():
    args = session.parser(__doc__).parse_args()
    return self_test() if args.self_test else session.run(args, "selftest_judge", judge)


if __name__ == "__main__":
    sys.exit(main())
