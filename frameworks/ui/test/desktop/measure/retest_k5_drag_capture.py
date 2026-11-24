# K5 머리글 열 폭 끌기 판별용 래퍼 — dense_wheel_check 를 고치지 않고 끌기 직전·직후 캡처와 상태를 남긴다
"""
사용:
  python3 -B retest_k5_drag_capture.py --drag-shots <폴더> [dense_wheel_check.py 인자...]
      dense_wheel_check.drag_measure 에 넘어가는 drag_header 를 감싸 끌기 직전(k5_drag_before.png)·
      직후(k5_drag_after.png) 캡처와 ui.find bounds·grid 상태를 <폴더>/k5_drag_trace.json 에 남긴다.
      판정 논리·입력 경로(safe_click)는 원본 그대로다.
  python3 -B retest_k5_drag_capture.py --self-test
      입력 없이 감싸기 순서(캡처 전 → 끌기 → 캡처 후)와 기록 형식을 확인한다.
"""
import json
import os
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "judge"))
sys.path.insert(0, HERE)


def wrap_drag(drag_header, r, shots, win=lambda p: p):
    """drag_header(cid, state, col, dip) 를 감싸 전후 캡처·상태를 남긴다. 기록 목록을 함께 돌려준다."""
    trace = []

    def shot(name, cid):
        doc = r.result("docs.list")
        doc = (doc if isinstance(doc, list) else doc.get("docs", []))[-1]["id"]
        path = os.path.join(shots, name + ".png")
        r.result("screenshot", doc=doc, path=win(path))
        return path

    def wrapped(cid, state, col, dip):
        rec = {"cid": cid, "col": col, "dip": dip, "state_before": state,
               "bounds_before": (r.result("ui.find", id=cid) or {}).get("bounds")}
        rec["shot_before"] = shot("k5_drag_before", cid)
        rec["t_before"] = time.time()
        drag_header(cid, state, col, dip)
        time.sleep(0.5)
        rec["t_after"] = time.time()
        rec["shot_after"] = shot("k5_drag_after", cid)
        rec["state_after"] = r.result("dense.state", id=cid)
        trace.append(rec)
        with open(os.path.join(shots, "k5_drag_trace.json"), "w", encoding="utf-8") as f:
            json.dump(trace, f, ensure_ascii=False, indent=1, default=str)
    return wrapped, trace


def self_test():
    import tempfile
    calls = []

    class FakeR:
        def result(self, cmd, **a):
            calls.append(cmd)
            if cmd == "docs.list":
                return [{"id": "d1"}]
            if cmd == "ui.find":
                return {"bounds": {"x": 1, "y": 2, "width": 3, "height": 4}}
            if cmd == "dense.state":
                return {"colWidth1": 180.0}
            return {}

    def fake_drag(cid, state, col, dip):
        calls.append("DRAG")

    d = tempfile.mkdtemp(prefix="k5cap_")
    w, trace = wrap_drag(fake_drag, FakeR(), d)
    w("grid.dgMain", {"colWidth1": 100.0}, 1, 80)
    order = [c for c in calls if c in ("screenshot", "DRAG")]
    fails = []
    if order != ["screenshot", "DRAG", "screenshot"]:
        fails.append("순서 %s" % order)
    j = json.load(open(os.path.join(d, "k5_drag_trace.json"), encoding="utf-8"))
    if not (j[0]["shot_before"].endswith("k5_drag_before.png") and j[0]["shot_after"].endswith("k5_drag_after.png")):
        fails.append("캡처 경로")
    if j[0]["bounds_before"] != {"x": 1, "y": 2, "width": 3, "height": 4}:
        fails.append("bounds 기록")
    print("자가검사 실패: %s" % fails if fails else "self-test ok")
    return 1 if fails else 0


def main():
    argv = sys.argv[1:]
    if "--self-test" in argv:
        return self_test()
    if "--drag-shots" not in argv:
        print("--drag-shots <폴더> 가 필요하다", file=sys.stderr)
        return 2
    i = argv.index("--drag-shots")
    shots = os.path.abspath(argv[i + 1])
    del argv[i:i + 2]
    os.makedirs(shots, exist_ok=True)
    import dense_wheel_check as dwc
    import mdi_drag_app as app
    orig = dwc.drag_measure

    def patched(r, drag_header, wheel_at):
        w, _ = wrap_drag(drag_header, r, shots, win=app.win)
        return orig(r, w, wheel_at)
    dwc.drag_measure = patched
    sys.argv = ["dense_wheel_check.py"] + argv
    return dwc.main()


if __name__ == "__main__":
    sys.exit(main())
