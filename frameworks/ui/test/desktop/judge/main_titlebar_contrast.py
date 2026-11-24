#!/usr/bin/env python3
# 메인 타이틀바와 제목 줄의 바탕 구분 판정 — 내장 테마 전부에서 토큰 값·실제 바탕·캡처 픽셀로 대비를 재고 기준 미달이면 rc=1 로 끝낸다
"""
사용:
  python3 -B main_titlebar_contrast.py --run-dir <bin/run/복사본> --out <결과.json> --shots <캡처 폴더> [--monitor DISPLAY3] [--user-pid N] [--activate]
  python3 -B main_titlebar_contrast.py --self-test
종료 코드: 0=전 테마 통과, 1=기준 미달, 2=기동·연결 실패, 3=유휴 부족 또는 --activate 인데 사용자 Pd 를 확정 못 해 기동 안 함, 5=시험 중 입력 감지.
안전: 새 임시 data·profile 로 앱 1개만 띄우고 자기 PID 만 종료한다. 실입력을 보내지 않고 비전경(--bg-pos)으로 기동한다. 기동 전 유휴 --min-idle 초(기본 300)를 확인하고, 시험 중 입력이 있으면 멈춘다.
실제 바탕은 그때 창 상태(결과의 live_state)만 실측하고, 나머지 상태는 토큰 값으로 잰다. --activate 는 전경을 가져가므로 사용자가 PC 를 안 쓸 때만 쓴다.
기준(임시, 05_창크롬·01 §4.2 에 수치 없음): 활성 바탕 대 제목 줄 3.0 이상(WCAG 2.1 SC 1.4.11 비텍스트 3:1 준용), 활성 글자 대 바탕 4.5 이상(SC 1.4.3),
비활성 글자(투명도 반영) 대 바탕 3.0 이상, 비활성 바탕 대 제목 줄 1.2 이상, 활성과 비활성 바탕이 다름, 끌기 빈칸 픽셀이 제목 줄과 다름.
새 토큰이 없는 판본은 기존 토큰(PdBrushMenuBar·PdBrushBackground·PdBrushText)으로 잰다. 그 값이 수정 전 FAIL 근거다.
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
MIN_BAR_STRIP, MIN_TEXT, MIN_TEXT_INACTIVE, MIN_INACTIVE_STRIP = 3.0, 4.5, 3.0, 1.2
INACTIVE_OPACITY = 0.75  # WindowChromeStyle 의 Windows 스타일 PdChromeInactiveOpacity
PIXEL_TOL = 3


# Avalonia 가 KnownColor 와 같은 값의 브러시를 이름으로 내보내는 경우(실측 11-07: pd-light 새로고침 버튼 Foreground "Silver"). 값은 CSS/Avalonia KnownColor 표준값
NAMED_COLORS = {"white": "ffffff", "black": "000000", "silver": "c0c0c0", "gray": "808080", "darkgray": "a9a9a9", "lightgray": "d3d3d3",
                "dimgray": "696969", "gainsboro": "dcdcdc", "whitesmoke": "f5f5f5", "transparent": "00ffffff"}


def parse_color(text):
    """#RRGGBB·#AARRGGBB(대소문자 무관)·Avalonia 가 이름으로 내보내는 White·Black → (a, r, g, b). 해석할 수 없으면 None."""
    s = NAMED_COLORS.get(str(text or "").strip().lower(), str(text or "").strip().lstrip("#"))
    if len(s) == 6:
        s = "ff" + s
    if len(s) != 8:
        return None
    try:
        v = int(s, 16)
    except ValueError:
        return None
    return (v >> 24) & 255, (v >> 16) & 255, (v >> 8) & 255, v & 255


def luminance(rgb):
    def lin(c):
        c /= 255.0
        return c / 12.92 if c <= 0.03928 else ((c + 0.055) / 1.055) ** 2.4
    return 0.2126 * lin(rgb[0]) + 0.7152 * lin(rgb[1]) + 0.0722 * lin(rgb[2])


def contrast(a, b):
    la, lb = luminance(a), luminance(b)
    return (max(la, lb) + 0.05) / (min(la, lb) + 0.05)


