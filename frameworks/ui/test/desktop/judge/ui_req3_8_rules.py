#!/usr/bin/env python3
# 요건 3~7 시험 판정 규칙 — 측정 결과 JSON 과 사전 고정 기준을 대조해 AC 별 PASS/FAIL 을 낸다(측정·기록 없음). ui_req2_8_rules.py(요건 2) 의 짝이다
# 2026-11-04 otest-1: 결과를 열어 보기 전에 고정했다(측정 스크립트 소스와 AC 문구만 보고 작성). 측정 뒤 기준을 바꾸지 않는다
"""
사용: python3 -I ui_req3_8_rules.py panel <panel_all.json> <corner_split.json> <panel_persist_after.json>
      python3 -I ui_req3_8_rules.py redock <redock.json>
      python3 -I ui_req3_8_rules.py monitor <monitor.json>
      python3 -I ui_req3_8_rules.py settings <settings.json>
      python3 -I ui_req3_8_rules.py tour <tour.json>
출력: {"AC-x-y": {"verdict": "PASS|FAIL", "reasons": [...]}} 종료 코드 0=전부 PASS 1=FAIL 있음 2=입력 오류
고정 기준: DOC_W_TOL=1(AC-3-03 ±1dip) · REDOCK_TOL=2(AC-4-02 ±2px) · MAX_DRIFT=2(AC-4-06) · ALPHA_MS=1200(AC-5-03 지연 400+α 의 α 상한)
α 근거: 이전 관측 573~1046ms(부하 중, odev), 지연 측정은 원격 HTTP 폴링(호출 1회 100~500ms)을 포함하므로 1046 위 여유를 둔 1200 으로 정한다. 400ms 디바운스 자체는 상수로 따로 본다
"""
import json
import sys

DOC_W_TOL = 1.0
REDOCK_TOL = 2
MAX_DRIFT = 2
ALPHA_MS = 1200
DEBOUNCE_MS = 400
UNMAPPED_OK = {"TabletUiModeOverride", "UiStyleOverride"}  # OverrideSetting 으로 tabletMode·uiStyle 항목에 귀속(team-lead 지시)


def _res(reasons):
    return {"verdict": "FAIL" if reasons else "PASS", "reasons": reasons}


def _checks(raws, prefix=None, names=None, minimum=1):
    """script 가 낸 checks 중 prefix/이름 집합에 해당하는 항목의 실패 이유. 해당 항목이 minimum 개 미만이면 실패다."""
    got = [c for raw in raws if raw for c in raw.get("checks", [])]
    sel = [c for c in got if (prefix and c["name"].startswith(prefix)) or (names and c["name"] in names)]
    if len(sel) < minimum:
        return ["검사 항목 %d개 < %d: %s" % (len(sel), minimum, prefix or sorted(names))]
    return ["검사 거짓 %s: %s" % (c["name"], c) for c in sel if not c["pass"]]


def judge_panel(a, s, p):
    out = {}
    out["AC-3-01"] = _res(_checks([a], prefix="rules.", minimum=4))
    out["AC-3-02"] = _res(_checks([a], names={"default.only_bottom_ends"}))
    r3 = _checks([a], names={"expand.bottom_reaches_left", "expand.left_height_shrinks", "expand.left_button_appears"}, minimum=3)
    rt = a.get("roundtrip") or {}
    try:
        dw = abs(rt["after"]["Documents"]["w"] - rt["before"]["Documents"]["w"])
        if dw > DOC_W_TOL:
            r3.append("문서 폭 변화 %.2f > %s" % (dw, DOC_W_TOL))
    except (KeyError, TypeError):
        r3.append("문서 폭 전후 측정 없음")
    out["AC-3-03"] = _res(r3)
    out["AC-3-04"] = _res(_checks([a], prefix="roundtrip.", minimum=4))
    out["AC-3-05"] = _res(_checks([a], names={"rules.pinwheel_click_hidden"}))
    out["AC-3-06"] = _res(_checks([a], names={"persist.corner_owners_saved"}) + _checks([p], prefix="persist_after.", minimum=2))
    r7 = _checks([a], names={"default.glyphs", "default.tips", "default.focusable", "expand.glyph_down"}, minimum=4)
    if not (a.get("uia_activation") or {}).get("invoked"):
        r7.append("UIA 활성화(Tab 도달·Invoke) 측정 결과 uia_activation.invoked 없음")
    out["AC-3-07"] = _res(r7)
    out["AC-3-08"] = _res(_checks([a], prefix="edges.", minimum=4))
    out["REV-I3"] = _res(_checks([s], prefix="docsplit.", minimum=5) + _checks([s], prefix="wrap.", minimum=1) + _checks([s], prefix="edge.", minimum=2))
    return out


