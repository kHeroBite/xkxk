# AC-014 판정기 — 배경 무늬·사용자 이미지(맞춤 5방식)가 작업 영역에만 그려지고 손상 파일은 none 으로 낮춰 알림 1회인지 캡처·원격 상태로 본다
"""
사용
  background_pattern_judge.py --info <격리>/remote-control.json [--away-state <pc_away.json>] --approved '<누가·언제>' --out <결과.json>
  background_pattern_judge.py --self-test   (본문은 background_pattern_judge_selftest.py). 기본 remote_only(원격 명령·렌더 캡처만 — PC 비움 검사 없음)
1) 무늬마다 decor=none 캡처와 비교: 작업 영역(문서 창 제외)은 차이 타일 ≥ 1, 문서 창 안은 0. 문턱은 pngdiff_lib.diff_stats 기본값.
2) 사용자 이미지 맞춤 5방식: 1) 과 같은 판정 + 맞춤끼리 서로 다름. 3) 손상 파일: decorEffective=none·fallbackCount +1·알림 1회.
판정 규칙 상세와 예외는 test/README.md 의 이 판정기 행. 끝나면 decor·이미지 선택·맞춤을 되돌리고 시험 이미지를 지운다. rc 는 judge_session 과 같다.
"""
import os
import shutil
import subprocess
import sys
import tempfile
import time
from collections import Counter

from PIL import Image, ImageDraw

import judge_session as session

HERE = os.path.dirname(os.path.abspath(__file__))
AREA = os.path.normpath(os.path.join(HERE, "..", "..", ".."))
sys.path.insert(0, os.path.join(AREA, "test", "perf", "measure"))
import pngdiff_lib  # noqa: E402

PATTERNS = ("dots", "aurora", "waves", "grid", "diagonal", "hex", "topo", "gradient")  # DesignThemes decor 값 중 none·image 를 뺀 무늬 전부
FITS = ("fill", "fit", "stretch", "tile", "center")
SETTLE = 1.2  # 장식 적용 뒤 렌더 대기(초)
EDGE = 2  # 문서 창 테두리 안티앨리어싱을 빼려고 안쪽으로 줄이는 픽셀
SHRINK = 0.5  # 문서 창을 작업 영역 폭·높이의 이 비율로 줄인다(면적 약 25%)
COVER_LIMIT = 0.7  # 문서 창이 작업 영역 면적의 이 비율 이상을 덮으면 INVALID(기준값 불변)
# 성긴 무늬 예외(team-lead 2026-11-25 승인): dots(2x2 점·24px 간격)는 50px 타일당 최대 약 20화소라 타일 규칙을 못 넘는다. 문턱은 그대로,
# '작업 영역 무변화'만 걸리면 덩어리 수 ≥ 기대 50% + 간격 최빈값 24×배율 ±1 로 다시 본다. 문서 창 안 타일 0 은 그대로 적용한다.
SPARSE = {"dots": 24}  # 무늬 → 무늬 간격(DIP)
SPARSE_MIN_RATIO = 0.5


def to_dec(img):
    """PIL RGB → pngdiff_lib 디코드 형식 (w, h, bpp, rows)."""
    img = img.convert("RGB")
    w, h = img.size
    raw = img.tobytes()
    return w, h, 3, [raw[y * w * 3:(y + 1) * w * 3] for y in range(h)]


def region_diff(a, b, rect, mask=None):
    """두 캡처의 rect(px) 안 차이 (화소 수, 타일 수). mask(px, 절대 좌표) 영역은 비교에서 뺀다."""
    ca, cb = a.crop(rect), b.crop(rect)
    if mask:
        rel = (mask[0] - rect[0], mask[1] - rect[1], mask[2] - rect[0], mask[3] - rect[1])
        for im in (ca, cb):
            ImageDraw.Draw(im).rectangle(rel, fill=(0, 0, 0))
    cnt, tiles = pngdiff_lib.diff_stats(to_dec(ca), to_dec(cb))
    return (cnt, len(tiles)) if cnt is not None else (None, None)


def shrink(rect, px=EDGE):
    return (rect[0] + px, rect[1] + px, rect[2] - px, rect[3] - px)


