# 차트 집계기의 읽기·요약·판정 도우미 모음 — chart_table.py 에서 가져다 쓴다(300줄 원칙 때문에 분리, 동작 불변)
import argparse
import json
import os
import re
import statistics
import sys

LIBS = ["scottplot", "oxyplot", "procharts", "livecharts"]
PLATS = ["desktop", "android", "web"]
NAME = re.compile(r"^chart_(desktop|android|web)_([^_]+)_(scottplot|oxyplot|procharts|livecharts)_(.+?)(?:_r(\d+))?\.json$")
EXCL = re.compile(r"^chart_(desktop|android|web)_([^_]+)_(scottplot|oxyplot|procharts|livecharts)_excluded\.(?:txt|json)$")



def 찾기(o, key):
    if isinstance(o, dict):
        if key in o and isinstance(o[key], dict):
            return o[key]
        for v in o.values():
            r = 찾기(v, key)
            if r is not None:
                return r
    return None


SOLO = re.compile(r"^(.+)_solo(\d*)$")  # 오염 의심 회차의 단독 재측정(team-lead 00:3x): 같은 회차는 solo<숫자>가 큰 쪽 > ... > solo2 > solo > 접미사 없음(solo=1)


def solo우선(n):
    """solo 접미사 숫자 → 우선순위(없음 0, solo 1, soloN N)."""
    return 1 if n == "" else int(n)
solo_적용 = []  # (plat, lib, 기준 접미사, ["r1=파일명", ...]) — 어떤 파일을 썼는지 표에 적는다


부하표, 오염_사용 = {}, {}  # 부하표: (lib, 기준 접미사, 회차, 우선순위) → (측정 전 load1, 후 load1) / 오염_사용: (plat, lib, 기준 접미사) → [쓴 파일이 오염 가능인 회차]
LOAD_MAX = 2.0  # 측정 전·후 load1 중 하나라도 이 값 이상이면 '오염 가능'(team-lead 00:3x 기준 통일)
KIND_SUF = {"E": "stream_u1a_q", "F": "static_raw_1k"}  # 웹 E·F 요약의 kind → 항목 접미사


def 부하읽기():
    """otest_w1/chart_web_ef_summary*.json(웹 E·F 요약, 파일명에 solo2·solo 가 있으면 그 우선순위)에서 회차별 load1 을 읽는다. 없으면 표지를 달지 않는다."""
    d = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "otest_w1")
    if not os.path.isdir(d):
        return
    for fn in sorted(os.listdir(d)):
        if not (fn.startswith(("chart_web_ef_summary", "chart_web_oxy_static_summary")) and fn.endswith(".json")):  # E·F 요약 + OxyPlot 정적 요약(ds·pts 필드)
            continue
        sm_ = re.search(r"solo(\d*)", fn)
        prio = solo우선(sm_.group(1)) if sm_ else 0
        try:
            with open(os.path.join(d, fn), encoding="utf-8") as f:
                rows = json.load(f)
        except (OSError, ValueError):
            continue
        for x in rows if isinstance(rows, list) else []:
            if not isinstance(x, dict) or "before" not in x or "after" not in x:
                continue
            if x.get("kind") in KIND_SUF:
                부하표[(x.get("lib"), KIND_SUF[x["kind"]], x.get("r"), prio)] = (x["before"], x["after"])
            elif "ds" in x and "pts" in x:  # OxyPlot 정적 요약: lib 필드가 없다(웹 OxyPlot 전용)
                부하표[(x.get("lib", "oxyplot"), f"static_{x['ds']}_{x['pts']}", x.get("r"), prio)] = (x["before"], x["after"])


def 오염표지(plat, lib, suffix):
    rs = 오염_사용.get((plat, lib, suffix))
    return f" ⚠오염가능 r{','.join(str(r) for r in sorted(rs))}" if rs else ""


def solo_표():
    print("\n## solo/solo2 적용 내역 (같은 회차는 solo2 > solo > 접미사 없음 순으로 쓴다)\n")
    if not solo_적용:
        print("없음")
        return
    print("| plat | lib | 항목 | 사용한 파일(회차=파일) | 오염 가능 회차(측정 전·후 load1 ≥ 2.0) |\n|---|---|---|---|---|")
    for (p, lib, suf), used in sorted(solo_적용):
        dirty = sorted(오염_사용.get((p, lib, suf), []))
        print(f"| {p} | {lib} | {suf} | " + "<br>".join(used) + f" | {('r' + ',r'.join(str(r) for r in dirty)) if dirty else '없음/미기록'} |")