def _within(r, a, tol):
    return all(abs(r[k] - a[k]) <= tol for k in ("x", "y", "w", "h"))


def judge_redock(raw):
    out = {}
    cases = {c["name"]: c for c in (raw.get("selftest") or {}).get("cases", [])}
    rd = [c for n, c in cases.items() if n.startswith("redock_")]
    out["AC-4-01"] = _res(([] if len(rd) >= 8 else ["redock 표본 8건 미만: %d" % len(rd)]) + ["%s: %s" % (c["name"], c["detail"]) for c in rd if not c["pass"]])
    k = raw.get("keep_60") or {}
    r2 = []
    try:
        fr, sc, ar = k["frame"], k["screen"], k["area"]
        if not _within(sc, fr, REDOCK_TOL):
            r2.append("MDI 사각형 %s ≠ SDI 프레임 %s (±%d)" % (sc, fr, REDOCK_TOL))
        if sc["x"] + sc["w"] <= ar["x"] + ar["w"] + REDOCK_TOL:
            r2.append("영역 안으로 당겨졌음: %s area=%s" % (sc, ar))
    except (KeyError, TypeError):
        r2.append("60% 재결합 측정 없음")
    out["AC-4-02"] = _res(r2)
    c = raw.get("clamp_40") or {}
    r3 = []
    try:
        sc, ar = c["screen"], c["area"]
        if sc["x"] + sc["w"] > ar["x"] + ar["w"] + REDOCK_TOL or sc["x"] < ar["x"] - REDOCK_TOL:
            r3.append("40% 재결합이 영역 안으로 이동하지 않음: %s area=%s" % (sc, ar))
    except (KeyError, TypeError):
        r3.append("40% 재결합 측정 없음")
    out["AC-4-03"] = _res(r3)
    h = raw.get("hold") or {}
    r4 = []
    try:
        if not h["start"].get("placed"):
            r4.append("놓은 값 유지 표시가 시작부터 없음")
        for key in ("after_grow", "after_back"):
            if h[key]["bounds"] != h["start"]["bounds"]:
                r4.append("%s 에서 위치가 바뀜: %s → %s" % (key, h["start"]["bounds"], h[key]["bounds"]))
        if h["after_user_move"].get("placed"):
            r4.append("사용자 이동 뒤에도 놓은 값 유지가 해제되지 않음")
    except (KeyError, TypeError):
        r4.append("연속 관측 측정 없음")
    out["AC-4-04"] = _res(r4)
    rects = raw.get("round_trips") or []
    r6 = []
    if len(rects) < 4:
        r6.append("왕복 3회 측정 없음")
    else:
        drift = max(max(abs(r[k] - rects[0][k]) for k in ("x", "y", "w", "h")) for r in rects[1:])
        if drift > MAX_DRIFT:
            r6.append("왕복 드리프트 %s > %d px" % (drift, MAX_DRIFT))
    out["AC-4-06"] = _res(r6)
    return out


def _inside(win, work):
    return win["x"] >= work["x"] and win["y"] >= work["y"] and win["x"] + win["w"] <= work["x"] + work["w"] and win["y"] + win["h"] <= work["y"] + work["h"]


