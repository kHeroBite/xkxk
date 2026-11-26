# 성능 기준선 교대 측정 — PC 비움일 때만 기준판(git archive) 임시 트리를 만들어 빌드하고 대상판과 번갈아 기동 시간을 잰 뒤 반드시 트리를 지운다
"""
사용
  python3 -B perf_baseline.py --target-run <bin/run/…/net11.0> --runs 5 --out logs/b05_perf.json --away-state logs/pcaway/pc_away.json
                              [--base-commit 25c31b6] [--repo /mnt/c/DATA/Project/xkxk] [--pct 5] [--abs PERF-01=2000] [--carryover UTODO-0029]
  python3 -B perf_baseline.py --self-test

순서(계획서 §4.5 v2 — git worktree 를 쓰지 않는다)
  0. PC 비움 확인(pc_away_monitor.py check). 비움이 아니면 트리를 만들지 않고 status WAIT + evidence + carryover 로 끝난다(rc=3).
  1. Windows %TEMP%(cmd.exe → wslpath) 아래 xkxk_b05_base_<uuid 8자> 를 만들고, 만든 직후 --out 에 status RUNNING·base_tree_wsl·
     base_tree_removed:false 를 먼저 쓴다(강제 종료돼 finally 가 돌지 않아도 남은 폴더 위치가 남는다).
  2. try: git -C <repo> archive <기준 커밋> frameworks/ui | tar -x → PD_OUT_NAME=Work build_desktop.sh Work(rc·오류 0개 확인)
     → 두 판 교대(B,T,B,T…) N회. 매 회차 직전 PC 비움을 다시 확인하고, 사용자가 돌아오면 측정을 멈추고 WAIT 로 끝난다.
     기동 인자: --fresh --bg-pos auto(화면 밖) --multi-instance on --data-dir·--profile-dir(트리 안 회차별 임시) --perf-out --exit-after.
     제한 시간을 넘기면 명령줄에 회차 고유 data-dir 이 든 자기 프로세스만 taskkill.exe /PID 로 끝낸다(/IM 금지).
  3. finally: 트리를 지우고 부재를 확인한 뒤 base_tree_removed 를 기록한다(예외·중단 때도 실행).
  4. perf_regress_judge.py(PERF-01=startup_ms, 중앙값 +pct%, 절대 기준) → status PASS|FAIL|INVALID, judge_rc.
  부하(load1)는 회차마다 참고로만 남기고 시행을 무효 처리하지 않는다.
종료 코드: 0=PASS 1=FAIL 2=INVALID·오류 3=WAIT
"""
import argparse
import datetime
import json
import os
import shutil
import statistics  # noqa: F401  (판정은 perf_regress_judge 가 한다)
import subprocess
import sys
import tempfile
import time
import uuid

HERE = os.path.dirname(os.path.abspath(__file__))
UI = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
JUDGE = os.path.join(HERE, "..", "judge", "perf_regress_judge.py")
AWAY = os.path.join(UI, "test", "desktop", "measure", "pc_away_monitor.py")
EXE = "xkxk.frameworks.ui.desktop.exe"
METRIC_MAP = {"PERF-01": "startup_ms"}


def now():
    return datetime.datetime.now().astimezone().isoformat(timespec="seconds")


def kill_cmd(pid):
    # 자기 PID 한 개만 끝내는 명령. 이름(/IM) 일괄 종료는 만들지 않는다
    if not isinstance(pid, int) or isinstance(pid, bool) or pid <= 0:
        raise ValueError("PID 아님: %r" % (pid,))
    return ["taskkill.exe", "/PID", str(pid), "/F"]


def plan_order(runs):
    # 두 판을 정확히 번갈아(B,T,B,T…) — seq 는 1부터
    return [("base" if i % 2 == 0 else "target", i + 1) for i in range(runs * 2)]


