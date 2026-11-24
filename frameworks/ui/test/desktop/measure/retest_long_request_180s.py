#!/usr/bin/env python3
# 원격 제어 요청이 180초를 넘겨도 끊기지 않고 응답하는지 재는 전용 도구(perf_ui.py 의 curl timeout 60 한계 밖)
"""
무엇을 재는가 (계획서 conv_179419201221 7e '180초' / a9 §5 '원격 perf-ui 180초 끊김 최종 확인')
  원격 제어(api/cmd) 요청 하나를 180초 넘게(기본 190초) 붙들어 두어도 연결이 끊기지 않고 응답이 돌아오는지,
  그동안 같은 앱이 다른 요청(ping)에 계속 답하는지(UI 응답)를 잰다.
  요청 = `wait.for`(조건 대기, UI 스레드를 막지 않는 await) 에 절대 충족될 수 없는 조건(control=없는 id)과 timeout=<초>*1000 을 준 것.
  정상이면 앱이 timeout 후 'wait.for 시간 초과' 오류로 응답한다 — 그 응답이 연결이 살아 있었다는 증거다.
기존 한계: perf_ui.py·pdctl.curl 은 --max-time 60 이라 180초 요청 자체를 보낼 수 없다. 이 도구는 curl 을 직접 불러 --max-time 을 요청 시간+30초로 둔다.
판정 기준: 수치 기준 없음 — 계획서·plan 문서에 있는 조건은 '180초 넘는 요청 1회 이상 끊김 없이 응답'(a9 §5, 이전 값 128.6·167.6초 정상)뿐이다.
  PASS = 경과 > 180초 AND 응답이 예상한 timeout 오류(연결 유지 증거) AND 대기 중 ping 전부 응답. 하나라도 어긋나면 PASS 가 아니다(공허한 PASS 방지).
  ping 지연(ms)은 판정에 쓰지 않고 참고 기록으로만 남긴다.
안전:
  - 사용자 부재 가드(user_away_guard.check)를 시작 전과 ping 마다 부른다. 복귀 감지면 이 도구가 띄운 curl 만 종료하고 rc 5.
  - 시작 전 유휴 --min-idle 초(기본 300) 게이트. 못 채우면 측정하지 않고 PENDING(rc 6).
  - 앱을 기동·종료하지 않는다. 종료하는 프로세스는 자기가 Popen 으로 띄운 curl 뿐이다(pgrep -f·pkill 안 씀).
  - --info <remote-control.json> 필수(사용자 Pd 의 기본 연결 정보를 쓰지 않는다). 포트 5180·5220 이면 거부(rc 3).
사용:
  python3 -B retest_long_request_180s.py --info <data>/remote-control.json --out <결과.json> [--wait-s 190] [--min-idle 300]
  python3 -B retest_long_request_180s.py --dry-run --info <...>   (계획만 출력, 앱 접속·요청 없음)
  python3 -B retest_long_request_180s.py --self-test              (앱·입력 없음)
rc: 0 PASS / 1 FAIL / 2 UNDET(연결 정보 읽기 실패 등) / 3 거부(금지 포트) / 4 인자 오류 / 5 사용자 복귀 / 6 PENDING(유휴 미확보)
"""
import argparse
import json
import os
import subprocess
import sys
import threading
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(HERE, "..", "..", "common"))
import _paths  # noqa: E402

_paths.add()
import pdctl  # noqa: E402

FORBIDDEN_PORTS = (5180, 5220)
LIMIT_S = 180  # 기존 끊김 의심 한계 — 이보다 길어야 의미가 있다
NEVER_ID = "__pd_180s_never_exists__"  # wait.for control= 에 주는, 존재할 수 없는 컨트롤 id
PING_EVERY_S = 10
TIMEOUT_MARK = "wait.for 시간 초과"


def build_body(wait_s):
    return json.dumps({"cmd": "wait.for", "args": {"control": NEVER_ID, "timeout": int(wait_s * 1000)}}, ensure_ascii=False)


def curl_cmd(info, wait_s, path="api/cmd"):
    exe = pdctl.CURL_EXE if os.path.exists(pdctl.CURL_EXE) else "curl"
    return [exe, "-s", "--max-time", str(int(wait_s) + 30), "-X", "POST", "-H", "Content-Type: application/json",
            "-H", "X-Pd-Token: " + info["token"], "--data-binary", "@-", "http://localhost:%d/%s" % (info["port"], path)]


