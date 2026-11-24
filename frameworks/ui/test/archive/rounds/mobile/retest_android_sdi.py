# ③ SDI 전환 시 Grid 0/0 판별(Fold5, odev-2r): MDI 에서 선택·스크롤 → docs.sdi on/off 직후·400ms 뒤 grid.visible(hash 포함) 원문을 기록하는 스크립트
"""
사용: python3 -E -u tools/retest_android_sdi.py --apk <APK> --outdir <폴더>
해석: hash 같고 즉시만 0/0 → (b) 측정 시점, hash 같고 나중도 0/0 → (a) 분리·재부착 초기화, hash 다름 → 캐시 미스.
"""
import argparse
import json
import os
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import retest_android as ra  # noqa: E402
import retest_android_cond2 as c2  # noqa: E402


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--apk", required=True)
    p.add_argument("--serial", default="R3CW70202FP")
    p.add_argument("--outdir", required=True)
    p.add_argument("--rounds", type=int, default=3)
    a = p.parse_args()
    os.makedirs(a.outdir, exist_ok=True)
    state, rep = {}, {"rounds": []}
    try:
        info = ra.prepare(a.serial, a.apk, state)
        from retest_remote import Remote
        c2.launch(a.serial, "--fresh", info)
        time.sleep(3)
        r = Remote()
        r.call("docs.close", all=True)
        g = (r.call("docs.open", kind="grid") or [{}])[0].get("id")
        gt = (r.call("docs.open", kind="gantt") or [{}])[0].get("id")  # F1 판정: 간트·폼도 같은 인스턴스인지(odev-2r)
        fm = (r.call("docs.open", kind="form") or [{}])[0].get("id")
        r.call("layout.template.apply", id="tpl.mdi-cascade")
        r.call("docs.activate", doc=g)
        time.sleep(1.5)
        from retest_android_emptycells import walk

        def inst():
            tr = r.call("tree.get", named=False, depth=90) or {}
            cnt = lambda key: len(walk(tr, [], lambda n: key in [v for v in n.values() if isinstance(v, str)]))
            gp = r.call("ui.find", id="gantt.vScroll", doc=gt, props="Value,Maximum") or {}
            return {"grid": cnt("grid.dgMain"), "gantt_lblZoom": cnt("gantt.lblZoom"), "form_txtTitle": cnt("form.txtTitle"), "gantt_props": gp.get("props") if isinstance(gp, dict) else None}

        def gv():
            return r.call("grid.visible", id="grid.dgMain", doc=g)

        # D-2 회귀: 탭 ↔ MDI 전환 전후 viewHash
        d2 = {"mdi0": (gv() or {}).get("viewHash")}
        r.call("layout.template.apply", id="tpl.tabbed")
        time.sleep(1.5)
        d2["tab"] = (gv() or {}).get("viewHash")
        r.call("layout.template.apply", id="tpl.mdi-cascade")
        r.call("docs.activate", doc=g)
        time.sleep(1.5)
        d2["mdi1"] = (gv() or {}).get("viewHash")
        rep["d2_viewHash"] = d2

        for i in range(1, a.rounds + 1):
            r.call("grid.select", id="grid.dgMain", doc=g, index=300, scroll=True)
            r.call("ui.scroll", id="grid.dgMain", doc=g, lines=-10)
            time.sleep(0.8)
            rd = {"i": i, "mdi_before": gv(), "inst_before": inst()}
            r.call("docs.sdi", doc=g, on=True)
            rd["sdi_immediate"] = gv()
            time.sleep(0.4)
            rd["sdi_400ms"] = gv()
            rd["inst_sdi"] = inst()
            time.sleep(1.0)
            rd["sdi_1400ms"] = gv()
            r.call("docs.sdi", doc=g, on=False)
            rd["rejoin_immediate"] = gv()
            time.sleep(0.4)
            rd["rejoin_400ms"] = gv()
            time.sleep(1.0)
            rd["rejoin_1400ms"] = gv()
            rd["inst_rejoin"] = inst()
            lines = r.call("log.tail", n=400) or []  # [sdi] float.before/after/laid·redock.before/laid 원문(odev-2r, Activation.cs 8e548470 판부터)
            rd["sdi_log"] = [str(x) for x in lines if "[sdi]" in str(x)]
            rep["rounds"].append(rd)
            print(i, {k: (v or {}).get("first") if isinstance(v, dict) else v for k, v in rd.items() if k != "i"}, flush=True)
    finally:
        json.dump(rep, open(os.path.join(a.outdir, "sdi_result.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=1)
        ra.cleanup(a.serial, state)
        ra.adb(a.serial, "shell", "wm", "size", "reset")


if __name__ == "__main__":
    main()
