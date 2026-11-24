# M4(폰 모드·접힘 전환 때 문서 뷰 2중 생성) 판정·기준선(Fold5): 접힘↔펼침 왕복·화면 모드 전환·접힘 기동·SDI 에서 grid.dgMain 개수·첫 행·선택 행·폼 글자·간트 스크롤·캡처를 기록하는 스크립트
"""
사용: python3 -E -u tools/retest_android_m4.py --apk <APK> --outdir <폴더>
수정 전(기준선)·수정 후 판에 같은 절차로 돌려 비교한다(odev-2r 판정 절차 1~5 + 접힘 기동).
"""
import argparse
import json
import os
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import retest_android as ra  # noqa: E402
import retest_android_cond2 as c2  # noqa: E402

FOLD = "904x2316"
TEXT = "한글입력확인 가나다"


def count_val(t, val):
    if isinstance(t, dict):
        return (1 if val in [v for v in t.values() if isinstance(v, str)] else 0) + sum(count_val(v, val) for v in t.values() if isinstance(v, (dict, list)))
    if isinstance(t, list):
        return sum(count_val(v, val) for v in t)
    return 0


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

        def snap(r, tag, ids):
            tr = r.call("tree.get", named=False, depth=80) or {}
            gid = ids.get("grid")
            gv = r.call("grid.visible", id="grid.dgMain", doc=gid) or {}
            ft = r.call("ui.find", id="form.txtTitle", doc=ids.get("form")) or {}
            gs = r.call("ui.find", id="gantt.vScroll", doc=ids.get("gantt"), props="Value,Maximum") or {}  # props 로 확실히 읽는다(odev-2r 10-29)
            gs = {"props": gs.get("props"), "value": gs.get("value"), "zoom": (r.call("ui.find", id="gantt.lblZoom", doc=ids.get("gantt")) or {}).get("text")} if isinstance(gs, dict) else gs
            perf = r.call("perf.get") or {}
            s = {"tag": tag, "mode": r.call("ui.mode"), "grid_dgMain_in_tree": count_val(tr, "grid.dgMain"), "grid_visible": {k: gv.get(k) for k in ("first", "last", "selectedIndex", "offsetY", "count")},
                 "form_text": (ft.get("text") if isinstance(ft, dict) else None), "gantt_vscroll": (gs.get("props") if isinstance(gs, dict) else gs),
                 "docs": [(d.get("id"), d.get("active"), d.get("state")) for d in (r.call("docs.list") or [])],
                 "grid_measure_sizechg": perf.get("grid_measure_sizechg"), "cap": cap("m4_" + tag)}
            print(tag, s["grid_dgMain_in_tree"], s["grid_visible"], s["form_text"], flush=True)
            return s

        def setup(r):
            r.call("docs.close", all=True)
            ids = {k: (r.call("docs.open", kind=k) or [{}])[0].get("id") for k in ("grid", "gantt", "form")}
            r.call("layout.template.apply", id="tpl.mdi-cascade")
            time.sleep(1.5)
            r.call("docs.activate", doc=ids["grid"])
            r.call("ui.scroll", id="grid.dgMain", doc=ids["grid"], index=300)
            r.call("grid.select", id="grid.dgMain", doc=ids["grid"], index=300, scroll=True)
            r.call("ui.scroll", id="grid.dgMain", doc=ids["grid"], lines=-10)
            r.call("ui.setText", id="form.txtTitle", doc=ids["form"], text=TEXT)
            r.call("ui.scroll", id="gantt.vScroll", doc=ids["gantt"], lines=20)
            time.sleep(1)
            return ids

        def fold(on):
            ra.adb(sr, "shell", "wm", "size", FOLD if on else "reset")
            time.sleep(2.5)

        # 1~3: 펼침 → 접기 → 펴기 3회
        c2.launch(sr, "--fresh", info)
        time.sleep(3)
        r = Remote()
        ids = setup(r)
        F = {"ids": ids, "steps": [snap(r, "fold0_unfolded", ids)]}
        for i in range(1, 4):
            fold(True)
            F["steps"].append(snap(r, "fold%d_folded" % i, ids))
            if i == 2:  # 접힌 상태에서 드롭다운(원격 activate)으로 활성 문서 바꾸기
                r.call("docs.activate", doc=ids["gantt"])
                time.sleep(1)
                F["steps"].append(snap(r, "fold2_folded_activate_gantt", ids))
            fold(False)
            F["steps"].append(snap(r, "fold%d_unfolded" % i, ids))
        rep["fold_roundtrip"] = F
        # 4: 펼침에서 화면 모드 폰 ↔ 태블릿
        r.call("docs.activate", doc=ids["grid"])
        M = {"steps": []}
        for i in range(1, 3):
            r.call("ui.mode", set="phone")
            time.sleep(1.5)
            M["steps"].append(snap(r, "mode%d_phone" % i, ids))
            r.call("ui.mode", set="tablet")
            time.sleep(1.5)
            M["steps"].append(snap(r, "mode%d_tablet" % i, ids))
        r.call("ui.mode", set="auto")
        rep["mode_switch"] = M
        # 5: SDI(관리형 창) Grid 를 띄운 채 접기 → 펴기
        r.call("docs.sdi", doc=ids["grid"], on=True)
        time.sleep(1.5)
        S = {"steps": [snap(r, "sdi0", ids)]}
        fold(True)
        S["steps"].append(snap(r, "sdi1_folded", ids))
        fold(False)
        S["steps"].append(snap(r, "sdi1_unfolded", ids))
        r.call("docs.sdi", doc=ids["grid"], on=False)
        rep["sdi"] = S
        # 접힘 상태로 기동 → Grid 열기 → 펴기(odev-2r 추가)
        fold(True)
        c2.launch(sr, "--fresh", info)
        time.sleep(3)
        r = Remote()
        r.call("docs.close", all=True)
        ids2 = {k: (r.call("docs.open", kind=k) or [{}])[0].get("id") for k in ("grid",)}
        r.call("ui.scroll", id="grid.dgMain", doc=ids2["grid"], index=300)
        r.call("grid.select", id="grid.dgMain", doc=ids2["grid"], index=300, scroll=True)
        time.sleep(1)
        C = {"steps": [snap(r, "coldfold_folded", ids2)]}
        fold(False)
        C["steps"].append(snap(r, "coldfold_unfolded", ids2))
        rep["cold_folded_start"] = C
    finally:
        json.dump(rep, open(os.path.join(O, "m4_result.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=1)
        ra.cleanup(sr, state)
        ra.adb(sr, "shell", "wm", "size", "reset")


if __name__ == "__main__":
    main()
