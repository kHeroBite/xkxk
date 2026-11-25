# B10(D-5) 판정 공용 — 창 메뉴 열린 창 칸의 활성 ✓(진단 필드 active·activeSource) 기계 판정·✓ 이동 확인·활성/비활성 칸 확대 크롭 나란히
"""
menu_icons_judge.py --part b10(데스크톱) · menu_modal_font_web.py --part b10(웹)이 import 한다. 직접 실행은 --self-test 만.
진단 필드(odev-5 menu.icons 칸 항목): active = 화면 체크 칸 ✓(ToggleType==CheckBox && IsChecked), activeSource = OpenWindowSlot.IsActive(열린 창 칸이 아니면 null).
규칙(문서 2개, 데스크톱·웹 같음): ① 보이는 칸마다 active == activeSource ② active=true 칸은 정확히 1개이고 그 칸이 활성 문서의 칸(칸 n = 열린 순서 n)
③ 비활성 칸 active=false ④ 보이는 칸 모두 문서 아이콘(doc·리소스·표시). 다른 문서를 활성화한 뒤 같은 규칙으로 다시 본다(✓ 이동).
active 필드가 없는 판(고치기 전)은 기계 판정 없이 RECORDED(판독 대기) — 판독자가 c27_judge_common.py --set-verdict 로 정한다.
필드가 있으면 사례가 PASS·FAIL 만이라 결과가 바로 PASS·FAIL 이다(확대 크롭은 meta.b10 과 증거로만 남는다).
"""
import os
import sys
import time

import judge_session as session
from c27_judge_common import verdict

B10_METHOD = ("문서 2개(표·간트, 간트 활성)를 연 채 menu.icons 열린 창 칸별 active(화면 ✓)·activeSource(OpenWindowSlot.IsActive) 대조 — "
              "칸마다 active==activeSource, 활성 칸 1개만 true·활성 문서 칸, 비활성 칸 false, 보이는 칸 모두 문서 아이콘. 표 문서를 활성화해 "
              "다시 조회(✓ 이동). 창 메뉴 전체 캡처 + 활성·비활성 칸 4배 확대 크롭 나란히(b10_compare.png). 필드가 없는 판은 판독 기준 "
              "'활성 칸 표시가 비활성 칸과 눈으로 바로 구분되는가'로 판독자가 정한다")


def slots_of(s):
    menu = (s.call("menu.icons", id="shell.mnuWindow").get("menus") or [{}])[0]
    return [it for it in menu.get("items") or [] if str(it.get("id", "")).startswith("shell.mnuWinList") and it["id"][-1:].isdigit()]


def active_doc(s):
    listed = s.call("docs.list")
    listed = listed if isinstance(listed, list) else listed.get("docs", [])
    return next((d.get("id") for d in listed if d.get("active")), None)


def b10_open(s):
    """문서를 비우고 표·간트 2개를 연다(간트가 활성). → (열린 창 칸 항목들, 열린 순서 문서들, 활성 문서 id)."""
    s.call("docs.close", all=True)
    time.sleep(1)
    for kind in ("grid", "gantt"):
        s.call("docs.open", kind=kind)
        time.sleep(2.5)
    return slots_of(s), s.call("window.load.state").get("docs") or [], active_doc(s)


def judge_b10_field(slots, docs, active_id):
    """active 필드가 하나도 없으면 None(판독 몫). 있으면 규칙 ①~④ 로 PASS·FAIL."""
    shown = [it for it in slots if it.get("visible")]
    if not shown or all(it.get("active") is None for it in shown):
        return None
    bad = {}
    for i, it in enumerate(shown):
        name = it.get("id") or "slot%d" % (i + 1)
        if it.get("active") is None or it.get("active") != it.get("activeSource"):
            bad[name + " ✓≠실제"] = [it.get("active"), it.get("activeSource")]
        if it.get("iconKind") != "doc" or not it.get("iconPresent") or it.get("geometryFound") is not True:
            bad[name + " 아이콘"] = {k: it.get(k) for k in ("iconKind", "iconKey", "geometryFound", "iconPresent")}
        own = i < len(docs) and docs[i].get("id") == active_id
        if bool(it.get("active")) != own:
            bad[name + (" 활성 칸인데 ✓ 없음" if own else " 비활성 칸에 ✓")] = it.get("active")
    marks = [it.get("active") for it in shown]
    if marks.count(True) != 1:
        bad["✓ 개수"] = marks
    return verdict(bad, "활성 문서 칸만 ✓(= 실제 활성)·보이는 칸 문서 아이콘", marks=marks, active_doc=active_id)


def icon_bands(img, x0, x1, min_h=5, delta=40):
    """img 의 x0~x1 세로 띠에서 바탕과 다른 화소가 있는 행 덩어리 [(y0, y1)] — 높이 min_h 미만(구분선)은 뺀다."""
    band = img.crop((x0, 0, x1, img.height)).convert("L")
    ground = max(band.getcolors(band.width * band.height))[1]
    rows = [any(abs(band.getpixel((x, y)) - ground) > delta for x in range(band.width)) for y in range(band.height)]
    out, start = [], None
    for y, hit in enumerate(rows + [False]):
        if hit and start is None:
            start = y
        elif not hit and start is not None:
            if y - start >= min_h:
                out.append((start, y))
            start = None
    return out