def judge_drawn(base, shot, work, doc):
    """작업 영역(문서 창 제외)은 바뀌고 문서 창 안은 그대로인가."""
    if base.size != shot.size:
        return {"verdict": "INVALID", "why": "캡처 크기가 다르다(창 크기 변화)", "sizes": [base.size, shot.size]}
    w_cnt, w_tiles = region_diff(base, shot, work, mask=doc)
    d_cnt, d_tiles = region_diff(base, shot, shrink(doc))
    if w_cnt is None or d_cnt is None:
        return {"verdict": "INVALID", "why": "캡처 크기가 다르다"}
    bad = {}
    if w_tiles < 1:
        bad["작업 영역 무변화"] = [w_cnt, w_tiles]
    if d_tiles > 0:
        bad["문서 창 안 변화"] = [d_cnt, d_tiles]
    return {"verdict": "FAIL" if bad else "PASS", "why": "작업 영역에만 그림", "work_diff": [w_cnt, w_tiles],
            "doc_diff": [d_cnt, d_tiles], "problems": bad}


def blobs(a, b, rect, mask):
    """rect 안(mask 제외) 채널 차 > 16 화소를 4방향 연결 덩어리로 묶어 중심 목록을 돌려준다(pngdiff_lib 과 같은 화소 문턱)."""
    pa, pb = a.load(), b.load()
    hot = {(x, y) for y in range(rect[1], rect[3]) for x in range(rect[0], rect[2])
           if not (mask[0] <= x < mask[2] and mask[1] <= y < mask[3]) and max(abs(pa[x, y][k] - pb[x, y][k]) for k in range(3)) > 16}
    centers = []
    while hot:
        stack, pts = [hot.pop()], []
        while stack:
            x, y = stack.pop()
            pts.append((x, y))
            for q in ((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)):
                if q in hot:
                    hot.remove(q)
                    stack.append(q)
        centers.append((sum(p[0] for p in pts) / len(pts), sum(p[1] for p in pts) / len(pts)))
    return centers


def spacing_mode(centers):
    """같은 줄(중심 y 반올림)·같은 열에서 이웃 덩어리 간격의 최빈값(px). 이웃이 없으면 None."""
    gaps = Counter()
    for axis in (0, 1):
        lines = {}
        for c in centers:
            lines.setdefault(round(c[1 - axis]), []).append(c[axis])
        for vals in lines.values():
            vals.sort()
            gaps.update(round(v2 - v1) for v1, v2 in zip(vals, vals[1:]))
    return gaps.most_common(1)[0][0] if gaps else None


def judge_sparse(base, shot, work, doc, tile, scale):
    """성긴 무늬 보조 판정. 타일 규칙 결과를 먼저 보고, '작업 영역 무변화'만 걸렸을 때만 덩어리 수·간격으로 다시 본다."""
    first = judge_drawn(base, shot, work, doc)
    if first["verdict"] != "FAIL" or set(first["problems"]) != {"작업 영역 무변화"}:
        return dict(first, sparse="미적용(타일 규칙 결과 그대로)")
    centers = blobs(base.convert("RGB"), shot.convert("RGB"), work, doc)
    area = (work[2] - work[0]) * (work[3] - work[1]) - (doc[2] - doc[0]) * (doc[3] - doc[1])
    expect = area / float(tile * scale) ** 2
    mode = spacing_mode(centers)
    bad = {}
    if len(centers) < SPARSE_MIN_RATIO * expect:
        bad["덩어리 부족"] = [len(centers), round(expect)]
    if mode is None or abs(mode - tile * scale) > 1:
        bad["간격 불일치"] = [mode, tile * scale]
    return dict(first, verdict="FAIL" if bad else "PASS", problems=bad,
                why="성긴 무늬 예외(team-lead 승인): 타일 규칙 대신 덩어리 수·간격으로 작업 영역 출현 판정, 문서 창 안 타일 0 유지",
                sparse={"blobs": len(centers), "expect": round(expect), "spacing_px": mode, "tile_px": tile * scale})


