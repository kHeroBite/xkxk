#!/usr/bin/env python3
# 글자 PNG 의 선명도 지표(중간 회색 비율·가장자리 기울기·색 번짐·잉크 양)를 수치로만 재는 측정기 (판정 없음, 입력 API 미사용)
"""글자 선명도 측정기 — 측정 계층(판정 없음).

사용:
  text_sharpness.py measure A.png [B.png ...] [--crop x,y,w,h]
      PNG 마다 지표 JSON 1줄. 배경은 네 모서리 화소 중앙값, 글자색은 배경과 가장 먼 화소(99.5 백분위)로 잡는다.
  text_sharpness.py matrix --out DIR [--fonts "굴림,돋움,맑은 고딕,나눔고딕,Segoe UI"] [--weights Normal,SemiBold]
                           [--size 14] [--mode unspecified] [--hinting unspecified] [--baseline unspecified] [--real]
                           [--tag 이름] [--text 문자열]
      실행 중인 진단 앱(원격 제어)에 text.render 를 보내 폰트×굵기 PNG 를 DIR 에 만들고 각각 measure 한 결과를
      DIR/<tag>.json 으로 남긴다. 연결 정보는 tools/pdctl.py 규칙(PD_REMOTE_INFO 우선)을 따른다.

지표(모두 글자 화소 = 덮임 c>=0.08 인 화소 기준, c = |L-배경|/|글자색-배경| 를 0~1 로 자름):
  gray_ratio  0.25<=c<=0.75 인 화소 비율 — 번짐(회색 테두리) 정도. 낮을수록 선명.
  solid_ratio c>=0.9 인 화소 비율 — 꽉 찬 획 비율. 높을수록 선명.
  edge_p80    덮임 기울기(|dx|+|dy|, 중앙 차분 아님 — 이웃 화소 차) 상위 20% 경계값. 높을수록 경계가 가파름.
  fringe      글자 화소의 (max(R,G,B)-min(R,G,B))/255 평균 — 서브픽셀(LCD) 색 번짐 여부.
  ink_mean    글자 화소의 평균 덮임 — 획 진하기.
  ink_px      글자 화소 수 — 획 굵기(합성 굵게면 커진다).
"""
import argparse
import json
import os
import subprocess
import sys

import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
PDCTL = os.path.normpath(os.path.join(HERE, "..", "..", "..", "tools", "pdctl.py"))
DEFAULT_FONTS = "굴림,돋움,맑은 고딕,나눔고딕,Segoe UI"


def measure(path, crop=None):
    img = Image.open(path).convert("RGB")
    if crop:
        x, y, w, h = crop
        img = img.crop((x, y, x + w, y + h))
    rgb = np.asarray(img).astype(np.float64)
    lum = rgb @ np.array([0.2126, 0.7152, 0.0722])
    h, w = lum.shape
    corners = np.array([lum[0, 0], lum[0, w - 1], lum[h - 1, 0], lum[h - 1, w - 1]])
    bg = float(np.median(corners))
    dist = np.abs(lum - bg)
    span = float(np.percentile(dist, 99.5))
    if span < 1.0:
        return {"file": os.path.abspath(path), "error": "글자 없음(배경과 차이 없음)"}
    cov = np.clip(dist / span, 0.0, 1.0)
    ink = cov >= 0.08
    n = int(ink.sum())
    if n == 0:
        return {"file": os.path.abspath(path), "error": "글자 화소 0"}
    gx = np.abs(np.diff(cov, axis=1, append=cov[:, -1:]))
    gy = np.abs(np.diff(cov, axis=0, append=cov[-1:, :]))
    grad = gx + gy
    edge = grad[grad > 0.05]
    chroma = (rgb.max(axis=2) - rgb.min(axis=2)) / 255.0
    return {
        "file": os.path.abspath(path),
        "size": [w, h],
        "bg": round(bg, 1),
        "ink_px": n,
        "gray_ratio": round(float(((cov >= 0.25) & (cov <= 0.75) & ink).sum()) / n, 4),
        "solid_ratio": round(float((cov >= 0.9).sum()) / n, 4),
        "edge_p80": round(float(np.percentile(edge, 80)) if edge.size else 0.0, 4),
        "fringe": round(float(chroma[ink].mean()), 4),
        "ink_mean": round(float(cov[ink].mean()), 4),
    }


def to_win(path):
    return subprocess.run(["wslpath", "-w", path], capture_output=True, text=True, check=True).stdout.strip()


def pdctl(cmd, **args):
    pairs = ["%s=%s" % (k, v) for k, v in args.items() if v is not None]
    proc = subprocess.run([sys.executable, PDCTL, cmd] + pairs, capture_output=True, text=True, timeout=60)
    try:
        return json.loads(proc.stdout)
    except ValueError:
        return {"ok": False, "raw": proc.stdout + proc.stderr}


def matrix(a):
    a.out = os.path.abspath(a.out)  # 앱이 Windows 경로로 저장하므로 상대 경로를 쓰면 앱 작업 폴더 기준이 된다
    os.makedirs(a.out, exist_ok=True)
    tag = a.tag or "%s_%s_%s%s" % (a.mode, a.hinting, a.baseline, "_real" if a.real else "")
    rows = []
    for font in [f.strip() for f in a.fonts.split(",") if f.strip()]:
        for weight in [w.strip() for w in a.weights.split(",") if w.strip()]:
            name = "%s_%s_%s.png" % (tag, font.replace(" ", ""), weight)
            png = os.path.join(a.out, name)
            res = pdctl("text.render", font=font, weight=weight, size=a.size, text=a.text, mode=a.mode,
                        hinting=a.hinting, baseline=a.baseline, real="true" if a.real else None, path=to_win(png))
            row = {"font": font, "weight": weight, "tag": tag}
            if not res.get("ok"):
                row["error"] = res
            else:
                r = res.get("result", {})
                row["typeface"] = r.get("typeface")
                row["requested"] = r.get("requested")
                row["usedWeight"] = r.get("usedWeight")
                row["metrics"] = measure(png)
            rows.append(row)
            print(json.dumps(row, ensure_ascii=False))
    with open(os.path.join(a.out, tag + ".json"), "w", encoding="utf-8") as f:
        json.dump(rows, f, ensure_ascii=False, indent=1)
    return 0


def main(argv):
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = p.add_subparsers(dest="cmd", required=True)
    m = sub.add_parser("measure")
    m.add_argument("files", nargs="+")
    m.add_argument("--crop")
    x = sub.add_parser("matrix")
    x.add_argument("--out", required=True)
    x.add_argument("--fonts", default=DEFAULT_FONTS)
    x.add_argument("--weights", default="Normal,SemiBold")
    x.add_argument("--size", default="14")
    x.add_argument("--text")
    x.add_argument("--mode", default="unspecified")
    x.add_argument("--hinting", default="unspecified")
    x.add_argument("--baseline", default="unspecified")
    x.add_argument("--real", action="store_true")
    x.add_argument("--tag")
    a = p.parse_args(argv)
    if a.cmd == "measure":
        crop = tuple(int(v) for v in a.crop.split(",")) if a.crop else None
        for f in a.files:
            print(json.dumps(measure(f, crop), ensure_ascii=False))
        return 0
    return matrix(a)


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
