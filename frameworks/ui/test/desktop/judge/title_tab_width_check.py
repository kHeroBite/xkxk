#!/usr/bin/env python3
# E2 제목 탭 폭 판정 — TitleTabWidth.Compute 규칙의 대조값과 실제 앱의 탭 폭(shell.tab.*)을 표본(탭 1·3·5·10·30개 × 가용폭 3종)으로 비교한다
"""
사용:
  title_tab_width_check.py                  정적 모드(앱 불필요): 규칙 대조값 자기 점검 + 소스(상수·말줄임·툴팁·패널) 정적 확인. rc 0/1
  title_tab_width_check.py --live [--out 결과.json] [--widths 700,1100,1600]
                                            진단 빌드에 원격 연결해 실제 폭을 잰다. 연결은 pdctl.py 와 같다. rc 0/1/2(판정불가)

규칙(정본 plan/09_화면설계/01_화면설계_공통원칙.md §2-1, 구현 Pd.Ui/Controls/TitleTabWidth.cs):
  폭 = clamp(자연 폭, 72, 240). 합계가 (가용 폭 - 간격 2×(n-1)) 를 넘으면 모든 탭에 같은 상한 max(72, 가용/n) 을 씌운다.
  72 에서도 넘치면 가로 스크롤(마지막 탭이 줄 오른쪽 밖). 자연 폭은 같은 제목의 탭을 홀로 열어 넓은 줄에서 잰 값이다.
측정 이름: tab_natural_le_240 · tab_shrink_common_cap · tab_min_72_then_scroll · ellipsis_tooltip (판정기 E2 필수 이름)
"""
import argparse
import json
import os
import sys
import time

ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", ".."))
WIDTH_CS = os.path.join(ROOT, "src/Pd.Ui/Controls/TitleTabWidth.cs")
PANEL_CS = os.path.join(ROOT, "src/Pd.Ui/Controls/TitleTabPanel.cs")
VIEW = os.path.join(ROOT, "src/Pd.Ui.Views/MainView.axaml")
MIN, MAX, GAP, TOL = 72.0, 240.0, 2.0, 2.5
COUNTS = (1, 3, 5, 10, 30)
TITLES = ("가", "열린 문서 제목 스무 자 정도", "아주 긴 문서 제목 " * 8)  # 짧음·중간·240 초과(말줄임) — 번갈아 쓴다


def compute(natural, available):
    """TitleTabWidth.Compute 와 같은 규칙의 대조값 → (폭 목록, overflow)."""
    w = [MIN if n != n else min(max(n, MIN), MAX) for n in natural]
    if not w or sum(w) <= available:
        return w, False
    room = max(0.0, available)
    cap = max(MIN, room / len(w))
    return [min(x, cap) for x in w], MIN * len(w) > room


def static_checks():
    """앱 없이 보는 것: 대조값 자기 점검과 소스 상수·말줄임·툴팁·패널 연결."""
    out = []
    nat = [300, 100, 10, 240]
    w, ov = compute(nat, 10000)
    out.append(("ref_clamp", w == [240, 100, 72, 240] and not ov))
    w, ov = compute(nat, 400)  # 합 652 > 400 → 상한 100, 짧은 것은 유지
    out.append(("ref_common_cap", w == [100, 100, 72, 100] and not ov))
    w, ov = compute([200] * 30, 800)  # 72×30 > 800 → 모두 72 + overflow
    out.append(("ref_min_overflow", all(x == 72 for x in w) and ov))
    src = _read(WIDTH_CS)
    out.append(("src_defaults_72_240", "DefaultMin = 72" in src and "DefaultMax = 240" in src))
    out.append(("src_panel_gap_and_compute", "Spacing * Math.Max(0, count - 1)" in _read(PANEL_CS) and "TitleTabWidth.Compute" in _read(PANEL_CS)))
    view = _read(VIEW)
    out.append(("view_uses_panel", "TitleTabPanel" in view))
    out.append(("view_ellipsis", 'TextTrimming="CharacterEllipsis"' in view))
    out.append(("view_tooltip_title", "ToolTip.Tip" in view and "Title" in view))
    return [{"name": n, "pass": bool(p)} for n, p in out]


def _read(path):
    try:
        with open(path, encoding="utf-8") as f:
            return f.read()
    except OSError:
        return ""


def bounds_of(r, id_):
    res = r.result("ui.find", id=id_)
    b = res.get("bounds") or {}
    return b.get("x", 0.0), b.get("w", 0.0), bool(res.get("visible", True))


def tab_widths(r, ids):
    """탭 항목 폭 = 제목 버튼 폭 + 닫기 버튼 폭(같은 Grid 의 두 열). 줄 시작·폭도 함께 돌려준다."""
    rows = []
    for i in ids:
        x, w, _ = bounds_of(r, "shell.tab.%s" % i)
        _, cw, _ = bounds_of(r, "shell.tabClose.%s" % i)
        rows.append((x, w + cw))
    sx, sw, _ = bounds_of(r, "shell.tabStrip")
    return rows, sx, sw


def open_n(r, n):
    r.result("docs.close", all="true")
    for k in range(n):
        r.result("docs.open", kind="form", title=TITLES[k % 3] + (" %d" % k if k % 3 else ""), allowDuplicate="true")
    return [d["id"] for d in _docs(r)][-n:]


def set_width(r, w):
    r.result("window.resize", w=int(w))
    time.sleep(0.6)  # 자동 전환 디바운스(200ms) + 배치