def 읽기(folder, tag):
    data, excl = {}, {}
    picks = {}
    부하읽기()
    if not os.path.isdir(folder):
        return data, excl, None
    for fn in sorted(os.listdir(folder)):
        p = os.path.join(folder, fn)
        m = EXCL.match(fn)
        if m and (not tag or m.group(2) == tag):
            with open(p, encoding="utf-8") as f:
                excl[(m.group(1), m.group(3))] = f.read().strip()[:120] or "사유 미기재"
            continue
        m = NAME.match(fn)
        if not m or (tag and m.group(2) != tag):
            continue
        try:
            with open(p, encoding="utf-8") as f:
                j = json.load(f)
        except (OSError, ValueError) as e:
            print(f"[경고] 읽기 실패 {fn}: {e}", file=sys.stderr)
            continue
        sm = SOLO.match(m.group(4))
        base, prio = (sm.group(1), solo우선(sm.group(2))) if sm else (m.group(4), 0)
        picks.setdefault((m.group(1), m.group(3), base), {}).setdefault(int(m.group(5)) if m.group(5) else -1, []).append((prio, fn, j))
    for key, rmap in picks.items():
        used = []
        for rk in sorted(rmap):
            cands = rmap[rk] if rk == -1 else [max(rmap[rk], key=lambda t: t[0])]  # 회차 번호가 없는 파일은 전부 쓴다
            for prio, fn, j in cands:
                data.setdefault(key, []).append(j)
                if prio > 0:
                    used.append(f"r{rk}={fn}")
                b = 부하표.get((key[1], key[2], rk, prio)) if key[0] == "web" else None  # 부하 요약은 웹 E·F 것뿐이다
                if b and max(b) >= LOAD_MAX:
                    오염_사용.setdefault(key, []).append(rk)
        if used:
            solo_적용.append((key, used))
    m11 = None
    mp = os.path.join(folder, "chart_m11.json")
    if os.path.exists(mp):
        with open(mp, encoding="utf-8") as f:
            m11 = json.load(f)
    return data, excl, m11


def 수(o, *path):
    for k in path:
        if not isinstance(o, dict) or k not in o:
            return None
        o = o[k]
    return o if isinstance(o, (int, float)) and not isinstance(o, bool) else None


HZ_MIN = 60  # 주사율 규칙(lead 19:5x): 측정 구간 주사율(SurfaceFlinger activeMode)이 60Hz 미만인 회차는 판정에서 뺀다
주사율_제외, 주사율_미기록 = set(), set()


def 주사율(j):
    """결과 JSON 안의 refresh_hz(숫자, 또는 before/after/samples 를 가진 객체·목록) 중 가장 낮은 값. 기록이 없으면 None."""
    found = []

    def walk(o, hit=False):
        if isinstance(o, dict):
            for k, v in o.items():
                walk(v, hit or k == "refresh_hz")
        elif isinstance(o, list):
            for v in o:
                walk(v, hit)
        elif hit and isinstance(o, (int, float)) and not isinstance(o, bool):
            found.append(float(o))
    walk(j)
    return min(found) if found else None


def 값들(data, plat, lib, suffix, fn):
    js = data.get((plat, lib, suffix), [])
    ok = [j for j in js if 주사율(j) is None or 주사율(j) >= HZ_MIN]
    if len(ok) < len(js):
        주사율_제외.add((plat, lib, suffix, len(js) - len(ok)))
        if len(ok) < 2:  # 빼고 나서 2회 미만이면 미측정으로 둔다(재측정 대상)
            return []
    if plat == "android" and any(주사율(j) is None for j in ok):
        주사율_미기록.add((plat, lib, suffix))
    out = []
    for j in ok:
        v = fn(j)
        if v is not None:
            out.append(v)
    return out


def 표본_주사율(j):
    """refresh_hz.samples(측정 중 표본)의 최솟값. 표본이 없으면 None."""
    r = j.get("refresh_hz") if isinstance(j, dict) else None
    s = r.get("samples") if isinstance(r, dict) else None
    vs = [float(v) for v in s if isinstance(v, (int, float)) and not isinstance(v, bool)] if isinstance(s, list) else []
    return min(vs) if vs else None