def side_by_side(crops, labels, out, zoom=4):
    """크롭들을 zoom 배(NEAREST)로 키워 가로로 붙이고 위에 ASCII 꼬리표를 단다."""
    from PIL import Image, ImageDraw

    big = [c.resize((c.width * zoom, c.height * zoom), Image.NEAREST) for c in crops]
    canvas = Image.new("RGB", (sum(b.width for b in big) + 16 * (len(big) - 1), max(b.height for b in big) + 18), (255, 255, 255))
    x, draw = 0, ImageDraw.Draw(canvas)
    for b, label in zip(big, labels):
        canvas.paste(b, (x, 18))
        draw.text((x + 2, 2), label, fill=(200, 0, 0))
        x += b.width + 16
    canvas.save(out)
    return out


def b10_cases(s, slots, docs, active_id, crops, moved_slots, moved_active, prefix="b10."):
    """크롭 저장 + 사례. 필드가 있으면 PASS·FAIL 사례만(전/후), 없으면 RECORDED(판독 대기) 1건."""
    labels = ["slot%d %s" % (i + 1, "ACTIVE" if i < len(docs) and docs[i].get("id") == active_id else "inactive") for i in range(len(crops))]
    paths = []
    for c, label in zip(crops, labels):
        paths.append(os.path.join(s.shots, "b10_%s.png" % label.replace(" ", "_")))
        c.save(paths[-1])
    compare = side_by_side(crops, labels, os.path.join(s.shots, "b10_compare.png")) if crops else None
    s.meta["b10"] = {"compare": compare, "crops": paths, "labels": labels, "read_rule": "활성 칸 표시가 비활성 칸과 눈으로 바로 구분되는가"}
    before, after = judge_b10_field(slots, docs, active_id), judge_b10_field(moved_slots, docs, moved_active)
    if before is None and after is None:
        return {prefix + "read": {"verdict": "RECORDED", "why": "진단 active 필드 없음 — 판독 대기(--set-verdict)", "compare": compare}}
    missing = {"verdict": "FAIL", "why": "한쪽 조회에만 active 필드가 있다"}
    return {prefix + "field": before or missing, prefix + "field_moved": after or missing}


def b10_self_test():
    from PIL import Image, ImageDraw

    img = Image.new("RGB", (100, 80), (255, 255, 255))
    d = ImageDraw.Draw(img)
    d.rectangle((5, 10, 20, 25), fill=(0, 0, 0))
    d.line((0, 40, 99, 40), fill=(0, 0, 0))  # 구분선(높이 1) — 빼야 한다
    d.rectangle((5, 50, 20, 65), fill=(0, 0, 0))
    docs = [{"id": "G"}, {"id": "T"}]
    icon = {"visible": True, "iconKind": "doc", "iconKey": "K", "geometryFound": True, "iconPresent": True}
    on, off = dict(icon, active=True, activeSource=True), dict(icon, active=False, activeSource=False)

    class _S:  # b10_cases 용 가짜 세션(shots·meta 만)
        import tempfile
        shots, meta = tempfile.mkdtemp(), {}

    crop = Image.new("RGB", (10, 6), (255, 255, 255))
    rec = b10_cases(_S, [dict(icon), dict(icon)], docs, "T", [crop, crop], [dict(icon), dict(icon)], "G")
    fld = b10_cases(_S, [off, on], docs, "T", [crop, crop], [on, off], "G")
    bad = b10_cases(_S, [off, on], docs, "T", [crop, crop], [off, on], "G")  # ✓ 가 안 옮겨 감
    import shutil
    shutil.rmtree(_S.shots)
    return session.run_cases([
        ("띠: 구분선 빼고 아이콘 2개", icon_bands(img, 0, 30) == [(10, 26), (50, 66)]),
        ("필드 없음 → None(판독 몫)", judge_b10_field([dict(icon), dict(icon)], docs, "T") is None),
        ("필드 PASS", judge_b10_field([off, on], docs, "T")["verdict"] == "PASS"),
        ("✓≠실제(activeSource) FAIL", judge_b10_field([off, dict(on, activeSource=False)], docs, "T")["verdict"] == "FAIL"),
        ("엉뚱한 칸 ✓ FAIL", judge_b10_field([on, off], docs, "T")["verdict"] == "FAIL"),
        ("✓ 0개 FAIL", judge_b10_field([off, off], docs, "T")["verdict"] == "FAIL"),
        ("✓ 2개 FAIL", judge_b10_field([on, on], docs, "T")["verdict"] == "FAIL"),
        ("아이콘 없음 FAIL", judge_b10_field([dict(off, iconKind="none"), on], docs, "T")["verdict"] == "FAIL"),
        ("필드 없음 → RECORDED 1건", list(rec) == ["b10.read"] and rec["b10.read"]["verdict"] == "RECORDED"),
        ("필드 있음 → PASS 바로(RECORDED 없음)", session.overall(fld) == "PASS" and "b10.read" not in fld),
        ("✓ 이동 안 함 → FAIL 바로", session.overall(bad) == "FAIL"),
        ("크롭·비교 그림 meta 기록", len(_S.meta["b10"]["crops"]) == 2 and _S.meta["b10"]["labels"][1] == "slot2 ACTIVE"),
    ])


if __name__ == "__main__":
    sys.exit(b10_self_test() if "--self-test" in sys.argv else 2)
