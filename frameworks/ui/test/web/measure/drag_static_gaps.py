# 웹 끌기 trace 의 DrawAndSwap 간격에서 무변화 간격(사용자 결정 D-39)을 판별하고 판정 p95·원래 p95 를 함께 내는 모듈
"""판별 규칙 정본: plan/09_화면설계/04_웹_화면설계.md §4 'A15(H-02) 판정값 정의 개정'(odev-4 분류 규칙 그대로).

간격 g(연속 Display::DrawAndSwap 시각 차, ms) 종류
- ok:       g <= 25ms
- release:  g > 25 이고 놓기(MOUSE_RELEASED 시작)가 간격 안에 있음 → 판정에 남김(실제 지연)
- nochange: g > 25, 놓기 아님, 간격 양끝 2ms 안쪽 BeginFrame 중 무작업(8ms 안 rAF 없음 또는 최대 < 2ms)이 1개 이상,
            CrRendererMain RunTask 겹침 합 < 0.6 g → 판정에서 뺌(무변화 간격)
- slow:     그 밖의 g > 25 → 판정에 남김(느린 렌더)

사용
  python3 drag_static_gaps.py --self-test
  python3 drag_static_gaps.py --traces <trace 폴더>   # dragos_doc{1,3,5}_*.json 을 다시 계산해 문서별 판정값·원래값 출력
"""
import glob
import json
import math
import os
import sys

THR_MS = 25.0        # 이 값을 넘는 간격만 분류한다
EDGE_US = 2000       # 간격 양끝에서 이만큼 안쪽 BeginFrame 만 본다
RAF_WIN_US = 8000    # BeginFrame 뒤 이 시간 안에 시작한 rAF 를 그 프레임의 rAF 로 본다
RAF_WORK_MS = 2.0    # rAF 가 이보다 짧으면 무작업
BUSY_RATIO = 0.6     # 주 스레드 작업 겹침이 간격의 이 비율 미만이어야 무변화
KEPT = ("ok", "release", "slow")


def classify(ev, press, rel, sw):
    """sw: 끌기 구간 DrawAndSwap 시작 시각(us) 정렬 목록. 간격마다 종류 문자열 목록을 돌려준다(길이 len(sw)-1)."""
    if press is None or rel is None or len(sw) < 2:
        return []
    tn = {(e["pid"], e["tid"]): (e.get("args") or {}).get("name") for e in ev if e.get("name") == "thread_name"}

    def th(e):
        return tn.get((e.get("pid"), e.get("tid")))
    raf = [(e["ts"], e.get("dur", 0) / 1000) for e in ev if e.get("ph") == "X" and e.get("name") == "FireAnimationFrame" and th(e) == "CrRendererMain"]
    bf = [e["ts"] for e in ev if e.get("ph") == "X" and e.get("name") == "ExternalBeginFrameSource::OnBeginFrame" and th(e) == "VizCompositorThread"]
    tasks = [(e["ts"], e.get("dur", 0) / 1000) for e in ev if e.get("ph") == "X" and e.get("name") == "ThreadControllerImpl::RunTask" and th(e) == "CrRendererMain"]
    kinds = []
    for a, b in zip(sw, sw[1:]):
        g = (b - a) / 1000
        if g <= THR_MS:
            kinds.append("ok")
            continue
        if a <= rel <= b:
            kinds.append("release")
            continue
        idle_bf = 0
        for t in (t for t in bf if a + EDGE_US < t < b - EDGE_US):
            r = [d for s, d in raf if t <= s <= t + RAF_WIN_US]
            if not r or max(r) < RAF_WORK_MS:
                idle_bf += 1
        busy = sum(max(0, min(b, s + d * 1000) - max(a, s)) for s, d in tasks) / 1000
        kinds.append("nochange" if idle_bf >= 1 and busy < g * BUSY_RATIO else "slow")
    return kinds


