# Fold5 실기기에서 files/pdargs.txt 로 조건(A~F·A_nogrid)을 주입해 적용 확인 후 census·theme.next·font 성능을 3회씩 재는 스크립트
"""
사용: python3 test/mobile/measure/retest_android_cond2.py --apk <진단 APK> [--conds A,B,C,D,E,F,A_nogrid] [--runs 3] [--outdir logs/otest_m1/i0235]
기동: force-stop → run-as 로 files/pdargs.txt 작성(1회용, 매 실행마다) → monkey 기동 → 새 토큰 읽기.
적용 확인(결과 JSON 의 checks 에 기록, 기대와 다르면 해당 조건 무효):
  - 공통: logcat '[host] pdargs(file)=' 줄, 시작 직후 문서 수(docs.list), 시각 트리 노드 수(tree.get named=false)
  - B: lang.get staticMode=true·staticCount>0   - C: panel.bounds NotificationPane=null·트리 노드 수 감소
  - D: perf.get chrome_buttons=="min"            - E·F: perf.get theme_overrides 에 fluent=off / newTokens=off
각 실행: docs.close all → 문서 열기(A_nogrid 는 gantt·form 만) → cmd.run command=perf-reset → theme.census ×4 + themeId=pd-light → perf.get
         → census_{조건}_{n}.json, theme.next 12회·settings.set fontName 12회 → perf_{조건}_{n}.json
원복(finally): forward 해제·앱 제거·토큰 파일 삭제·stay_on 원값. 종료 코드 0=완료, 2=준비 오류.
"""
import argparse
import datetime
import json
import os
import statistics
import sys
import time

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "common"))  # D-TESTDIR: test/common/_paths.py
import _paths  # noqa: E402
_paths.add()
import retest_android as ra  # noqa: E402

CONDS = {
    "A": "--fresh", "B": "--fresh --loc static", "C": "--fresh --notify-panel off", "D": "--fresh --chrome-buttons min",
    "E": "--fresh --fluent-override off", "F": "--fresh --theme-new-tokens off", "A_nogrid": "--fresh",
}


def count_nodes(node):
    n = 0
    if isinstance(node, dict):
        n += 1 if node.get("type") else 0
        for v in node.values():
            if isinstance(v, (dict, list)):
                n += count_nodes(v)
    elif isinstance(node, list):
        for v in node:
            n += count_nodes(v)
    return n


def launch(serial, args, info_path):
    """앱을 종료하고 files/pdargs.txt 를 쓴 뒤 기동해 새 토큰을 연결 정보 파일에 쓴다."""
    ra.adb(serial, "shell", "am", "force-stop", ra.PKG)
    ra.adb(serial, "shell", "run-as", ra.PKG, "rm", "-f", "files/" + "/".join(_paths.get("data.segments")) + "/remote-control.json")
    ra.adb(serial, "logcat", "-c")
    ra.adb(serial, "shell", "run-as %s sh -c 'echo \"%s\" > files/pdargs.txt'" % (ra.PKG, args))
    ra.adb(serial, "shell", "monkey", "-p", ra.PKG, "-c", "android.intent.category.LAUNCHER", "1")
    for _ in range(45):
        rc, out = ra.adb(serial, "exec-out", "run-as", ra.PKG, "cat", "files/" + "/".join(_paths.get("data.segments")) + "/remote-control.json")
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


def one_run(Remote, serial, cond, n, outdir):
    r = Remote()
    chk = {}
    rc, log = ra.adb(serial, "logcat", "-d", "-v", "brief")
    chk["logcat_pdargs_file"] = [ln[-120:] for ln in log.splitlines() if "pdargs(file)" in ln][:2]
    chk["docs_at_start"] = len(r.call("docs.list") or [])
    lang = r.call("lang.get") or {}
    chk["staticMode"], chk["staticCount"] = lang.get("staticMode"), lang.get("staticCount")
    chk["tree_nodes_no_docs"] = count_nodes(r.call("tree.get", named=False, depth=60) or {})
    pb = r.call("panel.bounds") or {}
    chk["NotificationPane_bounds"] = pb.get("NotificationPane")
    r.call("docs.close", all=True)
    for k in (("gantt", "form") if cond.endswith("nogrid") else ("grid", "gantt", "form")):
        r.call("docs.open", kind=k)
    calls = []

    def cmd(c, **kw):
        res = r.call(c, **kw)
        calls.append({"cmd": c, "args": kw, "ok": r.calls[-1]["ok"], "error": r.calls[-1]["error"]})
        return res

    cmd("cmd.run", command="perf-reset")
    for _ in range(4):
        cmd("theme.census")
    cmd("theme.census", themeId="pd-light")
    perf = cmd("perf.get")
    chk["theme_overrides"] = (perf or {}).get("theme_overrides") if isinstance(perf, dict) else None
    chk["chrome_buttons"] = (perf or {}).get("chrome_buttons") if isinstance(perf, dict) else None
    tc = ((perf or {}).get("theme_census") or {}) if isinstance(perf, dict) else {}
    chk["census_baseline_total"] = (tc.get("baseline") or {}).get("total")
    json.dump({"cond": cond, "n": n, "checks": chk, "perf": perf, "calls": calls}, open(os.path.join(outdir, "census_%s_%d.json" % (cond, n)), "w", encoding="utf-8"), ensure_ascii=False, indent=1)
    cmd("perf.reset")
    theme, font = [], []
    for _ in range(12):
        cmd("theme.next")
        theme.append({"ok": r.calls[-1]["ok"], "elapsed_ms": r.calls[-1]["elapsed_ms"]})
    fonts = r.call("settings.fonts") or ["Noto Sans KR", "monospace"]  # Android 글꼴 목록에 없는 이름(Noto Mono)을 쓰지 않는다
    for i in range(12):
        cmd("settings.set", fontName=fonts[i % len(fonts)])
        font.append({"ok": r.calls[-1]["ok"], "elapsed_ms": r.calls[-1]["elapsed_ms"]})
    ops = r.call("perf.ops")
    rep = {"cond": cond, "n": n, "args": CONDS[cond], "checks": chk, "theme_next": summarize(theme), "font_family": summarize(font), "perf_ops": ops}
    json.dump(dict(rep, theme_rows=theme, font_rows=font), open(os.path.join(outdir, "perf_%s_%d.json" % (cond, n)), "w", encoding="utf-8"), ensure_ascii=False, indent=1)
    return rep


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--apk", required=True)
    p.add_argument("--serial", default="R3CW70202FP")
    p.add_argument("--conds", default="A,B,C,D,E,F,A_nogrid")
    p.add_argument("--runs", type=int, default=3)
    p.add_argument("--outdir", default=os.path.join(os.path.dirname(ra.DEFAULT_OUT), "i0235"))
    a = p.parse_args()
    os.makedirs(a.outdir, exist_ok=True)
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
                    rep = one_run(Remote, a.serial, cond, n, a.outdir)
                    summary.append({k: rep[k] for k in ("cond", "n", "args", "checks", "theme_next", "font_family")})
                    print(cond, n, rep["theme_next"]["median"], rep["font_family"]["median"], json.dumps(rep["checks"], ensure_ascii=False)[:260], flush=True)
            code = 0
    finally:
        ra.cleanup(a.serial, state)
        ra.adb(a.serial, "shell", "wm", "size", "reset")
        json.dump({"finished": datetime.datetime.now().isoformat(timespec="seconds"), "apk": a.apk, "summary": summary}, open(os.path.join(a.outdir, "cond_summary.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=1)
    return code


if __name__ == "__main__":
    sys.exit(main())
