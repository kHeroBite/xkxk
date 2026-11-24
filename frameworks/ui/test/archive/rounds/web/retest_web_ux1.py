# Fold5 기기 Chrome 웹판의 D-UX1 폼팩터 판정(UX1-WU)을 확인하는 스크립트 — 탭을 다시 읽으며 콘솔 '[host] form-factor=' 줄을 받고 split.dump 를 저장(otest-m-2, i1720rc_test_plan.md 3절)
"""
사용: python3 -E -u tools/retest_web_ux1.py --url-part 127.0.0.1:5187/?otm2 --case UX1-WU --out-dir logs/otest_m2/i1720rc
결과: <out-dir>/web_<case>_console.txt(줄 원문), web_<case>_console_raw.json, web_<case>_dump.json, web_<case>.json(기대·판정)
"""
import argparse
import json
import os
import subprocess
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from cdp_bridge import Bridge, NODE, winpath  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--url-part", required=True)
    ap.add_argument("--case", default="UX1-WU")
    ap.add_argument("--out-dir", required=True)
    ap.add_argument("--wait", type=int, default=30)
    ap.add_argument("--expect-source", default="pointer")
    a = ap.parse_args()
    os.makedirs(a.out_dir, exist_ok=True)
    pr = subprocess.run([NODE, winpath(os.path.join(HERE, "cdp_console.mjs")), a.url_part, str(a.wait)], capture_output=True, text=True, encoding="utf-8", timeout=a.wait + 40)
    raw = json.loads(pr.stdout.strip().splitlines()[-1]) if pr.stdout.strip() else {"ok": False, "stderr": pr.stderr}
    json.dump(raw, open(os.path.join(a.out_dir, "web_%s_console_raw.json" % a.case), "w", encoding="utf-8"), ensure_ascii=False, indent=1)
    hits = [x["text"] for x in raw.get("lines", []) if "form-factor" in x["text"] or "desktop 으로 판정" in x["text"]]
    open(os.path.join(a.out_dir, "web_%s_console.txt" % a.case), "w", encoding="utf-8").write("\n".join(hits) + "\n")
    b = Bridge(a.url_part)
    for _ in range(40):
        if b.eval('typeof pdAutomation !== "undefined"') is True:
            break
        time.sleep(1)
    time.sleep(2)
    sd = b.call("split.dump")
    mode = b.call("ui.mode")
    metrics = b.eval("({w:innerWidth,h:innerHeight,dpr:devicePixelRatio,coarse:matchMedia('(pointer: coarse)').matches,ua:navigator.userAgent,url:location.href})")
    b.close()
    json.dump(sd, open(os.path.join(a.out_dir, "web_%s_dump.json" % a.case), "w", encoding="utf-8"), ensure_ascii=False, indent=1)
    want = "form-factor=touch source=%s" % a.expect_source
    ok = (sd or {}).get("touchFormFactor") is True and any(want in h for h in hits)
    res = {"id": a.case, "build": "i1720rc", "at": time.strftime("%Y-%m-%d %H:%M:%S"),
           "expect": {"touchFormFactor": True, "console": "[host] %s" % want},
           "actual": {"touchFormFactor": (sd or {}).get("touchFormFactor"), "platform": (sd or {}).get("platform"), "compact": (sd or {}).get("compact"),
                      "console_lines": hits, "console_total": raw.get("total"), "ui_mode": mode, "metrics": metrics},
           "verdict": "PASS" if ok else "FAIL"}
    json.dump(res, open(os.path.join(a.out_dir, "web_%s.json" % a.case), "w", encoding="utf-8"), ensure_ascii=False, indent=1)
    print(json.dumps(res, ensure_ascii=False)[:1500])


if __name__ == "__main__":
    main()
