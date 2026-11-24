# 차트 D-50 가중 점수·판정 보류·민감도 계산 모듈 — chart_table.py 가 집계 결과(res)를 넘겨 호출한다
LIBS = ["scottplot", "oxyplot", "procharts", "livecharts"]
PLATS = ["desktop", "android", "web"]

# 가중치(사용자·lead 승인, draft_chart_verdict 3-1·10-1): 합 100
WEIGHTS = {"기능": 20, "성능": 20, "메모리": 15, "사용자지정": 10, "실시간": 5, "화려함": 5, "라이선스": 5, "한글": 10, "플랫폼": 5, "유지보수": 5}
# 측정으로 정하지 않는 항목의 고정 점수(draft_chart_verdict 3-1 표)
FIXED = {
    "scottplot": {"기능": 4, "사용자지정": 4, "라이선스": 5, "플랫폼": 4, "유지보수": 5, "한글_기본": 4},
    "oxyplot": {"기능": 3, "사용자지정": 3, "라이선스": 5, "플랫폼": 3, "유지보수": 3, "한글_기본": 3},
    "procharts": {"기능": 5, "사용자지정": 4, "라이선스": 5, "플랫폼": 3, "유지보수": 2, "한글_기본": 1},
    "livecharts": {"기능": 4, "사용자지정": 4, "라이선스": 4, "플랫폼": 2, "유지보수": 3, "한글_기본": 5},
}
BUILTIN_ANIM = {"livecharts": True}  # 내장 애니메이션(조사 2-6). scottplot 은 proto 시제품으로 M10 판정
# R3 기존 판정(chart_procedure 4절: 데스크톱·Android 는 oxyplot 만 새로 잰다). 근거 component_study 8-4·draft 2-2(C)
PRIOR_R3 = {("scottplot", "desktop"): True, ("scottplot", "android"): True, ("livecharts", "desktop"): True,
            ("livecharts", "android"): True, ("procharts", "desktop"): False, ("procharts", "android"): False}
# 최종 판정에서 미측정이면 '판정 보류' 가 되는 측정 항목(lead 19:2x)
MEASURED_KEYS = ("성능", "메모리", "실시간", "한글")
NAMES = {"scottplot": "ScottPlot 5.1.59", "oxyplot": "OxyPlot.Avalonia 2.2.0", "procharts": "ProCharts 12.1.0.6", "livecharts": "LiveCharts2 2.1-dev"}


def 측정된(res, lib, key):
    return [res[lib][p][key] for p in PLATS if res[lib][p].get(key) is not None]


def 다있음(res, lib, req, *keys):
    return bool(req) and all(res[lib][p].get(k) is not None for p in req for k in keys)


def 점수(res, lib, excl=None):
    """측정 항목 점수와 미측정 목록, 관문 결과를 낸다. 대상 플랫폼(제외 플랫폼 뺌) 중 하나라도 값이 없으면 그 항목은 None(미측정)."""
    f, 미 = FIXED[lib], []
    req = [p for p in PLATS if (p, lib) not in (excl or {})]
    a_ok = [v <= 500 for v in 측정된(res, lib, "A_raw")]
    b_ok = [res[lib][p]["B_fps"] >= 30 and (res[lib][p].get("B_ratio") or 0) >= 0.99 for p in PLATS if res[lib][p].get("B_fps") is not None]
    c_ok = [res[lib][p]["R3_ok"] for p in PLATS if "R3_ok" in res[lib][p]]
    s = {k: f[k] for k in ("기능", "사용자지정", "라이선스", "플랫폼", "유지보수")}
    if not 다있음(res, lib, req, "A_raw", "B_fps"):
        s["성능"] = None
    else:
        실패 = sum(1 for p in req if not (res[lib][p]["A_raw"] <= 500 and res[lib][p]["B_fps"] >= 30 and (res[lib][p].get("B_ratio") or 0) >= 0.99))
        s["성능"] = 5 if 실패 == 0 and max(res[lib][p]["A_raw"] for p in req) <= 100 else {0: 4, 1: 3, 2: 2}.get(실패, 1)
    bpp = 측정된(res, lib, "R7_bpp")
    if 다있음(res, lib, req, "R7_bpp"):
        w = max(bpp)
        s["메모리"] = 5 if w <= 4 else 4 if w <= 16 else 3 if w <= 64 else 2 if w <= 256 else 1
    else:
        s["메모리"] = None
    fps = 측정된(res, lib, "B_fps")
    if 다있음(res, lib, req, "B_fps"):
        w = min(fps)
        s["실시간"] = 5 if w >= 60 else 4 if w >= 45 else 3 if w >= 30 else 2 if w >= 20 else 1
    else:
        s["실시간"] = None
    m10 = [res[lib][p]["M10_ok"] for p in PLATS if "M10_ok" in res[lib][p]]
    s["화려함"] = 5 if BUILTIN_ANIM.get(lib) else (3 if lib == "scottplot" and m10 and all(m10) else 1)
    if lib == "scottplot" and not m10:
        미.append("화려함(M10)")
    if all("R3_ok" in res[lib][p] for p in req) and req:
        s["한글"] = (f["한글_기본"] if f["한글_기본"] >= 3 else 3) if all(c_ok) else 1
    else:
        s["한글"] = None
    미 = [k for k in MEASURED_KEYS if s[k] is None] + 미
    gates = (a_ok, b_ok, c_ok)
    return s, 미, gates


