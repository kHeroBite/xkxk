# D-12 재현 시험(Fold5): Q4 공식 순서(theme.next → ctrlWheel Grid-1 → 글꼴 → save → load → docs.sdi 토글)를 반복하며 문서 수·Id 를 단계마다 기록하고, 유실 즉시 log.tail 의 [d12] 줄을 저장한다
"""
사용: python3 -I -u tools/retest_android_d12.py --apk <진단 APK> --outdir <폴더> [--rounds 30] [--variant base|sdi_then_save]
- base: theme.next → input.ctrlWheel(Grid-1) → settings.set fontName → layout.save → layout.load → docs.sdi(Grid-1, on 교대)  (retest_android.measure 와 같은 순서)
- sdi_then_save: theme.next → ctrlWheel → fontName → docs.sdi → (지연 없이) layout.save → layout.load
- 단계마다 docs.list 의 id 목록을 남기고, save·load 직후에는 log.tail n=200 의 [d12] 줄을 저장한다(odev-2r 요청).
- 처음 연 문서 id 가 하나라도 빠지거나 '문서를 찾지 못했습니다' 가 나오면 즉시 log.tail n=200 을 저장하고 멈춘다.
"""
import argparse
import datetime
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
    p.add_argument("--rounds", type=int, default=30)
    p.add_argument("--variant", default="base", choices=("base", "sdi_then_save", "fontorder"))  # fontorder: Q4 공식 글꼴 순서(fonts[(회차-1)%n], 6회차 courier new — odev-2r 고부하 계획)
    p.add_argument("--outdir", required=True)
    a = p.parse_args()
    os.makedirs(a.outdir, exist_ok=True)
    state, out = {}, {"started": datetime.datetime.now().isoformat(timespec="seconds"), "apk": a.apk, "variant": a.variant, "rounds": [], "lost": None}
    tails = []
    try:
        info = ra.prepare(a.serial, a.apk, state)
        if not info or not c2.launch(a.serial, "--fresh", info):
            out["error"] = "기동 실패"
            return
        time.sleep(3)
        from retest_remote import Remote
        r = Remote()
        r.call("docs.close", all=True)
        ids0 = [(r.call("docs.open", kind=k) or [{}])[0].get("id") for k in ("grid", "gantt", "form")]
        grid = ids0[0]
        fonts = r.call("settings.fonts") or ["Noto Sans KR", "monospace"]
        out["ids0"] = ids0

        def ids():
            return [d.get("id") for d in (r.call("docs.list") or [])]

        out["fonts"] = fonts
        if a.variant == "fontorder":
            out["round6_font"] = fonts[5 % len(fonts)]

        def tail(tag, rnd, n=200):
            lines = r.call("log.tail", n=n) or []
            d12 = [str(x) for x in lines if "[d12]" in str(x)]
            tails.append({"round": rnd, "tag": tag, "time": time.strftime("%H:%M:%S"), "d12": d12, "all": lines})
            return d12

        for i in range(1, a.rounds + 1):
            steps, lost = [], None
            load = {"start": os.getloadavg()[0]}  # 고부하 조건 기록(15 미만이면 조건 미달로 센다 — 리드 11:5x)
            fi = (i - 1) if a.variant == "fontorder" else i
            seq = [("theme.next", {}), ("input.ctrlWheel", {"delta": 1 if i % 2 else -1, "doc": grid}), ("settings.set", {"fontName": fonts[fi % len(fonts)]})]
            sdi = ("docs.sdi", {"doc": grid, "on": i % 2 == 1})
            seq += [("layout.save", {}), ("layout.load", {}), sdi] if a.variant == "base" else [sdi, ("layout.save", {}), ("layout.load", {})]
            for cmd, args in seq:
                r.call(cmd, **args)
                c = r.calls[-1]
                if cmd in ("layout.save", "layout.load"):
                    tail(cmd, i)
                if cmd == "layout.save":
                    load["mid"] = os.getloadavg()[0]
                now = ids() if a.variant == "base" or cmd != "docs.sdi" else None  # sdi_then_save 는 sdi 와 save 사이에 지연을 두지 않는다
                steps.append({"cmd": cmd, "args": args, "ok": c["ok"], "ms": c["elapsed_ms"], "error": c.get("error"), "ids": now})
                notfound = "문서를 찾지 못했습니다" in str(c.get("error") or "")
                missing = [x for x in ids0 if now is not None and x not in now]
                if notfound or missing:
                    lost = {"round": i, "after": cmd, "missing": missing, "error": c.get("error"), "ids": now, "d12": tail("lost", i, 400)}
                    break
            load["end"] = os.getloadavg()[0]
            load["below15"] = min(v for v in load.values() if isinstance(v, float)) < 15
            out["rounds"].append({"round": i, "load": load, "font": fonts[fi % len(fonts)], "steps": steps, "count": len(steps[-1]["ids"] or []) if steps[-1]["ids"] is not None else None})
            print("round", i, "ok" if not lost else "LOST %s" % lost["missing"], "load %.1f/%.1f/%.1f" % (load["start"], load.get("mid", -1), load["end"]), flush=True)
            if lost:
                out["lost"] = lost
                break
    finally:
        out["finished"] = datetime.datetime.now().isoformat(timespec="seconds")
        json.dump(out, open(os.path.join(a.outdir, "d12_%s.json" % a.variant), "w", encoding="utf-8"), ensure_ascii=False, indent=1)
        json.dump(tails, open(os.path.join(a.outdir, "d12_%s_logtail.json" % a.variant), "w", encoding="utf-8"), ensure_ascii=False, indent=1)
        ra.cleanup(a.serial, state)
        ra.adb(a.serial, "shell", "wm", "size", "reset")


if __name__ == "__main__":
    main()
