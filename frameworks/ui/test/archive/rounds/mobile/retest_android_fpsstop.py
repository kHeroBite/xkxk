# 조작 뒤 상태바 FPS 정지 판정(odev-3 README_idle_fix 정의): adb swipe 5초 → screencap 만으로 0.5초 간격 6초 상태바 영역 md5 → 마지막 변화 시각, 끝에 perf.idle 1회
"""
사용: python3 -E -u tools/retest_android_fpsstop.py --apk <진단 APK> --outdir <폴더> [--reps 3]
판정 구간(swipe 시작~캡처 끝)에는 원격 명령을 부르지 않는다. 상태바 위치는 시작 전에 ui.find(StatusLabel)로 한 번만 읽는다.
PASS: 마지막 입력 뒤 4초 안에 영역이 고정되고 끝까지 변하지 않음.
"""
import argparse
import hashlib
import io
import json
import os
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import retest_android as ra  # noqa: E402
import retest_android_cond2 as c2  # noqa: E402
from PIL import Image  # noqa: E402


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--apk", required=True)
    p.add_argument("--serial", default="R3CW70202FP")
    p.add_argument("--outdir", required=True)
    p.add_argument("--reps", type=int, default=3)
    a = p.parse_args()
    os.makedirs(a.outdir, exist_ok=True)
    sr = a.serial
    state, rep = {}, {"reps": []}

    def shot():
        rc, data = ra.adb(sr, "exec-out", "screencap", "-p", binary=True)
        k = data.find(b"\x89PNG") if rc == 0 else -1
        return Image.open(io.BytesIO(data[k:])).convert("RGB") if k >= 0 else None

    try:
        info = ra.prepare(sr, a.apk, state)
        from retest_remote import Remote
        c2.launch(sr, "--fresh", info)
        time.sleep(3)
        r = Remote()
        r.call("docs.close", all=True)
        r.call("docs.open", kind="grid")  # 폼 문서는 입력칸 포커스로 키보드가 떠 상태바를 가린다(1차 실측) — 그리드만 연다
        r.call("layout.template.apply", id="tpl.mdi-cascade")
        lab = r.call("ui.find", id="StatusLabel") or {}
        rep["status_label"] = {k: lab.get(k) for k in ("bounds", "text", "effectivelyVisible")}
        rc, ime = ra.adb(sr, "shell", "dumpsys input_method | grep -m1 mInputShown")
        rep["ime_before"] = ime.strip()
        time.sleep(4)
        img0 = shot()
        img0.save(os.path.join(a.outdir, "full_ref.png"))
        W, H = img0.size
        # 상태바 띠: StatusLabel 의 화면 y(DIP×배율)를 쓰되, 못 얻으면 화면 아래 12% 띠
        b = lab.get("bounds") or {}
        sc = 2.25
        try:
            y0 = int(float(b.get("y")) * sc) - 10
            y1 = int((float(b.get("y")) + float(b.get("h"))) * sc) + 10
        except Exception:
            y0, y1 = int(H * 0.88), H
        y0, y1 = max(0, y0), min(H, max(y1, y0 + 20))
        # x 는 FPS 글자 칸만(2차 실측: 띠 전체는 위쪽 그리드 테두리 화소까지 들어가 스크롤 관성과 섞일 수 있다)
        try:
            x0, x1 = int((float(b.get("x")) - 4) * sc), int((float(b.get("x")) + float(b.get("w")) + 24) * sc)
        except Exception:
            x0, x1 = 0, W
        y0 = max(y0, int(float(b.get("y") or 0) * sc) + 4) if b else y0
        box = (max(0, x0), y0, min(W, x1), y1)
        rep["crop_box"] = box
        img0.crop(box).save(os.path.join(a.outdir, "crop_ref.png"))
        for rp in range(1, a.reps + 1):
            time.sleep(3)
            # 1) 손가락 끌기 약 5초 — 원격 명령 없음
            ins = []
            t0 = time.time()
            j = 0
            while time.time() - t0 < 5:
                ra.adb(sr, "shell", "input", "swipe", "900", "1300", "900", "900" if j % 2 == 0 else "1500", "200")
                h = hashlib.md5(shot().crop(box).tobytes()).hexdigest()[:10]
                ins.append({"t": round(time.time() - t0, 2), "md5": h})
                j += 1
            t_last = time.time()
            # 2) 0.5초 간격 약 6초 screencap 만
            aft = []
            nxt = t_last
            while time.time() - t_last < 6.2:
                time.sleep(max(0, nxt - time.time()))
                nxt += 0.5
                im = shot()
                aft.append({"since_input": round(time.time() - t_last, 2), "md5": hashlib.md5(im.crop(box).tobytes()).hexdigest()[:10]})
                if len(aft) in (1, 6, 12):
                    im.crop(box).save(os.path.join(a.outdir, "r%d_after_%d.png" % (rp, len(aft))))
            last_change = 0.0
            for k in range(1, len(aft)):
                if aft[k]["md5"] != aft[k - 1]["md5"]:
                    last_change = aft[k]["since_input"]
            # 3) 보조: perf.idle 1회
            pi = r.call("perf.idle")
            res = {"rep": rp, "input_samples": ins, "input_changed": len({x["md5"] for x in ins}) > 1, "after": aft,
                   "last_change_s": last_change, "pass": last_change <= 4.0, "perf_idle": pi}
            rep["reps"].append(res)
            print("rep", rp, "input_changed", res["input_changed"], "last_change_s", last_change, "pass", res["pass"], "perf.idle", pi, flush=True)
    finally:
        rep["host_load"] = os.getloadavg()
        json.dump(rep, open(os.path.join(a.outdir, "fpsstop_result.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=1)
        ra.cleanup(sr, state)
        ra.adb(sr, "shell", "wm", "size", "reset")


if __name__ == "__main__":
    main()