def 합계(s, fill):
    return sum((fill if s[k] is None else s[k]) * WEIGHTS[k] / 5 for k in WEIGHTS)


def 관문문자(gates):
    def g(xs):
        return "미측정" if not xs else ("통과" if all(xs) else "탈락")
    return "·".join(g(x) for x in gates)


def 점수표(res, excl):
    print("\n## D-50 가중 점수 (draft_chart_verdict 10-1 규칙)\n")
    print("| lib | 관문 A·B·C | 성능 | 메모리 | 실시간 | 화려함 | 한글 | 초안 합(미측정=3) | 최종 판정 |")
    print("|---|---|---|---|---|---|---|---|---|")
    rows = {}
    for lib in LIBS:
        s, 미, gates = 점수(res, lib, excl)
        탈락 = any(x and not all(x) for x in gates)
        보류 = [k for k in 미 if k in MEASURED_KEYS]
        if any(res[lib][p].get("hz_missing") for p in PLATS):  # Android 주사율 미기록 회차가 있으면 확정하지 않는다(lead 19:5x)
            보류.append("Android 주사율 미기록")
        lo, hi = 합계(s, 1), 합계(s, 5)
        rows[lib] = {"s": s, "미": 미, "탈락": 탈락, "보류": 보류, "lo": lo, "hi": hi, "mid": 합계(s, 3)}
        관문 = 관문문자(gates)
        제외 = [p for p in PLATS if (p, lib) in excl]
        if 제외:
            관문 += f" (제외: {','.join(제외)})"
        판 = ("탈락(관문)" + (" — 한글: 글꼴 지정 API 없음, 성능·메모리 관문도 실패(웹 1차: 10만점 raw 983ms·R7 59.9B/pt) — 포크 시 재검증 가능" if lib == "procharts" else "")) if 탈락 else (f"판정 보류({', '.join(보류)})" if 보류 else f"확정 {lo:.0f}")
        c = lambda k: "미측정" if s[k] is None else str(s[k])
        print(f"| {lib} | {관문} | {c('성능')} | {c('메모리')} | {c('실시간')} | {s['화려함']} | {c('한글')} | {rows[lib]['mid']:.0f} | {판} |")
    print("| Avalonia Charts(유료) | 사용자 체험판 승인 대기 | — | — | — | — | — | — | 판정 보류(체험판 승인 대기) |")
    print("\n- 초안 합은 미측정 측정 항목을 중립 3점으로 넣은 임시값이다(초안 전용). **최종 판정에는 쓰지 않는다**(lead 19:2x).")
    print("- 관문이 점수보다 우선한다. 관문이 하나라도 '탈락'이면 순위에서 뺀다.")
    민감도(rows)
    return rows


def 민감도(rows):
    """판정 보류 후보의 미측정 칸을 1점·5점으로 바꿔도 1·2위가 바뀌지 않는지 본다."""
    print("\n## 순위·민감도 (미측정 칸 1점 / 5점)\n")
    후보 = [lib for lib in LIBS if not rows[lib]["탈락"]]
    if not 후보:
        print("관문을 통과한 후보가 없다(또는 전부 탈락).")
        return
    print("| lib | 최저(미측정=1) | 최고(미측정=5) | 상태 |")
    print("|---|---|---|---|")
    for lib in 후보:
        r = rows[lib]
        print(f"| {lib} | {r['lo']:.0f} | {r['hi']:.0f} | {'보류' if r['보류'] else '확정'} |")
    # 보류 후보는 최저·최고 두 극단, 확정 후보는 고정값으로 순위를 매겨 1·2위 집합이 같은지 본다
    def 상위2(pick):
        return [lib for lib, _ in sorted(((l, pick(rows[l])) for l in 후보), key=lambda x: -x[1])[:2]]
    극단 = []
    보류목록 = [l for l in 후보 if rows[l]["보류"]]
    for mask in range(1 << len(보류목록)):
        hi_set = {보류목록[i] for i in range(len(보류목록)) if mask >> i & 1}
        극단.append(tuple(상위2(lambda r, hs=hi_set: r["hi"] if any(r is rows[h] for h in hs) else r["lo"])))
    같음 = len(set(극단)) == 1
    print(f"\n- 1·2위(극단 {len(극단)}가지 조합): {'불변 — ' + ' > '.join(극단[0]) if 같음 else '바뀜 — ' + ' / '.join(' > '.join(x) for x in sorted(set(극단)))}")
    hz보류 = [l for l in 보류목록 if "Android 주사율 미기록" in rows[l]["보류"]]
    if hz보류:
        print(f"- 주사율 미기록 후보({', '.join(hz보류)})는 민감도 결과와 무관하게 보류한다. 점수가 아니라 측정 유효성 문제이므로 재측정해야 한다.")
    if 보류목록:
        print("- 판정 보류 후보(주사율 미기록 후보 제외)는 " + ("1·2위가 바뀌지 않으므로 위 범위를 붙여 순위에 넣을 수 있다(lead 19:2x 조건)." if 같음 else "1·2위를 바꿀 수 있으므로 순위에 넣지 않는다. 미측정 칸을 먼저 채운다."))