def blend(top, bottom, alpha):
    return tuple(round(t * alpha + u * (1 - alpha)) for t, u in zip(top, bottom))


def rgb_of(text):
    c = parse_color(text)
    return c[1:] if c else None


def token_row(colors):
    """테마 색 사전 → 판정 값. 새 토큰이 없으면 기존 토큰으로 잰다."""
    active = colors.get("PdBrushMainTitleBar") or colors["PdBrushMenuBar"]
    inactive = colors.get("PdBrushMainTitleBarInactive") or colors["PdBrushBackground"]
    text = colors.get("PdBrushMainTitleText") or colors["PdBrushText"]
    strip = colors["PdBrushToolBar"]
    a, i, t, s = rgb_of(active), rgb_of(inactive), rgb_of(text), rgb_of(strip)
    floor2 = lambda x: int(x * 100) / 100  # 반올림으로 기준을 넘기지 않게 내림한다(WCAG 판정은 반올림하지 않는다)
    return {
        "active": active, "inactive": inactive, "text": text, "strip": strip,
        "bar_strip": floor2(contrast(a, s)), "inactive_strip": floor2(contrast(i, s)),
        "text_bar": floor2(contrast(t, a)), "text_inactive": floor2(contrast(blend(t, i, INACTIVE_OPACITY), i)),
        "active_ne_inactive": a != i,
        "old_tokens": "PdBrushMainTitleBar" not in colors,
    }


def token_fails(row):
    fails = []
    if row["bar_strip"] < MIN_BAR_STRIP:
        fails.append("활성 바탕 대 제목 줄 %.2f < %.1f" % (row["bar_strip"], MIN_BAR_STRIP))
    if row["inactive_strip"] < MIN_INACTIVE_STRIP:
        fails.append("비활성 바탕 대 제목 줄 %.2f < %.1f" % (row["inactive_strip"], MIN_INACTIVE_STRIP))
    if row["text_bar"] < MIN_TEXT:
        fails.append("활성 글자 대 바탕 %.2f < %.1f" % (row["text_bar"], MIN_TEXT))
    if row["text_inactive"] < MIN_TEXT_INACTIVE:
        fails.append("비활성 글자 대 바탕 %.2f < %.1f" % (row["text_inactive"], MIN_TEXT_INACTIVE))
    if not row["active_ne_inactive"]:
        fails.append("활성과 비활성 바탕이 같다")
    return fails


class UserActive(BaseException):
    """시험 중 사용자 입력을 감지했다. 측정의 예외 처리에 삼켜지지 않게 BaseException 에서 파생한다."""


class IdleWatch:
    """사용자 유휴를 읽기 전용으로 본다(복귀 플래그·주입 기록을 건드리지 않는다). 기동 전 유휴 부족이면 start 가 False, 기동 뒤 입력이 있으면 check 가 UserActive 를 던진다."""

    def __init__(self, read_probe, min_idle_s):
        self.read_probe, self.min_idle_s, self.t0 = read_probe, min_idle_s, None

    def start(self):
        idle = self.read_probe()["idle_ms"] / 1000.0
        print("유휴 %.0f초(기준 %d초)" % (idle, self.min_idle_s), flush=True)
        if idle < self.min_idle_s:
            return False
        self.t0 = time.time()
        return True

    def check(self):
        elapsed = (time.time() - self.t0) * 1000
        idle = self.read_probe()["idle_ms"]
        if idle + 2000 < elapsed:
            raise UserActive("기동 뒤 입력 감지: 유휴 %.1f초, 경과 %.1f초" % (idle / 1000.0, elapsed / 1000.0))


def same_rgb(a, b, tol=PIXEL_TOL):
    return bool(a and b) and all(abs(x - y) <= tol for x, y in zip(a, b))


