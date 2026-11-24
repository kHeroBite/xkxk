#!/usr/bin/env python3
# 패널 기능(핀·도킹 안내) 설정 PanelFeatureMode 의 PC형·태블릿형 기본값, 수동 켬/끔, 끔 시 핀 해제, 같은 영역 순서 바꾸기, 재기동 유지, 실끌기 안내 미표시를 진단판 복사본에서 재 pcaway_gate 형식(Q2)으로 쓴다
"""
사용:
  python3 -B retest_panel_feature.py --items Q2 --run-dir <bin/run/<복사본>/net10.0> --phase final --writer otest-d
  python3 -B retest_panel_feature.py --annotate <결과.json> --capture all --read-by otest-d --finding '<판독 내용 10자 이상>'
  python3 -B retest_panel_feature.py --self-test
측정 경로(원격 명령): panel.canDrop(policy·targetVisible) · dock.chrome(핀 버튼 pinVisible) · panel.pin(Done|Rejected, pinned) · panel.join·panel.tabs · ui.style ·
  설정 '창' 페이지 콤보 settings.cmbPanelFeature(sys.open Settings → settings.window category=Window → ui.select index 0=자동 1=켬 2=끔).
실끌기는 retest_real_input 의 safe_click 끌기와 watch_guide 로 가운데 분할 표적 창이 뜨는지 본다. 같은 도구가 PC형 기본(켬)에서 표적이 보이는 대조도 먼저 확인하고, 대조가 안 보이면 감시가 고장난 것이므로 FAIL 이다.
안전 규칙과 종료 코드는 retest_real_input.py 와 같다. 실행은 otest 단계에서만 한다.
"""
import argparse
import base64
import json
import os
import subprocess
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import retest_real_input as ri  # noqa: E402
import retest_title_strip as strip_lib  # noqa: E402  UI 형 전환(set_style)을 함께 쓴다

REQ = {
    "Q2": ["pc_default_on_drop_visible", "pc_default_on_pin_visible", "tablet_default_off_drop_hidden", "tablet_default_off_pin_hidden", "tablet_off_pin_refused",
           "mode_on_in_tablet_enables", "mode_off_in_pc_disables", "off_unpins_pinned_panel", "reorder_same_tabs_kept", "setting_persisted_restart", "real_drag_no_indicator"],
}
LP, BP = ri.LP, ri.BP
COMBO = "settings.cmbPanelFeature"
AUTO, ON, OFF = 0, 1, 2  # 콤보 순서: 자동·켬·끔
PROBE = {"source": "Explorer", "target": "Documents", "op": "Right"}  # 문서 영역 오른쪽 놓기 = 가운데 분할 표적(도킹 안내)이 가리키는 연산


# 순수 판정(자가시험 대상) -----------------------------------------------------
def drop_visible(drop):
    """panel.canDrop 응답에서 정책이 허용하고 Dock 표적도 보이면 안내가 보인다고 본다."""
    return bool(drop and drop.get("policy") is True and drop.get("targetVisible") is True)


def drop_hidden(drop):
    return bool(drop and drop.get("policy") is False and drop.get("targetVisible") is False)


def pin_buttons_visible(chromes):
    """dock.chrome 의 보이는 머리줄 중 핀 버튼이 보이는 수와 보이는 머리줄 수."""
    shown = [c for c in chromes or [] if c.get("visible")]
    return sum(1 for c in shown if c.get("pinVisible")), len(shown)


def pin_refused(resp):
    return bool(resp) and resp.get("status") == "Rejected" and resp.get("pinned") is False


def pin_accepted(resp):
    return bool(resp) and resp.get("status") == "Done" and resp.get("pinned") is True


# 원격 읽기·조작 ---------------------------------------------------------------
def probe(rig):
    drop = rig.r.call("panel.canDrop", **PROBE)
    pins, shown = pin_buttons_visible((rig.r.call("dock.chrome") or {}).get("chromes"))
    return {"drop": drop, "pin_buttons_visible": pins, "chromes_visible": shown}


