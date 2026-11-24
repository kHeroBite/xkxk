#!/usr/bin/env python3
# Fold5 Chrome 웹에서 문서 열기 1회를 CDP Tracing 으로 모아 주 스레드 긴 작업·GC·콘솔 표식을 뽑는 A16 진단 도구
"""
사용(가상환경 python, websocket-client 필요):
  python a16_trace.py --out <폴더> --opener X,Y --item X,Y [--url http://127.0.0.1:5180/] [--runs 3] [--long-ms 50] [--settle-s 3.0]
  python a16_trace.py --summarize <trace.json>      # 기기 없이 저장된 trace 만 요약(검사용)
방법:
  - adb forward 로 Chrome 원격 디버깅(CDP)에 붙어 시험 URL 탭(webperf_fold5.py 와 같은 연결 방식)을 잡는다. 다른 탭은 건드리지 않는다.
  - Tracing.start(타임라인·v8·GC·blink.console) 후 메뉴 버튼 탭 → 항목 탭(문서 열기)을 80ms 누름으로 보내고 settle-s 초 뒤 Tracing.end.
    좌표 X,Y 는 webperf_fold5.py 결과(ids.opener·ids.item 의 중심 + web_dy)를 그대로 넣는다. 사용자 앱·다른 탭은 만지지 않는다.
  - 요약: 시험 페이지 렌더러 메인 스레드의 긴 작업(기본 50ms 초과)과 그 하위 이벤트 합, GC(Minor/Major) 횟수·시간,
    console.timeStamp/console.time 표식(blink.console)의 이름·시각을 문서 열기 시작(항목 탭 시각) 기준 ms 로 남긴다.
  - 시행마다 <out>/a16_trace_<k>.json(원본 이벤트)과 <out>/a16_trace_summary.json 을 쓴다. 측정 값은 판정 기준을 바꾸는 데 쓰지 않는다.
"""
import argparse
import json
import os
import sys
import time

LONG_NAMES = ("RunTask", "ThreadControllerImpl::RunTask")
GC_NAMES = ("MinorGC", "MajorGC", "V8.GC_MC_INCREMENTAL", "V8.GCScavenger", "V8.GC_SCAVENGER", "V8.GCCompactor", "V8.GC_MARK_COMPACTOR")
CATS = ("devtools.timeline,disabled-by-default-devtools.timeline,v8,v8.gc,blink.console,blink.user_timing,toplevel,"
        "disabled-by-default-v8.gc")


def page_thread(events):
    """시험 페이지 렌더러 메인 스레드 = CrRendererMain 중 X 이벤트 시간 합이 가장 큰 스레드."""
    mains = {(e["pid"], e["tid"]) for e in events if e.get("ph") == "M" and e.get("name") == "thread_name" and (e.get("args") or {}).get("name") == "CrRendererMain"}
    score = {}
    for e in events:
        k = (e.get("pid"), e.get("tid"))
        if k in mains and e.get("ph") == "X":
            score[k] = score.get(k, 0) + e.get("dur", 0)
    return max(score, key=score.get) if score else None


def summarize(events, t0_us=None, long_ms=50.0):
    """긴 작업·GC·콘솔 표식 요약. t0_us 가 있으면 모든 시각을 그 기준 ms 로 바꾼다(없으면 trace 첫 이벤트)."""
    page = page_thread(events)
    ts_all = [e["ts"] for e in events if e.get("ts")]
    base = t0_us if t0_us is not None else (min(ts_all) if ts_all else 0)
    xs = [e for e in events if e.get("ph") == "X" and (e.get("pid"), e.get("tid")) == page]
    tasks = sorted([e for e in xs if e.get("name") in LONG_NAMES and e.get("dur", 0) > long_ms * 1000], key=lambda e: e["ts"])
    out_tasks = []
    for t in tasks:
        a0, a1 = t["ts"], t["ts"] + t["dur"]
        kids = {}
        for e in xs:
            if e is not t and a0 <= e.get("ts", 0) < a1 and e.get("name") not in LONG_NAMES:
                kids[e["name"]] = kids.get(e["name"], 0) + e.get("dur", 0) / 1000
        out_tasks.append({"start_ms": round((a0 - base) / 1000, 1), "dur_ms": round(t["dur"] / 1000, 1),
                          "children_ms": {k: round(v, 1) for k, v in sorted(kids.items(), key=lambda kv: -kv[1])[:10]}})
    gcs = [e for e in events if e.get("ph") == "X" and (e.get("pid") == (page or (None,))[0]) and (e.get("name") in GC_NAMES or "GC" in e.get("name", ""))]
    gc = {}
    for e in gcs:
        s = gc.setdefault(e["name"], {"n": 0, "total_ms": 0.0, "max_ms": 0.0})
        s["n"] += 1
        s["total_ms"] += e.get("dur", 0) / 1000
        s["max_ms"] = max(s["max_ms"], e.get("dur", 0) / 1000)
    gc = {k: {"n": v["n"], "total_ms": round(v["total_ms"], 1), "max_ms": round(v["max_ms"], 1)} for k, v in gc.items()}
    marks = []
    for e in events:
        if e.get("pid") == (page or (None,))[0] and ("blink.console" in e.get("cat", "") or e.get("name") == "TimeStamp"):
            marks.append({"name": e.get("name"), "label": ((e.get("args") or {}).get("data") or {}).get("message") or (e.get("args") or {}).get("name"),
                          "t_ms": round((e.get("ts", 0) - base) / 1000, 1), "dur_ms": round(e.get("dur", 0) / 1000, 1) if e.get("dur") else None})
    marks.sort(key=lambda m: m["t_ms"])
    return {"page_thread": list(page) if page else None, "long_ms": long_ms, "long_tasks_n": len(out_tasks),
            "long_tasks_total_ms": round(sum(t["dur_ms"] for t in out_tasks), 1), "long_tasks": out_tasks[:20], "gc": gc, "console_marks": marks[:60]}


