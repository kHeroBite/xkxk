#!/usr/bin/env python3
# K3 측정 — 크롬 버튼 상태 대비 전 테마 최솟값(chrome_state_rules 그대로)과 실제 포인터 호버(safe_move 커서 이동 전후 캡처 픽셀 차)를 pcaway_report --measure-file 형식으로 쓴다
"""
사용:
  python3 -B retest_k3_hover.py --run-dir <bin/run/<복사본>> --live-json <chrome_button_states --activate 결과.json> --out <K3.measure.json> --shots <폴더> --monitor DISPLAY1 [--dry-run]
기준(goal.json K3·pcaway_gate K3 정의를 그대로 쓴다, 새 기준 없음):
  - rest_hover·rest_on·on_hover_fill·on_onhover_border·glyph_on_hover·glyph_on_on: 전 내장 테마 × 막대(active·pinned·surface·main) model 값의 최솟값 ≥ 3.0
  - inactive_rest_on(model.inactive.rest_on 최솟값) ≥ 2.0, surface_inactive_rest_on(extended surface_inactive.rest_on) ≥ 2.0
  - cache_min(cache_row 최솟값) ≥ 3.0, cache_surface_min(cache_surface 의 _inactive 아닌 값) ≥ 3.0, cache_surface_inactive_min ≥ 2.0
  - caption_min(caption_accent·caption_surface·caption_main 최솟값) ≥ 3.0, hover_live = chrome_button_states --activate 결과의 hover_live
  - real_hover_accent·real_hover_surface: 커서를 문서 창 최대화 버튼 위로 옮긴 전후 캡처의 버튼 영역 픽셀이 다름(표면색은 chrome.tune accentTitle=false 전환 뒤)
  - tool_rc: chrome_button_states.py --static BuiltinThemes.cs 의 종료 코드
실입력: safe_move.ps1(SendInput 상대 이동)만 쓴다. 클릭 없음. --dry-run 은 앱을 띄우고 좌표·캡처만 하고 커서는 옮기지 않는다(real_hover_* 미측정).
안전: 임시 data·profile, --multi-instance, 자기 PID 만 종료, 이동 전 user_away_guard.check·전경 게이트(retest_real_input.Rig).
"""
import argparse
import json
import os
import re
import subprocess
import sys
import tempfile
import time

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
JUDGE = os.path.abspath(os.path.join(HERE, "..", "judge"))
sys.path.insert(0, HERE)
sys.path.insert(0, JUDGE)
import chrome_state_rules as csr  # noqa: E402

THEME_SRC = os.path.join(ROOT, "src/xkxk.frameworks.ui.shell/Theming/BuiltinThemes.cs")
THEME_DEF = os.path.join(ROOT, "src/xkxk.frameworks.ui.shell/Theming/ThemeDefinition.cs")
STATES_TOOL = os.path.join(JUDGE, "chrome_button_states.py")
SAFE_MOVE = os.path.join(HERE, "safe_move.ps1")
BARS = ("active", "pinned", "surface", "main")
STATE_KEYS = ("rest_hover", "rest_on", "on_hover_fill", "on_onhover_border", "glyph_on_hover", "glyph_on_on")


def theme_colors():
    """chrome_button_states.static_check 와 같은 정규식으로 내장 테마 색 표를 읽는다."""
    keys = re.findall(r'"(PdBrush\w+)"', open(THEME_DEF, encoding="utf-8").read().split("KeyList =")[1].split("public static void FillMissing")[0])
    table = open(THEME_SRC, encoding="utf-8").read().split("Table =")[1]
    out = {}
    for m in re.finditer(r'"theme\.builtin\.([\w-]+)", ThemeBase\.\w+,(.*?)\)\s*,\s*\n', table, re.S):
        out[m.group(1)] = dict(zip(keys, "".join(re.findall(r'"([^"]*)"', m.group(2))).split(",")))
    return out


def static_values():
    themes = theme_colors()
    vals = {k: [] for k in STATE_KEYS}
    inactive, surf_inactive, cache, cache_surf, cache_surf_in, caption = [], [], [], [], [], []
    for colors in themes.values():
        rows = csr.model(colors)
        for bar in BARS:
            for k in STATE_KEYS:
                vals[k].append(rows[bar][k])
        inactive.append(rows["inactive"]["rest_on"])
        ext = csr.extended_rows(colors)
        surf_inactive.append(ext["surface_inactive"]["rest_on"])
        cache += list(csr.cache_row(colors).values())
        for k, v in ext["cache_surface"].items():
            (cache_surf_in if k.endswith("_inactive") else cache_surf).append(v)
        for name in ("caption_accent", "caption_surface", "caption_main"):
            caption += list((ext[name] or {}).values())
    m = {k: min(v) for k, v in vals.items()}
    m.update({"inactive_rest_on": min(inactive), "surface_inactive_rest_on": min(surf_inactive), "cache_min": min(cache),
              "cache_surface_min": min(cache_surf), "cache_surface_inactive_min": min(cache_surf_in), "caption_min": min(caption)})
    return sorted(themes), m