def pinned(rig, pane):
    return next((p.get("pinned") for p in rig.r.call("panel.list") or [] if p.get("id") == pane), None)


def tab_ids(rig, pane):
    return [t["id"] for t in (rig.r.call("panel.tabs", id=pane) or {}).get("tabs", [])]


def set_mode(rig, index):
    """설정 창 '창' 페이지 콤보로 패널 기능 값을 바꾼다(원격 진단 명령에 이 설정이 없어 화면 경로를 쓴다)."""
    rig.r.call("sys.open", view="Settings")
    time.sleep(1.0)
    rig.r.call("settings.window", category="Window")
    time.sleep(0.8)
    out = rig.r.call("ui.select", id=COMBO, index=index)
    time.sleep(1.0)
    return out


def read_mode(rig):  # selected_index 는 ui.find props 값이라 문자열("1")로 온다
    rig.r.call("sys.open", view="Settings")
    time.sleep(1.0)
    rig.r.call("settings.window", category="Window")
    time.sleep(0.8)
    node = rig.r.call("ui.find", id=COMBO, props="SelectedIndex") or {}
    return {"found": bool(node.get("bounds")), "selected_index": (node.get("props") or {}).get("SelectedIndex")}


# 끌기 중 캡처 판정(team-lead 14:04 (a), 웹 Q3 와 같은 방식). 제품 기동도 Dock 안내는 창 안 adorner 라(dock-floating-adorner=False) 새 창 수로는 못 본다.
# 문서 영역 가운데 십자의 왼쪽·가운데 칸(커서가 있는 오른쪽 칸 +50 dip 은 미리 보기 창이 겹칠 수 있어 뺀다)을 끌기 내내 찍어 기준 화면 대비 변화 픽셀과 강조색(PdBrushAccent) 픽셀을 센다
CAP_DIP = (-100, -50, 25, 50)  # 문서 영역 가운데 기준 x0,y0,x1,y1(dip)
PIX_DIFF = 24                   # 변화 픽셀 채널 차 임계(웹 Q3 diff_pixels 와 같다)
ACCENT_TOL = 40                 # 강조색과 채널별 차 허용
DIFF_MIN, ACCENT_MIN = 300, 100 # 켬 검출 임계(둘 다 넘어야 검출). 끔은 둘 다 0 이어야 한다
FRAMES_PS = r"""
Add-Type -AssemblyName System.Drawing
Add-Type @'
using System; using System.Runtime.InteropServices;
public static class PdF { [DllImport("SHCore.dll")] public static extern int SetProcessDpiAwareness(int v); }
'@
try { [void][PdF]::SetProcessDpiAwareness(2) } catch { }
function Grab($i) { $b = New-Object System.Drawing.Bitmap %(w)d, %(h)d; $g = [System.Drawing.Graphics]::FromImage($b); $g.CopyFromScreen(%(x)d, %(y)d, 0, 0, $b.Size)
  $b.Save(('%(dir)s\f{0:D3}.png' -f $i), [System.Drawing.Imaging.ImageFormat]::Png); $g.Dispose(); $b.Dispose() }
Grab 0
[Console]::Out.WriteLine('READY'); [Console]::Out.Flush()
$sw = [Diagnostics.Stopwatch]::StartNew(); $i = 1
while ($sw.ElapsedMilliseconds -lt %(limit)d) { Grab $i; $i++; Start-Sleep -Milliseconds %(interval)d }
[Console]::Out.WriteLine('{"frames":' + $i + ',"ms":' + $sw.ElapsedMilliseconds + '}')
"""


def accent_rgb(rig, work):
    """현재 테마의 PdBrushAccent(Dock 안내 DockTargetIndicatorBrush 의 원천, ThemeApplier.cs) RGB."""
    themes = rig.r.call("themes.get") or {}
    themes = json.loads(themes) if isinstance(themes, str) else themes
    tid = themes.get("activeThemeId") or themes.get("themeId")
    path = os.path.join(work, "theme_%s.json" % tid)
    rig.r.call("theme.export", path=ri.app.win(path), id=tid)
    with open(path, encoding="utf-8-sig") as f:
        colors = (json.load(f).get("Colors") or {})
    text = str(colors.get("PdBrushAccent") or "").lstrip("#")
    return tid, (tuple(int(text[-6:][i:i + 2], 16) for i in (0, 2, 4)) if len(text) >= 6 else None)


