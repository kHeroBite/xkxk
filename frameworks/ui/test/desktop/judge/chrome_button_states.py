#!/usr/bin/env python3
# 문서 창 기능 버튼 7개의 간격과 상태 표현 판정 — 그림 사이 빈 공간과 기본·호버·켜짐 상태 대비를 내장 테마 전부에서 재고 기준 미달이면 rc=1 로 끝낸다
"""
사용:
  python3 -B chrome_button_states.py --run-dir <bin/run/복사본> --out <결과.json> --shots <캡처 폴더> [--base-gap 수정 전 빈 공간] [--monitor DISPLAY3] [--user-pid N]
  python3 -B chrome_button_states.py --static <BuiltinThemes.cs>   앱 없이 내장 테마 값으로 상태 대비만 계산한다
  python3 -B chrome_button_states.py --self-test
종료 코드: 0=통과, 1=기준 미달, 2=기동·연결 실패, 3=유휴 부족 또는 --activate 인데 사용자 Pd 를 확정 못 해 기동 안 함, 5=시험 중 입력 감지.
안전: 새 임시 data·profile 로 앱 1개만 비전경(--bg-pos)으로 띄우고 자기 PID 만 종료한다. 실입력을 보내지 않는다. 기동 전 유휴 --min-idle 초(기본 300)를 확인하고, 시험 중 입력이 있으면 멈춘다. 켜짐은 원격 ui.toggle, 호버는 진단 명령 ui.pseudo(의사 클래스 강제)로 만든다. 그 명령이 없는 판본은 호버 실측을 건너뛴다.
간격: 캡처에서 인접 버튼의 그림(바탕과 다른 픽셀) 사이 빈 공간을 잰다. --base-gap 이 있으면 그 값의 1/2(±1px)인지, 없으면 값만 기록하고 FAIL 로 둔다(수정 전 기록용).
상태 대비(임시, 05_창크롬·01 §4.2 에 수치 없음. WCAG 2.1 SC 1.4.11 인접 색 3:1 준용):
  활성·항상 위 바탕: 기본↔호버 3.0, 기본↔켜짐 3.0, 켜짐↔호버 바탕 3.0, 켜짐↔켜짐+호버 테두리 3.0, 그림 대 자기 바탕 3.0.
  새로고침 적중률 색 4개(0%·낮음·높음·100%) 대 활성 제목줄 3.0, 표면색 제목줄(강조 끔·macOS 스타일) 3.0, 그 비활성 2.0(PD 판단값).
  캡션 3버튼은 강조 띠·표면색·메인 타이틀바 바탕에서 호버 테두리·누름·닫기를 3.0 으로 잰다. 규칙은 chrome_state_rules.py 에 있고 토큰 계산이며, 앱 실측은 Windows 스타일 MDI 만 한다.
  비활성 바탕(요소 투명도 반영): 기본↔켜짐 2.0 이상은 PD 판단값이다(문서 창 비활성 토큰은 Mars 실측 회색이라 바꾸지 않았다).
"""
import argparse
import json
import os
import shutil
import sys
import tempfile
import time

HERE = os.path.dirname(os.path.abspath(__file__))
MEASURE = os.path.join(HERE, "..", "measure")
sys.path.insert(0, HERE)
from chrome_state_rules import MIN_GLYPH_DIST, MIN_INACTIVE_ON, MIN_STATE, cache_fails, cache_row, floor2, inactive_hover_fails, inactive_hover_ratio, model, model_fails, old_weakness, on_glyph_vs_off_glyph, over, ratio, rules_self_test, tc  # noqa: E402

BUTTONS = ["chrome.btnSdi", "chrome.btnPin", "chrome.btnLock", "chrome.btnSync", "chrome.btnDup", "chrome.btnCache", "chrome.btnHelp"]
TOGGLES = BUTTONS[:5]
INK_TOL, GAP_TOL = 40, 1.0


def ink_runs(row, bg, tol=INK_TOL):
    """가로 픽셀 줄들에서 바탕과 다른 열의 범위를 (시작, 끝) 목록으로 돌려준다."""
    width = len(row[0])
    cols = [any(sum(abs(p[i] - bg[i]) for i in range(3)) > tol for p in (line[x] for line in row)) for x in range(width)]
    runs, start = [], None
    for x, on in enumerate(cols + [False]):
        if on and start is None:
            start = x
        elif not on and start is not None:
            runs.append((start, x - 1))
            start = None
    return runs


