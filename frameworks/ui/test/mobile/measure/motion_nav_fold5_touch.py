# AC-011·022 Fold5 '실재생' 사례 — 전면 화면 문서 선택 칸을 실제로 터치해 전환하고 배치·캡처·재생을 잰다(motion_nav_fold5.py 가 부른다)
"""
전환은 화면 좌표 터치로만 만든다. docs.activateFrom 은 출처 표식 경로를 건너뛰므로 쓰지 않는다.
재생 양성은 perf 구간 motion.play 이고, 효과 종류는 터치 직후 본문 호스트 자리·불투명도 표본으로 본다.
양성 대조군(열기 fade)에 재생이 없거나 ui.style 실효값이 모바일형이 아니면 INVALID(UNDET)다.
"""
import os
import re
import sys
import time

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "desktop", "judge"))
from motion_close_rules import layout_problem  # noqa: E402

NODE = re.compile(r"<node [^>]*>")


def point_of(xml, title):
    """제목이 같은 노드 가운데 마지막(펼친 목록 항목)의 가운데."""
    found = None
    for node in NODE.findall(xml):
        if ('text="%s"' % title) in node or ('content-desc="%s"' % title) in node:
            box = re.search(r'bounds="\[(\d+),(\d+)\]\[(\d+),(\d+)\]"', node)
            if box:
                x1, y1, x2, y2 = map(int, box.groups())
                if x2 > x1 and y2 > y1:
                    found = (x1 + x2) // 2, (y1 + y2) // 2
    return found


def played(call):
    return "motion.play" in [p.get("name") for p in call("perf.get").get("phases") or []]


def run(c, call, device):
    call("style.effect.set", key="motion.switch", value="none")
    call("style.effect.set", key="motion.open", value="fade")
    call("perf.reset")
    call("docs.open", kind="gantt", count=2, allowDuplicate=True)  # 시작 문서가 없어도 전환 상대가 있게
    time.sleep(3)
    control = played(call)
    if not control:
        return {"verdict": "UNDET", "why": "INVALID — 양성 대조군(열기 fade) 재생 0", "evidence": "touch"}
    call("style.effect.set", key="motion.switch", value="same_as_open")
    rows = []
    for effect in ("pushLeft", "wipeRight"):
        call("style.effect.set", key="motion.open", value=effect)
        docs = call("docs.list")
        target = next(d for d in docs if not d.get("active"))
        style = call("ui.style").get("effective")
        row = {"effect": effect, "evidence": "touch", "ui_style": style, "capture": c.shot("playback_%s" % effect), "target": target["id"]}
        rows.append(row)
        row["problem"] = layout_problem("nav", {"effective": style})
        if row["problem"]:
            continue
        if not c.tap_id("shell.cmbDocs"):
            row["problem"] = "문서 선택 칸을 못 찾았다"
            continue
        time.sleep(1.5)
        xml = c.dump()
        point = point_of(xml, target["title"])
        if not point:
            row["problem"] = "목록 항목을 못 찾았다"
            device.key_back()  # 펼친 목록만 닫는다
            time.sleep(1.5)
            continue
        call("perf.reset")  # 화면 상태 확인을 겸한다
        device.sh("input tap %d %d" % (point[0], point[1] + c.bounds_top(xml, "android:id/content")))
        found = [c.call("ui.find", id="NavBodyHost", props="Opacity") for _ in range(8)]  # 읽기만, 확인 없이 빠르게
        row["samples"] = [(f["bounds"]["x"], float(f["props"]["Opacity"])) for f in found if f]
        time.sleep(2.5)
        row["played"], row["active"] = played(call), call("style.state")["active"]
        if row["active"] != target["id"]:
            row["problem"] = "터치로 활성이 바뀌지 않았다"
    return judge(rows)


def judge(rows):
    if not rows or any(r.get("problem") for r in rows):
        return {"verdict": "UNDET", "why": "INVALID — 배치·조작", "evidence": "touch", "rows": rows}
    if not all(r["played"] for r in rows):
        return {"verdict": "FAIL", "why": "실터치 전환에 재생 0", "evidence": "touch", "rows": rows}
    push, wipe = rows[0]["samples"], rows[1]["samples"]
    pushed = any(abs(x) > 1 for x, _ in push)
    faded = any(o < 0.99 for _, o in wipe) and all(abs(x) <= 1 for x, _ in wipe)
    why = "실터치 재생 2/2, 표본 pushLeft 밀림=%s·wipeRight fade=%s" % (pushed, faded)
    return {"verdict": "PASS" if pushed and faded else "UNDET", "why": why, "evidence": "touch", "rows": rows}