def judge_distinct(shots, work, doc):
    """맞춤끼리 작업 영역(문서 창 제외)이 서로 다른가. 같은 쌍이 있으면 FAIL."""
    names, same = list(shots), []
    for i, a in enumerate(names):
        for b in names[i + 1:]:
            if region_diff(shots[a], shots[b], work, mask=doc)[1] < 1:
                same.append([a, b])
    return {"verdict": "FAIL" if same else "PASS", "why": "맞춤 %d방식이 서로 다른 그림" % len(names), "same_pairs": same}


def judge_corrupt(before, after, notes_first, notes_again):
    bad = {}
    if after.get("decorEffective") != "none":
        bad["decorEffective"] = after.get("decorEffective")
    if (after.get("fallbackCount") or 0) != (before.get("fallbackCount") or 0) + 1:
        bad["fallbackCount"] = [before.get("fallbackCount"), after.get("fallbackCount")]
    if not after.get("lastFallback"):
        bad["lastFallback"] = after.get("lastFallback")
    if notes_first != 1:
        bad["알림 증가(첫 손상)"] = notes_first
    if notes_again != 0:
        bad["알림 증가(같은 파일 재손상)"] = notes_again
    return {"verdict": "FAIL" if bad else "PASS", "why": "손상 → none·알림 1회", "before": before, "after": after,
            "notes_first": notes_first, "notes_again": notes_again, "problems": bad}


