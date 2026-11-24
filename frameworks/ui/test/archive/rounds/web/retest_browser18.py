# 웹 벤치(b1126 등, PdBench 진단판) 기능 확인 — bench.env·그리드 1만/10만 행 로드·정렬·필터·스크롤·차트 3종 렌더·xlsx 를 원격으로 돌리고 예외·콘솔 오류·캡처를 남긴다(성능 값은 참고)
# 사용: PD_URL=http://127.0.0.1:5199/ python3 tools/retest_browser18.py [--tag b1126]   (헤드리스 전용)
"""
절차: poc/ui/logs/odevgc/component_study.md 8-4 기능 부분(lead 11:4x 배정).
판정(기능만):
  E  bench.env: 차트 3종 포함·한글 글꼴(has_hangul)·ko-KR collation(ga_lt_na·linguistic_a_lt_B) 값
  G  그리드: 1만·10만 행 load·정렬(ko-KR, head 가나다)·필터(김/개발/"")·스크롤이 예외 없이 결과를 돌려줌 + 캡처
  C  차트 3종(scottplot·procharts·livecharts): static 1만 점·짧은 stream 이 예외 없이 결과를 돌려줌 + 캡처(한글 제목·축·범례 □ 여부는 캡처를 눈으로 확인)
     콘솔·pageerror 에 MissingMethod 등 실행 오류가 없는지
  X  xlsx: scenario=xlsx xlsxRows=10000 → bytes>0(빌드에 PocXlsx 가 없으면 그 사실을 기록)
"""
import json
import os
import sys
import time

from playwright.sync_api import sync_playwright

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import retest_browser2 as t  # noqa: E402

OUT = t.OUT
TAG = sys.argv[sys.argv.index("--tag") + 1] if "--tag" in sys.argv else "bench"
res = []


def rec(item, expect, actual, verdict):
    res.append({"item": item, "expect": expect, "actual": actual, "verdict": verdict})
    print("%-6s %s | %s" % (verdict, item, str(actual)[:500]), flush=True)


def call(r, cmd, **kw):
    """원격 호출 + 오류 문구까지 돌려준다."""
    v = r.call(cmd, _timeout=240, **kw)
    err = None if v is not None else (r.calls[-1].get("error") if r.calls else "?")
    return v, err


def pick(d, keys):
    """보고용 요약 + 원문(키 이름이 예상과 다를 수 있어 응답 원문을 함께 남긴다 — b1126 첫 실행 교훈)."""
    if not isinstance(d, dict):
        return d
    out = {k: d.get(k) for k in keys if k in d}
    out["_raw"] = json.dumps(d, ensure_ascii=False)[:1200]
    return out


LIBS = ["scottplot", "procharts", "livecharts"]


def find_key(d, key):
    """응답 안 어디든 있는 key 의 첫 값(구조가 판마다 달라 재귀로 찾는다)."""
    stack = [d]
    while stack:
        x = stack.pop()
        if isinstance(x, dict):
            if key in x and not isinstance(x[key], (dict, list)):
                return x[key]
            stack.extend(x.values())
        elif isinstance(x, list):
            stack.extend(x)
    return None


def maximize_active(r):
    d = next((x["id"] for x in (r.call("docs.list") or []) if x.get("active")), None)
    if d:
        r.call("docs.state", doc=d, state="maximized")
        time.sleep(0.8)
    return d


def header_widths(r):
    """① 고정 폭 붕괴: 벤치 그리드(bench.grid.main) 아래 DataGridColumnHeader 의 폭 목록(보이는 것). 22px 이하는 붕괴로 본다(MinWidth 20)."""
    from retest_browser11 import _walk, full_tree
    tr = full_tree(r)
    ws = []
    for n, p in _walk(tr.get("tree"), []):
        if n.get("type") == "DataGridColumnHeader" and n.get("visible") and any(x.get("id") == "bench.grid.main" for x in p):
            b = n.get("bounds") or {}
            ws.append({"text": n.get("text"), "w": b.get("w"), "x": b.get("x")})
    return {"truncated": tr.get("truncated"), "count": len(ws), "narrow": [w for w in ws if (w.get("w") or 0) <= 22], "headers": ws}


def combo_state(r):
    """④ 차트 콤보(bench.chart.lib) 선택 — selectedIndex 와 표시 글자. 벤치 문서마다 콤보가 있으므로 활성(차트) 문서로 한정한다(bp1146w 첫 실행 교훈)."""
    d = next((x["id"] for x in (r.call("docs.list") or []) if x.get("active")), None)
    c = r.call("ui.find", id="bench.chart.lib", doc=d) or {}
    return {"doc": d, "selectedIndex": c.get("selectedIndex"), "text": c.get("text")}


