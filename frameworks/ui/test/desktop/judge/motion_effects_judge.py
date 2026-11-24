# AC-012 판정기 — 창 움직임 효과의 표 전수 복원과, 재생 중 누름·닫기·새 재생 때 즉시 끝·원래 화면 복원을 판정한다
"""
사용
  motion_effects_judge.py --info <격리>/remote-control.json --away-state <pc_away.json> --approved '<누가·언제>' --out <결과.json>
  motion_effects_judge.py --bundle <wwwroot> --port <포트> --out <결과.json>      (웹 결과)
  motion_effects_judge.py --self-test

'재생 중' 은 motion.probe hold 로 실제 재생 코드를 중간 지점에 멈춘 상태다. 시간에 따라 흐르는 재생의 매끄러움은 재지 않는다.
멈춘 화면이 정지 화면과 다르지 않으면 효과가 그려졌다고 볼 수 없으므로 복원 판정도 INVALID 다.
깨끗한 격리 인스턴스에서만 돈다(문서를 열고 닫는다).
"""
import sys
import time

import judge_session as session

HELD_MIN = 0.05   # 멈춘 화면이 정지 화면과 달라야 하는 화소 비율
SAME_MAX = 0.005  # 복원 뒤 정지 화면과 달라도 되는 화소 비율(캐럿·시각 표시)
PIXEL = 30
PIXEL_LOW = 12    # 어두운 테마처럼 창과 뒤 배경의 밝기차가 작을 때만 쓰는 낮은 색차 기준(HELD_MIN 문턱은 그대로)


def verdict(problems, why, **data):
    return dict(data, verdict="FAIL" if problems else "PASS", why=why, problems=problems)


def judge_table(probe):
    rows = probe.get("effects") or []
    if not rows:
        return {"verdict": "INVALID", "why": "효과 표본이 없다(대상 창 없음)"}
    bad = {r["id"]: "전 값으로 돌아오지 않음" for r in rows if not r.get("restored")}
    for r in rows:
        if any(not 0 <= s["o"] <= 1 for s in r["samples"]):
            bad[r["id"] + ".opacity"] = "0~1 밖"
    if not probe.get("allRestored"):
        bad["allRestored"] = probe.get("allRestored")
    return verdict(bad, "표 전 행 복원·불투명도 0~1", count=len(rows))


def judge_interrupt(held, left, changed, after):
    """held = hold 응답, left = 중단 뒤 release 응답, changed·after = 정지 화면과 다른 화소 비율(None = 화면 비교 없음)."""
    if not held.get("held"):
        return {"verdict": "INVALID", "why": "재생을 멈춰 세우지 못했다", "held": held}
    if changed is not None and changed < HELD_MIN:
        return {"verdict": "INVALID", "why": "멈춘 화면이 정지 화면과 같아 복원을 잴 수 없다", "changed": changed}
    bad = {}
    if left.get("released") != 0:
        bad["남은 재생"] = left.get("released")
    if left.get("allRestored") is not True:
        bad["allRestored"] = left.get("allRestored")
    if after is not None and after > SAME_MAX:
        bad["복원 화면 차이"] = after
    return verdict(bad, "남은 전환 0·원래 값 복원", changed=changed, after=after)


def ratio(a, b, box, pixel=PIXEL):
    from PIL import ImageChops

    diff = ImageChops.difference(a.crop(box), b.crop(box)).convert("L").point(lambda v: 255 if v > pixel else 0)
    return diff.histogram()[255] / float(max(1, diff.width * diff.height))


def held_changed(strict, low, noise):
    """멈춘 화면 변화 비율. 기본 색차(PIXEL) 값이 문턱 미만이어도, 같은 정지 화면끼리의 낮은 색차 잡음(noise)이
    SAME_MAX 이하라 낮은 색차 값을 믿을 수 있으면 낮은 색차 값을 쓴다(밝은 테마처럼 strict 가 문턱 이상이면 그대로)."""
    if strict >= HELD_MIN or noise is None or noise > SAME_MAX:
        return strict
    return max(strict, low)