def frame_counts(base, frame, accent):
    """기준 대비 변화 픽셀 수, 그중 강조색 픽셀 수(기준에 이미 있던 강조색은 빼려고 변화 픽셀 안에서만 센다)."""
    changed = acc = 0
    for (r0, g0, b0), (r1, g1, b1) in zip(base, frame):
        if max(abs(r1 - r0), abs(g1 - g0), abs(b1 - b0)) > PIX_DIFF:
            changed += 1
            if accent and max(abs(r1 - accent[0]), abs(g1 - accent[1]), abs(b1 - accent[2])) <= ACCENT_TOL:
                acc += 1
    return changed, acc


RELEASE_BEFORE_RETURN_S = 0.75  # safe_click 반환 시각 - 0.75초 이전 프레임만 끌기 중으로 본다(뗀 뒤 650ms 대기 + 여유 100ms)
PREVIEW_BOX = (-100, -35, 45, 35)  # 끌기 미리 보기 상자('탐색기 ⧉', 커서 기준 약 -60~+30 x ±20px — 2차 실측 14:29 프레임 판독)가 지나는 띠. 커서 기준 x0,y0,x1,y1(px)


def path_mask(size, origin, frm, to, box=PREVIEW_BOX):
    """끌기 경로를 따라 미리 보기 상자가 지나는 띠를 255 로 칠한 L 이미지(계산에서 뺄 곳)."""
    from PIL import Image, ImageDraw
    m = Image.new("L", size, 0)
    dr = ImageDraw.Draw(m)
    for k in range(101):
        x = frm[0] + (to[0] - frm[0]) * k / 100.0 - origin[0]
        y = frm[1] + (to[1] - frm[1]) * k / 100.0 - origin[1]
        dr.rectangle([x + box[0], y + box[1], x + box[2], y + box[3]], fill=255)
    return m


def image_counts(base_img, img, accent, mask=None):
    """frame_counts 와 같은 계산을 PIL 연산으로 빠르게 한다(문서 영역 전체 × 프레임 100장 이상). mask(255=뺄 곳)가 있으면 그 픽셀은 세지 않는다."""
    from PIL import Image, ImageChops
    chmax = lambda im: ImageChops.lighter(ImageChops.lighter(*im.split()[:2]), im.split()[2])  # noqa: E731 채널별 차의 최댓값
    changed = chmax(ImageChops.difference(base_img, img)).point(lambda v: 255 if v > PIX_DIFF else 0)
    if mask is not None:
        changed = ImageChops.subtract(changed, mask)
    n_changed = changed.histogram()[255]
    if not accent:
        return n_changed, 0
    return n_changed, accent_tinted(base_img, img, accent, changed)


ACCENT_COS = 0.9  # 변화 방향이 기준→강조색 방향과 이루는 코사인 하한(반투명 안내 = 기준·강조색 혼합)


def accent_tinted(base_img, img, accent, changed):
    """변화 픽셀 중 강조색 쪽으로 섞인 픽셀 수. 3차 실측(14:33): 안내 미리 채움은 반투명이라 강조색과 ±40 안으로 같지 않았다(같음 판정 10px).
    픽셀 = 기준 + a(강조색 - 기준) 꼴이면 (픽셀-기준) 과 (강조색-기준) 이 같은 방향이다. 강조색과 거의 같은 픽셀(±ACCENT_TOL)도 센다."""
    import numpy as np
    b = np.asarray(base_img, dtype=np.float32)
    f = np.asarray(img, dtype=np.float32)
    a = np.asarray(accent, dtype=np.float32)
    d, t = f - b, a - b
    dot = (d * t).sum(axis=2)
    norm = np.sqrt((d * d).sum(axis=2)) * np.sqrt((t * t).sum(axis=2))
    cos = np.where(norm > 0, dot / np.maximum(norm, 1e-6), 0)
    near = np.abs(f - a).max(axis=2) <= ACCENT_TOL
    ch = np.asarray(changed) > 0
    return int((ch & ((cos >= ACCENT_COS) | near)).sum())


