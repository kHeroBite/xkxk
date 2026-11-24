#!/usr/bin/env python3
# K5 행 새 판 — 원본 dense_wheel_check.py 를 재사용해 표 캡처 픽셀로 grid_row_stripes(행 바탕 교대)·grid_selected_row_line(선택 행 위·아래 줄) 측정 이름을 만든다
"""
사용:
  dense_wheel_check_k5rows.py --live --run-dir <bin/run 복사본> --out 결과.json [--shots 폴더] [--min-idle 300] [--wait 7200] [--status 상태파일] [--real ...]
      원본 --live(같은 인자, 같은 안전 규칙: 임시 data·profile, 화면 밖·비전경, 자기 PID 만 종료)를 그대로 돌린 뒤
      원본이 남긴 캡처 grid_initial.png·grid_selected.png 를 읽어 두 측정을 결과 JSON(results)에 더하고 rc 를 다시 정한다.
  dense_wheel_check_k5rows.py --analyze <캡처 폴더> [--out 결과.json]   앱 없이 이미 있는 두 캡처만 판정한다.
  dense_wheel_check_k5rows.py --self-test                                합성 이미지로 판정 논리만 확인한다(앱·입력 없음).
rc: 원본과 같다(0 통과, 1 위반, 2 판정불가, 3 유휴 대기 초과·실입력 거부, 5 사용자 입력 감지). 원본 rc 가 0 이 아니면 그 값을 그대로 쓴다.

원본과의 차이(11-09, 원본 파일·sha 불변):
- 원본은 표 회귀에서 grid_initial(첫 화면)·grid_selected(grid.select index=2 뒤) 캡처를 찍지만 '눈 확인' 으로만 두고 측정 이름을 만들지 않았다.
- 판정 기준(정본 poc/ui/src/Pd.Ui.Views/Documents/PdDataGrid.cs·PdDataGridStyles.axaml, UI-07):
  grid_row_stripes: 행 바탕이 홀짝으로 두 색(PdBrushBackground·PdBrushGridStripe)을 번갈아 쓴다 → 캡처 각 줄의 최빈색으로 띠를 나눠
      서로 다른 두 색(채널 차 ≥ 5)이 같은 높이(±3px)로 6띠 이상 번갈아 나오면 통과.
  grid_selected_row_line: 선택 행은 위·아래 1px PdBrushSelectionLine 선만, 좌우 선 없음, 바탕은 원래색 그대로 →
      두 캡처의 차이가 가로 전폭 선 2묶음(행 한 칸 간격 ±3px)뿐이고, 선 두께가 1 DIP×추정 배율+1px 이하, 선 색이 바탕과 다르며(채널 차 ≥ 30),
      두 선 사이(행 안쪽) 변화가 2% 이하이고 세로로 이어진 변화 열(좌우 선)이 없으면 통과.
  배율은 띠 간격 ÷ 실현 행 높이 31 DIP(D-RH1 실측 약 31.1, RemoteScrollCommands 주석)로 추정한다. 추정이라 값(scale_est)을 함께 남긴다.
- 캡처가 없으면(원본이 --shots 없이 돌았거나 앞 단계 실패) 두 이름을 pass=None(판정불가)으로 남기고 rc 는 원본 규칙대로 2 로 둔다. 값을 만들지 않는다.
- 판정기 pcaway_gate.py 의 K5 계약은 files=[dense_wheel_check.py] 를 요구한다. 원본 sha 는 그대로이나, 결과의 script 기록이 이 판이면 판정기가 rc=2(판정불가)를 낼 수 있다.
"""
import argparse
import json
import os
import sys
import tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import dense_wheel_check as base  # noqa: E402

SELECTED_INDEX = 2  # 원본 regress_grid 의 grid.select index=2
MIN_BANDS, BAND_TOL, MIN_RUN, STRIPE_DIFF = 6, 3, 3, 5
LINE_DIFF, PIXEL_DIFF, LINE_FRACTION, INTERIOR_MAX, SIDE_FRACTION = 30, 8, 0.6, 0.02, 0.8
ROW_PITCH_DIP = 31.0  # 실현 행 Bounds 높이(D-RH1 실측 약 31.1). 배율 추정에만 쓴다


