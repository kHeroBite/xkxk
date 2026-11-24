# Fold5 대시보드 벤치(B4 5절·B3 6절) — 라이브러리 1개를 새로 기동해 로딩 콜드1+웜3·relayout(회전)·라이트/다크 rest 캡처를 재는 스크립트 (otest-m-6)
"""
사용: python3 -E -u test/mobile/measure/dash_android.py --apk <i2400to2 APK> --expect-md5 d333bdc6... --tag i2400to2 --lib baseline|scottplot|livecharts|theone [--anim 0|1] [--parts load,relayout,capture]
저장: logs/odevgh/dash/ (기존 파일은 덮어쓰지 않고 _dupN 을 붙임). 응답 JSON 원문 그대로 저장. 실행 기록은 runs_android.txt 에 date 실측으로 append.
원복: user_rotation·accelerometer_rotation 원값, wm size 안 건드림, 앱 제거·forward 해제·stay_on 원값(ra.cleanup).
"""
import argparse
import hashlib
import json
import os
import sys
import threading
import time

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "common"))  # D-TESTDIR: test/common/_paths.py
import _paths  # noqa: E402
_paths.add()
import retest_android as ra  # noqa: E402

ROOT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
OUT = os.path.join(ROOT, "logs", "odevgh", "dash")