def analyze_frames(paths, accent, mask_fn=None, cutoff=None):
    """프레임 파일 목록(첫 장=기준)에서 변화·강조색 픽셀 최댓값과 그 프레임. mask_fn(size) 은 뺄 곳 마스크.
    cutoff(파일 mtime, 초)가 있으면 그 전에 찍힌 프레임만 센다 — 놓은 뒤 패널 이동으로 생긴 변화를 안내로 세지 않게 한다(3차 실측 14:33)."""
    from PIL import Image
    if cutoff is not None:
        paths = paths[:1] + [p for p in paths[1:] if os.path.getmtime(p) <= cutoff]
    if len(paths) < 2:
        return {"frames": len(paths), "changed_px_max": None, "accent_px_max": None}
    base = Image.open(paths[0]).convert("RGB")
    mask = mask_fn(base.size) if mask_fn else None
    best = {"changed_px_max": 0, "accent_px_max": 0, "peak_frame": None}
    for p in paths[1:]:
        changed, acc = image_counts(base, Image.open(p).convert("RGB"), accent, mask)
        if (acc, changed) > (best["accent_px_max"], best["changed_px_max"]):
            best.update(changed_px_max=changed, accent_px_max=acc, peak_frame=p)
        best["changed_px_max"] = max(best["changed_px_max"], changed)
    best["frames"] = len(paths)
    return best


def guide_detected(cap):
    return bool(cap) and (cap.get("changed_px_max") or 0) >= DIFF_MIN and (cap.get("accent_px_max") or 0) >= ACCENT_MIN


def guide_zero(cap):
    return bool(cap) and cap.get("changed_px_max") == 0 and cap.get("accent_px_max") == 0


def judge_real_drag(control_cap, off_cap, side_after_off):
    """(값, 통과). 대조(켬)에서 미검출이면 판정 불가로 남기고 통과시키지 않는다."""
    on, zero = guide_detected(control_cap), guide_zero(off_cap)
    value = {"control_detected": on, "off_zero": zero, "side_after_off": side_after_off, "undetermined": not on,
             "thresholds": {"diff_min": DIFF_MIN, "accent_min": ACCENT_MIN, "pix_diff": PIX_DIFF, "accent_tol": ACCENT_TOL}}
    return value, on and zero and side_after_off == "Left"


