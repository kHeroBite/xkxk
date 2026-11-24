#!/usr/bin/env python3
# motion_web_check.py 의 판정 규칙(움직임·화소 차·종료 코드)과 그 자체 시험 — 브라우저 없이 돈다
from PIL import Image, ImageChops

MOVED_DIP = 1.5


def moved(rest, samples):
    """정지 자리에서 벗어난 표본 수."""
    return sum(1 for s in samples if abs(s["x"] - rest["x"]) + abs(s["y"] - rest["y"]) > MOVED_DIP)


def judge_switch(want_motion, changed, rest, samples, last):
    if not changed:
        return "FAIL", "Ctrl+F6 에 활성 문서가 바뀌지 않았다"
    if not samples:
        return "UNDET", "표본이 없다"
    n = moved(rest, samples)
    if moved(rest, [last]):
        return "FAIL", "끝난 뒤 제자리로 돌아오지 않았다"
    if want_motion:
        return ("PASS", "움직인 표본 %d/%d" % (n, len(samples))) if n else ("UNDET", "움직인 표본이 없다(재생 안 됨 또는 표본 간격보다 짧음)")
    return ("FAIL", "움직인 표본 %d/%d" % (n, len(samples))) if n else ("PASS", "움직임 없음(표본 %d)" % len(samples))


def diff_count(a, b, rects_out=(), box=None):
    """다른 화소 수. box 안만 세고 rects_out 사각형은 뺀다."""
    d = ImageChops.difference(a, b).convert("L").point(lambda v: 255 if v > 8 else 0)
    for rect in rects_out:
        d.paste(0, rect)
    if box:
        d = d.crop(box)
    return d.histogram()[255]


def judge_area(on_shots, off_shots, box):
    """켬·끔을 되풀이한 캡처. 창 안이 한 화소도 안 다른 켬·끔 짝이 있으면 창 안 변화는 배경 탓이 아니다(기준 0화소는 그대로)."""
    pairs = [(a, b) for a in on_shots for b in off_shots]
    inside = min(diff_count(a, b, box=box) for a, b in pairs)
    outside = min(diff_count(a, b, rects_out=[box]) for a, b in pairs)
    verdict = "PASS" if inside == 0 and outside >= 5000 else "FAIL"
    return verdict, "켬 %d·끔 %d장, 창 안 최소 %d·밖 최소 %d 화소 변화" % (len(on_shots), len(off_shots), inside, outside)


def not_in_build(error):
    """명령 자체가 없을 때만 '이 판에 없음'이다. 컨트롤을 못 찾은 것은 시험 잘못일 수 있어 ERROR 로 남긴다."""
    return "알 수 없는 명령" in error


def exit_code(verdicts):
    if not verdicts or "ERROR" in verdicts:
        return 2
    if "FAIL" in verdicts:
        return 1
    return 0 if set(verdicts) == {"PASS"} else 4


def self_test():
    rest, away = {"x": 100, "y": 100}, {"x": 110, "y": 108}
    base = Image.new("RGB", (40, 30), (10, 10, 10))
    inner, outer = base.copy(), base.copy()
    inner.paste((200, 0, 0), (10, 10, 14, 12))
    outer.paste((200, 0, 0), (0, 0, 5, 2))
    box = (8, 8, 30, 25)
    big = Image.new("RGB", (200, 200), (10, 10, 10))
    big_off, big_jitter = Image.new("RGB", (200, 200), (90, 90, 90)), big.copy()
    big_off.paste((10, 10, 10), (80, 80, 120, 120))
    big_jitter.paste((200, 0, 0), (90, 90, 92, 91))
    checks = {
        "움직임 검출": judge_switch(True, True, rest, [away, rest], rest)[0] == "PASS",
        "움직임 없으면 미확인": judge_switch(True, True, rest, [rest], rest)[0] == "UNDET",
        "대조군 움직임은 실패": judge_switch(False, True, rest, [away], rest)[0] == "FAIL",
        "대조군 정지": judge_switch(False, True, rest, [rest], rest)[0] == "PASS",
        "활성 미전환은 실패": judge_switch(True, False, rest, [away], rest)[0] == "FAIL",
        "제자리 복귀 실패": judge_switch(True, True, rest, [away], away)[0] == "FAIL",
        "표본 없음": judge_switch(False, True, rest, [], rest)[0] == "UNDET",
        "창 안 변화 검출": diff_count(base, inner, box=box) == 8,
        "창 밖 변화 제외": diff_count(base, outer, box=box) == 0,
        "창 밖만 셈": diff_count(base, inner, rects_out=[box]) == 0 and diff_count(base, outer, rects_out=[box]) == 10,
        "흔들림은 배경 탓이 아님": judge_area([big, big_jitter], [big_off], (80, 80, 120, 120))[0] == "PASS",
        "창 안 변화는 실패": judge_area([big_jitter], [big_off], (80, 80, 120, 120))[0] == "FAIL",
        "창 밖 변화 없음은 실패": judge_area([big], [big], (80, 80, 120, 120))[0] == "FAIL",
        "없는 명령": not_in_build("알 수 없는 명령입니다: x"),
        "없는 컨트롤은 오류": not not_in_build("컨트롤을 찾지 못했습니다"),
        "종료 코드": [exit_code(v) for v in (["PASS"], ["PASS", "FAIL"], ["FAIL", "ERROR"], ["PASS", "UNDET"], [])] == [0, 1, 2, 4, 2],
    }
    for name, ok in checks.items():
        print("%s %s" % ("PASS" if ok else "FAIL", name))
    print("self-test %d/%d" % (sum(checks.values()), len(checks)))
    return 0 if all(checks.values()) else 1