규칙_기록 = []  # (plat, lib, suffix, 고정 n, 대체 n, 제외 n)


def 값들2(data, plat, lib, suffix, fn, min_n=2):
    """lead 22:41 주사율 규칙: ① 전 구간(before·samples·after) ≥60Hz = '고정' ② 아니면 측정 중 표본이 전부 ≥60Hz = '대체' ③ 그 밖은 제외.
    U1-a 처럼 1회 설계 항목은 min_n=1. 주사율 기록이 없는 회차(데스크톱·웹)는 그대로 쓴다."""
    js = data.get((plat, lib, suffix), [])
    고정, 대체, 병목아님, 제외, out = 0, 0, 0, 0, []
    for j in js:
        m, s = 주사율(j), 표본_주사율(j)
        if m is not None and m < HZ_MIN:
            f = 수(찾기(j, "stream") or {}, "fps")
            ref = s if s is not None else m
            if s is not None and s >= HZ_MIN:
                대체 += 1
            elif f is not None and ref and f < 0.5 * ref:
                병목아님 += 1  # lead 22:5x: fps 가 그 회차 주사율의 50% 미만이면 주사율은 병목이 아니다 → 유효(FAIL 쪽 판정에만 영향)
            else:
                제외 += 1
                continue
        elif m is not None:
            고정 += 1
        v = fn(j)
        if v is not None:
            out.append(v)
    if 고정 or 대체 or 병목아님 or 제외:
        규칙_기록.append((plat, lib, suffix, 고정, 대체, 병목아님, 제외))
    return out if len(out) >= min_n else []


def 고정접미(data, plat, lib, suffix):
    """Android 는 고정 120Hz 재측정(_fx) 파일이 있으면 그것을 정본으로 쓴다(otest-m-5 23:36 정리). 원래 이름은 대체 규칙 원본."""
    return suffix + "_fx" if plat == "android" and data.get((plat, lib, suffix + "_fx")) else suffix


def 요약(vs, fmt="{:.1f}"):
    if not vs:
        return None, "미측정"
    med = statistics.median(vs)
    rng = f"{fmt.format(min(vs))}~{fmt.format(max(vs))}" if len(vs) > 1 else "1회"
    return med, f"{fmt.format(med)} ({rng}, n={len(vs)})"


def a값(j):  # (A) max(first_show_ms, redraw.p95_ms)
    s = 찾기(j, "static")
    if s is None:
        return None
    fs, p95 = 수(s, "first_show_ms"), 수(s, "redraw", "p95_ms")
    return None if fs is None or p95 is None else max(fs, p95)


def 기대값_검사(data):
    문제 = []
    for (plat, lib, suf), js in data.items():
        for j in js:
            s = 찾기(j, "static") if suf.startswith("static") else 찾기(j, "stream")
            if s is None:
                continue
            ad = str(s.get("adapter", "")).lower().replace(".", "").replace(" ", "")
            if lib not in ad and not (lib == "livecharts" and "livecharts" in ad):
                문제.append(f"{plat}/{lib}/{suf}: adapter={s.get('adapter')}")
            if suf.startswith("static_"):
                if s.get("max_points") not in (0, None):
                    문제.append(f"{plat}/{lib}/{suf}: max_points={s.get('max_points')}(0 이어야 함)")
                if "_lttb_" in suf and s.get("drawn_points") not in (2000, None):
                    문제.append(f"{plat}/{lib}/{suf}: lttb drawn_points={s.get('drawn_points')}")
            if suf.startswith("stream") and s.get("prefilled_points") not in (None, s.get("window")):
                문제.append(f"{plat}/{lib}/{suf}: prefilled={s.get('prefilled_points')} window={s.get('window')}")
    return 문제


def 표(title, rows, note=""):
    print(f"\n## {title}\n")
    if note:
        print(note + "\n")
    print("| lib | " + " | ".join(PLATS) + " |")
    print("|---|" + "---|" * len(PLATS))
    for lib in LIBS:
        print(f"| {lib} | " + " | ".join(rows[lib][p] for p in PLATS) + " |")


def 칸(excl, plat, lib, text):
    return f"제외({excl[(plat, lib)]})" if (plat, lib) in excl else text


def 판정(med, ok):
    return "" if med is None else (" **PASS**" if ok else " **FAIL**")
