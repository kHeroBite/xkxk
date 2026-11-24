# 자동 저장 flush 시험 — 자동 저장 ON 에서 화면을 바꾸고 debounce 전(1초 안)에 앱을 내리면(Android 홈→강제 종료·데스크톱 메인 창 닫기) 바꾼 화면이 재기동 뒤 복원되는가(lead 01:4x)
"""
사용: python3 test/desktop/measure/retest_autosave_flush.py --target desktop --conf <판>
      PD_REMOTE_INFO=<forward 포트로 고친 연결 정보> python3 test/desktop/measure/retest_autosave_flush.py --target android [--serial S]
판정: 재기동 뒤 자동 복원된 문서 제목 = 바꾼 화면(FL-A·FL-B). FL-B 가 없으면 flush 미저장(debounce 저장만 됨) → AutoSaver(odev-2) 결함 후보.
사용자 파일: 시작 전 백업·끝나면(실패해도) 복원(try/finally), 시작 전·종료 후 md5 를 결과에 기록(lead 01:4x 재발 방지).
기동(desktop): run_desktop_bg.sh --kill 의 종료 대상은 그 스크립트가 기록한 자기 PID 뿐이다. 다른 Pd 가 떠 있으면 rc=11 로 바로 실패한다.
"""
import argparse
import datetime
import glob
import hashlib
import json
import os
import subprocess
import sys
import time

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "common"))  # D-TESTDIR: test/common/_paths.py
import _paths  # noqa: E402
_paths.add()
TOOLS = _paths.TOOLS  # 옛 위치(tools) — run_desktop_bg.sh·pdctl.py·logs 경로 기준 그대로
ROOT = os.path.dirname(TOOLS)
sys.path.insert(0, TOOLS)
import pdctl  # noqa: E402
from retest_remote import Remote  # noqa: E402

ADB = "/mnt/c/Program Files (x86)/Android/android-sdk/platform-tools/adb.exe"
PKG = "com.pd.poc"
USER_FILES = ("layout.json", "settings.json")


class Host:
    """플랫폼별: 사용자 파일 읽기·쓰기, 기동, '1초 안에 내리기', 강제 종료."""

    def __init__(self, a):
        self.a = a
        self.adb = [ADB] + (["-s", a.serial] if a.serial else [])

    # ── 사용자 파일 ──
    def read(self, name):
        if self.a.target == "desktop":
            p = os.path.join(user_dir(), name)
            return open(p, "rb").read() if os.path.exists(p) else None
        r = subprocess.run(self.adb + ["exec-out", "run-as", PKG, "cat", "files/PdPoc/" + name], capture_output=True, timeout=20)
        return r.stdout if r.returncode == 0 and r.stdout else None

    def write(self, name, data):
        if self.a.target == "desktop":
            p = os.path.join(user_dir(), name)
            if data is None:
                if os.path.exists(p):
                    os.remove(p)
                return
            with open(p, "wb") as f:
                f.write(data)
            return
        if data is None:
            subprocess.run(self.adb + ["shell", "run-as", PKG, "rm", "-f", "files/PdPoc/" + name], capture_output=True, timeout=20)
            return
        subprocess.run(self.adb + ["shell", "run-as %s sh -c 'cat > files/PdPoc/%s'" % (PKG, name)], input=data, capture_output=True, timeout=20)

    # ── 기동·종료 ──
    def launch(self, old_pid=None):
        if self.a.target == "desktop":
            run = subprocess.run(["bash", os.path.join(TOOLS, "run_desktop_bg.sh"), "--conf", self.a.conf, "--kill", "--", "--remote-control"],
                                 capture_output=True, timeout=80)
            if run.returncode == 11:
                raise RuntimeError("기동 거부(rc=11): 다른 Pd 가 떠 있다. --kill 은 기록된 자기 PID 만 끄므로 그 Pd 는 그대로 둔다")
            for _ in range(40):
                try:
                    info = pdctl.load_info()
                    if info.get("pid") != old_pid:
                        return remote_with(info)
                except SystemExit:
                    pass
                time.sleep(1)
            raise RuntimeError("데스크톱 기동 실패")
        subprocess.run(self.adb + ["shell", "monkey", "-p", PKG, "-c", "android.intent.category.LAUNCHER", "1"], capture_output=True, timeout=20)
        time.sleep(6)
        raw = subprocess.run(self.adb + ["exec-out", "run-as", PKG, "cat", "files/PdPoc/remote-control.json"], capture_output=True, timeout=20).stdout
        info = json.loads(raw.decode("utf-8"))
        info["port"] = pdctl.load_info()["port"]  # PD_REMOTE_INFO 의 forward 포트 유지
        return remote_with(info)

    def leave_fast(self, r):
        """화면을 바꾼 직후(debounce 전) 사용자 방식으로 내린다: 데스크톱 = 메인 창 닫기, Android = 홈 키(OnPause·OnStop)."""
        if self.a.target == "desktop":
            r.call("app.closeMain")
        else:
            subprocess.run(self.adb + ["shell", "input", "keyevent", "KEYCODE_HOME"], capture_output=True, timeout=10)

    def kill(self, r):
        if self.a.target == "desktop":
            for _ in range(15):  # 메인 창 닫기 = 정상 종료 — 끝날 때까지 기다린다(강제 종료는 하지 않는다: 종료 저장 경로를 보는 시험)
                if subprocess.run(["tasklist.exe", "/FI", "IMAGENAME eq Pd.Poc.Desktop.exe", "/NH"], capture_output=True).stdout.count(b"Pd.Poc.Desktop") == 0:
                    return
                time.sleep(1)
            return
        time.sleep(1)
        subprocess.run(self.adb + ["shell", "am", "force-stop", PKG], capture_output=True, timeout=20)


