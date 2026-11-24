# 유휴 수정 판 회귀(Fold5, odev-3 README_idle_fix 회귀 항목): 유휴 10초×3 frames·CPU, 조작 중/조작 뒤 상태바 FPS 갱신, 원격 조작 elapsed_ms·perf.ops, first_frame_ms 를 기록하는 스크립트
"""
사용: python3 -E -u tools/retest_android_idlefix.py --apk <유휴 수정 판 APK> --outdir <폴더>
판정 기준(README_idle_fix):
 1) 유휴 10초×3: frames 증가 0(또는 구간 끝 직후만), CPU 1~2% 수준
 2) 조작 중 상태바 FPS 글자가 바뀌고, 조작을 멈추면 약 2초 뒤 바뀌지 않음
 3) 원격 조작(theme.next 등) elapsed_ms·ui_frame_ms 가 기록되고 perf.ops 에 remote.* 가 있음
 4) perf.get first_frame_ms 가 기록됨
"""
import argparse
import json
import os
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import retest_android as ra  # noqa: E402
import retest_android_cond2 as c2  # noqa: E402
from retest_android_emptycells import walk  # noqa: E402


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--apk", required=True)
    p.add_argument("--serial", default="R3CW70202FP")
    p.add_argument("--outdir", required=True)
    p.add_argument("--seconds", type=float, default=10)
    a = p.parse_args()
    os.makedirs(a.outdir, exist_ok=True)
    sr = a.serial
    state, rep = {"_": None}, {}
    try:
        info = ra.prepare(sr, a.apk, state)
        from retest_remote import Remote
        c2.launch(sr, "--fresh", info)
        time.sleep(3)
        r = Remote()
        pg = r.call("perf.get") or {}
        rep["first_frame"] = {k: pg.get(k) for k in ("first_frame_ms", "startup_ms")}
        r.call("docs.close", all=True)
        for k in ("grid", "gantt", "form"):
            r.call("docs.open", kind=k)
        r.call("layout.template.apply", id="tpl.mdi-cascade")
        time.sleep(3)
        rc, pid = ra.adb(sr, "shell", "pidof", ra.PKG)
        pid = pid.strip().split()[0] if pid.strip() else None
        hz = 100

        def cpu():
            rc, st = ra.adb(sr, "shell", "cat", "/proc/%s/stat" % pid)
            f = st.split(")")[-1].split()
            return (int(f[11]) + int(f[12])) / hz if len(f) > 12 else None

        def cap(name):
            rc, data = ra.adb(sr, "exec-out", "screencap", "-p", binary=True)
            k = data.find(b"\x89PNG") if rc == 0 else -1
            if k >= 0:
                open(os.path.join(a.outdir, name + ".png"), "wb").write(data[k:])
                return name + ".png"
            return None

        def fps_text():
            tr = r.call("tree.get", named=False, depth=40) or {}
            n = walk(tr, [], lambda x: "TextBlock" in str(x.get("type")) and "FPS" in str(x.get("text")))
            return n[0].get("text") if n else None

        # 1) 유휴 10초 × 3
        idle = []
        for i in range(3):
            time.sleep(3)  # 직전 조작 구간(2초)이 끝나도록
            s0, c0, t0 = r.call("perf.idle") or {}, cpu(), time.time()
            time.sleep(a.seconds)
            s1, c1, t1 = r.call("perf.idle") or {}, cpu(), time.time()
            el = t1 - t0
            idle.append({"i": i + 1, "before": s0, "after": s1, "elapsed_s": round(el, 2),
                         "frames_delta": (s1.get("frames") or 0) - (s0.get("frames") or 0),
                         "cpu_s": round(c1 - c0, 3), "cpu_pct": round((c1 - c0) / el * 100, 1)})
            print("idle", i + 1, idle[-1]["frames_delta"], idle[-1]["cpu_pct"], flush=True)
        rep["idle"] = idle
        # 2) 조작 중·조작 뒤 상태바 FPS 글자(0.5초 간격) — 그리드 위 손가락 끌기 반복
        samples = []
        t_start = time.time()
        for j in range(10):  # 약 5초 조작
            ra.adb(sr, "shell", "input", "swipe", "900", "1300", "900", "900", "200")
            samples.append({"t": round(time.time() - t_start, 2), "phase": "input", "fps": fps_text()})
            if j == 5:
                rep["cap_fps_during_input"] = cap("fps_during_input")
        t_stop = time.time()
        for j in range(10):  # 조작 뒤 약 5초
            time.sleep(0.5)
            samples.append({"t": round(time.time() - t_start, 2), "phase": "after", "since_stop": round(time.time() - t_stop, 2), "fps": fps_text()})
            if j in (1, 5, 9):  # 약 1·3·5초 뒤 캡처(odev-3: 캡처로 확인)
                rep["cap_fps_after_%d" % j] = cap("fps_after_stop_%d" % j)
        rep["status_fps_samples"] = samples
        after = [s for s in samples if s["phase"] == "after"]
        last_change = None
        for k in range(1, len(after)):
            if after[k]["fps"] != after[k - 1]["fps"]:
                last_change = after[k]["since_stop"]
        rep["status_fps_last_change_after_stop_s"] = last_change
        rep["status_fps_changed_during_input"] = len({s["fps"] for s in samples if s["phase"] == "input"}) > 1
        # 3) 원격 조작 계측
        r.call("perf.reset")
        rem = []
        for i in range(5):
            r.call("theme.next")
            c = r.calls[-1]
            rem.append({"ok": c["ok"], "elapsed_ms": c["elapsed_ms"], "ui_frame_ms": c["ui_frame_ms"]})
        rep["remote_theme_next"] = rem
        rep["perf_ops_remote"] = [o for o in (r.call("perf.ops") or []) if isinstance(o, dict) and str(o.get("name", "")).startswith(("remote.", "theme."))]
        rep["idle_after_remote"] = r.call("perf.idle")
    finally:
        rep["host_load"] = os.getloadavg()
        json.dump(rep, open(os.path.join(a.outdir, "idlefix_result.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=1)
        ra.cleanup(sr, state)
        ra.adb(sr, "shell", "wm", "size", "reset")


if __name__ == "__main__":
    main()
