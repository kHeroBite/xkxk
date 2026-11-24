#!/usr/bin/env python3
# 넓은 창(열 합 ≤ 셀 영역)에서 DataGrid 'Layout cycle detected' 가 반복되는 요소 고리를 화면 안 격리 앱 로그에서 한 번 추적해 고리 특정 여부만 기록하는 진단 도구
"""
목적(계획서 conv_179419201221 항목 8, 03_데스크톱_화면설계.md '넓은 창 레이아웃 순환'): 진단 전용 — 판정 기준 없음. 수정은 원인 확정 뒤 다음 사이클.
  실제 로그 줄 형식: [Layout]Layout cycle detected. Item '<형식>' was enqueued '10' times. (LayoutQueue`1 #<id>)
고리 특정(이 도구의 조작적 정의 — 문서에 정의 없음): 연속한 순환 줄의 요소 형식열이 주기 p(1~4)로 반복되고 그 구간이 줄의 80% 이상이면 SPECIFIED(주기 열을 기록),
  순환 줄은 있으나 주기가 안 잡히면 NOT_SPECIFIED(상위 요소 형식만 후보로 기록), 순환 줄 0 이면 NO_CYCLE(재현 안 됨), 창이 넓어지지 않았거나 줄이 너무 적으면 UNDET.
Verbose: 앱의 Layout 로그 Verbose 켜는 인자를 확인하지 못했다. 이미 Verbose 로 남긴 로그는 --analyze-log 로 같은 방식으로 분석한다(무효화 출처 줄 해석은 하지 않음 — 형식 미확인).
사용:
  python3 -B retest_wide_window_cycle.py --onscreen-approved '<누가·언제>' --conf <bin/run 복사본> --out-dir <폴더> [--monitor DISPLAY3] [--terminal-monitor DISPLAY2] [--grow 1200] [--wait 3]
      기동은 retest_window_menu_fg.launch(--bg-pos off, 화면 안 격리). 터미널 모니터와 겹치면 거부. 자기 PID 만 app.shutdown. 입력 없음(원격 명령 docs.open·docs.resize·docs.list).
  python3 -B retest_wide_window_cycle.py --analyze-log <앱 로그>
  python3 -B retest_wide_window_cycle.py --self-test
rc: 0 SPECIFIED·NOT_SPECIFIED·NO_CYCLE(분류 완료), 2 UNDET, 3 승인 누락·거부, 5 복귀 감지·인자·기동 오류
"""
import argparse
import collections
import json
import os
import re
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(HERE, "..", "..", "common"))
import _paths  # noqa: E402

_paths.add()
sys.path.insert(0, _paths.TOOLS)

LINE = re.compile(r"Layout cycle detected\. Item '([^']+)' was enqueued '(\d+)' times\.(?: \(LayoutQueue`1 #(\d+)\))?")
MIN_LINES = 6
MIN_GROW = 100  # DIP — 이보다 덜 넓어지면 '넓은 창' 조건이 만들어지지 않은 것(공허한 결론 방지)


def parse(text):
    """로그 문자열 → 요소 형식(짧은 이름) 목록(등장 순서)."""
    return [m.group(1).rsplit(".", 1)[-1] for m in LINE.finditer(text)]


def period(kinds, max_p=4, cover=0.8):
    """kinds 가 주기 p 로 반복되는 가장 작은 p 와 그 주기 열. 못 찾으면 None. 반복 구간 비율이 cover 이상이어야 한다."""
    n = len(kinds)
    for p in range(1, max_p + 1):
        if n < p * 2:
            continue
        match = sum(1 for i in range(p, n) if kinds[i] == kinds[i - p])
        if match / float(n - p) >= cover:
            return p, kinds[:p]
    return None


def classify(kinds, grown):
    """(분류, 근거)."""
    top = collections.Counter(kinds).most_common(4)
    ev = {"cycle_lines": len(kinds), "top_items": top, "grown_dip": grown}
    if grown is not None and grown < MIN_GROW:
        return "UNDET", dict(ev, reason="창이 %s DIP 만 넓어짐(<%d) — 넓은 창 조건 미성립" % (grown, MIN_GROW))
    if not kinds:
        return "NO_CYCLE", ev
    if len(kinds) < MIN_LINES:
        return "UNDET", dict(ev, reason="순환 줄 %d개(<%d) — 고리 판단 표본 부족" % (len(kinds), MIN_LINES))
    pr = period(kinds)
    return ("SPECIFIED", dict(ev, period=pr[0], ring=pr[1])) if pr else ("NOT_SPECIFIED", dict(ev, note="주기 반복 없음 — 상위 요소만 후보"))


def first_id(opened):
    """docs.open 반환(문서 행 목록 list, 코드 RemoteWindowsCommands: DocsJsonZ(opened)) → 첫 문서 id. dict 단건이어도 받는다. 없으면 None."""
    if isinstance(opened, dict):
        return opened.get("id")
    for d in opened or []:
        if isinstance(d, dict) and d.get("id") is not None:
            return d["id"]
    return None


def grown_from(resp):
    """docs.resize 응답(MagnetJson: status·start{x,y,w,h}·final{x,y,w,h}) → 폭 증가량(DIP). status!=Done 이거나 값이 없으면 None.
    docs.list 의 window 는 TopLevel 인덱스(정수)라 폭이 아니다 — 폭은 이 응답의 start/final 로 잰다."""
    if not isinstance(resp, dict) or resp.get("status") != "Done":
        return None
    w0, w1 = (resp.get("start") or {}).get("w"), (resp.get("final") or {}).get("w")
    return round(w1 - w0, 1) if isinstance(w0, (int, float)) and isinstance(w1, (int, float)) else None


