# i1137 기능 재판정(Fold5): 핀치 모의(+3·거절 0)·좁은 머리줄 ko/en(제목 폭)·A3 canDrop inner=true·B3 menu.list 태블릿 항목·A4 머리줄 ▾·📌·✕ 실제 탭을 기록하는 스크립트
"""
사용: python3 -E -u tools/retest_android_i1137_recheck.py --apk <APK> --outdir <폴더>
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
S = 360 / 160.0


def find_nodes(tree, pred, out, path=""):
    """tree.get 결과에서 조건에 맞는 노드(타입·이름·텍스트·bounds)를 모은다."""
    if isinstance(tree, dict):
        if pred(tree):
            out.append({k: tree.get(k) for k in ("type", "name", "id", "text", "bounds", "visible") if k in tree})
        for v in tree.values():
            if isinstance(v, (dict, list)):
                find_nodes(v, pred, out)
    elif isinstance(tree, list):
        for v in tree:
            find_nodes(v, pred, out)
    return out


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

    def tap(x, y):
        ra.adb(sr, "shell", "input", "tap", str(int(x)), str(int(y)))

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
        # C. 핀치 모의
        C = {"state0": r.call("pinch.state")}
        for tag, sc in (("up_1.33", "1.1,1.21,1.33"), ("down", "0.9,0.82"), ("ref_1.09", "1.09"), ("ref_0.75", "0.75")):
            C[tag] = r.call("pinch.sim", scales=sc, end=True)
        C["outside"] = r.call("pinch.sim", scales="1.1,1.21", x=20, y=20, end=True)
        C["state_end"] = r.call("pinch.state")
        C["ops"] = [o for o in (r.call("perf.ops") or []) if isinstance(o, dict) and "pinch" in str(o.get("name"))]
        rep["C"] = C
        # H. 좁은 머리줄 ko/en + 제목 폭
        H = {}
        for lang in ("ko", "en"):
            r.call("lang.set", code=lang)
            time.sleep(1.2)
            H["cap_" + lang] = cap("H_header_" + lang)
            tr = r.call("tree.get", named=False, depth=60) or {}
            want = ("탐색기", "로그") if lang == "ko" else ("Explorer", "Log")
            H["title_nodes_" + lang] = find_nodes(tr, lambda n: str(n.get("text") or "") in want and "TextBlock" in str(n.get("type")), [])[:8]
        H["bounds"] = r.call("panel.bounds")
        r.call("lang.set", code="ko")
        rep["H"] = H
        # A3: inner=true
        rep["A3"] = {"inner": r.call("panel.canDrop", source=ids[2], target="LeftPane", op="Fill", inner=True),
                     "outer": r.call("panel.canDrop", source=ids[2], target="LeftPane", op="Fill")}
        # A4: 로그 패널 머리줄 ▾ · 📌 · ✕ 실제 탭(좌표는 panel.bounds BottomPane 오른쪽 끝 기준) + panel.pin 원격
        bp = (r.call("panel.bounds") or {}).get("BottomPane") or {}
        A4 = {"BottomPane": bp, "list0": r.call("panel.list")}
        if bp:
            hy = (bp["y"] + 11) * S
            right = (bp["x"] + bp["w"]) * S
            xs = {"close": right - 26, "pin": right - 71, "menu": right - 112}
            A4["xy"] = {k: [round(v), round(hy)] for k, v in xs.items()}
            tap(xs["menu"], hy)
            time.sleep(1.2)
            A4["cap_menu"] = cap("A4_tap_menu")
            ra.adb(sr, "shell", "input", "keyevent", "111")
            time.sleep(0.8)
            tap(xs["pin"], hy)
            time.sleep(1.5)
            A4["list_after_pin_tap"] = r.call("panel.list")
            A4["cap_pin"] = cap("A4_tap_pin")
            A4["pin_remote_off"] = r.call("panel.pin", id="BottomPane", on=False)
            A4["pin_remote_on"] = r.call("panel.pin", id="BottomPane", on=True)
            time.sleep(1)
            A4["list_after_pin_on"] = r.call("panel.list")
            A4["pin_remote_off2"] = r.call("panel.pin", id="BottomPane", on=False)
            time.sleep(1.5)
            bp2 = (r.call("panel.bounds") or {}).get("BottomPane") or bp
            hy2, right2 = (bp2["y"] + 11) * S, (bp2["x"] + bp2["w"]) * S
            tap(right2 - 26, hy2)
            time.sleep(1.5)
            A4["list_after_close_tap"] = r.call("panel.list")
            A4["cap_close"] = cap("A4_tap_close")
        rep["A4"] = A4
        # B3: menu.list 펼침/접힘
        B3 = {"unfolded": r.call("menu.list", id="shell.mnuUiMode"), "mode_unfolded": r.call("ui.mode")}
        ra.adb(sr, "shell", "wm", "size", FOLD)
        time.sleep(2.5)
        B3["folded"] = r.call("menu.list", id="shell.mnuUiMode")
        B3["mode_folded"] = r.call("ui.mode")
        ra.adb(sr, "shell", "wm", "size", "reset")
        time.sleep(2)
        rep["B3"] = B3
    finally:
        json.dump(rep, open(os.path.join(O, "recheck_result.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=1)
        ra.cleanup(sr, state)
        ra.adb(sr, "shell", "wm", "size", "reset")


if __name__ == "__main__":
    main()