def judge(s):
    if s.call("window.load.state").get("docs"):
        raise session.Abort("INVALID", "문서가 열려 있다(깨끗한 격리 폴더로 다시)")
    opened = []

    def open_one(kind):
        before = {d["id"] for d in s.call("window.load.state")["docs"]}
        s.call("docs.open", kind=kind)
        time.sleep(3)
        new = [d["id"] for d in s.call("window.load.state")["docs"] if d["id"] not in before]
        if len(new) != 1:
            raise session.Abort("INVALID", "문서를 열지 못했다: %s" % kind)
        opened.append(new[0])
        return new[0]

    cases = {}
    try:
        doc = open_one("form")
        cases["table"] = judge_table(s.call("motion.probe", effect="all"))
        bounds = s.call("ui.find", id="PART_OuterBorder")["bounds"]
        still, scale = s.capture("motion_still")
        x, y = bounds["x"] * scale, bounds["y"] * scale
        box = (max(0, int(x) + 4), max(0, int(y) + 4),
               min(still.width, int(x + bounds["w"] * scale) - 4), min(still.height, int(y + bounds["h"] * scale) - 4))

        noise = ratio(still, s.capture("motion_still_again")[0], box, PIXEL_LOW)  # 낮은 색차 기준의 잡음 바닥

        def hold(effect, name):
            held = s.call("motion.probe", effect=effect, hold="0.3")
            time.sleep(0.6)
            shot = s.capture(name)[0]
            return held, held_changed(ratio(still, shot, box), ratio(still, shot, box, PIXEL_LOW), noise)

        held, changed = hold("wipeRight", "motion_held_press")
        s.call("ui.pointerDrag", id="PART_Grid", doc=doc, dx=0, dy=0, steps=1)  # 끌기 시작 = 누름(이동 0)
        time.sleep(0.8)
        left = s.call("motion.probe", hold="release")
        cases["interrupt.drag_start"] = judge_interrupt(held, left, changed, ratio(still, s.capture("motion_after_press")[0], box))

        s.call("motion.probe", effect="wipeRight", hold="0.3")
        held, changed = hold("circle", "motion_held_next")  # 다른 재생이 시작되면 앞 재생은 끝나고 1건만 남는다
        left = s.call("motion.probe", hold="release")
        time.sleep(0.8)
        one = dict(left, released=left.get("released", -1) - 1)
        cases["interrupt.next_effect"] = judge_interrupt(held, one, changed, ratio(still, s.capture("motion_after_next")[0], box))

        held, changed = hold("split", "motion_held_close")
        s.call("docs.close", doc=doc)
        opened.remove(doc)
        time.sleep(1.5)
        cases["interrupt.close"] = judge_interrupt(held, s.call("motion.probe", hold="release"), changed, None)
        # 사례로 두면 전체가 늘 RECORDED 가 되므로 결과의 meta 에 근거로 남긴다
        s.meta["flowing"] = ("시간에 따라 흐르는 재생은 이 판정기가 재지 않는다 — 근거는 odev-2 의 실제 재생 표본 실측"
                             "(test/web/measure/motion_web_check.py·test/mobile/measure/motion_nav_fold5.py)")
    finally:
        s.call("motion.probe", hold="release")
        for doc in opened:
            s.call("docs.close", doc=doc)
    return cases


def self_test():
    row = {"id": "fade", "restored": True, "samples": [{"o": 0}, {"o": 1}]}
    good = {"effects": [row], "allRestored": True}
    held, left = {"held": "split"}, {"released": 0, "allRestored": True}
    return session.run_cases([
        ("표 PASS", judge_table(good)["verdict"] == "PASS"),
        ("복원 안 됨 FAIL", judge_table({"effects": [dict(row, restored=False)], "allRestored": False})["verdict"] == "FAIL"),
        ("범위 밖 FAIL", judge_table({"effects": [dict(row, samples=[{"o": 1.375}])], "allRestored": True})["verdict"] == "FAIL"),
        ("표본 없음 INVALID", judge_table({"effects": [], "allRestored": True})["verdict"] == "INVALID"),
        ("중단 PASS", judge_interrupt(held, left, 0.4, 0.0)["verdict"] == "PASS"),
        ("남은 재생 FAIL", judge_interrupt(held, {"released": 1, "allRestored": True}, 0.4, 0.0)["verdict"] == "FAIL"),
        ("복원 거짓 FAIL", judge_interrupt(held, {"released": 0, "allRestored": False}, 0.4, 0.0)["verdict"] == "FAIL"),
        ("화면 남음 FAIL", judge_interrupt(held, left, 0.4, 0.2)["verdict"] == "FAIL"),
        ("안 그려짐 INVALID", judge_interrupt(held, left, 0.0, 0.0)["verdict"] == "INVALID"),
        ("멈춤 실패 INVALID", judge_interrupt({"held": None}, left, 0.4, 0.0)["verdict"] == "INVALID"),
        ("닫기 PASS", judge_interrupt(held, left, 0.4, None)["verdict"] == "PASS"),
        ("밝은 테마 변화 그대로", held_changed(0.4, 0.5, 0.0) == 0.4),
        ("어두운 테마 0.0489 보정", held_changed(0.0489, 0.12, 0.0) == 0.12),
        ("보정 후 문턱 통과", judge_interrupt(held, left, held_changed(0.0489, 0.12, 0.0), 0.0)["verdict"] == "PASS"),
        ("잡음 큼 보정 안 함", held_changed(0.0489, 0.12, 0.02) == 0.0489),
        ("진짜 안 그려짐 INVALID", judge_interrupt(held, left, held_changed(0.0, 0.001, 0.0), 0.0)["verdict"] == "INVALID"),
    ])


def main():
    args = session.parser(__doc__).parse_args()
    return self_test() if args.self_test else session.run(args, "motion_effects_judge", judge)


if __name__ == "__main__":
    sys.exit(main())
