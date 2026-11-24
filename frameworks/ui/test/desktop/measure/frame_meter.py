# 창틀(제목색 띠·외곽선·모서리 반경·계단·밝은 띠)을 PNG 에서 수치로만 재는 측정기 (판정 없음, 입력 API 미사용)
"""frame_meter.py — 창 PNG 한 장에서 창틀 수치를 낸다. 판정은 하지 않고 수치와 근거만 JSON 으로 낸다.

입력
  --png <파일>            이미 있는 캡처 PNG (PrintWindow 비합성 또는 화면 캡처)
  --capture-hwnd <hwnd>   PrintWindow(PW_RENDERFULLCONTENT) 로 직접 캡처해 --out 에 저장 후 측정 (입력 API 미사용)
  --box L,T,R,B           창 바깥 경계(포함, 픽셀). 생략 시 PNG 전체
  --mode noncomposited|composited   비합성은 그림자가 없다. 합성 캡처는 그림자가 경계에 섞이므로 경고를 낸다
  --out <파일>            캡처 저장 경로(--capture-hwnd 필수) 또는 JSON 저장 경로(--json-out)

출력 JSON 키: titlebar_height_px, edge_title_band_px, outline_px, corner_radius_fit, arc_deviation_rows, bright_band, warnings, evidence
"""
import argparse
import json
import math
import os
import subprocess
import sys
import tempfile

from PIL import Image

TOL = 18          # 바탕과 다름 판정 허용 채널 차
BODY_TOL = 10     # 몸통과 다름 판정
RUN_TOL = 6       # 세로 단면 구간 분할 허용 차
ARC_DEV_PX = 1.0  # 단일 원호 이탈 판정: 피팅 잔차 절댓값이 이 값(px)을 넘는 행
ROWS = 16         # 모서리에서 읽는 행 수
SCAN = 24         # 모서리 행당 가로 탐색 폭


def lum(p):
    return 0.299 * p[0] + 0.587 * p[1] + 0.114 * p[2]


def diff(a, b):
    return max(abs(a[i] - b[i]) for i in range(4))


