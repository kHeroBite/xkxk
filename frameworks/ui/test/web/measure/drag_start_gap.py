# 웹 끌기 trace 에서 누름 뒤 창이 처음 움직이기까지의 시작 구간 최대 프레임 간격(A29)을 내는 모듈
"""판정 기준 정본: plan/09_화면설계/01a_화면설계_공통원칙_끌기경량크롬.md 'A29 끌기 시작 멈춤 기준'.
이 모듈은 값만 계산한다. 문턱(판정)은 판정기 한 곳에 둔다.

시작 구간(측정 전 고정)
- 점 목록 = [누름(MOUSE_PRESSED 시작)] + 끌기 구간 DrawAndSwap 중 '첫 이동 프레임' 다음 1개까지.
- 첫 이동 프레임 = 누름 뒤 처음으로 작업이 있는 rAF(FireAnimationFrame ≥ RAF_WORK_MS, CrRendererMain)가 시작한 뒤 첫 DrawAndSwap.
  (창이 움직이려면 Avalonia 가 rAF 안에서 그려야 한다. 그 다음 간격 1개는 창이 처음 한 걸음 움직이는 간격이다.)
- 간격 종류는 drag_static_gaps.classify(D-39)를 그대로 쓰고, 무변화 간격(자석 유지처럼 앱이 그릴 것이 없던 간격)은 뺀다.
- 회차값 = 남은 간격의 최대. 문서값 start_p95_ms = 회차값들의 nearest-rank p95(12회면 최대).
- 첫 이동 프레임을 찾지 못하면 회차값은 None 이고 문서값 계산에서 빠진다(start_missing 에 센다).

사용
  python3 drag_start_gap.py --self-test
  python3 drag_start_gap.py --traces <trace 폴더>                      # 문서별 start_p95_ms 출력
  python3 drag_start_gap.py --traces <trace 폴더> --merge <perf_h02.json>  # docs.docN 에 start_* 필드만 더한다(기존 필드 불변 확인)
"""
import glob
import json
import os
import sys

import drag_static_gaps as DSG

RAF_WORK_MS = DSG.RAF_WORK_MS  # 무변화 판별과 같은 '작업 있는 rAF' 기준
FIELDS = ("start_p95_ms", "start_run_ms", "start_runs", "start_missing", "start_rule")
RULE = "누름~첫 이동 프레임+1간격, D-39 무변화 제외, 회차 최대의 nearest-rank p95(drag_start_gap.py)"


def _press_rel(ev):
    press = rel = None
    for e in ev:
        if e.get("name") == "EventLatency" and e.get("ph") == "b":
            t = ((e.get("args") or {}).get("event_latency") or {}).get("event_type")
            if t == "MOUSE_PRESSED" and press is None:
                press = e["ts"]
            if t == "MOUSE_RELEASED":
                rel = e["ts"]
    return press, rel


def start_gap(ev, press, rel, sw):
    """sw: 끌기 구간 DrawAndSwap 시작 시각(us) 정렬 목록(analyze_drag 와 같은 목록). 시작 구간 값 dict 를 돌려준다."""
    empty = {"start_gap_ms": None, "start_gaps_ms": [], "start_kinds": [], "start_move_frame_ms": None}
    if press is None or rel is None or not sw:
        return empty
    tn = {(e["pid"], e["tid"]): (e.get("args") or {}).get("name") for e in ev if e.get("name") == "thread_name"}
    raf = sorted(e["ts"] for e in ev if e.get("ph") == "X" and e.get("name") == "FireAnimationFrame"
                 and tn.get((e.get("pid"), e.get("tid"))) == "CrRendererMain" and press <= e["ts"] <= rel and e.get("dur", 0) / 1000 >= RAF_WORK_MS)
    if not raf:
        return empty
    i = next((k for k, t in enumerate(sw) if t >= raf[0]), None)
    if i is None:
        return empty
    pts = [press] + sw[:i + 2]
    gaps = [(b - a) / 1000 for a, b in zip(pts, pts[1:])]
    kinds = DSG.classify(ev, press, rel, pts)
    kept = DSG.judged(gaps, kinds)
    return {"start_gap_ms": round(max(kept), 1) if kept else None, "start_gaps_ms": [round(g, 1) for g in gaps], "start_kinds": kinds,
            "start_move_frame_ms": round((sw[i] - press) / 1000, 1)}


def trace_start(path):
    """trace 파일 하나 → 시작 구간 값(재분석용, analyze_drag 와 같은 DrawAndSwap 구간)."""
    ev = json.load(open(path))["traceEvents"]
    press, rel = _press_rel(ev)
    sw = sorted(e["ts"] for e in ev if e.get("name") == "Display::DrawAndSwap" and e.get("ph") == "X"
                and press is not None and rel is not None and press <= e["ts"] <= rel + 50000)
    return start_gap(ev, press, rel, sw)


def pooled_start(runs):
    """한 문서의 회차 목록(start_gap 결과 또는 analyze_drag 결과) → 문서값."""
    vals = [r.get("start_gap_ms") for r in runs]
    ok = [v for v in vals if v is not None]
    p = DSG.nearest_rank(ok, .95)
    return {"start_p95_ms": round(p, 1) if p is not None else None, "start_run_ms": vals, "start_runs": len(runs),
            "start_missing": len(vals) - len(ok), "start_rule": RULE}