def newest_log(since):
    import glob
    logs = [p for p in glob.glob(os.path.join(_paths.TOOLS, "..", "logs", "desktop_*.log")) if not p.endswith("_stdio.log") and os.path.getmtime(p) >= since]
    return max(logs, key=os.path.getmtime) if logs else None


def write(res, out_dir):
    res["criteria"] = "기준 없음(진단 전용). 고리 특정은 이 도구의 조작적 정의"
    if out_dir:
        with open(os.path.join(out_dir, "wide_cycle.json"), "w", encoding="utf-8") as f:
            json.dump(res, f, ensure_ascii=False, indent=1)
    print(json.dumps(res, ensure_ascii=False)[:800])
    return 2 if res["classification"] == "UNDET" else 0


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
    bad = "시험 모니터 없음" if area is None else wf.area_problem(area, mons, wf.dev(a.terminal_monitor))
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
    remote, grown, pos, log = Remote(), None, 0, newest_log(t0 - 5)
    try:
        remote.call("docs.close", all=True)
        doc = first_id(remote.call("docs.open", kind="grid", title="WIDE-1", allowDuplicate=True))
        time.sleep(1)
        if log:
            pos = os.path.getsize(log)  # 이후 구간만 센다
        resp = remote.call("docs.resize", doc=doc, edge="right", d=a.grow)
        time.sleep(a.wait)
        grown = grown_from(resp)  # 폭 증가량은 docs.resize 응답 start/final 로(docs.list window 는 정수 인덱스)
    finally:
        remote.call("app.shutdown")  # 자기 PID(방금 기동·연결한 앱)만
        time.sleep(2)
        p = before.split("|")
        if len(p) == 4:
            print("전경·커서 복원:", fg.run_ps(["-Action", "restore", "-Hwnd", p[0], "-X", p[2], "-Y", p[3]]), flush=True)
    text = ""
    if log:
        with open(log, "rb") as f:
            f.seek(pos)
            text = f.read().decode("utf-8", "replace")
    verdict, ev = classify(parse(text), grown if grown is not None else 0)
    return write({"tool": "retest_wide_window_cycle", "classification": verdict, "evidence": ev, "log": log, "verbose": "미확인(기본 로그만)"}, out_dir)


def self_test():
    fails = []

    def expect(name, ok):
        print("OK  " if ok else "FAIL", name)
        if not ok:
            fails.append(name)
    ln = "[Layout]Layout cycle detected. Item '%s' was enqueued '10' times. (LayoutQueue`1 #16227114)\r\n"
    A, B = "Avalonia.Controls.Primitives.DataGridRowsPresenter", "Avalonia.Controls.Primitives.DataGridColumnHeadersPresenter"
    ring = "".join((ln % A, ln % B) * 5)
    k = parse(ring + "noise\n")
    expect("로그 줄 파싱 10건·짧은 이름", len(k) == 10 and k[0] == "DataGridRowsPresenter")
    expect("주기 2 반복 → SPECIFIED", classify(k, 500)[0] == "SPECIFIED" and classify(k, 500)[1]["period"] == 2)
    rnd = parse("".join(ln % x for x in (A, B, B, A, A, A, B, B)))
    expect("주기 없음 → NOT_SPECIFIED", classify(["a", "b", "c", "a", "c", "b", "c", "a"], 500)[0] == "NOT_SPECIFIED" and rnd != [])
    expect("순환 0 → NO_CYCLE", classify([], 500)[0] == "NO_CYCLE")
    expect("공허: 창이 안 넓어지면 UNDET(순환 0 이어도)", classify([], 10)[0] == "UNDET" and classify(k, 0)[0] == "UNDET")
    expect("공허: 표본 부족 UNDET", classify(k[:3], 500)[0] == "UNDET")
    expect("docs.open list 반환 → 첫 id", first_id([{"id": 3, "window": 0}, {"id": 4}]) == 3 and first_id({"id": 7}) == 7 and first_id([]) is None and first_id(None) is None)
    expect("docs.resize 응답 → 증가량", grown_from({"status": "Done", "start": {"w": 640.0}, "final": {"w": 1840.0}}) == 1200.0
           and grown_from({"status": "NotApplicable", "start": {"w": 1}, "final": {"w": 2}}) is None and grown_from(None) is None and grown_from({"status": "Done"}) is None)
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
    ap.add_argument("--grow", type=float, default=1200.0, help="docs.resize 오른쪽 가장자리 이동량(DIP)")
    ap.add_argument("--wait", type=float, default=3.0)
    ap.add_argument("--analyze-log")
    ap.add_argument("--self-test", action="store_true")
    a = ap.parse_args(argv)
    if a.self_test:
        return self_test()
    if a.analyze_log:
        with open(a.analyze_log, encoding="utf-8", errors="replace") as f:
            verdict, ev = classify(parse(f.read()), None)
        return write({"tool": "retest_wide_window_cycle", "classification": verdict, "evidence": ev, "log": a.analyze_log}, None)
    if not a.onscreen_approved or len(a.onscreen_approved) < 10:
        print("거부: 시험 창이 사용자 모니터를 차지한다. --onscreen-approved '<누가·언제>'(10자 이상)가 필요하다.")
        return 3
    if not a.conf or not a.out_dir:
        print("거부: --conf·--out-dir 가 필요하다")
        return 3
    return live(a)


if __name__ == "__main__":
    sys.exit(main())