def capture_during(rig, res, tag, fn, limit_ms=8000, interval_ms=60, path=None):
    """fn(끌기)을 하는 동안 문서 영역 전체를 주기적으로 찍는다. safe_click 은 끌기 전 3축 검증에 수 초를 쓰므로 8초를 찍는다(1차 실측 3.5초는 짧았다).
    1차 실측(14:26)에서 가운데 십자 일부 칸만 찍으면 표적이 영역 끝에만 걸렸고, 이 판에서는 끌기 미리 보기 별도 창이 뜨지 않아(새 창 0) 문서 영역 전체를 찍는다."""
    d = (rig.r.call("panel.bounds") or {}).get("Documents") or {}
    x0, y0 = rig.dip_point(rig.main, d.get("x", 0) + 2, d.get("y", 0) + 2)
    x1, y1 = rig.dip_point(rig.main, d.get("x", 0) + d.get("w", 0) - 2, d.get("y", 0) + d.get("h", 0) - 2)
    folder = os.path.join(res.dir, "work", res.run_id, tag + "_frames")
    os.makedirs(folder, exist_ok=True)
    script = FRAMES_PS % {"x": x0, "y": y0, "w": x1 - x0, "h": y1 - y0, "dir": ri.app.win(folder), "limit": limit_ms, "interval": interval_ms}
    proc = subprocess.Popen([ri.app.PS, "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-EncodedCommand", base64.b64encode(script.encode("utf-16-le")).decode("ascii")],
                            stdin=subprocess.DEVNULL, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, encoding="utf-8", errors="replace")
    proc.stdout.readline()  # READY: 기준 프레임을 찍은 뒤 끌기를 시작한다
    t_end = None
    try:
        fn()
        t_end = time.time()  # safe_click 은 마우스를 뗀 뒤 650ms(400+250) 기다렸다 돌아온다
    finally:
        try:
            proc.communicate(timeout=30)
        except subprocess.TimeoutExpired:
            proc.kill()
    paths = sorted(os.path.join(folder, f) for f in os.listdir(folder) if f.endswith(".png"))
    _, accent = accent_rgb(rig, folder)
    mask_fn = (lambda size: path_mask(size, (x0, y0), path[0], path[1])) if path else None
    cutoff = t_end - RELEASE_BEFORE_RETURN_S if t_end else None
    out = dict(analyze_frames(paths, accent, mask_fn, cutoff), region=[x0, y0, x1, y1], accent=accent, excluded_preview_band=PREVIEW_BOX if path else None,
               frames_total=len(paths), cutoff_before_return_s=RELEASE_BEFORE_RETURN_S)
    if out.get("peak_frame"):
        res.capture(out["peak_frame"])
    if paths:
        res.capture(paths[0])
    out["peak_frame"] = ri.rel(out["peak_frame"], res.root) if out.get("peak_frame") else None
    return out


def real_drag_guide(rig, res, tag):
    """왼쪽 패널 제목 줄을 문서 영역 가운데 오른쪽 표적으로 실제로 끌어 안내가 보이는지 본다(새 창 감시 + 끌기 중 캡처). 끝나면 패널을 제자리로 되돌린다."""
    rig.r.call("settings.esc")  # set_mode 가 연 설정 창이 끌기 중 앞으로 올라와 문서 영역을 가렸다(1차 실측 14:27) — 끌기 전에 닫는다
    time.sleep(1.0)
    rig.acquire(rig.main)
    frm, to = ri.panel_drag_points(rig, LP, "cross.right")
    watcher = rig.watch_guide(res, tag + "_guide")
    guide = {}
    try:
        cap = capture_during(rig, res, tag, lambda: rig.click(*frm, drag=to, hwnd=rig.main), path=(frm, to))
    finally:
        guide = rig.finish_guide(watcher[0], watcher[1], res)
    time.sleep(ri.SETTLE_S)
    side = next((p.get("side") for p in rig.r.call("panel.list") or [] if p.get("id") == LP), None)
    rig.grab(res, tag)
    ri.reset_panels(rig)
    return {"guide": guide, "capture": cap, "side_after": side, "from": frm, "to": to}


