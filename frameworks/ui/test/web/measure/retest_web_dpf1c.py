# D-PF1 Fold5 보충: 정지 rAF 를 5초 창으로 나눠 재면서 창마다 기기 화면 주사율(SurfaceFlinger activeMode·display renderFrameRate)을 함께 읽어 41.7ms(24Hz)와 대조하는 스크립트(otest-m-3)
"""
사용: python3 -E -u test/web/measure/retest_web_dpf1c.py --url-part 127.0.0.1:5193 --out <json> [--windows 12] [--sec 5]
읽기만 한다(기기 설정을 바꾸지 않는다).
"""
import argparse
import json
import os
import re
import subprocess
import sys
import time

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "common"))  # D-TESTDIR: test/common/_paths.py
import _paths  # noqa: E402
_paths.add()
from cdp_bridge import Bridge  # noqa: E402
from retest_web_dpf1 import RAF_JS, RAF_STOP, stats  # noqa: E402

ADB = "/mnt/c/Program Files (x86)/Android/android-sdk/platform-tools/adb.exe"


def display_rate(serial):
    out = subprocess.run([ADB, "-s", serial, "shell", "dumpsys SurfaceFlinger | grep -m1 activeMode; dumpsys display | grep -m1 -o 'renderFrameRate [0-9.]*'"],
                         capture_output=True, text=True, timeout=30).stdout
    m = re.search(r"vsyncRate=([0-9.]+)", out)
    r2 = re.search(r"renderFrameRate ([0-9.]+)", out)
    return {"sf_vsync": float(m.group(1)) if m else None, "render_rate": float(r2.group(1)) if r2 else None}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--url-part", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--windows", type=int, default=12)
    ap.add_argument("--sec", type=float, default=5.0)
    ap.add_argument("--serial", default="R3CW70202FP")
    ap.add_argument("--arrange", action="store_true", help="창마다 Tabbed/MdiCascade 를 번갈아 적용")
    a = ap.parse_args()
    b = Bridge(a.url_part)
    rows = []
    presets = ["Tabbed", "MdiCascade"]
    G = [d.get("id") for d in (b.call("docs.list") or []) if d.get("kind") == "grid"][0]
    for i in range(a.windows):
        arr = None
        if a.arrange:
            # 41.7ms 는 1차·보충 실행에서 배치 전환 직후 창에서만 나왔다 — 창마다 Tabbed/MdiCascade 를 번갈아 적용하고 5초 쉰 뒤 잰다
            arr = presets[i % 2]
            b.call("docs.arrange", preset=arr)
            if arr == "Tabbed":
                b.call("docs.activate", doc=G)
            time.sleep(5)
        d0 = display_rate(a.serial)
        b.eval(RAF_JS)
        time.sleep(a.sec)
        st = stats(json.loads(b.eval(RAF_STOP)), drop_ms=0)
        d1 = display_rate(a.serial)
        rows.append({"w": i + 1, "arrange": arr, "raf": st, "display_before": d0, "display_after": d1, "at": time.strftime("%H:%M:%S")})
        print(i + 1, st.get("median_ms"), d0, d1, flush=True)
    with open(a.out, "w", encoding="utf-8") as f:
        json.dump({"docs": len(b.call("docs.list") or []), "layout_mode": (b.call("layout.get") or {}).get("Mode"), "rows": rows}, f, ensure_ascii=False, indent=1)
    b.close()


if __name__ == "__main__":
    main()