class Runner:
    """외부 동작(비움 확인·임시 폴더·archive·빌드·기동)을 메서드로 분리해 자체 시험에서 바꿔 끼운다."""

    def __init__(self, a):
        self.a = a

    def away(self):
        p = subprocess.run(["python3", "-B", AWAY, "check", "--state", self.a.away_state], capture_output=True, text=True, timeout=30)
        return p.returncode, (p.stdout + p.stderr).strip()[-300:]

    def win_temp(self):
        w = subprocess.run(["cmd.exe", "/c", "echo %TEMP%"], capture_output=True, text=True, timeout=30, cwd="/mnt/c").stdout.strip()
        return subprocess.run(["wslpath", w], capture_output=True, text=True, timeout=30).stdout.strip()

    def archive(self, tree):
        g = subprocess.Popen(["git", "-C", self.a.repo, "archive", self.a.base_commit, "frameworks/ui"], stdout=subprocess.PIPE)
        t = subprocess.run(["tar", "-x", "-C", tree], stdin=g.stdout, timeout=600)
        g.stdout.close()
        if g.wait(timeout=60) != 0 or t.returncode != 0:
            raise RuntimeError("git archive|tar 실패 git=%s tar=%s" % (g.returncode, t.returncode))

    def build(self, tree):
        sh = os.path.join(tree, "frameworks", "ui", "tools", "build_desktop.sh")
        log = os.path.join(tree, "build_base.log")
        # 빌드 서버·노드 재사용을 끈다 — 상주 MSBuild·VBCSCompiler 가 임시 트리 폴더를 잡고 있으면 finally 삭제가 늦어진다
        env = dict(os.environ, PD_OUT_NAME="Work", BUILD_LOG=log, PD_EXTRA_PROPS="-p:UseSharedCompilation=false -nodeReuse:false")
        rc = subprocess.run(["bash", sh, "Work"], env=env, stdin=subprocess.DEVNULL, capture_output=True, timeout=1800).returncode
        txt = open(log, encoding="utf-8", errors="replace").read() if os.path.exists(log) else ""
        ok = rc == 0 and ("오류 0개" in txt or "0 Error(s)" in txt)
        return {"rc": rc, "ok": ok, "log_tail": txt[-600:]}

    def own_pids(self, marker):
        ps = "Get-CimInstance Win32_Process -Filter \"Name='%s'\" | ForEach-Object { \"$($_.ProcessId)`t$($_.CommandLine)\" }" % EXE
        out = subprocess.run(["powershell.exe", "-NoProfile", "-Command", ps], capture_output=True, text=True, timeout=60).stdout
        return pids_with_marker(out, marker)

    def launch(self, exe, run_dir):
        os.makedirs(os.path.join(run_dir, "data"), exist_ok=True)
        os.makedirs(os.path.join(run_dir, "profile"), exist_ok=True)
        w = lambda p: subprocess.run(["wslpath", "-w", p], capture_output=True, text=True).stdout.strip()  # noqa: E731
        perf = os.path.join(run_dir, "perf.json")
        args = [exe, "--fresh", "--bg-pos", "auto", "--multi-instance", "on", "--data-dir", w(os.path.join(run_dir, "data")),
                "--profile-dir", w(os.path.join(run_dir, "profile")), "--perf-out", w(run_dir) + "\\perf.json",
                "--exit-after", str(self.a.exit_after)]
        t0 = time.time()
        pr = subprocess.Popen(args, cwd=os.path.dirname(exe), stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL,
                              stderr=subprocess.DEVNULL, start_new_session=True)
        killed = []
        try:
            pr.wait(timeout=self.a.exit_after + 90)
        except subprocess.TimeoutExpired:
            for pid in self.own_pids(w(os.path.join(run_dir, "data"))):
                subprocess.run(kill_cmd(pid), capture_output=True, timeout=30)
                killed.append(pid)
            pr.wait(timeout=30)
        d = json.load(open(perf, encoding="utf-8-sig")) if os.path.exists(perf) else {}
        return {"metrics": {k: d.get(v) for k, v in METRIC_MAP.items()}, "wall_s": round(time.time() - t0, 1), "killed": killed}


def pids_with_marker(ps_out, marker):
    # 명령줄에 회차 고유 marker(data-dir)가 든 프로세스만 자기 것으로 본다
    res = []
    for line in ps_out.splitlines():
        pid, _, cmd = line.partition("\t")
        if pid.strip().isdigit() and marker and marker.lower() in cmd.lower():
            res.append(int(pid.strip()))
    return res


def write(path, d):
    tmp = path + ".tmp"
    with open(tmp, "w", encoding="utf-8") as f:
        json.dump(d, f, ensure_ascii=False, indent=2)
        f.write("\n")
    os.replace(tmp, path)


def load1():
    try:
        return float(open("/proc/loadavg").read().split()[0])
    except (OSError, ValueError):
        return None