def _rgb(path):
    from PIL import Image
    with Image.open(path) as im:
        return im.convert("RGB").copy()


def _cdiff(a, b):
    return max(abs(x - y) for x, y in zip(a, b))


def row_modes(img):
    w, h = img.size
    px = img.load()
    out = []
    for y in range(h):
        counts = {}
        for x in range(w):
            c = px[x, y]
            counts[c] = counts.get(c, 0) + 1
        out.append(max(counts.items(), key=lambda kv: kv[1])[0])
    return out


def runs_of(modes):
    runs, start = [], 0
    for y in range(1, len(modes) + 1):
        if y == len(modes) or modes[y] != modes[start]:
            runs.append({"color": modes[start], "start": start, "len": y - start})
            start = y
    return [r for r in runs if r["len"] >= MIN_RUN]


def stripes(img):
    """가장 긴 두 색 교대 띠 묶음. 돌려주는 값의 bands 가 MIN_BANDS 이상이면 통과."""
    runs = runs_of(row_modes(img))
    best = []
    for i in range(len(runs)):
        seq = [runs[i]]
        for r in runs[i + 1:]:
            prev = seq[-1]
            if r["start"] - (prev["start"] + prev["len"]) > 2:
                break
            if len(seq) >= 2 and r["color"] != seq[-2]["color"]:
                break
            if r["color"] == prev["color"] or _cdiff(r["color"], prev["color"]) < STRIPE_DIFF:
                break
            seq.append(r)
        lens = sorted(s["len"] for s in seq[1:-1]) or sorted(s["len"] for s in seq)
        med = lens[len(lens) // 2]
        inner = seq[1:-1] if len(seq) > 2 else seq  # 맨 위·아래 띠는 화면 끝에서 잘릴 수 있어 높이 비교에서 뺀다
        if all(abs(s["len"] - med) <= BAND_TOL for s in inner) and len(seq) > len(best):
            best = seq
    if not best:
        return {"bands": 0, "pass": False}
    lens = sorted(s["len"] for s in best[1:-1]) or [best[0]["len"]]
    pitch = lens[len(lens) // 2]
    colors = sorted({"#%02X%02X%02X" % s["color"] for s in best})
    return {"bands": len(best), "colors": colors, "pitch_px": pitch, "start_y": best[0]["start"], "band_starts": [s["start"] for s in best][:40],
            "pass": len(best) >= MIN_BANDS and len(colors) == 2, "rule": "두 색 교대 %d띠 이상, 높이 ±%dpx, 채널 차 ≥ %d" % (MIN_BANDS, BAND_TOL, STRIPE_DIFF)}


def _mode_at(img, y):
    w = img.size[0]
    px = img.load()
    counts = {}
    for x in range(w):
        counts[px[x, y]] = counts.get(px[x, y], 0) + 1
    return max(counts.items(), key=lambda kv: kv[1])[0]


def selected_line(init, sel, pitch, bg_colors=None):
    """두 캡처 차이로 선택 행 줄을 판정한다. pitch = 띠 간격(px). bg_colors = 행 바탕 두 색('#RRGGBB' 목록, stripes 결과).
    첫 화면에 다른 행이 이미 선택돼 있어도 된다. 차이 묶음 중 선택 후 캡처에서 새로 선색이 된 묶음만 센다
    (첫 화면 줄 색이 바탕색이고 선택 후 줄 색이 바탕색이 아닌 묶음). 첫 화면에서 선색이던 묶음(이전 선택 해제)은 old_groups 로 따로 기록하고 개수에 넣지 않는다.
    두께·한 행 간격·색 구분·행 안쪽 바탕 유지·좌우 선 없음 기준은 그대로다."""
    w, h = init.size
    if sel.size != init.size:
        return {"pass": False, "reason": "두 캡처 크기가 다르다", "sizes": [init.size, sel.size]}
    a, b = init.load(), sel.load()
    changed = [[_cdiff(a[x, y], b[x, y]) > PIXEL_DIFF for x in range(w)] for y in range(h)]
    frac = [sum(r) / float(w) for r in changed]
    all_groups, cur = [], None
    for y, f in enumerate(frac):
        if f >= LINE_FRACTION:
            if cur and y == cur[1] + 1:
                cur[1] = y
            else:
                cur = [y, y]
                all_groups.append(cur)
    bgs = [tuple(int(c[i:i + 2], 16) for i in (1, 3, 5)) for c in (bg_colors or [])]

    def is_bg(c):
        return any(_cdiff(c, g) < STRIPE_DIFF for g in bgs)

    groups, old_groups = [], []
    for g in all_groups:
        y = (g[0] + g[1]) // 2
        if bgs and not is_bg(_mode_at(init, y)) and is_bg(_mode_at(sel, y)):
            old_groups.append(g)  # 첫 화면에서 선색이던 줄 = 이전 선택 해제
        else:
            groups.append(g)
    scale = (pitch / ROW_PITCH_DIP) if pitch else None
    t_max = int(round(scale)) + 1 if scale else 2
    out = {"line_groups": [list(g) for g in groups], "old_groups": [list(g) for g in old_groups], "scale_est": round(scale, 3) if scale else None,
           "thickness_max_px": t_max, "pitch_px": pitch}
    if len(groups) != 2:
        out["pass"] = False
        out["reason"] = "새로 선색이 된 가로 전폭 선 묶음이 2개가 아니다(%d)" % len(groups)
        return out
    (t0, t1), (b0, b1) = groups
    thick = [t1 - t0 + 1, b1 - b0 + 1]
    span = b1 + 1 - t0
    line_color = sel.getpixel((w // 2, t0))
    bg = init.getpixel((w // 2, t0))
    old_rows = {y for g in old_groups for y in range(g[0], g[1] + 1)}
    interior = [y for y in range(t1 + 1, b0) if y not in old_rows]
    interior_frac = max([frac[y] for y in interior] or [0.0])
    side_cols = []
    if len(interior) > 0:
        for x in range(w):
            if sum(changed[y][x] for y in interior) >= SIDE_FRACTION * len(interior):
                side_cols.append(x)
    out.update(thickness_px=thick, span_px=span, line_color="#%02X%02X%02X" % line_color, background="#%02X%02X%02X" % bg,
               interior_changed_max=round(interior_frac, 4), side_columns=side_cols[:10],
               checks={"thickness": all(1 <= t <= t_max for t in thick), "one_row": bool(pitch) and abs(span - pitch) <= BAND_TOL,
                       "color_distinct": _cdiff(line_color, bg) >= LINE_DIFF, "background_kept": interior_frac <= INTERIOR_MAX, "no_side_lines": not side_cols})
    out["pass"] = all(out["checks"].values())
    return out


def analyze(shots):
    init_p, sel_p = os.path.join(shots, "grid_initial.png"), os.path.join(shots, "grid_selected.png")
    if not (os.path.exists(init_p) and os.path.exists(sel_p)):
        note = "캡처 없음(grid_initial.png·grid_selected.png) — 판정불가"
        return [{"name": "grid_row_stripes", "pass": None, "note": note}, {"name": "grid_selected_row_line", "pass": None, "note": note}]
    init, sel = _rgb(init_p), _rgb(sel_p)
    st = stripes(init)
    ln = selected_line(init, sel, st.get("pitch_px"), st.get("colors"))
    ln["selected_index"] = SELECTED_INDEX
    if st.get("band_starts"):
        ln["band_of_top_line"] = next((i for i, s in enumerate(st["band_starts"]) if ln.get("line_groups") and s <= ln["line_groups"][0][0] < s + st["pitch_px"]), None)
    return [dict(st, name="grid_row_stripes", source="grid_initial.png", tool="dense_wheel_check_k5rows"),
            dict(ln, name="grid_selected_row_line", source="grid_initial.png+grid_selected.png", tool="dense_wheel_check_k5rows")]


def merge(out_path, rows, base_rc):
    doc = {"rc": base_rc, "results": []}
    if out_path and os.path.exists(out_path):
        with open(out_path, encoding="utf-8") as f:
            doc = json.load(f)
    doc["results"] = [r for r in doc.get("results", []) if r.get("name") not in {x["name"] for x in rows}] + rows
    rc = base_rc
    if base_rc == 0:
        rc = 2 if any(r["pass"] is None for r in rows) else (1 if any(r["pass"] is False for r in rows) else 0)
    doc["rc"], doc["k5rows"] = rc, {"base_rc": base_rc, "tool": os.path.abspath(__file__)}
    if out_path:
        with open(out_path, "w", encoding="utf-8") as f:
            json.dump(doc, f, ensure_ascii=False, indent=1)
    for r in rows:
        print(("OK  " if r["pass"] else ("?   " if r["pass"] is None else "FAIL")), json.dumps(r, ensure_ascii=False)[:600])
    return rc


def _synth(rows=12, pitch=31, header=30, w=400, stripe=(0xF5, 0xF5, 0xF5), sel=None, fill=None, side=False, thick=1, lines=True):
    from PIL import Image, ImageDraw
    im = Image.new("RGB", (w, header + rows * pitch), (0xF0, 0xF0, 0xF0))
    d = ImageDraw.Draw(im)
    for i in range(rows):
        y = header + i * pitch
        d.rectangle([0, y, w - 1, y + pitch - 1], fill=(255, 255, 255) if i % 2 == 0 else stripe)
        d.rectangle([20, y + 10, 60 + (i * 7) % 40, y + 18], fill=(0, 0, 0))  # 글자 흉내
        if sel == i:
            if fill:
                d.rectangle([0, y + thick, w - 1, y + pitch - 1 - thick], fill=fill)
                d.rectangle([20, y + 10, 60 + (i * 7) % 40, y + 18], fill=(0, 0, 0))
            if lines:
                d.rectangle([0, y, w - 1, y + thick - 1], fill=(0x40, 0x56, 0x8D))
                d.rectangle([0, y + pitch - thick, w - 1, y + pitch - 1], fill=(0x40, 0x56, 0x8D))
            if side:
                d.rectangle([0, y, 0, y + pitch - 1], fill=(0x40, 0x56, 0x8D))
                d.rectangle([w - 1, y, w - 1, y + pitch - 1], fill=(0x40, 0x56, 0x8D))
    return im


def self_test():
    fails = []

    def expect(name, ok):
        print("OK  " if ok else "FAIL", name)
        if not ok:
            fails.append(name)

    init = _synth()
    st = stripes(init)
    expect("줄무늬 12띠·두 색·간격 31 통과", st["pass"] and st["bands"] == 12 and st["pitch_px"] == 31 and st["colors"] == ["#F5F5F5", "#FFFFFF"])
    expect("줄무늬 없음(한 색)은 FAIL", stripes(_synth(stripe=(255, 255, 255)))["pass"] is False)
    expect("색 차가 기준 미만(채널 차 2)은 FAIL", stripes(_synth(stripe=(0xFD, 0xFD, 0xFD)))["pass"] is False)
    expect("5띠뿐이면 FAIL", stripes(_synth(rows=5))["pass"] is False)
    good = selected_line(init, _synth(sel=SELECTED_INDEX), 31)
    expect("선택 행 위·아래 1px 선 통과", good["pass"] and good["thickness_px"] == [1, 1] and good["line_color"] == "#40568D" and good["span_px"] == 31)
    pre = _synth(sel=0)
    pre_st = stripes(pre)
    pc = pre_st.get("colors")
    expect("첫 화면에 다른 행(index 0)이 이미 선택돼도 줄무늬 통과", pre_st["pass"])
    pre_ok = selected_line(pre, _synth(sel=SELECTED_INDEX), 31, pc)
    expect("첫 화면에 다른 행 선택: 새 선 2묶음만 세고 통과·이전 선택 묶음 2개 기록", pre_ok["pass"] and len(pre_ok["line_groups"]) == 2 and len(pre_ok["old_groups"]) == 2 and pre_ok["line_groups"][0][0] == 30 + 2 * 31)
    expect("첫 화면에 다른 행 선택 + 새 선택에 좌우 선이 있으면 FAIL", selected_line(pre, _synth(sel=SELECTED_INDEX, side=True), 31, pc)["pass"] is False)
    expect("첫 화면에 다른 행 선택 + 새 선 없음은 FAIL", selected_line(pre, _synth(sel=SELECTED_INDEX, lines=False), 31, pc)["pass"] is False)
    expect("첫 화면에 다른 행 선택 + 새 선 두께 4px 는 FAIL", selected_line(pre, _synth(sel=SELECTED_INDEX, thick=4), 31, pc)["pass"] is False)
    expect("선 없음은 FAIL", selected_line(init, _synth(sel=SELECTED_INDEX, lines=False), 31)["pass"] is False)
    expect("바탕이 선택색으로 칠해지면 FAIL", selected_line(init, _synth(sel=SELECTED_INDEX, fill=(0xCC, 0xE8, 0xFF)), 31)["pass"] is False)
    expect("좌우 선이 있으면 FAIL", selected_line(init, _synth(sel=SELECTED_INDEX, side=True), 31)["pass"] is False)
    expect("선 두께 4px(배율 1 추정)은 FAIL", selected_line(init, _synth(sel=SELECTED_INDEX, thick=4), 31)["pass"] is False)
    expect("크기 다른 캡처는 FAIL", selected_line(init, _synth(rows=11), 31)["pass"] is False)
    with tempfile.TemporaryDirectory(prefix="pd_k5rows_") as d:
        rows = analyze(d)
        expect("캡처 없음은 판정불가(None)", [r["pass"] for r in rows] == [None, None])
        out = os.path.join(d, "r.json")
        with open(out, "w", encoding="utf-8") as f:
            json.dump({"rc": 0, "results": [{"name": "grid_first_row_height", "pass": True}]}, f)
        expect("원본 rc=0 + 판정불가 → rc=2", merge(out, rows, 0) == 2)
        init.save(os.path.join(d, "grid_initial.png"))
        _synth(sel=SELECTED_INDEX).save(os.path.join(d, "grid_selected.png"))
        rows = analyze(d)
        expect("캡처 두 장 판정 통과·선 위치 = 선택 행(띠 3번째, 머리글 제외 시 index 2)", all(r["pass"] for r in rows) and rows[1].get("band_of_top_line") == SELECTED_INDEX)
        expect("원본 rc=0 + 통과 → rc=0, 원본 결과 보존", merge(out, rows, 0) == 0 and len(json.load(open(out, encoding="utf-8"))["results"]) == 3)
        expect("원본 rc=5 는 그대로", merge(out, rows, 5) == 5)
    expect("원본 모듈 재사용(live·self_test 존재)", callable(base.live) and callable(base.self_test))
    print("자가검사 실패 %d건" % len(fails) if fails else "self-test ok")
    return 1 if fails else 0


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--live", action="store_true")
    ap.add_argument("--real", action="store_true", help="원본과 같다(--live 뒤 실제 OS 휠·수식키 측정)")
    ap.add_argument("--self-test", action="store_true")
    ap.add_argument("--analyze", help="이미 있는 캡처 폴더(grid_initial.png·grid_selected.png)만 판정")
    ap.add_argument("--monitor", default="DISPLAY3")
    ap.add_argument("--user-pid", type=int, default=None)
    ap.add_argument("--run-dir")
    ap.add_argument("--out")
    ap.add_argument("--shots", help="캡처 복사 폴더. 생략하면 --out 옆 <out>.shots(없으면 임시 폴더)에 남긴다 — 두 측정은 캡처가 있어야 한다")
    ap.add_argument("--status")
    ap.add_argument("--min-idle", type=int, default=300)
    ap.add_argument("--wait", type=int, default=7200)
    a = ap.parse_args()
    if a.self_test:
        return self_test()
    if a.analyze:
        return merge(a.out, analyze(a.analyze), 0)
    if a.real and not a.live:
        ap.error("--real 은 --live 와 함께 쓴다")
    if not a.live:
        ap.error("--live, --analyze, --self-test 중 하나를 준다(정적 모드는 원본 dense_wheel_check.py)")
    if not a.shots:
        a.shots = (a.out + ".shots") if a.out else tempfile.mkdtemp(prefix="pd_k5rows_shots_")
    base_rc = base.live(a)
    return merge(a.out, analyze(a.shots), base_rc)


if __name__ == "__main__":
    sys.exit(main())
