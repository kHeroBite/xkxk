#!/usr/bin/env python3
# 하드코딩 색 정적 검사 — 신규 UI(스피너·바쁨·투어·설정 뼈대·패널 모서리 버튼) 파일에서 색 리터럴·Brushes/Colors 직접 사용을 찾는다(주석 줄 제외). AC-2-10 용
"""
사용: python3 -I scan_hardcoded_colors.py [--out 결과.json]   종료 코드 0=위반 0, 1=위반 있음, 2=대상 파일 없음
대상 목록은 아래 FILES(poc/ui/src 기준). 토큰은 PdBrush* 리소스로만 받아야 한다.
"""
import json
import os
import re
import sys

SRC = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "..", "src")
FILES = ["Pd.Ui/Controls/GanttSpinner.cs", "Pd.Ui/Controls/GanttSpinnerVisual.cs", "Pd.Ui/Controls/BusyOverlay.cs", "Pd.Ui/Controls/CursorSpinnerFrames.cs",
         "Pd.Ui/Controls/PointerFollower.cs", "Pd.Poc.Shell/Busy/BusyCursorDriver.cs", "Pd.Poc.Shell/Busy/BusyService.cs", "Pd.Poc.Shell/Busy/BusyHooks.cs",
         "Pd.Ui/Controls/PanelCornerButton.cs", "Pd.Ui/Controls/TourOverlay.axaml", "Pd.Ui/Controls/TourOverlay.axaml.cs", "Pd.Ui/Controls/TourCard.axaml", "Pd.Ui/Controls/TourCard.axaml.cs",
         "Pd.Ui/Controls/ShortcutLayer.axaml", "Pd.Ui/Controls/ShortcutLayer.axaml.cs", "Pd.Ui/Controls/SettingsShell.axaml", "Pd.Ui/Controls/SettingsShell.axaml.cs", "Pd.Ui/Controls/SettingsRow.cs", "Pd.Ui/Styles/PdUiStyles.axaml"]
RX = re.compile(r"Color\.Parse|Color\.FromRgb|Color\.FromArgb|new Color\(|#[0-9A-Fa-f]{6,8}\b|Colors\.[A-Z][A-Za-z]+|Brushes\.[A-Z][A-Za-z]+|new SolidColorBrush\(")


def main(argv):
    hits, missing = {}, []
    for f in FILES:
        p = os.path.join(SRC, f)
        if not os.path.exists(p):
            missing.append(f)
            continue
        rows = []
        for i, ln in enumerate(open(p, encoding="utf-8", errors="ignore"), 1):
            if ln.strip().startswith(("//", "///", "<!--", "*")):
                continue
            if RX.search(ln):
                rows.append("%d: %s" % (i, ln.strip()[:140]))
        hits[f] = rows
    out = {"missing": missing, "files_scanned": len(hits), "violations": {f: r for f, r in hits.items() if r}}
    if len(argv) == 2 and argv[0] == "--out":
        with open(argv[1], "w", encoding="utf-8") as fp:
            json.dump(out, fp, ensure_ascii=False, indent=1)
    print("파일 %d개 검사, 위반 파일 %d개, 없음 %s" % (len(hits), len(out["violations"]), missing))
    return 2 if missing else (1 if out["violations"] else 0)


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
