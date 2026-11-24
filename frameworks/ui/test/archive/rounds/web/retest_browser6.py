# 브라우저(WASM) 판정 스크립트 — Q14 제목줄 한 줄 배치·로고/제목 대비, Q8·Q15 MDI 바탕색(#778899)·관리형 창 바깥 패널 가시성을 판정한다
# 사용: python3 tools/retest_browser6.py [--only q14,q8]   |   python3 tools/retest_browser6.py --selftest   (브라우저 없이 가짜 응답으로 판정 논리만 시험)
import io
import json
import os
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

OUT = os.path.join(HERE, "..", "logs", "otest_w2")
res = []
# 제목줄 구성요소 AutomationId(셸 담당 확정 전 추정 — 없으면 '미검증'). 키 → 후보 Id 목록
TITLE_IDS = {  # odev-2 확정(Views/MainTitleBar.axaml, 2차 판본)
    "logo": ["shell.titleLogo"], "title": ["shell.titleText"],
    "menu_file": ["shell.mnuFile"], "menu_window": ["shell.mnuWindow"], "menu_view": ["shell.mnuView"], "menu_win": ["shell.mnuWinMenu"],
    "drag": ["shell.titleDragArea"],
}
CAPTION_IDS = {"min": "shell.btnMinimize", "max": "shell.btnMaximize", "close": "shell.btnClose"}  # 웹·Android 는 설계상 숨김(없음이 정상)
MDI_BG = (0x77, 0x88, 0x99)


def rec(item, expect, actual, verdict):
    res.append({"item": item, "expect": expect, "actual": actual, "verdict": verdict})
    print("%-6s %s | %s" % (verdict, item, str(actual)[:400]), flush=True)


def judge_row(boxes, caption_visible=None):
    """boxes: key→bounds. 웹: 로고→제목→파일→창→보기→윈도우→끌기 빈칸이 한 줄(중심 y 차이 16px 이하·상단 48px 이내)·x 순서 일치이고,
    캡션 버튼(최소·최대·닫기)은 보이지 않아야 정상(caption_visible 이 {키: 보임여부})."""
    need = ["logo", "title", "menu_file", "menu_window", "menu_view", "menu_win", "drag"]
    miss = [k for k in need if k not in boxes]
    if miss:
        return False, {"missing": miss}
    cy = {k: boxes[k]["y"] + boxes[k]["h"] / 2 for k in need}
    one_row = max(cy.values()) - min(cy.values()) <= 16 and max(cy.values()) <= 48
    xs = [boxes[k]["x"] for k in need]
    ordered = xs == sorted(xs)
    caps_hidden = not any((caption_visible or {}).values())
    return one_row and ordered and caps_hidden, {"one_row": one_row, "ordered": ordered, "caption_visible": caption_visible, "cy": cy, "x": xs}


def luminance_spread(img, box):
    """박스 안 픽셀의 밝기 최대-최소 차(텍스트가 배경과 구분되는지의 근사). 0~255."""
    l, t, r, b = [int(v) for v in (box["x"], box["y"], box["x"] + box["w"], box["y"] + box["h"])]
    crop = img.convert("L").crop((max(l, 0), max(t, 0), min(r, img.size[0]), min(b, img.size[1])))
    px = list(crop.getdata())
    return (max(px) - min(px)) if px else 0


def close_rgb(c, ref, tol=6):
    return all(abs(a - b) <= tol for a, b in zip(c[:3], ref))


def judge_mdi_bg(img, area):
    """MDI 영역 안쪽 4점(모서리에서 12% 안쪽)의 색이 #778899 인지."""
    pts = [(area["x"] + area["w"] * fx, area["y"] + area["h"] * fy) for fx, fy in ((0.12, 0.12), (0.88, 0.12), (0.12, 0.88), (0.88, 0.88))]
    cols = [img.convert("RGB").getpixel((int(x), int(y))) for x, y in pts]
    return all(close_rgb(c, MDI_BG) for c in cols), cols


def find_box(r, ids):
    for i in ids:
        n = r.call("ui.find", id=i)
        if n and n.get("bounds") and n.get("visible", True):
            return n["bounds"], i
    return None, None


def part_q14(page, r, Image):
    boxes = {}
    for k, ids in TITLE_IDS.items():
        b, _ = find_box(r, ids)
        if b:
            boxes[k] = b
    caps = {}
    for k, i in CAPTION_IDS.items():
        n = r.call("ui.find", id=i)
        caps[k] = bool(n and n.get("visible") and (n.get("bounds") or {}).get("w", 0) > 0)
    ok, info = judge_row(boxes, caps)
    if info.get("missing"):
        rec("Q14 제목줄 구성요소 찾기", "로고·제목·파일/창/보기/윈도우·끌기 빈칸", info, "FAIL(구성요소 못 찾음)")
    else:
        rec("Q14 한 줄 배치(로고→제목→메뉴 4종→끌기 빈칸) + 웹 캡션 버튼 숨김", "한 줄·순서 일치·캡션 버튼 없음", info, "PASS" if ok else "FAIL")
    shot = os.path.join(OUT, "q14_titlebar.png")
    page.screenshot(path=shot)
    if "logo" in boxes and "title" in boxes:
        img = Image.open(shot)
        sp = {k: luminance_spread(img, boxes[k]) for k in ("logo", "title")}
        rec("Q14 로고·제목이 배경과 구분(밝기 차)", "각 40 이상(근사 기준, PdBrushText 수정판에서 판정)", sp, "PASS" if all(v >= 40 for v in sp.values()) else "FAIL")
    # 끌기 빈칸 더블클릭·끌기는 브라우저 창에 해당 없음(OS 창 이동 불가) — 기록
    rec("Q14 빈 공간 끌기·더블클릭·Win11 스냅(웹)", "해당 없음(브라우저 창은 OS 가 관리)", "N/A", "차이-사유")