def main():
    p = argparse.ArgumentParser(description="Fold5 Chrome 웹 문서 열기 1회의 주 스레드 긴 작업·GC·콘솔 표식을 CDP Tracing 으로 추출한다(A16 진단)")
    p.add_argument("--out")
    p.add_argument("--opener", help="메뉴 버튼 탭 좌표 X,Y (웹 화면 좌표, dy 포함)")
    p.add_argument("--item", help="문서 열기 항목 탭 좌표 X,Y")
    p.add_argument("--url", default="http://127.0.0.1:5180/")
    p.add_argument("--runs", type=int, default=3)
    p.add_argument("--long-ms", type=float, default=50.0)
    p.add_argument("--settle-s", type=float, default=3.0)
    p.add_argument("--away-state", default="", help="pc_away.json 경로 — 시행 전마다 present 이면 중단한다")
    p.add_argument("--summarize", help="저장된 trace JSON(이벤트 배열)만 요약하고 끝낸다(기기 불필요)")
    a = p.parse_args()
    if a.summarize:
        with open(a.summarize, encoding="utf-8") as f:
            print(json.dumps(summarize(json.load(f), None, a.long_ms), ensure_ascii=False, indent=1))
        return 0
    if not (a.out and a.opener and a.item):
        p.error("--out, --opener, --item 이 필요하다(또는 --summarize)")
    sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
    import webperf_fold5 as w  # noqa: PLC0415 — 기기 쪽 헬퍼(adb·CDP·tap_hold)를 재사용한다
    os.makedirs(a.out, exist_ok=True)
    op = tuple(int(v) for v in a.opener.split(","))
    it = tuple(int(v) for v in a.item.split(","))
    cdp = w.CDP()
    runs = []
    orig_rot = {k: w.sh("settings get system %s" % k) for k in w.ROT_KEYS}
    try:
        w.lock_portrait()  # 가로면 좌표가 어긋난다 — webperf_fold5.py 와 같은 세로 고정(끝에 원복)
        ok, info = w.ensure_portrait()
        if not ok:
            raise SystemExit("방향 불일치: %s" % info)
        port = a.url.split("//")[1].split("/")[0].split(":")[-1]
        w.adb("reverse", "tcp:%s" % port, "tcp:%s" % port)
        w.sh("am start -a android.intent.action.VIEW -d %s %s" % (a.url, w.CHROME))
        time.sleep(5)
        cdp.new_tab(a.url + "?fresh")
        cdp.call("Page.enable")
        time.sleep(25)
        for k in range(a.runs):
            if a.away_state and '"present"' in open(a.away_state, encoding="utf-8").read():
                print("사용자 복귀(present) — 중단", flush=True)
                break
            w.tap(*op)  # 메뉴 열기(측정 밖)
            time.sleep(2.0)
            stamp = {}

            def act():
                time.sleep(0.5)
                stamp["t"] = time.time()
                w.tap_hold(*it)
                time.sleep(a.settle_s)
            ev = cdp.trace(act, CATS)
            path = os.path.join(a.out, "a16_trace_%d.json" % k)
            with open(path, "w", encoding="utf-8") as f:
                json.dump(ev, f)
            # 항목 탭 시각 = 첫 Input 이벤트(없으면 trace 첫 이벤트)
            inp = [e["ts"] for e in ev if e.get("ts") and "Input" in e.get("name", "")]
            runs.append({"k": k, "file": path, "events": len(ev), **summarize(ev, min(inp) if inp else None, a.long_ms)})
            print(k, "long", runs[-1]["long_tasks_n"], "gc", runs[-1]["gc"], flush=True)
            # Back 키는 앱을 빠져나가므로 쓰지 않는다 — 다음 시행은 같은 문서 열기를 다시 수행한다
        with open(os.path.join(a.out, "a16_trace_summary.json"), "w", encoding="utf-8") as f:
            json.dump({"url": a.url, "opener": op, "item": it, "runs": runs}, f, ensure_ascii=False, indent=1)
    finally:
        cdp.close()
        for k, ov in orig_rot.items():  # 회전 설정 원복(webperf_fold5.py 와 같은 값 복구)
            w.sh("settings delete system %s" % k if ov in ("null", "") else "settings put system %s %s" % (k, ov))
    return 0


if __name__ == "__main__":
    sys.exit(main())
