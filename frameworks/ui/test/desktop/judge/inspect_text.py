#!/usr/bin/env python3
# 문서·소스 문구 대조 검사기(정적) — 명세 JSON 의 항목마다 파일에 반드시 있어야 할 문구(must)와 없어야 할 문구(absent)를 대조해 PASS/FAIL 을 낸다
"""
사용: python3 -I inspect_text.py <명세.json> [--only <항목 id>]
명세 형식: {"AC-5-01": [{"file": "저장소 상대 경로", "must": ["문구", ...], "absent": ["문구", ...]}], ...}
출력: {"<id>": {"verdict": "PASS|FAIL", "reasons": [...]}} / 종료 코드 0=전부 PASS, 1=FAIL 있음, 2=입력 오류
대조 대상은 저장소 루트(poc/ui 의 두 단계 위가 아니라 --root 기본값 = 이 파일 기준 상위 .claude/worktrees 가 아닌 git 최상위)다. 주석 줄도 포함해 읽는다(문서 대조용이라 코드 심볼 판정과 다르다).
"""
import json
import os
import subprocess
import sys


def root():
    return subprocess.run(["git", "rev-parse", "--show-toplevel"], capture_output=True, text=True, cwd=os.path.dirname(os.path.abspath(__file__))).stdout.strip()


def main(argv):
    if not argv:
        print(__doc__)
        return 2
    only = argv[argv.index("--only") + 1] if "--only" in argv else None
    spec = json.load(open(argv[0], encoding="utf-8"))
    base = root()
    out = {}
    for ident, rows in spec.items():
        if only and ident != only:
            continue
        reasons = []
        for row in rows:
            path = os.path.join(base, row["file"])
            try:
                text = open(path, encoding="utf-8", errors="ignore").read()
            except OSError:
                reasons.append("파일 없음 " + row["file"])
                continue
            reasons += ["%s 에 없음: %r" % (row["file"], m) for m in row.get("must", []) if m not in text]
            reasons += ["%s 에 남아 있음: %r" % (row["file"], a) for a in row.get("absent", []) if a in text]
        out[ident] = {"verdict": "FAIL" if reasons else "PASS", "reasons": reasons}
    print(json.dumps(out, ensure_ascii=False, indent=1))
    return 0 if all(v["verdict"] == "PASS" for v in out.values()) else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
