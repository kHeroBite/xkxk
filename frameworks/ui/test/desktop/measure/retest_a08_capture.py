#!/usr/bin/env python3
# A08 전용 래퍼 — 동결 retest_real_input 을 그대로 import 해 caption_max 의 독립창 최대화 직후와 1초 뒤 캡처 2장만 더 찍는다(측정·판정 로직 불변)
"""
사용: python3 -B retest_a08_capture.py --items A08 --run-dir <bin/run/<복사본>> --phase final --writer otest-N --monitor DISPLAY1
      python3 -B retest_a08_capture.py --annotate <결과.json> --capture all --read-by otest-N --finding '<판독>'
배경: retest_real_input.a08 의 maximize() 는 메인 창만 최대화한 시점에 a08_max 를 찍고, 독립창 최대화는 그 뒤에 한다.
      그래서 a08_max 캡처에는 독립창이 보통 크기로 보인다(측정 zoomed 는 독립창 최대화 뒤 값). 이 래퍼는 독립창을
      되돌리는 rig.show 직전에 a08_max_sdi(직후)·a08_max_sdi_later(1초 뒤) 를 더 찍어 판독 불일치를 가린다.
동결 파일(retest_real_input.py)은 고치지 않는다 — sha 가 바뀌면 그 도구로 받은 결과가 무효가 된다. 결과의 script 는 이 파일이다.
안전 규칙·종료 코드는 retest_real_input.py 와 같다.
"""
import argparse
import os
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import retest_real_input as ri  # noqa: E402

_state = {"res": None}
_orig_measure = ri.Result.measure
_orig_show = ri.Rig.show


def _measure(self, name, fn, *a, **kw):
    if name != "caption_max":
        return _orig_measure(self, name, fn, *a, **kw)
    _state["res"] = self
    try:
        return _orig_measure(self, name, fn, *a, **kw)
    finally:
        _state["res"] = None


def _show(self, hwnd, cmd=9):
    res = _state["res"]
    if res is not None and hwnd != self.main:  # caption_max 안에서 독립창을 되돌리기 직전
        self.grab(res, "a08_max_sdi")
        time.sleep(1.0)
        self.grab(res, "a08_max_sdi_later")
    return _orig_show(self, hwnd, cmd)


ri.Result.measure = _measure
ri.Rig.show = _show


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ri.add_common_args(p)
    args = p.parse_args()
    if args.annotate:
        return ri.annotate(args)
    if args.items and [n.strip() for n in args.items.split(",") if n.strip()] != ["A08"]:
        print("거부: 이 래퍼는 A08 전용이다")
        return 3
    return ri.drive(args, {"A08": ri.ITEMS["A08"]}, ri.REQ, "desktop_work", os.path.abspath(__file__), ri.HUMAN)


if __name__ == "__main__":
    sys.exit(main())