def self_test():
    assert round(contrast((0, 0, 0), (255, 255, 255)), 1) == 21.0
    assert blend((255, 255, 255), (0, 0, 0), 0.5) == (128, 128, 128)
    assert parse_color("#ff40568d") == (255, 0x40, 0x56, 0x8D) and parse_color("#40568D") == (255, 0x40, 0x56, 0x8D) and parse_color("x") is None
    assert parse_color("White") == (255, 255, 255, 255) and parse_color("Black") == (255, 0, 0, 0)
    assert parse_color("Silver") == (255, 0xC0, 0xC0, 0xC0) and parse_color("Transparent") == (0, 255, 255, 255) and parse_color("NoSuchColor") is None
    same = token_row({"PdBrushMenuBar": "#FFFFFF", "PdBrushBackground": "#FFFFFF", "PdBrushText": "#000000", "PdBrushToolBar": "#FFFFFF"})
    assert same["bar_strip"] == 1.0 and len(token_fails(same)) == 3, same  # 진양성: 수정 전 모양은 반드시 잡는다
    good = token_row({"PdBrushMainTitleBar": "#40568D", "PdBrushMainTitleBarInactive": "#6E6E6E", "PdBrushMainTitleText": "#FFFFFF", "PdBrushMenuBar": "#FFFFFF", "PdBrushBackground": "#FFFFFF", "PdBrushText": "#000000", "PdBrushToolBar": "#FFFFFF"})
    assert not token_fails(good), good
    assert same_rgb((1, 2, 3), (2, 3, 4)) and not same_rgb((0, 0, 0), (9, 9, 9)) and not same_rgb(None, (1, 1, 1))
    print("self-test ok")
    return 0


def prop(node, name):
    return ((node or {}).get("props") or {}).get(name)


def sample(path, bounds, scale, fx, fy):
    from PIL import Image
    with Image.open(path) as img:
        x = min(img.width - 1, int((bounds["x"] + bounds["w"] * fx) * scale))
        y = min(img.height - 1, int((bounds["y"] + bounds["h"] * fy) * scale))
        return img.convert("RGB").getpixel((x, y))


