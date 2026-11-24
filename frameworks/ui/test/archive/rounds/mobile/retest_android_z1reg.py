# Z1 회귀 확인(Fold5, odev-3): MDI 계단식에서 UI-06 창 배율 200%·100% 캡처(줄 접힘·WrapPanel 겹침 대조용)와 pinch.sim 6종·rejections 를 기록하는 스크립트
"""
사용: python3 -E -u tools/retest_android_z1reg.py --apk <APK> --outdir <폴더>
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
        c2.launch(sr, "--fresh", info)
        time.sleep(3)
        r = Remote()
        r.call("docs.close", all=True)
        ids = [(r.call("docs.open", kind=k) or [{}])[0].get("id") for k in ("grid", "gantt", "form")]
        r.call("layout.template.apply", id="tpl.mdi-cascade")
        time.sleep(1.5)
        Z = {}
        for doc in (ids[2], ids[0]):  # 폼(줄 접힘 텍스트·WrapPanel)과 그리드
            r.call("docs.activate", doc=doc)
            for z in (2.0, 1.0):
                Z["%s_%s" % (doc, z)] = r.call("docs.zoom", doc=doc, zoom=z)
                time.sleep(1.5)
                Z["cap_%s_%s" % (doc, z)] = cap("ui06_%s_zoom%d" % (doc, int(z * 100)))
        rep["zoom"] = Z
        # pinch.sim 6종(odev-7c 기대값: +3, −2, 0, −3, 문서 밖 무시)
        r.call("docs.activate", doc=ids[2])
        time.sleep(1)
        C = {"state0": r.call("pinch.state")}
        for tag, sc in (("up_1.33", "1.1,1.21,1.33"), ("down", "0.9,0.82"), ("ref_1.09", "1.09"), ("ref_0.75", "0.75")):
            C[tag] = r.call("pinch.sim", scales=sc, end=True)
        C["outside"] = r.call("pinch.sim", scales="1.1,1.21", x=20, y=20, end=True)
        C["state_end"] = r.call("pinch.state")
        rep["pinch"] = C
    finally:
        json.dump(rep, open(os.path.join(O, "z1reg_result.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=1)
        ra.cleanup(sr, state)
        ra.adb(sr, "shell", "wm", "size", "reset")


if __name__ == "__main__":
    main()