def glyph_pixel(image, rect, bg):
    """단추 영역에서 바탕(bg)과 RGB 거리가 가장 먼 화소 = 기호 색(가장 굵은 획 중심). 꺼짐·켜짐을 같은 방법으로 잰다."""
    x0, y0, x1, y1 = rect
    best, best_d = bg, -1
    for y in range(max(0, y0), min(image.height, y1)):
        for x in range(max(0, x0), min(image.width, x1)):
            p = image.getpixel((x, y))[:3]
            d = sum(abs(p[i] - bg[i]) for i in range(3))
            if d > best_d:
                best, best_d = p, d
    return best


def self_test():
    colors = {"PdBrushMdiTitleText": "#FFFFFF", "PdBrushMdiTitleButtonHover": "#33FFFFFF", "PdBrushMdiTitleBar": "#40568D", "PdBrushMdiTitleBarPinned": "#800000", "PdBrushSurface": "#FFFFFF", "PdBrushText": "#000000", "PdBrushCaptionHover": "#1A000000", "PdBrushMdiTitleBarInactive": "#808080"}
    rows = model(colors)
    assert rows["active"]["rest_on"] >= 3 and not model_fails(rows), model_fails(rows)
    flat = dict(colors, PdBrushMdiTitleText="#40568D")
    assert model_fails(model(flat)), "진양성: 그림색이 바탕과 같으면 잡아야 한다"
    assert over("#80FFFFFF", (0, 0, 0)) == (128, 128, 128)
    b, i = (0, 0, 0), (255, 255, 255)
    assert ink_runs([[b, i, i, b, b, i, b]], b) == [(1, 2), (5, 5)]
    assert floor2(4.4999) == 4.49
    from PIL import Image
    probe = Image.new("RGB", (6, 6), (10, 10, 10))
    probe.putpixel((3, 3), (255, 224, 102))
    assert glyph_pixel(probe, (0, 0, 6, 6), (10, 10, 10)) == (255, 224, 102)
    rules_self_test()
    print("self-test ok")
    return 0


