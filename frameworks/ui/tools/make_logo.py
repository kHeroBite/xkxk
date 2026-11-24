# 새 PD 로고 원본 한 장으로 전 플랫폼 아이콘(단순화 판·알림 아이콘·제목줄 로고 포함)을 지정한 출력 트리에 만드는 변환 스크립트
"""
python3 poc/ui/tools/make_logo.py --out <출력폴더> --preview <미리보기폴더> [--src <원본.png>] [--simple-max 40] [--preview-only]
- 원본 기본값: poc/ui/assets/logo/PD_Dimension_source.png
- 출력 트리는 poc/ui 아래 상대 경로를 그대로 흉내 낸다(<출력폴더>/poc/ui/...). 기존 tools/make_icons.py 와 같은 규격·파일명을 쓴다.
  프로젝트 반영은 출력 트리의 파일을 같은 상대 경로로 복사한다(Android 배경색은 출력의 ANDROID_BG 값으로 values/ic_launcher_background.xml 을 고친다).
- 작은 크기(<= simple-max, 기본 40)는 퍼즐 선을 지우고 두 색 면 곡선만 남긴 단순화 판을 쓴다. 제목줄 로고(pd-logo-64.png)는 항상 단순화 판이다.
- Pillow·numpy 필요: python3 -m pip install --user Pillow numpy
"""
import argparse
import json
import os
import shutil

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

RADIUS_RATIO = 0.17  # 기존 make_icons.py 와 같은 값
SUPERSAMPLE = 4
ICO_SIZES = [16, 20, 24, 30, 32, 36, 40, 48, 60, 64, 72, 80, 96, 128, 256]  # 기존 14종 + 요청 128
ICNS_SIZES = [16, 32, 64, 128, 256, 512, 1024]
ANDROID = {"mdpi": 1.0, "hdpi": 1.5, "xhdpi": 2.0, "xxhdpi": 3.0, "xxxhdpi": 4.0}
FAV_SIZES = [16, 32, 48]


def blue_mask(src):
    a = np.asarray(src.convert("RGB")).astype(np.int32)
    return a[..., 2] > a[..., 0]  # 파랑 면: B > R


def simplify(src):
    """주황 면을 퍼즐 선 없는 2차 그라데이션으로 다시 칠한다(최소제곱 맞춤, 선 화소는 반복 제외)."""
    a = np.asarray(src.convert("RGB")).astype(np.float64)
    h, w, _ = a.shape
    blue = blue_mask(src)
    yy, xx = np.mgrid[0:h, 0:w]
    x = xx / w
    y = yy / h
    feats = np.stack([np.ones_like(x), x, y, x * x, y * y, x * y], -1)
    use = ~blue
    for _ in range(3):
        A = feats[use]
        B = a[use]
        coef, *_ = np.linalg.lstsq(A, B, rcond=None)
        fit = feats @ coef
        err = np.abs(a - fit).sum(-1)
        use = (~blue) & (err < 25)
    out = a.copy()
    out[~blue] = fit[~blue]
    # 파랑 면: 퍼즐 선 끝 자국(경계·왼쪽 가장자리 점)만 파랑 맞춤색으로 덮는다
    useb = blue.copy()
    for _ in range(3):
        coefb, *_ = np.linalg.lstsq(feats[useb], a[useb], rcond=None)
        fitb = feats @ coefb
        errb = np.abs(a - fitb).sum(-1)
        useb = blue & (errb < 25)
    fix = blue & (errb >= 25)
    out[fix] = fitb[fix]
    return Image.fromarray(np.clip(out, 0, 255).astype(np.uint8), "RGB").convert("RGBA")


def shape_mask(size, kind="rounded", radius_ratio=RADIUS_RATIO):
    big = size * SUPERSAMPLE
    m = Image.new("L", (big, big), 0)
    d = ImageDraw.Draw(m)
    if kind == "circle":
        d.ellipse((0, 0, big - 1, big - 1), fill=255)
    else:
        d.rounded_rectangle((0, 0, big - 1, big - 1), radius=round(big * radius_ratio), fill=255)
    return m.resize((size, size), Image.LANCZOS)