def q2(rig, res):
    # 1) PC형 기본(자동 → 켬)
    strip_lib.set_style(rig, "pc")
    rig.acquire(rig.main)
    pc = probe(rig)
    rig.grab(res, "q2_pc_default")
    res.add("pc_default_on_drop_visible", pc["drop"], drop_visible(pc["drop"]))
    res.add("pc_default_on_pin_visible", {"pin_buttons_visible": pc["pin_buttons_visible"], "chromes_visible": pc["chromes_visible"]}, pc["pin_buttons_visible"] > 0)
    try:
        control = real_drag_guide(rig, res, "q2_pc_control")  # 대조 끌기: PC형 기본에서 같은 도구가 안내를 잡아야 끔 측정이 의미가 있다
    except ri.InputRefused as e:
        control = {"refused": str(e)}
    rig.r.call("panel.join", id=LP, tool="Log")  # 같은 탭 영역에 두 탭: 순서 바꾸기 시험 준비
    time.sleep(0.5)
    tabs_before = tab_ids(rig, LP)
    pin_pre = rig.r.call("panel.pin", id=LP, on=True)
    time.sleep(0.5)
    pinned_before = pinned(rig, LP)

    # 2) PC형 + 끔
    set_mode(rig, OFF)
    off_pc = probe(rig)
    refuse_pc = rig.r.call("panel.pin", id=BP, on=True)
    res.add("mode_off_in_pc_disables", {"probe": off_pc, "pin_request": refuse_pc},
            drop_hidden(off_pc["drop"]) and off_pc["pin_buttons_visible"] == 0 and pin_refused(refuse_pc))
    after_off = pinned(rig, LP)
    res.add("off_unpins_pinned_panel", {"pin_before_off": pin_pre, "pinned_before_off": pinned_before, "pinned_after_off": after_off}, pinned_before is True and after_off is False)
    same = rig.r.call("panel.canDrop", source="Log", target="Explorer", op="Fill")
    tabs_after = tab_ids(rig, LP)
    res.add("reorder_same_tabs_kept", {"canDrop_same_area": same, "tabs_before": tabs_before, "tabs_after": tabs_after},
            bool(same) and same.get("policy") is True and len(tabs_before) >= 2 and sorted(tabs_before) == sorted(tabs_after))
    try:
        drag = real_drag_guide(rig, res, "q2_off_drag")
    except ri.InputRefused as e:
        drag = {"guide": {}, "side_after": None, "refused": str(e)}
    value, ok = judge_real_drag(control.get("capture"), drag.get("capture"), drag.get("side_after"))
    value.update(control=control, off=drag)
    res.add("real_drag_no_indicator", value, ok)

    # 3) 태블릿형 기본(자동 → 끔)
    set_mode(rig, AUTO)
    strip_lib.set_style(rig, "tablet")
    rig.acquire(rig.main)
    tab = probe(rig)
    refused = rig.r.call("panel.pin", id=LP, on=True)
    rig.grab(res, "q2_tablet_default")
    res.add("tablet_default_off_drop_hidden", tab["drop"], drop_hidden(tab["drop"]))
    res.add("tablet_default_off_pin_hidden", {"pin_buttons_visible": tab["pin_buttons_visible"], "chromes_visible": tab["chromes_visible"]}, tab["chromes_visible"] > 0 and tab["pin_buttons_visible"] == 0)
    res.add("tablet_off_pin_refused", {"pin_request": refused, "pinned": pinned(rig, LP)}, pin_refused(refused) and pinned(rig, LP) is False)

    # 4) 태블릿형 + 켬
    set_mode(rig, ON)
    on_tab = probe(rig)
    accepted = rig.r.call("panel.pin", id=LP, on=True)
    res.add("mode_on_in_tablet_enables", {"probe": on_tab, "pin_request": accepted},
            drop_visible(on_tab["drop"]) and on_tab["pin_buttons_visible"] > 0 and pin_accepted(accepted))
    rig.r.call("panel.pin", id=LP, on=False)

    # 5) 재기동 유지(켬을 저장한 채 같은 임시 data·profile 로 다시 띄운다)
    rig.stop()
    rig.start(reuse=True)
    rig.acquire(rig.main)
    strip_lib.set_style(rig, "tablet")
    persisted = probe(rig)
    mode = read_mode(rig)
    res.add("setting_persisted_restart", {"combo": mode, "probe_after_restart": persisted},
            str(mode.get("selected_index")) == str(ON) and drop_visible(persisted["drop"]) and persisted["pin_buttons_visible"] > 0)
    set_mode(rig, AUTO)  # 임시 data 라 남지 않지만 끝 상태를 기본으로 돌려 둔다


ITEMS = {"Q2": q2}


