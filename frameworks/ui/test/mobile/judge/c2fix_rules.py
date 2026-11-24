# Android 재측정 판정 규칙(판정 계층) — retest_android_c2fix.py 결과(c2fix_measure.json·캡처)를 D21·D22·U15 수치 기준으로 판정해 rc 를 낸다
"""
사용: python3 -B c2fix_rules.py <결과 폴더(c2fix_measure.json 위치)> [--loc <Localization 폴더>]
rc: 0 = 전부 PASS, 1 = FAIL 있음, 3 = 판정 불가(측정 없음·조작 경로 없음). 결과는 <결과 폴더>/c2fix_judge.json 에 쓴다.

기준(측정 전 고정, 2026-11-01 otest-m-1 · team-lead 지시)
D21 설정 줄 라벨·컨트롤 짝 — 펼침·접힘 × ko·en 모든 경우, 짝 6개(언어·문서 기본 글자 크기·테마·폰트·Ctrl+휠 동작·창 스타일) 각각:
  ① 라벨·컨트롤 둘 다 보임  ② |라벨 세로 중심 − 컨트롤 세로 중심| ≤ 컨트롤 높이/2 (같은 행)
  ③ 라벨 오른쪽 끝 ≤ 컨트롤 왼쪽 + 1 DIP 이고 둘 사이 간격 ≤ 24 DIP (라벨이 바로 앞)  ④ 라벨이 설정 패널 오른쪽 경계를 넘지 않음(잘림 없음)
D22 패널 본문 빈 곳 터치 — 패널마다(빈 곳이 있는 패널만) 머리줄 띠(패널 위 24 DIP) 화소로:
  ① 빈 곳 터치 뒤 머리줄 바탕 채널합이 터치 전보다 30 이상 변함  ② 그때 머리줄 글자(바탕과 가장 먼 화소) 채널합 ≥ 600(흰색)
  ③ 문서를 다시 누르면 바탕이 터치 전과 채널합 차 ≤ 6 으로 돌아옴  ④ 본문 안 버튼 터치는 기존대로 문서 수 +1
  유효성: 빈 곳 터치 직전·직후 키보드 표시가 true 면 그 패널은 판정 불가(UNDET)다. 지난 측정에서 키보드 위를 누른 무효 측정이 있었다(lead 05:0x).
U15 SDI 탭 줄 — ① 문서 1개 SDI 창 안 DocumentTabStrip 보이지 않음(또는 높이 0)
  ② 합류 뒤 SDI 창 안 DocumentTabStrip 보임·높이 > 0·탭 항목 2개 이상. 합류 조작이 안 됐으면(창 안 문서 1개) 판정 불가(rc 3, 경로 보고)
"""
import argparse
import json
import os
import sys
from PIL import Image

# (라벨 키, 컨트롤 id, 설정 창 범주) — 새 설정 창은 범주별 페이지라 짝마다 자기 범주 노드만 본다
PAIRS = [("lang.label", "shell.cmbLanguage", "Language"), ("settings.docFont", "shell.numDocFont", "Appearance"),
         ("settings.theme", "shell.cmbTheme", "Appearance"), ("settings.font", "shell.cmbFont", "Appearance"),
         ("settings.wheelMode", "shell.cmbWheelMode", "Appearance"), ("settings.windowStyle", "shell.cmbWindowStyle", "Window")]
# 이 체크아웃의 로컬라이제이션(메인 체크아웃 경로 하드코딩 금지)
LOC = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "..", "src", "xkxk.frameworks.ui.shell", "Localization"))


def labels(loc, lang):
    d = {}
    for f in sorted(os.listdir(loc)):
        if f.endswith(".%s.json" % lang):
            d.update(json.load(open(os.path.join(loc, f), encoding="utf-8")))
    return d


def cy(b):
    return b["y"] + b["h"] / 2