class Art:
    def __init__(self, full, simple, simple_max):
        self.full = full.convert("RGBA")
        self.simple = simple
        self.simple_max = simple_max

    def pick(self, size):
        return self.simple if size <= self.simple_max else self.full

    def art(self, size):
        return self.pick(size).resize((size, size), Image.LANCZOS)

    def masked(self, size, kind="rounded", radius_ratio=RADIUS_RATIO):
        img = self.art(size)
        img.putalpha(shape_mask(size, kind, radius_ratio))
        return img

    def opaque(self, size):
        return self.art(size).convert("RGB")

    def bleed(self, size, inner_ratio):
        inner = round(size * inner_ratio)
        p0 = (size - inner) // 2
        p1 = size - inner - p0
        a = self.art(inner)
        out = Image.new("RGBA", (size, size))
        out.paste(a, (p0, p0))
        out.paste(a.crop((0, 0, 1, inner)).resize((p0, inner), Image.NEAREST), (0, p0))
        out.paste(a.crop((inner - 1, 0, inner, inner)).resize((p1, inner), Image.NEAREST), (p0 + inner, p0))
        out.paste(a.crop((0, 0, inner, 1)).resize((inner, p0), Image.NEAREST), (p0, 0))
        out.paste(a.crop((0, inner - 1, inner, inner)).resize((inner, p1), Image.NEAREST), (p0, p0 + inner))
        for (cx, cy, px, py, w, h) in [(0, 0, 0, 0, p0, p0), (inner - 1, 0, p0 + inner, 0, p1, p0),
                                       (0, inner - 1, 0, p0 + inner, p0, p1), (inner - 1, inner - 1, p0 + inner, p0 + inner, p1, p1)]:
            out.paste(Image.new("RGBA", (w, h), a.getpixel((cx, cy))), (px, py))
        return out


