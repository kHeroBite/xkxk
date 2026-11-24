#!/usr/bin/env python3
# RETEST 1차 이식 시나리오 판정 — 새 실행 폴더와 기준 폴더(logs/otest/desktop/retest2)를 비교해 상태 값·레이아웃 구조·캡처 본문 유무로 PASS/FAIL 을 낸다
"""
사용: retest2_port_judge.py <새 폴더> [기준 폴더] [출력 json]   rc 0=전부 PASS, 1=FAIL 있음, 3=실행 기록 없음
규칙(결과 보기 전 고정), 시나리오마다:
 a perf JSON 이 있다(실행이 끝까지 감).
 b perf 의 'state_after_*' 키와 값이 기준과 같다.
 c 저장 레이아웃 JSON 이 있으면 문서 수와 State 값의 개수 분포가 기준과 같다.
 d 캡처 PNG 본문 화소 표준편차 > 10 (빈 화면 아님).
기준에 없는 시나리오는 a·c·d 만 본다. 기준 자체가 FAIL 이었는지는 알 수 없으므로 '기준과 같음'은 회귀 없음을 뜻한다.
"""
import collections
import glob
import json
import os
import statistics
import sys

from PIL import Image

NEW = sys.argv[1]
# 영역 루트 = 이 파일 위치(test/archive/rounds/desktop) 기준 네 단계 위(UTODO-0012)
UI_ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "..", ".."))
BASE = sys.argv[2] if len(sys.argv) > 2 else os.path.join(UI_ROOT, "logs", "otest", "desktop", "retest2")
OUT = sys.argv[3] if len(sys.argv) > 3 else os.path.join(NEW, "judge.json")


def jl(p):
    try:
        return json.load(open(p, encoding="utf-8-sig"))
    except Exception:
        return None


def states(d):
    c = collections.Counter()
    docs = 0

    def walk(o):
        nonlocal docs
        if isinstance(o, dict):
            for k in ("State", "state"):
                if isinstance(o.get(k), str):
                    c[o[k]] += 1
            if any(k in o for k in ("DocId", "docId", "Id", "id")) and any(k in o for k in ("Kind", "kind", "State", "state")):
                docs += 1
            for v in o.values():
                walk(v)
        elif isinstance(o, list):
            for v in o:
                walk(v)
    walk(d)
    return docs, dict(c)


def std(p):
    im = Image.open(p).convert("L")
    w, h = im.size
    return round(statistics.pstdev(list(im.crop((0, int(h * 0.12), w, h)).getdata())), 1)


names = sorted(os.path.basename(p)[:-len(".perf.json")] for p in glob.glob(os.path.join(NEW, "*.perf.json")))
if not names:
    print("실행 기록 없음")
    sys.exit(3)
res = []
for n in names:
    pn, pb = jl(os.path.join(NEW, n + ".perf.json")), jl(os.path.join(BASE, n + ".perf.json"))
    r = {"scenario": n, "checks": {}}
    r["checks"]["a_perf"] = pn is not None
    sn = {k: v for k, v in (pn or {}).items() if k.startswith("state_after_")}
    if pb is not None:
        sb = {k: v for k, v in pb.items() if k.startswith("state_after_")}
        r["checks"]["b_state_same"] = sn == sb
        r["state_new"], r["state_base"] = sn, sb
    png = os.path.join(NEW, n + ".png")
    if os.path.exists(png):
        s = std(png)
        r["checks"]["d_body_std_gt10"] = s > 10
        r["std"] = s
    else:
        r["checks"]["d_body_std_gt10"] = False
    r["verdict"] = "PASS" if all(r["checks"].values()) else "FAIL"
    res.append(r)
# c 레이아웃 구조 비교(저장 파일은 시나리오 접두어 r01·r02·… 와 같은 이름)
lay = []
for p in sorted(glob.glob(os.path.join(NEW, "r0*.json"))):
    if p.endswith(".perf.json") or os.path.basename(p) == "judge.json":
        continue
    b = os.path.join(BASE, os.path.basename(p))
    dn, db = states(jl(p)), states(jl(b)) if os.path.exists(b) else None
    lay.append({"file": os.path.basename(p), "new": dn, "base": db, "same": (db is None) or dn == db})
out = {"scenarios": res, "layouts": lay, "pass": sum(1 for r in res if r["verdict"] == "PASS"), "fail": [r["scenario"] for r in res if r["verdict"] != "PASS"],
       "layout_diff": [x["file"] for x in lay if not x["same"]]}
json.dump(out, open(OUT, "w", encoding="utf-8"), ensure_ascii=False, indent=1)
print("PASS %d / %d, FAIL %s, 레이아웃 불일치 %s" % (out["pass"], len(res), out["fail"], out["layout_diff"]))
sys.exit(0 if not out["fail"] and not out["layout_diff"] else 1)