def measure_theme(rig, app, theme, tmp, shots):
    """테마 1개를 적용해 토큰·실제 바탕·픽셀을 읽는다."""
    rig.r.call("theme.select", id=theme)
    time.sleep(0.6)
    export = os.path.join(tmp, theme + ".json")
    rig.r.call("theme.export", path=app.win(export), id=theme)
    with open(export, encoding="utf-8-sig") as f:
        doc = json.load(f)
    colors = doc.get("Colors") or doc.get("colors")
    row = token_row(colors)
    bar = rig.r.call("ui.find", id="shell.titleBar", props="Background")
    strip = rig.r.call("ui.find", id="shell.titleStrip", props="Background,IsVisible")
    drag = rig.r.call("ui.find", id="shell.titleDragArea")
    menu = rig.r.call("ui.find", id="shell.mnuFile", props="Foreground")
    cap = rig.r.call("ui.find", id="shell.btnMinimize", props="Foreground")
    state = rig.r.call("sim.activeState") or {}
    main = next(iter(state.get("windows") or []), {})
    is_active = bool(main.get("isActive"))
    png = os.path.join(tmp, theme + ".png")
    rig.r.call("screenshot", window=0, path=app.win(png))
    width = (bar.get("bounds") or {}).get("w") or 1
    from PIL import Image
    with Image.open(png) as img:
        scale = img.width / width
    px_drag = sample(png, drag["bounds"], scale, 0.5, 0.5)
    px_strip = sample(png, strip["bounds"], scale, 0.92, 0.5) if strip.get("bounds") else None
    os.makedirs(shots, exist_ok=True)
    shutil.copy(png, os.path.join(shots, theme + ".png"))
    want = row["active"] if is_active else row["inactive"]
    live = {
        "live_state": "active" if is_active else "inactive",
        "bar_bg": prop(bar, "Background"), "strip_bg": prop(strip, "Background"), "strip_visible": prop(strip, "IsVisible"),
        "menu_fg": prop(menu, "Foreground"), "caption_fg": prop(cap, "Foreground"),
        "px_drag": "%02X%02X%02X" % px_drag, "px_strip": "%02X%02X%02X" % px_strip if px_strip else None, "scale": round(scale, 3),
    }
    fails = token_fails(row)
    if not same_rgb(rgb_of(live["bar_bg"]), rgb_of(want)):
        fails.append("실제 바탕 %s 이 %s 토큰 %s 과 다르다" % (live["bar_bg"], live["live_state"], want))
    if same_rgb(px_drag, px_strip):
        fails.append("끌기 빈칸 픽셀 %s 이 제목 줄 픽셀과 같다" % live["px_drag"])
    if not row["old_tokens"] and not same_rgb(rgb_of(live["menu_fg"]), rgb_of(row["text"])):
        fails.append("상위 메뉴 글자 %s 이 토큰 %s 과 다르다" % (live["menu_fg"], row["text"]))
    return {"theme": theme, "tokens": row, "live": live, "fails": fails, "pass": not fails}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--self-test", action="store_true")
    ap.add_argument("--run-dir")
    ap.add_argument("--out")
    ap.add_argument("--shots")
    ap.add_argument("--monitor", default="DISPLAY3")
    ap.add_argument("--user-pid", type=int, default=None, help="--activate 때 생략하면 실행 경로로 찾고, 찾지 못하면 거부한다")
    ap.add_argument("--activate", action="store_true")
    ap.add_argument("--min-idle", type=int, default=300, help="기동 전 필요한 사용자 유휴(초). 0 이면 보지 않는다")
    args = ap.parse_args()
    if args.self_test:
        return self_test()
    if not (args.run_dir and args.out and args.shots):
        ap.error("--run-dir --out --shots 가 필요하다")
    self_test()
    sys.path.insert(0, MEASURE)
    import mdi_drag_app as app
    import retest_real_input as ri
    import user_away_guard as away
    if args.activate and not ri.resolve_user_pid(args):
        return 3
    watch = IdleWatch(away.read_probe, args.min_idle)
    if not watch.start():
        print("유휴 부족 — 기동하지 않음 rc=3", flush=True)
        return 3

    class QuietRig(ri.Rig):
        """전경을 가져가지 않는다(--activate 일 때만 기존 경로)."""

        def acquire(self, hwnd):
            return super().acquire(hwnd) if self.args.activate else {"hwnd": 0, "pid": 0}

    win_temp = app.ps("[IO.Path]::GetTempPath()").strip()
    base_tmp = os.popen("wslpath -u '%s'" % win_temp).read().strip()
    work = tempfile.mkdtemp(prefix="pd_titlebar_", dir=base_tmp)
    rig = QuietRig(args, work)
    rows, code = [], 2
    try:
        try:
            work_area = rig.monitor["work"]
            rig.start(extra=() if args.activate else ("--bg-pos", "%d,%d" % (work_area["r"] - ri.MAIN_W - 180, work_area["t"] + 150)))
        except SystemExit as e:
            print("기동 실패:", e, flush=True)
            return 2
        themes = json.loads(rig.r.call("themes.get")) if isinstance(rig.r.call("themes.get"), str) else rig.r.call("themes.get")
        for theme in themes["builtinThemes"]:
            watch.check()
            row = measure_theme(rig, app, theme, work, args.shots)
            rows.append(row)
            print(theme, "PASS" if row["pass"] else "FAIL", json.dumps(row["tokens"], ensure_ascii=False), json.dumps(row["live"], ensure_ascii=False), row["fails"], flush=True)
        code = 0 if rows and all(r["pass"] for r in rows) else 1
    except UserActive as e:
        print("입력 감지 — 자기 PID 만 종료 rc=5:", e, flush=True)
        code = 5
    finally:
        rig.stop()
        os.makedirs(os.path.dirname(os.path.abspath(args.out)), exist_ok=True)
        with open(args.out, "w", encoding="utf-8") as f:
            json.dump({"rc": code, "criteria": {"bar_strip": MIN_BAR_STRIP, "text": MIN_TEXT, "text_inactive": MIN_TEXT_INACTIVE, "inactive_strip": MIN_INACTIVE_STRIP}, "themes": rows}, f, ensure_ascii=False, indent=1)
        shutil.rmtree(work, ignore_errors=True)
    print("rc=%d 결과=%s" % (code, args.out), flush=True)
    return code


if __name__ == "__main__":
    sys.exit(main())