def j_d21(M, loc):
    rows, ok_all, any_data = [], True, False
    for st in ("open", "fold"):
        part = M.get("d21_" + st) or {}
        for lang in ("ko", "en"):
            data = part.get(lang) or {}
            nodes = data.get("nodes") or []
            if not nodes:
                rows.append({"state": st, "lang": lang, "verdict": "UNDET", "why": "설정 패널 측정 없음"})
                continue
            any_data = True
            tx = labels(loc, lang)
            for key, cid, cat in PAIRS:
                cn = [n for n in nodes if n.get("category", cat) == cat]  # 새 설정 창: 짝의 범주 페이지 노드만(옛 측정은 category 없음 → 전체)
                rights = [(n["bounds"]["x"] + n["bounds"]["w"]) for n in cn if n.get("bounds") and (n.get("id") or "").startswith("shell.")]
                pane_right = max(rights) if rights else 0
                lab = [n for n in cn if n.get("type") == "TextBlock" and n.get("text") == tx.get(key) and n.get("bounds")]
                ctl = [n for n in cn if n.get("id") == cid and n.get("bounds")]
                if not lab or not ctl:
                    rows.append({"state": st, "lang": lang, "pair": key, "verdict": "FAIL", "why": "라벨 또는 컨트롤 없음", "label": tx.get(key)})
                    ok_all = False
                    continue
                c = ctl[0]["bounds"]
                lb = min(lab, key=lambda n: abs(cy(n["bounds"]) - cy(c)))["bounds"]
                dy = abs(cy(lb) - cy(c))
                gap = c["x"] - (lb["x"] + lb["w"])
                vis = lab[0].get("effectivelyVisible") is not False and ctl[0].get("effectivelyVisible") is not False
                ok = vis and dy <= c["h"] / 2 and -1 <= gap <= 24 and lb["x"] + lb["w"] <= pane_right + 0.5
                ok_all = ok_all and ok
                rows.append({"state": st, "lang": lang, "pair": key, "verdict": "PASS" if ok else "FAIL",
                             "dy": round(dy, 1), "half_h": round(c["h"] / 2, 1), "gap": round(gap, 1), "label": lb, "control": c})
    v = "UNDET" if not any_data else ("PASS" if ok_all and all(r["verdict"] != "UNDET" for r in rows) else "FAIL")
    return v, rows


REFY = [None]  # 머리줄 기준 y(LeftPane bounds.y) — j_d22 가 설정


def header_stat(path, b, k, pane=None):
    im = Image.open(path).convert("RGB")
    # 알림 패널 bounds 가 Dock 머리줄 아래 안쪽부터면(LeftPane y 보다 12 DIP 넘게 크면) 그 차이만큼 위로 머리줄을 본다(u 판본 24 DIP). w 판본은 bounds.y=148 로 LeftPane 과 같아 보정 0
    oy = -(b["y"] - REFY[0]) if (pane == "NotificationPane" and REFY[0] is not None and b["y"] - REFY[0] > 12) else 0
    x0, y0, x1, y1 = round(b["x"] * k), round((b["y"] + oy) * k), round((b["x"] + b["w"]) * k), round((b["y"] + oy + 24) * k)
    px = [im.getpixel((x, y)) for y in range(y0, y1, 2) for x in range(x0, x1, 2)]
    bg = max(set(px), key=px.count)
    fg = max(px, key=lambda p: sum(abs(p[i] - bg[i]) for i in range(3)))
    return bg, fg