def main():
    with sync_playwright() as p:
        b = p.chromium.launch(headless=True, args=t.ARGS)
        ctx, page, r = t.boot(b, "?layout-key=otestw2-bench")
        errs = []
        page.on("console", lambda m: errs.append(("console." + m.type, m.text[:400])) if m.type in ("error", "warning") or "Exception" in m.text else None)
        page.on("pageerror", lambda e: errs.append(("pageerror", str(e)[:400])))
        shot = lambda name: page.screenshot(path=os.path.join(OUT, "bench_%s_%s.png" % (TAG, name)))  # noqa: E731

        env, e = call(r, "bench.env")
        rec("E bench.env", "차트 3종·한글 글꼴·ko-KR collation", env if env is not None else {"error": e}, "기록" if env is not None else "FAIL")

        o, e = call(r, "bench.open", target="grid")
        time.sleep(1.5)
        rec("G0 bench.open target=grid", "예외 없음", o if o is not None else {"error": e}, "PASS" if o is not None else "FAIL")
        maximize_active(r)  # 캡처 판독용(b1126 교훈: 작은 창에서는 열·축 글자 판독 불가)
        impl = None
        for rows in (10000, 100000):
            v, e = call(r, "bench.grid", scenario="load", rows=rows)
            impl = (v or {}).get("impl") or impl
            time.sleep(1.0)
            shot("grid_load_%d" % rows)
            rec("G1 load rows=%d" % rows, "결과 반환(예외 없음), 시간은 참고", pick(v, ("rows", "first_show_ms", "seed_ms", "memory_loaded", "error")) if v is not None else {"error": e}, "PASS" if v is not None else "FAIL")
        hw = header_widths(r)
        rec("G5 20열 고정 폭 붕괴 0", "보이는 열 머리 중 폭 22px 이하 0개(MinWidth 20 으로 눌린 열 없음)", {"impl": impl, "count": hw["count"], "narrow": hw["narrow"], "widths": [(h["text"], h["w"]) for h in hw["headers"]]},
            "PASS" if hw["count"] > 0 and not hw["narrow"] else ("FAIL" if hw["count"] else "판정불가(열 머리 못 찾음)"))
        # G8 통화 기호(₩, U+20A9): 예산 열(12번째)을 화면에 끌어와 캡처(odev-gc: Noto 부분집합에 U+20A9 글리프 없음 — 제품 영향 건)
        sc = None
        for gid in ("bench.grid.main", "BenchGrid"):
            sc = r.call("ui.scroll", id=gid, col=12)
            if sc is not None:
                break
        time.sleep(0.8)
        shot("grid_won")
        rec("G8 예산 열 ₩ 표시(캡처로 판정)", "예산 칸 앞 글자가 ₩ 로 보임(☒ 면 글리프 없음)", {"scroll": sc, "error": None if sc is not None else (r.calls[-1].get("error") if r.calls else None)}, "기록")
        if impl and "Pro" in impl:
            v, e = call(r, "bench.grid", scenario="bands")
            time.sleep(1.0)
            shot("grid_bands")
            rec("G6 (Pro) 병합 헤더 bands", "결과 반환 + 캡처에 병합 머리줄(눈으로 확인)", pick(v, ("bands", "error")) if v is not None else {"error": e}, "PASS" if v is not None else "FAIL")
        v, e = call(r, "bench.grid", scenario="sort", property="Name", culture="ko-KR")
        shot("grid_sort")
        head = (v or {}).get("head")
        rec("G2 정렬 Name ko-KR", "결과 반환, head 가 가나다 순(첫 이름 '강…')", pick(v, ("sort_ms", "head", "error")) if v is not None else {"error": e},
            "PASS" if v is not None and head else ("FAIL" if v is None else "기록"))
        for txt in ("김", "개발", ""):
            v, e = call(r, "bench.grid", scenario="filter", text=txt)
            time.sleep(0.5)
            shot("grid_filter_%s" % ({"김": "kim", "개발": "dev", "": "clear"}[txt]))
            rec("G3 필터 text=%r" % txt, "결과 반환(visible_rows)", pick(v, ("filter_ms", "visible_rows", "error")) if v is not None else {"error": e}, "PASS" if v is not None else "FAIL")
        # G7 영문 정렬(odev-gc 11:5x): 영문 이름 행만 남긴 뒤 ko-KR 언어 정렬(a<B) 과 culture 빈값 서수 정렬(대문자 먼저)의 head 대비 — WASM 에 ko-KR ICU 데이터가 있는지 확인
        fl, e1 = call(r, "bench.grid", scenario="filter", latin=True)
        sk, e2 = call(r, "bench.grid", scenario="sort", property="Name", culture="ko-KR")
        so, e3 = call(r, "bench.grid", scenario="sort", property="Name", culture="ordinal")  # 비우면 현재 문화권(서수 아님) — 서수는 culture=ordinal(GridBenchModel 주석, otest-1 bi1201 관찰)
        shot("grid_latin_sort")
        hk, ho = ((sk or {}).get("sort") or {}).get("head"), ((so or {}).get("sort") or {}).get("head")
        rec("G7 영문 정렬 ko-KR(언어) 대 서수", "ko-KR head 는 대소문자 섞인 알파벳 순(a<B), 서수 head 는 대문자 먼저 — 두 head 가 달라야 함",
            {"filter_latin": pick(fl, ("filter",)) if fl is not None else {"error": e1}, "koKR_culture_echo": ((sk or {}).get("sort") or {}).get("culture"), "koKR_head": hk,
             "ordinal_culture_echo": ((so or {}).get("sort") or {}).get("culture"), "ordinal_head": ho, "errors": [x for x in (e2, e3) if x]},
            "PASS" if hk and ho and hk != ho else ("기록(같음 — 서수 경로 확인 필요)" if hk and ho else "FAIL"))
        call(r, "bench.grid", scenario="filter", text="")
        v, e = call(r, "bench.grid", scenario="scroll", seconds=3, step=3)
        rec("G4 스크롤 3초", "결과 반환(fps·p95 는 참고)", pick(v, ("fps", "p95_ms", "frames", "error", "TimedOut")) if v is not None else {"error": e}, "PASS" if v is not None and not (v or {}).get("TimedOut") else "FAIL")
        v, e = call(r, "bench.grid", scenario="xlsx", xlsxRows=10000)
        rec("X xlsx 10000행", "bytes>0(빌드에 PocXlsx 없으면 사실 기록)", pick(v, ("total_ms", "bytes", "error", "available", "reason")) if v is not None else {"error": e},
            "PASS" if (v or {}).get("bytes", 0) > 0 else ("기록(미포함·미지원)" if v is not None or e else "FAIL"))

        o, e = call(r, "bench.open", target="chart")
        time.sleep(1.5)
        rec("C0 bench.open target=chart", "예외 없음", o if o is not None else {"error": e}, "PASS" if o is not None else "FAIL")
        maximize_active(r)
        for lib in LIBS:
            n0 = len(errs)
            v, e = call(r, "bench.chart", scenario="static", lib=lib, points=10000, redrawSeconds=1)
            time.sleep(1.5)
            shot("chart_%s_static" % lib)
            cb = combo_state(r)
            rec("C3 %s 콤보 반영" % lib, "콤보 선택 = %s(selectedIndex %d)" % (lib, LIBS.index(lib)), cb,
                "PASS" if cb.get("selectedIndex") == LIBS.index(lib) or (cb.get("text") or "") == lib else "FAIL")
            # 글꼴(odev-gc 12:1x 절차 수정): 기본값 = ScottPlot nanum, LiveCharts·ProCharts noto. ScottPlot 만 font=noto 로 1회 더 캡처(깨짐 대조)
            info = {"default": find_key(v, "font")}
            if lib == "scottplot":
                fn, _ = call(r, "bench.chart", scenario="static", lib=lib, points=10000, redrawSeconds=1, font="noto")
                time.sleep(1.5)
                shot("chart_%s_fontnoto" % lib)
                info["font=noto"] = find_key(fn, "font")
                call(r, "bench.chart", scenario="static", lib=lib, points=10000, redrawSeconds=1)  # 기본 글꼴로 되돌림
                time.sleep(1.0)
            rec("C4 %s 글꼴 출처" % lib, "font 필드 기록 + 캡처(기본=chart_%s_static%s) — 한글 판정은 캡처를 눈으로" % (lib, ", noto=chart_scottplot_fontnoto" if lib == "scottplot" else ""), info, "기록")
            r.call("theme.select", id="pd-dark")  # ③ 바탕·글자색이 어두운 테마를 따라가는지(캡처로 확인)
            time.sleep(0.8)
            call(r, "bench.chart", scenario="static", lib=lib, points=10000, redrawSeconds=1)
            time.sleep(1.5)
            shot("chart_%s_dark" % lib)
            r.call("theme.select", id="pd-ocean")
            time.sleep(0.8)
            rec("C1 %s static 1만 점" % lib, "결과 반환(예외 없음), first_show 는 참고", pick(v, ("lib", "first_show_ms", "memory_shown", "redraw", "error", "TimedOut")) if v is not None else {"error": e},
                "PASS" if v is not None and not (v or {}).get("TimedOut") else "FAIL")
            v2, e2 = call(r, "bench.chart", scenario="stream", lib=lib, seconds=2, rate=1000, window=10000)
            time.sleep(0.5)
            shot("chart_%s_stream" % lib)
            new = errs[n0:]
            mm = [x for x in new if "MissingMethod" in x[1] or "Exception" in x[1]]
            rec("C2 %s stream 2초 + 실행 오류" % lib, "결과 반환, MissingMethod 등 예외 없음", {"stream": pick(v2, ("fps", "achieved_points_per_s", "error", "TimedOut")) if v2 is not None else {"error": e2}, "exceptions": mm, "console_new": new[:6]},
                "PASS" if v2 is not None and not mm else "FAIL")
        rec("콘솔·pageerror 전체", "기록", errs[:30], "기록")
        with open(os.path.join(OUT, "retest_web18_%s.json" % TAG), "w", encoding="utf-8") as f:
            json.dump(res, f, ensure_ascii=False, indent=2)
        b.close()


if __name__ == "__main__":
    main()
