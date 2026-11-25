# 움직임 효과·배경·그림자 성능 도구(PERF-12~17)의 공용 부분 — 기동 전제 확인·표본 수집·커밋 프레임 집계·판정 값
"""
PERF-12~17 도구 5개가 import 한다(직접 실행은 --self-test 만).
앱을 띄우거나 끝내지 않는다. 격리 폴더(logs/proddata/<이름>)로 이미 뜬 진단판의 remote-control.json 만 받는다.
전제가 어긋나면 측정하지 않는다: 기동 보류 해제 근거(--approved) 없음, PC 비움(away) 아님, 사용자 포트(5180·5220), 격리 밖 연결 정보.
첫 조작 전에 기동을 확인한다: run_desktop_bg.sh 가 기록한 PID 이고, 인자에 `--multi-instance on` 과 절대경로 격리 `--data-dir` 가 있고,
앱 로그의 layout-file 이 그 격리 폴더 안이어야 한다. 하나라도 확인되지 않으면 조작 없이 멈춘다.

판정 값과 종료 코드
  PASS 0 · FAIL 1 · INVALID 2(절차 확인 실패 — 판정에 쓰지 않고 다시 잼) · DEFERRED 3(표본 부족·전제 미충족·실측 대기)
  RECORDED 4(판정선 없는 기록) · STOPPED 5(측정 중 사용자 복귀 — 무효 회차)
원격 docs.moveAll 로 잰 끌기 값은 '합성 이동'이라 PASS 로 내지 않는다(실제 끌기가 남았으므로 DEFERRED).
"""
import argparse
import glob
import json
import os
import re
import subprocess
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(HERE, "..", "..", "common"))
import _paths  # noqa: E402

_paths.add()
import b2_commit_frames as b2  # noqa: E402
import pc_away_monitor  # noqa: E402
from perf_repeat import Rig, build_sha, load1, percentile, summarize  # noqa: E402

PROD_ROOT = os.path.abspath(os.path.join(HERE, "..", "..", "..", "logs", "proddata"))
USER_PORTS = (5180, 5220)
MIN_TRIALS = 20  # 04/03 §6.4 PERF-12·15 표본 하한
RC = {"PASS": 0, "FAIL": 1, "INVALID": 2, "DEFERRED": 3, "RECORDED": 4, "STOPPED": 5}
SYNTHETIC = "합성 이동(docs.moveAll) — 실제 끌기 합격으로 쓰지 않는다"


class Abort(Exception):
    """측정을 멈춘다. verdict 는 RC 의 키."""

    def __init__(self, verdict, reason):
        super().__init__(reason)
        self.verdict = verdict


