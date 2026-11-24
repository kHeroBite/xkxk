# 판 이분 탐색 결과 요약 — dpr 과 무관한 판정 지표(화면 안(x>=0)에 있는데 visible=false 인 셀 수)를 단계별로 계산해 판마다 한 줄로 정리
# 사용: python3 tools/retest_browser23_summary.py   (logs/odevgc/hscroll_bisect/hscroll_*.json 을 읽어 bisect_table.json·표준 출력)
import glob
import json
import os

HERE = os.path.dirname(os.path.abspath(__file__))
D = os.path.join(HERE, "..", "logs", "odevgc", "hscroll_bisect")


def hidden_in_view(step, right):
    """앞 3행에서 화면 안(0<=x<right — 왼쪽 밖으로 밀리지도, 오른쪽 끝을 넘지도 않음)인데 visible=false 인 셀 수.
    오른쪽 끝 밖 셀은 그리드가 정상적으로 숨기므로 뺀다. right = 뷰포트 폭 800 ÷ dpr(앱 DIP 폭 — dpr 2.625 에서 폰 모드로 바뀜, 실측) − 여유 20.
    색 수는 dpr 에 따라 캡처 영역이 어긋나 판정에 쓰지 않는다."""
    n = 0
    for row in (step.get("cells") or {}).get("rows", []):
        for text, vis, x in row.get("cells", []):
            if x is not None and 0 <= x < right and vis is False:
                n += 1
    return n


rows = []
for f in sorted(glob.glob(os.path.join(D, "hscroll_*_dpr*.json"))):
    js = json.load(open(f, encoding="utf-8"))
    name = os.path.basename(f)[len("hscroll_"):-len(".json")]
    right = 800.0 / (js.get("dpr") or 1.0) - 20
    st = {s["step"]: hidden_in_view(s, right) for s in js["steps"]}
    body = [v for k, v in st.items() if k not in ("before", "nudge1px", "after2s")]
    blank = max(body) > 0 if body else None
    rows.append({"run": name, "dpr": js.get("dpr"), "hidden_by_step": st, "blank": blank,
                 "refilled_by_input": blank and st.get("nudge1px", 1) == 0, "refilled_after_2s": blank and st.get("after2s", 1) == 0})
    print("%-22s dpr=%-5s blank=%-5s nudge=%s 2s=%s  %s" % (name, js.get("dpr"), blank, st.get("nudge1px"), st.get("after2s"), st), flush=True)
json.dump(rows, open(os.path.join(D, "bisect_table.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=1)