def run(a, rn):
    res = {"status": None, "started": now(), "base_commit": a.base_commit, "target_run": a.target_run, "runs": a.runs,
           "pc_away_checked": False, "base_tree_wsl": None, "base_tree_removed": None, "evidence": [], "samples": []}
    rc, msg = rn.away()
    res["pc_away_checked"] = True
    res["evidence"].append({"when": now(), "pc_away_rc": rc, "detail": msg})
    if rc != 0:
        res.update(status="WAIT", carryover=a.carryover, finished=now(), reason="PC 비움 아님(pc_away rc=%d) — 기준판 트리를 만들지 않음" % rc)
        write(a.out, res)
        return 3
    tree = os.path.join(rn.win_temp(), "xkxk_b05_base_" + uuid.uuid4().hex[:8])
    os.makedirs(tree)
    res.update(status="RUNNING", base_tree_wsl=tree, base_tree_removed=False)
    write(a.out, res)  # 트리 생성 직후 위치를 먼저 남긴다
    base, target = {"version": "base", "commit": a.base_commit, "samples": []}, {"version": "target", "commit": a.target_run, "samples": []}
    try:
        rn.archive(tree)
        res["build"] = rn.build(tree)
        if not res["build"]["ok"]:
            raise RuntimeError("기준판 빌드 실패 rc=%s" % res["build"]["rc"])
        exes = {"base": os.path.join(tree, "frameworks", "ui", "src", "xkxk.frameworks.ui.desktop", "bin", "Work", "net11.0", EXE),
                "target": os.path.join(a.target_run, EXE)}
        for ver, seq in plan_order(a.runs):
            rc, msg = rn.away()
            if rc != 0:
                res["evidence"].append({"when": now(), "pc_away_rc": rc, "detail": msg, "seq": seq})
                res.update(status="WAIT", carryover=a.carryover, reason="측정 중 사용자 복귀(seq=%d) — 측정 중단" % seq)
                break
            r = rn.launch(exes[ver], os.path.join(tree, "runs", "%02d_%s" % (seq, ver)))
            s = {"seq": seq, "metrics": r["metrics"], "load1_ref": load1(), "wall_s": r.get("wall_s"), "killed": r.get("killed", [])}
            (base if ver == "base" else target)["samples"].append(s)
            res["samples"].append(dict(s, version=ver))
    except Exception as e:  # noqa: BLE001 — 어떤 예외든 finally 로 트리를 지우고 기록한다
        res.update(status="ERROR", reason="%s: %s" % (type(e).__name__, e))
    finally:
        for _ in range(20):  # Windows 쪽 핸들이 늦게 풀려 빈 폴더가 잠시 남는 경우가 있다(2026-11-26 실측) — 최대 약 60초 재시도
            shutil.rmtree(tree, ignore_errors=True)
            if not os.path.exists(tree):
                break
            time.sleep(3)
        res["base_tree_removed"] = not os.path.exists(tree)
        write(a.out, res)
    if res["status"] == "RUNNING":
        with tempfile.TemporaryDirectory() as td:
            bp, tp = os.path.join(td, "base.json"), os.path.join(td, "target.json")
            write(bp, base)
            write(tp, target)
            cmd = ["python3", "-B", JUDGE, bp, tp, "--pct", str(a.pct), "--out", os.path.join(td, "j.json")] + sum([["--abs", x] for x in a.abs], [])
            jr = subprocess.run(cmd, capture_output=True, text=True, timeout=60).returncode
            j = json.load(open(os.path.join(td, "j.json"), encoding="utf-8")) if os.path.exists(os.path.join(td, "j.json")) else {}
        res.update(judge_rc=jr, n_base=j.get("n_base"), n_target=j.get("n_target"), alternated=j.get("alternated"),
                   metrics=j.get("metrics", {}), judge_reasons=j.get("reasons"), status={0: "PASS", 1: "FAIL"}.get(jr, "INVALID"))
    res["finished"] = now()
    write(a.out, res)
    return {"PASS": 0, "FAIL": 1, "WAIT": 3}.get(res["status"], 2)


