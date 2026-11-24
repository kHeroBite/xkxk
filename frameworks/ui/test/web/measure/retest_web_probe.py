# Fold5 Chrome 웹 펼침에서 GridScrollProbe(D-SC2 H1) 계수기를 회마다 페이지를 다시 읽어 0 에서 재는 스크립트(i1835rc 계획서 §5, otest-m-3)
"""
사용: python3 -E -u test/web/measure/retest_web_probe.py --url-part 127.0.0.1:5191 --out-dir <폴더> [--oy 297 | --calib-oy] [--runs adb300:3,adb1000:3,cdp:1] [--finger 205.9]
회마다: Page.reload → 문서 수 기록(docs_at_open) → 그리드 1개가 아니면 모두 닫고 그리드 1개(normalized=true 기록) → 맨 위 → perf.get(전) → __pd 계수기 설치
       → 205.9 CSS px 위로 끌기(adb input swipe 지속 ms 또는 CDP touchMove 12단계·16ms) → 2.5초 → perf.get(후)·grid.visible·__pd
grid_probe_* 합계는 앞뒤 차이로, *_max 는 재기동(=페이지 다시 읽기) 직후 0 에서 시작한 회 값으로 읽는다.
--calib-oy: 행 탭 훑기로 adb 원점 oy 를 구한 뒤 페이지를 다시 읽는다(탭으로 생긴 포커스 행이 계측에 남지 않게).
결과: <out-dir>/webU_probe_{adb300|adb1000|cdp}_r{N}.json, <out-dir>/webU_probe_summary.json
"""
import argparse
import json
import math
import os
import subprocess
import sys
import time

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "common"))  # D-TESTDIR: test/common/_paths.py
import _paths  # noqa: E402
_paths.add()
from cdp_bridge import Bridge  # noqa: E402
from retest_web_scroll_ratio import PROBE_JS, PD_READ  # noqa: E402

ADB = "/mnt/c/Program Files (x86)/Android/android-sdk/platform-tools/adb.exe"


