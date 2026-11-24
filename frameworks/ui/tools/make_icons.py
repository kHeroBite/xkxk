# 원본 그림 한 장(assets/icon/source.png)으로 Windows·macOS·iOS·Android·웹 앱 아이콘 전체를 다시 만드는 생성 스크립트(임시 아이콘용)
"""
사용법(WSL·Windows 공통, Python 3 + Pillow):
    python3 tools/make_icons.py                  # 기본 원본 assets/icon/source.png
    python3 tools/make_icons.py --source 새그림.png  # 원본 교체(새 그림은 assets/icon/source.png 로 복사된다)
    python3 tools/make_icons.py --radius 0.2       # 모서리 반경 비율만 바꿔 다시 만들기

아이콘을 바꿀 때는 원본 한 장만 바꾸고 이 스크립트를 다시 돌린다. 산출물 경로·파일명은 고정이라 csproj·매니페스트·index.html 은 손대지 않아도 된다.
Pillow 가 없으면: python3 -m pip install --user Pillow (root·apt 불필요)
"""
import argparse
import json
import os
import shutil
import sys

from PIL import Image, ImageDraw

# ── 설정값 ──────────────────────────────────────────────────────────────
# 모서리 반경 비율 = 반경 / 아이콘 한 변. 사용자 참고 이미지(40×39 앱 아이콘, 몸체 24px)를 화소 단위로 재어
# 모서리 첫 세 줄의 안쪽 들여쓰기가 2·1·0px 이었다 → 반경 약 4px/24px ≈ 0.167. 반올림해 0.17 로 둔다.
RADIUS_RATIO = 0.17
# 비정사각형 원본 맞춤 방식: "crop"(가운데 정사각 자르기) | "pad"(가운데 두고 가장자리 색으로 여백 채우기)
# 첫 원본(258×243)은 좌우 끝 8열이 단색(왼쪽 파랑·오른쪽 노랑)이라 가운데를 243×243 으로 잘라도 그림 손실이 없다.
# pad 는 위아래에 띠가 생기고 그림이 작아져서 crop 을 기본으로 한다.
FIT_MODE = "crop"
SUPERSAMPLE = 4  # 둥근 마스크 계단 현상 방지용 확대 배율

TOOLS_DIR = os.path.dirname(os.path.abspath(__file__))
UI_DIR = os.path.dirname(TOOLS_DIR)
SRC_DIR = os.path.join(UI_DIR, "src")
ASSET_DIR = os.path.join(UI_DIR, "assets", "icon")
DEFAULT_SOURCE = os.path.join(ASSET_DIR, "source.png")
PREVIEW_DIR = os.path.join(UI_DIR, "logs", "odevic", "preview")

# Windows .ico 크기: 요청 목록(16·20·24·32·40·48·64·256)에 Microsoft Learn 표의 작업표시줄 배율 크기(30·36·60·72·96)와 80 을 더했다.
# Windows 는 정확히 같은 크기를 먼저 찾고 없으면 한 단계 큰 것을 줄인다(Microsoft Learn "Icon scaling").
ICO_SIZES = [16, 20, 24, 30, 32, 36, 40, 48, 60, 64, 72, 80, 96, 256]
ICNS_SIZES = [16, 32, 64, 128, 256, 512, 1024]
# Android 밀도별 크기: 런처 아이콘 48dp, adaptive 레이어 108dp (mdpi=1x, hdpi=1.5x, xhdpi=2x, xxhdpi=3x, xxxhdpi=4x)
ANDROID_DENSITIES = {"mdpi": 1.0, "hdpi": 1.5, "xhdpi": 2.0, "xxhdpi": 3.0, "xxxhdpi": 4.0}