def judge(elapsed_s, wait_s, curl_rc, resp_text, pings):
    """순수 판정. (verdict, 사유 목록). PASS 는 모든 조건이 맞을 때만."""
    why = []
    if wait_s <= LIMIT_S:
        why.append("요청 시간 %ss 가 한계 %ss 이하라 의미 없음" % (wait_s, LIMIT_S))
    if curl_rc != 0:
        why.append("curl 실패 rc=%s — 연결이 끊김" % curl_rc)
    resp = None
    if curl_rc == 0:
        try:
            resp = json.loads(resp_text)
        except (ValueError, TypeError):
            why.append("응답이 JSON 이 아님: %r" % (resp_text or "")[:120])
    if resp is not None and not (resp.get("ok") is False and TIMEOUT_MARK in str(resp.get("error", ""))):
        why.append("예상한 timeout 오류 응답이 아님(명령 오류·조기 충족 가능): %s" % json.dumps(resp, ensure_ascii=False)[:160])
    if elapsed_s <= LIMIT_S:
        why.append("경과 %.1fs 가 %ss 이하 — 180초 초과 경로를 지나지 않음" % (elapsed_s, LIMIT_S))
    if not pings:
        why.append("대기 중 ping 이 한 번도 없음 — UI 응답 미측정")
    elif any(not p.get("ok") for p in pings):
        why.append("대기 중 ping 실패 %d/%d" % (sum(1 for p in pings if not p.get("ok")), len(pings)))
    return ("PASS" if not why else "FAIL"), why


def ping(info):
    t0 = time.time()
    try:
        proc = subprocess.run(curl_cmd(info, 10), input=json.dumps({"cmd": "ping", "args": {}}).encode("utf-8"), capture_output=True, timeout=45)
        ok = proc.returncode == 0 and json.loads(proc.stdout.decode("utf-8", "replace")).get("ok") is True
    except (subprocess.TimeoutExpired, ValueError, OSError):
        ok = False
    return {"t_s": None, "ok": ok, "ms": round((time.time() - t0) * 1000, 1)}


def idle_gate(guard, min_idle):
    """(통과 여부, 사유). 가드가 복귀를 알리면 예외를 그대로 올린다."""
    guard.check()
    idle = guard.read_probe()["idle_ms"] / 1000.0
    return idle >= min_idle, "유휴 %.0f초(기준 %d초)" % (idle, min_idle)


