# PERF-15 3840px 배경 이미지 적용(읽기 시작 → 적용 뒤 첫 프레임) 시간을 재고 판정하는 도구
"""
사용(앱은 run_desktop_bg.sh 로 격리 기동돼 있어야 한다)
  perf_background_image.py --info <격리>/remote-control.json --away-state <pc_away.json> --approved '<누가·언제>' --out <결과.json> [--n 20]
  perf_background_image.py --self-test

긴 변 3840px 시험 이미지(사진에 가까운 잡음 JPEG)를 임시 폴더에 만들어 background.import 로 넣고,
시행마다 perf.reset → background.reload 뒤 ui.background.apply.firstframe 1건을 받는다(첫 1회는 예열로 버림).
기준 04/03 §6.4: p95 <= 300ms, n >= 20. 값이 빠진 시행이 있으면 INVALID.
"""
import os
import subprocess
import sys
import tempfile
import time

import perf_effect_common as common

METRIC = "ui.background.apply.firstframe"
LIMIT_MS = 300.0
SIZE = (3840, 2160)


def judge(values, minimum=common.MIN_TRIALS):
    good = [v for v in values if v is not None]
    if len(good) != len(values):
        return "INVALID", {"missing": len(values) - len(good)}
    if len(good) < minimum:
        return "DEFERRED", {"n": len(good), "need": minimum}
    p95 = common.percentile(good, 95)
    return ("PASS" if p95 <= LIMIT_MS else "FAIL"), {"p95_ms": p95, "limit_ms": LIMIT_MS, "stats": common.summarize(good)}


def make_image(folder):
    from PIL import Image

    noise = Image.effect_noise((SIZE[0] // 4, SIZE[1] // 4), 48).resize(SIZE)
    path = os.path.join(folder, "perf_background_3840.jpg")
    Image.merge("RGB", (noise, noise.rotate(180), noise.point(lambda v: 255 - v))).save(path, quality=88)
    return path


def self_test():
    return common.run_cases([
        ("300 경계 PASS", judge([300.0] * 20)[0] == "PASS"),
        ("300 초과 FAIL", judge([300.1] * 20)[0] == "FAIL"),
        ("표본 19 DEFERRED", judge([10.0] * 19)[0] == "DEFERRED"),
        ("빠진 시행 INVALID", judge([10.0] * 19 + [None])[0] == "INVALID"),
    ])


def main():
    ap = common.parser(__doc__)
    ap.add_argument("--n", type=int, default=common.MIN_TRIALS)
    a = ap.parse_args()
    if a.self_test:
        return self_test()

    def body():
        s = common.Session(a)
        values = []
        with tempfile.TemporaryDirectory() as folder:
            path = make_image(folder)
            win = subprocess.check_output(["wslpath", "-w", path], text=True).strip()
            r = s.call("background.import", path=win)
            if not r.get("ok") or r.get("width") != SIZE[0]:
                raise common.Abort("INVALID", "이미지를 넣지 못했다: %s" % r)
            time.sleep(1.5)
            try:
                for i in range(a.n + 1):
                    s.away(True)
                    s.call("perf.reset")
                    s.call("background.reload")
                    time.sleep(1.2)
                    v = common.single(s.rig.ops(METRIC), METRIC, "frame_p50_ms")
                    if i:
                        values.append(v)
            finally:
                s.rig.call("background.set", clear=True)
                s.rig.call("style.effect.set", key="decor", value="")
        v, r = judge(values)
        return {"verdict": v, "PERF-15": r, "values": values, "meta": s.meta}

    return common.finish(a, "perf_background_image", body)


if __name__ == "__main__":
    sys.exit(main())