def load_square(path):
    """원본을 RGBA 로 읽어 FIT_MODE 에 따라 정사각형으로 맞춘다."""
    img = Image.open(path).convert("RGBA")
    w, h = img.size
    if w == h:
        return img
    side = min(w, h) if FIT_MODE == "crop" else max(w, h)
    if FIT_MODE == "crop":
        left, top = (w - side) // 2, (h - side) // 2
        return img.crop((left, top, left + side, top + side))
    canvas = Image.new("RGBA", (side, side), img.getpixel((0, 0)))
    canvas.paste(img, ((side - w) // 2, (side - h) // 2))
    return canvas


def art(square, size):
    """정사각 그림을 size 로 리샘플(불투명, 둥글림 없음)."""
    return square.resize((size, size), Image.LANCZOS)


def shape_mask(size, kind, radius_ratio):
    """size 크기의 알파 마스크. kind: rounded | circle. SUPERSAMPLE 배로 그린 뒤 줄여 가장자리를 부드럽게 한다."""
    big = size * SUPERSAMPLE
    m = Image.new("L", (big, big), 0)
    d = ImageDraw.Draw(m)
    if kind == "circle":
        d.ellipse((0, 0, big - 1, big - 1), fill=255)
    else:
        d.rounded_rectangle((0, 0, big - 1, big - 1), radius=round(big * radius_ratio), fill=255)
    return m.resize((size, size), Image.LANCZOS)


def masked(square, size, kind="rounded", radius_ratio=None):
    img = art(square, size)
    img.putalpha(shape_mask(size, kind, RADIUS_RATIO if radius_ratio is None else radius_ratio))
    return img


def opaque(square, size):
    """iOS·apple-touch-icon·maskable 용: 둥글리지 않은 불투명 RGB 정사각형(OS 가 모서리를 깎는다)."""
    return art(square, size).convert("RGB")


def bleed(square, size, inner_ratio):
    """가운데 inner_ratio 크기로 그림을 두고 바깥은 가장자리 화소를 늘려 채운 불투명 정사각형(Android adaptive 전경)."""
    inner = round(size * inner_ratio)
    pad0 = (size - inner) // 2
    pad1 = size - inner - pad0
    a = art(square, inner)
    out = Image.new("RGBA", (size, size))
    out.paste(a, (pad0, pad0))
    # 좌우·상하 띠: 가장자리 1화소 줄을 띠 폭만큼 늘린다
    out.paste(a.crop((0, 0, 1, inner)).resize((pad0, inner), Image.NEAREST), (0, pad0))
    out.paste(a.crop((inner - 1, 0, inner, inner)).resize((pad1, inner), Image.NEAREST), (pad0 + inner, pad0))
    out.paste(a.crop((0, 0, inner, 1)).resize((inner, pad0), Image.NEAREST), (pad0, 0))
    out.paste(a.crop((0, inner - 1, inner, inner)).resize((inner, pad1), Image.NEAREST), (pad0, pad0 + inner))
    # 네 귀퉁이: 모서리 화소 색
    for (cx, cy, px, py, w, h) in [
        (0, 0, 0, 0, pad0, pad0),
        (inner - 1, 0, pad0 + inner, 0, pad1, pad0),
        (0, inner - 1, 0, pad0 + inner, pad0, pad1),
        (inner - 1, inner - 1, pad0 + inner, pad0 + inner, pad1, pad1),
    ]:
        out.paste(Image.new("RGBA", (w, h), a.getpixel((cx, cy))), (px, py))
    return out


def ensure(path):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    return path


def save_png(img, path, written):
    img.save(ensure(path), "PNG", optimize=True)
    written.append(path)


def make_windows(square, written):
    """Desktop 머리: 다중 해상도 .ico — exe(ApplicationIcon)·창(기본 창 아이콘)·트레이가 같이 쓴다."""
    frames = [masked(square, s) for s in ICO_SIZES]
    path = ensure(os.path.join(SRC_DIR, "xkxk.frameworks.ui.desktop", "Assets", "pd.ico"))
    frames[-1].save(path, format="ICO", sizes=[(s, s) for s in ICO_SIZES], append_images=frames[:-1])
    written.append(path)


def make_macos(square, written):
    """macOS .icns(번들 미구성 — 파일만 준비). 1024 캔버스 가운데 824 둥근 사각형(바깥 100px 투명 여백).
    824/1024 여백은 Apple 디자인 리소스 템플릿 값으로 알려진 수치(추정)이다. 정사각형 1024 원본도 같이 둔다(자산 카탈로그·Icon Composer 용)."""
    def mac_frame(size):
        body = round(size * 824 / 1024)
        canvas = Image.new("RGBA", (size, size), (0, 0, 0, 0))
        canvas.paste(masked(square, body), ((size - body) // 2, (size - body) // 2))
        return canvas
    frames = {s: mac_frame(s) for s in ICNS_SIZES}
    path = ensure(os.path.join(ASSET_DIR, "macos", "pd.icns"))
    frames[1024].save(path, format="ICNS", append_images=[frames[s] for s in ICNS_SIZES if s != 1024])
    written.append(path)
    save_png(frames[1024], os.path.join(ASSET_DIR, "macos", "pd-1024-rounded.png"), written)
    save_png(opaque(square, 1024), os.path.join(ASSET_DIR, "macos", "pd-1024-square.png"), written)


def make_ios(square, written):
    """iOS AppIcon 세트: 1024 단일 크기(universal), 둥글림 없음·알파 없음(HIG: 시스템이 마스크로 모서리를 만든다)."""
    d = os.path.join(ASSET_DIR, "ios", "AppIcon.appiconset")
    save_png(opaque(square, 1024), os.path.join(d, "AppIcon-1024.png"), written)
    contents = {
        "images": [{"filename": "AppIcon-1024.png", "idiom": "universal", "platform": "ios", "size": "1024x1024"}],
        "info": {"author": "xcode", "version": 1},
    }
    p = os.path.join(d, "Contents.json")
    with open(p, "w", encoding="utf-8", newline="\n") as f:
        json.dump(contents, f, indent=2)
        f.write("\n")
    written.append(p)


def make_android(square, written):
    """Android 머리 Resources: 밀도별 ic_launcher(둥근 사각)·ic_launcher_round(원) + adaptive(API 26+) 전경 PNG·XML·배경색."""
    res = os.path.join(SRC_DIR, "xkxk.frameworks.ui.android", "Resources")
    for name, scale in ANDROID_DENSITIES.items():
        d = os.path.join(res, "mipmap-" + name)
        legacy = round(48 * scale)
        layer = round(108 * scale)
        save_png(masked(square, legacy), os.path.join(d, "ic_launcher.png"), written)
        save_png(masked(square, legacy, "circle"), os.path.join(d, "ic_launcher_round.png"), written)
        # 전경 레이어 108dp: 그림 전체가 가운데 72dp 보이는 영역에 오도록 두고 바깥 18dp 는 가장자리 색으로 늘린다.
        # 안전 영역(가운데 66dp)은 72dp 안에 들어가므로 원형 마스크에서도 그림 중앙(곡선)이 잘리지 않는다.
        save_png(bleed(square, layer, 72 / 108), os.path.join(d, "ic_launcher_foreground.png"), written)
    any_dir = os.path.join(res, "mipmap-anydpi-v26")
    adaptive = (
        '<?xml version="1.0" encoding="utf-8"?>\n'
        "<!-- 임시 앱 아이콘(adaptive, API 26+) — tools/make_icons.py 가 생성. 전경이 불투명 전체 그림이라 배경색은 가장자리 보정용 -->\n"
        '<adaptive-icon xmlns:android="http://schemas.android.com/apk/res/android">\n'
        '  <background android:drawable="@color/ic_launcher_background" />\n'
        '  <foreground android:drawable="@mipmap/ic_launcher_foreground" />\n'
        "</adaptive-icon>\n"
    )
    for fname in ("ic_launcher.xml", "ic_launcher_round.xml"):
        p = ensure(os.path.join(any_dir, fname))
        with open(p, "w", encoding="utf-8", newline="\n") as f:
            f.write(adaptive)
        written.append(p)
    r, g, b, _ = square.getpixel((0, square.height // 2))
    p = ensure(os.path.join(res, "values", "ic_launcher_background.xml"))
    with open(p, "w", encoding="utf-8", newline="\n") as f:
        f.write(
            '<?xml version="1.0" encoding="utf-8"?>\n'
            "<!-- adaptive 아이콘 배경색 — tools/make_icons.py 가 원본 왼쪽 가장자리 색으로 생성 -->\n"
            "<resources>\n"
            f'  <color name="ic_launcher_background">#{r:02X}{g:02X}{b:02X}</color>\n'
            "</resources>\n"
        )
    written.append(p)


def make_web(square, written):
    """Browser 머리 wwwroot: favicon.ico(16·32·48), PNG(16·32·192·512 둥근 사각), apple-touch-icon 180·maskable 512(불투명 정사각)."""
    www = os.path.join(SRC_DIR, "xkxk.frameworks.ui.browser", "wwwroot")
    fav = [masked(square, s) for s in (16, 32, 48)]
    p = ensure(os.path.join(www, "favicon.ico"))
    fav[-1].save(p, format="ICO", sizes=[(16, 16), (32, 32), (48, 48)], append_images=fav[:-1])
    written.append(p)
    for s in (16, 32, 192, 512):
        save_png(masked(square, s), os.path.join(www, "icons", f"icon-{s}.png"), written)
    save_png(opaque(square, 180), os.path.join(www, "icons", "apple-touch-icon.png"), written)
    # maskable: 안전 영역은 가운데 반지름 40% 원(web.dev). 그림이 꽉 찬 추상 도형이라 그대로 불투명 정사각으로 둔다.
    save_png(opaque(square, 512), os.path.join(www, "icons", "icon-maskable-512.png"), written)


def make_previews(square, written):
    """보고서용 미리보기: 어두운·밝은 바탕 위 크기별 Windows 아이콘, Android 마스크 모의, iOS·macOS 모의."""
    previews = {}
    # 1) Windows 크기별(밝은/어두운 바탕)
    sizes = [16, 24, 32, 48, 64, 96, 256]
    w = sum(sizes) + 20 * (len(sizes) + 1)
    sheet = Image.new("RGBA", (w, 2 * (256 + 40)), (0, 0, 0, 0))
    for row, bg in enumerate([(243, 243, 243, 255), (32, 32, 32, 255)]):
        band = Image.new("RGBA", (w, 256 + 40), bg)
        x = 20
        for s in sizes:
            band.alpha_composite(masked(square, s), (x, 20 + (256 - s)))
            x += s + 20
        sheet.paste(band, (0, row * (256 + 40)))
    previews["windows_sizes.png"] = sheet
    # 2) Android: 전경 레이어에 원형·스퀴클·둥근사각 마스크를 씌운 모의(안쪽 72dp 보이는 영역 기준)
    layer = 432
    fg = bleed(square, layer, 72 / 108)
    view = fg.crop((72, 72, 360, 360))  # 가운데 72dp(=288px@4x)
    tiles = [masked(view, 288, "circle"), masked(view, 288, "rounded", 0.30), masked(view, 288, "rounded", 0.12),
             masked(square, 192), masked(square, 192, "circle")]
    sheet2 = Image.new("RGBA", (288 * 3 + 192 * 2 + 20 * 6, 328), (250, 250, 250, 255))
    x = 20
    for t in tiles:
        sheet2.alpha_composite(t, (x, 20 + (288 - t.height)))
        x += t.width + 20
    previews["android_masks.png"] = sheet2
    # 3) iOS(시스템 마스크 모의 — 둥근 사각 근사) · macOS(824 몸체)
    ios = masked(Image.open(os.path.join(ASSET_DIR, "ios", "AppIcon.appiconset", "AppIcon-1024.png")).convert("RGBA"), 256, "rounded", 0.2237)
    mac = Image.open(os.path.join(ASSET_DIR, "macos", "pd-1024-rounded.png")).convert("RGBA").resize((256, 256), Image.LANCZOS)
    sheet3 = Image.new("RGBA", (256 * 2 + 60, 296), (236, 236, 236, 255))
    sheet3.alpha_composite(ios, (20, 20))
    sheet3.alpha_composite(mac, (296, 20))
    previews["ios_macos.png"] = sheet3
    previews["icon_256.png"] = masked(square, 256)
    for name, img in previews.items():
        save_png(img, os.path.join(PREVIEW_DIR, name), written)


def main():
    global RADIUS_RATIO
    ap = argparse.ArgumentParser(description="원본 한 장으로 전 플랫폼 앱 아이콘 생성")
    ap.add_argument("--source", help="새 원본 그림(지정하면 assets/icon/source.png 로 복사한 뒤 생성)")
    ap.add_argument("--radius", type=float, help=f"모서리 반경 비율(기본 {RADIUS_RATIO})")
    ap.add_argument("--no-preview", action="store_true", help="미리보기 PNG 생략")
    a = ap.parse_args()
    if a.radius is not None:
        RADIUS_RATIO = a.radius
    if a.source:
        os.makedirs(ASSET_DIR, exist_ok=True)
        if os.path.abspath(a.source) != os.path.abspath(DEFAULT_SOURCE):
            shutil.copyfile(a.source, DEFAULT_SOURCE)
    if not os.path.exists(DEFAULT_SOURCE):
        sys.exit(f"원본 없음: {DEFAULT_SOURCE}")
    square = load_square(DEFAULT_SOURCE)
    if square.width < 1024:
        print(f"[경고] 원본 정사각 {square.width}px < 1024px — 큰 크기(256·512·1024)는 확대라 가장자리가 흐려진다. 교체 때 1024px 이상 원본 권장")
    written = []
    make_windows(square, written)
    make_macos(square, written)
    make_ios(square, written)
    make_android(square, written)
    make_web(square, written)
    if not a.no_preview:
        make_previews(square, written)
    for p in written:
        print(os.path.relpath(p, UI_DIR))
    print(f"완료: {len(written)}개, RADIUS_RATIO={RADIUS_RATIO}, FIT_MODE={FIT_MODE}, 원본 정사각 {square.width}px")


if __name__ == "__main__":
    main()