def j_d22(M):
    d = M.get("d22") or {}
    k = (M.get("scale_open") or {}).get("k") or 2.25
    rows, measured, ok_all = [], 0, True
    REFY[0] = ((d.get("LeftPane") or {}).get("bounds") or {}).get("y")
    for pid in ("LeftPane", "BottomPane", "NotificationPane"):
        rec = d.get(pid) or {}
        if rec.get("skipped"):
            rows.append({"pane": pid, "verdict": "UNDET", "why": rec["skipped"], "hide": rec.get("hide_before")})
            continue
        if not rec.get("after"):
            rows.append({"pane": pid, "verdict": "N/A", "why": "빈 곳 없음 또는 패널 없음", "blank": rec.get("blank")})
            continue
        # 측정 유효성(lead 05:0x): 터치 전 키보드 닫힘, 빈 곳 터치가 키보드를 띄우지 않음. 캡처 무변화는 FAIL 근거로만 쓰고 유효성 실패로 보지 않는다
        if rec.get("kb_before_tap") == "true" or rec.get("kb_after_tap") == "true":
            rows.append({"pane": pid, "verdict": "UNDET", "why": "키보드가 떠 있어 측정 무효", "kb": [rec.get("kb_before_tap"), rec.get("kb_after_tap")]})
            continue
        measured += 1
        b = rec["bounds"]
        bg0, _ = header_stat(rec["before"], b, k, pid)
        bg1, fg1 = header_stat(rec["after"], b, k, pid)
        bg2, _ = header_stat(rec["after_doc"], b, k, pid)
        c1, c2, c3 = abs(sum(bg1) - sum(bg0)) >= 30, sum(fg1) >= 600, abs(sum(bg2) - sum(bg0)) <= 6
        ok = c1 and c2 and c3
        ok_all = ok_all and ok
        rows.append({"pane": pid, "verdict": "PASS" if ok else "FAIL", "bg_before": bg0, "bg_after": bg1, "fg_after": fg1, "bg_after_doc": bg2,
                     "capture_changed": rec.get("changed"), "kb": [rec.get("kb_before_tap"), rec.get("kb_after_tap"), rec.get("kb_after_doc")],
                     "checks": {"①바탕변화≥30": c1, "②글자흰색≥600": c2, "③문서터치원복≤6": c3}})
    for sw in d.get("switch") or []:  # ③ 패널 A→B 이동 시 활성 머리줄 하나뿐
        if not sw.get("after_b"):
            rows.append({"switch": [sw.get("a"), sw.get("b")], "verdict": "N/A", "why": "빈 곳 없음"})
            continue
        if "true" in (sw.get("kb_before"), sw.get("kb_a"), sw.get("kb_b")):
            rows.append({"switch": [sw["a"], sw["b"]], "verdict": "UNDET", "why": "키보드가 떠 있어 측정 무효"})
            continue
        measured += 1
        ba, bb_ = sw["a_bounds"], sw["b_bounds"]
        def act(shotp, b0, ref, pn):  # 머리줄 바탕이 비활성 기준(ref)에서 30 이상 변했는가
            return abs(sum(header_stat(shotp, b0, k, pn)[0]) - sum(ref)) >= 30
        r0a, r0b = header_stat(sw["start"], ba, k, sw["a"])[0], header_stat(sw["start"], bb_, k, sw["b"])[0]
        st = {"A활성(A터치)": act(sw["after_a"], ba, r0a, sw["a"]), "B비활성(A터치)": not act(sw["after_a"], bb_, r0b, sw["b"]),
              "B활성(B터치)": act(sw["after_b"], bb_, r0b, sw["b"]), "A비활성(B터치)": not act(sw["after_b"], ba, r0a, sw["a"])}
        ok = all(st.values())
        ok_all = ok_all and ok
        rows.append({"switch": [sw["a"], sw["b"]], "verdict": "PASS" if ok else "FAIL", "checks": st})
    row_ok = True
    for rt in d.get("row_tap") or []:  # 목록 행 터치: 문서 집합 불변 + 행 선택 표시(트리 selected 변화 또는 행 영역 화소 변화)
        if not rt.get("after"):
            rows.append({"row_tap": rt.get("pane"), "verdict": "N/A", "why": "목록 행 없음(rows=%s)" % rt.get("rows")})
            continue
        if "true" in (rt.get("kb_before"), rt.get("kb_after")):
            rows.append({"row_tap": rt["pane"], "verdict": "UNDET", "why": "키보드가 떠 있어 측정 무효"})
            continue
        b = rt["row"]["bounds"]
        im0, im1 = Image.open(rt["before"]).convert("RGB"), Image.open(rt["after"]).convert("RGB")
        box = (round(b["x"] * k), round(b["y"] * k), round((b["x"] + b["w"]) * k), round((b["y"] + b["h"]) * k))
        diff = sum(abs(p - q) for a_, b_ in zip(im0.crop(box).resize((8, 4)).getdata(), im1.crop(box).resize((8, 4)).getdata()) for p, q in zip(a_, b_))
        ra = rt.get("row_after") or {}
        sel = bool(ra.get("selected") or ra.get("isSelected")) or diff >= 48
        same_docs = sorted(rt.get("docs_before") or []) == sorted(rt.get("docs_after") or [])
        ok = sel and same_docs
        row_ok = row_ok and ok
        rows.append({"row_tap": rt["pane"], "verdict": "PASS" if ok else "FAIL", "선택표시": sel, "행영역화소차": diff, "문서불변": same_docs,
                     "row_after": ra, "docs": [rt.get("docs_before"), rt.get("docs_after")]})
    ok_all = ok_all and row_ok
    ct = d.get("control_tap") or {}
    c4 = ct.get("docs_after") == (ct.get("docs_before") or 0) + 1
    rows.append({"control_tap": ct, "verdict": "PASS" if c4 else "FAIL"})
    v = "UNDET" if measured == 0 else ("PASS" if ok_all and c4 else "FAIL")
    return v, rows


def lum(c):
    f = [(v / 255.0) / 12.92 if v / 255.0 <= 0.03928 else (((v / 255.0) + 0.055) / 1.055) ** 2.4 for v in c]
    return 0.2126 * f[0] + 0.7152 * f[1] + 0.0722 * f[2]


def contrast(a, b):
    la, lb = sorted((lum(a), lum(b)), reverse=True)
    return (la + 0.05) / (lb + 0.05)


