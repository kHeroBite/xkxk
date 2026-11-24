#!/usr/bin/env python3
# 탭→MDI 전환 직후 첫 cascade 와 두 번째 cascade 의 창 사각형을 화면 안 격리 앱에서 비교하고 순환 줄 수·캡처를 남기는 C1 측정 도구
"""
판정 기준(03_데스크톱_화면설계.md C1 절): '육안으로 첫·두 번째 cascade 동일'. 이 도구는 그 기준을 창 사각형 일치로 기계화한다(허용 오차 --tol 기본 0 DIP — 문서에 오차 값은 없다).
  FIRST_EQ_SECOND : 두 cascade 의 문서별 창 사각형이 같다
  DIFF            : 다르다(문서·축별 차이를 기록) — 원인 문서의 '창 높이 약 9px 큼' 증상과 같은 방향인지 dh 로 본다
  UNDET           : 공허한 결론 방지 — 문서 3개 미만, 탭 상태와 첫 cascade 사각형이 같음(전환이 안 일어남), 창 필드 없음
가로 스크롤 막대 유무는 기계로 못 읽는다 → 항상 'UNDET(캡처 판독 필요)' 로 두고 캡처 경로를 남긴다(문서도 UNDET 로 적었다).
'Layout cycle detected' 는 phase(첫/두 번째 cascade) 구간 로그 줄 수를 센다. 문서상 C1 단독 효과는 Hold off 에서 순환이 남아 분리되지 않았으므로 순환 수는 판정이 아니라 참고 기록이다.
사용:
  python3 -B retest_first_cascade_c1.py --onscreen-approved '<누가·언제>' --conf <bin/run 복사본> --out-dir <폴더> [--monitor DISPLAY3] [--terminal-monitor DISPLAY2] [--docs 3] [--tol 0]
      기동은 retest_window_menu_fg.launch(--bg-pos off, 화면 안 격리). 터미널 모니터와 겹치면 실행 거부. 자기 PID 만 app.shutdown. 입력 없음(원격 명령만).
  python3 -B retest_first_cascade_c1.py --self-test
rc: 0 FIRST_EQ_SECOND, 1 DIFF, 2 UNDET, 3 승인 누락·거부, 5 복귀 감지·인자·기동 오류
"""
import argparse
import glob
import json
import os
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(HERE, "..", "..", "common"))
import _paths  # noqa: E402

_paths.add()
sys.path.insert(0, _paths.TOOLS)

CYCLE = "Layout cycle detected"
VOLATILE = {"active", "zIndex", "zScale", "zSource"}


def rects(docs):
    """docs.list → {id: 창 사각형 표현}. 'window' 필드가 있으면 그것, 없으면 변동 필드를 뺀 항목 전체."""
    out = {}
    for d in docs or []:
        out[str(d.get("id"))] = d["window"] if d.get("window") is not None else {k: v for k, v in d.items() if k not in VOLATILE}
    return out


def numbers(v, prefix=""):
    """중첩 값에서 숫자 리프만 {경로: 값}."""
    if isinstance(v, dict):
        return {k2: x for k, sub in v.items() for k2, x in numbers(sub, prefix + str(k) + ".").items()}
    if isinstance(v, (int, float)) and not isinstance(v, bool):
        return {prefix.rstrip("."): v}
    return {}


def diff(a, b, tol):
    """두 rects 표의 차이 [{doc, key, a, b}] (숫자는 tol 초과만, 숫자 아닌 값은 불일치)."""
    out = []
    for doc in sorted(set(a) | set(b)):
        na, nb = numbers(a.get(doc)), numbers(b.get(doc))
        if a.get(doc) is None or b.get(doc) is None:
            out.append({"doc": doc, "key": "(missing)", "a": a.get(doc) is not None, "b": b.get(doc) is not None})
            continue
        for k in sorted(set(na) | set(nb)):
            if k not in na or k not in nb or abs(na[k] - nb[k]) > tol:
                out.append({"doc": doc, "key": k, "a": na.get(k), "b": nb.get(k)})
    return out