def notif_master(src, size=960):
    """알림 아이콘 원판: 흰색 둥근 사각을 원본 곡선 경계 자리에서 투명 홈으로 가른 실루엣(살아있는 영역 20/24)."""
    work = 480
    m = Image.fromarray((blue_mask(src.resize((work, work), Image.LANCZOS)) * 255).astype(np.uint8), "L")
    k = 37  # 홈 폭 ≈ 36/480 = 7.5%
    band = ImageChops_sub(m.filter(ImageFilter.MaxFilter(k)), m.filter(ImageFilter.MinFilter(k)))
    body_alpha = shape_mask(work, "rounded", 0.20)
    alpha = np.asarray(body_alpha).astype(np.int32) - np.asarray(band).astype(np.int32)
    alpha = Image.fromarray(np.clip(alpha, 0, 255).astype(np.uint8), "L").filter(ImageFilter.GaussianBlur(1.2))
    body = round(size * 20 / 24)
    canvas = Image.new("L", (size, size), 0)
    canvas.paste(alpha.resize((body, body), Image.LANCZOS), ((size - body) // 2, (size - body) // 2))
    out = Image.new("RGBA", (size, size), (255, 255, 255, 0))
    out.putalpha(canvas)
    return out


def ImageChops_sub(a, b):
    return Image.fromarray(np.clip(np.asarray(a).astype(np.int32) - np.asarray(b).astype(np.int32), 0, 255).astype(np.uint8), "L")


def save_png(img, path, written):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    img.save(path, "PNG", optimize=True)
    written.append(path)


def build(art, src_path, src_img, out, written):
    ui = os.path.join(out, "poc", "ui")
    srcd = os.path.join(ui, "src")
    asset = os.path.join(ui, "assets", "icon")
    # 원본 사본(바이트 그대로)
    for p in [os.path.join(ui, "assets", "logo", "PD_Dimension_source.png"), os.path.join(asset, "source.png")]:
        os.makedirs(os.path.dirname(p), exist_ok=True)
        shutil.copyfile(src_path, p)
        written.append(p)
    # Windows .ico
    frames = [art.masked(s) for s in ICO_SIZES]
    p = os.path.join(srcd, "Pd.Poc.Desktop", "Assets", "pd.ico")
    os.makedirs(os.path.dirname(p), exist_ok=True)
    frames[-1].save(p, format="ICO", sizes=[(s, s) for s in ICO_SIZES], append_images=frames[:-1])
    written.append(p)
    # macOS
    def mac_frame(size):
        body = round(size * 824 / 1024)
        c = Image.new("RGBA", (size, size), (0, 0, 0, 0))
        c.paste(art.masked(body), ((size - body) // 2, (size - body) // 2))
        return c
    mf = {s: mac_frame(s) for s in ICNS_SIZES}
    p = os.path.join(asset, "macos", "pd.icns")
    os.makedirs(os.path.dirname(p), exist_ok=True)
    mf[1024].save(p, format="ICNS", append_images=[mf[s] for s in ICNS_SIZES if s != 1024])
    written.append(p)
    save_png(mf[1024], os.path.join(asset, "macos", "pd-1024-rounded.png"), written)
    save_png(art.opaque(1024), os.path.join(asset, "macos", "pd-1024-square.png"), written)
    # iOS (Contents.json 은 기존과 동일 내용이라 손대지 않음)
    save_png(art.opaque(1024), os.path.join(asset, "ios", "AppIcon.appiconset", "AppIcon-1024.png"), written)
    # Android 런처·adaptive 전경·알림
    res = os.path.join(srcd, "Pd.Poc.Android", "Resources")
    nm = notif_master(src_img)
    for name, sc in ANDROID.items():
        d = os.path.join(res, "mipmap-" + name)
        legacy, layer = round(48 * sc), round(108 * sc)
        save_png(art.masked(legacy), os.path.join(d, "ic_launcher.png"), written)
        save_png(art.masked(legacy, "circle"), os.path.join(d, "ic_launcher_round.png"), written)
        save_png(art.bleed(layer, 72 / 108), os.path.join(d, "ic_launcher_foreground.png"), written)
        n = round(24 * sc)
        save_png(nm.resize((n, n), Image.LANCZOS), os.path.join(res, "drawable-" + name, "ic_stat_pd.png"), written)
    r, g, b, _ = art.full.getpixel((0, art.full.height // 2))
    bg = f"#{r:02X}{g:02X}{b:02X}"
    # 웹
    www = os.path.join(srcd, "Pd.Poc.Browser", "wwwroot")
    fav = [art.masked(s) for s in FAV_SIZES]
    p = os.path.join(www, "favicon.ico")
    os.makedirs(os.path.dirname(p), exist_ok=True)
    fav[-1].save(p, format="ICO", sizes=[(s, s) for s in FAV_SIZES], append_images=fav[:-1])
    written.append(p)
    for s in (16, 32, 192, 512):
        save_png(art.masked(s), os.path.join(www, "icons", f"icon-{s}.png"), written)
    save_png(art.opaque(180), os.path.join(www, "icons", "apple-touch-icon.png"), written)
    save_png(art.opaque(512), os.path.join(www, "icons", "icon-maskable-512.png"), written)
    # 제목줄 컬러 로고(16·11px 표시용, 항상 단순화 판)
    title = Art(art.full, art.simple, 9999)
    save_png(title.masked(64), os.path.join(srcd, "Pd.Poc.Shell", "Assets", "Logo", "pd-logo-64.png"), written)
    return bg


def previews(full, simple, nm, pdir, written):
    full = full.convert("RGBA")
    sizes = [16, 24, 32, 48]
    def m(img, s):
        a = img.resize((s, s), Image.LANCZOS)
        a.putalpha(shape_mask(s))
        return a
    for s in sizes:
        zoom = 8 if s <= 32 else 4
        Z = s * zoom
        sheet = Image.new("RGBA", ((Z + 20) * 2 + 20 + (s + 20) * 2, Z + 40), (243, 243, 243, 255))
        x = 20
        for img in (full, simple):
            sheet.alpha_composite(m(img, s).resize((Z, Z), Image.NEAREST), (x, 20))
            x += Z + 20
        for img in (full, simple):
            sheet.alpha_composite(m(img, s), (x, 20))
            x += s + 20
        save_png(sheet, os.path.join(pdir, f"preview_{s}px_full_vs_simple.png"), written)
    # 실제 크기 비교(밝은·어두운 바탕), 위 줄 원본 축소, 아래 줄 단순화
    W = sum(sizes) + 20 * (len(sizes) + 1)
    sheet = Image.new("RGBA", (W * 2, 2 * (48 + 40)), (0, 0, 0, 0))
    for col, bgc in enumerate([(243, 243, 243, 255), (32, 32, 32, 255)]):
        band = Image.new("RGBA", (W, 2 * (48 + 40)), bgc)
        for row, img in enumerate((full, simple)):
            x = 20
            for s in sizes:
                band.alpha_composite(m(img, s), (x, 20 + row * 88 + (48 - s)))
                x += s + 20
        sheet.paste(band, (col * W, 0))
    save_png(sheet, os.path.join(pdir, "actual_sizes_full_top_simple_bottom.png"), written)
    save_png(simple.resize((512, 512), Image.LANCZOS), os.path.join(pdir, "simple_512.png"), written)
    nsheet = Image.new("RGBA", (20 + sum(s + 20 for s in (24, 36, 48, 72, 96)) + 120, 136), (60, 60, 60, 255))
    x = 20
    for s in (24, 36, 48, 72, 96):
        nsheet.alpha_composite(nm.resize((s, s), Image.LANCZOS), (x, 20))
        x += s + 20
    nsheet.alpha_composite(nm.resize((24, 24), Image.LANCZOS).resize((96, 96), Image.NEAREST), (x, 20))
    save_png(nsheet, os.path.join(pdir, "android_notification.png"), written)


def main():
    ap = argparse.ArgumentParser()
    default_src = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "assets", "logo", "PD_Dimension_source.png")
    ap.add_argument("--src", default=default_src)
    ap.add_argument("--out", required=True)
    ap.add_argument("--preview", required=True)
    ap.add_argument("--simple-max", type=int, default=40)
    ap.add_argument("--preview-only", action="store_true")
    a = ap.parse_args()
    src = Image.open(a.src).convert("RGB")
    simple = simplify(src)
    nm = notif_master(src)
    written = []
    previews(src, simple, nm, a.preview, written)
    if not a.preview_only:
        bg = build(Art(src, simple, a.simple_max), a.src, src, a.out, written)
        print("ANDROID_BG", bg)
    for p in written:
        print(p)
    print("완료", len(written), "simple_max", a.simple_max)


if __name__ == "__main__":
    main()