def judged(intervals, kinds):
    """판정에 쓰는 간격(무변화 간격 제외). kinds 가 없거나 길이가 다르면 전부 돌려준다(옛 결과 호환)."""
    if not kinds or len(kinds) != len(intervals):
        return list(intervals)
    return [g for g, k in zip(intervals, kinds) if k in KEPT]


def nearest_rank(v, p):
    s = sorted(v)
    return s[max(0, math.ceil(p * len(s)) - 1)] if s else None


def pooled_doc(runs):
    """한 문서의 회차 목록(analyze_drag 결과) → 게이트 방식(전 회차 합친 nearest-rank p95) 판정값·원래값."""
    allv, judv, exc = [], [], 0
    run_p95, run_p95_all = [], []
    for r in runs:
        fi = r.get("frame_intervals_ms") or []
        jv = judged(fi, r.get("frame_interval_kinds"))
        allv += fi
        judv += jv
        exc += len(fi) - len(jv)
        run_p95.append(round(nearest_rank(jv, .95), 1) if jv else None)
        run_p95_all.append(round(nearest_rank(fi, .95), 1) if fi else None)
    rd = lambda x: round(x, 1) if x is not None else None
    return {"p95_ms": rd(nearest_rank(judv, .95)), "p95_all_ms": rd(nearest_rank(allv, .95)), "excluded_static_gaps": exc,
            "sample_intervals": len(judv), "sample_intervals_all": len(allv), "frames": sum(r.get("frames", 0) for r in runs), "runs": len(runs),
            "run_p95_ms": run_p95, "run_p95_all_ms": run_p95_all, "max_ms": rd(max(judv) if judv else None), "max_all_ms": rd(max(allv) if allv else None),
            "kinds_gt25": {k: sum(1 for r in runs for g, kk in zip(r.get("frame_intervals_ms") or [], r.get("frame_interval_kinds") or []) if kk == k)
                           for k in ("nochange", "release", "slow")}}


def pooled_docs(raw):
    """w10_web.py drag_os 결과(dict) → {doc1, doc3, doc5: pooled_doc}."""
    return {"doc%d" % n: pooled_doc(raw.get("dragos_doc%d" % n) or []) for n in (1, 3, 5)}


def trace_run(path):
    """trace 파일 하나 → analyze_drag 와 같은 구간의 간격·종류(재계산용)."""
    ev = json.load(open(path))["traceEvents"]
    press = rel = None
    for e in ev:
        if e.get("name") == "EventLatency" and e.get("ph") == "b":
            t = ((e.get("args") or {}).get("event_latency") or {}).get("event_type")
            if t == "MOUSE_PRESSED" and press is None:
                press = e["ts"]
            if t == "MOUSE_RELEASED":
                rel = e["ts"]
    sw = sorted(e["ts"] for e in ev if e.get("name") == "Display::DrawAndSwap" and e.get("ph") == "X" and press is not None and rel is not None and press <= e["ts"] <= rel + 50000)
    fi = [round((b - a) / 1000, 1) for a, b in zip(sw, sw[1:])]
    return {"frames": len(sw), "frame_intervals_ms": fi, "frame_interval_kinds": classify(ev, press, rel, sw)}