def natural_of(r, wide):
    """제목 3종의 자연 폭: 넓은 줄에서 1개씩 홀로 열어 잰다."""
    set_width(r, wide)
    nat = []
    for k in range(3):
        r.result("docs.close", all="true")
        r.result("docs.open", kind="form", title=TITLES[k] + (" %d" % k if k else ""), allowDuplicate="true")
        ids = [d["id"] for d in _docs(r)]
        rows, _, _ = tab_widths(r, ids[-1:])
        nat.append(rows[0][1])
    return nat


def _docs(r):
    lst = r.result("docs.list")
    return [d for d in (lst if isinstance(lst, list) else lst.get("docs", [])) if not d.get("floating")]


def live(widths, out):
    sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "common"))
    import _paths
    _paths.add()
    from ui20_lib import Remote, Undecidable
    r = Remote()
    names = json.dumps(r.result("help"), ensure_ascii=False)
    if "docs.open" not in names:
        raise Undecidable("진단 빌드 아님(docs.open 없음)")
    wide = max(widths) + 400
    nat = natural_of(r, wide)  # 제목 3종의 자연 폭(실측)
    meas = {k: {"name": k, "pass": True, "samples": []} for k in
            ("tab_natural_le_240", "tab_shrink_common_cap", "tab_min_72_then_scroll", "ellipsis_tooltip")}
    for w in widths:
        for n in COUNTS:
            set_width(r, w)
            ids = open_n(r, n)
            time.sleep(0.4)
            rows, sx, sw = tab_widths(r, ids)
            got = [x[1] for x in rows]
            exp, ov = compute([nat[k % 3] for k in range(n)], sw - GAP * max(0, n - 1))
            ok = all(abs(a - b) <= TOL for a, b in zip(got, exp)) and len(got) == len(exp)
            last_right = rows[-1][0] + rows[-1][1] if rows else 0
            scrolled = last_right > sx + sw + 0.5
            s = {"n": n, "strip_w": sw, "got": [round(g, 1) for g in got], "exp": [round(e, 1) for e in exp], "overflow_exp": ov, "last_right": round(last_right, 1)}
            fits = sum(compute([nat[k % 3] for k in range(n)], 1e9)[0]) + GAP * max(0, n - 1) <= sw
            if fits:  # 안 줄어드는 표본: 자연 폭 유지, 모두 72~240
                m = meas["tab_natural_le_240"]
                m["pass"] &= ok and all(MIN - TOL <= g <= MAX + TOL for g in got)
                m["samples"].append(s)
            elif not ov:  # 줄어드는 표본: 같은 상한
                m = meas["tab_shrink_common_cap"]
                m["pass"] &= ok and all(g >= MIN - TOL for g in got) and max(got) - max(e for e in exp) <= TOL
                m["samples"].append(s)
            else:  # 72 에서도 넘침: 모두 72, 마지막 탭이 줄 밖(가로 스크롤)
                m = meas["tab_min_72_then_scroll"]
                m["pass"] &= ok and all(abs(g - MIN) <= TOL for g in got) and scrolled
                m["samples"].append(s)
    # 말줄임·툴팁: 240 초과 제목이 240 으로 잘렸는지(실측) + 정적 확인(TextTrimming·ToolTip)
    long_cut = abs(nat[2] - MAX) <= TOL
    st = {x["name"]: x["pass"] for x in static_checks()}
    meas["ellipsis_tooltip"]["pass"] = bool(long_cut and st.get("view_ellipsis") and st.get("view_tooltip_title"))
    meas["ellipsis_tooltip"]["samples"].append({"long_natural_w": round(nat[2], 1), "static": st})
    for name in ("tab_natural_le_240", "tab_shrink_common_cap", "tab_min_72_then_scroll"):
        if not meas[name]["samples"]:  # 표본이 한 번도 해당 구간에 걸리지 않으면 통과로 치지 않는다
            meas[name]["pass"] = False
            meas[name]["note"] = "해당 구간 표본 없음 — --widths 를 조정해 다시 잰다"
    r.result("docs.close", all="true")
    report = {"item": "E2", "time": time.strftime("%Y-%m-%d %H:%M:%S"), "natural": [round(x, 1) for x in nat],
              "measurements": list(meas.values())}
    rc = 0 if all(m["pass"] for m in meas.values()) else 1
    report["verdict"] = "PASS" if rc == 0 else "FAIL"
    if out:
        with open(out, "w", encoding="utf-8") as f:
            json.dump(report, f, ensure_ascii=False, indent=1)
    print(json.dumps(report, ensure_ascii=False)[:3000])
    return rc


def main(argv):
    ap = argparse.ArgumentParser(description="E2 제목 탭 폭 판정")
    ap.add_argument("--live", action="store_true")
    ap.add_argument("--out")
    ap.add_argument("--widths", default="700,1100,1600")
    a = ap.parse_args(argv)
    if not a.live:
        res = static_checks()
        for x in res:
            print(("PASS " if x["pass"] else "FAIL ") + x["name"])
        return 0 if all(x["pass"] for x in res) else 1
    try:
        return live([int(x) for x in a.widths.split(",") if x], a.out)
    except Exception as e:  # 연결 실패·진단 빌드 아님은 판정불가(rc=2), 단정 실패는 FAIL
        if isinstance(e, AssertionError):
            print("FAIL: %s" % e)
            return 1
        print("UNDECIDABLE: %s" % e)
        return 2


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
