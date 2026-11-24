# 웹 벤치 xlsx 내보내기(ClosedXML) WASM 동작 확인(⑤ 측정 2, odev-gc 요청) — bench.open → load 1만 행 → scenario=xlsx 의 ok·bytes·total_ms·메모리 증가와 콘솔·pageerror 예외를 기록
# 사용: PD_URL=http://127.0.0.1:5201/ python3 tools/retest_browser19.py [--out logs/odevgc/bench_web_Builtin_xlsx.json] [--repeat 2]   (PocXlsx=true 진단판·헤드리스 전용)
import json
import os
import sys
import time

from playwright.sync_api import sync_playwright

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import retest_browser2 as t  # noqa: E402

OUTF = sys.argv[sys.argv.index("--out") + 1] if "--out" in sys.argv else os.path.join(t.OUT, "retest_web19_xlsx.json")
REPEAT = int(sys.argv[sys.argv.index("--repeat") + 1]) if "--repeat" in sys.argv else 2


def raw(page, cmd, args):
    """원격 응답 원문(ok·error 그대로) — bench.open 은 b1126 에서 응답 직렬화 예외가 있었으므로 원문으로 본다."""
    txt = page.evaluate("b => window.pdAutomation.invoke(b)", json.dumps({"cmd": cmd, "args": args}, ensure_ascii=False))
    try:
        return json.loads(txt)
    except Exception:
        return {"ok": False, "error": "JSON 아님: " + str(txt)[:300]}


def main():
    out = {"url": t.URL, "steps": [], "console": []}
    with sync_playwright() as p:
        b = p.chromium.launch(headless=True, args=t.ARGS)
        ctx, page, r = t.boot(b, "?layout-key=otestw2-xlsx")
        page.on("console", lambda m: out["console"].append((m.type, m.text[:400])) if m.type in ("error", "warning") or "Exception" in m.text else None)
        page.on("pageerror", lambda e: out["console"].append(("pageerror", str(e)[:400])))
        r.call("docs.close", all=True)
        for cmd, args in (("bench.env", {}), ("bench.open", {"target": "grid"}), ("bench.grid", {"scenario": "load", "rows": 10000})):
            v = raw(page, cmd, args)
            out["steps"].append({"cmd": cmd, "args": args, "ok": v.get("ok"), "error": (v.get("error") or "")[:600] or None, "result": v.get("result")})
            print(cmd, v.get("ok"), (v.get("error") or "")[:160], flush=True)
            time.sleep(1.0)
        for i in range(1, REPEAT + 1):
            v = raw(page, "bench.grid", {"scenario": "xlsx", "xlsxRows": 10000})
            res = v.get("result") or {}
            x = res.get("xlsx") if isinstance(res, dict) else None
            out["steps"].append({"cmd": "bench.grid", "args": {"scenario": "xlsx", "xlsxRows": 10000}, "run": i, "ok": v.get("ok"), "error": (v.get("error") or "")[:1200] or None,
                                 "xlsx": x, "memory_after": res.get("memory_after") if isinstance(res, dict) else None})
            print("xlsx", i, v.get("ok"), json.dumps(x, ensure_ascii=False)[:400], (v.get("error") or "")[:300], flush=True)
            time.sleep(1.0)
        page.screenshot(path=os.path.splitext(OUTF)[0] + ".png")
        b.close()
    xs = [s for s in out["steps"] if s.get("args", {}).get("scenario") == "xlsx"]
    out["verdict"] = "PASS" if xs and all(s.get("ok") and isinstance(s.get("xlsx"), dict) and s["xlsx"].get("ok") is not False and (s["xlsx"].get("bytes") or 0) > 0 for s in xs) else "FAIL"
    os.makedirs(os.path.dirname(os.path.abspath(OUTF)), exist_ok=True)
    with open(OUTF, "w", encoding="utf-8") as f:
        json.dump(out, f, ensure_ascii=False, indent=1)
    print("VERDICT", out["verdict"], "console", out["console"][:5], flush=True)


if __name__ == "__main__":
    main()