def traces_docs(folder):
    return {"doc%d" % n: pooled_start([trace_start(f) for f in sorted(glob.glob(os.path.join(folder, "dragos_doc%d_*.json" % n)))]) for n in (1, 3, 5)}


def merge(perf_path, add):
    """perf_h02.json docs.docN 에 start_* 필드만 더한다. 기존 필드 값이 바뀌면 쓰지 않고 오류."""
    with open(perf_path, encoding="utf-8") as f:
        before = json.load(f)
    after = json.loads(json.dumps(before))
    for k, v in add.items():
        after.setdefault("docs", {}).setdefault(k, {}).update({x: v[x] for x in FIELDS})
    same = all(after["docs"][k].get(x) == before["docs"][k].get(x) for k in before.get("docs", {}) for x in before["docs"][k] if x not in FIELDS) \
        and all(after[x] == before[x] for x in before if x != "docs")
    if not same:
        raise SystemExit("기존 필드가 바뀌어 쓰지 않았다")
    with open(perf_path, "w", encoding="utf-8") as f:
        json.dump(after, f, ensure_ascii=False, indent=1)
    return same


def _ev_fixture(stall):
    """합성 trace. 누름 0, 놓기 300ms. stall=True 면 누름 뒤 150ms 작업(멈춤), False 면 같은 길이 무작업(자석 유지)."""
    P, V = (1, 1), (1, 3)
    ev = [{"name": "thread_name", "pid": 1, "tid": 1, "args": {"name": "CrRendererMain"}},
          {"name": "thread_name", "pid": 1, "tid": 3, "args": {"name": "VizCompositorThread"}},
          {"name": "EventLatency", "ph": "b", "pid": 1, "tid": 9, "ts": 0, "args": {"event_latency": {"event_type": "MOUSE_PRESSED"}}},
          {"name": "EventLatency", "ph": "b", "pid": 1, "tid": 9, "ts": 300000, "args": {"event_latency": {"event_type": "MOUSE_RELEASED"}}}]
    for t in (40000, 80000, 120000):
        ev.append({"name": "ExternalBeginFrameSource::OnBeginFrame", "ph": "X", "pid": V[0], "tid": V[1], "ts": t, "dur": 50})
    if stall:
        ev.append({"name": "ThreadControllerImpl::RunTask", "ph": "X", "pid": P[0], "tid": P[1], "ts": 10000, "dur": 150000})
    for t in (5000, 165000, 182000):  # 첫 rAF 0.2ms(무작업), 165 에 작업 rAF → 첫 이동 프레임 170
        ev.append({"name": "FireAnimationFrame", "ph": "X", "pid": P[0], "tid": P[1], "ts": t, "dur": 200 if t == 5000 else 8000})
    for t in (170000, 187000, 204000):
        ev.append({"name": "Display::DrawAndSwap", "ph": "X", "pid": 2, "tid": 1, "ts": t, "dur": 300})
    return ev


def self_test():
    fails = []
    sw = [170000, 187000, 204000]
    a = start_gap(_ev_fixture(True), 0, 300000, sw)
    if a["start_gap_ms"] != 170.0 or a["start_gaps_ms"] != [170.0, 17.0] or a["start_kinds"] != ["slow", "ok"] or a["start_move_frame_ms"] != 170.0:
        fails.append("멈춤 %s" % a)
    b = start_gap(_ev_fixture(False), 0, 300000, sw)
    if b["start_gap_ms"] != 17.0 or b["start_kinds"] != ["nochange", "ok"]:
        fails.append("자석 유지 %s" % b)
    if start_gap(_ev_fixture(True), None, None, sw)["start_gap_ms"] is not None or start_gap(_ev_fixture(True), 0, 300000, [])["start_gap_ms"] is not None:
        fails.append("빈 입력")
    d = pooled_start([{"start_gap_ms": v} for v in (10.0, 20.0, 150.0, None)])
    if d["start_p95_ms"] != 150.0 or d["start_missing"] != 1 or d["start_runs"] != 4:
        fails.append("pooled %s" % d)
    if pooled_start([])["start_p95_ms"] is not None:
        fails.append("빈 문서")
    print("drag_start_gap self-test", "PASS" if not fails else "FAIL %s" % fails)
    return 1 if fails else 0


def main(argv):
    if len(argv) > 1 and argv[1] == "--self-test":
        return self_test()
    if len(argv) > 2 and argv[1] == "--traces":
        out = traces_docs(argv[2])
        for k, v in out.items():
            print(k, json.dumps({x: v[x] for x in ("start_p95_ms", "start_runs", "start_missing", "start_run_ms")}, ensure_ascii=False))
        if len(argv) > 4 and argv[3] == "--merge":
            merge(argv[4], out)
            print("merged", argv[4], "기존 필드 불변")
        return 0
    print(__doc__)
    return 2


if __name__ == "__main__":
    sys.exit(main(sys.argv))