def self_test():
    ok_all = []

    def case(name, cond):
        print("SELFTEST %s %s" % ("PASS" if cond else "FAIL", name))
        ok_all.append(bool(cond))

    class A:
        target_run, runs, base_commit, repo, pct, abs, carryover, exit_after = "/x", 3, "25c31b6", "/r", 5, ["PERF-01=2000"], "UTODO-0029", 3

    class Fake(Runner):
        def __init__(self, a, root, away_rc=0, fail_at=None):
            super().__init__(a)
            self.root, self.away_rc, self.fail_at, self.seen_running = root, away_rc, fail_at, None

        def away(self):
            return self.away_rc, "fake"

        def win_temp(self):
            return self.root

        def archive(self, tree):
            self.seen_running = json.load(open(self.a.out))  # archive 시점에 RUNNING 이 먼저 기록돼 있어야 한다
            if self.fail_at == "archive":
                raise RuntimeError("의도한 실패")

        def build(self, tree):
            return {"rc": 0, "ok": True, "log_tail": ""}

        def launch(self, exe, run_dir):
            os.makedirs(run_dir, exist_ok=True)
            return {"metrics": {"PERF-01": 1000.0}, "wall_s": 0, "killed": []}

    with tempfile.TemporaryDirectory() as td:
        a = A()
        a.out = os.path.join(td, "perf.json")
        rc = run(a, Fake(a, td, away_rc=1))
        d = json.load(open(a.out))
        case("wait_path_no_tree", rc == 3 and d["status"] == "WAIT" and d["base_tree_wsl"] is None and d["carryover"] and d["evidence"]
             and not [n for n in os.listdir(td) if n.startswith("xkxk_b05_base_")])
        case("alternate_order", [v for v, _ in plan_order(3)] == ["base", "target"] * 3 and [s for _, s in plan_order(3)] == list(range(1, 7)))
        fk = Fake(a, td, fail_at="archive")
        rc = run(a, fk)
        d = json.load(open(a.out))
        case("finally_removes_on_exception", rc == 2 and d["status"] == "ERROR" and d["base_tree_removed"] is True
             and not os.path.exists(d["base_tree_wsl"]))
        case("running_written_first", fk.seen_running and fk.seen_running["status"] == "RUNNING"
             and fk.seen_running["base_tree_removed"] is False and fk.seen_running["base_tree_wsl"])
        rc = run(a, Fake(a, td))
        d = json.load(open(a.out))
        case("full_pass_path", rc == 0 and d["status"] == "PASS" and d["alternated"] is True and d["n_base"] == 3
             and d["base_tree_removed"] is True and d["pc_away_checked"] is True)
    try:
        kill_cmd("1234 /IM x")
        bad = False
    except ValueError:
        bad = True
    case("own_pid_kill_cmd", kill_cmd(4321) == ["taskkill.exe", "/PID", "4321", "/F"] and bad
         and pids_with_marker("11\tC:\\a.exe --data-dir C:\\T\\run1\\data\n4700\tC:\\a.exe\n", "C:\\T\\run1\\data") == [11])
    return 0 if all(ok_all) else 1


def main():
    ap = argparse.ArgumentParser(description="기준판·대상판 교대 성능 측정(PC 비움 전용)")
    ap.add_argument("--target-run")
    ap.add_argument("--runs", type=int, default=5)
    ap.add_argument("--out")
    ap.add_argument("--away-state")
    ap.add_argument("--base-commit", default="25c31b6")
    ap.add_argument("--repo", default="/mnt/c/DATA/Project/xkxk")
    ap.add_argument("--pct", type=float, default=5.0)
    ap.add_argument("--abs", action="append", default=None)
    ap.add_argument("--carryover", default="UTODO-0029")
    ap.add_argument("--exit-after", type=int, default=3)
    ap.add_argument("--self-test", action="store_true")
    a = ap.parse_args()
    if a.self_test:
        return self_test()
    a.abs = a.abs or ["PERF-01=2000"]
    a.target_run = os.path.abspath(a.target_run) if a.target_run else a.target_run  # 기동 cwd 가 실행 폴더라 상대경로면 대상판을 못 찾는다
    if not (a.target_run and a.out and a.away_state) or a.runs < 3:
        ap.print_usage()
        print("--target-run·--out·--away-state 필수, --runs ≥ 3")
        return 2
    if not os.path.exists(os.path.join(a.target_run, EXE)):
        print("대상판 실행 파일 없음: %s" % a.target_run)
        return 2
    return run(a, Runner(a))


if __name__ == "__main__":
    sys.exit(main())