def judge(tabbed, first, second, min_docs, tol):
    """(판정, 근거). 공허한 PASS 를 UNDET 로 돌린다."""
    ev = {"docs": len(first), "tol": tol}
    if len(first) < min_docs or len(second) < min_docs:
        return "UNDET", dict(ev, reason="문서 %d/%d개 (<%d)" % (len(first), len(second), min_docs))
    if not any(numbers(v) for v in first.values()):
        return "UNDET", dict(ev, reason="창 사각형 숫자 필드 없음")
    if not diff(tabbed, first, tol):
        return "UNDET", dict(ev, reason="탭 상태와 첫 cascade 사각형이 같다(전환·배치가 일어나지 않음)")
    d = diff(first, second, tol)
    return ("DIFF", dict(ev, diffs=d[:20])) if d else ("FIRST_EQ_SECOND", ev)


def count_cycles(path, start):
    """로그 path 의 start 바이트 이후 'Layout cycle detected' 줄 수와 새 끝 위치."""
    with open(path, "rb") as f:
        f.seek(start)
        data = f.read()
    return data.decode("utf-8", "replace").count(CYCLE), start + len(data)


def newest_log(since):
    logs = [p for p in glob.glob(os.path.join(_paths.TOOLS, "..", "logs", "desktop_*.log")) if not p.endswith("_stdio.log") and os.path.getmtime(p) >= since]
    return max(logs, key=os.path.getmtime) if logs else None


def live(a):
    import user_away_guard as away
    import retest_maximize_monitor as mm0
    import retest_maximize_monitor_fg as fg
    import retest_window_menu_fg as wf
    from retest_remote import Remote
    try:
        away.check()
    except (away.UserReturned, away.GuardUnavailable) as e:
        print("복귀 감지 또는 가드 불가 — 시작하지 않는다:", e)
        return 5
    mons = mm0.wp(0, ["monitors"])[0]["monitors"]
    mons = [mons] if isinstance(mons, dict) else mons
    area = wf.monitor_area(mons, wf.dev(a.monitor))
    bad = area is None and "시험 모니터 없음" or (wf.area_problem(area, mons, wf.dev(a.terminal_monitor)) if area else None)
    if bad:
        print("거부:", bad)
        return 5
    out_dir = os.path.abspath(a.out_dir)
    os.makedirs(out_dir, exist_ok=True)
    before, t0 = fg.run_ps(["-Action", "read"]), time.time()
    a.onscreen_who = a.onscreen_approved
    if wf.launch(a, out_dir) != 0:
        print("화면 안 격리 기동 실패")
        return 5
    r, res, remote = None, {"tool": "retest_first_cascade_c1", "area": area}, Remote()
    log, pos = newest_log(t0 - 5), 0
    try:
        r = remote
        r.call("docs.close", all=True)
        for i in range(a.docs):
            r.call("docs.open", kind="grid", title="C1-%d" % (i + 1), allowDuplicate=True)
        r.call("docs.arrange", mode="tabbed")
        time.sleep(1)
        tabbed = rects(r.call("docs.list"))
        if log:
            pos = count_cycles(log, 0)[1]
        phases = {}
        for name in ("first", "second"):
            r.call("docs.arrange", mode="cascade")
            time.sleep(1.5)
            phases[name] = rects(r.call("docs.list"))
            shot = r.call("screenshot", path=os.path.join(out_dir, "c1_%s_cascade.png" % name))
            n, pos = count_cycles(log, pos) if log else (None, 0)
            res["cycle_lines_" + name] = n
            res["shot_" + name] = shot
        res["verdict"], res["evidence"] = judge(tabbed, phases["first"], phases["second"], a.docs, a.tol)
    finally:
        if r is not None:
            r.call("app.shutdown")  # 자기 PID(방금 기동·연결한 앱)만
        time.sleep(2)
        p = before.split("|")
        if len(p) == 4:
            print("전경·커서 복원:", fg.run_ps(["-Action", "restore", "-Hwnd", p[0], "-X", p[2], "-Y", p[3]]), flush=True)
    res["scrollbar"] = "UNDET(캡처 판독 필요)"
    res["criteria"] = "03 문서 C1 절: 첫·두 번째 cascade 동일(오차 문서에 없음, --tol)"
    with open(os.path.join(out_dir, "c1.json"), "w", encoding="utf-8") as f:
        json.dump(res, f, ensure_ascii=False, indent=1)
    print(json.dumps(res, ensure_ascii=False)[:800])
    return {"FIRST_EQ_SECOND": 0, "DIFF": 1}.get(res.get("verdict"), 2)