def tool_rc():
    p = subprocess.run([sys.executable, "-B", STATES_TOOL, "--static", THEME_SRC], capture_output=True, text=True, timeout=120)
    return p.returncode


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--run-dir", required=True)
    ap.add_argument("--live-json", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--shots", required=True)
    ap.add_argument("--monitor", default="DISPLAY1")
    ap.add_argument("--user-pid", type=int, default=None)
    ap.add_argument("--dry-run", action="store_true")
    args = ap.parse_args()
    import mdi_drag_app as app
    import retest_real_input as ri
    import user_away_guard as away
    from PIL import Image, ImageChops

    started = time.strftime("%Y-%m-%dT%H:%M:%S%z")
    themes, sv = static_values()
    rc = tool_rc()
    live = json.load(open(args.live_json, encoding="utf-8"))
    src_static = "retest_k3_hover.py static_values(chrome_state_rules model·extended_rows·cache_row, BuiltinThemes.cs %d개 테마)" % len(themes)
    ms = [{"name": "themes_all", "value": themes, "pass": len(themes) >= 7, "source": src_static}]
    for k in STATE_KEYS + ("cache_min", "cache_surface_min", "caption_min"):
        ms.append({"name": k, "value": sv[k], "pass": sv[k] >= csr.MIN_STATE, "source": src_static})
    for k in ("inactive_rest_on", "surface_inactive_rest_on", "cache_surface_inactive_min"):
        ms.append({"name": k, "value": sv[k], "pass": sv[k] >= csr.MIN_INACTIVE_ON, "source": src_static})
    ms.append({"name": "hover_live", "value": live.get("hover_live"), "pass": live.get("hover_live") is True, "source": "chrome_button_states.py --activate 결과 " + args.live_json})
    ms.append({"name": "tool_rc", "value": rc, "pass": rc == 0, "source": "chrome_button_states.py --static BuiltinThemes.cs"})

    if not ri.resolve_user_pid(args):
        return 3
    os.makedirs(args.shots, exist_ok=True)
    base_tmp = os.popen("wslpath -u '%s'" % app.ps("[IO.Path]::GetTempPath()").strip()).read().strip()
    rig = ri.Rig(args, tempfile.mkdtemp(prefix="pd_k3hover_", dir=base_tmp))
    hover, caps, code = {}, [], 0
    try:
        away.check()
        rig.start()
        doc = ri.open_docs(rig, count=1)[0]
        time.sleep(1.0)

        def shot(name):
            path = os.path.join(rig.work, name + ".png")
            rig.r.call("screenshot", window=0, path=app.win(path))
            keep = os.path.join(args.shots, name + ".png")
            Image.open(path).save(keep)
            caps.append(keep)
            return Image.open(path).convert("RGB")

        def park():
            """커서를 버튼에서 떨어진 자기 창 문서 영역 가운데로 옮긴다(호버 해제)."""
            d = rig.find("shell.dock")["bounds"]
            return rig.point(rig.main, d)

        def move(x, y):
            rig.check_coord(x, y)
            rig.gate(rig.main)
            done = ri.run_ps_file(SAFE_MOVE, "-ScreenX", x, "-ScreenY", y, "-ExpectedProcessId", rig.pid, "-ForbiddenProcessId", rig.args.user_pid or 4)
            rig.after_input()
            if done.returncode != 0:
                raise ri.InputRefused("safe_move rc=%s: %s" % (done.returncode, (done.stderr or done.stdout)[-200:]))
            time.sleep(0.6)

        def one(tag):
            b = rig.find("chrome.btnMax", doc=doc)
            if not b or not b.get("effectivelyVisible"):
                return {"error": "chrome.btnMax 없음"}, False
            bx = b["bounds"]
            x, y = rig.point(rig.main, bx)
            if args.dry_run:
                before = shot("k3_%s_dry" % tag)
                return {"dry_run": True, "target": [x, y], "bounds": bx}, None
            move(*park())
            before = shot("k3_%s_before" % tag)
            move(x, y)
            after = shot("k3_%s_after" % tag)
            move(*park())
            box = (int(bx["x"]), int(bx["y"]), int(bx["x"] + bx["w"]), int(bx["y"] + bx["h"]))
            diff = ImageChops.difference(before.crop(box), after.crop(box))
            changed = sum(1 for p in diff.getdata() if max(p) > 8)
            return {"target": [x, y], "bounds": bx, "changed_px": changed, "box_px": (box[2] - box[0]) * (box[3] - box[1])}, changed > 0

        v, ok = one("accent")
        hover["real_hover_accent"] = (v, ok)
        tune = rig.r.call("chrome.tune", accentTitle=False)
        time.sleep(1.0)
        v, ok = one("surface")
        v["chrome_tune"] = tune
        hover["real_hover_surface"] = (v, ok)
        rig.r.call("chrome.tune", accentTitle=True)
    except (away.UserReturned, away.GuardUnavailable) as e:
        print("중단: %r" % (e,), flush=True)
        code = 5
    except ri.InputRefused as e:
        print("거부: %s" % e, flush=True)
        code = 1
    finally:
        rig.stop()
    for name in ("real_hover_accent", "real_hover_surface"):
        v, ok = hover.get(name, ({"unmeasured": True}, False))
        ms.append({"name": name, "value": v, "pass": ok is True, "source": "retest_k3_hover.py safe_move 커서 이동 전후 screenshot 버튼 영역 픽셀 차" + (" (드라이런 — 미측정)" if args.dry_run else "")})
    out = {"item": "K3", "measured_started_at": started, "measured_finished_at": time.strftime("%Y-%m-%dT%H:%M:%S%z"), "measurements": ms, "shots": caps, "dry_run": args.dry_run}  # 캡처는 emit 의 --capture 로 넘긴다(captures 키는 pcaway_report 가 dict 목록으로 읽는다)
    with open(args.out, "w", encoding="utf-8") as f:
        json.dump(out, f, ensure_ascii=False, indent=1)
    for m in ms:
        print(("PASS" if m["pass"] else "FAIL"), m["name"], json.dumps(m["value"], ensure_ascii=False)[:160], flush=True)
    print("측정 파일", args.out, flush=True)
    return code or (0 if all(m["pass"] for m in ms) else 1)


if __name__ == "__main__":
    sys.exit(main())
