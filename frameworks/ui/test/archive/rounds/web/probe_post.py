# retest_web_probe.py 결과(webU_probe_*.json)를 앞값 부재=0 으로 보고 grid_probe_* 차이와 H1 판독 보조값을 표로 다시 내는 후처리 스크립트(otest-m-3)
"""
사용: python3 -I test/perf/measure/probe_post.py <폴더> [--tag i1835rc]
다시 읽은 직후 계수기는 0 이라 perf.get 에 grid_probe_* 키가 없다 — 이때 앞값을 0 으로 본다.
출력: <폴더>/webU_probe_table.md, <폴더>/webU_probe_post.json
"""
import glob
import json
import os
import sys

SUMS = ["grid_probe_gestures", "grid_probe_sum_dy", "grid_probe_multi_frames", "grid_probe_lost_dy"]
REF = ["grid_probe_per_frame_max", "grid_probe_move_first_ms_max", "grid_probe_move_last_ms", "grid_probe_move_intermediate_last"]


def main():
    d = sys.argv[1]
    rows = []
    for f in sorted(glob.glob(os.path.join(d, "webU_probe_*_r*.json"))):
        j = json.load(open(f, encoding="utf-8"))
        b, a = j.get("probe_before") or {}, j.get("probe_after") or {}
        delta = {k: round((a.get(k) or 0) - (b.get(k) or 0), 3) for k in SUMS}
        fin = j.get("finger_css") or 205.9
        ratio = round((j.get("d_offsetY") or 0) / fin, 3)
        sdy, lost = delta["grid_probe_sum_dy"], delta["grid_probe_lost_dy"]
        pd = j.get("js_pd") or {}
        raf = j.get("js_raf_during")
        rows.append({"file": os.path.basename(f), "kind": j.get("kind"), "rep": j.get("rep"), "at": j.get("at"),
                     "docs_at_open": ((j.get("condition") or {}).get("docs_at_open") or {}).get("count"), "normalized": (j.get("condition") or {}).get("normalized"),
                     "d_offsetY": j.get("d_offsetY"), "ratio_offset": ratio, "one_minus_ratio": round(1 - ratio, 3),
                     **delta, "abs_sum_dy": abs(sdy), "sum_dy_vs_201": "미달" if abs(sdy) < 201 else "이상",
                     "lost_over_sum": round(lost / sdy, 3) if sdy else None,
                     "offset_over_sum": round((j.get("d_offsetY") or 0) / sdy, 3) if sdy else None,
                     **{k: a.get(k) for k in REF}, "js_move": pd.get("move"), "js_cancel": pd.get("cancel"), "js_coalesced": pd.get("coalesced"),
                     "js_handlerMs_per_move": round(pd["handlerMs"] / pd["move"], 2) if pd.get("move") else None, "js_handlerMax": round(pd.get("handlerMax") or 0, 1),
                     "js_raf_during_wait": raf, "touchAction": pd.get("touchAction"), "dpr": j.get("dpr"), "host_load1": (j.get("host_load") or [None])[0]})
    json.dump({"note": "앞값 부재=0(다시 읽은 직후). raf 는 swipe 시작~2.5초 대기 끝까지의 증가분", "rows": rows}, open(os.path.join(d, "webU_probe_post.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=1)
    cols = ["kind", "rep", "docs_at_open", "d_offsetY", "ratio_offset", "grid_probe_gestures", "grid_probe_sum_dy", "sum_dy_vs_201", "grid_probe_lost_dy",
            "lost_over_sum", "one_minus_ratio", "offset_over_sum", "grid_probe_multi_frames", "grid_probe_per_frame_max", "js_move", "js_cancel", "grid_probe_move_first_ms_max", "host_load1"]
    out = ["# GridScrollProbe 원자료 표(i1835rc, Fold5 Chrome 펼침, 205.9 CSS px, 회마다 다시 읽기)", "",
           "판독은 odev-8n 이 한다. 아래는 원자료 정리뿐이다. 앞값 부재=0 으로 계산했다.", "",
           "| " + " | ".join(cols) + " |", "|" + "---|" * len(cols)]
    for r in rows:
        out.append("| " + " | ".join(str(r.get(c)) for c in cols) + " |")
    open(os.path.join(d, "webU_probe_table.md"), "w", encoding="utf-8").write("\n".join(out) + "\n")
    print("\n".join(out))


if __name__ == "__main__":
    main()