def parser(doc):
    ap = argparse.ArgumentParser(description=doc, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--self-test", action="store_true", help="앱 없이 판정 함수만 시험")
    ap.add_argument("--info", help="격리 폴더의 remote-control.json")
    ap.add_argument("--away-state", help="pc_away_monitor.py run 이 갱신하는 상태 파일")
    ap.add_argument("--approved", help="기동 보류 해제 근거(누가·언제)")
    ap.add_argument("--out", help="결과 JSON")
    ap.add_argument("--build-dir", help="실행 폴더(xkxk.frameworks.ui.views.dll sha256 기록)")
    return ap


def info_problem(path, info, root=PROD_ROOT):
    """연결 정보를 쓸 수 없는 이유. 쓸 수 있으면 None."""
    full = os.path.abspath(path)
    if not full.startswith(os.path.abspath(root) + os.sep):
        return "격리 폴더(logs/proddata/<이름>) 밖 연결 정보: %s" % full
    try:
        port = int(info["port"])
    except (KeyError, TypeError, ValueError):
        return "연결 정보에 port 가 없다"
    return "사용자 포트 %d" % port if port in USER_PORTS else None


def launch_problem(run_args, app_log, data_win):
    """기동이 규칙과 다른 이유. run_args = stdio 로그의 인자 줄, app_log = 앱 로그, data_win = 격리 폴더의 Windows 절대경로. 맞으면 None."""
    want = data_win.rstrip("\\").lower()
    args = run_args.lower() + " "
    if "--multi-instance on " not in args:
        return "--multi-instance on 이 없다"
    if not re.match(r"^[a-z]:\\", want) or "--data-dir %s " % want not in args:
        return "절대경로 격리 --data-dir %s 가 인자에 없다" % data_win
    found = re.findall(r"layout-file=(.*?) bg-pos=", app_log)
    if not found:
        return "앱 로그에 layout-file 이 없어 격리를 확인할 수 없다"
    if any(p.strip().lower() != want + "\\layout.json" for p in found):
        return "layout-file 이 격리 경로가 아니다"
    return None


def verify_launch(rig, info_path):
    """첫 조작 전 확인. 읽기(status·로그)만 하고, 확인하지 못하면 이유를 돌려준다."""
    status = rig.call("status") or {}
    pid = str(status.get("pid") or "")
    logs = os.path.dirname(PROD_ROOT)
    names = []
    for path in glob.glob(os.path.join(logs, "run_desktop_bg_pids", "*.txt")):
        with open(path, encoding="utf-8") as f:
            names += [p[1] for p in (line.split() for line in f) if len(p) == 2 and p[0] == pid]
    if not pid or not names:
        return "run_desktop_bg.sh 가 기록한 PID 가 아니다(%s)" % (pid or "pid 없음")
    app_log = os.path.join(logs, names[-1])
    try:
        with open(app_log, encoding="utf-8", errors="replace") as f:
            app_text = f.read()
        with open(app_log[:-len(".log")] + "_stdio.log", encoding="utf-8", errors="replace") as f:
            run_args = f.readline().strip()
        if not re.search(r"host start pid=%s\b" % pid, app_text):
            return "앱 로그의 pid 가 다르다"
        data_win = subprocess.check_output(["wslpath", "-w", os.path.dirname(os.path.abspath(info_path))], text=True).strip()
    except (OSError, subprocess.SubprocessError) as e:
        return "기동 기록을 읽지 못했다: %r" % e
    return launch_problem(run_args, app_text, data_win)


class Session:
    """측정 1회: 전제 확인, 명령 실행, 매 시행 비움 재확인."""

    def __init__(self, args):
        missing = [n for n in ("info", "away_state", "approved", "out") if not getattr(args, n)]
        if missing:
            raise Abort("DEFERRED", "필수 인자 없음: %s" % ", ".join("--" + m.replace("_", "-") for m in missing))
        self.args = args
        self.away()
        with open(args.info, encoding="utf-8") as f:
            problem = info_problem(args.info, json.load(f))
        if problem:
            raise Abort("DEFERRED", problem)
        os.environ["PD_REMOTE_INFO"] = os.path.abspath(args.info)  # pdctl 이 사용자 인스턴스로 넘어가지 않게 고정
        self.rig = Rig()  # 이미 뜬 인스턴스에 붙기만 한다(exe 를 띄우지 않음)
        problem = verify_launch(self.rig, args.info)
        if problem:
            raise Abort("DEFERRED", "기동 확인 실패: " + problem)
        self.state0 = self.call("style.state")
        self.meta = {"approved": args.approved, "started": time.strftime("%Y-%m-%dT%H:%M:%S%z"), "load1_before": load1(),
                     "build_sha256_Pd.Ui.Views": build_sha(args.build_dir), "style_state": self.state0}

    def away(self, started=False):
        if pc_away_monitor.check(argparse.Namespace(state=self.args.away_state, max_age=60)) != 0:
            raise Abort("STOPPED" if started else "DEFERRED", "PC 비움(away)이 아니다")

    def call(self, cmd, **kw):
        r = self.rig.raw(cmd, **kw)
        if not r.get("ok"):
            raise Abort("INVALID", "%s 실패: %s" % (cmd, r.get("error")))
        return r.get("result")

    def effect(self, key, value):
        r = self.call("style.effect.set", key=key, value=value) or {}
        if not r.get("applied"):
            raise Abort("INVALID", "%s=%s 가 적용되지 않았다: %s" % (key, value, r))

    def fresh_docs(self, kind, count):
        self.call("docs.close", all=True)
        time.sleep(0.5)
        self.call("docs.open", kind=kind, count=count, allowDuplicate=True)
        self.call("wait.for", docCount=count, timeout=30000)
        time.sleep(1.0)
        docs = self.call("docs.list")
        if not isinstance(docs, list) or len(docs) != count:
            raise Abort("INVALID", "문서 %d개를 열지 못했다" % count)
        return docs

    def open_trial(self, kind="grid"):
        """문서 1개 열기의 doc.open.<kind>(열기 명령 → 첫 프레임) 1건. 기록이 1건이 아니면 None."""
        self.away(True)
        self.call("docs.close", all=True)
        time.sleep(0.5)
        self.call("perf.reset")
        self.call("docs.open", kind=kind, count=1)
        self.call("wait.for", docCount=1, timeout=20000)
        time.sleep(0.8)
        return single(self.rig.ops("doc.open." + kind), "doc.open." + kind, "frame_p50_ms")

    def synthetic_drag(self, moves, dx=8):
        """열린 창 전부를 번갈아 옮기며 커밋 원자료를 받는다."""
        self.away(True)
        self.call("perf.reset")
        for i in range(moves):
            self.call("docs.moveAll", dx=dx if i % 2 == 0 else -dx, dy=0)
            time.sleep(0.03)
        return self.call("perf.get")


def single(ops, name, field):
    """perf.reset 뒤 1건만 기록됐을 때 그 값."""
    o = ops.get(name) or {}
    v = o.get(field)
    return float(v) if o.get("count") == 1 and isinstance(v, (int, float)) else None


def commit_verdict(perf):
    """b2 커밋 프레임 집계에 '입력 0 = 무효'와 '표본 부족 = 보류'(03 §6.3)를 더한 값."""
    r = b2.summarize(perf)
    if r["indeterminate"] or r.get("commit_source") != "after_commit":
        return "INVALID", r
    if not r["inputs"]:
        return "INVALID", dict(r, invalid_reason="입력 사건 0 — 누락 0 을 잴 수 없다")
    if r["commit_frame_samples"] < b2.MIN_SAMPLES:
        return "DEFERRED", dict(r, deferred_reason="커밋 표본 부족")
    return ("PASS" if r["pass"] else "FAIL"), r


def synthetic_verdict(perf):
    """합성 이동 값. 기준 안팎은 남기되 최종 값은 PASS·FAIL 로 내지 않는다."""
    v, r = commit_verdict(perf)
    out = dict(r, kind="synthetic_move", note=SYNTHETIC, within_limit={"PASS": True, "FAIL": False}.get(v))
    out.pop("pass", None)
    return ("DEFERRED" if v in ("PASS", "FAIL") else v), out


def finish(args, name, body):
    """measure 의 결과·중단을 파일과 종료 코드로 바꾼다. body 는 () → dict(verdict 포함)."""
    result = {"tool": name}
    try:
        result.update(body())
    except Abort as e:
        result.update(verdict=e.verdict, reason=str(e))
    except (OSError, ValueError, KeyError) as e:
        result.update(verdict="INVALID", reason=repr(e))
    result["load1_after"] = load1()
    if args.out:
        os.makedirs(os.path.dirname(os.path.abspath(args.out)), exist_ok=True)
        with open(args.out, "w", encoding="utf-8") as f:
            json.dump(result, f, ensure_ascii=False, indent=1)
    print(json.dumps({k: result.get(k) for k in ("tool", "verdict", "reason")}, ensure_ascii=False))
    return RC[result["verdict"]]


def fake_perf(dt=16.7, n=40, inputs=True):
    """자체시험용 커밋 원자료(간격 dt 로 n 번 커밋, 간격마다 커밋 0.5ms 전 입력)."""
    commits = [dt * i + 1 for i in range(n)]
    return {"commit_source": "after_commit", "commit_raw": {
        "dt_ms": [dt] * n, "committed": [1] * n, "commits_ms": commits,
        "inputs_ms": [c - 0.5 for c in commits] if inputs else [], "now_ms": commits[-1] if commits else 0}}


def run_cases(cases):
    bad = [name for name, ok in cases if not ok]
    for name, ok in cases:
        print(("OK  " if ok else "BAD ") + name)
    print("self-test %s %d/%d" % ("PASS" if not bad else "FAIL", len(cases) - len(bad), len(cases)))
    return 1 if bad else 0


def self_test():
    inside = os.path.join(PROD_ROOT, "x", "remote-control.json")
    data = "C:\\wt\\logs\\proddata\\x"
    good_args = "# run args: --multi-instance on --remote-control=5242 --data-dir %s --bg-pos auto" % data
    good_log = "layout-file=%s\\layout.json bg-pos=auto" % data
    return run_cases([
        ("기동 규칙 통과", launch_problem(good_args, good_log, data) is None),
        ("multi-instance 값 없음 거부", bool(launch_problem(good_args.replace("--multi-instance on", "--multi-instance"), good_log, data))),
        ("data-dir 없음 거부", bool(launch_problem("# run args: --multi-instance on", good_log, data))),
        ("다른 data-dir 거부", bool(launch_problem(good_args.replace(data, data + "2"), good_log, data))),
        ("상대경로 data-dir 거부", bool(launch_problem(good_args.replace(data, "logs\\x"), good_log, "logs\\x"))),
        ("사용자 경로 layout-file 거부", bool(launch_problem(good_args, "layout-file=C:\\Users\\u\\xkxk\\frameworks\\ui\\layout.json bg-pos=auto", data))),
        ("layout-file 없음 거부", bool(launch_problem(good_args, "", data))),
        ("격리 안 연결 정보 허용", info_problem(inside, {"port": 5242}) is None),
        ("사용자 포트 거부", info_problem(inside, {"port": 5180}) and info_problem(inside, {"port": 5220})),
        ("격리 밖 거부", bool(info_problem("/tmp/remote-control.json", {"port": 5242}))),
        ("이름만 비슷한 폴더 거부", bool(info_problem(PROD_ROOT + "2/r.json", {"port": 5242}))),
        ("port 없음 거부", bool(info_problem(inside, {}))),
        ("1건만 표본", single({"a": {"count": 1, "v": 3}}, "a", "v") == 3.0),
        ("2건·없음은 표본 아님", single({"a": {"count": 2, "v": 3}}, "a", "v") is None and single({}, "a", "v") is None),
        ("커밋 25ms 안 PASS", commit_verdict(fake_perf())[0] == "PASS"),
        ("커밋 40ms FAIL", commit_verdict(fake_perf(40.0))[0] == "FAIL"),
        ("입력 0 은 INVALID", commit_verdict(fake_perf(inputs=False))[0] == "INVALID"),
        ("원자료 없음 INVALID", commit_verdict({})[0] == "INVALID"),
        ("표본 29 는 DEFERRED", commit_verdict(fake_perf(n=29))[0] == "DEFERRED"),
        ("합성 이동은 PASS 로 안 냄", synthetic_verdict(fake_perf())[0] == "DEFERRED" and synthetic_verdict(fake_perf())[1]["within_limit"] is True),
        ("합성 이동 초과 기록", synthetic_verdict(fake_perf(40.0))[1]["within_limit"] is False),
    ])


if __name__ == "__main__":
    sys.exit(self_test() if "--self-test" in sys.argv else (print(__doc__) or 3))