def static_check(source, defs):
    """앱 없이 소스의 토큰 표로 상태 대비를 계산한다(빌드 전 값 확정용)."""
    import re
    keys = re.findall(r'"(PdBrush\w+)"', open(defs, encoding="utf-8").read().split("KeyList =")[1].split("public static void FillMissing")[0])
    table = open(source, encoding="utf-8").read().split("Table =")[1]
    code, out = 0, []
    for m in re.finditer(r'"theme\.builtin\.([\w-]+)", ThemeBase\.\w+,(.*?)\)\s*,\s*\n', table, re.S):
        colors = dict(zip(keys, "".join(re.findall(r'"([^"]*)"', m.group(2))).split(",")))
        rows = model(colors)
        fails = model_fails(rows) + cache_fails(colors) + inactive_hover_fails(colors)
        code = code or (1 if fails else 0)
        out.append({"theme": m.group(1), "model": rows, "cache": cache_row(colors), "inactive_hover": inactive_hover_ratio(colors), "before_weakness": old_weakness(colors), "fails": fails})
        print("INACTIVE_HOVER", m.group(1), "%.2f" % inactive_hover_ratio(colors), flush=True)
        print(m.group(1), "PASS" if not fails else "FAIL", "cache", cache_row(colors), {k: {x: v[x] for x in ("rest_hover", "rest_on", "on_hover_fill", "on_onhover_border", "glyph_on_hover", "glyph_on_on")} for k, v in rows.items() if v}, fails, flush=True)
    return code, out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--self-test", action="store_true")
    ap.add_argument("--static")
    ap.add_argument("--run-dir")
    ap.add_argument("--out")
    ap.add_argument("--shots")
    ap.add_argument("--base-gap", type=float)
    ap.add_argument("--monitor", default="DISPLAY3")
    ap.add_argument("--user-pid", type=int, default=None, help="--activate 때 생략하면 실행 경로로 찾고, 찾지 못하면 거부한다")
    ap.add_argument("--activate", action="store_true")
    ap.add_argument("--min-idle", type=int, default=300, help="기동 전 필요한 사용자 유휴(초). 0 이면 보지 않는다")
    args = ap.parse_args()
    if args.self_test:
        return self_test()
    if args.static:
        return static_check(args.static, os.path.join(os.path.dirname(args.static), "ThemeDefinition.cs"))[0]
    if not (args.run_dir and args.out and args.shots):
        ap.error("--run-dir --out --shots 가 필요하다")
    self_test()
    sys.path.insert(0, MEASURE)
    import mdi_drag_app as app
    import retest_real_input as ri
    import user_away_guard as away
    from PIL import Image

    if args.activate and not ri.resolve_user_pid(args):
        return 3
    watch = tc.IdleWatch(away.read_probe, args.min_idle)
    if not watch.start():
        print("유휴 부족 — 기동하지 않음 rc=3", flush=True)
        return 3

    class QuietRig(ri.Rig):
        def acquire(self, hwnd):
            return {"hwnd": 0, "pid": 0}

    base_tmp = os.popen("wslpath -u '%s'" % app.ps("[IO.Path]::GetTempPath()").strip()).read().strip()
    work = tempfile.mkdtemp(prefix="pd_chromebtn_", dir=base_tmp)
    rig = QuietRig(args, work)
    report, code = {"themes": []}, 2
    try:
        try:
            work_area = rig.monitor["work"]
            rig.start(extra=("--bg-pos", "%d,%d" % (work_area["r"] - ri.MAIN_W - 180, work_area["t"] + 150)))
        except SystemExit as e:
            print("기동 실패:", e, flush=True)
            return 2
        os.makedirs(args.shots, exist_ok=True)
        doc = ri.open_docs(rig, count=1)[0]
        time.sleep(1.0)

        def find(cid, **kw):
            return rig.r.call("ui.find", id=cid, doc=doc, **kw)

        def shot(name):
            path = os.path.join(work, name + ".png")
            rig.r.call("screenshot", window=0, path=app.win(path))
            shutil.copy(path, os.path.join(args.shots, name + ".png"))
            return Image.open(path).convert("RGB")

        def set_toggles(on):
            for cid in TOGGLES:
                try:
                    rig.r.call("ui.toggle", id=cid, doc=doc, on=on)
                except Exception as e:  # 제목줄 상태와 묶인 토글이 거부해도 측정을 이어 간다. 결과의 fails 로 드러난다
                    print("토글 거부", cid, e, flush=True)
            time.sleep(0.4)

        pseudo_ok = True
        themes = rig.r.call("themes.get")
        themes = json.loads(themes) if isinstance(themes, str) else themes
        for theme in themes["builtinThemes"]:
            watch.check()
            rig.r.call("theme.select", id=theme)
            time.sleep(0.6)
            export = os.path.join(work, theme + ".json")
            rig.r.call("theme.export", path=app.win(export), id=theme)
            with open(export, encoding="utf-8-sig") as f:
                data = json.load(f)
            colors = data.get("Colors") or data.get("colors")
            rows, fails = model(colors), []
            fails += model_fails(rows) + cache_fails(colors) + inactive_hover_fails(colors)
            set_toggles(False)
            cache_fg = tc.rgb_of(tc.prop(find(BUTTONS[5], props="Foreground"), "Foreground"))
            cache_live = ratio(cache_fg, tc.rgb_of(colors["PdBrushMdiTitleBar"])) if cache_fg else None
            if cache_live is None or cache_live < MIN_STATE:
                fails.append("실측 새로고침 그림색 대비 %s < %.1f" % (cache_live, MIN_STATE))
            nodes = [find(c, props="Width,IsChecked") for c in BUTTONS]
            bounds = [n["bounds"] for n in nodes]
            img = shot(theme + "_rest")
            scale = img.width / (rig.r.call("ui.find", id="shell.titleBar")["bounds"]["w"] or 1)
            box = lambda b: (int(b["x"] * scale), int(b["y"] * scale), int((b["x"] + b["w"]) * scale), int((b["y"] + b["h"]) * scale))

            def px(b, fx, fy, image):
                x0, y0, x1, y1 = box(b)
                return image.getpixel((min(image.width - 1, int(x0 + (x1 - x0) * fx)), min(image.height - 1, int(y0 + (y1 - y0) * fy))))

            target = bounds[4]  # 중복 허용: 문서 상태와 묶이지 않아 측정 대상으로 쓴다
            rest_bar = px(target, 0.12, 0.5, img)
            x0, y0, x1, y1 = box(bounds[0])[0], box(bounds[0])[1], box(bounds[-1])[2], box(bounds[0])[3]
            lines = [[img.getpixel((x, y)) for x in range(x0, x1)] for y in range(y0, y1)]
            runs = ink_runs(lines, rest_bar)
            gaps = [(runs[i + 1][0] - runs[i][1] - 1) / scale for i in range(len(runs) - 1)]
            gap = sorted(gaps)[len(gaps) // 2] if gaps else None
            widths = [b["w"] for b in bounds]
            pitch = bounds[1]["x"] - bounds[0]["x"]
            if len(runs) != len(BUTTONS):
                fails.append("그림 묶음 %d개(기대 %d) — 겹침·잘림 또는 측정 실패" % (len(runs), len(BUTTONS)))
            if args.base_gap is None:
                fails.append("수정 전 기록(--base-gap 없음): 빈 공간 %s" % gap)
            elif gap is None or abs(gap - args.base_gap / 2) > GAP_TOL:
                fails.append("빈 공간 %s 이 %.1f 의 1/2 이 아니다" % (gap, args.base_gap))

            rig.r.call("ui.toggle", id=BUTTONS[4], doc=doc, on=True)
            time.sleep(0.4)
            on_img = shot(theme + "_on")
            on_fill = px(target, 0.12, 0.5, on_img)
            # 켜짐은 기호 색으로 구분한다(2026-11-11 사용자 지시) — 켜짐 바탕이 기본 바탕과 같아도 된다.
            # 켜짐 색은 단추 Foreground 가 아니라 내부 ContentPresenter·PathIcon 에 걸리므로(ChromeButtonStyles.axaml) find 로 읽으면 켜짐 전후가 같다. 실제 그려진 기호 화소로 잰다
            off_glyph, on_glyph = glyph_pixel(img, box(target), rest_bar), glyph_pixel(on_img, box(target), rest_bar)
            # 비전경 기동이면 문서 창이 비활성 바탕일 수 있다. 그때는 비활성 기준으로 잰다
            need = MIN_INACTIVE_ON if tc.same_rgb(rest_bar, tc.rgb_of(colors["PdBrushMdiTitleBarInactive"])) else MIN_STATE
            glyph_dist, on_vs_bar = on_glyph_vs_off_glyph(off_glyph, on_glyph), ratio(on_glyph, rest_bar)
            if glyph_dist < MIN_GLYPH_DIST:
                fails.append("실측 on_glyph_vs_off_glyph %.0f < %d (꺼짐 %s 켜짐 %s)" % (glyph_dist, MIN_GLYPH_DIST, off_glyph, on_glyph))
            if need == MIN_STATE and on_vs_bar < need:  # 비활성 바탕이면 값만 기록한다(정적 모델과 같은 규칙)
                fails.append("실측 on_glyph_vs_bar %.2f < %.1f" % (on_vs_bar, need))
            rig.r.call("ui.toggle", id=BUTTONS[4], doc=doc, on=False)
            time.sleep(0.3)

            live_hover = None
            if pseudo_ok:
                if rig.r.call("ui.pseudo", id=BUTTONS[5], doc=doc, cls=":pointerover", on=True) is None:
                    pseudo_ok = False
                    print("ui.pseudo 실패 — 호버 실측 건너뜀", flush=True)
                else:
                    time.sleep(0.3)
                    hover_img = shot(theme + "_hover")
                    live_hover = px(bounds[5], 0.12, 0.5, hover_img)
                    rig.r.call("ui.pseudo", id=BUTTONS[5], doc=doc, cls=":pointerover", on=False)
                    if tc.same_rgb(live_hover, rest_bar):
                        fails.append("호버 바탕 %s 이 기본 바탕과 같다" % (live_hover,))
            row = {"theme": theme, "gap_ink": gap, "gaps": gaps, "pitch": pitch, "widths": widths, "scale": round(scale, 3), "live": {"rest": "%02X%02X%02X" % rest_bar, "on": "%02X%02X%02X" % on_fill, "hover": "%02X%02X%02X" % live_hover if live_hover else None},
                   "model": rows, "cache": cache_row(colors), "cache_live": cache_live, "before_weakness": old_weakness(colors), "fails": fails, "pass": not fails}
            report["themes"].append(row)
            print(theme, "PASS" if row["pass"] else "FAIL", "gap", gap, "pitch", pitch, "w", widths[0], row["live"], "약함", row["before_weakness"], fails, flush=True)
        report["hover_live"] = pseudo_ok
        code = 0 if report["themes"] and all(t["pass"] for t in report["themes"]) else 1
    except tc.UserActive as e:
        print("입력 감지 — 자기 PID 만 종료 rc=5:", e, flush=True)
        code = 5
    finally:
        rig.stop()
        os.makedirs(os.path.dirname(os.path.abspath(args.out)), exist_ok=True)
        report["rc"] = code
        with open(args.out, "w", encoding="utf-8") as f:
            json.dump(report, f, ensure_ascii=False, indent=1)
        shutil.rmtree(work, ignore_errors=True)
    print("rc=%d 결과=%s" % (code, args.out), flush=True)
    return code


if __name__ == "__main__":
    sys.exit(main())
