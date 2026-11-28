# Fold5 실기기에서 Android 앱의 원격 제어로 성능 조작(테마·글자 크기·폰트·레이아웃·SDI)을 12회씩 재는 스크립트
"""
사용:
  python3 test/mobile/measure/retest_android.py [--apk <APK 경로>] [--serial R3CW70202FP] [--runs 12] [--out 결과.json]
순서: adb install → monkey 기동 → adb forward tcp:15960 tcp:5960 → run-as 로 remote-control.json 읽기
      → 포트를 15960 으로 고쳐 임시 파일에 저장 → PD_REMOTE_INFO 설정 → 측정 → finally 에서 원복.
원복: forward 해제·앱 제거·임시 토큰 파일 삭제·stay_on_while_plugged_in 원값. 화면 크기·밝기·회전은 건드리지 않는다.
종료 코드: 0=측정 완료, 1=설치·연결 실패, 2=측정 중 오류.
"""
import argparse
import datetime
import json
import os
import subprocess
import sys
import tempfile
import time

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "common"))  # D-TESTDIR: test/common/_paths.py
import _paths  # noqa: E402
_paths.add()

ADB = os.environ.get("ADB", "/mnt/c/Program Files (x86)/Android/android-sdk/platform-tools/adb.exe")  # SDK 폴더가 없어진 머신은 ADB 환경변수로 지정한다
PKG = _paths.get("android.package")
HOST_PORT = 15960
DEFAULT_APK = os.path.normpath(os.path.join(_paths.bin_dir("android", "Debug", "net10.0-android"), _paths.get("android.apk_name")))
DEFAULT_OUT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "logs/otest_m1/android_perf.json"))


def adb(serial, *args, timeout=120, binary=False):
    """Windows adb.exe 호출. 반환은 (rc, stdout)."""
    p = subprocess.run([ADB, "-s", serial] + list(args), capture_output=True, timeout=timeout)
    out = p.stdout if binary else p.stdout.decode("utf-8", "replace").replace("\r", "")
    return p.returncode, out


def win_path(p):
    return subprocess.run(["wslpath", "-w", p], capture_output=True, text=True).stdout.strip()


def prepare(serial, apk, state):
    """설치→기동→forward→토큰 파일. 반환은 임시 연결 정보 경로(실패 시 None)."""
    rc, out = adb(serial, "shell", "settings", "get", "global", "stay_on_while_plugged_in")
    state["stay_on"] = out.strip() or "0"
    adb(serial, "shell", "settings", "put", "global", "stay_on_while_plugged_in", "3")
    adb(serial, "shell", "input", "keyevent", "224")
    rc, out = adb(serial, "install", "-r", win_path(apk), timeout=300)
    state["installed"] = True
    print("install rc=%d %s" % (rc, out.strip().splitlines()[-1] if out.strip() else ""))
    if rc != 0 or "Success" not in out:
        return None
    adb(serial, "shell", "am", "force-stop", PKG)
    adb(serial, "logcat", "-c")
    adb(serial, "shell", "monkey", "-p", PKG, "-c", "android.intent.category.LAUNCHER", "1")
    rc, out = adb(serial, "forward", "tcp:%d" % HOST_PORT, "tcp:5960")
    state["forwarded"] = True
    print("forward rc=%d" % rc)
    info = None
    for _ in range(40):  # 앱 기동과 원격 제어 파일 생성 대기(최대 약 80초)
        rc, out = adb(serial, "exec-out", "run-as", PKG, "cat", "files/" + "/".join(_paths.get("data.segments")) + "/remote-control.json")
        if rc == 0 and out.strip().startswith("{"):
            info = json.loads(out)
            break
        time.sleep(2)
    if not info:
        return None
    info["port"] = HOST_PORT
    if "url" in info:
        info["url"] = "http://localhost:%d/" % HOST_PORT
    fd, path = tempfile.mkstemp(prefix="pd_android_", suffix=".json")
    with os.fdopen(fd, "w", encoding="utf-8") as f:
        json.dump(info, f)
    state["info_path"] = path
    os.environ["PD_REMOTE_INFO"] = path
    return path


def timed(r, cmd, **args):
    """명령 1회 호출 후 응답의 ms·elapsed_ms·ui_frame_ms 를 모은다."""
    t = time.time()
    res = r.call(cmd, _timeout=120, **args)
    c = r.calls[-1]
    return {"cmd": cmd, "args": args, "ok": c["ok"], "ms": c["ms"], "elapsed_ms": c["elapsed_ms"],
            "ui_frame_ms": c["ui_frame_ms"], "wall_ms": round((time.time() - t) * 1000), "error": c["error"]}, res