def measure(info, wait_s, guard):
    """요청을 보내고 대기 중 ping·가드 확인. (curl_rc, 응답 텍스트, 경과초, ping 목록, 복귀 여부)"""
    proc = subprocess.Popen(curl_cmd(info, wait_s), stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    box = {}

    def talk():
        out, _ = proc.communicate(build_body(wait_s).encode("utf-8"))
        box["out"] = out.decode("utf-8", "replace")

    t0 = time.time()
    th = threading.Thread(target=talk, daemon=True)
    th.start()
    pings, returned = [], None
    while th.is_alive():
        th.join(PING_EVERY_S)
        if not th.is_alive():
            break
        try:
            guard.check()
        except (guard.UserReturned, guard.GuardUnavailable) as e:
            returned = repr(e)
            proc.terminate()  # 자기가 띄운 curl 만 종료
            th.join(10)
            break
        p = ping(info)
        p["t_s"] = round(time.time() - t0, 1)
        pings.append(p)
        print("  t=%.0fs ping ok=%s %sms" % (p["t_s"], p["ok"], p["ms"]), flush=True)
    return proc.returncode, box.get("out", ""), time.time() - t0, pings, returned


def main(argv):
    ap = argparse.ArgumentParser(description="원격 제어 180초 초과 요청 끊김 확인 전용 도구")
    ap.add_argument("--info", help="remote-control.json 경로(필수, 격리 앱의 것)")
    ap.add_argument("--out", help="결과 JSON 경로")
    ap.add_argument("--wait-s", type=int, default=190, help="요청을 붙들어 둘 시간(초, 180 초과)")
    ap.add_argument("--min-idle", type=int, default=300, help="시작 전 유휴 최소(초)")
    ap.add_argument("--dry-run", action="store_true", help="계획만 출력")
    ap.add_argument("--self-test", action="store_true")
    a = ap.parse_args(argv)
    if a.self_test:
        return self_test()
    if not a.info or not os.path.exists(a.info):
        print("--info <remote-control.json> 필요(사용자 Pd 기본 정보를 쓰지 않는다): %r" % a.info)
        return 4
    if a.wait_s <= LIMIT_S:
        print("--wait-s 는 %d 초 초과여야 한다(공허한 측정 방지): %d" % (LIMIT_S, a.wait_s))
        return 4
    try:
        with open(a.info, encoding="utf-8") as f:
            info = json.load(f)
        port = int(info["port"])
        info["token"]
    except (OSError, ValueError, KeyError) as e:
        print("연결 정보 읽기 실패:", repr(e))
        return 2
    if port in FORBIDDEN_PORTS:
        print("거부: 포트 %d 는 사용자 인스턴스 포트(5180·5220) — 접촉하지 않는다" % port)
        return 3
    plan = {"port": port, "request": json.loads(build_body(a.wait_s)), "curl_max_time_s": a.wait_s + 30, "ping_every_s": PING_EVERY_S,
            "idle_gate_s": a.min_idle, "pass_rule": "경과>%ds AND timeout 오류 응답 AND ping 전부 응답(수치 기준 없음)" % LIMIT_S}
    print("계획:", json.dumps(plan, ensure_ascii=False))
    if a.dry_run:
        return 0
    import user_away_guard as guard
    try:
        ok, msg = idle_gate(guard, a.min_idle)
    except (guard.UserReturned, guard.GuardUnavailable) as e:
        print("가드: 복귀 또는 판단 불가 — 시작하지 않는다:", repr(e))
        return 5
    print(msg, flush=True)
    result = {"started": time.strftime("%Y-%m-%d %H:%M:%S"), "plan": plan, "idle": msg}
    if not ok:
        result.update(verdict="PENDING", why=["유휴 미확보: " + msg])
        save(a.out, result)
        print("PENDING —", msg)
        return 6
    rc, text, elapsed, pings, returned = measure(info, a.wait_s, guard)
    if returned:
        result.update(verdict="USER_RETURNED", why=[returned], elapsed_s=round(elapsed, 1), pings=pings)
        save(a.out, result)
        print("사용자 복귀로 중단:", returned)
        return 5
    verdict, why = judge(elapsed, a.wait_s, rc, text, pings)
    result.update(verdict=verdict, why=why, elapsed_s=round(elapsed, 1), curl_rc=rc, response=text[:300], pings=pings,
                  ping_ms_ref=[p["ms"] for p in pings])
    save(a.out, result)
    print(verdict, "경과 %.1fs" % elapsed, why)
    return 0 if verdict == "PASS" else 1


def save(path, obj):
    if path:
        os.makedirs(os.path.dirname(os.path.abspath(path)), exist_ok=True)
        with open(path, "w", encoding="utf-8") as f:
            json.dump(obj, f, ensure_ascii=False, indent=1)


def self_test():
    good = json.dumps({"ok": False, "error": "wait.for 시간 초과 190000ms — 마지막 상태: x"}, ensure_ascii=False)
    pg = [{"ok": True, "ms": 5.0, "t_s": 10}]
    v = lambda *x: judge(*x)[0]  # noqa: E731
    info = {"token": "t", "port": 5185}
    cmd = curl_cmd(info, 190)

    class G:
        class UserReturned(BaseException):
            pass

        class GuardUnavailable(BaseException):
            pass

        @staticmethod
        def check():
            return {}

        @staticmethod
        def read_probe():
            return {"idle_ms": 400000}

    class G2(G):
        @staticmethod
        def read_probe():
            return {"idle_ms": 100000}

    class G3(G):
        @staticmethod
        def check():
            raise G.UserReturned("복귀")
    checks = {
        "정상(190초·timeout 오류·ping 전부 응답) → PASS": v(191.0, 190, 0, good, pg) == "PASS",
        "공허: 5초 만에 응답(조기 충족·오류) → PASS 아님": v(5.0, 190, 0, good, pg) == "FAIL",
        "공허: 예상 아닌 응답(명령 오류) → PASS 아님": v(191.0, 190, 0, json.dumps({"ok": False, "error": "명령 없음"}), pg) == "FAIL",
        "공허: ok=true 응답(조건이 충족된 것) → PASS 아님": v(191.0, 190, 0, json.dumps({"ok": True, "result": {}}), pg) == "FAIL",
        "끊김(curl rc 28) → FAIL": v(100.0, 190, 28, "", pg) == "FAIL",
        "ping 실패 섞임 → FAIL": v(191.0, 190, 0, good, pg + [{"ok": False, "ms": 9000, "t_s": 20}]) == "FAIL",
        "ping 없음(UI 응답 미측정) → FAIL": v(191.0, 190, 0, good, []) == "FAIL",
        "요청 시간 180 이하 → FAIL": v(181.0, 180, 0, good, pg) == "FAIL",
        "JSON 아님 → FAIL": v(191.0, 190, 0, "<html>", pg) == "FAIL",
        "curl 명령: 요청+30초 max-time(60 한계 밖)": cmd[cmd.index("--max-time") + 1] == "220" and "localhost:5185/api/cmd" in cmd[-1],
        "요청 본문: wait.for 불가능 조건 + timeout ms": json.loads(build_body(190)) == {"cmd": "wait.for", "args": {"control": NEVER_ID, "timeout": 190000}},
        "유휴 300초 이상이면 통과": idle_gate(G, 300)[0] is True,
        "유휴 부족이면 미통과(PENDING 사유)": idle_gate(G2, 300)[0] is False,
    }
    try:
        idle_gate(G3, 300)
        checks["복귀 감지는 예외로 전파"] = False
    except G.UserReturned:
        checks["복귀 감지는 예외로 전파"] = True
    checks.update({
        "--info 없으면 rc 4": main([]) == 4,
        "--wait-s 180 이하 rc 4": main(["--info", __file__, "--wait-s", "180"]) == 4,
    })
    import tempfile
    for port, want in ((5180, 3), (5220, 3), (5185, 0)):
        with tempfile.NamedTemporaryFile("w", suffix=".json", delete=False) as f:
            json.dump({"port": port, "token": "t"}, f)
        try:
            checks["포트 %d dry-run rc %d" % (port, want)] = main(["--info", f.name, "--dry-run"]) == want
        finally:
            os.unlink(f.name)
    for k, ok in checks.items():
        print("%-4s %s" % ("PASS" if ok else "FAIL", k))
    print("self-test:", "OK" if all(checks.values()) else "FAIL")
    return 0 if all(checks.values()) else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