def _sdi(snap, doc):
    return next((s for s in snap.get("sdi", []) if s.get("doc") == doc), None)


def _locked(state):
    return any("lock" in str(k).lower() and v is True for k, v in (state or {}).items())


def judge_monitor(raw):
    out = {}
    cases = {c["name"]: c for c in (raw.get("selftest") or {}).get("cases", [])}
    rec = [c for n, c in cases.items() if not n.startswith(("redock_", "debounce_"))]
    out["AC-5-02"] = _res(([] if len(rec) >= 10 else ["복구 표본 10건 미만: %d" % len(rec)]) + ["%s: %s" % (c["name"], c["detail"]) for c in rec if not c["pass"]])
    deb = [c for n, c in cases.items() if n.startswith("debounce_")]
    out["AC-5-06"] = _res(([] if len(deb) >= 5 else ["디바운스 표본 5건 미만: %d" % len(deb)]) + ["%s: %s" % (c["name"], c["detail"]) for c in deb if not c["pass"]])
    m = raw.get("monitors") or {}
    r3 = []
    try:
        wk, b, a = m["primary_work"], m["before"], m["after"]
        if a["main"]["x"] == b["main"]["x"]:
            r3.append("메인 창이 이동하지 않음")
        if not _inside(a["main"], wk):
            r3.append("메인 창이 주 모니터 작업영역 밖: %s" % a["main"])
        if m.get("latency_ms") is None or m["latency_ms"] > DEBOUNCE_MS + ALPHA_MS:
            r3.append("이동 지연 %s ms > %d+%d" % (m.get("latency_ms"), DEBOUNCE_MS, ALPHA_MS))
        hid = _sdi(a, m["hidden"])
        if hid is None or not _inside(hid, wk):
            r3.append("안 보이던 SDI 가 작업영역 안 기본 위치로 오지 않음: %s" % hid)
        vb, va = _sdi(b, m["visible"]), _sdi(a, m["visible"])
        if vb is None or va is None or (vb["x"], vb["y"]) != (va["x"], va["y"]):
            r3.append("보이던 SDI 가 움직임: %s → %s" % (vb, va))
    except (KeyError, TypeError):
        r3.append("2→1 측정 없음")
    out["AC-5-03"] = _res(r3)
    d = raw.get("mdi") or {}
    r4 = []
    try:
        bf, af = {x["id"]: x for x in d["before"]["docs"]}, {x["id"]: x for x in d["after"]["docs"]}
        if af[d["placed"]]["bounds"] == bf[d["placed"]]["bounds"]:
            r4.append("안 보이는 MDI 창이 이동하지 않음")
        for key in ("keep", "minimized", "maximized"):
            if af[d[key]]["bounds"] != bf[d[key]]["bounds"]:
                r4.append("%s 창이 변함: %s → %s" % (key, bf[d[key]]["bounds"], af[d[key]]["bounds"]))
        if d.get("latency_ms") is None or d["latency_ms"] > DEBOUNCE_MS + ALPHA_MS:
            r4.append("MDI 이동 지연 %s ms" % d.get("latency_ms"))
    except (KeyError, TypeError):
        r4.append("MDI 측정 없음")
    out["AC-5-04"] = _res(r4)
    r5 = []
    try:
        lk = _sdi(m["after"], m["locked"])
        if lk is None or not _inside(lk, m["primary_work"]):
            r5.append("고정 SDI 가 보이는 곳으로 이동하지 않음: %s" % lk)
        if not _locked(m.get("locked_state")):
            r5.append("SDI 고정 상태가 풀림: %s" % m.get("locked_state"))
        bf, af = {x["id"]: x for x in d["before"]["docs"]}, {x["id"]: x for x in d["after"]["docs"]}
        if af[d["locked"]]["bounds"] == bf[d["locked"]]["bounds"]:
            r5.append("고정 MDI 창이 이동하지 않음")
        if not _locked(d.get("locked_state")):
            r5.append("MDI 고정 상태가 풀림: %s" % d.get("locked_state"))
    except (KeyError, TypeError):
        r5.append("고정 창 측정 없음")
    out["AC-5-05"] = _res(r5)
    g = raw.get("background_off") or {}
    r8 = []
    if not g:
        r8.append("비전경 측정 없음")
    else:
        if g.get("after", {}).get("bounds") != g.get("before", {}).get("bounds"):
            r8.append("복구가 꺼진 상태에서 창이 이동함")
        if "recovery off" not in json.dumps(g.get("log"), ensure_ascii=False):
            r8.append("로그에 'recovery off' 없음")
    out["AC-5-08"] = _res(r8)
    return out


