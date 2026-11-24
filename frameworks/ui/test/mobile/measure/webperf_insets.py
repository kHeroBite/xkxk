#!/usr/bin/env python3
# webperf_fold5 의 웹 탭 y 보정값(dy)을 dumpsys window insets 줄에서 계산하는 순수 함수와 기기 없는 자체 검사
"""
사용:
  python3 -B webperf_insets.py --self-test   # rc 0 = 전 사례 PASS, 1 = FAIL
dy = 화면 높이 − 내비게이션 막대 높이 − innerHeight×dpr − 상태 표시줄 높이.
"""
import re
import sys

_NB = re.compile(r"type=navigationBars frame=\[0,(\d+)\]\[\d+,(\d+)\]([^\r\n]*)")
_SB = re.compile(r"type=statusBars frame=\[0,0\]\[\d+,(\d+)\]")


def nav_bar_h(ins, default=0):
    """insets 줄에서 보이는(visible=true·높이>0) 내비게이션 막대 높이(px). 없으면 default.
    펼친 Fold5 는 invisible [0,0][0,0] 항목이 먼저 나올 수 있다(2026-11-14 r12, D-44)."""
    for m in _NB.finditer(ins):
        hh = int(m.group(2)) - int(m.group(1))
        if hh > 0 and "visible=true" in m.group(3):
            return hh
    return default


def calc_web_dy(h, ins, vp):
    """h: 화면 높이(px), ins: dumpsys insets 줄, vp: [innerHeight, devicePixelRatio]."""
    sb = _SB.search(ins)
    return h - nav_bar_h(ins, 0) - round(vp[0] * vp[1]) - (int(sb.group(1)) if sb else 0)


# 재현 입력 — 실측 원자료(webperf_web_*.json 의 web_dy.insets) 그대로
_R12_OPEN = (  # 2026-11-14 a16r12/open_invalid_wrongdoc_gantt_dy251 — 첫 navigationBars 가 invisible [0,0][0,0]
    "InsetsSource id=85590000 type=statusBars frame=[0,0][1812,80] visible=true flags= sideHint=TOP boundingRects=null\r\n"
    "        InsetsSource id=9f230001 type=navigationBars frame=[0,0][0,0] visible=false flags=SUPPRESS_SCRIM|INVALID sideHint=NONE boundingRects=null\r\n"
    "        InsetsSource id=b0aa0001 type=navigationBars frame=[0,2142][1812,2176] visible=true flags= sideHint=BOTTOM boundingRects=null\r\n")
_R8_OPEN = (  # a16r8/open — visible navigationBars 가 먼저(기존 값 217)
    "InsetsSource id=e6fd0001 type=navigationBars frame=[0,2142][1812,2176] visible=true flags= sideHint=BOTTOM boundingRects=null\r\n"
    "        InsetsSource id=6e380001 type=navigationBars frame=[0,0][0,0] visible=false flags=SUPPRESS_SCRIM|INVALID sideHint=NONE boundingRects=null\r\n"
    "        InsetsSource id=77b80000 type=statusBars frame=[0,0][1812,80] visible=true flags= sideHint=TOP boundingRects=null\r\n")
_R12_CLOSED = (  # a16r12/closed — 접힘, visible 먼저(기존 값 127)
    "InsetsSource id=85590000 type=statusBars frame=[0,0][904,85] visible=true flags= sideHint=TOP boundingRects=null\r\n"
    "        InsetsSource id=9f230001 type=navigationBars frame=[0,2282][904,2316] visible=true flags=SUPPRESS_SCRIM sideHint=BOTTOM boundingRects=null\r\n"
    "        InsetsSource id=b0aa0001 type=navigationBars frame=[0,0][0,0] visible=false flags=INVALID sideHint=NONE boundingRects=null\r\n")

CASES = [  # (이름, 화면 높이, insets, viewport, 기대 dy)
    ("a_r12_open_invisible_first", 2176, _R12_OPEN, [820, 2.25], 217),
    ("b_r8_open_visible_first", 2176, _R8_OPEN, [820, 2.25], 217),
    ("c_r12_closed_visible_first", 2316, _R12_CLOSED, [920, 2.25], 127),
]


def self_test():
    bad = 0
    for name, h, ins, vp, want in CASES:
        got = calc_web_dy(h, ins, vp)
        ok = got == want
        bad += not ok
        print("%s %s dy=%d 기대=%d nav=%d" % ("PASS" if ok else "FAIL", name, got, want, nav_bar_h(ins)))
    print("RESULT %s %d/%d" % ("PASS" if not bad else "FAIL", len(CASES) - bad, len(CASES)))
    return 0 if not bad else 1


if __name__ == "__main__":
    if "--self-test" in sys.argv[1:]:
        sys.exit(self_test())
    print(__doc__)
    sys.exit(2)