def j_u19(M):
    """U19(D-C2-24): 머리줄 터치 후 바탕 변화≥30·글자 흰색계(채널합≥600)·문서 터치 원복≤6. 다크는 활성 바탕이 #3366CC(±12/채널)이고 글자 대비≥4.5."""
    u = M.get("u19") or {}
    k = (M.get("scale_open") or {}).get("k") or 2.25
    rows, ok_all, n = [], True, 0
    for tid in ("pd-light", "pd-dark"):
        rec = u.get(tid) or {}
        if not rec.get("after"):
            rows.append({"theme": tid, "verdict": "UNDET", "why": "측정 없음"})
            continue
        if rec.get("kb_before") == "true" or rec.get("kb_after") == "true":
            rows.append({"theme": tid, "verdict": "UNDET", "why": "키보드가 떠 있어 측정 무효", "kb": [rec.get("kb_before"), rec.get("kb_after")]})
            continue
        n += 1
        b = rec["left"]
        bg0, _ = header_stat(rec["before"], b, k)
        bg1, fg1 = header_stat(rec["after"], b, k)
        bg2, _ = header_stat(rec["after_doc"], b, k)
        chk = {"①바탕변화≥30": abs(sum(bg1) - sum(bg0)) >= 30, "②글자밝음≥600": sum(fg1) >= 600, "③원복≤6": abs(sum(bg2) - sum(bg0)) <= 6}
        cr = round(contrast(fg1, bg1), 2)
        row = {"theme": tid, "bg_before": bg0, "bg_after": bg1, "fg_after": fg1, "bg_after_doc": bg2, "contrast": cr}
        if tid == "pd-dark":
            chk["④바탕≈#3366CC"] = all(abs(bg1[i] - v) <= 12 for i, v in enumerate((0x33, 0x66, 0xCC)))
            chk["⑤대비≥4.5"] = cr >= 4.5
        ok = all(chk.values())
        ok_all = ok_all and ok
        row.update({"verdict": "PASS" if ok else "FAIL", "checks": chk})
        rows.append(row)
    return ("UNDET" if n == 0 else ("PASS" if ok_all and n == 2 else ("FAIL" if not ok_all else "UNDET"))), rows


def strip_in(strips, wb, typ):
    return [s for s in strips if s["type"] == typ and s.get("bounds") and
            wb["x"] - 1 <= s["bounds"]["x"] <= wb["x"] + wb["w"] and wb["y"] - 1 <= s["bounds"]["y"] <= wb["y"] + wb["h"]]


def j_u15(M):
    u = M.get("u15") or {}
    win = u.get("win1") or []
    if not win or not win[0].get("bounds"):
        return "UNDET", [{"why": "SDI 창 없음"}]
    wb = win[0]["bounds"]
    one = strip_in((u.get("one") or {}).get("strips") or [], wb, "DocumentTabStrip")
    c1 = all((not s.get("vis")) or s["bounds"]["h"] == 0 for s in one)
    rows = [{"step": "문서 1개", "strips": one, "①숨김": c1}]
    att = [a for a in u.get("attempts") or [] if a.get("strips") is not None]
    if not att:
        return ("FAIL" if not c1 else "UNDET"), rows + [{"why": "합류 조작 미실행", "attempts": u.get("attempts")}]
    a = att[-1]
    wins = [w for w in a.get("windows") or [] if not w.get("main")]
    ndocs = len((wins[0].get("docs") if wins else []) or [])
    st = strip_in(a["strips"], wb, "DocumentTabStrip")
    items = strip_in(a["strips"], wb, "DocumentTabStripItem")
    c2 = any(s.get("vis") and s["bounds"]["h"] > 0 for s in st) and len([i for i in items if i.get("vis")]) >= 2
    rows.append({"step": "합류 뒤", "sdi_docs": ndocs, "managed_windows": len(wins),
                 "windows_docs": [(w.get("main"), w.get("docs")) for w in a.get("windows") or []], "sdi_docs_ge2(관찰)": ndocs >= 2, "strips": st, "items": len(items), "②표시": c2})
    if ndocs < 2 and not c2:
        return ("FAIL" if not c1 else "UNDET"), rows + [{"why": "합류 조작이 SDI 창에 문서를 넣지 못함 — Android 합류 경로 확인 필요"}]
    return ("PASS" if c1 and c2 else "FAIL"), rows


def main(argv):
    p = argparse.ArgumentParser()
    p.add_argument("out")
    p.add_argument("--loc", default=LOC)
    a = p.parse_args(argv)
    M = json.load(open(os.path.join(a.out, "c2fix_measure.json"), encoding="utf-8"))
    res = {}
    for k, fn in (("D21", lambda: j_d21(M, a.loc)), ("D22", lambda: j_d22(M)), ("U15", lambda: j_u15(M)), ("U19", lambda: j_u19(M))):
        v, rows = fn()
        res[k] = {"verdict": v, "rows": rows}
        print("%-4s %s" % (k, v))
    res["fatal_lines"] = M.get("fatal_lines")
    res["apk_md5"] = M.get("apk_md5")
    json.dump(res, open(os.path.join(a.out, "c2fix_judge.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=1, default=str)
    vs = [r["verdict"] for r in res.values() if isinstance(r, dict) and "verdict" in r]
    return 1 if "FAIL" in vs or res["fatal_lines"] else (3 if "UNDET" in vs else 0)


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