def test_image(path):
    """가로세로가 작업 영역과 다른 작은 그림(맞춤마다 모양이 달라지게 줄무늬 + 그라데이션)."""
    im = Image.new("RGB", (320, 160))
    d = ImageDraw.Draw(im)
    for x in range(320):
        d.line([(x, 0), (x, 159)], fill=(40 + x * 200 // 320, 90, 220 - x * 180 // 320))
    for k in range(-160, 320, 40):
        d.line([(k, 0), (k + 160, 159)], fill=(250, 240, 60), width=8)
    im.save(path)


def stage_dir(args):
    """앱(Windows)이 읽을 수 있는 경로. 결과 폴더가 /mnt 아래면 그 옆, 아니면 영역 logs/bg_pattern_stage(NTFS)."""
    out = os.path.dirname(os.path.abspath(args.out or "."))
    base = out if out.startswith("/mnt/") else os.path.join(AREA, "logs")
    return tempfile.mkdtemp(prefix="bg_pattern_stage_", dir=base)


def find_type(nodes, typ):
    for node in nodes:
        if node.get("type") == typ and node.get("visible") and node["bounds"]["w"] > 0:
            return node
        hit = find_type(node.get("children", []), typ)
        if hit:
            return hit
    return None


def shrink_deltas(work_b, doc_b, frac=SHRINK):
    """문서 창을 작업 영역 폭·높이의 frac 배로 줄이는 오른쪽·아래 가장자리 이동량(DIP, 음수=줄임). 이미 작으면 0."""
    d_right = min(0, round(work_b["w"] * frac - doc_b["w"]))
    d_bottom = min(0, round(work_b["h"] * frac - doc_b["h"]))
    return d_right, d_bottom


def covers(work, doc, limit=COVER_LIMIT):
    """문서 창(px)이 작업 영역(px)의 limit 이상을 덮는가(덮으면 무늬를 볼 자리가 없다)."""
    area = lambda r: max(0, r[2] - r[0]) * max(0, r[3] - r[1])  # noqa: E731
    return area(doc) >= limit * area(work)


def locate(s):
    tree = s.call("tree.get", window=0, named=False, depth=400)["tree"]
    work_n, doc_n = find_type(tree, "MdiDocumentControl"), find_type(tree, "MdiDocumentWindow")
    if not work_n or not doc_n:
        raise session.Abort("INVALID", "작업 영역·문서 창 노드를 찾지 못했다(MDI 형인지 확인)")
    return work_n, doc_n


def judge(s):
    start = s.call("style.state")
    if start.get("locked"):
        raise session.Abort("INVALID", "회사 잠금이라 decor 를 바꿀 수 없다(잠금 없는 격리 폴더로 다시)")
    orig_decor = (start.get("overrides") or {}).get("decor", "")
    orig_bg = s.call("background.state")
    stage = stage_dir(s.args)
    cases = {}

    def notes():
        return int(s.call("notify.post", title="bg_judge_probe", message="count")["total"])

    try:
        s.call("docs.close", all=True)
        s.call("docs.open", kind="grid")
        s.call("wait.for", docCount=1, timeout=30000)
        s.call("docs.arrange", mode="cascade")
        time.sleep(2)
        work_n, doc_n = locate(s)  # 계단식만으로는 문서 창이 작업 영역을 그대로 덮을 수 있다(실측 912x631) — 비율로 줄인다
        doc_id = s.call("window.load.state")["docs"][0]["id"]
        d_right, d_bottom = shrink_deltas(work_n["bounds"], doc_n["bounds"])
        for edge, d in (("right", d_right), ("bottom", d_bottom)):
            if d:
                s.call("docs.resize", doc=doc_id, edge=edge, d=d)
        time.sleep(1)
        s.effect("decor", "none")
        time.sleep(SETTLE)
        base, scale = s.capture("decor_none")
        work_n, doc_n = locate(s)  # 줄인 뒤 실제 크기를 다시 읽는다
        work, doc = session.box(work_n["bounds"], scale), session.box(doc_n["bounds"], scale)
        if covers(work, doc):
            raise session.Abort("INVALID", "문서 창이 작업 영역을 거의 덮어 무늬를 볼 수 없다: work=%s doc=%s (줄이기 %s,%s)" % (work, doc, d_right, d_bottom))
        meta = {"work_px": work, "doc_px": doc, "scale": scale, "patterns": len(PATTERNS), "resize_dip": [d_right, d_bottom]}
        for name in PATTERNS:
            s.effect("decor", name)
            time.sleep(SETTLE)
            shot, _ = s.capture("decor_" + name)
            cases["pattern." + name] = judge_sparse(base, shot, work, doc, SPARSE[name], scale) if name in SPARSE else judge_drawn(base, shot, work, doc)
        img = os.path.join(stage, "bg_judge_test.png")
        test_image(img)
        win = subprocess.check_output(["wslpath", "-w", img], text=True).strip()
        imp = s.call("background.import", path=win)
        if not imp.get("ok"):
            raise session.Abort("INVALID", "background.import 실패: %s" % imp)
        fit_shots = {}
        for fit in FITS:
            s.call("background.set", fit=fit, opacity="1", dim="0")
            for _ in range(25):  # 디코드는 비동기 — ready 까지 최대 5초
                if s.call("background.state").get("ready"):
                    break
                time.sleep(0.2)
            time.sleep(SETTLE)
            fit_shots[fit], _ = s.capture("image_" + fit)
            cases["image." + fit] = judge_drawn(base, fit_shots[fit], work, doc)
        cases["image.fits_distinct"] = judge_distinct(fit_shots, work, doc)

        before, n0 = s.call("background.state"), notes()
        s.call("background.corrupt")
        time.sleep(2.5)
        after, n1 = s.call("background.state"), notes()
        s.call("background.corrupt")
        time.sleep(2.5)
        n2 = notes()
        cases["image.corrupt"] = judge_corrupt(before, after, n1 - n0 - 1, n2 - n1 - 1)
        s.capture("image_corrupt")
        cases["meta"] = dict(meta, verdict="PASS", why="측정 조건 기록(판정 아님)")
    finally:
        s.call("background.set", clear=True, fit=orig_bg.get("fit") or "fill")
        s.call("style.effect.set", key="decor", value=orig_decor or "")
        s.call("docs.close", all=True)
        shutil.rmtree(stage, ignore_errors=True)
    return cases


def self_test():
    """앱 없이 판정 함수 고정 확인. 본문은 300줄 한도로 같은 폴더 background_pattern_judge_selftest.py 에 있다."""
    import background_pattern_judge_selftest
    return background_pattern_judge_selftest.run()


def main():
    ap = session.parser(__doc__)
    ap.set_defaults(remote_only=True)  # 원격 명령·앱 렌더 캡처만 쓴다(실입력 없음) — PC 비움 검사 생략
    args = ap.parse_args()
    if args.self_test:
        return self_test()

    def body(s):
        s.args = args
        return judge(s)

    return session.run(args, "background_pattern_judge", body)


if __name__ == "__main__":
    sys.exit(main())