def summarize(rows):
    vals = sorted(x["elapsed_ms"] for x in rows if x["ok"] and x["elapsed_ms"] is not None)
    if not vals:
        return {"n": 0}
    pick = lambda q: vals[min(len(vals) - 1, int(q * len(vals)))]
    return {"n": len(vals), "min": vals[0], "p50": pick(0.5), "p95": pick(0.95), "max": vals[-1]}


def measure(r, runs):
    results = {}
    rec = lambda name, row: results.setdefault(name, []).append(row)
    r.call("docs.close", all=True)
    docs = [(r.call("docs.open", kind=k) or [{}])[0].get("id") for k in ("grid", "gantt", "form")]
    doc = docs[0]
    fonts = r.call("settings.fonts") or ["Noto Sans KR", "monospace"]  # Android 글꼴 목록에 없는 이름(Noto Mono)을 시험이 쓰지 않도록 기본값을 목록에 있는 monospace 로 둔다
    orig = r.call("settings.get") or {}
    r.call("perf.reset")
    for i in range(runs):
        rec("theme.next", timed(r, "theme.next")[0])
        rec("input.ctrlWheel", timed(r, "input.ctrlWheel", delta=1 if i % 2 == 0 else -1, doc=doc)[0])
        rec("ui.font.family", timed(r, "settings.set", fontName=fonts[i % len(fonts)])[0])
        r.call("layout.save")
        rec("layout.load", timed(r, "layout.load")[0])
        rec("docs.sdi", timed(r, "docs.sdi", doc=doc, on=(i % 2 == 0))[0])
        print("  반복 %d/%d 완료" % (i + 1, runs))
    ops = r.call("perf.ops")
    perf = r.call("perf.get")
    # 원값 복원(테마·글자 크기·폰트)
    if orig.get("themeId"):
        r.call("theme.select", id=orig["themeId"])
    r.call("settings.set", baseFontSize=orig.get("baseFontSize"), fontName=orig.get("fontName"))
    r.call("docs.sdi", doc=doc, on=False)
    return {"runs": results, "summary": {k: summarize(v) for k, v in results.items()},
            "perf_ops": ops, "perf_get": perf, "settings_before": orig}


def cleanup(serial, state):
    """finally 원복. 각 단계는 독립적으로 시도하고 실패를 출력만 한다."""
    steps = [
        ("forward 해제", lambda: state.get("forwarded") and adb(serial, "forward", "--remove", "tcp:%d" % HOST_PORT)),
        ("앱 종료·제거", lambda: state.get("installed") and (adb(serial, "shell", "am", "force-stop", PKG), os.environ.get("PD_KEEP_APP") != "1" and adb(serial, "uninstall", PKG))),  # PD_KEEP_APP=1 이면 앱을 지우지 않고 종료만 한다(사용자 앱 삭제 금지 제약)
        ("토큰 파일 삭제", lambda: state.get("info_path") and os.path.exists(state["info_path"]) and os.remove(state["info_path"])),
        ("stay_on 원복", lambda: "stay_on" in state and adb(serial, "shell", "settings", "put", "global", "stay_on_while_plugged_in", state["stay_on"])),
    ]
    for name, fn in steps:
        try:
            fn()
            print("원복: " + name)
        except Exception as e:  # noqa: BLE001 — 한 단계 실패가 나머지 원복을 막지 않게 한다
            print("원복 실패: %s %s" % (name, e))


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--apk", default=DEFAULT_APK)
    p.add_argument("--serial", default="R3CW70202FP")
    p.add_argument("--runs", type=int, default=12)
    p.add_argument("--out", default=DEFAULT_OUT)
    a = p.parse_args()
    state, code = {}, 0
    report = {"started": datetime.datetime.now().isoformat(timespec="seconds"), "serial": a.serial, "apk": a.apk,
              "apk_mtime": datetime.datetime.fromtimestamp(os.path.getmtime(a.apk)).isoformat(timespec="seconds")}
    try:
        if not prepare(a.serial, a.apk, state):
            print("설치·기동·원격 제어 연결 실패")
            report["error"] = "prepare 실패"
            code = 1
        else:
            from retest_remote import Remote  # PD_REMOTE_INFO 설정 뒤에 가져온다
            r = Remote()
            report.update(measure(r, a.runs))
            report["calls"] = r.calls
    except Exception as e:  # noqa: BLE001 — 측정 오류도 보고서에 남기고 원복은 finally 가 보장
        report["error"] = repr(e)
        code = 2
    finally:
        cleanup(a.serial, state)
        os.makedirs(os.path.dirname(a.out), exist_ok=True)
        with open(a.out, "w", encoding="utf-8") as f:
            json.dump(report, f, ensure_ascii=False, indent=1)
        print("결과: " + a.out)
    return code


if __name__ == "__main__":
    sys.exit(main())