def capture(hwnd, out_png):
    """PrintWindow(PW_RENDERFULLCONTENT=2) 로 대상 창을 가려져도 그대로 캡처한다. 마우스·키 입력 API 는 쓰지 않는다."""
    ps = r'''
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System; using System.Runtime.InteropServices;
public class PwCap {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L,T,R,B; }
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint f);
}
"@
$h = [IntPtr][int64]%HWND%
$r = New-Object PwCap+RECT
if (-not [PwCap]::GetWindowRect($h, [ref]$r)) { exit 3 }
$w = $r.R - $r.L; $ht = $r.B - $r.T
$bmp = New-Object System.Drawing.Bitmap $w, $ht
$g = [System.Drawing.Graphics]::FromImage($bmp)
$dc = $g.GetHdc()
$ok = [PwCap]::PrintWindow($h, $dc, 2)
$g.ReleaseHdc($dc); $g.Dispose()
$bmp.Save("%OUT%", [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
if (-not $ok) { exit 4 }
'''
    win_out = subprocess.check_output(["wslpath", "-w", os.path.abspath(out_png)]).decode().strip()
    ps = ps.replace("%HWND%", str(int(hwnd))).replace("%OUT%", win_out)
    with tempfile.NamedTemporaryFile("w", suffix=".ps1", delete=False, encoding="utf-8") as f:
        f.write(ps)
        tmp = f.name
    try:
        win_tmp = subprocess.check_output(["wslpath", "-w", tmp]).decode().strip()
        rc = subprocess.call(["powershell.exe", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", win_tmp])
    finally:
        os.unlink(tmp)
    return rc


def corner_fit(dxs):
    """행별 dx(바닥에서 위로 k행, 화소 중심 높이 k+0.5)를 원 방정식 dx=r-sqrt(r^2-(r-h)^2) 에 최소제곱 피팅한다."""
    pts = [(k + 0.5, v) for k, v in enumerate(dxs) if v is not None]
    if not pts:
        return {"r": None, "rms": None, "n_rows": 0}
    best = (None, 1e18)
    r = 0.0
    while r <= 40.0:
        s = 0.0
        for h, v in pts:
            m = 0.0 if h >= r else r - math.sqrt(max(r * r - (r - h) ** 2, 0.0))
            s += (m - v) ** 2
        if s < best[1]:
            best = (r, s)
        r += 0.05
    r = best[0]
    res = []
    for h, v in pts:
        m = 0.0 if h >= r else r - math.sqrt(max(r * r - (r - h) ** 2, 0.0))
        res.append(round(v - m, 2))
    dev = sum(1 for e in res if abs(e) > ARC_DEV_PX)
    return {"r": round(r, 2), "rms": round(math.sqrt(best[1] / len(pts)), 3), "n_rows": len(pts),
            "residual_per_row": res, "arc_deviation_rows": dev}


def runs(col):
    """색 목록을 인접 색 차 RUN_TOL 이하로 이어지는 구간으로 나눈다."""
    out = []
    start = 0
    for i in range(1, len(col) + 1):
        if i == len(col) or diff(col[i], col[start]) > RUN_TOL:
            out.append({"from": start, "len": i - start, "rgb": list(col[start][:3])})
            start = i
    return out


def auto_box(im):
    """투명 여백(alpha<=16)을 뺀 불투명 화소 외접 사각형. 투명 화소가 없으면 None."""
    a = im.getchannel("A")
    if a.getextrema()[0] <= 16:
        return a.point(lambda v: 255 if v > 16 else 0).getbbox(), "auto_alpha_bbox"  # (l,t,r+1,b+1)
    # PrintWindow 는 여백을 불투명 검정(0,0,0,255)으로 남기기도 한다 → 모서리 화소가 순검정이면 그 색 여백을 뺀 외접 사각형
    W, H = im.size
    corner = im.getpixel((0, H - 1))
    if corner[:3] != (0, 0, 0):
        return None, None
    # 외톨이 화소·전폭 선이 여백에 끼어도 되도록 투영 비율(25% 이상 비검정)로 열→행 순서로 경계를 잡는다
    px = im.convert("RGB").load()
    nb = lambda x, y: max(px[x, y]) > TOL
    cols = [x for x in range(W) if sum(1 for y in range(H) if nb(x, y)) > H * 0.25]
    if not cols:
        return None, None
    cl, cr = cols[0], cols[-1]
    rows = [y for y in range(H) if sum(1 for x in range(cl, cr + 1) if nb(x, y)) > (cr - cl + 1) * 0.25]
    if not rows:
        return None, None
    return (cl, rows[0], cr + 1, rows[-1] + 1), "auto_black_margin_bbox"


def measure(path, box, mode):
    im = Image.open(path).convert("RGBA")
    px = im.load()
    W, H = im.size
    warnings = []
    box_source = "arg"
    if not box:
        bb, src = auto_box(im)
        if bb:
            box, box_source = (bb[0], bb[1], bb[2] - 1, bb[3] - 1), src
        else:
            box, box_source = (0, 0, W - 1, H - 1), "full_image"
    L, T, R, B = box
    res = {"png": os.path.abspath(path), "size": [W, H], "box": [L, T, R, B], "box_source": box_source, "mode": mode}
    if box_source.startswith("auto_"):
        warnings.append(f"--box 생략: 여백({box_source})을 뺀 외접 사각형을 창 경계로 자동 인식했다.")
    if mode == "composited":
        warnings.append("합성(화면) 캡처: 창 바깥 그림자가 경계 판정에 섞여 outline_px 는 상한(과대) 추정일 수 있다. 외곽선 폭은 비합성(PrintWindow) 캡처와 대조할 것.")
    ym, xm = (T + B) // 2, (L + R) // 2
    # 바탕 기준
    if L >= 3:
        ref, ref_src = px[L - 3, ym], "left_outside_3px"
    else:
        ref, ref_src = px[L, B], "bottom_left_corner_pixel"
        warnings.append("창 왼쪽 바깥 여백이 없어 모서리 화소(L,B)를 바탕으로 쓴다(반경 0 이면 오류).")
    res["bg_ref"] = {"rgba": list(ref), "source": ref_src}

    def side_rows(x0, step):
        rows = []
        for k in range(ROWS):
            y = B - k
            dx = None
            for i in range(SCAN):
                x = x0 + step * i
                if 0 <= x < W and diff(px[x, y], ref) > TOL:
                    dx = i
                    break
            rows.append(dx)
        return rows

    bl, br = side_rows(L, 1), side_rows(R, -1)
    fit_l, fit_r = corner_fit(bl), corner_fit(br)

    def stairs(rr):
        v = [x for x in rr if x is not None]
        return sum(1 for i in range(1, len(v)) if v[i] != v[i - 1])

    radius_valid = mode == "composited"
    if not radius_valid:
        warnings.append("비합성(PrintWindow) 캡처는 DWM 둥근 모서리를 담지 못한다. corner_radius_fit 값은 무효(valid=false)이며, 반경은 합성(화면) 캡처의 피팅 결과를 쓸 것.")
    res["corner_radius_fit"] = {"valid": radius_valid,
                                "valid_note": None if radius_valid else "noncomposited 캡처 — 반경 무효. composited 캡처로 재측정할 것",
                                "bottom_left": fit_l, "bottom_right": fit_r,
                                "model": "dx=r-sqrt(r^2-(r-h)^2), h=행 중심 높이(k+0.5), 최소제곱 r 격자 0.05",
                                "bl_row_dx_bottom_up": bl, "br_row_dx_bottom_up": br}
    res["arc_deviation_rows"] = {"bottom_left": fit_l["arc_deviation_rows"], "bottom_right": fit_r["arc_deviation_rows"],
                                 "valid": radius_valid,
                                 "definition": f"단일 원호 피팅 잔차 절댓값 > {ARC_DEV_PX}px 인 행 수(0 이면 매끈한 단일 원호, 클수록 계단·이탈)",
                                 "dx_level_changes_ref": {"bottom_left": stairs(bl), "bottom_right": stairs(br)}}
    # 몸통 색
    body = px[L + 10, ym] if R - L > 24 else px[xm, ym]
    res["body_rgb"] = list(body[:3])

    # 외곽선: 가장자리에서 안쪽으로 바탕/몸통과 모두 다른 연속 화소 수 (4변)
    def outline(getter, n=8):
        w = 0
        for i in range(n):
            p = getter(i)
            if diff(p, ref) > TOL and diff(p, body) > BODY_TOL:
                w += 1
            elif w:
                break
        return w

    res["outline_px"] = {
        "left": outline(lambda i: px[L + i, ym]),
        "right": outline(lambda i: px[R - i, ym]),
        "bottom": outline(lambda i: px[xm, B - i]),
        "top": outline(lambda i: px[xm, T + i]),
        "note": "바탕과 몸통 모두와 다른 연속 화소 수. 비합성 캡처는 그림자가 없어 외곽선만 남는다.",
    }
    if mode == "composited":
        res["outline_px"]["shadow_warning"] = True

    # 밝은 띠: 외곽선 바로 안쪽 3px 중 몸통보다 밝기 +12 이상
    def bright(getter, start):
        return sum(1 for i in range(start, start + 3) if lum(getter(i)) > lum(body) + 12)

    # 위쪽은 제목색 띠가 몸통과 달라 위 방식이 띠를 외곽선으로 센다 → 세로 단면의 선두 짧은 구간(길이 2 이하) 합으로 다시 잰다
    depth = min(80, B - T)
    col = [px[xm, T + i] for i in range(depth)]
    segs = runs(col)
    top_ol = 0
    for s in segs:
        if s["len"] <= 2:
            top_ol += s["len"]
        else:
            break
    res["outline_px"]["top"] = top_ol
    res["outline_px"]["top_note"] = "선두 짧은 구간(<=2px) 합. 제목색 띠(>=3px 균일 구간)는 titlebar_height_px 로 분리"
    ol = res["outline_px"]
    res["bright_band"] = {
        "left": bright(lambda i: px[L + i, ym], ol["left"]),
        "right": bright(lambda i: px[R - i, ym], ol["right"]),
        "bottom": bright(lambda i: px[xm, B - i], ol["bottom"]),
        "criterion": "외곽선 안쪽 3px 중 몸통 밝기+12 초과 화소 수",
    }

    # 제목색 띠: 위 가장자리 세로 단면을 구간으로 나눠, 외곽선 뒤 첫 구간(길이>=3)을 제목색 띠로 본다
    band = None
    for s in segs:
        if s["from"] >= ol["top"] and s["len"] >= 3:
            band = s
            break
    res["titlebar_height_px"] = {
        "value": band["len"] if band else None,
        "rgb": band["rgb"] if band else None,
        "starts_at_row": band["from"] if band else None,
        "note": "맨 위 제목줄 높이(좌·우·아래 테두리의 제목색 띠 폭은 edge_title_band_px). 가로 중앙 세로 단면의 첫 3px 이상 균일 구간 길이. 제목 글자·단추가 중앙에 걸리면 과소 → 근거 segments 확인",
        "segments_top_down": segs[:8],
    }
    # 몸통 대비 띠 색 차(제목색 띠가 몸통과 구분되는지)
    tc = band["rgb"] if band else None

    def edge_band(getter, start):
        if tc is None:
            return None
        n = 0
        for i in range(start, 40):
            p = getter(i)
            if diff(p, tuple(tc) + (255,)) <= TOL:
                n += 1
            elif n:
                break
        return n

    res["edge_title_band_px"] = {
        "left": edge_band(lambda i: px[L + i, ym], 0),
        "right": edge_band(lambda i: px[R - i, ym], 0),
        "bottom": edge_band(lambda i: px[xm, B - i], 0),
        "title_rgb": tc,
        "definition": "각 테두리에서 안쪽으로, 제목줄 색(titlebar_height_px.rgb)과 TOL 이내인 연속 화소 수(최초 일치 구간)",
    }
    res["evidence"] = {
        "right_profile_mid": [list(px[x, ym][:3]) for x in range(R - 8, min(R + 3, W))],
        "left_profile_mid": [list(px[x, ym][:3]) for x in range(max(L - 2, 0), L + 9)],
        "bottom_profile_mid": [list(px[xm, y][:3]) for y in range(B - 8, min(B + 3, H))],
    }
    res["warnings"] = warnings
    return res


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--png")
    ap.add_argument("--capture-hwnd", type=int)
    ap.add_argument("--out")
    ap.add_argument("--box")
    ap.add_argument("--mode", default="noncomposited", choices=["noncomposited", "composited"])
    ap.add_argument("--json-out")
    a = ap.parse_args()
    png = a.png
    if a.capture_hwnd:
        if not a.out:
            ap.error("--capture-hwnd 는 --out 필요")
        rc = capture(a.capture_hwnd, a.out)
        if rc != 0:
            print(json.dumps({"error": "capture_failed", "rc": rc}))
            return rc
        png = a.out
    if not png:
        ap.error("--png 또는 --capture-hwnd 필요")
    box = tuple(int(v) for v in a.box.split(",")) if a.box else None
    res = measure(png, box, a.mode)
    s = json.dumps(res, ensure_ascii=False, indent=1)
    if a.json_out:
        with open(a.json_out, "w", encoding="utf-8") as f:
            f.write(s)
    print(s)
    return 0


if __name__ == "__main__":
    sys.exit(main())
