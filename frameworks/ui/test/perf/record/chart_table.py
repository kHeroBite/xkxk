# 차트 측정(D-50) 결과 JSON 을 읽어 라이브러리 4종 × 항목 × 플랫폼 표와 가중 점수표를 만드는 집계기
"""사용: python3 -I chart_table.py [--dir odevge/chart] [--tag i1853gp] [--json 요약.json]
입력(chart_procedure.md 2절): chart_<plat>_<tag>_<lib>_<끝부분>_r<n>.json (끝부분 static_<raw|lttb>_<10k|100k|1m>·stream_<base|anim>·static_raw_100k_proto),
_korean_r1.json(R3 {"tofu":0,"layout_broken":false}), _excluded.txt(제외 사유), chart_m11.json(M11 {"<lib>": "메모"}).
값이 없으면 '미측정'(PASS 로 채우지 않음). 중앙값과 3회 범위를 함께 낸다."""
import argparse
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))  # -I 로 실행해도 같은 폴더의 모듈을 읽는다
from chart_score import PRIOR_R3, 점수표  # noqa: E402  가중치·판정 보류·민감도(chart_score.py)
from chart_table_lib import *  # noqa: E402,F401,F403  읽기·요약·판정 도우미(chart_table_lib.py)
from chart_table_lib import 규칙_기록, 주사율_미기록, 주사율_제외  # noqa: E402  측정 중 쌓이는 공유 집합(같은 객체)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--dir", default=os.path.join(os.path.dirname(os.path.abspath(__file__)), "chart"))
    ap.add_argument("--tag", default="i1853gp")
    ap.add_argument("--json", default="")
    a = ap.parse_args()
    data, excl, m11 = 읽기(a.dir, a.tag)
    print(f"# 차트 D-50 집계 — 폴더 {a.dir}, 판 {a.tag}, 파일 묶음 {len(data)}개, 제외 {len(excl)}건")
    res = {lib: {p: {} for p in PLATS} for lib in LIBS}

    # R1·(A): 10만 raw·lttb, 그리고 1만·100만 참고
    for ds in ("raw", "lttb"):
        rows = {lib: {} for lib in LIBS}
        for lib in LIBS:
            for p in PLATS:
                med, t = 요약(값들(data, p, lib, f"static_{ds}_100k", a값))
                res[lib][p][f"A_{ds}"] = med
                rows[lib][p] = 칸(excl, p, lib, t + 판정(med, med is not None and med <= 500) + 오염표지(p, lib, f"static_{ds}_100k"))
        표(f"R1 (A) 정적 10만 점 {ds} — max(first_show, redraw p95) ms ≤ 500", rows)
    for n in ("10k", "1m"):
        to = lambda p, lib: sum(len(v) for (pp, ll, s), v in data.items() if pp == p and ll == lib and s.startswith(f"static_raw_{n}_r") and s.endswith("_timeout"))
        rows = {lib: {p: 칸(excl, p, lib, (lambda t: f"timeout {to(p, lib)}회(생략)" if t == "미측정" and to(p, lib) else t)(요약(값들(data, p, lib, f"static_raw_{n}", a값))[1]) + 오염표지(p, lib, f"static_raw_{n}")) for p in PLATS} for lib in LIBS}
        표(f"R1 참고 — 정적 {n} raw (A 값 ms)", rows)

    # R8: raw ÷ lttb
    rows = {lib: {} for lib in LIBS}
    for lib in LIBS:
        for p in PLATS:
            r, l = res[lib][p].get("A_raw"), res[lib][p].get("A_lttb")
            rows[lib][p] = 칸(excl, p, lib, "미측정" if r is None or not l else f"{r / l:.2f}배 (raw {r:.0f} / lttb {l:.0f})" + 오염표지(p, lib, "static_raw_100k") + 오염표지(p, lib, "static_lttb_100k"))
    표("R8 다운샘플 효과 — 10만 점 A 값 raw ÷ lttb", rows)

    # R2·(B)
    rows = {lib: {} for lib in LIBS}
    for lib in LIBS:
        for p in PLATS:
            # 웹은 조용한 호스트 재측정(stream_base_q, lead 20:1x)만 관문에 쓴다. 옛 stream_base 는 참고(호스트 부하 교란)
            q = p == "web" and bool(data.get((p, lib, "stream_base_q")))
            suf = "stream_base_q" if q else "stream_base"
            fps = 값들(data, p, lib, suf, lambda j: 수(찾기(j, "stream"), "fps"))
            rat = 값들(data, p, lib, suf, lambda j: (lambda s: None if not s or not 수(s, "target_points_per_s") or 수(s, "achieved_points_per_s") is None else s["achieved_points_per_s"] / s["target_points_per_s"])(찾기(j, "stream")))
            leak = 값들(data, p, lib, suf, lambda j: 수(찾기(j, "stream"), "memory_delta", "managed_mb"))
            fm, ft = 요약(fps)
            rm, _ = 요약(rat, "{:.3f}")
            lm, lt = 요약(leak)
            res[lib][p].update({"B_ref": fm, "B_ratio": rm, "leak_mb": lm})
            if p == "web" and not q and fm is not None:
                rows[lib][p] = 칸(excl, p, lib, f"재측정 대기(호스트 부하 교란) — 참고 {ft}")
                continue
            res[lib][p]["B_fps"] = fm
            ok = fm is not None and rm is not None and fm >= 30 and rm >= 0.99
            rows[lib][p] = 칸(excl, p, lib, "미측정" if fm is None else f"{ft} · 반영 {rm:.3f}{판정(fm, ok)} · 60초 증가 {lt}MB{' [조용한 호스트 재측정]' if q else ''}")
    표("R2 (B) 스트리밍 base(애니 끔) — fps ≥ 30 · 반영률 ≥ 99%", rows)

    # 용도별 관문(lead 22:41, draft 11-6) — 위 표들은 원문 그대로 두고 아래에 따로 낸다
    비율 = lambda j: (lambda s: None if not s or not 수(s, "target_points_per_s") or 수(s, "achieved_points_per_s") is None else s["achieved_points_per_s"] / s["target_points_per_s"])(찾기(j, "stream"))
    rows = {lib: {} for lib in LIBS}
    for lib in LIBS:
        for p in PLATS:
            suf = "stream_u1a_q" if p == "web" and data.get((p, lib, "stream_u1a_q")) else "stream_u1a"
            suf = 고정접미(data, p, lib, suf)
            fm, ft = 요약(값들2(data, p, lib, suf, lambda j: 수(찾기(j, "stream"), "fps"), min_n=1))
            rm, _ = 요약(값들2(data, p, lib, suf, 비율, min_n=1), "{:.3f}")
            p99, _ = 요약(값들2(data, p, lib, suf, lambda j: 수(찾기(j, "stream"), "p99_ms"), min_n=1))
            mx, _ = 요약(값들2(data, p, lib, suf, lambda j: 수(찾기(j, "stream"), "max_ms"), min_n=1))
            ok = fm is not None and rm is not None and fm >= 30 and rm >= 0.99
            res[lib][p]["U1a_fps"] = fm
            rows[lib][p] = 칸(excl, p, lib, "미측정" if fm is None else f"{ft} · 반영 {rm:.3f} · p99 {p99} · max {mx}ms{판정(fm, ok)}{' [_q]' if suf.endswith('_q') else ''}{오염표지(p, lib, suf)}")
    표("U1-a 실사용 실시간 — 창 1000·초당 10점·60초·애니 끔, fps ≥ 30 · 반영률 ≥ 99% (lead 22:41 관문. 위 R2 는 U1-b 여유 — 원문 유지)", rows)

    rows = {lib: {} for lib in LIBS}
    for lib in LIBS:
        for p in PLATS:
            med, t = 요약(값들2(data, p, lib, 고정접미(data, p, lib, "static_raw_1k"), a값))
            res[lib][p]["U3_1k"] = med
            rows[lib][p] = 칸(excl, p, lib, t + 판정(med, med is not None and med <= 200) + 오염표지(p, lib, "static_raw_1k"))
    표("U3 ① 소량 정적 1천 점 raw — max(first_show, redraw p95) ms ≤ 200 (lead 22:41 관문, 3플랫폼 모두)", rows)

    # Android 주사율 재측정(_rh, lead 22:41 규칙) — 원 회차 표는 위에 그대로 둔다
    rh = [(s, n) for s, n in (("static_raw_10k_rh", "10k raw (A ms)"), ("static_raw_100k_rh", "10만 raw (A ms)"), ("stream_base_rh", "R2 base fps"), ("stream_base_rh_fx", "고정 120Hz R2 base fps"),
                               ("static_raw_10k_rh_fx", "고정 120Hz 10k raw (A ms)"), ("static_raw_100k_rh_fx", "고정 120Hz 10만 raw (A ms, ≤500)"), ("static_raw_1m_fx", "고정 120Hz 100만 raw (A ms)"))]
    if any(k[2] in [s for s, _ in rh] for k in data):
        for s, n in rh:
            rows = {lib: {} for lib in LIBS}
            for lib in LIBS:
                for p in PLATS:
                    fn = (lambda j: 수(찾기(j, "stream"), "fps")) if s.startswith("stream") else a값
                    rows[lib][p] = 요약(값들2(data, p, lib, s, fn))[1]
            표(f"주사율 재측정(_rh·_fx) — {n}", rows)
        rows = {lib: {p: 요약(값들2(data, p, lib, "static_raw_1m_fx", lambda j: 수(찾기(j, "static"), "memory_delta", "managed_bytes_per_point")))[1] for p in PLATS} for lib in LIBS}
        표("고정 120Hz R7 — 100만 raw 점당 바이트(≤16B)", rows)

    # 기존 회차를 22:41 대체 규칙(측정 중 표본이 전부 ≥60Hz 면 유효)으로 다시 읽은 값 — 위 원 표는 그대로 둔다
    for s, n in (("static_raw_10k", "10k raw (A ms)"), ("static_raw_100k", "10만 raw (A ms, ≤500)"), ("stream_base", "R2 base fps (≥30)")):
        rows = {lib: {} for lib in LIBS}
        for lib in LIBS:
            for p in PLATS:
                fn = (lambda j: 수(찾기(j, "stream"), "fps")) if s.startswith("stream") else a값
                rows[lib][p] = 칸(excl, p, lib, 요약(값들2(data, p, lib, s, fn))[1]) if p == "android" else "—"
        표(f"대체 규칙 재해석(Android 기존 회차) — {n}", rows)

    # R7: 100만 raw 우선, 없으면 10만 raw
    rows = {lib: {} for lib in LIBS}
    for lib in LIBS:
        for p in PLATS:
            src, vs = "1m", 값들(data, p, lib, "static_raw_1m", lambda j: 수(찾기(j, "static"), "memory_delta", "managed_bytes_per_point"))
            if not vs:
                src, vs = "100k", 값들(data, p, lib, "static_raw_100k", lambda j: 수(찾기(j, "static"), "memory_delta", "managed_bytes_per_point"))
            med, t = 요약(vs)
            res[lib][p]["R7_bpp"] = med
            rows[lib][p] = 칸(excl, p, lib, (t if med is None else f"{t} [{src}]{판정(med, med <= 16)}") + 오염표지(p, lib, "static_raw_" + src))
    표("R7 점당 바이트 — 라이브러리 추가분(원본 배열 8B/점 제외), 관리 힙, ≤ 16B", rows)

    # M10: anim ÷ base
    rows = {lib: {} for lib in LIBS}
    for lib in LIBS:
        for p in PLATS:
            fa, _ = 요약(값들(data, p, lib, "stream_anim", lambda j: 수(찾기(j, "stream"), "fps")))
            ka, _ = 요약(값들(data, p, lib, "stream_anim", lambda j: 수(찾기(j, "stream"), "alloc", "allocated_kb_per_frame")))
            kb, _ = 요약(값들(data, p, lib, "stream_base", lambda j: 수(찾기(j, "stream"), "alloc", "allocated_kb_per_frame")))
            fb, _ = 요약(값들(data, p, lib, "stream_base", lambda j: 수(찾기(j, "stream"), "fps")))  # M10 은 같은 세션의 base 대비(관문 아님, 상대 비용)
            if fa is None or not fb:
                rows[lib][p] = 칸(excl, p, lib, "해당 없음(내장 애니 없음)" if lib in ("oxyplot", "procharts") else "미측정")
                continue
            drop = 1 - fa / fb
            ar = None if ka is None or not kb else ka / kb
            ok = drop <= 0.15 and (ar is None or ar <= 2)
            res[lib][p]["M10_ok"] = ok
            rows[lib][p] = 칸(excl, p, lib, f"fps {fb:.1f}→{fa:.1f} (감소 {drop * 100:.0f}%) · 할당 {'미측정' if ar is None else f'{ar:.2f}배'}{판정(fa, ok)}")
    표("M10 애니메이션 비용 — fps 감소 ≤ 15% · 프레임당 할당 ≤ 2배", rows, "scottplot=anim proto, livecharts=animate true")

    # R3·M11
    rows = {lib: {} for lib in LIBS}
    for lib in LIBS:
        for p in PLATS:
            js = [j for j in data.get((p, lib, "korean"), []) if isinstance(j.get("tofu"), (int, float))]  # tofu=null(캡처 불가)은 미측정
            if (p, lib) in excl:
                rows[lib][p] = 칸(excl, p, lib, "")
                continue
            if not js and (lib, p) in PRIOR_R3:
                res[lib][p]["R3_ok"] = PRIOR_R3[(lib, p)]
                rows[lib][p] = f"기존 판정 {'통과' if PRIOR_R3[(lib, p)] else '탈락(두부)'} [8-4·2-2]"
                continue
            if not js:
                rows[lib][p] = 칸(excl, p, lib, "미측정")
                continue
            tofu = sum(int(j.get("tofu", 0)) for j in js)
            broken = any(bool(j.get("layout_broken")) for j in js)
            res[lib][p]["R3_ok"] = tofu == 0 and not broken
            rows[lib][p] = 칸(excl, p, lib, f"두부 {tofu} · 붕괴 {'있음' if broken else '없음'}{판정(1, tofu == 0 and not broken)}")
    표("R3 (C) 한글 — 두부·배치 붕괴 0", rows)
    print("\n## M11 사용자 지정 작업량(정성)\n")
    for lib in LIBS:
        print(f"- {lib}: {(m11 or {}).get(lib, '미측정')}")

    문제 = 기대값_검사(data)
    print("\n## 기대값 검사(chart_procedure 3절)\n")
    print("없음" if not 문제 else "\n".join("- " + s for s in 문제))
    print(f"\n## 주사율(refresh_hz, {HZ_MIN}Hz 규칙)\n")
    print("- 60Hz 미만 제외: " + ("없음" if not 주사율_제외 else "; ".join(f"{p}/{l}/{s} {n}회" for p, l, s, n in sorted(주사율_제외))))
    print("- Android 주사율 미기록: " + ("없음" if not 주사율_미기록 else "; ".join(f"{p}/{l}/{s}" for p, l, s in sorted(주사율_미기록))))
    print("- 용도별 표(U1-a·U3·_rh) 적용 규칙(lead 22:41·22:5x, 고정/대체=측정 중 표본만 ≥60Hz/병목아님=fps<주사율 50%/제외): " + ("없음" if not 규칙_기록 else "; ".join(f"{p}/{l}/{s} 고정{a}·대체{b}·병목아님{c}·제외{d}" for p, l, s, a, b, c, d in sorted(set(규칙_기록)))))
    # 대체 규칙 회차(시작 전만 60Hz 미만, 측정 중 표본은 ≥60Hz)는 측정 중 표본 최솟값과 비교한다(lead 22:41)
    기준hz = lambda j: 표본_주사율(j) if 주사율(j) and 주사율(j) < HZ_MIN and (표본_주사율(j) or 0) >= HZ_MIN else 주사율(j)
    상한 = sorted({(p, l, s) for (p, l, s), js in data.items() for j in js
                 if 기준hz(j) and 수(찾기(j, "stream") or {}, "fps") is not None and 수(찾기(j, "stream"), "fps") >= 0.95 * 기준hz(j)})
    print("- 주사율 상한 도달(fps ≥ 주사율의 95%, 값은 하한으로 읽음): " + ("없음" if not 상한 else "; ".join("/".join(x) for x in 상한)))
    for p, l, _ in 주사율_미기록:
        res[l][p]["hz_missing"] = True
    solo_표()
    점수표(res, excl)
    if a.json:
        with open(a.json, "w", encoding="utf-8") as f:
            json.dump({"tag": a.tag, "results": res, "excluded": {f"{k[0]}:{k[1]}": v for k, v in excl.items()}, "issues": 문제}, f, ensure_ascii=False, indent=1)
    return 0


if __name__ == "__main__":
    sys.exit(main())
