#!/usr/bin/env python3
# retest_r2_titlebar.py 의 T3 에 '제목 탭 확대 캡처(좌표 계산 수정)'를 더한 새 판 — 원본은 pcaway_gate 가 sha 를 대조하므로 건드리지 않고 이 파일이 T3 만 바꿔 원본 main 을 부른다
"""
11-08 새 판 사유: 원본 T3 의 t3_tab_zoom 은 확대가 아니라 창 전체 캡처(cgrab_*_<i>.png)라, 판독자가 보는 '_1' 번호 파일이 제목 탭이 아닌 다른 최상위 창(로그 패널)이었다.
이 판은 메인 창 클라이언트 크기와 같은 캡처를 고르고, 제목 탭 bounds(DIP, 클라이언트 기준)를 dpi/96 배율로 물리 좌표로 바꿔 잘라 4배 확대한 t3_tab_zoom_crop.png·t3_strip_crop.png 를 따로 남긴다.
사용법·인자는 retest_r2_titlebar.py 와 같다(--items T1,T3 ...). 기존 결과(원본 스크립트로 잰 것)는 이 판으로 재측정해야 확대 캡처가 생긴다.
"""
import glob
import os
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import retest_r2_titlebar as base  # noqa: E402
import retest_real_input as ri  # noqa: E402

SCALE_UP = 4
MARGIN_PX = 6


def crop_box(bounds, dpi, img_w, img_h, margin=MARGIN_PX):
    """DIP bounds{x,y,w,h}(클라이언트 기준) → 이미지 안으로 맞춘 물리 픽셀 (l,t,r,b). 이미지 밖이거나 크기 0 이면 None."""
    if not bounds or not bounds.get("w") or not bounds.get("h"):
        return None
    s = dpi / 96.0
    l = int(bounds["x"] * s) - margin
    t = int(bounds["y"] * s) - margin
    r = int((bounds["x"] + bounds["w"]) * s) + margin
    b = int((bounds["y"] + bounds["h"]) * s) + margin
    l, t, r, b = max(l, 0), max(t, 0), min(r, img_w), min(b, img_h)
    return (l, t, r, b) if r - l > 0 and b - t > 0 else None


def zoom_capture(rig, res, tag, bounds):
    """rig.grab 직후 호출. 메인 창 클라이언트와 같은 크기의 캡처를 골라 bounds 를 잘라 확대 저장하고 캡처로 등록한다. 못 찾으면 notes 에 사유를 남기고 None."""
    from PIL import Image
    win = rig.win(rig.main) or {}
    c = win.get("client") or {}
    want = (c.get("r", 0) - c.get("l", 0), c.get("b", 0) - c.get("t", 0))
    for path in sorted(glob.glob(os.path.join(res.shots, "cgrab_%s_*.png" % tag))):
        with Image.open(path) as im:
            if im.size != want:
                continue
            box = crop_box(bounds, win.get("dpi", 96), im.width, im.height)
            if not box:
                break
            out = os.path.join(res.shots, tag + "_crop.png")
            im.crop(box).resize(((box[2] - box[0]) * SCALE_UP, (box[3] - box[1]) * SCALE_UP), Image.NEAREST).save(out)
            res.capture(out)
            return out
    res.notes.append({"zoom_capture_failed": tag, "want_size": want, "bounds": bounds})
    return None


