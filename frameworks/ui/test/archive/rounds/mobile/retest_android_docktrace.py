# Q4 판별 실험(Fold5, odev-3·odev-2r): A·A_empty 각 1회 기동해 dock.trace action=theme-next frames=8 을 5회, action=none 1회 실행하고 원문 JSON 을 그대로 저장하는 스크립트
"""
사용: python3 -E -u tools/retest_android_docktrace.py --apk <APK(DockTrace 20bc146d 판)> --outdir <폴더>
판별은 저장된 원문으로 한다(가로 바깥 패널 proportion 고정 + w·measureGiven 만 오감 = H-a, proportion 끝자리 진동 = H-b).
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
    os.makedirs(a.outdir, exist_ok=True)
    state = {}
    try:
        info = ra.prepare(a.serial, a.apk, state)
        from retest_remote import Remote
        for cond, args in (("A", "--fresh"), ("A_empty", "--fresh --notify-panel empty")):
            c2.launch(a.serial, args, info)
            time.sleep(3)
            r = Remote()
            r.call("docs.close", all=True)
            for k in ("grid", "gantt", "form"):
                r.call("docs.open", kind=k)
            time.sleep(2)
            out = {"cond": cond, "args": args, "host_load": os.getloadavg(), "traces": []}
            for i in range(1, 6):
                out["traces"].append({"i": i, "action": "theme-next", "result": r.call("dock.trace", action="theme-next", frames=8, _timeout=120),
                                      "ok": r.calls[-1]["ok"], "error": r.calls[-1].get("error")})
                time.sleep(0.5)
            out["traces"].append({"i": 6, "action": "none", "result": r.call("dock.trace", action="none", frames=8, _timeout=120),
                                  "ok": r.calls[-1]["ok"], "error": r.calls[-1].get("error")})
            json.dump(out, open(os.path.join(a.outdir, "docktrace_%s.json" % cond), "w", encoding="utf-8"), ensure_ascii=False, indent=1)
            print(cond, "saved", [t["ok"] for t in out["traces"]], flush=True)
    finally:
        ra.cleanup(a.serial, state)
        ra.adb(a.serial, "shell", "wm", "size", "reset")


if __name__ == "__main__":
    main()