def probe_metrics(pg):
    """perf.get 응답에서 grid_probe_* 값을 모두 모은다(위치와 무관하게 재귀 탐색)."""
    out = {}

    def walk(o):
        if isinstance(o, dict):
            for k, v in o.items():
                if isinstance(k, str) and k.startswith("grid_probe_"):
                    out[k] = v
                else:
                    walk(v)
        elif isinstance(o, list):
            for v in o:
                walk(v)
    walk(pg)
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--url-part", required=True)
    ap.add_argument("--out-dir", required=True)
    ap.add_argument("--oy", type=float, default=None)
    ap.add_argument("--calib-oy", action="store_true")
    ap.add_argument("--runs", default="adb300:3,adb1000:3,cdp:1")
    ap.add_argument("--finger", type=float, default=205.9)
    ap.add_argument("--serial", default="R3CW70202FP")
    ap.add_argument("--tag", default="i1835rc")
    a = ap.parse_args()
    os.makedirs(a.out_dir, exist_ok=True)
    b = Bridge(a.url_part)
    sl = time.sleep
    summary = {"tag": a.tag, "url": b.url, "started": time.strftime("%Y-%m-%d %H:%M:%S"), "finger_css": a.finger, "runs": []}

    def wait_ready():
        for _ in range(40):
            try:
                if b.eval('typeof pdAutomation !== "undefined"') is True and isinstance(b.call("docs.list"), list):
                    return True
            except Exception:  # noqa: BLE001
                pass
            sl(1)
        return False

    def fresh():
        """다시 읽기 → 문서 1개(그리드) 상태. 반환 (G, docs_at_open, normalized, gb)."""
        b.cdp("Page.reload", ignoreCache=False)
        sl(3)
        ready = wait_ready()
        sl(2)
        ds = b.call("docs.list") or []
        at_open = {"count": len(ds), "kinds": [d.get("kind") for d in ds]}
        normalized = False
        if not (len(ds) == 1 and ds[0].get("kind") == "grid"):
            b.call("docs.close", all=True)
            sl(1.5)
            b.call("docs.open", kind="grid")
            sl(4)
            ds = b.call("docs.list") or []
            normalized = True
        G = [d.get("id") for d in ds if d.get("kind") == "grid"][0]
        b.call("docs.activate", doc=G)
        sl(1)
        gb = (b.call("ui.find", id="grid.dgMain", doc=G) or {}).get("bounds")
        return G, {"ready": ready, "docs_at_open": at_open, "normalized": normalized,
                   "docs_after": {"count": len(ds), "kinds": [d.get("kind") for d in ds]}}, gb

    G, cond, gb = fresh()
    metrics = b.eval("({w:innerWidth,h:innerHeight,dpr:devicePixelRatio,ua:navigator.userAgent})")
    dpr = metrics["dpr"]
    summary.update(metrics=metrics, gb=gb, mode=b.call("ui.mode"))
    GV = lambda: b.call("grid.visible", id="grid.dgMain", doc=G) or {}  # noqa: E731
    colx = gb["x"] + (230 if gb["w"] > 300 else gb["w"] * 0.55)
    oy = a.oy
    if a.calib_oy or oy is None:
        # 행 탭으로 adb 원점 보정(retest_web_dw.py 방식, P 는 grid.visible 의 offsetY/first 대신 rowHeight 근사 대신 실측 탭으로)
        rc = subprocess.run([ADB, "-s", a.serial, "shell", "wm", "size"], capture_output=True, text=True, timeout=30).stdout
        hscr = int(rc.strip().splitlines()[-1].split(":")[-1].strip().split("x")[1])
        guess = hscr - 34 - metrics["h"] * dpr
        b.call("grid.select", id="grid.dgMain", doc=G, index=0, scroll=True)
        sl(0.6)
        # 맨 위에서 4번째 행 중앙: 그리드 y + 머리글 + 3.5 × P(31.05 근사)
        P0 = 31.05
        target_i = 3
        tyc = gb["y"] + 32.25 + (target_i + 0.5) * P0
        tries = []
        for d in range(-90, 91, 12):
            yy = int(guess + d + tyc * dpr)
            subprocess.run([ADB, "-s", a.serial, "shell", "input", "tap", str(int(colx * dpr)), str(yy)], capture_output=True, timeout=30)
            sl(0.5)
            tries.append((d, yy, GV().get("selectedIndex")))
        ok_ds = [t[0] for t in tries if t[2] == target_i]
        oy = guess + (min(ok_ds) + max(ok_ds)) / 2.0 if ok_ds else (a.oy if a.oy is not None else 297.0)
        summary["calib_oy"] = {"guess": guess, "tries": tries, "oy": oy, "found": bool(ok_ds)}
        print("oy", oy, ok_ds, flush=True)
    summary["oy"] = oy
    first = True
    for spec in a.runs.split(","):
        kind, n = spec.split(":")
        for k in range(int(n)):
            if not first or a.calib_oy or a.oy is None:
                G, cond, gb = fresh()
                GV = lambda: b.call("grid.visible", id="grid.dgMain", doc=G) or {}  # noqa: E731
            first = False
            # 다시 읽은 직후라 맨 위(offsetY 0)다 — 선택·탭을 하지 않는다(포커스 행이 남지 않게)
            la = os.getloadavg()
            pg0 = b.call("perf.get")
            gv0 = GV()
            b.eval(PROBE_JS)
            pd0 = json.loads(b.eval(PD_READ))
            y0 = gb["y"] + gb["h"] * 0.8
            y1 = y0 - a.finger
            t0 = time.time()
            if kind.startswith("adb"):
                dur = int(kind[3:])
                sx, sya, syb = int(round(colx * dpr)), int(round(y0 * dpr + oy)), int(round(y1 * dpr + oy))
                act = ["shell", "input", "swipe", str(sx), str(sya), str(sx), str(syb), str(dur)]
                subprocess.run([ADB, "-s", a.serial] + act, capture_output=True, timeout=60)
            else:
                act = {"cdp": "touchStart→touchMove×12(16ms)→60ms→touchEnd", "x": colx, "y0": y0, "y1": y1}
                b.touch("touchStart", [{"x": colx, "y": y0, "id": 1}])
                for i in range(1, 13):
                    b.sleep_ms(16)
                    b.touch("touchMove", [{"x": colx, "y": y0 + (y1 - y0) * i / 12, "id": 1}])
                b.sleep_ms(60)
                b.touch("touchEnd", [])
            t1 = time.time()
            sl(2.5)
            gv1 = GV()
            pg1 = b.call("perf.get")
            pd1 = json.loads(b.eval(PD_READ))
            m0, m1 = probe_metrics(pg0), probe_metrics(pg1)
            delta = {}
            for key in sorted(set(m0) | set(m1)):
                v0, v1 = m0.get(key), m1.get(key)
                if isinstance(v0, (int, float)) and isinstance(v1, (int, float)) and not key.endswith("_max") and "intermediate" not in key:
                    delta[key] = round(v1 - v0, 3)
            d_off = (gv1.get("offsetY") or 0) - (gv0.get("offsetY") or 0)
            sum_dy = delta.get("grid_probe_sum_dy")
            lost = delta.get("grid_probe_lost_dy")
            ratio = round(d_off / a.finger, 3)
            derived = {"ratio_offset": ratio, "abs_sum_dy": abs(sum_dy) if isinstance(sum_dy, (int, float)) else None,
                       "lost_over_sum": round(lost / sum_dy, 3) if isinstance(lost, (int, float)) and sum_dy else None,
                       "one_minus_ratio": round(1 - ratio, 3),
                       "sum_dy_vs_201": ("미달" if abs(sum_dy) < 201 else "이상") if isinstance(sum_dy, (int, float)) else None}
            pdd = {k2: pd1.get(k2) for k2 in ("down", "move", "up", "cancel", "coalesced", "raf", "handlerMs", "handlerMax", "target", "touchAction", "cancelTarget")}
            row = {"kind": kind, "rep": k + 1, "tag": a.tag, "at": time.strftime("%Y-%m-%dT%H:%M:%S"), "condition": cond,
                   "action": act, "oy": oy, "dpr": dpr, "colx_css": round(colx, 1), "finger_css": a.finger, "drag_wall_ms": round((t1 - t0) * 1000),
                   "grid_before": gv0, "grid_after": gv1, "d_offsetY": round(d_off, 1),
                   "probe_before": m0, "probe_after": m1, "probe_delta": delta, "derived": derived,
                   "js_pd": pdd, "js_raf_during": pd1.get("raf", 0) - pd0.get("raf", 0), "host_load": [round(x, 2) for x in la]}
            nm = "webU_probe_%s_r%d.json" % (kind, k + 1)
            with open(os.path.join(a.out_dir, nm), "w", encoding="utf-8") as f:
                json.dump(row, f, ensure_ascii=False, indent=1)
            summary["runs"].append({"file": nm, "kind": kind, "rep": k + 1, "docs_at_open": cond["docs_at_open"]["count"], "normalized": cond["normalized"],
                                    "d_offsetY": row["d_offsetY"], "probe_delta": delta, "probe_after_max": {x: y for x, y in m1.items() if x.endswith("_max") or "intermediate" in x},
                                    "derived": derived, "js_move": pdd.get("move"), "host_load1": round(la[0], 2)})
            print("RUN", kind, k + 1, json.dumps(summary["runs"][-1], ensure_ascii=False), flush=True)
            with open(os.path.join(a.out_dir, "webU_probe_summary.json"), "w", encoding="utf-8") as f:
                json.dump(summary, f, ensure_ascii=False, indent=1)
    b.close()


if __name__ == "__main__":
    main()