def t3(rig, res):
    a, b = ri.open_docs(rig, count=2, title="한글 gjpq 제목 탭 시험")
    h_sdi = ri.make_sdi(rig, b)
    rig.r.call("docs.activate", doc=a)
    time.sleep(0.4)
    rig.acquire(rig.main)
    rig.grab(res, "t3_strip")
    strip_h, tab_h = base.height_of(rig, base.STRIP), base.height_of(rig, base.TAB_PREFIX + a)
    zoom_capture(rig, res, "t3_strip", base.vis_bounds(rig, base.STRIP))
    if res.args.phase == "discovery":
        before = {"strip": strip_h, "tab": tab_h, "source": "now"}
    else:
        before = base.before_heights(res.args)
    res.add("strip_height_before", {"h": before.get("strip"), "source": before.get("source"), "run_id": before.get("run_id")}, isinstance(before.get("strip"), (int, float)) and before["strip"] > 0)
    res.add("tab_height_before", {"h": before.get("tab"), "source": before.get("source")}, isinstance(before.get("tab"), (int, float)) and before["tab"] > 0)
    sr, s_ok = base.ratio_ok(strip_h, before.get("strip"))
    tr, t_ok = base.ratio_ok(tab_h, before.get("tab"))
    if res.args.phase == "discovery":
        s_ok = t_ok = False
    res.add("strip_ratio", {"now": strip_h, "before": before.get("strip"), "ratio": sr, "range": [base.RATIO_MIN, base.RATIO_MAX]}, s_ok)
    res.add("tab_ratio", {"now": tab_h, "before": before.get("tab"), "ratio": tr, "range": [base.RATIO_MIN, base.RATIO_MAX]}, t_ok)
    res.measure("menu_bar_32_unchanged", lambda: ({"h": base.height_of(rig, base.TITLE_BAR), "want": base.MENU_H}, abs((base.height_of(rig, base.TITLE_BAR) or 0) - base.MENU_H) <= base.TOL))

    def clipped():
        tab = base.find_node(rig, base.TAB_PREFIX + a)
        texts = [n for n in base.subtree(tab) if n.get("type") in base.TITLE_TEXT_TYPES and n.get("effectivelyVisible") and n.get("bounds")]
        box = (tab or {}).get("bounds")
        rows = [{"type": n["type"], "bounds": n["bounds"]} for n in texts]
        return {"tab": box, "texts": rows}, bool(box and texts) and all(base.inside(n["bounds"], box) for n in texts)
    res.measure("tab_text_not_clipped", clipped)
    rig.grab(res, "t3_tab_zoom")
    tab_node = base.find_node(rig, base.TAB_PREFIX + a) or {}
    zoom_capture(rig, res, "t3_tab_zoom", tab_node.get("bounds"))

    def fits():
        icon, bar = base.vis_bounds(rig, base.ICON_PREFIX + b), base.vis_bounds(rig, base.STRIP)
        return {"icon": icon, "strip": bar, "sdi_hwnd": h_sdi}, bool(icon and bar) and base.inside(icon, bar) and icon["h"] < bar["h"]
    res.measure("sdi_icon_fits_strip", fits)


base.ITEMS["T3"] = t3
_orig_self_test = base.self_test


def self_test(args):
    rc = _orig_self_test(args)
    fails = []
    if crop_box({"x": 10, "y": 2, "w": 100, "h": 24}, 96, 800, 600) != (4, 0, 116, 32):
        fails.append("crop_box 기본")
    if crop_box({"x": 10, "y": 2, "w": 100, "h": 24}, 192, 800, 600) != (14, 0, 226, 58):
        fails.append("crop_box dpi 192 배율")
    if crop_box({"x": 780, "y": 590, "w": 100, "h": 40}, 96, 800, 600) != (774, 584, 800, 600):
        fails.append("crop_box 이미지 경계 맞춤")
    if crop_box({"x": 900, "y": 10, "w": 10, "h": 10}, 96, 800, 600) is not None or crop_box(None, 96, 800, 600) is not None:
        fails.append("crop_box 밖/None")
    for f in fails:
        print("FAIL", f)
    print("self-test(crop) %s, 실패 %d건" % ("PASS" if not fails else "FAIL", len(fails)))
    return 1 if (rc or fails) else 0


def main():
    # 결과의 script{path,sha256} 가 이 파일을 가리켜야 하므로 원본 main 을 부르지 않고 같은 인자로 직접 구동한다
    import argparse
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ri.add_common_args(p)
    p.add_argument("--before-strip", type=float, default=None)
    p.add_argument("--before-tab", type=float, default=None)
    args = p.parse_args()
    if args.self_test:
        return self_test(args)
    if args.annotate:
        return ri.annotate(args)
    return ri.drive(args, base.ITEMS, base.REQ, "desktop_work", os.path.abspath(__file__))


if __name__ == "__main__":
    sys.exit(main())
