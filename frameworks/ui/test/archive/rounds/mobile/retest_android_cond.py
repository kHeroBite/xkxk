# Fold5 실기기에서 pdargs 조건(A·B·C·…)별로 census 와 theme.next·font.family 성능을 3회씩 재는 Q4 원인 분리 스크립트
"""
사용: python3 tools/retest_android_cond.py --apk <진단 APK> [--conds A,B,C,A_nogrid] [--runs 3] [--outdir logs/otest_m1]
조건(pdargs 값; 이 APK 에서 인식되는 인자만): A=`--notify-panel on`(기준, 영향 없는 인자로 기동 경로를 맞춤), B=`--loc static`, C=`--notify-panel off`, A_nogrid=A 인데 gantt·form 만 연다.
각 실행: force-stop → `am start -n <MainActivity> --es pdargs "<인자>"` → remote-control.json 새 토큰 읽기 → 문서 3종 열기
         → census(cmd.run perf-reset, theme.census ×4, theme.census themeId=pd-light, perf.get → census_{조건}_{n}.json)
         → perf(theme.next 12회·settings.set fontName 12회 → perf_{조건}_{n}.json, perf.ops 포함).
원복(finally): forward 해제·앱 제거·토큰 파일 삭제·stay_on 원값. 종료 코드 0=완료, 2=준비 오류.
"""
import argparse
import datetime
import json
import os
import statistics
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import retest_android as ra  # noqa: E402

ACT = "com.pd.poc/crc64470901fbb571b1ad.MainActivity"
CONDS = {"A": "--notify-panel on", "B": "--loc static", "C": "--notify-panel off", "A_nogrid": "--notify-panel on"}


def launch(serial, pdargs, info_path):
    """앱을 완전 종료 후 pdargs 로 기동하고 새 토큰을 연결 정보 파일에 쓴다."""
    ra.adb(serial, "shell", "am", "force-stop", ra.PKG)
    ra.adb(serial, "shell", "run-as", ra.PKG, "rm", "-f", "files/PdPoc/remote-control.json")
    ra.adb(serial, "shell", "am start -n %s --es pdargs '%s'" % (ACT, pdargs))
    for _ in range(45):
        rc, out = ra.adb(serial, "exec-out", "run-as", ra.PKG, "cat", "files/PdPoc/remote-control.json")
        if rc == 0 and out.strip().startswith("{"):
            info = json.loads(out)
            info["port"] = ra.HOST_PORT
            info["url"] = "http://localhost:%d/" % ra.HOST_PORT
            with open(info_path, "w", encoding="utf-8") as f:
                json.dump(info, f)
            return True
        time.sleep(2)
    return False


def summarize(rows):
    v = [x["elapsed_ms"] for x in rows if x["ok"] and x["elapsed_ms"] is not None]
    return {"n": len(v), "median": round(statistics.median(v), 1), "min": min(v), "max": max(v)} if v else {"n": 0}


def one_run(Remote, cond, n, outdir, nogrid):
    r = Remote()
    r.call("docs.close", all=True)  # 자동 저장으로 복원된 이전 실행의 문서를 먼저 닫아 실행 간 누적(3→6→9개)을 막는다
    for k in (("gantt", "form") if nogrid else ("grid", "gantt", "form")):
        r.call("docs.open", kind=k)
    calls = []

    def cmd(c, **kw):
        res = r.call(c, **kw)
        calls.append({"cmd": c, "args": kw, "ok": r.calls[-1]["ok"], "error": r.calls[-1]["error"]})
        return res

    import pdctl  # Remote.call 의 첫 인자 이름이 cmd 라 cmd.run 의 인자 'cmd' 를 못 넘기므로 직접 요청한다
    body = json.dumps({"cmd": "cmd.run", "args": {"cmd": "perf-reset"}})
    raw = pdctl.curl(r.info, "api/cmd", body, timeout=60)
    calls.append({"cmd": "cmd.run", "args": {"cmd": "perf-reset"}, "ok": '"ok": true' in raw.replace('"ok":true', '"ok": true'), "raw": raw[:120]})
    for _ in range(4):
        cmd("theme.census")
    cmd("theme.census", themeId="pd-light")
    perf = cmd("perf.get")
    json.dump({"cond": cond, "n": n, "perf": perf, "calls": calls}, open(os.path.join(outdir, "census_%s_%d.json" % (cond, n)), "w", encoding="utf-8"), ensure_ascii=False, indent=1)
    cmd("perf.reset")
    theme, font = [], []
    for i in range(12):
        cmd("theme.next")
        theme.append({"ok": r.calls[-1]["ok"], "elapsed_ms": r.calls[-1]["elapsed_ms"]})
    fonts = r.call("settings.fonts") or ["Noto Sans KR", "monospace"]  # Android 글꼴 목록에 없는 이름(Noto Mono)을 쓰지 않는다
    for i in range(12):
        cmd("settings.set", fontName=fonts[i % len(fonts)])
        font.append({"ok": r.calls[-1]["ok"], "elapsed_ms": r.calls[-1]["elapsed_ms"]})
    ops = r.call("perf.ops")
    perf2 = r.call("perf.get")
    rep = {"cond": cond, "n": n, "docs": "gantt,form" if nogrid else "grid,gantt,form", "theme_next": summarize(theme), "font_family": summarize(font), "perf_ops": ops,
           "theme_overrides": (perf2 or {}).get("theme_overrides") if isinstance(perf2, dict) else None}
    json.dump(dict(rep, theme_rows=theme, font_rows=font), open(os.path.join(outdir, "perf_%s_%d.json" % (cond, n)), "w", encoding="utf-8"), ensure_ascii=False, indent=1)
    return rep


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--apk", required=True)
    p.add_argument("--serial", default="R3CW70202FP")
    p.add_argument("--conds", default="A,B,C,A_nogrid")
    p.add_argument("--runs", type=int, default=3)
    p.add_argument("--outdir", default=os.path.dirname(ra.DEFAULT_OUT))
    a = p.parse_args()
    state, summary, code = {}, [], 2
    try:
        info = ra.prepare(a.serial, a.apk, state)
        if info:
            from retest_remote import Remote
            for cond in a.conds.split(","):
                for n in range(1, a.runs + 1):
                    if not launch(a.serial, CONDS[cond], info):
                        print("기동 실패", cond, n)
                        continue
                    rep = one_run(Remote, cond, n, a.outdir, cond.endswith("nogrid"))
                    summary.append({k: rep[k] for k in ("cond", "n", "docs", "theme_next", "font_family")})
                    print(cond, n, rep["theme_next"], rep["font_family"], flush=True)
            code = 0
    finally:
        ra.cleanup(a.serial, state)
        ra.adb(a.serial, "shell", "wm", "size", "reset")
        json.dump({"finished": datetime.datetime.now().isoformat(timespec="seconds"), "apk": a.apk, "summary": summary}, open(os.path.join(a.outdir, "cond_summary.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=1)
    return code


if __name__ == "__main__":
    sys.exit(main())