def judge_settings(raw):
    out = {}
    o = raw.get("open") or {}
    r2 = []
    if "settings" not in (o.get("afterFirst") or []):
        r2.append("새 창으로 열리지 않음: %s" % o.get("afterFirst"))
    if (o.get("afterSecond") or []).count("settings") != 1:
        r2.append("재호출 시 창이 1개가 아님: %s" % o.get("afterSecond"))
    c = raw.get("close") or {}
    e = c.get("esc") or {}
    if "settings" in (c.get("afterEsc") or []) or e.get("docsBefore") != e.get("docsAfter"):
        r2.append("Esc 로 설정 창이 닫히지 않았거나 문서 수가 변함: %s" % c)
    out["AC-7-02"] = _res(r2)
    inv = raw.get("inventory") or {}
    r1 = []
    for k in ("missing", "wrongCategory", "notInInventory"):
        if inv.get(k) != []:
            r1.append("%s=%s" % (k, inv.get(k)))
    extra = set(inv.get("unmappedSettings") or []) - UNMAPPED_OK
    if extra:
        r1.append("UiSettings 공개 설정이 창에 매핑되지 않음: %s" % sorted(extra))
    if len((inv.get("listing") or {}).get("entries", [])) < 25:
        r1.append("인벤토리 항목 25 미만")
    out["AC-7-01"] = _res(r1)
    a = raw.get("apply") or {}
    r3 = []
    if str((a.get("afterDark") or {}).get("theme")) != "Dark":
        r3.append("다크 즉시 반영 안 됨: %s" % (a.get("afterDark") or {}).get("theme"))
    if "Dark" not in str(a.get("savedThemeDark")):
        r3.append("settings.json Theme 저장 안 됨: %s" % a.get("savedThemeDark"))
    if "afterFont" in a and (a["afterFont"].get("fontName") == (a.get("before") or {}).get("fontName") or a.get("savedFont") != a["afterFont"].get("fontName")):
        r3.append("글꼴 반영/저장 불일치: %s / %s" % (a["afterFont"].get("fontName"), a.get("savedFont")))
    lang0, lang1 = (a.get("before") or {}).get("language"), (a.get("afterLanguage") or {}).get("language")
    if lang1 in (None, lang0):
        r3.append("언어 즉시 반영 안 됨")
    if a.get("savedLanguage") is None or str(a.get("savedLanguage")).lower() not in (str(lang1).lower(), "en", "english"):
        r3.append("언어 저장 불일치: %s" % a.get("savedLanguage"))
    out["AC-7-03"] = _res(r3)
    rs = raw.get("reset") or {}
    r4 = []
    if rs.get("shadowOff") is not False:
        r4.append("그림자 끄기 선행 조건 실패")
    if rs.get("shadowAfterCategory") is not True:
        r4.append("창 범주 기본값 뒤 그림자가 복원되지 않음")
    if str((rs.get("themeAfterCategory") or {}).get("theme")) != "Dark":
        r4.append("범주 기본값이 다른 범주(모양)까지 되돌림")
    if str((rs.get("themeAfterAll") or {}).get("theme")) != "System":
        r4.append("전체 기본값 뒤 테마가 System 이 아님")
    out["AC-7-04"] = _res(r4)
    gp = raw.get("gpu") or {}
    r7 = []
    if gp.get("before") or not gp.get("pending") or gp.get("after"):
        r7.append("GPU 재시작 사유 표시 전/중/후가 (숨김/표시/숨김) 이 아님: %s" % {k: gp.get(k) for k in ("before", "pending", "after")})
    out["AC-7-07"] = _res(r7)
    se = raw.get("search") or {}
    r8 = []
    if not se.get("noneEmptyText") or (se.get("none") or {}).get("categories"):
        r8.append("없는 검색어에서 범주 0·안내 문구가 아님")
    all_rows = [r["id"] for r in (se.get("all") or {}).get("rows", []) if r.get("visible")]
    gpu_rows = [r["id"] for r in (se.get("gpu") or {}).get("rows", []) if r.get("visible")]
    if not any("gpu" in i.lower() for i in gpu_rows):
        r8.append("GPU 검색 결과에 gpu 행이 없음: %s" % gpu_rows)
    if (se.get("restored") or {}).get("categories") != (se.get("all") or {}).get("categories"):
        r8.append("검색을 지워도 범주가 복원되지 않음")
    if gpu_rows and len(gpu_rows) > len(all_rows):
        r8.append("검색이 항목을 걸러 내지 못함")
    out["AC-7-08"] = _res(r8)
    return out