def now():
    return time.strftime("%Y-%m-%d %H:%M:%S KST")


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--apk", required=True)
    p.add_argument("--expect-md5", required=True)
    p.add_argument("--tag", required=True)
    p.add_argument("--lib", required=True)
    p.add_argument("--anim", type=int, default=0)
    p.add_argument("--style", default="default")
    p.add_argument("--n", type=int, default=12)
    p.add_argument("--parts", default="load,relayout,capture")
    p.add_argument("--serial", default="R3CW70202FP")
    a = p.parse_args()
    sr = a.serial
    parts = set(a.parts.split(","))
    md5 = hashlib.md5(open(a.apk, "rb").read()).hexdigest()
    if not md5.startswith(a.expect_md5):
        print("MD5_MISMATCH", md5)
        sys.exit(4)
    os.makedirs(OUT, exist_ok=True)
    runs = os.path.join(OUT, "runs_android.txt")
    state, code, files, rot_orig = {}, 0, [], {}

    def log(msg):
        with open(runs, "a", encoding="utf-8") as f:
            f.write("%s [otest-m-6 %s lib=%s anim=%d] %s\n" % (now(), a.tag, a.lib, a.anim, msg))
        print(msg, flush=True)

    def unique(name):
        path = os.path.join(OUT, name)
        k = 1
        base, ext = os.path.splitext(path)
        while os.path.exists(path):
            path = "%s_dup%d%s" % (base, k, ext)
            k += 1
        return path

    def save_json(name, obj):
        path = unique(name)
        json.dump(obj, open(path, "w", encoding="utf-8"), ensure_ascii=False, indent=1)
        files.append(os.path.basename(path))
        return path

    def shot(name):
        rc, data = ra.adb(sr, "exec-out", "screencap", "-p", binary=True)
        k = data.find(b"\x89PNG") if rc == 0 else -1
        if k < 0:
            return None
        path = unique(name)
        open(path, "wb").write(data[k:])
        files.append(os.path.basename(path))
        return os.path.basename(path)

    def gs(ns, key):
        return ra.adb(sr, "shell", "settings", "get", ns, key)[1].strip()

    try:
        env_dev = {"battery_temp_x10": ra.adb(sr, "shell", "dumpsys battery")[1].split("temperature:")[1].split()[0] if "temperature:" in ra.adb(sr, "shell", "dumpsys battery")[1] else None,
                   "min_refresh_rate": gs("system", "min_refresh_rate"), "peak_refresh_rate": gs("system", "peak_refresh_rate"),
                   "accelerometer_rotation": gs("system", "accelerometer_rotation"), "user_rotation": gs("system", "user_rotation")}
        rot_orig = {"accelerometer_rotation": env_dev["accelerometer_rotation"], "user_rotation": env_dev["user_rotation"]}
        log("시작 apk_md5=%s 기기=%s" % (md5, json.dumps(env_dev, ensure_ascii=False)))
        ra.adb(sr, "shell", "wm", "size", "reset")
        if not ra.prepare(sr, a.apk, state):
            raise RuntimeError("앱 연결 실패")
        from retest_remote import Remote
        time.sleep(4)
        r = Remote()

        def R(cmd, **k):
            try:
                return r.call(cmd, _timeout=180, **k)
            except SystemExit:
                return {"_timeout": True}

        def err():
            return (r.calls[-1] if r.calls else {}).get("error")

        env = R("bench.env")
        envp = os.path.join(OUT, "dash_android_%s_env.json" % a.tag)
        if not os.path.exists(envp):
            json.dump(env, open(envp, "w", encoding="utf-8"), ensure_ascii=False, indent=1)
            files.append(os.path.basename(envp))
        log("bench.env 저장: %s err=%s" % (json.dumps(env, ensure_ascii=False)[:300], err()))
        op = R("bench.open", target="dashboard")
        time.sleep(3)
        log("bench.open dashboard → %s err=%s docs=%s" % (json.dumps(op, ensure_ascii=False)[:200], err(), json.dumps(R("docs.list"), ensure_ascii=False)[:300]))
        fa = "a%d" % a.anim
        stem = "dash_android_%s_%s_%%s_n%d_%s_%s_r%%d.json" % (a.tag, a.lib, a.n, a.style, fa)

        def dash(sc, **k):
            t = time.time()
            res = R("bench.dashboard", scenario=sc, lib=a.lib, n=a.n, points=500, style=a.style, anim=("true" if a.anim else "false"), **k)
            return res, round((time.time() - t) * 1000), err()

        if "load" in parts:
            for rep in range(1, 5):
                res, wall, e = dash("load")
                path = save_json(stem % ("load", rep), res)
                ld = (res or {}).get("load") if isinstance(res, dict) else None
                log("load r%d (콜드=r1) wall=%dms err=%s load=%s → %s" % (rep, wall, e, json.dumps(ld, ensure_ascii=False)[:300], os.path.basename(path)))
                if isinstance(ld, dict) and (ld.get("failed") or e):
                    log("실패/오류 감지 — 이어서 진행하지 않음(원문은 저장됨)")
                    code = 3
                    break
                time.sleep(1.5)

        if "relayout" in parts and code == 0:
            log("relayout 시작: 20초 안에 회전 2회 이상(user_rotation 1→0→1), 원복 예정 %s" % json.dumps(rot_orig))

            def rotate():
                ra.adb(sr, "shell", "settings", "put", "system", "accelerometer_rotation", "0")
                for rv, wait in (("1", 4), ("0", 5), ("1", 5), ("0", 3)):
                    ra.adb(sr, "shell", "settings", "put", "system", "user_rotation", rv)
                    time.sleep(wait)
            th = threading.Thread(target=rotate)
            th.start()
            res, wall, e = dash("relayout", seconds=20)
            th.join()
            path = save_json(stem % ("relayout", 1), res)
            log("relayout wall=%dms err=%s resp=%s → %s" % (wall, e, json.dumps(res, ensure_ascii=False)[:400], os.path.basename(path)))
            ra.adb(sr, "shell", "settings", "put", "system", "user_rotation", rot_orig["user_rotation"])
            ra.adb(sr, "shell", "settings", "put", "system", "accelerometer_rotation", rot_orig["accelerometer_rotation"])
            time.sleep(2)

        if "capture" in parts and code == 0:
            themes = {"light": "pd-light", "dark": "pd-dark"}
            s0 = R("settings.get") or {}
            for th_name, tid in themes.items():
                R("theme.select", id=tid)
                time.sleep(1.5)
                dash("clear")
                res, wall, e = dash("load")
                time.sleep(2.5)
                host = R("ui.find", id="bench.dashboard.host")
                png = shot("dash_android_%s_%s_%s_%s_rest_%s.png" % (a.tag, a.lib, a.style, fa, th_name))
                save_json("dash_android_%s_%s_%s_%s_rest_%s_host.json" % (a.tag, a.lib, a.style, fa, th_name), {"host": host, "load_err": e, "png": png, "theme": tid})
                log("U6 캡처 %s: %s host=%s" % (th_name, png, json.dumps((host or {}).get("bounds") if isinstance(host, dict) else None)))
            if s0.get("themeId"):
                R("theme.select", id=s0["themeId"])
        if "record" in parts and code == 0:
            # B3 6-1 대체: 기기 안 screenrecord(5초 이내)로 애니메이션 진입을 녹화. 콜드 로딩이 길 수 있어 워밍업 1회 뒤 clear 하고 두 번째(웜) 로딩 중 녹화한다.
            import subprocess
            dev_path = "/sdcard/pd_dash_rec.mp4"
            dash("load")
            time.sleep(1.5)
            dash("clear")
            time.sleep(1.5)
            ra.adb(sr, "shell", "rm", "-f", dev_path)
            proc = subprocess.Popen([ra.ADB, "-s", sr, "shell", "screenrecord", "--time-limit", "5", dev_path], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
            time.sleep(1.0)
            res, wall, e = dash("load")
            try:
                proc.wait(timeout=20)
            except subprocess.TimeoutExpired:
                proc.terminate()
            time.sleep(1.0)
            rc, data = ra.adb(sr, "exec-out", "cat", dev_path, binary=True)
            mp4 = None
            if rc == 0 and len(data) > 1000:
                mp4 = unique("dash_android_%s_%s_%s_%s_anim.mp4" % (a.tag, a.lib, a.style, fa))
                open(mp4, "wb").write(data)
                files.append(os.path.basename(mp4))
            ra.adb(sr, "shell", "rm", "-f", dev_path)
            left = ra.adb(sr, "shell", "ls", dev_path)[1].strip()
            save_json("dash_android_%s_%s_%s_%s_anim_rec.json" % (a.tag, a.lib, a.style, fa), {"load_resp": res, "wall_ms": wall, "err": e, "mp4": os.path.basename(mp4) if mp4 else None, "bytes": len(data) if rc == 0 else None, "record_started_before_load_s": 1.0, "device_file_after_rm": left, "note": "웜 로딩 중 녹화(워밍업 1회+clear 뒤)"})
            log("screenrecord anim=%d: %s bytes=%s 기기 파일 삭제 후 ls=%r" % (a.anim, os.path.basename(mp4) if mp4 else None, len(data) if rc == 0 else None, left))
        log("끝 files=%s" % json.dumps(files, ensure_ascii=False))
    except Exception as e:  # noqa: BLE001
        code = code or 2
        log("오류 %r" % (e,))
    finally:
        if rot_orig:
            ra.adb(sr, "shell", "settings", "put", "system", "user_rotation", rot_orig["user_rotation"])
            ra.adb(sr, "shell", "settings", "put", "system", "accelerometer_rotation", rot_orig["accelerometer_rotation"])
        ra.adb(sr, "shell", "wm", "size", "reset")
        ra.cleanup(sr, state)
        log("원복 후 rotation=%s/%s stay_on=%s" % (gs("system", "accelerometer_rotation"), gs("system", "user_rotation"), gs("global", "stay_on_while_plugged_in")))
        print("EXIT", code, flush=True)
        sys.exit(code)


if __name__ == "__main__":
    main()