def _ev_fixture():
    """앱·trace 없이 쓰는 합성 trace. 간격: 17(ok) · 60(무변화) · 40(느림) · 30(놓기)."""
    P, R, V = (1, 1), (1, 2), (1, 3)
    ev = [{"name": "thread_name", "pid": 1, "tid": 1, "args": {"name": "CrRendererMain"}},
          {"name": "thread_name", "pid": 1, "tid": 3, "args": {"name": "VizCompositorThread"}},
          {"name": "EventLatency", "ph": "b", "pid": 1, "tid": 9, "ts": 0, "args": {"event_latency": {"event_type": "MOUSE_PRESSED"}}},
          {"name": "EventLatency", "ph": "b", "pid": 1, "tid": 9, "ts": 130000, "args": {"event_latency": {"event_type": "MOUSE_RELEASED"}}}]
    for t in (1000, 18000, 78000, 118000, 148000):
        ev.append({"name": "Display::DrawAndSwap", "ph": "X", "pid": 2, "tid": 1, "ts": t, "dur": 300})
    # 무변화 간격(18000~78000): BeginFrame 2개, rAF 0.2ms, 작업 적음
    for t in (35000, 52000):
        ev.append({"name": "ExternalBeginFrameSource::OnBeginFrame", "ph": "X", "pid": V[0], "tid": V[1], "ts": t, "dur": 50})
        ev.append({"name": "FireAnimationFrame", "ph": "X", "pid": P[0], "tid": P[1], "ts": t + 1000, "dur": 200})
    # 느린 간격(78000~118000): BeginFrame 있고 rAF 14ms(작업)
    ev.append({"name": "ExternalBeginFrameSource::OnBeginFrame", "ph": "X", "pid": V[0], "tid": V[1], "ts": 95000, "dur": 50})
    ev.append({"name": "FireAnimationFrame", "ph": "X", "pid": P[0], "tid": P[1], "ts": 96000, "dur": 14000})
    ev.append({"name": "ThreadControllerImpl::RunTask", "ph": "X", "pid": R[0], "tid": 1, "ts": 96000, "dur": 14000})
    return ev


def self_test():
    fails = []
    ev = _ev_fixture()
    sw = [1000, 18000, 78000, 118000, 148000]
    k = classify(ev, 0, 130000, sw)
    if k != ["ok", "nochange", "slow", "release"]:
        fails.append("분류 %s" % k)
    fi = [17.0, 60.0, 40.0, 30.0]
    if judged(fi, k) != [17.0, 40.0, 30.0] or judged(fi, None) != fi or judged(fi, ["ok"]) != fi:
        fails.append("judged")
    if nearest_rank([1, 2, 3, 4, 5, 6, 7, 8, 9, 10], .95) != 10 or nearest_rank([], .95) is not None:
        fails.append("nearest_rank")
    d = pooled_doc([{"frames": 5, "frame_intervals_ms": fi, "frame_interval_kinds": k}])
    if d["p95_ms"] != 40.0 or d["p95_all_ms"] != 60.0 or d["excluded_static_gaps"] != 1 or d["kinds_gt25"] != {"nochange": 1, "release": 1, "slow": 1}:
        fails.append("pooled_doc %s" % d)
    old = pooled_doc([{"frames": 5, "frame_intervals_ms": fi}])  # 옛 결과(kinds 없음)는 원래값과 같다
    if old["p95_ms"] != old["p95_all_ms"] or old["excluded_static_gaps"] != 0:
        fails.append("옛 결과 호환 %s" % old)
    if classify(ev, None, None, sw) != [] or classify(ev, 0, 130000, [1000]) != []:
        fails.append("빈 입력")
    print("drag_static_gaps self-test", "PASS" if not fails else "FAIL %s" % fails)
    return 1 if fails else 0


def main(argv):
    if len(argv) > 1 and argv[1] == "--self-test":
        return self_test()
    if len(argv) > 2 and argv[1] == "--traces":
        raw = {"dragos_doc%d" % n: [trace_run(f) for f in sorted(glob.glob(os.path.join(argv[2], "dragos_doc%d_*.json" % n)))] for n in (1, 3, 5)}
        out = pooled_docs(raw)
        for k, v in out.items():
            print(k, json.dumps({x: v[x] for x in ("p95_ms", "p95_all_ms", "excluded_static_gaps", "sample_intervals_all", "runs", "kinds_gt25")}, ensure_ascii=False))
        if len(argv) > 3:
            json.dump(out, open(argv[3], "w", encoding="utf-8"), ensure_ascii=False, indent=1)
        return 0
    print(__doc__)
    return 2


if __name__ == "__main__":
    sys.exit(main(sys.argv))