def user_dir():
    found = sorted(glob.glob("/mnt/c/Users/*/AppData/Local/PdPoc"), key=os.path.getmtime)
    if not found:
        raise RuntimeError("사용자 설정 폴더(PdPoc) 없음")
    return found[-1]


def remote_with(info):
    r = Remote.__new__(Remote)
    r.info, r.calls, r.checks = info, [], []
    for _ in range(30):
        if r.call("status") is not None:
            return r
        time.sleep(1)
    raise RuntimeError("원격 연결 실패")


def md5(data):
    return hashlib.md5(data).hexdigest()[:8] if data is not None else None


def titles(r):
    return sorted(d.get("title") for d in r.call("docs.list") or [])


def main(argv):
    p = argparse.ArgumentParser(description="자동 저장 flush 시험")
    p.add_argument("--target", choices=["desktop", "android"], required=True)
    p.add_argument("--conf", default="Release")
    p.add_argument("--serial", default=os.environ.get("PD_ADB_SERIAL"))
    p.add_argument("--out", default=None)
    a = p.parse_args(argv)
    out = a.out or os.path.join(ROOT, "logs", "odev6r", "autosave_flush_%s_%s.json" % (a.target, datetime.datetime.now().strftime("%H%M%S")))
    h = Host(a)
    backup = {n: h.read(n) for n in USER_FILES}
    rep = {"target": a.target, "md5_before": {n: md5(v) for n, v in backup.items()}}
    try:
        r = h.launch(None) if a.target == "desktop" else remote_with(pdctl.load_info())
        auto0 = r.call("window.autosave.get")
        r.call("window.autosave.set", on=True)
        r.call("docs.close", all=True)
        r.call("docs.open", kind="grid", title="FL-A", allowDuplicate=True)
        time.sleep(3)  # 기준 화면은 debounce 저장까지 끝나게
        r.call("docs.open", kind="grid", title="FL-B", allowDuplicate=True)  # 바꾼 화면 — 이것은 flush 로만 저장될 수 있다
        t0 = time.time()
        h.leave_fast(r)
        rep["leave_after_ms"] = round((time.time() - t0) * 1000)  # 변경 → 내리기 요청까지(1초 안이어야 debounce 전)
        h.kill(r)
        r2 = h.launch(r.info.get("pid"))
        r2.call("wait.for", _timeout=20, docCount=2, timeout=10000)
        rep["restored_titles"] = titles(r2)
        rep["pass"] = rep["restored_titles"] == ["FL-A", "FL-B"]
        rep["diagnosis"] = ("flush 저장됨" if rep["pass"] else
                            "FL-B 없음 — 종료 직전 flush 미저장(debounce 저장만) → AutoSaver(odev-2) 결함 후보" if rep["restored_titles"] == ["FL-A"] else
                            "기준 화면도 복원 안 됨 — 자동 복원·저장 경로 확인 필요")
        r2.call("window.autosave.set", on=bool(auto0) if auto0 is not None else True)
        if a.target == "desktop":
            r2.call("app.shutdown")
            time.sleep(3)
    except Exception as e:  # 실패해도 복원은 한다
        rep["error"] = repr(e)
    finally:
        if a.target == "android":  # 데스크톱은 위에서 app.shutdown(자동 저장 없음)으로 내렸다
            subprocess.run(h.adb + ["shell", "am", "force-stop", PKG], capture_output=True, timeout=20)
        else:
            from retest_user_files import wait_app_exit
            wait_app_exit(30)  # 프로세스 0개 확인 뒤 복원(종료 중 재기록이 복원본을 덮은 02:15 사고)
        for n, v in backup.items():
            h.write(n, v)
        rep["md5_after"] = {n: md5(h.read(n)) for n in USER_FILES}
        rep["restored_ok"] = rep["md5_after"] == rep["md5_before"]
    with open(out, "w", encoding="utf-8") as f:
        json.dump(rep, f, ensure_ascii=False, indent=2)
    print(json.dumps(rep, ensure_ascii=False, indent=1))
    print("→", out)
    return 0 if rep.get("pass") and rep.get("restored_ok") else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
