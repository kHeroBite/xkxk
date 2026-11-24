# 웹·Android 임베드 한글 글꼴(Noto Sans KR Regular 부분집합)을 다시 만드는 스크립트 — 기존 부분집합의 글자 전체 + PMS 표 기호(통화·단위·①~⑳·✓ 등)
# 소스: Noto Sans KR 가변 글꼴 NotoSansKR-VF.ttf, Version 2.04;241114210130(Windows Fonts 의 것), wght 400 고정(Regular). 라이선스 SIL OFL 1.1.
# 추가 범위: U+2010–206F 일반 구두점 · U+20A0–20CF 통화 · U+2100–214F 문자형 기호 · U+2190–21FF 화살표 · U+2460–2473 ①~⑳ · U+3380–33FF CJK 단위
#            + 개별 ✓ U+2713 · ≤ U+2264 · ≥ U+2265 · ≠ U+2260 · ∞ U+221E · ★ U+2605 · ☆ U+2606 · ㈜ U+321C (lead 12:1x 승인 중간안, +175자 · +17,772B)
"""
사용:
  python3 tools/build_font_subset.py <원본 가변 글꼴 NotoSansKR-VF.ttf> [출력 경로]
  예) python3 tools/build_font_subset.py /mnt/c/Windows/Fonts/NotoSansKR-VF.ttf
  출력 기본값: src/xkxk.frameworks.ui.browser/Assets/Fonts/NotoSansKR-Regular-subset.ttf (Android·Shell 벤치가 링크로 같은 파일을 쓴다)

방법(2026-10-29 odev-3 확인):
  1) 가변 글꼴(Version 2.04, wght 100~900)을 wght=400 으로 고정(fontTools.varLib.instancer)
  2) 기준 글자 = 지금 부분집합의 cmap 전체(12,100자) — 같은 옵션으로 이것만 다시 만들면 크기·cmap·글리프 수가 기존 파일과 같다(재현 확인)
  3) 추가 글자(EXTRA) 를 더해 fontTools.subset 으로 자른다(배치 기능 전체·이름표 전체 유지)
  원본에 없는 글자(✔ U+2714, ☐ U+2610, ☑ U+2611)는 넣을 수 없다 — 대신 ✓ □ ■ 를 쓴다.
필요: fontTools(pip --user, root 불필요). 라이선스: Noto Sans KR 은 SIL OFL 1.1.
"""
import os
import sys

from fontTools import subset
from fontTools.ttLib import TTFont
from fontTools.varLib import instancer

DEFAULT_OUT = os.path.join(os.path.dirname(__file__), "..", "src", "xkxk.frameworks.ui.browser", "Assets", "Fonts", "NotoSansKR-Regular-subset.ttf")

# PMS 표·금액 칸용 추가 글자(lead 12:0x — 웹 ₩ ☒ 표시 결함)
EXTRA = {0x2713, 0x2264, 0x2265, 0x2260, 0x221E, 0x2605, 0x2606, 0x321C}  # ✓ ≤ ≥ ≠ ∞ ★ ☆ ㈜
for lo, hi in ((0x2010, 0x206F),   # 일반 구두점(‐ – — ‘ ’ “ ” † ‡ • … ‰ ′ ″ ※ 등, lead 12:1x)
               (0x20A0, 0x20CF),   # 통화(₩ € 등)
               (0x2100, 0x214F),   # 문자형 기호(℃ ℉ № ™ 등)
               (0x2190, 0x21FF),   # 화살표(lead 12:1x)
               (0x2460, 0x2473),   # ①~⑳
               (0x3380, 0x33FF)):  # CJK 단위(㎡ ㎏ ㎞ ㎜ ㎝ ㎖ 등)
    EXTRA |= set(range(lo, hi + 1))


def main() -> int:
    if len(sys.argv) < 2:
        print(__doc__)
        return 2
    vf_path = sys.argv[1]
    out = sys.argv[2] if len(sys.argv) > 2 else DEFAULT_OUT
    base = set(TTFont(out).getBestCmap()) if os.path.exists(out) else set()
    print(f"기준 글자 {len(base)}자(기존 {out})")
    inst = instancer.instantiateVariableFont(TTFont(vf_path), {"wght": 400})
    have = set(inst.getBestCmap())
    wanted = (base | EXTRA) & have
    lost = base - have
    if lost:
        print(f"경고: 기준 글자 {len(lost)}자가 원본에 없음 — 중단")
        return 3
    opts = subset.Options()
    opts.layout_features = ["*"]
    opts.name_IDs = ["*"]
    opts.name_languages = ["*"]
    opts.notdef_outline = True
    opts.glyph_names = False
    opts.hinting = True
    opts.legacy_kern = True
    sub = subset.Subsetter(opts)
    sub.populate(unicodes=sorted(wanted))
    sub.subset(inst)
    tmp = out + ".tmp"
    inst.save(tmp)
    os.replace(tmp, out)
    added = len(wanted - base)
    print(f"완료: {out} {os.path.getsize(out):,} 바이트, 글자 {len(wanted)}자(추가 {added})")
    return 0


if __name__ == "__main__":
    sys.exit(main())