def self_test():
    fails = []

    def expect(name, ok):
        print("OK  " if ok else "FAIL", name)
        if not ok:
            fails.append(name)
    w = lambda x, y, wd, h: {"window": {"x": x, "y": y, "w": wd, "h": h}}  # noqa: E731
    docs = lambda *rs: [dict(id=i, **r) for i, r in enumerate(rs)]  # noqa: E731
    tab = rects(docs(w(0, 0, 900, 600), w(0, 0, 900, 600), w(0, 0, 900, 600)))
    c1 = rects(docs(w(10, 10, 600, 400), w(40, 40, 600, 400), w(70, 70, 600, 400)))
    c2 = rects(docs(w(10, 10, 600, 400), w(40, 40, 600, 400), w(70, 70, 600, 400)))
    c2b = rects(docs(w(10, 10, 600, 409), w(40, 40, 600, 409), w(70, 70, 600, 409)))
    expect("동일 → FIRST_EQ_SECOND", judge(tab, c1, c2, 3, 0)[0] == "FIRST_EQ_SECOND")
    v, ev = judge(tab, c1, c2b, 3, 0)
    expect("창 높이 9px 차 → DIFF(dh 기록)", v == "DIFF" and ev["diffs"][0]["key"] == "h" and ev["diffs"][0]["b"] - ev["diffs"][0]["a"] == 9)
    expect("오차 허용(tol 9)이면 동일", judge(tab, c1, c2b, 3, 9)[0] == "FIRST_EQ_SECOND")
    expect("공허: 문서 2개 → UNDET", judge(tab, dict(list(c1.items())[:2]), dict(list(c2.items())[:2]), 3, 0)[0] == "UNDET")
    expect("공허: 탭 상태와 첫 cascade 같음 → UNDET", judge(c1, c1, c2, 3, 0)[0] == "UNDET")
    expect("공허: 숫자 필드 없음 → UNDET", judge(tab, {"a": {}, "b": {}, "c": {}}, {"a": {}, "b": {}, "c": {}}, 3, 0)[0] == "UNDET")
    expect("항목 누락도 차이", diff(c1, {"0": c1["0"]}, 0) != [])
    tmp = os.path.join(os.environ.get("TMPDIR", "/tmp"), "c1_selftest.log")
    with open(tmp, "w", encoding="utf-8") as f:
        f.write("a\nLayout cycle detected x\nb\nLayout cycle detected y\n")
    n, pos = count_cycles(tmp, 0)
    n2, _ = count_cycles(tmp, pos)
    os.unlink(tmp)
    expect("순환 줄 수 구간 계산", n == 2 and n2 == 0)
    expect("승인 없으면 rc 3", main(["--conf", "x", "--out-dir", "/nonexistent_never_created"]) == 3 and not os.path.exists("/nonexistent_never_created"))
    print("self-test %s, 실패 %d건" % ("PASS" if not fails else "FAIL", len(fails)))
    return 1 if fails else 0


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--onscreen-approved", help="실제 모니터 사용 승인 근거(누가·언제, 10자 이상). 없으면 rc=3")
    ap.add_argument("--conf")
    ap.add_argument("--out-dir")
    ap.add_argument("--monitor", default="DISPLAY3")
    ap.add_argument("--terminal-monitor", default="DISPLAY2")
    ap.add_argument("--docs", type=int, default=3)
    ap.add_argument("--tol", type=float, default=0.0)
    ap.add_argument("--self-test", action="store_true")
    a = ap.parse_args(argv)
    if a.self_test:
        return self_test()
    if not a.onscreen_approved or len(a.onscreen_approved) < 10:
        print("거부: 시험 창이 사용자 모니터를 차지한다. --onscreen-approved '<누가·언제>'(10자 이상)가 필요하다.")
        return 3
    if not a.conf or not a.out_dir:
        print("거부: --conf·--out-dir 가 필요하다")
        return 3
    return live(a)


if __name__ == "__main__":
    sys.exit(main())