def part_q8(page, r, Image):
    r.call("docs.close", all=True)
    time.sleep(0.8)
    panels = r.call("panel.list") or []
    pb = r.call("panel.bounds") or {}
    area = pb.get("Documents") or ((next((p for p in panels if p.get("id") == "CenterPane"), None) or {}).get("bounds")) or (r.call("ui.find", id="shell.dock") or {}).get("bounds")
    shot = os.path.join(OUT, "q8_mdi_bg.png")
    page.screenshot(path=shot)
    if not area:
        rec("Q8·Q15 MDI 영역 bounds", "CenterPane 또는 shell.dock", None, "미검증")
        return
    ok, cols = judge_mdi_bg(Image.open(shot), area)
    rec("Q8·Q15 MDI 바탕 4점 #778899", "4점 모두 #778899(±6)", {"colors": cols, "area": area}, "PASS" if ok else "FAIL")
    # 관리형 창이 바깥 패널을 가리지 않는지: SDI 로 띄운 창 bounds 가 알림·탐색기 패널과 겹치면 기록
    ids = [x["id"] for x in r.call("docs.open", kind="grid", count=1)]
    time.sleep(1)
    r.call("docs.sdi", doc=ids[0], on=True)
    time.sleep(1.5)
    wl = r.call("windows.list") or []
    mw = next((w for w in wl if w.get("type") == "ManagedWindow"), None)
    pans = [(p.get("id"), p.get("bounds")) for p in panels if p.get("bounds")]
    page.screenshot(path=os.path.join(OUT, "q8_managed_vs_panels.png"))
    rec("Q8·Q15 관리형 창과 바깥 패널 가시성", "패널이 보임(스크린샷 판독) — 관리형 창 bounds 와 겹침 기록", {"managed": mw, "panels": pans}, "증거")


def main():
    only = set(sys.argv[sys.argv.index("--only") + 1].split(",")) if "--only" in sys.argv else set()
    from playwright.sync_api import sync_playwright
    from PIL import Image
    import retest_browser2 as t
    with sync_playwright() as p:
        b = p.chromium.launch(headless=True, args=t.ARGS)
        ctx, page, r = t.boot(b)
        r.call("docs.open", kind="grid", count=1)
        time.sleep(1.5)
        for name, fn in (("q14", part_q14), ("q8", part_q8)):
            if only and name not in only:
                continue
            try:
                fn(page, r, Image)
            except Exception as ex:
                rec("부분 %s 예외" % name, "예외 없음", repr(ex), "FAIL")
        with open(os.path.join(OUT, "retest_web6.json"), "w", encoding="utf-8") as f:
            json.dump(res, f, ensure_ascii=False, indent=2)
        b.close()


def selftest():
    """가짜 bounds·합성 이미지로 판정 논리만 시험한다."""
    from PIL import Image
    ok_boxes = {"logo": {"x": 8, "y": 6, "w": 18, "h": 18}, "title": {"x": 32, "y": 8, "w": 60, "h": 14}, "menu_file": {"x": 110, "y": 6, "w": 40, "h": 18},
                "menu_window": {"x": 155, "y": 6, "w": 40, "h": 18}, "menu_view": {"x": 200, "y": 6, "w": 40, "h": 18}, "menu_win": {"x": 245, "y": 6, "w": 50, "h": 18},
                "drag": {"x": 300, "y": 0, "w": 800, "h": 30}}
    cases = []
    cases.append(("한 줄 정상·캡션 숨김", judge_row(ok_boxes, {"min": False, "max": False, "close": False})[0] is True))
    cases.append(("웹인데 캡션 버튼이 보이면 거부", judge_row(ok_boxes, {"min": True, "max": False, "close": False})[0] is False))
    two_rows = dict(ok_boxes, menu_file={"x": 110, "y": 60, "w": 40, "h": 18})
    cases.append(("메뉴가 아래 줄이면 거부", judge_row(two_rows)[0] is False))
    swapped = dict(ok_boxes, menu_file={"x": 300, "y": 6, "w": 40, "h": 18})
    cases.append(("순서 어긋나면 거부", judge_row(swapped)[0] is False))
    cases.append(("요소 누락이면 거부", judge_row({k: v for k, v in ok_boxes.items() if k != "logo"})[0] is False))
    bg = Image.new("RGB", (400, 300), MDI_BG)
    cases.append(("MDI 바탕 정상", judge_mdi_bg(bg, {"x": 0, "y": 0, "w": 400, "h": 300})[0] is True))
    bad = Image.new("RGB", (400, 300), (255, 255, 255))
    cases.append(("MDI 바탕 흰색이면 거부", judge_mdi_bg(bad, {"x": 0, "y": 0, "w": 400, "h": 300})[0] is False))
    flat = Image.new("RGB", (100, 30), (30, 30, 30))
    cases.append(("대비 없음(단색)이면 0", luminance_spread(flat, {"x": 0, "y": 0, "w": 100, "h": 30}) == 0))
    txt = Image.new("RGB", (100, 30), (30, 30, 30))
    for x in range(10, 50):
        txt.putpixel((x, 15), (240, 240, 240))
    cases.append(("글자 있으면 대비 큼", luminance_spread(txt, {"x": 0, "y": 0, "w": 100, "h": 30}) >= 200))
    for name, okc in cases:
        print(("PASS " if okc else "FAIL ") + name)
    sys.exit(0 if all(o for _, o in cases) else 1)


if __name__ == "__main__":
    if "--selftest" in sys.argv:
        selftest()
    main()
