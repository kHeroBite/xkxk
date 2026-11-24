# 제품 Grid-1 가로→세로 빈 칸 판별(Fold5, odev-3): 탭 모드에서 ① col=6→lines=20, ② 테마 전환 직후 바로 col=6→lines=20 을 하고 행별 셀(x·visible·text)·캡처·탭/2초 뒤 캡처를 남기는 스크립트
"""
사용: python3 -E -u tools/retest_android_emptycells_h.py --apk <APK> --outdir <폴더>
"""
import argparse
import collections
import json
import os
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import retest_android as ra  # noqa: E402
import retest_android_cond2 as c2  # noqa: E402
from retest_android_emptycells import walk  # noqa: E402


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--apk", required=True)
    p.add_argument("--serial", default="R3CW70202FP")
    p.add_argument("--outdir", required=True)
    a = p.parse_args()
    O, sr = a.outdir, a.serial
    os.makedirs(O, exist_ok=True)
    state, rep = {}, {}

    def cap(name):
        rc, data = ra.adb(sr, "exec-out", "screencap", "-p", binary=True)
        k = data.find(b"\x89PNG") if rc == 0 else -1
        if k >= 0:
            open(os.path.join(O, name + ".png"), "wb").write(data[k:])
            return name + ".png"
        return None

    try:
        info = ra.prepare(sr, a.apk, state)
        from retest_remote import Remote

        def cells(r):
            tr = r.call("tree.get", named=False, depth=90) or {}
            cs = walk(tr, [], lambda n: "DataGridCell" == str(n.get("type")))
            rows = collections.defaultdict(list)
            for c in cs:
                b = c.get("bounds") or {}
                if c.get("visible"):
                    rows[round(b.get("y", 0))].append((round(b.get("x", 0), 1), c.get("text")))
            return {"rows": [(y, sorted(v)) for y, v in sorted(rows.items())][:16], "hidden": sum(1 for c in cs if not c.get("visible"))}

        for tag, pre_theme in (("A", False), ("B_theme_then_col", True)):
            c2.launch(sr, "--fresh", info)
            time.sleep(3)
            r = Remote()
            r.call("docs.close", all=True)
            g = (r.call("docs.open", kind="grid") or [{}])[0].get("id")
            r.call("layout.template.apply", id="tpl.tabbed")
            time.sleep(1.5)
            R = {}
            if pre_theme:
                r.call("theme.next")  # 예전 측정 왕복이 있던 시점 직후 바로 가로 이동
            R["col6"] = r.call("ui.scroll", id="grid.dgMain", doc=g, col=6)
            time.sleep(0.8)
            R["s1"] = cells(r)
            R["lines20"] = r.call("ui.scroll", id="grid.dgMain", doc=g, lines=20)
            time.sleep(1)
            R["s2"] = cells(r)
            R["gv2"] = r.call("grid.visible", id="grid.dgMain", doc=g)
            R["cap2"] = cap("%s_lines20" % tag)
            ra.adb(sr, "shell", "input", "tap", "1700", "2100")
            time.sleep(2.5)
            R["s3"] = cells(r)
            R["cap3"] = cap("%s_after_tap_2s" % tag)
            rep[tag] = R
            print(tag, (R["col6"] or {}).get("afterCols"), [len(v) for y, v in R["s2"]["rows"]][:8], flush=True)
    finally:
        json.dump(rep, open(os.path.join(O, "emptycells_h_result.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=1)
        ra.cleanup(sr, state)
        ra.adb(sr, "shell", "wm", "size", "reset")


if __name__ == "__main__":
    main()