def self_test(args):
    fails = ri.self_test_names(REQ, args.session) + ri.self_test_gate(REQ, args.session, "desktop_work", list(REQ))
    on = {"policy": True, "targetVisible": True}
    off = {"policy": False, "targetVisible": False}
    if not drop_visible(on) or drop_visible(off) or drop_visible({"policy": True, "targetVisible": False}) or drop_visible(None):
        fails.append("안내 보임 판정 오류")
    if not drop_hidden(off) or drop_hidden(on) or drop_hidden({"policy": False, "targetVisible": True}) or drop_hidden(None):
        fails.append("안내 숨김 판정 오류")
    if pin_buttons_visible([{"visible": True, "pinVisible": True}, {"visible": True, "pinVisible": False}, {"visible": False, "pinVisible": True}]) != (1, 2):
        fails.append("핀 버튼 수 계산 오류")
    if pin_buttons_visible(None) != (0, 0):
        fails.append("빈 머리줄 계산 오류")
    if not pin_refused({"status": "Rejected", "pinned": False}) or pin_refused({"status": "Done", "pinned": True}) or pin_refused(None):
        fails.append("핀 거부 판정 오류")
    if not pin_accepted({"status": "Done", "pinned": True}) or pin_accepted({"status": "Rejected", "pinned": False}):
        fails.append("핀 수락 판정 오류")
    if len(REQ["Q2"]) != 11 or (AUTO, ON, OFF) != (0, 1, 2):
        fails.append("측정 이름 개수·콤보 순서")
    # 끌기 중 캡처 판정: 켬 검출 + 끔 0 + 제자리만 통과, 켬 미검출은 판정 불가(FAIL)
    det, none = {"changed_px_max": 2400, "accent_px_max": 900}, {"changed_px_max": 0, "accent_px_max": 0}
    for name, c, o, side, want_ok, want_und in (("정상", det, none, "Left", True, False), ("끔에서 안내", det, det, "Left", False, False),
                                                ("끔에서 작은 변화", det, {"changed_px_max": 12, "accent_px_max": 0}, "Left", False, False),
                                                ("켬 미검출", none, none, "Left", False, True), ("켬 강조색 없음", {"changed_px_max": 2400, "accent_px_max": 0}, none, "Left", False, True),
                                                ("끔인데 도킹됨", det, none, "Right", False, False), ("캡처 없음", None, None, "Left", False, True)):
        value, ok = judge_real_drag(c, o, side)
        if ok is not want_ok or value["undetermined"] is not want_und:
            fails.append("실끌기 캡처 판정 오류 %s: ok=%s undetermined=%s" % (name, ok, value["undetermined"]))
    acc = (51, 102, 204)
    base, frame = [(30, 30, 30)] * 4, [(30, 30, 30), (51, 102, 204), (55, 100, 200), (200, 200, 200)]
    if frame_counts(base, frame, acc) != (3, 2) or frame_counts(base, base, acc) != (0, 0) or frame_counts([(51, 102, 204)], [(51, 102, 204)], acc) != (0, 0):
        fails.append("프레임 변화·강조색 픽셀 계산 오류")
    from PIL import Image
    im0, im1 = Image.new("RGB", (4, 1)), Image.new("RGB", (4, 1))
    im0.putdata(base)
    im1.putdata(frame)
    if image_counts(im0, im1, acc) != (3, 2) or image_counts(im0, im0, acc) != (0, 0):
        fails.append("PIL 픽셀 계산이 목록 계산과 다름")
    im2 = Image.new("RGB", (2, 1))
    im2.putdata([(38, 59, 100), (90, 30, 30)])  # 강조색 40% 반투명 혼합(셈) · 빨강 쪽 변화(안 셈)
    if image_counts(Image.new("RGB", (2, 1), (30, 30, 30)), im2, acc) != (2, 1):
        fails.append("반투명 강조색 혼합 판정 오류")
    for f in fails:
        print("FAIL", f)
    print("self-test %s, 실패 %d건" % ("PASS" if not fails else "FAIL", len(fails)))
    return 1 if fails else 0


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ri.add_common_args(p)
    args = p.parse_args()
    if args.self_test:
        return self_test(args)
    if args.annotate:
        return ri.annotate(args)
    return ri.drive(args, ITEMS, REQ, "desktop_work", os.path.abspath(__file__))


if __name__ == "__main__":
    sys.exit(main())