def _sess(x):
    return x[0] if x else {}


def judge_tour(t):
    out = {}
    st, two = t.get("start") or {}, t.get("twoWindows") or {}
    r2 = []
    m = st.get("main") or {}
    if not m.get("overlay") or not m.get("card") or m.get("sysOpen") not in ([], None):
        r2.append("메인 투어가 오버레이로 시작하지 않거나 새 창이 생김: %s" % m)
    if _sess(m.get("state")).get("scope") != "MainWindow":
        r2.append("메인 세션 scope != MainWindow")
    if not (st.get("doc") or {}).get("overlay"):
        r2.append("MDI 문서 창 ? 에서 투어 시작 안 됨")
    ss = two.get("sessionsStart") or []
    if len(ss) < 2 or ss[0].get("window") == ss[1].get("window"):
        r2.append("2창 동시 세션 아님: %s" % ss)
    one = two.get("sessionsAfterOne") or []
    if len(one) >= 2 and len({s.get("index") for s in one}) < 2:
        r2.append("한 창을 진행해도 다른 창이 독립적이지 않음: %s" % one)
    out["AC-6-02"] = _res(r2)
    b = t.get("blocked") or {}
    r3 = []
    for cid, v in (b.get("clicks") or {}).items():
        txt = json.dumps(v.get("click"), ensure_ascii=False)
        if "비활성" not in txt and "거부" not in txt and "blocked" not in txt:
            r3.append("%s UIA 클릭이 거부되지 않음: %s" % (cid, v.get("click")))
    for key, v in (b.get("keys") or {}).items():
        if v.get("stepAfterKey") != _sess(b.get("stepBefore")).get("stepId"):
            r3.append("%s 키가 단계를 바꿈" % key)
    if b.get("probeBefore") and b.get("probeAfter"):
        for k in ("activeDocument", "docCount"):
            if b["probeBefore"].get(k) != b["probeAfter"].get(k):
                r3.append("투어 중 %s 가 변함" % k)
    else:
        r3.append("스냅샷 없음")
    out["AC-6-03"] = _res(r3)
    ex, ec = t.get("exit") or {}, t.get("escControl") or {}
    r4 = []
    for k in ("escOverlayGone", "xOverlayGone", "lastOverlayGone", "layerEnterGone"):
        if not ex.get(k):
            r4.append("%s 거짓" % k)
    if ex.get("docsBefore") != ex.get("docsAfterEsc") or ex.get("docsBefore") != ex.get("docsAfter"):
        r4.append("종료 뒤 문서 수 변함")
    if ec.get("docsBefore") != ec.get("docsAfterTourEsc"):
        r4.append("투어 중 Esc 로 문서가 닫힘")
    if ec.get("docsBefore") == ec.get("docsAfterPlainEsc"):
        r4.append("양성 대조 실패: 세션 없는 Esc 가 문서를 닫지 않아 대조가 성립하지 않음")
    out["AC-6-04"] = _res(r4)
    r5 = []
    ly = ex.get("layer") or {}
    if not ly.get("visible") or not ly.get("keyRow") or not ly.get("featureRow"):
        r5.append("마지막 레이어에 단축키·기능 행이 없음: %s" % {k: ly.get(k) for k in ("visible", "keyRow", "featureRow")})
    out["AC-6-05-runtime"] = _res(r5)
    rs = t.get("restore") or {}
    r6 = []
    for case in ("noFocus", "withFocus"):
        c = rs.get(case) or {}
        if not c.get("before") or c.get("before") != c.get("after"):
            r6.append("%s 종료 후 스냅샷이 시작 전과 다름: %s → %s" % (case, c.get("before"), c.get("after")))
        if (c.get("during") or {}).get("rootEffectivelyEnabled") is not False and (c.get("during") or {}).get("rootEnabled") is not False:
            r6.append("%s 투어 중 본창이 비활성화되지 않음(양성 대조 실패)" % case)
    out["AC-6-06"] = _res(r6)
    sk = t.get("skip") or {}
    r7 = []
    if not sk.get("trailWithoutDocs") or len(sk["trailWithoutDocs"]) >= len(sk.get("trailWithDocs") or []):
        r7.append("문서 0개일 때 단계가 줄지 않음: %d vs %d" % (len(sk.get("trailWithoutDocs") or []), len(sk.get("trailWithDocs") or [])))
    ids = [x.get("stepId") for x in sk.get("trailWithoutDocs") or []]
    if any(i in ("main.docHelp", "main.zoom") for i in ids):
        r7.append("대상 없는 단계가 건너뛰어지지 않음: %s" % ids)
    out["AC-6-07"] = _res(r7)
    return out


def _consistent_rows(node, path=""):
    """결과 트리에서 'consistent' 키를 가진 모든 항목을 모은다."""
    rows = []
    if isinstance(node, dict):
        if "consistent" in node:
            rows.append((path, node["consistent"]))
        for k, v in node.items():
            rows += _consistent_rows(v, path + "/" + str(k))
    elif isinstance(node, list):
        for i, v in enumerate(node):
            rows += _consistent_rows(v, "%s[%d]" % (path, i))
    return rows


def judge_theme_sync(raw):
    """AC-7-03 회귀(리뷰 I-1·I-2): 적용 테마와 콤보·목록 표시가 항상 같고, 다음 테마 2회 동시 요청은 2칸 이동한다."""
    reasons = []
    rows = _consistent_rows(raw)
    if len(rows) < 5:
        reasons.append("consistent 항목 5건 미만: %d" % len(rows))
    reasons += ["표시와 적용 테마 불일치 %s" % p for p, ok in rows if ok is not True]
    burst = (raw.get("nextBurst") or {})
    if not burst.get("deltas") or burst.get("two") != len(burst["deltas"]):
        reasons.append("다음 테마 동시 요청이 항상 2칸이 아님: %s" % burst)
    return {"AC-7-03-sync": _res(reasons)}


def main(argv):
    try:
        kind, files = argv[0], [json.load(open(p, encoding="utf-8")) for p in argv[1:]]
        fn = {"panel": judge_panel, "redock": judge_redock, "monitor": judge_monitor, "settings": judge_settings, "tour": judge_tour, "theme": judge_theme_sync}[kind]
        res = fn(*files)
    except (IndexError, KeyError, OSError, ValueError, TypeError) as ex:
        print(__doc__)
        print("입력 오류: %s" % ex)
        return 2
    print(json.dumps(res, ensure_ascii=False, indent=1))
    return 0 if all(v["verdict"] == "PASS" for v in res.values()) else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
